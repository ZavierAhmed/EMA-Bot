import { useEffect, useRef, useState } from 'react'
import { CandlestickSeries, LineSeries, LineType, createChart, createSeriesMarkers, type IChartApi, type ISeriesApi, type ISeriesMarkersPluginApi, type SeriesMarker, type Time, type UTCTimestamp } from 'lightweight-charts'
import type { TradeChartCandle, TradeChartData, TradeDetail } from '../api'

type Visibility = { ema9: boolean; ema15: boolean; ema100: boolean; levels: boolean; markers: boolean }
type LinePoint = { time: UTCTimestamp; value: number }

const utc = (value: string): UTCTimestamp => Math.floor(new Date(value).getTime() / 1000) as UTCTimestamp

const chartBarTime = (candles: TradeChartCandle[], eventTime: string | null): UTCTimestamp | null => {
  if (!eventTime) return null
  const event = new Date(eventTime).getTime()
  const candle = candles.find(item => new Date(item.openTimeUtc).getTime() <= event && event <= new Date(item.closeTimeUtc).getTime())
  return candle ? utc(candle.openTimeUtc) : null
}

const normalizeLinePoints = (points: LinePoint[]) => Array.from(new Map(points.map(point => [point.time, point])).values())
  .sort((left, right) => Number(left.time) - Number(right.time))

