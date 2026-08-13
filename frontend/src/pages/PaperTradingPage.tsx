import { useCallback, useEffect, useRef, useState } from 'react'
import { ApiError, getActivePaperSession, getMarketProviderCapabilities, getMonitoredSymbols, getPaperSessions, startPaperSession, stopPaperSession, type MarketProviderCapabilities, type MonitoredSymbol, type PaperSession, type PaperSessionSummary } from '../api'

const price = (value: number | null | undefined) => value === null || value === undefined ? '—' : value.toFixed(4)
const pollIntervalMs = 1000

export function PaperTradingPage() {
  const [active, setActive] = useState<PaperSession | null>(null); const [history, setHistory] = useState<PaperSessionSummary[]>([]); const [symbols, setSymbols] = useState<MonitoredSymbol[]>([]); const [caps, setCaps] = useState<MarketProviderCapabilities | null>(null); const [interval, setInterval] = useState('3m'); const [selected, setSelected] = useState<string[]>([]); const [error, setError] = useState<string | null>(null); const [liveWarning, setLiveWarning] = useState<string | null>(null); const [busy, setBusy] = useState(false)
  const mounted = useRef(false); const pollGeneration = useRef(0); const pollFailures = useRef(0)

  const refresh = useCallback(async () => {
    const [sessions, monitored, capabilities, current] = await Promise.all([
      getPaperSessions(), getMonitoredSymbols(), getMarketProviderCapabilities(),
      getActivePaperSession().catch(error => {
        if (error instanceof ApiError && error.status === 404) return null
        throw error
      })
    ])
    if (!mounted.current) return
    setHistory(sessions); setSymbols(monitored.filter(symbol => symbol.source === 'Mt5Exness' && symbol.isEnabled)); setCaps(capabilities); setActive(current)
  }, [])

  useEffect(() => {
    mounted.current = true
    void refresh().catch(error => { if (mounted.current) setError(error instanceof Error ? error.message : 'Could not load Paper.') })
    return () => { mounted.current = false }
  }, [refresh])

  const activeSessionId = active?.id
  useEffect(() => {
    if (activeSessionId === undefined) return
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
        pollFailures.current = 0; setActive(next); setLiveWarning(null)
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
  }, [activeSessionId, refresh])

  const start = async () => { setBusy(true); setError(null); setLiveWarning(null); pollFailures.current = 0; pollGeneration.current++; try { await startPaperSession(interval, selected); await refresh() } catch (error) { if (mounted.current) setError(error instanceof Error ? error.message : 'Could not start Paper.') } finally { if (mounted.current) setBusy(false) } }
  const stop = async () => { if (!active) return; const sessionId = active.id; setBusy(true); setLiveWarning(null); pollFailures.current = 0; pollGeneration.current++; try { await stopPaperSession(sessionId); if (mounted.current) setActive(current => current?.id === sessionId ? null : current); await refresh() } catch (error) { if (mounted.current) setError(error instanceof Error ? error.message : 'Could not stop Paper.') } finally { if (mounted.current) setBusy(false) } }
  if (active) return <Active session={active} busy={busy} stop={stop} error={error} liveWarning={liveWarning} />
  const ready = caps?.liveBarProviderConfigured && selected.length > 0 && selected.every(name => symbols.find(symbol => symbol.symbol === name)?.paperCommissionPerLotPerSide !== null)
  return <div className="space-y-7"><div><p className="text-sm font-medium text-amber-700">PAPER MODE</p><h1 className="mt-2 text-3xl font-semibold">MT5 broker-aware Paper simulation</h1><p className="mt-2 text-slate-600">Observed Bid/Ask fills, MT5 lots/contracts, MT5 margin and profit calculation, and explicit per-lot commission. Additional slippage and swap financing are not simulated. No broker orders are sent.</p></div>{error && <p className="text-sm text-red-700">{error}</p>}<section className="rounded-lg border bg-white p-5"><p className="text-sm">Broker: Exness / MT5 · Live data: {caps?.liveBarProviderConfigured ? 'Configured' : 'Not configured'} · Execution: Not configured</p><div className="mt-4 grid gap-3"><label>Timeframe<select value={interval} onChange={e => setInterval(e.target.value)} className="ml-2 rounded border p-2">{['3m','5m','15m','30m','1h','2h','4h','6h','8h','12h','1d','1w','1M'].map(value => <option key={value}>{value}</option>)}</select></label>{symbols.map(symbol => <label key={symbol.id} className="flex gap-2"><input type="checkbox" checked={selected.includes(symbol.symbol)} onChange={e => setSelected(e.target.checked ? [...selected, symbol.symbol] : selected.filter(value => value !== symbol.symbol))} />{symbol.symbol} · {symbol.paperCommissionPerLotPerSide === null ? 'Commission not configured' : symbol.paperCommissionPerLotPerSide === 0 ? 'Commission-free' : `Commission ${symbol.paperCommissionPerLotPerSide}/lot/side`}</label>)}</div><button disabled={!ready || busy} onClick={() => void start()} className="mt-5 rounded bg-slate-950 px-4 py-2 text-sm font-medium text-white disabled:opacity-50">{busy ? 'Starting…' : 'Start Paper'}</button></section><History rows={history} /></div>
}

