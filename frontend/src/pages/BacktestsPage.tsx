import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { Link } from 'react-router-dom'
import type { BacktestRun, BacktestRunSummary, MonitoredSymbol, Mt5HistoricalBacktestEconomicsPreview } from '../api'
import { ApiError, downloadBacktestExcel, getBacktests, getMonitoredSymbols, getMt5HistoricalBacktestEconomicsPreview, runBacktest } from '../api'
import { deleteGridBacktest, downloadGridBacktestExcel, downloadGridGuardResearch, downloadGridStrictGuardResearch, getGridBacktest, getGridBacktests, runGridBacktest } from '../api'
import type { GridBacktestDetail, GridRun } from '../gridBacktestTypes'
import { GridBacktestResult } from './GridBacktestResult'
import { backtestDates, defaultBacktestStrategy, gridBacktestRequest, isGridStrategy } from '../backtestForm'

const intervals = ['3m', '5m', '15m', '30m', '1h', '2h', '4h', '6h', '8h', '12h', '1d', '1w', '1M']
// The backend permits up to 30 minutes for bounded native-economics research; retain one minute for response delivery.
const backtestTimeoutMilliseconds = 1_860_000

export function BacktestsPage() {
  const [strategy, setStrategy] = useState(defaultBacktestStrategy)
  const [startingBalance, setStartingBalance] = useState('1000')
  const [gridRuns, setGridRuns] = useState<GridRun[]>([])
  const [gridSelected, setGridSelected] = useState<GridBacktestDetail | null>(null)
  const [gridBusyId, setGridBusyId] = useState<number | null>(null)
  const [symbols, setSymbols] = useState<MonitoredSymbol[]>([])
  const [runs, setRuns] = useState<BacktestRunSummary[]>([])
  const [selected, setSelected] = useState<BacktestRun | null>(null)
  const [symbol, setSymbol] = useState('')
  const [interval, setInterval] = useState('30m')
  const [start, setStart] = useState('')
  const [end, setEnd] = useState('')
  const [busy, setBusy] = useState(false)
  const [exportingId, setExportingId] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [economics, setEconomics] = useState<Mt5HistoricalBacktestEconomicsPreview | null>(null)

  async function refresh() {
    const [saved, history, gridHistory] = await Promise.all([getMonitoredSymbols(), getBacktests(), getGridBacktests()])
    setSymbols(saved.filter(item => item.isEnabled && item.source === 'Mt5Exness'))
    setRuns(history)
    setGridRuns(gridHistory)
  }

  useEffect(() => {
    void refresh().catch(loadError => setError(loadError instanceof Error ? loadError.message : 'Could not load backtests.'))
  }, [])
  useEffect(() => { if (!symbol) { setEconomics(null); return } void getMt5HistoricalBacktestEconomicsPreview(symbol).then(setEconomics).catch(value => setEconomics({ ready: false, reason: value instanceof Error ? value.message : 'MT5 economics preview is unavailable.', brokerSymbol: symbol, accountCurrency: null, startingBalance: null, sizingMode: null, fixedLots: null, marginPerTradePercent: null, riskPerTradePercent: null, commissionPerLotPerSide: null, historicalSpreadModel: null, chartMode: null, contractSize: null, volumeMin: null, volumeMax: null, volumeStep: null, volumeLimit: null, stopsLevelPoints: null, tradeMode: null, pointSize: null })) }, [symbol])

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!symbol || !start || !end || busy || !economics?.ready) return
    if (isGridStrategy(strategy) && (!Number.isFinite(Number(startingBalance)) || Number(startingBalance) <= 0)) { setError('Grid starting balance must be positive.'); return }
    const controller = new AbortController()
    const timeout = window.setTimeout(() => controller.abort(), backtestTimeoutMilliseconds)
    setBusy(true)
    setError(null)
    try {
      const dates = backtestDates(symbol, interval, start, end)
      if (isGridStrategy(strategy)) setGridSelected(await runGridBacktest(gridBacktestRequest(strategy, dates, startingBalance), controller.signal))
      else setSelected(await runBacktest(dates, controller.signal))
      await refresh()
    } catch (requestError) {
      setError(controller.signal.aborted ? 'Backtest did not complete within the maximum client wait time. Verify MT5 availability and retry.' : requestError instanceof Error ? requestError.message : 'Backtest failed.')
    } finally {
      window.clearTimeout(timeout)
      setBusy(false)
    }
  }

  async function exportExcel(id: number) {
    setExportingId(id)
    setError(null)
    try {
      await downloadBacktestExcel(id)
    } catch (exportError) {
      setError(exportError instanceof ApiError && exportError.status === 404 ? 'Backtest not found.' : 'Backtest Excel export could not be created.')
    } finally {
      setExportingId(null)
    }
  }

  async function gridAction(id: number, action: 'open' | 'export' | 'research' | 'strictResearch' | 'delete') {
    setGridBusyId(id); setError(null)
    try {
      if (action === 'open') { const detail = await getGridBacktest(id); setGridSelected(detail); setStrategy(detail.run.strategyId) }
      else if (action === 'export') await downloadGridBacktestExcel(id)
      else if (action === 'strictResearch') await downloadGridStrictGuardResearch(id)
      else if (action === 'research') await downloadGridGuardResearch(id)
      else { await deleteGridBacktest(id); if (gridSelected?.run.id === id) setGridSelected(null); await refresh() }
    } catch (value) { setError(value instanceof Error ? value.message : 'Grid action failed.') }
    finally { setGridBusyId(null) }
  }

  return <div className="space-y-8">
    <div>
      <p className="text-sm font-medium text-slate-500">Simulation</p>
      <h1 className="mt-2 text-3xl font-semibold">Backtests</h1>
      <p className="mt-2 text-slate-600">New MT5 / Exness backtests use broker-native lots, margin and profit calculations with a historical Bid/Ask bar-spread approximation. Swap and additional slippage are not modeled.</p>
    </div>
    <form onSubmit={submit} className="grid gap-4 rounded-lg border border-slate-200 bg-white p-5 md:grid-cols-4">
      <label className="text-sm font-medium md:col-span-4">Strategy<select disabled={busy} value={strategy} onChange={e => setStrategy(e.target.value)} className="mt-2 block rounded border border-slate-300 p-2"><option value="EMA_TREND_V1">EMA Trend</option><option value="GRID_RANGE_V1">Grid Range V1</option><option value="GRID_RANGE_4L_RESEARCH_V1">Grid Range Research — 4 Levels</option><option value="GRID_RANGE_BREAKOUT_GUARD_RESEARCH_V1">Grid Range Research — Breakout Guard</option></select></label>
      {isGridStrategy(strategy) && <><p className="text-sm text-slate-600 md:col-span-4">{strategy === 'GRID_RANGE_BREAKOUT_GUARD_RESEARCH_V1' ? 'RESEARCH ONLY — 5-level Grid with normal 1% total basket risk. After Level 3, the basket exits at candle close when ADX has risen by at least 1.5 and price has closed adversely for 2 consecutive candles.' : strategy === 'GRID_RANGE_4L_RESEARCH_V1' ? 'RESEARCH ONLY — Research variant: 4 equal-lot levels, emergency stop one spacing beyond Level 4, 1% total basket risk. Not used by Paper/Demo/Live.' : 'Range / mean-reversion, 5 equal-lot levels, non-martingale, 1% total basket risk.'}</p><label className="text-sm font-medium">Starting balance ({economics?.accountCurrency ?? 'account currency'})<input type="number" required min="0.00000001" step="any" value={startingBalance} onChange={e => setStartingBalance(e.target.value)} className="mt-2 block w-full rounded border border-slate-300 p-2" /></label></>}
      <Select label="Symbol" value={symbol} onChange={setSymbol} options={symbols.map(item => item.displayName ? `${item.symbol} — ${item.displayName}` : item.symbol)} values={symbols.map(item => item.symbol)} />
      <Select label="Timeframe" value={interval} onChange={setInterval} options={intervals} />
      <label className="text-sm font-medium">Start date<input className="mt-2 block w-full rounded border border-slate-300 p-2" type="date" value={start} onChange={event => setStart(event.target.value)} required /></label>
      <label className="text-sm font-medium">End date<input className="mt-2 block w-full rounded border border-slate-300 p-2" type="date" value={end} onChange={event => setEnd(event.target.value)} required /></label>
      <EconomicsPreview value={economics} grid={isGridStrategy(strategy)} />
      <button disabled={busy || !economics?.ready} className="rounded bg-slate-950 px-4 py-2 text-sm font-medium text-white disabled:opacity-50 md:col-span-4">{busy ? 'Running backtest...' : 'Run Backtest'}</button>
    </form>
    {error && <p role="alert" className="text-sm text-red-700">{error}</p>}
    {strategy === 'EMA_TREND_V1' && selected && <Result run={selected} exporting={exportingId === selected.id} exportExcel={exportExcel} />}
    {isGridStrategy(strategy) && gridSelected && <GridBacktestResult detail={gridSelected} busy={gridBusyId === gridSelected.run.id} exportExcel={() => void gridAction(gridSelected.run.id, 'export')} exportGuardResearch={() => void gridAction(gridSelected.run.id, 'research')} exportStrictGuardResearch={() => void gridAction(gridSelected.run.id, 'strictResearch')} />}
    <section className="rounded-lg border border-slate-200 bg-white p-5"><h2 className="font-semibold">Recent Grid backtests</h2><div className="overflow-x-auto"><table className="mt-4 w-full text-left text-sm"><thead><tr><th>Strategy</th><th>Symbol / timeframe</th><th>Dates</th><th>Baskets</th><th>Net P/L</th><th>Actions</th></tr></thead><tbody>{gridRuns.map(run => <tr key={run.id} className="border-t"><td className="py-3">{run.strategyId}</td><td>{run.symbol} · {run.interval}</td><td>{run.requestedStartUtc.slice(0, 10)} – {run.requestedEndUtc.slice(0, 10)}</td><td>{run.basketCount}</td><td>{run.netPnl.toFixed(2)} {run.accountCurrency}</td><td className="space-x-2"><button disabled={gridBusyId !== null} onClick={() => void gridAction(run.id, 'open')}>Open</button><button disabled={gridBusyId !== null} onClick={() => void gridAction(run.id, 'export')}>Export Excel</button><button disabled={gridBusyId !== null} onClick={() => { if (window.confirm('Delete this saved Grid backtest?')) void gridAction(run.id, 'delete') }}>Delete</button></td></tr>)}</tbody></table></div>{gridRuns.length === 0 && <p className="mt-3 text-sm text-slate-500">No saved Grid runs.</p>}</section>
    <section className="rounded-lg border border-slate-200 bg-white p-5">
      <h2 className="font-semibold">Recent EMA Trend backtests</h2>
      <table className="mt-4 w-full text-left text-sm">
        <thead className="text-slate-500"><tr><th>Symbol</th><th>Data</th><th>Interval</th><th>Trades</th><th>Net PnL</th><th>Actions</th></tr></thead>
        <tbody>{runs.map(run => <tr key={run.id} className="border-t"><td className="py-3">{run.symbol}</td><td>{run.marketDataSourceLabel}</td><td>{run.interval}</td><td>{run.totalTrades}</td><td>{run.netPnlUsdt.toFixed(2)}</td><td><button type="button" disabled={exportingId === run.id} onClick={() => void exportExcel(run.id)} className="rounded border px-3 py-1 text-xs disabled:opacity-50">{exportingId === run.id ? 'Exporting…' : 'Export Excel'}</button></td></tr>)}</tbody>
      </table>
    </section>
  </div>
}

