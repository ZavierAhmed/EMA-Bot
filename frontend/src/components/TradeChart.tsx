import { useEffect, useRef, useState } from 'react'
import { CandlestickSeries, LineSeries, LineType, createChart, createSeriesMarkers, type IChartApi, type ISeriesApi, type ISeriesMarkersPluginApi, type SeriesMarker, type Time, type UTCTimestamp } from 'lightweight-charts'
import type { TradeChartCandle, TradeChartData } from '../api'
import type { TradeChartAnnotations, TradeChartVisibility } from './tradeChartAnnotations'
type LinePoint = { time: UTCTimestamp; value: number }

const utc = (value: string): UTCTimestamp => Math.floor(new Date(value).getTime() / 1000) as UTCTimestamp
const chartBarTime = (candles: TradeChartCandle[], eventTime: string | null): UTCTimestamp | null => {
  if (!eventTime) return null
  const event = new Date(eventTime).getTime()
  const candle = candles.find(item => new Date(item.openTimeUtc).getTime() <= event && event <= new Date(item.closeTimeUtc).getTime())
  return candle ? utc(candle.openTimeUtc) : null
}
const normalize = (points: LinePoint[]) => Array.from(new Map(points.map(point => [point.time, point])).values()).sort((left, right) => Number(left.time) - Number(right.time))

