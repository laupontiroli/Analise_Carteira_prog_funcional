module Program

open System
open Functions.DataLoader

[<EntryPoint>]
let main argv =
    
    // Configurações do período
    let startDate = "2025-07-01"
    let endDate   = "2025-12-31"
    
    // Arquivo de tickers (pode trocar por "custom_tickers.json")
    let tickersFile = "dow30.json"
    
    // Busca os dados de retorno para todos os tickers do arquivo
    let priceData =
        fetchAllPrices tickersFile startDate endDate
        |> Async.RunSynchronously

    printfn "Dados carregados: %d ativos" (priceData |> Array.length)

    // TODO: passar priceData para as funções puras de simulação

    0