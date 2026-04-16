import { useState } from 'react'
import { motion, AnimatePresence } from 'framer-motion'

export default function WidgetTooltip({ children, label, value, detail }) {
  const [show, setShow] = useState(false)

  return (
    <div
      className="relative"
      onMouseEnter={() => setShow(true)}
      onMouseLeave={() => setShow(false)}
    >
      {children}
      <AnimatePresence>
        {show && (
          <motion.div
            initial={{ opacity: 0, y: 6, scale: 0.95 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, y: 4, scale: 0.97 }}
            transition={{ duration: 0.15, ease: 'easeOut' }}
            className="absolute z-50 bottom-full left-1/2 -translate-x-1/2 mb-2.5 pointer-events-none"
          >
            <div className="glass-strong rounded-xl px-3.5 py-2.5 shadow-[0_8px_32px_rgba(15,23,42,0.15)] dark:shadow-[0_8px_32px_rgba(0,0,0,0.3)] min-w-[140px]">
              <p className="text-[10px] font-semibold uppercase tracking-wider text-slate-500 dark:text-white/40 mb-1">
                {label}
              </p>
              <p className="text-sm font-bold text-slate-800 dark:text-white leading-tight">
                {value}
              </p>
              {detail && (
                <p className="text-[11px] text-slate-500 dark:text-white/50 mt-1 leading-snug">
                  {detail}
                </p>
              )}
              <div className="absolute top-full left-1/2 -translate-x-1/2 -mt-px">
                <div className="w-2.5 h-2.5 rotate-45 bg-white/80 dark:bg-white/10 border-r border-b border-slate-200 dark:border-white/10" />
              </div>
            </div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  )
}
