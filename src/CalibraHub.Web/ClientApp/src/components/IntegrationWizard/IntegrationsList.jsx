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
  Plug, Plus, Search, Play, Edit2, Copy, Trash2, Power, Globe,
  AlertTriangle, RefreshCw, ArrowRight, Check, X, Loader2,
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
  const [runningId, setRunningId] = useState(null)

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

  const handleRun = useCallback(async (item) => {
    setRunningId(item.id)
    try {
      const r = await fetch(`/Integration/Run/${item.id}`, {
        method: 'POST', credentials: 'same-origin',
        headers: { RequestVerificationToken: getCsrf() },
      })
      const d = await r.json()
      if (d.success) toast(`✓ Aktarıldı (HTTP ${d.statusCode})`, 'ok')
      else toast(`✗ ${d.error || 'Hata'}`, 'err')
      refresh()
    } catch (e) {
      toast('Sunucu hatası: ' + e.message, 'err')
    } finally {
      setRunningId(null)
    }
  }, [refresh])

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
            {/* Aksiyonlar sol basta — Kaydet/Sil/Calistir/Toggle/Kopyala/Duzenle */}
            <div className="il-actions il-actions--leading">
              <button className="il-act il-act-run" title="Şimdi çalıştır"
                      onClick={() => handleRun(item)}
                      disabled={runningId === item.id || !item.isActive}>
                {runningId === item.id
                  ? <Loader2 className="iw-spin" size={14} />
                  : <Play size={14} />}
              </button>
              <button className="il-act il-act-edit" title="Düzenle"
                      onClick={() => window.location.href = wizardEdit(item.id)}>
                <Edit2 size={14} />
              </button>
              <button className="il-act" title="Kopyala"
                      onClick={() => handleDuplicate(item)}>
                <Copy size={14} />
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
              <span>{item.sourceFormLabel || item.sourceFormCode}</span>
              <span><ArrowRight size={11} style={{ opacity: 0.5, verticalAlign: 'middle' }} /> {item.endpointName} ({item.apiProfileName})</span>
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
    </div>
  )
}
