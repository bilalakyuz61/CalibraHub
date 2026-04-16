import { useState } from 'react'
import { motion, AnimatePresence } from 'framer-motion'

export default function WidgetTooltip(props) {
  var [show, setShow] = useState(false)
  var label = props.label
  var value = props.value
  var detail = props.detail

  return (
    <div
      className="relative"
      onMouseEnter={function() { setShow(true) }}
      onMouseLeave={function() { setShow(false) }}
    >
      {props.children}
      <AnimatePresence>
        {show && (label || value || detail) && (
          <motion.div
            initial={{ opacity: 0, y: 6, scale: 0.95 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, y: 4, scale: 0.97 }}
            transition={{ duration: 0.15, ease: 'easeOut' }}
            className="absolute z-50 bottom-full left-1/2 -translate-x-1/2 mb-2.5 pointer-events-none"
          >
            <div className="rounded-xl px-3.5 py-2.5 shadow-[0_8px_32px_rgba(15,23,42,0.15)] dark:shadow-[0_8px_32px_rgba(0,0,0,0.3)] min-w-[140px]"
                 style={{
                   background: 'rgba(255, 255, 255, 0.1)',
                   backdropFilter: 'blur(32px)',
                   WebkitBackdropFilter: 'blur(32px)',
                   border: '1px solid rgba(255, 255, 255, 0.15)',
                 }}>
              {label && (
                <p className="text-[10px] font-semibold uppercase tracking-wider text-slate-500 dark:text-white/40 mb-1">
                  {label}
                </p>
              )}
              {value && (
                <p className="text-sm font-bold text-slate-800 dark:text-white leading-tight">
                  {value}
                </p>
              )}
              {detail && (
                <p className="text-[11px] text-slate-500 dark:text-white/50 mt-1 leading-snug">
                  {detail}
                </p>
              )}
              <div className="absolute top-full left-1/2 -translate-x-1/2 -mt-px">
                <div className="w-2.5 h-2.5 rotate-45"
                     style={{
                       background: 'rgba(255, 255, 255, 0.1)',
                       borderRight: '1px solid rgba(255, 255, 255, 0.15)',
                       borderBottom: '1px solid rgba(255, 255, 255, 0.15)',
                     }} />
              </div>
            </div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  )
}
