module Program

open System
open System.IO
open System.Threading
open Functions.DataLoader
open Functions.Library

// Gera todas as combinações de tamanho k a partir de um array de índices
let combinations (arr: int array) (k: int) : int array seq =
    let n = arr.Length
    let rec generate (start: int) (combo: int list) : int array seq =
        seq {
            if combo.Length = k then
                yield combo |> List.rev |> List.toArray
            else
                for i in start .. n - (k - combo.Length) do
                    yield! generate (i + 1) (arr[i] :: combo)
        }
    generate 0 []

// Roda a simulação completa e retorna a melhor carteira
let runSimulation (tickersFile: string) (startDate: string) (endDate: string) (rFree: float) (maxWeight: float) (nSimulations: int) (minSelect: int) (maxSelect: int) =

    printfn "Buscando dados de %s a %s..." startDate endDate

    let assetData =
        fetchAllPrices tickersFile startDate endDate
        |> Async.RunSynchronously

    printfn "Dados carregados: %d ativos" assetData.Length

    let minDays    = assetData |> Array.map (fun a -> a.Prices.Length) |> Array.min
    let allTickers = assetData |> Array.map (fun a -> a.Ticker)

    let fullReturnsMatrix : float array array =
        [| for d in 0 .. minDays - 1 ->
            assetData |> Array.map (fun a -> a.Prices[d].Return) |]

    printfn "Matriz de retornos: %d dias x %d ativos" fullReturnsMatrix.Length allTickers.Length

    let indices = Array.init allTickers.Length id

    let allCombinations : int array array =
        [| for k in minSelect .. maxSelect do
            yield! combinations indices k |]

    let total = allCombinations.Length
    printfn "Total de combinações (C(30,%d) a C(30,%d)): %d\n" minSelect maxSelect total

    printfn "Iniciando simulação paralelizada..."
    let stopwatch = Diagnostics.Stopwatch.StartNew()
    let counter   = ref 0

    let bestPerCombo : PortfolioResult array =
        allCombinations
        |> Array.Parallel.mapi (fun i combo ->
            let tickers = combo |> Array.map (fun idx -> allTickers[idx])

            let subMatrix : float array array =
                fullReturnsMatrix
                |> Array.map (fun row -> combo |> Array.map (fun idx -> row[idx]))

            let result = simulateBest subMatrix tickers rFree maxWeight nSimulations (i * 31 + 7)

            let count = Interlocked.Increment(counter)
            if count % 1_000 = 0 || count = total then
                let pct     = float count / float total * 100.0
                let elapsed = stopwatch.Elapsed.TotalSeconds
                let eta     = if count < total then elapsed / float count * float (total - count) else 0.0
                printf "\r  Progresso: %6d / %d  (%.1f%%)  |  Decorrido: %.0fs  |  ETA: %.0fs   "
                    count total pct elapsed eta

            result)

    stopwatch.Stop()
    let elapsed = stopwatch.Elapsed.TotalSeconds
    printfn "\n\nSimulação concluída em %.2f segundos" elapsed

    bestPerCombo |> Array.maxBy (fun r -> r.Sharpe), elapsed

// Salva os resultados em Markdown usando sprintf para formatação
let saveResults (best: PortfolioResult) (elapsed: float) (testResult: PortfolioResult option) (outputPath: string) =
    let lines = System.Collections.Generic.List<string>()
    let w (s: string) = lines.Add(s)

    w "# Resultados da Simulação"
    w ""
    w (sprintf "**Data de execução:** %s" (DateTime.Now.ToString("dd/MM/yyyy HH:mm")))
    w (sprintf "**Tempo de simulação:** %.2f segundos" elapsed)
    w ""
    w "---"
    w ""
    w "## Melhor Carteira — Treino (2H 2025)"
    w ""
    w (sprintf "**Número de ativos:** %d" best.Portfolio.Tickers.Length)
    w ""
    w (sprintf "**Ativos:** %s" (String.concat ", " best.Portfolio.Tickers))
    w ""
    w "### Pesos"
    w ""
    w "| Ativo | Peso |"
    w "|-------|------|"
    Array.iter2
        (fun ticker weight -> w (sprintf "| %s | %.2f%% |" ticker (weight * 100.0)))
        best.Portfolio.Tickers
        best.Portfolio.Weights
    w ""
    w "### Métricas"
    w ""
    w "| Métrica | Valor |"
    w "|---------|-------|"
    w (sprintf "| Retorno anualizado | %+.2f%% |" (best.Return * 100.0))
    w (sprintf "| Volatilidade anual | %.2f%% |" (best.Volatility * 100.0))
    w (sprintf "| Sharpe Ratio | %.4f |" best.Sharpe)

    match testResult with
    | None -> ()
    | Some test ->
        w ""
        w "---"
        w ""
        w "## Backtest — Teste (Q1 2026)"
        w ""
        w "Mesmos pesos aplicados ao período de 01/01/2026 a 31/03/2026."
        w ""
        w "### Métricas"
        w ""
        w "| Métrica | Valor |"
        w "|---------|-------|"
        w (sprintf "| Retorno anualizado | %+.2f%% |" (test.Return * 100.0))
        w (sprintf "| Volatilidade anual | %.2f%% |" (test.Volatility * 100.0))
        w (sprintf "| Sharpe Ratio | %.4f |" test.Sharpe)
        w ""
        w "---"
        w ""
        w "## Comparação Treino vs Teste"
        w ""
        w "| Métrica | Treino (2H 2025) | Teste (Q1 2026) |"
        w "|---------|-----------------|-----------------|"
        w (sprintf "| Retorno anualizado | %+.2f%% | %+.2f%% |" (best.Return * 100.0) (test.Return * 100.0))
        w (sprintf "| Volatilidade anual | %.2f%% | %.2f%% |" (best.Volatility * 100.0) (test.Volatility * 100.0))
        w (sprintf "| Sharpe Ratio | %.4f | %.4f |" best.Sharpe test.Sharpe)

    File.WriteAllLines(outputPath, lines)
    printfn "Resultados salvos em: %s" outputPath

