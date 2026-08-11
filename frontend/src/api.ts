export type CurrentUser = {
  userName: string
  email: string
  role: string
}

export type HealthStatus = {
  api: string
  database: string
}

export type BinanceSymbol = { symbol: string; baseAsset: string; quoteAsset: string; status: string; contractType: string }
export type MonitoredSymbol = { id: number; symbol: string; baseAsset: string; quoteAsset: string; isEnabled: boolean }
export type PositionSizingMode = 'FixedNotional' | 'MarginPercent'
export type TradingSettings = { riskReward: number; fixedOrderSizeUsdt: number; minEmaGapPercent: number; maxStopDistancePercent: number; positionSizingMode: PositionSizingMode; simulatedAccountBalanceUsdt: number; marginPerTradePercent: number; leverage: number; waitForConfirmationCandle: boolean; useEma100Filter: boolean; trailingStopEnabled: boolean; feePercentPerSide: number; updatedAtUtc: string }
export type BacktestRunSummary = { id: number; symbol: string; interval: string; requestedStartUtc: string; requestedEndUtc: string; actualStartUtc: string | null; actualEndUtc: string | null; createdAtUtc: string; completedAtUtc: string | null; candleCount: number; totalTrades: number; winningTrades: number; losingTrades: number; breakEvenTrades: number; longTrades: number; shortTrades: number; winRatePercent: number; grossPnlUsdt: number; netPnlUsdt: number; totalFeesUsdt: number; profitFactor: number | null; averageNetPnlUsdt: number; averageRMultiple: number; maxDrawdownUsdt: number; totalCrossovers: number; longSignals: number; shortSignals: number; rejectedByEma100: number; confirmationFailed: number; invalidStopLoss: number; skippedWhilePositionOpen: number; noEntryCandle: number; riskReward: number; fixedOrderSizeUsdt: number; waitForConfirmationCandle: boolean; useEma100Filter: boolean; trailingStopEnabled: boolean; feePercentPerSide: number; status: string; failureMessage: string | null }
export type BacktestRun = BacktestRunSummary & { trades: BacktestTrade[] }
export type BacktestTrade = { id: number; direction: string; entryTimeUtc: string; exitTimeUtc: string; entryPrice: number; exitPrice: number; initialStopLoss: number; finalStopLoss: number; originalTakeProfit: number; finalTakeProfit: number; exitReason: string; netPnlUsdt: number; netPnlPercent: number; grossRMultiple: number }
export type PaperTrade = { id: number; symbol: string; status: string; direction: string; entryTimeUtc: string; exitTimeUtc: string | null; entryPrice: number; exitPrice: number | null; quantity: number; initialStopLoss: number; currentStopLoss: number; currentTakeProfit: number; takeProfitExtended: boolean; bestFavorableProgressPercent: number; grossPnlUsdt: number; netPnlUsdt: number; netPnlPercent: number; mfePrice: number; maePrice: number; exitReason: string | null }
export type PaperSymbol = { symbol: string; latestPrice: number | null; lastMarketEventUtc: string | null; lastClosedCandleUtc: string | null; trend: string | null; ema9: number | null; ema15: number | null; ema100: number | null; gapPercent: number | null; gapState: string | null; pendingDirection: string | null; openTrade: PaperTrade | null }
export type PaperSession = { id: number; interval: string; status: string; startedAtUtc: string; stoppedAtUtc: string | null; interruptedAtUtc: string | null; failureMessage: string | null; riskReward: number; fixedOrderSizeUsdt: number; waitForConfirmationCandle: boolean; useEma100Filter: boolean; trailingStopEnabled: boolean; feePercentPerSide: number; completedTrades: number; netPnlUsdt: number; totalFeesUsdt: number; connectionState: string; lastUpdateUtc: string | null; symbols: PaperSymbol[]; recentTrades: PaperTrade[] }
export type PaperSessionSummary = { id: number; interval: string; status: string; startedAtUtc: string; symbolCount: number; completedTrades: number; netPnlUsdt: number; totalFeesUsdt: number }
export type TradeSource = 'Backtest' | 'Paper'
export type TradeSummary = { source: TradeSource; id: number; parentId: number; symbol: string; interval: string; status: string; direction: string; entryTimeUtc: string; exitTimeUtc: string | null; entryPrice: number; exitPrice: number | null; exitReason: string | null; grossPnlUsdt: number; netPnlUsdt: number; netPnlPercent: number; totalFeesUsdt: number; grossRMultiple: number | null; netRMultiple: number | null }
export type TradeManagementEvent = { timeUtc: string; effectiveTimeUtc: string | null; type: string; marketPrice: number; oldStop: number | null; newStop: number | null; oldTakeProfit: number | null; newTakeProfit: number | null; progressPercent: number | null }
export type TradeDetail = { summary: TradeSummary; crossoverTimeUtc: string; signalTimeUtc: string; quantity: number; entryNotionalUsdt: number; initialStopLoss: number; finalStopLoss: number; stopSourceType: string; stopSourceTimeUtc: string; originalTakeProfit: number; finalTakeProfit: number; takeProfitExtended: boolean; entryFeeUsdt: number; exitFeeUsdt: number | null; mfePrice: number; mfePercent: number; maePrice: number; maePercent: number; signalOpen: number | null; signalClose: number; signalEma9: number | null; signalEma15: number | null; signalEma100: number | null; signalGapPercent: number | null; signalGapState: string; riskReward: number; fixedOrderSizeUsdt: number; waitForConfirmationCandle: boolean; useEma100Filter: boolean; trailingStopEnabled: boolean; feePercentPerSide: number; positionSizingMode: PositionSizingMode; accountEquityAtEntryUsdt: number | null; marginUsedUsdt: number | null; leverage: number | null; isReentry: boolean; trendRegimeCrossoverTimeUtc: string | null; minEmaGapPercent: number; events: TradeManagementEvent[]; hasDetailedManagementHistory: boolean }
export type TradeChartCandle = { openTimeUtc: string; closeTimeUtc: string; open: number; high: number; low: number; close: number; volume: number }
export type TradeChartPoint = { timeUtc: string; value: number | null }
export type TradeChartData = { symbol: string; interval: string; candles: TradeChartCandle[]; ema9: TradeChartPoint[]; ema15: TradeChartPoint[]; ema100: TradeChartPoint[] }
export type OptimizerGrid = { riskRewards: number[]; minEmaGapPercents: number[]; maxStopDistancePercents: number[]; waitForConfirmationCandles: boolean[]; useEma100Filters: boolean[]; trailingStopEnableds: boolean[] }
export type OptimizerOptions = { enabledSymbols: string[]; supportedTimeframes: string[]; assumptions: TradingSettings; defaultGrid: OptimizerGrid }
export type OptimizerRun = { id: number; status: string; createdAtUtc: string; startedAtUtc: string | null; completedAtUtc: string | null; failureMessage: string | null; requestedStartUtc: string; requestedEndUtc: string; candidateCount: number; marketCount: number; totalWork: number; completedWork: number; progress: number; recommendedCandidateId: number | null; robustCandidateCount: number; assumptions: { simulatedAccountBalanceUsdt: number; fixedOrderSizeUsdt: number; marginPerTradePercent: number; leverage: number; feePercentPerSide: number; positionSizingMode: string } }
export type OptimizerCandidate = { id: number; riskReward: number; minEmaGapPercent: number; maxStopDistancePercent: number; waitForConfirmationCandle: boolean; useEma100Filter: boolean; trailingStopEnabled: boolean; isBaseline: boolean; robustCandidate: boolean; robustRank: number | null; profitableMarketRatio: number; validation: { totalTrades: number; winRatePercent: number; netPnlUsdt: number; netProfitFactor: number | null; netReturnPercent: number; maxDrawdownPercent: number; medianExpectedNetTargetR: number } }

