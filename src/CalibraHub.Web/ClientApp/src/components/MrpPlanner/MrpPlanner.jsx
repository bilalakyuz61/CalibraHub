/**
 * MrpPlanner — Malzeme İhtiyaç Planlama (MRP), Faz 2 (2026-08-29).
 *
 * Üç adım, tek sayfa:
 *   1) Sipariş seçimi — açık satış siparişi satırları (çoklu seçim)
 *   2) Önizleme       — POST /Production/MrpPreview → Draft koşu + planlanan emirler
 *   3) Onay           — POST /Production/MrpApply → iş emirleri açılır
 *
 * Önizleme HİÇBİR iş emri açmaz; onay TÜM planı uygular (düğüm bazlı seçim yok — alt
 * emri açılmayan üst emir eksik bileşenle kalırdı).
 *
 * SmartBoard bileşeni DEĞİL, bespoke liste (RoutingTree.jsx ile aynı istisna): çok
 * seviyeli plan ağacı + tek seferlik onay kapısı SmartBoard'un kart/entity modeline
 * sığmıyor. Ama C-Grid SAYFA STANDARDINA birebir uyar (2026-08-29):
 *
 *   [ikon] Başlık / alt başlık … [arama] [Filtre] [Excel] [Sütun ayarları] [Ana eylem]
 *
 * Filtre ve sütun ayarı panelleri SmartBoard'un paylaşılan bileşenleridir
 * (SmartBoardFilterPanel / SmartBoardConfigPanel); satırlar bu paneller için
 * "widget" biçimine çevrilir (buildLineWidgets / buildNodeWidgets). Böylece kullanıcı
 * diğer liste ekranlarındakiyle AYNI filtre ve sütun deneyimini görür, tercihleri de
 * aynı yerde (boardKey bazlı) saklanır.
 */
import { useState, useEffect, useCallback, useMemo } from 'react'
import {
  Workflow, Search, RefreshCw, ChevronRight, ChevronDown, Check, X,
  AlertTriangle, Loader2, PackageCheck, PlusCircle, GitMerge, ShoppingCart, Download,
  Filter, Settings2,
} from 'lucide-react'
import SmartBoardConfigPanel from '../CalibraSmartBoard/SmartBoardConfigPanel'
import SmartBoardFilterPanel, { entityMatchesFilters } from '../CalibraSmartBoard/SmartBoardFilterPanel'
import { loadWidgetConfig } from '../../services/widgetConfigService'
import './MrpPlanner.css'

// ── Biçimlendirme ────────────────────────────────────────────────────────────────
function fmtQty(v) {
  if (v == null) return '—'
  try { return Number(v).toLocaleString('tr-TR', { minimumFractionDigits: 0, maximumFractionDigits: 4 }) }
  catch (e) { return String(v) }
}
function fmtDate(iso) {
  if (!iso) return '—'
  var m = /^(\d{4})-(\d{2})-(\d{2})/.exec(String(iso))
  return m ? (m[3] + '.' + m[2] + '.' + m[1]) : String(iso)
}

var POLICY_LABEL = {
  PerOrderLine: 'Satır bazında',
  PerOrder:     'Sipariş bazında',
  Cumulative:   'Kümüle',
}

var ACTION_META = {
  NewWorkOrder:    { label: 'Yeni İş Emri',      cls: 'mrp-badge--indigo',  Icon: PlusCircle },
  MergeWorkOrder:  { label: 'Mevcut Emre Ekle',  cls: 'mrp-badge--blue',    Icon: GitMerge },
  CoveredByStock:  { label: 'Stoktan Karşılandı', cls: 'mrp-badge--emerald', Icon: PackageCheck },
  Shortage:        { label: 'Aksiyon Yok',       cls: 'mrp-badge--rose',    Icon: AlertTriangle },
  PurchaseRequest: { label: 'Satın Alma',        cls: 'mrp-badge--amber',   Icon: ShoppingCart },
}

/* ── C-Grid sütun katalogları ─────────────────────────────────────────────────
   Sütun ayarları paneli (SmartBoardConfigPanel) "master widget" listesi bekler:
   { id, label, dataType }. Sabit sütunlu bir tabloda bu liste kolonların ta kendisidir.
   `locked` olanlar panelde gizlenmez — onlar olmadan satır anlamsız kalır
   (seçim kutusu, malzeme, aksiyon). */
var LINE_COLUMNS = [
  { id: 'documentNumber',    label: 'Belge',        dataType: 'text',    locked: true },
  { id: 'contactName',       label: 'Cari',         dataType: 'text' },
  { id: 'item',              label: 'Malzeme',      dataType: 'text',    locked: true },
  { id: 'isProducible',      label: 'Üretilebilir', dataType: 'boolean' },
  { id: 'splitPolicy',       label: 'Kırılım',      dataType: 'text' },
  { id: 'orderQuantity',     label: 'Sipariş',      dataType: 'numeric' },
  { id: 'deliveredQuantity', label: 'Teslim',       dataType: 'numeric' },
  { id: 'reservedQuantity',  label: 'Rezerve',      dataType: 'numeric' },
  { id: 'allocatedQuantity', label: 'İş Emrinde',   dataType: 'numeric' },
  { id: 'openQuantity',      label: 'Açık',         dataType: 'numeric', locked: true },
  { id: 'deliveryDate',      label: 'Teslim Tarihi', dataType: 'date' },
]

