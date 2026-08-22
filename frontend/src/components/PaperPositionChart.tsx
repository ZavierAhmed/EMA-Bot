import { useEffect, useRef } from 'react'
import { CandlestickSeries, LineSeries, createChart, type IChartApi, type ISeriesApi, type LineWidth, type UTCTimestamp } from 'lightweight-charts'
import type { PaperRuntimeCandle, PaperTrade, TradeChartData } from '../api'

const utc = (value: string) => Math.floor(new Date(value).getTime() / 1000) as UTCTimestamp
type Series = { candles: ISeriesApi<'Candlestick'>; ema9: ISeriesApi<'Line'>; ema15: ISeriesApi<'Line'>; ema100: ISeriesApi<'Line'>; entry: ISeriesApi<'Line'>; stop: ISeriesApi<'Line'>; target: ISeriesApi<'Line'>; bid: ISeriesApi<'Line'>; ask: ISeriesApi<'Line'>; executableExit: ISeriesApi<'Line'> }

export function PaperPositionChart({ data, trade, formingCandle, bid, ask }: { data: TradeChartData; trade: PaperTrade; formingCandle: PaperRuntimeCandle | null; bid: number | null; ask: number | null }) {
  const host = useRef<HTMLDivElement>(null); const chart = useRef<IChartApi | null>(null); const series = useRef<Series | null>(null); const initial = useRef(true)
  useEffect(() => {
    if (!host.current) return
    const instance = createChart(host.current, { autoSize: true, height: 460, layout: { background: { color: '#fff' }, textColor: '#334155' } }); const line = (color: string, title: string, width: LineWidth = 2) => instance.addSeries(LineSeries, { color, lineWidth: width, title })
    series.current = { candles: instance.addSeries(CandlestickSeries, { upColor: '#16a34a', downColor: '#dc2626', borderVisible: false, wickUpColor: '#16a34a', wickDownColor: '#dc2626' }), ema9: line('#111827', 'EMA 9'), ema15: line('#2563eb', 'EMA 15'), ema100: line('#dc2626', 'EMA 100'), entry: line('#64748b', 'Entry'), stop: line('#991b1b', 'SL'), target: line('#15803d', 'TP'), bid: line('#0ea5e9', 'Bid', 1), ask: line('#f59e0b', 'Ask', 1), executableExit: line('#7c3aed', 'Executable Exit', 3) }; chart.current = instance; initial.current = true
    return () => { series.current = null; chart.current = null; instance.remove() }
  }, [trade.id])
  useEffect(() => {
    const item = series.current; if (!item) return
    const candles = [...data.candles, ...(formingCandle ? [formingCandle] : [])].sort((a, b) => a.openTimeUtc.localeCompare(b.openTimeUtc)).filter((candle, index, all) => index === 0 || candle.openTimeUtc !== all[index - 1].openTimeUtc)
    item.candles.setData(candles.map(value => ({ time: utc(value.openTimeUtc), open: value.open, high: value.high, low: value.low, close: value.close })))
    const points = (items: typeof data.ema9) => items.filter(value => value.value !== null).map(value => ({ time: utc(value.timeUtc), value: value.value! }))
    item.ema9.setData(points(data.ema9)); item.ema15.setData(points(data.ema15)); item.ema100.setData(points(data.ema100))
    const first = candles.at(0); const last = candles.at(-1); if (!first || !last) return
    const level = (target: ISeriesApi<'Line'>, value: number | null) => target.setData(value === null ? [] : [{ time: utc(first.openTimeUtc), value }, { time: utc(last.openTimeUtc), value }])
    level(item.entry, trade.entryPrice); level(item.stop, trade.currentStopLoss); level(item.target, trade.currentTakeProfit); level(item.bid, bid); level(item.ask, ask); level(item.executableExit, trade.direction === 'Long' ? bid : ask)
    if (initial.current) { chart.current?.timeScale().fitContent(); initial.current = false }
  }, [data, trade, formingCandle, bid, ask])
  return <div><p className="mb-2 text-xs text-slate-500">Live Paper runtime candles and MT5 Bid/Ask. Trade management uses the executable quote side. Current spread: {bid !== null && ask !== null ? (ask - bid).toFixed(5) : '—'}.</p><div ref={host} className="w-full" aria-label="Live Paper position chart" /></div>
}
