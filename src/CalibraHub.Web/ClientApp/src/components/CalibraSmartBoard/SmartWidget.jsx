/**
 * SmartWidget — Generic widget renderer.
 *
 * Dumb component. Sadece JSON widget objesini alir ve cizer.
 * Drag-drop YOK (siralama board-level config panelinden yapilir).
 *
 * Iki tip destekler:
 *   - type: 'data'  → Deger kutusu (icon + label + value)
 *   - type: 'link'  → Tiklanabilir dashed border buton (hover'da ArrowUpRight)
 */
import { useState, useEffect } from 'react'
import { motion } from 'framer-motion'
import { ArrowUpRight, AlertTriangle } from 'lucide-react'
import { resolveIcon, resolveColorForTheme, formatValue, resolveBooleanIcon } from './DynamicWidgetFactory'
import WidgetTooltip from './WidgetTooltip'

// Kısıt ihlali varsa uyarı mesajını döndür, uygunsa null.
function checkConstraintViolation(widget) {
  var raw = widget.value
  var dt  = (widget.dataType || '').toLowerCase()

  if (dt === 'text' || dt === 'string') {
    var len = (raw != null && raw !== '') ? String(raw).length : 0
    if (widget.expectedLength != null && len > 0 && len !== widget.expectedLength)
      return 'Beklenen uzunluk: ' + widget.expectedLength + ' karakter (girilen: ' + len + ')'
    if (widget.minLength != null && len > 0 && len < widget.minLength)
      return 'En az ' + widget.minLength + ' karakter olmalı (girilen: ' + len + ')'
    if (widget.maxLength != null && len > widget.maxLength)
      return 'En fazla ' + widget.maxLength + ' karakter olabilir (girilen: ' + len + ')'
  }

  if (dt === 'numeric' || dt === 'currency' || dt === 'percent') {
    if (raw == null || raw === '') return null
    var num = parseFloat(String(raw).replace(',', '.'))
    if (isNaN(num)) return null
    if (widget.minValue != null && num < widget.minValue)
      return 'En az ' + widget.minValue + ' olmalı (girilen: ' + num + ')'
    if (widget.maxValue != null && num > widget.maxValue)
      return 'En fazla ' + widget.maxValue + ' olabilir (girilen: ' + num + ')'
  }

  return null
}

export default function SmartWidget(props) {
  var widget = props.widget

  // Theme-aware palette — body/html class'ini izle, tema toggle'inda
  // renkleri otomatik yenile (palette.text koyu ↔ acik pastel).
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

  if (!widget) return null

  var type = widget.type || 'data'
  var dataType = widget.dataType || null

  // Icon: widget.icon > dataType > fallback. Boolean icin dinamik.
  var Icon
  if (dataType === 'boolean' && !widget.icon) {
    Icon = resolveBooleanIcon(widget.value)
  } else {
    Icon = resolveIcon(widget.icon, null, dataType)
  }

  var palette = resolveColorForTheme(widget.color, dataType, isDark)
  var label = widget.label || ''
  var hasValue = widget.value != null && widget.value !== ''
  var value = hasValue
    ? (dataType ? formatValue(widget.value, dataType) : String(widget.value))
    : '—'
  var detail = widget.detail || ''
  var url = widget.url || null

  /* ── Link tipi ─────────────────────────────── */
  if (type === 'link') {
    function handleClick(e) {
      e.stopPropagation()
      if (url) {
        e.preventDefault()
        window.location.href = url
      } else {
        console.log('[SmartWidget] link clicked but no url:', widget.id)
      }
    }

    return (
      <WidgetTooltip label={label} value={value} detail={detail || (url ? 'Git: ' + url : '')}>
        <motion.a
          href={url || '#'}
          onClick={handleClick}
          whileHover={{ scale: 1.03, y: -1 }}
          transition={{ type: 'spring', stiffness: 350, damping: 26, mass: 0.7 }}
          className="group flex items-center gap-2.5 px-3 py-2.5 rounded-xl cursor-pointer select-none whitespace-nowrap no-underline"
          style={{
            background: palette.bg,
            border: '1px dashed ' + palette.border,
            boxShadow: '0 2px 8px rgba(0,0,0,0.08)',
          }}
        >
          <div
            className="w-7 h-7 rounded-lg flex items-center justify-center flex-shrink-0"
            style={{ background: palette.bg, border: '1px solid ' + palette.border }}
          >
            <Icon size={14} style={{ color: palette.icon }} strokeWidth={1.8} />
          </div>
          <div className="flex flex-col min-w-0 gap-1">
            <span className="text-[9px] font-semibold uppercase tracking-wider leading-none text-slate-600 dark:text-white/70">
              Kisa Yol
            </span>
            <span
              className="text-xs font-bold leading-none tracking-tight"
              style={{ color: palette.text }}
            >
              {label}
            </span>
          </div>
          <motion.span
            initial={{ opacity: 0, x: -4 }}
            whileHover={{ opacity: 1, x: 0 }}
            animate={{ opacity: 0 }}
            className="group-hover:opacity-100 transition-opacity duration-200 -ml-1"
          >
            <ArrowUpRight size={12} style={{ color: palette.icon }} strokeWidth={2} />
          </motion.span>
        </motion.a>
      </WidgetTooltip>
    )
  }

  /* ── Data tipi (default) ───────────────────── */
  var violation = checkConstraintViolation(widget)
  var warnBg     = isDark ? 'rgba(245,158,11,0.08)' : 'rgba(245,158,11,0.07)'
  var warnBorder = isDark ? 'rgba(245,158,11,0.40)' : 'rgba(217,119,6,0.45)'

  return (
    <WidgetTooltip label={label} value={violation ? (value + ' — ⚠ ' + violation) : value} detail={detail}>
      <div
        className="flex items-center gap-2.5 px-3 py-2.5 rounded-xl select-none whitespace-nowrap"
        style={{
          background: violation ? warnBg : palette.bg,
          border: '1px solid ' + (violation ? warnBorder : palette.border),
          boxShadow: '0 2px 8px rgba(0,0,0,0.08)',
        }}
      >
        <div
          className="w-7 h-7 rounded-lg flex items-center justify-center flex-shrink-0"
          style={{
            background: violation ? warnBg : palette.bg,
            border: '1px solid ' + (violation ? warnBorder : palette.border),
          }}
        >
          {violation
            ? <AlertTriangle size={14} style={{ color: '#f59e0b' }} strokeWidth={2} />
            : <Icon size={14} style={{ color: palette.icon }} strokeWidth={1.8} />}
        </div>
        <div className="flex flex-col min-w-0 gap-1">
          <span
            className={'text-[9px] font-semibold uppercase tracking-wider leading-none ' + (violation ? '' : 'text-slate-500 dark:text-white/70')}
            style={violation ? { color: '#f59e0b' } : {}}
          >
            {label}
          </span>
          <span
            className="text-xs font-bold leading-none tracking-tight"
            style={{ color: violation ? (isDark ? '#fbbf24' : '#b45309') : palette.text }}
          >
            {value}
          </span>
        </div>
      </div>
    </WidgetTooltip>
  )
}
