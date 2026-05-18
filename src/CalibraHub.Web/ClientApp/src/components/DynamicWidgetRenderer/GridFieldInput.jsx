/**
 * GridFieldInput — Faz E master-detail grid widget.
 *
 * Bir ana form'un icindeki alt tablo (kalem listesi). Card-based UI ile:
 *   - Ust kisimda baslik + "Yeni Satir Ekle" butonu
 *   - Her satir glassmorphism card — ozet kolonlar + hover'da edit/delete
 *   - "Yeni Satir Ekle" → modal acilir → modal'in icinde child form'a ait
 *     DynamicWidgetRenderer mount edilir (form icinde form — Inception).
 *   - Kullanici modal'da alanlari doldurup "Ekle" → getValues() ile satir
 *     degerleri extract edilir, parent (ana form) grid state'ine push.
 *   - Edit & delete satir kart icinde mini butonlarla.
 *
 * Save akisi: bu bilesen kendi API POST'u yapmaz. Tum degisiklikler
 * parent'in (DynamicWidgetRenderer) grids state'ine yansir ve ana form
 * save'inde tek JSON payload olarak backend'e gider.
 *
 * Props:
 *   widgetId        grid widget'in WidgetCode'u (key icin)
 *   label           grid basligi (UI'de gosterilir)
 *   childFormCode   alt form kodu — schema fetch + renderer mount icin
 *   rows            [{ recordId?, values: {...} }]
 *   onRowsChange    rows state'ini update eden callback
 *   classPrefix     'mce' | 'ca' | 'sqe'
 */
import { useState, useEffect } from 'react'
import { createPortal } from 'react-dom'
import { Plus, X, Edit2, Trash2, Table } from 'lucide-react'
import { widgetSchemaByCode } from './dynamicWidgetService'
import DynamicWidgetRenderer from './DynamicWidgetRenderer'

export default function GridFieldInput(props) {
  var widgetId      = props.widgetId
  var label         = props.label || 'Alt Tablo'
  var childFormCode = props.childFormCode || ''
  var rows          = Array.isArray(props.rows) ? props.rows : []
  var onRowsChange  = props.onRowsChange
  var prefix        = props.classPrefix || 'mce'

  var [schema, setSchema]           = useState(null)
  var [schemaError, setSchemaError] = useState(null)
  var [modalOpen, setModalOpen]     = useState(false)
  var [editingIndex, setEditingIndex] = useState(-1)  // -1 = yeni satir
  var [editingValues, setEditingValues] = useState(null)
  var innerRef = null    // DynamicWidgetRenderer ref — satir extract icin

  // ── Child form schema'sini mount'ta cek ──
  // Kolon header'larini bu schema'dan aliriz (grup/grid satirlari haric,
  // sadece gerçek field'lar).
  useEffect(function () {
    if (!childFormCode) return
    var alive = true
    widgetSchemaByCode(childFormCode)
      .then(function (s) {
        if (!alive) return
        if (!s) { setSchemaError('Alt form bulunamadi: ' + childFormCode); return }
        setSchema(s)
      })
      .catch(function (e) {
        if (alive) setSchemaError('Schema yuklenemedi: ' + e.message)
      })
    return function () { alive = false }
  }, [childFormCode])

  // Gosterilecek ozet kolonlar: child form'un ilk aktif field'lari (max 4 kolon)
  var columnWidgets = []
  if (schema && Array.isArray(schema.widgets)) {
    columnWidgets = schema.widgets
      .filter(function (w) {
        if (w.isActive === false) return false
        var dt = String(w.dataType || '').toLowerCase()
        return dt !== 'group' && dt !== 'grid'
      })
      .sort(function (a, b) { return (a.sortOrder || 0) - (b.sortOrder || 0) })
      .slice(0, 4)
  }

  function openAddRow() {
    if (!childFormCode) return
    setEditingIndex(-1)
    setEditingValues(null)
    setModalOpen(true)
  }

  function openEditRow(idx) {
    var r = rows[idx]
    if (!r) return
    setEditingIndex(idx)
    setEditingValues(Object.assign({}, r.values || {}))
    setModalOpen(true)
  }

  async function deleteRow(idx) {
    if (!onRowsChange) return
    // Rapor §6.6 — CalibraAlert.confirm fallback
    var ok = window.CalibraAlert && window.CalibraAlert.confirm
      ? await window.CalibraAlert.confirm('Bu satiri silmek istediginizden emin misiniz?',
          { title: 'Satır Sil', okText: 'Evet, Sil', cancelText: 'Vazgeç', danger: true })
      : window.confirm('Bu satiri silmek istediginizden emin misiniz?')
    if (!ok) return
    var next = rows.filter(function (_, i) { return i !== idx })
    onRowsChange(next)
  }

  function closeModal() {
    setModalOpen(false)
    setEditingIndex(-1)
    setEditingValues(null)
  }

  function confirmModal() {
    if (!innerRef || !onRowsChange) { closeModal(); return }
    var newValues = innerRef.getValues()
    if (editingIndex < 0) {
      // Yeni satir — recordId null (backend save'de uretilir)
      var next = rows.concat([{ recordId: null, values: newValues }])
      onRowsChange(next)
    } else {
      // Mevcut satir update — recordId korunur
      var updated = rows.map(function (r, i) {
        if (i !== editingIndex) return r
        return { recordId: r.recordId, values: newValues }
      })
      onRowsChange(updated)
    }
    closeModal()
  }

  // Ozet hucre degeri — kolon datatype'ina gore format
  function formatCell(val, dt) {
    if (val == null || val === '') return '—'
    var s = Array.isArray(val) ? val.join(', ') : String(val)
    if (s.length > 60) s = s.substring(0, 60) + '…'
    return s
  }

  return (
    <div className={prefix + '-grid-wrap'}>
      {/* Header: baslik + yeni satir butonu */}
      <div className={prefix + '-grid-header'}>
        <div className={prefix + '-grid-header__title'}>
          <Table size={14} strokeWidth={2} />
          <span>{rows.length} kayit</span>
        </div>
        <button
          type="button"
          className={prefix + '-grid-add-btn'}
          onClick={openAddRow}
          disabled={!childFormCode || !schema}
          title={childFormCode ? 'Yeni satir ekle' : 'Alt form tanimli degil'}
        >
          <Plus size={14} strokeWidth={2.4} />
          Yeni Satir Ekle
        </button>
      </div>

      {schemaError && (
        <div className={prefix + '-grid-error'}>{schemaError}</div>
      )}

      {/* Satir listesi — card-based */}
      {rows.length === 0 ? (
        <div className={prefix + '-grid-empty'}>
          <Table size={24} strokeWidth={1.5} />
          <span>Henuz satir eklenmemis</span>
          <small>Ust taraftaki "Yeni Satir Ekle" butonuna basarak baslayin.</small>
        </div>
      ) : (
        <div className={prefix + '-grid-rows'}>
          {rows.map(function (row, idx) {
            return (
              <div key={(row.recordId || 'new') + '_' + idx} className={prefix + '-grid-row'}>
                <div className={prefix + '-grid-row__index'}>{idx + 1}</div>
                <div className={prefix + '-grid-row__cells'}>
                  {columnWidgets.length === 0 ? (
                    <div className={prefix + '-grid-row__cell'}>
                      <span className={prefix + '-grid-row__label'}>—</span>
                      <span className={prefix + '-grid-row__value'}>(Kolon tanimi bekleniyor)</span>
                    </div>
                  ) : (
                    columnWidgets.map(function (cw) {
                      var v = row.values ? row.values[cw.widgetCode] : null
                      return (
                        <div key={cw.widgetCode} className={prefix + '-grid-row__cell'}>
                          <span className={prefix + '-grid-row__label'}>{cw.label}</span>
                          <span className={prefix + '-grid-row__value'}>{formatCell(v, cw.dataType)}</span>
                        </div>
                      )
                    })
                  )}
                </div>
                <div className={prefix + '-grid-row__actions'}>
                  <button
                    type="button"
                    className={prefix + '-grid-row__edit'}
                    onClick={function () { openEditRow(idx) }}
                    title="Duzenle"
                  >
                    <Edit2 size={13} strokeWidth={2} />
                  </button>
                  <button
                    type="button"
                    className={prefix + '-grid-row__delete'}
                    onClick={function () { deleteRow(idx) }}
                    title="Sil"
                  >
                    <Trash2 size={13} strokeWidth={2} />
                  </button>
                </div>
              </div>
            )
          })}
        </div>
      )}

      {/* Satir modal'i (portal) — iç icine child form renderer gomulur */}
      {modalOpen && childFormCode && createPortal(
        <GridRowModal
          prefix={prefix}
          childFormCode={childFormCode}
          title={editingIndex < 0 ? (label + ' — Yeni Satir') : (label + ' — Satir Duzenle')}
          initialValues={editingValues}
          onMounted={function (h) { innerRef = h }}
          onCancel={closeModal}
          onConfirm={confirmModal}
          isEdit={editingIndex >= 0}
        />,
        document.body
      )}
    </div>
  )
}

