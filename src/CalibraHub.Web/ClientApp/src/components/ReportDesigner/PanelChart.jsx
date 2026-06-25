import React, { useState, useEffect, useRef } from 'react'
import {
  LineChart, Line, AreaChart, Area, BarChart, Bar, PieChart, Pie, Cell,
  Treemap, LabelList, XAxis, YAxis, Tooltip, ResponsiveContainer,
  FunnelChart, Funnel, ComposedChart, ReferenceLine,
  RadarChart, Radar, PolarGrid, PolarAngleAxis, PolarRadiusAxis,
  ScatterChart, Scatter, ZAxis,
} from 'recharts'
import ChartPreview from './ChartPreview'

// ── SQL üretici ───────────────────────────────────────────────────────────────

function aggSql(fn, col) {
  if (!fn || fn === 'SUM')     return `SUM(${col})`
  if (fn === 'COUNT')          return `COUNT(${col})`
  if (fn === 'COUNT_DISTINCT') return `COUNT(DISTINCT ${col})`
  if (fn === 'AVG')            return `AVG(${col})`
  if (fn === 'MIN')            return `MIN(${col})`
  if (fn === 'MAX')            return `MAX(${col})`
  return `SUM(${col})`
}

// Aktif filtreleri WHERE'e cevir — yalniz AYNI view'i kullanan panellere uygulanir.
// filters: { [key]: { source, field, values: [] } }
// Bir filtre bu panele uygulanır mı?
//  - viewFields biliniyorsa: panelin view'i o ALANI (kolonu) içeriyorsa uygulanır
//    (farklı view olsa bile alan adı eşleşirse). SQL hatasına karşı güvenli.
//  - viewFields bilinmiyorsa: eski davranış — aynı source eşleşmesi.
function filterApplies(panel, f, viewFields) {
  if (panel.sourceType !== 'view' || !f || !f.field || !f.values || !f.values.length) return false
  const fields = viewFields && viewFields[panel.source]
  if (fields && fields.length) return fields.indexOf(f.field) !== -1
  return f.source === panel.source
}

function buildWhere(panel, filters, viewFields) {
  if (panel.sourceType !== 'view' || !filters) return ''
  const clauses = []
  for (const key in filters) {
    const f = filters[key]
    if (!filterApplies(panel, f, viewFields)) continue
    const list = f.values.map(v => `'${String(v).replace(/'/g, "''")}'`).join(', ')
    clauses.push(`[${f.field}] IN (${list})`)
  }
  return clauses.length ? ' WHERE ' + clauses.join(' AND ') : ''
}

// useReportData deps icin filtre imzasi (yalniz bu panelin source'unu etkileyen)
function filtersSig(panel, filters, viewFields) {
  if (panel.sourceType !== 'view' || !filters) return ''
  let s = ''
  for (const key in filters) {
    const f = filters[key]
    if (filterApplies(panel, f, viewFields)) s += (f.field || '') + '=' + f.values.join(',') + ';'
  }
  return s
}

function buildSql(panel, filters, viewFields) {
  const { sourceType, source, metric, group, groupIsTime, sqlQuery, aggregate, type } = panel
  if (sourceType === 'sql') return sqlQuery?.trim() || null
  if (type === 'text') return null

  const where = buildWhere(panel, filters, viewFields)

  // Pivot: ham satır+sütun+değer kolonlarını çek; çok-alanlı pivot client-side hesaplanır (computePivot)
  if (type === 'pivot') {
    const cfg = ensurePivotCfg(panel)
    const fields = [...new Set([...cfg.rows, ...cfg.cols, ...cfg.values.map(v => v.field)])].filter(Boolean)
    if (!source || !fields.length) return null
    const view = `[${source}]`
    return `SELECT ${fields.map(f => `[${f}]`).join(', ')} FROM ${view}${where}`
  }

  if (!source || !metric) return null
  const view = `[${source}]`
  const col  = `[${metric}]`
  const grp  = group ? `[${group}]` : null
  const agg  = aggSql(aggregate, col)

  // Tek değer çıktısı: stat (KPI) + gauge (gösterge) + bullet (hedef göstergesi)
  if (!grp || type === 'stat' || type === 'gauge' || type === 'bullet')
    return `SELECT ${agg} AS [value] FROM ${view}${where}`

  // Kombi (bar + çizgi): iki metrik
  if (type === 'combo') {
    const agg2 = panel.metric2
      ? `, ${aggSql(panel.aggregate2 || aggregate, `[${panel.metric2}]`)} AS [value2]`
      : ''
    if (groupIsTime)
      return `SELECT CAST(${grp} AS DATE) AS [time], ${agg} AS [value]${agg2} FROM ${view}${where} GROUP BY CAST(${grp} AS DATE) ORDER BY [time]`
    return `SELECT ${grp} AS [label], ${agg} AS [value]${agg2} FROM ${view}${where} GROUP BY ${grp} ORDER BY ${grp}`
  }

  // %100 Yığılmış bar: üç kolon (label, series, value)
  if (type === 'stacked100' && panel.series)
    return `SELECT ${grp} AS [label], [${panel.series}] AS [series], ${agg} AS [value] FROM ${view}${where} GROUP BY ${grp}, [${panel.series}] ORDER BY ${grp}, [${panel.series}]`

  // Isı haritası: group → satır, heatCol → sütun
  if (type === 'heatmap' && panel.heatCol)
    return `SELECT ${grp} AS [row], [${panel.heatCol}] AS [col], ${agg} AS [value] FROM ${view}${where} GROUP BY ${grp}, [${panel.heatCol}] ORDER BY ${grp}, [${panel.heatCol}]`

  // Dağılım: group → etiket, metric → x, metric2 → y
  if (type === 'scatter') {
    const agg2 = panel.metric2
      ? `, ${aggSql(panel.aggregate2 || aggregate, `[${panel.metric2}]`)} AS [y]`
      : ''
    return `SELECT ${grp} AS [label], ${agg} AS [x]${agg2} FROM ${view}${where} GROUP BY ${grp} ORDER BY ${grp}`
  }

  // Şelale: grup sırası korunur (ORDER BY değere göre değil)
  if (type === 'waterfall') {
    if (groupIsTime)
      return `SELECT CAST(${grp} AS DATE) AS [time], ${agg} AS [value] FROM ${view}${where} GROUP BY CAST(${grp} AS DATE) ORDER BY [time]`
    return `SELECT ${grp} AS [label], ${agg} AS [value] FROM ${view}${where} GROUP BY ${grp} ORDER BY ${grp}`
  }

  if (groupIsTime)
    return `SELECT CAST(${grp} AS DATE) AS [time], ${agg} AS [value] FROM ${view}${where} GROUP BY CAST(${grp} AS DATE) ORDER BY [time]`
  if (type === 'table')
    return `SELECT TOP 100 ${grp} AS [label], ${col} FROM ${view}${where} ORDER BY ${grp}`
  return `SELECT ${grp} AS [label], ${agg} AS [value] FROM ${view}${where} GROUP BY ${grp} ORDER BY [value] DESC`
}

// ── Veri cekme hook'u ─────────────────────────────────────────────────────────

function useReportData(panel, activeFilters, viewFields) {
  const [state, setState] = useState({ data: null, loading: false, error: null })
  const abortRef = useRef(null)
  const fkey = filtersSig(panel, activeFilters, viewFields)
  // Pivot config (view kaynağında SQL'i etkiler) — stabil imza ile refetch tetikle
  const pvSig = panel.type === 'pivot' ? JSON.stringify(ensurePivotCfg(panel)) : ''

  useEffect(() => {
    if (panel.sourceType === 'saved' && panel.sourceId > 0) {
      setState(s => ({ ...s, loading: true, error: null }))
      abortRef.current?.abort()
      abortRef.current = new AbortController()
      fetch(`/api/report/query/source/${panel.sourceId}`, {
        credentials: 'same-origin',
        signal: abortRef.current.signal,
      })
        .then(r => r.json())
        .then(d => setState({ data: d.ok ? d : null, loading: false, error: d.ok ? null : d.error }))
        .catch(e => { if (e.name !== 'AbortError') setState({ data: null, loading: false, error: e.message }) })
      return () => abortRef.current?.abort()
    }

    const sql = buildSql(panel, activeFilters, viewFields)
    if (!sql) { setState({ data: null, loading: false, error: null }); return }

    setState(s => ({ ...s, loading: true, error: null }))
    abortRef.current?.abort()
    abortRef.current = new AbortController()

    fetch('/api/report/query/inline', {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ sql, cacheTtlMinutes: 5 }),
      signal: abortRef.current.signal,
    })
      .then(r => r.json())
      .then(d => setState({ data: d.ok ? d : null, loading: false, error: d.ok ? null : d.error }))
      .catch(e => { if (e.name !== 'AbortError') setState({ data: null, loading: false, error: e.message }) })

    return () => abortRef.current?.abort()
  }, [
    panel.sourceType, panel.source, panel.metric, panel.aggregate,
    panel.group, panel.groupIsTime, panel.sqlQuery, panel.sourceId, panel.type,
    panel.rowField, panel.colField, panel.measure, panel._nonce, pvSig, fkey,
    panel.metric2, panel.aggregate2, panel.series, panel.heatCol,
  ])

  return state
}

// ── Veri donusturucüler ─────────────────────────────────────────────────────