function Active({ session, busy, stop, error, liveWarning }: { session: PaperSession; busy: boolean; stop: () => Promise<void>; error: string | null; liveWarning: string | null }) {
  const lastUpdate = session.lastUpdateUtc ? new Date(session.lastUpdateUtc).toLocaleTimeString() : 'Waiting for first market update…'
  const stale = session.connectionState === 'Connected' && session.lastUpdateUtc !== null && Date.now() - new Date(session.lastUpdateUtc).getTime() > 5000
  return <div className="space-y-6"><div><p className="text-sm font-medium text-amber-700">MT5 BROKER-AWARE PAPER</p><h1 className="mt-2 text-3xl font-semibold">{session.status} · {session.interval}</h1><p className="mt-2 text-slate-600">Connection: {session.connectionState} · Last market update: {lastUpdate} · Account currency: {session.accountCurrency} · Starting balance: {session.startingBalance} · Used margin: {session.usedMargin}</p></div>{error && <p className="text-sm text-red-700">{error}</p>}{liveWarning && <p className="text-sm text-amber-700">{liveWarning}</p>}{stale && <p className="text-sm text-amber-700">Market updates appear stale.</p>}<button disabled={busy || session.status === 'Interrupted'} onClick={() => void stop()} className="rounded bg-slate-950 px-4 py-2 text-sm font-medium text-white">{busy ? 'Stopping…' : 'Stop session'}</button><section className="overflow-x-auto rounded-lg border bg-white p-5"><h2 className="font-semibold">Live symbols</h2><table className="mt-3 w-full text-left text-sm"><thead><tr><th>Symbol</th><th>Bid</th><th>Ask</th><th>Spread</th><th>Trend</th><th>Position</th></tr></thead><tbody>{session.symbols.map(symbol => <tr key={symbol.symbol} className="border-t"><td className="py-3">{symbol.symbol}</td><td>{price(symbol.latestBid)}</td><td>{price(symbol.latestAsk)}</td><td>{price(symbol.latestSpread)}</td><td>{symbol.trend ?? '—'}</td><td>{symbol.openTrade ? `${symbol.openTrade.direction} ${symbol.openTrade.lots ?? '—'} lots · margin ${symbol.openTrade.requiredMargin ?? '—'} · commission ${symbol.openTrade.roundTripCommission ?? '—'}` : '—'}</td></tr>)}</tbody></table></section><section className="rounded-lg border bg-white p-5"><h2 className="font-semibold">Completed trades</h2>{session.recentTrades.filter(trade => trade.status === 'Closed').map(trade => <p key={trade.id} className="mt-2 text-sm">{trade.symbol} {trade.direction} · Gross {trade.grossPnl ?? '—'} · Net {trade.netPnl ?? '—'} {session.accountCurrency}</p>)}</section></div>
}
function History({ rows }: { rows: PaperSessionSummary[] }) { return <section className="rounded-lg border bg-white p-5"><h2 className="font-semibold">Paper session history</h2>{rows.map(row => <p className="mt-2 text-sm" key={row.id}>{new Date(row.startedAtUtc).toLocaleString()} · {row.interval} · {row.status} · {row.completedTrades} trades · {row.netPnl} {row.accountCurrency} net</p>)}</section> }
