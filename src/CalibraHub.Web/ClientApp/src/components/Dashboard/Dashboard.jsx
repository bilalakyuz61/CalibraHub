/**
 * Dashboard — Ana sayfa özelleştirilebilir pano (üst düzey konteyner).
 *
 * Mimari: Shell.jsx, hiç sekme açık değilken (isHomePage) bu bileşeni EmptyState
 * yerine doğrudan render eder — iframe YOK, ikinci mount YOK (Matruşka riski yok).
 *
 * Çok sayfalı yapı (2026-06-15):
 *   - Backend { pages: [{id,label,widgets:[]}] } döner.
 *   - activePageId ile aktif sayfa seçilir; widget grid yalnızca aktif sayfanın widget'larını gösterir.
 *   - Düzenleme modunda sekme çubuğu: etiket yeniden adlandırma (aktif sekme input),
 *     sayfa silme (×) ve yeni sayfa ekleme (+ Sayfa) butonları.
 *   - Otomatik kayıt (700ms debounce) tüm sayfalar için tek POST yapar.
 */
import { useState, useEffect, useRef, useCallback } from 'react'
import { LayoutDashboard, Pencil, Check, Plus, RotateCcw, Loader2, AlertTriangle, X } from 'lucide-react'
import * as dashboardService from './dashboardService'
import WidgetGrid from './WidgetGrid'
import WidgetCatalogModal from './WidgetCatalogModal'
import ConfirmRemoveModal from './ConfirmRemoveModal'
import { getWidgetMeta } from './widgetRegistry'
import './Dashboard.css'

function detectDark() {
  if (typeof document === 'undefined') return false
  return document.body.classList.contains('app-theme-dark') ||
         document.documentElement.classList.contains('dark')
}

