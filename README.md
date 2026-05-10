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
└── README.md
```

### `Library.fs` — Funções Puras

| Função | Descrição |
|---|---|
| `portfolioDailyReturns` | Retorno diário da carteira: `r_p = R * w` |
| `annualizedReturn` | Média dos retornos diários × 252 |
| `annualizedVolatility` | `sqrt(wᵀ * C * w) * sqrt(252)` |
| `sharpeRatio` | `(μ - r_free) / σ` |
| `generateWeights` | Gera pesos aleatórios válidos (long-only, soma=1, max 20%) |
| `simulateBest` | Roda N simulações e retorna a carteira com maior Sharpe |

### `DataLoader.fs` — I/O

| Função | Descrição |
|---|---|
| `fetchAllPrices` | Lê tickers do JSON e busca dados históricos via EODHD |

O DataLoader implementa **cache em disco**: na primeira execução busca da API e salva em `cache/TICKER_start_end.json`. Nas execuções seguintes lê do cache, evitando requisições desnecessárias e o limite de chamadas da API.

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

> A pasta `cache/` é criada automaticamente na primeira execução e fica ignorada pelo `.gitignore`.

---

## Como Rodar

```bash
cd carteiraSimulada
dotnet run
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
| `tickersFile` | `dow30.json` | Arquivo de tickers (`custom_tickers.json` para customizado) |

### Exemplo de output

```
Buscando dados de 2025-07-01 a 2025-12-31...
  [cache] AAPL
  [cache] MSFT
  ...
Dados carregados: 30 ativos
Matriz de retornos: 127 dias x 30 ativos
Total de combinações (C(30,25) a C(30,30)): 174437
Iniciando simulação paralelizada...
Simulação concluída em 42.30 segundos

====== MELHOR CARTEIRA ======
Número de ativos  : 25
Ativos: AAPL, AMGN, ...

Pesos:
  AAPL   18.45%
  AMGN   12.30%
  ...

Retorno anualizado : 0.2341 (23.41%)
Volatilidade anual : 0.1420 (14.20%)
Sharpe Ratio       : 1.3104
```

### Usando tickers customizados

Edite o arquivo `custom_tickers.json` na raiz do projeto e troque no `Program.fs`:

```fsharp
let tickersFile = Path.Combine(baseDir, "custom_tickers.json")
```

---

## Por que F# e Programação Funcional?

- **Funções puras** eliminam efeitos colaterais e tornam o código trivialmente paralelizável
- **Ausência de estado compartilhado** entre combinações permite uso seguro de `Array.Parallel.mapi`
- **Pipeline funcional** (`|>`) torna o fluxo de dados explícito e legível
- O padrão `map → filter → reduce` se encaixa naturalmente no problema de simulação em larga escala

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

Período utilizado: **01/07/2025 a 31/12/2025** (segundo semestre de 2025).