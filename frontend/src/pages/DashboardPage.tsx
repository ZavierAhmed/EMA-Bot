import { useEffect, useState } from 'react'
import { getHealth, getMarketProviderCapabilities, getMt5BridgeStatus, type HealthStatus, type MarketProviderCapabilities, type Mt5BridgeStatus } from '../api'

export function DashboardPage() {
  const [health, setHealth] = useState<HealthStatus | null>(null)
  const [capabilities, setCapabilities] = useState<MarketProviderCapabilities | null>(null)
  const [bridge, setBridge] = useState<Mt5BridgeStatus | null>(null)

  useEffect(() => {
    void getHealth().then(setHealth).catch(() => setHealth({ api: 'unavailable', database: 'unavailable' }))
    void getMarketProviderCapabilities().then(setCapabilities).catch(() => setCapabilities(null))
    void getMt5BridgeStatus().then(setBridge).catch(() => setBridge(null))
  }, [])

  return (
    <div>
      <p className="text-sm font-medium text-slate-500">Dashboard</p>
      <h1 className="mt-2 text-3xl font-semibold tracking-tight">EMA-Bot</h1>
      <p className="mt-3 max-w-xl text-slate-600">EMA crossover research and trading system with backtesting, paper simulation, and trade analysis.</p>
      <dl className="mt-10 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        <StatusCard label="Historical research" value={capabilities?.historicalProvider ?? 'Checking...'} />
        <StatusCard label="Target terminal" value={capabilities?.targetTerminal ?? 'Checking...'} />
        <StatusCard label="Target broker" value={capabilities?.targetBroker ?? 'Checking...'} />
        <StatusCard label="Instrument catalog" value={capabilities?.instrumentCatalogConfigured ? 'Configured' : capabilities ? 'Not configured' : 'Checking...'} />
        <StatusCard label="Quotes" value={capabilities?.quoteProviderConfigured ? 'Configured' : capabilities ? 'Not configured' : 'Checking...'} />
        <StatusCard label="Live market data" value={bridge?.connectionState === 'Connected' ? 'Connected' : capabilities?.liveBarProviderConfigured ? 'Configured' : 'Not configured'} />
        <StatusCard label="Paper economics" value="MT5 broker-aware" />
        <StatusCard label="Execution" value={capabilities?.executionProviderConfigured ? 'Configured' : capabilities ? 'Not configured' : 'Checking...'} />
        <StatusCard label="MT5 Bridge configured" value={bridge?.enabled ? 'Configured' : bridge ? 'Not configured' : 'Checking...'} />
        <StatusCard label="MT5 Bridge connection" value={bridge?.connectionState ?? 'Checking...'} />
        <StatusCard label="MT5 terminal" value={bridge?.terminalName ?? 'Not connected'} />
        <StatusCard label="Last heartbeat" value={bridge?.lastHeartbeatAtUtc ? new Date(bridge.lastHeartbeatAtUtc).toLocaleString() : 'Not available'} />
        <StatusCard label="Transport RTT" value={bridge?.lastRoundTripMs === null || bridge?.lastRoundTripMs === undefined ? 'Not measured' : `${bridge.lastRoundTripMs} ms`} />
        <StatusCard label="API status" value={health?.api ?? 'Checking...'} />
        <StatusCard label="Database status" value={health?.database ?? 'Checking...'} />
      </dl>
    </div>
  )
}

function StatusCard({ label, value }: { label: string; value: string }) {
  return <div className="rounded-lg border border-slate-200 bg-white p-5"><dt className="text-sm text-slate-500">{label}</dt><dd className="mt-3 font-medium capitalize text-slate-950">{value}</dd></div>
}
