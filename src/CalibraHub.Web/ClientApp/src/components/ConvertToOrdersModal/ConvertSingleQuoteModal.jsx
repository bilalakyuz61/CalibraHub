/**
 * ConvertSingleQuoteModal — Tek teklifi siparise donusturen kart-bazli modal.
 *
 * Acilis: SmartCard'taki "Siparise Donustur" extraAction (trigger).
 * Akis:
 *  1) Teklif info gosterimi (no, cari, tarih, tutar, satir sayisi)
 *  2) Siparis tarihi (default bugun)
 *  3) "Is emri de acilsin" opsiyonu (her satir icin yeni WorkOrder)
 *  4) Onay (custom confirm — browser confirm yok, CLAUDE.md kurali)
 *  5) POST /Sales/ConvertSingleQuoteToOrder
 *  6) Success → onSuccess({orderId, workOrderIds})
 */
import { useState, useEffect } from 'react'
import { ShoppingCart, X, Check, Loader2, AlertTriangle, Factory } from 'lucide-react'

function todayIso() {
  var d = new Date()
  return d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-' + String(d.getDate()).padStart(2, '0')
}

function formatTr(num) {
  if (num == null) return '0,00'
  try { return Number(num).toLocaleString('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) }
  catch (e) { return String(num) }
}

export default function ConvertSingleQuoteModal(props) {
  var quoteId       = props.quoteId
  var quoteNumber   = props.quoteNumber || ''
  var contactName   = props.contactName || '(musterisiz)'
  var grandTotal    = props.grandTotal != null ? Number(props.grandTotal) : 0
  var currency      = props.currency || 'TRY'
  var lineCount     = props.lineCount != null ? Number(props.lineCount) : null
  var onClose       = props.onClose || function () {}
  var onSuccess     = props.onSuccess || function () {}

  // Theme
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
    var obs = new MutationObserver(sync)
    obs.observe(document.body, { attributes: true, attributeFilter: ['class'] })
    obs.observe(document.documentElement, { attributes: true, attributeFilter: ['class'] })
    return function () { obs.disconnect() }
  }, [])

  var [orderDate, setOrderDate]   = useState(todayIso())
  var [createWO, setCreateWO]     = useState(false)
  var [confirmOpen, setConfirmOpen] = useState(false)
  var [submitting, setSubmitting]   = useState(false)
  var [resultMsg, setResultMsg]     = useState(null)

  useEffect(function () {
    function onKey(e) {
      if (e.key !== 'Escape') return
      if (confirmOpen) setConfirmOpen(false)
      else onClose()
    }
    document.addEventListener('keydown', onKey)
    return function () { document.removeEventListener('keydown', onKey) }
  }, [confirmOpen, onClose])

  function handleSubmit() {
    if (!quoteId) return
    setConfirmOpen(true)
  }

  function doConfirm() {
    setSubmitting(true)
    setResultMsg(null)
    fetch('/Sales/ConvertSingleQuoteToOrder', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'same-origin',
      body: JSON.stringify({
        quoteId: Number(quoteId),
        orderDate: orderDate,
        createWorkOrders: !!createWO,
      }),
    })
      .then(function (r) { return r.json() })
      .then(function (resp) {
        if (resp && resp.success) {
          var woCount = (resp.workOrderIds && resp.workOrderIds.length) || 0
          setResultMsg({
            type: 'success',
            text: 'Siparis olusturuldu (#' + resp.orderId + ')' +
                  (woCount > 0 ? ' — ' + woCount + ' is emri acildi.' : '.'),
          })
          setConfirmOpen(false)
          setTimeout(function () {
            onSuccess({ orderId: resp.orderId, workOrderIds: resp.workOrderIds || [] })
            onClose()
          }, 700)
        } else {
          setResultMsg({ type: 'error', text: (resp && resp.error) || 'Donusturme basarisiz.' })
          setConfirmOpen(false)
        }
      })
      .catch(function (e) {
        setResultMsg({ type: 'error', text: 'Sunucuya ulasilamadi: ' + (e.message || e) })
        setConfirmOpen(false)
      })
      .finally(function () { setSubmitting(false) })
  }

  var palette = isDark
    ? {
        backdrop: 'rgba(2,6,23,0.78)', modalBg: '#0f172a',
        modalBorder: 'rgba(255,255,255,0.08)', headerBg: 'rgba(255,255,255,0.03)',
        textPrimary: '#f1f5f9', textSecondary: '#94a3b8', textMuted: '#64748b',
        cardBg: 'rgba(255,255,255,0.04)', cardBorder: 'rgba(255,255,255,0.06)',
        accentGreen: '#10b981', accentDanger: '#f43f5e', accentAmber: '#f59e0b',
      }
    : {
        backdrop: 'rgba(15,23,42,0.45)', modalBg: '#ffffff',
        modalBorder: 'rgba(15,23,42,0.10)', headerBg: '#f8fafc',
        textPrimary: '#0f172a', textSecondary: '#475569', textMuted: '#94a3b8',
        cardBg: '#f8fafc', cardBorder: '#e2e8f0',
        accentGreen: '#059669', accentDanger: '#e11d48', accentAmber: '#d97706',
      }

  return (
    <div
      onClick={function (e) { if (e.target === e.currentTarget) onClose() }}
      style={{
        position: 'fixed', inset: 0, zIndex: 9999,
        background: palette.backdrop, backdropFilter: 'blur(4px)',
        display: 'flex', alignItems: 'center', justifyContent: 'center', padding: '24px',
      }}
    >
      <div
        style={{
          background: palette.modalBg, border: '1px solid ' + palette.modalBorder,
          borderRadius: '16px', width: '100%', maxWidth: '460px',
          display: 'flex', flexDirection: 'column',
          boxShadow: '0 25px 80px rgba(0,0,0,0.45)', overflow: 'hidden',
          color: palette.textPrimary,
        }}
      >
        {/* HEADER */}
        <div style={{
          padding: '14px 18px', borderBottom: '1px solid ' + palette.cardBorder,
          background: palette.headerBg,
          display: 'flex', alignItems: 'center', gap: '12px',
        }}>
          <div style={{
            width: '34px', height: '34px', borderRadius: '10px',
            background: 'linear-gradient(135deg, rgba(16,185,129,0.18), rgba(16,185,129,0.06))',
            border: '1px solid rgba(16,185,129,0.30)',
            display: 'flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0,
          }}>
            <ShoppingCart size={17} style={{ color: palette.accentGreen }} />
          </div>
          <div style={{ flex: 1, minWidth: 0 }}>
            <div style={{ fontSize: '14px', fontWeight: 700 }}>Siparise Donustur</div>
            <div style={{ fontSize: '11px', color: palette.textSecondary, marginTop: '1px' }}>
              Bu teklif tek bir siparise donusturulecek
            </div>
          </div>
          <button
            type="button" onClick={onClose}
            style={{
              padding: '7px', borderRadius: '8px', cursor: 'pointer',
              background: 'transparent', border: '1px solid ' + palette.cardBorder,
              color: palette.textSecondary,
              display: 'flex', alignItems: 'center', justifyContent: 'center',
            }}
            title="Kapat (Esc)"
          >
            <X size={15} />
          </button>
        </div>

        {/* BODY */}
        <div style={{ padding: '16px 18px', display: 'flex', flexDirection: 'column', gap: '14px' }}>
          {/* Teklif ozeti kart */}
          <div style={{
            background: palette.cardBg, border: '1px solid ' + palette.cardBorder,
            borderRadius: '10px', padding: '11px 13px', fontSize: '12.5px',
          }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '5px' }}>
              <span style={{ color: palette.textSecondary }}>Teklif No</span>
              <span style={{ fontFamily: 'monospace', fontWeight: 700 }}>{quoteNumber || '#' + quoteId}</span>
            </div>
            <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '5px' }}>
              <span style={{ color: palette.textSecondary }}>Cari</span>
              <span style={{ fontWeight: 600, textAlign: 'right', maxWidth: '60%' }}>{contactName}</span>
            </div>
            {lineCount != null && (
              <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '5px' }}>
                <span style={{ color: palette.textSecondary }}>Kalem Sayisi</span>
                <span>{lineCount}</span>
              </div>
            )}
            <div style={{ display: 'flex', justifyContent: 'space-between' }}>
              <span style={{ color: palette.textSecondary }}>Toplam</span>
              <span style={{ fontWeight: 700 }}>{currency} {formatTr(grandTotal)}</span>
            </div>
          </div>

          {/* Siparis tarihi */}
          <div>
            <label style={{
              display: 'block', fontSize: '11.5px', color: palette.textSecondary,
              fontWeight: 600, marginBottom: '4px',
            }}>Siparis Tarihi</label>
            <input
              type="date" value={orderDate}
              onChange={function (e) { setOrderDate(e.target.value) }}
              style={{
                width: '100%', padding: '8px 10px', fontSize: '13px',
                background: palette.cardBg, color: palette.textPrimary,
                border: '1px solid ' + palette.cardBorder, borderRadius: '8px', outline: 'none',
              }}
            />
          </div>

          {/* Is emri opsiyonu */}
          <label style={{
            display: 'flex', alignItems: 'flex-start', gap: '10px',
            background: palette.cardBg, border: '1px solid ' + palette.cardBorder,
            borderRadius: '10px', padding: '11px 13px', cursor: 'pointer',
          }}>
            <input
              type="checkbox" checked={createWO}
              onChange={function (e) { setCreateWO(e.target.checked) }}
              style={{ width: '15px', height: '15px', cursor: 'pointer', accentColor: palette.accentAmber, marginTop: '2px' }}
            />
            <div style={{ flex: 1 }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: '6px', fontSize: '12.5px', fontWeight: 600 }}>
                <Factory size={13} style={{ color: palette.accentAmber }} />
                <span>Is emri de acilsin</span>
              </div>
              <div style={{ fontSize: '11px', color: palette.textSecondary, marginTop: '2px', lineHeight: 1.4 }}>
                Olusturulan siparişin her satiri icin ayri bir uretim is emri (WorkOrder) acilir.
              </div>
            </div>
          </label>

          {resultMsg && (
            <div style={{
              padding: '9px 12px', borderRadius: '8px', fontSize: '12px',
              background: resultMsg.type === 'success'
                ? (isDark ? 'rgba(16,185,129,0.10)' : 'rgba(16,185,129,0.08)')
                : 'rgba(244,63,94,0.10)',
              border: '1px solid ' + (resultMsg.type === 'success'
                ? 'rgba(16,185,129,0.35)' : 'rgba(244,63,94,0.30)'),
              color: resultMsg.type === 'success' ? palette.accentGreen : palette.accentDanger,
              display: 'flex', alignItems: 'center', gap: '7px',
            }}>
              {resultMsg.type === 'success' ? <Check size={13} /> : <AlertTriangle size={13} />}
              <span>{resultMsg.text}</span>
            </div>
          )}
        </div>

        {/* FOOTER */}
        <div style={{
          padding: '12px 18px', borderTop: '1px solid ' + palette.cardBorder,
          background: palette.headerBg,
          display: 'flex', justifyContent: 'flex-end', gap: '8px',
        }}>
          <button
            type="button" onClick={onClose}
            style={{
              padding: '8px 14px', borderRadius: '8px', fontSize: '12.5px', fontWeight: 600,
              background: 'transparent', color: palette.textSecondary,
              border: '1px solid ' + palette.cardBorder, cursor: 'pointer',
            }}
          >Vazgec</button>
          <button
            type="button" onClick={handleSubmit}
            disabled={submitting}
            style={{
              padding: '8px 16px', borderRadius: '8px', fontSize: '12.5px', fontWeight: 700,
              background: palette.accentGreen, color: '#ffffff', border: 'none',
              cursor: submitting ? 'wait' : 'pointer',
              display: 'inline-flex', alignItems: 'center', gap: '6px',
            }}
          >
            {submitting && <Loader2 size={13} className="animate-spin" />}
            <ShoppingCart size={13} />
            <span>Siparise Donustur</span>
          </button>
        </div>
      </div>

      {/* CONFIRM */}
      {confirmOpen && (
        <div
          onClick={function (e) { if (e.target === e.currentTarget) setConfirmOpen(false) }}
          style={{
            position: 'fixed', inset: 0, zIndex: 10000,
            background: 'rgba(2,6,23,0.65)', backdropFilter: 'blur(6px)',
            display: 'flex', alignItems: 'center', justifyContent: 'center', padding: '20px',
          }}
        >
          <div style={{
            background: palette.modalBg, border: '1px solid ' + palette.cardBorder,
            borderRadius: '14px', width: '100%', maxWidth: '380px', padding: '20px',
            color: palette.textPrimary, boxShadow: '0 25px 60px rgba(0,0,0,0.50)',
          }}>
            <div style={{
              width: '44px', height: '44px', borderRadius: '12px',
              background: 'linear-gradient(135deg, rgba(16,185,129,0.20), rgba(16,185,129,0.06))',
              border: '1px solid rgba(16,185,129,0.35)',
              display: 'flex', alignItems: 'center', justifyContent: 'center', marginBottom: '12px',
            }}>
              <ShoppingCart size={20} style={{ color: palette.accentGreen }} />
            </div>
            <div style={{ fontSize: '15px', fontWeight: 700, marginBottom: '5px' }}>Siparis olusturulsun mu?</div>
            <div style={{ fontSize: '12px', color: palette.textSecondary, lineHeight: 1.5, marginBottom: '14px' }}>
              <strong style={{ color: palette.textPrimary }}>{quoteNumber || '#' + quoteId}</strong> teklifi
              <strong style={{ color: palette.accentGreen }}> Converted</strong> durumuna gecirilir ve yeni bir
              siparis olusur.{createWO ? <span> Her satir icin <strong style={{ color: palette.accentAmber }}>is emri</strong> de acilir.</span> : null}
            </div>
            <div style={{ display: 'flex', gap: '7px', justifyContent: 'flex-end' }}>
              <button
                type="button" onClick={function () { setConfirmOpen(false) }}
                disabled={submitting}
                style={{
                  padding: '7px 13px', borderRadius: '8px', fontSize: '12px', fontWeight: 600,
                  background: 'transparent', color: palette.textSecondary,
                  border: '1px solid ' + palette.cardBorder, cursor: 'pointer',
                }}
              >Vazgec</button>
              <button
                type="button" onClick={doConfirm} disabled={submitting} autoFocus
                style={{
                  padding: '7px 14px', borderRadius: '8px', fontSize: '12px', fontWeight: 700,
                  background: palette.accentGreen, color: '#ffffff', border: 'none',
                  cursor: submitting ? 'wait' : 'pointer',
                  display: 'inline-flex', alignItems: 'center', gap: '6px',
                }}
              >
                {submitting && <Loader2 size={12} className="animate-spin" />}
                <span>Evet, donustur</span>
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
