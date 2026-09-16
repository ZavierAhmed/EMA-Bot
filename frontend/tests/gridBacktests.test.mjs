import { test } from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import path from 'node:path'
import ts from 'typescript'
import React from 'react'
import { renderToStaticMarkup } from 'react-dom/server'

const require = createRequire(import.meta.url)
// Transpile actual application modules with the existing TypeScript dependency.
// React server rendering performs no effects/network calls or manual backtest.
function loader(react = React) {
  const cache = new Map()
  function load(file) {
    file = path.resolve(file)
    if (cache.has(file)) return cache.get(file)
    const code = ts.transpileModule(readFileSync(file, 'utf8'), { compilerOptions: { module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX, target: ts.ScriptTarget.ES2022 } }).outputText
    const module = { exports: {} }; cache.set(file, module.exports)
    function resolve(name) {
      if (name === 'react') return react
      if (!name.startsWith('.')) return require(name)
      const base = path.resolve(path.dirname(file), name)
      try { return load(`${base}.ts`) } catch (e) { if (e.code !== 'ENOENT') throw e; return load(`${base}.tsx`) }
    }
    new Function('require', 'module', 'exports', code)(resolve, module, module.exports)
    return module.exports
  }
  return load
}
const load = loader()
const form = load('src/backtestForm.ts')
const api = load('src/api.ts')

test('default form selects EMA and hides Grid balance', () => {
  const { BacktestsPage } = loader()('src/pages/BacktestsPage.tsx')
  const html = renderToStaticMarkup(React.createElement(BacktestsPage))
  assert.equal(form.defaultBacktestStrategy, 'EMA_TREND_V1')
  assert.match(html, /value="EMA_TREND_V1" selected/)
  assert.doesNotMatch(html, /type="number"/)
})
test('Grid selection exposes required explicit balance and frozen explanation', () => {
  let state = 0
  const react = { ...React, useState(initial) { return React.useState(state++ === 0 ? 'GRID_RANGE_V1' : initial) } }
  const { BacktestsPage } = loader(react)('src/pages/BacktestsPage.tsx')
  const html = renderToStaticMarkup(React.createElement(BacktestsPage))
  assert.match(html, /value="GRID_RANGE_V1" selected/)
  assert.match(html, /type="number" required/)
  assert.match(html, /non-martingale, 1% total basket risk/)
})
test('date-only input is sent as SOD/EOD UTC', () => {
  assert.deepEqual(form.backtestDates('TESTm', '3m', '2026-07-01', '2026-07-31'), { symbol: 'TESTm', interval: '3m', startUtc: '2026-07-01T00:00:00.000Z', endUtc: '2026-07-31T23:59:59.999Z' })
  assert.throws(() => form.backtestDates('TESTm', '3m', '2026-08-01', '2026-07-01'))
})
test('Grid balance rejects missing, zero, negative and nonfinite values', () => {
  for (const value of ['', '0', '-1', 'NaN', 'Infinity']) assert.throws(() => form.gridStartingBalance(value))
  assert.equal(form.gridStartingBalance('1000.25'), 1000.25)
})
test('Grid POST has discriminant and no client economics; EMA remains compatible', async () => {
  const calls = []; const saved = globalThis.fetch
  globalThis.fetch = async (url, init) => { calls.push([url, init]); return new Response(JSON.stringify(url.includes('antiforgery') ? { token: 'test' } : {})) }
  try {
    const dates = form.backtestDates('TESTm', '3m', '2026-07-01', '2026-07-31')
    await api.runGridBacktest({ ...dates, strategyId: 'GRID_RANGE_V1', startingBalance: 1000 })
    const grid = JSON.parse(calls[1][1].body)
    assert.equal(grid.strategyId, 'GRID_RANGE_V1'); assert.equal(grid.startingBalance, 1000)
    assert.equal(grid.accountCurrency, undefined); assert.equal(grid.commissionPerLotPerSide, undefined)
    assert.equal(calls[1][0], '/api/backtests')
    await api.runBacktest(dates)
    assert.deepEqual(JSON.parse(calls[3][1].body), dates)
  } finally { globalThis.fetch = saved }
})
test('Grid Excel and history use dedicated routes', async () => {
  const calls = []; const savedFetch = globalThis.fetch; const savedDocument = globalThis.document
  globalThis.fetch = async url => { calls.push(url); return new Response('[]') }
  globalThis.document = { createElement: () => ({ click() {} }) }
  try {
    await api.downloadGridBacktestExcel(7); await api.getGridBacktests(); await api.getGridBacktest(7)
    assert.deepEqual(calls, ['/api/backtests/grid/7/export/excel', '/api/backtests/grid', '/api/backtests/grid/7'])
  } finally { globalThis.fetch = savedFetch; globalThis.document = savedDocument }
})
test('Grid result shows account currency and Grid metrics without EMA diagnostics', () => {
  const { GridBacktestResult } = loader()('src/pages/GridBacktestResult.tsx')
  const run = new Proxy({ symbol: 'TESTm', interval: '3m', requestedStartUtc: '2026-07-01', requestedEndUtc: '2026-07-31', accountCurrency: 'USD', tradeMode: 'LongOnly' }, { get: (o, key) => key in o ? o[key] : 0 })
  const html = renderToStaticMarkup(React.createElement(GridBacktestResult, { detail: { run, baskets: [], cycles: [] }, busy: false, exportExcel() {} }))
  assert.match(html, /GRID_RANGE_V1/); assert.match(html, /USD/); assert.match(html, /Qualified cycles/)
  assert.doesNotMatch(html, /EMA100|Crossovers|Confirmation failed|USDT/)
})
test('recent Grid rows identify strategy before opening or exporting', () => {
  let state = 0
  const row = { id: 7, strategyId: 'GRID_RANGE_V1', symbol: 'TESTm', interval: '3m', requestedStartUtc: '2026-07-01', requestedEndUtc: '2026-07-31', basketCount: 1, netPnl: 1, accountCurrency: 'USD' }
  const react = { ...React, useState(initial) { return React.useState(state++ === 2 ? [row] : initial) } }
  const { BacktestsPage } = loader(react)('src/pages/BacktestsPage.tsx')
  const html = renderToStaticMarkup(React.createElement(BacktestsPage))
  assert.match(html, /<td class="py-3">GRID_RANGE_V1<\/td>/); assert.match(html, />Open<\/button>/)
})