[<EntryPoint>]
let main argv =

    // ── Configurações ─────────────────────────────────────────────────────
    let baseDir     = AppDomain.CurrentDomain.BaseDirectory
    let tickersFile = Path.Combine(baseDir, "dow30.json")
    let rFree        = 0.05
    let maxWeight    = 0.20
    let nSimulations = 1_000
    let minSelect    = 25
    let maxSelect    = 30
    let outputFile   = Path.Combine(baseDir, "results.md")

    let doBacktest = argv |> Array.contains "--backtest"

    // ── Treino: 2H 2025 ───────────────────────────────────────────────────
    let best, elapsed =
        runSimulation tickersFile "2025-07-01" "2025-12-31" rFree maxWeight nSimulations minSelect maxSelect

    printfn "\n====== MELHOR CARTEIRA — TREINO (2H 2025) ======"
    printfn "Número de ativos  : %d" best.Portfolio.Tickers.Length
    printfn "Ativos: %s" (String.concat ", " best.Portfolio.Tickers)
    printfn "\nPesos:"
    Array.iter2
        (fun ticker weight -> printfn "  %-5s  %.2f%%" ticker (weight * 100.0))
        best.Portfolio.Tickers
        best.Portfolio.Weights
    printfn "\nRetorno anualizado : %+.4f (%+.2f%%)" best.Return (best.Return * 100.0)
    printfn "Volatilidade anual : %.4f (%.2f%%)" best.Volatility (best.Volatility * 100.0)
    printfn "Sharpe Ratio       : %.4f" best.Sharpe

    // ── Backtest: Q1 2026 ─────────────────────────────────────────────────
    let testResult =
        if doBacktest then
            printfn "\n\nBuscando dados do período de teste (Q1 2026)..."

            let testData =
                fetchAllPrices tickersFile "2026-01-01" "2026-03-31"
                |> Async.RunSynchronously

            let bestTickers = best.Portfolio.Tickers |> Set.ofArray

            let testAssets =
                testData
                |> Array.filter (fun a -> bestTickers.Contains(a.Ticker))
                |> Array.sortBy (fun a -> Array.findIndex ((=) a.Ticker) best.Portfolio.Tickers)

            let minDays = testAssets |> Array.map (fun a -> a.Prices.Length) |> Array.min

            let testMatrix : float array array =
                [| for d in 0 .. minDays - 1 ->
                    testAssets |> Array.map (fun a -> a.Prices[d].Return) |]

            printfn "Período de teste: %d dias" testMatrix.Length

            let result = evaluatePortfolio testMatrix rFree best.Portfolio

            printfn "\n====== BACKTEST — TESTE (Q1 2026) ======"
            printfn "Retorno anualizado : %+.4f (%+.2f%%)" result.Return (result.Return * 100.0)
            printfn "Volatilidade anual : %.4f (%.2f%%)" result.Volatility (result.Volatility * 100.0)
            printfn "Sharpe Ratio       : %.4f" result.Sharpe

            printfn "\n====== COMPARAÇÃO ======"
            printfn "                     Treino (2H 2025)   Teste (Q1 2026)"
            printfn "  Retorno            %+8.2f%%           %+8.2f%%"
                (best.Return * 100.0) (result.Return * 100.0)
            printfn "  Volatilidade       %8.2f%%           %8.2f%%"
                (best.Volatility * 100.0) (result.Volatility * 100.0)
            printfn "  Sharpe Ratio       %8.4f           %8.4f"
                best.Sharpe result.Sharpe

            Some result
        else
            None

    // ── Salva resultados em Markdown ──────────────────────────────────────
    saveResults best elapsed testResult outputFile

    0