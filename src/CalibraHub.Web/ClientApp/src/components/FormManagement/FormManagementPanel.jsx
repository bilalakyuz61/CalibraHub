/**
 * FormManagementPanel — Form Tasarım Ayarları (Kart Görünümü)
 */
import { useState, useEffect, useCallback } from 'react'
import {
  LayoutGrid, Plus, Pencil, Trash2, RefreshCw,
  Database, Tag, AlertCircle, Loader2, Search,
  CheckCircle, XCircle, Layers, Key
} from 'lucide-react'
import { getForms, deleteForm } from '../../services/formManagementService'
import FormDefinitionModal from './FormDefinitionModal'

export default function FormManagementPanel() {
  var [forms,         setForms]         = useState([])
  var [loading,       setLoading]       = useState(true)
  var [error,         setError]         = useState(null)
  var [searchTerm,    setSearchTerm]    = useState('')
  var [modalOpen,     setModalOpen]     = useState(false)
  var [editForm,      setEditForm]      = useState(null)
  var [deleteId,      setDeleteId]      = useState(null)
  var [deleteLoading, setDeleteLoading] = useState(false)

  var load = useCallback(function () {
    setLoading(true)
    setError(null)
    getForms()
      .then(function (data) { setForms(Array.isArray(data) ? data : []) })
      .catch(function (err) { setError('Formlar yüklenemedi: ' + err.message) })
      .finally(function () { setLoading(false) })
  }, [])

  useEffect(function () { load() }, [load])

  var filtered = forms.filter(function (f) {
    if (!searchTerm.trim()) return true
    var q = searchTerm.toLowerCase()
    return (
      (f.formCode  || '').toLowerCase().includes(q) ||
      (f.formName  || '').toLowerCase().includes(q) ||
      (f.module    || '').toLowerCase().includes(q) ||
      (f.subModule || '').toLowerCase().includes(q) ||
      (f.baseTable || '').toLowerCase().includes(q)
    )
  })

  function handleNew() { setEditForm(null); setModalOpen(true) }
  function handleEdit(f) { setEditForm(f); setModalOpen(true) }
  function handleDeleteRequest(id) { setDeleteId(id) }
  function handleDeleteCancel() { setDeleteId(null) }
  function handleSaved() { setModalOpen(false); load() }

  async function handleDeleteConfirm() {
    if (!deleteId) return
    setDeleteLoading(true)
    var result = await deleteForm(deleteId)
    setDeleteLoading(false)
    setDeleteId(null)
    if (result.success) { load() }
    else { setError('Silme başarısız: ' + (result.message || 'Bilinmeyen hata')) }
  }

  return (
    <div className="gm-root">

      {/* ── Üst bar ── */}
      <div className="gm-topbar">
        <div className="gm-topbar-left">
          <div className="gm-topbar-icon gm-topbar-icon--indigo">
            <LayoutGrid size={20} />
          </div>
          <div>
            <h1 className="gm-title">Form Tasarım Ayarları</h1>
            <p className="gm-subtitle">Form (ekran) kataloğunu yönetin</p>
          </div>
        </div>
        <div className="gm-topbar-right">
          <button type="button" className="gm-btn gm-btn--ghost" onClick={load} disabled={loading} title="Yenile">
            <RefreshCw size={15} className={loading ? 'gm-spin' : ''} />
          </button>
          <button type="button" className="gm-btn gm-btn--indigo" onClick={handleNew} disabled={loading}>
            <Plus size={15} /> Yeni Form
          </button>
        </div>
      </div>

      {/* ── Hata ── */}
      {error && (
        <div className="gm-alert" role="alert">
          <AlertCircle size={15} />
          <span>{error}</span>
          <button type="button" onClick={function () { setError(null) }}>×</button>
        </div>
      )}

      {/* ── Arama ── */}
      <div className="gm-search-wrap">
        <Search size={15} className="gm-search-ico" />
        <input
          type="search"
          className="gm-search"
          placeholder="Form kodu, adı, modül veya tablo ara…"
          value={searchTerm}
          onChange={function (e) { setSearchTerm(e.target.value) }}
        />
        {searchTerm && (
          <button type="button" className="gm-search-clear" onClick={function () { setSearchTerm('') }}>×</button>
        )}
      </div>

      {/* ── İçerik ── */}
      {loading ? (
        <div className="gm-center">
          <Loader2 size={28} className="gm-spin" />
          <span>Formlar yükleniyor…</span>
        </div>
      ) : filtered.length === 0 ? (
        <div className="gm-empty">
          <LayoutGrid size={44} />
          <p>{searchTerm ? 'Sonuç bulunamadı.' : 'Henüz form tanımlanmamış.'}</p>
          {!searchTerm && (
            <button type="button" className="gm-btn gm-btn--indigo" onClick={handleNew}>
              <Plus size={15} /> İlk Formu Ekle
            </button>
          )}
        </div>
      ) : (
        <div className="gm-grid">
          {filtered.map(function (f) {
            return (
              <div key={f.id} className={'gm-card' + (f.isActive ? '' : ' gm-card--inactive')}>

                {/* Üst kısım */}
                <div className="gm-card-header">
                  <div className="gm-card-icon gm-card-icon--indigo">
                    <LayoutGrid size={18} />
                  </div>
                  <div className="gm-card-title-wrap">
                    <div className="gm-card-name">{f.formName}</div>
                    <div className="gm-card-code">{f.formCode}</div>
                  </div>
                  <div className="gm-card-status">
                    {f.isActive
                      ? <span className="gm-status gm-status--on"><CheckCircle size={11} /> Aktif</span>
                      : <span className="gm-status gm-status--off"><XCircle size={11} /> Pasif</span>
                    }
                  </div>
                </div>

                {/* Meta bilgiler */}
                <div className="gm-card-meta">
                  {(f.module || f.subModule) && (
                    <div className="gm-card-row">
                      <Layers size={13} className="gm-card-row-ico" />
                      <span className="gm-badge gm-badge--module">{f.module}{f.subModule ? ' / ' + f.subModule : ''}</span>
                    </div>
                  )}
                  {f.baseTable && (
                    <div className="gm-card-row">
                      <Database size={13} className="gm-card-row-ico" />
                      <span className="gm-badge gm-badge--view">{f.baseTable}</span>
                      {f.baseRecordKey && (
                        <>
                          <Key size={11} className="gm-card-row-ico" style={{marginLeft: 4}} />
                          <span className="gm-badge gm-badge--col">{f.baseRecordKey}</span>
                        </>
                      )}
                    </div>
                  )}
                  <div className="gm-card-row">
                    <Tag size={13} className="gm-card-row-ico" />
                    <span className="gm-badge gm-badge--count">Sıra: {f.sortOrder}</span>
                  </div>
                </div>

                {/* Aksiyon butonları */}
                <div className="gm-card-actions">
                  <button
                    type="button"
                    className="gm-action-btn gm-action-btn--edit"
                    onClick={function () { handleEdit(f) }}
                    title="Düzenle"
                  >
                    <Pencil size={13} /> Düzenle
                  </button>
                  <button
                    type="button"
                    className="gm-action-btn gm-action-btn--delete"
                    onClick={function () { handleDeleteRequest(f.id) }}
                    title="Sil"
                  >
                    <Trash2 size={13} />
                  </button>
                </div>
              </div>
            )
          })}
        </div>
      )}

      {/* ── Sayaç ── */}
      {!loading && forms.length > 0 && (
        <div className="gm-footer">
          {filtered.length} / {forms.length} form
        </div>
      )}

      {/* ── Silme onayı ── */}
      {deleteId && (
        <div className="gm-backdrop" onClick={handleDeleteCancel}>
          <div className="gm-confirm" onClick={function (e) { e.stopPropagation() }}>
            <div className="gm-confirm-ico"><Trash2 size={26} /></div>
            <h3 className="gm-confirm-title">Formu Sil</h3>
            <p className="gm-confirm-text">Bu form tanımı kalıcı olarak silinecek. Devam edilsin mi?</p>
            <div className="gm-confirm-btns">
              <button type="button" className="gm-btn gm-btn--ghost" onClick={handleDeleteCancel} disabled={deleteLoading}>İptal</button>
              <button type="button" className="gm-btn gm-btn--danger" onClick={handleDeleteConfirm} disabled={deleteLoading}>
                {deleteLoading ? <Loader2 size={13} className="gm-spin" /> : <Trash2 size={13} />}
                {deleteLoading ? 'Siliniyor…' : 'Evet, Sil'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* ── Form Modalı ── */}
      <FormDefinitionModal
        isOpen={modalOpen}
        onClose={function () { setModalOpen(false) }}
        onSaved={handleSaved}
        editForm={editForm}
      />
    </div>
  )
}
