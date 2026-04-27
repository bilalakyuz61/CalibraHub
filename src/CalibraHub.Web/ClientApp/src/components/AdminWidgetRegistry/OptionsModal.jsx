/**
 * OptionsModal
 *
 * Iki modlu modal:
 *
 *  mode='options' (varsayilan) — dropdown / multi-select icin secenek listesi
 *    editoru. Faz A spec'ine gore string[] formati (sadece label, key YOK).
 *    onSaved(['Kirmizi','Mavi','Yesil']) doner.
 *
 *  mode='link' — link tipi icin URL sablon editoru. OptionsJSON[0] = URL
 *    sablonu (tek string). Ust kisimda "Hazir Sablonlar" dropdown'i ile ERP
 *    standart rotalar arasindan hizli secim. onSaved(['/Finance/...?code={value}'])
 *    (tek elemanli dizi — mevcut sozlesme ile uyumlu).
 *
 * Props:
 *   isOpen, onClose
 *   mode           — 'options' | 'link' (default 'options')
 *   initialOptions — options mode: string[] veya legacy [{optionKey,optionLabel}]
 *                    link mode:    [urlTemplate] tek elemanli dizi
 *   onSaved(string[]) — kaydet geri cagrisi
 *   isEdit         — UI label farki icin (opsiyonel)
 */
import { useState, useEffect, useRef } from 'react'
import { createPortal } from 'react-dom'
import { List, Plus, X, Check, ExternalLink, Search, Table, ChevronDown } from 'lucide-react'
import AdminMiniModal from './AdminMiniModal'

/**
 * ERP standart bag sablonlari — admin hizli secim icin. {value} placeholder'i
 * runtime'da kullanicinin girdigi deger ile yer degistirilir.
 */
var LINK_TEMPLATES = [
  { label: 'Cari Hesap Karti (kod ile)',    value: '/Finance/ContactEdit?code={value}' },
  { label: 'Cari Hesap Karti (id ile)',     value: '/Finance/ContactEdit?id={value}' },
  { label: 'Cari Hesap Ekstresi',           value: '/Finance/ContactStatement?code={value}' },
  { label: 'Malzeme Karti (kod ile)',       value: '/Logistics/MaterialCardEdit?code={value}' },
  { label: 'Malzeme Karti (id ile)',        value: '/Logistics/MaterialCardEdit?id={value}' },
  { label: 'Urun Agaci / Recete',           value: '/Logistics/BOMs?materialCode={value}' },
  { label: 'Satis Teklifi (duzenleme)',     value: '/Sales/DocumentEdit?id={value}' },
]

/**
 * GlassSelect — Tema uyumlu custom dropdown.
 * Native <select> OS tarafindan render edilir ve glassmorphism temaya uymaz.
 * Bu bilesen WidgetBuilderForm'da zaten kullanilan inputBase/inputOk class'larini
 * tetikleyici (trigger) butonu olarak kullanir, acilan panel ise DataTypeDropdown
 * ile ayni glassmorphism style'ina sahip.
 *
 * Props:
 *   value         — secili option.value (string)
 *   onChange(v)   — yeni deger secildiginde tetiklenir
 *   options       — [{ value, label, hint? }]
 *   placeholder   — hicbir sey secili degilse gosterilecek metin
 *   triggerClass  — butonun extra class'lari
 *   disabled      — boolean
 *   emptyText     — options bos ise panelde gosterilecek mesaj
 */