export function TradeChart({ data, annotations, visibility }: { data: TradeChartData; annotations: TradeChartAnnotations; visibility: TradeChartVisibility }) {
  const host = useRef<HTMLDivElement>(null)
  const series = useRef<{ ema9: ISeriesApi<'Line'>; ema15: ISeriesApi<'Line'>; ema100: ISeriesApi<'Line'>; stop: ISeriesApi<'Line'>; target: ISeriesApi<'Line'>; executionEntry: ISeriesApi<'Line'>; executionExit: ISeriesApi<'Line'> } | null>(null)
  const markers = useRef<ISeriesMarkersPluginApi<Time> | null>(null); const markerData = useRef<SeriesMarker<Time>[]>([])
  const [renderError, setRenderError] = useState(false); const [renderAttempt, setRenderAttempt] = useState(0)
  useEffect(() => {
    if (!host.current) return
    let chart: IChartApi | null = null
    try {
      chart = createChart(host.current, { autoSize: true, height: 500, layout: { background: { color: '#ffffff' }, textColor: '#334155' } })
      const candleSeries = chart.addSeries(CandlestickSeries, { upColor: '#16a34a', downColor: '#dc2626', borderVisible: false, wickUpColor: '#16a34a', wickDownColor: '#dc2626' })
      const ema9 = chart.addSeries(LineSeries, { color: '#111111', lineWidth: 2, title: 'EMA 9' }); const ema15 = chart.addSeries(LineSeries, { color: '#2563eb', lineWidth: 2, title: 'EMA 15' }); const ema100 = chart.addSeries(LineSeries, { color: '#dc2626', lineWidth: 2, title: 'EMA 100' })
      const stop = chart.addSeries(LineSeries, { color: '#991b1b', lineWidth: 1, lineStyle: 2, lineType: LineType.WithSteps, title: 'SL' }); const target = chart.addSeries(LineSeries, { color: '#15803d', lineWidth: 1, lineStyle: 2, lineType: LineType.WithSteps, title: 'TP' })
      const executionEntry = chart.addSeries(LineSeries, { color: '#0f172a', lineWidth: 3, title: annotations.executionKind === 'PaperBidAsk' ? (annotations.direction === 'Long' ? 'Entry Ask' : 'Entry Bid') : 'Broker Fill', visible: annotations.executionKind !== 'None' })
      const executionExit = chart.addSeries(LineSeries, { color: '#ea580c', lineWidth: 3, title: annotations.executionKind === 'PaperBidAsk' ? (annotations.direction === 'Long' ? 'Exit Bid' : 'Exit Ask') : 'Broker Exit', visible: annotations.executionKind !== 'None' })
      candleSeries.setData(data.candles.map(item => ({ time: utc(item.openTimeUtc), open: item.open, high: item.high, low: item.low, close: item.close })))
      const points = (items: typeof data.ema9) => normalize(items.filter(item => item.value !== null).map(item => ({ time: utc(item.timeUtc), value: item.value! })))
      ema9.setData(points(data.ema9)); ema15.setData(points(data.ema15)); ema100.setData(points(data.ema100))
      const exact = (when: string | null, value: number | null) => { if (annotations.executionKind === 'None' || value === null) return []; const index = data.candles.findIndex(item => chartBarTime(data.candles, when) === utc(item.openTimeUtc)); if (index < 0) return []; const next = data.candles[Math.min(index + 1, data.candles.length - 1)]; return next.openTimeUtc === data.candles[index].openTimeUtc ? [{ time: utc(data.candles[index].openTimeUtc), value }] : [{ time: utc(data.candles[index].openTimeUtc), value }, { time: utc(next.openTimeUtc), value }] }
      executionEntry.setData(exact(annotations.actualEntryTimeUtc, annotations.actualEntryPrice)); executionExit.setData(exact(annotations.actualExitTimeUtc, annotations.actualExitPrice))
      const start = chartBarTime(data.candles, annotations.actualEntryTimeUtc ?? annotations.expectedEntryOpenUtc); const terminal = annotations.actualExitTimeUtc ? chartBarTime(data.candles, annotations.actualExitTimeUtc) : null
      const level = (initial: number | null, current: number | null, key: 'stopLoss' | 'takeProfit') => start === null || initial === null ? [] : [{ time: start, value: initial }, ...annotations.levelChanges.flatMap(change => { const time = chartBarTime(data.candles, change.timeUtc); const value = change[key]; return time === null || value === null ? [] : [{ time, value }] }), ...(terminal && current !== null ? [{ time: terminal, value: current }] : [])]
      stop.setData(normalize(level(annotations.initialStopLoss, annotations.currentStopLoss, 'stopLoss'))); target.setData(normalize(level(annotations.initialTakeProfit, annotations.currentTakeProfit, 'takeProfit')))
      const long = annotations.direction === 'Long'; const marker = (when: string | null, position: 'aboveBar' | 'belowBar', shape: 'circle' | 'arrowUp' | 'arrowDown', text: string, color: string) => { const time = chartBarTime(data.candles, when); return time === null ? [] : [{ time, position, shape, text, color }] }
      markerData.current = [...marker(annotations.crossoverTimeUtc, long ? 'belowBar' : 'aboveBar', 'circle', 'Cross', '#64748b'), ...marker(annotations.signalTimeUtc, long ? 'belowBar' : 'aboveBar', long ? 'arrowUp' : 'arrowDown', 'Signal', '#7c3aed'), ...marker(annotations.expectedEntryOpenUtc, long ? 'belowBar' : 'aboveBar', 'circle', 'Window', '#0f172a'), ...marker(annotations.actualEntryTimeUtc, long ? 'belowBar' : 'aboveBar', long ? 'arrowUp' : 'arrowDown', annotations.executionKind === 'BrokerFillExit' ? 'Fill' : annotations.direction, '#0f172a'), ...marker(annotations.actualExitTimeUtc, long ? 'aboveBar' : 'belowBar', long ? 'arrowDown' : 'arrowUp', annotations.exitLabel, '#dc2626')].sort((left, right) => Number(left.time) - Number(right.time))
      markers.current = createSeriesMarkers(candleSeries, markerData.current); series.current = { ema9, ema15, ema100, stop, target, executionEntry, executionExit }; chart.timeScale().fitContent(); setRenderError(false)
    } catch { markers.current = null; series.current = null; chart?.remove(); setRenderError(true); return }
    return () => { markers.current = null; series.current = null; chart?.remove() }
  }, [data, annotations, renderAttempt])
  useEffect(() => { const current = series.current; if (!current) return; current.ema9.applyOptions({ visible: visibility.ema9 }); current.ema15.applyOptions({ visible: visibility.ema15 }); current.ema100.applyOptions({ visible: visibility.ema100 }); current.stop.applyOptions({ visible: visibility.levels }); current.target.applyOptions({ visible: visibility.levels }); current.executionEntry.applyOptions({ visible: annotations.executionKind !== 'None' && visibility.execution }); current.executionExit.applyOptions({ visible: annotations.executionKind !== 'None' && visibility.execution }); markers.current?.setMarkers(visibility.markers ? markerData.current : []) }, [visibility, annotations.executionKind])
  return <div>{renderError && <div className="rounded border border-red-200 bg-red-50 p-4 text-sm text-red-700">The chart could not be rendered. <button className="underline" onClick={() => { setRenderError(false); setRenderAttempt(value => value + 1) }}>Retry chart</button></div>}<div ref={host} className="w-full" aria-label="Trade candlestick chart" /></div>
}
