/**
 * FormDefinitionModal
 *
 * "Yeni Form Ekle" / "Form Düzenle" modalı.
 *
 * Akıllı form akışı:
 *   1. FormCode — sadece A-Z ve _ karakterleri, otomatik büyük harf
 *   2. FormName — serbest metin
 *   3. Module / SubModule — serbest metin
 *   4. SortOrder — sayı
 *   5. IsActive — toggle
 *   6. BaseTable — veritabanı tablolarından dropdown
 *   7. BaseRecordKey — BaseTable seçilince dinamik dolan dropdown
 *
 * Props:
 *   isOpen      — boolean
 *   onClose()   — kapat callback
 *   onSaved()   — başarılı kayıt sonrası callback
 *   editForm    — null=yeni, {id,formCode,formName,...}=düzenle
 */
import { useState, useEffect, useRef } from 'react'
import { X, LayoutGrid, Database, Tag, Hash, SortAsc, Table2, Loader2, AlertCircle, Check, ToggleLeft, ToggleRight } from 'lucide-react'
import { createForm, updateForm, getTables, getTableColumns } from '../../services/formManagementService'

// ─── FormCode karakter filtresi ───
var FORM_CODE_REGEX = /[^A-Z_]/g

function sanitizeFormCode(value) {
  return value.toUpperCase().replace(FORM_CODE_REGEX, '')
}

// ─── Focus trap yardımcısı ───
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