// Kolon adından index — bulunamazsa fallback (varsayılan konum)
function colIndex(columns, name, fallback) {
  if (name) { const i = columns.indexOf(name); if (i >= 0) return i }
  return fallback
}

// Kayıtlı/SQL kaynakta seçilen kolonları sırayla alıp yeni apiData üret
function pickColumns(apiData, names) {
  const cols = apiData?.columns || []
  const idx = names.map(n => cols.indexOf(n)).filter(i => i >= 0)
  if (!idx.length) return apiData
  return {
    columns: idx.map(i => cols[i]),
    rows: (apiData?.rows || []).map(r => idx.map(i => r[i])),
  }
}

// labelField / valueField: kayıtlı/SQL kaynakta hangi kolon kategori/değer olacak (boşsa 1./2. kolon).
// agg: kategoriye göre gruplama (SUM/AVG/…). 'NONE' veya boşsa ham satırlar.
function toChartData(apiData, labelField, valueField, agg) {
  if (!apiData?.columns?.length || !apiData.rows?.length) return []
  const cols = apiData.columns
  const li = colIndex(cols, labelField, 0)
  const vi = colIndex(cols, valueField, cols.length > 1 ? 1 : 0)
  const raw = apiData.rows.map(row => ({
    label: row[li] != null ? String(row[li]) : '',
    value: row[vi] != null ? Number(row[vi]) : 0,
  }))
  if (!agg || agg === 'NONE') return raw
  const order = []
  const groups = {}
  raw.forEach(d => {
    if (!(d.label in groups)) { groups[d.label] = []; order.push(d.label) }
    groups[d.label].push(isNaN(d.value) ? 0 : d.value)
  })
  return order.map(label => ({ label, value: aggValues(groups[label], agg) }))
}

// Kombi (bar+line): labelField/valueField/valueField2 → {label, bar, line}[]
function toComboData(apiData, labelField, valueField, valueField2, agg, agg2) {
  if (!apiData?.columns?.length || !apiData.rows?.length) return []
  const cols = apiData.columns
  const li  = colIndex(cols, labelField, 0)
  const v1i = colIndex(cols, valueField,  cols.length > 1 ? 1 : 0)
  const v2i = colIndex(cols, valueField2 || 'value2', cols.length > 2 ? 2 : (cols.length > 1 ? 1 : 0))
  const raw = apiData.rows.map(row => ({
    label: row[li]  != null ? String(row[li])  : '',
    bar:   row[v1i] != null ? Number(row[v1i]) : 0,
    line:  row[v2i] != null ? Number(row[v2i]) : 0,
  }))
  if (!agg || agg === 'NONE') return raw
  const order = [], groups = {}
  raw.forEach(d => {
    if (!(d.label in groups)) { groups[d.label] = { bars: [], lines: [] }; order.push(d.label) }
    groups[d.label].bars.push(isNaN(d.bar) ? 0 : d.bar)
    groups[d.label].lines.push(isNaN(d.line) ? 0 : d.line)
  })
  return order.map(label => ({
    label,
    bar:  aggValues(groups[label].bars,  agg),
    line: aggValues(groups[label].lines, agg2 || agg),
  }))
}

// %100 Yığılmış bar: → { seriesKeys, data: [{label, [s]: pct}] }
function toStacked100Data(apiData, labelField, seriesField, valueField) {
  if (!apiData?.columns?.length || !apiData.rows?.length) return { seriesKeys: [], data: [] }
  const cols = apiData.columns
  const li = colIndex(cols, labelField, 0)
  const si = colIndex(cols, seriesField, 1)
  const vi = colIndex(cols, valueField, 2)
  const labelOrder = [], seriesSet = new Set(), matrix = {}
  apiData.rows.forEach(row => {
    const lbl = row[li] != null ? String(row[li]) : ''
    const ser = row[si] != null ? String(row[si]) : ''
    const val = row[vi] != null ? Number(row[vi]) : 0
    if (!matrix[lbl]) { matrix[lbl] = {}; labelOrder.push(lbl) }
    matrix[lbl][ser] = (matrix[lbl][ser] || 0) + val
    seriesSet.add(ser)
  })
  const seriesKeys = [...seriesSet].sort()
  const data = labelOrder.map(lbl => {
    const row = matrix[lbl]
    const total = seriesKeys.reduce((s, k) => s + (row[k] || 0), 0)
    const entry = { label: lbl }
    seriesKeys.forEach(k => { entry[k] = total > 0 ? Math.round(((row[k] || 0) / total) * 1000) / 10 : 0 })
    return entry
  })
  return { seriesKeys, data }
}

// Dağılım: ham kaynak için xField/yField/labelField → {x, y, label?}[]
function toScatterData(apiData, xField, yField, labelField) {
  if (!apiData?.columns?.length || !apiData.rows?.length) return []
  const cols = apiData.columns
  const xi = colIndex(cols, xField, 0)
  const yi = colIndex(cols, yField, cols.length > 1 ? 1 : 0)
  const li = labelField ? colIndex(cols, labelField, -1) : -1
  return apiData.rows
    .map(row => ({
      x:     row[xi] != null ? Number(row[xi]) : 0,
      y:     row[yi] != null ? Number(row[yi]) : 0,
      label: li >= 0 && row[li] != null ? String(row[li]) : undefined,
    }))
    .filter(d => !isNaN(d.x) && !isNaN(d.y))
}

// valueField verilirse o kolonu agg'le (kayıtlı/SQL KPI/gösterge); değilse 1. satırdaki ilk sayısal (view).
function toStatValue(apiData, valueField, agg) {
  if (!apiData?.rows?.length) return null
  const cols = apiData.columns || []
  const vi = valueField ? cols.indexOf(valueField) : -1
  if (vi >= 0) {
    const vals = apiData.rows.map(r => Number(r[vi])).filter(v => !isNaN(v))
    if (!vals.length) return null
    return aggValues(vals, agg || 'SUM')
  }
  const row = apiData.rows[0]
  const num = row.find(v => v != null && !isNaN(Number(v)))
  if (num == null) return null
  return Number(num)
}

function toTableData(apiData) {
  if (!apiData?.columns?.length) return { columns: [], rows: [] }
  return { columns: apiData.columns, rows: apiData.rows || [] }
}

// Kolon SQL tipi tespiti: int/decimal/float değerler JS'e `number`, nvarchar/date `string` gelir.
// Bir kolon sayısal sayılır ancak ÖRNEKLENEN tüm boş-olmayan değerleri `number` ise.
function colIsNumeric(rows, idx) {
  let seen = 0
  for (let i = 0; i < rows.length && seen < 40; i++) {
    const v = rows[i][idx]
    if (v == null || v === '') continue
    seen++
    if (typeof v !== 'number') return false
  }
  return seen > 0
}

function detectNumericCols(apiData) {
  const cols = apiData?.columns || []
  const rows = apiData?.rows || []
  const map  = {}
  cols.forEach((name, ci) => { map[name] = colIsNumeric(rows, ci) })
  return map
}

// ── Tooltip ─────────────────────────────────────────────────────────────────

function RdTooltip({ active, payload, color }) {
  if (!active || !payload?.length) return null
  const v = payload[0]?.value
  return (
    <div style={{
      background: '#0c1525', border: '1px solid rgba(255,255,255,.1)',
      borderRadius: 6, padding: '5px 10px', fontSize: 11, color: '#e2e8f0',
    }}>
      <span style={{ color }}>{typeof v === 'number' ? v.toLocaleString('tr-TR') : v}</span>
    </div>
  )
}

// ── Chart renderers ───────────────────────────────────────────────────────────

const TICK = { fill: '#4a5568', fontSize: 9 }
const axisLine = { stroke: 'rgba(255,255,255,.06)' }
const PIE_COLORS = ['#6366f1', '#10b981', '#f59e0b', '#ef4444', '#3b82f6', '#8b5cf6']

function RdLineChart({ data, color, thickness = 2, height = 110, curve = true, dots = false }) {
  return (
    <ResponsiveContainer width="100%" height={height}>
      <LineChart data={data} margin={{ top: 4, right: 4, bottom: 0, left: -20 }}>
        <XAxis dataKey="label" tick={TICK} axisLine={axisLine} tickLine={false} interval="preserveStartEnd" />
        <YAxis tick={TICK} axisLine={false} tickLine={false} />
        <Tooltip content={<RdTooltip color={color} />} />
        <Line type={curve ? 'monotone' : 'linear'} dataKey="value" stroke={color} strokeWidth={thickness}
              dot={dots ? { r: 2, strokeWidth: 0, fill: color } : false} activeDot={{ r: 4, strokeWidth: 0 }} />
      </LineChart>
    </ResponsiveContainer>
  )
}

function RdAreaChart({ data, color, thickness = 2, height = 110, curve = true, fillOpacity = 0.25, dots = false }) {
  const gid = 'rda_' + color.replace('#', '')
  return (
    <ResponsiveContainer width="100%" height={height}>
      <AreaChart data={data} margin={{ top: 4, right: 4, bottom: 0, left: -20 }}>
        <defs>
          <linearGradient id={gid} x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor={color} stopOpacity={fillOpacity} />
            <stop offset="100%" stopColor={color} stopOpacity={0} />
          </linearGradient>
        </defs>
        <XAxis dataKey="label" tick={TICK} axisLine={axisLine} tickLine={false} interval="preserveStartEnd" />
        <YAxis tick={TICK} axisLine={false} tickLine={false} />
        <Tooltip content={<RdTooltip color={color} />} />
        <Area type={curve ? 'monotone' : 'linear'} dataKey="value" stroke={color} strokeWidth={thickness}
              fill={`url(#${gid})`} dot={dots ? { r: 2, strokeWidth: 0, fill: color } : false} activeDot={{ r: 4, strokeWidth: 0 }} />
      </AreaChart>
    </ResponsiveContainer>
  )
}

