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
import { Search, Settings2, Loader2, ChevronDown, Filter, X, Download, FileSpreadsheet, RefreshCw, Layers, AlertTriangle, Wrench } from 'lucide-react'
import SmartCard from './SmartCard'
import SmartTable from './SmartTable'
import SmartBoardConfigPanel from './SmartBoardConfigPanel'
import SmartColumnSettings from './SmartColumnSettings'
import SmartGroupPanel from './SmartGroupPanel'
import SmartBoardFilterPanel, { describeFilter, entityMatchesFilters } from './SmartBoardFilterPanel'
import { resolveIcon, resolveColor } from './DynamicWidgetFactory'
import { loadWidgetConfig } from '../../services/widgetConfigService'
import { loadBoardColumnConfig, readBoardColumnConfigLocal } from '../../services/columnConfigService'
import { navigateInWorkspace } from '../../utils/workspaceNav'
import { openActionUrl } from './openActionUrl'

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

// ── Cok alanli gruplama zinciri kaliciligi (2026-07-25, PageComment Seq 36) ──
// Filtre kaliciligiyla AYNI hafif desen: boardKey scope'lu localStorage, ayri
// prefix. Backend'e yazilmaz (sutun ayarlarinin aksine) — gruplama gecici bir
// gorunum tercihi (filtre gibi), kullanicidan kullaniciya/cihazdan cihaza
// tasinmasi gerekmez; mevcut filtre altyapisiyla birebir tutarli.
var GROUPBY_STORAGE_PREFIX = 'cb-sb-groupby:'
function loadInitialGroupBy(boardKey) {
  if (!boardKey || typeof window === 'undefined') return []
  try {
    var raw = window.localStorage.getItem(GROUPBY_STORAGE_PREFIX + boardKey)
    if (!raw) return []
    var arr = JSON.parse(raw)
    return Array.isArray(arr) ? arr.filter(function (x) { return typeof x === 'string' }) : []
  } catch (e) { return [] }
}

// ── Tablo modu kimlik sentezi ─────────────────────────────────────────────
// Kart-liste board'lari kimligini (isim/kod) entity.title / entity.subtitle ile
// tasir; SmartTable ise SADECE widget sutunlarini render eder (entity.title/
// subtitle OKUMAZ — bkz. SmartTableRow dosya ustu). Malzeme Kartlari bunu
// w_kod/w_ad widget'lari ekleyerek cozmustu (LogisticsController); diger 51
// kart-liste board'i cozmedi → tabloya cevrilince isim sutunu kaybolurdu. Bu
// yuzden tablo modunda, board kendi w_ad/w_kod'unu TANIMLAMAMISSA, entity.title
// → "Ad" ve (varsa ve isimden farkliysa) entity.subtitle → "Kod" sanal
// widget'lari sentezlenir. id'ler w_ad/w_kod YENIDEN kullanildigi icin
// leadsFirst (basa alma), isCodeStyle (monospace) ve sutun ayarlari otomatik
// dogru calisir — SmartTable/SmartTableRow'a hicbir ozel-durum eklemeden.
var IDENTITY_NAME_ID = 'w_ad'
var IDENTITY_CODE_ID = 'w_kod'

function hasLeadIdentity(masterWidgets) {
  for (var i = 0; i < masterWidgets.length; i++) {
    var w = masterWidgets[i]
    if (w && (w.id === IDENTITY_NAME_ID || w.id === IDENTITY_CODE_ID)) return true
  }
  return false
}

function synthesizeMasterIdentity(masterWidgets, wantName, wantCode) {
  var lead = []
  if (wantName) lead.push({ id: IDENTITY_NAME_ID, type: 'data', dataType: 'text', label: 'Ad' })
  if (wantCode) lead.push({ id: IDENTITY_CODE_ID, type: 'data', dataType: 'text', label: 'Kod' })
  return lead.concat(masterWidgets)
}

