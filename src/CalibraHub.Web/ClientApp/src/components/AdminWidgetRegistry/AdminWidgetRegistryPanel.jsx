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
import { useState, useCallback, useEffect, useRef } from 'react'
import { motion, AnimatePresence } from 'framer-motion'
import { CheckCircle, XCircle, Loader2, Search, X, Trash2, Plus, Download, Upload } from 'lucide-react'
import { resolveIcon, resolveColor } from '../CalibraSmartBoard/DynamicWidgetFactory'
import WidgetBuilderForm from './WidgetBuilderForm'
import WidgetRegistryList from './WidgetRegistryList'
import AdminMiniModal from './AdminMiniModal'
import GroupModal from './GroupModal'
import { buildEntitiesFromForms, findEntityByFormCodeInForms, getDefaultFormCode } from './entityRegistry'
import {
  listForms as listFormsApi,
  getSchema as getSchemaApi,
  upsertWidget as upsertWidgetApi,
  deleteWidget as deleteWidgetApi,
  patchSortOrders as patchSortOrdersApi,
  patchWidgetActive as patchWidgetActiveApi,
  patchIsPlainField as patchIsPlainFieldApi,
} from './adminRegistryService'
import { discoverFields as discoverFieldsApi, getFieldsByForm as getFieldsByFormApi } from '../../services/fieldSettingService'

// Form hiyerarsisi — bir form tanimi yapilirken hangi ust formlarin widget'lari
// kullanilabilir? Ornek: Satis Teklifi Kalem Bilgisi (lines) icinde hem kendi
// alanlarini hem de Ust Bilgi (header) alanlarini rule/formul'de kullanabiliriz.
// Ust bilgide ise sadece kendi alanlari gorunur (alt alanlar henuz yok).
// Scope: parent → child tek yon (child, parent alanlarini gorebilir).
var FORM_PARENTS = {
  SALES_QUOTE_LINES: ['SALES_QUOTE_EDIT'],
  // Ileride: INVOICE_LINES → INVOICE_EDIT, PO_LINES → PO_EDIT, ...
}

