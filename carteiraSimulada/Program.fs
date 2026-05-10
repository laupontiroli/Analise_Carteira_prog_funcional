module Program

open System
open System.IO
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

[<EntryPoint>]
let main argv =

    // ── Configurações ────────────────────────────────────────────────────────
    let startDate    = "2025-07-01"
    let endDate      = "2025-12-31"
    let baseDir     = AppDomain.CurrentDomain.BaseDirectory
    let tickersFile = Path.Combine(baseDir, "dow30.json")
    let rFree        = 0.05           // taxa livre de risco anual (5%)
    let maxWeight    = 0.20           // concentração máxima por ativo (20%)
    let nSimulations = 1_000          // simulações por combinação (aumente para produção)
    let minSelect    = 25             // tamanho mínimo de carteira
    let maxSelect    = 30             // tamanho máximo de carteira

    // ── 1. Busca os dados via API ────────────────────────────────────────────
    printfn "Buscando dados de %s a %s..." startDate endDate

    let assetData =
        fetchAllPrices tickersFile startDate endDate
        |> Async.RunSynchronously

    printfn "Dados carregados: %d ativos" assetData.Length

    // ── 2. Monta a matriz global de retornos (dias x ativos) ─────────────────
    let minDays =
        assetData |> Array.map (fun a -> a.Prices.Length) |> Array.min

    let allTickers =
        assetData |> Array.map (fun a -> a.Ticker)

    let fullReturnsMatrix : float array array =
        [| for d in 0 .. minDays - 1 ->
            assetData |> Array.map (fun a -> a.Prices[d].Return) |]

    printfn "Matriz de retornos: %d dias x %d ativos" fullReturnsMatrix.Length allTickers.Length

    // ── 3. Gera todas as combinações de minSelect a maxSelect ativos ─────────
    let indices = Array.init allTickers.Length id

    let allCombinations : int array array =
        [| for k in minSelect .. maxSelect do
            yield! combinations indices k |]

    printfn "Total de combinações (C(30,25) a C(30,30)): %d" allCombinations.Length

    // ── 4. Avalia cada combinação em paralelo ────────────────────────────────
    printfn "Iniciando simulação paralelizada..."
    let stopwatch = Diagnostics.Stopwatch.StartNew()

    let bestPerCombo : PortfolioResult array =
        allCombinations
        |> Array.Parallel.mapi (fun i combo ->
            let tickers = combo |> Array.map (fun idx -> allTickers[idx])

            let subMatrix : float array array =
                fullReturnsMatrix
                |> Array.map (fun row -> combo |> Array.map (fun idx -> row[idx]))

            let seed = i * 31 + 7
            simulateBest subMatrix tickers rFree maxWeight nSimulations seed)

    stopwatch.Stop()
    printfn "Simulação concluída em %.2f segundos" stopwatch.Elapsed.TotalSeconds

    // ── 5. Seleciona a melhor carteira global ────────────────────────────────
    let best = bestPerCombo |> Array.maxBy (fun r -> r.Sharpe)

    printfn "\n====== MELHOR CARTEIRA ======"
    printfn "Número de ativos  : %d" best.Portfolio.Tickers.Length
    printfn "Ativos: %s" (String.concat ", " best.Portfolio.Tickers)
    printfn "\nPesos:"
    Array.iter2
        (fun ticker weight -> printfn "  %-5s  %.2f%%" ticker (weight * 100.0))
        best.Portfolio.Tickers
        best.Portfolio.Weights
    printfn "\nRetorno anualizado : %.4f (%.2f%%)" best.Return (best.Return * 100.0)
    printfn "Volatilidade anual : %.4f (%.2f%%)" best.Volatility (best.Volatility * 100.0)
    printfn "Sharpe Ratio       : %.4f" best.Sharpe

    0