const valueLabelStyle = { fontSize: 9, fill: '#94a3b8' }
const fmtBarLabel = v => (typeof v === 'number' ? v.toLocaleString('tr-TR') : v)

function RdBarChart({ data, color, height = 110, horizontal = false, showValues = false }) {
  if (horizontal) {
    return (
      <ResponsiveContainer width="100%" height={height}>
        <BarChart data={data} layout="vertical" margin={{ top: 4, right: showValues ? 36 : 8, bottom: 0, left: 4 }}>
          <XAxis type="number" tick={TICK} axisLine={false} tickLine={false} />
          <YAxis type="category" dataKey="label" tick={TICK} axisLine={axisLine} tickLine={false} width={72} />
          <Tooltip content={<RdTooltip color={color} />} />
          <Bar dataKey="value" fill={color} radius={[0, 3, 3, 0]} maxBarSize={28}>
            {showValues && <LabelList dataKey="value" position="right" style={valueLabelStyle} formatter={fmtBarLabel} />}
          </Bar>
        </BarChart>
      </ResponsiveContainer>
    )
  }
  return (
    <ResponsiveContainer width="100%" height={height}>
      <BarChart data={data} margin={{ top: showValues ? 14 : 4, right: 4, bottom: 0, left: -20 }}>
        <XAxis dataKey="label" tick={TICK} axisLine={axisLine} tickLine={false} interval="preserveStartEnd" />
        <YAxis tick={TICK} axisLine={false} tickLine={false} />
        <Tooltip content={<RdTooltip color={color} />} />
        <Bar dataKey="value" fill={color} radius={[3, 3, 0, 0]} maxBarSize={40}>
          {showValues && <LabelList dataKey="value" position="top" style={valueLabelStyle} formatter={fmtBarLabel} />}
        </Bar>
      </BarChart>
    </ResponsiveContainer>
  )
}

function RdPieChart({ data, color, height = 110, radiusPx = 110, donut = true, showLabels = false, showPercent = false }) {
  const colors = data.length > 0 ? data.map((_, i) => PIE_COLORS[i % PIE_COLORS.length]) : [color]
  const outerR = Math.round(radiusPx * 0.42)
  const innerR = donut ? Math.round(radiusPx * 0.18) : 0
  return (
    <ResponsiveContainer width="100%" height={height}>
      <PieChart>
        <Pie data={data} dataKey="value" nameKey="label" cx="50%" cy="50%"
             outerRadius={outerR} innerRadius={innerR} paddingAngle={donut ? 2 : 0}
             label={(showLabels || showPercent) ? ({ name, percent }) => showPercent ? `%${Math.round((percent || 0) * 100)}` : name : false} labelLine={false}
             style={{ fontSize: 9, fill: '#94a3b8' }}>
          {data.map((_, i) => <Cell key={i} fill={colors[i]} />)}
        </Pie>
        <Tooltip content={<RdTooltip color={color} />} />
      </PieChart>
    </ResponsiveContainer>
  )
}

// Huni (Funnel) — aşama bazlı değer; çoktan aza sıralanır, her aşama bir dilim.
function RdFunnelChart({ data, color, height = 110 }) {
  const rows = [...data]
    .sort((a, b) => (b.value || 0) - (a.value || 0))
    .map((d, i) => ({ ...d, _fill: i === 0 ? color : PIE_COLORS[i % PIE_COLORS.length] }))
  return (
    <ResponsiveContainer width="100%" height={height}>
      <FunnelChart margin={{ top: 6, right: 70, bottom: 6, left: 8 }}>
        <Tooltip content={<RdTooltip color={color} />} />
        <Funnel dataKey="value" nameKey="label" data={rows} isAnimationActive={false} stroke="rgba(10,16,32,.55)">
          {rows.map((r, i) => <Cell key={i} fill={r._fill} />)}
          <LabelList position="right" dataKey="label" stroke="none" fill="#cbd5e1" fontSize={10} />
          <LabelList position="center" dataKey="value" stroke="none" fill="#ffffff" fontSize={10} formatter={fmtBarLabel} />
        </Funnel>
      </FunnelChart>
    </ResponsiveContainer>
  )
}

// ── Kombi (Bar + Çizgi) ───────────────────────────────────────────────────────

function RdComboChart({ data, color, color2 = '#10b981', thickness = 2, height = 110, showValues = false, curve = true }) {
  return (
    <ResponsiveContainer width="100%" height={height}>
      <ComposedChart data={data} margin={{ top: showValues ? 14 : 4, right: 4, bottom: 0, left: -20 }}>
        <XAxis dataKey="label" tick={TICK} axisLine={axisLine} tickLine={false} interval="preserveStartEnd" />
        <YAxis tick={TICK} axisLine={false} tickLine={false} />
        <Tooltip content={<RdTooltip color={color} />} />
        <Bar dataKey="bar" fill={color} radius={[3, 3, 0, 0]} maxBarSize={40}>
          {showValues && <LabelList dataKey="bar" position="top" style={{ fontSize: 8, fill: '#94a3b8' }} />}
        </Bar>
        <Line type={curve ? 'monotone' : 'linear'} dataKey="line" stroke={color2} strokeWidth={thickness}
              dot={false} activeDot={{ r: 4, strokeWidth: 0, fill: color2 }} />
      </ComposedChart>
    </ResponsiveContainer>
  )
}

// ── Şelale (Waterfall) ────────────────────────────────────────────────────────

function RdWaterfallChart({ data, height = 110 }) {
  let base = 0
  const wData = data.map(d => {
    const entry = { label: d.label, base: d.value >= 0 ? base : base + d.value, delta: Math.abs(d.value), start: base, up: d.value >= 0 }
    base += d.value
    return entry
  })
  return (
    <ResponsiveContainer width="100%" height={height}>
      <BarChart data={wData} margin={{ top: 4, right: 4, bottom: 0, left: -20 }}>
        <XAxis dataKey="label" tick={TICK} axisLine={axisLine} tickLine={false} interval="preserveStartEnd" />
        <YAxis tick={TICK} axisLine={false} tickLine={false} />
        <Tooltip content={({ active, payload, label }) => {
          if (!active || !payload?.length) return null
          const e = payload[0]?.payload
          if (!e) return null
          const val = e.up ? e.delta : -e.delta
          return (
            <div style={{ background: '#0c1525', border: '1px solid rgba(255,255,255,.1)', borderRadius: 6, padding: '5px 10px', fontSize: 11, color: '#e2e8f0' }}>
              <div style={{ color: '#94a3b8', marginBottom: 2 }}>{label}</div>
              <div style={{ color: e.up ? '#10b981' : '#ef4444' }}>{val >= 0 ? '+' : ''}{val.toLocaleString('tr-TR')}</div>
              <div style={{ fontSize: 9, color: '#64748b' }}>{(e.start + val).toLocaleString('tr-TR')}</div>
            </div>
          )
        }} />
        <Bar dataKey="base" stackId="wf" fill="transparent" stroke="none" />
        <Bar dataKey="delta" stackId="wf" radius={[3, 3, 0, 0]} maxBarSize={40}>
          {wData.map((r, i) => <Cell key={i} fill={r.up ? '#10b981' : '#ef4444'} />)}
        </Bar>
      </BarChart>
    </ResponsiveContainer>
  )
}

// ── %100 Yığılmış Bar ─────────────────────────────────────────────────────────

function RdStacked100Chart({ stacked, height = 110 }) {
  const { seriesKeys = [], data = [] } = stacked || {}
  if (!data.length || !seriesKeys.length) return null
  return (
    <ResponsiveContainer width="100%" height={height}>
      <BarChart data={data} margin={{ top: 4, right: 4, bottom: 0, left: -20 }}>
        <XAxis dataKey="label" tick={TICK} axisLine={axisLine} tickLine={false} interval="preserveStartEnd" />
        <YAxis tick={TICK} axisLine={false} tickLine={false} domain={[0, 100]} tickFormatter={v => v + '%'} />
        <Tooltip formatter={(v, name) => [v.toLocaleString('tr-TR') + '%', name]} />
        {seriesKeys.map((sk, i) => (
          <Bar key={sk} dataKey={sk} stackId="s100" fill={PIE_COLORS[i % PIE_COLORS.length]}
               maxBarSize={40} radius={i === seriesKeys.length - 1 ? [3, 3, 0, 0] : undefined} />
        ))}
      </BarChart>
    </ResponsiveContainer>
  )
}

// ── Hedef Göstergesi (Bullet) ─────────────────────────────────────────────────

