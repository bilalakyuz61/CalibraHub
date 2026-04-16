/**
 * GuideManagementPanel — Rehber Merkezi Ana Paneli (Kart Görünümü)
 */
import { useState, useEffect, useCallback } from 'react'
import {
  BookOpen, Plus, Pencil, Trash2, RefreshCw,
  Database, Hash, Tag, Table2, AlertCircle, Loader2, Search, FlaskConical, Link2
} from 'lucide-react'
import { getCatalog, deleteGuide } from '../../services/guideManagementService'
import GuideDefinitionModal from './GuideDefinitionModal'
import GuideTryModal from './GuideTryModal'
import GuideFieldMappingModal from './GuideFieldMappingModal'

export default function GuideManagementPanel() {
  var [guides,        setGuides]        = useState([])
  var [loading,       setLoading]       = useState(true)
  var [error,         setError]         = useState(null)
  var [searchTerm,    setSearchTerm]    = useState('')
  var [modalOpen,     setModalOpen]     = useState(false)
  var [editGuide,     setEditGuide]     = useState(null)
  var [deleteId,      setDeleteId]      = useState(null)
  var [deleteLoading, setDeleteLoading] = useState(false)
  var [tryGuide,      setTryGuide]      = useState(null)   // GuideTryModal için
  var [mapGuide,      setMapGuide]      = useState(null)   // GuideFieldMappingModal için

  var load = useCallback(function () {
    setLoading(true)
    setError(null)
    getCatalog()
      .then(function (data) { setGuides(Array.isArray(data) ? data : []) })
      .catch(function (err) { setError('Rehberler yüklenemedi: ' + err.message) })
      .finally(function () { setLoading(false) })
  }, [])

  useEffect(function () { load() }, [load])

  var filtered = guides.filter(function (g) {
    if (!searchTerm.trim()) return true
    var q = searchTerm.toLowerCase()
    return (
      (g.guideLabel || '').toLowerCase().includes(q) ||
      (g.guideCode  || '').toLowerCase().includes(q) ||
      (g.viewName   || '').toLowerCase().includes(q)
    )
  })

  function handleNew() { setEditGuide(null); setModalOpen(true) }
  function handleEdit(g) { setEditGuide(g); setModalOpen(true) }
  function handleTry(g) { setTryGuide(g) }
  function handleMap(g) { setMapGuide(g) }
  function handleDeleteRequest(id) { setDeleteId(id) }
  function handleDeleteCancel() { setDeleteId(null) }
  function handleSaved() { setModalOpen(false); load() }

  async function handleDeleteConfirm() {
    if (!deleteId) return
    setDeleteLoading(true)
    var result = await deleteGuide(deleteId)
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
          <div className="gm-topbar-icon">
            <BookOpen size={20} />
          </div>
          <div>
            <h1 className="gm-title">Rehber Merkezi</h1>
            <p className="gm-subtitle">SQL View tabanlı arama rehberlerini yönetin</p>
          </div>
        </div>
        <div className="gm-topbar-right">
          <button type="button" className="gm-btn gm-btn--ghost" onClick={load} disabled={loading} title="Yenile">
            <RefreshCw size={15} className={loading ? 'gm-spin' : ''} />
          </button>
          <button type="button" className="gm-btn gm-btn--primary" onClick={handleNew} disabled={loading}>
            <Plus size={15} /> Yeni Rehber
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
          placeholder="Rehber adı, kod veya view ara…"
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
          <span>Rehberler yükleniyor…</span>
        </div>
      ) : filtered.length === 0 ? (
        <div className="gm-empty">
          <BookOpen size={44} />
          <p>{searchTerm ? 'Sonuç bulunamadı.' : 'Henüz rehber tanımlanmamış.'}</p>
          {!searchTerm && (
            <button type="button" className="gm-btn gm-btn--primary" onClick={handleNew}>
              <Plus size={15} /> İlk Rehberi Ekle
            </button>
          )}
        </div>
      ) : (
        <div className="gm-grid">
          {filtered.map(function (g) {
            var colCount = Array.isArray(g.columns) ? g.columns.length : 0
            return (
              <div key={g.id} className="gm-card">
                {/* Kart üst kısmı */}
                <div className="gm-card-header">
                  <div className="gm-card-icon">
                    <BookOpen size={18} />
                  </div>
                  <div className="gm-card-title-wrap">
                    <div className="gm-card-name">{g.guideLabel}</div>
                    <div className="gm-card-code">{g.guideCode}</div>
                  </div>
                </div>

                {/* Meta bilgiler */}
                <div className="gm-card-meta">
                  <div className="gm-card-row">
                    <Database size={13} className="gm-card-row-ico" />
                    <span className="gm-badge gm-badge--view">{g.viewName || '—'}</span>
                  </div>
                  <div className="gm-card-row">
                    <Hash size={13} className="gm-card-row-ico" />
                    <span className="gm-badge gm-badge--col">{g.valueColumn}</span>
                    <span className="gm-card-sep">→</span>
                    <Tag size={13} className="gm-card-row-ico" />
                    <span className="gm-badge gm-badge--col">{g.displayColumn}</span>
                  </div>
                  <div className="gm-card-row">
                    <Table2 size={13} className="gm-card-row-ico" />
                    <span className="gm-badge gm-badge--count">{colCount} kolon</span>
                  </div>
                </div>

                {/* Aksiyon butonları */}
                <div className="gm-card-actions">
                  <button
                    type="button"
                    className="gm-action-btn gm-action-btn--try"
                    onClick={function () { handleTry(g) }}
                    title="Rehberi canlı test et"
                  >
                    <FlaskConical size={13} /> Dene
                  </button>
                  <button
                    type="button"
                    className="gm-action-btn gm-action-btn--map"
                    onClick={function () { handleMap(g) }}
                    title="Sabit alan eslestirme"
                  >
                    <Link2 size={13} /> Esle
                  </button>
                  <button
                    type="button"
                    className="gm-action-btn gm-action-btn--edit"
                    onClick={function () { handleEdit(g) }}
                    title="Düzenle"
                  >
                    <Pencil size={13} /> Düzenle
                  </button>
                  <button
                    type="button"
                    className="gm-action-btn gm-action-btn--delete"
                    onClick={function () { handleDeleteRequest(g.id) }}
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
      {!loading && guides.length > 0 && (
        <div className="gm-footer">
          {filtered.length} / {guides.length} rehber
        </div>
      )}

      {/* ── Silme onayı ── */}
      {deleteId && (
        <div className="gm-backdrop" onClick={handleDeleteCancel}>
          <div className="gm-confirm" onClick={function (e) { e.stopPropagation() }}>
            <div className="gm-confirm-ico"><Trash2 size={26} /></div>
            <h3 className="gm-confirm-title">Rehberi Sil</h3>
            <p className="gm-confirm-text">Bu rehber tanımı devre dışı bırakılacak. Devam edilsin mi?</p>
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
      <GuideDefinitionModal
        isOpen={modalOpen}
        onClose={function () { setModalOpen(false) }}
        onSaved={handleSaved}
        editGuide={editGuide}
      />

      {/* ── Dene Modalı ── */}
      <GuideTryModal
        isOpen={tryGuide != null}
        onClose={function () { setTryGuide(null) }}
        guide={tryGuide}
      />

      {/* ── Eslestirme Modalı ── */}
      <GuideFieldMappingModal
        isOpen={mapGuide != null}
        onClose={function () { setMapGuide(null) }}
        guide={mapGuide}
      />
    </div>
  )
}