/* Filtre paneli entity.widgets üzerinden çalışır — satırı o biçime çeviriyoruz.
   Not: değerler GÖSTERİLDİĞİ gibi (etiketlenmiş) verilir; kullanıcı filtre panelinde
   "Mamul" yazdığında ekranda gördüğü değerle eşleşsin. */
function buildLineWidgets(l, policyLabel) {
  return [
    { id: 'documentNumber',    label: 'Belge',         dataType: 'text',    value: l.documentNumber || '' },
    { id: 'contactName',       label: 'Cari',          dataType: 'text',    value: l.contactName || '' },
    { id: 'item',              label: 'Malzeme',       dataType: 'text',    value: (l.itemCode || '') + ' ' + (l.itemName || '') },
    { id: 'isProducible',      label: 'Üretilebilir',  dataType: 'boolean', value: l.isProducible ? 'Evet' : 'Hayır' },
    { id: 'splitPolicy',       label: 'Kırılım',       dataType: 'text',    value: policyLabel },
    { id: 'orderQuantity',     label: 'Sipariş',       dataType: 'numeric', value: l.orderQuantity },
    { id: 'deliveredQuantity', label: 'Teslim',        dataType: 'numeric', value: l.deliveredQuantity },
    { id: 'reservedQuantity',  label: 'Rezerve',       dataType: 'numeric', value: l.reservedQuantity },
    { id: 'allocatedQuantity', label: 'İş Emrinde',    dataType: 'numeric', value: l.allocatedQuantity },
    { id: 'openQuantity',      label: 'Açık',          dataType: 'numeric', value: l.openQuantity },
    { id: 'deliveryDate',      label: 'Teslim Tarihi', dataType: 'date',    value: l.deliveryDate || '' },
  ]
}

/** Kullanıcının sütun tercihini uygular; kayıt yoksa katalog sırası aynen döner. */
function visibleColumns(catalog, userCfg) {
  var cfg = userCfg || {}
  var vis = Array.isArray(cfg.visibleIds) ? cfg.visibleIds : null
  var order = Array.isArray(cfg.order) ? cfg.order : null
  var list = catalog.filter(function (c) { return c.locked || !vis || vis.indexOf(c.id) >= 0 })
  if (!order) return list
  return list.slice().sort(function (a, b) {
    var ia = order.indexOf(a.id), ib = order.indexOf(b.id)
    if (ia < 0) ia = 999
    if (ib < 0) ib = 999
    return ia - ib
  })
}

function csrfToken() {
  var el = document.querySelector('input[name="__RequestVerificationToken"]')
  return el ? el.value : ''
}

/**
 * Excel dışa aktarımı — C-Grid standardındaki `/api/export/smartboard-excel` ucuna
 * form POST'u. fetch DEĞİL: tarayıcı dosyayı doğrudan indirsin, blob bellekte
 * tutulmasın (RoutingTree deseni).
 */
function submitExcel(fileBase, sheetName, headers, rows) {
  var ts = new Date()
  var pad = function (x) { return x < 10 ? '0' + x : String(x) }
  var stamp = ts.getFullYear() + pad(ts.getMonth() + 1) + pad(ts.getDate()) + '_' +
              pad(ts.getHours()) + pad(ts.getMinutes()) + pad(ts.getSeconds())

  var form = document.createElement('form')
  form.method = 'POST'; form.action = '/api/export/smartboard-excel'
  form.target = '_self'; form.style.display = 'none'
  var hidden = document.createElement('textarea')
  hidden.name = 'payload'
  hidden.value = JSON.stringify({
    fileName: fileBase + '_' + stamp + '.xlsx',
    sheetName: sheetName,
    headers: headers, rows: rows,
  })
  form.appendChild(hidden)
  var token = csrfToken()
  if (token) {
    var ti = document.createElement('input')
    ti.type = 'hidden'; ti.name = '__RequestVerificationToken'; ti.value = token
    form.appendChild(ti)
  }
  document.body.appendChild(form)
  form.submit()
  setTimeout(function () { try { document.body.removeChild(form) } catch (e) {} }, 1000)
}

function postJson(url, body) {
  return fetch(url, {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': csrfToken() },
    body: JSON.stringify(body || {}),
  }).then(function (r) { return r.json() })
}

