/**
 * FieldSettingsForm — Alan Ayarları (bağımsız modal)
 *
 * Props:
 *   column  : { key, label, formCode, guideCode, formatJson, filterJson }
 *   isOpen  : boolean
 *   onClose : function()
 *
 * formatJson: { visibleColumns: string[] | null, columnLabels: {[col]: string} }
 */
import { useState, useEffect } from 'react'
import { createPortal } from 'react-dom'
import { getCatalog, getViewColumns } from '../../services/guideManagementService'
import { upsertFieldByFormCode } from '../../services/fieldSettingService'

function useIsLight() {
  var [light, setLight] = useState(function() {
    return document.body.classList.contains('app-theme-light')
  })
  useEffect(function() {
    var obs = new MutationObserver(function() {
      setLight(document.body.classList.contains('app-theme-light'))
    })
    obs.observe(document.body, { attributes: true, attributeFilter: ['class'] })
    return function() { obs.disconnect() }
  }, [])
  return light
}

function parseFormat(json) {
  if (!json) return { visibleColumns: null, columnLabels: {} }
  try {
    var p = JSON.parse(json)
    return { visibleColumns: p.visibleColumns || null, columnLabels: p.columnLabels || {} }
  } catch (e) {
    return { visibleColumns: null, columnLabels: {} }
  }
}

