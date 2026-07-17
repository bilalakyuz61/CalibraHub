/**
 * ViewBuilder — SQL View Yönetimi (Faz 2 React ekranı).
 *
 * CLAUDE.md "SQL View Yönetimi — Kontrollü İstisna" (2026-07-17). Backend Faz 1
 * zaten canlı (SystemAdmin-only, api/view-builder/*); bu bileşen o kilitli
 * kontrata karşı yazılmış hibrit bir kurucu ekranıdır:
 *   - Sol panel: mevcut view listesi (sistem view'ları + önceden oluşturulmuş
 *     özel view'lar), override rozetleri, arama, "+ Yeni Özel View".
 *   - Sağ panel: Görsel Kurucu (taban nesne + join'ler + kolonlar) veya
 *     Gelişmiş SQL (üretilen/serbest SQL metni) sekmesi + Önizleme + Kaydet/Geri Al.
 *
 * Enum benzeri alanlar (Kind, join.type, on.op) backend'de zaten düz string
 * olarak tanımlı (bkz. ViewBuilderContracts.cs) — CLAUDE.md'nin enum normalize
 * kuralı burada geçerli değil (integer/enum karışıklığı yok).
 */
import React, { useState, useEffect, useMemo, useCallback } from 'react'
import {
  Layers, Search, Plus, RefreshCw, Loader2, Eye, Save, RotateCcw,
  Code2, AlertTriangle, ChevronDown, ChevronRight, X, Sparkles, Info,
} from 'lucide-react'
import ViewBuilderVisual from './ViewBuilderVisual'
import ViewBuilderPreview from './ViewBuilderPreview'

const IDENTIFIER_RE = /^[A-Za-z_][A-Za-z0-9_]{0,127}$/

function isValidIdentifier(s) {
  return typeof s === 'string' && IDENTIFIER_RE.test(s.trim())
}

function emptyDefinition() {
  return { baseObject: null, joins: [], columns: [] }
}

function tryParseDefinition(json) {
  if (!json) return null
  try {
    var obj = JSON.parse(json)
    if (obj && obj.baseObject && obj.baseObject.schema && obj.baseObject.name) {
      return {
        baseObject: obj.baseObject,
        joins: Array.isArray(obj.joins) ? obj.joins : [],
        columns: Array.isArray(obj.columns) ? obj.columns : [],
      }
    }
    return null
  } catch (e) {
    return null
  }
}

function seedDefinitionFromViewName(name) {
  return { baseObject: { schema: 'dbo', name: name, alias: 'base' }, joins: [], columns: [] }
}

function apiGet(apiBase, path) {
  return fetch(apiBase + path, { credentials: 'same-origin' }).then(function (r) {
    if (r.ok) return r.json()
    return r.json().catch(function () { return null }).then(function (d) {
      throw new Error((d && d.message) || ('İstek başarısız (' + r.status + ')'))
    })
  })
}

function apiPost(apiBase, path, body) {
  return fetch(apiBase + path, {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body || {}),
  }).then(function (r) {
    return r.json().catch(function () { return null }).then(function (d) {
      if (!r.ok && !d) throw new Error('İstek başarısız (' + r.status + ')')
      return d || {}
    })
  })
}

function toast(msg, kind) {
  if (window.CalibraHub && typeof window.CalibraHub.toast === 'function') window.CalibraHub.toast(msg, kind)
}

function confirmDialog(opts) {
  if (typeof window.showConfirm === 'function') return window.showConfirm(opts)
  return Promise.resolve(window.confirm(opts.message))
}

function fmtDate(iso) {
  if (!iso) return null
  try { return new Date(iso).toLocaleString('tr-TR') } catch (e) { return iso }
}

