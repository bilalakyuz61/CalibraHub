/**
 * WidgetBuilderForm — Sol kolon (yeni / edit widget formu)
 *
 * Kompakt yatay layout: label solda (sabit genislik), input sagda.
 * Yer tasarrufu icin etiket ve deger yan yana.
 *
 * Duplicate check: fieldLabel veya fieldKey ayni olan baska bir widget
 * varsa kaydetmeye izin verilmez (case-insensitive).
 */
import { useState, useEffect } from 'react'
import { motion } from 'framer-motion'
import { Plus, Check, Sparkles, Info, Pencil, List, Wrench } from 'lucide-react'
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
      <label className="w-[85px] flex-shrink-0 text-[10px] font-semibold uppercase tracking-wider text-slate-500 dark:text-white/40 leading-tight">
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

export default function WidgetBuilderForm(props) {
  var editingField = props.editingField
  var onSubmit = props.onSubmit
  var onCancel = props.onCancel
  var saving = props.saving
  var groups = Array.isArray(props.groups) ? props.groups : []
  var existingFields = Array.isArray(props.existingFields) ? props.existingFields : []
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
  var [numericFormat, setNumericFormat] = useState('number') // number | decimal | currency | percent
  var [decimalPlaces, setDecimalPlaces] = useState('2')      // ondalik hane sayisi
  var [decimalSep, setDecimalSep]       = useState(',')      // , veya .
  var [thousandSep, setThousandSep]     = useState('.')      // . veya , veya bos
  var [textGuideCode, setTextGuideCode] = useState('')       // string tipi icin opsiyonel rehber kodu
  var [textConstraints, setTextConstraints] = useState('')   // rehber kisit JSON dizisi
  var [permissionKey, setPermissionKey] = useState('')       // backend'de saklanmayan UI-only alan
  var [groupId, setGroupId]             = useState(null)     // → payload.parentId (int|null)
  var [sortOrder, setSortOrder]         = useState('')       // → payload.sortOrder (int)
  var [isActive, setIsActive]           = useState(true)
  var [isPlainField, setIsPlainField]   = useState(false)
  var [isRequired, setIsRequired]       = useState(false)
  var [errors, setErrors]               = useState({})
  // Options = string[] (sadece label — Faz A spec)
  // OptionsModal icinde duzenleniyor, bu state modal'dan donen veriyi tutar.
  var [options, setOptions]             = useState([])
  // Faz G — Rule Engine kural alanlari (opsiyonel string ifade)
  var [ruleVisibleIf, setRuleVisibleIf]   = useState('')
  var [ruleDisabledIf, setRuleDisabledIf] = useState('')
  var [ruleFormula, setRuleFormula]       = useState('')
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
      var nfmt = (editingField.metadata && editingField.metadata.numericFormat) || 'number'
      // Legacy: decimal2/decimal4 → decimal + decimalPlaces
      if (nfmt === 'decimal2') { setNumericFormat('decimal'); setDecimalPlaces('2') }
      else if (nfmt === 'decimal4') { setNumericFormat('decimal'); setDecimalPlaces('4') }
      else { setNumericFormat(nfmt); setDecimalPlaces((editingField.metadata && editingField.metadata.decimalPlaces) || '2') }
      setDecimalSep((editingField.metadata && editingField.metadata.decimalSep) || ',')
      setThousandSep((editingField.metadata && editingField.metadata.thousandSep) != null
        ? editingField.metadata.thousandSep : '.')
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
      setPermissionKey('')
      setGroupId(null)
      setSortOrder('')
      setIsActive(true)
      setIsPlainField(false)
      setIsRequired(false)
      setOptions([])
      setRuleVisibleIf('')
      setRuleDisabledIf('')
      setRuleFormula('')
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
      setNumericFormat('number')
      setDecimalPlaces('2')
      setDecimalSep(',')
      setThousandSep('.')
    }
  }, [dataType])

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
        payloadOptions = [numericFormat, decimalSep, thousandSep, decimalPlaces]
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
      var rulesPayload = (viTrim || diTrim || fmTrim)
        ? {
            visibleIf:  viTrim || null,
            disabledIf: diTrim || null,
            formula:    fmTrim || null,
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
        isActive: isActive,
        isPlainField: isPlainField,
        isRequired: isRequired,
        rules: rulesPayload,
        colorType: colorType,
        colorValue: colorValue.trim() || null,
        // UI-only alanlar (backend ignore eder ama parent state'i icin iletiliyor)
        permissionKey: permissionKey.trim() || null,
      })
    }
  }

  var isEdit = editingField != null

  // Ortak CSS — input field (sabit yukseklik 36px tum alanlar icin)
  var inputBase = 'w-full h-9 px-3 rounded-lg text-xs transition-all ' +
    'bg-white/60 dark:bg-white/[0.04] ' +
    'text-slate-800 dark:text-white/85 ' +
    'placeholder:text-slate-400 dark:placeholder:text-white/25 ' +
    'focus:outline-none ' +
    '[&>option]:bg-white [&>option]:text-slate-800 dark:[&>option]:bg-[#1e293b] dark:[&>option]:text-white/85 '
  var inputOk = 'border border-slate-200 dark:border-white/[0.08] focus:border-indigo-400/60 dark:focus:border-white/20 focus:shadow-[0_0_0_3px_rgba(99,102,241,0.12)]'
  var inputErr = 'border border-red-400/60 focus:border-red-400/80 focus:shadow-[0_0_0_3px_rgba(239,68,68,0.15)]'

  return (
    <motion.form
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
          className={inputBase + (errors.fieldLabel ? inputErr : inputOk)}
        />
        {errors.fieldLabel && (
          <p className="text-[10px] text-red-600 dark:text-red-400/80 mt-1">{errors.fieldLabel}</p>
        )}
      </Row>

      {/* Veri Tipi — Edit modunda DAIMA kilitli.
          Sebep: değerler NVARCHAR olarak saklanır, tip değişince parse
          semantiği bozulur (örn. "abc" → Integer parse hatası). */}
      <Row label="Veri Tipi">
        <DataTypeDropdown value={dataType} onChange={setDataType} disabled={isEdit} />
        {errors.dataType && (
          <p className="text-[10px] text-red-600 dark:text-red-400/80 mt-1">{errors.dataType}</p>
        )}
      </Row>

      {/* Maks. Uzunluk — sadece text tipi */}
      {dataType === 'text' && (
        <Row label="Maks. Uzunluk" hint="karakter (opsiyonel)">
          <input
            type="number"
            min="1"
            max="8000"
            value={maxLength}
            onChange={function(e) { setMaxLength(e.target.value) }}
            placeholder="örn. 255"
            className={inputBase + inputOk}
          />
        </Row>
      )}

      {/* Min. Uzunluk — sadece text tipi */}
      {dataType === 'text' && (
        <Row label="Min. Uzunluk" hint="karakter (opsiyonel)">
          <input
            type="number"
            min="1"
            max="8000"
            value={minLength}
            onChange={function(e) { setMinLength(e.target.value) }}
            placeholder="örn. 5"
            className={inputBase + (errors.minLength ? inputErr : inputOk)}
          />
          {errors.minLength && (
            <p className="text-[10px] text-red-600 dark:text-red-400/80 mt-1">{errors.minLength}</p>
          )}
        </Row>
      )}

      {/* Beklenen Uzunluk — sadece text tipi */}
      {dataType === 'text' && (
        <Row label="Beklenen Uzunluk" hint="tam eşleşme">
          <input
            type="number"
            min="1"
            max="8000"
            value={expectedLength}
            onChange={function(e) { setExpectedLength(e.target.value) }}
            placeholder="örn. 10 (VKN)"
            className={inputBase + (errors.expectedLength ? inputErr : inputOk)}
          />
          {errors.expectedLength && (
            <p className="text-[10px] text-red-600 dark:text-red-400/80 mt-1">{errors.expectedLength}</p>
          )}
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
            className={inputBase + (errors.minValue ? inputErr : inputOk)}
          />
          {errors.minValue && (
            <p className="text-[10px] text-red-600 dark:text-red-400/80 mt-1">{errors.minValue}</p>
          )}
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

      {/* Sayısal format ayarları */}
      {dataType === 'numeric' && (
        <Row label="Format">
          <select
            value={numericFormat}
            onChange={function(e) { setNumericFormat(e.target.value) }}
            className={inputBase + inputOk}
          >
            <option value="number">Tam Sayi</option>
            <option value="decimal">Ondalik</option>
            <option value="currency">Para Birimi</option>
            <option value="percent">Yuzde (%)</option>
          </select>
        </Row>
      )}
      {dataType === 'numeric' && (numericFormat === 'decimal' || numericFormat === 'currency' || numericFormat === 'percent') && (
        <Row label="Ondalik Hane">
          <input
            type="number"
            min="0"
            max="8"
            step="1"
            value={decimalPlaces}
            onChange={function(e) { setDecimalPlaces(e.target.value) }}
            className={inputBase + inputOk}
            style={{ width: 70 }}
          />
        </Row>
      )}
      {dataType === 'numeric' && (numericFormat === 'decimal' || numericFormat === 'currency' || numericFormat === 'percent') && (
        <Row label="Ondalik">
          <select
            value={decimalSep}
            onChange={function(e) { setDecimalSep(e.target.value) }}
            className={inputBase + inputOk}
          >
            <option value=",">, (virgul)</option>
            <option value=".">. (nokta)</option>
          </select>
        </Row>
      )}
      {dataType === 'numeric' && (numericFormat === 'decimal' || numericFormat === 'currency' || numericFormat === 'percent') && (
        <Row label="Binlik">
          <select
            value={thousandSep}
            onChange={function(e) { setThousandSep(e.target.value) }}
            className={inputBase + inputOk}
          >
            <option value=".">. (nokta)</option>
            <option value=",">, (virgul)</option>
            <option value="">Yok</option>
          </select>
        </Row>
      )}
      {dataType === 'numeric' && numericFormat !== 'number' && (
        <Row label="Onizleme">
          <span className="text-xs text-slate-400 dark:text-white/40 font-mono">
            {numericFormat === 'currency' ? '₺ ' : ''}
            {(function() {
              var d = parseInt(decimalPlaces, 10) || 0
              var parts = (1234567.89).toFixed(d).split('.')
              var intPart = parts[0]
              if (thousandSep) intPart = intPart.replace(/\B(?=(\d{3})+(?!\d))/g, thousandSep)
              return d > 0 ? intPart + decimalSep + parts[1] : intPart
            })()}
            {numericFormat === 'percent' ? ' %' : ''}
          </span>
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
          <div className="flex items-center gap-2">
            <div
              className={
                'flex-1 h-9 px-3 rounded-lg text-xs flex items-center truncate ' +
                'bg-white/60 dark:bg-white/[0.04] ' +
                (errors.options
                  ? 'border border-red-400/60 text-red-600 dark:text-red-400/80'
                  : 'border border-slate-200 dark:border-white/[0.08] text-slate-600 dark:text-white/60')
              }
            >
              {errors.options
                ? errors.options
                : (dataType === 'link'
                    ? (options && options[0] ? options[0] : 'URL sablonu tanimlanmamis')
                    : dataType === 'lookup'
                      ? (options && options[0]
                          ? (guideCatalog.find(function(g) { return g.guideCode === options[0] }) || {}).guideLabel || options[0]
                          : 'Rehber secilmemis')
                      : dataType === 'grid'
                        ? (options && options[0] ? 'Alt form: ' + options[0] : 'Alt form secilmemis')
                        : (options.length === 0
                            ? 'Seçenek tanımlanmamış'
                            : options.length + ' seçenek tanımlı'))}
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

      {/* Faz G — Kural/Formül & Renk Modu */}
      <div className="pt-2 border-t border-slate-200/40 dark:border-white/[0.06] flex flex-col gap-2">
        {/* Özet badge */}
        {(ruleVisibleIf || ruleDisabledIf || ruleFormula || colorValue) && (
          <div className="flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg"
            style={{ background: 'rgba(245,158,11,0.08)', border: '1px solid rgba(245,158,11,0.2)' }}
          >
            <Check size={10} style={{ color: '#fbbf24', flexShrink: 0 }} />
            <span className="text-[10px] font-semibold" style={{ color: '#fbbf24' }}>Tanımlı</span>
            <span className="text-[9px] ml-auto" style={{ color: 'rgba(245,158,11,0.55)' }}>
              {[ruleVisibleIf && 'görünürlük', ruleDisabledIf && 'aktiflik', ruleFormula && 'formül', colorValue && 'renk'].filter(Boolean).join(' · ')}
            </span>
          </div>
        )}
        <button
          type="button"
          onClick={function() { setRuleModalOpen(true) }}
          className="w-full flex items-center gap-2 px-3 py-2.5 rounded-lg text-left transition-all"
          style={{
            background: 'rgba(245,158,11,0.07)',
            border: '1px solid rgba(245,158,11,0.2)',
          }}
        >
          <Wrench size={13} style={{ color: '#fbbf24', flexShrink: 0 }} strokeWidth={2} />
          <span className="text-[11px] font-semibold" style={{ color: '#fbbf24' }}>
            Kuralları &amp; Formülleri Düzenle
          </span>
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

      {/* Aktif toggle + Phase 2 info icon */}
      <div className="flex items-center justify-between pt-2 border-t border-slate-200/40 dark:border-white/[0.06]">
        <div className="flex items-center gap-1.5">
          <Info size={11} className="text-slate-400 dark:text-white/45" />
          <span className="text-[10px] text-slate-400 dark:text-white/30">
            Yetki kontrolü Phase 2
          </span>
        </div>
        <div className="flex items-center gap-2">
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
          setRuleModalOpen(false)
        }}
        initialValues={{
          visibleIf:  ruleVisibleIf,
          disabledIf: ruleDisabledIf,
          formula:    ruleFormula,
          colorType:  colorType,
          colorValue: colorValue,
        }}
        availableWidgets={existingFields.map(function(f) {
          return {
            widgetCode: f.widgetCode || f.fieldKey || '',
            label:      f.label || f.fieldLabel || '',
            dataType:   f.dataType || 'text',
          }
        }).filter(function(w) { return w.widgetCode && w.widgetCode !== fieldKey })}
      />
    </motion.form>
  )
}