function RdBulletChart({ apiData, color = '#6366f1', height = 110, min, max, bulletTarget, valueField, valueAgg, fill = false }) {
  const actual = toStatValue(apiData, valueField, valueAgg)
  if (actual == null) return (
    <div style={{ height: fill ? '100%' : height, display: 'flex', alignItems: 'center', justifyContent: 'center', color: '#4a5568', fontSize: 11 }}>—</div>
  )
  const lo = Number.isFinite(+min) ? +min : 0
  const tgt = Number.isFinite(+bulletTarget) ? +bulletTarget : null
  const hi = Number.isFinite(+max) && +max > lo ? +max : Math.max(actual, tgt ?? actual) * 1.25 || 100
  const W = 220, barH = 16, totalH = 62
  const barY = (totalH - barH) / 2
  const pct = v => Math.min(1, Math.max(0, (v - lo) / (hi - lo || 1)))
  return (
    <div style={{ height: fill ? '100%' : height, display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: 6, padding: '0 16px' }}>
      <svg viewBox={`0 0 ${W} ${totalH}`} style={{ width: '100%', maxWidth: 300 }}>
        <rect x={0} y={barY} width={W} height={barH} rx={3} fill="rgba(255,255,255,.08)" />
        <rect x={0} y={barY} width={W * pct(actual)} height={barH} rx={3} fill={color} fillOpacity=".85" />
        {tgt != null && <rect x={W * pct(tgt) - 1.5} y={barY - 6} width={3} height={barH + 12} rx={1.5} fill="#f59e0b" />}
        <text x={0} y={totalH - 1} fontSize={8} fill="#475569">{lo.toLocaleString('tr-TR')}</text>
        <text x={W} y={totalH - 1} textAnchor="end" fontSize={8} fill="#475569">{hi.toLocaleString('tr-TR')}</text>
        {tgt != null && (
          <text x={Math.min(W - 10, Math.max(10, W * pct(tgt)))} y={barY - 9} textAnchor="middle" fontSize={8} fill="#f59e0b">
            {tgt.toLocaleString('tr-TR')}
          </text>
        )}
      </svg>
      <div style={{ fontSize: 13, fontWeight: 600, color: '#e2e8f0', letterSpacing: '-0.02em', lineHeight: 1 }}>
        {actual.toLocaleString('tr-TR')}
        {tgt != null && <span style={{ fontSize: 9, color: '#64748b', marginLeft: 6, fontWeight: 400 }}>/ {tgt.toLocaleString('tr-TR')}</span>}
      </div>
    </div>
  )
}

// ── Isı Haritası (Heatmap) ────────────────────────────────────────────────────

