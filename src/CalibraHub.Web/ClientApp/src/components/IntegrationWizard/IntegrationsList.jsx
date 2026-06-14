/**
 * IntegrationsList — Entegrasyon liste sayfası.
 *
 * /Integrations/api/list endpoint'inden çekip kart-stilinde gösterir.
 * Her satırda: ad/açıklama, form→endpoint flow, durum chip'leri, aksiyon butonları.
 *
 * Aksiyonlar:
 *   ▶  Çalıştır (Manual trigger — POST /Integration/Run/{id})
 *   ✏  Düzenle  (wizard'a yönlendir)
 *   ⎘  Kopyala  (POST /api/duplicate/{id} → wizard'a aç)
 *   ⏼  Toggle   (POST /api/toggle/{id})
 *   🗑  Sil      (POST /api/delete/{id} — onay modali)
 */
import React, { useState, useEffect, useCallback, useMemo } from 'react'
import {
  Plug, Plus, Search, Edit2, Copy, Trash2, Power, Globe,
  AlertTriangle, RefreshCw, Check, X, Loader2,
  Download, Upload,
} from 'lucide-react'

function getCsrf() {
  const el = document.querySelector('input[name="__RequestVerificationToken"]')
  return el ? el.value : ''
}
function toast(msg, kind) {
  if (window.CalibraHub?.toast) window.CalibraHub.toast(msg, kind || 'info')
}

function DeleteModal({ name, onCancel, onConfirm, loading }) {
  useEffect(() => {
    const h = e => { if (e.key === 'Escape') onCancel() }
    window.addEventListener('keydown', h)
    return () => window.removeEventListener('keydown', h)
  }, [onCancel])
  return (
    <div className="iw-modal-bd" onClick={onCancel}>
      <div className="iw-modal" onClick={e => e.stopPropagation()}>
        <div className="iw-modal-icon"><AlertTriangle size={32} /></div>
        <div className="iw-modal-title">Entegrasyonu Sil</div>
        <div className="iw-modal-msg">
          <strong>{name}</strong> tamamen silinecek — mapping kuralları, trigger'lar
          ve <strong>audit log kayıtları</strong> dahil. Bu işlem geri alınamaz.
          <br /><br />
          <span style={{ fontSize: 12, opacity: 0.8 }}>
            💡 Logu korumak istiyorsanız "Sil" yerine <strong>Pasif Yap</strong> (⏼) butonunu kullanın.
          </span>
        </div>
        <div className="iw-modal-actions">
          <button className="iw-modal-cancel" onClick={onCancel}>Vazgeç</button>
          <button className="iw-modal-del" onClick={onConfirm} disabled={loading}>
            {loading ? 'Siliniyor…' : 'Sil'}
          </button>
        </div>
      </div>
    </div>
  )
}

