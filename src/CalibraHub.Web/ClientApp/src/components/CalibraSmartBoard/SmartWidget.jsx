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
import { createPortal } from 'react-dom'
import { motion } from 'framer-motion'
import { ArrowUpRight, AlertTriangle, X } from 'lucide-react'
import { resolveIcon, resolveColorForTheme, formatValue, resolveBooleanIcon, resolveChipWidth } from './DynamicWidgetFactory'
import WidgetTooltip from './WidgetTooltip'
import GuideListField from '../DynamicWidgetRenderer/GuideListField'

/**
 * Liste sayfasinda DOM input'lari yok — kart-bazli token resolve icin
 * record values dictionary'i kullanir. `{#code}` ve eski `{xxx}` formatlarini
 * destekler. Guide-list popup'inda WHERE kisitinin runtime'da resolve olmasini
 * saglar. Match yoksa empty string ile replace edilir.
 */
function resolveTokensWithRecord(text, recordValues) {
  if (!text) return text
  if (typeof text !== 'string') return text
  if (!recordValues || typeof recordValues !== 'object') return text
  function readKey(key) {
    if (Object.prototype.hasOwnProperty.call(recordValues, key)) {
      var v = recordValues[key]
      return v == null ? '' : String(v)
    }
    var lower = String(key).toLowerCase()
    var keys = Object.keys(recordValues)
    for (var i = 0; i < keys.length; i++) {
      if (keys[i].toLowerCase() === lower) {
        var v2 = recordValues[keys[i]]
        return v2 == null ? '' : String(v2)
      }
    }
    return ''
  }
  // Standart: {#xxx} (nokta yok). DOM lookup token formati.
  var out = String(text).replace(/\{#(\w+)\}/g, function(_, key) { return readKey(key) })
  // Legacy: {xxx} (raw widget token) — bazi eski rule'larda
  out = out.replace(/\{(\w+)\}/g, function(_, key) { return readKey(key) })
  return out
}

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

  // Sabit chip genisligi (px) — icerige degil dataType/type'a bagli, boylece
  // ayni board'daki tum kartlarda ayni pozisyondaki widget ayni genislikte
  // olur ve dikey sutun hizalamasi saglanir (bkz. DynamicWidgetFactory.js).
  var chipWidth = resolveChipWidth(dataType, type)
  var chipBoxStyle = { flex: '0 0 ' + chipWidth + 'px', width: chipWidth + 'px', maxWidth: chipWidth + 'px' }

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

  /* ── Guide List (salt okunur akordion liste) — kart uzerinde modal popup ── */
  // Local modal state — useState top-level olamaz cunku once if/return geliyor.
  // Bu yuzden hook'u en basta render edenleri yer aldik.
  if ((dataType || '').toLowerCase() === 'guide-list') {
    return (
      <GuideListWidgetButton
        widget={widget}
        palette={palette}
        Icon={Icon}
        label={label}
        recordValues={props.recordValues || {}}
        chipWidth={chipWidth}
      />
    )
  }

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
      <WidgetTooltip label={label} value={value} detail={detail || (url ? 'Git: ' + url : '')} style={chipBoxStyle}>
        <motion.a
          href={url || '#'}
          onClick={handleClick}
          whileHover={{ scale: 1.03, y: -1 }}
          transition={{ type: 'spring', stiffness: 350, damping: 26, mass: 0.7 }}
          className="group flex items-center gap-2.5 px-3 py-2.5 rounded-xl cursor-pointer select-none whitespace-nowrap no-underline w-full overflow-hidden"
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
          <div className="flex flex-col min-w-0 flex-1 gap-1">
            <span className="text-[9px] font-semibold uppercase tracking-wider leading-none text-slate-600 dark:text-white/70 truncate">
              Kisa Yol
            </span>
            <span
              className="text-xs font-bold leading-none tracking-tight truncate"
              style={{ color: palette.text }}
            >
              {label}
            </span>
          </div>
          <motion.span
            initial={{ opacity: 0, x: -4 }}
            whileHover={{ opacity: 1, x: 0 }}
            animate={{ opacity: 0 }}
            className="group-hover:opacity-100 transition-opacity duration-200 -ml-1 flex-shrink-0"
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

/**
 * GuideListWidgetButton — Salt okunur Rehber Listesi widget'i kart uzerinde:
 * tek tikla portal modal'inda full-genislikte tabloyu acar. Modal icinde
 * GuideListField alwaysOpen ile inline render edilir.
 *
 * widget.metadata.guideCode ve widget.metadata.guideConfig backend tarafindan
 * BuildRenderDtos icinde set edilir; constraints (varsa) raw olarak gecirilir
 * (token resolve liste sayfasinda DOM'da olmadigi icin SmartCard'in widget
 * mapping'ine birakilmistir).
 */
function GuideListWidgetButton(props) {
  var widget   = props.widget
  var palette  = props.palette
  var Icon     = props.Icon
  var label    = props.label
  var recordValues = props.recordValues || {}
  var meta     = (widget && widget.metadata) || {}
  var [open, setOpen] = useState(false)

  // metadata.guideConfig JSON parse → constraint cikar.
  // Liste sayfasinda DOM yok; recordValues uzerinden manuel token resolve yapariz.
  var guideConfigParsed = null
  try {
    guideConfigParsed = (typeof meta.guideConfig === 'string')
      ? JSON.parse(meta.guideConfig)
      : (meta.guideConfig || null)
  } catch (e) { guideConfigParsed = null }
  var rawConstraint = (guideConfigParsed && guideConfigParsed.constraint) || ''
  var resolvedConstraint = resolveTokensWithRecord(rawConstraint, recordValues)

  // ESC ile kapat
  useEffect(function() {
    if (!open) return undefined
    function onKey(e) { if (e.key === 'Escape') setOpen(false) }
    document.addEventListener('keydown', onKey)
    return function() { document.removeEventListener('keydown', onKey) }
  }, [open])

  function handleOpen(e) {
    e.stopPropagation()
    e.preventDefault()
    setOpen(true)
  }

  return (
    <>
      <WidgetTooltip label={label} value="Listeyi Aç" detail={meta.guideCode ? ('Rehber: ' + meta.guideCode) : 'Rehber tanımlı değil'}>
        <motion.button
          type="button"
          onClick={handleOpen}
          whileHover={{ scale: 1.03, y: -1 }}
          transition={{ type: 'spring', stiffness: 350, damping: 26, mass: 0.7 }}
          className="group flex items-center gap-2.5 px-3 py-2.5 rounded-xl cursor-pointer select-none whitespace-nowrap"
          style={{
            background: palette.bg,
            border: '1px solid ' + palette.border,
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
              Rehber
            </span>
            <span className="text-xs font-bold leading-none tracking-tight" style={{ color: palette.text }}>
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
        </motion.button>
      </WidgetTooltip>
      {open && createPortal(
        <div
          onClick={function() { setOpen(false) }}
          style={{
            position: 'fixed', inset: 0, zIndex: 10300,
            background: 'rgba(0,0,0,0.55)', backdropFilter: 'blur(2px)',
            display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 16,
          }}
        >
          <div
            onClick={function(e) { e.stopPropagation() }}
            style={{
              width: '100%', maxWidth: 1080, maxHeight: '85vh',
              background: 'rgba(13,17,27,0.98)',
              border: '1px solid rgba(255,255,255,0.12)',
              borderRadius: 14,
              boxShadow: '0 16px 48px rgba(0,0,0,0.55)',
              display: 'flex', flexDirection: 'column', overflow: 'hidden',
            }}
          >
            <div style={{
              display: 'flex', alignItems: 'center', gap: 10,
              padding: '14px 18px', borderBottom: '1px solid rgba(255,255,255,0.08)',
            }}>
              <div
                style={{
                  width: 30, height: 30, borderRadius: 8,
                  display: 'flex', alignItems: 'center', justifyContent: 'center',
                  background: palette.bg, border: '1px solid ' + palette.border,
                }}
              >
                <Icon size={15} style={{ color: palette.icon }} strokeWidth={1.8} />
              </div>
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{ fontSize: 14, fontWeight: 700, color: 'rgba(255,255,255,0.92)' }}>{label}</div>
                {meta.guideCode && (
                  <div style={{ fontSize: 11, fontFamily: 'monospace', color: 'rgba(255,255,255,0.5)', marginTop: 2 }}>
                    {meta.guideCode}
                  </div>
                )}
              </div>
              <button
                type="button"
                onClick={function() { setOpen(false) }}
                aria-label="Kapat"
                style={{
                  width: 30, height: 30, borderRadius: 8, cursor: 'pointer',
                  background: 'rgba(255,255,255,0.04)', border: '1px solid rgba(255,255,255,0.08)',
                  color: 'rgba(255,255,255,0.65)',
                  display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
                }}
              >
                <X size={14} />
              </button>
            </div>
            <div style={{ flex: 1, minHeight: 0, overflow: 'auto', padding: 14 }}>
              <GuideListField
                widgetId={widget.widgetId || widget.id}
                label={label}
                guideCode={meta.guideCode || ''}
                guideConfig={meta.guideConfig || null}
                constraints={resolvedConstraint || null}
                classPrefix="wf"
                alwaysOpen={true}
              />
            </div>
          </div>
        </div>,
        document.body
      )}
    </>
  )
}
