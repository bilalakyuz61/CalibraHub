/**
 * GuideDefinitionModal
 *
 * "Yeni Rehber Ekle" / "Rehber Düzenle" modalı.
 *
 * Akıllı form akışı:
 *   1. Rehber Adı — serbest metin
 *   2. SQL View Kaynağı — cbv_Guide_% view'ları dropdown (API'den gelir)
 *   3. Dinamik Kolonlar — View seçilince otomatik çekilir:
 *        • Değer (ID) Kolonu dropdown
 *        • Gösterim (Label) Kolonu dropdown
 *        • Varsayılan Sıralama dropdown
 *        • Grid Kolonları — checkbox listesi
 *   4. Kaydet → upsertGuide POST
 *
 * Props:
 *   isOpen        — boolean
 *   onClose()     — kapat callback
 *   onSaved()     — başarılı kayıt sonrası callback (listeyi yenile)
 *   editGuide     — null=yeni, {id,guideLabel,viewName,...}=düzenle
 */
import { useState, useEffect, useRef } from 'react'
import { X, BookOpen, Database, Hash, Tag, SortAsc, Table2, Loader2, AlertCircle, Check } from 'lucide-react'
import { listGuideViews, getViewColumns, upsertGuide } from '../../services/guideManagementService'

// ─── Yardımcı: basit focus trap (modal dışına Tab çıkmasını engeller) ───
function useFocusTrap(ref, isOpen) {
  useEffect(function () {
    if (!isOpen || !ref.current) return
    var focusable = ref.current.querySelectorAll(
      'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])'
    )
    if (focusable.length === 0) return
    focusable[0].focus()
    function onKey(e) {
      if (e.key !== 'Tab') return
      var first = focusable[0]
      var last = focusable[focusable.length - 1]
      if (e.shiftKey) {
        if (document.activeElement === first) { e.preventDefault(); last.focus() }
      } else {
        if (document.activeElement === last) { e.preventDefault(); first.focus() }
      }
    }
    document.addEventListener('keydown', onKey)
    return function () { document.removeEventListener('keydown', onKey) }
  }, [isOpen, ref])
}