export default function ViewBuilder({ config }) {
  var apiBase = (config && config.apiBase) || '/api/view-builder'
  var get = useCallback(function (path) { return apiGet(apiBase, path) }, [apiBase])
  var post = useCallback(function (path, body) { return apiPost(apiBase, path, body) }, [apiBase])

  // ── View listesi ─────────────────────────────────────────────────────
  var [views, setViews] = useState([])
  var [viewsLoading, setViewsLoading] = useState(true)
  var [viewsError, setViewsError] = useState(null)
  var [search, setSearch] = useState('')

  // ── Şema yardımcıları (dropdown kaynakları) ─────────────────────────
  var [schemaTables, setSchemaTables] = useState([])
  var [columnsCache, setColumnsCache] = useState({})

  // ── Seçili view / yeni view akışı ───────────────────────────────────
  var [selectedViewName, setSelectedViewName] = useState(null)
  var [creatingNew, setCreatingNew] = useState(false)
  var [newViewName, setNewViewName] = useState('')
  var [detail, setDetail] = useState(null)
  var [detailLoading, setDetailLoading] = useState(false)
  var [showCurrentSql, setShowCurrentSql] = useState(false)

  // ── Kurucu state ─────────────────────────────────────────────────────
  var [kind, setKind] = useState('SystemExtend')
  var [activeTab, setActiveTab] = useState('visual')
  var [definition, setDefinition] = useState(emptyDefinition())
  var [rawSql, setRawSql] = useState('')
  var [isRawMode, setIsRawMode] = useState(false)

  // ── Önizleme / kaydet / geri al ─────────────────────────────────────
  var [preview, setPreview] = useState(null)
  var [previewLoading, setPreviewLoading] = useState(false)
  var [previewStale, setPreviewStale] = useState(false)
  var [applying, setApplying] = useState(false)
  var [reverting, setReverting] = useState(false)
  var [seedingRaw, setSeedingRaw] = useState(false)

  var loadViews = useCallback(function () {
    setViewsLoading(true)
    setViewsError(null)
    get('/views')
      .then(function (list) { setViews(list || []) })
      .catch(function (e) { setViewsError(String(e.message || e)) })
      .finally(function () { setViewsLoading(false) })
  }, [get])

  var loadSchemaTables = useCallback(function () {
    return get('/schema/tables')
      .then(function (list) { setSchemaTables(list || []) })
      .catch(function () { /* sessiz — dropdown boş kalır, önizleme hatası zaten görünür */ })
  }, [get])

  useEffect(function () {
    loadViews()
    loadSchemaTables()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // ── Alias → tablo haritası (görsel kurucu dropdown'ları için) ───────
  var aliasMap = useMemo(function () {
    var map = {}
    if (definition.baseObject && definition.baseObject.alias) {
      map[definition.baseObject.alias] = { schema: definition.baseObject.schema, name: definition.baseObject.name }
    }
    ;(definition.joins || []).forEach(function (j) {
      if (j.alias && j.schema && j.name) map[j.alias] = { schema: j.schema, name: j.name }
    })
    return map
  }, [definition])

  var ensureColumns = useCallback(function (schema, name) {
    if (!schema || !name) return
    var key = schema + '.' + name
    setColumnsCache(function (prev) {
      if (Object.prototype.hasOwnProperty.call(prev, key)) return prev
      var next = Object.assign({}, prev)
      next[key] = null // yukleniyor isareti
      get('/schema/tables/' + encodeURIComponent(schema) + '/' + encodeURIComponent(name) + '/columns')
        .then(function (cols) {
          setColumnsCache(function (p2) { var n2 = Object.assign({}, p2); n2[key] = cols || []; return n2 })
        })
        .catch(function () {
          setColumnsCache(function (p2) { var n2 = Object.assign({}, p2); n2[key] = []; return n2 })
        })
      return next
    })
  }, [get])

  useEffect(function () {
    Object.keys(aliasMap).forEach(function (alias) {
      var ref = aliasMap[alias]
      ensureColumns(ref.schema, ref.name)
    })
  }, [aliasMap, ensureColumns])

  // ── Detay yükleme + kurucu state'e uygulama ─────────────────────────
  function applyDetailToBuilder(d) {
    if (d.hasOverride && d.override) {
      var ov = d.override
      setKind(ov.kind || 'SystemExtend')
      if (ov.isRawMode) {
        setIsRawMode(true)
        setActiveTab('raw')
        setRawSql(ov.generatedSql || '')
        setDefinition(tryParseDefinition(ov.definitionJson) || seedDefinitionFromViewName(d.viewName))
      } else {
        var parsed = tryParseDefinition(ov.definitionJson)
        if (parsed) {
          setIsRawMode(false)
          setActiveTab('visual')
          setDefinition(parsed)
          setRawSql(ov.generatedSql || '')
        } else {
          // Görsel tanım parse edilemedi (bozuk/eski JSON) → gelişmiş SQL'e düş.
          setIsRawMode(true)
          setActiveTab('raw')
          setRawSql(ov.generatedSql || d.currentSql || '')
          setDefinition(seedDefinitionFromViewName(d.viewName))
        }
      }
    } else {
      // Hiç özelleştirme yok — sistem view'ı ilk kez genişletiliyor.
      // Güvenli varsayılan: mevcut SQL'i olduğu gibi göster (Gelişmiş SQL sekmesi).
      // Görsel kurucuya geçilirse taban nesne olarak view'ın kendisi seçili gelir.
      setKind('SystemExtend')
      setIsRawMode(true)
      setActiveTab('raw')
      setRawSql(d.currentSql || '')
      setDefinition(seedDefinitionFromViewName(d.viewName))
    }
    setPreview(null)
    setPreviewStale(false)
  }

  function selectView(viewName) {
    setCreatingNew(false)
    setNewViewName('')
    setSelectedViewName(viewName)
    setShowCurrentSql(false)
    setDetailLoading(true)
    setDetail(null)
    get('/views/' + encodeURIComponent(viewName))
      .then(function (d) {
        setDetail(d)
        applyDetailToBuilder(d)
      })
      .catch(function (e) {
        toast('View tanımı alınamadı: ' + (e.message || e), 'err')
      })
      .finally(function () { setDetailLoading(false) })
  }

  function startNewView() {
    setSelectedViewName(null)
    setDetail(null)
    setCreatingNew(true)
    setNewViewName('')
    setKind('Custom')
    setIsRawMode(false)
    setActiveTab('visual')
    setDefinition(emptyDefinition())
    setRawSql('')
    setPreview(null)
    setPreviewStale(false)
  }

  function cancelNewView() {
    setCreatingNew(false)
    setNewViewName('')
    setDefinition(emptyDefinition())
    setRawSql('')
    setPreview(null)
  }

  // ── Definition/rawSql değişince önizleme "güncel değil" işaretlenir ──
  useEffect(function () {
    setPreviewStale(function (prevStale) { return preview ? true : prevStale })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [definition, rawSql, isRawMode])

  function handleDefinitionChange(next) {
    setDefinition(next)
  }

  function handleRawSqlChange(value) {
    setRawSql(value)
    if (!isRawMode) setIsRawMode(true)
  }

  function switchToVisual() {
    setIsRawMode(false)
    setActiveTab('visual')
  }

  function buildDefinitionPayload() {
    if (!definition || !definition.baseObject) return null
    return { baseObject: definition.baseObject, joins: definition.joins || [], columns: definition.columns || [] }
  }

  function currentViewNameForApi() {
    return creatingNew ? (newViewName.trim() || null) : selectedViewName
  }

  function seedRawFromVisual() {
    if (!definition.baseObject) return
    setSeedingRaw(true)
    post('/preview', {
      isRawMode: false,
      definition: buildDefinitionPayload(),
      rawSql: null,
      viewName: currentViewNameForApi(),
      top: 1,
    }).then(function (result) {
      if (result && result.sql) setRawSql(result.sql)
      else if (result && result.errors && result.errors.length) toast(result.errors.join(' '), 'err')
    }).catch(function (e) {
      toast('SQL oluşturulamadı: ' + (e.message || e), 'err')
    }).finally(function () { setSeedingRaw(false) })
  }

  // ── Önizle ───────────────────────────────────────────────────────────
  var hasVisualContent = !!(definition && definition.baseObject) && (definition.columns || []).length > 0
  var hasRawContent = !!(rawSql && rawSql.trim())
  var canPreview = isRawMode ? hasRawContent : hasVisualContent
  var canSave = canPreview && (!creatingNew || isValidIdentifier(newViewName))

  function runPreview() {
    if (!canPreview) return
    setPreviewLoading(true)
    post('/preview', {
      isRawMode: isRawMode,
      definition: buildDefinitionPayload(),
      rawSql: rawSql || null,
      viewName: currentViewNameForApi(),
      top: 20,
    }).then(function (result) {
      setPreview(result)
      setPreviewStale(false)
    }).catch(function (e) {
      setPreview({
        success: false, errors: [String(e.message || e)], sql: null, columns: [], sampleRows: [],
        totalRowCount: null, baseRowCount: null, cartesianFactor: null, cartesianWarning: false,
      })
      setPreviewStale(false)
    }).finally(function () { setPreviewLoading(false) })
  }

  // ── Kaydet ───────────────────────────────────────────────────────────
  function doApply() {
    var vName = currentViewNameForApi()
    setApplying(true)
    post('/apply', {
      viewName: vName,
      kind: kind,
      isRawMode: isRawMode,
      definition: buildDefinitionPayload(),
      rawSql: rawSql || null,
    }).then(function (result) {
      if (result && result.success) {
        var msg = result.cartesianWarning
          ? 'Kaydedildi — kartezyen çarpım uyarısı sürüyor' + (result.cartesianFactor != null ? ' (' + result.cartesianFactor + '×).' : '.')
          : 'Kaydedildi.'
        toast(msg, result.cartesianWarning ? 'warn' : 'ok')
        loadViews()
        loadSchemaTables()
        selectView(vName)
      } else {
        var errMsg = (result && result.errors && result.errors.length) ? result.errors.join(' ') : 'Kaydetme başarısız.'
        toast(errMsg, 'err')
      }
    }).catch(function (e) {
      toast('Kaydetme hatası: ' + (e.message || e), 'err')
    }).finally(function () { setApplying(false) })
  }

  function runApply() {
    if (!canSave) return
    if (preview && preview.cartesianWarning) {
      confirmDialog({
        title: 'Kartezyen çarpım uyarısı',
        message: 'Son önizlemede satır sayısı' + (preview.cartesianFactor != null ? ' ' + preview.cartesianFactor + '×' : '') +
          ' arttı. ON koşulları eksik/yanlış olabilir. Yine de kaydetmek istiyor musunuz?',
        okLabel: 'Yine de Kaydet',
        cancelLabel: 'Vazgeç',
        danger: true,
      }).then(function (ok) { if (ok) doApply() })
    } else {
      doApply()
    }
  }

  // ── Geri Al ──────────────────────────────────────────────────────────
  function runRevert() {
    if (!detail || !detail.override || !detail.override.id) return
    var vName = detail.viewName
    confirmDialog({
      title: 'Önceki sürüme geri al',
      message: '"' + vName + '" için özelleştirme son kayıtlı revizyona geri alınacak. Bu işlem de yeni bir revizyon olarak loglanır. Devam edilsin mi?',
      okLabel: 'Geri Al',
      cancelLabel: 'Vazgeç',
      danger: true,
    }).then(function (ok) {
      if (!ok) return
      setReverting(true)
      post('/revert/' + detail.override.id, {})
        .then(function (result) {
          if (result && result.success) {
            toast('Geri alındı.', 'ok')
            loadViews()
            selectView(vName)
          } else {
            var msg = (result && result.errors && result.errors.length) ? result.errors.join(' ') : 'Geri alma başarısız.'
            toast(msg, 'err')
          }
        })
        .catch(function (e) { toast('Hata: ' + (e.message || e), 'err') })
        .finally(function () { setReverting(false) })
    })
  }

  // ── Sidebar filtre ───────────────────────────────────────────────────
  var filteredViews = useMemo(function () {
    var q = search.trim().toLowerCase()
    if (!q) return views
    return views.filter(function (v) { return (v.schema + '.' + v.name).toLowerCase().indexOf(q) !== -1 })
  }, [views, search])

  function displayName(viewName) {
    var found = views.find(function (v) { return String(v.name).toLowerCase() === String(viewName).toLowerCase() })
    return found ? (found.schema + '.' + found.name) : viewName
  }

  function kindBadge(k) {
    if (k === 'Custom') return <span className="vb-badge vb-badge-violet">Özel View</span>
    return <span className="vb-badge vb-badge-indigo">Sistem View Genişletmesi</span>
  }

  var showDetailPanel = creatingNew || !!selectedViewName

  return (
    <div className="vb-root">
      {/* ── Üst başlık ────────────────────────────────────────────── */}
      <div className="vb-header">
        <div className="vb-header-icon"><Layers size={19} /></div>
        <div className="vb-header-titles">
          <div className="vb-header-title">SQL View Yönetimi</div>
          <div className="vb-header-sub">
            {viewsLoading ? 'Yükleniyor…' : (views.length + ' view · ' + views.filter(function (v) { return v.hasOverride }).length + ' özelleştirilmiş')}
          </div>
        </div>
        <div className="vb-header-actions">
          <button type="button" className="vb-btn vb-btn-ghost vb-btn-icon" title="Listeyi yenile" onClick={function () { loadViews(); loadSchemaTables() }}>
            {viewsLoading ? <Loader2 size={14} className="vb-spin" /> : <RefreshCw size={14} />}
          </button>
        </div>
      </div>

      <div className="vb-body">
        {/* ── Sol panel — view listesi ────────────────────────────── */}
        <div className="vb-sidebar">
          <div className="vb-sidebar-search">
            <Search size={13} />
            <input placeholder="Şema.view ara…" value={search} onChange={function (e) { setSearch(e.target.value) }} />
          </div>
          <button type="button" className="vb-btn vb-btn-primary vb-sidebar-newbtn" onClick={startNewView}>
            <Plus size={14} /> Yeni Özel View
          </button>

          <div className="vb-sidebar-list">
            {viewsLoading && <div className="vb-sidebar-loading"><Loader2 size={14} className="vb-spin" /> Yükleniyor…</div>}
            {!viewsLoading && viewsError && <div className="vb-sidebar-empty">{viewsError}</div>}
            {!viewsLoading && !viewsError && filteredViews.length === 0 && (
              <div className="vb-sidebar-empty">Eşleşen view bulunamadı.</div>
            )}
            {!viewsLoading && !viewsError && filteredViews.map(function (v) {
              var isActive = !creatingNew && selectedViewName && String(selectedViewName).toLowerCase() === String(v.name).toLowerCase()
              return (
                <button key={v.schema + '.' + v.name} type="button"
                        className={'vb-sidebar-item' + (isActive ? ' active' : '')}
                        onClick={function () { selectView(v.name) }}>
                  <div className="vb-sidebar-item-name">{v.schema}.{v.name}</div>
                  <div className="vb-sidebar-item-badges">
                    {v.hasOverride
                      ? (v.overrideKind === 'Custom'
                          ? <span className="vb-badge vb-badge-violet">Özel</span>
                          : <span className="vb-badge vb-badge-indigo">Genişletildi</span>)
                      : <span className="vb-badge vb-badge-slate">Sistem</span>}
                  </div>
                </button>
              )
            })}
          </div>
        </div>

        {/* ── Sağ panel — kurucu ───────────────────────────────────── */}
        <div className="vb-main">
          {!showDetailPanel && (
            <div className="vb-empty-state">
              <div className="vb-empty-state-inner">
                <Layers size={40} color="var(--vb-border)" />
                <p>Sol taraftan bir view seçin ya da <strong>Yeni Özel View</strong> oluşturun.</p>
              </div>
            </div>
          )}

          {showDetailPanel && detailLoading && (
            <div className="vb-loading-panel"><Loader2 size={18} className="vb-spin" /> View tanımı yükleniyor…</div>
          )}

          {showDetailPanel && !detailLoading && !creatingNew && !detail && (
            <div className="vb-empty-state">
              <div className="vb-empty-state-inner">
                <AlertTriangle size={40} color="var(--vb-rose)" />
                <p>View tanımı yüklenemedi. Sol taraftan tekrar seçmeyi deneyin.</p>
              </div>
            </div>
          )}

          {showDetailPanel && !detailLoading && (creatingNew || detail) && (
            <>
              {/* ── Detay başlığı ─────────────────────────────────── */}
              <div className="vb-detail-header">
                <div className="vb-detail-title-block">
                  {creatingNew ? (
                    <>
                      <input className="vb-newname-input" placeholder="yeni_view_adi" value={newViewName}
                             onChange={function (e) { setNewViewName(e.target.value) }} autoFocus />
                      {newViewName && !isValidIdentifier(newViewName) && (
                        <div className="vb-newname-hint">Geçerli bir tanımlayıcı girin (harfle/alt çizgiyle başlar, yalnız harf/sayı/alt çizgi).</div>
                      )}
                      <div className="vb-detail-meta">{kindBadge('Custom')}</div>
                    </>
                  ) : (
                    <>
                      <div className="vb-detail-title-row">
                        <div className="vb-detail-title">{displayName(selectedViewName)}</div>
                        {kindBadge(kind)}
                        {detail && !detail.existsInDb && <span className="vb-badge vb-badge-amber">DB'de Yok (taslak)</span>}
                      </div>
                      {detail && detail.hasOverride && detail.override && (
                        <div className="vb-detail-meta">
                          {detail.override.updatedBy || detail.override.createdBy || 'bilinmiyor'}
                          {' · '}{fmtDate(detail.override.updated || detail.override.created)}
                          {' · '}{detail.override.revisionCount} revizyon
                        </div>
                      )}
                    </>
                  )}
                </div>

                <div className="vb-detail-actions">
                  {creatingNew && (
                    <button type="button" className="vb-btn vb-btn-ghost" onClick={cancelNewView}><X size={14} /> Vazgeç</button>
                  )}
                  {!creatingNew && detail && detail.hasOverride && detail.override && (
                    <button type="button" className="vb-btn vb-btn-danger" disabled={reverting} onClick={runRevert}>
                      {reverting ? <Loader2 size={14} className="vb-spin" /> : <RotateCcw size={14} />} Geri Al
                    </button>
                  )}
                  <button type="button" className="vb-btn vb-btn-primary" disabled={!canSave || applying} onClick={runApply}
                          title={!canSave ? (creatingNew && !isValidIdentifier(newViewName) ? 'Geçerli bir view adı girin' : 'Önce bir kaynak/SQL tanımlayın') : undefined}>
                    {applying ? <Loader2 size={14} className="vb-spin" /> : <Save size={14} />} Kaydet
                  </button>
                </div>
              </div>

              {/* ── Mevcut tanım (DB) ─────────────────────────────── */}
              {!creatingNew && detail && detail.currentSql && (
                <div className="vb-current-sql">
                  <button type="button" className="vb-current-sql-toggle" onClick={function () { setShowCurrentSql(!showCurrentSql) }}>
                    {showCurrentSql ? <ChevronDown size={13} /> : <ChevronRight size={13} />} Mevcut Tanım (DB)
                  </button>
                  {showCurrentSql && <pre>{detail.currentSql}</pre>}
                </div>
              )}

              {/* ── Sekmeler ───────────────────────────────────────── */}
              <div className="vb-tabs">
                <button type="button" className={'vb-tab' + (activeTab === 'visual' ? ' active' : '')} onClick={function () { setActiveTab('visual') }}>
                  <Layers size={14} /> Görsel Kurucu
                </button>
                <button type="button" className={'vb-tab' + (activeTab === 'raw' ? ' active' : '')} onClick={function () { setActiveTab('raw') }}>
                  <Code2 size={14} /> Gelişmiş SQL
                </button>
              </div>

              {isRawMode && activeTab === 'visual' && (
                <div className="vb-banner vb-banner-amber">
                  <AlertTriangle size={15} />
                  <span>Ham SQL modu aktif — bu view artık <strong>Gelişmiş SQL</strong> sekmesindeki metni kullanıyor; buradaki görsel kurucu değişiklikleri kaydedilmeyecek.</span>
                  <button type="button" className="vb-btn vb-btn-sm" onClick={switchToVisual}>Görsel Kurucuya Dön</button>
                </div>
              )}

              {activeTab === 'visual' ? (
                <div className={'vb-tab-content' + (isRawMode ? ' vb-disabled' : '')}>
                  <ViewBuilderVisual
                    definition={definition}
                    onChange={handleDefinitionChange}
                    schemaTables={schemaTables}
                    columnsCache={columnsCache}
                    aliasMap={aliasMap}
                    disabled={isRawMode}
                  />
                </div>
              ) : (
                <div className="vb-tab-content">
                  <div className="vb-raw-toolbar">
                    {!isRawMode && (
                      <button type="button" className="vb-btn vb-btn-sm" disabled={!definition.baseObject || seedingRaw} onClick={seedRawFromVisual}>
                        {seedingRaw ? <Loader2 size={12} className="vb-spin" /> : <Sparkles size={12} />} SQL'i Görselden Oluştur
                      </button>
                    )}
                    {isRawMode && (
                      <button type="button" className="vb-btn vb-btn-sm" onClick={switchToVisual}>Görsel Kurucuya Dön</button>
                    )}
                    {!isRawMode && (
                      <span className="vb-help-text" style={{ marginTop: 0 }}>
                        <Info size={12} /> Bu metni elle düzenlemeye başladığınızda ham SQL modu devreye girer.
                      </span>
                    )}
                  </div>
                  <textarea className="vb-raw-textarea" spellCheck={false} value={rawSql}
                            placeholder="SELECT ..."
                            onChange={function (e) { handleRawSqlChange(e.target.value) }} />
                </div>
              )}

              {/* ── Önizleme ───────────────────────────────────────── */}
              <div className="vb-preview-toolbar">
                <span className="vb-preview-toolbar-title">Önizleme</span>
                {previewStale && preview && <span className="vb-badge vb-badge-amber">güncel değil</span>}
                <button type="button" className="vb-btn vb-btn-sm" disabled={!canPreview || previewLoading} onClick={runPreview}
                        title={!canPreview ? 'Önce bir kaynak/SQL tanımlayın' : undefined}>
                  {previewLoading ? <Loader2 size={12} className="vb-spin" /> : <Eye size={12} />} Önizle
                </button>
              </div>
              <div className="vb-preview-panel">
                <ViewBuilderPreview preview={preview} loading={previewLoading} />
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  )
}
