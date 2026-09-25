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
  const run = new Proxy({ strategyId: 'GRID_RANGE_V1', levelCount: 5, symbol: 'TESTm', interval: '3m', requestedStartUtc: '2026-07-01', requestedEndUtc: '2026-07-31', accountCurrency: 'USD', tradeMode: 'LongOnly' }, { get: (o, key) => key in o ? o[key] : 0 })
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

test('G4A research selection visibly labels research and keeps balance input', () => {
  let state = 0
  const react = { ...React, useState(initial) { return React.useState(state++ === 0 ? 'GRID_RANGE_4L_RESEARCH_V1' : initial) } }
  const { BacktestsPage } = loader(react)('src/pages/BacktestsPage.tsx')
  const html = renderToStaticMarkup(React.createElement(BacktestsPage))
  assert.match(html, /value="GRID_RANGE_4L_RESEARCH_V1" selected/)
  assert.match(html, /Grid Range Research — 4 Levels/)
  assert.match(html, /RESEARCH ONLY/)
  assert.match(html, /beyond Level 4/)
  assert.match(html, /type="number" required/)
})

test('G4A research POST contains only identity, dates, symbol, timeframe and balance', async () => {
  const calls = []; const saved = globalThis.fetch
  globalThis.fetch = async (url, init) => { calls.push([url, init]); return new Response(JSON.stringify(url.includes('antiforgery') ? { token: 'test' } : {})) }
  try {
    const dates = form.backtestDates('BTCUSDm', '3m', '2026-06-01', '2026-06-30')
    const body = form.gridBacktestRequest('GRID_RANGE_4L_RESEARCH_V1', dates, '1000')
    await api.runGridBacktest(body)
    assert.deepEqual(JSON.parse(calls[1][1].body), { ...dates, strategyId: 'GRID_RANGE_4L_RESEARCH_V1', startingBalance: 1000 })
    assert.equal(form.isGridStrategy('GRID_RANGE_V1'), true)
    assert.equal(form.isGridStrategy('GRID_RANGE_4L_RESEARCH_V1'), true)
    assert.equal(form.isGridStrategy('GRID_RANGE_3L_RESEARCH_V1'), false)
  } finally { globalThis.fetch = saved }
})

for (const [strategyId, levelCount] of [['GRID_RANGE_V1', 5], ['GRID_RANGE_4L_RESEARCH_V1', 4]]) {
  test(`G4A saved ${strategyId} renders actual persisted rules`, () => {
    const { GridBacktestResult } = loader()('src/pages/GridBacktestResult.tsx')
    const run = new Proxy({ strategyId, levelCount, gridBasketRiskPercent: 1, cooldownBars: 3, rangeLookback: 50, atrPeriod: 14, adxPeriod: 14, adxThreshold: 20, atrSpacingMultiplier: .5, symbol: 'BTCUSDm', interval: '3m', requestedStartUtc: '2026-06-01', requestedEndUtc: '2026-06-30', accountCurrency: 'USD' }, { get: (o, key) => key in o ? o[key] : 0 })
    const html = renderToStaticMarkup(React.createElement(GridBacktestResult, { detail: { strategyId, run, baskets: [], cycles: [] }, busy: false, exportExcel() {} }))
    assert.ok(html.includes(strategyId))
    assert.ok(html.includes(`${levelCount} equal-lot levels`))
    assert.ok(html.includes(`beyond Level ${levelCount} (stop level ${levelCount + 1})`))
    assert.match(html, /1% total basket price-risk/)
    if (levelCount === 4) { assert.match(html, /RESEARCH ONLY/); assert.doesNotMatch(html, /5 equal-lot levels|stop level 6/) }
    else assert.doesNotMatch(html, /RESEARCH ONLY/)
  })
}

test('G4A mixed recent history preserves both explicit identities', () => {
  let state = 0
  const rows = ['GRID_RANGE_V1', 'GRID_RANGE_4L_RESEARCH_V1'].map((strategyId, id) => ({ id, strategyId, symbol: 'TESTm', interval: '3m', requestedStartUtc: '2026-07-01', requestedEndUtc: '2026-07-31', basketCount: 1, netPnl: 1, accountCurrency: 'USD' }))
  const react = { ...React, useState(initial) { return React.useState(state++ === 2 ? rows : initial) } }
  const { BacktestsPage } = loader(react)('src/pages/BacktestsPage.tsx')
  const html = renderToStaticMarkup(React.createElement(BacktestsPage))
  for (const row of rows) assert.ok(html.includes(`<td class="py-3">${row.strategyId}</td>`))
})