export default function FieldSettingsForm({ column, isOpen, onClose }) {
  var isLight = useIsLight()

  var [catalog, setCatalog]                 = useState(null)
  var [catalogLoading, setCatalogLoading]   = useState(false)
  var [viewCols, setViewCols]               = useState([])
  var [viewColsLoading, setViewColsLoading] = useState(false)

  var [selectedView, setSelectedView] = useState('')
  var [colRows, setColRows]           = useState([])   // [{name, visible, label}]
  var [filterText, setFilterText]     = useState('')

  var [saving, setSaving] = useState(false)
  var [error, setError]   = useState(null)

  // Açılışta başlangıç değerleri
  useEffect(function() {
    if (!isOpen) return
    setError(null)
    setFilterText(column.filterJson || '')
  }, [isOpen, column.filterJson])

  // Katalog yükle
  useEffect(function() {
    if (!isOpen || catalog !== null) return
    var alive = true
    setCatalogLoading(true)
    getCatalog()
      .then(function(list) {
        if (!alive) return
        var arr = Array.isArray(list) ? list : []
        setCatalog(arr)
        var current = arr.find(function(g) { return g.guideCode === column.guideCode })
        if (current) setSelectedView(current.viewName || '')
      })
      .catch(function(e) { if (alive) setError('Rehber listesi yüklenemedi: ' + e.message) })
      .finally(function() { if (alive) setCatalogLoading(false) })
    return function() { alive = false }
  }, [isOpen, catalog, column.guideCode])

  // View seçilince kolonları yükle
  useEffect(function() {
    if (!isOpen || !selectedView) { setViewCols([]); setColRows([]); return }
    var alive = true
    setViewColsLoading(true)
    var parsed = parseFormat(column.formatJson)
    getViewColumns(selectedView)
      .then(function(cols) {
        if (!alive) return
        setViewCols(cols || [])
        setColRows((cols || []).map(function(name) {
          var visible = parsed.visibleColumns ? parsed.visibleColumns.includes(name) : true
          var label   = parsed.columnLabels[name] || ''
          return { name: name, visible: visible, label: label }
        }))
      })
      .catch(function() { if (alive) { setViewCols([]); setColRows([]) } })
      .finally(function() { if (alive) setViewColsLoading(false) })
    return function() { alive = false }
  }, [isOpen, selectedView, column.formatJson])

  function handleViewChange(viewName) { setSelectedView(viewName) }

  function toggleCol(idx) {
    setColRows(function(prev) {
      return prev.map(function(r, i) { return i === idx ? Object.assign({}, r, { visible: !r.visible }) : r })
    })
  }

  function setLabel(idx, val) {
    setColRows(function(prev) {
      return prev.map(function(r, i) { return i === idx ? Object.assign({}, r, { label: val }) : r })
    })
  }

  function buildFormatJson() {
    var visible  = colRows.filter(function(r) { return r.visible }).map(function(r) { return r.name })
    var labels   = {}
    colRows.forEach(function(r) { if (r.label.trim()) labels[r.name] = r.label.trim() })
    var allVisible = colRows.length === 0 || colRows.every(function(r) { return r.visible })
    return JSON.stringify({
      visibleColumns: allVisible ? null : visible,
      columnLabels:   Object.keys(labels).length > 0 ? labels : undefined,
    })
  }

  async function handleSave() {
    if (!selectedView) { setError('Lütfen bir view seçin.'); return }
    var guideEntry = (catalog || []).find(function(g) { return g.viewName === selectedView })
    if (!guideEntry) { setError('Seçilen view için rehber bulunamadı.'); return }
    setSaving(true); setError(null)
    var fmtJson = buildFormatJson()
    try {
      var result = await upsertFieldByFormCode({
        formCode:   column.formCode,
        fieldKey:   column.key,
        fieldLabel: column.label || column.key,
        guideCode:  guideEntry.guideCode,
        filterJson: filterText.trim() || null,
        isRequired: false,
        formatJson: fmtJson,
      })
      setSaving(false)
      if (!result.success) {
        setError(result.message || 'Kayıt başarısız.')
      } else {
        column.guideCode  = guideEntry.guideCode
        column.filterJson = filterText.trim() || null
        column.formatJson = fmtJson
        handleClose()
      }
    } catch (e) {
      setSaving(false)
      setError('Kayıt hatası: ' + e.message)
    }
  }

  function handleClose() {
    setCatalog(null); setViewCols([]); setColRows([])
    setSelectedView(''); setError(null)
    onClose()
  }

  if (!isOpen) return null

  var canSave = !!selectedView && !saving && !viewColsLoading && !catalogLoading

  // ── Stiller ──
  var bg      = isLight ? '#fff'                          : 'rgba(13,17,27,0.98)'
  var bdr     = isLight ? '1px solid #e2e8f0'             : '1px solid rgba(255,255,255,0.12)'
  var shadow  = isLight ? '0 16px 48px rgba(0,0,0,0.15)' : '0 16px 48px rgba(0,0,0,0.55)'
  var hdrBg   = isLight ? '#f8fafc'                       : 'rgba(255,255,255,0.04)'
  var divBdr  = isLight ? '1px solid #e2e8f0'             : '1px solid rgba(255,255,255,0.08)'
  var iBg     = isLight ? '#f8fafc'                       : 'rgba(255,255,255,0.06)'
  var iBdr    = isLight ? '1px solid #cbd5e1'             : '1px solid rgba(255,255,255,0.14)'
  var iClr    = isLight ? '#1e293b'                       : 'rgba(255,255,255,0.85)'
  var lblClr  = isLight ? '#64748b'                       : 'rgba(255,255,255,0.4)'
  var txtClr  = isLight ? '#334155'                       : 'rgba(255,255,255,0.75)'
  var btnClr  = isLight ? '#6366f1'                       : '#818cf8'
  var rowAlt  = isLight ? 'rgba(0,0,0,0.03)'             : 'rgba(255,255,255,0.03)'

  return createPortal(
    <div
      onClick={function(e) { e.stopPropagation() }}
      style={{
        position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.6)',
        display: 'flex', alignItems: 'center', justifyContent: 'center',
        zIndex: 10000, padding: 16,
      }}
    >
      <div style={{
        background: bg, border: bdr, boxShadow: shadow, borderRadius: 10,
        width: '100%', maxWidth: 460, maxHeight: '80vh',
        display: 'flex', flexDirection: 'column', overflow: 'hidden',
        backdropFilter: isLight ? undefined : 'blur(24px)',
      }}>

        {/* Başlık */}
        <div style={{ display: 'flex', alignItems: 'center', padding: '8px 14px', borderBottom: divBdr, background: hdrBg, flexShrink: 0, gap: 7 }}>
          <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke={btnClr} strokeWidth="2" strokeLinecap="round" style={{ flexShrink: 0 }}>
            <circle cx="12" cy="12" r="3"/>
            <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z"/>
          </svg>
          <span style={{ fontSize: 12, fontWeight: 600, color: txtClr, flex: 1 }}>
            Alan Ayarları — <span style={{ fontFamily: 'monospace', color: btnClr }}>{column.label || column.key}</span>
          </span>
          <button type="button" onClick={handleClose}
            style={{ background: 'none', border: 'none', color: lblClr, cursor: 'pointer', fontSize: 17, lineHeight: 1, padding: '0 2px' }}>×</button>
        </div>

        {/* İçerik */}
        <div style={{ flex: 1, overflowY: 'auto', padding: '12px 14px', display: 'flex', flexDirection: 'column', gap: 12 }}>

          {/* View seçimi */}
          <div>
            <label style={{ display: 'flex', alignItems: 'center', gap: 3, fontSize: 10, fontWeight: 700, color: lblClr, textTransform: 'uppercase', letterSpacing: '.06em', marginBottom: 4 }}>
              Rehber View <span style={{ color: '#f87171' }}>*</span>
            </label>
            {catalogLoading
              ? <div style={{ fontSize: 11, color: lblClr }}>Yükleniyor...</div>
              : (
                <select
                  value={selectedView}
                  onChange={function(e) { handleViewChange(e.target.value) }}
                  style={{ width: '100%', padding: '6px 8px', fontSize: 12, background: iBg, border: selectedView ? iBdr : '1px solid rgba(239,68,68,0.5)', borderRadius: 5, color: iClr, outline: 'none', cursor: 'pointer', colorScheme: isLight ? 'light' : 'dark' }}
                >
                  <option value="" style={{ background: isLight ? '#ffffff' : '#0d1117', color: iClr }}>— View seçin —</option>
                  {(catalog || []).map(function(g) {
                    return <option key={g.guideCode} value={g.viewName} style={{ background: isLight ? '#ffffff' : '#0d1117', color: iClr }}>{g.viewName}</option>
                  })}
                </select>
              )
            }
          </div>

          {/* Kolonlar */}
          {selectedView && (
            <div>
              <div style={{ display: 'flex', alignItems: 'center', marginBottom: 5 }}>
                <label style={{ fontSize: 10, fontWeight: 700, color: lblClr, textTransform: 'uppercase', letterSpacing: '.06em', flex: 1 }}>
                  Görünür Kolonlar &amp; Etiketler
                </label>
                {colRows.length > 0 && (
                  <button
                    type="button"
                    onClick={function() { setColRows(function(prev) { return prev.map(function(r) { return Object.assign({}, r, { visible: true }) }) }) }}
                    style={{ fontSize: 10, color: btnClr, background: 'none', border: 'none', cursor: 'pointer', textDecoration: 'underline' }}
                  >
                    Tümünü seç
                  </button>
                )}
              </div>
              {viewColsLoading
                ? <div style={{ fontSize: 11, color: lblClr }}>Yükleniyor...</div>
                : viewCols.length === 0
                  ? <div style={{ fontSize: 11, color: lblClr }}>Kolon bulunamadı.</div>
                  : (
                    <div style={{ border: divBdr, borderRadius: 6, overflow: 'hidden' }}>
                      <div style={{ display: 'grid', gridTemplateColumns: '34px 1fr 1fr', background: hdrBg, padding: '4px 8px', borderBottom: divBdr }}>
                        <span />
                        <span style={{ fontSize: 9.5, fontWeight: 700, color: lblClr, textTransform: 'uppercase' }}>Alan Adı</span>
                        <span style={{ fontSize: 9.5, fontWeight: 700, color: lblClr, textTransform: 'uppercase' }}>Etiket</span>
                      </div>
                      {colRows.map(function(row, idx) {
                        return (
                          <div
                            key={row.name}
                            style={{
                              display: 'grid', gridTemplateColumns: '34px 1fr 1fr',
                              padding: '5px 8px', alignItems: 'center',
                              background: idx % 2 === 1 ? rowAlt : 'transparent',
                              borderBottom: idx < colRows.length - 1 ? divBdr : 'none',
                              opacity: row.visible ? 1 : 0.4,
                            }}
                          >
                            <button
                              type="button"
                              onClick={function() { toggleCol(idx) }}
                              style={{
                                width: 28, height: 16, borderRadius: 8, border: 'none', cursor: 'pointer',
                                background: row.visible ? btnClr : (isLight ? '#e2e8f0' : 'rgba(255,255,255,0.15)'),
                                position: 'relative', transition: 'background 0.2s',
                              }}
                            >
                              <span style={{
                                position: 'absolute', top: 2, left: row.visible ? 13 : 2,
                                width: 12, height: 12, borderRadius: '50%', background: '#fff',
                                transition: 'left 0.18s', display: 'block',
                              }} />
                            </button>
                            <span style={{ fontSize: 11, color: txtClr, fontFamily: 'monospace', paddingRight: 6 }}>{row.name}</span>
                            <input
                              type="text"
                              value={row.label}
                              onChange={function(e) { setLabel(idx, e.target.value) }}
                              placeholder={row.name}
                              style={{ fontSize: 11, padding: '3px 6px', background: iBg, border: iBdr, borderRadius: 4, color: iClr, outline: 'none', width: '100%' }}
                            />
                          </div>
                        )
                      })}
                    </div>
                  )
              }
            </div>
          )}

          {/* SQL Kısıtı */}
          <div>
            <label style={{ display: 'block', fontSize: 10, fontWeight: 700, color: lblClr, textTransform: 'uppercase', letterSpacing: '.06em', marginBottom: 4 }}>
              SQL Kısıtı
            </label>
            <textarea
              value={filterText}
              onChange={function(e) { setFilterText(e.target.value) }}
              placeholder={"Örn: IsActive = 1 AND CategoryCode = 'A'"}
              rows={2}
              style={{
                width: '100%', padding: '6px 8px', fontSize: 11,
                background: iBg, border: iBdr, borderRadius: 5,
                color: iClr, outline: 'none', resize: 'vertical',
                fontFamily: 'monospace', boxSizing: 'border-box',
              }}
            />
            <div style={{ fontSize: 10, color: lblClr, marginTop: 3 }}>WHERE klauzülüne eklenecek koşul.</div>
          </div>

          {error && (
            <div style={{ padding: '6px 10px', fontSize: 11, color: '#f87171', background: 'rgba(239,68,68,0.08)', borderRadius: 5, border: '1px solid rgba(239,68,68,0.2)' }}>{error}</div>
          )}
        </div>

        {/* Footer */}
        <div style={{ padding: '8px 14px', borderTop: divBdr, background: hdrBg, flexShrink: 0, display: 'flex', gap: 7, alignItems: 'center' }}>
          <button
            type="button"
            onClick={handleSave}
            disabled={!canSave}
            title={!selectedView ? 'Önce bir view seçin' : viewColsLoading ? 'Kolonlar yükleniyor...' : ''}
            style={{
              padding: '6px 16px', fontSize: 12, fontWeight: 600,
              background: canSave ? btnClr : (isLight ? '#e2e8f0' : 'rgba(255,255,255,0.1)'),
              color: canSave ? '#fff' : lblClr,
              border: 'none', borderRadius: 5,
              cursor: canSave ? 'pointer' : 'not-allowed',
            }}
          >
            {saving ? 'Kaydediliyor...' : 'Kaydet'}
          </button>
          <button
            type="button"
            onClick={handleClose}
            style={{ padding: '6px 14px', fontSize: 12, background: 'transparent', color: lblClr, border: iBdr, borderRadius: 5, cursor: 'pointer' }}
          >
            İptal
          </button>
        </div>
      </div>
    </div>,
    document.body
  )
}
