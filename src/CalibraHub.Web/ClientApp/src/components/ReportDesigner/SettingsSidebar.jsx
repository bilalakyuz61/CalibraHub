import React, { useState } from 'react'
import {
  DndContext, pointerWithin, closestCenter, PointerSensor, TouchSensor, useSensor, useSensors,
} from '@dnd-kit/core'
import {
  arrayMove, SortableContext, verticalListSortingStrategy, useSortable,
} from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'

/* İmleç-tabanlı çakışma: yalnızca imlecin İÇİNDE olduğu satır hedef olur
   (merkez-tabanlı closestCenter'a göre çok daha az hassas/erken tetiklenir).
   İmleç boşluktaysa en yakın merkeze düş (kenarlarda boşluk kalmasın). */
function colCollision(args) {
  const within = pointerWithin(args)
  return within.length ? within : closestCenter(args)
}

const SIZE_OPTIONS = [
  { k: 1, label: '¼' },
  { k: 2, label: '½' },
  { k: 3, label: '¾' },
  { k: 4, label: 'Tam' },
]

const HEIGHT_OPTIONS = [
  { k: 'normal', label: 'Normal', bar: 6 },
  { k: 'tall',   label: 'Yüksek', bar: 11 },
  { k: 'full',   label: 'Tam',    bar: 16 },
]

// Serbest ızgara: genişlik 12 kolon (¼=3 … Tam=12), yükseklik satır birimi
const WIDTH_COLS  = { 1: 3, 2: 6, 3: 9, 4: 12 }
const HEIGHT_ROWS = { normal: 7, tall: 11, full: 16 }

const FORMAT_OPTIONS = [
  { k: 'auto',     label: 'Otomatik' },
  { k: 'text',     label: 'Metin' },
  { k: 'number',   label: 'Sayı' },
  { k: 'currency', label: 'Para' },
  { k: 'percent',  label: 'Yüzde' },
  { k: 'date',     label: 'Tarih' },
  { k: 'datetime', label: 'Tarih + Saat' },
  { k: 'duration', label: 'Süre' },
  { k: 'bool',     label: 'Evet / Hayır' },
  { k: 'custom',   label: 'Özel' },
]

const AGG_OPTIONS = [
  ['SUM', 'Toplam (SUM)'],
  ['AVG', 'Ortalama (AVG)'],
  ['COUNT', 'Sayı (COUNT)'],
  ['COUNT_DISTINCT', 'Tekil Sayı (COUNT DISTINCT)'],
  ['MIN', 'En Küçük (MIN)'],
  ['MAX', 'En Büyük (MAX)'],
]

// Kayıtlı/SQL kaynakta istemci-taraflı (client-side) gruplama hesaplamaları
const RAW_AGG_OPTIONS = [
  ['SUM', 'Toplam'],
  ['AVG', 'Ortalama'],
  ['COUNT', 'Sayı'],
  ['MIN', 'En küçük'],
  ['MAX', 'En büyük'],
]

// Toplam alınabilen (sayısal) biçimler — string biçimlerde alt toplam yok
const NUMERIC_FORMATS = new Set(['number', 'decimal2', 'currency', 'percent', 'duration'])

// Hangi türlerde "Görünüm" bölümü/renk gösterilsin
const APPEARANCE_TYPES = ['line', 'area', 'bar', 'pie', 'stat', 'gauge', 'treemap']
const COLOR_TYPES      = ['line', 'area', 'bar', 'pie', 'gauge']

function TypeBadge({ sqlType }) {
  if (!sqlType) return null
  const short = sqlType.length > 7 ? sqlType.slice(0, 6) + '…' : sqlType
  const isNum = /^(int|bigint|decimal|numeric|float|real|money|small)/i.test(sqlType)
  const isTime = /^(date|datetime)/i.test(sqlType)
  const color = isTime ? '#818cf8' : isNum ? '#10b981' : '#64748b'
  return (
    <span style={{
      fontSize: 9, fontFamily: 'monospace', color,
      background: `${color}18`, border: `1px solid ${color}28`,
      borderRadius: 3, padding: '1px 5px', letterSpacing: '.02em',
    }}>
      {short}
    </span>
  )
}

const EyeOnIcon = (
  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" style={{ width: 13, height: 13 }}>
    <path d="M2 12s3-7 10-7 10 7 10 7-3 7-10 7-10-7-10-7Z" /><circle cx="12" cy="12" r="3" />
  </svg>
)
const EyeOffIcon = (
  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" style={{ width: 13, height: 13 }}>
    <path d="M9.88 9.88a3 3 0 1 0 4.24 4.24" />
    <path d="M10.73 5.08A10.43 10.43 0 0 1 12 5c7 0 10 7 10 7a13.16 13.16 0 0 1-1.67 2.68" />
    <path d="M6.61 6.61A13.526 13.526 0 0 0 2 12s3 7 10 7a9.74 9.74 0 0 0 5.39-1.61" />
    <line x1="2" y1="2" x2="22" y2="22" />
  </svg>
)
const FilterIcon = (
  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" style={{ width: 13, height: 13 }}>
    <polygon points="22 3 2 3 10 12.46 10 19 14 21 14 12.46 22 3" />
  </svg>
)

