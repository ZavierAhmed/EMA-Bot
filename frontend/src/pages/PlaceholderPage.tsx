export function PlaceholderPage({ title }: { title: string }) {
  return (
    <div>
      <p className="text-sm font-medium text-slate-500">EMA Bot</p>
      <h1 className="mt-2 text-3xl font-semibold tracking-tight">{title}</h1>
      <p className="mt-3 text-slate-600">This module is coming in the next milestone.</p>
    </div>
  )
}
