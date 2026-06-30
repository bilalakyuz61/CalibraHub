/**
 * SmartBoard — Generic entity list container with server-side pagination.
 *
 * Props:
 *   {
 *     boardKey:  'logistics-material-cards',  // localStorage scope
 *     title:     'Malzeme Kartlari',
 *     subtitle:  '4 kayit',
 *     icon:      'Package',
 *     iconColor: 'indigo',
 *     actions:   [ { id, label, icon, variant, url } ],
 *     entities:  [ { ...SmartCardProps, widgets: [master list from C#] } ],
 *     emptyText: 'Sonuc bulunamadi',
 *     // Pagination (optional — omit for client-only mode):
 *     apiUrl:     '/Finance/GetContactsPage',
 *     totalCount: 300000,
 *     pageSize:   50,
 *   }
 */
import { useState, useMemo, useEffect, useCallback, useRef } from 'react'
import { Search, Settings2, Loader2, ChevronDown, Filter, X, Download, FileSpreadsheet } from 'lucide-react'
import SmartCard from './SmartCard'
import SmartBoardConfigPanel from './SmartBoardConfigPanel'
import SmartBoardFilterPanel, { describeFilter, entityMatchesFilters } from './SmartBoardFilterPanel'
import { resolveIcon, resolveColor } from './DynamicWidgetFactory'
import { loadWidgetConfig } from '../../services/widgetConfigService'
import { navigateInWorkspace } from '../../utils/workspaceNav'

var FILTER_STORAGE_PREFIX = 'cb-sb-filters:'
function loadInitialFilters(boardKey) {
  if (!boardKey || typeof window === 'undefined') return []
  try {
    var raw = window.localStorage.getItem(FILTER_STORAGE_PREFIX + boardKey)
    if (!raw) return []
    var arr = JSON.parse(raw)
    return Array.isArray(arr) ? arr : []
  } catch (e) { return [] }
}

