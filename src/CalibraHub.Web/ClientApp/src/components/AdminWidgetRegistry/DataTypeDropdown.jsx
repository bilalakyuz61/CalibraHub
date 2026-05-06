/**
 * DataTypeDropdown — Custom ikonlu dropdown (Faz B)
 *
 * Mevcut `<select>` yerine sik bir acilir liste. Her secenek:
 *   [icon kutusu] label (secili ise Check ikonu)
 *
 * Value: Yeni EAV API key'leri — kucuk harfli, kisa cizgili.
 *   'text', 'numeric', 'date', 'boolean', 'dropdown', 'multi-select', 'link'
 *
 * Faz A'daki "numeric" tek tip (eski Integer + Decimal birlesti).
 * 'link' tipi: OptionsJSON[0] = URL sablonu, {value} runtime'da kullanicinin
 * girdigi ile yer degistirir. Edit sayfalarinda input + "Git" butonu cizer.
 * Gorsel stiller (Glassmorphism, Tailwind class'lari) aynen korundu.
 */
import { useState, useEffect, useRef } from 'react'
import { motion, AnimatePresence } from 'framer-motion'
import { ChevronDown, Check } from 'lucide-react'
import { resolveIcon, resolveColor } from '../CalibraSmartBoard/DynamicWidgetFactory'

/**
 * Veri tipleri — Yeni EAV (WidgetMas) API key'leri, kucuk harfli.
 * Her biri icin icon + renk + Turkce label.
 */
export var DATA_TYPES = [
  { value: 'text',         label: 'Metin',            icon: 'FileText',     color: 'slate' },
  { value: 'numeric',      label: 'Sayi',             icon: 'Hash',         color: 'blue' },
  { value: 'date',         label: 'Tarih',            icon: 'Calendar',     color: 'cyan' },
  { value: 'boolean',      label: 'Evet / Hayir',     icon: 'CheckCircle',  color: 'emerald' },
  { value: 'dropdown',     label: 'Secim Listesi',    icon: 'List',         color: 'teal' },
  { value: 'multi-select', label: 'Coklu Secim',      icon: 'Layers',       color: 'teal' },
  { value: 'link',         label: 'Baglanti',         icon: 'ExternalLink', color: 'violet' },
  // 'lookup' ve 'grid' eski tipler — yeni tanim listesinden gizlendi (hidden:true).
  // Eski tanimlanmis widget'lar edit acilinca DATA_TYPES.find(...) ile yine bulunur,
  // ekranda dogru ikon/renk goster — sadece dropdown listesinde gorunmez.
  { value: 'lookup',       label: 'Rehber (Lookup)',  icon: 'Search',       color: 'amber', hidden: true },
  { value: 'guide-list',   label: 'Rehber Listesi',   icon: 'Table',        color: 'amber' },
  { value: 'grid',         label: 'Alt Tablo',        icon: 'Table',        color: 'blue',  hidden: true },
]

export default function DataTypeDropdown(props) {
  var value = props.value || 'text'
  var onChange = props.onChange
  var disabled = props.disabled

  var [open, setOpen] = useState(false)
  var wrapperRef = useRef(null)

  // Disari tiklama -> kapat
  useEffect(function() {
    if (!open) return undefined
    function onDocClick(e) {
      if (wrapperRef.current && !wrapperRef.current.contains(e.target)) {
        setOpen(false)
      }
    }
    document.addEventListener('mousedown', onDocClick)
    return function() { document.removeEventListener('mousedown', onDocClick) }
  }, [open])

  var selected = DATA_TYPES.find(function(t) { return t.value === value }) || DATA_TYPES[0]
  var SelectedIcon = resolveIcon(selected.icon)
  var selectedPalette = resolveColor(selected.color)

  return (
    <div className="relative" ref={wrapperRef}>
      <button
        type="button"
        disabled={disabled}
        onClick={function() { setOpen(function(o) { return !o }) }}
        className={
          'w-full h-9 flex items-center gap-2 px-2.5 rounded-lg text-xs transition-all ' +
          'bg-white/60 dark:bg-white/[0.04] border border-slate-200 dark:border-white/[0.08] ' +
          'text-slate-800 dark:text-white/85 ' +
          (disabled
            ? 'opacity-50 cursor-not-allowed'
            : 'hover:border-indigo-400/60 dark:hover:border-white/15 cursor-pointer') +
          (open ? ' border-indigo-400/60 dark:border-white/20 shadow-[0_0_0_3px_rgba(99,102,241,0.12)]' : '')
        }
      >
        <div
          className="w-5 h-5 rounded flex items-center justify-center flex-shrink-0"
          style={{ background: selectedPalette.bg, border: '1px solid ' + selectedPalette.border }}
        >
          <SelectedIcon size={11} style={{ color: selectedPalette.icon }} strokeWidth={1.8} />
        </div>
        <span className="flex-1 text-left font-medium">{selected.label}</span>
        <motion.span
          animate={{ rotate: open ? 180 : 0 }}
          transition={{ duration: 0.2 }}
          className="text-slate-400 dark:text-white/30"
        >
          <ChevronDown size={14} />
        </motion.span>
      </button>

      <AnimatePresence>
        {open && (
          <motion.div
            initial={{ opacity: 0, y: -6, scale: 0.98 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, y: -6, scale: 0.98 }}
            transition={{ duration: 0.15, ease: [0.23, 1, 0.32, 1] }}
            className="absolute left-0 right-0 top-full mt-1 z-50 rounded-xl overflow-hidden"
            style={{
              background: 'rgba(8, 11, 20, 0.96)',
              backdropFilter: 'blur(24px)',
              WebkitBackdropFilter: 'blur(24px)',
              border: '1px solid rgba(255, 255, 255, 0.12)',
              boxShadow: '0 12px 40px rgba(0, 0, 0, 0.4)',
            }}
          >
            {DATA_TYPES.filter(function(t) { return !t.hidden }).map(function(t) {
              var palette = resolveColor(t.color)
              var Icon = resolveIcon(t.icon)
              var isSel = t.value === value
              return (
                <button
                  key={t.value}
                  type="button"
                  onClick={function() {
                    if (onChange) onChange(t.value)
                    setOpen(false)
                  }}
                  className={
                    'w-full flex items-center gap-3 px-3 py-2.5 transition-colors text-left ' +
                    (isSel ? 'bg-white/[0.08]' : 'hover:bg-white/[0.04]')
                  }
                >
                  <div
                    className="w-7 h-7 rounded-lg flex items-center justify-center flex-shrink-0"
                    style={{ background: palette.bg, border: '1px solid ' + palette.border }}
                  >
                    <Icon size={14} style={{ color: palette.icon }} strokeWidth={1.8} />
                  </div>
                  <span className="text-sm text-white/85 flex-1">{t.label}</span>
                  {isSel && <Check size={14} className="text-indigo-400 flex-shrink-0" />}
                </button>
              )
            })}
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  )
}
