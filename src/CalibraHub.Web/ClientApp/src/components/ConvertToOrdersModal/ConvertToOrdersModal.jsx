/**
 * ConvertToOrdersModal — Onaylanmis teklifleri siparise donusturen modal.
 *
 * Akis:
 *  1) Acilis: GetConvertibleQuotes(filtre yok) -> tum uygun teklifleri yukler
 *  2) Filtreler: tarih araligi, cari, belge no -> yeniden yukler
 *  3) Liste: cari bazli grupli, her grupta checkbox'lar
 *  4) Footer'da ozet ve "Siparis Olustur" butonu
 *  5) Onay: ortalanmis custom confirm modal (CLAUDE.md kurali — browser confirm yok)
 *  6) POST /Sales/CreateOrdersFromQuotes -> success ise toast + opsiyonel navigate
 */
import { useState, useEffect, useMemo, useCallback, useRef } from 'react'
import { ShoppingCart, X, Search, Calendar, Loader2, Check, AlertTriangle } from 'lucide-react'
import { getConvertibleQuotes, createOrdersFromQuotes, getCustomers } from '../../services/salesService'

function todayIso() {
  var d = new Date()
  var y = d.getFullYear()
  var m = String(d.getMonth() + 1).padStart(2, '0')
  var day = String(d.getDate()).padStart(2, '0')
  return y + '-' + m + '-' + day
}