type AntiforgeryResponse = { token: string }
type ApiMessage = { message?: string }

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, { credentials: 'include', ...init })
  if (!response.ok) {
    const body = (await response.json().catch(() => ({}))) as ApiMessage
    throw new ApiError(response.status, body.message ?? 'The request could not be completed.')
  }
  return response.status === 204 ? (undefined as T) : (response.json() as Promise<T>)
}

export class ApiError extends Error {
  constructor(public readonly status: number, message: string) {
    super(message)
  }
}

async function antiforgeryToken(): Promise<string> {
  const response = await request<AntiforgeryResponse>('/api/auth/antiforgery')
  return response.token
}

export async function getCurrentUser(): Promise<CurrentUser> {
  return request<CurrentUser>('/api/auth/me')
}

export async function login(userName: string, password: string): Promise<void> {
  const token = await antiforgeryToken()
  await request<void>('/api/auth/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token },
    body: JSON.stringify({ userName, password }),
  })
}

export async function logout(): Promise<void> {
  const token = await antiforgeryToken()
  await request<void>('/api/auth/logout', {
    method: 'POST',
    headers: { 'X-CSRF-TOKEN': token },
  })
}

export async function getHealth(): Promise<HealthStatus> {
  return request<HealthStatus>('/api/health')
}

async function protectedRequest<T>(path: string, method: string, body?: unknown): Promise<T> {
  const token = await antiforgeryToken()
  return request<T>(path, { method, headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token }, body: body === undefined ? undefined : JSON.stringify(body) })
}