export default function SmartBoard(props) {
  var boardKey = props.boardKey || 'default-board'
  var title = props.title || ''
  var iconHint = props.icon || 'CircleDot'
  var iconColor = props.iconColor || 'indigo'
  var actions = Array.isArray(props.actions) ? props.actions : []
  var initialEntities = Array.isArray(props.entities) ? props.entities : []
  var emptyText = props.emptyText || 'Kayit bulunamadi'
  var searchable = props.searchable !== false
  var searchPlaceholder = props.searchPlaceholder || 'Ara...'

  // In-place refresh
  var refreshUrl = props.refreshUrl || null

  // Pagination props
  var apiUrl = props.apiUrl || null
  var initialTotalCount = props.totalCount || 0
  var pageSize = props.pageSize || 50
  var isPaginated = !!apiUrl
  // skipInitialFetch — initial entities zaten dolu, mount aninda fetchPage(1) atlanir.
  // Combined payload pattern: config + ilk sayfa tek istek geldiginde aktif edilir.
  var skipInitialFetch = props.skipInitialFetch === true

  var HeaderIcon = resolveIcon(iconHint)
  var headerPalette = resolveColor(iconColor)

  var [search, setSearch] = useState('')
  var [configOpen, setConfigOpen] = useState(false)
  var [userConfig, setUserConfig] = useState(null)

  // ── Filter state (hayalet mod) ──
  // localStorage'dan initial yukleme — sayfa arasi tercih korunur (boardKey scope)
  var [filterOpen, setFilterOpen] = useState(false)
  var [filters, setFilters] = useState(function () { return loadInitialFilters(boardKey) })

  // formCode — props'tan gelmezse body'nin data-form-code attribute'undan oku.
  // _Layout.cshtml ViewData["FormCode"]'u body'ye yazar; tum SmartBoard sayfalari
  // bu sayede config degisikligi gerektirmeden filter panele FormCode aktarir.
  var formCode = useMemo(function () {
    if (props.formCode) return String(props.formCode)
    if (typeof document !== 'undefined' && document.body) {
      var fc = document.body.getAttribute('data-form-code')
      if (fc) return fc
    }
    return ''
  }, [props.formCode])

  // Pagination state
  var [entities, setEntities] = useState(initialEntities)
  var [totalCount, setTotalCount] = useState(initialTotalCount)
  var [currentPage, setCurrentPage] = useState(1)
  var [loading, setLoading] = useState(false)
  var [hasMore, setHasMore] = useState(isPaginated && initialEntities.length < initialTotalCount)
  var [searchQuery, setSearchQuery] = useState('') // debounced + committed search
  var searchTimerRef = useRef(null)
  var sentinelRef = useRef(null)

  // In-place refresh state
  var [recentIds, setRecentIds] = useState(function () { return new Set() })

  var refreshBoard = useCallback(function (highlightId) {
    if (!refreshUrl) { window.location.reload(); return }
    fetch(refreshUrl, { credentials: 'same-origin' })
      .then(function (r) { return r.json() })
      .then(function (data) {
        var newEntities = Array.isArray(data.entities) ? data.entities : []
        setEntities(newEntities)
        if (highlightId != null) {
          setRecentIds(function (prev) { var n = new Set(prev); n.add(highlightId); return n })
          setTimeout(function () {
            setRecentIds(function (prev) { var n = new Set(prev); n.delete(highlightId); return n })
          }, 1800)
        }
      })
      .catch(function () { window.location.reload() })
  }, [refreshUrl])

  // Theme detection
  var [isDark, setIsDark] = useState(function () {
    if (typeof document === 'undefined') return true
    return document.body.classList.contains('app-theme-dark') ||
           document.documentElement.classList.contains('dark')
  })
  useEffect(function () {
    function sync() {
      setIsDark(
        document.body.classList.contains('app-theme-dark') ||
        document.documentElement.classList.contains('dark')
      )
    }
    sync()
    var obs = new MutationObserver(sync)
    obs.observe(document.body, { attributes: true, attributeFilter: ['class'] })
    obs.observe(document.documentElement, { attributes: true, attributeFilter: ['class'] })
    return function () { obs.disconnect() }
  }, [])

  useEffect(function () {
    var cfg = loadWidgetConfig(boardKey)
    setUserConfig(cfg)
  }, [boardKey])

  // Master widget list
  // 2026-05-24: Backend'den gelen ek field'lar (source, options, group, groupLabel)
  // filter panel multi-select / collapsible icin gerekli — burada KORUNMALI.
  var masterWidgets = useMemo(function () {
    if (Array.isArray(props.masterWidgets)) {
      return props.masterWidgets.map(function (w) {
        return {
          id: w.id, dbId: w.dbId, type: w.type || 'data',
          dataType: w.dataType, icon: w.icon, label: w.label || w.id,
          color: w.color, isPlainField: w.isPlainField,
          // Filter panel icin ek meta:
          source: w.source, options: w.options,
          group: w.group, groupLabel: w.groupLabel,
        }
      })
    }
    if (entities.length === 0) return []
    var seen = {}
    var master = []
    entities.forEach(function (ent) {
      if (!Array.isArray(ent.widgets)) return
      ent.widgets.forEach(function (w) {
        if (!w || !w.id) return
        if (!seen[w.id]) {
          seen[w.id] = true
          master.push({
            id: w.id, type: w.type || 'data', dataType: w.dataType,
            icon: w.icon, label: w.label || w.id, color: w.color,
            source: w.source, options: w.options,
            group: w.group, groupLabel: w.groupLabel,
          })
        }
      })
    })
    return master
  }, [entities, props.masterWidgets])

  // ── Fetch page from API ──
  // İlk fetch tamamlandiginda dis dinleyicilere haber vermek icin
  var firstReadyFiredRef = useRef(false)
  var onReadyCb = props.onReady

  var fetchPage = useCallback(function (page, searchTerm, append) {
    if (!apiUrl || loading) return
    setLoading(true)
    var url = apiUrl + '?page=' + page + '&pageSize=' + pageSize
    if (searchTerm) url += '&search=' + encodeURIComponent(searchTerm)

    fetch(url, { credentials: 'same-origin' })
      .then(function (r) { return r.json() })
      .then(function (data) {
        if (data.error) { console.error('[SmartBoard] API error:', data.error); return }
        var newEntities = Array.isArray(data.entities) ? data.entities : []
        var total = data.totalCount || 0

        if (append) {
          setEntities(function (prev) { return prev.concat(newEntities) })
        } else {
          setEntities(newEntities)
        }
        setTotalCount(total)
        setCurrentPage(page)
        var loadedCount = append ? (page * pageSize) : newEntities.length
        setHasMore(loadedCount < total && newEntities.length > 0)
      })
      .catch(function (err) { console.error('[SmartBoard] fetch error:', err) })
      .finally(function () {
        setLoading(false)
        // Ilk fetch tamamlandi — onReady callback'i bir kez tetikle
        if (!firstReadyFiredRef.current) {
          firstReadyFiredRef.current = true
          if (typeof onReadyCb === 'function') {
            try { onReadyCb() } catch (e) { console.warn('[SmartBoard] onReady callback hata:', e) }
          }
        }
      })
  }, [apiUrl, pageSize, loading, onReadyCb])

  // ── Debounced search → server ──
  useEffect(function () {
    if (!isPaginated) return
    if (searchTimerRef.current) clearTimeout(searchTimerRef.current)
    searchTimerRef.current = setTimeout(function () {
      setSearchQuery(search)
    }, 400)
    return function () { clearTimeout(searchTimerRef.current) }
  }, [search, isPaginated])

  // When searchQuery changes, reset and fetch page 1
  // Initial mount + skipInitialFetch=true → fetch atlanir, onReady hemen tetiklenir
  // (initial entities config payload icinde geldi, double-fetch onlenir).
  var firstSearchEffectRef = useRef(true)
  useEffect(function () {
    if (!isPaginated) return
    if (firstSearchEffectRef.current) {
      firstSearchEffectRef.current = false
      if (skipInitialFetch) {
        // Initial veri zaten var — onReady'i bir kez tetikle, fetch atma.
        if (!firstReadyFiredRef.current) {
          firstReadyFiredRef.current = true
          if (typeof onReadyCb === 'function') {
            try { onReadyCb() } catch (e) { console.warn('[SmartBoard] onReady callback hata:', e) }
          }
        }
        return
      }
    }
    fetchPage(1, searchQuery, false)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [searchQuery])

  // ── Load more ──
  var handleLoadMore = useCallback(function () {
    if (!hasMore || loading) return
    fetchPage(currentPage + 1, searchQuery, true)
  }, [hasMore, loading, currentPage, searchQuery, fetchPage])

  // ── Intersection observer for infinite scroll ──
  // 2026-05-24: Aktif filtre varken auto-load KAPALI — client-side filtering ile
  // birlikte sonsuz loop'a giriyordu ("1 sonuc bulundu, 49 atlandi, sentinel hala
  // gorunur, sonraki sayfayi getir, yine filtrelendi, ..." flickering).
  // Filter aktifken kullanici "Daha Fazla Yukle" butonuna basarak manuel ilerler.
  var hasActiveFilter = Array.isArray(filters) && filters.length > 0
  useEffect(function () {
    if (!isPaginated || !sentinelRef.current) return
    if (hasActiveFilter) return  // filtre aktifken auto-load yok
    var observer = new IntersectionObserver(function (entries) {
      if (entries[0].isIntersecting && hasMore && !loading) {
        handleLoadMore()
      }
    }, { rootMargin: '200px' })
    observer.observe(sentinelRef.current)
    return function () { observer.disconnect() }
  }, [isPaginated, hasMore, loading, handleLoadMore, hasActiveFilter])

  // Client-side filtering — search + filter panel (her iki mod icin)
  // Not: Server-side paginated mode'da search server'da yapilir, ama filter panel
  // her iki modda da CLIENT-SIDE calisir. Server-side filter destegi sonra eklenebilir.
  var filteredEntities = useMemo(function () {
    var arr = entities
    // 1) Search (client-side mode'da) — title/subtitle/description + opsiyonel searchTags
    // searchTags: controller'in entity'ye eklediği gizli ek arama keywords'i
    // (ör. enum kartlarinda endpoint adi + field path'leri). UI'da gosterilmez.
    if (!isPaginated && search.trim()) {
      var q = search.toLowerCase()
      arr = arr.filter(function (e) {
        return (
          (e.title && e.title.toLowerCase().indexOf(q) !== -1) ||
          (e.subtitle && e.subtitle.toLowerCase().indexOf(q) !== -1) ||
          (e.description && e.description.toLowerCase().indexOf(q) !== -1) ||
          (e.searchTags && String(e.searchTags).toLowerCase().indexOf(q) !== -1)
        )
      })
    }
    // 2) Filter panel kurallari (client-side, her iki mod icin)
    if (filters.length > 0) {
      arr = arr.filter(function (e) { return entityMatchesFilters(e, filters) })
    }
    return arr
  }, [search, entities, isPaginated, filters])

  var subtitle = isPaginated
    ? (totalCount > 0 ? totalCount.toLocaleString('tr-TR') + ' cari' : '')
    : (props.subtitle || '')

  var handleActionClick = useCallback(function (action) {
    // Trigger: window.CalibraHub.openXyzModal()  pattern'i ile global modal acar.
    // Server-side config'te action.trigger string'i ile gelir; URL navigate'in alternatifi.
    if (action.trigger === 'convert-orders-modal') {
      var ch = (typeof window !== 'undefined') && window.CalibraHub
      if (ch && typeof ch.openConvertToOrdersModal === 'function') {
        ch.openConvertToOrdersModal({
          onSuccess: function (res) {
            // Basari sonrasi sayfayi yenile — yeni durum (Converted) listede yansimasi icin
            try { window.location.reload() } catch (e) { /* ignore */ }
          },
        })
      } else {
        console.warn('[SmartBoard] openConvertToOrdersModal global fonksiyon bulunamadi')
      }
      return
    }
    // Generic trigger: window.CalibraHub[trigger]() veya window[trigger]() cagrilir.
    // Kullanim: board config'te action.trigger = 'fnName', sayfada window.fnName = function() {...}
    if (action.trigger) {
      var ch2 = (typeof window !== 'undefined') && window.CalibraHub
      var fn = (ch2 && typeof ch2[action.trigger] === 'function')
        ? ch2[action.trigger]
        : (typeof window !== 'undefined' && typeof window[action.trigger] === 'function')
          ? window[action.trigger]
          : null
      if (fn) fn()
      else console.warn('[SmartBoard] trigger fonksiyon bulunamadi:', action.trigger)
      return
    }
    if (action.url) navigateInWorkspace(action.url)
  }, [])

  var handleConfigSaved = useCallback(function (newConfig) {
    setUserConfig(newConfig)
  }, [])

  /* ── Excel (.xlsx) export — server-side ClosedXML uretir, hidden form POST
        ile gonderilir (iframe blob URL kisitlamalarini bypass eder; "Tasinmis,
        duzenlenmis veya silinmis olabilir" hatasinin sebebi).
        - Paginated mode: tum sayfalari ardisik cekip birlestirir
        - Client-only mode: filteredEntities'i dogrudan kullanir
        Kolonlar: Kod (subtitle) + Ad (title) + tum widget alanlari. */
  var [exporting, setExporting] = useState(false)
  var [showExportConfirm, setShowExportConfirm] = useState(false)

  // Esc ile onay modalını kapat
  useEffect(function() {
    if (!showExportConfirm) return
    function onKey(e) { if (e.key === 'Escape') setShowExportConfirm(false) }
    document.addEventListener('keydown', onKey)
    return function() { document.removeEventListener('keydown', onKey) }
  }, [showExportConfirm])

  var handleExportCsv = useCallback(async function () {
    if (exporting) return
    try {
      setExporting(true)

      // 1) Veriyi topla — paginated ise tum sayfalari, degilse filteredEntities'i.
      var allRows = []
      if (isPaginated && apiUrl) {
        var batchSize = Math.min(pageSize > 0 ? pageSize : 50, 200)
        var p = 1
        var maxPages = 200 // 200 * 200 = 40,000 kayit guvenlik valfı
        var fetched = 0
        while (p <= maxPages) {
          var u = apiUrl + '?page=' + p + '&pageSize=' + batchSize
          if (search && search.trim()) u += '&search=' + encodeURIComponent(search.trim())
          // eslint-disable-next-line no-await-in-loop
          var resp = await fetch(u, { credentials: 'same-origin' })
          // eslint-disable-next-line no-await-in-loop
          var data = await resp.json()
          if (!data) break
          if (data.error) { throw new Error(String(data.error)) }
          var ents = Array.isArray(data.entities) ? data.entities : []
          var total = data.totalCount || 0
          allRows = allRows.concat(ents)
          fetched += ents.length
          if (ents.length === 0) break
          if (total > 0 && fetched >= total) break
          p++
        }
        // Aktif filtre panel kurallarini client-side uygula (henuz server-side filter yok)
        if (filters && filters.length > 0) {
          allRows = allRows.filter(function (e) { return entityMatchesFilters(e, filters) })
        }
      } else {
        allRows = filteredEntities || []
      }

      if (!allRows || allRows.length === 0) {
        // Rapor §6.6 — toast fallback
        if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast('Aktarılacak satır yok.', 'warn')
        else try { window.alert('Aktarılacak satır yok.') } catch (_) { /* ignore */ }
        return
      }

      // 2) Kolonlari belirle — master widgets oncelikli, sonra entity widgets'tan ek
      var seen = {}
      var widgetCols = []
      function addCol(id, label) {
        if (!id || seen[id]) return
        seen[id] = true
        widgetCols.push({ id: String(id), label: String(label || id) })
      }
      masterWidgets.forEach(function (w) { if (w) addCol(w.id, w.label) })
      allRows.forEach(function (e) {
        if (!e || !Array.isArray(e.widgets)) return
        e.widgets.forEach(function (w) { if (w) addCol(w.id, w.label) })
      })

      // 3) Server payload — Kod + Ad + widget kolonlari
      var headers = [{ id: '__code', label: 'Kod' }, { id: '__name', label: 'Ad' }]
        .concat(widgetCols.map(function (c) { return { id: c.id, label: c.label } }))

      function valueOf(w) {
        if (!w) return null
        var v = w.value
        if (v === undefined) return null
        return v // backend tip kontrolunu kendisi yapar (string/number/bool/object/array)
      }

      var rows = allRows.map(function (e) {
        var obj = {
          __code: e.subtitle || '',
          __name: e.title    || '',
        }
        if (Array.isArray(e.widgets)) {
          e.widgets.forEach(function (w) {
            if (w && w.id) obj[w.id] = valueOf(w)
          })
        }
        return obj
      })

      var ts = new Date()
      var pad = function (n) { return n < 10 ? '0' + n : String(n) }
      var stamp = ts.getFullYear() + pad(ts.getMonth() + 1) + pad(ts.getDate()) + '_' +
                  pad(ts.getHours()) + pad(ts.getMinutes()) + pad(ts.getSeconds())
      var fileName = (boardKey || 'liste') + '_' + stamp + '.xlsx'
      var sheetName = (title || 'Liste').slice(0, 31)

      var payload = {
        fileName:  fileName,
        sheetName: sheetName,
        headers:   headers,
        rows:      rows,
      }

      // 4) Hidden form POST submission — iframe blob URL kisitlamalarini bypass eder.
      //    Browser dogal navigation handle eder, server Content-Disposition header'i
      //    ile attachment olarak donerse browser dosyayi indirir. CSP / sandbox
      //    'allow-downloads' bayragi yoksa bile bu yontem calisir.
      var token = ''
      var tokenInput = document.querySelector('input[name="__RequestVerificationToken"]')
      if (tokenInput) token = tokenInput.value || ''

      var form = document.createElement('form')
      form.method = 'POST'
      form.action = '/api/export/smartboard-excel'
      form.target = '_self'
      form.style.display = 'none'

      var hidden = document.createElement('textarea')
      hidden.name = 'payload'
      hidden.value = JSON.stringify(payload)
      form.appendChild(hidden)

      if (token) {
        var tokInput = document.createElement('input')
        tokInput.type = 'hidden'
        tokInput.name = '__RequestVerificationToken'
        tokInput.value = token
        form.appendChild(tokInput)
      }

      document.body.appendChild(form)
      form.submit()
      // Submit non-navigating attachment, ama yine de form'u temizle
      setTimeout(function () {
        if (form.parentNode) form.parentNode.removeChild(form)
      }, 1500)
    } catch (err) {
      console.error('[SmartBoard] Export hatasi:', err)
      // Rapor §6.6 — toast fallback
      var em = 'Aktarma sırasında hata: ' + (err && err.message ? err.message : err)
      if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast(em, 'err')
      else try { window.alert(em) } catch (_) { /* ignore */ }
    } finally {
      setExporting(false)
    }
  }, [exporting, isPaginated, apiUrl, pageSize, search, filters, filteredEntities, masterWidgets, boardKey, title])

  var visibleIds = userConfig && Array.isArray(userConfig.visibleIds) ? userConfig.visibleIds : null
  var order = userConfig && Array.isArray(userConfig.order) ? userConfig.order : null

  var meshStyle = isDark
    ? {
        backgroundColor: '#0a0d17',
        backgroundImage:
          'radial-gradient(at 20% 30%, rgba(99,102,241,0.12) 0px, transparent 50%), ' +
          'radial-gradient(at 80% 20%, rgba(14,165,233,0.08) 0px, transparent 50%), ' +
          'radial-gradient(at 50% 80%, rgba(168,85,247,0.08) 0px, transparent 50%), ' +
          'radial-gradient(at 90% 70%, rgba(20,184,166,0.06) 0px, transparent 50%)',
      }
    : {
        backgroundColor: '#f8fafc',
        backgroundImage:
          'radial-gradient(at 20% 30%, rgba(99,102,241,0.05) 0px, transparent 50%), ' +
          'radial-gradient(at 80% 20%, rgba(14,165,233,0.04) 0px, transparent 50%), ' +
          'radial-gradient(at 50% 80%, rgba(168,85,247,0.04) 0px, transparent 50%)',
      }

  return (
    <div className="h-full flex flex-col" style={meshStyle}>

      {/* ── Header ──────────────────────────── */}
      <div className="flex items-center gap-4 px-5 py-3 border-b border-slate-200/60 dark:border-white/[0.06] flex-shrink-0">
        <div className="flex items-center gap-3 flex-shrink-0">
          <div
            className="w-9 h-9 rounded-xl flex items-center justify-center"
            style={{
              background: headerPalette.bg,
              border: '1px solid ' + headerPalette.border,
            }}
          >
            <HeaderIcon size={17} style={{ color: headerPalette.icon }} />
          </div>
          <div>
            <h1 className="text-base font-bold text-slate-800 dark:text-white/90 tracking-tight leading-tight">
              {title}
            </h1>
            {subtitle && (
              <p className="text-[11px] text-slate-500 dark:text-white/45 leading-tight">
                {subtitle}
              </p>
            )}
          </div>
        </div>

        {/* Search */}
        {searchable && (
          <div className="flex-1 max-w-md" data-nodirty>
            <div className="relative">
              <Search size={14} className="absolute left-3.5 top-1/2 -translate-y-1/2 text-slate-400 dark:text-white/40" />
              <input
                type="text"
                value={search}
                onChange={function (e) { setSearch(e.target.value) }}
                placeholder={searchPlaceholder}
                className="w-full pl-10 pr-4 py-2 rounded-xl bg-white/60 dark:bg-white/[0.04] border border-slate-200 dark:border-white/[0.06] text-sm text-slate-700 dark:text-white/70 placeholder:text-slate-400 dark:placeholder:text-white/40 focus:outline-none focus:border-indigo-400/50 dark:focus:border-white/15 transition-colors"
              />
              {isPaginated && loading && search && (
                <Loader2 size={14} className="absolute right-3.5 top-1/2 -translate-y-1/2 text-indigo-400 animate-spin" />
              )}
            </div>
          </div>
        )}

        {!searchable && <div className="flex-1" />}

        {/* Filter button — hayalet mod (low saturation, dot indicator aktifte) */}
        <button
          onClick={function () { setFilterOpen(true) }}
          className={'relative p-2.5 rounded-xl border transition-all group flex-shrink-0 ' +
            (filters.length > 0
              ? 'bg-indigo-50 dark:bg-indigo-500/10 border-indigo-200 dark:border-indigo-400/30'
              : 'bg-white/60 dark:bg-white/[0.04] hover:bg-white/80 dark:hover:bg-white/[0.08] border-slate-200 dark:border-white/[0.06]')
          }
          title={filters.length > 0 ? (filters.length + ' filtre aktif') : 'Filtreleme'}
        >
          <Filter size={15} className={filters.length > 0
            ? 'text-indigo-600 dark:text-indigo-400'
            : 'text-slate-500 dark:text-white/40 group-hover:text-indigo-600 dark:group-hover:text-indigo-400/80 transition-colors'
          } />
          {filters.length > 0 && (
            <span
              className="absolute -top-1 -right-1 min-w-[16px] h-[16px] px-1 rounded-full text-[9px] font-bold bg-indigo-500 text-white flex items-center justify-center"
              style={{ boxShadow: '0 0 0 2px rgba(15,23,42,0.6)' }}
            >
              {filters.length}
            </span>
          )}
        </button>

        {/* Excel/CSV export — paginated mode'da tum sayfalari ardisik ceker;
            UTF-8 BOM + CSV (Excel Tr lokali ile dogrudan acar). Master + sistem
            widget'lari kolon olarak yazilir. Export sirasinda spinner gosterilir. */}
        <button
          onClick={function () { if (!exporting) setShowExportConfirm(true) }}
          disabled={exporting}
          className={'p-2.5 rounded-xl border transition-all group flex-shrink-0 ' +
            (exporting
              ? 'bg-emerald-50 dark:bg-emerald-500/10 border-emerald-200 dark:border-emerald-400/30 cursor-wait'
              : 'bg-white/60 dark:bg-white/[0.04] hover:bg-white/80 dark:hover:bg-white/[0.08] border-slate-200 dark:border-white/[0.06]')
          }
          title={exporting ? 'Aktariliyor...' : "Excel'e Aktar"}
        >
          {exporting
            ? <Loader2 size={15} className="text-emerald-600 dark:text-emerald-400 animate-spin" />
            : <Download size={15} className="text-slate-500 dark:text-white/40 group-hover:text-emerald-600 dark:group-hover:text-emerald-400/80 transition-colors" />
          }
        </button>

        <button
          onClick={function () { setConfigOpen(true) }}
          className="p-2.5 rounded-xl bg-white/60 dark:bg-white/[0.04] hover:bg-white/80 dark:hover:bg-white/[0.08] border border-slate-200 dark:border-white/[0.06] transition-all group flex-shrink-0"
          title="Widget Ayarlari"
        >
          <Settings2 size={15} className="text-slate-500 dark:text-white/40 group-hover:text-indigo-600 dark:group-hover:text-indigo-400/80 transition-colors" />
        </button>

        {/* Actions — ikon-only, label tooltip olarak gösterilir (Onay Akışı Edit header pattern).
            Primary action indigo bg ile ayırt edilir; diğerleri Filter/Excel/Widget tarzı hayalet. */}
        {actions.length > 0 && (
          <div className="flex items-center gap-2 flex-shrink-0">
            {actions.map(function (action) {
              var ActionIcon = resolveIcon(action.icon)
              var isPrimary = action.variant === 'primary'
              return (
                <button
                  key={action.id || action.label}
                  onClick={function () { handleActionClick(action) }}
                  title={action.label}
                  aria-label={action.label}
                  className={'p-2.5 rounded-xl border transition-all group flex-shrink-0 ' +
                    (isPrimary
                      ? 'bg-indigo-500 hover:bg-indigo-600 dark:bg-indigo-500/20 dark:hover:bg-indigo-500/30 border-indigo-500 dark:border-indigo-400/20 text-white dark:text-indigo-300 shadow-sm'
                      : 'bg-white/60 dark:bg-white/[0.04] hover:bg-white/80 dark:hover:bg-white/[0.08] border-slate-200 dark:border-white/[0.06] text-slate-500 dark:text-white/40 hover:text-indigo-600 dark:hover:text-indigo-400/80')
                  }
                >
                  <ActionIcon size={15} />
                </button>
              )
            })}
          </div>
        )}
      </div>

      {/* ── Aktif filtre chip strip (hayalet mod) ──
          Topbar altinda, dusuk opacity (0.65) ile lebon-floating gorunur.
          Her chip × ile silinir, aktif filtre toplam ekran genisliginde scroll'lanir. */}
      {filters.length > 0 && (
        <div
          className="flex items-center gap-1.5 px-5 py-2 flex-shrink-0 overflow-x-auto"
          style={{
            background: isDark ? 'rgba(99,102,241,0.05)' : 'rgba(99,102,241,0.04)',
            borderBottom: isDark ? '1px solid rgba(99,102,241,0.1)' : '1px solid rgba(99,102,241,0.08)',
            opacity: 0.85,
          }}
        >
          <Filter size={11} className="text-indigo-500/70 dark:text-indigo-400/70 flex-shrink-0" />
          {filters.map(function (f) {
            return (
              <span
                key={f.id}
                className="inline-flex items-center gap-1 pl-2.5 pr-1 py-0.5 rounded-full text-[10.5px] font-medium flex-shrink-0"
                style={{
                  background: isDark ? 'rgba(99,102,241,0.12)' : 'rgba(99,102,241,0.08)',
                  border: isDark ? '1px solid rgba(99,102,241,0.25)' : '1px solid rgba(99,102,241,0.18)',
                  color: isDark ? '#a5b4fc' : '#4338ca',
                }}
                title={describeFilter(f)}
              >
                <span className="truncate max-w-[200px]">{describeFilter(f)}</span>
                <button
                  type="button"
                  onClick={function () {
                    var next = filters.filter(function (x) { return x.id !== f.id })
                    setFilters(next)
                    try {
                      if (next.length === 0) window.localStorage.removeItem(FILTER_STORAGE_PREFIX + boardKey)
                      else window.localStorage.setItem(FILTER_STORAGE_PREFIX + boardKey, JSON.stringify(next))
                    } catch (e) { /* ignore */ }
                  }}
                  className="ml-0.5 p-0.5 rounded-full hover:bg-indigo-500/20 dark:hover:bg-indigo-400/20 transition-colors flex-shrink-0"
                  title="Bu filtreyi kaldir"
                >
                  <X size={10} strokeWidth={2.5} />
                </button>
              </span>
            )
          })}
          <button
            type="button"
            onClick={function () {
              setFilters([])
              try { window.localStorage.removeItem(FILTER_STORAGE_PREFIX + boardKey) } catch (e) { /* ignore */ }
            }}
            className="ml-2 px-2 py-0.5 rounded-full text-[10px] font-medium text-slate-500 dark:text-white/50 hover:text-rose-500 dark:hover:text-rose-300 transition-colors flex-shrink-0"
            title="Tum filtreleri temizle"
          >
            Tumunu temizle
          </button>
        </div>
      )}

      {/* ── Kart Listesi ─────────────────────── */}
      <div className="flex-1 overflow-y-auto px-4 py-3 min-h-0">
        {filteredEntities.length === 0 && !loading ? (
          <div className="text-center py-20">
            <HeaderIcon size={36} className="mx-auto text-slate-300 dark:text-white/30 mb-3" />
            <p className="text-sm text-slate-400 dark:text-white/45">{emptyText}</p>
          </div>
        ) : (
          <div className="flex flex-col gap-2">
            {filteredEntities.map(function (entity) {
              return (
                <SmartCard
                  key={entity.id}
                  {...entity}
                  visibleIds={visibleIds}
                  order={order}
                  onRefresh={refreshUrl ? refreshBoard : undefined}
                  isHighlighted={recentIds.has(entity.id)}
                />
              )
            })}

            {/* Infinite scroll sentinel */}
            {isPaginated && hasMore && (
              <div ref={sentinelRef} className="flex items-center justify-center py-6 gap-3">
                {loading ? (
                  <div className="flex items-center gap-2 text-xs text-slate-400 dark:text-white/40">
                    <Loader2 size={16} className="animate-spin" />
                    <span>Yukleniyor...</span>
                  </div>
                ) : (
                  <button
                    onClick={handleLoadMore}
                    className="flex items-center gap-2 px-5 py-2.5 rounded-xl text-xs font-semibold bg-white/60 dark:bg-white/[0.04] hover:bg-white/80 dark:hover:bg-white/[0.08] border border-slate-200 dark:border-white/[0.06] text-slate-600 dark:text-white/60 transition-all"
                  >
                    <ChevronDown size={14} />
                    <span>Daha Fazla Yukle ({(totalCount - entities.length).toLocaleString('tr-TR')} kalan)</span>
                  </button>
                )}
              </div>
            )}

            {/* Loading indicator for initial/search load */}
            {isPaginated && loading && filteredEntities.length === 0 && (
              <div className="flex items-center justify-center py-20 gap-3">
                <Loader2 size={24} className="animate-spin text-indigo-400" />
                <span className="text-sm text-slate-400 dark:text-white/40">Yukleniyor...</span>
              </div>
            )}
          </div>
        )}
      </div>

      {/* ── Widget Config Panel ─────────────── */}
      <SmartBoardConfigPanel
        isOpen={configOpen}
        onClose={function () { setConfigOpen(false) }}
        boardKey={boardKey}
        masterWidgets={masterWidgets}
        onSaved={handleConfigSaved}
      />

      {/* ── Filter Panel (hayalet mod) ─────── */}
      <SmartBoardFilterPanel
        isOpen={filterOpen}
        onClose={function () { setFilterOpen(false) }}
        boardKey={boardKey}
        formCode={formCode}
        masterWidgets={masterWidgets}
        entities={entities}
        filters={filters}
        onApply={function (next) { setFilters(next) }}
      />

      {/* ── Excel Aktar Onay Modalı ─────────── */}
      {showExportConfirm && (
        <div
          onClick={function(e) { if (e.target === e.currentTarget) setShowExportConfirm(false) }}
          style={{
            position: 'fixed', inset: 0, zIndex: 9999,
            display: 'flex', alignItems: 'center', justifyContent: 'center',
            background: 'rgba(0,0,0,0.55)',
            backdropFilter: 'blur(4px)',
          }}
        >
          <div style={{
            position: 'relative',
            background: isDark ? '#1e293b' : '#ffffff',
            border: isDark ? '1px solid rgba(255,255,255,0.08)' : '1px solid #e2e8f0',
            borderRadius: '16px',
            padding: '28px 32px',
            maxWidth: '400px',
            width: '90%',
            boxShadow: '0 24px 64px rgba(0,0,0,0.4)',
            textAlign: 'center',
          }}>
            {/* İkon */}
            <div style={{ display: 'flex', justifyContent: 'center', marginBottom: '16px' }}>
              <div style={{
                width: '52px', height: '52px', borderRadius: '50%',
                background: 'rgba(16,185,129,0.12)',
                border: '2px solid rgba(16,185,129,0.3)',
                display: 'flex', alignItems: 'center', justifyContent: 'center',
              }}>
                <FileSpreadsheet size={24} style={{ color: '#10b981' }} />
              </div>
            </div>

            {/* Başlık */}
            <h3 style={{
              fontSize: '16px', fontWeight: 700, marginBottom: '8px',
              color: isDark ? '#f1f5f9' : '#0f172a',
            }}>
              Excel'e Aktar
            </h3>

            {/* Açıklama */}
            <p style={{
              fontSize: '13px', lineHeight: 1.65, marginBottom: '24px',
              color: isDark ? '#94a3b8' : '#64748b',
            }}>
              <strong style={{ color: isDark ? '#cbd5e1' : '#334155' }}>{title}</strong> listesi
              Excel dosyası olarak dışa aktarılacak.
            </p>

            {/* Butonlar */}
            <div style={{ display: 'flex', gap: '10px', justifyContent: 'center' }}>
              <button
                type="button"
                onClick={function() { setShowExportConfirm(false) }}
                style={{
                  padding: '9px 20px', borderRadius: '10px', fontSize: '13px', fontWeight: 600,
                  cursor: 'pointer', transition: 'background 0.15s',
                  background: isDark ? 'rgba(255,255,255,0.06)' : '#f1f5f9',
                  color: isDark ? '#94a3b8' : '#475569',
                  border: isDark ? '1px solid rgba(255,255,255,0.1)' : '1px solid #e2e8f0',
                }}
              >
                Vazgeç
              </button>
              <button
                type="button"
                autoFocus
                onClick={function() { setShowExportConfirm(false); handleExportCsv() }}
                style={{
                  padding: '9px 20px', borderRadius: '10px', fontSize: '13px', fontWeight: 600,
                  cursor: 'pointer', transition: 'background 0.15s',
                  background: '#10b981', color: '#ffffff', border: 'none',
                }}
              >
                Aktar
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