function formatTr(num) {
  if (num == null) return '0,00'
  try { return Number(num).toLocaleString('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) }
  catch (e) { return String(num) }
}

function formatDateTr(iso) {
  if (!iso) return ''
  try {
    var d = new Date(iso)
    if (isNaN(d.getTime())) return iso
    return d.toLocaleDateString('tr-TR')
  } catch (e) { return iso }
}

export default function ConvertToOrdersModal(props) {
  var onClose = props.onClose || function () {}
  var onSuccess = props.onSuccess || function () {}

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

  // ── Filtre state'leri ──
  var [fromDate, setFromDate] = useState('')
  var [toDate, setToDate] = useState('')
  var [contactId, setContactId] = useState('')
  var [contactSearch, setContactSearch] = useState('')
  var [search, setSearch] = useState('')

  // ── Veri state'leri ──
  var [quotes, setQuotes] = useState([])
  var [loading, setLoading] = useState(false)
  var [error, setError] = useState(null)
  var [contacts, setContacts] = useState([])
  var [contactsOpen, setContactsOpen] = useState(false)

  // ── Secim state'leri ──
  var [selectedIds, setSelectedIds] = useState({})  // { quoteId: true }
  var [orderDate, setOrderDate] = useState(todayIso())

  // ── Submit state ──
  var [confirmOpen, setConfirmOpen] = useState(false)
  var [submitting, setSubmitting] = useState(false)
  var [resultMsg, setResultMsg] = useState(null)

  // ── ESC ile kapat ──
  useEffect(function () {
    function onKey(e) {
      if (e.key === 'Escape') {
        if (confirmOpen) setConfirmOpen(false)
        else onClose()
      }
    }
    document.addEventListener('keydown', onKey)
    return function () { document.removeEventListener('keydown', onKey) }
  }, [confirmOpen, onClose])

  // ── Veri yukle ──
  var loadQuotes = useCallback(function () {
    setLoading(true)
    setError(null)
    getConvertibleQuotes({
      fromDate: fromDate || undefined,
      toDate: toDate || undefined,
      contactId: contactId ? Number(contactId) : undefined,
      search: search || undefined,
    })
      .then(function (data) {
        setQuotes(Array.isArray(data) ? data : [])
        setSelectedIds({})  // filtre degisince secimleri sifirla
      })
      .catch(function (e) {
        console.error('[ConvertModal] Veri yukleme hatasi:', e)
        setError('Veri yuklenemedi: ' + (e.message || e))
        setQuotes([])
      })
      .finally(function () { setLoading(false) })
  }, [fromDate, toDate, contactId, search])

  // Ilk yukleme + filtre degisince
  useEffect(function () {
    loadQuotes()
  }, [loadQuotes])

  // Cari listesi (ilk render)
  useEffect(function () {
    getCustomers()
      .then(function (data) {
        setContacts(Array.isArray(data) ? data : [])
      })
      .catch(function (e) {
        console.warn('[ConvertModal] Cari listesi yuklenemedi:', e)
        setContacts([])
      })
  }, [])

  // ── Cari arama filtre ──
  var filteredContacts = useMemo(function () {
    if (!contactSearch.trim()) return contacts.slice(0, 30)
    var q = contactSearch.toLowerCase()
    return contacts.filter(function (c) {
      return (c.accountCode && c.accountCode.toLowerCase().indexOf(q) !== -1) ||
             (c.accountTitle && c.accountTitle.toLowerCase().indexOf(q) !== -1)
    }).slice(0, 50)
  }, [contacts, contactSearch])

  // ── Cari bazinda grupla ──
  var groups = useMemo(function () {
    var map = new Map()
    quotes.forEach(function (q) {
      var key = q.contactId || 0
      if (!map.has(key)) {
        map.set(key, {
          contactId: q.contactId,
          contactName: q.contactName || '(musterisiz)',
          quotes: [],
          totalAmount: 0,
        })
      }
      var g = map.get(key)
      g.quotes.push(q)
      g.totalAmount += Number(q.grandTotal || 0)
    })
    return Array.from(map.values()).sort(function (a, b) {
      return (a.contactName || '').localeCompare(b.contactName || '', 'tr')
    })
  }, [quotes])

  // ── Secim helper'lari ──
  function toggleQuote(id) {
    setSelectedIds(function (prev) {
      var next = Object.assign({}, prev)
      if (next[id]) delete next[id]
      else next[id] = true
      return next
    })
  }

  function toggleGroup(group) {
    var allSelected = group.quotes.every(function (q) { return selectedIds[q.id] })
    setSelectedIds(function (prev) {
      var next = Object.assign({}, prev)
      group.quotes.forEach(function (q) {
        if (allSelected) delete next[q.id]
        else next[q.id] = true
      })
      return next
    })
  }

  function toggleAll() {
    var allSelected = quotes.every(function (q) { return selectedIds[q.id] })
    if (allSelected) {
      setSelectedIds({})
    } else {
      var next = {}
      quotes.forEach(function (q) { next[q.id] = true })
      setSelectedIds(next)
    }
  }

  // ── Ozet ──
  var summary = useMemo(function () {
    var ids = Object.keys(selectedIds).map(Number)
    var selectedQuotes = quotes.filter(function (q) { return selectedIds[q.id] })
    var byContact = {}
    selectedQuotes.forEach(function (q) {
      var k = q.contactId || 0
      byContact[k] = (byContact[k] || 0) + 1
    })
    var contactCount = Object.keys(byContact).length
    var totalAmount = selectedQuotes.reduce(function (a, q) { return a + Number(q.grandTotal || 0) }, 0)
    return { quoteCount: ids.length, contactCount, ordersToCreate: contactCount, totalAmount }
  }, [selectedIds, quotes])

  // ── Submit ──
  function handleSubmit() {
    if (summary.quoteCount === 0) return
    setConfirmOpen(true)
  }

  function doConfirm() {
    setSubmitting(true)
    setResultMsg(null)
    var ids = Object.keys(selectedIds).map(Number).filter(function (n) { return n > 0 })
    createOrdersFromQuotes({ quoteIds: ids, orderDate: orderDate })
      .then(function (resp) {
        if (resp && resp.success) {
          setResultMsg({
            type: 'success',
            text: resp.ordersCreated + ' siparis olusturuldu.',
          })
          setConfirmOpen(false)
          // Parent callback (toast + navigate)
          setTimeout(function () {
            onSuccess({ ordersCreated: resp.ordersCreated, orderIds: resp.orderIds || [] })
            onClose()
          }, 600)
        } else {
          setResultMsg({
            type: 'error',
            text: (resp && resp.error) || 'Olusturma basarisiz.',
          })
          setConfirmOpen(false)
        }
      })
      .catch(function (e) {
        console.error('[ConvertModal] Submit hatasi:', e)
        setResultMsg({ type: 'error', text: 'Sunucuya ulasilamadi: ' + (e.message || e) })
        setConfirmOpen(false)
      })
      .finally(function () { setSubmitting(false) })
  }

  // ── Stil paleti (tema'ya gore) ──
  var palette = isDark
    ? {
        backdrop: 'rgba(2,6,23,0.78)',
        modalBg: '#0f172a',
        modalBorder: 'rgba(255,255,255,0.08)',
        headerBg: 'rgba(255,255,255,0.03)',
        textPrimary: '#f1f5f9',
        textSecondary: '#94a3b8',
        textMuted: '#64748b',
        cardBg: 'rgba(255,255,255,0.04)',
        cardBorder: 'rgba(255,255,255,0.06)',
        cardHover: 'rgba(255,255,255,0.07)',
        inputBg: 'rgba(255,255,255,0.04)',
        inputBorder: 'rgba(255,255,255,0.08)',
        accentGreen: '#10b981',
        accentDanger: '#f43f5e',
        rowDivider: 'rgba(255,255,255,0.04)',
      }
    : {
        backdrop: 'rgba(15,23,42,0.45)',
        modalBg: '#ffffff',
        modalBorder: 'rgba(15,23,42,0.10)',
        headerBg: '#f8fafc',
        textPrimary: '#0f172a',
        textSecondary: '#475569',
        textMuted: '#94a3b8',
        cardBg: '#f8fafc',
        cardBorder: '#e2e8f0',
        cardHover: '#f1f5f9',
        inputBg: '#ffffff',
        inputBorder: '#cbd5e1',
        accentGreen: '#059669',
        accentDanger: '#e11d48',
        rowDivider: '#e2e8f0',
      }

  var allSelected = quotes.length > 0 && quotes.every(function (q) { return selectedIds[q.id] })

  return (
    <div
      onClick={function (e) { if (e.target === e.currentTarget) onClose() }}
      style={{
        position: 'fixed', inset: 0, zIndex: 9999,
        background: palette.backdrop,
        backdropFilter: 'blur(4px)',
        display: 'flex', alignItems: 'center', justifyContent: 'center',
        padding: '24px',
      }}
    >
      <div
        style={{
          background: palette.modalBg,
          border: '1px solid ' + palette.modalBorder,
          borderRadius: '16px',
          width: '100%', maxWidth: '960px',
          maxHeight: 'calc(100vh - 48px)',
          display: 'flex', flexDirection: 'column',
          boxShadow: '0 25px 80px rgba(0,0,0,0.45)',
          overflow: 'hidden',
          color: palette.textPrimary,
        }}
      >
        {/* ── HEADER ── */}
        <div style={{
          padding: '16px 20px', borderBottom: '1px solid ' + palette.cardBorder,
          background: palette.headerBg,
          display: 'flex', alignItems: 'center', gap: '12px',
        }}>
          <div style={{
            width: '36px', height: '36px',
            borderRadius: '10px',
            background: 'linear-gradient(135deg, rgba(16,185,129,0.18), rgba(16,185,129,0.06))',
            border: '1px solid rgba(16,185,129,0.30)',
            display: 'flex', alignItems: 'center', justifyContent: 'center',
            flexShrink: 0,
          }}>
            <ShoppingCart size={18} style={{ color: palette.accentGreen }} />
          </div>
          <div style={{ flex: 1, minWidth: 0 }}>
            <div style={{ fontSize: '15px', fontWeight: 700, letterSpacing: '-0.01em' }}>
              Tekliflerden Siparis Olustur
            </div>
            <div style={{ fontSize: '11.5px', color: palette.textSecondary, marginTop: '2px' }}>
              Onayli tekliflerden cari bazli siparis(ler) uretir. Ayni cari = tek siparis.
            </div>
          </div>
          <button
            type="button" onClick={onClose}
            style={{
              padding: '8px', borderRadius: '8px', cursor: 'pointer',
              background: 'transparent', border: '1px solid ' + palette.cardBorder,
              color: palette.textSecondary,
              display: 'flex', alignItems: 'center', justifyContent: 'center',
            }}
            title="Kapat (Esc)"
          >
            <X size={16} />
          </button>
        </div>

        {/* ── FILTER BAR ── */}
        <div style={{
          padding: '12px 20px',
          borderBottom: '1px solid ' + palette.cardBorder,
          display: 'grid',
          gridTemplateColumns: '1fr 1fr 1.4fr 1.4fr',
          gap: '10px',
          alignItems: 'end',
        }}>
          <FilterField label="Baslangic Tarihi" palette={palette}>
            <input type="date" value={fromDate} onChange={function (e) { setFromDate(e.target.value) }}
              style={inputStyle(palette)} />
          </FilterField>
          <FilterField label="Bitis Tarihi" palette={palette}>
            <input type="date" value={toDate} onChange={function (e) { setToDate(e.target.value) }}
              style={inputStyle(palette)} />
          </FilterField>
          <FilterField label="Cari" palette={palette}>
            <ContactPicker
              palette={palette}
              contacts={filteredContacts}
              search={contactSearch}
              setSearch={setContactSearch}
              isOpen={contactsOpen}
              setIsOpen={setContactsOpen}
              selectedId={contactId}
              onSelect={function (c) {
                setContactId(c ? c.id : '')
                setContactSearch(c ? c.accountTitle : '')
                setContactsOpen(false)
              }}
            />
          </FilterField>
          <FilterField label="Belge No" palette={palette}>
            <div style={{ position: 'relative' }}>
              <Search size={13} style={{
                position: 'absolute', left: '10px', top: '50%', transform: 'translateY(-50%)',
                color: palette.textMuted, pointerEvents: 'none',
              }} />
              <input
                type="text"
                value={search}
                onChange={function (e) { setSearch(e.target.value) }}
                placeholder="Belge no ara..."
                style={Object.assign({}, inputStyle(palette), { paddingLeft: '32px' })}
              />
            </div>
          </FilterField>
        </div>

        {/* ── LIST ── */}
        <div style={{ flex: 1, overflow: 'auto', padding: '12px 20px' }}>
          {loading ? (
            <div style={{
              display: 'flex', alignItems: 'center', justifyContent: 'center',
              padding: '60px 0', color: palette.textSecondary,
            }}>
              <Loader2 size={24} className="animate-spin" />
              <span style={{ marginLeft: '10px', fontSize: '13px' }}>Yukleniyor...</span>
            </div>
          ) : error ? (
            <div style={{
              padding: '14px', borderRadius: '10px',
              background: 'rgba(244,63,94,0.10)',
              border: '1px solid rgba(244,63,94,0.30)',
              color: palette.accentDanger,
              fontSize: '13px',
            }}>
              <AlertTriangle size={14} style={{ display: 'inline', marginRight: '6px' }} />
              {error}
            </div>
          ) : quotes.length === 0 ? (
            <div style={{
              textAlign: 'center', padding: '60px 20px',
              color: palette.textSecondary, fontSize: '13px',
            }}>
              <ShoppingCart size={32} style={{ color: palette.textMuted, marginBottom: '12px' }} />
              <div>Filtreye uyan, siparise donusturulebilir teklif yok.</div>
              <div style={{ fontSize: '11.5px', marginTop: '6px', color: palette.textMuted }}>
                Sadece onayli (Approved) ve daha once siparise donusturulmemis teklifler listelenir.
              </div>
            </div>
          ) : (
            <div>
              {/* Tum sec / temizle */}
              <div style={{
                display: 'flex', alignItems: 'center', gap: '8px',
                padding: '6px 4px', marginBottom: '8px',
              }}>
                <label style={{
                  display: 'inline-flex', alignItems: 'center', gap: '7px',
                  fontSize: '12px', cursor: 'pointer', color: palette.textSecondary,
                }}>
                  <input
                    type="checkbox"
                    checked={allSelected}
                    onChange={toggleAll}
                    style={{ width: '14px', height: '14px', cursor: 'pointer', accentColor: palette.accentGreen }}
                  />
                  <span>{allSelected ? 'Tum secimleri temizle' : 'Tumunu sec'} ({quotes.length} teklif)</span>
                </label>
              </div>

              {groups.map(function (group) {
                var groupAllSelected = group.quotes.every(function (q) { return selectedIds[q.id] })
                var groupSomeSelected = !groupAllSelected && group.quotes.some(function (q) { return selectedIds[q.id] })
                return (
                  <div
                    key={'g-' + (group.contactId || 0)}
                    style={{
                      background: palette.cardBg,
                      border: '1px solid ' + palette.cardBorder,
                      borderRadius: '12px',
                      marginBottom: '10px',
                      overflow: 'hidden',
                    }}
                  >
                    {/* Group header */}
                    <div
                      style={{
                        padding: '10px 14px',
                        background: groupAllSelected
                          ? (isDark ? 'rgba(16,185,129,0.10)' : 'rgba(16,185,129,0.06)')
                          : 'transparent',
                        borderBottom: '1px solid ' + palette.rowDivider,
                        display: 'flex', alignItems: 'center', gap: '10px',
                        cursor: 'pointer',
                      }}
                      onClick={function () { toggleGroup(group) }}
                    >
                      <input
                        type="checkbox"
                        checked={groupAllSelected}
                        ref={function (el) { if (el) el.indeterminate = groupSomeSelected }}
                        onChange={function () { toggleGroup(group) }}
                        onClick={function (e) { e.stopPropagation() }}
                        style={{ width: '15px', height: '15px', cursor: 'pointer', accentColor: palette.accentGreen }}
                      />
                      <div style={{ flex: 1, minWidth: 0 }}>
                        <div style={{ fontSize: '13.5px', fontWeight: 600 }}>
                          {group.contactName}
                        </div>
                        <div style={{ fontSize: '11.5px', color: palette.textSecondary, marginTop: '1px' }}>
                          {group.quotes.length} teklif &middot; ₺{formatTr(group.totalAmount)}
                        </div>
                      </div>
                    </div>

                    {/* Quote rows */}
                    <div>
                      {group.quotes.map(function (q) {
                        var checked = !!selectedIds[q.id]
                        return (
                          <label
                            key={q.id}
                            style={{
                              display: 'flex', alignItems: 'center', gap: '12px',
                              padding: '9px 14px 9px 38px',
                              borderBottom: '1px solid ' + palette.rowDivider,
                              cursor: 'pointer',
                              fontSize: '12.5px',
                              background: checked
                                ? (isDark ? 'rgba(16,185,129,0.06)' : 'rgba(16,185,129,0.04)')
                                : 'transparent',
                            }}
                          >
                            <input
                              type="checkbox"
                              checked={checked}
                              onChange={function () { toggleQuote(q.id) }}
                              style={{ width: '14px', height: '14px', cursor: 'pointer', accentColor: palette.accentGreen }}
                            />
                            <div style={{ flex: '0 0 130px', fontWeight: 600, fontFamily: 'monospace', fontSize: '11.5px' }}>
                              {q.documentNumber}
                            </div>
                            <div style={{ flex: '0 0 90px', color: palette.textSecondary }}>
                              {formatDateTr(q.documentDate)}
                            </div>
                            <div style={{ flex: '0 0 80px', color: palette.textSecondary, fontSize: '11.5px' }}>
                              {q.lineCount} kalem
                            </div>
                            <div style={{ flex: 1, textAlign: 'right', fontWeight: 600 }}>
                              {q.currency || 'TRY'} {formatTr(q.grandTotal)}
                            </div>
                          </label>
                        )
                      })}
                    </div>
                  </div>
                )
              })}
            </div>
          )}
        </div>

        {/* ── FOOTER ── */}
        <div style={{
          padding: '14px 20px',
          borderTop: '1px solid ' + palette.cardBorder,
          background: palette.headerBg,
          display: 'flex', alignItems: 'center', gap: '14px', flexWrap: 'wrap',
        }}>
          <div style={{ flex: 1, minWidth: '240px', fontSize: '12.5px', color: palette.textSecondary }}>
            {summary.quoteCount > 0 ? (
              <span>
                <strong style={{ color: palette.textPrimary }}>{summary.contactCount}</strong> cari icin{' '}
                <strong style={{ color: palette.accentGreen }}>{summary.ordersToCreate}</strong> siparis olusturulacak{' '}
                ({summary.quoteCount} teklif birlestiriliyor){summary.totalAmount > 0
                  ? ' — toplam ₺' + formatTr(summary.totalAmount)
                  : ''}
              </span>
            ) : (
              <span style={{ color: palette.textMuted }}>Henuz secim yok</span>
            )}
            {resultMsg && (
              <div style={{
                marginTop: '6px', fontSize: '12px',
                color: resultMsg.type === 'success' ? palette.accentGreen : palette.accentDanger,
              }}>
                {resultMsg.type === 'success' ? <Check size={12} style={{ display: 'inline', marginRight: '4px' }} /> : <AlertTriangle size={12} style={{ display: 'inline', marginRight: '4px' }} />}
                {resultMsg.text}
              </div>
            )}
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
            <label style={{ fontSize: '12px', color: palette.textSecondary }}>Siparis Tarihi</label>
            <input
              type="date" value={orderDate}
              onChange={function (e) { setOrderDate(e.target.value) }}
              style={Object.assign({}, inputStyle(palette), { width: '150px' })}
            />
          </div>
          <button
            type="button" onClick={onClose}
            style={{
              padding: '9px 16px', borderRadius: '9px', fontSize: '12.5px', fontWeight: 600,
              background: 'transparent', color: palette.textSecondary,
              border: '1px solid ' + palette.cardBorder, cursor: 'pointer',
            }}
          >
            Vazgec
          </button>
          <button
            type="button"
            onClick={handleSubmit}
            disabled={summary.quoteCount === 0 || submitting}
            style={{
              padding: '9px 18px', borderRadius: '9px', fontSize: '12.5px', fontWeight: 700,
              background: summary.quoteCount === 0 || submitting
                ? (isDark ? 'rgba(255,255,255,0.06)' : '#e2e8f0')
                : palette.accentGreen,
              color: summary.quoteCount === 0 || submitting
                ? palette.textMuted
                : '#ffffff',
              border: 'none',
              cursor: summary.quoteCount === 0 || submitting ? 'not-allowed' : 'pointer',
              display: 'inline-flex', alignItems: 'center', gap: '7px',
            }}
          >
            {submitting && <Loader2 size={13} className="animate-spin" />}
            <ShoppingCart size={13} />
            <span>Siparis Olustur</span>
          </button>
        </div>
      </div>

      {/* ── CONFIRM MODAL ── */}
      {confirmOpen && (
        <ConfirmDialog
          palette={palette}
          isDark={isDark}
          summary={summary}
          orderDate={orderDate}
          onCancel={function () { setConfirmOpen(false) }}
          onConfirm={doConfirm}
          submitting={submitting}
        />
      )}
    </div>
  )
}

// ── Yardimci component'ler ─────────────────────────

function FilterField(props) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
      <label style={{ fontSize: '11px', color: props.palette.textSecondary, fontWeight: 500 }}>
        {props.label}
      </label>
      {props.children}
    </div>
  )
}