export const getBinanceSymbols = () => request<BinanceSymbol[]>('/api/binance/symbols')
export const getMonitoredSymbols = () => request<MonitoredSymbol[]>('/api/symbols')
export const addMonitoredSymbol = (symbol: string) => protectedRequest<MonitoredSymbol>('/api/symbols', 'POST', { symbol })
export const setSymbolEnabled = (id: number, isEnabled: boolean) => protectedRequest<MonitoredSymbol>(`/api/symbols/${id}/enabled`, 'PATCH', { isEnabled })
export const removeMonitoredSymbol = (id: number) => protectedRequest<void>(`/api/symbols/${id}`, 'DELETE')
export const getTradingSettings = () => request<TradingSettings>('/api/settings/trading')
export const updateTradingSettings = (settings: Omit<TradingSettings, 'updatedAtUtc'>) => protectedRequest<TradingSettings>('/api/settings/trading', 'PUT', settings)
export const getBacktests = () => request<BacktestRunSummary[]>('/api/backtests')
export const getBacktest = (id: number) => request<BacktestRun>(`/api/backtests/${id}`)
export const runBacktest = (requestBody: { symbol: string; interval: string; startUtc: string; endUtc: string }) => protectedRequest<BacktestRun>('/api/backtests', 'POST', requestBody)
export const deleteBacktest = (id: number) => protectedRequest<void>(`/api/backtests/${id}`, 'DELETE')
export const getOptimizerOptions = () => request<OptimizerOptions>('/api/strategy-optimizer/options')
export const getOptimizerRuns = () => request<OptimizerRun[]>('/api/strategy-optimizer/runs')
export const getOptimizerRun = (id: number) => request<OptimizerRun>(`/api/strategy-optimizer/runs/${id}`)
export const getOptimizerCandidates = (id: number, page = 1, pageSize = 50) => request<{ total: number; items: OptimizerCandidate[] }>(`/api/strategy-optimizer/runs/${id}/candidates?page=${page}&pageSize=${pageSize}`)
export const getOptimizerCandidate = (runId: number, candidateId: number) => request<{ candidate: OptimizerCandidate; markets: Array<{ symbol: string; timeframe: string; validation: OptimizerCandidate['validation'] }> }>(`/api/strategy-optimizer/runs/${runId}/candidates/${candidateId}`)
export const startOptimizer = (body: { symbols: string[]; timeframes: string[]; startUtc: string; endUtc: string; grid: OptimizerGrid }) => protectedRequest<OptimizerRun>('/api/strategy-optimizer/runs', 'POST', body)
export const cancelOptimizer = (id: number) => protectedRequest<void>(`/api/strategy-optimizer/runs/${id}/cancel`, 'POST')
export async function downloadOptimizerExcel(id: number) { await download(`/api/strategy-optimizer/runs/${id}/excel`, `ema-bot-optimizer-${id}.xlsx`) }
export const getPaperSessions = () => request<PaperSessionSummary[]>('/api/paper-sessions')
export const getActivePaperSession = () => request<PaperSession>('/api/paper-sessions/active')
export const startPaperSession = (interval: string, symbols: string[]) => protectedRequest<PaperSession>('/api/paper-sessions', 'POST', { interval, symbols })
export const stopPaperSession = (id: number) => protectedRequest<void>(`/api/paper-sessions/${id}/stop`, 'POST')
export const resumePaperSession = (id: number) => protectedRequest<PaperSession>(`/api/paper-sessions/${id}/resume`, 'POST')
export const getTrades = (filters: Record<string, string>) => request<TradeSummary[]>(`/api/trades?${new URLSearchParams(Object.entries(filters).filter(([, value]) => value)).toString()}`)
export const getTrade = (source: string, id: number, signal?: AbortSignal) => request<TradeDetail>(`/api/trades/${source.toLowerCase()}/${id}`, { signal })
export const getTradeChart = (source: string, id: number, signal?: AbortSignal) => request<TradeChartData>(`/api/trades/${source.toLowerCase()}/${id}/chart`, { signal })
export async function downloadTradeExcel(filters: Record<string, string>) { await download(`/api/trade-exports/excel?${new URLSearchParams(Object.entries(filters).filter(([, value]) => value)).toString()}`, 'ema-bot-trades.xlsx') }
export async function downloadTradePdf(source: string, id: number) { await download(`/api/trade-exports/${source.toLowerCase()}/${id}/pdf`, `ema-bot-trade-${source.toLowerCase()}-${id}.pdf`) }
async function download(path: string, fileName: string) { const response = await fetch(path, { credentials: 'include' }); if (!response.ok) { const body = await response.json().catch(() => ({})) as ApiMessage; throw new ApiError(response.status, body.message ?? 'The export could not be created.') }; const url = URL.createObjectURL(await response.blob()); const link = document.createElement('a'); link.href = url; link.download = fileName; link.click(); URL.revokeObjectURL(url) }
