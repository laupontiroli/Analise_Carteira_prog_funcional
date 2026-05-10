module Functions.DataLoader

open System
open System.Net.Http
open System.Text.Json
open System.IO

// Chave da API do EODHD — defina como variável de ambiente: EODHD_API_KEY
let private apiKey =
    Environment.GetEnvironmentVariable("EODHD_API_KEY")

// Pasta onde os caches ficam salvos (relativo ao executável)
let private cacheDir =
    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache")

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

// Retorna o caminho do arquivo de cache para um ticker e período
let private cachePath (ticker: string) (startDate: string) (endDate: string) : string =
    Directory.CreateDirectory(cacheDir) |> ignore
    Path.Combine(cacheDir, $"{ticker}_{startDate}_{endDate}.json")

// Verifica se existe cache válido para o ticker e período
let private tryReadCache (ticker: string) (startDate: string) (endDate: string) : string option =
    let path = cachePath ticker startDate endDate
    if File.Exists(path) then
        printfn "  [cache] %s" ticker
        Some (File.ReadAllText(path))
    else
        None

// Salva a resposta bruta da API em cache
let private writeCache (ticker: string) (startDate: string) (endDate: string) (json: string) : unit =
    let path = cachePath ticker startDate endDate
    File.WriteAllText(path, json)

// Parseia o JSON bruto da API e retorna um AssetData
let private parseResponse (ticker: string) (response: string) : AssetData =
    let doc     = JsonDocument.Parse(response)
    let entries = doc.RootElement.EnumerateArray() |> Seq.toArray

    let closes =
        entries |> Array.map (fun el -> el.GetProperty("close").GetDouble())

    let dates =
        entries |> Array.map (fun el -> el.GetProperty("date").GetString())

    let returns = computeReturns closes

    let prices =
        returns
        |> Array.mapi (fun i r ->
            { Date   = dates[i + 1]
              Close  = closes[i + 1]
              Return = r })

    { Ticker = ticker; Prices = prices }

// Busca os preços de um ticker — lê do cache se disponível, senão chama a API
// Impura: acessa disco e/ou rede
let private fetchPrices (ticker: string) (startDate: string) (endDate: string) : Async<AssetData> =
    async {
        let cached = tryReadCache ticker startDate endDate

        let! response =
            match cached with
            | Some json ->
                async { return json }
            | None ->
                async {
                    use client = new HttpClient()
                    let url =
                        $"https://eodhd.com/api/eod/{ticker}.US?from={startDate}&to={endDate}&period=d&api_token={apiKey}&fmt=json"
                    printfn "  [api]   %s" ticker
                    let! json = client.GetStringAsync(url) |> Async.AwaitTask
                    writeCache ticker startDate endDate json
                    return json
                }

        return parseResponse ticker response
    }

// Lê o arquivo de tickers e busca os dados de todos em paralelo
// Impura: lê arquivo, acessa disco e/ou rede
let fetchAllPrices (tickersFile: string) (startDate: string) (endDate: string) : Async<AssetData array> =
    async {
        let tickers = readTickers tickersFile

        let! results =
            tickers
            |> Array.map (fun t -> fetchPrices t startDate endDate)
            |> Async.Parallel

        return results
    }