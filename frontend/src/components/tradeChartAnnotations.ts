import type { DemoExecutionManagementAction, ExnessDemoTradeDetail, TradeDetail } from '../api'

export type TradeChartVisibility = { ema9: boolean; ema15: boolean; ema100: boolean; levels: boolean; markers: boolean; execution: boolean }
type LevelChange = { timeUtc: string; stopLoss: number | null; takeProfit: number | null }
export type TradeChartAnnotations = {
  direction: string; crossoverTimeUtc: string; signalTimeUtc: string; expectedEntryOpenUtc: string | null
  actualEntryTimeUtc: string | null; actualEntryPrice: number | null; actualExitTimeUtc: string | null; actualExitPrice: number | null; exitLabel: string
  initialStopLoss: number | null; initialTakeProfit: number | null; currentStopLoss: number | null; currentTakeProfit: number | null; levelChanges: LevelChange[]
  executionKind: 'PaperBidAsk' | 'BrokerFillExit' | 'None'
}

export const annotationsFromTradeDetail = (detail: TradeDetail): TradeChartAnnotations => ({
  direction: detail.summary.direction, crossoverTimeUtc: detail.crossoverTimeUtc, signalTimeUtc: detail.signalTimeUtc,
  expectedEntryOpenUtc: detail.summary.entryTimeUtc, actualEntryTimeUtc: detail.summary.entryTimeUtc, actualEntryPrice: detail.summary.entryPrice,
  actualExitTimeUtc: detail.summary.exitTimeUtc, actualExitPrice: detail.summary.exitPrice, exitLabel: detail.summary.exitReason ?? 'Exit',
  initialStopLoss: detail.initialStopLoss, initialTakeProfit: detail.originalTakeProfit, currentStopLoss: detail.finalStopLoss, currentTakeProfit: detail.finalTakeProfit,
  levelChanges: detail.hasDetailedManagementHistory ? detail.events.filter(item => item.effectiveTimeUtc && (item.newStop !== null || item.newTakeProfit !== null)).map(item => ({ timeUtc: item.effectiveTimeUtc!, stopLoss: item.newStop, takeProfit: item.newTakeProfit })) : [],
  executionKind: detail.summary.source === 'Paper' && detail.summary.marketDataSource === 'Mt5Exness' ? 'PaperBidAsk' : 'None'
})

const appliedLevelChanges = (actions: DemoExecutionManagementAction[]): LevelChange[] => actions
  .filter(action => action.state === 'Applied' && (action.appliedStopLoss !== null || action.appliedTakeProfit !== null) && (action.completedAtUtc ?? action.reconciledAtUtc))
  .map(action => ({ timeUtc: (action.completedAtUtc ?? action.reconciledAtUtc)!, stopLoss: action.appliedStopLoss, takeProfit: action.appliedTakeProfit }))

export const annotationsFromExnessDemo = (detail: ExnessDemoTradeDetail): TradeChartAnnotations => {
  const execution = detail.execution
  return {
    direction: detail.intent.direction, crossoverTimeUtc: detail.intent.crossoverTimeUtc, signalTimeUtc: detail.intent.signalTimeUtc, expectedEntryOpenUtc: detail.intent.expectedEntryOpenUtc,
    actualEntryTimeUtc: execution.brokerExecutedAtUtc, actualEntryPrice: execution.averageFillPrice, actualExitTimeUtc: execution.brokerClosedAtUtc ?? execution.closedAtUtc, actualExitPrice: execution.averageClosePrice,
    exitLabel: execution.nativeExitReasonConflicted ? 'Exit' : execution.nativeExitReason === 'TP' ? 'TP' : execution.nativeExitReason === 'SL' ? 'SL' : 'Exit',
    initialStopLoss: execution.requestedStopLoss, initialTakeProfit: execution.requestedTakeProfit, currentStopLoss: execution.currentStopLoss, currentTakeProfit: execution.currentTakeProfit,
    levelChanges: appliedLevelChanges(detail.managementActions), executionKind: 'BrokerFillExit'
  }
}
