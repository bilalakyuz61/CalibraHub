/**
 * InvoiceDataGrid — Elektronik Belgeler için yoğun veri tablosu.
 *
 * Tema: body.app-theme-dark → CSS değişkenleri .igd-root üzerinde override edilir
 * (Tailwind dark: çalışmaz — proje body.app-theme-dark kullanıyor)
 *
 * Props:
 *   rows:               [{ id, documentNumber, kind, scenario, senderTaxNumber,
 *                          senderName, issueDate, importedAt, isProcessed }]
 *   antiforgeryToken:   string
 *   toggleProcessedUrl: string
 */
import { useState, useMemo } from 'react'
import { Eye, List, Download, CheckCircle2, X, Loader2, Search } from 'lucide-react'
import { navigateInWorkspace } from '../../utils/workspaceNav'

export default function InvoiceDataGrid(props) {
  var rows        = Array.isArray(props.rows) ? props.rows : []
  var antiforgery = props.antiforgeryToken || ''
  var toggleUrl   = props.toggleProcessedUrl || '/Approval/ToggleProcessed'

  var [search,     setSearch]     = useState('')
  var [liveRows,   setLiveRows]   = useState(rows)
  var [modalOpen,  setModalOpen]  = useState(false)
  var [modalHtml,  setModalHtml]  = useState('')
  var [modalTitle, setModalTitle] = useState('')
  var [modalLoad,  setModalLoad]  = useState(false)
  var [togglingId, setTogglingId] = useState(null)

  var filtered = useMemo(function() {
    if (!search.trim()) return liveRows
    var q = search.trim().toLowerCase()
    return liveRows.filter(function(r) {
      return (
        (r.documentNumber || '').toLowerCase().includes(q) ||
        (r.scenario       || '').toLowerCase().includes(q) ||
        (r.kind           || '').toLowerCase().includes(q) ||
        (r.senderTaxNumber|| '').toLowerCase().includes(q) ||
        (r.senderName     || '').toLowerCase().includes(q)
      )
    })
  }, [liveRows, search])

  function handleView(row) { navigateInWorkspace('/Approval/ViewPayload/' + row.id) }

  function handleLines(row) {
    setModalTitle('Kalemler — ' + row.documentNumber)
    setModalHtml(''); setModalLoad(true); setModalOpen(true)
    fetch('/Approval/DocumentLines/' + row.id, { credentials: 'same-origin' })
      .then(function(r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.text() })
      .then(function(html) { setModalHtml(html); setModalLoad(false) })
      .catch(function(err) {
        setModalHtml('<p style="padding:20px;color:#f87171;text-align:center">Yüklenemedi: ' + err.message + '</p>')
        setModalLoad(false)
      })
  }

  function handleDownload(row) {
    var a = document.createElement('a')
    a.href = '/Approval/DownloadPayload/' + row.id; a.download = ''
    document.body.appendChild(a); a.click(); document.body.removeChild(a)
  }

  async function handleToggle(row) {
    if (togglingId === row.id) return
    setTogglingId(row.id)
    var fd = new FormData()
    fd.append('id', row.id)
    fd.append('isProcessed', String(!row.isProcessed))
    if (antiforgery) fd.append('__RequestVerificationToken', antiforgery)
    try {
      var res  = await fetch(toggleUrl, { method: 'POST', body: fd, credentials: 'same-origin' })
      var data = await res.json()
      if (data && data.success === false) {
        // Rapor §6.6 — toast fallback
        var m = 'Hata: ' + (data.message || 'Bilinmeyen')
        if (window.CalibraAlert && window.CalibraAlert.error) window.CalibraAlert.error(m)
        else if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast(m, 'err')
        else alert(m)
      } else {
        setLiveRows(function(prev) {
          return prev.map(function(r) { return r.id === row.id ? Object.assign({}, r, { isProcessed: !r.isProcessed }) : r })
        })
      }
    } catch (err) {
      var em = 'Bağlantı hatası: ' + err.message
      if (window.CalibraAlert && window.CalibraAlert.error) window.CalibraAlert.error(em)
      else if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast(em, 'err')
      else alert(em)
    }
    finally { setTogglingId(null) }
  }

  return (
    <div className="igd-root" style={{
      display: 'flex', flexDirection: 'column',
      height: '100%', minHeight: 0, overflow: 'hidden',
      background: 'var(--igd-bg)',
      color: 'var(--igd-text)',
    }}>

      {/* ── Arama çubuğu ─────────────────────────────────── */}
      <div style={{
        display: 'flex', alignItems: 'center', gap: 8,
        padding: '7px 12px', flexShrink: 0,
        borderBottom: '1px solid var(--igd-border)',
        background: 'var(--igd-toolbar-bg)',
      }}>
        <Search size={13} style={{ color: 'var(--igd-text-muted)', flexShrink: 0 }} />
        <input
          type="text"
          value={search}
          onChange={function(e) { setSearch(e.target.value) }}
          placeholder="Belge no, gönderici, VKN ara…"
          style={{
            flex: 1, background: 'transparent', border: 'none', outline: 'none',
            fontSize: 13, color: 'var(--igd-text)',
          }}
        />
        {search && (
          <button onClick={function() { setSearch('') }}
            style={{ background: 'none', border: 'none', cursor: 'pointer', padding: 2, color: 'var(--igd-text-muted)', lineHeight: 0 }}>
            <X size={12} />
          </button>
        )}
        <span style={{ fontSize: 11, color: 'var(--igd-text-muted)', flexShrink: 0, fontVariantNumeric: 'tabular-nums' }}>
          {filtered.length}&thinsp;/&thinsp;{liveRows.length}
        </span>
      </div>

      {/* ── Tablo ────────────────────────────────────────── */}
      <div style={{ flex: '1 1 auto', overflow: 'auto', minHeight: 0 }}>
        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12 }}>
          <thead>
            <tr style={{ position: 'sticky', top: 0, zIndex: 10, background: 'var(--igd-thead-bg)' }}>
              {[
                { label: '',              w: 124 },
                { label: 'Belge No',      w: null },
                { label: 'Senaryo',       w: null },
                { label: 'Gönderici VKN', w: null },
                { label: 'Cari İsim',     w: null },
                { label: 'Belge Tarihi',  w: null },
                { label: 'Sisteme Giriş', w: null },
              ].map(function(col, i) {
                return (
                  <th key={i} style={{
                    padding: '8px 12px',
                    textAlign: 'left',
                    fontWeight: 600,
                    fontSize: 10,
                    textTransform: 'uppercase',
                    letterSpacing: '.06em',
                    color: 'var(--igd-th-text)',
                    borderBottom: '1px solid var(--igd-border)',
                    whiteSpace: 'nowrap',
                    width: col.w || undefined,
                    background: 'var(--igd-thead-bg)',
                  }}>
                    {col.label}
                  </th>
                )
              })}
            </tr>
          </thead>
          <tbody>
            {filtered.length === 0 && (
              <tr>
                <td colSpan={7} style={{
                  padding: '52px 12px', textAlign: 'center',
                  fontSize: 13, color: 'var(--igd-text-muted)'
                }}>
                  {search ? 'Arama kriterine uyan belge bulunamadı.' : 'Bekleyen belge bulunmuyor.'}
                </td>
              </tr>
            )}

            {filtered.map(function(row) {
              return (
                <tr key={row.id} className="igd-row" style={{
                  borderBottom: '1px solid var(--igd-row-border)',
                  transition: 'background 60ms',
                }}>

                  {/* Aksiyon butonları */}
                  <td style={{ padding: '3px 6px', whiteSpace: 'nowrap' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 2 }}>

                      <button onClick={function() { handleView(row) }} title="Görüntüle"
                        className="igd-btn igd-btn--view"
                        style={{ padding: '5px 6px', borderRadius: 7, border: 'none', cursor: 'pointer', background: 'transparent', lineHeight: 0, color: 'var(--igd-text-muted)' }}>
                        <Eye size={13} />
                      </button>

                      <button onClick={function() { handleLines(row) }} title="Kalem Detayları"
                        className="igd-btn igd-btn--lines"
                        style={{ padding: '5px 6px', borderRadius: 7, border: 'none', cursor: 'pointer', background: 'transparent', lineHeight: 0, color: 'var(--igd-text-muted)' }}>
                        <List size={13} />
                      </button>

                      <button onClick={function() { handleDownload(row) }} title="XML İndir"
                        className="igd-btn igd-btn--dl"
                        style={{ padding: '5px 6px', borderRadius: 7, border: 'none', cursor: 'pointer', background: 'transparent', lineHeight: 0, color: 'var(--igd-text-muted)' }}>
                        <Download size={13} />
                      </button>

                      <button
                        onClick={function() { handleToggle(row) }}
                        title={row.isProcessed ? 'İşlenmedi Yap' : 'İşlendi İşaretle'}
                        disabled={togglingId === row.id}
                        className={'igd-btn ' + (row.isProcessed ? 'igd-btn--processed' : 'igd-btn--unprocessed')}
                        style={{
                          padding: '5px 6px', borderRadius: 7, border: 'none', cursor: 'pointer',
                          background: 'transparent', lineHeight: 0, opacity: togglingId === row.id ? 0.5 : 1,
                          color: row.isProcessed ? 'var(--igd-ok)' : 'var(--igd-text-dim)',
                        }}>
                        {togglingId === row.id
                          ? <Loader2 size={13} className="igd-spin" />
                          : <CheckCircle2 size={13} />}
                      </button>

                    </div>
                  </td>

                  {/* Belge No */}
                  <td style={{ padding: '5px 12px', whiteSpace: 'nowrap' }}>
                    <span style={{ fontFamily: 'monospace', fontSize: 11.5, fontWeight: 500, color: 'var(--igd-text)' }}>
                      {row.documentNumber}
                    </span>
                    {row.isProcessed && (
                      <span style={{
                        marginLeft: 6, display: 'inline-flex', alignItems: 'center',
                        padding: '1px 6px', borderRadius: 4, fontSize: 10, fontWeight: 600,
                        background: 'var(--igd-badge-ok-bg)', color: 'var(--igd-ok)',
                      }}>
                        İşlendi
                      </span>
                    )}
                  </td>

                  {/* Senaryo */}
                  <td style={{ padding: '5px 12px', color: 'var(--igd-text-muted)' }}>
                    {row.scenario || row.kind || '—'}
                  </td>

                  {/* Gönderici VKN */}
                  <td style={{ padding: '5px 12px', fontFamily: 'monospace', fontSize: 11, color: 'var(--igd-text-muted)' }}>
                    {row.senderTaxNumber || '—'}
                  </td>

                  {/* Cari İsim */}
                  <td style={{ padding: '5px 12px', maxWidth: 280, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', color: 'var(--igd-text)' }}>
                    {row.senderName || '—'}
                  </td>

                  {/* Belge Tarihi */}
                  <td style={{ padding: '5px 12px', whiteSpace: 'nowrap', fontVariantNumeric: 'tabular-nums', color: 'var(--igd-text-muted)' }}>
                    {row.issueDate}
                  </td>

                  {/* Sisteme Giriş */}
                  <td style={{ padding: '5px 12px', whiteSpace: 'nowrap', fontVariantNumeric: 'tabular-nums', fontSize: 11, color: 'var(--igd-text-dim)' }}>
                    {row.importedAt}
                  </td>

                </tr>
              )
            })}
          </tbody>
        </table>
      </div>

      {/* ── Modal ─────────────────────────────────────────── */}
      {modalOpen && (
        <div
          style={{
            position: 'fixed', inset: 0, zIndex: 9999,
            display: 'flex', alignItems: 'center', justifyContent: 'center',
            background: 'rgba(0,0,0,0.62)', backdropFilter: 'blur(6px)',
          }}
          onClick={function() { setModalOpen(false) }}>
          <div
            style={{
              background: 'var(--igd-modal-bg)',
              border: '1px solid var(--igd-modal-border)',
              borderRadius: 16,
              boxShadow: '0 24px 80px rgba(0,0,0,.45)',
              width: '100%', maxWidth: 820,
              maxHeight: '80vh',
              display: 'flex', flexDirection: 'column',
              overflow: 'hidden',
              margin: '0 16px',
            }}
            onClick={function(e) { e.stopPropagation() }}>

            {/* Modal header */}
            <div style={{
              display: 'flex', alignItems: 'center', justifyContent: 'space-between',
              padding: '12px 18px', flexShrink: 0,
              borderBottom: '1px solid var(--igd-modal-border)',
            }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                <List size={15} style={{ color: 'var(--igd-accent)', flexShrink: 0 }} />
                <h3 style={{ margin: 0, fontSize: 13.5, fontWeight: 600, color: 'var(--igd-text)' }}>
                  {modalTitle}
                </h3>
              </div>
              <button
                onClick={function() { setModalOpen(false) }}
                className="igd-btn igd-btn--close"
                style={{
                  padding: '5px 6px', borderRadius: 7, border: 'none',
                  cursor: 'pointer', background: 'transparent', lineHeight: 0,
                  color: 'var(--igd-text-muted)',
                }}>
                <X size={16} />
              </button>
            </div>

            {/* Modal body */}
            <div style={{ flex: '1 1 auto', overflowY: 'auto', padding: 16, minHeight: 0 }}>
              {modalLoad
                ? (
                  <div style={{
                    display: 'flex', alignItems: 'center', justifyContent: 'center',
                    gap: 8, padding: '52px 0', fontSize: 13, color: 'var(--igd-text-muted)'
                  }}>
                    <Loader2 size={16} className="igd-spin" />
                    Kalemler getiriliyor…
                  </div>
                )
                : <div className="igd-lines-body" dangerouslySetInnerHTML={{ __html: modalHtml }} />
              }
            </div>
          </div>
        </div>
      )}

    </div>
  )
}
