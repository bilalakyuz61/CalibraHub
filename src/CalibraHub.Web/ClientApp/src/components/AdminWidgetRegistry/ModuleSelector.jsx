/**
 * ModuleSelector — Entity-based form seçim dropdown'u (modül bazlı gruplu)
 *
 * 2026-06-09: DB-driven — ENTITY_REGISTRY statik listesine bağımlılık kaldırıldı.
 *   Dropdown, backend /api/widgets/forms endpoint'inden gelen form listesini
 *   SubModule'e göre gruplayarak entity türetir; ardından Module alanına göre
 *   section header'larla gruplar.
 *
 * 2026-06-09b: Modül bazlı kırılımlı dropdown — her Module ayrı bir başlık altında
 *   gösterilir (Genel, Lojistik, Satış, Üretim …). SubModule aynı formlar tek entity
 *   olarak görünür; variant toggle (Üst Bilgi / Kalem Bilgisi) parent panel tarafında.
 */
import { useState, useEffect, useRef } from 'react'
import { motion, AnimatePresence } from 'framer-motion'
import { ChevronDown, Check } from 'lucide-react'
import { resolveIcon, resolveColor } from '../CalibraSmartBoard/DynamicWidgetFactory'
import { buildEntitiesFromForms, getDefaultFormCode } from './entityRegistry'

// 2026-06-02: Iframe/workspace tab içinde Tailwind `dark:` her zaman tetiklenmiyor;
// body.app-theme-light class'ı tüm sayfada ve iframe body'sinde kesinlikle doğru.
function useThemeIsLight() {
  var [light, setLight] = useState(function () {
    if (typeof document === 'undefined') return false
    return document.body.classList.contains('app-theme-light')
  })
  useEffect(function () {
    var obs = new MutationObserver(function () {
      setLight(document.body.classList.contains('app-theme-light'))
    })
    obs.observe(document.body, { attributes: true, attributeFilter: ['class'] })
    return function () { obs.disconnect() }
  }, [])
  return light
}

