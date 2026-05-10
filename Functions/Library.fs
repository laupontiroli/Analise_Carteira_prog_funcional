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
let private matVecMul (matrix: float array array) (vec: float array) : float array =
    matrix |> Array.map (fun row -> dot row vec)

/// Calcula a matriz de covariância de uma matriz de retornos
/// matrix: cada linha é um dia, cada coluna é um ativo
let covarianceMatrix (matrix: float array array) : float array array =
    let n    = matrix.Length
    let cols = matrix[0].Length

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
let portfolioDailyReturns (returnsMatrix: float array array) (weights: float array) : float array =
    matVecMul returnsMatrix weights

/// Retorno médio anualizado da carteira (252 dias úteis)
let annualizedReturn (dailyReturns: float array) : float =
    Array.average dailyReturns * 252.0

/// Retorno médio anualizado a partir da matriz e pesos (usa média das colunas)
let annualizedReturnFast (meanReturns: float array) (weights: float array) : float =
    dot meanReturns weights * 252.0

/// Volatilidade anualizada usando covariância pré-calculada
let annualizedVolatilityFast (cov: float array array) (weights: float array) : float =
    let cwVec = cov |> Array.map (fun row -> dot row weights)
    let wTCw  = dot weights cwVec
    Math.Sqrt(wTCw) * Math.Sqrt(252.0)

/// Avalia uma carteira completa — retorna PortfolioResult

/// Sharpe Ratio anualizado
let sharpeRatio (mu: float) (sigma: float) (rFree: float) : float =
    if sigma = 0.0 then Double.NegativeInfinity
    else (mu - rFree) / sigma

let evaluatePortfolio
    (returnsMatrix : float array array)
    (rFree         : float)
    (portfolio     : Portfolio)
    : PortfolioResult =

    let cov         = covarianceMatrix returnsMatrix
    let meanReturns =
        [| for j in 0 .. portfolio.Weights.Length - 1 ->
            returnsMatrix |> Array.averageBy (fun row -> row[j]) |]

    let mu     = annualizedReturnFast meanReturns portfolio.Weights
    let sigma  = annualizedVolatilityFast cov portfolio.Weights
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
            if final |> Array.exists (fun x -> x > maxWeight + 1e-9) then generate ()
            else final
    generate ()

// ── Simulação de carteiras ───────────────────────────────────────────────────

/// Simula N carteiras para uma combinação, pré-calculando covariância e médias
/// uma única vez — muito mais eficiente que recalcular por simulação
let simulateBest
    (returnsMatrix : float array array)
    (tickers       : string array)
    (rFree         : float)
    (maxWeight     : float)
    (nSimulations  : int)
    (seed          : int)
    : PortfolioResult =

    let n   = tickers.Length
    let rng = Random(seed)

    // Pré-calcula covariância e retornos médios — feito UMA VEZ por combinação
    let cov         = covarianceMatrix returnsMatrix
    let meanReturns =
        [| for j in 0 .. n - 1 ->
            returnsMatrix |> Array.averageBy (fun row -> row[j]) |]

    Array.init nSimulations (fun _ ->
        let weights = generateWeights rng n maxWeight

        let mu     = annualizedReturnFast meanReturns weights
        let sigma  = annualizedVolatilityFast cov weights
        let sharpe = sharpeRatio mu sigma rFree

        { Portfolio  = { Tickers = tickers; Weights = weights }
          Return     = mu
          Volatility = sigma
          Sharpe     = sharpe })
    |> Array.maxBy (fun r -> r.Sharpe)