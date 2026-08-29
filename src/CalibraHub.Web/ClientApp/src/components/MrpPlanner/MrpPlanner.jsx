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
 * SmartBoard değil, bespoke liste (RoutingTree.jsx istisnası): çok seviyeli plan +
 * onay kapısı SmartBoard'un kart/entity modeline sığmıyor. Header düzeni C-Grid
 * standardını izler: ikon + başlık/alt başlık → arama → aksiyonlar.
 */
import { useState, useEffect, useCallback, useMemo } from 'react'
import {
  Workflow, Search, RefreshCw, ChevronRight, ChevronDown, Check, X,
  AlertTriangle, Loader2, PackageCheck, PlusCircle, GitMerge, ShoppingCart, Download,
} from 'lucide-react'
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

function csrfToken() {
  var el = document.querySelector('input[name="__RequestVerificationToken"]')
  return el ? el.value : ''
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

  var visibleLines = useMemo(function () {
    if (!search.trim()) return lines
    var q = search.trim().toLocaleLowerCase('tr')
    return lines.filter(function (l) {
      return (l.itemCode || '').toLocaleLowerCase('tr').indexOf(q) >= 0
          || (l.itemName || '').toLocaleLowerCase('tr').indexOf(q) >= 0
          || (l.documentNumber || '').toLocaleLowerCase('tr').indexOf(q) >= 0
          || (l.contactName || '').toLocaleLowerCase('tr').indexOf(q) >= 0
    })
  }, [lines, search])

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
      fileName: 'mrp-plani_' + stamp + '.xlsx',
      sheetName: 'MRP Plani',
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

  function restart() {
    setPreview(null); setApplyResult(null); setSelected({}); setStep(1); setError(null)
    loadLines(search)
  }

  var actionableCount = preview
    ? (preview.summary.newWorkOrderCount + preview.summary.mergeWorkOrderCount)
    : 0

  return (
    <div className="mrp-root">
      {/* ── HEADER ── */}
      <div className="mrp-header">
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

        {step === 1 && (
          <>
            <div className="mrp-search">
              <Search size={13} className="mrp-dim" />
              <input
                type="text" value={search} placeholder="Malzeme, belge no veya cari ara…"
                onChange={function (e) { setSearch(e.target.value) }}
              />
            </div>
            <button type="button" className="mrp-btn" onClick={function () { loadLines(search) }} disabled={loading}>
              <RefreshCw size={13} /> Yenile
            </button>
          </>
        )}

        {step === 2 && (
          <button type="button" className="mrp-btn" style={{ marginLeft: 'auto' }}
                  onClick={exportExcel} title="Planı Excel olarak indir">
            <Download size={13} /> Excel
          </button>
        )}
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
                        <th>Belge</th>
                        <th>Cari</th>
                        <th>Malzeme</th>
                        <th>Kırılım</th>
                        <th className="mrp-num">Sipariş</th>
                        <th className="mrp-num">Teslim</th>
                        <th className="mrp-num">Rezerve</th>
                        <th className="mrp-num">İş Emrinde</th>
                        <th className="mrp-num">Açık</th>
                        <th>Teslim Tarihi</th>
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
                            <td style={{ fontWeight: 600 }}>{l.documentNumber}</td>
                            <td className="mrp-dim">{l.contactName || '—'}</td>
                            <td>
                              <div style={{ fontWeight: 600 }}>{l.itemCode}</div>
                              <div className="mrp-dim" style={{ fontSize: 11 }}>{l.itemName}</div>
                              {!l.isProducible && (
                                <span className="mrp-badge mrp-badge--rose" style={{ marginTop: 3 }}>
                                  Üretilebilir değil
                                </span>
                              )}
                            </td>
                            <td><span className="mrp-policy">{POLICY_LABEL[l.splitPolicy] || l.splitPolicy}</span></td>
                            <td className="mrp-num">{fmtQty(l.orderQuantity)} {l.unitCode || ''}</td>
                            <td className="mrp-num mrp-dim">{fmtQty(l.deliveredQuantity)}</td>
                            <td className="mrp-num mrp-dim">{fmtQty(l.reservedQuantity)}</td>
                            <td className="mrp-num mrp-dim">{fmtQty(l.allocatedQuantity)}</td>
                            <td className="mrp-num" style={{ fontWeight: 700 }}>{fmtQty(l.openQuantity)}</td>
                            <td>{fmtDate(l.deliveryDate)}</td>
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
        {step === 1 && (
          <>
            <span className="mrp-footer-info">
              {selectedIds.length === 0 ? 'Planlanacak sipariş satırlarını seçin.' : (selectedIds.length + ' satır seçildi.')}
            </span>
            <button type="button" className="mrp-btn mrp-btn--primary"
                    onClick={runPreview} disabled={loading || selectedIds.length === 0}>
              {loading ? <Loader2 size={13} /> : <ChevronRight size={13} />} Planı Hesapla
            </button>
          </>
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
            <button type="button" className="mrp-btn mrp-btn--primary"
                    onClick={function () { setConfirmOpen(true) }} disabled={loading || actionableCount === 0}>
              {loading ? <Loader2 size={13} /> : <Check size={13} />} Onayla ve Oluştur
            </button>
          </>
        )}
        {step === 3 && (
          <>
            <span className="mrp-footer-info">Yeni bir plan çalıştırmak için başa dönün.</span>
            <button type="button" className="mrp-btn mrp-btn--primary" onClick={restart}>
              <RefreshCw size={13} /> Yeni Plan
            </button>
          </>
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
    </div>
  )
}
