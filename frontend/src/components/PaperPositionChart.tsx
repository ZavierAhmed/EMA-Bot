import { useEffect, useRef } from 'react'
import { CandlestickSeries, LineSeries, createChart, type IChartApi, type ISeriesApi, type UTCTimestamp } from 'lightweight-charts'
import type { PaperTrade, TradeChartData } from '../api'

const utc = (value: string) => Math.floor(new Date(value).getTime() / 1000) as UTCTimestamp

export function PaperPositionChart({ data, trade, current }: { data: TradeChartData; trade: PaperTrade; current: number | null }) {
  const host = useRef<HTMLDivElement>(null); const chart = useRef<IChartApi | null>(null); const levels = useRef<ISeriesApi<'Line'>[] | null>(null); const initial = useRef({ entry: trade.entryPrice, stop: trade.currentStopLoss, target: trade.currentTakeProfit, current })
  initial.current = { entry: trade.entryPrice, stop: trade.currentStopLoss, target: trade.currentTakeProfit, current }
  useEffect(() => {
    if (!host.current) return; const snapshot = initial.current
    const instance = createChart(host.current, { autoSize: true, height: 460, layout: { background: { color: '#fff' }, textColor: '#334155' } })
    const candles = instance.addSeries(CandlestickSeries, { upColor: '#16a34a', downColor: '#dc2626', borderVisible: false, wickUpColor: '#16a34a', wickDownColor: '#dc2626' })
    const line = (color: string, title: string) => instance.addSeries(LineSeries, { color, lineWidth: 2, title })
    const ema9 = line('#111827', 'EMA 9'), ema15 = line('#2563eb', 'EMA 15'), ema100 = line('#dc2626', 'EMA 100')
    candles.setData(data.candles.map(item => ({ time: utc(item.openTimeUtc), open: item.open, high: item.high, low: item.low, close: item.close })))
    const points = (items: typeof data.ema9) => items.filter(item => item.value !== null).map(item => ({ time: utc(item.timeUtc), value: item.value! }))
    ema9.setData(points(data.ema9)); ema15.setData(points(data.ema15)); ema100.setData(points(data.ema100))
    const level = (color: string, title: string, value: number) => { const result = line(color, title); const first = data.candles.at(0); const last = data.candles.at(-1); if (first && last) result.setData([{ time: utc(first.openTimeUtc), value }, { time: utc(last.openTimeUtc), value }]); return result }
    levels.current = [level('#64748b', 'Entry', snapshot.entry), level('#991b1b', 'SL', snapshot.stop), level('#15803d', 'TP', snapshot.target), level('#7c3aed', 'Current', snapshot.current ?? snapshot.entry)]
    chart.current = instance; instance.timeScale().fitContent()
    return () => { levels.current = null; chart.current = null; instance.remove() }
  }, [data, trade.id])
  useEffect(() => { const first = data.candles.at(0); const last = data.candles.at(-1); const currentLine = levels.current?.[3]; if (currentLine && first && last && current !== null) currentLine.setData([{ time: utc(first.openTimeUtc), value: current }, { time: utc(last.openTimeUtc), value: current }]) }, [current, data.candles])
  useEffect(() => { const first = data.candles.at(0); const last = data.candles.at(-1); if (!first || !last || !levels.current) return; levels.current[1].setData([{ time: utc(first.openTimeUtc), value: trade.currentStopLoss }, { time: utc(last.openTimeUtc), value: trade.currentStopLoss }]); levels.current[2].setData([{ time: utc(first.openTimeUtc), value: trade.currentTakeProfit }, { time: utc(last.openTimeUtc), value: trade.currentTakeProfit }]) }, [trade.currentStopLoss, trade.currentTakeProfit, data.candles])
  return <div><p className="mb-2 text-xs text-slate-500">MT5 / Exness historical closed candles with live executable quote line.</p><div ref={host} className="w-full" aria-label="Live Paper position chart" /></div>
}
