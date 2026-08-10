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
export type TradingSettings = { riskReward: number; fixedOrderSizeUsdt: number; waitForConfirmationCandle: boolean; useEma100Filter: boolean; trailingStopEnabled: boolean; feePercentPerSide: number; updatedAtUtc: string }
export type BacktestRunSummary = { id: number; symbol: string; interval: string; requestedStartUtc: string; requestedEndUtc: string; actualStartUtc: string | null; actualEndUtc: string | null; createdAtUtc: string; completedAtUtc: string | null; candleCount: number; totalTrades: number; winningTrades: number; losingTrades: number; breakEvenTrades: number; longTrades: number; shortTrades: number; winRatePercent: number; grossPnlUsdt: number; netPnlUsdt: number; totalFeesUsdt: number; profitFactor: number | null; averageNetPnlUsdt: number; averageRMultiple: number; maxDrawdownUsdt: number; riskReward: number; fixedOrderSizeUsdt: number; waitForConfirmationCandle: boolean; useEma100Filter: boolean; trailingStopEnabled: boolean; feePercentPerSide: number; status: string; failureMessage: string | null }
export type BacktestRun = BacktestRunSummary & { trades: BacktestTrade[] }
export type BacktestTrade = { id: number; direction: string; entryTimeUtc: string; exitTimeUtc: string; entryPrice: number; exitPrice: number; initialStopLoss: number; finalStopLoss: number; originalTakeProfit: number; finalTakeProfit: number; exitReason: string; netPnlUsdt: number; netPnlPercent: number; grossRMultiple: number }
export type PaperTrade = { id: number; symbol: string; status: string; direction: string; entryTimeUtc: string; exitTimeUtc: string | null; entryPrice: number; exitPrice: number | null; quantity: number; initialStopLoss: number; currentStopLoss: number; currentTakeProfit: number; takeProfitExtended: boolean; bestFavorableProgressPercent: number; grossPnlUsdt: number; netPnlUsdt: number; netPnlPercent: number; mfePrice: number; maePrice: number; exitReason: string | null }
export type PaperSymbol = { symbol: string; latestPrice: number | null; lastMarketEventUtc: string | null; lastClosedCandleUtc: string | null; trend: string | null; ema9: number | null; ema15: number | null; ema100: number | null; gapPercent: number | null; gapState: string | null; pendingDirection: string | null; openTrade: PaperTrade | null }
export type PaperSession = { id: number; interval: string; status: string; startedAtUtc: string; stoppedAtUtc: string | null; interruptedAtUtc: string | null; failureMessage: string | null; riskReward: number; fixedOrderSizeUsdt: number; waitForConfirmationCandle: boolean; useEma100Filter: boolean; trailingStopEnabled: boolean; feePercentPerSide: number; completedTrades: number; netPnlUsdt: number; totalFeesUsdt: number; connectionState: string; lastUpdateUtc: string | null; symbols: PaperSymbol[]; recentTrades: PaperTrade[] }
export type PaperSessionSummary = { id: number; interval: string; status: string; startedAtUtc: string; symbolCount: number; completedTrades: number; netPnlUsdt: number; totalFeesUsdt: number }

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
export const getPaperSessions = () => request<PaperSessionSummary[]>('/api/paper-sessions')
export const getActivePaperSession = () => request<PaperSession>('/api/paper-sessions/active')
export const startPaperSession = (interval: string, symbols: string[]) => protectedRequest<PaperSession>('/api/paper-sessions', 'POST', { interval, symbols })
export const stopPaperSession = (id: number) => protectedRequest<void>(`/api/paper-sessions/${id}/stop`, 'POST')
export const resumePaperSession = (id: number) => protectedRequest<PaperSession>(`/api/paper-sessions/${id}/resume`, 'POST')
