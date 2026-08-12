import { useEffect, useState } from 'react'
import { getHealth, type HealthStatus } from '../api'

export function DashboardPage() {
  const [health, setHealth] = useState<HealthStatus | null>(null)

  useEffect(() => {
    void getHealth().then(setHealth).catch(() => setHealth({ api: 'unavailable', database: 'unavailable' }))
  }, [])

  return (
    <div>
      <p className="text-sm font-medium text-slate-500">Dashboard</p>
      <h1 className="mt-2 text-3xl font-semibold tracking-tight">EMA-Bot</h1>
      <p className="mt-3 max-w-xl text-slate-600">EMA crossover research and trading system with backtesting, paper simulation, and trade analysis.</p>
      <dl className="mt-10 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        <StatusCard label="Historical research data" value="Legacy Binance" />
        <StatusCard label="Live market-data provider" value="Not configured" />
        <StatusCard label="Execution broker" value="Not configured" />
        <StatusCard label="Target" value="MT5 / Exness" />
        <StatusCard label="API status" value={health?.api ?? 'Checking…'} />
        <StatusCard label="Database status" value={health?.database ?? 'Checking…'} />
      </dl>
    </div>
  )
}

function StatusCard({ label, value }: { label: string; value: string }) {
  return <div className="rounded-lg border border-slate-200 bg-white p-5"><dt className="text-sm text-slate-500">{label}</dt><dd className="mt-3 font-medium capitalize text-slate-950">{value}</dd></div>
}
