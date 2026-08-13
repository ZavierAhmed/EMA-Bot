import { useEffect, useRef } from 'react'
import { CandlestickSeries, LineSeries, createChart, type IChartApi, type ISeriesApi, type UTCTimestamp } from 'lightweight-charts'
import type { PaperTrade, TradeChartData } from '../api'

const utc = (value: string) => Math.floor(new Date(value).getTime() / 1000) as UTCTimestamp
type Series = { candles: ISeriesApi<'Candlestick'>; ema9: ISeriesApi<'Line'>; ema15: ISeriesApi<'Line'>; ema100: ISeriesApi<'Line'>; entry: ISeriesApi<'Line'>; stop: ISeriesApi<'Line'>; target: ISeriesApi<'Line'>; current: ISeriesApi<'Line'> }

export function PaperPositionChart({ data, trade, current }: { data: TradeChartData; trade: PaperTrade; current: number | null }) {
  const host = useRef<HTMLDivElement>(null); const chart = useRef<IChartApi | null>(null); const series = useRef<Series | null>(null); const initial = useRef(true)
  useEffect(() => {
    if (!host.current) return
    const instance = createChart(host.current, { autoSize: true, height: 460, layout: { background: { color: '#fff' }, textColor: '#334155' } }); const line = (color: string, title: string) => instance.addSeries(LineSeries, { color, lineWidth: 2, title })
    series.current = { candles: instance.addSeries(CandlestickSeries, { upColor: '#16a34a', downColor: '#dc2626', borderVisible: false, wickUpColor: '#16a34a', wickDownColor: '#dc2626' }), ema9: line('#111827', 'EMA 9'), ema15: line('#2563eb', 'EMA 15'), ema100: line('#dc2626', 'EMA 100'), entry: line('#64748b', 'Entry'), stop: line('#991b1b', 'SL'), target: line('#15803d', 'TP'), current: line('#7c3aed', 'Current') }; chart.current = instance; initial.current = true
    return () => { series.current = null; chart.current = null; instance.remove() }
  }, [trade.id])
  useEffect(() => {
    const currentSeries = series.current; if (!currentSeries) return
    currentSeries.candles.setData(data.candles.map(item => ({ time: utc(item.openTimeUtc), open: item.open, high: item.high, low: item.low, close: item.close })))
    const points = (items: typeof data.ema9) => items.filter(item => item.value !== null).map(item => ({ time: utc(item.timeUtc), value: item.value! }))
    currentSeries.ema9.setData(points(data.ema9)); currentSeries.ema15.setData(points(data.ema15)); currentSeries.ema100.setData(points(data.ema100))
    if (initial.current) { chart.current?.timeScale().fitContent(); initial.current = false }
  }, [data])
  useEffect(() => { const item = series.current; const first = data.candles.at(0); const last = data.candles.at(-1); if (!item || !first || !last) return; const level = (target: ISeriesApi<'Line'>, value: number) => target.setData([{ time: utc(first.openTimeUtc), value }, { time: utc(last.openTimeUtc), value }]); level(item.entry, trade.entryPrice); level(item.stop, trade.currentStopLoss); level(item.target, trade.currentTakeProfit); level(item.current, current ?? trade.entryPrice) }, [data.candles, trade.entryPrice, trade.currentStopLoss, trade.currentTakeProfit, current])
  return <div><p className="mb-2 text-xs text-slate-500">MT5 / Exness historical closed candles with live executable quote line. Levels: Entry, SL, TP, Current.</p><div ref={host} className="w-full" aria-label="Live Paper position chart" /></div>
}
