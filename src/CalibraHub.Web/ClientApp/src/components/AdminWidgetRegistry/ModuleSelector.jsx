/**
 * ModuleSelector — Form (screen) secim dropdown'u
 *
 * Eski sekme bar (yan yana tab) yerine tek secimli, ikonlu dropdown.
 * Yer tasarrufu saglar ve ileride 5-6+ modul eklendiginde sekme bar dar
 * kalmayacak sekilde olceklenir.
 *
 * Sadece `UsesMaterialCardSchema = true` olan screen'ler gosterilir:
 *   - material_cards  → Malzeme Kartlari
 *   - contact_accounts → Cari Hesaplar
 */
import { useState, useEffect, useRef } from 'react'
import { motion, AnimatePresence } from 'framer-motion'
import { ChevronDown, Check } from 'lucide-react'
import { resolveIcon, resolveColor } from '../CalibraSmartBoard/DynamicWidgetFactory'

/**
 * Faz B — Form katalogu metadata'si. Anahtar: FormCode (dbo.Forms.FormCode).
 * Backend whitelist (Faz B): ITEMS, CONTACTS, SALES_QUOTE_EDIT, PRODUCT_TREES, PRODUCT_CONFIG
 * Ayrica geriye donuk uyumluluk icin eski snake_case kodlari da tutuyoruz.
 */
var MODULE_META = {
  // Yeni Faz B FormMas kodlari (dbo.Forms)
  ITEMS:             { label: 'Malzeme Kartları',              icon: 'Package',   color: 'indigo' },
  CONTACTS:          { label: 'Cari Hesaplar',                 icon: 'Building2', color: 'cyan'   },
  SALES_QUOTE_EDIT:  { label: 'Satış Teklifi — Üst Bilgi',     icon: 'FileText',  color: 'violet' },
  SALES_QUOTE_LINES: { label: 'Satış Teklifi — Kalem Bilgisi', icon: 'List',      color: 'amber'  },
  PRODUCT_TREES:     { label: 'Ürün Ağacı',                    icon: 'GitBranch', color: 'emerald'},
  PRODUCT_CONFIG:    { label: 'Ürün Konfigürasyonu',           icon: 'Sliders',   color: 'teal'   },
  // Legacy (eski admin akisi)
  material_cards:        { label: 'Malzeme Kartları',    icon: 'Package',   color: 'indigo' },
  contact_accounts:      { label: 'Cari Hesaplar',       icon: 'Building2', color: 'cyan'   },
  sales_quotes:          { label: 'Satış Teklifleri',    icon: 'FileText',  color: 'violet' },
  product_configuration: { label: 'Ürün Konfigürasyonu', icon: 'Sliders',   color: 'teal'   },
}

export default function ModuleSelector(props) {
  var options = Array.isArray(props.options) ? props.options : []
  var selectedCode = props.selectedCode
  var onChange = props.onChange
  var trailing = props.trailing || null

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

  // Sadece MaterialCardSchema kullanan modulleri filtrele
  var supported = options.filter(function(opt) {
    return MODULE_META[opt.value] != null
  })

  if (supported.length === 0) {
    return null
  }

  var selectedOpt = supported.find(function(o) { return o.value === selectedCode }) || supported[0]
  var selectedMeta = MODULE_META[selectedOpt.value]
  var selectedPalette = resolveColor(selectedMeta.color)
  var SelectedIcon = resolveIcon(selectedMeta.icon)

  // Body grid'i ile ayni olculer: grid-cols-[1fr_2fr] + gap-4 + px-5.
  // Secici 1fr (sol form eninde), trailing 2fr (sag liste eninde).
  // Trigger w-full ile kolon genisligini kaplar — secilen formun etiketine
  // gore boyutu degismez.
  return (
    <div className="px-5 py-2.5 border-b border-slate-200/50 dark:border-white/[0.06] flex-shrink-0 grid grid-cols-1 md:grid-cols-[1fr_2fr] gap-4 items-center">
      <div className="relative min-w-0" ref={wrapperRef}>
        {/* Trigger — kompakt: sadece ikon + etiket + chevron */}
        <button
          type="button"
          onClick={function() { setOpen(function(o) { return !o }) }}
          className={
            'w-full flex items-center gap-2 pl-2 pr-3 py-1.5 rounded-lg text-[13px] font-semibold transition-all ' +
            'bg-white/70 dark:bg-white/[0.04] border text-slate-800 dark:text-white/90 ' +
            (open
              ? 'border-indigo-400/60 dark:border-white/20 shadow-[0_0_0_3px_rgba(99,102,241,0.12)]'
              : 'border-slate-200 dark:border-white/[0.08] hover:border-indigo-400/40 dark:hover:border-white/15')
          }
        >
          <div
            className="w-6 h-6 rounded-md flex items-center justify-center flex-shrink-0"
            style={{ background: selectedPalette.bg, border: '1px solid ' + selectedPalette.border }}
          >
            <SelectedIcon size={12} style={{ color: selectedPalette.icon }} strokeWidth={1.8} />
          </div>
          <span className="flex-1 min-w-0 text-left truncate">{selectedMeta.label}</span>
          <motion.span
            animate={{ rotate: open ? 180 : 0 }}
            transition={{ duration: 0.2 }}
            className="text-slate-400 dark:text-white/30 flex-shrink-0"
          >
            <ChevronDown size={13} />
          </motion.span>
        </button>

        {/* Dropdown panel */}
        <AnimatePresence>
          {open && (
            <motion.div
              initial={{ opacity: 0, y: -6, scale: 0.98 }}
              animate={{ opacity: 1, y: 0, scale: 1 }}
              exit={{ opacity: 0, y: -6, scale: 0.98 }}
              transition={{ duration: 0.15, ease: [0.23, 1, 0.32, 1] }}
              className="absolute left-0 top-full mt-1 z-50 rounded-xl overflow-hidden whitespace-nowrap"
              style={{
                minWidth: '100%',
                background: 'rgba(8, 11, 20, 0.96)',
                backdropFilter: 'blur(24px)',
                WebkitBackdropFilter: 'blur(24px)',
                border: '1px solid rgba(255, 255, 255, 0.12)',
                boxShadow: '0 12px 40px rgba(0, 0, 0, 0.4)',
              }}
            >
              {supported.map(function(opt) {
                var meta = MODULE_META[opt.value]
                var palette = resolveColor(meta.color)
                var Icon = resolveIcon(meta.icon)
                var isSel = opt.value === selectedCode
                return (
                  <button
                    key={opt.value}
                    type="button"
                    onClick={function() {
                      if (onChange) onChange(opt.value)
                      setOpen(false)
                    }}
                    className={
                      'w-full flex items-center gap-3 px-4 py-2.5 transition-colors text-left ' +
                      (isSel ? 'bg-white/[0.08]' : 'hover:bg-white/[0.04]')
                    }
                  >
                    <div
                      className="w-7 h-7 rounded-lg flex items-center justify-center flex-shrink-0"
                      style={{ background: palette.bg, border: '1px solid ' + palette.border }}
                    >
                      <Icon size={14} style={{ color: palette.icon }} strokeWidth={1.8} />
                    </div>
                    <div className="flex flex-col flex-1 min-w-0">
                      <span className="text-sm font-semibold text-white/90">{meta.label}</span>
                      <span className="text-[10px] font-mono text-white/35">{opt.value}</span>
                    </div>
                    {isSel && <Check size={14} className="text-indigo-400 flex-shrink-0" />}
                  </button>
                )
              })}
            </motion.div>
          )}
        </AnimatePresence>
      </div>
      {trailing}
    </div>
  )
}