export default function Dashboard(props) {
  var config = props.config || {}
  var user = config.user || { name: '—', initials: '?' }
  var system = config.system || {}
  var lang = (config.lang || 'tr-TR').toLowerCase().indexOf('en') === 0 ? 'EN' : 'TR'

  var [serverConfig, setServerConfig] = useState(null) // { availableWidgets, quickLinkOptions }
  var [pages, setPages] = useState([])                 // [{ id, label, widgets: [...] }]
  var [activePageId, setActivePageId] = useState(null)
  var [loading, setLoading] = useState(true)
  var [loadError, setLoadError] = useState(null)
  var [editMode, setEditMode] = useState(false)
  var [catalogOpen, setCatalogOpen] = useState(false)
  var [saving, setSaving] = useState(false)
  var [dirty, setDirty] = useState(false)
  var [removeIdx, setRemoveIdx] = useState(null)
  var [resetAsk, setResetAsk] = useState(false)

  // ── Türetilmiş: aktif sayfa ve widget listesi ──
  var activePage = pages.find(function(p) { return p.id === activePageId }) || { id: '', label: '', widgets: [] }
  var layout = activePage.widgets || []

  // ── Tema izleme ──
  var [isDark, setIsDark] = useState(detectDark)
  useEffect(function () {
    function sync() { setIsDark(detectDark()) }
    sync()
    var obs = new MutationObserver(sync)
    obs.observe(document.body, { attributes: true, attributeFilter: ['class'] })
    obs.observe(document.documentElement, { attributes: true, attributeFilter: ['class'] })
    return function () { obs.disconnect() }
  }, [])

  // ── Config yükle ──
  var loadConfig = useCallback(function () {
    setLoading(true)
    setLoadError(null)
    dashboardService.getConfig()
      .then(function (d) {
        setServerConfig({
          availableWidgets: (d && d.availableWidgets) || [],
          quickLinkOptions: (d && d.quickLinkOptions) || [],
        })
        var userPages = (d && Array.isArray(d.userPages)) ? d.userPages : []
        setPages(userPages)
        setActivePageId(userPages.length > 0 ? userPages[0].id : null)
        setLoading(false)
        setDirty(false)
      })
      .catch(function (err) {
        setLoadError(err.message || 'Pano yüklenemedi')
        setLoading(false)
      })
  }, [])

  useEffect(function () { loadConfig() }, [loadConfig])

  // ── Debounced autosave ──
  var saveTimerRef = useRef(null)
  var persist = useCallback(function (nextPages) {
    setSaving(true)
    return dashboardService.savePages(nextPages)
      .then(function () { setDirty(false) })
      .catch(function (err) {
        if (window.CalibraHub && window.CalibraHub.toast) {
          window.CalibraHub.toast('Pano kaydedilemedi: ' + (err.message || ''), 'err')
        }
      })
      .finally(function () { setSaving(false) })
  }, [])

  function scheduleAutosave(nextPages) {
    setDirty(true)
    if (saveTimerRef.current) clearTimeout(saveTimerRef.current)
    saveTimerRef.current = setTimeout(function () { persist(nextPages) }, 700)
  }

  useEffect(function () {
    return function () { if (saveTimerRef.current) clearTimeout(saveTimerRef.current) }
  }, [])

  // ── Aktif sayfa widget listesini güncelle ──
  function applyPageWidgets(nextWidgets) {
    var nextPages = pages.map(function (p) {
      return p.id === activePageId ? Object.assign({}, p, { widgets: nextWidgets }) : p
    })
    setPages(nextPages)
    scheduleAutosave(nextPages)
  }

  // ── Widget mutasyonları ──
  function layoutChange(changes) {
    var byIdx = {}
    changes.forEach(function (c) { byIdx[c.idx] = c.layout })
    var next = layout.map(function (w, i) {
      return byIdx[i] ? Object.assign({}, w, { layout: byIdx[i] }) : w
    })
    applyPageWidgets(next)
  }

  function widgetSettingsChange(idx, nextSettings) {
    var next = layout.map(function (w, i) {
      return i === idx ? Object.assign({}, w, { settings: nextSettings }) : w
    })
    applyPageWidgets(next)
  }

  function addWidget(catalogItem) {
    setCatalogOpen(false)
    var meta = getWidgetMeta(catalogItem.type)
    var next = layout.concat([{
      type: catalogItem.type,
      size: catalogItem.defaultSize || (meta && meta.defaultSize) || 'md',
      height: (meta && meta.defaultHeight) || 1,
      settings: {},
    }])
    applyPageWidgets(next)
  }

  function confirmRemove() {
    if (removeIdx === null) return
    var idx = removeIdx
    setRemoveIdx(null)
    var next = layout.filter(function (_, i) { return i !== idx })
    applyPageWidgets(next)
  }

  // ── Sayfa yönetimi ──
  function addPage() {
    var newId = 'page-' + pages.length + '-' + Date.now().toString(36)
    var newLabel = 'Sayfa ' + (pages.length + 1)
    var nextPages = pages.concat([{ id: newId, label: newLabel, widgets: [] }])
    setPages(nextPages)
    setActivePageId(newId)
    scheduleAutosave(nextPages)
  }

  function deletePage(pageId) {
    if (pages.length <= 1) return
    var nextPages = pages.filter(function (p) { return p.id !== pageId })
    setPages(nextPages)
    if (activePageId === pageId) {
      setActivePageId(nextPages[0].id)
    }
    scheduleAutosave(nextPages)
  }

  function renamePage(pageId, newLabel) {
    var nextPages = pages.map(function (p) {
      return p.id === pageId ? Object.assign({}, p, { label: newLabel }) : p
    })
    setPages(nextPages)
    scheduleAutosave(nextPages)
  }

  // ── Edit mode ──
  function toggleEditMode() {
    if (editMode) {
      if (saveTimerRef.current) clearTimeout(saveTimerRef.current)
      if (dirty) persist(pages)
      setEditMode(false)
    } else {
      setEditMode(true)
    }
  }

  function doReset() {
    setResetAsk(false)
    setSaving(true)
    dashboardService.resetPages()
      .then(function (d) {
        setServerConfig({
          availableWidgets: (d && d.availableWidgets) || [],
          quickLinkOptions: (d && d.quickLinkOptions) || [],
        })
        var userPages = (d && Array.isArray(d.userPages)) ? d.userPages : []
        setPages(userPages)
        setActivePageId(userPages.length > 0 ? userPages[0].id : null)
        setDirty(false)
      })
      .catch(function (err) {
        if (window.CalibraHub && window.CalibraHub.toast) {
          window.CalibraHub.toast('Sıfırlanamadı: ' + (err.message || ''), 'err')
        }
      })
      .finally(function () { setSaving(false) })
  }

  // ── Arka plan ──
  var meshStyle = isDark
    ? {
        backgroundColor: '#0a0d17',
        backgroundImage:
          'radial-gradient(at 20% 30%, rgba(99,102,241,0.12) 0px, transparent 50%), ' +
          'radial-gradient(at 80% 20%, rgba(14,165,233,0.08) 0px, transparent 50%), ' +
          'radial-gradient(at 50% 80%, rgba(168,85,247,0.08) 0px, transparent 50%)',
      }
    : {
        backgroundColor: '#f8fafc',
        backgroundImage:
          'radial-gradient(at 20% 30%, rgba(99,102,241,0.05) 0px, transparent 50%), ' +
          'radial-gradient(at 80% 20%, rgba(14,165,233,0.04) 0px, transparent 50%), ' +
          'radial-gradient(at 50% 80%, rgba(168,85,247,0.04) 0px, transparent 50%)',
      }

  var greeting = user.name && user.name !== '—' ? ('Hoş geldin, ' + user.name) : 'Ana Sayfa'

  // Tüm sayfalardaki widget tipleri (katalog AllowMultiple filtresi için)
  var placedTypes = pages.flatMap(function (p) {
    return (p.widgets || []).map(function (w) { return w.type })
  })

  var showPageTabs = pages.length > 1 || editMode

  return (
    <div className="dash-root" style={meshStyle}>
      {/* ── Header ── */}
      <div className="dash-header">
        <div className="dash-header__identity">
          <div
            className="dash-header__icon"
            style={{
              background: 'linear-gradient(135deg, #6366f1 0%, #8b5cf6 100%)',
              boxShadow: '0 6px 16px rgba(99,102,241,0.35)',
            }}
          >
            <LayoutDashboard size={18} className="text-white" style={{ color: '#fff' }} />
          </div>
          <div>
            <h1 className="dash-header__title">Ana Sayfa</h1>
            <p className="dash-header__subtitle">{greeting}</p>
          </div>
        </div>

        <div className="dash-header__spacer" />

        <div className="dash-header__tools">
          {saving && (
            <span className="dash-row__sub" style={{ display: 'inline-flex', alignItems: 'center', gap: 5 }}>
              <Loader2 size={13} style={{ animation: 'spin 1s linear infinite' }} /> Kaydediliyor…
            </span>
          )}
          {editMode && (
            <>
              <button type="button" className="dash-tool-btn" onClick={function () { setCatalogOpen(true) }}>
                <Plus size={15} /> Widget Ekle
              </button>
              <button type="button" className="dash-tool-btn dash-tool-btn--danger" onClick={function () { setResetAsk(true) }}>
                <RotateCcw size={15} /> Sıfırla
              </button>
            </>
          )}
          <button
            type="button"
            className={'dash-tool-btn' + (editMode ? ' dash-tool-btn--primary' : '')}
            onClick={toggleEditMode}
            disabled={loading || !!loadError}
          >
            {editMode ? <Check size={15} /> : <Pencil size={15} />}
            {editMode ? 'Bitti' : 'Düzenle'}
          </button>
        </div>
      </div>

      {/* ── Sayfa sekme çubuğu ── */}
      {showPageTabs && (
        <div className="dash-page-tabs">
          {pages.map(function (page) {
            var isActive = page.id === activePageId
            return (
              <div
                key={page.id}
                className={'dash-page-tab' + (isActive ? ' dash-page-tab--active' : '')}
              >
                {editMode && isActive
                  ? (
                    <input
                      className="dash-page-tab__input"
                      value={page.label}
                      onChange={function (e) { renamePage(page.id, e.target.value) }}
                      style={{ width: Math.max(60, (page.label || '').length * 9) + 'px' }}
                    />
                  )
                  : (
                    <button
                      className="dash-page-tab__btn"
                      onClick={function () { setActivePageId(page.id) }}
                    >
                      {page.label}
                    </button>
                  )
                }
                {editMode && pages.length > 1 && (
                  <button
                    className="dash-page-tab__del"
                    onClick={function () { deletePage(page.id) }}
                    title={'Sayfayı sil: ' + page.label}
                  >
                    <X size={11} />
                  </button>
                )}
              </div>
            )
          })}
          {editMode && (
            <button className="dash-page-add-btn" onClick={addPage}>
              <Plus size={13} /> Sayfa
            </button>
          )}
        </div>
      )}

      {/* ── Gövde ── */}
      <div className="dash-scroll">
        {loading ? (
          <div className="dash-empty">
            <Loader2 size={28} style={{ animation: 'spin 1s linear infinite', color: '#6366f1' }} />
            <span className="dash-empty__sub">Pano yükleniyor…</span>
          </div>
        ) : loadError ? (
          <div className="dash-empty">
            <AlertTriangle size={30} style={{ color: '#f59e0b' }} />
            <div className="dash-empty__title">Pano yüklenemedi</div>
            <div className="dash-empty__sub">{loadError}</div>
            <button type="button" className="dash-tool-btn" style={{ marginTop: 8 }} onClick={loadConfig}>
              <RotateCcw size={14} /> Tekrar Dene
            </button>
          </div>
        ) : layout.length === 0 ? (
          <div className="dash-empty">
            <LayoutDashboard size={42} strokeWidth={1.2} style={{ opacity: 0.4 }} />
            <div className="dash-empty__title">Bu sayfa boş</div>
            <div className="dash-empty__sub">Widget eklemek için düzenleme modunu açın</div>
            <button
              type="button"
              className="dash-tool-btn dash-tool-btn--primary"
              style={{ marginTop: 8 }}
              onClick={function () { setEditMode(true); setCatalogOpen(true) }}
            >
              <Plus size={15} /> Widget Ekle
            </button>
          </div>
        ) : (
          <WidgetGrid
            layout={layout}
            editMode={editMode}
            isDark={isDark}
            lang={lang}
            user={user}
            system={system}
            quickLinkOptions={serverConfig ? serverConfig.quickLinkOptions : null}
            onLayoutChange={layoutChange}
            onRemoveRequest={function (idx) { setRemoveIdx(idx) }}
            onWidgetSettingsChange={widgetSettingsChange}
          />
        )}
      </div>

      {/* ── Katalog modalı ── */}
      <WidgetCatalogModal
        open={catalogOpen}
        isDark={isDark}
        catalog={serverConfig ? serverConfig.availableWidgets : []}
        placedTypes={placedTypes}
        onAdd={addWidget}
        onClose={function () { setCatalogOpen(false) }}
      />

      {/* ── Widget kaldırma onayı (CLAUDE.md merkezi modal) ── */}
      <ConfirmRemoveModal
        open={removeIdx !== null}
        variant="danger"
        title="Widget'ı Kaldır"
        message={removeIdx !== null && layout[removeIdx]
          ? ('"' + ((getWidgetMeta(layout[removeIdx].type) || {}).title || layout[removeIdx].type) + '" panodan kaldırılsın mı?')
          : ''}
        okLabel="Kaldır"
        onConfirm={confirmRemove}
        onCancel={function () { setRemoveIdx(null) }}
      />

      {/* ── Sıfırlama onayı ── */}
      <ConfirmRemoveModal
        open={resetAsk}
        variant="primary"
        title="Panoyu Sıfırla"
        message="Tüm sayfa düzeniniz varsayılana döndürülecek. Devam edilsin mi?"
        okLabel="Sıfırla"
        onConfirm={doReset}
        onCancel={function () { setResetAsk(false) }}
      />
    </div>
  )
}
