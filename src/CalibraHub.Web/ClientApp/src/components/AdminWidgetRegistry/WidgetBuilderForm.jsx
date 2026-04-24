/**
 * WidgetBuilderForm — Sol kolon (yeni / edit widget formu)
 *
 * Kompakt yatay layout: label solda (sabit genislik), input sagda.
 * Yer tasarrufu icin etiket ve deger yan yana.
 *
 * Duplicate check: fieldLabel veya fieldKey ayni olan baska bir widget
 * varsa kaydetmeye izin verilmez (case-insensitive).
 */
import { useState, useEffect, useRef } from 'react'
import { motion } from 'framer-motion'
import { Plus, Check, Sparkles, Pencil, List, Wrench, CheckCircle2, CircleDashed } from 'lucide-react'
import DataTypeDropdown from './DataTypeDropdown'
import GroupSelector from './GroupSelector'
import OptionsModal from './OptionsModal'
import RuleBuilderModal from './RuleBuilderModal'

/**
 * Row — label + input satiri.
 * ONEMLI: Row component'ini WidgetBuilderForm render fonksiyonunun
 * DISINDA tanimliyoruz. Iceride tanimlarsak her render'da yeni component
 * referansi olusur, React eski input'u unmount edip yenisini mount
 * eder ve type sirasinda focus kaybolur.
 */
function Row(props) {
  return (
    <div className="flex items-center gap-2.5 min-h-[36px]">
      <label className="w-[140px] flex-shrink-0 text-[10px] font-semibold uppercase tracking-wider text-slate-500 dark:text-white/40 leading-tight">
        {props.label}
        {props.hint && (
          <span className="block text-slate-400 dark:text-white/45 font-normal normal-case text-[9px] mt-0.5">
            {props.hint}
          </span>
        )}
      </label>
      <div className="flex-1 min-w-0">{props.children}</div>
    </div>
  )
}

// Sayisal bicim presetleri — tek dropdown secimi. Ayraclar (ondalik/binlik)
// tarayicinin bolge/dil ayarindan Intl.NumberFormat ile turetilir, preset
// sadece stil (decimal/percent/currency) + ondalik hane sayisi belirler.
var NUMBER_FORMAT_PRESETS = [
  { code: 'int',  style: 'decimal',  places: 0, sample: 1000        },
  { code: 'dec1', style: 'decimal',  places: 1, sample: 1000.1      },
  { code: 'dec2', style: 'decimal',  places: 2, sample: 1000.12     },
  { code: 'dec3', style: 'decimal',  places: 3, sample: 1000.123    },
  { code: 'dec4', style: 'decimal',  places: 4, sample: 1000.1234   },
  { code: 'pct0', style: 'percent',  places: 0, sample: 0.12        },
  { code: 'pct1', style: 'percent',  places: 1, sample: 0.123       },
  { code: 'pct2', style: 'percent',  places: 2, sample: 0.1234      },
  { code: 'cur',  style: 'currency', places: 2, sample: 1000, currency: 'TRY' },
]

function formatNumberSample(preset) {
  try {
    var opts = {
      style: preset.style,
      minimumFractionDigits: preset.places,
      maximumFractionDigits: preset.places,
    }
    if (preset.currency) opts.currency = preset.currency
    var loc = (typeof navigator !== 'undefined' && navigator.language) || 'tr-TR'
    return new Intl.NumberFormat(loc, opts).format(preset.sample)
  } catch (e) {
    return String(preset.sample)
  }
}

// Saklanan metadata (eski veya yeni) → preset code eslestir.
function metaToPresetCode(meta) {
  if (!meta) return 'int'
  var style = String(meta.numericFormat || 'number')
  var places = parseInt(meta.decimalPlaces, 10)
  if (isNaN(places)) places = 0
  // Legacy: decimal2 / decimal4
  if (style === 'decimal2') { style = 'decimal'; places = 2 }
  else if (style === 'decimal4') { style = 'decimal'; places = 4 }
  if (style === 'currency') return 'cur'
  if (style === 'percent') {
    if (places <= 0) return 'pct0'
    if (places === 1) return 'pct1'
    return 'pct2'
  }
  // number / decimal
  if (places <= 0) return 'int'
  if (places === 1) return 'dec1'
  if (places === 2) return 'dec2'
  if (places === 3) return 'dec3'
  return 'dec4'
}

// Preset code → backend payload icin [numericFormat, decimalSep, thousandSep, decimalPlaces].
// Ayraclar tarayici locale'inden turetilir (render tarafi da locale'den okuyacak).
function presetToPayload(code) {
  var p = NUMBER_FORMAT_PRESETS.find(function(x) { return x.code === code }) || NUMBER_FORMAT_PRESETS[0]
  var style = p.style === 'currency' ? 'currency'
            : p.style === 'percent'  ? 'percent'
            : p.places > 0 ? 'decimal' : 'number'
  // Locale'den ayraclari cikar (backend arka uyumluluk icin her iki ayraci da bekliyor).
  var decSep = ','
  var thouSep = '.'
  try {
    var loc = (typeof navigator !== 'undefined' && navigator.language) || 'tr-TR'
    var parts = new Intl.NumberFormat(loc, { minimumFractionDigits: 1 }).formatToParts(1234.5)
    var dp = parts.find(function(x) { return x.type === 'decimal' })
    var gp = parts.find(function(x) { return x.type === 'group' })
    if (dp) decSep = dp.value
    if (gp) thouSep = gp.value
  } catch (e) {}
  return [style, decSep, thouSep, String(p.places)]
}

