module Functions.DataLoader

open System
open System.Net.Http
open System.Text.Json
open System.IO

// Chave da API do EODHD — defina como variável de ambiente: EODHD_API_KEY
let private apiKey =
    Environment.GetEnvironmentVariable("EODHD_API_KEY")

// Representa um dia de preço retornado pela API
type PriceRecord = {
    Date   : string
    Close  : float
    Return : float  // retorno diário calculado: (close_t / close_{t-1}) - 1
}

// Representa os dados de um ativo
type AssetData = {
    Ticker  : string
    Prices  : PriceRecord array
}

// Lê a lista de tickers de um arquivo JSON no formato:
// { "tickers": ["AAPL", "MSFT", ...] }
let private readTickers (filePath: string) : string array =
    let json = File.ReadAllText(filePath)
    let doc  = JsonDocument.Parse(json)
    doc.RootElement.GetProperty("tickers").EnumerateArray()
    |> Seq.map (fun el -> el.GetString())
    |> Seq.toArray

// Calcula os retornos diários a partir de uma sequência de preços de fechamento
let private computeReturns (closes: float array) : float array =
    closes
    |> Array.pairwise
    |> Array.map (fun (prev, curr) -> (curr / prev) - 1.0)

// Busca os preços de fechamento de um ticker na API do EODHD
// Impura: faz chamada HTTP
let private fetchPrices (ticker: string) (startDate: string) (endDate: string) : Async<AssetData> =
    async {
        use client = new HttpClient()
        let url =
            $"https://eodhd.com/api/eod/{ticker}.US?from={startDate}&to={endDate}&period=d&api_token={apiKey}&fmt=json"

        let! response = client.GetStringAsync(url) |> Async.AwaitTask
        let doc       = JsonDocument.Parse(response)
        let entries   = doc.RootElement.EnumerateArray() |> Seq.toArray

        let closes =
            entries
            |> Array.map (fun el -> el.GetProperty("close").GetDouble())

        let dates =
            entries
            |> Array.map (fun el -> el.GetProperty("date").GetString())

        let returns = computeReturns closes

        // retornos têm 1 elemento a menos que closes, alinhamos a partir do índice 1
        let prices =
            returns
            |> Array.mapi (fun i r ->
                { Date   = dates[i + 1]
                  Close  = closes[i + 1]
                  Return = r })

        return { Ticker = ticker; Prices = prices }
    }

// Lê o arquivo de tickers e busca os dados de todos em paralelo
// Impura: lê arquivo e faz chamadas HTTP
let fetchAllPrices (tickersFile: string) (startDate: string) (endDate: string) : Async<AssetData array> =
    async {
        let tickers = readTickers tickersFile

        let! results =
            tickers
            |> Array.map (fun t -> fetchPrices t startDate endDate)
            |> Async.Parallel

        return results
    }