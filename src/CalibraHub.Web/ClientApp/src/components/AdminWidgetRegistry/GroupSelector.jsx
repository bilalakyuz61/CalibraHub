/**
 * GroupSelector — Widget grubu dropdown'i (yalnizca secim).
 *
 * Eskiden inline "Yeni Grup Olustur" form'u da bu bilesen icindeydi; o simdi
 * WidgetBuilderForm tarafindaki GroupModal'a tasindi. Bu component artik sadece
 * mevcut gruplar arasindan secim yapmak icin kullaniliyor.
 *
 * Props:
 *   - groups:   [{ id, groupKey, groupLabel, displayOrder, isActive }, ...]
 *   - value:    secili groupId (null = "Grupsuz")
 *   - onChange: function(groupId|null)
 *   - disabled: bool (edit modunda veya saving sirasinda)
 */
import { useState, useEffect, useRef } from 'react'
import { motion, AnimatePresence } from 'framer-motion'
import { ChevronDown, Check, Layers } from 'lucide-react'
import { resolveColor } from '../CalibraSmartBoard/DynamicWidgetFactory'

// Tema algilama — SmartBoard/SmartCard ile ayni pattern (body.app-theme-dark +
// MutationObserver). Dropdown arka plani ve row text renklerini senkronlar.
function useIsDark() {
  var [isDark, setIsDark] = useState(function () {
    if (typeof document === 'undefined') return true
    return document.body.classList.contains('app-theme-dark') ||
           document.documentElement.classList.contains('dark')
  })
  useEffect(function () {
    function sync() {
      setIsDark(
        document.body.classList.contains('app-theme-dark') ||
        document.documentElement.classList.contains('dark')
      )
    }
    sync()
    var obs = new MutationObserver(sync)
    obs.observe(document.body, { attributes: true, attributeFilter: ['class'] })
    obs.observe(document.documentElement, { attributes: true, attributeFilter: ['class'] })
    return function () { obs.disconnect() }
  }, [])
  return isDark
}

export default function GroupSelector(props) {
  var groups = Array.isArray(props.groups) ? props.groups : []
  var value = props.value || null
  var onChange = props.onChange
  var disabled = props.disabled === true

  var [open, setOpen] = useState(false)
  var wrapperRef = useRef(null)
  var isDark = useIsDark()

  // Disari tiklama kapatir
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

  var selected = groups.find(function(g) { return g.id === value }) || null
  var palette = resolveColor('slate')
  var selectedPalette = resolveColor('teal')

  return (
    <div className="relative" ref={wrapperRef}>
      <button
        type="button"
        disabled={disabled}
        onClick={function() { if (!disabled) setOpen(function(o) { return !o }) }}
        className={
          'w-full h-9 flex items-center gap-2 px-2.5 rounded-lg text-xs transition-all ' +
          'bg-white/60 dark:bg-white/[0.04] border text-slate-800 dark:text-white/85 ' +
          (open
            ? 'border-indigo-400/60 dark:border-white/20 shadow-[0_0_0_3px_rgba(99,102,241,0.12)]'
            : 'border-slate-200 dark:border-white/[0.08] hover:border-indigo-400/40 dark:hover:border-white/15') +
          (disabled ? ' opacity-50 cursor-not-allowed' : '')
        }
      >
        <div
          className="w-5 h-5 rounded flex items-center justify-center flex-shrink-0"
          style={{
            background: selected ? selectedPalette.bg : palette.bg,
            border: '1px solid ' + (selected ? selectedPalette.border : palette.border),
          }}
        >
          <Layers
            size={11}
            style={{ color: selected ? selectedPalette.icon : palette.icon }}
            strokeWidth={1.8}
          />
        </div>
        <span className="flex-1 text-left truncate">
          {selected ? selected.groupLabel : <span className="text-slate-400 dark:text-white/30">Grupsuz</span>}
        </span>
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
            className="absolute left-0 top-full mt-1 z-50 rounded-xl overflow-hidden"
            style={{
              background: isDark ? 'rgba(8, 11, 20, 0.96)' : 'rgba(255, 255, 255, 0.98)',
              backdropFilter: 'blur(24px)',
              WebkitBackdropFilter: 'blur(24px)',
              border: isDark ? '1px solid rgba(255, 255, 255, 0.12)' : '1px solid rgba(15, 23, 42, 0.1)',
              boxShadow: isDark ? '0 12px 40px rgba(0, 0, 0, 0.4)' : '0 12px 40px rgba(15, 23, 42, 0.15)',
              maxHeight: 320,
              overflowY: 'auto',
              // Dropdown tetigiden genis olsun → grup adlari kesilmesin.
              // min: tetigin genisligi (100%), max: formun oldukca dismaya acabilir
              minWidth: '100%',
              width: 'max-content',
              maxWidth: 420,
            }}
          >
            {/* Grupsuz secenegi */}
            <button
              type="button"
              onClick={function() {
                if (onChange) onChange(null)
                setOpen(false)
              }}
              className={
                'w-full flex items-center gap-3 px-4 py-2.5 transition-colors text-left ' +
                (value === null
                  ? (isDark ? 'bg-white/[0.08]' : 'bg-slate-100')
                  : (isDark ? 'hover:bg-white/[0.04]' : 'hover:bg-slate-50'))
              }
            >
              <div
                className="w-6 h-6 rounded-lg flex items-center justify-center flex-shrink-0"
                style={{
                  background: palette.bg,
                  border: '1px solid ' + palette.border,
                }}
              >
                <Layers size={12} style={{ color: palette.icon }} strokeWidth={1.8} />
              </div>
              <span className={'flex-1 text-sm ' + (isDark ? 'text-white/60' : 'text-slate-500')}>
                Grupsuz
              </span>
              {value === null && <Check size={13} className="flex-shrink-0 text-indigo-500 dark:text-indigo-400" />}
            </button>

            {/* Mevcut gruplar — sadece groupLabel gosteriliyor (groupKey gizlendi) */}
            {groups.map(function(g) {
              var isSel = g.id === value
              return (
                <button
                  key={g.id}
                  type="button"
                  onClick={function() {
                    if (onChange) onChange(g.id)
                    setOpen(false)
                  }}
                  className={
                    'w-full flex items-center gap-3 px-4 py-2.5 transition-colors text-left ' +
                    (isSel
                      ? (isDark ? 'bg-white/[0.08]' : 'bg-slate-100')
                      : (isDark ? 'hover:bg-white/[0.04]' : 'hover:bg-slate-50'))
                  }
                >
                  <div
                    className="w-6 h-6 rounded-lg flex items-center justify-center flex-shrink-0"
                    style={{
                      background: selectedPalette.bg,
                      border: '1px solid ' + selectedPalette.border,
                    }}
                  >
                    <Layers size={12} style={{ color: selectedPalette.icon }} strokeWidth={1.8} />
                  </div>
                  <span className={'flex-1 text-sm font-medium whitespace-nowrap ' + (isDark ? 'text-white/90' : 'text-slate-800')}>
                    {g.groupLabel}
                  </span>
                  {isSel && <Check size={13} className="flex-shrink-0 text-indigo-500 dark:text-indigo-400" />}
                </button>
              )
            })}
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  )
}
