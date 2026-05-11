# Análise de Carteira — Programação Funcional

Projeto da disciplina de Programação Funcional (Insper, 2026-1).

Simula e otimiza carteiras de investimento usando as 30 ações do índice Dow Jones, buscando a carteira com maior Sharpe Ratio via força bruta paralelizada, escrito em **F#**.

---

## Contexto

Um portfolio manager quer descobrir a melhor alocação entre as ações do Dow Jones para o segundo semestre de 2025. A "melhor carteira" é definida como aquela que maximiza o **Sharpe Ratio**:

$$SR = \frac{\mu - r_{free}}{\sigma}$$

Onde:
- **μ** — retorno médio anualizado da carteira
- **r_free** — taxa livre de risco anual (5%)
- **σ** — volatilidade anualizada da carteira

---

## Abordagem

- São avaliadas **todas as combinações** de 25 a 30 ativos entre os 30 do Dow Jones — totalizando **174.437 combinações**
- Para cada combinação, são simuladas **N carteiras aleatórias** com pesos válidos (configurável via `nSimulations` no `Program.fs`)
- A simulação é **paralelizada** via `Array.Parallel.mapi`, onde cada combinação é avaliada de forma independente
- Todas as funções de cálculo são **puras** (sem efeitos colaterais), isolando a impureza apenas na camada de I/O

---

## Restrições da Carteira

| Restrição | Valor |
|---|---|
| Long-only | wᵢ ≥ 0 |
| Soma dos pesos | Σwᵢ = 1 |
| Concentração máxima | wᵢ ≤ 20% |
| Ativos por carteira | 25 a 30 |

---

## Estrutura do Projeto

```
Analise_Carteira_prog_funcional/
├── Functions/
│   ├── Library.fs        # Funções puras: retorno, volatilidade, Sharpe, simulação
│   ├── DataLoader.fs     # Funções impuras: leitura de JSON e chamada à API
│   └── Functions.fsproj
├── carteiraSimulada/
│   ├── Program.fs        # Ponto de entrada: orquestra busca, combinações e paralelismo
│   └── carteiraSimulada.fsproj
├── dow30.json            # Tickers oficiais do Dow Jones
├── custom_tickers.json   # Tickers customizados pelo usuário
├── results.md            # Resultados da última simulação
└── README.md
```

### `Library.fs` — Funções Puras

| Função | Descrição |
|---|---|
| `portfolioDailyReturns` | Retorno diário da carteira: `r_p = R * w` |
| `annualizedReturn` | Média dos retornos diários × 252 |
| `annualizedReturnFast` | Retorno anualizado via produto escalar com médias pré-calculadas |
| `annualizedVolatilityFast` | Volatilidade com covariância pré-calculada: `sqrt(wᵀ * C * w) * sqrt(252)` |
| `sharpeRatio` | `(μ - r_free) / σ` |
| `evaluatePortfolio` | Avalia uma carteira completa retornando todas as métricas |
| `generateWeights` | Gera pesos aleatórios válidos (long-only, soma=1, max 20%) |
| `simulateBest` | Pré-calcula covariância uma vez e roda N simulações, retornando a melhor |

### `DataLoader.fs` — I/O

| Função | Descrição |
|---|---|
| `fetchAllPrices` | Lê tickers do JSON e busca dados históricos via EODHD em paralelo |

O DataLoader implementa **cache em disco**: na primeira execução busca da API e salva em `cache/TICKER_start_end.json`. Nas execuções seguintes lê do cache, evitando requisições desnecessárias e o limite de chamadas da API.

---

## Resultados

Os resultados completos estão em [results.md](results.md). Abaixo um resumo:

### Melhor Carteira Encontrada — Treino (2H 2025)

**25 ativos:** AAPL, AMGN, AXP, BA, CAT, CRM, CSCO, CVX, DIS, DOW, GS, HD, INTC, JNJ, JPM, KO, MCD, MMM, MRK, MSFT, NKE, TRV, UNH, VZ, WMT

| Métrica | Treino (2H 2025) | Backtest (Q1 2026) |
|---------|-----------------|-------------------|
| Retorno anualizado | +35.06% | +21.21% |
| Volatilidade anual | 9.31% | 12.59% |
| Sharpe Ratio | 3.2284 | 1.2880 |

> A carteira manteve retorno positivo no período de teste, com Sharpe acima de 1 — indicando que a estratégia generalizou razoavelmente para fora do período de treino.

**Tempo de simulação:** 339 segundos (~5.6 minutos) com paralelismo em 174.437 combinações.

---

## Pré-requisitos

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- Chave de API do [EODHD](https://eodhd.com) (plano gratuito é suficiente)

---

## Instalação

```bash
git clone https://github.com/laupontiroli/Analise_Carteira_prog_funcional.git
cd Analise_Carteira_prog_funcional
dotnet restore
```

---

## Configuração

Defina sua chave da API como variável de ambiente:

```bash
export EODHD_API_KEY="sua_chave_aqui"
```

> A pasta `cache/` é criada automaticamente na primeira execução e está no `.gitignore`.

---

## Como Rodar

```bash
cd carteiraSimulada

# Apenas otimização (gera results.md com treino)
dotnet run

# Otimização + backtest no Q1 2026 (gera results.md completo)
dotnet run -- --backtest
```

### Parâmetros configuráveis no `Program.fs`

| Parâmetro | Padrão | Descrição |
|---|---|---|
| `startDate` | `2025-07-01` | Início do período de treino |
| `endDate` | `2025-12-31` | Fim do período de treino |
| `rFree` | `0.05` | Taxa livre de risco anual |
| `maxWeight` | `0.20` | Concentração máxima por ativo |
| `nSimulations` | `1_000` | Simulações por combinação |
| `minSelect` | `25` | Mínimo de ativos por carteira |
| `maxSelect` | `30` | Máximo de ativos por carteira |
| `tickersFile` | `dow30.json` | Arquivo de tickers |

### Usando tickers customizados

Edite o arquivo `custom_tickers.json` e troque no `Program.fs`:

```fsharp
let tickersFile = Path.Combine(baseDir, "custom_tickers.json")
```

---

## Por que F# e Programação Funcional?

- **Funções puras** eliminam efeitos colaterais e tornam o código trivialmente paralelizável
- **Ausência de estado compartilhado** entre combinações permite uso seguro de `Array.Parallel.mapi`
- **Pipeline funcional** (`|>`) torna o fluxo de dados explícito e legível
- A covariância é calculada **uma vez por combinação** e reutilizada nas N simulações, padrão natural em programação funcional de evitar recomputação

---

## Dependências

| Pacote | Uso |
|---|---|
| `FSharp.Core` | Linguagem base |
| `FSharp.Data` | Utilitários F# |
| `System.Text.Json` | Parse do JSON da API |

---

## Dados

Os dados históricos são obtidos via [EODHD Historical Data API](https://eodhd.com/financial-apis/stock-market-data/), endpoint `/api/eod/{TICKER}.US`.

- **Treino:** 01/07/2025 a 31/12/2025 (127 dias úteis)
- **Teste:** 01/01/2026 a 31/03/2026