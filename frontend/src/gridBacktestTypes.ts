// Dedicated Grid API contracts; no EMA trade fields.
export type GridRun = {
  id: number
  strategyId: string
  marketDataSource: string
  symbol: string
  brokerSymbol: string
  interval: string
  status: string
  accountCurrency: string
  historicalSpreadModel: string
  historicalChartMode: string
  tradeMode: string
  failureMessage: string | null
  requestedStartUtc: string
  requestedEndUtc: string
  createdAtUtc: string
  completedAtUtc: string
  actualStartUtc: string | null
  actualEndUtc: string | null
  startingBalance: number
  endingBalance: number
  gridBasketRiskPercent: number
  adxThreshold: number
  atrSpacingMultiplier: number
  commissionPerLotPerSide: number
  contractSize: number
  volumeMin: number
  volumeMax: number
  volumeStep: number
  pointSize: number
  grossPnl: number
  totalCommission: number
  netPnl: number
  averageNetPnl: number
  maxDrawdown: number
  volumeLimit: number | null
  tickSize: number | null
  tickValueProfit: number | null
  tickValueLoss: number | null
  grossProfitFactor: number | null
  netProfitFactor: number | null
  stopsLevelPoints: number | null
  levelCount: number
  cooldownBars: number
  rangeLookback: number
  atrPeriod: number
  adxPeriod: number
  reportingCandleCount: number
  warmupCandleCount: number
  basketCount: number
  winningBaskets: number
  losingBaskets: number
  breakEvenBaskets: number
  longBaskets: number
  shortBaskets: number
  qualifiedCycles: number
  rejectedQualificationCount: number
  ambiguousFirstSideCount: number
  noFillCyclesAtEndOfData: number
  tradeModeBlockedCount: number
  riskBelowMinimumVolumeCount: number
  riskCannotBeSafelySizedCount: number
  riskCalculationUnavailableCount: number
  marginCalculationUnavailableCount: number
  insufficientMarginCount: number
  economicsCallCount: number
  economicsElapsedMilliseconds: number
}
export type GridCycle = {
  id: number
  gridBacktestRunId: number
  sequence: number
  qualificationTimeUtc: string
  rangeHigh: number
  rangeLow: number
  qualificationClose: number
  atr: number
  adx: number
  anchor: number
  spacing: number
  longStop: number
  shortStop: number
  entryEquity: number
  targetRiskPercent: number
  targetRiskAmount: number
  commonLots: number
  longRisk: number | null
  shortRisk: number | null
  longMargin: number | null
  shortMargin: number | null
  allowedDirections: string
}
export type GridPlannedLevel = {
  id: number
  gridBacktestCycleId: number
  levelNumber: number
  direction: string
  price: number
  lots: number
  initialStopRisk: number | null
  requiredMargin: number | null
  allowed: boolean
}
export type GridBasket = {
  id: number
  gridBacktestCycleId: number
  direction: string
  exitReason: string
  exitTimeUtc: string
  exitPrice: number
  exitSpread: number
  grossPnl: number
  commission: number
  netPnl: number
  endingBalance: number
  usedMargin: number
  actualFilledInitialStopRisk: number
  plannedWorstCasePriceRisk: number | null
  plannedWorstCaseMargin: number | null
}
export type GridLeg = {
  id: number
  gridBacktestBasketId: number
  levelNumber: number
  direction: string
  fillTimeUtc: string
  plannedPrice: number
  fillPrice: number
  lots: number
  requiredMargin: number
  initialStopRisk: number
  entryCommission: number
  exitCommission: number
  totalCommission: number
  grossPnl: number
  netPnl: number
}
export type GridEvent = {
  id: number
  gridBacktestRunId: number
  sequence: number
  time: string
  type: string
  level: number | null
  executablePrice: number | null
  detail: string | null
}
export type GridDiagnostic = {
  id: number
  gridBacktestRunId: number
  sequence: number
  time: string | null
  code: string
  domainCode: string | null
  operation: string | null
  brokerSymbol: string | null
  direction: string | null
  detail: string | null
  lots: number | null
  entry: number | null
  stop: number | null
}
export type GridBacktestDetail = { strategyId: 'GRID_RANGE_V1'; run: GridRun; cycles: { cycle: GridCycle; plannedLevels: GridPlannedLevel[] }[]; baskets: { basket: GridBasket; legs: GridLeg[] }[]; events: GridEvent[]; diagnostics: GridDiagnostic[] }