export default function GuideDefinitionModal(props) {
  var isOpen    = props.isOpen
  var onClose   = props.onClose
  var onSaved   = props.onSaved
  var editGuide = props.editGuide || null

  var isEdit = Boolean(editGuide && editGuide.id > 0)

  // ─── Form state ───
  var [guideLabel,        setGuideLabel]        = useState('')
  var [selectedView,      setSelectedView]       = useState('')
  var [valueColumn,       setValueColumn]        = useState('')
  var [displayColumn,     setDisplayColumn]      = useState('')
  var [defaultSortColumn, setDefaultSortColumn]  = useState('')
  var [gridColumns,       setGridColumns]        = useState([])   // seçili kolonlar (checkbox)

  // ─── API state ───
  var [views,             setViews]              = useState([])   // [{viewName, columns}]
  var [viewCols,          setViewCols]           = useState([])   // seçili view'ın kolonları
  var [viewsLoading,      setViewsLoading]       = useState(false)
  var [colsLoading,       setColsLoading]        = useState(false)
  var [saving,            setSaving]             = useState(false)
  var [error,             setError]              = useState(null)
  var [viewsError,        setViewsError]         = useState(null)

  var modalRef = useRef(null)
  useFocusTrap(modalRef, isOpen)

  // ─── Modal açılınca: form doldur + view listesini çek ───
  useEffect(function () {
    if (!isOpen) return
    setError(null)
    setViewsError(null)

    if (isEdit && editGuide) {
      setGuideLabel(editGuide.guideLabel || '')
      setSelectedView(editGuide.viewName || '')
      setValueColumn(editGuide.valueColumn || '')
      setDisplayColumn(editGuide.displayColumn || '')
      setDefaultSortColumn(editGuide.defaultSortColumn || '')
      setGridColumns(Array.isArray(editGuide.columns) ? editGuide.columns : [])
    } else {
      setGuideLabel('')
      setSelectedView('')
      setValueColumn('')
      setDisplayColumn('')
      setDefaultSortColumn('')
      setGridColumns([])
      setViewCols([])
    }

    setViewsLoading(true)
    listGuideViews()
      .then(function (data) {
        setViews(Array.isArray(data) ? data : [])
        setViewsError(null)
      })
      .catch(function (err) {
        setViewsError('View listesi yüklenemedi: ' + err.message)
      })
      .finally(function () { setViewsLoading(false) })
  }, [isOpen, isEdit, editGuide])

  // ─── View seçilince kolonları çek ───
  useEffect(function () {
    if (!selectedView) { setViewCols([]); return }

    // Düzenleme modunda viewName değişmediyse API çağrısı yapma —
    // editGuide.columns'u doğrudan kullan
    if (isEdit && editGuide && editGuide.viewName === selectedView && Array.isArray(editGuide.columns) && editGuide.columns.length > 0) {
      setViewCols(editGuide.columns)
      return
    }

    setColsLoading(true)
    setError(null)
    getViewColumns(selectedView)
      .then(function (cols) {
        var colList = Array.isArray(cols) ? cols : []
        setViewCols(colList)
        // Varsayılan seçimler (yeni form)
        if (!isEdit) {
          setValueColumn(colList[0] || '')
          setDisplayColumn(colList[1] || colList[0] || '')
          setDefaultSortColumn(colList[0] || '')
          setGridColumns(colList)
        }
      })
      .catch(function (err) {
        setError('Kolon listesi alınamadı: ' + err.message)
        setViewCols([])
      })
      .finally(function () { setColsLoading(false) })
  }, [selectedView])

  // ─── Grid kolonları checkbox toggle ───
  function toggleGridCol(col) {
    setGridColumns(function (prev) {
      var exists = prev.indexOf(col) >= 0
      return exists ? prev.filter(function (c) { return c !== col }) : prev.concat([col])
    })
  }

  // ─── Form kaydet ───
  async function handleSubmit(e) {
    e.preventDefault()
    setError(null)

    if (!guideLabel.trim()) { setError('Rehber adı zorunlu.'); return }
    if (!selectedView) { setError('SQL View seçin.'); return }
    if (!valueColumn) { setError('Değer kolonu seçin.'); return }
    if (gridColumns.length === 0) { setError('En az bir grid kolonu seçin.'); return }

    // Gösterim kolonu seçilmemişse Değer kolonu ile aynı kabul et
    var effectiveDisplayColumn = displayColumn || valueColumn

    setSaving(true)
    var result = await upsertGuide({
      id: isEdit ? editGuide.id : 0,
      guideLabel: guideLabel.trim(),
      viewName: selectedView,
      valueColumn,
      displayColumn: effectiveDisplayColumn,
      defaultSortColumn: defaultSortColumn || null,
      gridColumns,
      guideCode: isEdit ? (editGuide.guideCode || null) : null,
    })
    setSaving(false)

    if (!result.success) {
      setError(result.message || 'Kayıt başarısız.')
      return
    }

    if (onSaved) onSaved()
    if (onClose) onClose()
  }

  // ESC tuşu ile kapat
  useEffect(function () {
    if (!isOpen) return
    function onKey(e) { if (e.key === 'Escape' && !saving) onClose && onClose() }
    document.addEventListener('keydown', onKey)
    return function () { document.removeEventListener('keydown', onKey) }
  }, [isOpen, saving, onClose])

  if (!isOpen) return null

  // Düzenleme modunda mevcut view listede yoksa ekstra seçenek olarak ekle
  var viewOptions = views.map(function (v) {
    return { value: v.viewName, label: v.viewName + ' (' + v.schemaName + ')' }
  })
  if (isEdit && selectedView && !viewOptions.find(function (o) { return o.value === selectedView })) {
    viewOptions = [{ value: selectedView, label: selectedView + ' (mevcut)' }].concat(viewOptions)
  }
  var colOptions = viewCols.map(function (c) { return { value: c, label: c } })

  return (
    <div className="guide-modal-backdrop" onClick={function (e) { if (e.target === e.currentTarget && !saving) onClose && onClose() }}>
      <div className="guide-modal" ref={modalRef} role="dialog" aria-modal="true"
           aria-labelledby="guide-modal-title">

        {/* ── Başlık ── */}
        <div className="guide-modal-header">
          <div className="guide-modal-title-row">
            <BookOpen size={20} className="guide-modal-icon" />
            <h2 id="guide-modal-title" className="guide-modal-title">
              {isEdit ? 'Rehber Düzenle' : 'Yeni Rehber Ekle'}
            </h2>
          </div>
          <button
            type="button"
            className="guide-modal-close"
            onClick={onClose}
            disabled={saving}
            aria-label="Kapat"
          >
            <X size={18} />
          </button>
        </div>

        {/* ── Form ── */}
        <form onSubmit={handleSubmit} className="guide-modal-body" noValidate>

          {/* Hata bandı */}
          {error && (
            <div className="guide-modal-error" role="alert">
              <AlertCircle size={16} />
              <span>{error}</span>
            </div>
          )}

          {/* Rehber Adı */}
          <div className="guide-modal-field">
            <label className="guide-modal-label" htmlFor="gm-label">
              <BookOpen size={14} /> Rehber Adı
            </label>
            <input
              id="gm-label"
              type="text"
              className="guide-modal-input"
              placeholder="örn. Cari Hesap Rehberi"
              value={guideLabel}
              onChange={function (e) { setGuideLabel(e.target.value) }}
              disabled={saving}
              autoComplete="off"
              required
            />
          </div>

          {/* SQL View Kaynağı */}
          <div className="guide-modal-field">
            <label className="guide-modal-label" htmlFor="gm-view">
              <Database size={14} /> SQL View Kaynağı
              {viewsLoading && <Loader2 size={13} className="guide-spin" />}
            </label>
            {viewsError
              ? <div className="guide-modal-hint guide-modal-hint--warn">{viewsError}</div>
              : (
                <select
                  id="gm-view"
                  className="guide-modal-select"
                  value={selectedView}
                  onChange={function (e) {
                    setSelectedView(e.target.value)
                    // Kolon seçimleri sıfırla
                    setValueColumn('')
                    setDisplayColumn('')
                    setDefaultSortColumn('')
                    setGridColumns([])
                  }}
                  disabled={saving || viewsLoading}
                  required
                >
                  <option value="">-- cbv_Guide_% view seçin --</option>
                  {viewOptions.map(function (o) {
                    return <option key={o.value} value={o.value}>{o.label}</option>
                  })}
                </select>
              )
            }
          </div>

          {/* Kolon alanları — view seçilince göster */}
          {selectedView && (
            <div className="guide-modal-cols-section">
              {colsLoading
                ? (
                  <div className="guide-modal-loading">
                    <Loader2 size={18} className="guide-spin" />
                    <span>Kolon listesi yükleniyor…</span>
                  </div>
                )
                : viewCols.length === 0
                  ? (
                    <div className="guide-modal-hint guide-modal-hint--warn">
                      Bu view'ın kolonu okunamadı.
                    </div>
                  )
                  : (
                    <>
                      {/* Değer Kolonu */}
                      <div className="guide-modal-field guide-modal-field--half">
                        <label className="guide-modal-label" htmlFor="gm-valcol">
                          <Hash size={14} /> Değer Kolonu
                        </label>
                        <select
                          id="gm-valcol"
                          className="guide-modal-select"
                          value={valueColumn}
                          onChange={function (e) { setValueColumn(e.target.value) }}
                          disabled={saving}
                          required
                        >
                          <option value="">-- Seç --</option>
                          {colOptions.map(function (o) {
                            return <option key={o.value} value={o.value}>{o.label}</option>
                          })}
                        </select>
                        <span className="guide-modal-hint">Veritabanına kaydedilecek alan (örn. kod, ID)</span>
                      </div>

                      {/* Gösterim Kolonu — opsiyonel */}
                      <div className="guide-modal-field guide-modal-field--half">
                        <label className="guide-modal-label" htmlFor="gm-dispcol">
                          <Tag size={14} /> Gösterim Kolonu
                          <span style={{ fontWeight: 400, textTransform: 'none', fontSize: '.7rem', color: '#64748b', marginLeft: 4 }}>(opsiyonel)</span>
                        </label>
                        <select
                          id="gm-dispcol"
                          className="guide-modal-select"
                          value={displayColumn}
                          onChange={function (e) { setDisplayColumn(e.target.value) }}
                          disabled={saving}
                        >
                          <option value="">-- Değer kolonuyla aynı --</option>
                          {colOptions.map(function (o) {
                            return <option key={o.value} value={o.value}>{o.label}</option>
                          })}
                        </select>
                        <span className="guide-modal-hint">Kullanıcıya gösterilecek alan (örn. ad, başlık). Boş = Değer kolonu</span>
                      </div>

                      {/* Varsayılan Sıralama */}
                      <div className="guide-modal-field guide-modal-field--half">
                        <label className="guide-modal-label" htmlFor="gm-sortcol">
                          <SortAsc size={14} /> Varsayılan Sıralama (opsiyonel)
                        </label>
                        <select
                          id="gm-sortcol"
                          className="guide-modal-select"
                          value={defaultSortColumn}
                          onChange={function (e) { setDefaultSortColumn(e.target.value) }}
                          disabled={saving}
                        >
                          <option value="">-- Otomatik --</option>
                          {colOptions.map(function (o) {
                            return <option key={o.value} value={o.value}>{o.label}</option>
                          })}
                        </select>
                      </div>

                      {/* Grid Kolonları — checkbox */}
                      <div className="guide-modal-field">
                        <label className="guide-modal-label">
                          <Table2 size={14} /> Modal Tablosunda Gösterilecek Kolonlar
                        </label>
                        <div className="guide-modal-checkboxes">
                          {viewCols.map(function (col) {
                            var checked = gridColumns.indexOf(col) >= 0
                            return (
                              <label key={col} className={'guide-modal-checkbox-item' + (checked ? ' guide-modal-checkbox-item--checked' : '')}>
                                <input
                                  type="checkbox"
                                  checked={checked}
                                  onChange={function () { toggleGridCol(col) }}
                                  disabled={saving}
                                />
                                <span className="guide-modal-checkbox-indicator">
                                  {checked && <Check size={11} />}
                                </span>
                                <span className="guide-modal-checkbox-label">{col}</span>
                              </label>
                            )
                          })}
                        </div>
                      </div>
                    </>
                  )
              }
            </div>
          )}

          {/* ── Footer ── */}
          <div className="guide-modal-footer">
            <button
              type="button"
              className="guide-modal-btn guide-modal-btn--cancel"
              onClick={onClose}
              disabled={saving}
            >
              İptal
            </button>
            <button
              type="submit"
              className="guide-modal-btn guide-modal-btn--save"
              disabled={saving || colsLoading || viewsLoading}
            >
              {saving
                ? <><Loader2 size={14} className="guide-spin" /> Kaydediliyor…</>
                : <><Check size={14} /> {isEdit ? 'Güncelle' : 'Kaydet'}</>
              }
            </button>
          </div>

        </form>
      </div>
    </div>
  )
}