function EconomicsPreview({ value, grid }: { value: Mt5HistoricalBacktestEconomicsPreview | null; grid: boolean }) { if (!value) return <p className="text-sm text-slate-500 md:col-span-4">Select an MT5 instrument to inspect native historical economics.</p>; if (!value.ready) return <p role="alert" className="text-sm text-amber-800 md:col-span-4">MT5 Historical Economics unavailable: {value.reason}</p>; const sizing = grid ? '1% total basket price-risk' : value.sizingMode === 'FixedLots' ? `${value.fixedLots} lots` : value.sizingMode === 'MarginPercent' ? `${value.marginPerTradePercent}% margin` : `Initial-stop risk · ${value.riskPerTradePercent}% of current equity`; return <div className="rounded border border-slate-200 bg-slate-50 p-3 text-xs md:col-span-4"><p className="font-semibold">MT5 Historical Economics</p><p className="mt-1">Market data: MT5 / Exness · Broker: {value.brokerSymbol} · Account currency: {value.accountCurrency}{!grid && <> · Starting balance: {value.startingBalance}</>}</p><p>Sizing: {sizing} · Commission: {value.commissionPerLotPerSide}/lot/side · Chart: {value.chartMode}</p><p>Spread: {value.historicalSpreadModel} · Point: {value.pointSize} · Contract: {value.contractSize} · Volume: {value.volumeMin}/{value.volumeMax}/{value.volumeStep} · Volume limit: {value.volumeLimit ?? '—'} · Stops level: {value.stopsLevelPoints} · Trade mode: {value.tradeMode ?? '—'}</p></div> }

