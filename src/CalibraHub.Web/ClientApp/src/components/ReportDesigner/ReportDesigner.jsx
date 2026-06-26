import React, { useState, useEffect, useCallback, useRef } from 'react'
import {
  DndContext, closestCenter, PointerSensor, TouchSensor, useSensor, useSensors,
} from '@dnd-kit/core'
import {
  arrayMove, SortableContext, rectSortingStrategy, useSortable,
} from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import PanelCard from './PanelCard'
import PanelChart, { distinctVals } from './PanelChart'
import FilterField from './FilterField'
import SettingsSidebar from './SettingsSidebar'
import SourcesModal from './SourcesModal'
import LeftPalette from './LeftPalette'
import ReportGrid, { ensureLayouts, nextPanelLayout, ensurePageSource } from './ReportGrid'

let _nextId = 1
function genId() { return 'rdp_' + (_nextId++) }

const EMPTY_FILTERS = {}   // sabit referans — boş filtre için gereksiz render önler

// Bir sayfadaki panellerde EN ÇOK kullanılan view (filtre paneli bununla eşleşmeli)
function dominantSource(list) {
  const counts = {}
  let best = null, bestN = 0
  ;(list || []).forEach(p => {
    if ((p.sourceType || 'view') === 'view' && p.source) {
      counts[p.source] = (counts[p.source] || 0) + 1
      if (counts[p.source] > bestN) {
        bestN = counts[p.source]
        best  = { source: p.source, sourceLabel: p.sourceLabel || p.source }
      }
    }
  })
  return best
}

// Sürüklenebilir panel sarmalı (@dnd-kit) — grip'ten tutup sırala
function SortablePanel({ p, selected, onSelect, onDelete, onColumns, activeFilters, onFilterChange }) {
  const sortable = useSortable({ id: p.id })
  const style = {
    // CSS.Translate (Transform değil) → farklı boyutlu panellerde sürüklerken
    // scaleX/scaleY morph'unu engeller; panel boyutu sabit kalır.
    transform:  CSS.Translate.toString(sortable.transform),
    transition: sortable.transition,
    zIndex:     sortable.isDragging ? 50 : undefined,
    opacity:    sortable.isDragging ? 0.9 : 1,
  }
  const handleProps = { ...sortable.attributes, ...sortable.listeners }
  return (
    <div
      ref={sortable.setNodeRef}
      style={style}
      className={`rd-panel-wrap rd-panel-wrap--col${p.colSpan || 1} rd-panel-wrap--h-${resolveHeightMode(p)}${sortable.isDragging ? ' rd-panel-wrap--dragging' : ''}`}
    >
      <PanelCard
        panel={p}
        selected={selected}
        onSelect={onSelect}
        onDelete={onDelete}
        onColumns={onColumns}
        activeFilters={activeFilters}
        onFilterChange={onFilterChange}
        dragHandleProps={handleProps}
      />
    </div>
  )
}

function getCsrf() {
  return document.querySelector('input[name="__RequestVerificationToken"]')?.value
    || document.querySelector('meta[name="csrf-token"]')?.content
    || ''
}

const EMPTY_PANEL = {
  type:        'line',
  sourceType:  'view',
  source:      '',
  sourceLabel: '',
  metric:      '',
  aggregate:   'SUM',
  group:       '',
  groupIsTime: false,
  sqlQuery:    '',
  sourceId:    null,
  sourceName:  '',
  color:       '#6366f1',
  thickness:   2,
  colSpan:     1,
  panelHeight: 'normal',
}

// Eski kayıtlarla geriye uyum: tall:true → 'tall'
function resolveHeightMode(p) {
  return p.panelHeight || (p.tall ? 'tall' : 'normal')
}

function normalizePagesData(raw) {
  if (!Array.isArray(raw) || raw.length === 0)
    return [{ id: 'pg_1', title: 'Sayfa 1', panels: [] }]
  if (typeof raw[0].panels !== 'undefined')
    return raw
  return [{ id: 'pg_1', title: 'Sayfa 1', panels: raw }]
}

