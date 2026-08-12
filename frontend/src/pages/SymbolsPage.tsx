import { useEffect, useState } from 'react'
import { getMarketProviderCapabilities, getMonitoredSymbols, removeMonitoredSymbol, setSymbolEnabled, type MarketProviderCapabilities, type MonitoredSymbol } from '../api'

export function SymbolsPage() {
  const [monitored, setMonitored] = useState<MonitoredSymbol[]>([])
  const [error, setError] = useState<string | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [capabilities, setCapabilities] = useState<MarketProviderCapabilities | null>(null)

  async function refresh() {
    setError(null); setIsLoading(true)
    try { const [symbols, providerCapabilities] = await Promise.all([getMonitoredSymbols(), getMarketProviderCapabilities()]); setMonitored(symbols); setCapabilities(providerCapabilities) }
    catch (requestError) { setError(requestError instanceof Error ? requestError.message : 'Could not load monitored instruments.') }
    finally { setIsLoading(false) }
  }

  useEffect(() => { void refresh() }, [])
  async function toggle(symbol: MonitoredSymbol) { try { await setSymbolEnabled(symbol.id, !symbol.isEnabled); await refresh() } catch (requestError) { setError(requestError instanceof Error ? requestError.message : 'Could not update instrument.') } }
  async function remove(id: number) { try { await removeMonitoredSymbol(id); await refresh() } catch (requestError) { setError(requestError instanceof Error ? requestError.message : 'Could not remove instrument.') } }

  return <div className="space-y-8"><div><p className="text-sm font-medium text-slate-500">Market data</p><h1 className="mt-2 text-3xl font-semibold tracking-tight">Symbols</h1><p className="mt-3 text-slate-600">Manage the existing instruments retained for legacy historical research.</p></div><p className="rounded-lg border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900">MT5 instrument catalog contracts are implemented, but the MT5 provider is not connected yet. Existing symbols remain available for legacy historical research.</p>{error && <p role="alert" className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}<section className="rounded-lg border border-slate-200 bg-white"><div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-200 p-5"><div><h2 className="font-semibold">Monitored instruments</h2><p className="mt-1 text-sm text-slate-500">Catalog status: {capabilities?.instrumentCatalogConfigured ? 'Configured' : capabilities ? 'Not configured' : 'Checking…'}.</p></div><button onClick={() => void refresh()} className="rounded-md border border-slate-300 px-3 py-2 text-sm font-medium hover:bg-slate-50">Refresh</button></div><div className="p-5">{isLoading ? <p className="text-sm text-slate-500">Loading instruments...</p> : monitored.length === 0 ? <p className="text-sm text-slate-500">No instruments are monitored yet.</p> : <table className="w-full text-left text-sm"><thead className="text-slate-500"><tr><th className="pb-3 font-medium">Symbol</th><th className="pb-3 font-medium">Base</th><th className="pb-3 font-medium">Quote</th><th className="pb-3 font-medium">Status</th><th /></tr></thead><tbody>{monitored.map(symbol => <tr key={symbol.id} className="border-t border-slate-100"><td className="py-3 font-medium">{symbol.symbol}</td><td>{symbol.baseAsset}</td><td>{symbol.quoteAsset}</td><td>{symbol.isEnabled ? 'Enabled' : 'Disabled'}</td><td className="space-x-3 text-right"><button onClick={() => void toggle(symbol)} className="text-slate-600 hover:text-slate-950">{symbol.isEnabled ? 'Disable' : 'Enable'}</button><button onClick={() => void remove(symbol.id)} className="text-red-700 hover:text-red-900">Remove</button></td></tr>)}</tbody></table>}</div></section></div>
}