// ──────────────────────────────────────────────────────────────
// GridRowModal — portal backdrop + child form renderer container
// ──────────────────────────────────────────────────────────────
function GridRowModal(props) {
  var prefix        = props.prefix
  var childFormCode = props.childFormCode
  var title         = props.title
  var initialValues = props.initialValues
  var onMounted     = props.onMounted
  var onCancel      = props.onCancel
  var onConfirm     = props.onConfirm
  var isEdit        = props.isEdit

  // ESC ile kapat
  useEffect(function () {
    function onKey(e) { if (e.key === 'Escape') onCancel() }
    document.addEventListener('keydown', onKey)
    return function () { document.removeEventListener('keydown', onKey) }
  }, [onCancel])

  return (
    <div
      className={prefix + '-grid-modal-backdrop'}
      onClick={function (e) { if (e.target === e.currentTarget) onCancel() }}
    >
      <div className={prefix + '-grid-modal'}>
        <header>
          <Table size={16} strokeWidth={2} />
          <span className={prefix + '-grid-modal__title'}>{title}</span>
          <button type="button" onClick={onCancel} title="Kapat" className={prefix + '-grid-modal__close'}>
            <X size={16} strokeWidth={2.2} />
          </button>
        </header>
        <div className={prefix + '-grid-modal__body'}>
          {/* Inner DynamicWidgetRenderer — child form alanlarini cizer.
              recordId: editing modunda bos (server'a save yapmaz, sadece getValues uzerinden degerleri topluyoruz).
              initialValues: edit modunda mevcut row.values ile onceden doldurulur. */}
          <DynamicWidgetRenderer
            formCode={childFormCode}
            recordId=""
            classPrefix={prefix}
            initialValues={initialValues || null}
            onMounted={function (handle) {
              if (onMounted) onMounted(handle)
            }}
          />
        </div>
        <footer className={prefix + '-grid-modal__footer'}>
          <button type="button" className={prefix + '-grid-modal__cancel'} onClick={onCancel}>
            Iptal
          </button>
          <button type="button" className={prefix + '-grid-modal__confirm'} onClick={onConfirm}>
            {isEdit ? 'Guncelle' : 'Ekle'}
          </button>
        </footer>
      </div>
    </div>
  )
}
