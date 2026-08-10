import { useEffect, useRef } from 'react'
import { CandlestickSeries, LineSeries, LineType, createChart, createSeriesMarkers, type ISeriesApi, type ISeriesMarkersPluginApi, type SeriesMarker, type Time, type UTCTimestamp } from 'lightweight-charts'
import type { TradeChartData, TradeDetail } from '../api'

type Visibility = { ema9: boolean; ema15: boolean; ema100: boolean; levels: boolean; markers: boolean }
const utc = (value: string): UTCTimestamp => Math.floor(new Date(value).getTime() / 1000) as UTCTimestamp

export function TradeChart({ data, detail, visibility }: { data: TradeChartData; detail: TradeDetail; visibility: Visibility }) {
  const host = useRef<HTMLDivElement>(null); const series = useRef<{ ema9: ISeriesApi<'Line'>; ema15: ISeriesApi<'Line'>; ema100: ISeriesApi<'Line'>; stop: ISeriesApi<'Line'>; target: ISeriesApi<'Line'> } | null>(null); const markers = useRef<ISeriesMarkersPluginApi<Time> | null>(null); const markerData = useRef<SeriesMarker<Time>[]>([])
  useEffect(() => {
    if (!host.current) return
    const chart = createChart(host.current, { autoSize: true, height: 420, layout: { background: { color: '#ffffff' }, textColor: '#334155' }, grid: { vertLines: { color: '#f1f5f9' }, horzLines: { color: '#f1f5f9' } } })
    const candles = chart.addSeries(CandlestickSeries, { upColor: '#16a34a', downColor: '#dc2626', borderVisible: false, wickUpColor: '#16a34a', wickDownColor: '#dc2626' })
    const ema9 = chart.addSeries(LineSeries, { color: '#111111', lineWidth: 2, title: 'EMA 9' }); const ema15 = chart.addSeries(LineSeries, { color: '#2563eb', lineWidth: 2, title: 'EMA 15' }); const ema100 = chart.addSeries(LineSeries, { color: '#dc2626', lineWidth: 2, title: 'EMA 100' }); const stop = chart.addSeries(LineSeries, { color: '#991b1b', lineWidth: 1, lineStyle: 2, lineType: LineType.WithSteps, title: 'SL' }); const target = chart.addSeries(LineSeries, { color: '#15803d', lineWidth: 1, lineStyle: 2, lineType: LineType.WithSteps, title: 'TP' })
    candles.setData(data.candles.map(item => ({ time: utc(item.openTimeUtc), open: item.open, high: item.high, low: item.low, close: item.close })))
    const points = (items: typeof data.ema9) => items.filter(item => item.value !== null).map(item => ({ time: utc(item.timeUtc), value: item.value! }))
    ema9.setData(points(data.ema9)); ema15.setData(points(data.ema15)); ema100.setData(points(data.ema100))
    const events = detail.events; const levelPoints = (initial: number, field: 'newStop' | 'newTakeProfit') => [{ time: utc(detail.summary.entryTimeUtc), value: initial }, ...events.filter(item => item.effectiveTimeUtc && item[field] !== null).map(item => ({ time: utc(item.effectiveTimeUtc!), value: item[field]! }))]
    stop.setData(levelPoints(detail.initialStopLoss, 'newStop')); target.setData(levelPoints(detail.originalTakeProfit, 'newTakeProfit'))
    markerData.current = [{ time: utc(detail.crossoverTimeUtc), position: detail.summary.direction === 'Long' ? 'belowBar' : 'aboveBar', color: '#64748b', shape: 'circle', text: 'Cross' }, { time: utc(detail.signalTimeUtc), position: detail.summary.direction === 'Long' ? 'belowBar' : 'aboveBar', color: '#7c3aed', shape: 'arrowUp', text: 'Signal' }, { time: utc(detail.summary.entryTimeUtc), position: detail.summary.direction === 'Long' ? 'belowBar' : 'aboveBar', color: '#0f172a', shape: 'arrowUp', text: detail.summary.direction }, ...events.filter(item => item.type !== 'Entry' && item.type !== 'Exit').map(item => ({ time: utc(item.timeUtc), position: 'aboveBar' as const, color: item.type === 'TakeProfitExtended' ? '#15803d' : '#991b1b', shape: 'circle' as const, text: item.type === 'TakeProfitExtended' ? 'TP 110%' : 'SL' })), ...(detail.summary.exitTimeUtc ? [{ time: utc(detail.summary.exitTimeUtc), position: 'aboveBar' as const, color: '#dc2626', shape: 'arrowDown' as const, text: detail.summary.exitReason ?? 'Exit' }] : [])]
    markers.current = createSeriesMarkers(candles, markerData.current)
    series.current = { ema9, ema15, ema100, stop, target }; chart.timeScale().fitContent()
    return () => { markers.current = null; series.current = null; chart.remove() }
  }, [data, detail])
  useEffect(() => { const current = series.current; if (!current) return; current.ema9.applyOptions({ visible: visibility.ema9 }); current.ema15.applyOptions({ visible: visibility.ema15 }); current.ema100.applyOptions({ visible: visibility.ema100 }); current.stop.applyOptions({ visible: visibility.levels }); current.target.applyOptions({ visible: visibility.levels }); markers.current?.setMarkers(visibility.markers ? markerData.current : []) }, [visibility])
  return <div ref={host} className="w-full" aria-label="Trade candlestick chart" />
}
