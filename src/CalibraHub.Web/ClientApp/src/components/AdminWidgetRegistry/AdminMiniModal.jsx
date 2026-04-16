/**
 * AdminMiniModal — Reusable merkez dialog (GroupModal + OptionsModal icin base).
 *
 * Codebase'de generic modal yoktu; SmartBoardConfigPanel slide-in side panel
 * uyarlamasini kopyalayip merkez dialog'a donusturduk.
 *
 * Props:
 *   isOpen     bool              — parent acik/kapali state'i
 *   onClose    fn                — backdrop / ESC / X butonu tetikler
 *   title      string            — header baslik
 *   subtitle   string (ops.)     — header alt aciklama
 *   icon       lucide Component  — header ikon (ops.)
 *   iconColor  string (ops.)     — 'indigo' (default) | 'emerald' | ...
 *   children   ReactNode         — modal govdesi (flex-1, overflow-y-auto)
 *   footer     ReactNode (ops.)  — footer butonlari (genelde Iptal/Kaydet)
 *   maxWidth   string (ops.)     — default 'max-w-lg'; 'max-w-xl', 'max-w-2xl' vs.
 *
 * Tema: body.app-theme-dark / html.dark class'ini MutationObserver ile izler,
 * glass arka plani isDark'a gore otomatik degistirir.
 *
 * iframe scope: position:fixed inset-0 sadece admin panel iframe viewport'unu
 * kaplar, parent Shell'e tasmaz — istenen davranis.
 */
import { useState, useEffect } from 'react'
import { createPortal } from 'react-dom'
import { motion, AnimatePresence } from 'framer-motion'
import { X } from 'lucide-react'

export default function AdminMiniModal(props) {
  var isOpen    = !!props.isOpen
  var onClose   = props.onClose || function () {}
  var title     = props.title || ''
  var subtitle  = props.subtitle || ''
  var Icon      = props.icon || null
  var iconColor = props.iconColor || 'indigo'
  var children  = props.children
  var footer    = props.footer
  var maxWidth  = props.maxWidth || 'max-w-lg'

  // ── Tema algilama (SmartBoard pattern'i) ──
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

  // ── ESC tusuyla kapatma ──
  useEffect(function () {
    if (!isOpen) return undefined
    function onKey(e) {
      if (e.key === 'Escape') onClose()
    }
    document.addEventListener('keydown', onKey)
    return function () { document.removeEventListener('keydown', onKey) }
  }, [isOpen, onClose])

  // ── Icon palette ──
  var iconPalette = {
    indigo:  { bg: 'rgba(99, 102, 241, 0.18)', border: 'rgba(99, 102, 241, 0.35)', color: '#a5b4fc' },
    emerald: { bg: 'rgba(16, 185, 129, 0.18)', border: 'rgba(16, 185, 129, 0.35)', color: '#6ee7b7' },
    amber:   { bg: 'rgba(245, 158, 11, 0.18)', border: 'rgba(245, 158, 11, 0.35)', color: '#fcd34d' },
    rose:    { bg: 'rgba(244, 63, 94, 0.18)',  border: 'rgba(244, 63, 94, 0.35)',  color: '#fda4af' },
  }
  var pal = iconPalette[iconColor] || iconPalette.indigo

  // React Portal hedefi — document.body.
  // Neden? WidgetBuilderForm bir `motion.form`. Framer-motion transform
  // uyguladigi icin icindeki `position:fixed` descendant'lar viewport yerine
  // transformed ancestor'a gore konumlanir (CSS spec). Portal ile DOM tree'de
  // form'un disina cikariyoruz; fixed artik iframe viewport'una gore hizalanir.
  if (typeof document === 'undefined') return null

  var content = (
    <AnimatePresence>
      {isOpen && (
        <>
          {/* Backdrop */}
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.15 }}
            className="fixed inset-0 z-[9998] bg-black/60 backdrop-blur-sm"
            onClick={onClose}
          />

          {/* Flex-center wrapper — motion.div'in kendi transform'u
              Tailwind -translate-*-1/2'yi ezmesin diye layout'u flex'e birakiyoruz.
              pointer-events-none: backdrop tiklanabilir kalsin; ic dialog'a
              pointer-events-auto veriyoruz. */}
          <div className="fixed inset-0 z-[9999] flex items-center justify-center p-4 pointer-events-none">
            <motion.div
              initial={{ opacity: 0, scale: 0.95, y: 20 }}
              animate={{ opacity: 1, scale: 1, y: 0 }}
              exit={{ opacity: 0, scale: 0.97, y: 10 }}
              transition={{ type: 'spring', stiffness: 340, damping: 28 }}
              className={
                'w-full ' + maxWidth + ' max-h-[88vh] flex flex-col ' +
                'rounded-2xl overflow-hidden pointer-events-auto'
              }
              style={{
                background: isDark ? 'rgba(8, 11, 20, 0.94)' : 'rgba(255, 255, 255, 0.98)',
                border: isDark ? '1px solid rgba(255, 255, 255, 0.1)' : '1px solid rgba(15, 23, 42, 0.08)',
                backdropFilter: 'blur(32px)',
                WebkitBackdropFilter: 'blur(32px)',
                boxShadow: isDark
                  ? '0 20px 80px rgba(0, 0, 0, 0.55)'
                  : '0 20px 80px rgba(15, 23, 42, 0.18)',
              }}
            >
            {/* Header */}
            <div className="flex items-center gap-3 px-5 py-4 border-b border-slate-200/60 dark:border-white/[0.06] flex-shrink-0">
              {Icon && (
                <div
                  className="w-9 h-9 rounded-xl flex items-center justify-center flex-shrink-0"
                  style={{
                    background: pal.bg,
                    border: '1px solid ' + pal.border,
                  }}
                >
                  <Icon size={16} style={{ color: pal.color }} strokeWidth={2} />
                </div>
              )}
              <div className="flex-1 min-w-0">
                <h3 className="text-sm font-bold text-slate-800 dark:text-white/90 leading-tight truncate">
                  {title}
                </h3>
                {subtitle && (
                  <p className="text-[11px] text-slate-500 dark:text-white/40 mt-0.5 truncate">
                    {subtitle}
                  </p>
                )}
              </div>
              <button
                type="button"
                onClick={onClose}
                className="p-2 rounded-xl hover:bg-slate-100 dark:hover:bg-white/5 transition-colors text-slate-400 dark:text-white/40 hover:text-slate-700 dark:hover:text-white/80"
                title="Kapat (ESC)"
              >
                <X size={16} strokeWidth={2} />
              </button>
            </div>

            {/* Body — scrollable */}
            <div className="flex-1 overflow-y-auto min-h-0 px-5 py-4">
              {children}
            </div>

            {/* Footer */}
            {footer && (
              <div className="px-5 py-3 border-t border-slate-200/60 dark:border-white/[0.06] flex items-center gap-2 flex-shrink-0">
                {footer}
              </div>
            )}
            </motion.div>
          </div>
        </>
      )}
    </AnimatePresence>
  )

  return createPortal(content, document.body)
}