export default function IntegrationsList({ config }) {
  const apiBase     = config?.apiBase || '/Integrations/api'
  const wizardNew   = config?.wizardUrlNew || '/Integrations/Wizard'
  const wizardEdit  = (id) => (config?.wizardUrlEdit || '/Integrations/Wizard/{id}').replace('{id}', id)

  const [items, setItems]   = useState([])
  const [loading, setLoading] = useState(true)
  const [search, setSearch] = useState('')
  const [showInactive, setShowInactive] = useState(true)
  const [deleteTarget, setDeleteTarget] = useState(null)
  const [deleting, setDeleting] = useState(false)
  // 2026-05-22: Play butonu kaldırıldı (Wizard Step 4'te Test akışı var).
  // runningId state + handleRun callback de gereksizdi, silindi.

  const refresh = useCallback(async () => {
    try {
      setLoading(true)
      const r = await fetch(`${apiBase}/list?includeInactive=${showInactive}`, { credentials: 'same-origin' })
      const d = await r.json()
      if (d.success) setItems(d.items || [])
      else toast(d.error || 'Yenileme hatası', 'err')
    } catch (e) {
      toast('Sunucu hatası: ' + (e.message || ''), 'err')
    } finally {
      setLoading(false)
    }
  }, [apiBase, showInactive])

  useEffect(() => { refresh() }, [refresh])

  const filtered = useMemo(() => {
    if (!search) return items
    const q = search.toLowerCase()
    return items.filter(i =>
      (i.name || '').toLowerCase().includes(q) ||
      (i.description || '').toLowerCase().includes(q) ||
      (i.sourceFormCode || '').toLowerCase().includes(q) ||
      (i.endpointName || '').toLowerCase().includes(q)
    )
  }, [items, search])

  const handleToggle = useCallback(async (item) => {
    try {
      const r = await fetch(`${apiBase}/toggle/${item.id}`, {
        method: 'POST', credentials: 'same-origin',
        headers: { RequestVerificationToken: getCsrf() },
      })
      const d = await r.json()
      if (d.success) {
        toast(d.isActive ? 'Aktif edildi' : 'Pasif edildi', 'ok')
        refresh()
      } else toast(d.error || 'Hata', 'err')
    } catch (e) {
      toast('Sunucu hatası: ' + e.message, 'err')
    }
  }, [apiBase, refresh])

  const handleDuplicate = useCallback(async (item) => {
    try {
      const r = await fetch(`${apiBase}/duplicate/${item.id}`, {
        method: 'POST', credentials: 'same-origin',
        headers: { RequestVerificationToken: getCsrf() },
      })
      const d = await r.json()
      if (d.success) {
        toast('Kopya oluşturuldu — düzenleme ekranına yönlendiriliyor', 'ok')
        window.location.href = wizardEdit(d.id)
      } else toast(d.error || 'Hata', 'err')
    } catch (e) {
      toast('Sunucu hatası: ' + e.message, 'err')
    }
  }, [apiBase, wizardEdit])

  const handleDelete = useCallback(async () => {
    if (!deleteTarget) return
    setDeleting(true)
    try {
      const r = await fetch(`${apiBase}/delete/${deleteTarget.id}`, {
        method: 'POST', credentials: 'same-origin',
        headers: { RequestVerificationToken: getCsrf() },
      })
      const d = await r.json()
      if (d.success) {
        toast('Silindi', 'ok')
        setDeleteTarget(null)
        refresh()
      } else toast(d.error || 'Silinemedi', 'err')
    } catch (e) {
      toast('Sunucu hatası: ' + e.message, 'err')
    } finally {
      setDeleting(false)
    }
  }, [apiBase, deleteTarget, refresh])

  // 2026-05-21 Faz 1: Dışa Aktar — server endpoint dosyayı stream eder,
  // browser otomatik indirir. Yeni window açıp da yaptırılabilir.
  const handleExport = useCallback((item) => {
    try {
      window.location.assign(`/Integrations/api/export/${item.id}`)
    } catch (e) {
      toast('İndirme başlatılamadı: ' + e.message, 'err')
    }
  }, [])

  // İçe Aktar — file picker → JSON parse → conflict strategy modal
  const [importPending, setImportPending] = useState(null) // { bundle, fileName, existingMatch }
  const [importing, setImporting]         = useState(false)
  const fileInputRef = React.useRef(null)

  const handleImportButton = useCallback(() => {
    if (fileInputRef.current) {
      fileInputRef.current.value = '' // önceki seçim varsa sıfırla
      fileInputRef.current.click()
    }
  }, [])

  const handleImportFile = useCallback(async (e) => {
    const file = e.target.files?.[0]
    if (!file) return
    try {
      const text = await file.text()
      const bundle = JSON.parse(text)
      // Validation: kind ve integration alanı zorunlu
      if (!bundle || !bundle.integration) {
        toast('Geçersiz bundle: integration alanı eksik.', 'err')
        return
      }
      const name = bundle.integration.name || ''
      const existing = items.find(i => (i.name || '').toLowerCase() === name.toLowerCase())
      setImportPending({ bundle, fileName: file.name, existingMatch: existing })
    } catch (err) {
      toast('JSON parse hatası: ' + err.message, 'err')
    }
  }, [items])

  const handleImportConfirm = useCallback(async (strategy) => {
    if (!importPending) return
    setImporting(true)
    try {
      const r = await fetch(`${apiBase}/import`, {
        method: 'POST', credentials: 'same-origin',
        headers: {
          'Content-Type': 'application/json',
          RequestVerificationToken: getCsrf(),
        },
        body: JSON.stringify({
          bundle: importPending.bundle,
          conflictStrategy: strategy,
        }),
      })
      const d = await r.json()
      if (d.success) {
        let msg = d.status === 'Skipped'
          ? 'İçe aktarma atlandı.'
          : (d.status === 'Overwritten' ? 'Mevcut entegrasyon güncellendi.' : 'Yeni entegrasyon oluşturuldu.')
        if (d.message) msg += ' ' + d.message
        if (d.warnings && d.warnings.length > 0) msg += ' Uyarılar: ' + d.warnings.join(' | ')
        toast(msg, 'ok')
        setImportPending(null)
        refresh()
      } else {
        toast(d.error || d.message || 'İçe aktarma başarısız.', 'err')
      }
    } catch (e) {
      toast('Sunucu hatası: ' + e.message, 'err')
    } finally {
      setImporting(false)
    }
  }, [apiBase, importPending, refresh])

  return (
    <div className="il-root">
      <div className="il-toolbar">
        <div className="il-title">
          <Plug size={16} />
          <span>Entegrasyonlar</span>
          <span className="il-count">{filtered.length} / {items.length}</span>
        </div>
        <div className="il-spacer" />
        <div className="il-search-wrap">
          <Search size={13} className="il-search-icon" />
          <input className="il-search" placeholder="Ad, form veya endpoint ara…"
                 value={search} onChange={e => setSearch(e.target.value)} />
        </div>
        <button className="iw-btn-secondary" onClick={refresh} title="Yenile">
          <RefreshCw size={13} />
        </button>
        {/* 2026-05-21 Faz 1: İçe Aktar — JSON dosyası seç */}
        <button className="iw-btn-secondary" onClick={handleImportButton} title="JSON dosyasından içe aktar">
          <Upload size={13} /> İçe Aktar
        </button>
        <input ref={fileInputRef} type="file" accept="application/json,.json"
               style={{ display: 'none' }} onChange={handleImportFile} />
        <a href={wizardNew} className="il-btn-primary">
          <Plus size={14} /> Yeni Entegrasyon
        </a>
      </div>

      <div className="il-list">
        {loading && (
          <div className="il-empty">
            <Loader2 className="iw-spin" size={32} />
            <span>Yükleniyor…</span>
          </div>
        )}

        {!loading && filtered.length === 0 && (
          <div className="il-empty">
            <Plug size={48} style={{ opacity: 0.3 }} />
            <span>{items.length === 0 ? 'Henüz entegrasyon tanımlanmamış.' : 'Aramaya uyan kayıt yok.'}</span>
            {items.length === 0 && (
              <a href={wizardNew} className="il-btn-primary">
                <Plus size={14} /> İlk entegrasyonu oluştur
              </a>
            )}
          </div>
        )}

        {!loading && filtered.map(item => (
          <div key={item.id} className={'il-card' + (item.isActive ? '' : ' is-inactive')}>
            {/* Aksiyonlar sol basta — Düzenle/Kopyala/Export/Toggle/Sil
                2026-05-22: "Şimdi çalıştır" play butonu kaldırıldı. Test/run akışı zaten
                Wizard Step 4 (Test) içinde — kullanıcı JSON çıktıyı görür + dry-run + gerçek
                gönderim seçenekleri olur. Liste kartı sadeleştirildi. */}
            <div className="il-actions il-actions--leading">
              <button className="il-act il-act-edit" title="Düzenle"
                      onClick={() => window.location.href = wizardEdit(item.id)}>
                <Edit2 size={14} />
              </button>
              <button className="il-act" title="Kopyala"
                      onClick={() => handleDuplicate(item)}>
                <Copy size={14} />
              </button>
              <button className="il-act" title="Dışa Aktar (JSON indir)"
                      onClick={() => handleExport(item)}>
                <Download size={14} />
              </button>
              <button className="il-act" title={item.isActive ? 'Pasif yap' : 'Aktif yap'}
                      onClick={() => handleToggle(item)}>
                <Power size={14} />
              </button>
              <button className="il-act il-act-del" title="Sil"
                      onClick={() => setDeleteTarget(item)}>
                <Trash2 size={14} />
              </button>
            </div>

            <div className="il-card-main">
              <div className="il-card-name">{item.name}</div>
              <div className="il-card-desc">{item.description || '—'}</div>
            </div>

            <div className="il-card-flow">
              {/* 2026-05-22: "Liste / Üst Bilgi" form name kaldırıldı — entity adı zaten
                  integration adından bellidir. Sadece endpoint + profile kalsın, kart daha dar. */}
              <span>{item.endpointName} ({item.apiProfileName})</span>
            </div>

            <div className="il-chips">
              <span className={'il-chip ' + (item.isActive ? 'il-chip-active' : 'il-chip-inactive')}>
                {item.isActive ? <Check size={11} /> : <X size={11} />}
                {item.isActive ? 'Aktif' : 'Pasif'}
              </span>
              {item.triggerCount > 0 && (
                <span className="il-chip il-chip-trigger" title="Aktif tetikleyici sayısı">
                  ⚡ {item.triggerCount}
                </span>
              )}
              {item.runCount > 0 && (
                <span className={'il-chip ' + (item.lastRunStatus === 'Success' ? 'il-chip-success' : item.lastRunStatus === 'Failed' ? 'il-chip-failed' : 'il-chip-runs')}
                      title={`Son: ${item.lastRunAt || ''}`}>
                  ↻ {item.runCount}
                </span>
              )}
            </div>
          </div>
        ))}
      </div>

      {deleteTarget && (
        <DeleteModal
          name={deleteTarget.name}
          onCancel={() => setDeleteTarget(null)}
          onConfirm={handleDelete}
          loading={deleting}
        />
      )}

      {importPending && (
        <ImportConfirmModal
          fileName={importPending.fileName}
          bundle={importPending.bundle}
          existingMatch={importPending.existingMatch}
          loading={importing}
          onCancel={() => setImportPending(null)}
          onConfirm={handleImportConfirm}
        />
      )}
    </div>
  )
}

