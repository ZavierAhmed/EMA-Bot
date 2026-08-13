import { useCallback, useEffect, useRef, useState } from 'react'
import { ApiError, getActivePaperSession, getMarketProviderCapabilities, getMonitoredSymbols, getPaperSessions, resumePaperSession, startPaperSession, stopPaperSession, type MarketProviderCapabilities, type MonitoredSymbol, type PaperSession, type PaperSessionSummary } from '../api'

const price = (value: number | null | undefined) => value === null || value === undefined ? '—' : value.toFixed(4)
const pollIntervalMs = 1000
type BusyAction = 'start' | 'stop' | 'resume' | 'end' | null

export function PaperTradingPage() {
  const [active, setActive] = useState<PaperSession | null>(null)
  const [history, setHistory] = useState<PaperSessionSummary[]>([])
  const [symbols, setSymbols] = useState<MonitoredSymbol[]>([])
  const [caps, setCaps] = useState<MarketProviderCapabilities | null>(null)
  const [interval, setInterval] = useState('3m')
  const [selected, setSelected] = useState<string[]>([])
  const [error, setError] = useState<string | null>(null)
  const [liveWarning, setLiveWarning] = useState<string | null>(null)
  const [busyAction, setBusyAction] = useState<BusyAction>(null)
  const mounted = useRef(false)
  const pollGeneration = useRef(0)
  const pollFailures = useRef(0)

  const refresh = useCallback(async () => {
    const [sessions, monitored, capabilities, current] = await Promise.all([
      getPaperSessions(), getMonitoredSymbols(), getMarketProviderCapabilities(),
      getActivePaperSession().catch(error => {
        if (error instanceof ApiError && error.status === 404) return null
        throw error
      })
    ])
    if (!mounted.current) return
    setHistory(sessions)
    setSymbols(monitored.filter(symbol => symbol.source === 'Mt5Exness' && symbol.isEnabled))
    setCaps(capabilities)
    setActive(current)
  }, [])

  useEffect(() => {
    mounted.current = true
    void refresh().catch(error => { if (mounted.current) setError(error instanceof Error ? error.message : 'Could not load Paper.') })
    return () => { mounted.current = false }
  }, [refresh])

  const activeSessionId = active?.id
  const activeIsRunning = active?.status === 'Running'
  useEffect(() => {
    if (activeSessionId === undefined || !activeIsRunning) return
    const sessionId = activeSessionId
    const generation = ++pollGeneration.current
    let cancelled = false
    let timer: number | undefined
    const isCurrent = () => !cancelled && mounted.current && pollGeneration.current === generation
    const schedule = () => { if (isCurrent()) timer = window.setTimeout(() => void poll(), pollIntervalMs) }
    const poll = async () => {
      try {
        const next = await getActivePaperSession()
        if (!isCurrent()) return
        if (next.id !== sessionId) { pollFailures.current = 0; setActive(next); return }
        pollFailures.current = 0
        setActive(next)
        setLiveWarning(null)
      } catch (error) {
        if (!isCurrent()) return
        if (error instanceof ApiError && error.status === 404) {
          pollGeneration.current++
          setActive(current => current?.id === sessionId ? null : current)
          void refresh().catch(refreshError => { if (mounted.current) setError(refreshError instanceof Error ? refreshError.message : 'Could not refresh Paper history.') })
          return
        }
        pollFailures.current++
        if (pollFailures.current >= 2) setLiveWarning('Live Paper refresh is temporarily unavailable.')
      }
      schedule()
    }
    timer = window.setTimeout(() => void poll(), 0)
    return () => { cancelled = true; if (timer !== undefined) window.clearTimeout(timer) }
  }, [activeIsRunning, activeSessionId, refresh])

  const start = async () => {
    setBusyAction('start'); setError(null); setLiveWarning(null); pollFailures.current = 0; pollGeneration.current++
    try { await startPaperSession(interval, selected); await refresh() }
    catch (error) { if (mounted.current) setError(error instanceof Error ? error.message : 'Could not start Paper.') }
    finally { if (mounted.current) setBusyAction(null) }
  }

  const resume = async () => {
    if (!active) return
    const sessionId = active.id
    setBusyAction('resume'); setError(null); setLiveWarning(null); pollFailures.current = 0; pollGeneration.current++
    try {
      const resumed = await resumePaperSession(sessionId)
      if (mounted.current) setActive(current => current?.id === sessionId ? resumed : current)
    } catch (error) {
      if (mounted.current) setError(error instanceof Error ? error.message : 'Could not resume Paper.')
    } finally { if (mounted.current) setBusyAction(null) }
  }

  const stop = async (endingInterrupted = false) => {
    if (!active) return
    const sessionId = active.id
    setBusyAction(endingInterrupted ? 'end' : 'stop'); setError(null); setLiveWarning(null); pollFailures.current = 0; pollGeneration.current++
    try {
      await stopPaperSession(sessionId)
      if (mounted.current) setActive(current => current?.id === sessionId ? null : current)
      await refresh()
    } catch (error) {
      if (mounted.current) setError(error instanceof Error ? error.message : endingInterrupted ? 'Could not end Paper session.' : 'Could not stop Paper.')
    } finally { if (mounted.current) setBusyAction(null) }
  }

  if (active) return <Active session={active} busyAction={busyAction} stop={stop} resume={resume} error={error} liveWarning={liveWarning} />
  const ready = caps?.liveBarProviderConfigured && selected.length > 0 && selected.every(name => symbols.find(symbol => symbol.symbol === name)?.paperCommissionPerLotPerSide !== null)
  return <div className="space-y-7"><div><p className="text-sm font-medium text-amber-700">PAPER MODE</p><h1 className="mt-2 text-3xl font-semibold">MT5 broker-aware Paper simulation</h1><p className="mt-2 text-slate-600">Observed Bid/Ask fills, MT5 lots/contracts, MT5 margin and profit calculation, and explicit per-lot commission. Additional slippage and swap financing are not simulated. No broker orders are sent.</p></div>{error && <p className="text-sm text-red-700">{error}</p>}<section className="rounded-lg border bg-white p-5"><p className="text-sm">Broker: Exness / MT5 · Live data: {caps?.liveBarProviderConfigured ? 'Configured' : 'Not configured'} · Execution: Not configured</p><div className="mt-4 grid gap-3"><label>Timeframe<select value={interval} onChange={event => setInterval(event.target.value)} className="ml-2 rounded border p-2">{['3m', '5m', '15m', '30m', '1h', '2h', '4h', '6h', '8h', '12h', '1d', '1w', '1M'].map(value => <option key={value}>{value}</option>)}</select></label>{symbols.map(symbol => <label key={symbol.id} className="flex gap-2"><input type="checkbox" checked={selected.includes(symbol.symbol)} onChange={event => setSelected(event.target.checked ? [...selected, symbol.symbol] : selected.filter(value => value !== symbol.symbol))} />{symbol.symbol} · {symbol.paperCommissionPerLotPerSide === null ? 'Commission not configured' : symbol.paperCommissionPerLotPerSide === 0 ? 'Commission-free' : `Commission ${symbol.paperCommissionPerLotPerSide}/lot/side`}</label>)}</div><button disabled={!ready || busyAction !== null} onClick={() => void start()} className="mt-5 rounded bg-slate-950 px-4 py-2 text-sm font-medium text-white disabled:opacity-50">{busyAction === 'start' ? 'Starting…' : 'Start Paper'}</button></section><History rows={history} /></div>
}