/* Tek kolon satırı — @dnd-kit ile yumuşak sürükle-sırala */
function SortableColRow({ id, cfg, expanded, onSetCol, onToggleExpand, detail }) {
  const sortable = useSortable({ id })
  const style = {
    transform:  CSS.Transform.toString(sortable.transform),
    transition: sortable.transition,
    zIndex:     sortable.isDragging ? 40 : 1,
    position:   'relative',
  }
  const visible = cfg.visible !== false
  return (
    <div
      ref={sortable.setNodeRef}
      style={style}
      className={`rd-col-item${sortable.isDragging ? ' rd-col-item--drag' : ''}`}
    >
      <div className={`rd-col-row${visible ? '' : ' rd-col-row--off'}`}>
        <span
          className="rd-col-grip"
          {...sortable.attributes}
          {...sortable.listeners}
          title="Sürükleyerek sırala"
        >
          <svg viewBox="0 0 24 24" width="12" height="12" fill="currentColor">
            <circle cx="9" cy="6" r="1.4" /><circle cx="15" cy="6" r="1.4" />
            <circle cx="9" cy="12" r="1.4" /><circle cx="15" cy="12" r="1.4" />
            <circle cx="9" cy="18" r="1.4" /><circle cx="15" cy="18" r="1.4" />
          </svg>
        </span>
        <button type="button" className={`rd-col-tog${visible ? ' rd-col-tog--on' : ''}`} title="Raporda görünsün" onClick={() => onSetCol(id, { visible: !visible })}>
          {visible ? EyeOnIcon : EyeOffIcon}
        </button>
        <button type="button" className={`rd-col-tog${cfg.filter ? ' rd-col-tog--on' : ''}`} title="Filtrede görünsün" onClick={() => onSetCol(id, { filter: !cfg.filter })}>
          {FilterIcon}
        </button>
        <input
          className="rd-col-label"
          value={cfg.label ?? ''}
          placeholder={id}
          onChange={e => onSetCol(id, { label: e.target.value })}
        />
        <button
          type="button"
          className={`rd-col-exp${expanded ? ' rd-col-exp--on' : ''}`}
          title="Alan ayarları"
          onClick={() => onToggleExpand(id)}
        >
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" style={{ width: 12, height: 12 }}>
            <polyline points="6 9 12 15 18 9" />
          </svg>
        </button>
      </div>
      {expanded && detail && <div className="rd-col-detail">{detail}</div>}
    </div>
  )
}

/* Açılır-kapanır grup başlığı (akordeon) */
function GroupHead({ title, open, badge, onToggle }) {
  return (
    <button type="button" className={`rd-group-head${open ? ' rd-group-head--open' : ''}`} onClick={onToggle}>
      <svg className="rd-group-chev" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round">
        <polyline points="9 18 15 12 9 6" />
      </svg>
      <span className="rd-group-title">{title}</span>
      {badge != null && badge !== '' && badge !== 0 && <span className="rd-group-badge">{badge}</span>}
    </button>
  )
}

// ── Çok-alanlı pivot alan paneli (Excel tarzı: Satırlar / Sütunlar / Değerler) ──
const PIVOT_AGG_OPTS = [['sum', 'Topla'], ['count', 'Say'], ['avg', 'Ortalama'], ['min', 'En Az'], ['max', 'En Çok']]

// settings → {rows, cols, values}; yeni alanlar yoksa legacy rowField/colField/measure'dan migrasyon
function pvGet(s) {
  if (Array.isArray(s.pivotRows) || Array.isArray(s.pivotCols) || Array.isArray(s.pivotValues))
    return { rows: s.pivotRows || [], cols: s.pivotCols || [], values: (s.pivotValues || []).filter(v => v && v.field) }
  return {
    rows:   s.rowField ? [s.rowField] : [],
    cols:   s.colField ? [s.colField] : [],
    values: s.measure  ? [{ field: s.measure, agg: String(s.aggregate || 'sum').toLowerCase() }] : [],
  }
}