export default function FormDefinitionModal(props) {
  var isOpen   = props.isOpen
  var onClose  = props.onClose
  var onSaved  = props.onSaved
  var editForm = props.editForm || null

  var isEdit = Boolean(editForm && editForm.id > 0)

  // ─── Form state ───
  var [formCode,      setFormCode]      = useState('')
  var [formName,      setFormName]      = useState('')
  var [module,        setModule]        = useState('')
  var [subModule,     setSubModule]     = useState('')
  var [sortOrder,     setSortOrder]     = useState(0)
  var [isActive,      setIsActive]      = useState(true)
  var [baseTable,     setBaseTable]     = useState('')
  var [baseRecordKey, setBaseRecordKey] = useState('')

  // ─── API state ───
  var [tables,        setTables]        = useState([])
  var [columns,       setColumns]       = useState([])
  var [tablesLoading, setTablesLoading] = useState(false)
  var [colsLoading,   setColsLoading]   = useState(false)
  var [saving,        setSaving]        = useState(false)
  var [error,         setError]         = useState(null)
  var [tablesError,   setTablesError]   = useState(null)

  var modalRef = useRef(null)
  useFocusTrap(modalRef, isOpen)

  // ─── Modal açılınca: form doldur + tablo listesini çek ───
  useEffect(function () {
    if (!isOpen) return
    setError(null)
    setTablesError(null)

    if (isEdit && editForm) {
      setFormCode(editForm.formCode || '')
      setFormName(editForm.formName || '')
      setModule(editForm.module || '')
      setSubModule(editForm.subModule || '')
      setSortOrder(editForm.sortOrder || 0)
      setIsActive(editForm.isActive !== false)
      setBaseTable(editForm.baseTable || '')
      setBaseRecordKey(editForm.baseRecordKey || '')
    } else {
      setFormCode('')
      setFormName('')
      setModule('')
      setSubModule('')
      setSortOrder(0)
      setIsActive(true)
      setBaseTable('')
      setBaseRecordKey('')
      setColumns([])
    }

    setTablesLoading(true)
    getTables()
      .then(function (data) {
        setTables(Array.isArray(data) ? data : [])
        setTablesError(null)
      })
      .catch(function (err) {
        setTablesError('Tablo listesi yüklenemedi: ' + err.message)
      })
      .finally(function () { setTablesLoading(false) })
  }, [isOpen, isEdit, editForm])

  // ─── BaseTable seçilince kolonları çek ───
  useEffect(function () {
    if (!baseTable) { setColumns([]); return }

    setColsLoading(true)
    setError(null)
    getTableColumns(baseTable)
      .then(function (cols) {
        setColumns(Array.isArray(cols) ? cols : [])
      })
      .catch(function (err) {
        setError('Kolon listesi alınamadı: ' + err.message)
        setColumns([])
      })
      .finally(function () { setColsLoading(false) })
  }, [baseTable])

  // ─── BaseTable değişince BaseRecordKey sıfırla ───
  function handleBaseTableChange(value) {
    setBaseTable(value)
    setBaseRecordKey('')
    setColumns([])
  }

  // ─── FormCode değişince filtrele ───
  function handleFormCodeChange(e) {
    setFormCode(sanitizeFormCode(e.target.value))
  }

  // ─── Form kaydet ───
  async function handleSubmit(e) {
    e.preventDefault()
    setError(null)

    if (!formCode.trim()) { setError('FormCode zorunludur.'); return }
    if (!formName.trim()) { setError('Form adı zorunludur.'); return }

    var payload = {
      id: isEdit ? editForm.id : 0,
      formCode: formCode.trim(),
      formName: formName.trim(),
      module: module.trim() || null,
      subModule: subModule.trim() || null,
      sortOrder: Number(sortOrder) || 0,
      isActive: isActive,
      baseTable: baseTable || null,
      baseRecordKey: baseRecordKey || null,
    }

    setSaving(true)
    var result
    if (isEdit) {
      result = await updateForm(editForm.id, payload)
    } else {
      result = await createForm(payload)
    }
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

  return (
    <div
      className="form-modal-backdrop"
      onClick={function (e) { if (e.target === e.currentTarget && !saving) onClose && onClose() }}
    >
      <div
        className="form-modal"
        ref={modalRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="form-modal-title"
      >

        {/* ── Başlık ── */}
        <div className="form-modal-header">
          <div className="form-modal-title-row">
            <LayoutGrid size={20} className="form-modal-icon" />
            <h2 id="form-modal-title" className="form-modal-title">
              {isEdit ? 'Form Düzenle' : 'Yeni Form Ekle'}
            </h2>
          </div>
          <button
            type="button"
            className="form-modal-close"
            onClick={onClose}
            disabled={saving}
            aria-label="Kapat"
          >
            <X size={18} />
          </button>
        </div>

        {/* ── Form ── */}
        <form onSubmit={handleSubmit} className="form-modal-body" noValidate>

          {/* Hata bandı */}
          {error && (
            <div className="form-modal-error" role="alert">
              <AlertCircle size={16} />
              <span>{error}</span>
            </div>
          )}

          {/* Üst satır: FormCode + FormName */}
          <div className="form-modal-cols-section">
            <div className="form-modal-field form-modal-field--half">
              <label className="form-modal-label" htmlFor="fm-code">
                <Hash size={14} /> Form Kodu
              </label>
              <input
                id="fm-code"
                type="text"
                className="form-modal-input form-modal-input--mono"
                placeholder="örn. SALES_QUOTE"
                value={formCode}
                onChange={handleFormCodeChange}
                disabled={saving || isEdit}
                autoComplete="off"
                required
                maxLength={60}
              />
              <span className="form-modal-hint">Sadece A-Z ve _ karakterleri</span>
            </div>

            <div className="form-modal-field form-modal-field--half">
              <label className="form-modal-label" htmlFor="fm-name">
                <Tag size={14} /> Form Adı
              </label>
              <input
                id="fm-name"
                type="text"
                className="form-modal-input"
                placeholder="örn. Satış Teklifi"
                value={formName}
                onChange={function (e) { setFormName(e.target.value) }}
                disabled={saving}
                autoComplete="off"
                required
                maxLength={200}
              />
            </div>
          </div>

          {/* Modül + Alt Modül */}
          <div className="form-modal-cols-section">
            <div className="form-modal-field form-modal-field--half">
              <label className="form-modal-label" htmlFor="fm-module">
                <LayoutGrid size={14} /> Modül
              </label>
              <input
                id="fm-module"
                type="text"
                className="form-modal-input"
                placeholder="örn. Satış"
                value={module}
                onChange={function (e) { setModule(e.target.value) }}
                disabled={saving}
                autoComplete="off"
                maxLength={100}
              />
            </div>

            <div className="form-modal-field form-modal-field--half">
              <label className="form-modal-label" htmlFor="fm-submodule">
                <LayoutGrid size={14} /> Alt Modül
              </label>
              <input
                id="fm-submodule"
                type="text"
                className="form-modal-input"
                placeholder="örn. Teklifler"
                value={subModule}
                onChange={function (e) { setSubModule(e.target.value) }}
                disabled={saving}
                autoComplete="off"
                maxLength={100}
              />
            </div>
          </div>

          {/* SortOrder + IsActive */}
          <div className="form-modal-cols-section">
            <div className="form-modal-field form-modal-field--half">
              <label className="form-modal-label" htmlFor="fm-sort">
                <SortAsc size={14} /> Sıralama
              </label>
              <input
                id="fm-sort"
                type="number"
                className="form-modal-input"
                value={sortOrder}
                onChange={function (e) { setSortOrder(e.target.value) }}
                disabled={saving}
                min={0}
              />
            </div>

            <div className="form-modal-field form-modal-field--half">
              <label className="form-modal-label">
                Aktif Durum
              </label>
              <button
                type="button"
                className={'form-modal-toggle' + (isActive ? ' form-modal-toggle--on' : '')}
                onClick={function () { setIsActive(function (prev) { return !prev }) }}
                disabled={saving}
                aria-pressed={isActive}
              >
                {isActive
                  ? <><ToggleRight size={20} /> Aktif</>
                  : <><ToggleLeft size={20} /> Pasif</>
                }
              </button>
            </div>
          </div>

          {/* BaseTable */}
          <div className="form-modal-field">
            <label className="form-modal-label" htmlFor="fm-table">
              <Database size={14} /> Fiziksel Tablo (BaseTable)
              {tablesLoading && <Loader2 size={13} className="form-spin" />}
            </label>
            {tablesError
              ? <div className="form-modal-hint form-modal-hint--warn">{tablesError}</div>
              : (
                <select
                  id="fm-table"
                  className="form-modal-select"
                  value={baseTable}
                  onChange={function (e) { handleBaseTableChange(e.target.value) }}
                  disabled={saving || tablesLoading}
                >
                  <option value="">-- Tablo seçin (opsiyonel) --</option>
                  {tables.map(function (t) {
                    return (
                      <option key={t.fullName} value={t.fullName}>
                        {t.fullName}
                      </option>
                    )
                  })}
                </select>
              )
            }
            <span className="form-modal-hint">Flat view oluşturmak için fiziksel tablo seçin</span>
          </div>

          {/* BaseRecordKey — BaseTable seçilince göster */}
          {baseTable && (
            <div className="form-modal-field">
              <label className="form-modal-label" htmlFor="fm-reckey">
                <Hash size={14} /> Kayıt Anahtarı (BaseRecordKey)
                {colsLoading && <Loader2 size={13} className="form-spin" />}
              </label>
              {colsLoading
                ? (
                  <div className="form-modal-loading">
                    <Loader2 size={18} className="form-spin" />
                    <span>Kolon listesi yükleniyor…</span>
                  </div>
                )
                : columns.length === 0
                  ? (
                    <div className="form-modal-hint form-modal-hint--warn">
                      Bu tablonun kolonları okunamadı.
                    </div>
                  )
                  : (
                    <select
                      id="fm-reckey"
                      className="form-modal-select"
                      value={baseRecordKey}
                      onChange={function (e) { setBaseRecordKey(e.target.value) }}
                      disabled={saving}
                    >
                      <option value="">-- PK kolonu seçin --</option>
                      {columns.map(function (col) {
                        return <option key={col} value={col}>{col}</option>
                      })}
                    </select>
                  )
              }
              <span className="form-modal-hint">WidgetTra.RecordId ile eşleşen PK kolonu</span>
            </div>
          )}

          {/* ── Footer ── */}
          <div className="form-modal-footer">
            <button
              type="button"
              className="form-modal-btn form-modal-btn--cancel"
              onClick={onClose}
              disabled={saving}
            >
              İptal
            </button>
            <button
              type="submit"
              className="form-modal-btn form-modal-btn--save"
              disabled={saving || colsLoading || tablesLoading}
            >
              {saving
                ? <><Loader2 size={14} className="form-spin" /> Kaydediliyor…</>
                : <><Check size={14} /> {isEdit ? 'Güncelle' : 'Kaydet'}</>
              }
            </button>
          </div>

        </form>
      </div>
    </div>
  )
}