function synthesizeEntityIdentity(entity, wantName, wantCode) {
  var extra = []
  if (wantName) extra.push({ id: IDENTITY_NAME_ID, type: 'data', dataType: 'text', label: 'Ad', value: (entity && entity.title != null) ? entity.title : '' })
  if (wantCode) extra.push({ id: IDENTITY_CODE_ID, type: 'data', dataType: 'text', label: 'Kod', value: (entity && entity.subtitle != null) ? entity.subtitle : '' })
  if (extra.length === 0) return entity
  var widgets = (entity && Array.isArray(entity.widgets)) ? entity.widgets : []
  return Object.assign({}, entity, { widgets: extra.concat(widgets) })
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
  // viewMode: VARSAYILAN "table" (2026-07-18 "kesin tablo" karari) — tum kart-
  // liste board'lari satir bazli SmartTable ile render edilir (kurumsal tek-tip
  // gorunum). Bir board tabloya uygun degilse (ozel/gomulu ekran) config'inde
  // AÇIKÇA viewMode:'card' vererek opt-out eder. Kimligi widget olmayan (isim/
  // kod'u entity.title/subtitle'da tutan) board'lar icin kimlik sutunu asagida
  // sentezlenir (tableMasterWidgets/tableEntities), boylece tabloya cevrilince
  // isim sutunu kaybolmaz.
  var viewMode = props.viewMode === 'card' ? 'card' : 'table'
  // Opt-in, entity-bazlı client-side dönüşüm (2026-08-29, PageComment Seq 1129).
  // Verilmişse HER entity'ye (ilk yük + sonsuz-kaydırma/sayfalama ile sonradan
  // çekilen sayfalar dahil) uygulanır — bkz. filteredEntities altında. Diğer
  // board'lar için varsayılan null, davranış değişmez. Amaç: sayfa-özel bir
  // .cshtml'in (ör. ItemDocumentLocks) board config'e dokunmadan entity'lere
  // ekstra widget/aksiyon eklemesine izin vermek — controller'a dokunulmadan.
  var entityTransform = typeof props.entityTransform === 'function' ? props.entityTransform : null

  // In-place refresh
  var refreshUrl = props.refreshUrl || null
  // Opt-in otomatik tazeleme (ms). Yalnız config'te verilmişse çalışır — diğer board'lar
  // etkilenmez. Durumu kendiliğinden değişen listeler için (ör. Zamanlanmış Görevler'de
  // "Çalışıyor"), aksi halde kullanıcı "Yenile"ye basmadıkça anlık durumu göremez.
  var autoRefreshMs = Number(props.autoRefreshMs) || 0

  // ── Sunucu filtreleri (2026-08-29, opt-in) ──
  // Filtre panelinin en ustunde cizilen, listeyi YENIDEN CEKTIREN secimler
  // (ornek: Onayda Bekleyenler → Bekleyen/Tamamlanan + kapsam). Board sadece
  // tasiyicidir; yeniden cekmeyi sayfa yapar cunku sorgu parametrelerinin adini
  // yalnizca o bilir. onServerFilterChange bir FONKSIYON oldugundan JSON config'te
  // tasinmaz — mountSmartBoard'a sayfa JS'inden eklenir. Verilmezse bolum cizilmez.
  var serverFilters = Array.isArray(props.serverFilters) ? props.serverFilters.filter(Boolean) : []
  var onServerFilterChange = typeof props.onServerFilterChange === 'function'
    ? props.onServerFilterChange
    : null
  if (!onServerFilterChange) serverFilters = []

  // ── Satir secimi + acilir detay (opt-in, YALNIZCA tablo modu) ───────────
  // selectable:true      → satir basi onay kutusu + baslikta "tumunu sec"
  // bulkActions:[...]    → secim varken alttan cikan toplu aksiyon seridi
  //                        { id, label, icon, variant:'primary'|'danger'|'ghost',
  //                          apiUrl, apiMethod:'POST', confirm? }
  //                        POST govdesi: { ids:[...] }
  // expandable:true      → satir sonunda detay oku; acilinca entity.detailUrl
  //                        (yoksa detailUrl sablonu, "{id}" degistirilir)
  //                        GET edilir ve mini tablo olarak cizilir:
  //                        { ok, columns:[{key,label,align,width}], rows:[{...}],
  //                          empty?:"metin", error?:"metin" }
  // Hicbiri verilmezse mevcut board'larin render'i BIREBIR aynidir.
  /* iconMenu (2026-08-22, kullanici istegi): baslik ikonu TIKLANABILIR bir
     buton olur ve altinda "Islemler" tarzi bir menu acilir.
     Sozlesme normal header `actions` ile AYNI: [{ id, label, icon, url,
     openInTab? }] — tiklama handleActionClick'e gider, dolayisiyla `openInTab`
     verilince YENI workspace SEKMESI acilir (sol menuden tiklanmis gibi).
     Ilk surumde dogrudan navigateInWorkspace cagriliyordu; o MEVCUT sekmenin
     icerigini degistiriyordu — kullanici "yeni bir sayfa acilmali" dedi.
     Verilmezse ikon eskisi gibi salt gorsel kalir (fail-open). */
  var iconMenu = Array.isArray(props.iconMenu) ? props.iconMenu.filter(Boolean) : []
  /* toolbarMenu (2026-08-30, kullanici karari): STANDART buton seridine kalici bir
     "Islemler" menusu. Ekrana ozgu toplu islemler (ornek: e-belgede "Cari Eslestir")
     baslik ikonuna ya da yeni bir butona degil, HEP bu menunun altina eklenir —
     islem sayisi arttikca serit buyumez ve buton sirasi ekrandan ekrana kaymaz.
     Sozlesme header `actions` ile AYNI: [{ id, label, icon, url|trigger, openInTab? }].
     Bos/verilmemisse buton hic cizilmez (fail-open; mevcut board'lar degismez). */
  var toolbarMenu = Array.isArray(props.toolbarMenu) ? props.toolbarMenu.filter(Boolean) : []
  var selectable = props.selectable === true
  var bulkActions = Array.isArray(props.bulkActions) ? props.bulkActions.filter(Boolean) : []
  var detailUrlTemplate = props.detailUrl || null
  var expandable = props.expandable === true || !!detailUrlTemplate

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

  // ── Tablo modu "Sutun Ayarlari" (SmartColumnSettings) — kart modundan AYRI
  //    state/servis. viewMode !== 'table' oldugunda hicbiri kullanilmaz;
  //    panel de asagida sadece table modunda mount edilir (regresyonsuz —
  //    kart board'lari icin bu kod yolu hic calismaz). ──
  var [columnSettingsOpen, setColumnSettingsOpen] = useState(false)
  // Ilk deger localStorage'dan SENKRON okunur: asenkron yukleme beklenirken tablo
  // tum kolonlari varsayilan sirayla cizip sonra suzuyordu — yenilemede kolonlarin
  // "bir gorunup kaybolmasi" bundandi. Yerel kayit yoksa null kalir (eski davranis).
  var [tableColumnConfig, setTableColumnConfig] = useState(function () {
    return readBoardColumnConfigLocal(props.boardKey)
  })
  useEffect(function () {
    if (viewMode !== 'table') return undefined
    var cancelled = false
    loadBoardColumnConfig(boardKey).then(function (cfg) {
      if (!cancelled) setTableColumnConfig(cfg)
    })
    return function () { cancelled = true }
  }, [boardKey, viewMode])

  // ── Filter state (hayalet mod) ──
  // localStorage'dan initial yukleme — sayfa arasi tercih korunur (boardKey scope)
  var [filterOpen, setFilterOpen] = useState(false)
  var [filters, setFilters] = useState(function () { return loadInitialFilters(boardKey) })

  // ── Gruplama state (yalnizca tablo modu — asagida isTableMode ile kosullu
  //    render/derive). setGroupBy: state + localStorage kaliciligi (filtre
  //    setFilters kaliciligiyla ayni desen). ──
  var [groupOpen, setGroupOpen] = useState(false)
  var [groupBy, setGroupByState] = useState(function () { return loadInitialGroupBy(boardKey) })
  var setGroupBy = useCallback(function (next) {
    var arr = Array.isArray(next) ? next.filter(function (x) { return typeof x === 'string' }) : []
    setGroupByState(arr)
    try {
      if (arr.length === 0) window.localStorage.removeItem(GROUPBY_STORAGE_PREFIX + boardKey)
      else window.localStorage.setItem(GROUPBY_STORAGE_PREFIX + boardKey, JSON.stringify(arr))
    } catch (e) { /* quota/private — sessiz gec */ }
  }, [boardKey])

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

  // In-place refresh state
  var [recentIds, setRecentIds] = useState(function () { return new Set() })
  var [refreshing, setRefreshing] = useState(false)

  var refreshBoard = useCallback(function (highlightId) {
    if (!refreshUrl) { window.location.reload(); return Promise.resolve() }
    return fetch(refreshUrl, { credentials: 'same-origin' })
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

  // ── Otomatik tazeleme ──
  // refreshBoard'ın hata yolu tam sayfa reload yapar; bu ARKA PLAN tazelemesi için
  // kabul edilemez (geçici bir ağ hatası kullanıcının altından sayfayı yeniler).
  // Bu yüzden ayrı, sessizce vazgeçen bir çekim kullanılıyor — hata yutulmaz, konsola yazılır.
  useEffect(function () {
    if (!refreshUrl || autoRefreshMs < 5000) return undefined
    var cancelled = false
    var timer = setInterval(function () {
      // Sekme arka plandayken istek atma; sayfalanmış listede 1. sayfa dışındayken de
      // tazeleme kullanıcının bulunduğu sayfayı altından değiştirir — atla.
      if (typeof document !== 'undefined' && document.hidden) return
      if (isPaginated && currentPage > 1) return
      fetch(refreshUrl, { credentials: 'same-origin' })
        .then(function (r) { return r.ok ? r.json() : null })
        .then(function (data) {
          if (cancelled || !data || !Array.isArray(data.entities)) return
          setEntities(data.entities)
        })
        .catch(function (err) {
          if (!cancelled && typeof console !== 'undefined') {
            console.warn('SmartBoard otomatik tazeleme basarisiz:', err)
          }
        })
    }, autoRefreshMs)
    return function () { cancelled = true; clearInterval(timer) }
  }, [refreshUrl, autoRefreshMs, isPaginated, currentPage])

  // Header "Yenile" butonu — in-place refresh (refreshUrl yoksa tam sayfa reload)
  var handleManualRefresh = useCallback(function () {
    setRefreshing(true)
    Promise.resolve(refreshBoard()).finally(function () { setRefreshing(false) })
  }, [refreshBoard])

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

  /* ── Filtre + sunucu sayfalama uyusmazligi (2026-08-28) ─────────────────
     SORUN: Filtreler ISTEMCI tarafinda, sunucu sayfalamasi ise SUNUCU tarafinda
     calisiyor. Yani filtre yalnizca O ANDA YUKLENMIS satirlarda arama yapiyordu:
     39.803 malzemenin ilk 50'si yuklenmisken "Barkod 8 ile baslar" filtresi tek
     sonuc gosteriyor, kullanici "Daha Fazla Yukle"ye bastikca yeni eslesmeler
     "sonradan ortaya cikiyordu". Kullanici acisindan bu, filtrenin YANLIS sonuc
     vermesi demek.

     COZUM: Filtre aktifken kalan sayfalar arka planda TARANIR ve yalnizca
     ESLESEN satirlar bellekte tutulur. Neden sadece eslesenler: tablo satirlari
     sanallastirilmiyor (virtualization yok); 39 bin satiri bellekte tutup
     render etmek tarayiciyi kilitlerdi. Filtre degisince/temizlenince liste
     bastan (sayfa 1) yeniden yuklenir.

     Tarama sayfa 1'den ve SABIT SCAN_BATCH ile yapilir: sayfa/offset hesabi
     sunucuda (page-1)*pageSize oldugu icin tarama ortasinda batch boyutu
     degistirmek satir atlamaya/tekrarina yol acardi. */
  var SCAN_BATCH = 200
  var [scan, setScan] = useState({ running: false, scanned: 0, done: false, stopped: false })
  var scanTokenRef = useRef(0)
  var filterSignature = JSON.stringify(filters || [])

  // Filtre (veya arama) degisti → onceki tarama gecersiz.
  useEffect(function () {
    scanTokenRef.current += 1
    setScan({ running: false, scanned: 0, done: false, stopped: false })
  }, [filterSignature, searchQuery])

  var runFilterScan = useCallback(function () {
    if (!apiUrl) return
    var token = ++scanTokenRef.current
    setScan({ running: true, scanned: 0, done: false, stopped: false })

    var matches = []
    var page = 1
    var total = 0

    function step() {
      if (token !== scanTokenRef.current) return   // filtre degisti / iptal
      var url = apiUrl + '?page=' + page + '&pageSize=' + SCAN_BATCH
      if (searchQuery) url += '&search=' + encodeURIComponent(searchQuery)
      fetch(url, { credentials: 'same-origin' })
        .then(function (r) { return r.json() })
        .then(function (data) {
          if (token !== scanTokenRef.current) return
          if (data.error) throw new Error(data.error)
          var rows = Array.isArray(data.entities) ? data.entities : []
          total = data.totalCount || total
          rows.forEach(function (e) { if (entityMatchesFilters(e, filters)) matches.push(e) })

          var scanned = (page - 1) * SCAN_BATCH + rows.length
          setEntities(matches.slice())
          setTotalCount(total)
          setScan({ running: scanned < total && rows.length > 0, scanned: scanned, done: false, stopped: false })

          if (rows.length > 0 && scanned < total) {
            page += 1
            step()
          } else {
            setHasMore(false)
            setScan({ running: false, scanned: scanned, done: true, stopped: false })
          }
        })
        .catch(function (err) {
          console.error('[SmartBoard] filtre taramasi hatasi:', err)
          if (token === scanTokenRef.current) {
            // Sessizce bitirme YOK: tarama yarim kaldi bilgisi kullaniciya gosterilir.
            setScan({ running: false, scanned: (page - 1) * SCAN_BATCH, done: false, stopped: true })
          }
        })
    }
    step()
  }, [apiUrl, searchQuery, filters])

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

  /* Filtre aktif ve sunucuda daha fazla kayit varsa TARAMAYI baslat.
     Filtre kalkinca liste kirpilmis (yalnizca eslesenler) durumda oldugu icin
     sayfa 1'den yeniden yuklenir — aksi halde kullanici filtreyi temizledigi
     halde eski eslesme kumesini gormeye devam ederdi. */
  var prevHadFilterRef = useRef(hasActiveFilter)
  useEffect(function () {
    if (!isPaginated) return
    if (hasActiveFilter) {
      if (!scan.running && !scan.done && !scan.stopped && totalCount > entities.length) runFilterScan()
    } else if (prevHadFilterRef.current) {
      fetchPage(1, searchQuery, false)
    }
    prevHadFilterRef.current = hasActiveFilter
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [hasActiveFilter, filterSignature, isPaginated])

  /* ── Sonsuz kaydirma (2026-08-22 yeniden yazildi) ────────────────────────
     ONCEKI TASARIM: listenin altina bir "sentinel" <div> konur, IntersectionObserver
     (root: viewport) onu izlerdi. Iki sorunu vardi:
       (a) Sentinel gorunur oldugu surece tetikleniyordu; tablo modunda kaydirma
           artik ic kutuda (.cst-wrap) oldugu icin sentinel EKRANDA SABIT kaliyor
           → her sayfa gelisinde tekrar tetikleniyor, "surekli yukleniyor" hali.
       (b) Sentinel gercek bir kutu oldugu icin sayfanin altinda KALICI yer
           kapliyordu ("Daha Fazla Yukle (N kalan)" seridi).
     YENI TASARIM: DOM'da hicbir yer tutmaz. Kaydirilan kabin scroll olayi
     dinlenir; dibe ~320px kala bir sonraki sayfa istenir. Hangi kabin kaydigi
     moda gore degistigi (tablo: .cst-wrap, kart: govde) icin ikisi de dinlenir.
     Icerik ekrani doldurmuyorsa (kaydirma cubugu yok) hic tetiklenmezdi — bu
     yuzden yukleme sonrasi "hala kaydirilamiyor mu" kontrolu de yapilir. */
  var listBodyRef = useRef(null)
  useEffect(function () {
    if (!isPaginated || hasActiveFilter) return undefined
    var body = listBodyRef.current
    if (!body) return undefined
    var targets = [body]
    var wrap = body.querySelector('.cst-wrap')
    if (wrap) targets.push(wrap)

    /* GORUNMEYEN ve KAYDIRILMAYAN kap sayfa CEKMEZ (2026-08-30 hata duzeltmesi).

       Workspace sekmeleri kapatilmaz, yalnizca GIZLENIR (display:none iframe).
       Gizli kapta scrollHeight = clientHeight = 0 oldugundan "dibe geldik" kosulu
       (0 - 0 - 0 <= 320) HER ZAMAN dogruydu: arka plandaki liste kendi kendine
       sayfa sayfa TUM kayitlari cekiyordu. Olculdu: gizli e-Fatura sekmesi 15
       saniyede 2.801 -> 4.151 satira cikti, JS yigini 83 -> 301 MB. Kullanici bunu
       "e-Fatura sekmesi 4,9 GB RAM yiyor" olarak bildirdi.

       Ayrica karar TEK kural haline getirildi: kaydirilan kap hangisiyse (tablo
       modunda .cst-wrap, kart modunda govde) yalnizca ONUN dibi olculur. Her hedefi
       tek tek "dibe geldi mi" diye sinamak, kaydirmayan dis kabi (scrollHeight ==
       clientHeight) surekli "dipte" sayip ayni donguyu geri getiriyordu. */
    function isVisible(el) {
      // offsetParent null => kendisi ya da bir atasi display:none (gizli sekme).
      return el.offsetParent !== null && el.clientHeight > 0
    }

    function maybeLoad() {
      if (!hasMore || loading) return

      var visible = targets.filter(isVisible)
      if (visible.length === 0) return          // gizli sekme: hicbir sey yukleme

      var scrollables = visible.filter(function (el) {
        return el.scrollHeight > el.clientHeight + 8
      })

      // Hicbiri kaydirilamiyorsa icerik ekrani doldurmamistir -> bir sayfa daha.
      if (scrollables.length === 0) { handleLoadMore(); return }

      for (var i = 0; i < scrollables.length; i++) {
        var el = scrollables[i]
        if (el.scrollHeight - el.scrollTop - el.clientHeight <= 320) { handleLoadMore(); return }
      }
    }

    function onScroll() { maybeLoad() }
    targets.forEach(function (t) { t.addEventListener('scroll', onScroll, { passive: true }) })

    // Ilk sayfa ekrani doldurmadiysa bir sonrakini iste.
    var t = setTimeout(maybeLoad, 250)

    /* Gizli sekme GORUNUR olunca kap yuksekligi 0'dan gercek degere ciker; bu an
       ResizeObserver ile yakalanir ve kontrol o zaman calisir. Aksi halde gizliyken
       atlanan yukleme, sekmeye donuldugunde kullanici kaydirana kadar hic
       tetiklenmezdi. */
    var ro = null
    if (typeof ResizeObserver !== 'undefined') {
      ro = new ResizeObserver(function () { maybeLoad() })
      targets.forEach(function (x) { ro.observe(x) })
    }

    return function () {
      clearTimeout(t)
      if (ro) ro.disconnect()
      targets.forEach(function (x) { x.removeEventListener('scroll', onScroll) })
    }
  }, [isPaginated, hasActiveFilter, hasMore, loading, handleLoadMore, entities.length, viewMode])

  // Client-side filtering — search + filter panel (her iki mod icin)
  // Not: Server-side paginated mode'da search server'da yapilir, ama filter panel
  // her iki modda da CLIENT-SIDE calisir. Server-side filter destegi sonra eklenebilir.
  var filteredEntities = useMemo(function () {
    var arr = entityTransform ? entities.map(entityTransform) : entities
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
  }, [search, entities, isPaginated, filters, entityTransform])

  // Sayfali modda sayac etiketi board config'ten gelir (itemLabel: "reçete", "cari"...).
  // Varsayilan 'cari' — pagination ilk Cari board'unda dogdu; itemLabel gondermeyen
  // mevcut ekranlarin davranisi degismesin.
  /* 2026-08-22 DUZELTME: itemLabel gonderilmediginde sabit 'cari' yaziliyordu —
     Malzeme Kartlari'nda "4.466 cari" cikiyordu. Once itemLabel, o yoksa
     sunucunun kendi subtitle'i ("4.466 malzeme"), o da yoksa sayinin tek basina. */
  var subtitle = isPaginated
    ? (props.itemLabel
        ? (totalCount > 0 ? totalCount.toLocaleString('tr-TR') + ' ' + props.itemLabel : '')
        : (props.subtitle || (totalCount > 0 ? totalCount.toLocaleString('tr-TR') : '')))
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
    if (action.url) {
      /* Sekme açma mantığı TEK YERDE: openActionUrl (bkz. o dosyanın kuralı).
         Header aksiyonları da ("Yeni X") varsayılan olarak ALT SEKMEDE açılır —
         liste sekmesi kalıcı kalır, yeni kayıt onun altında görünür. */
      if (openActionUrl(action, { defaultTitle: action.label })) return
      navigateInWorkspace(action.url)
    }
  }, [])

  /* ── F8 — primary header action ("Yeni X") kisayolu ──
     Odak iframe icindeyken dogrudan keydown yakalanir; odak Shell'de
     (sidebar/menu) iken Shell aktif tab'a calibra:hotkey mesaji forward
     eder, burada message listener'i ile yakalanir. */
  useEffect(function () {
    var primary = null
    for (var i = 0; i < actions.length; i++) {
      if (actions[i].variant === 'primary') { primary = actions[i]; break }
    }
    if (!primary && actions.length > 0) primary = actions[0]
    if (!primary) return undefined

    function trigger() { handleActionClick(primary) }

    function onKey(e) {
      // Aksiyon seridi (edit ekrani) hotkey'i onceden yakalayip preventDefault ettiyse tekrar tetikleme
      if (e.defaultPrevented) return
      var isF8 = (e.key === 'F8' || e.keyCode === 119) && !e.altKey && !e.ctrlKey && !e.metaKey && !e.shiftKey
      if (!isF8) return
      e.preventDefault()
      trigger()
    }
    function onMsg(e) {
      var d = e && e.data
      if (d && typeof d === 'object' && d.type === 'calibra:hotkey' && d.action === 'new') trigger()
    }
    document.addEventListener('keydown', onKey)
    window.addEventListener('message', onMsg)
    return function () {
      document.removeEventListener('keydown', onKey)
      window.removeEventListener('message', onMsg)
    }
  }, [actions, handleActionClick])

  /* ── F6 — board yenile (Yenile butonuyla ayni in-place refresh) ── */
  useEffect(function () {
    function onKey(e) {
      if (e.defaultPrevented) return
      if (e.altKey || e.ctrlKey || e.metaKey || e.shiftKey) return
      if (e.key !== 'F6' && e.keyCode !== 117) return
      e.preventDefault()
      handleManualRefresh()
    }
    document.addEventListener('keydown', onKey)
    return function () { document.removeEventListener('keydown', onKey) }
  }, [handleManualRefresh])

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

  // Kart modu: userConfig/widgetConfigService (localStorage-only) — AYNEN eskisi
  // gibi, degismedi. Tablo modu: tableColumnConfig/columnConfigService (backend +
  // localStorage fallback) — SmartColumnSettings'in urettigi genisletilmis sema.
  // Ikisi birbirinden tamamen izole; SmartCard'a giden visibleIds/order kart
  // modunda HICBIR ZAMAN tableColumnConfig'ten etkilenmez.
  var isTableMode = viewMode === 'table'

  // ── Secim / detay durumu ────────────────────────────────────────────────
  var [iconMenuOpen, setIconMenuOpen] = useState(false)
  var [toolbarMenuOpen, setToolbarMenuOpen] = useState(false)
  var [selectedIds, setSelectedIds] = useState(function () { return new Set() })
  var [expandedIds, setExpandedIds] = useState(function () { return new Set() })
  var [detailData, setDetailData] = useState({})   // id -> {loading|error|payload}
  var [bulkBusy, setBulkBusy] = useState(false)
  var [bulkConfirm, setBulkConfirm] = useState(null)   // onay bekleyen toplu aksiyon

  var selectEnabled = isTableMode && selectable
  var expandEnabled = isTableMode && expandable

  var handleToggleSelect = useCallback(function (id, checked) {
    setSelectedIds(function (prev) {
      var next = new Set(prev)
      if (checked) next.add(id); else next.delete(id)
      return next
    })
  }, [])

  var handleToggleSelectAll = useCallback(function (checked, ids) {
    setSelectedIds(function (prev) {
      var next = new Set(prev)
      ;(ids || []).forEach(function (id) { if (checked) next.add(id); else next.delete(id) })
      return next
    })
  }, [])

  var clearSelection = useCallback(function () { setSelectedIds(new Set()) }, [])

  // Detay: acilista bir kez cekilir, kapanip acilinca cache'ten gelir.
  // Yenile (refreshBoard) cache'i temizler ki bayat detay gosterilmesin.
  var loadDetail = useCallback(function (entity) {
    var id = entity.id
    var url = entity.detailUrl || (detailUrlTemplate ? String(detailUrlTemplate).replace('{id}', encodeURIComponent(id)) : null)
    if (!url) {
      setDetailData(function (p) { var n = Object.assign({}, p); n[id] = { error: 'Detay adresi tanımlı değil.' }; return n })
      return
    }
    setDetailData(function (p) { var n = Object.assign({}, p); n[id] = { loading: true }; return n })
    fetch(url, { headers: { Accept: 'application/json' } })
      .then(function (r) { return r.json() })
      .then(function (data) {
        setDetailData(function (p) {
          var n = Object.assign({}, p)
          n[id] = (data && data.ok === false) ? { error: data.error || 'Detay yüklenemedi.' } : { payload: data }
          return n
        })
      })
      .catch(function () {
        setDetailData(function (p) { var n = Object.assign({}, p); n[id] = { error: 'Bağlantı hatası — detay yüklenemedi.' }; return n })
      })
  }, [detailUrlTemplate])

  var handleToggleExpand = useCallback(function (id) {
    setExpandedIds(function (prev) {
      var next = new Set(prev)
      if (next.has(id)) next.delete(id); else next.add(id)
      return next
    })
  }, [])

  // Acilan satirin detayi henuz yuklenmediyse cek (kapali satir icin istek YOK).
  useEffect(function () {
    if (!expandEnabled) return
    expandedIds.forEach(function (id) {
      if (detailData[id]) return
      var ent = entities.find(function (e) { return e.id === id })
      if (ent) loadDetail(ent)
    })
  }, [expandedIds, expandEnabled, entities, detailData, loadDetail])

  // Toplu aksiyon: secili id'leri POST eder, sonucu toast'lar, board'u tazeler.
  // Sunucu sozlesmesi: { ok, message?, error? } (Fulfillment/ShipReservations deseni).
  var executeBulkAction = useCallback(function (action) {
    if (!action || bulkBusy) return
    var ids = Array.isArray(action._forcedIds) ? action._forcedIds : Array.from(selectedIds)
    if (ids.length === 0) return

    // type:'event' → POST YOK. Secili id'ler DOM olayi ile host sayfaya verilir;
    // ek girdi toplayan akislar (miktar/depo/tarih soran modal) boyle baglanir.
    // Host sayfa isi bitince board'u tazelemek icin window.CalibraSmartBoard
    // .refresh(boardKey) cagirir (asagida kayit edilir).
    if (action.type === 'event') {
      try {
        window.dispatchEvent(new CustomEvent(action.event || 'smartboard:bulk', {
          detail: { boardKey: boardKey, actionId: action.id || null, ids: ids },
        }))
      } catch (e) { /* CustomEvent desteklenmiyorsa sessiz gec */ }
      return
    }

    var url = action.apiUrl || action.url
    if (!url) return
    setBulkBusy(true)
    var token = null
    try {
      var ti = document.querySelector('input[name="__RequestVerificationToken"]')
      token = ti ? ti.value : (window.__CALIBRA_SHELL_CONFIG__ && window.__CALIBRA_SHELL_CONFIG__.antiforgeryToken) || null
    } catch (e) { /* token yoksa sunucu zaten reddeder */ }
    var headers = { 'Content-Type': 'application/json' }
    if (token) headers['RequestVerificationToken'] = token
    fetch(url, {
      method: action.apiMethod || 'POST',
      headers: headers,
      // idsField — hedef uc farkli bir alan adi bekliyorsa (ornegin
      // /Sales/ShipReservations → { reservationIds }) config'te verilir.
      body: JSON.stringify(Object.assign({}, action.apiBody || {}, (function () {
        var b = {}; b[action.idsField || 'ids'] = ids; return b
      })())),
    })
      .then(function (r) { return r.json() })
      .then(function (res) {
        setBulkBusy(false)
        if (res && res.ok === false) {
          if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast(res.error || 'İşlem tamamlanamadı.', 'error')
          return
        }
        if (res && res.message && window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast(res.message, 'success')
        clearSelection()
        setDetailData({})
        if (refreshUrl) refreshBoard()
      })
      .catch(function () {
        setBulkBusy(false)
        if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast('Bağlantı hatası — işlem tamamlanamadı.', 'error')
      })
  }, [bulkBusy, selectedIds, clearSelection, refreshUrl, boardKey])

  // action.confirm verilmişse ÖNCE ekran ortasında onay modalı açılır (CLAUDE.md
  // silme/geri-alınamaz işlem standardı — native confirm() KULLANILMAZ).
  var runBulkAction = useCallback(function (action) {
    if (!action || bulkBusy) return
    if (action.confirm) { setBulkConfirm(action); return }
    executeBulkAction(action)
  }, [bulkBusy, executeBulkAction])

  // Host sayfa koprusu — type:'event' aksiyonundan sonra kendi modalini
  // tamamlayinca board'u tazelemesi/secimi temizlemesi icin.
  useEffect(function () {
    if (!selectEnabled) return undefined
    var api = window.CalibraSmartBoard || (window.CalibraSmartBoard = {})
    var store = api._boards || (api._boards = {})
    store[boardKey] = {
      refresh: function () { setDetailData({}); if (refreshUrl) refreshBoard() },
      clearSelection: clearSelection,
    }
    api.refresh = function (key) { var b = store[key]; if (b) b.refresh() }
    api.clearSelection = function (key) { var b = store[key]; if (b) b.clearSelection() }
    return function () { delete store[boardKey] }
  }, [selectEnabled, boardKey, refreshUrl, refreshBoard, clearSelection])

  var renderDetail = useCallback(function (entity) {
    var st = detailData[entity.id]
    if (!st || st.loading) return <div className="cst-detail-msg">Yükleniyor…</div>
    if (st.error) return <div className="cst-detail-msg cst-detail-msg--err">{st.error}</div>
    var d = st.payload || {}
    var cols = Array.isArray(d.columns) ? d.columns : []
    var rows = Array.isArray(d.rows) ? d.rows : []
    if (rows.length === 0) return <div className="cst-detail-msg">{d.empty || 'Kayıt yok.'}</div>
    if (cols.length === 0) {
      cols = Object.keys(rows[0])
        .filter(function (k) { return k !== 'id' })
        .map(function (k) { return { key: k, label: k } })
    }
    // rowActions — her detay satirinin sonunda buton kolonu. Sunucu sozlesmesi:
    // { rowActions: [{ id, label, icon, variant, apiUrl, confirm? }] } ve her
    // satirda `id`. POST govdesi { ids:[<satir id>] } (toplu aksiyonla ayni sekil).
    var rowActions = Array.isArray(d.rowActions) ? d.rowActions.filter(Boolean) : []
    return (
      <table className="cst-detail-tbl">
        <thead>
          <tr>
            {cols.map(function (c) {
              return <th key={c.key} style={{ textAlign: c.align || 'left', width: c.width || undefined }}>{c.label}</th>
            })}
            {rowActions.length > 0 && <th style={{ width: 1 }} />}
          </tr>
        </thead>
        <tbody>
          {rows.map(function (r, i) {
            return (
              <tr key={r.id != null ? r.id : i}>
                {cols.map(function (c) {
                  var v = r[c.key]
                  return <td key={c.key} style={{ textAlign: c.align || 'left' }}>{v == null ? '' : String(v)}</td>
                })}
                {rowActions.length > 0 && (
                  <td style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>
                    {rowActions.map(function (a, ai) {
                      return (
                        <button
                          key={a.id || ai}
                          type="button"
                          disabled={bulkBusy}
                          className={'cst-bulk-btn cst-bulk-btn--' + (a.variant === 'danger' ? 'danger' : a.variant === 'primary' ? 'primary' : 'ghost')}
                          style={{ padding: '4px 10px', fontSize: 11.5, marginLeft: 6 }}
                          onClick={function (e) {
                            e.stopPropagation()
                            runBulkAction(Object.assign({}, a, { _forcedIds: [r.id] }))
                          }}
                        >
                          {a.label}
                        </button>
                      )
                    })}
                  </td>
                )}
              </tr>
            )
          })}
        </tbody>
      </table>
    )
  }, [detailData, bulkBusy, runBulkAction])

  var visibleIds = isTableMode
    ? (tableColumnConfig && Array.isArray(tableColumnConfig.visibleIds) ? tableColumnConfig.visibleIds : null)
    : (userConfig && Array.isArray(userConfig.visibleIds) ? userConfig.visibleIds : null)
  var order = isTableMode
    ? (tableColumnConfig && Array.isArray(tableColumnConfig.order) ? tableColumnConfig.order : null)
    : (userConfig && Array.isArray(userConfig.order) ? userConfig.order : null)
  var tableColumnFormats = (isTableMode && tableColumnConfig && tableColumnConfig.columns && typeof tableColumnConfig.columns === 'object')
    ? tableColumnConfig.columns : null
  // tableGeneralFormat — SUTUN BAZLI DEGIL, tablo geneli 3 ayar (Baslik/Veri
  // font boyutu + Satir Araligi, kullanici bazinda). SmartColumnSettings.jsx
  // "Genel" bolumunun urettigi { headerFontSize, bodyFontSize, rowSpacing }
  // — SmartTable.jsx bunu CSS degiskeni olarak (.cst-root'a) uygular.
  var tableGeneralFormat = (isTableMode && tableColumnConfig && tableColumnConfig.table && typeof tableColumnConfig.table === 'object')
    ? tableColumnConfig.table : null

  // ── Tablo modu kimlik sentezi (bkz. dosya ustu helper'lar) ──
  // Board kendi w_ad/w_kod widget'ini tanimlamadiysa (Malzeme Kartlari tanimlar
  // → dokunulmaz), entity.title/subtitle'dan "Ad"/"Kod" sanal sutunlari uretilir.
  // wantName/wantCode tespiti TAM listeden (entities) yapilir — arama/filtre
  // sonucu (filteredEntities) daralinca sutunun kaybolmamasi icin.
  var tableIdentity = useMemo(function () {
    if (!isTableMode) return null
    if (hasLeadIdentity(masterWidgets)) return null
    var wantName = entities.some(function (e) { return e && e.title != null && e.title !== '' })
    var wantCode = entities.some(function (e) { return e && e.subtitle != null && e.subtitle !== '' && e.subtitle !== e.title })
    if (!wantName && !wantCode) return null
    return { wantName: wantName, wantCode: wantCode }
  }, [isTableMode, masterWidgets, entities])

  var tableMasterWidgets = useMemo(function () {
    if (!tableIdentity) return masterWidgets
    return synthesizeMasterIdentity(masterWidgets, tableIdentity.wantName, tableIdentity.wantCode)
  }, [masterWidgets, tableIdentity])

  var tableEntities = useMemo(function () {
    if (!tableIdentity) return filteredEntities
    return filteredEntities.map(function (e) { return synthesizeEntityIdentity(e, tableIdentity.wantName, tableIdentity.wantCode) })
  }, [filteredEntities, tableIdentity])

  // ── Gruplanabilir alan listesi (Grupla paneli) ──
  // Kaynak: tableMasterWidgets (kimlik-sentezli TAM master set — gorunur olsun/
  // olmasin her alan gruplanabilir). Sayisal-olmayanlar ONCE (kullanici genelde
  // metin/durum alanina gore gruplar), sayisal aile (numeric/currency/percent)
  // sona — her iki grup icinde master sirasi korunur. Kart modunda [] (buton
  // zaten render edilmez).
  var groupFields = useMemo(function () {
    if (!isTableMode) return []
    var nonNum = []
    var num = []
    tableMasterWidgets.forEach(function (w) {
      if (!w || !w.id) return
      var dt = String(w.dataType || '').toLowerCase()
      var item = { id: w.id, label: w.label || w.id, dataType: w.dataType, icon: w.icon, color: w.color }
      if (dt === 'numeric' || dt === 'currency' || dt === 'percent') num.push(item)
      else nonNum.push(item)
    })
    return nonNum.concat(num)
  }, [isTableMode, tableMasterWidgets])

  // Kayitli zincirdeki stale id'leri (artik master'da olmayan alan) ele —
  // panel numaralandirmasi ile SmartTable render'i tutarli kalsin.
  var effectiveGroupBy = useMemo(function () {
    if (groupBy.length === 0) return groupBy
    var idSet = {}
    groupFields.forEach(function (f) { idSet[f.id] = true })
    var clean = groupBy.filter(function (id) { return idSet[id] })
    return clean.length === groupBy.length ? groupBy : clean
  }, [groupBy, groupFields])

  var meshStyle = isDark
    ? {
        backgroundColor: 'var(--app-content-bg)',
        backgroundImage:
          'radial-gradient(at 20% 30%, rgba(99,102,241,0.12) 0px, transparent 50%), ' +
          'radial-gradient(at 80% 20%, rgba(14,165,233,0.08) 0px, transparent 50%), ' +
          'radial-gradient(at 50% 80%, rgba(168,85,247,0.08) 0px, transparent 50%), ' +
          'radial-gradient(at 90% 70%, rgba(20,184,166,0.06) 0px, transparent 50%)',
      }
    : {
        backgroundColor: 'var(--app-content-bg)',
        backgroundImage:
          'radial-gradient(at 20% 30%, rgba(99,102,241,0.05) 0px, transparent 50%), ' +
          'radial-gradient(at 80% 20%, rgba(14,165,233,0.04) 0px, transparent 50%), ' +
          'radial-gradient(at 50% 80%, rgba(168,85,247,0.04) 0px, transparent 50%)',
      }

  /* Yukleme gostergesi — DOM'da YER KAPLAMAZ (kullanici istegi): listenin
     uzerinde, alt ortada duran saydam bir serit. `pointer-events-none` ile
     altindaki satirlarin tiklanabilirligi bozulmaz.
     Filtre aktifken otomatik yukleme kapali oldugundan (bkz. yukaridaki not)
     orada TIKLANABILIR bir "Daha Fazla Yukle" cipi gosterilir — o da katmanda,
     yine yer kaplamadan. */
  var kalan = Math.max(0, totalCount - entities.length)

  /* Filtre taramasi seridi — kullanici "kac kayit tarandi" bilgisini GORUR ve
     istedigi an durdurabilir. Sessizce tarama yapip kullaniciyi bekletmek,
     "liste dondu mu kaldi mi" belirsizligi yaratirdi. */
  var scanOverlay = (isPaginated && hasActiveFilter && (scan.running || scan.stopped)) ? (
    <div className="pointer-events-none absolute inset-x-0 bottom-0 z-10 flex justify-center pb-2">
      <span className="pointer-events-auto flex items-center gap-2 px-3.5 py-1.5 rounded-full text-[11px] font-medium text-slate-600 bg-white/85 backdrop-blur-sm border border-slate-200 dark:text-white/70 dark:bg-[#0f172a]/85 dark:border-white/10">
        {scan.running ? <Loader2 size={13} className="animate-spin" /> : null}
        {scan.running
          ? ('Filtre taraniyor… ' + scan.scanned.toLocaleString('tr-TR') + ' / ' + totalCount.toLocaleString('tr-TR'))
          : ('Tarama durduruldu — ' + scan.scanned.toLocaleString('tr-TR') + ' kayit tarandi, sonrasi kontrol edilmedi')}
        {scan.running ? (
          <button
            onClick={function () {
              scanTokenRef.current += 1
              setScan(function (p) { return { running: false, scanned: p.scanned, done: false, stopped: true } })
            }}
            className="ml-1 px-2 py-0.5 rounded-full text-[10px] font-semibold text-slate-500 hover:text-slate-800 hover:bg-slate-100 dark:text-white/50 dark:hover:text-white dark:hover:bg-white/10"
          >Durdur</button>
        ) : (
          <button
            onClick={runFilterScan}
            className="ml-1 px-2 py-0.5 rounded-full text-[10px] font-semibold text-indigo-600 hover:bg-indigo-50 dark:text-indigo-300 dark:hover:bg-indigo-400/10"
          >Yeniden tara</button>
        )}
      </span>
    </div>
  ) : null

  /* Filtre YOKKEN yukleme gostergesi. Filtre varken "Daha Fazla Yukle" butonu
     kaldirildi (2026-08-28): filtre artik tum kayitlari tariyor, dolayisiyla
     elle sayfa ilerletmek gereksiz — ve daha onemlisi yaniltici, cunku butona
     basmadan filtre sonucunun eksik oldugu anlasilmiyordu. */
  var paginationOverlay = (isPaginated && !hasActiveFilter && loading) ? (
    <div className="pointer-events-none absolute inset-x-0 bottom-0 z-10 flex justify-center pb-2">
      <span className="flex items-center gap-2 px-3 py-1.5 rounded-full text-[11px] font-medium text-slate-500 bg-white/70 backdrop-blur-sm dark:text-white/55 dark:bg-[#0f172a]/70">
        <Loader2 size={13} className="animate-spin" />
        {kalan > 0 ? ('Yukleniyor… (' + kalan.toLocaleString('tr-TR') + ' kalan)') : 'Yukleniyor…'}
      </span>
    </div>
  ) : null

  /* Ilk yukleme (liste tamamen bos) — burada yer kaplamasi DOGRU, gosterecek
     baska bir sey yok. */
  var initialLoader = (isPaginated && loading && filteredEntities.length === 0) ? (
    <div className="flex items-center justify-center py-20 gap-3">
      <Loader2 size={24} className="animate-spin text-indigo-400" />
      <span className="text-sm text-slate-400 dark:text-white/40">Yukleniyor…</span>
    </div>
  ) : null

  return (
    // relative — toplu aksiyon seridi (.cst-bulkbar, absolute) bu kutuya gore konumlanir.
    <div className="h-full flex flex-col relative" style={meshStyle}>

      {/* ── Header ──────────────────────────── */}
      <div className="flex items-center gap-4 px-5 py-3 border-b border-slate-200/60 dark:border-white/[0.06] flex-shrink-0">
        <div className="flex items-center gap-3 flex-shrink-0">
          {iconMenu.length === 0 ? (
            <div
              className="w-9 h-9 rounded-xl flex items-center justify-center"
              style={{ background: headerPalette.bg, border: '1px solid ' + headerPalette.border }}
            >
              <HeaderIcon size={17} style={{ color: headerPalette.icon }} />
            </div>
          ) : (
            <div className="relative">
              <button
                type="button"
                onClick={function () { setIconMenuOpen(function (v) { return !v }) }}
                title="İşlemler"
                aria-haspopup="menu"
                aria-expanded={iconMenuOpen}
                className="w-9 h-9 rounded-xl flex items-center justify-center transition-shadow hover:shadow-md"
                style={{ background: headerPalette.bg, border: '1px solid ' + headerPalette.border }}
              >
                <HeaderIcon size={17} style={{ color: headerPalette.icon }} />
              </button>
              {iconMenuOpen && (
                <>
                  {/* Disari tiklayinca kapanma — ayri dinleyici yerine seffaf perde
                      (SmartBoard'un diger acilirlariyla ayni desen). */}
                  <div className="fixed inset-0 z-40" onClick={function () { setIconMenuOpen(false) }} />
                  <div
                    role="menu"
                    className="absolute left-0 top-11 z-50 min-w-[230px] rounded-xl border border-slate-200 bg-[#fff] shadow-xl py-1 dark:border-white/10 dark:bg-[#171c2a]"
                  >
                    {iconMenu.map(function (mi) {
                      var MIcon = resolveIcon(mi.icon, null, null)
                      return (
                        <button
                          key={mi.id || mi.label}
                          type="button"
                          role="menuitem"
                          onClick={function () {
                            setIconMenuOpen(false)
                            handleActionClick(mi)
                          }}
                          className="w-full flex items-center gap-2.5 px-3 py-2 text-[12px] font-medium text-slate-700 hover:bg-slate-50 dark:text-white/80 dark:hover:bg-white/[0.06]"
                        >
                          <MIcon size={14} strokeWidth={2} className="flex-shrink-0 text-slate-400 dark:text-white/45" />
                          <span className="truncate">{mi.label}</span>
                        </button>
                      )
                    })}
                  </div>
                </>
              )}
            </div>
          )}
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
                className="w-full pl-10 pr-4 py-2 rounded-xl bg-white/60 dark:bg-white/[0.04] border-[1px] border-slate-200 dark:border-white/[0.06] text-sm text-slate-700 dark:text-white/70 placeholder:text-slate-400 dark:placeholder:text-white/40 focus:outline-none focus:border-indigo-400/50 dark:focus:border-white/15 transition-colors"
              />
              {isPaginated && loading && search && (
                <Loader2 size={14} className="absolute right-3.5 top-1/2 -translate-y-1/2 text-indigo-400 animate-spin" />
              )}
            </div>
          </div>
        )}

        {!searchable && <div className="flex-1" />}

        {/* Yenile — in-place board refresh (refreshUrl yoksa tam sayfa reload) */}
        <button
          onClick={handleManualRefresh}
          disabled={refreshing}
          className={'p-2.5 rounded-xl border-[1px] transition-all group flex-shrink-0 ' +
            (refreshing
              ? 'bg-indigo-50 dark:bg-indigo-500/10 border-indigo-200 dark:border-indigo-400/30 cursor-wait'
              : 'bg-white/60 dark:bg-white/[0.04] hover:bg-white/80 dark:hover:bg-white/[0.08] border-slate-200 dark:border-white/[0.06]')
          }
          title="Yenile"
        >
          <RefreshCw size={15} className={refreshing
            ? 'text-indigo-600 dark:text-indigo-400 animate-spin'
            : 'text-slate-500 dark:text-white/40 group-hover:text-indigo-600 dark:group-hover:text-indigo-400/80 transition-colors'
          } />
        </button>

        {/* Filter button — hayalet mod (low saturation, dot indicator aktifte) */}
        <button
          onClick={function () { setFilterOpen(true) }}
          className={'relative p-2.5 rounded-xl border-[1px] transition-all group flex-shrink-0 ' +
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
              className="absolute -top-1 -right-1 min-w-[16px] h-[16px] px-1 rounded-full text-[9px] font-bold bg-indigo-500 text-white/100 flex items-center justify-center"
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
          className={'p-2.5 rounded-xl border-[1px] transition-all group flex-shrink-0 ' +
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

        {/* Grupla — YALNIZCA tablo modu. Layers ikonu + aktif rozet (Filtre
            butonuyla ayni hayalet-mod + sayaç badge deseni). Panel butonun
            altina acilir (sarmalayici .relative, SmartGroupPanel absolute). */}
        {isTableMode && (
          <div className="relative flex-shrink-0">
            <button
              onClick={function () { setGroupOpen(function (o) { return !o }) }}
              className={'relative p-2.5 rounded-xl border-[1px] transition-all group flex-shrink-0 ' +
                (effectiveGroupBy.length > 0
                  ? 'bg-indigo-50 dark:bg-indigo-500/10 border-indigo-200 dark:border-indigo-400/30'
                  : 'bg-white/60 dark:bg-white/[0.04] hover:bg-white/80 dark:hover:bg-white/[0.08] border-slate-200 dark:border-white/[0.06]')
              }
              title={effectiveGroupBy.length > 0 ? (effectiveGroupBy.length + ' alana göre gruplandı') : 'Gruplama'}
            >
              <Layers size={15} className={effectiveGroupBy.length > 0
                ? 'text-indigo-600 dark:text-indigo-400'
                : 'text-slate-500 dark:text-white/40 group-hover:text-indigo-600 dark:group-hover:text-indigo-400/80 transition-colors'
              } />
              {effectiveGroupBy.length > 0 && (
                <span
                  className="absolute -top-1 -right-1 min-w-[16px] h-[16px] px-1 rounded-full text-[9px] font-bold bg-indigo-500 text-white/100 flex items-center justify-center"
                  style={{ boxShadow: '0 0 0 2px rgba(15,23,42,0.6)' }}
                >
                  {effectiveGroupBy.length}
                </span>
              )}
            </button>
            <SmartGroupPanel
              isOpen={groupOpen}
              onClose={function () { setGroupOpen(false) }}
              fields={groupFields}
              groupBy={effectiveGroupBy}
              onChange={function (next) { setGroupBy(next) }}
              isDark={isDark}
            />
          </div>
        )}

        {/* Tablo modunda (viewMode:'table') Sutun Ayarlari (SmartColumnSettings)
            acilir; kart modunda AYNEN eskisi gibi Widget Ayarlari (SmartBoardConfigPanel).
            Regresyonsuz: viewMode!=='table' oldugunda bu dal hicbir zaman calismaz. */}
        <button
          onClick={function () { if (isTableMode) setColumnSettingsOpen(true); else setConfigOpen(true) }}
          className="p-2.5 rounded-xl bg-white/60 dark:bg-white/[0.04] hover:bg-white/80 dark:hover:bg-white/[0.08] border-[1px] border-slate-200 dark:border-white/[0.06] transition-all group flex-shrink-0"
          title={isTableMode ? 'Sütun Ayarları' : 'Widget Ayarlari'}
        >
          <Settings2 size={15} className="text-slate-500 dark:text-white/40 group-hover:text-indigo-600 dark:group-hover:text-indigo-400/80 transition-colors" />
        </button>

        {/* Islemler — ekrana ozgu toplu islemlerin TEK yeri (bkz. toolbarMenu notu).
            Widget/Sutun ayarlarindan sonra, ana eylemden ONCE durur: buton sirasi
            C-Grid standardidir, ekranlar arasi kas hafizasini bozmamak icin sabittir. */}
        {toolbarMenu.length > 0 && (
          <div className="relative flex-shrink-0">
            <button
              type="button"
              onClick={function () { setToolbarMenuOpen(function (v) { return !v }) }}
              title="İşlemler"
              aria-haspopup="menu"
              aria-expanded={toolbarMenuOpen}
              className="p-2.5 rounded-xl bg-white/60 dark:bg-white/[0.04] hover:bg-white/80 dark:hover:bg-white/[0.08] border-[1px] border-slate-200 dark:border-white/[0.06] transition-all group"
            >
              <Wrench size={15} className="text-slate-500 dark:text-white/40 group-hover:text-indigo-600 dark:group-hover:text-indigo-400/80 transition-colors" />
            </button>
            {toolbarMenuOpen && (
              <>
                {/* Disari tiklayinca kapanma — seffaf perde (iconMenu ile ayni desen). */}
                <div className="fixed inset-0 z-40" onClick={function () { setToolbarMenuOpen(false) }} />
                <div
                  role="menu"
                  className="absolute right-0 top-11 z-50 min-w-[230px] rounded-xl border border-slate-200 bg-[#fff] shadow-xl py-1 dark:border-white/10 dark:bg-[#171c2a]"
                >
                  {toolbarMenu.map(function (mi) {
                    var MIcon = resolveIcon(mi.icon, null, null)
                    return (
                      <button
                        key={mi.id || mi.label}
                        type="button"
                        role="menuitem"
                        onClick={function () {
                          setToolbarMenuOpen(false)
                          handleActionClick(mi)
                        }}
                        className="w-full flex items-center gap-2.5 px-3 py-2 text-[12px] font-medium text-slate-700 hover:bg-slate-50 dark:text-white/80 dark:hover:bg-white/[0.06]"
                      >
                        <MIcon size={14} strokeWidth={2} className="flex-shrink-0 text-slate-400 dark:text-white/45" />
                        <span className="truncate">{mi.label}</span>
                      </button>
                    )
                  })}
                </div>
              </>
            )}
          </div>
        )}

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
                  title={isPrimary ? action.label + ' (Alt+N / Insert)' : action.label}
                  aria-label={action.label}
                  className={'p-2.5 rounded-xl border-[1px] transition-all group flex-shrink-0 ' +
                    (isPrimary
                      ? 'bg-indigo-500 hover:bg-indigo-600 dark:bg-indigo-500/20 dark:hover:bg-indigo-500/30 border-indigo-500 dark:border-indigo-400/20 text-white/100 dark:text-indigo-300 shadow-sm'
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

      {/* ── Kart / Tablo Listesi ─────────────────
          viewMode==='table' → SmartTable (satir bazli); aksi halde mevcut kart
          listesi AYNEN kalir. Sonsuz kaydirma append'i (SmartBoard.entities state'i
          buyur) otomatik olarak yeni satir/kart olarak gorunur; yukleme gostergesi
          listenin USTUNDE saydam katmanda durur (yer kaplamaz). */}
      <div ref={listBodyRef} className="relative flex-1 overflow-y-auto px-4 py-3 min-h-0">
        {filteredEntities.length === 0 && !loading ? (
          <div className="text-center py-20">
            <HeaderIcon size={36} className="mx-auto text-slate-300 dark:text-white/30 mb-3" />
            <p className="text-sm text-slate-400 dark:text-white/45">{emptyText}</p>
          </div>
        ) : viewMode === 'table' ? (
          <div className="flex flex-col gap-3 min-w-0 h-full min-h-0">
            <SmartTable
              entities={tableEntities}
              masterWidgets={tableMasterWidgets}
              visibleIds={visibleIds}
              order={order}
              columnConfig={tableColumnFormats}
              tableFormat={tableGeneralFormat}
              groupBy={effectiveGroupBy}
              onRefresh={refreshUrl ? refreshBoard : undefined}
              recentIds={recentIds}
              isDark={isDark}
              selectable={selectEnabled}
              selectedIds={selectedIds}
              onToggleSelect={handleToggleSelect}
              onToggleSelectAll={handleToggleSelectAll}
              expandable={expandEnabled}
              expandedIds={expandedIds}
              onToggleExpand={handleToggleExpand}
              renderDetail={renderDetail}
            />
            {initialLoader}
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
            {initialLoader}
          </div>
        )}
        {paginationOverlay}
        {scanOverlay}
      </div>

      {/* ── Widget Config Panel (kart modu) ─── */}
      <SmartBoardConfigPanel
        isOpen={configOpen}
        onClose={function () { setConfigOpen(false) }}
        boardKey={boardKey}
        masterWidgets={masterWidgets}
        onSaved={handleConfigSaved}
      />

      {/* ── Sutun Ayarlari Paneli (tablo modu) ─
          Sadece isTableMode iken mount edilir — kart modu board'lari icin bu
          bilesen HIC render edilmez (extra network call / hook calismaz,
          regresyon riski sifir). onChange artik SADECE Kaydet'te degil, panel
          icindeki HER degisiklikte senkron cagrilir (2026-07-25, Kaydet/Iptal
          kalkti — bkz. SmartColumnSettings.jsx dosya ustu notu); bu yuzden
          tableColumnConfig set'i ve dolayisiyla SmartTable re-render'i panel
          acikken sik sik tetiklenir — bilincli (anlik onizleme). Kalicilik
          (localStorage+backend) panelin kendi icinde debounce'lidir. */}
      {isTableMode && (
        <SmartColumnSettings
          isOpen={columnSettingsOpen}
          onClose={function () { setColumnSettingsOpen(false) }}
          boardKey={boardKey}
          masterWidgets={tableMasterWidgets}
          onChange={function (cfg) { setTableColumnConfig(cfg) }}
        />
      )}

      {/* ── Filter Panel (hayalet mod) ─────── */}
      <SmartBoardFilterPanel
        isOpen={filterOpen}
        onClose={function () { setFilterOpen(false) }}
        boardKey={boardKey}
        formCode={formCode}
        masterWidgets={masterWidgets}
        entities={entities}
        filters={filters}
        serverFilters={serverFilters}
        onApply={function (next, serverNext) {
          setFilters(next)
          // serverNext yalnizca sunucu filtresi DEGISTIYSE gelir. Board bu secimi
          // kendisi uygulayamaz (hangi sorgu parametresi oldugunu bilmez) — sayfaya
          // devreder; sayfa config'i yeniden ceker.
          if (serverNext && onServerFilterChange) onServerFilterChange(serverNext)
        }}
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
            background: 'var(--app-surface)',
            border: '1px solid var(--app-border)',
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
              color: 'var(--app-text)',
            }}>
              Excel'e Aktar
            </h3>

            {/* Açıklama */}
            <p style={{
              fontSize: '13px', lineHeight: 1.65, marginBottom: '24px',
              color: 'var(--app-text-muted)',
            }}>
              <strong style={{ color: 'var(--app-text)' }}>{title}</strong> listesi
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
                  background: 'var(--app-muted-surface)',
                  color: 'var(--app-text-muted)',
                  border: '1px solid var(--app-border)',
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
      {/* ── Toplu aksiyon seridi — secim varken alttan belirir. Silme/geri
          alinamaz aksiyonlar icin action.confirm metni verilir (CLAUDE.md
          silme-onay standardi: ortada modal degil, burada seride onay istenir
          — seride yalnizca tetikleme var, onay modali runBulkAction icinde). */}
      {selectEnabled && bulkActions.length > 0 && selectedIds.size > 0 && (
        <div className="cst-bulkbar">
          <span className="cst-bulkbar__cnt">{selectedIds.size} seçili</span>
          <button type="button" className="cst-bulk-btn cst-bulk-btn--ghost" onClick={clearSelection}>
            Seçimi Temizle
          </button>
          <span className="cst-bulkbar__sep" />
          {bulkActions.map(function (a, i) {
            var ActionIcon = a.icon ? resolveIcon(a.icon) : null
            return (
              <button
                key={a.id || a.label || i}
                type="button"
                disabled={bulkBusy}
                className={'cst-bulk-btn cst-bulk-btn--' + (a.variant === 'danger' ? 'danger' : a.variant === 'ghost' ? 'ghost' : 'primary')}
                onClick={function () { runBulkAction(a) }}
              >
                {ActionIcon ? <ActionIcon size={14} /> : null}
                {a.label || 'Uygula'}
              </button>
            )
          })}
        </div>
      )}
      {/* Toplu aksiyon onayı — ekranın ORTASINDA modal (CLAUDE.md silme-onay
          standardı: backdrop + ikon + başlık + Vazgeç/Onayla; Esc ve backdrop
          tıklaması iptal eder, onay butonu odakta açılır). */}
      {bulkConfirm && (
        <div
          style={{ position: 'fixed', inset: 0, zIndex: 10000, background: 'rgba(0,0,0,0.6)', backdropFilter: 'blur(4px)', display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 20 }}
          onClick={function () { setBulkConfirm(null) }}
          onKeyDown={function (e) { if (e.key === 'Escape') setBulkConfirm(null) }}
        >
          <div
            style={{ background: 'var(--app-surface)', border: '1px solid var(--app-border)', borderRadius: 16, padding: '32px 28px', maxWidth: 400, width: '90vw', boxShadow: '0 24px 64px rgba(0,0,0,0.5)', display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 12, textAlign: 'center' }}
            onClick={function (e) { e.stopPropagation() }}
          >
            <AlertTriangle size={26} style={{ color: bulkConfirm.variant === 'danger' ? '#ef4444' : '#6366f1' }} />
            <h3 style={{ fontSize: '1.05rem', fontWeight: 700, color: 'var(--app-text)', margin: 0 }}>Emin misiniz?</h3>
            <p style={{ fontSize: '.84rem', color: 'var(--app-text-muted)', margin: 0, lineHeight: 1.5 }}>{bulkConfirm.confirm}</p>
            <div style={{ display: 'flex', gap: 10, marginTop: 8 }}>
              <button
                type="button"
                onClick={function () { setBulkConfirm(null) }}
                style={{ padding: '8px 16px', borderRadius: 8, fontSize: '.84rem', fontWeight: 600, background: 'var(--app-muted-surface)', color: 'var(--app-text)', border: '1px solid var(--app-border)', cursor: 'pointer' }}
              >
                Vazgeç
              </button>
              <button
                type="button"
                autoFocus
                onClick={function () { var a = bulkConfirm; setBulkConfirm(null); executeBulkAction(a) }}
                style={{ padding: '8px 16px', borderRadius: 8, fontSize: '.84rem', fontWeight: 600, border: 'none', cursor: 'pointer', color: '#fff', background: bulkConfirm.variant === 'danger' ? 'linear-gradient(135deg,#ef4444,#dc2626)' : 'linear-gradient(135deg,#6366f1,#4f46e5)' }}
              >
                {bulkConfirm.label || 'Onayla'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
