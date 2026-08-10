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
      <h1 className="mt-2 text-3xl font-semibold tracking-tight">EMA Bot</h1>
      <p className="mt-3 max-w-xl text-slate-600">EMA 9/15 strategy backtesting, live paper simulation, and trade analysis using Binance USD-M Futures market data.</p>
      <dl className="mt-10 grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <StatusCard label="Exchange" value="Binance Futures" />
        <StatusCard label="Mode" value="Simulation" />
        <StatusCard label="API status" value={health?.api ?? 'Checking…'} />
        <StatusCard label="Database status" value={health?.database ?? 'Checking…'} />
      </dl>
    </div>
  )
}

function StatusCard({ label, value }: { label: string; value: string }) {
  return <div className="rounded-lg border border-slate-200 bg-white p-5"><dt className="text-sm text-slate-500">{label}</dt><dd className="mt-3 font-medium capitalize text-slate-950">{value}</dd></div>
}