function GlassSelect(props) {
  var value        = props.value || ''
  var onChange     = props.onChange
  var options      = Array.isArray(props.options) ? props.options : []
  var placeholder  = props.placeholder || '-- Sec --'
  var triggerClass = props.triggerClass || ''
  var disabled     = props.disabled
  var emptyText    = props.emptyText || 'Secenek yok'

  var [open, setOpen]   = useState(false)
  var [panelPos, setPanelPos] = useState({ top: 0, left: 0, width: 0 })
  var triggerRef = useRef(null)

  // Panel konumunu trigger butonundan hesapla (fixed positioning icin)
  function calcPos() {
    if (!triggerRef.current) return
    var rect = triggerRef.current.getBoundingClientRect()
    setPanelPos({ top: rect.bottom + 4, left: rect.left, width: rect.width })
  }

  function handleOpen() {
    if (disabled) return
    calcPos()
    setOpen(function (o) { return !o })
  }

  useEffect(function () {
    if (!open) return undefined
    function onDocClick(e) {
      if (triggerRef.current && !triggerRef.current.contains(e.target)) setOpen(false)
    }
    function onKey(e) { if (e.key === 'Escape') setOpen(false) }
    function onScroll() { calcPos() }
    document.addEventListener('mousedown', onDocClick)
    document.addEventListener('keydown', onKey)
    window.addEventListener('scroll', onScroll, true)
    return function () {
      document.removeEventListener('mousedown', onDocClick)
      document.removeEventListener('keydown', onKey)
      window.removeEventListener('scroll', onScroll, true)
    }
  }, [open])

  var selected = options.find(function (o) { return o.value === value })

  var panel = open ? createPortal(
    <div
      style={{
        position: 'fixed',
        top: panelPos.top,
        left: panelPos.left,
        width: panelPos.width,
        zIndex: 99999,
        borderRadius: 12,
        overflow: 'hidden',
        background: 'rgba(8, 11, 20, 0.97)',
        backdropFilter: 'blur(24px)',
        WebkitBackdropFilter: 'blur(24px)',
        border: '1px solid rgba(255, 255, 255, 0.12)',
        boxShadow: '0 12px 40px rgba(0, 0, 0, 0.5)',
        maxHeight: 260,
        overflowY: 'auto',
      }}
    >
      {options.length === 0 ? (
        <div style={{ padding: '12px', textAlign: 'center', fontSize: 11, color: 'rgba(255,255,255,0.4)' }}>
          {emptyText}
        </div>
      ) : (
        <>
          <button
            type="button"
            onMouseDown={function (e) { e.preventDefault(); if (onChange) onChange(''); setOpen(false) }}
            style={{
              width: '100%', display: 'flex', alignItems: 'center',
              padding: '8px 12px', background: 'transparent', border: 'none',
              cursor: 'pointer', fontSize: 11, color: 'rgba(255,255,255,0.4)',
              textAlign: 'left',
            }}
          >
            — Temizle —
          </button>
          {options.map(function (o) {
            var isSel = o.value === value
            return (
              <button
                key={o.value}
                type="button"
                onMouseDown={function (e) { e.preventDefault(); if (onChange) onChange(o.value); setOpen(false) }}
                style={{
                  width: '100%', display: 'flex', alignItems: 'center', gap: 8,
                  padding: '10px 12px', background: isSel ? 'rgba(255,255,255,0.08)' : 'transparent',
                  border: 'none', cursor: 'pointer', textAlign: 'left', fontSize: 12,
                  color: isSel ? '#fff' : 'rgba(255,255,255,0.85)',
                  transition: 'background 0.12s',
                }}
                onMouseEnter={function (e) { if (!isSel) e.currentTarget.style.background = 'rgba(255,255,255,0.04)' }}
                onMouseLeave={function (e) { if (!isSel) e.currentTarget.style.background = 'transparent' }}
              >
                <span style={{ flex: 1, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                  {o.label}
                </span>
                {o.hint && (
                  <span style={{ fontSize: 10, color: 'rgba(255,255,255,0.35)', fontFamily: 'monospace' }}>
                    {o.hint}
                  </span>
                )}
                {isSel && <Check size={13} style={{ color: '#818cf8', flexShrink: 0 }} />}
              </button>
            )
          })}
        </>
      )}
    </div>,
    document.body
  ) : null

  return (
    <div style={{ position: 'relative' }}>
      <button
        ref={triggerRef}
        type="button"
        disabled={disabled}
        onClick={handleOpen}
        className={
          'w-full h-9 flex items-center gap-2 px-3 rounded-lg text-xs transition-all ' +
          'bg-white/60 dark:bg-white/[0.04] border ' +
          (open
            ? 'border-indigo-400/60 dark:border-white/20 shadow-[0_0_0_3px_rgba(99,102,241,0.12)]'
            : 'border-slate-200 dark:border-white/[0.08] hover:border-indigo-400/60 dark:hover:border-white/15') +
          ' ' + (disabled ? 'opacity-50 cursor-not-allowed' : 'cursor-pointer') + ' ' + triggerClass
        }
      >
        <span className={
          'flex-1 text-left truncate ' +
          (selected
            ? 'text-slate-800 dark:text-white/85 font-medium'
            : 'text-slate-400 dark:text-white/50')
        }>
          {selected ? selected.label : placeholder}
        </span>
        <ChevronDown
          size={14}
          className={
            'text-slate-400 dark:text-white/40 transition-transform duration-150 ' +
            (open ? 'rotate-180' : '')
          }
        />
      </button>
      {panel}
    </div>
  )
}

export default function OptionsModal(props) {
  var isOpen         = props.isOpen
  var onClose        = props.onClose
  var initialOptions = Array.isArray(props.initialOptions) ? props.initialOptions : []
  var onSaved        = props.onSaved
  var mode = (props.mode === 'link' || props.mode === 'lookup' || props.mode === 'grid')
    ? props.mode
    : 'options'

  // Options mode state: [{ label }] — sadece label tutuluyor.
  var [options, setOptions] = useState([])
  // Link mode state: tek URL sablonu string'i.
  var [linkUrl, setLinkUrl] = useState('')
  // Lookup mode state: secilen guideCode + katalog (API'den)
  var [lookupCode, setLookupCode]       = useState('')
  var [guides, setGuides]               = useState([])
  var [loadingGuides, setLoadingGuides] = useState(false)
  // Grid mode state: secilen childFormCode + form katalogu (API'den)
  var [gridFormCode, setGridFormCode]   = useState('')
  var [forms, setForms]                 = useState([])
  var [loadingForms, setLoadingForms]   = useState(false)
  var [errors, setErrors]               = useState({})

  // Modal her acildiginda initial'dan hydrate
  useEffect(function () {
    if (!isOpen) return
    if (mode === 'link') {
      var tpl = ''
      if (initialOptions.length > 0) {
        tpl = typeof initialOptions[0] === 'string' ? initialOptions[0] : ''
      }
      setLinkUrl(tpl)
      setErrors({})
    } else if (mode === 'lookup') {
      var code = ''
      if (initialOptions.length > 0) {
        code = typeof initialOptions[0] === 'string' ? initialOptions[0] : ''
      }
      setLookupCode(code)
      setErrors({})
      // Guide katalogunu cek
      setLoadingGuides(true)
      fetch('/api/guides')
        .then(function (r) { return r.ok ? r.json() : [] })
        .then(function (list) { setGuides(Array.isArray(list) ? list : []) })
        .catch(function () { setGuides([]) })
        .finally(function () { setLoadingGuides(false) })
    } else if (mode === 'grid') {
      var fc = ''
      if (initialOptions.length > 0) {
        fc = typeof initialOptions[0] === 'string' ? initialOptions[0] : ''
      }
      setGridFormCode(fc)
      setErrors({})
      // Form katalogunu cek — admin whitelist'indeki tum formlar
      setLoadingForms(true)
      fetch('/api/widgets/forms')
        .then(function (r) { return r.ok ? r.json() : [] })
        .then(function (list) { setForms(Array.isArray(list) ? list : []) })
        .catch(function () { setForms([]) })
        .finally(function () { setLoadingForms(false) })
    } else {
      // options mode: string[] veya legacy obje dizisi
      var clone = initialOptions.map(function (o) {
        if (typeof o === 'string') return { label: o }
        return { label: o.optionLabel || o.label || '' }
      })
      setOptions(clone)
      setErrors({})
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isOpen, mode])

  function addOption() {
    setOptions(function (prev) {
      return prev.concat([{ label: '' }])
    })
  }

  function updateOption(index, label) {
    setOptions(function (prev) {
      return prev.map(function (o, i) {
        return i === index ? { label: label } : o
      })
    })
    if (errors.optionRows && errors.optionRows[index]) {
      setErrors(function (prev) {
        var copy = Object.assign({}, prev)
        if (copy.optionRows) {
          var rows = Object.assign({}, copy.optionRows)
          delete rows[index]
          copy.optionRows = rows
        }
        return copy
      })
    }
  }

  function removeOption(index) {
    setOptions(function (prev) {
      return prev.filter(function (_, i) { return i !== index })
    })
  }

  function validate() {
    var e = {}
    if (mode === 'link') {
      var url = (linkUrl || '').trim()
      if (!url) {
        e.general = 'Hedef URL zorunlu'
      } else if (url.indexOf('{value}') === -1) {
        e.general = "URL icinde '{value}' yer tutucusu bulunmali"
      }
      return e
    }
    if (mode === 'lookup') {
      if (!lookupCode || !lookupCode.trim()) {
        e.general = 'Bir rehber secin'
      }
      return e
    }
    if (mode === 'grid') {
      if (!gridFormCode || !gridFormCode.trim()) {
        e.general = 'Bir alt form secin'
      }
      return e
    }
    if (options.length === 0) {
      e.general = 'En az bir seçenek ekleyin'
      return e
    }
    var rowErrors = {}
    var seenLabels = new Set()
    for (var i = 0; i < options.length; i++) {
      var label = (options[i].label || '').trim()
      var rowErr = []
      if (!label) rowErr.push('Etiket zorunlu')
      else if (seenLabels.has(label.toLowerCase())) rowErr.push('Tekrarlı etiket')
      else seenLabels.add(label.toLowerCase())
      if (rowErr.length > 0) rowErrors[i] = rowErr.join(', ')
    }
    if (Object.keys(rowErrors).length > 0) e.optionRows = rowErrors
    return e
  }

  function handleSave() {
    var e = validate()
    if (Object.keys(e).length > 0) {
      setErrors(e)
      return
    }
    if (mode === 'link') {
      if (onSaved) onSaved([(linkUrl || '').trim()])
      return
    }
    if (mode === 'lookup') {
      if (onSaved) onSaved([(lookupCode || '').trim()])
      return
    }
    if (mode === 'grid') {
      if (onSaved) onSaved([(gridFormCode || '').trim()])
      return
    }
    if (onSaved) {
      // Sadece label dizisi dondur — Faz A string[] kontrati
      var labels = options
        .map(function (o) { return (o.label || '').trim() })
        .filter(function (l) { return l.length > 0 })
      onSaved(labels)
    }
  }

  // Input stil helper'lari (WidgetBuilderForm ile tutarli)
  var inputBase = 'w-full h-9 px-3 rounded-lg text-xs transition-all ' +
    'bg-white/60 dark:bg-white/[0.04] ' +
    'text-slate-800 dark:text-white/90 ' +
    'placeholder:text-slate-400 dark:placeholder:text-white/40 ' +
    'focus:outline-none '
  var inputOk = 'border border-slate-200 dark:border-white/[0.08] focus:border-indigo-400/60 dark:focus:border-white/20 focus:shadow-[0_0_0_3px_rgba(99,102,241,0.12)]'
  var inputErr = 'border border-red-400/60 focus:border-red-400/80 focus:shadow-[0_0_0_3px_rgba(239,68,68,0.15)]'

  var footer = (
    <>
      <span className="text-[11px] text-slate-400 dark:text-white/50">
        {mode === 'link'
          ? (linkUrl ? 'URL tanimli' : 'URL bos')
          : mode === 'lookup'
            ? (lookupCode ? 'Rehber: ' + lookupCode : 'Rehber secilmedi')
            : mode === 'grid'
              ? (gridFormCode ? 'Alt form: ' + gridFormCode : 'Alt form secilmedi')
              : options.length + ' seçenek'}
      </span>
      <div className="flex-1" />
      <button
        type="button"
        onClick={onClose}
        className="px-4 py-2 rounded-xl bg-white/[0.04] hover:bg-white/[0.08] border border-slate-200 dark:border-white/[0.08] text-xs font-medium text-slate-600 dark:text-white/60 hover:text-slate-900 dark:hover:text-white/85 transition-all"
      >
        İptal
      </button>
      <button
        type="button"
        onClick={handleSave}
        className="flex items-center gap-1.5 px-4 py-2 rounded-xl bg-indigo-500 hover:bg-indigo-600 dark:bg-indigo-500/25 dark:hover:bg-indigo-500/35 border border-indigo-500 dark:border-indigo-400/30 text-xs font-semibold text-white dark:text-indigo-200 transition-all shadow-sm"
      >
        <Check size={13} strokeWidth={2.4} />
        Tamam
      </button>
    </>
  )

  // ── LINK MODU ────────────────────────────────────────────────
  if (mode === 'link') {
    var linkTemplateOptions = LINK_TEMPLATES.map(function (t) {
      return { value: t.value, label: t.label }
    })
    var selectedLinkLabel = LINK_TEMPLATES.find(function (t) { return t.value === linkUrl })
    return (
      <AdminMiniModal
        isOpen={isOpen}
        onClose={onClose}
        title="Hedef URL Sablonu"
        subtitle="Kullanici bu alana yazacagi deger, URL icindeki {value} ile yer degistirir"
        icon={ExternalLink}
        iconColor="violet"
        maxWidth="max-w-md"
        footer={footer}
      >
        <div className="flex flex-col gap-3">
          {errors.general && (
            <p className="text-[11px] text-red-600 dark:text-red-400/90 bg-red-500/5 border border-red-400/20 rounded-lg px-3 py-2">
              {errors.general}
            </p>
          )}

          {/* Hazir Sablonlar — GlassSelect ile */}
          <div className="flex flex-col gap-1">
            <label className="text-[10px] font-bold uppercase tracking-wider text-slate-400 dark:text-white/50">
              Hazır Şablonlar
            </label>
            <GlassSelect
              value={linkUrl}
              onChange={function (val) { setLinkUrl(val || ''); if (errors.general) setErrors({}) }}
              options={linkTemplateOptions}
              placeholder="-- Şablon seç --"
              emptyText="Şablon bulunamadı"
            />
          </div>

          {/* Secilen URL - readonly goster */}
          {linkUrl && (
            <div className="text-[10px] leading-relaxed text-slate-500 dark:text-white/40 bg-violet-500/5 border border-violet-400/20 rounded-lg px-3 py-2 break-all">
              <strong className="text-violet-600 dark:text-violet-300">URL:</strong>{' '}
              <code className="text-[10px]">{linkUrl}</code>
            </div>
          )}
        </div>
      </AdminMiniModal>
    )
  }

  // ── LOOKUP MODU ──────────────────────────────────────────────
  if (mode === 'lookup') {
    var selectedGuide = guides.find(function (g) { return g.guideCode === lookupCode })
    var guideOptions = guides.map(function (g) {
      return { value: g.guideCode, label: g.guideLabel, hint: g.guideCode }
    })
    return (
      <AdminMiniModal
        isOpen={isOpen}
        onClose={onClose}
        title="Rehber (Lookup) Sec"
        subtitle="Kullanici bu widget'a deger girerken secili rehberde arama yapacak"
        icon={Search}
        iconColor="amber"
        maxWidth="max-w-md"
        footer={footer}
      >
        <div className="flex flex-col gap-3">
          {errors.general && (
            <p className="text-[11px] text-red-600 dark:text-red-400/90 bg-red-500/5 border border-red-400/20 rounded-lg px-3 py-2">
              {errors.general}
            </p>
          )}

          <div className="flex flex-col gap-1">
            <label className="text-[10px] font-bold uppercase tracking-wider text-slate-400 dark:text-white/50">
              Rehber
            </label>
            {loadingGuides ? (
              <div className="text-[12px] text-slate-400 dark:text-white/50 py-2">
                Rehberler yukleniyor…
              </div>
            ) : guides.length === 0 ? (
              <div className="text-[11px] text-amber-600 dark:text-amber-400/80 bg-amber-500/5 border border-amber-400/20 rounded-lg px-3 py-2">
                Henuz tanimli rehber yok. SSMS'ten <code>v_Guide*</code> view'i olustur + restart.
              </div>
            ) : (
              <GlassSelect
                value={lookupCode}
                onChange={function (val) {
                  setLookupCode(val)
                  if (errors.general) setErrors({})
                }}
                options={guideOptions}
                placeholder="-- Rehber sec --"
                emptyText="Tanimli rehber yok"
              />
            )}
          </div>

          {selectedGuide && (
            <div className="text-[10px] leading-relaxed text-slate-500 dark:text-white/40 bg-amber-500/5 border border-amber-400/20 rounded-lg px-3 py-2">
              <div><strong className="text-amber-600 dark:text-amber-300">Gosterim:</strong> {selectedGuide.displayColumn}</div>
              <div><strong className="text-amber-600 dark:text-amber-300">Kaydedilen:</strong> {selectedGuide.valueColumn}</div>
              <div><strong className="text-amber-600 dark:text-amber-300">Kolonlar:</strong> {(selectedGuide.columns || []).join(', ')}</div>
            </div>
          )}
        </div>
      </AdminMiniModal>
    )
  }

  // ── GRID MODU ────────────────────────────────────────────────
  if (mode === 'grid') {
    var selectedForm = forms.find(function (f) { return f.formCode === gridFormCode })
    var formOptions = forms.map(function (f) {
      return { value: f.formCode, label: f.formName, hint: f.formCode }
    })
    return (
      <AdminMiniModal
        isOpen={isOpen}
        onClose={onClose}
        title="Alt Tablo (Grid) Sec"
        subtitle="Bu widget master form icinde hangi alt form satirlarini gosterecek?"
        icon={Table}
        iconColor="blue"
        maxWidth="max-w-md"
        footer={footer}
      >
        <div className="flex flex-col gap-3">
          {errors.general && (
            <p className="text-[11px] text-red-600 dark:text-red-400/90 bg-red-500/5 border border-red-400/20 rounded-lg px-3 py-2">
              {errors.general}
            </p>
          )}

          <div className="flex flex-col gap-1">
            <label className="text-[10px] font-bold uppercase tracking-wider text-slate-400 dark:text-white/50">
              Alt Form
            </label>
            {loadingForms ? (
              <div className="text-[12px] text-slate-400 dark:text-white/50 py-2">
                Formlar yukleniyor…
              </div>
            ) : forms.length === 0 ? (
              <div className="text-[11px] text-blue-600 dark:text-blue-400/80 bg-blue-500/5 border border-blue-400/20 rounded-lg px-3 py-2">
                Henuz tanimli form yok.
              </div>
            ) : (
              <GlassSelect
                value={gridFormCode}
                onChange={function (val) {
                  setGridFormCode(val)
                  if (errors.general) setErrors({})
                }}
                options={formOptions}
                placeholder="-- Alt form sec --"
                emptyText="Tanimli form yok"
              />
            )}
          </div>

          {selectedForm && (
            <div className="text-[10px] leading-relaxed text-slate-500 dark:text-white/40 bg-blue-500/5 border border-blue-400/20 rounded-lg px-3 py-2">
              <div><strong className="text-blue-600 dark:text-blue-300">Modul:</strong> {selectedForm.module}</div>
              {selectedForm.subModule && (
                <div><strong className="text-blue-600 dark:text-blue-300">Alt Modul:</strong> {selectedForm.subModule}</div>
              )}
              <div className="mt-1 text-slate-500 dark:text-white/35">
                Ana form kaydedildiginde, bu tablodaki her satir alt form'a ait bir child kayit olarak yazilir.
              </div>
            </div>
          )}
        </div>
      </AdminMiniModal>
    )
  }

  // ── OPTIONS MODU (mevcut) ────────────────────────────────────

  return (
    <AdminMiniModal
      isOpen={isOpen}
      onClose={onClose}
      title="Seçenekleri Düzenle"
      subtitle="Kullanıcıya gösterilecek seçenek adlarını girin"
      icon={List}
      iconColor="indigo"
      maxWidth="max-w-md"
      footer={footer}
    >
      <div className="flex flex-col gap-3">
        {errors.general && (
          <p className="text-[11px] text-red-600 dark:text-red-400/90 bg-red-500/5 border border-red-400/20 rounded-lg px-3 py-2">
            {errors.general}
          </p>
        )}

        {options.length === 0 ? (
          <div className="text-center py-10 border-2 border-dashed border-slate-200 dark:border-white/[0.08] rounded-xl">
            <List size={24} className="mx-auto text-slate-300 dark:text-white/40 mb-2" strokeWidth={1.5} />
            <p className="text-[12px] text-slate-400 dark:text-white/50">
              Henüz seçenek eklenmemiş
            </p>
            <p className="text-[10px] text-slate-400 dark:text-white/45 mt-0.5">
              Aşağıdaki butonla en az bir seçenek ekleyin
            </p>
          </div>
        ) : (
          <div className="flex flex-col gap-2" data-options-list>
            {/* Header row — tek sutun (sadece Seçenek Adı) */}
            <div className="flex items-center gap-2 px-1 text-[9px] font-bold uppercase tracking-wider text-slate-400 dark:text-white/50">
              <span className="flex-1">Seçenek Adı</span>
              <span className="w-7" />
            </div>
            {options.map(function (opt, i) {
              var rowErr = errors.optionRows && errors.optionRows[i]
              return (
                <div key={i} className="flex flex-col gap-1">
                  <div className="flex items-center gap-2">
                    <input
                      type="text"
                      value={opt.label}
                      onChange={function (e) { updateOption(i, e.target.value) }}
                      onKeyDown={function (e) {
                        if (e.key === 'Enter') {
                          e.preventDefault()
                          if (!opt.label.trim()) return
                          addOption()
                          setTimeout(function () {
                            var inputs = e.target.closest('[data-options-list]')
                            if (!inputs) return
                            var all = inputs.querySelectorAll('input[type="text"]')
                            if (all.length > 0) all[all.length - 1].focus()
                          }, 30)
                        }
                      }}
                      placeholder="Ör. Küçük"
                      className={inputBase + 'flex-1 ' + (rowErr ? inputErr : inputOk)}
                    />
                    <button
                      type="button"
                      onClick={function () { removeOption(i) }}
                      className="w-7 h-9 flex items-center justify-center rounded-lg bg-white/[0.04] hover:bg-red-500/15 border border-slate-200 dark:border-white/[0.08] hover:border-red-400/40 text-slate-400 hover:text-red-500 transition-all flex-shrink-0"
                      title="Sil"
                    >
                      <X size={12} strokeWidth={2.4} />
                    </button>
                  </div>
                  {rowErr && (
                    <p className="text-[10px] text-red-600 dark:text-red-400/80 pl-0.5">{rowErr}</p>
                  )}
                </div>
              )
            })}
          </div>
        )}

        <button
          type="button"
          onClick={addOption}
          className="w-full h-9 flex items-center justify-center gap-1.5 rounded-lg bg-indigo-500/5 hover:bg-indigo-500/10 dark:bg-indigo-500/10 dark:hover:bg-indigo-500/20 border border-dashed border-indigo-400/30 dark:border-indigo-400/40 text-[11px] font-semibold text-indigo-600 dark:text-indigo-300 transition-all"
        >
          <Plus size={12} strokeWidth={2.4} />
          Seçenek Ekle
        </button>
      </div>
    </AdminMiniModal>
  )
}