test('G4B1 saved Grid result exposes guard research action with screening label', () => {
  const { GridBacktestResult } = loader()('src/pages/GridBacktestResult.tsx')
  const run = new Proxy({ id: 7, strategyId: 'GRID_RANGE_V1', levelCount: 5, symbol: 'TESTm', interval: '3m', requestedStartUtc: '2026-07-01', requestedEndUtc: '2026-07-31', accountCurrency: 'USD' }, { get: (o, key) => key in o ? o[key] : 0 })
  const html = renderToStaticMarkup(React.createElement(GridBacktestResult, { detail: { run, baskets: [], cycles: [] }, busy: false, exportExcel() {}, exportGuardResearch() {} }))
  assert.match(html, />Export Guard Research<\/button>/)
  assert.match(html, /SCREENING ONLY/)
  assert.match(html, /future sizing, qualification and later baskets are not rerun/)
  assert.doesNotMatch(html, /<input|<select/)
})

test('G4B1 export uses fixed endpoint and preserves safe old-run error', async () => {
  const calls = []; const savedFetch = globalThis.fetch; const savedDocument = globalThis.document
  let fail = false; let downloaded
  globalThis.fetch = async url => { calls.push(url); return fail ? new Response(JSON.stringify({ message: 'Grid breakout research requires a telemetry-enabled Grid backtest.' }), { status: 400 }) : new Response('test workbook') }
  globalThis.document = { createElement: () => ({ click() { downloaded = this.download } }) }
  try {
    await api.downloadGridGuardResearch(7)
    assert.equal(downloaded, 'grid-breakout-guard-research-7.xlsx')
    assert.deepEqual(calls, ['/api/backtests/grid/7/research/breakout-guards/export/excel'])
    fail = true
    await assert.rejects(api.downloadGridGuardResearch(8), /requires a telemetry-enabled Grid backtest/)
  } finally { globalThis.fetch = savedFetch; globalThis.document = savedDocument }
})

test('G4B1 guard research action calls its handler and is disabled while exporting', () => {
  const { GridBacktestResult } = loader()('src/pages/GridBacktestResult.tsx')
  const run = new Proxy({ strategyId: 'GRID_RANGE_4L_RESEARCH_V1', levelCount: 4, symbol: 'TESTm', interval: '3m', requestedStartUtc: '2026-07-01', requestedEndUtc: '2026-07-31', accountCurrency: 'USD' }, { get: (o, key) => key in o ? o[key] : 0 })
  let called = 0
  const tree = GridBacktestResult({ detail: { run, baskets: [], cycles: [] }, busy: true, exportExcel() {}, exportGuardResearch() { called++ } })
  const find = element => {
    if (!element || typeof element !== 'object') return null
    if (element.type === 'button' && element.props.children === 'Export Guard Research') return element
    for (const child of React.Children.toArray(element.props?.children)) { const result = find(child); if (result) return result }
    return null
  }
  const button = find(tree)
  assert.ok(button); assert.equal(button.props.disabled, true)
  button.props.onClick(); assert.equal(called, 1)
})

test('G4B2 selection explains frozen real guard without editable thresholds', () => {
  let state = 0
  const react = { ...React, useState(initial) { return React.useState(state++ === 0 ? 'GRID_RANGE_BREAKOUT_GUARD_RESEARCH_V1' : initial) } }
  const { BacktestsPage } = loader(react)('src/pages/BacktestsPage.tsx')
  const html = renderToStaticMarkup(React.createElement(BacktestsPage))
  assert.match(html, /Grid Range Research — Breakout Guard/)
  assert.match(html, /RESEARCH ONLY/)
  assert.match(html, /ADX has risen by at least 1.5/)
  assert.match(html, /2 consecutive candles/)
  assert.equal((html.match(/type="number"/g) ?? []).length, 1)
})

test('G4B2 request carries only frozen strategy identity and normal fields', async () => {
  const saved = globalThis.fetch; const calls = []
  globalThis.fetch = async (url, init) => { calls.push([url, init]); return new Response(JSON.stringify(url.includes('antiforgery') ? { token: 'test' } : {})) }
  try {
    const id = 'GRID_RANGE_BREAKOUT_GUARD_RESEARCH_V1'
    assert.equal(form.isGridStrategy(id), true)
    const dates = form.backtestDates('TESTm', '3m', '2026-07-01', '2026-07-31')
    await api.runGridBacktest(form.gridBacktestRequest(id, dates, '1000'))
    assert.deepEqual(JSON.parse(calls[1][1].body), { ...dates, strategyId: id, startingBalance: 1000 })
  } finally { globalThis.fetch = saved }
})