export default function AdminWidgetRegistryPanel(props) {
  // Fallback MATERIAL_CARD_EDIT — ITEMS formu artik seed edilmiyor (2026-07-06);
  // liste'de bulunamazsa boot() zaten formList[0]'a duser.
  var initialFormCode = props.initialFormCode || props.screenCode || 'MATERIAL_CARD_EDIT'

  // ── State ─────────────────────────────────
  var [forms, setForms]                 = useState([])      // whitelist form katalogu
  var [currentFormId, setCurrentFormId] = useState(null)
  var [currentFormCode, setCurrentFormCode] = useState(initialFormCode)
  // Aktif entity — variant secimi (Ust Bilgi / Kalem Bilgisi) icin. formCode'a
  // bagli olarak findEntityByFormCodeInForms ile turetilir (forms state'ten, DB-driven).
  // Variant'siz entity'lerde sadece dropdown gosterimi icin kullanilir; segmented control gizlenir.
  // İlk yükleme sırasında forms henüz boş, boot() sonrası set edilir.
  var [currentEntity, setCurrentEntity] = useState(null)
  var [widgets, setWidgets]             = useState([])      // tum widget'lar (group + field karma)
  var [parentFormWidgets, setParentFormWidgets] = useState([])  // ust form widget'lari (rule/formul icin)
  // Form sabit alanlari — INFORMATION_SCHEMA'dan kesfedilen DB tablo kolonlari.
  // Tip 2 rehberlerde "Form Alani Ekle" listesinde bunlar da gorunsun ki admin
  // ornek "WHERE [ItemCode] = '{#item_code}'" gibi kisitlar yazabilsin.
  var [formStaticFields, setFormStaticFields] = useState([])
  var [editingField, setEditingField]   = useState(null)
  var [savingGlobal, setSavingGlobal]   = useState(false)
  var [savingId, setSavingId]           = useState(null)
  var [loadingSchema, setLoadingSchema] = useState(true)
  var [toast, setToast]                 = useState(null)
  var [searchQuery, setSearchQuery]     = useState('')
  var [pendingDelete, setPendingDelete] = useState(null)   // { id, label } — silme onay modalı
  var [groupModalOpen, setGroupModalOpen] = useState(false)
  // Tanim transport — { package, fileName } dolu ise onay modalı acik
  var [importPending, setImportPending] = useState(null)
  var [importing, setImporting] = useState(false)
  var importFileRef = useRef(null)

  // Sol sidebar arama
  var [sidebarSearch, setSidebarSearch] = useState('')

  // Tema tespiti — sidebar + sağ panel header renk hesaplamaları için
  var [isLight, setIsLight] = useState(function() {
    if (typeof document === 'undefined') return false
    return document.body.classList.contains('app-theme-light')
  })
  useEffect(function() {
    var themeObs = new MutationObserver(function() {
      setIsLight(document.body.classList.contains('app-theme-light'))
    })
    themeObs.observe(document.body, { attributes: true, attributeFilter: ['class'] })
    return function() { themeObs.disconnect() }
  }, [])

  function showToast(type, message) {
    setToast({ type: type, message: message })
    setTimeout(function() { setToast(null) }, 3200)
  }

  // ── Schema yukleme yaris korumasi ──────────────
  // Her loadSchemaFor cagrisi sayaci arttirir; async adimlar arasinda sayac
  // degistiyse o cagri "bayat" demektir ve state'e yazmaz. Onceki cancelledFlag
  // parametresi primitive by-value kopyalandigi icin HICBIR ZAMAN true olmuyordu:
  // hizli form gecisinde eski formun yavas gelen yaniti yeni formun alanlarini
  // eziyordu (stale data). Unmount'ta da sayac arttirilir — tum in-flight
  // yuklemeler gecersizlesir.
  var loadSeqRef = useRef(0)

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
        await loadSchemaFor(startForm.formCode, formList)
      } catch (e) {
        if (!cancelled) {
          showToast('error', 'Formlar yuklenemedi: ' + e.message)
          setLoadingSchema(false)
        }
      }
    }
    boot()
    return function() {
      cancelled = true
      loadSeqRef.current++   // in-flight loadSchemaFor cagrilarini gecersiz kil
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // formList: forms state'i — entity lookup için geçirilir. Belirtilmezse
  // forms state'ini doğrudan okuyamayız (closure stale olabilir), bu yüzden
  // boot() ilk çağrıda formList'i argüman olarak geçer; sonraki çağrılarda
  // (kullanıcı form değiştirince) forms state günceli olduğu için 2. parametre
  // olarak yeniden geçilir.
  async function loadSchemaFor(formCode, formList) {
    var mySeq = ++loadSeqRef.current
    function isStale() { return mySeq !== loadSeqRef.current }

    setLoadingSchema(true)
    try {
      var schema = await getSchemaApi(formCode)
      if (isStale()) return
      setCurrentFormId(schema.formId)
      // Kullanicinin sectigi formCode'u state'e yazariz; schema response'undan
      // gelen alan farkli case/null olursa form beklenmedik sekilde degismesin.
      var effectiveFormCode = formCode || schema.formCode
      setCurrentFormCode(effectiveFormCode)
      // DB-driven entity lookup: formList argümanı varsa kullan, yoksa forms state günceli
      setCurrentEntity(findEntityByFormCodeInForms(formList || forms, effectiveFormCode))
      setWidgets(Array.isArray(schema.widgets) ? schema.widgets : [])
      setEditingField(null)

      // Form sabit alanlarini kesfet (INFORMATION_SCHEMA tablo kolonlari).
      // Paralel olarak FldSet kayitlarindaki Turkce label'lari da cek; eslesmis
      // alanlar icin label kullan, eslesmemisler icin raw column adi fallback.
      // Hata olursa sessizce bos liste — rehber tanimlamada sadece kardes widget'lar
      // ve DOM'dan gelenler gorunur (eski davranis).
      if (schema.formId) {
        Promise.all([
          // includeMapped=true: form'un TUM kolonlari (FldSet'e eslesmis dahil) gelir.
          // Widget rehberinde admin tum form alanlarini parametre olarak kullanmak ister.
          discoverFieldsApi(schema.formId, { includeMapped: true }).catch(function(e) {
            // eslint-disable-next-line no-console
            console.warn('[AdminRegistry] discoverFields failed:', e && e.message)
            return []
          }),
          getFieldsByFormApi(schema.formId).catch(function(e) {
            // eslint-disable-next-line no-console
            console.warn('[AdminRegistry] getFieldsByForm failed:', e && e.message)
            return []
          }),
        ]).then(function(results) {
          if (isStale()) return
          var cols = Array.isArray(results[0]) ? results[0] : []
          var mapped = Array.isArray(results[1]) ? results[1] : []
          // fieldKey → fieldLabel haritasi (case-insensitive)
          var labelMap = {}
          mapped.forEach(function(m) {
            if (m && m.fieldKey) labelMap[String(m.fieldKey).toLowerCase()] = m.fieldLabel || ''
          })
          var enriched = cols.map(function(col) {
            var key = String(col)
            var label = labelMap[key.toLowerCase()] || ''
            return { fieldKey: key, label: label || key }
          })
          setFormStaticFields(enriched)
        })
      } else {
        setFormStaticFields([])
      }

      // Ust form widget'larini da cek — rule/formula modalinde kullanicinin
      // gorebilmesi icin. Sadece alt form'da (child) calisiyoruz; header'da
      // FORM_PARENTS[formCode] bos olacagi icin bu blok atlaniyor.
      var parentCodes = FORM_PARENTS[formCode] || []
      if (parentCodes.length === 0) {
        setParentFormWidgets([])
      } else {
        var parentWidgetsAccum = []
        for (var i = 0; i < parentCodes.length; i++) {
          var pCode = parentCodes[i]
          try {
            var pSchema = await getSchemaApi(pCode)
            if (isStale()) return
            var parentFormLabel = pSchema.formLabel || pSchema.formCode || pCode
            ;(pSchema.widgets || []).forEach(function(w) {
              // Group tipini dahil etme — rule'da kullanilamaz
              if (String(w.dataType || '').toLowerCase() === 'group') return
              parentWidgetsAccum.push(Object.assign({}, w, {
                _sourceFormCode:  pSchema.formCode || pCode,
                _sourceFormLabel: parentFormLabel,
              }))
            })
          } catch (e) {
            // Ust form yuklenemezse devam et — rule UI daha az opsiyonla calisir
            /* eslint-disable-next-line no-console */
            console.warn('[AdminRegistry] Parent schema load failed:', pCode, e)
          }
        }
        if (isStale()) return
        setParentFormWidgets(parentWidgetsAccum)
      }
    } catch (e) {
      if (!isStale()) showToast('error', 'Schema yuklenemedi: ' + e.message)
    } finally {
      // Bayat cagri loading state'e dokunmaz — daha yeni cagri kendi yasam
      // dongusunu yonetiyor; erken false yazmak yarim icerigi "yuklendi" gosterir.
      if (!isStale()) setLoadingSchema(false)
    }
  }

  /* ── Form (modul) degistirme — page reload YOK ── */
  var handleFormChange = useCallback(function(newFormCode) {
    if (!newFormCode || newFormCode === currentFormCode) return
    loadSchemaFor(newFormCode, forms)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [currentFormCode, forms])

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
        isPermissionControlled: payload.isPermissionControlled === true,
        rules: payload.rules || null,     // Faz G — kural & formul JSON objesi
        // Form uzerinde kaplayacagi 24-col grid span degeri (1-24). Runtime
        // renderer CSS grid-column span'ine cevirir.
        colSpan: (typeof payload.colSpan === 'number' && payload.colSpan >= 1 && payload.colSpan <= 24)
          ? payload.colSpan : null,
        // Etiket gorunum stili — 'standard' (default) / 'modern' (floating) / 'inline' (sade).
        // Dikkat: onceki surum 'inline'i whitelist'e almadigi icin kullanicinin
        // "Sade" secimi sessizce 'standard'a donusuyordu (WidgetBuilderForm 3 deger yollar).
        labelStyle: (payload.labelStyle === 'modern' || payload.labelStyle === 'inline')
          ? payload.labelStyle : 'standard',
      }

      var result = await upsertWidgetApi(apiPayload)
      if (!result.success) {
        showToast('error', 'Kaydedilemedi: ' + (result.message || 'bilinmeyen hata'))
        return
      }

      // Schema'yi yeniden cek (id generated + server validation sonuclari icin)
      await loadSchemaFor(currentFormCode)
      try {
        localStorage.setItem('calibra:widget-schema-changed', String(Date.now()))
        // Aynı tab'da listener'lari da uyandir (storage event sadece DIGER tab'lere gider).
        window.dispatchEvent(new CustomEvent('calibra:widget-schema-changed'))
      } catch(_) {}
      showToast('success', payload.id ? 'Widget guncellendi' : 'Yeni widget eklendi')
    } catch (e) {
      showToast('error', 'Hata: ' + e.message)
    } finally {
      setSavingGlobal(false)
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [currentFormId, currentFormCode, widgets])

  /* ── Toggle aktif/pasif ─────────────────────── */
  // PATCH /widgets/{id}/active — yalnizca IsActive degisir. Onceki surum tam
  // upsert gonderiyordu; lookup/grid/rehber-bagli widget'larda OptionsJSON'un
  // client tarafinda yeniden insasi kayipliydi (guideConfig/displayScope/
  // constraints kayboluyordu, text+rehber baglantisi tamamen siliniyordu).
  var handleToggle = useCallback(async function(widget) {
    var wid = widget.id
    setSavingId(wid)
    try {
      var nextActive = !(widget.isActive !== false)
      var result = await patchWidgetActiveApi(wid, nextActive)
      if (!result.success) {
        showToast('error', 'Durum degistirilemedi: ' + (result.message || ''))
        return
      }
      setWidgets(function(prev) {
        return prev.map(function(w) {
          return w.id === wid ? Object.assign({}, w, { isActive: nextActive }) : w
        })
      })
      try {
        localStorage.setItem('calibra:widget-schema-changed', String(Date.now()))
        // Aynı tab'da listener'lari da uyandir (storage event sadece DIGER tab'lere gider).
        window.dispatchEvent(new CustomEvent('calibra:widget-schema-changed'))
      } catch(_) {}
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
      try {
        localStorage.setItem('calibra:widget-schema-changed', String(Date.now()))
        // Aynı tab'da listener'lari da uyandir (storage event sadece DIGER tab'lere gider).
        window.dispatchEvent(new CustomEvent('calibra:widget-schema-changed'))
      } catch(_) {}
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
    await loadSchemaFor(currentFormCode)
    showToast('success', 'Grup olusturuldu: ' + payload.groupLabel)

    // WidgetBuilderForm bu grubu secsin diye dondur — ama yeni id schema reload
    // sonrasi widgets state'inde olur, burada local id uret ve gercek id'yi
    // arayarak WidgetBuilderForm'a dondur.
    return { id: result.id, groupKey: payload.groupKey, groupLabel: payload.groupLabel }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [currentFormId, currentFormCode, widgets])

  /* ── "Sadece Alan" toggle ──────────────────────── */
  // PATCH /widgets/{id}/is-plain-field — backend LabelStyle otoriter alanini
  // gunceller (true → 'inline', false → inline ise 'standard') ve IsPlainField'i
  // senkron tutar. Onceki tam upsert lookup/grid metadata'sini kaybediyordu.
  var handlePlainFieldToggle = useCallback(async function(widget) {
    var wid = widget.id
    setSavingId(wid)
    try {
      var nextPlain = !(widget.isPlainField === true)
      var result = await patchIsPlainFieldApi(wid, nextPlain)
      if (!result.success) {
        showToast('error', 'Durum degistirilemedi: ' + (result.message || ''))
        return
      }
      setWidgets(function(prev) {
        return prev.map(function(w) {
          if (w.id !== wid) return w
          // Backend'in LabelStyle senkron mantigini lokalde aynala
          var nextLs = nextPlain
            ? 'inline'
            : (String(w.labelStyle || '').toLowerCase() === 'inline' ? 'standard' : (w.labelStyle || 'standard'))
          return Object.assign({}, w, { isPlainField: nextPlain, labelStyle: nextLs })
        })
      })
      showToast('success', nextPlain ? '"Düz alan" modu aktif' : '"Gruplu" moda geri döndü')
    } catch (e) {
      showToast('error', 'Hata: ' + e.message)
    } finally {
      setSavingId(null)
    }
  }, [])

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
  // PATCH /widgets/sort-orders — yalnizca siralama degisir. Onceki surum tam
  // upsert gonderiyordu: lookup/grid widget'larda schema'daki options=null
  // oldugu icin backend "guideCode zorunludur" hatasi veriyordu (reorder hep
  // rollback oluyordu) ve dropdown options'in client-side yeniden insasi
  // (o.optionCode || o.code — yanlis property adlari) veri kaybina acikti.
  // Ortak yardimci: swap + optimistic update + hata durumunda rollback.
  var swapSortOrders = useCallback(async function(itemA, itemB, logTag) {
    var sortA = itemA.sortOrder != null ? itemA.sortOrder : 0
    var sortB = itemB.sortOrder != null ? itemB.sortOrder : 0
    // Ayni sortOrder ise birini 1 artir
    if (sortA === sortB) sortB = sortA + 1

    // Optimistic UI update
    setWidgets(function(prev) {
      return prev.map(function(w) {
        if (w.id === itemA.id) return Object.assign({}, w, { sortOrder: sortB })
        if (w.id === itemB.id) return Object.assign({}, w, { sortOrder: sortA })
        return w
      })
    })

    try {
      var result = await patchSortOrdersApi([
        { id: itemA.id, sortOrder: sortB },
        { id: itemB.id, sortOrder: sortA },
      ])
      if (!result.success) throw new Error(result.message || 'Siralama kaydedilemedi')
    } catch (e) {
      console.error('[' + logTag + '] error:', e)
      showToast('error', 'Sıralama kaydedilemedi: ' + (e.message || ''))
      // Rollback
      setWidgets(function(prev) {
        return prev.map(function(w) {
          if (w.id === itemA.id) return Object.assign({}, w, { sortOrder: sortA })
          if (w.id === itemB.id) return Object.assign({}, w, { sortOrder: sortB })
          return w
        })
      })
    }
  }, [])

  var handleGroupReorder = useCallback(function(groupA, groupB) {
    if (!groupA || !groupB) return
    var rawA = widgets.find(function(w) { return w.id === groupA.id })
    var rawB = widgets.find(function(w) { return w.id === groupB.id })
    if (!rawA || !rawB) {
      console.warn('[GroupReorder] raw widget bulunamadi', { groupA, groupB })
      return
    }
    return swapSortOrders(rawA, rawB, 'GroupReorder')
  }, [widgets, swapSortOrders])

  var handleReorder = useCallback(function(fieldA, fieldB) {
    if (!fieldA || !fieldB) return
    return swapSortOrders(fieldA, fieldB, 'Reorder')
  }, [swapSortOrders])

  /* ── Tanim transport: dışa/içe aktar ────────────────────────
     Export: formun custom widget tanimlari JSON dosyasi olarak iner
     (sistem alanlari haric). Import: dosya secilir → client-side JSON
     dogrulama → onay modali → POST → WidgetCode uzerinden upsert. */
  async function handleExportDefinitions() {
    if (!currentFormCode) return
    try {
      var resp = await fetch('/api/widgets/forms/' + encodeURIComponent(currentFormCode) + '/export', {
        credentials: 'same-origin',
      })
      if (!resp.ok) throw new Error('HTTP ' + resp.status)
      var blob = await resp.blob()
      var url = URL.createObjectURL(blob)
      var a = document.createElement('a')
      a.href = url
      a.download = 'calibra-widgets-' + currentFormCode.toLowerCase() + '.json'
      document.body.appendChild(a)
      a.click()
      a.remove()
      setTimeout(function() { URL.revokeObjectURL(url) }, 2000)
      showToast('success', 'Widget tanımları dışa aktarıldı')
    } catch (e) {
      showToast('error', 'Dışa aktarım hatası: ' + e.message)
    }
  }

  function handleImportFilePick(e) {
    var file = e.target.files && e.target.files[0]
    e.target.value = ''  // ayni dosya tekrar secilebilsin
    if (!file) return
    var reader = new FileReader()
    reader.onload = function() {
      try {
        var pkg = JSON.parse(String(reader.result))
        if (!pkg || pkg.calibraWidgetPackage !== 1 || !Array.isArray(pkg.widgets)) {
          showToast('error', 'Geçersiz paket dosyası — Alan Rehberi dışa aktarım çıktısı bekleniyor')
          return
        }
        setImportPending({ package: pkg, fileName: file.name })
      } catch (err) {
        showToast('error', 'JSON okunamadı: ' + err.message)
      }
    }
    reader.readAsText(file)
  }

  async function confirmImportDefinitions() {
    if (!importPending || !currentFormCode) return
    setImporting(true)
    try {
      var resp = await fetch('/api/widgets/forms/' + encodeURIComponent(currentFormCode) + '/import', {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
        body: JSON.stringify(importPending.package),
      })
      var data = null
      try { data = await resp.json() } catch (e2) { /* not json */ }
      if (!resp.ok || !data || data.success === false) {
        throw new Error((data && data.message) || ('HTTP ' + resp.status))
      }
      var msg = data.created + ' yeni, ' + data.updated + ' güncellendi'
      if (data.skipped && data.skipped.length > 0) {
        msg += ', ' + data.skipped.length + ' atlandı'
        // eslint-disable-next-line no-console
        console.warn('[WidgetImport] atlananlar:', data.skipped)
      }
      showToast('success', 'İçe aktarım tamamlandı: ' + msg)
      setImportPending(null)
      await loadSchemaFor(currentFormCode)
      try {
        localStorage.setItem('calibra:widget-schema-changed', String(Date.now()))
        window.dispatchEvent(new CustomEvent('calibra:widget-schema-changed'))
      } catch (_) { /* ignore */ }
    } catch (e) {
      showToast('error', 'İçe aktarım hatası: ' + e.message)
    } finally {
      setImporting(false)
    }
  }

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
  // 2026-06-02: subModule alanini da gecirelim — ModuleSelector "SubModule —
  // FormName" composite label uretir, boylece dropdown'da "Düzenleme" / "Liste"
  // gibi anlamsiz tek-kelime etiketler yerine "İhtiyaç Kaydı — Üst Bilgi"
  // gibi okunaklı baslıklar gosterilir. Ayrıca "_NEW" variant'ları (sadece
  // yaratim ekrani; widget tanimi yoktur) ModuleSelector tarafinda filtrelenir.
  var moduleSelectorOptions = forms.map(function(f) {
    return {
      value: f.formCode,
      label: f.formName,
      subModule: f.subModule || null,
      module: f.module || null,
      icon: f.icon || null,
      color: f.iconColor || null,
      selected: f.formCode === currentFormCode,
    }
  })

  // ── Sol sidebar için entity gruplama ──────────────────────────────────────
  // ModuleSelector ile aynı mantık — forms state'ten entity listesi türet,
  // ardından module alanına göre bölümlere ayır.
  var sidebarFormLike = moduleSelectorOptions.map(function(opt) {
    return {
      formCode: opt.value,
      formName: opt.label,
      subModule: opt.subModule || null,
      module: opt.module || null,
      icon: opt.icon || null,
      iconColor: opt.color || null,
    }
  })
  var sidebarEntities = buildEntitiesFromForms(sidebarFormLike)
  var sidebarEntityModuleMap = {}
  sidebarFormLike.forEach(function(f) {
    var subModKey = f.subModule ? String(f.subModule).trim() : f.formCode
    if (!sidebarEntityModuleMap[subModKey]) sidebarEntityModuleMap[subModKey] = f.module || 'Diğer'
    if (f.formCode && !sidebarEntityModuleMap[f.formCode]) sidebarEntityModuleMap[f.formCode] = f.module || 'Diğer'
  })
  var sidebarModuleOrder = []
  var sidebarModuleGroups = {}
  sidebarEntities.forEach(function(entity) {
    var mod = sidebarEntityModuleMap[entity.key] || 'Diğer'
    if (!sidebarModuleGroups[mod]) { sidebarModuleGroups[mod] = []; sidebarModuleOrder.push(mod) }
    sidebarModuleGroups[mod].push(entity)
  })
  var sidebarSections = sidebarModuleOrder.map(function(m) { return { moduleName: m, entities: sidebarModuleGroups[m] } })

  // Sidebar arama filtresi — boşsa tüm bölümler, doluysa label'a göre filtrele
  var filteredSidebarSections = sidebarSearch.trim()
    ? sidebarSections.map(function(section) {
        var q = sidebarSearch.trim().toLowerCase()
        var filtered = section.entities.filter(function(entity) {
          return entity.label.toLowerCase().indexOf(q) !== -1
        })
        return filtered.length > 0 ? { moduleName: section.moduleName, entities: filtered } : null
      }).filter(Boolean)
    : sidebarSections
  // ─────────────────────────────────────────────────────────────────────────

  return (
    <div
      className="h-full flex flex-row bg-[#f8fafc] dark:bg-[#0a0d17] overflow-hidden rounded-[inherit]"
      style={{ colorScheme: isLight ? 'light' : 'dark' }}
    >

      {/* ── Sol Sidebar — form listesi, modül bazlı gruplu ──────────────── */}
      <div
        style={{
          width: '210px',
          flexShrink: 0,
          borderRight: isLight ? '1px solid #e2e8f0' : '1px solid rgba(255,255,255,0.06)',
          overflowY: 'hidden',
          display: 'flex',
          flexDirection: 'column',
          background: isLight ? 'rgba(241,245,249,0.85)' : 'rgba(255,255,255,0.015)',
        }}
      >
        {/* Sidebar arama kutusu — sabit, scroll dışı */}
        <div style={{
          padding: '8px 10px',
          borderBottom: isLight ? '1px solid #e2e8f0' : '1px solid rgba(255,255,255,0.06)',
          flexShrink: 0,
        }}>
          <div style={{
            display: 'flex',
            alignItems: 'center',
            gap: '6px',
            padding: '4px 8px',
            borderRadius: '7px',
            background: isLight ? '#ffffff' : 'rgba(255,255,255,0.06)',
            border: isLight ? '1px solid #e2e8f0' : '1px solid rgba(255,255,255,0.10)',
          }}>
            <Search size={11} strokeWidth={2} style={{ color: isLight ? '#94a3b8' : 'rgba(255,255,255,0.3)', flexShrink: 0 }} />
            <input
              type="text"
              value={sidebarSearch}
              onChange={function(e) { setSidebarSearch(e.target.value) }}
              placeholder="Form ara…"
              style={{
                flex: 1,
                background: 'transparent',
                border: 'none',
                outline: 'none',
                fontSize: '11.5px',
                color: isLight ? '#334155' : 'rgba(255,255,255,0.85)',
                minWidth: 0,
              }}
            />
            {sidebarSearch && (
              <button
                type="button"
                onClick={function() { setSidebarSearch('') }}
                style={{
                  display: 'flex', alignItems: 'center',
                  background: 'transparent', border: 'none', cursor: 'pointer', padding: 0,
                  color: isLight ? '#94a3b8' : 'rgba(255,255,255,0.3)', flexShrink: 0,
                }}
              >
                <X size={11} />
              </button>
            )}
          </div>
        </div>

        {/* Kaydırılabilir form listesi */}
        <div style={{ flex: 1, overflowY: 'auto', minHeight: 0 }}>
        {filteredSidebarSections.length === 0 && (
          <div style={{
            padding: '20px 12px',
            fontSize: '11px',
            color: isLight ? '#94a3b8' : 'rgba(255,255,255,0.3)',
            textAlign: 'center',
          }}>
            Sonuç yok
          </div>
        )}

        {filteredSidebarSections.map(function(section, si) {
          return (
            <div key={section.moduleName}>
              {/* Modül başlığı */}
              <div style={{
                padding: si === 0 ? '10px 12px 4px' : '8px 12px 4px',
                fontSize: '10px',
                fontWeight: 600,
                letterSpacing: '0.06em',
                textTransform: 'uppercase',
                color: isLight ? '#94a3b8' : 'rgba(255,255,255,0.28)',
                borderTop: si > 0 ? (isLight ? '1px solid #e2e8f0' : '1px solid rgba(255,255,255,0.06)') : 'none',
              }}>
                {section.moduleName}
              </div>
              {/* Modül altındaki entity'ler */}
              {section.entities.map(function(entity) {
                var palette = resolveColor(entity.color || 'slate')
                var EntityIcon = resolveIcon(entity.icon || 'Layers')
                var isSel = !!(currentEntity && entity.key === currentEntity.key)
                return (
                  <button
                    key={entity.key}
                    type="button"
                    onClick={function() {
                      var fc = getDefaultFormCode(entity)
                      if (fc) handleFormChange(fc)
                    }}
                    style={{
                      width: '100%',
                      display: 'flex',
                      alignItems: 'center',
                      gap: '8px',
                      padding: '5px 10px 5px 0',
                      paddingLeft: 0,
                      textAlign: 'left',
                      background: isSel ? (isLight ? '#e0e7ff' : 'rgba(99,102,241,0.14)') : 'transparent',
                      color: isSel ? (isLight ? '#3730a3' : '#a5b4fc') : (isLight ? '#475569' : 'rgba(255,255,255,0.65)'),
                      borderLeft: isSel ? '2px solid #6366f1' : '2px solid transparent',
                      borderRight: 'none',
                      borderTop: 'none',
                      borderBottom: 'none',
                      cursor: 'pointer',
                      transition: 'background 0.12s, color 0.12s',
                    }}
                    onMouseEnter={function(e) {
                      if (!isSel) e.currentTarget.style.background = isLight ? '#e8edf4' : 'rgba(255,255,255,0.04)'
                    }}
                    onMouseLeave={function(e) {
                      if (!isSel) e.currentTarget.style.background = 'transparent'
                    }}
                  >
                    <div style={{
                      width: '20px', height: '20px',
                      marginLeft: '10px',
                      borderRadius: '5px',
                      display: 'flex', alignItems: 'center', justifyContent: 'center',
                      flexShrink: 0,
                      background: palette.bg,
                      border: '1px solid ' + palette.border,
                    }}>
                      <EntityIcon size={10} style={{ color: palette.icon }} strokeWidth={1.8} />
                    </div>
                    <span style={{
                      fontSize: '12px',
                      fontWeight: isSel ? 600 : 400,
                      overflow: 'hidden',
                      textOverflow: 'ellipsis',
                      whiteSpace: 'nowrap',
                      flex: 1,
                      lineHeight: 1.3,
                    }}>
                      {entity.label}
                    </span>
                  </button>
                )
              })}
            </div>
          )
        })}
          <div style={{ height: '8px' }} />
        </div>{/* /kaydırılabilir liste */}
      </div>

      {/* ── Sağ Panel ─────────────────────────────────────────────────── */}
      <div className="flex-1 flex flex-col min-w-0">

        {/* ── Sağ panel header: entity adı + variant toggle + arama + yeni grup ── */}
        <div
          className="px-4 py-2 flex-shrink-0 flex flex-wrap items-center gap-2"
          style={{ borderBottom: isLight ? '1px solid rgba(226,232,240,0.7)' : '1px solid rgba(255,255,255,0.06)' }}
        >
          {/* Entity ikon + adı */}
          {currentEntity && (function() {
            var palette = resolveColor(currentEntity.color || 'slate')
            var HeaderIcon = resolveIcon(currentEntity.icon || 'Layers')
            return (
              <div className="flex items-center gap-2 flex-shrink-0">
                <div style={{
                  width: '24px', height: '24px',
                  borderRadius: '6px',
                  display: 'flex', alignItems: 'center', justifyContent: 'center',
                  background: palette.bg,
                  border: '1px solid ' + palette.border,
                }}>
                  <HeaderIcon size={12} style={{ color: palette.icon }} strokeWidth={1.8} />
                </div>
                <span className="text-[13px] font-semibold text-slate-800 dark:text-white/90">
                  {currentEntity.label}
                </span>
              </div>
            )
          })()}

          {/* Variant toggle — sadece variant'li entity'de görünür */}
          {currentEntity && Array.isArray(currentEntity.variants) && currentEntity.variants.length > 1 && (
            <EntityVariantToggle
              variants={currentEntity.variants}
              activeFormCode={currentFormCode}
              onPick={function(fc) {
                if (fc && fc !== currentFormCode) loadSchemaFor(fc)
              }}
            />
          )}

          <div className="flex-1" />

          {/* Arama */}
          <div className="flex items-center gap-2 px-3 py-1.5 rounded-xl bg-white/60 dark:bg-white/[0.04] border border-slate-200 dark:border-white/[0.08]">
            <Search size={14} className="text-slate-400 dark:text-white/30 flex-shrink-0" />
            <input
              type="text"
              value={searchQuery}
              onChange={function(e) { setSearchQuery(e.target.value) }}
              placeholder="Widget ara…"
              style={{ width: '130px' }}
              className="bg-transparent text-xs text-slate-800 dark:text-white/85 placeholder:text-slate-400 dark:placeholder:text-white/25 focus:outline-none"
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

          {/* Tanim transport — dışa/içe aktar */}
          <input
            ref={importFileRef}
            type="file"
            accept="application/json,.json"
            style={{ display: 'none' }}
            onChange={handleImportFilePick}
          />
          <button
            type="button"
            onClick={handleExportDefinitions}
            disabled={!currentFormId}
            className="flex items-center justify-center w-8 h-8 rounded-xl bg-white/60 dark:bg-white/[0.04] hover:bg-white dark:hover:bg-white/[0.08] border border-slate-200 dark:border-white/[0.08] text-slate-500 dark:text-white/50 hover:text-indigo-600 dark:hover:text-indigo-300 transition-all flex-shrink-0 disabled:opacity-40 disabled:cursor-not-allowed"
            title="Widget tanımlarını dışa aktar (JSON) — şirketler arası kopyalama / test→canlı taşıma"
          >
            <Download size={14} />
          </button>
          <button
            type="button"
            onClick={function() { if (importFileRef.current) importFileRef.current.click() }}
            disabled={!currentFormId}
            className="flex items-center justify-center w-8 h-8 rounded-xl bg-white/60 dark:bg-white/[0.04] hover:bg-white dark:hover:bg-white/[0.08] border border-slate-200 dark:border-white/[0.08] text-slate-500 dark:text-white/50 hover:text-indigo-600 dark:hover:text-indigo-300 transition-all flex-shrink-0 disabled:opacity-40 disabled:cursor-not-allowed"
            title="Widget tanımlarını içe aktar (JSON paketi)"
          >
            <Upload size={14} />
          </button>

          {/* Yeni Grup */}
          <button
            type="button"
            onClick={function() { setGroupModalOpen(true) }}
            disabled={!currentFormId}
            className="flex items-center gap-1.5 px-3 py-1.5 rounded-xl bg-indigo-500/10 hover:bg-indigo-500/20 dark:bg-indigo-500/15 dark:hover:bg-indigo-500/25 border border-indigo-400/30 dark:border-indigo-400/35 text-[11px] font-semibold text-indigo-600 dark:text-indigo-300 transition-all flex-shrink-0 disabled:opacity-40 disabled:cursor-not-allowed"
            title="Yeni grup tanımla"
          >
            <Plus size={13} strokeWidth={2.4} />
            Yeni Grup
          </button>
        </div>

        {/* ── Gövde (WidgetBuilderForm + WidgetRegistryList) ──────────── */}
        <div className="flex-1 overflow-hidden min-h-0 px-4 py-4">
          {loadingSchema ? (
            <div className="h-full flex flex-col items-center justify-center gap-3">
              <Loader2 size={28} className="text-indigo-500 animate-spin" />
              <span className="text-[11px] text-slate-500 dark:text-white/40">Yükleniyor...</span>
            </div>
          ) : (
            <div className="h-full grid grid-cols-1 md:grid-cols-[1fr_1fr] gap-4 min-h-0">
              {/* Sol kolon: Form */}
              <div className="overflow-y-auto min-h-0 pr-1">
                <WidgetBuilderForm
                  editingField={editingField}
                  onSubmit={handleSubmit}
                  onCancel={handleCancelEdit}
                  saving={savingGlobal}
                  groups={derivedGroups}
                  existingFields={derivedFields}
                  parentFormWidgets={parentFormWidgets}
                  formStaticFields={formStaticFields}
                  activeLayer={null}
                  activeLayerLabel={null}
                />
              </div>

              {/* Sağ kolon: Liste */}
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
                  onGroupReorder={handleGroupReorder}
                  editingId={editingId}
                  savingId={savingId}
                  searchQuery={searchQuery}
                />
              </div>
            </div>
          )}
        </div>

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

      {/* ── İçe aktarım onay modalı ──────────────── */}
      <AdminMiniModal
        isOpen={!!importPending}
        onClose={function() { if (!importing) setImportPending(null) }}
        title="Widget Tanımları İçe Aktarılacak"
        subtitle={importPending
          ? '"' + importPending.fileName + '" — ' + importPending.package.widgets.length +
            ' tanım. Aynı kodlu mevcut widget\'lar güncellenecek, yeni kodlar oluşturulacak. Kayıtlı veriler silinmez.'
          : ''}
        icon={Upload}
        iconColor="indigo"
        maxWidth="max-w-sm"
        footer={
          <>
            <div className="flex-1" />
            <button
              type="button"
              disabled={importing}
              onClick={function() { setImportPending(null) }}
              className="px-4 py-2 rounded-xl bg-white/[0.04] hover:bg-white/[0.08] border border-slate-200 dark:border-white/[0.08] text-xs font-medium text-slate-600 dark:text-white/60 hover:text-slate-900 dark:hover:text-white/85 transition-all disabled:opacity-40"
            >
              Vazgeç
            </button>
            <button
              type="button"
              disabled={importing}
              onClick={confirmImportDefinitions}
              className="flex items-center gap-1.5 px-4 py-2 rounded-xl bg-indigo-500 hover:bg-indigo-600 dark:bg-indigo-500/25 dark:hover:bg-indigo-500/35 border border-indigo-500 dark:border-indigo-400/30 text-xs font-semibold text-white dark:text-indigo-200 transition-all shadow-sm disabled:opacity-60"
            >
              {importing ? <Loader2 size={13} className="animate-spin" /> : <Upload size={13} strokeWidth={2.4} />}
              İçe Aktar
            </button>
          </>
        }
      >
        <p className="text-[12px] text-slate-500 dark:text-white/50 leading-relaxed">
          Paket <strong>{importPending ? (importPending.package.formCode || '?') : ''}</strong> formundan
          dışa aktarılmış{currentFormCode && importPending && importPending.package.formCode !== currentFormCode
            ? ' — bu formda (' + currentFormCode + ') uygulanacak'
            : ''}. Sistem alanları ve hedefte bulunamayan alt formlar otomatik atlanır.
        </p>
      </AdminMiniModal>

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

// ───────────────────────────────────────────────
// EntityVariantToggle — pill-style segmented control
// "Üst Bilgi / Kalem Bilgisi" gibi entity variant'lari arasinda gecis.
// Light/dark tema duyarli (body.app-theme-light kontrolu).
// ───────────────────────────────────────────────
function EntityVariantToggle(props) {
  var variants = Array.isArray(props.variants) ? props.variants : []
  var activeFormCode = props.activeFormCode
  var onPick = props.onPick
  var [isLight, setIsLight] = useState(function() {
    if (typeof document === 'undefined') return false
    return document.body.classList.contains('app-theme-light')
  })
  useEffect(function() {
    var obs = new MutationObserver(function() {
      setIsLight(document.body.classList.contains('app-theme-light'))
    })
    obs.observe(document.body, { attributes: true, attributeFilter: ['class'] })
    return function() { obs.disconnect() }
  }, [])

  if (variants.length === 0) return null

  var trackBg = isLight ? '#f1f5f9' : 'rgba(255,255,255,0.05)'
  var trackBorder = isLight ? '1px solid #e2e8f0' : '1px solid rgba(255,255,255,0.08)'
  var activeBg = isLight ? '#ffffff' : 'rgba(99,102,241,0.18)'
  var activeBorder = isLight ? '1px solid #c7d2fe' : '1px solid rgba(99,102,241,0.45)'
  var activeColor = isLight ? '#4f46e5' : '#c7d2fe'
  var inactiveColor = isLight ? '#64748b' : 'rgba(255,255,255,0.55)'
  var activeShadow = isLight ? '0 1px 2px rgba(15,23,42,0.06)' : '0 0 0 1px rgba(99,102,241,0.20)'

  return (
    <div
      className="inline-flex items-center gap-1 p-1 rounded-full"
      style={{ background: trackBg, border: trackBorder }}
      role="tablist"
    >
      {variants.map(function(v) {
        var isActive = String(v.formCode).toUpperCase() === String(activeFormCode || '').toUpperCase()
        return (
          <button
            key={v.key || v.formCode}
            type="button"
            role="tab"
            aria-selected={isActive}
            onClick={function() { if (onPick && !isActive) onPick(v.formCode) }}
            className="px-3.5 py-1 rounded-full text-[12px] font-semibold transition-all"
            style={{
              background: isActive ? activeBg : 'transparent',
              border: isActive ? activeBorder : '1px solid transparent',
              color: isActive ? activeColor : inactiveColor,
              boxShadow: isActive ? activeShadow : 'none',
              cursor: isActive ? 'default' : 'pointer',
            }}
          >
            {v.label}
          </button>
        )
      })}
    </div>
  )
}