function PivotZone({ title, hint, fields, opts, onAdd, onRemove }) {
  const used = new Set(fields)
  const avail = opts.filter(o => !used.has(o.value))
  return (
    <div className="rd-pivot-zone">
      <div className="rd-pivot-zone__head">{title}{hint && <span className="rd-label__val">{hint}</span>}</div>
      {fields.length === 0 && <div className="rd-pivot-zone__empty">Alan ekleyin</div>}
      {fields.map((f, i) => {
        const o = opts.find(x => x.value === f)
        return (
          <div key={f + i} className="rd-pivot-chip">
            <span className="rd-pivot-chip__lbl" title={(o && o.label) || f}>{(o && o.label) || f}</span>
            <button type="button" className="rd-pivot-chip__x" onClick={() => onRemove(i)} title="Kaldır">×</button>
          </div>
        )
      })}
      {avail.length > 0 && (
        <select className="rd-sel rd-pivot-add" value="" onChange={e => { if (e.target.value) onAdd(e.target.value) }}>
          <option value="">+ Alan ekle…</option>
          {avail.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
        </select>
      )}
    </div>
  )
}

function PivotValuesZone({ values, opts, isNumeric, onAdd, onRemove, onAgg }) {
  return (
    <div className="rd-pivot-zone">
      <div className="rd-pivot-zone__head">Değerler<span className="rd-label__val">özet fonksiyonu</span></div>
      {values.length === 0 && <div className="rd-pivot-zone__empty">Değer alanı ekleyin</div>}
      {values.map((v, i) => {
        const o = opts.find(x => x.value === v.field)
        return (
          <div key={v.field + i} className="rd-pivot-vrow">
            <span className="rd-pivot-chip__lbl" title={(o && o.label) || v.field}>{(o && o.label) || v.field}</span>
            <select className="rd-sel rd-pivot-agg" value={v.agg} onChange={e => onAgg(i, e.target.value)}>
              {PIVOT_AGG_OPTS.map(([val, lbl]) => <option key={val} value={val}>{lbl}</option>)}
            </select>
            <button type="button" className="rd-pivot-chip__x" onClick={() => onRemove(i)} title="Kaldır">×</button>
          </div>
        )
      })}
      {opts.length > 0 && (
        <select className="rd-sel rd-pivot-add" value="" onChange={e => { if (e.target.value) onAdd(e.target.value) }}>
          <option value="">+ Değer ekle…</option>
          {opts.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
        </select>
      )}
    </div>
  )
}

function PivotFieldPane({ cfg, fieldOpts, valueOpts, isNumeric, onChange }) {
  return (
    <div className="rd-pivot-pane">
      <PivotZone title="Satırlar" fields={cfg.rows} opts={fieldOpts}
        onAdd={f => onChange({ ...cfg, rows: [...cfg.rows, f] })}
        onRemove={i => onChange({ ...cfg, rows: cfg.rows.filter((_, k) => k !== i) })} />
      <PivotZone title="Sütunlar" hint="opsiyonel" fields={cfg.cols} opts={fieldOpts}
        onAdd={f => onChange({ ...cfg, cols: [...cfg.cols, f] })}
        onRemove={i => onChange({ ...cfg, cols: cfg.cols.filter((_, k) => k !== i) })} />
      <PivotValuesZone values={cfg.values} opts={valueOpts} isNumeric={isNumeric}
        onAdd={f => onChange({ ...cfg, values: [...cfg.values, { field: f, agg: isNumeric(f) ? 'sum' : 'count' }] })}
        onRemove={i => onChange({ ...cfg, values: cfg.values.filter((_, k) => k !== i) })}
        onAgg={(i, agg) => onChange({ ...cfg, values: cfg.values.map((v, k) => k === i ? { ...v, agg } : v) })} />
    </div>
  )
}

export default function SettingsSidebar({
  open, settings, sources, pageSource, discoveredColumns = [], discoveredNumeric = {}, onChange, onApply, onClose,
  reportTitle, reportGroup, reportDescription, currentPageTitle,
  onReportTitleChange, onReportGroupChange, onReportDescriptionChange, onManageSource,
}) {
  const [openGroups, setOpenGroups] = useState({ cols: true, sort: false, totals: false })
  const toggleGroup = k => setOpenGroups(g => ({ ...g, [k]: !g[k] }))
  const [expandedCol, setExpandedCol] = useState(null)
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 4 } }),
    useSensor(TouchSensor,   { activationConstraint: { delay: 120, tolerance: 5 } })
  )

  const setAndApply = (key, val) => { const next = { ...settings, [key]: val }; onChange(next); onApply(next, { silent: true }) }

  function setCol(name, patch) {
    const cols = { ...(settings.columns || {}) }
    cols[name] = { ...(cols[name] || {}), ...patch }
    const next = { ...settings, columns: cols }
    onChange(next); onApply(next, { silent: true })
  }

  function setColOrder(order) {
    const next = { ...settings, columnOrder: order }
    onChange(next); onApply(next, { silent: true })
  }

  function setLayout(patch) {
    const next = { ...settings, layout: { ...(settings.layout || {}), ...patch } }
    onChange(next); onApply(next, { silent: true })
  }

  // ── Sayfa modu: hiçbir panel seçili değil → Rapor/Sayfa özellikleri ──
  if (!settings) {
    const srcLabel = !pageSource ? '' :
      pageSource.sourceType === 'saved' ? (pageSource.sourceName || `Kayıt #${pageSource.sourceId}`) :
      (pageSource.sourceLabel || pageSource.source || '')
    return (
      <aside className={`rd-sidebar${open ? ' rd-sidebar--open' : ''}`}>
        <div className="rd-sidebar__inner">
          <div className="rd-sidebar__head">
            <span>Rapor Özellikleri</span>
          </div>

          <div className="rd-field">
            <label className="rd-label">Rapor Adı</label>
            <input type="text" className="rd-input" value={reportTitle || ''}
              onChange={e => onReportTitleChange && onReportTitleChange(e.target.value)} placeholder="Rapor adı…" />
          </div>

          <div className="rd-field">
            <label className="rd-label">Grup</label>
            <input type="text" className="rd-input" value={reportGroup || ''}
              onChange={e => onReportGroupChange && onReportGroupChange(e.target.value)} placeholder="Grupsuz" />
          </div>

          <div className="rd-field">
            <label className="rd-label">Açıklama</label>
            <textarea className="rd-input rd-textarea" rows={3} value={reportDescription || ''}
              onChange={e => onReportDescriptionChange && onReportDescriptionChange(e.target.value)}
              placeholder="Bu rapor ne içeriyor? (opsiyonel)" />
          </div>

          <div className="rd-divider" />
          <label className="rd-section-label">Sayfa{currentPageTitle ? ` · ${currentPageTitle}` : ''}</label>

          <div className="rd-field">
            <label className="rd-label">Veri Kaynağı</label>
            <div className="rd-src-display">
              <span className={`rd-src-display__name${srcLabel ? '' : ' rd-src-display__name--empty'}`}>
                {srcLabel || '— Seçilmedi —'}
              </span>
              <button type="button" className="rd-src-display__btn" onClick={() => onManageSource && onManageSource()}>
                {srcLabel ? 'Değiştir' : 'Seç'}
              </button>
            </div>
            <div className="rd-src-hint">Veri bağlantısı üst bardaki <strong>veri</strong> butonundan yönetilir.</div>
          </div>

          <div className="rd-saved-empty" style={{ marginTop: 6 }}>
            Bir panele tıklayarak o panelin ayarlarını düzenleyebilirsiniz.
          </div>
        </div>
      </aside>
    )
  }

  const set       = (key, val) => onChange({ ...settings, [key]: val })
  const setMany   = (patch, apply) => { const next = { ...settings, ...patch }; onChange(next); if (apply) onApply(next, { silent: true }) }
  const src       = sources.find(s => s.name === (pageSource && pageSource.source)) || null
  const hasPageSource = !!(pageSource && (pageSource.source || pageSource.sourceId || pageSource.sqlQuery))

  // Kolon sıralaması: kullanıcı sırası önce, sonra keşfedilen kalanlar
  const orderedColNames = (() => {
    const order   = settings.columnOrder || []
    const inOrder = order.filter(n => discoveredColumns.includes(n))
    const rest    = discoveredColumns.filter(n => !inOrder.includes(n))
    return [...inOrder, ...rest]
  })()

  // Toplamlar yalnız GÖRÜNÜR + SAYISAL kolonlar üzerinden (string toplamı yok)
  const visibleColNames = orderedColNames.filter(n => ((settings.columns || {})[n] || {}).visible !== false)
  const numericColNames = visibleColNames.filter(n => {
    const c = (settings.columns || {})[n] || {}
    return discoveredNumeric[n] || NUMERIC_FORMATS.has(c.format)
  })
  const totalCount      = numericColNames.filter(n => ((settings.columns || {})[n] || {}).total).length

  // Çoklu sıralama — eski tekil sortField/sortDir'i diziye normalize et
  const sorts = Array.isArray(settings.sorts)
    ? settings.sorts
    : (settings.sortField ? [{ field: settings.sortField, dir: settings.sortDir || 'asc' }] : [])
  const sortCount = sorts.filter(s => s && s.field).length

  function applySorts(next) {
    const ns = { ...settings, sorts: next, sortField: undefined, sortDir: undefined }
    onChange(ns); onApply(ns, { silent: true })
  }
  function updateSort(i, patch) { applySorts(sorts.map((s, idx) => idx === i ? { ...s, ...patch } : s)) }
  function removeSort(i)        { applySorts(sorts.filter((_, idx) => idx !== i)) }
  function addSort() {
    const used  = new Set(sorts.map(s => s.field))
    const avail = orderedColNames.find(n => !used.has(n)) || ''
    applySorts([...sorts, { field: avail, dir: 'asc' }])
  }

  function handleColDragEnd(event) {
    const { active, over } = event
    if (!over || active.id === over.id) return
    const oldIdx = orderedColNames.indexOf(active.id)
    const newIdx = orderedColNames.indexOf(over.id)
    if (oldIdx < 0 || newIdx < 0) return
    setColOrder(arrayMove(orderedColNames, oldIdx, newIdx))
  }

  function switchMode(mode) {
    onChange({
      ...settings,
      sourceType:  mode,
      source:      '',
      sourceLabel: '',
      sourceId:    null,
      sourceName:  '',
      metric:      '',
      aggregate:   'SUM',
      group:       '',
      groupIsTime: false,
      sqlQuery:    settings.sqlQuery || '',
    })
  }

  const selectedMetric = src ? (src.metrics || []).find(m => m.value === settings.metric) : null
  const selectedGroup  = src ? (src.groups  || []).find(g => g.value === settings.group)  : null

  // Biçime özel detay ayarları
  function fmtDetail(name, c, fmt) {
    if (fmt === 'number' || fmt === 'percent') {
      return (
        <label className="rd-col-opt">
          <span>Ondalık</span>
          <input
            type="number" min="0" max="4" className="rd-col-num"
            value={Number.isFinite(c.decimals) ? c.decimals : 0}
            onChange={e => setCol(name, { decimals: Math.max(0, Math.min(4, +e.target.value || 0)) })}
          />
        </label>
      )
    }
    if (fmt === 'currency') {
      return (
        <>
          <label className="rd-col-opt">
            <span>Ondalık</span>
            <input
              type="number" min="0" max="4" className="rd-col-num"
              value={Number.isFinite(c.decimals) ? c.decimals : 2}
              onChange={e => setCol(name, { decimals: Math.max(0, Math.min(4, +e.target.value || 0)) })}
            />
          </label>
          <label className="rd-col-opt">
            <span>Sembol</span>
            <select className="rd-col-optsel" value={c.currency || 'TRY'} onChange={e => setCol(name, { currency: e.target.value })}>
              <option value="TRY">₺ TRY</option>
              <option value="USD">$ USD</option>
              <option value="EUR">€ EUR</option>
            </select>
          </label>
        </>
      )
    }
    if (fmt === 'duration') {
      return (
        <label className="rd-col-opt">
          <span>Birim</span>
          <select className="rd-col-optsel" value={c.durationUnit || 'min'} onChange={e => setCol(name, { durationUnit: e.target.value })}>
            <option value="sec">Saniye</option>
            <option value="min">Dakika</option>
            <option value="hour">Saat</option>
          </select>
        </label>
      )
    }
    if (fmt === 'custom') {
      return (
        <div className="rd-col-opt rd-col-opt--col">
          <input
            className="rd-col-tpl"
            value={c.custom || ''}
            placeholder="örn: {} adet"
            onChange={e => setCol(name, { custom: e.target.value })}
          />
          <span className="rd-col-tpl-hint">{'{} = değer · {n} = sayı (binlik) · {n2} = 2 ondalık'}</span>
        </div>
      )
    }
    return <span className="rd-col-noopt">Bu biçim için ek ayar yok.</span>
  }

  // Alan ayarları paneli (Qlik benzeri): Biçim + biçime özel + Hizalama
  function fieldDetail(name, c) {
    const fmt = c.format || 'auto'
    return (
      <>
        <label className="rd-col-opt">
          <span>Biçim</span>
          <select className="rd-col-optsel" value={fmt} onChange={e => setCol(name, { format: e.target.value })}>
            {FORMAT_OPTIONS.map(f => <option key={f.k} value={f.k}>{f.label}</option>)}
          </select>
        </label>
        {fmtDetail(name, c, fmt)}
        <label className="rd-col-opt">
          <span>Hizalama</span>
          <select className="rd-col-optsel" value={c.align || 'auto'} onChange={e => setCol(name, { align: e.target.value })}>
            <option value="auto">Otomatik</option>
            <option value="left">Sol</option>
            <option value="center">Orta</option>
            <option value="right">Sağ</option>
          </select>
        </label>
      </>
    )
  }

  return (
    <aside className={`rd-sidebar${open ? ' rd-sidebar--open' : ''}`}>
      <div className="rd-sidebar__inner">

        <div className="rd-sidebar__head">
          <span>Panel Ayarları</span>
          <button type="button" className="rd-sidebar__close" onClick={onClose} aria-label="Kapat">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" style={{ width: 14, height: 14 }}>
              <path d="M18 6 6 18M6 6l12 12" />
            </svg>
          </button>
        </div>

        {/* Başlık */}
        <div className="rd-field">
          <label className="rd-label">Başlık</label>
          <input
            type="text"
            className="rd-input"
            value={settings.title || ''}
            onChange={e => set('title', e.target.value)}
            placeholder="Panel başlığı…"
          />
        </div>

        {src ? (
          <>
            {settings.type === 'pivot' ? (
              <PivotFieldPane
                cfg={pvGet(settings)}
                fieldOpts={(src?.groups || []).map(g => ({ value: g.value, label: g.label }))}
                valueOpts={(src?.metrics || []).map(m => ({ value: m.value, label: m.label }))}
                isNumeric={() => true}
                onChange={c => setMany({ pivotRows: c.rows, pivotCols: c.cols, pivotValues: c.values }, false)}
              />
            ) : settings.type === 'filter' ? (
              <div className="rd-field">
                <label className="rd-label">Filtre Alanı</label>
                <select className="rd-sel" value={settings.field || ''} onChange={e => set('field', e.target.value)}>
                  <option value="">— Seçin —</option>
                  {(src?.groups || []).map(g => <option key={g.value} value={g.value}>{g.label}</option>)}
                </select>
              </div>
            ) : (
              <>
                <div className="rd-field">
                  <label className="rd-label">
                    {settings.type === 'pie' || settings.type === 'treemap' ? 'Değer' : 'Ölçüm / Metrik (Y)'}
                    {selectedMetric && <TypeBadge sqlType={selectedMetric.sqlType} />}
                  </label>
                  <select className="rd-sel" value={settings.metric || ''} onChange={e => set('metric', e.target.value)}>
                    <option value="">— Seçin —</option>
                    {(src?.metrics || []).map(m => <option key={m.value} value={m.value}>{m.label}</option>)}
                  </select>
                </div>

                {settings.metric && settings.type !== 'table' && (
                  <div className="rd-field">
                    <label className="rd-label">Hesaplama</label>
                    <select className="rd-sel" value={settings.aggregate || 'SUM'} onChange={e => set('aggregate', e.target.value)}>
                      {AGG_OPTIONS.map(([v, l]) => <option key={v} value={v}>{l}</option>)}
                    </select>
                  </div>
                )}

                {settings.type !== 'stat' && settings.type !== 'gauge' && (
                  <div className="rd-field">
                    <label className="rd-label">
                      {settings.type === 'pie' ? 'Dilim (Kategori)' : settings.type === 'treemap' ? 'Kategori' : 'Gruplama / X Ekseni'}
                      {selectedGroup && <TypeBadge sqlType={selectedGroup.sqlType} />}
                      {selectedGroup?.isTime && (
                        <span style={{ fontSize: 9, color: '#818cf8', marginLeft: 2 }}>📅</span>
                      )}
                    </label>
                    <select
                      className="rd-sel"
                      value={settings.group || ''}
                      onChange={e => {
                        const grpDef = (src?.groups || []).find(g => g.value === e.target.value)
                        onChange({ ...settings, group: e.target.value, groupIsTime: grpDef?.isTime ?? false })
                      }}
                    >
                      <option value="">— Seçin —</option>
                      {(src?.groups || []).map(g => (
                        <option key={g.value} value={g.value}>{g.label}</option>
                      ))}
                    </select>
                  </div>
                )}
              </>
            )}
          </>
        ) : hasPageSource ? (
          settings.type === 'table' ? (
            <div className="rd-saved-empty">
              Bu sayfa <strong>kayıtlı/SQL kaynak</strong> kullanıyor. Kolon sıra, başlık ve biçimlerini aşağıdaki <strong>Kolonlar</strong> bölümünden düzenleyin.
            </div>
          ) : settings.type === 'filter' ? (
            <div className="rd-saved-empty">
              Filtre paneli yalnız <strong>View</strong> kaynağıyla çalışır. Üstten bir View kaynağı seçin.
            </div>
          ) : discoveredColumns.length === 0 ? (
            <div className="rd-saved-empty">
              Veri yüklendiğinde alanlar burada listelenir; hangi kolonun kategori/değer olacağını buradan seçeceksiniz.
            </div>
          ) : settings.type === 'pivot' ? (
            <PivotFieldPane
              cfg={pvGet(settings)}
              fieldOpts={discoveredColumns.map(n => ({ value: n, label: n }))}
              valueOpts={discoveredColumns.map(n => ({ value: n, label: n }))}
              isNumeric={f => !!discoveredNumeric[f]}
              onChange={c => setMany({ pivotRows: c.rows, pivotCols: c.cols, pivotValues: c.values }, true)}
            />
          ) : (settings.type === 'stat' || settings.type === 'gauge') ? (
            <>
              <div className="rd-field">
                <label className="rd-label">Değer Alanı</label>
                <select className="rd-sel" value={settings.valueField || ''} onChange={e => setAndApply('valueField', e.target.value)}>
                  <option value="">— İlk sayısal kolon —</option>
                  {discoveredColumns.map(n => <option key={n} value={n}>{n}</option>)}
                </select>
              </div>
              <div className="rd-field">
                <label className="rd-label">Hesaplama</label>
                <select className="rd-sel" value={settings.rawAgg || 'SUM'} onChange={e => setAndApply('rawAgg', e.target.value)}>
                  {RAW_AGG_OPTIONS.map(([v, l]) => <option key={v} value={v}>{l}</option>)}
                </select>
              </div>
            </>
          ) : (
            <>
              <div className="rd-field">
                <label className="rd-label">{settings.type === 'pie' || settings.type === 'treemap' ? 'Kategori' : 'Kategori (X)'}</label>
                <select className="rd-sel" value={settings.labelField || ''} onChange={e => setAndApply('labelField', e.target.value)}>
                  <option value="">— İlk kolon —</option>
                  {discoveredColumns.map(n => <option key={n} value={n}>{n}</option>)}
                </select>
              </div>
              <div className="rd-field">
                <label className="rd-label">{settings.type === 'pie' || settings.type === 'treemap' ? 'Değer' : 'Değer (Y)'}</label>
                <select className="rd-sel" value={settings.valueField || ''} onChange={e => setAndApply('valueField', e.target.value)}>
                  <option value="">— İkinci kolon —</option>
                  {discoveredColumns.map(n => <option key={n} value={n}>{n}</option>)}
                </select>
              </div>
              <div className="rd-field">
                <label className="rd-label">
                  Hesaplama
                  <span className="rd-label__val">kategoriye göre</span>
                </label>
                <select className="rd-sel" value={settings.rawAgg || 'SUM'} onChange={e => setAndApply('rawAgg', e.target.value)}>
                  <option value="NONE">Ham (gruplama yok)</option>
                  {RAW_AGG_OPTIONS.map(([v, l]) => <option key={v} value={v}>{l}</option>)}
                </select>
              </div>
            </>
          )
        ) : (
          <div className="rd-saved-empty">
            Bu sayfanın veri kaynağı seçili değil. Üstteki <strong>Veri Kaynağı</strong> alanından bir kaynak seçin.
          </div>
        )}

        {APPEARANCE_TYPES.includes(settings.type) && (
          <>
            <div className="rd-divider" />
            <label className="rd-section-label">Görünüm</label>

            {COLOR_TYPES.includes(settings.type) && (
              <div className="rd-field">
                <label className="rd-label">Renk</label>
                <div className="rd-colors">
                  {['#6366f1', '#10b981', '#f59e0b', '#ef4444', '#3b82f6', '#8b5cf6', '#ec4899'].map(c => (
                    <button
                      key={c}
                      type="button"
                      aria-label={c}
                      className={`rd-color${settings.color === c ? ' rd-color--on' : ''}`}
                      style={{ background: c, '--ring': c }}
                      onClick={() => setAndApply('color', c)}
                    />
                  ))}
                </div>
              </div>
            )}

            {(settings.type === 'line' || settings.type === 'area') && (
              <div className="rd-field">
                <label className="rd-label">
                  Çizgi Kalınlığı
                  <span className="rd-label__val">{settings.thickness ?? 2}px</span>
                </label>
                <input
                  type="range" min="1" max="6" step="1" className="rd-range"
                  value={settings.thickness ?? 2}
                  onChange={e => setAndApply('thickness', +e.target.value)}
                />
              </div>
            )}

            {(settings.type === 'line' || settings.type === 'area') && (
              <div className="rd-field">
                <label className="rd-label">
                  Eğri (yumuşat)
                  <span className="rd-label__val">{settings.curve !== false ? 'Açık' : 'Kapalı'}</span>
                </label>
                <button type="button" className={`rd-toggle${settings.curve !== false ? ' rd-toggle--on' : ''}`}
                  onClick={() => setAndApply('curve', settings.curve === false)}>
                  <span className="rd-toggle__thumb" />
                </button>
              </div>
            )}

            {settings.type === 'area' && (
              <div className="rd-field">
                <label className="rd-label">
                  Dolgu Opaklığı
                  <span className="rd-label__val">{Math.round((settings.fillOpacity ?? 0.25) * 100)}%</span>
                </label>
                <input
                  type="range" min="0" max="0.6" step="0.05" className="rd-range"
                  value={settings.fillOpacity ?? 0.25}
                  onChange={e => setAndApply('fillOpacity', +e.target.value)}
                />
              </div>
            )}

            {(settings.type === 'line' || settings.type === 'area') && (
              <div className="rd-field">
                <label className="rd-label">
                  Noktaları Göster
                  <span className="rd-label__val">{settings.dots ? 'Açık' : 'Kapalı'}</span>
                </label>
                <button type="button" className={`rd-toggle${settings.dots ? ' rd-toggle--on' : ''}`}
                  onClick={() => setAndApply('dots', !settings.dots)}>
                  <span className="rd-toggle__thumb" />
                </button>
              </div>
            )}

            {settings.type === 'bar' && (
              <div className="rd-field">
                <label className="rd-label">
                  Yatay Çubuklar
                  <span className="rd-label__val">{settings.horizontal ? 'Açık' : 'Kapalı'}</span>
                </label>
                <button type="button" className={`rd-toggle${settings.horizontal ? ' rd-toggle--on' : ''}`}
                  onClick={() => setAndApply('horizontal', !settings.horizontal)}>
                  <span className="rd-toggle__thumb" />
                </button>
              </div>
            )}

            {settings.type === 'bar' && (
              <div className="rd-field">
                <label className="rd-label">
                  Değerleri Göster
                  <span className="rd-label__val">{settings.showValues ? 'Açık' : 'Kapalı'}</span>
                </label>
                <button type="button" className={`rd-toggle${settings.showValues ? ' rd-toggle--on' : ''}`}
                  onClick={() => setAndApply('showValues', !settings.showValues)}>
                  <span className="rd-toggle__thumb" />
                </button>
              </div>
            )}

            {settings.type === 'pie' && (
              <div className="rd-field">
                <label className="rd-label">
                  Halka (Donut)
                  <span className="rd-label__val">{settings.donut !== false ? 'Açık' : 'Kapalı'}</span>
                </label>
                <button type="button" className={`rd-toggle${settings.donut !== false ? ' rd-toggle--on' : ''}`}
                  onClick={() => setAndApply('donut', settings.donut === false)}>
                  <span className="rd-toggle__thumb" />
                </button>
              </div>
            )}

            {settings.type === 'pie' && (
              <div className="rd-field">
                <label className="rd-label">
                  Etiketleri Göster
                  <span className="rd-label__val">{settings.showLabels ? 'Açık' : 'Kapalı'}</span>
                </label>
                <button type="button" className={`rd-toggle${settings.showLabels ? ' rd-toggle--on' : ''}`}
                  onClick={() => setAndApply('showLabels', !settings.showLabels)}>
                  <span className="rd-toggle__thumb" />
                </button>
              </div>
            )}

            {settings.type === 'pie' && (
              <div className="rd-field">
                <label className="rd-label">
                  Yüzde Göster
                  <span className="rd-label__val">{settings.showPercent ? 'Açık' : 'Kapalı'}</span>
                </label>
                <button type="button" className={`rd-toggle${settings.showPercent ? ' rd-toggle--on' : ''}`}
                  onClick={() => setAndApply('showPercent', !settings.showPercent)}>
                  <span className="rd-toggle__thumb" />
                </button>
              </div>
            )}

            {settings.type === 'stat' && (
              <>
                <div className="rd-field">
                  <label className="rd-label">Önek</label>
                  <input className="rd-input" value={settings.prefix || ''} placeholder="örn: ₺"
                    onChange={e => setAndApply('prefix', e.target.value)} />
                </div>
                <div className="rd-field">
                  <label className="rd-label">Sonek</label>
                  <input className="rd-input" value={settings.suffix || ''} placeholder="örn: adet"
                    onChange={e => setAndApply('suffix', e.target.value)} />
                </div>
                <div className="rd-field">
                  <label className="rd-label">Ondalık</label>
                  <input type="number" min="0" max="4" className="rd-input"
                    value={Number.isFinite(settings.decimals) ? settings.decimals : 0}
                    onChange={e => setAndApply('decimals', Math.max(0, Math.min(4, +e.target.value || 0)))} />
                </div>
              </>
            )}

            {settings.type === 'gauge' && (
              <>
                <div className="rd-field">
                  <label className="rd-label">Minimum</label>
                  <input type="number" className="rd-input"
                    value={Number.isFinite(settings.gaugeMin) ? settings.gaugeMin : 0}
                    onChange={e => setAndApply('gaugeMin', +e.target.value || 0)} />
                </div>
                <div className="rd-field">
                  <label className="rd-label">Maksimum</label>
                  <input type="number" className="rd-input"
                    value={Number.isFinite(settings.gaugeMax) ? settings.gaugeMax : 100}
                    onChange={e => setAndApply('gaugeMax', +e.target.value || 0)} />
                </div>
                <div className="rd-field">
                  <label className="rd-label">Sonek</label>
                  <input className="rd-input" value={settings.suffix || ''} placeholder="örn: %"
                    onChange={e => setAndApply('suffix', e.target.value)} />
                </div>
              </>
            )}

            {settings.type === 'treemap' && (
              <div className="rd-field">
                <label className="rd-label">
                  Etiketleri Göster
                  <span className="rd-label__val">{settings.showLabels !== false ? 'Açık' : 'Kapalı'}</span>
                </label>
                <button type="button" className={`rd-toggle${settings.showLabels !== false ? ' rd-toggle--on' : ''}`}
                  onClick={() => setAndApply('showLabels', settings.showLabels === false)}>
                  <span className="rd-toggle__thumb" />
                </button>
              </div>
            )}
          </>
        )}

        {/* ── Tablo kolonları (alan bazlı: sıra + biçim) — açılır grup ── */}
        {settings.type === 'table' && (
          <>
            <div className="rd-divider" />
            <GroupHead title="Kolonlar" open={openGroups.cols} badge={discoveredColumns.length || ''} onToggle={() => toggleGroup('cols')} />
            {openGroups.cols && (discoveredColumns.length === 0 ? (
              <div className="rd-saved-empty">
                Veri yüklendiğinde kolonlar burada listelenir; sıra, başlık ve biçimlerini buradan düzenleyin.
              </div>
            ) : (
              <DndContext sensors={sensors} collisionDetection={colCollision} onDragStart={() => setExpandedCol(null)} onDragEnd={handleColDragEnd}>
                <SortableContext items={orderedColNames} strategy={verticalListSortingStrategy}>
                  <div className="rd-cols">
                    {orderedColNames.map(name => {
                      const c        = (settings.columns || {})[name] || {}
                      const expanded = expandedCol === name
                      return (
                        <SortableColRow
                          key={name}
                          id={name}
                          cfg={c}
                          expanded={expanded}
                          onSetCol={setCol}
                          onToggleExpand={() => setExpandedCol(expanded ? null : name)}
                          detail={expanded ? fieldDetail(name, c) : null}
                        />
                      )
                    })}
                  </div>
                </SortableContext>
              </DndContext>
            ))}
          </>
        )}

        {/* ── Sıralama (çoklu ORDER BY) — açılır grup ── */}
        {settings.type === 'table' && discoveredColumns.length > 0 && (
          <>
            <div className="rd-divider" />
            <GroupHead title="Sıralama" open={openGroups.sort} badge={sortCount || ''} onToggle={() => toggleGroup('sort')} />
            {openGroups.sort && (
              <>
                {sorts.length === 0 && (
                  <div className="rd-saved-empty" style={{ marginBottom: 8 }}>
                    Henüz sıralama yok. Birden fazla alana göre öncelik sırasıyla sıralayabilirsiniz.
                  </div>
                )}
                {sorts.map((s, i) => (
                  <div key={i} className="rd-sort-row">
                    <span className="rd-sort-idx">{i + 1}</span>
                    <select className="rd-sel rd-sort-field" value={s.field || ''} onChange={e => updateSort(i, { field: e.target.value })}>
                      <option value="">— Seçin —</option>
                      {orderedColNames.map(n => {
                        const c = (settings.columns || {})[n] || {}
                        return <option key={n} value={n}>{c.label || n}</option>
                      })}
                    </select>
                    <button type="button" className="rd-sort-dir" title="Yönü değiştir"
                      onClick={() => updateSort(i, { dir: s.dir === 'desc' ? 'asc' : 'desc' })}>
                      {s.dir === 'desc' ? '↓ Azalan' : '↑ Artan'}
                    </button>
                    <button type="button" className="rd-sort-del" title="Kaldır" onClick={() => removeSort(i)}>
                      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" style={{ width: 12, height: 12 }}>
                        <path d="M18 6 6 18M6 6l12 12" />
                      </svg>
                    </button>
                  </div>
                ))}
                <button type="button" className="rd-add-sort" onClick={addSort} disabled={sortCount >= orderedColNames.length}>
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" style={{ width: 12, height: 12 }}>
                    <path d="M12 5v14M5 12h14" />
                  </svg>
                  Sıralama ekle
                </button>
              </>
            )}
          </>
        )}

        {/* ── Toplamlar (yalnız görünür kolonlar) — açılır grup ── */}
        {settings.type === 'table' && discoveredColumns.length > 0 && (
          <>
            <div className="rd-divider" />
            <GroupHead title="Toplamlar" open={openGroups.totals} badge={totalCount || ''} onToggle={() => toggleGroup('totals')} />
            {openGroups.totals && (
              <div className="rd-cols">
                {numericColNames.length === 0 ? (
                  <div className="rd-saved-empty">
                    Sayısal kolon yok. Toplam için bir kolonu <strong>Kolonlar</strong> bölümünden sayı/para/yüzde biçimine getirin.
                  </div>
                ) : numericColNames.map(name => {
                  const c = (settings.columns || {})[name] || {}
                  return (
                    <div key={name} className="rd-tot-row">
                      <button type="button" className={`rd-toggle rd-toggle--sm${c.total ? ' rd-toggle--on' : ''}`}
                        onClick={() => setCol(name, { total: !c.total })} title="Alt toplam göster">
                        <span className="rd-toggle__thumb" />
                      </button>
                      <span className="rd-tot-name">{c.label || name}</span>
                      {c.total && (
                        <select className="rd-col-fmt" value={c.totalAgg || 'SUM'} onChange={e => setCol(name, { totalAgg: e.target.value })}>
                          <option value="SUM">Toplam</option>
                          <option value="AVG">Ortalama</option>
                          <option value="COUNT">Sayı</option>
                          <option value="MIN">En küçük</option>
                          <option value="MAX">En büyük</option>
                        </select>
                      )}
                    </div>
                  )
                })}
              </div>
            )}
          </>
        )}

        {/* ── Toplamlar (pivot: aç/kapa) ── */}
        {settings.type === 'pivot' && (
          <>
            <div className="rd-divider" />
            <label className="rd-section-label">Toplamlar</label>
            <div className="rd-field">
              <label className="rd-label">
                Toplamları göster
                <span className="rd-label__val">{settings.showTotals !== false ? 'Açık' : 'Kapalı'}</span>
              </label>
              <button type="button" className={`rd-toggle${settings.showTotals !== false ? ' rd-toggle--on' : ''}`}
                onClick={() => setAndApply('showTotals', settings.showTotals === false)}>
                <span className="rd-toggle__thumb" />
              </button>
            </div>
          </>
        )}

        <div className="rd-sidebar__spacer" />

        <button type="button" className="rd-btn-apply" onClick={() => onApply(settings)}>
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" style={{ width: 14, height: 14 }}>
            <polyline points="20 6 9 17 4 12" />
          </svg>
          Değişiklikleri Uygula
        </button>

      </div>
    </aside>
  )
}