test('G4B2 saved result shows five-level real guard and hides shadow export', () => {
  const { GridBacktestResult } = loader()('src/pages/GridBacktestResult.tsx')
  const run = new Proxy({ strategyId: 'GRID_RANGE_BREAKOUT_GUARD_RESEARCH_V1', levelCount: 5, symbol: 'TESTm', interval: '3m', requestedStartUtc: '2026-07-01', requestedEndUtc: '2026-07-31', accountCurrency: 'USD' }, { get: (o, key) => key in o ? o[key] : 0 })
  const html = renderToStaticMarkup(React.createElement(GridBacktestResult, { detail: { run, baskets: [], cycles: [] }, busy: false, exportExcel() {}, exportGuardResearch() {} }))
  assert.match(html, /RESEARCH ONLY/); assert.match(html, /L3_ADX15_ADVERSE2/)
  assert.match(html, /5 equal-lot levels/); assert.match(html, /stop level 6/)
  assert.match(html, /Monitor from L3/); assert.match(html, /ADX increase ≥ 1.5/)
  assert.match(html, /Consecutive adverse closes ≥ 2/)
  assert.match(html, /Export Grid Excel/)
  assert.doesNotMatch(html, /Export Guard Research|SCREENING ONLY|4 equal-lot levels/)
})

for (const [strategyId, guard, strict] of [['GRID_RANGE_V1', true, true], ['GRID_RANGE_4L_RESEARCH_V1', true, false], ['GRID_RANGE_BREAKOUT_GUARD_RESEARCH_V1', false, false]]) {
  test(`G4B3 research actions match saved ${strategyId} eligibility`, () => {
    const { GridBacktestResult } = loader()('src/pages/GridBacktestResult.tsx')
    const run = new Proxy({ strategyId, levelCount: 5, symbol: 'TESTm', interval: '3m', requestedStartUtc: '2026-07-01', requestedEndUtc: '2026-07-31', accountCurrency: 'USD' }, { get: (o, key) => key in o ? o[key] : 0 })
    const props = { detail: { run, baskets: [], cycles: [] }, busy: false, exportExcel() {}, exportGuardResearch() {}, exportStrictGuardResearch() {} }
    const html = renderToStaticMarkup(React.createElement(GridBacktestResult, props))
    assert.equal(html.includes('Export Guard Research'), guard)
    assert.equal(html.includes('Export Strict Guard Research'), strict)
    assert.match(html, /Export Grid Excel/)
    assert.doesNotMatch(html, /<input|<select/)
  })
}

test('G4B3 strict action calls its handler and respects busy state', () => {
  const { GridBacktestResult } = loader()('src/pages/GridBacktestResult.tsx')
  const run = new Proxy({ strategyId: 'GRID_RANGE_V1', requestedStartUtc: '2026-07-01', requestedEndUtc: '2026-07-31' }, { get: (o, key) => key in o ? o[key] : 0 })
  let calls = 0
  function find(node) {
    if (!node || typeof node !== 'object') return undefined
    if (node.type === 'button' && node.props.children === 'Export Strict Guard Research') return node
    return React.Children.toArray(node.props?.children).map(find).find(Boolean)
  }
  for (const busy of [false, true]) {
    const button = find(GridBacktestResult({ detail: { run, baskets: [], cycles: [] }, busy, exportExcel() {}, exportGuardResearch() {}, exportStrictGuardResearch() { calls++ } }))
    assert.ok(button); assert.equal(button.props.disabled, busy)
    if (!busy) button.props.onClick()
  }
  assert.equal(calls, 1)
})

test('G4B3 strict download uses fixed route and filename and preserves source errors', async () => {
  const savedFetch = globalThis.fetch; const savedDocument = globalThis.document
  let url; let filename; let fail = false
  globalThis.fetch = async value => { url = value; return fail ? new Response(JSON.stringify({ message: 'Strict Grid guard research requires a telemetry-enabled GRID_RANGE_V1 backtest.' }), { status: 400 }) : new Response('workbook') }
  globalThis.document = { createElement() { return { set href(value) {}, set download(value) { filename = value }, click() {} } } }
  try {
    await api.downloadGridStrictGuardResearch(10)
    assert.equal(url, '/api/backtests/grid/10/research/strict-breakout-guards/export/excel')
    assert.equal(filename, 'grid-strict-guard-research-10.xlsx')
    fail = true
    await assert.rejects(api.downloadGridStrictGuardResearch(10), /telemetry-enabled GRID_RANGE_V1/)
  } finally { globalThis.fetch = savedFetch; globalThis.document = savedDocument }
})
