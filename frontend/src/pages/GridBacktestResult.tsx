import type { GridBacktestDetail } from '../gridBacktestTypes'

export function GridBacktestResult({ detail, busy, exportExcel }: { detail: GridBacktestDetail; busy: boolean; exportExcel: () => void }) {
  const r = detail.run
  const money = (value: number) => `${value.toFixed(2)} ${r.accountCurrency}`
  const metrics: [string, string | number][] = [
    ['Starting balance', money(r.startingBalance)], ['Ending balance', money(r.endingBalance)],
    ['Baskets', r.basketCount], ['Wins / losses / breakeven', `${r.winningBaskets} / ${r.losingBaskets} / ${r.breakEvenBaskets}`],
    ['Long / short', `${r.longBaskets} / ${r.shortBaskets}`], ['Gross P/L', money(r.grossPnl)],
    ['Commission', money(r.totalCommission)], ['Net P/L', money(r.netPnl)],
    ['Gross PF', r.grossProfitFactor?.toFixed(3) ?? '—'], ['Net PF', r.netProfitFactor?.toFixed(3) ?? '—'],
    ['Realized max drawdown', money(r.maxDrawdown)], ['Qualified cycles', r.qualifiedCycles],
    ['Ambiguous first side', r.ambiguousFirstSideCount], ['Risk below minimum', r.riskBelowMinimumVolumeCount],
    ['Insufficient margin', r.insufficientMarginCount], ['Reporting / warmup candles', `${r.reportingCandleCount} / ${r.warmupCandleCount}`],
  ]
  return <section className="space-y-5 rounded-lg border border-slate-200 bg-white p-5">
    <div className="flex flex-wrap justify-between gap-3"><div><h2 className="font-semibold">{r.strategyId} · {r.symbol} · {r.interval}</h2><p className="text-sm text-slate-600">MT5 / Exness · {r.accountCurrency} · {r.requestedStartUtc.slice(0, 10)} – {r.requestedEndUtc.slice(0, 10)}</p></div><button disabled={busy} onClick={exportExcel} className="rounded border px-3 py-2 text-sm disabled:opacity-50">{busy ? 'Exporting…' : 'Export Grid Excel'}</button></div>
    <div className="rounded border border-slate-200 bg-slate-50 p-4 text-sm"><h3 className="font-semibold">Frozen Grid strategy</h3>{r.strategyId === 'GRID_RANGE_4L_RESEARCH_V1' && <p className="mt-2 font-semibold text-amber-800">RESEARCH ONLY · Not approved for Paper/Demo/Live.</p>}<p className="mt-2">{r.levelCount} equal-lot levels · {r.gridBasketRiskPercent}% total basket price-risk · {r.rangeLookback}-bar range · ATR{r.atrPeriod} × {r.atrSpacingMultiplier} · ADX{r.adxPeriod} ≤ {r.adxThreshold} · anchor TP · emergency stop one spacing beyond Level {r.levelCount} (stop level {r.levelCount + 1}) · {r.cooldownBars}-bar cooldown · no martingale</p><p className="mt-2 text-slate-600">Commission: {r.commissionPerLotPerSide} {r.accountCurrency}/lot/side · {r.tradeMode} · Bid chart with captured spread · drawdown uses realized basket balances.</p></div>
    <div className="grid grid-cols-2 gap-4 text-sm md:grid-cols-4">{metrics.map(([label, value]) => <div key={label}><p className="text-slate-500">{label}</p><p className="mt-1 font-medium">{value}</p></div>)}</div>
    <h3 className="font-semibold">Baskets and filled legs</h3>
    {detail.baskets.length === 0 && <p className="text-sm text-slate-500">No completed baskets. Accepted cycles and diagnostics are available in the workbook.</p>}
    <div className="overflow-x-auto"><table className="w-full text-left text-xs"><thead><tr>{['# / direction', 'Qualified', 'Filled', 'Lots', 'Anchor', 'Reason / exit', 'Gross', 'Commission', 'Net', 'Ending balance'].map(h => <th className="p-2" key={h}>{h}</th>)}</tr></thead><tbody>{detail.baskets.map(({ basket: b, legs }, index) => {
      const cycle = detail.cycles.find(c => c.cycle.id === b.gridBacktestCycleId)?.cycle
      return <tr className="border-t align-top" key={b.id}><td className="p-2"><details><summary className="cursor-pointer">{index + 1} · {b.direction}</summary><div className="mt-3 min-w-96 space-y-2">{legs.map(l => <div key={l.id} className="rounded bg-slate-50 p-2"><strong>Level {l.levelNumber}</strong> · {l.fillTimeUtc}<br />Fill {l.fillPrice} · {l.lots} lots · margin {money(l.requiredMargin)}<br />Gross {money(l.grossPnl)} · commission {money(l.totalCommission)} · net {money(l.netPnl)}</div>)}</div></details></td><td className="p-2">{cycle?.qualificationTimeUtc}</td><td className="p-2">{legs.length}</td><td className="p-2">{cycle?.commonLots}</td><td className="p-2">{cycle?.anchor}</td><td className="p-2">{b.exitReason}<br />{b.exitPrice}</td><td className="p-2">{b.grossPnl.toFixed(2)}</td><td className="p-2">{b.commission.toFixed(2)}</td><td className="p-2">{b.netPnl.toFixed(2)}</td><td className="p-2">{b.endingBalance.toFixed(2)}</td></tr>
    })}</tbody></table></div>
    <p className="text-xs text-slate-500">All amounts in {r.accountCurrency}. Open a basket’s direction to inspect its legs. Blank economics means not calculated, not zero.</p>
  </section>
}