function inputStyle(palette) {
  return {
    width: '100%',
    padding: '7px 10px',
    fontSize: '12.5px',
    background: palette.inputBg,
    border: '1px solid ' + palette.inputBorder,
    borderRadius: '8px',
    color: palette.textPrimary,
    outline: 'none',
  }
}

function ContactPicker(props) {
  var ref = useRef(null)
  useEffect(function () {
    function onDocClick(e) {
      if (ref.current && !ref.current.contains(e.target)) {
        props.setIsOpen(false)
      }
    }
    document.addEventListener('mousedown', onDocClick)
    return function () { document.removeEventListener('mousedown', onDocClick) }
  }, [props.setIsOpen])

  return (
    <div ref={ref} style={{ position: 'relative' }}>
      <input
        type="text"
        value={props.search}
        onChange={function (e) { props.setSearch(e.target.value); props.setIsOpen(true) }}
        onFocus={function () { props.setIsOpen(true) }}
        placeholder="Cari ara veya bos birak..."
        style={inputStyle(props.palette)}
      />
      {props.selectedId && (
        <button
          type="button"
          onClick={function () { props.onSelect(null) }}
          style={{
            position: 'absolute', right: '8px', top: '50%', transform: 'translateY(-50%)',
            background: 'transparent', border: 'none', cursor: 'pointer',
            color: props.palette.textMuted, padding: '2px',
          }}
          title="Temizle"
        >
          <X size={12} />
        </button>
      )}
      {props.isOpen && props.contacts.length > 0 && (
        <div style={{
          position: 'absolute', top: 'calc(100% + 4px)', left: 0, right: 0,
          background: props.palette.modalBg,
          border: '1px solid ' + props.palette.cardBorder,
          borderRadius: '8px',
          maxHeight: '220px', overflowY: 'auto',
          boxShadow: '0 12px 30px rgba(0,0,0,0.30)',
          zIndex: 10,
        }}>
          {props.contacts.map(function (c) {
            return (
              <div
                key={c.id}
                onClick={function () { props.onSelect(c) }}
                style={{
                  padding: '8px 10px', fontSize: '12px',
                  cursor: 'pointer',
                  borderBottom: '1px solid ' + props.palette.rowDivider,
                  color: props.palette.textPrimary,
                }}
                onMouseEnter={function (e) { e.currentTarget.style.background = props.palette.cardHover }}
                onMouseLeave={function (e) { e.currentTarget.style.background = 'transparent' }}
              >
                <div style={{ fontWeight: 600 }}>{c.accountTitle || '(isimsiz)'}</div>
                <div style={{ fontSize: '11px', color: props.palette.textSecondary, fontFamily: 'monospace' }}>
                  {c.accountCode}
                </div>
              </div>
            )
          })}
        </div>
      )}
    </div>
  )
}

