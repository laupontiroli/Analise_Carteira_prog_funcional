module Functions.Library

open System

// ── Tipos ────────────────────────────────────────────────────────────────────

type Portfolio = {
    Tickers : string array
    Weights : float array
}

type PortfolioResult = {
    Portfolio  : Portfolio
    Return     : float
    Volatility : float
    Sharpe     : float
}

// ── Álgebra linear simples ───────────────────────────────────────────────────

/// Produto escalar entre dois vetores
let private dot (a: float array) (b: float array) : float =
    Array.map2 (*) a b |> Array.sum

/// Multiplica matriz (linhas x colunas) por vetor (colunas)
/// Retorna vetor (linhas) — cada elemento é o retorno diário da carteira
let private matVecMul (matrix: float array array) (vec: float array) : float array =
    matrix |> Array.map (fun row -> dot row vec)

/// Calcula a matriz de covariância de uma matriz de retornos
/// matrix: cada linha é um dia, cada coluna é um ativo
let private covarianceMatrix (matrix: float array array) : float array array =
    let n    = matrix.Length           // dias
    let cols = matrix[0].Length        // ativos

    let means =
        [| for j in 0 .. cols - 1 ->
            matrix |> Array.averageBy (fun row -> row[j]) |]

    [| for i in 0 .. cols - 1 ->
        [| for j in 0 .. cols - 1 ->
            let cov =
                matrix
                |> Array.sumBy (fun row ->
                    (row[i] - means[i]) * (row[j] - means[j]))
            cov / float (n - 1) |] |]

// ── Funções puras de carteira ────────────────────────────────────────────────

/// Calcula o retorno diário da carteira para cada dia
/// returnsMatrix: array de dias, cada dia é array de retornos por ativo
let portfolioDailyReturns (returnsMatrix: float array array) (weights: float array) : float array =
    matVecMul returnsMatrix weights

/// Retorno médio anualizado da carteira (252 dias úteis)
let annualizedReturn (dailyReturns: float array) : float =
    Array.average dailyReturns * 252.0

/// Volatilidade anualizada: σ_p = sqrt(w^T * C * w) * sqrt(252)
let annualizedVolatility (returnsMatrix: float array array) (weights: float array) : float =
    let cov    = covarianceMatrix returnsMatrix
    let cwVec  = cov |> Array.map (fun row -> dot row weights)   // C * w
    let wTCw   = dot weights cwVec                                // w^T * C * w
    Math.Sqrt(wTCw) * Math.Sqrt(252.0)

/// Sharpe Ratio anualizado
/// rFree: taxa livre de risco anual (ex: 0.05 para 5%)
let sharpeRatio (mu: float) (sigma: float) (rFree: float) : float =
    if sigma = 0.0 then Double.NegativeInfinity
    else (mu - rFree) / sigma

/// Avalia uma carteira completa — retorna PortfolioResult
let evaluatePortfolio
    (returnsMatrix : float array array)
    (rFree         : float)
    (portfolio     : Portfolio)
    : PortfolioResult =

    let daily  = portfolioDailyReturns returnsMatrix portfolio.Weights
    let mu     = annualizedReturn daily
    let sigma  = annualizedVolatility returnsMatrix portfolio.Weights
    let sharpe = sharpeRatio mu sigma rFree

    { Portfolio  = portfolio
      Return     = mu
      Volatility = sigma
      Sharpe     = sharpe }

// ── Geração de pesos aleatórios válidos ─────────────────────────────────────

/// Gera um vetor de pesos aleatórios que respeita:
///   - long-only: w_i >= 0
///   - soma = 1
///   - concentração máxima: w_i <= maxWeight
/// Estratégia: gera valores uniformes, clipa em maxWeight e renormaliza
let generateWeights (rng: Random) (n: int) (maxWeight: float) : float array =
    let rec generate () =
        let raw     = Array.init n (fun _ -> rng.NextDouble())
        let total   = Array.sum raw
        let norm    = raw |> Array.map (fun x -> x / total)
        let clipped = norm |> Array.map (fun x -> Math.Min(x, maxWeight))
        let total2  = Array.sum clipped

        if total2 = 0.0 then generate ()
        else
            let final = clipped |> Array.map (fun x -> x / total2)
            // verifica se ainda respeita após renormalização
            if final |> Array.exists (fun x -> x > maxWeight + 1e-9) then generate ()
            else final

    generate ()

// ── Simulação de carteiras ───────────────────────────────────────────────────

/// Simula N carteiras aleatórias para um conjunto de ativos e retorna a melhor
let simulateBest
    (returnsMatrix : float array array)
    (tickers       : string array)
    (rFree         : float)
    (maxWeight     : float)
    (nSimulations  : int)
    (seed          : int)
    : PortfolioResult =

    let rng = Random(seed)

    Array.init nSimulations (fun _ ->
        let weights   = generateWeights rng tickers.Length maxWeight
        let portfolio = { Tickers = tickers; Weights = weights }
        evaluatePortfolio returnsMatrix rFree portfolio)
    |> Array.maxBy (fun r -> r.Sharpe)