function Select({ label, value, onChange, options, values }: { label: string; value: string; onChange: (value: string) => void; options: string[]; values?: string[] }) {
  return <label className="text-sm font-medium">{label}<select value={value} onChange={event => onChange(event.target.value)} className="mt-2 block w-full rounded border border-slate-300 p-2" required><option value="">Select</option>{options.map((option, index) => <option key={values?.[index] ?? option} value={values?.[index] ?? option}>{option}</option>)}</select></label>
}

function Result({ run, exporting, exportExcel }: { run: BacktestRun; exporting: boolean; exportExcel: (id: number) => Promise<void> }) {
  return <section className="rounded-lg border border-slate-200 bg-white p-5">
    <div className="flex flex-wrap items-start justify-between gap-3"><div><h2 className="font-semibold">{run.symbol} · {run.interval}</h2><p className="mt-2 text-sm text-amber-800">Market data: {run.marketDataSourceLabel} · Economics: {run.economicsMode === 'Mt5HistoricalBidAsk' ? `MT5 native Bid/Ask (${run.accountCurrency ?? 'account currency'})` : 'Legacy compatibility model'}</p></div><button type="button" disabled={exporting} onClick={() => void exportExcel(run.id)} className="rounded border px-3 py-2 text-sm disabled:opacity-50">{exporting ? 'Exporting…' : 'Export Excel'}</button></div>
    <p className="mt-2 text-sm text-slate-600">Frozen settings: Adaptive initial SL {run.useAdaptiveInitialStop ? 'On' : 'Off'} · Same-trend re-entry {run.sameTrendReentryEnabled ? 'On' : 'Off'} · Maximum re-entry age {run.maxReentryAgeBars} bars · Exit on opposite crossover {run.exitOnOppositeCrossover ? 'On' : 'Off'}</p>
    {run.economicsMode === 'Mt5HistoricalBidAsk' && <p className="mt-2 text-sm text-slate-600">Frozen native sizing: {run.nativePositionSizingMode === null ? 'Not captured historically' : run.nativePositionSizingMode === 'FixedLots' ? `Fixed lots · ${run.nativeFixedLots ?? '—'} lots · Starting balance ${run.startingBalance ?? '—'} ${run.accountCurrency ?? ''}` : run.nativePositionSizingMode === 'MarginPercent' ? `Margin allocation · ${run.nativeMarginPerTradePercent ?? '—'}% · Starting balance ${run.startingBalance ?? '—'} ${run.accountCurrency ?? ''}` : `Initial-stop risk · ${run.nativeRiskPerTradePercent ?? '—'}% of current equity · Starting balance ${run.startingBalance ?? '—'} ${run.accountCurrency ?? ''}`}</p>}
    <div className="mt-4 grid grid-cols-2 gap-4 text-sm md:grid-cols-5"><Metric label="Candles" value={run.candleCount} /><Metric label="Trades" value={run.totalTrades} /><Metric label="Net PnL" value={run.netPnlUsdt.toFixed(2)} />{run.economicsMode === 'Mt5HistoricalBidAsk' ? <><Metric label="Gross PF" value={run.grossProfitFactor?.toFixed(2) ?? '—'} /><Metric label="Net PF" value={run.netProfitFactor?.toFixed(2) ?? '—'} /></> : <Metric label="Legacy gross PF" value={run.profitFactor?.toFixed(2) ?? '—'} />}<Metric label="Crossovers" value={run.totalCrossovers} /><Metric label="Long / short signals" value={`${run.longSignals} / ${run.shortSignals}`} /><Metric label="EMA100 rejected" value={run.rejectedByEma100} /><Metric label="Confirmation failed" value={run.confirmationFailed} /><Metric label="Invalid SL" value={run.invalidStopLoss} /><Metric label="Skipped while open" value={run.skippedWhilePositionOpen} />{run.economicsMode === 'Mt5HistoricalBidAsk' && <><Metric label="Insufficient margin" value={run.rejectedByInsufficientMargin ?? 0} /><Metric label="Invalid volume" value={run.rejectedByInvalidVolume ?? 0} /><Metric label="Risk below minimum lot" value={run.rejectedByRiskBelowMinimumVolume ?? '—'} /><Metric label="Trade mode rejected" value={run.rejectedByTradeMode ?? 0} /></>}<Metric label="No entry candle" value={run.noEntryCandle} /></div>
    <table className="mt-6 w-full text-left text-xs"><thead className="text-slate-500"><tr><th>#</th><th>Direction</th><th>Entry</th><th>Exit</th><th>Reason</th><th>Net PnL</th><th>Analyze</th></tr></thead><tbody>{run.trades.map((trade, index) => <tr key={trade.id || index} className="border-t"><td className="py-2">{index + 1}</td><td>{trade.direction}</td><td>{trade.entryPrice}</td><td>{trade.exitPrice}</td><td>{trade.exitReason}</td><td>{trade.netPnlUsdt.toFixed(2)}</td><td><Link to={`/trades?source=backtest&id=${trade.id}`}>View chart</Link></td></tr>)}</tbody></table>
  </section>
}

function Metric({ label, value }: { label: string; value: string | number }) {
  return <div><p className="text-slate-500">{label}</p><p className="mt-1 font-medium">{value}</p></div>
}