function ConfirmDialog(props) {
  var palette = props.palette
  return (
    <div
      onClick={function (e) { if (e.target === e.currentTarget) props.onCancel() }}
      style={{
        position: 'fixed', inset: 0, zIndex: 10000,
        background: 'rgba(2,6,23,0.65)',
        backdropFilter: 'blur(6px)',
        display: 'flex', alignItems: 'center', justifyContent: 'center',
        padding: '20px',
      }}
    >
      <div
        style={{
          background: palette.modalBg,
          border: '1px solid ' + palette.cardBorder,
          borderRadius: '14px',
          width: '100%', maxWidth: '420px',
          padding: '22px',
          color: palette.textPrimary,
          boxShadow: '0 25px 60px rgba(0,0,0,0.50)',
        }}
      >
        <div style={{
          width: '48px', height: '48px',
          borderRadius: '12px',
          background: 'linear-gradient(135deg, rgba(16,185,129,0.20), rgba(16,185,129,0.06))',
          border: '1px solid rgba(16,185,129,0.35)',
          display: 'flex', alignItems: 'center', justifyContent: 'center',
          marginBottom: '14px',
        }}>
          <ShoppingCart size={22} style={{ color: palette.accentGreen }} />
        </div>
        <div style={{ fontSize: '16px', fontWeight: 700, marginBottom: '6px' }}>
          Siparis(ler) olusturulsun mu?
        </div>
        <div style={{ fontSize: '12.5px', color: palette.textSecondary, lineHeight: '1.55', marginBottom: '14px' }}>
          <strong style={{ color: palette.textPrimary }}>{props.summary.contactCount}</strong> cari icin{' '}
          <strong style={{ color: palette.accentGreen }}>{props.summary.ordersToCreate}</strong> siparis uretilecek
          ({props.summary.quoteCount} teklif birlestiriliyor).
          <br />
          Siparis tarihi: <strong style={{ color: palette.textPrimary }}>{formatDateTr(props.orderDate)}</strong>.
          <br /><br />
          Kaynak teklif(ler)in durumu <strong style={{ color: palette.textPrimary }}>Converted</strong>'a
          gecirilir ve aynı teklif tekrar siparise donusturulemez.
        </div>
        <div style={{ display: 'flex', gap: '8px', justifyContent: 'flex-end' }}>
          <button
            type="button" onClick={props.onCancel}
            disabled={props.submitting}
            style={{
              padding: '8px 14px', borderRadius: '8px', fontSize: '12.5px', fontWeight: 600,
              background: 'transparent', color: palette.textSecondary,
              border: '1px solid ' + palette.cardBorder, cursor: 'pointer',
            }}
          >
            Vazgec
          </button>
          <button
            type="button"
            onClick={props.onConfirm}
            disabled={props.submitting}
            autoFocus
            style={{
              padding: '8px 16px', borderRadius: '8px', fontSize: '12.5px', fontWeight: 700,
              background: palette.accentGreen, color: '#ffffff', border: 'none',
              cursor: props.submitting ? 'wait' : 'pointer',
              display: 'inline-flex', alignItems: 'center', gap: '6px',
            }}
          >
            {props.submitting && <Loader2 size={13} className="animate-spin" />}
            <span>Evet, olustur</span>
          </button>
        </div>
      </div>
    </div>
  )
}