function RdHeatmap({ apiData, color = '#6366f1', height = 110, fill = false, labelField, seriesField, valueField }) {
  if (!apiData?.columns?.length || !apiData.rows?.length)
    return <div style={{ height: fill ? '100%' : height, display: 'flex', alignItems: 'center', justifyContent: 'center', color: '#4a5568', fontSize: 11 }}>Veri yok</div>
  const cols = apiData.columns
  const ri = colIndex(cols, labelField, 0)
  const ci = colIndex(cols, seriesField, 1)
  const vi = colIndex(cols, valueField, 2)
  const rowKeys = [], colKeys = [], matrix = {}
  let minV = Infinity, maxV = -Infinity
  apiData.rows.forEach(row => {
    const r = String(row[ri] ?? ''), c = String(row[ci] ?? ''), v = Number(row[vi]) || 0
    if (!rowKeys.includes(r)) rowKeys.push(r)
    if (!colKeys.includes(c)) colKeys.push(c)
    if (!matrix[r]) matrix[r] = {}
    matrix[r][c] = (matrix[r][c] || 0) + v
    if (v < minV) minV = v; if (v > maxV) maxV = v
  })
  const range = maxV - minV || 1
  const hex = color.replace('#', '')
  const rgb = hex.length === 6
    ? [parseInt(hex.slice(0,2),16), parseInt(hex.slice(2,4),16), parseInt(hex.slice(4,6),16)]
    : [99, 102, 241]
  const CELL = 28, PAD = 2, LBL = 56
  return (
    <div style={{ height: fill ? '100%' : height, overflow: 'auto', fontSize: 8, padding: '4px 0' }}>
      <div style={{ display: 'inline-block', minWidth: LBL + colKeys.length * (CELL + PAD) }}>
        <div style={{ display: 'flex', marginLeft: LBL }}>
          {colKeys.map(c => (
            <div key={c} style={{ width: CELL, marginRight: PAD, textAlign: 'center', color: '#64748b', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }} title={c}>{c}</div>
          ))}
        </div>
        {rowKeys.map(r => (
          <div key={r} style={{ display: 'flex', marginBottom: PAD }}>
            <div style={{ width: LBL, color: '#64748b', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', paddingRight: 4, flexShrink: 0 }} title={r}>{r}</div>
            {colKeys.map(c => {
              const v = matrix[r]?.[c]
              const op = v != null ? 0.08 + 0.82 * ((v - minV) / range) : 0
              return (
                <div key={c} title={v != null ? v.toLocaleString('tr-TR') : '—'}
                     style={{ width: CELL, height: CELL, marginRight: PAD, borderRadius: 3,
                              background: v != null ? `rgba(${rgb.join(',')},${op.toFixed(2)})` : 'rgba(255,255,255,.02)',
                              display: 'flex', alignItems: 'center', justifyContent: 'center',
                              color: op > 0.5 ? '#fff' : '#475569', fontSize: 7 }}>
                  {v != null ? (v > 9999 ? (v / 1000).toFixed(1) + 'k' : v) : ''}
                </div>
              )
            })}
          </div>
        ))}
      </div>
    </div>
  )
}

// ── Radar Grafiği ─────────────────────────────────────────────────────────────

function RdRadarChart({ data, color, height = 110, fillOpacity = 0.25 }) {
  return (
    <ResponsiveContainer width="100%" height={height}>
      <RadarChart data={data} margin={{ top: 8, right: 20, bottom: 8, left: 20 }}>
        <PolarGrid stroke="rgba(255,255,255,.08)" />
        <PolarAngleAxis dataKey="label" tick={{ ...TICK, fontSize: 8 }} />
        <PolarRadiusAxis tick={false} axisLine={false} />
        <Radar dataKey="value" stroke={color} strokeWidth={2} fill={color} fillOpacity={fillOpacity} dot={false} />
        <Tooltip content={<RdTooltip color={color} />} />
      </RadarChart>
    </ResponsiveContainer>
  )
}

// ── Metin Kartı ───────────────────────────────────────────────────────────────

function RdTextCard({ panel, height, fill = false }) {
  const text = panel.textContent || ''
  return (
    <div style={{ height: fill ? '100%' : height, padding: '8px 12px', overflow: 'auto',
                  fontSize: panel.textSize || 12, color: '#e2e8f0', lineHeight: 1.6,
                  whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>
      {text || <span style={{ color: '#475569', fontStyle: 'italic' }}>Metin kartı — sağ panelden içerik girin.</span>}
    </div>
  )
}

// ── Dağılım Grafiği (Scatter) ─────────────────────────────────────────────────

function RdScatterChart({ data, color, height = 110, xLabel = 'X', yLabel = 'Y' }) {
  return (
    <ResponsiveContainer width="100%" height={height}>
      <ScatterChart margin={{ top: 4, right: 8, bottom: 0, left: -20 }}>
        <XAxis dataKey="x" type="number" name={xLabel} tick={TICK} axisLine={axisLine} tickLine={false} />
        <YAxis dataKey="y" type="number" name={yLabel} tick={TICK} axisLine={false} tickLine={false} />
        <ZAxis range={[30, 30]} />
        <Tooltip cursor={{ strokeDasharray: '3 3', stroke: 'rgba(255,255,255,.15)' }}
          content={({ active, payload }) => {
            if (!active || !payload?.length) return null
            const p = payload[0]?.payload
            if (!p) return null
            return (
              <div style={{ background: '#0c1525', border: '1px solid rgba(255,255,255,.1)', borderRadius: 6, padding: '5px 10px', fontSize: 11, color: '#e2e8f0' }}>
                {p.label != null && <div style={{ color: '#94a3b8', marginBottom: 2 }}>{p.label}</div>}
                <div>{xLabel}: <span style={{ color }}>{p.x?.toLocaleString('tr-TR')}</span></div>
                <div>{yLabel}: <span style={{ color }}>{p.y?.toLocaleString('tr-TR')}</span></div>
              </div>
            )
          }}
        />
        <Scatter data={data} fill={color} fillOpacity={0.75} />
      </ScatterChart>
    </ResponsiveContainer>
  )
}

function RdStatCard({ apiData, height = 56, big = false, prefix = '', suffix = '', decimals, valueField, valueAgg }) {
  const val = toStatValue(apiData, valueField, valueAgg)
  if (val == null) return <div style={{ height, display: 'flex', alignItems: 'center', justifyContent: 'center', color: '#4a5568', fontSize: 11 }}>—</div>
  const opts = Number.isFinite(decimals) ? { minimumFractionDigits: decimals, maximumFractionDigits: decimals } : {}
  return (
    <div style={{ height, display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: 3 }}>
      <div style={{ fontSize: big ? 40 : 22, fontWeight: 600, color: '#f1f5f9', letterSpacing: '-0.03em', lineHeight: 1 }}>
        {prefix}{val.toLocaleString('tr-TR', opts)}{suffix ? ' ' + suffix : ''}
      </div>
      <div style={{ fontSize: 9, color: '#64748b' }}>{valueField || apiData.columns?.[0] || 'değer'}</div>
    </div>
  )
}

function RdGauge({ apiData, color, height = 110, min, max, suffix = '', valueField, valueAgg }) {
  const val = toStatValue(apiData, valueField, valueAgg)
  if (val == null) return <div style={{ height, display: 'flex', alignItems: 'center', justifyContent: 'center', color: '#4a5568', fontSize: 11 }}>—</div>
  const lo  = Number.isFinite(+min) ? +min : 0
  const hi  = Number.isFinite(+max) && +max > lo ? +max : lo + 100
  const pct = Math.min(1, Math.max(0, (val - lo) / (hi - lo)))
  const big = height > 150
  return (
    <div style={{ height, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
      <svg viewBox="0 0 200 118" style={{ width: '100%', height: '100%', maxWidth: big ? 340 : 220 }}>
        <path d="M14,104 A90,90 0 0 1 186,104" fill="none" stroke="#1e293b" strokeWidth="15" strokeLinecap="round" pathLength="100" />
        <path d="M14,104 A90,90 0 0 1 186,104" fill="none" stroke={color} strokeWidth="15" strokeLinecap="round" pathLength="100" strokeDasharray={`${pct * 100} 100`} />
        <text x="100" y="86" textAnchor="middle" fontSize="30" fontWeight="600" fill="#f1f5f9">{val.toLocaleString('tr-TR')}</text>
        {suffix ? <text x="100" y="103" textAnchor="middle" fontSize="11" fill="#64748b">{suffix}</text> : null}
        <text x="16" y="116" textAnchor="middle" fontSize="9" fill="#475569">{lo.toLocaleString('tr-TR')}</text>
        <text x="184" y="116" textAnchor="middle" fontSize="9" fill="#475569">{hi.toLocaleString('tr-TR')}</text>
      </svg>
    </div>
  )
}

function TreemapNode(props) {
  const { x, y, width, height, name, index, colors, showLabels } = props
  if (width <= 0 || height <= 0) return null
  const fill = colors[index % colors.length]
  return (
    <g>
      <rect x={x} y={y} width={width} height={height} fill={fill} stroke="#0c1525" strokeWidth={2} />
      {showLabels !== false && width > 42 && height > 18 && (
        <text x={x + 5} y={y + 14} fill="#fff" fontSize={10} style={{ pointerEvents: 'none' }}>{name}</text>
      )}
    </g>
  )
}

function RdTreemap({ data, height = 110, showLabels = true }) {
  const tdata = data.map(d => ({ name: d.label, size: Math.abs(d.value) || 0 }))
  return (
    <ResponsiveContainer width="100%" height={height}>
      <Treemap data={tdata} dataKey="size" nameKey="name" stroke="#0c1525"
               content={<TreemapNode colors={PIE_COLORS} showLabels={showLabels} />} isAnimationActive={false}>
        <Tooltip content={<RdTooltip color={PIE_COLORS[0]} />} />
      </Treemap>
    </ResponsiveContainer>
  )
}

// ── Filtre paneli ───────────────────────────────────────────────────────────────

function RdFilterPanel({ panel, activeFilters, onFilterChange, height }) {
  const [vals, setVals]       = useState([])
  const [loading, setLoading] = useState(false)
  const [err, setErr]         = useState(null)
  const [q, setQ]             = useState('')
  const source = panel.source
  const field  = panel.field
  const fkey   = source + '|' + field
  const selected = (activeFilters && activeFilters[fkey] && activeFilters[fkey].values) || []
  const [collapsed, setCollapsed] = useState(false)

  useEffect(() => {
    if (panel.sourceType !== 'view' || !source || !field) { setVals([]); return }
    setLoading(true); setErr(null)
    const ctrl = new AbortController()
    const sql = `SELECT DISTINCT [${field}] AS [v] FROM [${source}] ORDER BY [${field}]`
    fetch('/api/report/query/inline', {
      method: 'POST', credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ sql, cacheTtlMinutes: 5 }),
      signal: ctrl.signal,
    })
      .then(r => r.json())
      .then(d => { if (d.ok) setVals((d.rows || []).map(r => r[0])); else setErr(d.error || 'Yüklenemedi') })
      .catch(e => { if (e.name !== 'AbortError') setErr(e.message) })
      .finally(() => setLoading(false))
    return () => ctrl.abort()
  }, [panel.sourceType, source, field])

  if (panel.sourceType !== 'view' || !source || !field)
    return <div style={{ padding: '16px 12px', fontSize: 10, color: '#475569', textAlign: 'center', lineHeight: 1.5 }}>Filtre için View modunda bir kaynak ve alan seçin.</div>

  function toggle(v) {
    const sv = String(v)
    const next = selected.includes(sv) ? selected.filter(x => x !== sv) : [...selected, sv]
    onFilterChange && onFilterChange(fkey, { source, field, values: next })
  }
  function clearAll() {
    onFilterChange && onFilterChange(fkey, { source, field, values: [] })
  }

  const ql = q.trim().toLowerCase()
  const filtered = ql ? vals.filter(v => String(v).toLowerCase().includes(ql)) : vals
  const maxH = height === 'full' ? '100%' : (typeof height === 'number' ? Math.max(140, height + 50) : 220)

  return (
    <div className="rd-filterp">
      <button type="button" className="rd-filterp__toggle" onClick={() => setCollapsed(c => !c)} title={collapsed ? 'Filtreyi aç' : 'Filtreyi gizle'}>
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" style={{ width: 12, height: 12, flexShrink: 0 }}>
          <polygon points="22 3 2 3 10 12.46 10 19 14 21 14 12.46 22 3" />
        </svg>
        <span className="rd-filterp__fname">{field}</span>
        {selected.length > 0 && <span className="rd-filterp__count">{selected.length}</span>}
        <svg className={`rd-filterp__chev${collapsed ? '' : ' rd-filterp__chev--open'}`} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" style={{ width: 12, height: 12, flexShrink: 0, marginLeft: 'auto' }}>
          <polyline points="6 9 12 15 18 9" />
        </svg>
      </button>
      {collapsed ? null : (
      <div className="rd-filterp__body" style={{ maxHeight: maxH }}>
      <div className="rd-filterp__bar">
        <input className="rd-filterp__search" placeholder="Ara…" value={q} onChange={e => setQ(e.target.value)} />
        {selected.length > 0 && (
          <button type="button" className="rd-filterp__clear" onClick={clearAll}>Temizle ({selected.length})</button>
        )}
      </div>
      {loading ? (
        <div className="rd-filterp__msg">Yükleniyor…</div>
      ) : err ? (
        <div className="rd-filterp__msg rd-filterp__msg--err">{err}</div>
      ) : filtered.length === 0 ? (
        <div className="rd-filterp__msg">Değer yok</div>
      ) : (
        <div className="rd-filterp__list">
          {filtered.map((v, i) => {
            const sv = String(v)
            const on = selected.includes(sv)
            return (
              <button key={i} type="button" className={`rd-filterp__item${on ? ' rd-filterp__item--on' : ''}`} onClick={() => toggle(v)}>
                <span className={`rd-filterp__check${on ? ' rd-filterp__check--on' : ''}`}>
                  {on && (
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3.5" strokeLinecap="round" style={{ width: 9, height: 9 }}>
                      <polyline points="20 6 9 17 4 12" />
                    </svg>
                  )}
                </span>
                <span className="rd-filterp__label">{sv === '' ? '(boş)' : sv}</span>
              </button>
            )
          })}
        </div>
      )}
      </div>
      )}
    </div>
  )
}

// ── Tablo bicimlendirme ─────────────────────────────────────────────────────────

const FMT_NUMERIC = new Set(['number', 'decimal2', 'currency', 'percent', 'duration'])

const CURRENCY_SYMBOL = { TRY: '₺', USD: '$', EUR: '€' }

function formatDuration(n, unit) {
  let secs = unit === 'sec' ? n : unit === 'hour' ? n * 3600 : n * 60
  const sign = secs < 0 ? '-' : ''
  secs = Math.abs(Math.round(secs))
  const h = Math.floor(secs / 3600)
  const m = Math.floor((secs % 3600) / 60)
  const s = secs % 60
  const parts = []
  if (h) parts.push(h + 'sa')
  if (m) parts.push(m + 'dk')
  if (s || !parts.length) parts.push(s + 'sn')
  return sign + parts.join(' ')
}

function formatCell(val, c) {
  const fmt = c?.format || 'auto'
  if (val == null) return ''
  if (fmt === 'auto' || fmt === 'text') return String(val)

  const n   = Number(val)
  const dec = Number.isFinite(c?.decimals) ? c.decimals : null

  if (fmt === 'number' || fmt === 'decimal2') {
    if (isNaN(n)) return String(val)
    const d = dec != null ? dec : (fmt === 'decimal2' ? 2 : 0)
    return n.toLocaleString('tr-TR', { minimumFractionDigits: d, maximumFractionDigits: d })
  }
  if (fmt === 'currency') {
    if (isNaN(n)) return String(val)
    const d   = dec != null ? dec : 2
    const sym = CURRENCY_SYMBOL[c?.currency || 'TRY'] || '₺'
    return n.toLocaleString('tr-TR', { minimumFractionDigits: d, maximumFractionDigits: d }) + ' ' + sym
  }
  if (fmt === 'percent') {
    if (isNaN(n)) return String(val)
    const d = dec != null ? dec : 0
    return n.toLocaleString('tr-TR', { minimumFractionDigits: d, maximumFractionDigits: Math.max(d, 2) }) + '%'
  }
  if (fmt === 'duration') {
    if (isNaN(n)) return String(val)
    return formatDuration(n, c?.durationUnit || 'min')
  }
  if (fmt === 'date' || fmt === 'datetime') {
    const d = new Date(val)
    if (isNaN(d.getTime())) return String(val)
    return fmt === 'date' ? d.toLocaleDateString('tr-TR') : d.toLocaleString('tr-TR')
  }
  if (fmt === 'bool') {
    const s = String(val).toLowerCase()
    if (s === 'true'  || s === '1') return 'Evet'
    if (s === 'false' || s === '0') return 'Hayır'
    return String(val)
  }
  if (fmt === 'custom') {
    const tpl    = c?.custom || '{}'
    const numStr = isNaN(n) ? String(val) : n.toLocaleString('tr-TR')
    const num2   = isNaN(n) ? String(val) : n.toLocaleString('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
    return tpl.replace(/\{n2\}/g, num2).replace(/\{n\}/g, numStr).replace(/\{\}/g, String(val))
  }
  return String(val)
}

function orderColumns(names, order) {
  if (!order || !order.length) return names
  const inOrder = order.filter(n => names.includes(n))
  const rest    = names.filter(n => !inOrder.includes(n))
  return [...inOrder, ...rest]
}

function aggValues(vals, agg) {
  if (!vals.length) return 0
  if (agg === 'AVG')   return vals.reduce((a, b) => a + b, 0) / vals.length
  if (agg === 'COUNT') return vals.length
  if (agg === 'MIN')   return Math.min(...vals)
  if (agg === 'MAX')   return Math.max(...vals)
  return vals.reduce((a, b) => a + b, 0)   // SUM
}

// Hücre hizalaması: alan ayarındaki align ('auto'/'left'/'center'/'right'); auto → sayısal sağ, diğer sol
function cellAlign(c) {
  if (c.align && c.align !== 'auto') return c.align
  // Otomatik: başlık + hücreler birlikte SOLA hizalı (kod/ID/metin için doğal, aynı sol kenar).
  // Gerçek miktar kolonu için kullanıcı Hizalama → "Sağ" seçebilir.
  return 'left'
}

// Kolon başlığı (th) yazı stili — kullanıcı tanımlı font/renk/kalın/italik/boyut
const HEADER_FONTS = {
  sans:  'system-ui, "Segoe UI", Roboto, sans-serif',
  serif: 'Georgia, "Times New Roman", serif',
  mono:  'ui-monospace, Menlo, Consolas, monospace',
}
function headerStyle(c) {
  const s = {
    color: c.headerColor || '#64748b',
    fontWeight: c.headerBold ? 700 : 500,
    fontStyle: c.headerItalic ? 'italic' : 'normal',
  }
  if (c.headerSize) s.fontSize = c.headerSize + 'px'
  if (HEADER_FONTS[c.headerFont]) s.fontFamily = HEADER_FONTS[c.headerFont]
  return s
}

// Bir kolonun benzersiz değerleri (filtre listesi için) — yüklü veriden, client-side
export function distinctVals(rows, idx) {
  const seen = new Set(), out = []
  for (const r of rows) {
    const key = r[idx] == null ? '' : String(r[idx])
    if (!seen.has(key)) { seen.add(key); out.push(key); if (out.length > 500) break }
  }
  return out.sort((a, b) => a.localeCompare(b, 'tr'))
}

function RdTable({ apiData, maxHeight = 110, fill = false, colConfig, colOrder, sorts, sortField, sortDir, activeFilters }) {
  const { columns, rows } = toTableData(apiData)
  const cfg  = colConfig || {}
  const cols = orderColumns(columns, colOrder)
    .map(name => ({ name, idx: columns.indexOf(name), ...(cfg[name] || {}) }))
    .filter(c => c.idx >= 0 && c.visible !== false)   // "Raporda görünsün" kapalı olanlar hariç

  // Sayfa filtreleri (sol filtre rayı) → tabloda mevcut alanlar için client-side süzme
  const appliedFilters = Object.values(activeFilters || {}).filter(f => f && f.field && f.values && f.values.length)

  // Satır sıralaması (ORDER BY) — çoklu alan; eski sortField/sortDir geriye uyumlu
  const sortList = (Array.isArray(sorts) && sorts.length)
    ? sorts.filter(s => s && s.field)
    : (sortField ? [{ field: sortField, dir: sortDir }] : [])
  const sortInfo = {}
  sortList.forEach((s, i) => { sortInfo[s.field] = { dir: s.dir, ord: i + 1 } })

  // Önce filtre, sonra sıralama
  let dataRows = rows
  if (appliedFilters.length) {
    dataRows = dataRows.filter(row => appliedFilters.every(f => {
      const ci = columns.indexOf(f.field)
      return ci < 0 || f.values.includes(String(row[ci] ?? ''))
    }))
  }
  if (sortList.length) {
    const keys = sortList
      .map(s => ({ si: columns.indexOf(s.field), desc: s.dir === 'desc' }))
      .filter(k => k.si >= 0)
    if (keys.length) {
      dataRows = [...dataRows].sort((a, b) => {
        for (const k of keys) {
          const av = a[k.si], bv = b[k.si]
          const an = Number(av), bn = Number(bv)
          const cmp = (!isNaN(an) && !isNaN(bn) && av !== '' && bv !== '' && av != null && bv != null)
            ? an - bn
            : String(av ?? '').localeCompare(String(bv ?? ''), 'tr')
          if (cmp !== 0) return k.desc ? -cmp : cmp
        }
        return 0
      })
    }
  }

  // Alt toplam satırı — yalnız sayısal kolonlar (string toplamı yok)
  const totalCols = cols.filter(c => c.total && (FMT_NUMERIC.has(c.format) || colIsNumeric(dataRows, c.idx)))
  const totals = {}
  totalCols.forEach(c => {
    const vals = dataRows.map(r => Number(r[c.idx])).filter(v => !isNaN(v))
    totals[c.name] = aggValues(vals, c.totalAgg || 'SUM')
  })

  const visibleRows = fill ? 500 : (typeof maxHeight === 'number' && maxHeight > 150) ? 16 : 8

  if (cols.length === 0) {
    return <div style={{ height: 56, display: 'flex', alignItems: 'center', justifyContent: 'center', color: '#4a5568', fontSize: 10 }}>Raporda görünen alan yok</div>
  }

  return (
    <div style={{ maxHeight, height: fill ? '100%' : undefined, overflowY: 'auto', fontSize: 9, color: '#94a3b8' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr>
              {cols.map(c => (
                <th key={c.name} style={{ position: 'sticky', top: 0, textAlign: cellAlign(c), padding: '2px 6px', background: '#111827', borderBottom: '1px solid rgba(255,255,255,.06)', whiteSpace: 'nowrap', ...headerStyle(c) }}>
                  {c.label || c.name}{sortInfo[c.name] ? (sortInfo[c.name].dir === 'desc' ? ' ↓' : ' ↑') + (sortList.length > 1 ? sortInfo[c.name].ord : '') : ''}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {dataRows.slice(0, visibleRows).map((row, ri) => (
              <tr key={ri} style={{ background: ri % 2 === 0 ? 'rgba(255,255,255,.025)' : 'transparent' }}>
                {cols.map(c => (
                  <td key={c.name} style={{ padding: '2px 6px', color: '#e2e8f0', textAlign: cellAlign(c), whiteSpace: 'nowrap' }}>
                    {formatCell(row[c.idx], c)}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
          {totalCols.length > 0 && (
            <tfoot>
              <tr style={{ background: 'rgba(99,102,241,.1)' }}>
                {cols.map((c, ci) => (
                  <td key={c.name} style={{ position: 'sticky', bottom: 0, background: '#0f1830', padding: '3px 6px', color: '#a5b4fc', fontWeight: 600, textAlign: c.total ? cellAlign(c) : (ci === 0 ? 'left' : cellAlign(c)), whiteSpace: 'nowrap', borderTop: '1px solid rgba(99,102,241,.3)' }}>
                    {c.total ? formatCell(totals[c.name], c) : (ci === 0 ? 'Toplam' : '')}
                  </td>
                ))}
              </tr>
            </tfoot>
          )}
        </table>
    </div>
  )
}

// ── Pivot tablo ─────────────────────────────────────────────────────────────────

function pivotHeadStyle(align) {
  return { position: 'sticky', top: 0, textAlign: align, padding: '3px 7px', color: '#64748b', background: '#111827', borderBottom: '1px solid rgba(255,255,255,.08)', fontWeight: 600, whiteSpace: 'nowrap' }
}
function pivotCellStyle(align, color, bold) {
  return { textAlign: align, padding: '2px 7px', color, whiteSpace: 'nowrap', fontWeight: bold ? 600 : 400 }
}

// ── Çok-alanlı pivot (Excel tarzı) ──────────────────────────────────────────────
export const PIVOT_AGGS = [
  ['sum', 'Topla'], ['count', 'Say'], ['avg', 'Ortalama'], ['min', 'En Az'], ['max', 'En Çok'],
]
const PIVOT_AGG_LABEL = { sum: 'Topla', count: 'Say', avg: 'Ortalama', min: 'En Az', max: 'En Çok' }
const PIVOT_COL_CAP = 60
const PIVOT_SEP = ''   // composite anahtar ayracı (veride geçmez)

function pivotToNum(v) {
  if (v == null || v === '') return null
  if (typeof v === 'number') return isNaN(v) ? null : v
  const s = String(v).trim()
  if (!/^-?\d+([.,]\d+)?$/.test(s)) return null
  const n = parseFloat(s.replace(',', '.'))
  return isNaN(n) ? null : n
}

function applyPivotAgg(arr, agg) {
  if (!arr || !arr.length) return null
  if (agg === 'count') return arr.length
  if (agg === 'avg')   return arr.reduce((a, b) => a + b, 0) / arr.length
  if (agg === 'min')   return Math.min(...arr)
  if (agg === 'max')   return Math.max(...arr)
  return arr.reduce((a, b) => a + b, 0)   // sum
}

// Panel'den pivot config'i türet (yeni alanlar; yoksa legacy rowField/colField/measure'dan migrasyon)
export function ensurePivotCfg(panel) {
  if (Array.isArray(panel.pivotRows) || Array.isArray(panel.pivotCols) || Array.isArray(panel.pivotValues)) {
    return {
      rows:   panel.pivotRows || [],
      cols:   panel.pivotCols || [],
      values: (panel.pivotValues || []).filter(v => v && v.field),
    }
  }
  return {
    rows:   panel.rowField ? [panel.rowField] : [],
    cols:   panel.colField ? [panel.colField] : [],
    values: panel.measure  ? [{ field: panel.measure, agg: String(panel.aggregate || 'sum').toLowerCase() }] : [],
  }
}

// Ham veri ({columns, rows}) + config → pivot modeli (composite satır/sütun anahtarları, değer-başına özet).
// Alt/genel toplamlar HAM değerlerden hesaplanır (Excel gibi: ortalama, ortalamaların ortalaması değildir).
function computePivot(data, cfg) {
  const cols = data?.columns || []
  const rows = data?.rows || []
  const ix = n => cols.indexOf(n)
  const rIdx = (cfg.rows || []).map(ix).filter(i => i >= 0)
  const cIdx = (cfg.cols || []).map(ix).filter(i => i >= 0)
  const values = (cfg.values || [])
    .map(v => ({ field: v.field, agg: v.agg || 'sum', i: ix(v.field) }))
    .filter(v => v.i >= 0)
    .map(v => ({ ...v, label: `${PIVOT_AGG_LABEL[v.agg] || v.agg} · ${v.field}` }))
  if (!rIdx.length || !values.length) return null

  const SEP = ''
  const disp = v => (v == null || v === '') ? '(boş)' : String(v)
  const rowMap = new Map(), colMap = new Map()
  const cell = new Map(), rowAll = new Map(), colAll = new Map()
  const grand = values.map(() => [])

  const push = (arr, vi, raw) => {
    if (values[vi].agg === 'count') { if (raw != null && raw !== '') arr.push(1) }
    else { const n = pivotToNum(raw); if (n != null) arr.push(n) }
  }

  rows.forEach(rw => {
    const rParts = rIdx.map(i => disp(rw[i]))
    const rKey = rParts.join(SEP)
    if (!rowMap.has(rKey)) rowMap.set(rKey, rParts)
    const cParts = cIdx.map(i => disp(rw[i]))
    const cKey = cIdx.length ? cParts.join(SEP) : ''
    if (cIdx.length && !colMap.has(cKey)) colMap.set(cKey, cParts)

    if (!cell.has(rKey)) cell.set(rKey, new Map())
    const cm = cell.get(rKey)
    if (!cm.has(cKey)) cm.set(cKey, values.map(() => []))
    if (!rowAll.has(rKey)) rowAll.set(rKey, values.map(() => []))
    if (cIdx.length && !colAll.has(cKey)) colAll.set(cKey, values.map(() => []))

    values.forEach((v, vi) => {
      const raw = rw[v.i]
      push(cm.get(cKey)[vi], vi, raw)
      push(rowAll.get(rKey)[vi], vi, raw)
      if (cIdx.length) push(colAll.get(cKey)[vi], vi, raw)
      push(grand[vi], vi, raw)
    })
  })

  const cmp = (a, b) => {
    const L = Math.max(a.length, b.length)
    for (let k = 0; k < L; k++) {
      const c = String(a[k] ?? '').localeCompare(String(b[k] ?? ''), 'tr', { numeric: true })
      if (c) return c
    }
    return 0
  }
  const rowKeys = [...rowMap.entries()].map(([key, parts]) => ({ key, parts })).sort((A, B) => cmp(A.parts, B.parts))
  let colKeys = [...colMap.entries()].map(([key, parts]) => ({ key, parts })).sort((A, B) => cmp(A.parts, B.parts))
  let truncated = false
  if (colKeys.length > PIVOT_COL_CAP) { colKeys = colKeys.slice(0, PIVOT_COL_CAP); truncated = true }

  const aggOf = (map, key, vi) => { const e = map.get(key); return e ? applyPivotAgg(e[vi], values[vi].agg) : null }

  return {
    rowFields: rIdx.map(i => cols[i]),
    colFields: cIdx.map(i => cols[i]),
    values, rowKeys, colKeys, truncated,
    cell: (rKey, cKey, vi) => { const cm = cell.get(rKey); const e = cm && cm.get(cKey); return e ? applyPivotAgg(e[vi], values[vi].agg) : null },
    rowTotal: (rKey, vi) => aggOf(rowAll, rKey, vi),
    colTotal: (cKey, vi) => aggOf(colAll, cKey, vi),
    grand: vi => applyPivotAgg(grand[vi], values[vi].agg),
  }
}

function RdPivotEx({ pivot, maxHeight = 110, fill = false, showTotals = true }) {
  const emptyMsg = m => <div style={{ height: 56, display: 'flex', alignItems: 'center', justifyContent: 'center', color: '#4a5568', fontSize: 10, textAlign: 'center', padding: '0 8px' }}>{m}</div>
  if (!pivot) return emptyMsg('Pivot için en az bir Satır ve bir Değer alanı seçin')
  const { rowFields, colFields, values, rowKeys, colKeys, truncated } = pivot
  if (!rowKeys.length) return emptyMsg('Veri yok')

  const hasCols = colFields.length > 0
  const colList = hasCols ? colKeys : [{ key: '', parts: [] }]
  const V = values.length
  const fmt = n => n == null ? '' : (typeof n === 'number' ? n.toLocaleString('tr-TR', { maximumFractionDigits: 2 }) : String(n))

  const leaves = []
  colList.forEach(ck => values.forEach((v, vi) => leaves.push({ ck, vi })))

  const groupsAtLevel = level => {
    const out = []
    let i = 0
    while (i < colList.length) {
      const pref = colList[i].parts.slice(0, level + 1).join('')
      let j = i
      while (j < colList.length && colList[j].parts.slice(0, level + 1).join('') === pref) j++
      out.push({ label: colList[i].parts[level], span: (j - i) * V })
      i = j
    }
    return out
  }
  const headC = { ...pivotHeadStyle('center'), textAlign: 'center' }
  const bL = c => ({ borderLeft: '1px solid rgba(255,255,255,' + c + ')' })

  return (
    <div style={{ maxHeight, height: fill ? '100%' : undefined, overflow: 'auto', fontSize: 9, color: '#94a3b8' }}>
      <table style={{ width: '100%', borderCollapse: 'collapse' }}>
        <thead>
          {hasCols && colFields.map((cf, level) => (
            <tr key={'cl' + level}>
              {level === 0 && <th rowSpan={colFields.length} colSpan={rowFields.length} style={pivotHeadStyle('left')} />}
              {groupsAtLevel(level).map((g, gi) => (
                <th key={gi} colSpan={g.span} style={{ ...headC, ...bL('.06') }}>{g.label}</th>
              ))}
              {level === 0 && showTotals && <th rowSpan={colFields.length} colSpan={V} style={{ ...headC, ...bL('.14') }}>Genel Toplam</th>}
            </tr>
          ))}
          <tr>
            {rowFields.map((f, fi) => <th key={'rf' + fi} style={pivotHeadStyle('left')}>{f}</th>)}
            {leaves.map((lf, li) => <th key={'lf' + li} style={pivotHeadStyle('right')}>{values[lf.vi].label}</th>)}
            {showTotals && hasCols && values.map((v, vi) => <th key={'tt' + vi} style={{ ...pivotHeadStyle('right'), ...(vi === 0 ? bL('.14') : {}) }}>{V > 1 ? v.label : 'Toplam'}</th>)}
          </tr>
        </thead>
        <tbody>
          {rowKeys.map((rk, ri) => (
            <tr key={rk.key} style={{ background: ri % 2 === 0 ? 'rgba(255,255,255,.025)' : 'transparent' }}>
              {rk.parts.map((p, pi) => <td key={pi} style={pivotCellStyle('left', '#e2e8f0', pi < rowFields.length - 1)}>{p}</td>)}
              {leaves.map((lf, li) => <td key={li} style={pivotCellStyle('right', '#cbd5e1')}>{fmt(pivot.cell(rk.key, lf.ck.key, lf.vi))}</td>)}
              {showTotals && hasCols && values.map((v, vi) => <td key={'rt' + vi} style={{ ...pivotCellStyle('right', '#f1f5f9', true), ...(vi === 0 ? bL('.14') : {}) }}>{fmt(pivot.rowTotal(rk.key, vi))}</td>)}
            </tr>
          ))}
          {showTotals && (
            <tr style={{ background: 'rgba(99,102,241,.08)' }}>
              <td colSpan={rowFields.length} style={pivotCellStyle('left', '#a5b4fc', true)}>Genel Toplam</td>
              {leaves.map((lf, li) => <td key={li} style={pivotCellStyle('right', '#a5b4fc', true)}>{fmt(hasCols ? pivot.colTotal(lf.ck.key, lf.vi) : pivot.grand(lf.vi))}</td>)}
              {hasCols && values.map((v, vi) => <td key={'gt' + vi} style={{ ...pivotCellStyle('right', '#a5b4fc', true), ...(vi === 0 ? bL('.14') : {}) }}>{fmt(pivot.grand(vi))}</td>)}
            </tr>
          )}
        </tbody>
      </table>
      {truncated && <div style={{ padding: '4px 7px', fontSize: 9, color: '#f59e0b', fontStyle: 'italic' }}>Sütun sayısı {PIVOT_COL_CAP}'a sınırlandı (çok fazla farklı değer var).</div>}
    </div>
  )
}

// ── Ana component ─────────────────────────────────────────────────────────────

export default function PanelChart({ panel, chartHeight = 110, onColumns, onData, activeFilters, onFilterChange, viewFields }) {
  const { data, loading, error } = useReportData(panel, activeFilters, viewFields)
  const color = panel.color || '#6366f1'

  // Export için veriyi yukarı bildir (yalnız değiştiğinde)
  const lastDataRef = useRef(null)
  useEffect(() => {
    if (!onData || !data) return
    if (lastDataRef.current === data) return
    lastDataRef.current = data
    onData({ columns: data.columns || [], rows: data.rows || [] })
  }, [onData, data])

  // Kolonlari kesfet → sidebar kolon editoru + (kayitli/SQL) alan secicilerini besler
  const lastColsRef = useRef(null)
  useEffect(() => {
    if (!onColumns) return
    const cols = data?.columns
    if (!cols || !cols.length) return
    const key = cols.join('|')
    if (lastColsRef.current === key) return
    lastColsRef.current = key
    onColumns(cols, detectNumericCols(data))
  }, [onColumns, data])

  const isFull     = chartHeight === 'full'
  const px         = isFull ? 360 : chartHeight
  const containerH = isFull ? '100%' : chartHeight
  const statH      = isFull ? '100%' : Math.max(56, Math.round(px * 0.51))
  const bigStat    = isFull || px > 100

  const fullWrap = (children) => isFull
    ? <div style={{ height: '100%', minHeight: 0 }}>{children}</div>
    : children

  // Filtre paneli kendi verisini ceker (useReportData'dan bagimsiz)
  if (panel.type === 'filter')
    return fullWrap(<RdFilterPanel panel={panel} activeFilters={activeFilters} onFilterChange={onFilterChange} height={containerH} />)

  // Metin kartı: veri bağımsız, her zaman içeriği göster
  if (panel.type === 'text')
    return fullWrap(<RdTextCard panel={panel} height={containerH} fill={isFull} />)

  if (loading) {
    return fullWrap(
      <div style={{ height: statH, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
        <svg className="rd-spin" viewBox="0 0 24 24" fill="none" stroke={color} strokeWidth="2.5" style={{ width: 18, height: 18, opacity: .6 }}>
          <path d="M21 12a9 9 0 1 1-6.219-8.56" />
        </svg>
      </div>
    )
  }

  if (error) {
    return fullWrap(
      <div style={{ height: statH, display: 'flex', alignItems: 'center', justifyContent: 'center', padding: '0 8px' }}>
        <span style={{ fontSize: 9, color: '#ef4444', textAlign: 'center', lineHeight: 1.4 }}>{error}</span>
      </div>
    )
  }

  if (!data) return fullWrap(<ChartPreview type={panel.type} color={color} height={containerH} />)

  // Kayıtlı/SQL kaynak: kolonlar ham sorgudan gelir → kullanıcı seçtiği alanları kullan
  const isRaw   = panel.sourceType === 'saved' || panel.sourceType === 'sql'
  const rawAgg  = panel.rawAgg || 'SUM'

  if (panel.type === 'stat')  return fullWrap(<RdStatCard apiData={data} height={statH} big={bigStat} prefix={panel.prefix || ''} suffix={panel.suffix || ''} decimals={panel.decimals} valueField={isRaw ? panel.valueField : null} valueAgg={rawAgg} />)
  if (panel.type === 'gauge') return fullWrap(<RdGauge apiData={data} color={color} height={containerH} min={panel.gaugeMin} max={panel.gaugeMax} suffix={panel.suffix || ''} valueField={isRaw ? panel.valueField : null} valueAgg={rawAgg} />)
  if (panel.type === 'table') return fullWrap(<RdTable apiData={data} maxHeight={containerH} fill={isFull} colConfig={panel.columns} colOrder={panel.columnOrder} sorts={panel.sorts} sortField={panel.sortField} sortDir={panel.sortDir} activeFilters={activeFilters} />)
  if (panel.type === 'pivot') {
    const pv = computePivot(data, ensurePivotCfg(panel))
    return fullWrap(<RdPivotEx pivot={pv} maxHeight={containerH} fill={isFull} showTotals={panel.showTotals !== false} />)
  }

  // Hedef göstergesi: tek değer + hedef marker
  if (panel.type === 'bullet')
    return fullWrap(<RdBulletChart apiData={data} color={color} height={containerH} fill={isFull}
                      min={panel.gaugeMin} max={panel.gaugeMax} bulletTarget={panel.bulletTarget}
                      valueField={isRaw ? panel.valueField : null} valueAgg={rawAgg} />)

  // Isı haritası: matris grid
  if (panel.type === 'heatmap')
    return fullWrap(<RdHeatmap apiData={data} color={color} height={containerH} fill={isFull}
                      labelField={isRaw ? panel.labelField : null}
                      seriesField={isRaw ? panel.seriesField : null}
                      valueField={isRaw ? panel.valueField : null} />)

  // Kombi: bar + çizgi (toComboData ile)
  if (panel.type === 'combo') {
    const comboData = toComboData(
      data,
      isRaw ? panel.labelField : null,
      isRaw ? panel.valueField : null,
      isRaw ? panel.valueField2 : null,
      isRaw ? rawAgg : null,
      isRaw ? (panel.rawAgg2 || rawAgg) : null,
    )
    if (!comboData.length) return fullWrap(<ChartPreview type="combo" color={color} height={containerH} />)
    return fullWrap(<RdComboChart data={comboData} color={color} color2={panel.color2 || '#10b981'}
                      thickness={panel.thickness ?? 2} height={containerH}
                      showValues={!!panel.showValues} curve={panel.curve !== false} />)
  }

  // %100 Yığılmış bar
  if (panel.type === 'stacked100') {
    const stacked = toStacked100Data(
      data,
      isRaw ? panel.labelField : null,
      isRaw ? panel.seriesField : null,
      isRaw ? panel.valueField : null,
    )
    if (!stacked.data.length) return fullWrap(<ChartPreview type="stacked100" color={color} height={containerH} />)
    return fullWrap(<RdStacked100Chart stacked={stacked} height={containerH} />)
  }

  // Dağılım grafiği
  if (panel.type === 'scatter') {
    const sd = isRaw
      ? toScatterData(data, panel.xField, panel.yField, panel.labelField)
      : (data.rows || []).map(r => ({
          label: r[0] != null ? String(r[0]) : undefined,
          x: r[1] != null ? Number(r[1]) : 0,
          y: r[2] != null ? Number(r[2]) : 0,
        })).filter(d => !isNaN(d.x) && !isNaN(d.y))
    if (!sd.length) return fullWrap(<ChartPreview type="scatter" color={color} height={containerH} />)
    return fullWrap(<RdScatterChart data={sd} color={color} height={containerH}
                      xLabel={isRaw ? (panel.xField || 'X') : (panel.metric || 'X')}
                      yLabel={isRaw ? (panel.yField || 'Y') : (panel.metric2 || 'Y')} />)
  }

  const chartData = toChartData(
    data,
    isRaw ? panel.labelField : null,
    isRaw ? panel.valueField : null,
    isRaw ? rawAgg : null,
  )
  if (!chartData.length) return fullWrap(<ChartPreview type={panel.type} color={color} height={containerH} />)

  if (panel.type === 'bar')       return fullWrap(<RdBarChart data={chartData} color={color} height={containerH} horizontal={!!panel.horizontal} showValues={!!panel.showValues} />)
  if (panel.type === 'pie')       return fullWrap(<RdPieChart data={chartData} color={color} height={containerH} radiusPx={px} donut={panel.donut !== false} showLabels={!!panel.showLabels} showPercent={!!panel.showPercent} />)
  if (panel.type === 'area')      return fullWrap(<RdAreaChart data={chartData} color={color} thickness={panel.thickness} height={containerH} curve={panel.curve !== false} fillOpacity={panel.fillOpacity != null ? panel.fillOpacity : 0.25} dots={!!panel.dots} />)
  if (panel.type === 'treemap')   return fullWrap(<RdTreemap data={chartData} height={containerH} showLabels={panel.showLabels !== false} />)
  if (panel.type === 'funnel')    return fullWrap(<RdFunnelChart data={chartData} color={color} height={containerH} />)
  if (panel.type === 'waterfall') return fullWrap(<RdWaterfallChart data={chartData} height={containerH} />)
  if (panel.type === 'radar')     return fullWrap(<RdRadarChart data={chartData} color={color} height={containerH} fillOpacity={panel.fillOpacity ?? 0.25} />)
  return fullWrap(<RdLineChart data={chartData} color={color} thickness={panel.thickness} height={containerH} curve={panel.curve !== false} dots={!!panel.dots} />)
}
