/**
 * AdminWidgetRegistryPanel — Admin widget yonetim ekrani (Faz B)
 *
 * Faz A'da kurulan EAV tablolarini (WidgetMas + WidgetTra) yoneten yeni
 * arayuz. Eski Razor-inline-JSON akisi yerine /api/widgets REST endpoint'lerine
 * baglanir. Sayfa reload olmadan form (modul) arasi gecis yapabilir.
 *
 * Mount config (C# ViewSettings.cshtml'den gelen minimal inline JSON):
 *   {
 *     initialFormCode: 'ITEMS',       // opsiyonel — baslangic formu
 *     useApiMode: true                // Faz B flag (her zaman true)
 *   }
 *
 * Layer kavrami Faz B'de yok — her katman (sales_quote_header, sales_quote_line)
 * ayri FormMas kaydidir. Grup hiyerarsisi WidgetMas.ParentId self-FK ile kurulur:
 * DataType='group' olan satirlar root, field'lar ParentId ile onlara baglanir.
 */
import { useState, useCallback, useEffect } from 'react'
import { motion, AnimatePresence } from 'framer-motion'
import { CheckCircle, XCircle, Loader2, Search, X, Trash2, Plus } from 'lucide-react'
import ModuleSelector from './ModuleSelector'
import WidgetBuilderForm from './WidgetBuilderForm'
import WidgetRegistryList from './WidgetRegistryList'
import AdminMiniModal from './AdminMiniModal'
import GroupModal from './GroupModal'
import {
  listForms as listFormsApi,
  getSchema as getSchemaApi,
  upsertWidget as upsertWidgetApi,
  deleteWidget as deleteWidgetApi,
} from './adminRegistryService'