function Active({ session, busyAction, stop, resume, error, liveWarning }: { session: PaperSession; busyAction: BusyAction; stop: (endingInterrupted?: boolean) => Promise<void>; resume: () => Promise<void>; error: string | null; liveWarning: string | null }) {
  const lastUpdate = session.lastUpdateUtc ? new Date(session.lastUpdateUtc).toLocaleTimeString() : 'Waiting for first market update…'
  const stale = session.connectionState === 'Connected' && session.lastUpdateUtc !== null && Date.now() - new Date(session.lastUpdateUtc).getTime() > 5000
  const interrupted = session.status === 'Interrupted'
  const hasOpenTrade = session.symbols.some(symbol => symbol.openTrade !== null)
  return <div className="space-y-6"><div><p className="text-sm font-medium text-amber-700">MT5 BROKER-AWARE PAPER</p><h1 className="mt-2 text-3xl font-semibold">{session.status} · {session.interval}</h1><p className="mt-2 text-slate-600">Connection: {session.connectionState} · Last market update: {lastUpdate} · Account currency: {session.accountCurrency} · Starting balance: {session.startingBalance} · Used margin: {session.usedMargin}</p></div>{(interrupted || session.status === 'Faulted') && session.failureMessage && <p className="text-sm text-amber-700">{session.failureMessage}</p>}{error && <p className="text-sm text-red-700">{error}</p>}{liveWarning && <p className="text-sm text-amber-700">{liveWarning}</p>}{stale && <p className="text-sm text-amber-700">Market updates appear stale.</p>}{interrupted ? <section className="rounded-lg border border-amber-300 bg-amber-50 p-5"><h2 className="font-semibold">Paper session interrupted</h2><p className="mt-2 text-sm text-slate-700">The API restarted while this Paper session was running. No live market stream is attached until you resume it. Resume reconnects MT5; end closes this session without resuming it.</p>{hasOpenTrade && <p className="mt-2 text-sm text-amber-800">An open simulated position exists. Resume the session before ending it so an executable MT5 exit price can be obtained.</p>}<div className="mt-4 flex gap-3"><button disabled={busyAction !== null} onClick={() => void resume()} className="rounded bg-slate-950 px-4 py-2 text-sm font-medium text-white disabled:opacity-50">{busyAction === 'resume' ? 'Resuming…' : 'Resume session'}</button><button disabled={busyAction !== null} onClick={() => void stop(true)} className="rounded border border-slate-400 px-4 py-2 text-sm font-medium text-slate-800 disabled:opacity-50">{busyAction === 'end' ? 'Ending…' : 'End session'}</button></div></section> : <button disabled={busyAction !== null} onClick={() => void stop()} className="rounded bg-slate-950 px-4 py-2 text-sm font-medium text-white disabled:opacity-50">{busyAction === 'stop' ? 'Stopping…' : 'Stop session'}</button>}<section className="overflow-x-auto rounded-lg border bg-white p-5"><h2 className="font-semibold">Live symbols</h2><table className="mt-3 w-full text-left text-sm"><thead><tr><th>Symbol</th><th>Bid</th><th>Ask</th><th>Spread</th><th>Trend</th><th>Position</th></tr></thead><tbody>{session.symbols.map(symbol => <tr key={symbol.symbol} className="border-t"><td className="py-3">{symbol.symbol}</td><td>{price(symbol.latestBid)}</td><td>{price(symbol.latestAsk)}</td><td>{price(symbol.latestSpread)}</td><td>{symbol.trend ?? '—'}</td><td>{symbol.openTrade ? `${symbol.openTrade.direction} ${symbol.openTrade.lots ?? '—'} lots · entry ${price(symbol.openTrade.entryPrice)} · SL ${price(symbol.openTrade.currentStopLoss)} · TP ${price(symbol.openTrade.currentTakeProfit)} · margin ${symbol.openTrade.requiredMargin ?? '—'} · commission ${symbol.openTrade.roundTripCommission ?? '—'}` : '—'}</td></tr>)}</tbody></table></section><section className="rounded-lg border bg-white p-5"><h2 className="font-semibold">Completed trades</h2>{session.recentTrades.filter(trade => trade.status === 'Closed').map(trade => <p key={trade.id} className="mt-2 text-sm">{trade.symbol} {trade.direction} · Gross {trade.grossPnl ?? '—'} · Net {trade.netPnl ?? '—'} {session.accountCurrency}</p>)}</section></div>
}

function History({ rows }: { rows: PaperSessionSummary[] }) { return <section className="rounded-lg border bg-white p-5"><h2 className="font-semibold">Paper session history</h2>{rows.map(row => <p className="mt-2 text-sm" key={row.id}>{new Date(row.startedAtUtc).toLocaleString()} · {row.interval} · {row.status} · {row.completedTrades} trades · {row.netPnl} {row.accountCurrency} net</p>)}</section> }