export default function WidgetBuilderForm(props) {
  var editingField = props.editingField
  var onSubmit = props.onSubmit
  var onCancel = props.onCancel
  var saving = props.saving
  var groups = Array.isArray(props.groups) ? props.groups : []
  var existingFields = Array.isArray(props.existingFields) ? props.existingFields : []
  // Ust form (parent) widget'lari — rule/formul'de secilebilsin diye.
  // Ornek: SALES_QUOTE_LINES tanimi yaparken SALES_QUOTE_EDIT alanlari burada gelir.
  var parentFormWidgets = Array.isArray(props.parentFormWidgets) ? props.parentFormWidgets : []
  var activeLayer = props.activeLayer || null
  var activeLayerLabel = props.activeLayerLabel || null

  // Internal state isimleri UI ile uyumlu kalmaya devam ediyor, ama
  // submit payload'u ve editingField hydration'i artik Faz B API alanlarini
  // kullaniyor (label, widgetCode, parentId, dataType: lowercase, options: string[]).
  var [fieldLabel, setFieldLabel]       = useState('')       // → payload.label
  var [fieldKey, setFieldKey]           = useState('')       // → payload.widgetCode
  var [dataType, setDataType]           = useState('text')   // lowercase Faz A key
  var [maxLength, setMaxLength]         = useState('')       // → payload.maxLength (int|null, sadece text)
  var [minLength, setMinLength]         = useState('')       // → payload.minLength (int|null, sadece text)
  var [expectedLength, setExpectedLength] = useState('')    // → payload.expectedLength (int|null, sadece text)
  var [minValue, setMinValue]           = useState('')       // → payload.minValue (decimal|null, sadece numeric)
  var [maxValue, setMaxValue]           = useState('')       // → payload.maxValue (decimal|null, sadece numeric)
  // Sayisal bicim: tek bir preset secilir. Ayraclar (ondalik/binlik) tarayicinin
  // bolge/dil ayarindan (Intl.NumberFormat) runtime'da turetilir.
  var [numberFormatPreset, setNumberFormatPreset] = useState('int')
  var [textGuideCode, setTextGuideCode] = useState('')       // string tipi icin opsiyonel rehber kodu
  var [textConstraints, setTextConstraints] = useState('')   // rehber kisit JSON dizisi
  var [permissionKey, setPermissionKey] = useState('')       // backend'de saklanmayan UI-only alan
  var [groupId, setGroupId]             = useState(null)     // → payload.parentId (int|null)
  var [sortOrder, setSortOrder]         = useState('')       // → payload.sortOrder (int)
  // Form uzerinde kaplayacagi genislik — 12 kolonlu grid (1-12).
  // 3=1/4, 4=1/3, 6=1/2 (varsayilan), 8=2/3, 9=3/4, 12=tam satir.
  var [colSpan, setColSpan]             = useState(6)
  // Etiket gorunum stili: 'standard' (label input ustunde) veya 'modern'
  // (floating/outlined — label input cercevesi uzerinde).
  var [labelStyle, setLabelStyle]       = useState('standard')
  var [isActive, setIsActive]           = useState(true)
  var [isPlainField, setIsPlainField]   = useState(false)
  var [isRequired, setIsRequired]       = useState(false)
  var [errors, setErrors]               = useState({})
  // Shake animation — zorunlu alan bos biraktirildiginda Widget Ekle
  // tiklaninca kirmizi cerceveli titresim (metin yerine sadece gorsel).
  var [shakeTick, setShakeTick]         = useState(0)
  var formRef = useRef(null)
  // Options = string[] (sadece label — Faz A spec)
  // OptionsModal icinde duzenleniyor, bu state modal'dan donen veriyi tutar.
  var [options, setOptions]             = useState([])
  // Faz G — Rule Engine kural alanlari (opsiyonel string ifade)
  var [ruleVisibleIf, setRuleVisibleIf]   = useState('')
  var [ruleDisabledIf, setRuleDisabledIf] = useState('')
  var [ruleFormula, setRuleFormula]       = useState('')
  // Varsayilan deger — yeni kayit olusurken atanir (readonly degil)
  var [defaultValue,     setDefaultValue]     = useState('')
  var [defaultValueKind, setDefaultValueKind] = useState('static')  // 'static' | 'formula'
  // Semantik Renk Mimarisi: 0=Statik token, 1=Dinamik SQL (baska widget'in degeri)
  var [colorType, setColorType]   = useState(0)
  var [colorValue, setColorValue] = useState('')

  // Modal state'leri
  var [optionsModalOpen, setOptionsModalOpen] = useState(false)
  var [ruleModalOpen, setRuleModalOpen]       = useState(false)
  // Guide katalogu — lookup tipinde rehber adini gostermek icin
  var [guideCatalog, setGuideCatalog] = useState([])

  useEffect(function() {
    if (editingField) {
      // Faz B API alanlari: label, widgetCode, dataType (lowercase), parentId,
      // options (string[]). Legacy alanlara fallback ile geriye donuk uyum.
      setFieldLabel(editingField.label || editingField.fieldLabel || '')
      setFieldKey(editingField.widgetCode || editingField.fieldKey || '')
      setDataType(editingField.dataType || 'text')
      setMaxLength(editingField.maxLength != null ? String(editingField.maxLength) : '')
      setMinLength(editingField.minLength != null ? String(editingField.minLength) : '')
      setExpectedLength(editingField.expectedLength != null ? String(editingField.expectedLength) : '')
      setMinValue(editingField.minValue != null ? String(editingField.minValue) : '')
      setMaxValue(editingField.maxValue != null ? String(editingField.maxValue) : '')
      setNumberFormatPreset(metaToPresetCode(editingField.metadata))
      // Text tipi rehber eslestirme
      setTextGuideCode((editingField.metadata && editingField.metadata.guideCode) || '')
      var cJson = (editingField.metadata && editingField.metadata.constraints)
      setTextConstraints(cJson ? (typeof cJson === 'string' ? cJson : JSON.stringify(cJson, null, 2)) : '')
      setPermissionKey(editingField.permissionKey || '')
      setGroupId(
        editingField.parentId !== undefined
          ? editingField.parentId
          : (editingField.groupId || null)
      )
      setSortOrder(editingField.sortOrder != null ? String(editingField.sortOrder) : '')
      // colSpan — editingField veya metadata'dan oku (1-12 arasi); yoksa 6 (1/2).
      var cs = editingField.colSpan
      if (cs == null && editingField.metadata) cs = editingField.metadata.colSpan
      var csNum = parseInt(cs, 10)
      setColSpan(!isNaN(csNum) && csNum >= 1 && csNum <= 12 ? csNum : 6)
      // labelStyle — 'standard' varsayilan; sadece 'modern' degeri saklanir.
      setLabelStyle(editingField.labelStyle === 'modern' ? 'modern' : 'standard')
      setIsActive(editingField.isActive !== false)
      setIsPlainField(editingField.isPlainField === true)
      setIsRequired(editingField.isRequired === true)
      // Lookup: guideCode metadata'da saklanir (options degil)
      // Grid: childFormCode metadata'da saklanir
      var existingOpts = []
      var dt = (editingField.dataType || '').toLowerCase()
      if (dt === 'lookup' && editingField.metadata && editingField.metadata.guideCode) {
        existingOpts = [editingField.metadata.guideCode]
      } else if (dt === 'grid' && editingField.metadata && editingField.metadata.childFormCode) {
        existingOpts = [editingField.metadata.childFormCode]
      } else if (Array.isArray(editingField.options)) {
        existingOpts = editingField.options.map(function(o) {
          return typeof o === 'string' ? o : (o.optionLabel || o.label || '')
        }).filter(function(s) { return s && s.length > 0 })
      }
      setOptions(existingOpts)
      // Faz G — Rules hydration
      var r = editingField.rules || {}
      setRuleVisibleIf(r.visibleIf || '')
      setRuleDisabledIf(r.disabledIf || '')
      setRuleFormula(r.formula || '')
      // Varsayilan deger — top-level veya rules icinden oku (geriye uyum)
      var dvRaw = editingField.defaultValue != null
        ? editingField.defaultValue
        : (r.defaultValue != null ? r.defaultValue : '')
      var dvKind = editingField.defaultValueKind
        || r.defaultValueKind
        || 'static'
      setDefaultValue(String(dvRaw))
      setDefaultValueKind(dvKind)
      // Semantik Renk hydration
      setColorType(editingField.colorType != null ? editingField.colorType : 0)
      setColorValue(editingField.colorValue || '')
    } else {
      setFieldLabel('')
      setFieldKey('')
      setDataType('text')
      setMaxLength('')
      setMinLength('')
      setExpectedLength('')
      setMinValue('')
      setMaxValue('')
      setNumberFormatPreset('int')
      setPermissionKey('')
      setGroupId(null)
      setSortOrder('')
      setColSpan(6)
      setLabelStyle('standard')
      setIsActive(true)
      setIsPlainField(false)
      setIsRequired(false)
      setOptions([])
      setRuleVisibleIf('')
      setRuleDisabledIf('')
      setRuleFormula('')
      setDefaultValue('')
      setDefaultValueKind('static')
      setColorType(0)
      setColorValue('')
    }
    setErrors({})
  }, [editingField])

  // Dropdown/multi-select/link/lookup/grid disinda bir tipe donulurse options listesini temizle
  // Tip degisince kisitlama alanlarini da sifirla
  useEffect(function() {
    if (dataType !== 'dropdown' && dataType !== 'multi-select' && dataType !== 'link'
        && dataType !== 'lookup' && dataType !== 'grid') {
      setOptions([])
    }
    if (dataType !== 'text') {
      setMinLength('')
      setExpectedLength('')
      setTextGuideCode('')
      setTextConstraints('')
      if (!editingField || (editingField && editingField.dataType !== dataType)) {
        setMaxLength('')
      }
    }
    if (dataType !== 'numeric') {
      setMinValue('')
      setMaxValue('')
      setNumberFormatPreset('int')
    }
  }, [dataType])

  // Beklenen uzunluk girildiginde min/maks uzunluk sifirlanir ve disable edilir.
  // Beklenen uzunluk tam eslesmeyi sart kostugundan min/maks sinirlari anlamsiz olur.
  useEffect(function() {
    if (expectedLength !== '' && !isNaN(parseInt(expectedLength, 10))) {
      setMinLength('')
      setMaxLength('')
    }
  }, [expectedLength])

  // Yeni widget olusturulurken fieldKey'i label'dan otomatik turet.
  // Edit modunda fieldKey degistirilmez (veri butunlugu).
  useEffect(function() {
    if (editingField) return  // edit modunda dokunma
    var key = fieldLabel
      .toLowerCase()
      .replace(/ı/g, 'i').replace(/ğ/g, 'g').replace(/ş/g, 's')
      .replace(/ö/g, 'o').replace(/ü/g, 'u').replace(/ç/g, 'c')
      .replace(/[^a-z0-9\s]/g, '')
      .trim()
      .replace(/\s+/g, '_')
    if (key && !/^[a-z]/.test(key)) key = 'f_' + key
    setFieldKey(key || '')
  }, [fieldLabel, editingField])

  // Shake tetikleyici — shakeTick her hata submit'inde artar, DOM'daki
  // data-shake-key atributu hata anahtariyla eslesen elementlere
  // .cbh-shake class'ini yeniden uygulayip CSS animasyonu restart eder.
  useEffect(function() {
    if (shakeTick === 0) return
    var root = formRef.current
    if (!root) return
    Object.keys(errors).forEach(function(key) {
      var el = root.querySelector('[data-shake-key="' + key + '"]')
      if (!el) return
      el.classList.remove('cbh-shake')
      // Reflow ile animasyonu zorla restart et
      /* eslint-disable-next-line no-unused-expressions */
      el.offsetWidth
      el.classList.add('cbh-shake')
    })
  }, [shakeTick])

  // Lookup tipinde guide katalogu cek (sadece bir kez, cache'lenir)
  useEffect(function() {
    if (dataType !== 'lookup') return
    if (guideCatalog.length > 0) return
    fetch('/api/guides')
      .then(function(r) { return r.ok ? r.json() : [] })
      .then(function(list) { setGuideCatalog(Array.isArray(list) ? list : []) })
      .catch(function() {})
  }, [dataType])

  function validate() {
    var e = {}
    var lbl = fieldLabel.trim()
    var key = fieldKey.trim()

    if (!lbl) {
      e.fieldLabel = 'Başlık zorunlu'
    }
    if (!dataType) {
      e.dataType = 'Veri tipi seçin'
    }

    // Duplicate check — editingField'in kendisi haric
    // existingFields Faz B API'den gelir: her eleman { id, label, widgetCode, ... }
    var editingId = editingField ? editingField.id : null
    if (!e.fieldLabel && lbl) {
      var dupLabel = existingFields.find(function(f) {
        if (editingId && f.id === editingId) return false
        var existingLabel = f.label || f.fieldLabel || ''
        return existingLabel.trim().toLowerCase() === lbl.toLowerCase()
      })
      if (dupLabel) {
        e.fieldLabel = 'Bu başlık zaten kullanılıyor'
      }
    }

    // dropdown / multi-select options validation — Faz A string[] formati
    if (dataType === 'dropdown' || dataType === 'multi-select') {
      if (!options || options.length === 0) {
        e.options = 'En az bir seçenek ekleyin'
      }
    }

    // link validation — URL sablonu zorunlu + {value} yer tutucusu
    if (dataType === 'link') {
      var tpl = (options && options[0]) || ''
      if (!tpl) {
        e.options = 'Hedef URL sablonu zorunlu'
      } else if (String(tpl).indexOf('{value}') === -1) {
        e.options = "URL icinde '{value}' yer tutucusu bulunmali"
      }
    }

    // lookup validation — guideCode zorunlu
    if (dataType === 'lookup') {
      var gc = (options && options[0]) || ''
      if (!String(gc).trim()) {
        e.options = 'Rehber secin'
      }
    }

    // grid validation — childFormCode zorunlu
    if (dataType === 'grid') {
      var cfc = (options && options[0]) || ''
      if (!String(cfc).trim()) {
        e.options = 'Alt form secin'
      }
    }

    // text kisitlama tutarlilik kontrolu
    if (dataType === 'text') {
      var ml  = maxLength      !== '' ? parseInt(maxLength, 10)      : null
      var mnl = minLength      !== '' ? parseInt(minLength, 10)      : null
      var el2 = expectedLength !== '' ? parseInt(expectedLength, 10) : null
      if (mnl != null && ml != null && mnl > ml)
        e.minLength = 'Min. uzunluk, maks. uzunluktan büyük olamaz'
      if (el2 != null && ml != null && el2 > ml)
        e.expectedLength = 'Beklenen uzunluk, maks. uzunluktan büyük olamaz'
    }

    // numeric kisitlama tutarlilik kontrolu
    if (dataType === 'numeric') {
      var mnv = minValue !== '' ? parseFloat(minValue) : null
      var mxv = maxValue !== '' ? parseFloat(maxValue) : null
      if (mnv != null && mxv != null && mnv > mxv)
        e.minValue = 'Min. değer, maks. değerden büyük olamaz'
    }

    return e
  }

  // NOTE: addOption/updateOption/removeOption/slugifyLabel helperlar
  // OptionsModal'a tasindi. Bu form sadece modal'dan donen `options` state'ini
  // tutar ve submit payload'ina ekler.

  function handleSubmit(event) {
    event.preventDefault()
    var e = validate()
    if (Object.keys(e).length > 0) {
      setErrors(e)
      // Her basarisiz submit'te shakeTick artar → useEffect animasyonu tetikler.
      setShakeTick(function(t) { return t + 1 })
      return
    }
    if (onSubmit) {
      // Faz B payload — UpsertWidgetRequest sekli:
      //   { id?, formId, parentId?, widgetCode, label, dataType, maxLength?,
      //     sortOrder, options?, isActive }
      // formId + sortOrder parent (AdminWidgetRegistryPanel) tarafinda eklenir.
      var payloadOptions
      if (dataType === 'dropdown' || dataType === 'multi-select') {
        payloadOptions = options.map(function(o) {
          return typeof o === 'string' ? o : (o.label || '')
        }).filter(function(s) { return s && s.length > 0 })
      } else if (dataType === 'link') {
        // Link: tek elemanli dizi — URL sablonu
        var tpl = (options && options[0]) || ''
        payloadOptions = tpl ? [String(tpl)] : []
      } else if (dataType === 'lookup') {
        // Lookup: tek elemanli dizi — guideCode (backend object JSON'a cevirir)
        var gc = (options && options[0]) || ''
        payloadOptions = gc ? [String(gc)] : []
      } else if (dataType === 'grid') {
        // Grid: tek elemanli dizi — childFormCode (backend object JSON'a cevirir)
        var cfc = (options && options[0]) || ''
        payloadOptions = cfc ? [String(cfc)] : []
      } else if (dataType === 'numeric') {
        // Preset → [numericFormat, decimalSep, thousandSep, decimalPlaces].
        // Ayraclar tarayici locale'inden turetilir; render tarafi da runtime'da
        // locale'den okuyacaktir (bolge dil ayari).
        payloadOptions = presetToPayload(numberFormatPreset)
      } else if (dataType === 'text' && textGuideCode.trim()) {
        // Text + rehber: guideCode + constraints JSON olarak options'a gonderilir
        payloadOptions = [textGuideCode.trim(), textConstraints.trim() || '']
      } else {
        payloadOptions = null
      }

      // Faz G — Rules payload. Hicbir slot dolu degilse null gonder (backend sakin).
      var viTrim = ruleVisibleIf.trim()
      var diTrim = ruleDisabledIf.trim()
      var fmTrim = ruleFormula.trim()
      var dvTrim = String(defaultValue || '').trim()
      var rulesPayload = (viTrim || diTrim || fmTrim || dvTrim)
        ? {
            visibleIf:  viTrim || null,
            disabledIf: diTrim || null,
            formula:    fmTrim || null,
            // Varsayilan deger — backend'in rules JSON'una eklenir. Yeni kayitta
            // runtime tarafinda uygulanmasi beklenir (kullanici uzerine yazabilir).
            defaultValue:     dvTrim || null,
            defaultValueKind: dvTrim ? defaultValueKind : null,
          }
        : null

      onSubmit({
        id: editingField ? editingField.id : null,
        parentId: groupId,                     // null ise "Grupsuz"
        widgetCode: fieldKey.trim(),
        label: fieldLabel.trim(),
        dataType: dataType,                    // lowercase: text, numeric, ...
        maxLength: (dataType === 'text' && maxLength !== '' && !isNaN(parseInt(maxLength, 10)))
          ? Math.max(1, Math.min(8000, parseInt(maxLength, 10)))
          : null,
        minLength: (dataType === 'text' && minLength !== '' && !isNaN(parseInt(minLength, 10)))
          ? Math.max(1, Math.min(8000, parseInt(minLength, 10)))
          : null,
        expectedLength: (dataType === 'text' && expectedLength !== '' && !isNaN(parseInt(expectedLength, 10)))
          ? Math.max(1, Math.min(8000, parseInt(expectedLength, 10)))
          : null,
        minValue: (dataType === 'numeric' && minValue !== '' && !isNaN(parseFloat(minValue)))
          ? parseFloat(minValue)
          : null,
        maxValue: (dataType === 'numeric' && maxValue !== '' && !isNaN(parseFloat(maxValue)))
          ? parseFloat(maxValue)
          : null,
        options: payloadOptions,
        sortOrder: sortOrder !== '' && !isNaN(parseInt(sortOrder, 10)) ? parseInt(sortOrder, 10) : null,
        // Form uzerinde kaplayacagi genislik (12-col grid span) — runtime renderer
        // bu degeri CSS grid-column span'ine donusturur. Default 6 (1/2 satir).
        colSpan: (colSpan >= 1 && colSpan <= 12) ? colSpan : 6,
        // Etiket gorunum stili — 'standard' veya 'modern' (floating).
        labelStyle: labelStyle === 'modern' ? 'modern' : 'standard',
        isActive: isActive,
        isPlainField: isPlainField,
        isRequired: isRequired,
        rules: rulesPayload,
        colorType: colorType,
        colorValue: colorValue.trim() || null,
        // Varsayilan deger — ust seviye alan olarak da gonderilir (backend tercihen
        // buradan okuyabilir; rules icinde de yedegi var).
        defaultValue:     dvTrim || null,
        defaultValueKind: dvTrim ? defaultValueKind : null,
        // UI-only alanlar (backend ignore eder ama parent state'i icin iletiliyor)
        permissionKey: permissionKey.trim() || null,
      })
    }
  }

  var isEdit = editingField != null

  // Ortak CSS — input field (sabit yukseklik 36px tum alanlar icin).
  // type="number" inputlari icin webkit/firefox spinner butonlari gizlenir.
  var inputBase = 'w-full h-9 px-3 rounded-lg text-xs transition-all ' +
    'bg-white/60 dark:bg-white/[0.04] ' +
    'text-slate-800 dark:text-white/85 ' +
    'placeholder:text-slate-400 dark:placeholder:text-white/25 ' +
    'focus:outline-none ' +
    '[&>option]:bg-white [&>option]:text-slate-800 dark:[&>option]:bg-[#1e293b] dark:[&>option]:text-white/85 ' +
    '[appearance:textfield] ' +
    '[&::-webkit-outer-spin-button]:appearance-none ' +
    '[&::-webkit-inner-spin-button]:appearance-none [&::-webkit-inner-spin-button]:m-0 '
  var inputOk = 'border border-slate-200 dark:border-white/[0.08] focus:border-indigo-400/60 dark:focus:border-white/20 focus:shadow-[0_0_0_3px_rgba(99,102,241,0.12)]'
  var inputErr = 'border-2 border-red-500/80 focus:border-red-500 focus:shadow-[0_0_0_3px_rgba(239,68,68,0.22)]'

  return (
    <motion.form
      ref={formRef}
      layout
      onSubmit={handleSubmit}
      className="glass rounded-2xl p-4 flex flex-col gap-3 flex-shrink-0 h-fit shadow-[0_4px_24px_rgba(0,0,0,0.12)]"
    >
      {/* Header */}
      <div className="flex items-center gap-2.5 pb-2 border-b border-slate-200/40 dark:border-white/[0.06]">
        <div
          className="w-7 h-7 rounded-lg flex items-center justify-center"
          style={{
            background: isEdit ? 'rgba(245, 158, 11, 0.12)' : 'rgba(99, 102, 241, 0.12)',
            border: '1px solid ' + (isEdit ? 'rgba(245, 158, 11, 0.25)' : 'rgba(99, 102, 241, 0.25)'),
          }}
        >
          <Sparkles size={12} style={{ color: isEdit ? '#fbbf24' : '#818cf8' }} strokeWidth={2} />
        </div>
        <div className="flex-1 min-w-0">
          <h3 className="text-xs font-bold text-slate-800 dark:text-white/90 leading-tight">
            {isEdit ? 'Widget Düzenle' : 'Yeni Widget Tanımla'}
          </h3>
          <p className="text-[10px] text-slate-500 dark:text-white/30 truncate">
            {isEdit
              ? (editingField.widgetCode || editingField.fieldKey || '')
              : (activeLayerLabel
                  ? 'Katman: ' + activeLayerLabel
                  : 'Form alanına eklenecek alan')}
          </p>
        </div>
      </div>

      {/* Başlık */}
      <Row label="Başlık">
        <input
          type="text"
          value={fieldLabel}
          onChange={function(e) { setFieldLabel(e.target.value) }}
          placeholder="Son Kullanma Tarihi"
          data-shake-key="fieldLabel"
          className={inputBase + (errors.fieldLabel ? inputErr : inputOk)}
        />
      </Row>

      {/* Başlık Stili — Standart (label üstte) veya Modern (floating/outlined). */}
      {dataType !== 'group' && (
        <Row label="Başlık Stili" hint="görünüm">
          <div className="flex items-center gap-1 p-[2px] rounded-lg bg-slate-100 dark:bg-white/[0.04] border border-slate-200 dark:border-white/[0.06]">
            {[
              { v: 'standard', label: 'Standart', hint: 'Label üstte' },
              { v: 'modern',   label: 'Modern',   hint: 'Çizgi üzerinde (floating)' },
            ].map(function(opt) {
              var act = labelStyle === opt.v
              return (
                <button
                  key={opt.v}
                  type="button"
                  onClick={function() { setLabelStyle(opt.v) }}
                  title={opt.hint}
                  className={
                    'flex-1 h-7 px-2.5 rounded-md text-[11px] font-semibold transition-all ' +
                    (act
                      ? 'bg-white dark:bg-white/[0.08] shadow-sm text-indigo-600 dark:text-indigo-300'
                      : 'text-slate-500 dark:text-white/45 hover:text-slate-700 dark:hover:text-white/70')
                  }
                >
                  {opt.label}
                </button>
              )
            })}
          </div>
        </Row>
      )}

      {/* Veri Tipi — Edit modunda DAIMA kilitli.
          Sebep: değerler NVARCHAR olarak saklanır, tip değişince parse
          semantiği bozulur (örn. "abc" → Integer parse hatası). */}
      <Row label="Veri Tipi">
        <div data-shake-key="dataType" className="rounded-lg">
          <DataTypeDropdown value={dataType} onChange={setDataType} disabled={isEdit} />
        </div>
      </Row>

      {/* Maks. Uzunluk — sadece text tipi. Beklenen uzunluk doluysa disabled.
          Uzunluk negatif olamaz: '-' karakteri onChange'de atilir. */}
      {dataType === 'text' && (
        <Row label="Maks. Uzunluk" hint="karakter (opsiyonel)">
          <input
            type="number"
            min="1"
            max="8000"
            value={maxLength}
            disabled={expectedLength !== ''}
            onChange={function(e) { setMaxLength(e.target.value.replace(/-/g, '')) }}
            onKeyDown={function(e) { if (e.key === '-' || e.key === 'e' || e.key === 'E') e.preventDefault() }}
            placeholder="örn. 255"
            className={inputBase + inputOk + (expectedLength !== '' ? ' opacity-50 cursor-not-allowed' : '')}
          />
        </Row>
      )}

      {/* Min. Uzunluk — sadece text tipi. Beklenen uzunluk doluysa disabled.
          Uzunluk negatif olamaz. */}
      {dataType === 'text' && (
        <Row label="Min. Uzunluk" hint="karakter (opsiyonel)">
          <input
            type="number"
            min="1"
            max="8000"
            value={minLength}
            disabled={expectedLength !== ''}
            onChange={function(e) { setMinLength(e.target.value.replace(/-/g, '')) }}
            onKeyDown={function(e) { if (e.key === '-' || e.key === 'e' || e.key === 'E') e.preventDefault() }}
            placeholder="örn. 5"
            data-shake-key="minLength"
            className={inputBase + (errors.minLength ? inputErr : inputOk) + (expectedLength !== '' ? ' opacity-50 cursor-not-allowed' : '')}
          />
        </Row>
      )}

      {/* Beklenen Uzunluk — sadece text tipi. Uzunluk negatif olamaz. */}
      {dataType === 'text' && (
        <Row label="Beklenen Uzunluk" hint="tam eşleşme">
          <input
            type="number"
            min="1"
            max="8000"
            value={expectedLength}
            onChange={function(e) { setExpectedLength(e.target.value.replace(/-/g, '')) }}
            onKeyDown={function(e) { if (e.key === '-' || e.key === 'e' || e.key === 'E') e.preventDefault() }}
            placeholder="örn. 10 (VKN)"
            data-shake-key="expectedLength"
            className={inputBase + (errors.expectedLength ? inputErr : inputOk)}
          />
        </Row>
      )}

      {/* Rehber Bağla — sadece text tipi */}
      {dataType === 'text' && (
        <Row label="Rehber" hint="opsiyonel">
          <select
            value={textGuideCode}
            onChange={function(e) { setTextGuideCode(e.target.value) }}
            className={inputBase + inputOk}
          >
            <option value="">Rehber yok</option>
            {guideCatalog.map(function(g) {
              return <option key={g.guideCode} value={g.guideCode}>{g.guideLabel} ({g.guideCode})</option>
            })}
          </select>
        </Row>
      )}
      {dataType === 'text' && textGuideCode.trim() && (
        <Row label="Kisitlar" hint="JSON">
          <textarea
            value={textConstraints}
            onChange={function(e) { setTextConstraints(e.target.value) }}
            placeholder={'[\n  {"field":"Durum","operator":"eq","value":"Aktif"}\n]'}
            className={inputBase + inputOk}
            style={{ minHeight: 60, fontFamily: 'monospace', fontSize: '0.72rem', resize: 'vertical' }}
          />
        </Row>
      )}

      {/* Min. Değer — sadece numeric tipi */}
      {dataType === 'numeric' && (
        <Row label="Min. Değer" hint="opsiyonel">
          <input
            type="number"
            step="any"
            value={minValue}
            onChange={function(e) { setMinValue(e.target.value) }}
            placeholder="örn. 0"
            data-shake-key="minValue"
            className={inputBase + (errors.minValue ? inputErr : inputOk)}
          />
        </Row>
      )}

      {/* Max. Değer — sadece numeric tipi */}
      {dataType === 'numeric' && (
        <Row label="Max. Değer" hint="opsiyonel">
          <input
            type="number"
            step="any"
            value={maxValue}
            onChange={function(e) { setMaxValue(e.target.value) }}
            placeholder="örn. 100"
            className={inputBase + inputOk}
          />
        </Row>
      )}

      {/* Sayisal bicim — tek dropdown. Ayraclar tarayici locale'inden turetilir. */}
      {dataType === 'numeric' && (
        <Row label="Biçim">
          <select
            value={numberFormatPreset}
            onChange={function(e) { setNumberFormatPreset(e.target.value) }}
            className={inputBase + inputOk}
          >
            {NUMBER_FORMAT_PRESETS.map(function(p) {
              return <option key={p.code} value={p.code}>{formatNumberSample(p)}</option>
            })}
          </select>
        </Row>
      )}

      {/* Secenekler / Hedef URL / Rehber / Alt Tablo — dropdown, multi-select,
          link, lookup, grid tipleri icin. Inline panel yerine "Duzenle"
          butonu OptionsModal'i acar; her tip kendi moduna gecer. */}
      {(dataType === 'dropdown' || dataType === 'multi-select' || dataType === 'link'
        || dataType === 'lookup' || dataType === 'grid') && (
        <Row label={
          dataType === 'link'   ? 'Hedef URL' :
          dataType === 'lookup' ? 'Rehber' :
          dataType === 'grid'   ? 'Alt Form' :
          'Seçenekler'
        }>
          <div className="flex items-center gap-2" data-shake-key="options">
            <div
              className={
                'flex-1 h-9 px-3 rounded-lg text-xs flex items-center truncate ' +
                'bg-white/60 dark:bg-white/[0.04] ' +
                (errors.options
                  ? 'border-2 border-red-500/80 text-slate-600 dark:text-white/60'
                  : 'border border-slate-200 dark:border-white/[0.08] text-slate-600 dark:text-white/60')
              }
            >
              {dataType === 'link'
                ? (options && options[0] ? options[0] : 'URL sablonu tanımlanmamış')
                : dataType === 'lookup'
                  ? (options && options[0]
                      ? (guideCatalog.find(function(g) { return g.guideCode === options[0] }) || {}).guideLabel || options[0]
                      : 'Rehber seçilmemiş')
                  : dataType === 'grid'
                    ? (options && options[0] ? 'Alt form: ' + options[0] : 'Alt form seçilmemiş')
                    : (options.length === 0
                        ? 'Seçenek tanımlanmamış'
                        : options.length + ' seçenek tanımlı')}
            </div>
            <button
              type="button"
              onClick={function() { setOptionsModalOpen(true) }}
              className="h-9 px-3 flex items-center gap-1.5 rounded-lg bg-indigo-500/10 hover:bg-indigo-500/20 dark:bg-indigo-500/15 dark:hover:bg-indigo-500/25 border border-indigo-400/30 dark:border-indigo-400/35 text-[11px] font-semibold text-indigo-600 dark:text-indigo-300 transition-all flex-shrink-0"
              title={
                dataType === 'link'   ? 'Hedef URL sablonunu duzenle' :
                dataType === 'lookup' ? 'Rehberi sec' :
                dataType === 'grid'   ? 'Alt form sec' :
                'Seçenekleri düzenle'
              }
            >
              <Pencil size={11} strokeWidth={2.4} />
              Düzenle
            </button>
          </div>
        </Row>
      )}

      {/* Grup Adı */}
      <Row label="Grup Adı">
        <GroupSelector
          groups={groups}
          value={groupId}
          onChange={setGroupId}
          disabled={saving}
        />
      </Row>

      {/* Genişlik — 12-kolonlu interaktif slider. Cell'lere tikla veya
          klavye ok'lari ile genislik ayarla. Secilen kisim yesil ile dolar. */}
      {dataType !== 'group' && (
        <Row label="Genişlik" hint="form satırında">
          <div className="flex flex-col gap-1.5">
            <div
              role="slider"
              tabIndex={0}
              aria-valuemin={1}
              aria-valuemax={12}
              aria-valuenow={colSpan}
              aria-label="Widget genişliği 1-12"
              onKeyDown={function(e) {
                if (e.key === 'ArrowRight' || e.key === 'ArrowUp') {
                  e.preventDefault()
                  setColSpan(function(v) { return Math.min(12, v + 1) })
                } else if (e.key === 'ArrowLeft' || e.key === 'ArrowDown') {
                  e.preventDefault()
                  setColSpan(function(v) { return Math.max(1, v - 1) })
                } else if (e.key === 'Home') {
                  e.preventDefault(); setColSpan(1)
                } else if (e.key === 'End') {
                  e.preventDefault(); setColSpan(12)
                }
              }}
              className="flex gap-[2px] h-7 rounded-md overflow-hidden cursor-pointer select-none focus:outline-none focus:ring-2 focus:ring-emerald-400/40"
            >
              {Array.from({ length: 12 }).map(function(_, i) {
                var val = i + 1
                var filled = val <= colSpan
                var isLast = val === colSpan
                return (
                  <button
                    key={i}
                    type="button"
                    tabIndex={-1}
                    onClick={function() { setColSpan(val) }}
                    title={val + ' / 12'}
                    className={
                      'flex-1 transition-colors flex items-center justify-center text-[9px] font-bold ' +
                      (filled
                        ? 'bg-emerald-500 hover:bg-emerald-400 text-white'
                        : 'bg-slate-200 dark:bg-white/[0.06] hover:bg-slate-300 dark:hover:bg-white/[0.12] text-slate-400 dark:text-white/30')
                    }
                  >
                    {isLast ? val : ''}
                  </button>
                )
              })}
            </div>
            {/* Altta kucuk fraction etiketi */}
            <div className="flex items-center justify-between text-[10px] text-slate-500 dark:text-white/45">
              <span className="font-mono">{colSpan}/12</span>
              <span className="font-semibold text-emerald-600 dark:text-emerald-400">
                {(function() {
                  if (colSpan === 12) return 'Tam satır'
                  if (colSpan ===  9) return '3/4'
                  if (colSpan ===  8) return '2/3'
                  if (colSpan ===  6) return '1/2'
                  if (colSpan ===  4) return '1/3'
                  if (colSpan ===  3) return '1/4'
                  if (colSpan ===  2) return '1/6'
                  if (colSpan ===  1) return '1/12'
                  return colSpan + '/12'
                })()}
              </span>
            </div>
          </div>
        </Row>
      )}

      {/* Faz G — Kural/Formül & Renk Modu.
          Durum (tanimli/tanimsiz) butonun icinde sag kenarda ikon olarak gosterilir. */}
      <div className="pt-2 border-t border-slate-200/40 dark:border-white/[0.06]">
        <button
          type="button"
          onClick={function() { setRuleModalOpen(true) }}
          className="w-full flex items-center gap-2 px-3 py-2.5 rounded-lg text-left transition-all"
          style={{
            background: 'rgba(245,158,11,0.07)',
            border: '1px solid rgba(245,158,11,0.2)',
          }}
          title={(ruleVisibleIf || ruleDisabledIf || ruleFormula || colorValue || defaultValue) ? 'Tanımlı' : 'Tanımsız'}
        >
          <Wrench size={13} style={{ color: '#fbbf24', flexShrink: 0 }} strokeWidth={2} />
          <span className="text-[11px] font-semibold flex-1" style={{ color: '#fbbf24' }}>
            Kuralları &amp; Formülleri Düzenle
          </span>
          {(ruleVisibleIf || ruleDisabledIf || ruleFormula || colorValue || defaultValue) ? (
            <CheckCircle2 size={14} style={{ color: '#10b981', flexShrink: 0 }} strokeWidth={2.4} />
          ) : (
            <CircleDashed size={14} style={{ color: 'rgba(148,163,184,0.6)', flexShrink: 0 }} strokeWidth={2} />
          )}
        </button>
      </div>

      {/* Sadece alan (plain field) toggle — sadece field tipleri icin */}
      {/* Zorunlu alan toggle — sadece field tipleri icin */}
      {dataType !== 'group' && (
        <div className="flex items-center justify-between py-2 px-1">
          <span className="text-[10px] font-semibold uppercase tracking-wider text-slate-500 dark:text-white/40">
            Zorunlu Alan
          </span>
          <button
            type="button"
            onClick={function() { setIsRequired(function(v) { return !v }) }}
            className={
              'relative w-10 h-5 rounded-full transition-colors ' +
              (isRequired ? 'bg-red-500/70' : 'bg-slate-300 dark:bg-white/10')
            }
          >
            <motion.div
              className="absolute top-0.5 w-4 h-4 rounded-full bg-white shadow-sm"
              animate={{ left: isRequired ? 22 : 2 }}
              transition={{ type: 'spring', stiffness: 500, damping: 30 }}
            />
          </button>
        </div>
      )}

      {/* Aktif toggle */}
      <div className="flex items-center justify-between py-2 px-1 border-t border-slate-200/40 dark:border-white/[0.06]">
        <span className="text-[10px] font-semibold uppercase tracking-wider text-slate-500 dark:text-white/40">
          Aktif
        </span>
        <button
          type="button"
          onClick={function() { setIsActive(function(v) { return !v }) }}
          className={
            'relative w-10 h-5 rounded-full transition-colors ' +
            (isActive ? 'bg-emerald-500/70' : 'bg-slate-300 dark:bg-white/10')
          }
        >
          <motion.div
            className="absolute top-0.5 w-4 h-4 rounded-full bg-white shadow-sm"
            animate={{ left: isActive ? 22 : 2 }}
            transition={{ type: 'spring', stiffness: 500, damping: 30 }}
          />
        </button>
      </div>

      {/* Actions */}
      <div className="flex items-center gap-2 pt-1">
        {isEdit && (
          <button
            type="button"
            onClick={onCancel}
            disabled={saving}
            className="px-3 py-2 rounded-lg bg-white/[0.04] hover:bg-white/[0.08] border border-white/[0.06] text-xs font-medium text-white/50 hover:text-white/70 transition-all disabled:opacity-50"
          >
            İptal
          </button>
        )}
        <button
          type="submit"
          disabled={saving}
          className="flex-1 flex items-center justify-center gap-1.5 px-3 py-2 rounded-lg bg-indigo-500 hover:bg-indigo-600 dark:bg-indigo-500/25 dark:hover:bg-indigo-500/35 border border-indigo-500 dark:border-indigo-400/25 text-xs font-semibold text-white dark:text-indigo-200 transition-all disabled:opacity-50 shadow-sm"
        >
          {isEdit ? <Check size={13} /> : <Plus size={13} />}
          {saving ? 'Kaydediliyor...' : (isEdit ? 'Güncelle' : 'Widget Ekle')}
        </button>
      </div>

      <OptionsModal
        isOpen={optionsModalOpen}
        onClose={function() { setOptionsModalOpen(false) }}
        mode={
          dataType === 'link'   ? 'link' :
          dataType === 'lookup' ? 'lookup' :
          dataType === 'grid'   ? 'grid' :
          'options'
        }
        initialOptions={options}
        onSaved={function(newOpts) {
          setOptions(newOpts)
          // errors.options / optionRows temizle
          if (errors.options || errors.optionRows) {
            setErrors(function(prev) {
              var copy = Object.assign({}, prev)
              delete copy.options
              delete copy.optionRows
              return copy
            })
          }
          setOptionsModalOpen(false)
        }}
        isEdit={isEdit}
      />

      <RuleBuilderModal
        isOpen={ruleModalOpen}
        onClose={function() { setRuleModalOpen(false) }}
        onSave={function(vals) {
          setRuleVisibleIf(vals.visibleIf || '')
          setRuleDisabledIf(vals.disabledIf || '')
          setRuleFormula(vals.formula || '')
          setColorType(vals.colorType != null ? vals.colorType : 0)
          setColorValue(vals.colorValue || '')
          setDefaultValue(vals.defaultValue || '')
          setDefaultValueKind(vals.defaultValueKind || 'static')
          setRuleModalOpen(false)
        }}
        dataType={dataType}
        initialValues={{
          visibleIf:        ruleVisibleIf,
          disabledIf:       ruleDisabledIf,
          formula:          ruleFormula,
          colorType:        colorType,
          colorValue:       colorValue,
          defaultValue:     defaultValue,
          defaultValueKind: defaultValueKind,
        }}
        availableWidgets={(function() {
          // Onceki: sadece mevcut form'un alanlari. Simdi: kendi alanlari +
          // ust form (parent) alanlari. Her widget _sourceFormCode/Label
          // tasir; FieldDropdown etikete kucuk rozet olarak basar.
          var ownList = existingFields.map(function(f) {
            return {
              widgetCode:       f.widgetCode || f.fieldKey || '',
              label:            f.label || f.fieldLabel || '',
              dataType:         f.dataType || 'text',
              _sourceFormCode:  null,
              _sourceFormLabel: null,
            }
          })
          var parentList = parentFormWidgets.map(function(w) {
            return {
              widgetCode:       w.widgetCode || '',
              label:            w.label || '',
              dataType:         w.dataType || 'text',
              _sourceFormCode:  w._sourceFormCode || null,
              _sourceFormLabel: w._sourceFormLabel || null,
            }
          })
          return ownList.concat(parentList)
            .filter(function(w) { return w.widgetCode && w.widgetCode !== fieldKey })
        })()}
      />
    </motion.form>
  )
}