export default function ReportDesigner({ sourcesUrl, saveUrl, listUrl, loadId }) {
  const [title,          setTitle]          = useState('Yeni Rapor')
  const [groupName,      setGroupName]      = useState('')
  const [description,    setDescription]    = useState('')
  const [editTitle,      setEditTitle]      = useState(false)
  const [pages,          setPages]          = useState([{ id: 'pg_1', title: 'Sayfa 1', source: { sourceType: 'view', source: '', sourceLabel: '', sqlQuery: '', sourceId: null, sourceName: '' }, panels: [] }])
  const [currentPageIdx, setCurrentPageIdx] = useState(0)
  const [editingPageIdx, setEditingPageIdx] = useState(null)
  const [selectedId,     setSelId]          = useState(null)
  const [settings,       setSettings]       = useState(null)
  const [sources,        setSources]        = useState([])
  const [reports,        setReports]        = useState([])  // tıklama→rapora git hedefleri
  const [dataNonce,      setDataNonce]      = useState(0)   // kaynak kaydedilince bump → liste + panel verisi refetch
  const [saving,         setSaving]         = useState(false)
  const [toast,          setToast]          = useState(null)
  const [showSources,    setShowSrc]        = useState(false)
  const [designId,       setDesignId]       = useState(null)
  const [isSaved,        setIsSaved]        = useState(false)
  const [colMeta,        setColMeta]        = useState({})   // panelId → keşfedilen kolon adları
  const [colNumeric,     setColNumeric]     = useState({})   // panelId → { kolonAdı: sayısal mı }
  const [panelData,      setPanelData]      = useState({})   // panelId → { columns, rows } (filtre değerleri için)
  const [paletteOpen,    setPaletteOpen]    = useState(false) // Görseller dropdown'ı (topbar)
  const [desFiltersOpen, setDesFiltersOpen] = useState(false) // sol kaymalı filtre drawer'ı (designer)
  const [filtersByPage,  setFiltersByPage]  = useState({})   // sayfa-bazlı filtreler: { [pageId]: { key: { source, field, values } } }
  const [undoStack,      setUndoStack]      = useState([])   // geri al geçmişi (pages anlık görüntüleri)
  const [redoStack,      setRedoStack]      = useState([])
  const skipHistoryRef = useRef(false)
  const prevPagesRef   = useRef(null)
  const lastChangeRef  = useRef(0)

  const currentPage = pages[currentPageIdx] ?? pages[0]
  const panels = currentPage?.panels ?? []
  const filterPanels = panels.filter(p => p.type === 'filter')   // sol rayda
  const dataPanels   = panels.filter(p => p.type !== 'filter')   // ızgarada
  const pageFilters = (currentPage && filtersByPage[currentPage.id]) || EMPTY_FILTERS
  const desActiveFilterCount = Object.values(pageFilters).filter(e => e && Array.isArray(e.values) && e.values.length > 0).length
  useEffect(() => {
    if (!desFiltersOpen) return undefined
    const onKey = e => { if (e.key === 'Escape') setDesFiltersOpen(false) }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [desFiltersOpen])

  // "Filtrede kullan" işaretli kolonlardan otomatik filtre alanları (sol ray)
  const pageSrcName = currentPage?.source?.sourceName || currentPage?.source?.source || ''
  const filterFields = (() => {
    const out = [], seen = new Set()
    dataPanels.forEach(p => {
      const cc = p.columns || {}
      Object.keys(cc).forEach(name => {
        if (cc[name] && cc[name].filter && !seen.has(name)) {
          seen.add(name)
          out.push({ field: name, label: cc[name].label || name, source: pageSrcName })
        }
      })
    })
    return out
  })()
  function distinctOf(field) {
    for (const pid in panelData) {
      const d = panelData[pid]
      if (!d || !d.columns) continue
      const ci = d.columns.indexOf(field)
      if (ci >= 0) return distinctVals(d.rows || [], ci)
    }
    return []
  }

  // View → alan adları haritası (filtre alan-adı eşleştirmesi için)
  const viewFields = {}
  sources.forEach(s => {
    const f = []
    ;(s.metrics || []).forEach(x => f.push(x.value))
    ;(s.groups  || []).forEach(x => f.push(x.value))
    viewFields[s.name] = f
  })

  useEffect(() => {
    if (!sourcesUrl) return
    fetch(sourcesUrl, { credentials: 'same-origin' })
      .then(r => r.ok ? r.json() : [])
      .then(data => setSources(Array.isArray(data) ? data : []))
      .catch(() => setSources([]))
  }, [sourcesUrl, dataNonce])

  useEffect(() => {
    fetch('/Dashboard/DesignsList', { credentials: 'same-origin' })
      .then(r => r.ok ? r.json() : [])
      .then(data => setReports(Array.isArray(data) ? data : []))
      .catch(() => setReports([]))
  }, [])

  useEffect(() => {
    if (!loadId) return
    handleLoadDesign(loadId)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const showToast = useCallback((msg, kind = 'info') => {
    setToast({ msg, kind })
    setTimeout(() => setToast(null), 2600)
  }, [])

  // PanelChart'tan gelen keşfedilen tablo kolonlarını kaydet (değişmişse)
  const reportColumns = useCallback((pid, cols, numericMap) => {
    setColMeta(prev => {
      const prevCols = prev[pid]
      if (prevCols && prevCols.length === cols.length && prevCols.every((c, i) => c === cols[i])) return prev
      return { ...prev, [pid]: cols }
    })
    if (numericMap) setColNumeric(prev => ({ ...prev, [pid]: numericMap }))
  }, [])

  // PanelChart'tan gelen veriyi sakla (filtre alanı benzersiz değerleri için)
  const reportPanelData = useCallback((pid, d) => {
    setPanelData(prev => (prev[pid] === d ? prev : { ...prev, [pid]: d }))
  }, [])

  // Panelleri sürükleyerek sırala (@dnd-kit)
  const panelSensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 6 } }),
    useSensor(TouchSensor,   { activationConstraint: { delay: 160, tolerance: 6 } })
  )

  function handlePanelDragEnd(event) {
    const { active, over } = event
    if (!over || active.id === over.id) return
    const idx = currentPageIdx
    setPages(prev => prev.map((pg, i) => {
      if (i !== idx) return pg
      const oldI = pg.panels.findIndex(p => p.id === active.id)
      const newI = pg.panels.findIndex(p => p.id === over.id)
      if (oldI < 0 || newI < 0) return pg
      return { ...pg, panels: arrayMove(pg.panels, oldI, newI) }
    }))
    setSelId(null)
    setSettings(null)
    setIsSaved(false)
  }

  // ── Geri al / yinele (pages gecmisi; hizli ardisik degisiklikler tek adimda birlesir) ──
  useEffect(() => {
    if (skipHistoryRef.current) { skipHistoryRef.current = false; prevPagesRef.current = pages; return }
    if (prevPagesRef.current === null) { prevPagesRef.current = pages; return }
    if (prevPagesRef.current === pages) return
    const now = Date.now()
    if (now - lastChangeRef.current > 400) {
      const snapshot = prevPagesRef.current
      setUndoStack(s => [...s.slice(-49), snapshot])
      setRedoStack([])
    }
    lastChangeRef.current = now
    prevPagesRef.current = pages
  }, [pages])

  function undo() {
    if (!undoStack.length) return
    const prev = undoStack[undoStack.length - 1]
    skipHistoryRef.current = true
    prevPagesRef.current = prev
    lastChangeRef.current = 0
    setRedoStack(r => [...r, pages])
    setUndoStack(s => s.slice(0, -1))
    setPages(prev)
    setSelId(null); setSettings(null); setIsSaved(false)
  }

  function redo() {
    if (!redoStack.length) return
    const next = redoStack[redoStack.length - 1]
    skipHistoryRef.current = true
    prevPagesRef.current = next
    lastChangeRef.current = 0
    setUndoStack(s => [...s, pages])
    setRedoStack(r => r.slice(0, -1))
    setPages(next)
    setSelId(null); setSettings(null); setIsSaved(false)
  }

  const undoRef = useRef(undo); undoRef.current = undo
  const redoRef = useRef(redo); redoRef.current = redo
  useEffect(() => {
    function onKey(e) {
      const t   = e.target
      const tag = t && t.tagName
      if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || (t && t.isContentEditable)) return
      if (!(e.ctrlKey || e.metaKey)) return
      const k = (e.key || '').toLowerCase()
      if (k === 'z' && !e.shiftKey) { e.preventDefault(); undoRef.current() }
      else if (k === 'y' || (k === 'z' && e.shiftKey)) { e.preventDefault(); redoRef.current() }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  async function handleLoadDesign(id) {
    try {
      const r = await fetch(`/Dashboard/LoadDesign/${id}`, { credentials: 'same-origin' })
      const d = await r.json()
      if (!d.ok) { showToast(d.error || 'Tasarım yüklenemedi', 'error'); return }
      const loaded     = JSON.parse(d.panelsJson || '[]')
      const normalized = ensurePageSource(ensureLayouts(normalizePagesData(loaded)))
      normalized.forEach(pg => (pg.panels || []).forEach(p => {
        const n = parseInt((p.id || '').replace('rdp_', ''), 10)
        if (!isNaN(n) && n >= _nextId) _nextId = n + 1
      }))
      skipHistoryRef.current = true   // tasarım yüklemesi geri-al geçmişine girmesin
      setPages(normalized)
      setCurrentPageIdx(0)
      setTitle(d.title || 'Yeni Rapor')
      setGroupName(d.groupName || '')
      setDescription(d.description || '')
      setDesignId(id)
      setIsSaved(true)
      setSelId(null)
      setSettings(null)
      showToast(`"${d.title}" yüklendi`, 'success')
    } catch { showToast('Yükleme hatası', 'error') }
  }

  // ── Panel işlemleri ──────────────────────────────────────────────

  function addPanel() {
    addPanelOfType('line')
  }

  function addPanelOfType(type) {
    const id    = genId()
    const panel = { ...EMPTY_PANEL, id, type, title: `Panel ${panels.length + 1}`, layout: nextPanelLayout(panels, type) }
    // Filtre paneli, sayfadaki panellerle AYNI view'i kullanmalı ki onları süzebilsin
    if (type === 'filter') {
      const dom = dominantSource(panels)
      if (dom) { panel.sourceType = 'view'; panel.source = dom.source; panel.sourceLabel = dom.sourceLabel }
    }
    const idx   = currentPageIdx
    setPages(prev => prev.map((pg, i) =>
      i === idx ? { ...pg, panels: [...pg.panels, panel] } : pg
    ))
    setSelId(id)
    setSettings({ ...panel })
    setIsSaved(false)
  }

  // Sol paletten tür seçimi: panel seçiliyse türünü değiştir, değilse yeni panel ekle
  function pickType(type) {
    if (selectedId && settings) {
      applySettings({ ...settings, type }, { silent: true })
      return
    }
    addPanelOfType(type)
  }

  // Sayfa veri kaynağını (View) ayarla — tüm paneller bunu kullanır
  function setPageSourceView(viewName) {
    const s = sources.find(x => x.name === viewName)
    const idx = currentPageIdx
    setPages(prev => prev.map((pg, i) =>
      i === idx ? { ...pg, source: { sourceType: 'view', source: viewName, sourceLabel: s?.label || viewName, sqlQuery: '', sourceId: null, sourceName: '' } } : pg
    ))
    setIsSaved(false)
  }

  // Sayfa veri kaynağını (kayıtlı SQL kaynağı) ayarla
  function setPageSourceSaved(src) {
    const idx = currentPageIdx
    setPages(prev => prev.map((pg, i) =>
      i === idx ? { ...pg, source: { sourceType: 'saved', source: '', sourceLabel: src.name, sqlQuery: '', sourceId: src.id, sourceName: src.name } } : pg
    ))
    setIsSaved(false)
  }

  // RGL'den gelen yeni yerleşimi aktif sayfanın panellerine yaz (sürükle/boyut bitince)
  function updatePanelLayouts(layoutArr) {
    const byId = {}
    layoutArr.forEach(l => { byId[l.i] = { x: l.x, y: l.y, w: l.w, h: l.h } })
    const idx = currentPageIdx
    setPages(prev => prev.map((pg, i) =>
      i === idx ? { ...pg, panels: pg.panels.map(p => byId[p.id] ? { ...p, layout: byId[p.id] } : p) } : pg
    ))
    setIsSaved(false)
  }

  function selectPanel(panel) {
    setSelId(panel.id)
    setSettings({ ...panel })
  }

  function deletePanel(id) {
    const idx = currentPageIdx
    setPages(prev => prev.map((pg, i) =>
      i === idx ? { ...pg, panels: pg.panels.filter(p => p.id !== id) } : pg
    ))
    if (selectedId === id) { setSelId(null); setSettings(null) }
    setIsSaved(false)
  }

  function applySettings(next, opts) {
    const idx = currentPageIdx
    setPages(prev => prev.map((pg, i) =>
      i === idx ? { ...pg, panels: pg.panels.map(p => p.id === next.id ? next : p) } : pg
    ))
    setSettings(next)
    setIsSaved(false)
    if (!opts?.silent) showToast('Değişiklikler uygulandı', 'success')
  }

  function closeSidebar() { setSelId(null); setSettings(null) }

  // ── Sayfa işlemleri ──────────────────────────────────────────────

  function switchPage(idx) {
    if (idx === currentPageIdx) return
    setCurrentPageIdx(idx)
    setSelId(null)
    setSettings(null)
    // Filtreler sayfa-bazlı saklanır (filtersByPage); sayfa değişince temizlenmez,
    // her sayfa kendi seçimini korur ve diğer sayfaları etkilemez.
  }

  function handleFilterChange(key, entry) {
    const pid = currentPage?.id
    if (!pid) return
    setFiltersByPage(prev => ({ ...prev, [pid]: { ...(prev[pid] || {}), [key]: entry } }))
  }

  function addPage() {
    const num    = pages.length + 1
    const newIdx = pages.length
    setPages(prev => [...prev, { id: `pg_${Date.now()}`, title: `Sayfa ${num}`, source: { ...(currentPage?.source || { sourceType: 'view', source: '', sourceLabel: '', sqlQuery: '', sourceId: null, sourceName: '' }) }, panels: [] }])
    setCurrentPageIdx(newIdx)
    setSelId(null)
    setSettings(null)
    setIsSaved(false)
  }

  function deletePage(idx) {
    const pg = pages[idx]
    if (pages.length === 1) return
    if ((pg?.panels?.length ?? 0) > 0) {
      showToast(`"${pg.title}" sayfasında ${pg.panels.length} panel var — önce panelleri silin`, 'error')
      return
    }
    setPages(prev => prev.filter((_, i) => i !== idx))
    setCurrentPageIdx(prev => Math.min(prev, pages.length - 2))
    setSelId(null)
    setSettings(null)
    setIsSaved(false)
  }

  function renamePageConfirm(idx, newTitle) {
    const t = (newTitle || '').trim()
    setPages(prev => prev.map((pg, i) => i === idx ? { ...pg, title: t || `Sayfa ${i + 1}` } : pg))
    setEditingPageIdx(null)
    setIsSaved(false)
  }

  function handleTitleChange(val) { setTitle(val); setIsSaved(false) }

  async function handleSave() {
    if (saving) return
    if (!title.trim()) { showToast('Rapor adı boş olamaz', 'error'); return }
    setSaving(true)
    try {
      const res = await fetch(saveUrl, {
        method: 'POST',
        credentials: 'same-origin',
        headers: {
          'Content-Type': 'application/json',
          'RequestVerificationToken': getCsrf(),
        },
        body: JSON.stringify({ id: designId, title: title.trim(), groupName: groupName.trim() || null, description: description.trim() || null, panels: pages }),
      })
      const data = await res.json()
      if (data.ok) {
        setDesignId(data.id)
        setIsSaved(true)
        showToast('Rapor kaydedildi', 'success')
      } else {
        showToast(data.error || 'Kayıt başarısız', 'error')
      }
    } catch {
      showToast('Ağ hatası — kayıt yapılamadı', 'error')
    } finally {
      setSaving(false)
    }
  }

  const backUrl     = listUrl || '/Dashboard/Designer'

  return (
    <div className="rd-root">
      {/* ── Topbar ── */}
      <header className="rd-topbar">
        <a href={backUrl} className="rd-btn rd-btn--ghost rd-topbar__back" title="Tasarım listesine dön">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" style={{ width: 14, height: 14 }}>
            <polyline points="15 18 9 12 15 6" />
          </svg>
        </a>

        <span className="rd-topbar__logo">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" style={{ width: 18, height: 18 }}>
            <rect x="3" y="3" width="7" height="7" rx="1" /><rect x="14" y="3" width="7" height="7" rx="1" />
            <rect x="3" y="14" width="7" height="7" rx="1" /><rect x="14" y="14" width="7" height="7" rx="1" />
          </svg>
        </span>

        <div className="rd-topbar__center">
          <span className="rd-topbar__title rd-topbar__title--static">{title}</span>
        </div>

        <div className="rd-topbar__gap" />

        <div className="rd-topbar__tools">
          {(filterPanels.length > 0 || filterFields.length > 0) && (
            <button type="button" className="rd-topbar__tool rd-topbar__tool--icon rd-topbar__tool--badged" onClick={() => setDesFiltersOpen(true)} title="Filtreler">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                <polygon points="22 3 2 3 10 12.46 10 19 14 21 14 12.46 22 3" />
              </svg>
              {desActiveFilterCount > 0 && <span className="rd-topbar__badge">{desActiveFilterCount}</span>}
            </button>
          )}
          <LeftPalette
            open={paletteOpen}
            activeType={settings?.type}
            hasSelection={!!selectedId}
            onPick={pickType}
            onToggle={() => setPaletteOpen(o => !o)}
          />
          <button type="button" className="rd-topbar__tool rd-topbar__tool--icon" onClick={() => setShowSrc(true)} title="Veri Kaynakları">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
              <ellipse cx="12" cy="5" rx="9" ry="3" /><path d="M3 5v14c0 1.66 4.03 3 9 3s9-1.34 9-3V5" /><path d="M3 12c0 1.66 4.03 3 9 3s9-1.34 9-3" />
            </svg>
          </button>
          <button type="button" className="rd-topbar__tool rd-topbar__tool--icon" onClick={undo} disabled={!undoStack.length} title="Geri Al (Ctrl+Z)">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round"><polyline points="9 14 4 9 9 4" /><path d="M20 20v-7a4 4 0 0 0-4-4H4" /></svg>
          </button>
          <button type="button" className="rd-topbar__tool rd-topbar__tool--icon" onClick={redo} disabled={!redoStack.length} title="İleri Al (Ctrl+Y)">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round"><polyline points="15 14 20 9 15 4" /><path d="M4 20v-7a4 4 0 0 1 4-4h12" /></svg>
          </button>
        </div>

        <div className="rd-topbar__sep" />

        <button type="button" className="rd-btn rd-btn--primary" onClick={handleSave}>
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" style={{ width: 14, height: 14 }}><path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z" /><polyline points="17 21 17 13 7 13 7 21" /><polyline points="7 3 7 8 15 8" /></svg>
          Kaydet
        </button>
      </header>

      {/* ── Sayfa sekmeleri ── */}
      <div className="rd-pages">
        {pages.map((pg, idx) => (
          <div
            key={pg.id}
            className={`rd-page-tab${idx === currentPageIdx ? ' rd-page-tab--active' : ''}`}
            onClick={() => switchPage(idx)}
            role="tab"
          >
            {editingPageIdx === idx ? (
              <input
                autoFocus
                className="rd-page-tab__name-input"
                defaultValue={pg.title}
                onBlur={e => renamePageConfirm(idx, e.target.value)}
                onKeyDown={e => {
                  if (e.key === 'Enter') renamePageConfirm(idx, e.currentTarget.value)
                  if (e.key === 'Escape') setEditingPageIdx(null)
                }}
                onClick={e => e.stopPropagation()}
              />
            ) : (
              <span
                className="rd-page-tab__name"
                onDoubleClick={e => { e.stopPropagation(); setEditingPageIdx(idx) }}
              >
                {pg.title}
              </span>
            )}
            {pages.length > 1 && (
              <button
                type="button"
                className="rd-page-tab__del"
                title="Sayfayı sil"
                onClick={e => { e.stopPropagation(); deletePage(idx) }}
              >×</button>
            )}
          </div>
        ))}
        <button type="button" className="rd-page-add rd-page-add--icon" onClick={addPage} title="Sayfa Ekle" aria-label="Sayfa Ekle">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" style={{ width: 14, height: 14 }}>
            <path d="M12 5v14M5 12h14" />
          </svg>
        </button>
      </div>

      {/* ── Workspace ── */}
      <div className="rd-workspace">
        {(filterPanels.length > 0 || filterFields.length > 0) && (
          <>
            <div className={`rv-fdrawer__backdrop${desFiltersOpen ? ' is-open' : ''}`} onClick={() => setDesFiltersOpen(false)} />
            <aside className={`rv-fdrawer${desFiltersOpen ? ' is-open' : ''}`} aria-hidden={!desFiltersOpen}>
              <div className="rv-fdrawer__head">
                <span className="rv-fdrawer__icon">
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" style={{ width: 14, height: 14 }}>
                    <polygon points="22 3 2 3 10 12.46 10 19 14 21 14 12.46 22 3" />
                  </svg>
                </span>
                <span className="rv-fdrawer__title">Filtreler</span>
                <button type="button" className="rv-fdrawer__close" onClick={() => setDesFiltersOpen(false)} title="Kapat (Esc)">
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" style={{ width: 15, height: 15 }}>
                    <path d="M18 6 6 18M6 6l12 12" />
                  </svg>
                </button>
              </div>
              <div className="rv-fdrawer__list">
                {filterFields.map(ff => (
                  <FilterField
                    key={ff.field}
                    label={ff.label}
                    values={distinctOf(ff.field)}
                    selected={(pageFilters[ff.field] && pageFilters[ff.field].values) || []}
                    onChange={vals => handleFilterChange(ff.field, { source: ff.source, field: ff.field, values: vals })}
                  />
                ))}
                {filterPanels.map(p => (
                  <div
                    key={p.id}
                    className={`rv-filters__item rd-fitem${selectedId === p.id ? ' rd-fitem--sel' : ''}`}
                    onClick={() => selectPanel(p)}
                    role="button"
                    tabIndex={0}
                  >
                    <div className="rd-fitem__bar">
                      <span className="rd-fitem__title">{p.title || 'Filtre'}</span>
                      <button type="button" className="rd-fitem__del" title="Sil" onClick={e => { e.stopPropagation(); deletePanel(p.id) }}>
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" style={{ width: 11, height: 11 }}><path d="M18 6 6 18M6 6l12 12" /></svg>
                      </button>
                    </div>
                    <PanelChart panel={{ ...p, ...(currentPage?.source || {}), _nonce: dataNonce }} chartHeight={220} activeFilters={pageFilters} onFilterChange={handleFilterChange} viewFields={viewFields} />
                  </div>
                ))}
              </div>
            </aside>
          </>
        )}

        <div className="rd-canvas" onClick={e => { if (!e.target.closest('.rd-panel') && !e.target.closest('.rd-canvas-empty')) closeSidebar() }}>
          {dataPanels.length === 0 ? (
            <button type="button" className="rd-canvas-empty" onClick={addPanel}>
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" style={{ width: 36, height: 36 }}>
                <circle cx="12" cy="12" r="10" /><path d="M12 8v8M8 12h8" />
              </svg>
              <span>İlk paneli ekle</span>
              <small>Buraya tıklayın veya soldan bir panel türü seçin</small>
            </button>
          ) : (
            <>
              <ReportGrid
                panels={dataPanels}
                editable
                onLayoutChange={updatePanelLayouts}
                renderPanel={p => (
                  <PanelCard
                    panel={p}
                    selected={selectedId === p.id}
                    onSelect={() => selectPanel(p)}
                    onDelete={deletePanel}
                    onColumns={(cols, num) => reportColumns(p.id, cols, num)}
                    onData={d => reportPanelData(p.id, d)}
                    activeFilters={pageFilters}
                    onFilterChange={handleFilterChange}
                    viewFields={viewFields}
                    pageSource={{ ...(currentPage?.source || {}), _nonce: dataNonce }}
                    dragHandleProps={{}}
                    fillHeight
                  />
                )}
              />
              <button type="button" className="rd-canvas-add" onClick={addPanel} title="Panel Ekle">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" style={{ width: 18, height: 18 }}>
                  <path d="M12 5v14M5 12h14" />
                </svg>
                <span>Panel Ekle</span>
              </button>
            </>
          )}
        </div>

        <SettingsSidebar
          open
          settings={settings}
          sources={sources}
          pageSource={currentPage?.source}
          discoveredColumns={selectedId ? (colMeta[selectedId] || []) : []}
          discoveredNumeric={selectedId ? (colNumeric[selectedId] || {}) : {}}
          reports={reports}
          onChange={setSettings}
          onApply={applySettings}
          onClose={closeSidebar}
          reportTitle={title}
          reportGroup={groupName}
          reportDescription={description}
          currentPageTitle={currentPage?.title}
          onReportTitleChange={handleTitleChange}
          onReportGroupChange={v => { setGroupName(v); setIsSaved(false) }}
          onReportDescriptionChange={v => { setDescription(v); setIsSaved(false) }}
          onManageSource={() => setShowSrc(true)}
        />
      </div>

      {toast && (
        <div className={`rd-toast rd-toast--${toast.kind}`} role="status" aria-live="polite">
          {toast.msg}
        </div>
      )}

      <SourcesModal
        open={showSources}
        onClose={() => setShowSrc(false)}
        views={sources}
        currentSource={currentPage?.source}
        onSelectView={name => { setPageSourceView(name); setShowSrc(false) }}
        onSelect={src => { setPageSourceSaved(src); setShowSrc(false) }}
        onSaved={() => setDataNonce(n => n + 1)}
      />
    </div>
  )
}