export default function MrpPlanner(props) {
  var config = props.config || {}
  var focusDocumentId = config.documentId > 0 ? config.documentId : null

  var [step, setStep]         = useState(1)
  var [loading, setLoading]   = useState(false)
  var [error, setError]       = useState(null)
  var [lines, setLines]       = useState([])
  var [selected, setSelected] = useState({})     // lineId → true
  var [search, setSearch]     = useState('')
  var [preview, setPreview]   = useState(null)   // { runId, nodes, summary }
  var [expanded, setExpanded] = useState({})     // runLineId → true
  var [applyResult, setApplyResult] = useState(null)
  var [confirmOpen, setConfirmOpen] = useState(false)
  var [discardOpen, setDiscardOpen] = useState(false)
  // Eksik (satın alınacak) malzemeler icin talep belgesi de olussun mu — varsayilan ACIK,
  // kullanici kapatabilir (o zaman yalniz rapor).
  var [createPr, setCreatePr] = useState(true)

  // ── C-Grid araçları (2026-08-29) ──
  // boardKey: kullanıcı tercihleri (sütun görünürlüğü/sırası, filtreler) bu anahtar
  // altında saklanır — diğer board'larla çakışmaz.
  var LINE_BOARD_KEY = 'production-mrp-lines'
  var [filters, setFilters]           = useState([])
  var [filterOpen, setFilterOpen]     = useState(false)
  var [columnsOpen, setColumnsOpen]   = useState(false)
  var [lineCfg, setLineCfg]           = useState(function () { return loadWidgetConfig(LINE_BOARD_KEY) })
  var lineColumns = useMemo(function () { return visibleColumns(LINE_COLUMNS, lineCfg) }, [lineCfg])
  var showCol = useCallback(function (id) {
    return lineColumns.some(function (c) { return c.id === id })
  }, [lineColumns])

  // ── 1. adım: açık satırları yükle ──
  var loadLines = useCallback(function (q) {
    setLoading(true); setError(null)
    var url = '/Production/MrpOpenLines?search=' + encodeURIComponent(q || '')
    if (focusDocumentId) url += '&documentId=' + focusDocumentId
    fetch(url, { credentials: 'same-origin' })
      .then(function (r) { return r.json() })
      .then(function (d) {
        if (!d || !d.ok) { setError((d && d.error) || 'Satırlar yüklenemedi.'); setLines([]); return }
        setLines(d.lines || [])
        // Sipariş kartı kısayolunda o belgenin tüm satırları seçili gelir.
        if (focusDocumentId) {
          var pre = {}
          ;(d.lines || []).forEach(function (l) { pre[l.lineId] = true })
          setSelected(pre)
        }
      })
      .catch(function (e) { setError('Sunucuya ulaşılamadı: ' + (e.message || e)); setLines([]) })
      .finally(function () { setLoading(false) })
  }, [focusDocumentId])

  useEffect(function () { loadLines('') }, [loadLines])

  var selectedIds = useMemo(function () {
    return Object.keys(selected).filter(function (k) { return selected[k] }).map(Number)
  }, [selected])

  /* Satırlar filtre/sütun panelleri icin entity bicimine cevrilir (widgets dizisi).
     Ayni donusum hem filtreleme hem panelin alan kesfi icin kullanilir. */
  var lineEntities = useMemo(function () {
    return lines.map(function (l) {
      return {
        id: l.lineId,
        title: l.itemCode || '',
        subtitle: l.documentNumber || '',
        description: l.itemName || '',
        widgets: buildLineWidgets(l, POLICY_LABEL[l.splitPolicy] || l.splitPolicy || ''),
      }
    })
  }, [lines])

  var visibleLines = useMemo(function () {
    var q = search.trim().toLocaleLowerCase('tr')
    var byId = {}
    lineEntities.forEach(function (e) { byId[e.id] = e })
    return lines.filter(function (l) {
      if (q) {
        var hit = (l.itemCode || '').toLocaleLowerCase('tr').indexOf(q) >= 0
               || (l.itemName || '').toLocaleLowerCase('tr').indexOf(q) >= 0
               || (l.documentNumber || '').toLocaleLowerCase('tr').indexOf(q) >= 0
               || (l.contactName || '').toLocaleLowerCase('tr').indexOf(q) >= 0
        if (!hit) return false
      }
      // Filtreler SmartBoard ile AYNI motordan gecer — davranis farki olmasin.
      if (filters.length > 0 && !entityMatchesFilters(byId[l.lineId], filters)) return false
      return true
    })
  }, [lines, search, filters, lineEntities])

  var allVisibleSelected = visibleLines.length > 0 &&
    visibleLines.every(function (l) { return selected[l.lineId] })

  function toggleAll() {
    var next = Object.assign({}, selected)
    var target = !allVisibleSelected
    visibleLines.forEach(function (l) { next[l.lineId] = target })
    setSelected(next)
  }

  // ── 2. adım: önizleme ──
  function runPreview() {
    if (selectedIds.length === 0) return
    setLoading(true); setError(null); setApplyResult(null)
    postJson('/Production/MrpPreview', {
      lineIds: selectedIds,
      sourceScope: focusDocumentId ? 'SingleOrder' : 'Selected',
    })
      .then(function (d) {
        if (!d || !d.ok) { setError((d && d.error) || 'Önizleme hesaplanamadı.'); return }
        setPreview(d); setStep(2); setExpanded({})
      })
      .catch(function (e) { setError('Sunucuya ulaşılamadı: ' + (e.message || e)) })
      .finally(function () { setLoading(false) })
  }

  // ── 3. adım: uygula ──
  function applyPlan() {
    if (!preview || !preview.runId) return
    setConfirmOpen(false); setLoading(true); setError(null)
    postJson('/Production/MrpApply', { runId: preview.runId, createPurchaseRequest: !!createPr })
      .then(function (d) {
        if (!d || !d.ok) { setError((d && d.error) || 'Plan uygulanamadı.'); return }
        setApplyResult(d); setStep(3)
      })
      .catch(function (e) { setError('Sunucuya ulaşılamadı: ' + (e.message || e)) })
      .finally(function () { setLoading(false) })
  }

  function discardRun() {
    setDiscardOpen(false)
    if (preview && preview.runId) {
      postJson('/Production/MrpDiscard?runId=' + preview.runId, {}).catch(function () { /* iptal başarısızsa koşu Draft kalır, zararsız */ })
    }
    setPreview(null); setApplyResult(null); setStep(1); setError(null)
  }

  // ── Excel dışa aktarım (C-Grid standardı) ──
  // Önizlenen planı olduğu gibi indirir. Form POST kullanılır (fetch değil): tarayıcı
  // dosyayı doğrudan indirir, blob'u bellekte tutmaya gerek kalmaz — RoutingTree deseni.
  /** Adım 1'deki açık sipariş satırlarını dışa aktarır (görünen sütunlar + filtre uygulanmış). */
  function exportLinesExcel() {
    if (!visibleLines.length) return
    var cols = lineColumns
    var headers = cols.map(function (c) { return { id: c.id, label: c.label } })
    var rows = visibleLines.map(function (l) {
      var r = {}
      cols.forEach(function (c) {
        if (c.id === 'item') r.item = (l.itemCode || '') + (l.itemName ? ' — ' + l.itemName : '')
        else if (c.id === 'isProducible') r.isProducible = l.isProducible ? 'Evet' : 'Hayır'
        else if (c.id === 'splitPolicy') r.splitPolicy = POLICY_LABEL[l.splitPolicy] || l.splitPolicy
        else if (c.id === 'deliveryDate') r.deliveryDate = fmtDate(l.deliveryDate)
        else r[c.id] = l[c.id]
      })
      return r
    })
    submitExcel('mrp-siparis-satirlari', 'Acik Siparis Satirlari', headers, rows)
  }

  function exportExcel() {
    if (!preview || !preview.nodes.length) return
    var headers = [
      { id: 'level',    label: 'Seviye' },
      { id: 'itemCode', label: 'Stok Kodu' },
      { id: 'itemName', label: 'Stok Adı' },
      { id: 'action',   label: 'Aksiyon' },
      { id: 'policy',   label: 'Kırılım' },
      { id: 'gross',    label: 'Brüt' },
      { id: 'onHand',   label: 'Eldeki' },
      { id: 'supply',   label: 'Açık Arz' },
      { id: 'net',      label: 'Net' },
      { id: 'start',    label: 'Başlangıç' },
      { id: 'end',      label: 'Bitiş' },
      { id: 'sources',  label: 'Kaynak Siparişler' },
      { id: 'message',  label: 'Not' },
    ]
    var rows = preview.nodes.map(function (n) {
      return {
        level:    n.level,
        itemCode: n.itemCode || ('#' + n.itemId),
        itemName: n.itemName || '',
        action:   (ACTION_META[n.actionType] || {}).label || n.actionType,
        policy:   POLICY_LABEL[n.splitPolicy] || n.splitPolicy,
        gross:    n.grossQuantity,
        onHand:   n.onHandApplied,
        supply:   n.openSupplyApplied,
        net:      n.netQuantity,
        start:    fmtDate(n.plannedStartDate),
        end:      fmtDate(n.plannedEndDate),
        sources:  (n.pegs || []).map(function (p) { return p.rootDocumentNumber + ' (' + fmtQty(p.quantity) + ')' }).join(', '),
        message:  n.message || '',
      }
    })

    submitExcel('mrp-plani', 'MRP Plani', headers, rows)
  }

  function restart() {
    setPreview(null); setApplyResult(null); setSelected({}); setStep(1); setError(null)
    loadLines(search)
  }

  var actionableCount = preview
    ? (preview.summary.newWorkOrderCount + preview.summary.mergeWorkOrderCount)
    : 0

  return (
    <div className="mrp-root">
      {/* ── ŞERİT (C-Grid sayfa standardı) ──────────────────────────────────
          Sıra sabittir: kimlik → arama → Filtre → Excel → Sütunlar → ana eylem.
          Kullanıcı hangi liste ekranına giderse gitsin aynı düzeni bulur. */}
      <div className="mrp-header">
        <div className="mrp-header__id">
          <div className="mrp-header-icon"><Workflow size={18} strokeWidth={2} /></div>
          <div style={{ minWidth: 0 }}>
            <div className="mrp-header-title">Malzeme İhtiyaç Planlama</div>
            <div className="mrp-header-sub">
              {step === 1
                ? (visibleLines.length + ' açık sipariş satırı · ' + selectedIds.length + ' seçili')
                : step === 2
                  ? ('Koşu #' + preview.runId + ' · ' + preview.nodes.length + ' satır')
                  : 'Plan uygulandı'}
            </div>
          </div>
        </div>

        {step === 1 && (
          <div className="mrp-search">
            <Search size={13} className="mrp-dim" />
            <input
              type="text" value={search} placeholder="Malzeme, belge no veya cari ara…"
              onChange={function (e) { setSearch(e.target.value) }}
            />
            {search && (
              <button type="button" className="mrp-search__clear" title="Aramayı temizle"
                      onClick={function () { setSearch('') }}><X size={12} /></button>
            )}
          </div>
        )}

        <div className="mrp-header__tools">
          {step === 1 && (
            <>
              {/* SIRA SmartBoard ile BIREBIR AYNI (2026-08-29 kullanici kurali):
                  arama → Yenile → Filtre → Excel → Ayarlar → ana eylem.
                  Ayni isi yapan buton her ekranda ayni yerde olmali; sirayi burada
                  degistirmek kullanicinin kas hafizasini bozar. */}
              <button type="button" className="mrp-icon-btn" title="Yenile"
                      onClick={function () { loadLines(search) }} disabled={loading}>
                <RefreshCw size={15} />
              </button>
              <button type="button"
                      className={'mrp-icon-btn' + (filters.length > 0 ? ' mrp-icon-btn--active' : '')}
                      title={filters.length > 0 ? (filters.length + ' filtre aktif') : 'Filtreleme'}
                      onClick={function () { setFilterOpen(true) }}>
                <Filter size={15} />
                {filters.length > 0 && <span className="mrp-icon-btn__badge">{filters.length}</span>}
              </button>
              <button type="button" className="mrp-icon-btn" title="Excel'e Aktar"
                      onClick={exportLinesExcel} disabled={visibleLines.length === 0}>
                <Download size={15} />
              </button>
              <button type="button" className="mrp-icon-btn" title="Sütun Ayarları"
                      onClick={function () { setColumnsOpen(true) }}>
                <Settings2 size={15} />
              </button>
              <button type="button" className="mrp-btn mrp-btn--primary"
                      onClick={runPreview} disabled={loading || selectedIds.length === 0}>
                {loading ? <Loader2 size={13} /> : <ChevronRight size={13} />} Planı Hesapla
              </button>
            </>
          )}

          {step === 2 && (
            <>
              <button type="button" className="mrp-icon-btn" title="Planı Excel olarak indir"
                      onClick={exportExcel}>
                <Download size={15} />
              </button>
              <button type="button" className="mrp-btn mrp-btn--primary"
                      onClick={function () { setConfirmOpen(true) }} disabled={loading || actionableCount === 0}>
                <Check size={13} /> Planı Uygula
              </button>
            </>
          )}

          {step === 3 && (
            <button type="button" className="mrp-btn mrp-btn--primary" onClick={restart}>
              <RefreshCw size={13} /> Yeni Plan
            </button>
          )}
        </div>
      </div>

      {/* ── ADIM GÖSTERGESİ ── */}
      <div className="mrp-steps">
        <div className={'mrp-step ' + (step === 1 ? 'mrp-step--active' : '')}>
          <span className="mrp-step-num">1</span> Sipariş Seçimi
        </div>
        <span className="mrp-step-sep" />
        <div className={'mrp-step ' + (step === 2 ? 'mrp-step--active' : '')}>
          <span className="mrp-step-num">2</span> Önizleme
        </div>
        <span className="mrp-step-sep" />
        <div className={'mrp-step ' + (step === 3 ? 'mrp-step--active' : '')}>
          <span className="mrp-step-num">3</span> Sonuç
        </div>
      </div>

      {/* ── GÖVDE ── */}
      <div className="mrp-body">
        {error && <div className="mrp-alert"><AlertTriangle size={13} style={{ verticalAlign: '-2px' }} /> {error}</div>}

        {step === 1 && (
          loading && lines.length === 0
            ? <div className="mrp-empty"><Loader2 size={18} className="mrp-dim" /> Yükleniyor…</div>
            : visibleLines.length === 0
              ? <div className="mrp-empty">Açık satış siparişi satırı bulunamadı.</div>
              : (
                <div className="mrp-table-wrap">
                  <table className="mrp-table">
                    <thead>
                      <tr>
                        <th style={{ width: 34 }}>
                          <input type="checkbox" checked={allVisibleSelected} onChange={toggleAll}
                                 aria-label="Tümünü seç" />
                        </th>
                        {/* Başlıklar sütun ayarındaki SIRAYA ve görünürlüğe uyar. */}
                        {lineColumns.map(function (c) {
                          var numeric = c.dataType === 'numeric'
                          return <th key={c.id} className={numeric ? 'mrp-num' : undefined}>{c.label}</th>
                        })}
                      </tr>
                    </thead>
                    <tbody>
                      {visibleLines.map(function (l) {
                        return (
                          <tr key={l.lineId}>
                            <td>
                              <input type="checkbox" checked={!!selected[l.lineId]}
                                     onChange={function () {
                                       var n = Object.assign({}, selected)
                                       n[l.lineId] = !n[l.lineId]
                                       setSelected(n)
                                     }} />
                            </td>
                            {lineColumns.map(function (c) {
                              switch (c.id) {
                                case 'documentNumber':
                                  return <td key={c.id} style={{ fontWeight: 600 }}>{l.documentNumber}</td>
                                case 'contactName':
                                  return <td key={c.id} className="mrp-dim">{l.contactName || '—'}</td>
                                case 'item':
                                  return (
                                    <td key={c.id}>
                                      <div style={{ fontWeight: 600 }}>{l.itemCode}</div>
                                      <div className="mrp-dim" style={{ fontSize: 11 }}>{l.itemName}</div>
                                    </td>
                                  )
                                case 'isProducible':
                                  // Üretilebilirlik AYRI sütun — malzeme hücresini üç satıra
                                  // çıkarıp satır yüksekliğini şişirmesin (kompakt liste).
                                  return (
                                    <td key={c.id}>
                                      <span className={'mrp-badge ' + (l.isProducible ? 'mrp-badge--emerald' : 'mrp-badge--rose')}
                                            title={l.isProducible
                                              ? 'Mamul / Yarı Mamul — iş emri açılabilir'
                                              : 'Üretilebilir tipte değil — iş emri açılamaz, satın alma önerilir'}>
                                        {l.isProducible ? 'Evet' : 'Hayır'}
                                      </span>
                                    </td>
                                  )
                                case 'splitPolicy':
                                  return <td key={c.id}><span className="mrp-policy">{POLICY_LABEL[l.splitPolicy] || l.splitPolicy}</span></td>
                                case 'orderQuantity':
                                  return <td key={c.id} className="mrp-num">{fmtQty(l.orderQuantity)} {l.unitCode || ''}</td>
                                case 'deliveredQuantity':
                                  return <td key={c.id} className="mrp-num mrp-dim">{fmtQty(l.deliveredQuantity)}</td>
                                case 'reservedQuantity':
                                  return <td key={c.id} className="mrp-num mrp-dim">{fmtQty(l.reservedQuantity)}</td>
                                case 'allocatedQuantity':
                                  return <td key={c.id} className="mrp-num mrp-dim">{fmtQty(l.allocatedQuantity)}</td>
                                case 'openQuantity':
                                  return <td key={c.id} className="mrp-num" style={{ fontWeight: 700 }}>{fmtQty(l.openQuantity)}</td>
                                case 'deliveryDate':
                                  return <td key={c.id}>{fmtDate(l.deliveryDate)}</td>
                                default:
                                  return <td key={c.id} />
                              }
                            })}
                          </tr>
                        )
                      })}
                    </tbody>
                  </table>
                </div>
              )
        )}

        {step === 2 && preview && (
          <>
            <div className="mrp-summary">
              <div className="mrp-summary-card">
                <div className="mrp-summary-val" style={{ color: 'var(--mrp-indigo)' }}>{preview.summary.newWorkOrderCount}</div>
                <div className="mrp-summary-lbl">Yeni İş Emri</div>
              </div>
              <div className="mrp-summary-card">
                <div className="mrp-summary-val" style={{ color: 'var(--mrp-blue)' }}>{preview.summary.mergeWorkOrderCount}</div>
                <div className="mrp-summary-lbl">Mevcut Emre Ekleme</div>
              </div>
              <div className="mrp-summary-card">
                <div className="mrp-summary-val" style={{ color: 'var(--mrp-emerald)' }}>{preview.summary.coveredByStockCount}</div>
                <div className="mrp-summary-lbl">Stoktan Karşılanan</div>
              </div>
              <div className="mrp-summary-card">
                <div className="mrp-summary-val" style={{ color: 'var(--mrp-amber)' }}>{preview.summary.purchaseRequestCount}</div>
                <div className="mrp-summary-lbl">Satın Alınacak</div>
              </div>
              <div className="mrp-summary-card">
                <div className="mrp-summary-val" style={{ color: 'var(--mrp-rose)' }}>{preview.summary.shortageCount}</div>
                <div className="mrp-summary-lbl">Aksiyon Alınamayan</div>
              </div>
            </div>

            {/* Satın alınacak malzeme varsa: tek bir Satın Alma Talebi belgesi önerilir.
                Kapatılırsa yalnız rapor kalır, belge yazılmaz. */}
            {preview.summary.purchaseRequestCount > 0 && (
              <label style={{
                display: 'flex', alignItems: 'flex-start', gap: 9, marginBottom: 12,
                padding: '10px 12px', borderRadius: 10, cursor: 'pointer',
                background: 'var(--mrp-surface)', border: '1px solid var(--mrp-border)',
              }}>
                <input type="checkbox" checked={createPr}
                       onChange={function (e) { setCreatePr(e.target.checked) }}
                       style={{ marginTop: 2, accentColor: 'var(--mrp-amber)' }} />
                <span style={{ fontSize: 12.5 }}>
                  <strong>Satın Alma Talebi oluştur</strong>
                  <span className="mrp-dim">
                    {' '}— eksik {preview.summary.purchaseRequestCount} malzeme için tek belge açılır.
                  </span>
                </span>
              </label>
            )}

            <div className="mrp-table-wrap">
              <table className="mrp-table">
                <thead>
                  <tr>
                    <th style={{ width: 34 }}></th>
                    <th>Malzeme</th>
                    <th>Aksiyon</th>
                    <th>Kırılım</th>
                    <th className="mrp-num">Brüt</th>
                    <th className="mrp-num">Eldeki</th>
                    <th className="mrp-num">Açık Arz</th>
                    <th className="mrp-num">Net</th>
                    <th>Başlangıç</th>
                    <th>Bitiş</th>
                    <th>Not</th>
                  </tr>
                </thead>
                <tbody>
                  {preview.nodes.map(function (n) {
                    var meta = ACTION_META[n.actionType] || { label: n.actionType, cls: 'mrp-badge--slate', Icon: AlertTriangle }
                    var Icon = meta.Icon
                    var isOpen = !!expanded[n.runLineId]
                    return [
                      <tr key={'n' + n.runLineId}>
                        <td>
                          {n.pegs && n.pegs.length > 0 && (
                            <button type="button" className="mrp-expand"
                                    title={isOpen ? 'Kaynak siparişleri gizle' : 'Kaynak siparişleri göster'}
                                    onClick={function () {
                                      var e2 = Object.assign({}, expanded)
                                      e2[n.runLineId] = !isOpen
                                      setExpanded(e2)
                                    }}>
                              {isOpen ? <ChevronDown size={12} /> : <ChevronRight size={12} />}
                            </button>
                          )}
                        </td>
                        {/* Seviye girintisi: 0 = mamul, 1+ = yarı mamul/hammadde (çok seviyeli
                            patlatma). Ağaç hiyerarşisi girintiyle okunur. */}
                        <td style={{ paddingLeft: 10 + (n.level || 0) * 18 }}>
                          <div style={{ fontWeight: 600 }}>
                            {(n.level || 0) > 0 && <span className="mrp-dim" style={{ marginRight: 4 }}>↳</span>}
                            {n.itemCode || ('#' + n.itemId)}
                          </div>
                          <div className="mrp-dim" style={{ fontSize: 11 }}>{n.itemName}</div>
                        </td>
                        <td>
                          <span className={'mrp-badge ' + meta.cls}><Icon size={11} /> {meta.label}</span>
                          {n.targetWorkOrderNumber && (
                            <div className="mrp-dim" style={{ fontSize: 11, marginTop: 3 }}>→ {n.targetWorkOrderNumber}</div>
                          )}
                        </td>
                        <td><span className="mrp-policy">{POLICY_LABEL[n.splitPolicy] || n.splitPolicy}</span></td>
                        <td className="mrp-num">{fmtQty(n.grossQuantity)}</td>
                        <td className="mrp-num mrp-dim">{fmtQty(n.onHandApplied)}</td>
                        <td className="mrp-num mrp-dim">{fmtQty(n.openSupplyApplied)}</td>
                        <td className="mrp-num" style={{ fontWeight: 700 }}>{fmtQty(n.netQuantity)}</td>
                        <td>{fmtDate(n.plannedStartDate)}</td>
                        <td>{fmtDate(n.plannedEndDate)}</td>
                        <td className="mrp-dim" style={{ maxWidth: 260, fontSize: 11.5 }}>{n.message || ''}</td>
                      </tr>,
                      isOpen && (
                        <tr key={'p' + n.runLineId} className="mrp-peg-row">
                          <td colSpan={11}>
                            <table className="mrp-peg-table">
                              <thead>
                                <tr>
                                  <th style={{ textAlign: 'left' }}>Kaynak Sipariş</th>
                                  <th style={{ textAlign: 'left' }}>Satır</th>
                                  <th style={{ textAlign: 'right' }}>Miktar</th>
                                </tr>
                              </thead>
                              <tbody>
                                {n.pegs.map(function (p, i) {
                                  return (
                                    <tr key={i}>
                                      <td>{p.rootDocumentNumber}</td>
                                      <td className="mrp-dim">#{p.rootLineId}</td>
                                      <td className="mrp-num">{fmtQty(p.quantity)}</td>
                                    </tr>
                                  )
                                })}
                              </tbody>
                            </table>
                          </td>
                        </tr>
                      ),
                    ]
                  })}
                </tbody>
              </table>
            </div>
          </>
        )}

        {step === 3 && applyResult && (
          <>
            <div className="mrp-alert mrp-alert--ok">
              <Check size={13} style={{ verticalAlign: '-2px' }} />{' '}
              {applyResult.created.length} yeni iş emri açıldı
              {applyResult.merged.length > 0 ? (', ' + applyResult.merged.length + ' mevcut emre eklendi') : ''}.
            </div>
            {applyResult.purchaseRequestDocumentId > 0 && (
              <div className="mrp-alert mrp-alert--warn">
                <ShoppingCart size={13} style={{ verticalAlign: '-2px' }} /> Eksik malzemeler için Satın Alma Talebi oluşturuldu:{' '}
                <a href={'/Purchase/Edit?type=purchase_demand&id=' + applyResult.purchaseRequestDocumentId}
                   style={{ color: 'var(--mrp-amber)', fontWeight: 700 }}>
                  Belge #{applyResult.purchaseRequestDocumentId}
                </a>
              </div>
            )}
            {applyResult.warnings && applyResult.warnings.length > 0 && (
              <div className="mrp-alert mrp-alert--warn">
                <AlertTriangle size={13} style={{ verticalAlign: '-2px' }} /> Uygulanamayan satırlar:
                {'\n' + applyResult.warnings.join('\n')}
              </div>
            )}
            <div className="mrp-table-wrap">
              <table className="mrp-table">
                <thead>
                  <tr><th>İş Emri</th><th>Malzeme</th><th className="mrp-num">Miktar</th><th>Tür</th></tr>
                </thead>
                <tbody>
                  {applyResult.created.concat(applyResult.merged).map(function (w, i) {
                    var isMerge = i >= applyResult.created.length
                    return (
                      <tr key={w.workOrderId + '-' + i}>
                        <td>
                          <a href={'/Production/WorkOrderEdit?id=' + w.workOrderId}
                             style={{ color: 'var(--mrp-indigo)', fontWeight: 600, textDecoration: 'none' }}>
                            #{w.workOrderId}
                          </a>
                        </td>
                        <td>{w.itemCode} <span className="mrp-dim">{w.itemName}</span></td>
                        <td className="mrp-num">{fmtQty(w.quantity)}</td>
                        <td>
                          <span className={'mrp-badge ' + (isMerge ? 'mrp-badge--blue' : 'mrp-badge--indigo')}>
                            {isMerge ? 'Eklendi' : 'Yeni'}
                          </span>
                        </td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>
          </>
        )}
      </div>

      {/* ── FOOTER ── */}
      <div className="mrp-footer">
        {/* Ana eylemler ŞERİTTE (C-Grid standardı). Burada yalnız bağlam metni ve
            ikincil eylemler kalır; aynı butonu iki yerde göstermek kafa karıştırırdı. */}
        {step === 1 && (
          <span className="mrp-footer-info">
            {selectedIds.length === 0 ? 'Planlanacak sipariş satırlarını seçin.' : (selectedIds.length + ' satır seçildi.')}
          </span>
        )}
        {step === 2 && (
          <>
            <span className="mrp-footer-info">
              {actionableCount === 0
                ? 'Bu planda oluşturulacak iş emri yok.'
                : (actionableCount + ' iş emri işlemi uygulanacak. Plan bir bütün olarak uygulanır.')}
            </span>
            <button type="button" className="mrp-btn mrp-btn--ghost" onClick={function () { setDiscardOpen(true) }} disabled={loading}>
              Vazgeç
            </button>
          </>
        )}
        {step === 3 && (
          <span className="mrp-footer-info">Yeni bir plan çalıştırmak için başa dönün.</span>
        )}
      </div>

      {/* ── Onay modalı (ekran ORTASINDA, native confirm YASAK) ── */}
      {confirmOpen && (
        <div className="mrp-modal-backdrop"
             onClick={function (e) { if (e.target === e.currentTarget) setConfirmOpen(false) }}>
          <div className="mrp-modal">
            <div className="mrp-modal-head">
              <Check size={17} style={{ color: 'var(--mrp-indigo)' }} />
              <strong>Planı Uygula</strong>
            </div>
            <div className="mrp-modal-body">
              {actionableCount} iş emri işlemi uygulanacak
              ({preview.summary.newWorkOrderCount} yeni, {preview.summary.mergeWorkOrderCount} mevcut emre ekleme).
              Bu işlem geri alınamaz. Devam edilsin mi?
            </div>
            <div className="mrp-modal-foot">
              <button type="button" className="mrp-btn" onClick={function () { setConfirmOpen(false) }}>Vazgeç</button>
              <button type="button" className="mrp-btn mrp-btn--primary" onClick={applyPlan} autoFocus>Uygula</button>
            </div>
          </div>
        </div>
      )}

      {/* ── Vazgeçme onayı (destrüktif: hesaplanan plan atılır) ── */}
      {discardOpen && (
        <div className="mrp-modal-backdrop"
             onClick={function (e) { if (e.target === e.currentTarget) setDiscardOpen(false) }}>
          <div className="mrp-modal">
            <div className="mrp-modal-head">
              <X size={17} style={{ color: 'var(--mrp-rose)' }} />
              <strong>Plandan Vazgeç</strong>
            </div>
            <div className="mrp-modal-body">
              Hesaplanan plan iptal edilecek ve sipariş seçimine dönülecek.
              Hiçbir iş emri açılmamış olacak. Devam edilsin mi?
            </div>
            <div className="mrp-modal-foot">
              <button type="button" className="mrp-btn" onClick={function () { setDiscardOpen(false) }}>Geri Dön</button>
              <button type="button" className="mrp-btn mrp-btn--danger" onClick={discardRun} autoFocus>Vazgeç</button>
            </div>
          </div>
        </div>
      )}

      {/* ── C-Grid paylaşılan panelleri ──────────────────────────────────────
          SmartBoard ile AYNI bileşenler: filtre mantığı ve sütun tercihi tek
          yerde yaşar, ekrana özel ikinci bir uygulama yazılmaz. */}
      <SmartBoardFilterPanel
        isOpen={filterOpen}
        onClose={function () { setFilterOpen(false) }}
        boardKey={LINE_BOARD_KEY}
        formCode="MRP_PLANNING"
        masterWidgets={LINE_COLUMNS}
        entities={lineEntities}
        filters={filters}
        onApply={function (next) { setFilters(Array.isArray(next) ? next : []) }}
      />
      <SmartBoardConfigPanel
        isOpen={columnsOpen}
        onClose={function () { setColumnsOpen(false) }}
        boardKey={LINE_BOARD_KEY}
        masterWidgets={LINE_COLUMNS}
        onSaved={function () { setLineCfg(loadWidgetConfig(LINE_BOARD_KEY)) }}
      />
    </div>
  )
}