export default function ModuleSelector(props) {
  var options = Array.isArray(props.options) ? props.options : []
  var selectedCode = props.selectedCode
  var onChange = props.onChange
  var trailing = props.trailing || null

  var [open, setOpen] = useState(false)
  var wrapperRef = useRef(null)

  // Dışarı tıklama → kapat
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

  var isLight = useThemeIsLight()

  // Backend options'ı form-like nesneye dönüştür; module alanını da geçir
  var formLike = options.map(function(opt) {
    return {
      formCode: opt.value,
      formName: opt.label,
      subModule: opt.subModule || null,
      module: opt.module || null,
      icon: opt.icon || null,
      iconColor: opt.color || null,
    }
  })

  // SubModule'e göre grupla → entity listesi (DB-driven)
  var displayEntities = buildEntitiesFromForms(formLike)

  if (displayEntities.length === 0) {
    return null
  }

  // Seçili formCode'a karşılık gelen entity'yi bul; bulamazsak ilk entity'ye fallback.
  var selectedEntity = (function() {
    if (!selectedCode || displayEntities.length === 0) return displayEntities[0] || null
    var target = String(selectedCode).toUpperCase()
    for (var i = 0; i < displayEntities.length; i++) {
      var e = displayEntities[i]
      if (e.formCode && String(e.formCode).toUpperCase() === target) return e
      if (Array.isArray(e.variants)) {
        for (var j = 0; j < e.variants.length; j++) {
          if (String(e.variants[j].formCode).toUpperCase() === target) return e
        }
      }
    }
    return displayEntities[0]
  })()

  var selectedPalette = resolveColor(selectedEntity.color || 'slate')
  var SelectedIcon = resolveIcon(selectedEntity.icon || 'Layers')

  // ── Module bazlı gruplama ──────────────────────────────────────────────────
  // entityKey → module adı haritası (formLike'daki module alanından).
  // Hem SubModule key hem formCode indekslenir: "view-type olmayan" gruplar
  // buildEntitiesFromForms tarafından formCode bazlı ayrı entity'lere patlatılır,
  // bu durumda entity.key = formCode olur → formCode → module lookup gerekir.
  var entityModuleMap = {}
  formLike.forEach(function(f) {
    var subModKey = f.subModule ? String(f.subModule).trim() : f.formCode
    if (!entityModuleMap[subModKey]) {
      entityModuleMap[subModKey] = f.module || 'Diğer'
    }
    // formCode → module (patlatılmış entity'ler için)
    if (f.formCode && !entityModuleMap[f.formCode]) {
      entityModuleMap[f.formCode] = f.module || 'Diğer'
    }
  })

  // displayEntities'i module'e göre grupla; sıralamayı koru
  var moduleOrder = []
  var moduleGroups = {}  // moduleName → entity[]
  displayEntities.forEach(function(entity) {
    var mod = entityModuleMap[entity.key] || 'Diğer'
    if (!moduleGroups[mod]) {
      moduleGroups[mod] = []
      moduleOrder.push(mod)
    }
    moduleGroups[mod].push(entity)
  })
  // Render için dizi: [{moduleName, entities[]}]
  var groupedSections = moduleOrder.map(function(m) {
    return { moduleName: m, entities: moduleGroups[m] }
  })
  // ─────────────────────────────────────────────────────────────────────────

  function handlePickEntity(entity) {
    setOpen(false)
    if (!entity || !onChange) return
    var target = getDefaultFormCode(entity)
    if (target) onChange(target)
  }

  // Styling helpers
  var dropBg      = isLight ? '#ffffff' : 'rgba(8,11,20,0.96)'
  var dropBorder  = isLight ? '1px solid #e2e8f0' : '1px solid rgba(255,255,255,0.10)'
  var dropShadow  = isLight ? '0 12px 40px rgba(15,23,42,0.12)' : '0 12px 40px rgba(0,0,0,0.4)'
  var headerColor = isLight ? '#94a3b8' : 'rgba(255,255,255,0.28)'
  var headerBorder = isLight ? '1px solid #e2e8f0' : '1px solid rgba(255,255,255,0.07)'

  return (
    <div className="px-5 py-2.5 border-b border-slate-200/50 dark:border-white/[0.06] flex-shrink-0 grid grid-cols-1 md:grid-cols-[1fr_2fr] gap-4 items-center">
      <div className="relative min-w-0" ref={wrapperRef}>

        {/* Trigger */}
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
          <span className="flex-1 min-w-0 text-left truncate">{selectedEntity.label}</span>
          <motion.span
            animate={{ rotate: open ? 180 : 0 }}
            transition={{ duration: 0.2 }}
            className="text-slate-400 dark:text-white/30 flex-shrink-0"
          >
            <ChevronDown size={13} />
          </motion.span>
        </button>

        {/* Dropdown panel — modül bazlı kırılımlı */}
        <AnimatePresence>
          {open && (
            <motion.div
              initial={{ opacity: 0, y: -6, scale: 0.98 }}
              animate={{ opacity: 1, y: 0, scale: 1 }}
              exit={{ opacity: 0, y: -6, scale: 0.98 }}
              transition={{ duration: 0.15, ease: [0.23, 1, 0.32, 1] }}
              className="absolute left-0 top-full mt-1 z-50 rounded-xl"
              style={{
                minWidth: '220px',
                maxHeight: '70vh',
                overflowY: 'auto',
                background: dropBg,
                border: dropBorder,
                boxShadow: dropShadow,
                backdropFilter: isLight ? undefined : 'blur(24px)',
                WebkitBackdropFilter: isLight ? undefined : 'blur(24px)',
              }}
            >
              {groupedSections.map(function(section, si) {
                return (
                  <div key={section.moduleName}>
                    {/* Modül başlığı */}
                    <div
                      style={{
                        borderTop: si > 0 ? headerBorder : 'none',
                        color: headerColor,
                        fontSize: '10px',
                        fontWeight: 600,
                        letterSpacing: '0.06em',
                        textTransform: 'uppercase',
                        padding: si === 0 ? '8px 16px 4px' : '10px 16px 4px',
                      }}
                    >
                      {section.moduleName}
                    </div>

                    {/* Modül altındaki entity'ler */}
                    {section.entities.map(function(entity) {
                      var palette = resolveColor(entity.color || 'slate')
                      var Icon = resolveIcon(entity.icon || 'Layers')
                      var isSel = entity.key === selectedEntity.key
                      var rowBg = isSel
                        ? (isLight ? '#eef2ff' : 'rgba(255,255,255,0.08)')
                        : 'transparent'
                      var hoverBg = isLight ? '#f1f5f9' : 'rgba(255,255,255,0.04)'
                      return (
                        <button
                          key={entity.key}
                          type="button"
                          onClick={function() { handlePickEntity(entity) }}
                          className="w-full flex items-center gap-3 px-4 py-2 transition-colors text-left"
                          style={{ background: rowBg, color: isLight ? '#1e293b' : 'rgba(255,255,255,0.90)' }}
                          onMouseEnter={function(e) { if (!isSel) e.currentTarget.style.background = hoverBg }}
                          onMouseLeave={function(e) { if (!isSel) e.currentTarget.style.background = 'transparent' }}
                        >
                          <div
                            className="w-6 h-6 rounded-md flex items-center justify-center flex-shrink-0"
                            style={{ background: palette.bg, border: '1px solid ' + palette.border }}
                          >
                            <Icon size={12} style={{ color: palette.icon }} strokeWidth={1.8} />
                          </div>
                          <span className="flex-1 min-w-0 text-[13px] font-medium truncate">{entity.label}</span>
                          {isSel && <Check size={13} style={{ color: isLight ? '#6366f1' : '#a5b4fc' }} className="flex-shrink-0" />}
                        </button>
                      )
                    })}
                  </div>
                )
              })}

              {/* Alt padding */}
              <div style={{ height: '6px' }} />
            </motion.div>
          )}
        </AnimatePresence>
      </div>
      {trailing}
    </div>
  )
}