/**
 * 2026-05-21 Faz 1: İçe Aktarma onay/çakışma modal'ı.
 * Eğer aynı isimli integration varsa kullanıcı 3 strateji arasında seçim yapar:
 *   - Overwrite: mevcut kaydı güncelle
 *   - NewCopy: aynı içerikle " (Kopya)" suffix'li yeni kayıt
 *   - Skip: hiç yapma
 * Çakışma yoksa direkt "İçe Aktar" butonu — NewCopy stratejisi gönderilir.
 */
function ImportConfirmModal({ fileName, bundle, existingMatch, loading, onCancel, onConfirm }) {
  React.useEffect(() => {
    const h = e => { if (e.key === 'Escape' && !loading) onCancel() }
    window.addEventListener('keydown', h)
    return () => window.removeEventListener('keydown', h)
  }, [onCancel, loading])

  const entry = bundle?.integration || {}
  const mappingCount = (entry.mappings || []).length
  const triggerCount = (entry.triggers || []).length
  const hasEndpoint  = !!entry.endpoint
  const hasApiProfile = !!entry.apiProfile      // Faz 1.5 — self-contained profile

  return (
    <div className="iw-modal-bd" onClick={loading ? undefined : onCancel}>
      <div className="iw-modal" onClick={e => e.stopPropagation()} style={{ maxWidth: 560 }}>
        <div className="iw-modal-icon" style={{ color: '#a5b4fc' }}>
          <Upload size={32} />
        </div>
        <div className="iw-modal-title">İçe Aktar — Onay</div>
        <div className="iw-modal-msg" style={{ textAlign: 'left' }}>
          <div style={{ marginBottom: 12 }}>
            <strong>Dosya:</strong> <code style={{ fontSize: 11 }}>{fileName}</code>
          </div>
          <div style={{
            padding: 12, borderRadius: 8, background: 'rgba(99,102,241,.08)',
            border: '1px solid rgba(99,102,241,.25)', marginBottom: 12,
          }}>
            <div style={{ fontSize: 14, fontWeight: 600, marginBottom: 4 }}>
              {entry.name || '(adsız)'}
            </div>
            {entry.description && (
              <div style={{ fontSize: 12, color: '#94a3b8', marginBottom: 6 }}>{entry.description}</div>
            )}
            <div style={{ fontSize: 11, color: '#94a3b8', display: 'flex', gap: 12, flexWrap: 'wrap' }}>
              <span>Form: <code>{entry.sourceFormCode || '—'}</code></span>
              <span>{mappingCount} mapping</span>
              <span>{triggerCount} trigger</span>
              <span>{hasEndpoint ? 'Endpoint dahil' : 'Endpoint yok (Sadece Procedure)'}</span>
              {hasApiProfile && (
                <span title={`Profile: ${entry.apiProfile.name} (${entry.apiProfile.baseUrl})`}>
                  API Profile: <strong>{entry.apiProfile.name}</strong>
                </span>
              )}
            </div>
          </div>

          {/* Faz 1.5: Profile uyarısı — bundle'da profile var ve hedefte yoksa
              credentials uyarısı göster (kullanıcı önceden bilsin). */}
          {hasApiProfile && (
            <div style={{
              padding: '8px 12px', borderRadius: 6,
              background: 'rgba(99,102,241,.08)', border: '1px solid rgba(99,102,241,.25)',
              color: '#a5b4fc', fontSize: 11.5, marginBottom: 10,
            }}>
              🔑 <strong>API Profile dahil:</strong> "{entry.apiProfile.name}" (Auth: {entry.apiProfile.authType}).
              Hedef ortamda profile yoksa <strong>otomatik oluşturulur</strong>, ancak <strong>credentials boş gelir</strong> —
              Profiller sayfasından admin elle doldurmalı.
            </div>
          )}

          {existingMatch ? (
            <div style={{
              padding: 12, borderRadius: 8, background: 'rgba(245,158,11,.10)',
              border: '1px solid rgba(245,158,11,.4)', color: '#fbbf24', fontSize: 13,
            }}>
              ⚠ <strong>"{entry.name}"</strong> adında bir entegrasyon zaten var. Ne yapalım?
            </div>
          ) : (
            <div style={{
              padding: 12, borderRadius: 8, background: 'rgba(34,197,94,.10)',
              border: '1px solid rgba(34,197,94,.4)', color: '#4ade80', fontSize: 13,
            }}>
              ✓ Çakışma yok — yeni entegrasyon olarak içe alınacak.
            </div>
          )}
        </div>

        <div className="iw-modal-actions" style={{ flexWrap: 'wrap', gap: 8 }}>
          <button className="iw-btn-secondary" onClick={onCancel} disabled={loading}>
            Vazgeç
          </button>
          {existingMatch ? (
            <>
              <button className="iw-btn-secondary" onClick={() => onConfirm('Skip')} disabled={loading}>
                Atla
              </button>
              <button className="iw-btn-secondary" onClick={() => onConfirm('NewCopy')} disabled={loading}>
                Yeni Kopya
              </button>
              <button className="iw-btn-primary" onClick={() => onConfirm('Overwrite')} disabled={loading}
                      style={{ background: '#f59e0b', borderColor: '#d97706' }}>
                {loading ? <Loader2 className="iw-spin" size={14} /> : null}
                Üstüne Yaz
              </button>
            </>
          ) : (
            <button className="iw-btn-primary" onClick={() => onConfirm('NewCopy')} disabled={loading}>
              {loading ? <Loader2 className="iw-spin" size={14} /> : <Upload size={14} />}
              İçe Aktar
            </button>
          )}
        </div>
      </div>
    </div>
  )
}
