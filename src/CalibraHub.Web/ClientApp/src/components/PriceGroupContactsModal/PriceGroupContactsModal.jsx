/**
 * PriceGroupContactsModal — Fiyat grubu ile cari kartlari eslestiren modal.
 *
 * Acilis: SmartCard'taki "Cari Eslestir" extraAction (trigger = "price-group-contacts-modal").
 * Akis:
 *   1) Mevcut atanmis cariler listesi (GET /PriceList/GetGroupContacts)
 *   2) Cari arama (GET /PriceList/SearchContactsForGroup)
 *   3) Atama: POST /PriceList/SetContactPriceGroup
 *      - Cari baska bir gruba bagli ise CLAUDE.md "Silme onay standardi" tarzi
 *        ekran ortasi onay modali ile guncelleme onayi alinir.
 *   4) Kaldirma: priceGroupId=null ile PUT/POST.
 */
import { useState, useEffect, useRef, useCallback } from 'react'
import { Users, X, Search, Plus, Loader2, AlertTriangle, Check, Trash2 } from 'lucide-react'

export default function PriceGroupContactsModal(props) {
  var groupId   = props.groupId
  var groupCode = props.groupCode || ''
  var groupName = props.groupName || ''
  var onClose   = props.onClose   || function () {}

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

  // ── Atanmis cariler ──
  var [assigned, setAssigned] = useState([])
  var [loadingAssigned, setLoadingAssigned] = useState(false)

  // ── Arama ──
  var [search, setSearch] = useState('')
  var [searchResults, setSearchResults] = useState([])
  var [searchLoading, setSearchLoading] = useState(false)
  var searchTimerRef = useRef(null)

  // ── Onay modali (cari baska gruba bagli) ──
  var [confirm, setConfirm] = useState(null) // { contact, oldGroupCode, oldGroupName }

  // ── Kaldirma onay modali ──
  var [unassignConfirm, setUnassignConfirm] = useState(null) // contact

  // ── Submit state ──
  var [busy, setBusy] = useState(false)

  // ── ESC ile kapat ──
  useEffect(function () {
    function onKey(e) {
      if (e.key !== 'Escape') return
      if (confirm) { setConfirm(null); return }
      if (unassignConfirm) { setUnassignConfirm(null); return }
      onClose()
    }
    document.addEventListener('keydown', onKey)
    return function () { document.removeEventListener('keydown', onKey) }
  }, [confirm, unassignConfirm, onClose])

  // ── Atanmis cari listesini yukle ──
  var loadAssigned = useCallback(function () {
    if (!groupId) return
    setLoadingAssigned(true)
    fetch('/PriceList/GetGroupContacts?groupId=' + groupId, { credentials: 'same-origin' })
      .then(function (r) { return r.json() })
      .then(function (data) {
        setAssigned(Array.isArray(data) ? data : [])
      })
      .catch(function (err) {
        console.error('[PG Contacts] Atanmis cariler alinamadi:', err)
        setAssigned([])
      })
      .finally(function () { setLoadingAssigned(false) })
  }, [groupId])

  useEffect(function () { loadAssigned() }, [loadAssigned])

  // ── Arama (debounce 300ms) ──
  useEffect(function () {
    if (searchTimerRef.current) clearTimeout(searchTimerRef.current)
    var q = (search || '').trim()
    if (!q) {
      setSearchResults([])
      return undefined
    }
    setSearchLoading(true)
    searchTimerRef.current = setTimeout(function () {
      fetch('/PriceList/SearchContactsForGroup?q=' + encodeURIComponent(q) +
            '&excludeGroupId=' + (groupId || 0) + '&pageSize=50',
        { credentials: 'same-origin' })
        .then(function (r) { return r.json() })
        .then(function (data) { setSearchResults(Array.isArray(data) ? data : []) })
        .catch(function (err) {
          console.error('[PG Contacts] Arama basarisiz:', err)
          setSearchResults([])
        })
        .finally(function () { setSearchLoading(false) })
    }, 300)
    return function () { if (searchTimerRef.current) clearTimeout(searchTimerRef.current) }
  }, [search, groupId])

  // ── Atama: dogrudan veya onay sonrasi ──
  function performAssign(contactId) {
    setBusy(true)
    fetch('/PriceList/SetContactPriceGroup', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'same-origin',
      body: JSON.stringify({ contactId: contactId, priceGroupId: groupId }),
    })
      .then(function (r) { return r.json() })
      .then(function (resp) {
        if (resp && resp.success) {
          if (window.CalibraHub && window.CalibraHub.toast) {
            window.CalibraHub.toast(resp.message || 'Cari atandi.', 'ok')
          }
          // Listeleri tazele
          loadAssigned()
          // Search sonuclarini tazele (yeni durumlari yansitsin)
          if ((search || '').trim()) {
            // re-trigger by setting same value (debounce'a takilmaması icin direct)
            var q = search.trim()
            fetch('/PriceList/SearchContactsForGroup?q=' + encodeURIComponent(q) +
                  '&excludeGroupId=' + (groupId || 0) + '&pageSize=50',
              { credentials: 'same-origin' })
              .then(function (r) { return r.json() })
              .then(function (data) { setSearchResults(Array.isArray(data) ? data : []) })
              .catch(function () { /* ignore */ })
          }
        } else {
          var msg = (resp && resp.message) || 'Atama basarisiz.'
          if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast(msg, 'err')
          else alert(msg)
        }
      })
      .catch(function (err) {
        if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast('Hata: ' + err.message, 'err')
        else alert('Hata: ' + err.message)
      })
      .finally(function () { setBusy(false); setConfirm(null) })
  }

  function handleAssignClick(contact) {
    if (!contact || !contact.id) return
    if (contact.isAssignedToThisGroup) return // zaten ekli
    if (contact.isAssignedElsewhere) {
      // Bu cari baska bir gruba bagli — onay modali ile guncelleme onayi al.
      setConfirm({ contact: contact })
    } else {
      performAssign(contact.id)
    }
  }

  // ── Kaldirma ──
  function performUnassign(contactId) {
    setBusy(true)
    fetch('/PriceList/SetContactPriceGroup', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'same-origin',
      body: JSON.stringify({ contactId: contactId, priceGroupId: null }),
    })
      .then(function (r) { return r.json() })
      .then(function (resp) {
        if (resp && resp.success) {
          if (window.CalibraHub && window.CalibraHub.toast) {
            window.CalibraHub.toast(resp.message || 'Cari kaldirildi.', 'ok')
          }
          loadAssigned()
          if ((search || '').trim()) {
            // Aramayi tazele
            var q = search.trim()
            fetch('/PriceList/SearchContactsForGroup?q=' + encodeURIComponent(q) +
                  '&excludeGroupId=' + (groupId || 0) + '&pageSize=50',
              { credentials: 'same-origin' })
              .then(function (r) { return r.json() })
              .then(function (data) { setSearchResults(Array.isArray(data) ? data : []) })
              .catch(function () { /* ignore */ })
          }
        } else {
          var msg = (resp && resp.message) || 'Kaldirma basarisiz.'
          if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast(msg, 'err')
          else alert(msg)
        }
      })
      .catch(function (err) {
        if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast('Hata: ' + err.message, 'err')
        else alert('Hata: ' + err.message)
      })
      .finally(function () { setBusy(false); setUnassignConfirm(null) })
  }

  // ── Stiller (theme aware, inline) ──
  var bgBackdrop = 'rgba(0,0,0,0.55)'
  var cardBg = isDark ? '#0f172a' : '#ffffff'
  var cardBorder = isDark ? 'rgba(255,255,255,0.08)' : 'rgba(15,23,42,0.08)'
  var textPrimary = isDark ? '#f1f5f9' : '#0f172a'
  var textMuted = isDark ? 'rgba(255,255,255,0.55)' : '#64748b'
  var textSubtle = isDark ? 'rgba(255,255,255,0.4)' : '#94a3b8'
  var rowBg = isDark ? 'rgba(255,255,255,0.04)' : '#f8fafc'
  var rowBorder = isDark ? 'rgba(255,255,255,0.06)' : '#e2e8f0'
  var inputBg = isDark ? 'rgba(255,255,255,0.04)' : '#ffffff'
  var inputBorder = isDark ? 'rgba(255,255,255,0.1)' : '#cbd5e1'

  return (
    <div
      style={{ position: 'fixed', inset: 0, zIndex: 9998, background: bgBackdrop, backdropFilter: 'blur(4px)', display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 20 }}
      onClick={onClose}
    >
      <div
        style={{
          background: cardBg,
          border: '1px solid ' + cardBorder,
          borderRadius: 16,
          width: 'min(640px, 96vw)',
          maxHeight: '88vh',
          display: 'flex',
          flexDirection: 'column',
          overflow: 'hidden',
          boxShadow: '0 24px 64px rgba(0,0,0,0.45)',
        }}
        onClick={function (e) { e.stopPropagation() }}
      >
        {/* Header */}
        <div style={{ display: 'flex', alignItems: 'center', gap: 12, padding: '16px 20px', borderBottom: '1px solid ' + cardBorder, flexShrink: 0 }}>
          <div style={{
            width: 36, height: 36, borderRadius: 10, display: 'flex', alignItems: 'center', justifyContent: 'center',
            background: isDark ? 'rgba(14,165,233,0.15)' : '#ecfeff',
            border: '1px solid ' + (isDark ? 'rgba(14,165,233,0.3)' : '#a5f3fc'),
            color: isDark ? '#67e8f9' : '#0e7490',
          }}>
            <Users size={17} />
          </div>
          <div style={{ flex: 1, minWidth: 0 }}>
            <div style={{ fontSize: 13, fontWeight: 700, color: textPrimary, lineHeight: 1.2 }}>
              {groupCode ? groupCode + ' — ' : ''}{groupName || 'Fiyat Grubu'}
            </div>
            <div style={{ fontSize: 11, color: textMuted, marginTop: 2 }}>
              Eslesmis cariler ({assigned.length})
            </div>
          </div>
          <button
            type="button"
            onClick={onClose}
            style={{
              padding: 6, borderRadius: 8, background: 'transparent', border: '1px solid transparent',
              color: textMuted, cursor: 'pointer', display: 'flex', alignItems: 'center', justifyContent: 'center',
            }}
            title="Kapat (Esc)"
          >
            <X size={16} />
          </button>
        </div>

        {/* Body — assigned + search */}
        <div style={{ flex: 1, overflowY: 'auto', padding: '16px 20px', display: 'flex', flexDirection: 'column', gap: 18 }}>
          {/* Atanmis cariler */}
          <div>
            <div style={{ fontSize: 11, fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.04em', color: textMuted, marginBottom: 8 }}>
              Bu gruba atanmis cariler
            </div>
            {loadingAssigned ? (
              <div style={{ display: 'flex', alignItems: 'center', gap: 8, padding: 12, color: textSubtle, fontSize: 12 }}>
                <Loader2 size={14} className="animate-spin" /> Yukleniyor...
              </div>
            ) : assigned.length === 0 ? (
              <div style={{ padding: 16, textAlign: 'center', fontSize: 12, color: textSubtle, fontStyle: 'italic',
                background: rowBg, border: '1px dashed ' + rowBorder, borderRadius: 10,
              }}>
                Bu gruba atanmis cari yok.
              </div>
            ) : (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                {assigned.map(function (c) {
                  var meta = [c.phone, c.email, c.city].filter(Boolean).join(' · ')
                  return (
                    <div
                      key={c.id}
                      style={{
                        display: 'flex', alignItems: 'center', gap: 10, padding: '10px 12px',
                        background: rowBg, border: '1px solid ' + rowBorder, borderRadius: 10,
                      }}
                    >
                      <div style={{ flex: 1, minWidth: 0 }}>
                        <div style={{ fontSize: 13, color: textPrimary }}>
                          <strong style={{ fontFamily: 'ui-monospace, monospace', fontWeight: 700 }}>{c.accountCode}</strong>
                          <span style={{ marginLeft: 8 }}>{c.accountTitle}</span>
                        </div>
                        {meta && (
                          <div style={{ fontSize: 11, color: textSubtle, marginTop: 2 }}>{meta}</div>
                        )}
                      </div>
                      <button
                        type="button"
                        onClick={function () { setUnassignConfirm(c) }}
                        disabled={busy}
                        style={{
                          padding: 6, borderRadius: 8,
                          background: isDark ? 'rgba(239,68,68,0.1)' : '#fef2f2',
                          border: '1px solid ' + (isDark ? 'rgba(239,68,68,0.3)' : '#fecaca'),
                          color: isDark ? '#fca5a5' : '#dc2626',
                          cursor: busy ? 'not-allowed' : 'pointer',
                          opacity: busy ? 0.5 : 1,
                          display: 'flex', alignItems: 'center', justifyContent: 'center',
                        }}
                        title="Eslesmeyi kaldir"
                      >
                        <X size={13} />
                      </button>
                    </div>
                  )
                })}
              </div>
            )}
          </div>

          {/* Cari ekle */}
          <div>
            <div style={{ fontSize: 11, fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.04em', color: textMuted, marginBottom: 8 }}>
              Cari ekle
            </div>
            <div style={{ position: 'relative', marginBottom: 10 }}>
              <Search size={14} style={{ position: 'absolute', left: 12, top: '50%', transform: 'translateY(-50%)', color: textSubtle }} />
              <input
                type="search"
                value={search}
                onChange={function (e) { setSearch(e.target.value) }}
                placeholder="Kod veya unvan ile cari ara..."
                autoComplete="off"
                style={{
                  width: '100%', padding: '9px 12px 9px 34px', borderRadius: 10,
                  background: inputBg, border: '1px solid ' + inputBorder,
                  color: textPrimary, fontSize: 13, outline: 'none',
                }}
              />
              {searchLoading && (
                <Loader2 size={14} className="animate-spin" style={{ position: 'absolute', right: 12, top: '50%', transform: 'translateY(-50%)', color: textSubtle }} />
              )}
            </div>

            {!search.trim() ? (
              <div style={{ padding: 12, textAlign: 'center', fontSize: 12, color: textSubtle, fontStyle: 'italic' }}>
                Aramak icin yazmaya baslayin...
              </div>
            ) : searchResults.length === 0 && !searchLoading ? (
              <div style={{ padding: 12, textAlign: 'center', fontSize: 12, color: textSubtle, fontStyle: 'italic' }}>
                Cari bulunamadi.
              </div>
            ) : (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 6, maxHeight: 280, overflowY: 'auto' }}>
                {searchResults.map(function (c) {
                  var assignedHere    = !!c.isAssignedToThisGroup
                  var assignedElse    = !!c.isAssignedElsewhere
                  var btnLabel, btnStyle, btnIcon
                  if (assignedHere) {
                    btnLabel = 'Eklendi'
                    btnIcon = <Check size={13} />
                    btnStyle = {
                      background: isDark ? 'rgba(16,185,129,0.15)' : '#ecfdf5',
                      border: '1px solid ' + (isDark ? 'rgba(16,185,129,0.3)' : '#a7f3d0'),
                      color: isDark ? '#6ee7b7' : '#047857',
                    }
                  } else if (assignedElse) {
                    btnLabel = 'Tasi'
                    btnIcon = <AlertTriangle size={13} />
                    btnStyle = {
                      background: isDark ? 'rgba(245,158,11,0.15)' : '#fffbeb',
                      border: '1px solid ' + (isDark ? 'rgba(245,158,11,0.3)' : '#fde68a'),
                      color: isDark ? '#fcd34d' : '#b45309',
                    }
                  } else {
                    btnLabel = 'Ekle'
                    btnIcon = <Plus size={13} />
                    btnStyle = {
                      background: isDark ? 'rgba(99,102,241,0.15)' : '#eef2ff',
                      border: '1px solid ' + (isDark ? 'rgba(99,102,241,0.3)' : '#c7d2fe'),
                      color: isDark ? '#a5b4fc' : '#4338ca',
                    }
                  }
                  return (
                    <div
                      key={c.id}
                      style={{
                        display: 'flex', alignItems: 'center', gap: 10, padding: '10px 12px',
                        background: rowBg, border: '1px solid ' + rowBorder, borderRadius: 10,
                      }}
                    >
                      <div style={{ flex: 1, minWidth: 0 }}>
                        <div style={{ fontSize: 13, color: textPrimary }}>
                          <strong style={{ fontFamily: 'ui-monospace, monospace', fontWeight: 700 }}>{c.accountCode}</strong>
                          <span style={{ marginLeft: 8 }}>{c.accountTitle}</span>
                        </div>
                        {assignedElse && (
                          <div style={{ fontSize: 11, color: isDark ? '#fcd34d' : '#b45309', marginTop: 2, display: 'inline-flex', alignItems: 'center', gap: 4 }}>
                            <AlertTriangle size={11} /> Su an baska bir gruba bagli
                          </div>
                        )}
                      </div>
                      <button
                        type="button"
                        onClick={function () { handleAssignClick(c) }}
                        disabled={assignedHere || busy}
                        style={Object.assign({
                          padding: '6px 12px', borderRadius: 8, fontSize: 12, fontWeight: 600,
                          cursor: (assignedHere || busy) ? 'not-allowed' : 'pointer',
                          opacity: (assignedHere || busy) ? 0.7 : 1,
                          display: 'inline-flex', alignItems: 'center', gap: 4,
                        }, btnStyle)}
                        title={assignedHere ? 'Bu cari zaten bu gruba bagli' : (assignedElse ? 'Carinin grubu degisecek' : 'Bu gruba ekle')}
                      >
                        {btnIcon}
                        <span>{btnLabel}</span>
                      </button>
                    </div>
                  )
                })}
              </div>
            )}
          </div>
        </div>

        {/* Footer */}
        <div style={{ padding: '12px 20px', borderTop: '1px solid ' + cardBorder, flexShrink: 0, display: 'flex', justifyContent: 'flex-end' }}>
          <button
            type="button"
            onClick={onClose}
            style={{
              padding: '8px 16px', borderRadius: 8, fontSize: 12, fontWeight: 600,
              background: isDark ? 'rgba(255,255,255,0.06)' : '#f1f5f9',
              border: '1px solid ' + (isDark ? 'rgba(255,255,255,0.1)' : '#cbd5e1'),
              color: textPrimary, cursor: 'pointer',
            }}
          >
            Kapat
          </button>
        </div>
      </div>

      {/* ── Onay modali — cari baska gruba bagli ──
          CLAUDE.md "Silme onay standardi" tarzi ekran ortasi modal. */}
      {confirm && (
        <div
          style={{
            position: 'fixed', inset: 0, zIndex: 10001,
            background: 'rgba(0,0,0,0.55)', backdropFilter: 'blur(4px)',
            display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 20,
          }}
          onClick={function () { if (!busy) setConfirm(null) }}
        >
          <div
            style={{
              background: cardBg, border: '1px solid ' + cardBorder, borderRadius: 16,
              padding: '28px 24px', width: 'min(420px, 92vw)',
              boxShadow: '0 24px 64px rgba(0,0,0,0.5)',
              display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 12, textAlign: 'center',
            }}
            onClick={function (e) { e.stopPropagation() }}
          >
            <div style={{
              width: 44, height: 44, borderRadius: 22, display: 'flex', alignItems: 'center', justifyContent: 'center',
              background: isDark ? 'rgba(245,158,11,0.15)' : '#fffbeb',
              border: '1px solid ' + (isDark ? 'rgba(245,158,11,0.3)' : '#fde68a'),
              color: isDark ? '#fcd34d' : '#b45309',
            }}>
              <AlertTriangle size={22} />
            </div>
            <h3 style={{ fontSize: 14, fontWeight: 700, color: textPrimary, margin: 0 }}>
              Cari baska bir gruba bagli
            </h3>
            <p style={{ fontSize: 12.5, color: textMuted, margin: 0, lineHeight: 1.5 }}>
              <strong style={{ color: textPrimary, fontFamily: 'ui-monospace, monospace' }}>
                {confirm.contact.accountCode}
              </strong>
              {' '}— {confirm.contact.accountTitle}<br/>
              <span style={{ display: 'inline-block', marginTop: 6 }}>
                Bu cari su an baska bir fiyat grubuna bagli.
                {groupCode && (
                  <>
                    <br/>
                    <strong style={{ color: textPrimary }}>"{groupCode}"</strong> grubuna tasinsin mi?
                  </>
                )}
              </span>
            </p>
            <div style={{ display: 'flex', gap: 10, marginTop: 8 }}>
              <button
                type="button"
                onClick={function () { setConfirm(null) }}
                disabled={busy}
                style={{
                  padding: '8px 16px', borderRadius: 8, fontSize: 12.5, fontWeight: 600,
                  background: isDark ? 'rgba(255,255,255,0.06)' : '#f1f5f9',
                  color: textPrimary,
                  border: '1px solid ' + (isDark ? 'rgba(255,255,255,0.1)' : '#cbd5e1'),
                  cursor: busy ? 'not-allowed' : 'pointer', opacity: busy ? 0.5 : 1,
                }}
              >
                Vazgec
              </button>
              <button
                type="button"
                onClick={function () { performAssign(confirm.contact.id) }}
                disabled={busy}
                style={{
                  padding: '8px 16px', borderRadius: 8, fontSize: 12.5, fontWeight: 600,
                  background: 'linear-gradient(135deg,#f59e0b,#d97706)',
                  color: '#fff', border: 'none',
                  cursor: busy ? 'not-allowed' : 'pointer',
                  display: 'inline-flex', alignItems: 'center', gap: 6,
                }}
              >
                {busy ? <Loader2 size={13} className="animate-spin" /> : <AlertTriangle size={13} />}
                Tasi
              </button>
            </div>
          </div>
        </div>
      )}

      {/* ── Kaldirma onay modali ── */}
      {unassignConfirm && (
        <div
          style={{
            position: 'fixed', inset: 0, zIndex: 10001,
            background: 'rgba(0,0,0,0.55)', backdropFilter: 'blur(4px)',
            display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 20,
          }}
          onClick={function () { if (!busy) setUnassignConfirm(null) }}
        >
          <div
            style={{
              background: cardBg, border: '1px solid ' + cardBorder, borderRadius: 16,
              padding: '28px 24px', width: 'min(420px, 92vw)',
              boxShadow: '0 24px 64px rgba(0,0,0,0.5)',
              display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 12, textAlign: 'center',
            }}
            onClick={function (e) { e.stopPropagation() }}
          >
            <div style={{
              width: 44, height: 44, borderRadius: 22, display: 'flex', alignItems: 'center', justifyContent: 'center',
              background: isDark ? 'rgba(239,68,68,0.15)' : '#fef2f2',
              border: '1px solid ' + (isDark ? 'rgba(239,68,68,0.3)' : '#fecaca'),
              color: isDark ? '#fca5a5' : '#dc2626',
            }}>
              <Trash2 size={22} />
            </div>
            <h3 style={{ fontSize: 14, fontWeight: 700, color: textPrimary, margin: 0 }}>
              Eslesme kaldirilacak
            </h3>
            <p style={{ fontSize: 12.5, color: textMuted, margin: 0, lineHeight: 1.5 }}>
              <strong style={{ color: textPrimary, fontFamily: 'ui-monospace, monospace' }}>
                {unassignConfirm.accountCode}
              </strong>
              {' '}— {unassignConfirm.accountTitle}<br/>
              <span style={{ display: 'inline-block', marginTop: 6 }}>
                Bu carinin {groupCode ? <>"<strong style={{ color: textPrimary }}>{groupCode}</strong>"</> : 'fiyat grubu'} eslemesi kaldirilsin mi?
              </span>
            </p>
            <div style={{ display: 'flex', gap: 10, marginTop: 8 }}>
              <button
                type="button"
                onClick={function () { setUnassignConfirm(null) }}
                disabled={busy}
                style={{
                  padding: '8px 16px', borderRadius: 8, fontSize: 12.5, fontWeight: 600,
                  background: isDark ? 'rgba(255,255,255,0.06)' : '#f1f5f9',
                  color: textPrimary,
                  border: '1px solid ' + (isDark ? 'rgba(255,255,255,0.1)' : '#cbd5e1'),
                  cursor: busy ? 'not-allowed' : 'pointer', opacity: busy ? 0.5 : 1,
                }}
              >
                Vazgec
              </button>
              <button
                type="button"
                onClick={function () { performUnassign(unassignConfirm.id) }}
                disabled={busy}
                style={{
                  padding: '8px 16px', borderRadius: 8, fontSize: 12.5, fontWeight: 600,
                  background: 'linear-gradient(135deg,#ef4444,#dc2626)',
                  color: '#fff', border: 'none',
                  cursor: busy ? 'not-allowed' : 'pointer',
                  display: 'inline-flex', alignItems: 'center', gap: 6,
                }}
              >
                {busy ? <Loader2 size={13} className="animate-spin" /> : <Trash2 size={13} />}
                Kaldir
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
