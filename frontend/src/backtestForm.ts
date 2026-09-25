import type { GridStrategyId } from './gridBacktestTypes'

export function isGridStrategy(value: string): value is GridStrategyId {
  return value === 'GRID_RANGE_V1' || value === 'GRID_RANGE_4L_RESEARCH_V1' || value === 'GRID_RANGE_BREAKOUT_GUARD_RESEARCH_V1' || value === 'GRID_RANGE_BREAKOUT_GUARD_L4_RESEARCH_V1'
}
export function gridBacktestRequest(strategyId: GridStrategyId, dates: ReturnType<typeof backtestDates>, balance: string) {
  return { ...dates, strategyId, startingBalance: gridStartingBalance(balance) }
}
export const defaultBacktestStrategy = 'EMA_TREND_V1'
export function backtestDates(symbol: string, interval: string, start: string, end: string) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(start) || !/^\d{4}-\d{2}-\d{2}$/.test(end) || start > end) throw new Error('Use valid start and end dates.')
  return { symbol, interval, startUtc: `${start}T00:00:00.000Z`, endUtc: `${end}T23:59:59.999Z` }
}
export function gridStartingBalance(value: string) {
  const balance = Number(value)
  if (!Number.isFinite(balance) || balance <= 0) throw new Error('Grid starting balance must be positive.')
  return balance
}