export default function AdminWidgetRegistryPanel(props) {
  var initialFormCode = props.initialFormCode || props.screenCode || 'ITEMS'

  // ── State ─────────────────────────────────
  var [forms, setForms]                 = useState([])      // whitelist form katalogu
  var [currentFormId, setCurrentFormId] = useState(null)
  var [currentFormCode, setCurrentFormCode] = useState(initialFormCode)
  var [widgets, setWidgets]             = useState([])      // tum widget'lar (group + field karma)
  var [editingField, setEditingField]   = useState(null)
  var [savingGlobal, setSavingGlobal]   = useState(false)
  var [savingId, setSavingId]           = useState(null)
  var [loadingSchema, setLoadingSchema] = useState(true)
  var [toast, setToast]                 = useState(null)
  var [searchQuery, setSearchQuery]     = useState('')
  var [pendingDelete, setPendingDelete] = useState(null)   // { id, label } — silme onay modalı
  var [groupModalOpen, setGroupModalOpen] = useState(false)

  function showToast(type, message) {
    setToast({ type: type, message: message })
    setTimeout(function() { setToast(null) }, 3200)
  }

  // ── Initial load: forms + initial schema ──
  useEffect(function() {
    var cancelled = false
    async function boot() {
      try {
        var formList = await listFormsApi()
        if (cancelled) return
        setForms(formList)

        // initialFormCode listede yoksa ilk form'a fallback
        var startForm = formList.find(function(f) { return f.formCode === initialFormCode })
        if (!startForm && formList.length > 0) startForm = formList[0]
        if (!startForm) {
          setLoadingSchema(false)
          return
        }
        await loadSchemaFor(startForm.formCode, cancelled)
      } catch (e) {
        if (!cancelled) {
          showToast('error', 'Formlar yuklenemedi: ' + e.message)
          setLoadingSchema(false)
        }
      }
    }
    boot()
    return function() { cancelled = true }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  async function loadSchemaFor(formCode, cancelledFlag) {
    setLoadingSchema(true)
    try {
      var schema = await getSchemaApi(formCode)
      if (cancelledFlag) return
      setCurrentFormId(schema.formId)
      setCurrentFormCode(schema.formCode)
      setWidgets(Array.isArray(schema.widgets) ? schema.widgets : [])
      setEditingField(null)
    } catch (e) {
      showToast('error', 'Schema yuklenemedi: ' + e.message)
    } finally {
      setLoadingSchema(false)
    }
  }

  /* ── Form (modul) degistirme — page reload YOK ── */
  var handleFormChange = useCallback(function(newFormCode) {
    if (!newFormCode || newFormCode === currentFormCode) return
    loadSchemaFor(newFormCode, false)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [currentFormCode])

  /* ── Submit (create / update widget) ────────── */
  var handleSubmit = useCallback(async function(payload) {
    if (!currentFormId) return
    setSavingGlobal(true)
    try {
      // Yeni widget icin sortOrder hesapla (en sona ekle)
      var maxSort = widgets.reduce(function(m, w) {
        return (w.sortOrder || 0) > m ? (w.sortOrder || 0) : m
      }, 0)

      var apiPayload = {
        id: payload.id || null,
        formId: currentFormId,
        parentId: payload.parentId != null ? payload.parentId : null,
        widgetCode: payload.widgetCode,
        label: payload.label,
        dataType: payload.dataType,
        maxLength:      payload.maxLength      != null ? payload.maxLength      : null,
        minLength:      payload.minLength      != null ? payload.minLength      : null,
        expectedLength: payload.expectedLength != null ? payload.expectedLength : null,
        minValue:       payload.minValue       != null ? payload.minValue       : null,
        maxValue:       payload.maxValue       != null ? payload.maxValue       : null,
        sortOrder: payload.sortOrder != null ? payload.sortOrder : (payload.id ? 0 : (maxSort + 10)),
        options: payload.options || null,
        isActive: payload.isActive !== false,
        isPlainField: payload.isPlainField || false,
        isRequired: payload.isRequired === true,
        rules: payload.rules || null,     // Faz G — kural & formul JSON objesi
      }

      var result = await upsertWidgetApi(apiPayload)
      if (!result.success) {
        showToast('error', 'Kaydedilemedi: ' + (result.message || 'bilinmeyen hata'))
        return
      }

      // Schema'yi yeniden cek (id generated + server validation sonuclari icin)
      await loadSchemaFor(currentFormCode, false)
      try { localStorage.setItem('calibra:widget-schema-changed', String(Date.now())) } catch(_) {}
      showToast('success', payload.id ? 'Widget guncellendi' : 'Yeni widget eklendi')
    } catch (e) {
      showToast('error', 'Hata: ' + e.message)
    } finally {
      setSavingGlobal(false)
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [currentFormId, currentFormCode, widgets])

  // lookup → metadata.guideCode, grid → metadata.childFormCode olarak saklanir;
  // options dizisi null gelir. Upsert payload'u icin dogru options'i cikar.
  function resolveWidgetOptions(widget) {
    var dt = String(widget.dataType || '').toLowerCase()
    if (dt === 'lookup') {
      var gc = widget.metadata && widget.metadata.guideCode
      return gc ? [gc] : widget.options || null
    }
    if (dt === 'grid') {
      var cfc = widget.metadata && widget.metadata.childFormCode
      return cfc ? [cfc] : widget.options || null
    }
    return widget.options || null
  }

  /* ── Toggle aktif/pasif ─────────────────────── */
  var handleToggle = useCallback(async function(widget) {
    var wid = widget.id
    setSavingId(wid)
    try {
      var nextActive = !(widget.isActive !== false)
      var result = await upsertWidgetApi({
        id: wid,
        formId: currentFormId,
        parentId: widget.parentId != null ? widget.parentId : null,
        widgetCode: widget.widgetCode,
        label: widget.label,
        dataType: widget.dataType,
        maxLength: widget.maxLength != null ? widget.maxLength : null,
        minLength: widget.minLength != null ? widget.minLength : null,
        expectedLength: widget.expectedLength != null ? widget.expectedLength : null,
        minValue: widget.minValue != null ? widget.minValue : null,
        maxValue: widget.maxValue != null ? widget.maxValue : null,
        sortOrder: widget.sortOrder || 0,
        options: resolveWidgetOptions(widget),
        isActive: nextActive,
        isPlainField: widget.isPlainField === true,
        isRequired: widget.isRequired === true,
        rules: widget.rules || null,     // Faz G — mevcut kurallari koru (toggle sadece aktif/pasif)
      })
      if (!result.success) {
        showToast('error', 'Durum degistirilemedi: ' + (result.message || ''))
        return
      }
      setWidgets(function(prev) {
        return prev.map(function(w) {
          return w.id === wid ? Object.assign({}, w, { isActive: nextActive }) : w
        })
      })
      try { localStorage.setItem('calibra:widget-schema-changed', String(Date.now())) } catch(_) {}
      showToast('success', nextActive ? 'Widget aktif edildi' : 'Widget pasife alindi')
    } catch (e) {
      showToast('error', 'Hata: ' + e.message)
    } finally {
      setSavingId(null)
    }
  }, [currentFormId])

  /* ── Sil — onay modalini ac ── */
  var handleDelete = useCallback(function(widget) {
    setPendingDelete({ id: widget.id, label: widget.label || widget.widgetCode || 'Widget' })
  }, [])

  /* ── Sil onaylandi — API cagrisi ── */
  var confirmDelete = useCallback(async function() {
    if (!pendingDelete) return
    var wid = pendingDelete.id
    setPendingDelete(null)
    setSavingId(wid)
    try {
      var result = await deleteWidgetApi(wid)
      if (!result.success) {
        showToast('error', 'Silinemedi: ' + (result.message || ''))
        return
      }
      setWidgets(function(prev) { return prev.filter(function(w) { return w.id !== wid }) })
      try { localStorage.setItem('calibra:widget-schema-changed', String(Date.now())) } catch(_) {}
      showToast('success', 'Widget silindi')
    } catch (e) {
      showToast('error', 'Hata: ' + e.message)
    } finally {
      setSavingId(null)
    }
  }, [pendingDelete])

  /* ── Yeni grup olustur ──────────────────────── */
  // GroupModal.onCreated → { groupKey, groupLabel } geliyor.
  // Grup, WidgetMas'ta DataType='group' + ParentId=null kaydi olarak yazilir.
  var handleCreateGroup = useCallback(async function(payload) {
    if (!currentFormId) throw new Error('Form secilmemis')
    var maxSort = widgets.reduce(function(m, w) {
      return (w.sortOrder || 0) > m ? (w.sortOrder || 0) : m
    }, 0)

    var apiPayload = {
      id: null,
      formId: currentFormId,
      parentId: null,
      widgetCode: payload.groupKey,
      label: payload.groupLabel,
      dataType: 'group',
      maxLength: null,
      sortOrder: maxSort + 10,
      options: null,
      isActive: true,
    }

    var result = await upsertWidgetApi(apiPayload)
    if (!result.success) {
      showToast('error', 'Grup kaydedilemedi: ' + (result.message || 'hata'))
      throw new Error(result.message || 'hata')
    }

    // Schema yeniden cek ki gercek id alinsin
    await loadSchemaFor(currentFormCode, false)
    showToast('success', 'Grup olusturuldu: ' + payload.groupLabel)

    // WidgetBuilderForm bu grubu secsin diye dondur — ama yeni id schema reload
    // sonrasi widgets state'inde olur, burada local id uret ve gercek id'yi
    // arayarak WidgetBuilderForm'a dondur.
    return { id: result.id, groupKey: payload.groupKey, groupLabel: payload.groupLabel }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [currentFormId, currentFormCode, widgets])

  /* ── "Sadece Alan" toggle ──────────────────────── */
  var handlePlainFieldToggle = useCallback(async function(widget) {
    var wid = widget.id
    setSavingId(wid)
    try {
      var nextPlain = !(widget.isPlainField === true)
      var result = await upsertWidgetApi({
        id: wid,
        formId: currentFormId,
        parentId: widget.parentId != null ? widget.parentId : null,
        widgetCode: widget.widgetCode,
        label: widget.label,
        dataType: widget.dataType,
        maxLength: widget.maxLength != null ? widget.maxLength : null,
        sortOrder: widget.sortOrder || 0,
        options: resolveWidgetOptions(widget),
        isActive: widget.isActive !== false,
        isPlainField: nextPlain,
        isRequired: widget.isRequired === true,
        rules: widget.rules || null,
      })
      if (!result.success) {
        showToast('error', 'Durum degistirilemedi: ' + (result.message || ''))
        return
      }
      setWidgets(function(prev) {
        return prev.map(function(w) {
          return w.id === wid ? Object.assign({}, w, { isPlainField: nextPlain }) : w
        })
      })
      showToast('success', nextPlain ? '"Düz alan" modu aktif' : '"Gruplu" moda geri döndü')
    } catch (e) {
      showToast('error', 'Hata: ' + e.message)
    } finally {
      setSavingId(null)
    }
  }, [currentFormId])

  /* ── "Listede Göster" toggle — kaldirildi, widget aktifse her yerde gorunur ── */
  var handleListableToggle = useCallback(function() {}, [])

  /* ── Edit ────────────────────────────────────── */
  var handleEdit = useCallback(function(field) {
    setEditingField(field)
    if (typeof window !== 'undefined') {
      window.scrollTo({ top: 0, behavior: 'smooth' })
    }
  }, [])

  var handleCancelEdit = useCallback(function() {
    setEditingField(null)
  }, [])

  var editingId = editingField ? editingField.id : null

  /* ── Reorder — iki widget'in sortOrder'ini takas et ──── */
  var handleReorder = useCallback(async function(fieldA, fieldB) {
    if (!fieldA || !fieldB || !currentFormId) return
    var sortA = fieldA.sortOrder != null ? fieldA.sortOrder : 0
    var sortB = fieldB.sortOrder != null ? fieldB.sortOrder : 0
    // Ayni sortOrder ise birini 1 artir
    if (sortA === sortB) sortB = sortA + 1

    // Optimistic UI update
    setWidgets(function(prev) {
      return prev.map(function(w) {
        if (w.id === fieldA.id) return Object.assign({}, w, { sortOrder: sortB })
        if (w.id === fieldB.id) return Object.assign({}, w, { sortOrder: sortA })
        return w
      })
    })

    // Backend'e her iki widget'i da kaydet
    try {
      await Promise.all([
        upsertWidgetApi(Object.assign({}, fieldA, { formId: currentFormId, sortOrder: sortB })),
        upsertWidgetApi(Object.assign({}, fieldB, { formId: currentFormId, sortOrder: sortA })),
      ])
    } catch (e) {
      console.error('[Reorder] error:', e)
      // Hata olursa geri al
      setWidgets(function(prev) {
        return prev.map(function(w) {
          if (w.id === fieldA.id) return Object.assign({}, w, { sortOrder: sortA })
          if (w.id === fieldB.id) return Object.assign({}, w, { sortOrder: sortB })
          return w
        })
      })
    }
  }, [currentFormId])

  // ── Widgets'i group + field olarak bucket'la ──
  // WidgetRegistryList ve GroupSelector icin derived state:
  //   groups: widgets.filter(dataType='group')  → { id, groupKey, groupLabel, sortOrder }
  //   fields: widgets.filter(dataType!='group') → { id, widgetCode, label, dataType, parentId, ... }
  var derivedGroups = widgets
    .filter(function(w) { return String(w.dataType || '').toLowerCase() === 'group' })
    .map(function(w) {
      return {
        id: w.id,
        groupKey: w.widgetCode,
        groupLabel: w.label,
        sortOrder: w.sortOrder,
        displayOrder: w.sortOrder,  // legacy alias
        isActive: w.isActive,
      }
    })

  var derivedFields = widgets
    .filter(function(w) { return String(w.dataType || '').toLowerCase() !== 'group' })

  // ── Forms → ModuleSelector props
  var moduleSelectorOptions = forms.map(function(f) {
    return {
      value: f.formCode,
      label: f.formName,
      selected: f.formCode === currentFormCode,
    }
  })

  return (
    <div className="h-full flex flex-col bg-[#f8fafc] dark:bg-[#0a0d17] overflow-hidden rounded-[inherit]">

      {/* ── Modul Selector (whitelist formlari) ──────────────────── */}
      <ModuleSelector
        options={moduleSelectorOptions}
        selectedCode={currentFormCode}
        onChange={handleFormChange}
        trailing={
          <div className="flex items-center gap-2">
            <div className="flex items-center gap-2 flex-1 px-3 py-2 rounded-xl bg-white/60 dark:bg-white/[0.04] border border-slate-200 dark:border-white/[0.08]">
              <Search size={14} className="text-slate-400 dark:text-white/30 flex-shrink-0" />
              <input
                type="text"
                value={searchQuery}
                onChange={function(e) { setSearchQuery(e.target.value) }}
                placeholder="Widget ara…"
                className="flex-1 bg-transparent text-xs text-slate-800 dark:text-white/85 placeholder:text-slate-400 dark:placeholder:text-white/25 focus:outline-none"
              />
              {searchQuery && (
                <button
                  type="button"
                  onClick={function() { setSearchQuery('') }}
                  className="flex-shrink-0 text-slate-400 hover:text-slate-600 dark:text-white/45 dark:hover:text-white/70 transition-colors"
                  title="Aramayı temizle"
                >
                  <X size={13} />
                </button>
              )}
            </div>
            <button
              type="button"
              onClick={function() { setGroupModalOpen(true) }}
              disabled={!currentFormId}
              className="flex items-center gap-1.5 px-3 py-2 rounded-xl bg-indigo-500/10 hover:bg-indigo-500/20 dark:bg-indigo-500/15 dark:hover:bg-indigo-500/25 border border-indigo-400/30 dark:border-indigo-400/35 text-[11px] font-semibold text-indigo-600 dark:text-indigo-300 transition-all flex-shrink-0 disabled:opacity-40 disabled:cursor-not-allowed"
              title="Yeni grup tanımla"
            >
              <Plus size={13} strokeWidth={2.4} />
              Yeni Grup
            </button>
          </div>
        }
      />

      {/* ── Body (2 kolon layout) ───────────── */}
      <div className="flex-1 overflow-hidden min-h-0 px-5 py-4">
        {loadingSchema ? (
          <div className="h-full flex flex-col items-center justify-center gap-3">
            <Loader2 size={28} className="text-indigo-500 animate-spin" />
            <span className="text-[11px] text-slate-500 dark:text-white/40">Yukleniyor...</span>
          </div>
        ) : (
          <div className="h-full grid grid-cols-1 md:grid-cols-[1fr_2fr] gap-4 min-h-0">
            {/* Sol kolon: Form */}
            <div className="overflow-y-auto min-h-0 pr-1">
              <WidgetBuilderForm
                editingField={editingField}
                onSubmit={handleSubmit}
                onCancel={handleCancelEdit}
                saving={savingGlobal}
                groups={derivedGroups}
                existingFields={derivedFields}
                activeLayer={null}
                activeLayerLabel={null}
              />
            </div>

            {/* Sag kolon: Liste */}
            <div className="h-full min-h-0">
              <WidgetRegistryList
                fields={derivedFields}
                groups={derivedGroups}
                onEdit={handleEdit}
                onToggle={handleToggle}
                onDelete={handleDelete}
                onPlainFieldToggle={handlePlainFieldToggle}
                onListableToggle={handleListableToggle}
                onReorder={handleReorder}
                editingId={editingId}
                savingId={savingId}
                searchQuery={searchQuery}
              />
            </div>
          </div>
        )}
      </div>

      {/* ── Yeni Grup Modalı ──────────────── */}
      <GroupModal
        isOpen={groupModalOpen}
        onClose={function() { setGroupModalOpen(false) }}
        onCreated={function(payload) {
          var result = handleCreateGroup(payload)
          if (result && typeof result.then === 'function') {
            result.then(function() { setGroupModalOpen(false) })
                  .catch(function() {}) // hata toast handleCreateGroup icinde gosterilir
          } else {
            setGroupModalOpen(false)
          }
        }}
        saving={savingGlobal}
      />

      {/* ── Silme onay modalı ──────────────── */}
      <AdminMiniModal
        isOpen={!!pendingDelete}
        onClose={function() { setPendingDelete(null) }}
        title="Widget Silinecek"
        subtitle={pendingDelete ? '"' + pendingDelete.label + '" widget\'i ve tüm kayıtlı değerleri kalıcı olarak silinecek.' : ''}
        icon={Trash2}
        iconColor="rose"
        maxWidth="max-w-sm"
        footer={
          <>
            <div className="flex-1" />
            <button
              type="button"
              onClick={function() { setPendingDelete(null) }}
              className="px-4 py-2 rounded-xl bg-white/[0.04] hover:bg-white/[0.08] border border-slate-200 dark:border-white/[0.08] text-xs font-medium text-slate-600 dark:text-white/60 hover:text-slate-900 dark:hover:text-white/85 transition-all"
            >
              İptal
            </button>
            <button
              type="button"
              onClick={confirmDelete}
              className="flex items-center gap-1.5 px-4 py-2 rounded-xl bg-red-500 hover:bg-red-600 border border-red-500 text-xs font-semibold text-white transition-all shadow-sm"
            >
              <Trash2 size={13} strokeWidth={2.4} />
              Sil
            </button>
          </>
        }
      >
        <p className="text-[12px] text-slate-500 dark:text-white/50 leading-relaxed">
          Bu işlem geri alınamaz. Devam etmek istiyor musunuz?
        </p>
      </AdminMiniModal>

      {/* ── Toast bildirimleri ──────────────── */}
      <AnimatePresence>
        {toast && (
          <motion.div
            initial={{ opacity: 0, y: 20, scale: 0.95 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, y: 10, scale: 0.95 }}
            transition={{ type: 'spring', stiffness: 320, damping: 26 }}
            className="fixed bottom-6 right-6 z-[9999] flex items-center gap-3 px-4 py-3 rounded-xl shadow-2xl"
            style={{
              background: toast.type === 'error' ? 'rgba(239,68,68,0.12)' : 'rgba(16,185,129,0.12)',
              backdropFilter: 'blur(24px)',
              WebkitBackdropFilter: 'blur(24px)',
              border: '1px solid ' + (toast.type === 'error' ? 'rgba(239,68,68,0.35)' : 'rgba(16,185,129,0.35)'),
            }}
          >
            {toast.type === 'error'
              ? <XCircle size={16} className="text-red-400" />
              : <CheckCircle size={16} className="text-emerald-400" />}
            <span className={'text-sm font-medium ' + (toast.type === 'error' ? 'text-red-200' : 'text-emerald-200')}>
              {toast.message}
            </span>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  )
}
