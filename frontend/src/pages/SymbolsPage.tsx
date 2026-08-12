import { useEffect, useMemo, useState } from 'react'
import { addMonitoredSymbol, getBinanceSymbols, getMonitoredSymbols, removeMonitoredSymbol, setSymbolEnabled, type BinanceSymbol, type MonitoredSymbol } from '../api'

export function SymbolsPage() {
  const [available, setAvailable] = useState<BinanceSymbol[]>([])
  const [monitored, setMonitored] = useState<MonitoredSymbol[]>([])
  const [search, setSearch] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [isLoading, setIsLoading] = useState(true)

  async function refresh() {
    setError(null); setIsLoading(true)
    try { const [contracts, saved] = await Promise.all([getBinanceSymbols(), getMonitoredSymbols()]); setAvailable(contracts); setMonitored(saved) }
    catch (requestError) { setError(requestError instanceof Error ? requestError.message : 'Could not load symbols.') }
    finally { setIsLoading(false) }
  }
  useEffect(() => { void refresh() }, [])
  const visible = useMemo(() => available.filter(contract => contract.symbol.includes(search.trim().toUpperCase())).slice(0, 100), [available, search])
  const monitoredSymbols = new Set(monitored.map(symbol => symbol.symbol))
  async function add(symbol: string) { try { await addMonitoredSymbol(symbol); await refresh() } catch (requestError) { setError(requestError instanceof Error ? requestError.message : 'Could not add symbol.') } }
  async function toggle(symbol: MonitoredSymbol) { try { await setSymbolEnabled(symbol.id, !symbol.isEnabled); await refresh() } catch (requestError) { setError(requestError instanceof Error ? requestError.message : 'Could not update symbol.') } }
  async function remove(id: number) { try { await removeMonitoredSymbol(id); await refresh() } catch (requestError) { setError(requestError instanceof Error ? requestError.message : 'Could not remove symbol.') } }

  return <div className="space-y-10"><div><p className="text-sm font-medium text-slate-500">Market data</p><h1 className="mt-2 text-3xl font-semibold tracking-tight">Symbols</h1><p className="mt-3 text-slate-600">Current legacy market-data provider: choose active USDT-margined Binance Futures perpetual contracts to monitor.</p></div>{error && <p role="alert" className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}<section className="rounded-lg border border-slate-200 bg-white"><div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-200 p-5"><div><h2 className="font-semibold">Available legacy Binance Futures symbols</h2><p className="mt-1 text-sm text-slate-500">Search and add contracts individually.</p></div><button onClick={() => void refresh()} className="rounded-md border border-slate-300 px-3 py-2 text-sm font-medium hover:bg-slate-50">Refresh</button></div><div className="p-5"><input aria-label="Search symbols" value={search} onChange={event => setSearch(event.target.value)} placeholder="Search BTCUSDT" className="mb-4 w-full max-w-sm rounded-md border border-slate-300 px-3 py-2 text-sm outline-none focus:ring-1" />{isLoading ? <p className="text-sm text-slate-500">Loading symbols...</p> : <SymbolTable rows={visible} monitored={monitoredSymbols} onAdd={add} />}</div></section><section className="rounded-lg border border-slate-200 bg-white"><div className="border-b border-slate-200 p-5"><h2 className="font-semibold">Monitored Symbols</h2></div><div className="p-5">{monitored.length === 0 ? <p className="text-sm text-slate-500">No symbols are monitored yet.</p> : <table className="w-full text-left text-sm"><thead className="text-slate-500"><tr><th className="pb-3 font-medium">Symbol</th><th className="pb-3 font-medium">Base</th><th className="pb-3 font-medium">Quote</th><th className="pb-3 font-medium">Status</th><th /></tr></thead><tbody>{monitored.map(symbol => <tr key={symbol.id} className="border-t border-slate-100"><td className="py-3 font-medium">{symbol.symbol}</td><td>{symbol.baseAsset}</td><td>{symbol.quoteAsset}</td><td>{symbol.isEnabled ? 'Enabled' : 'Disabled'}</td><td className="space-x-3 text-right"><button onClick={() => void toggle(symbol)} className="text-slate-600 hover:text-slate-950">{symbol.isEnabled ? 'Disable' : 'Enable'}</button><button onClick={() => void remove(symbol.id)} className="text-red-700 hover:text-red-900">Remove</button></td></tr>)}</tbody></table>}</div></section></div>
}

function SymbolTable({ rows, monitored, onAdd }: { rows: BinanceSymbol[]; monitored: Set<string>; onAdd: (symbol: string) => Promise<void> }) { return <table className="w-full text-left text-sm"><thead className="text-slate-500"><tr><th className="pb-3 font-medium">Symbol</th><th className="pb-3 font-medium">Base</th><th className="pb-3 font-medium">Quote</th><th /></tr></thead><tbody>{rows.map(contract => <tr key={contract.symbol} className="border-t border-slate-100"><td className="py-3 font-medium">{contract.symbol}</td><td>{contract.baseAsset}</td><td>{contract.quoteAsset}</td><td className="text-right">{monitored.has(contract.symbol) ? <span className="text-slate-400">Added</span> : <button onClick={() => void onAdd(contract.symbol)} className="text-slate-700 hover:text-slate-950">Add</button>}</td></tr>)}</tbody></table> }