export function TradeChart({ data, detail, visibility }: { data: TradeChartData; detail: TradeDetail; visibility: Visibility }) {
  const host = useRef<HTMLDivElement>(null)
  const series = useRef<{ ema9: ISeriesApi<'Line'>; ema15: ISeriesApi<'Line'>; ema100: ISeriesApi<'Line'>; stop: ISeriesApi<'Line'>; target: ISeriesApi<'Line'> } | null>(null)
  const markers = useRef<ISeriesMarkersPluginApi<Time> | null>(null)
  const markerData = useRef<SeriesMarker<Time>[]>([])
  const [renderError, setRenderError] = useState(false)
  const [renderAttempt, setRenderAttempt] = useState(0)

  useEffect(() => {
    if (!host.current) return

    let chart: IChartApi | null = null
    try {
      chart = createChart(host.current, { autoSize: true, height: 500, layout: { background: { color: '#ffffff' }, textColor: '#334155' } })
      const candleSeries = chart.addSeries(CandlestickSeries, { upColor: '#16a34a', downColor: '#dc2626', borderVisible: false, wickUpColor: '#16a34a', wickDownColor: '#dc2626' })
      const ema9 = chart.addSeries(LineSeries, { color: '#111111', lineWidth: 2, title: 'EMA 9' })
      const ema15 = chart.addSeries(LineSeries, { color: '#2563eb', lineWidth: 2, title: 'EMA 15' })
      const ema100 = chart.addSeries(LineSeries, { color: '#dc2626', lineWidth: 2, title: 'EMA 100' })
      const stop = chart.addSeries(LineSeries, { color: '#991b1b', lineWidth: 1, lineStyle: 2, lineType: LineType.WithSteps, title: 'SL' })
      const target = chart.addSeries(LineSeries, { color: '#15803d', lineWidth: 1, lineStyle: 2, lineType: LineType.WithSteps, title: 'TP' })

      candleSeries.setData(data.candles.map(item => ({ time: utc(item.openTimeUtc), open: item.open, high: item.high, low: item.low, close: item.close })))
      const points = (items: typeof data.ema9) => normalizeLinePoints(items.filter(item => item.value !== null).map(item => ({ time: utc(item.timeUtc), value: item.value! })))
      ema9.setData(points(data.ema9))
      ema15.setData(points(data.ema15))
      ema100.setData(points(data.ema100))

      const terminal = detail.summary.exitTimeUtc ? chartBarTime(data.candles, detail.summary.exitTimeUtc) : data.candles.at(-1) ? utc(data.candles.at(-1)!.openTimeUtc) : null
      const level = (initial: number, final: number, field: 'newStop' | 'newTakeProfit') => {
        const changes = detail.hasDetailedManagementHistory
          ? detail.events.filter(item => item.effectiveTimeUtc && item[field] !== null).flatMap(item => {
              const time = chartBarTime(data.candles, item.effectiveTimeUtc)
              return time === null ? [] : [{ time, value: item[field]! }]
            })
          : []
        const entry = chartBarTime(data.candles, detail.summary.entryTimeUtc)
        return entry === null ? [] : [{ time: entry, value: initial }, ...changes, ...(terminal ? [{ time: terminal, value: detail.hasDetailedManagementHistory ? final : initial }] : [])]
      }
      stop.setData(normalizeLinePoints(level(detail.initialStopLoss, detail.finalStopLoss, 'newStop')))
      target.setData(normalizeLinePoints(level(detail.originalTakeProfit, detail.finalTakeProfit, 'newTakeProfit')))

      const isLong = detail.summary.direction === 'Long'
      const marker = (when: string | null, position: 'aboveBar' | 'belowBar', shape: 'circle' | 'arrowUp' | 'arrowDown', text: string, color: string) => {
        const time = chartBarTime(data.candles, when)
        return time === null ? [] : [{ time, position, shape, text, color }]
      }
      markerData.current = [
        ...marker(detail.crossoverTimeUtc, isLong ? 'belowBar' : 'aboveBar', 'circle', 'Cross', '#64748b'),
        ...marker(detail.signalTimeUtc, isLong ? 'belowBar' : 'aboveBar', isLong ? 'arrowUp' : 'arrowDown', 'Signal', '#7c3aed'),
        ...marker(detail.summary.entryTimeUtc, isLong ? 'belowBar' : 'aboveBar', isLong ? 'arrowUp' : 'arrowDown', detail.summary.direction, '#0f172a'),
        ...detail.events.filter(item => item.type !== 'Entry' && item.type !== 'Exit' && item.effectiveTimeUtc).flatMap(item => marker(item.effectiveTimeUtc, 'aboveBar', 'circle', item.type === 'TakeProfitExtended' ? 'TP 110%' : 'SL', item.type === 'TakeProfitExtended' ? '#15803d' : '#991b1b')),
        ...marker(detail.summary.exitTimeUtc, isLong ? 'aboveBar' : 'belowBar', isLong ? 'arrowDown' : 'arrowUp', detail.summary.exitReason ?? 'Exit', '#dc2626')
      ].sort((left, right) => Number(left.time) - Number(right.time))
      markers.current = createSeriesMarkers(candleSeries, markerData.current)
      series.current = { ema9, ema15, ema100, stop, target }
      chart.timeScale().fitContent()
      setRenderError(false)
    } catch {
      markers.current = null
      series.current = null
      chart?.remove()
      setRenderError(true)
      return
    }

    return () => {
      markers.current = null
      series.current = null
      chart?.remove()
    }
  }, [data, detail, renderAttempt])

  useEffect(() => {
    const current = series.current
    if (!current) return
    current.ema9.applyOptions({ visible: visibility.ema9 })
    current.ema15.applyOptions({ visible: visibility.ema15 })
    current.ema100.applyOptions({ visible: visibility.ema100 })
    current.stop.applyOptions({ visible: visibility.levels })
    current.target.applyOptions({ visible: visibility.levels })
    markers.current?.setMarkers(visibility.markers ? markerData.current : [])
  }, [visibility])

  const retry = () => {
    setRenderError(false)
    setRenderAttempt(value => value + 1)
  }

  return <div>
    {renderError && <div className="rounded border border-red-200 bg-red-50 p-4 text-sm text-red-700">The chart could not be rendered. <button className="underline" onClick={retry}>Retry chart</button></div>}
    <div ref={host} className="w-full" aria-label="Trade candlestick chart" />
  </div>
}
