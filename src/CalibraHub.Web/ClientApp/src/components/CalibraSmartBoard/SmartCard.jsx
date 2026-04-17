/**
 * SmartCard — Generic, entity-agnostic kart.
 *
 * Props (tamamen JSON — hic bir hardcoded is mantigi yok):
 *   {
 *     id, title, subtitle, description, imageUrl,
 *     statusBadge: { label, color },
 *     widgets: [...],
 *     primaryAction: { label, icon, url },
 *     secondaryAction: { label, icon, url, apiUrl, confirm },
 *   }
 */
import { useState, useMemo, useRef, useEffect, useCallback } from 'react'
import { createPortal } from 'react-dom'
import { motion, AnimatePresence } from 'framer-motion'
import { CircleDot, ChevronLeft, ChevronRight, X, AlertTriangle, Trash2 } from 'lucide-react'
import SmartWidget from './SmartWidget'
import { resolveColor, resolveIcon } from './DynamicWidgetFactory'
import { navigateInWorkspace } from '../../utils/workspaceNav'

// Bir widget'ın kısıt ihlali varsa açıklama döndür, yoksa null.
function getWidgetViolation(w) {
  var raw = w.value
  var dt  = (w.dataType || '').toLowerCase()
  if (dt === 'text' || dt === 'string') {
    var len = (raw != null && raw !== '') ? String(raw).length : 0
    if (w.expectedLength != null && len > 0 && len !== w.expectedLength)
      return w.label + ': beklenen ' + w.expectedLength + ' karakter (girilen: ' + len + ')'
    if (w.minLength != null && len > 0 && len < w.minLength)
      return w.label + ': en az ' + w.minLength + ' karakter (girilen: ' + len + ')'
    if (w.maxLength != null && len > w.maxLength)
      return w.label + ': en fazla ' + w.maxLength + ' karakter (girilen: ' + len + ')'
  }
  if (dt === 'numeric' || dt === 'currency' || dt === 'percent') {
    if (raw == null || raw === '') return null
    var num = parseFloat(String(raw).replace(',', '.'))
    if (isNaN(num)) return null
    if (w.minValue != null && num < w.minValue)
      return w.label + ': en az ' + w.minValue + ' (girilen: ' + num + ')'
    if (w.maxValue != null && num > w.maxValue)
      return w.label + ': en fazla ' + w.maxValue + ' (girilen: ' + num + ')'
  }
  return null
}

var hoverBgMap = {
  amber: 'hover:bg-amber-100 dark:hover:bg-amber-500/10',
  red:   'hover:bg-red-100 dark:hover:bg-red-500/10',
  blue:  'hover:bg-blue-100 dark:hover:bg-blue-500/10',
  green: 'hover:bg-emerald-100 dark:hover:bg-emerald-500/10',
  slate: 'hover:bg-slate-100 dark:hover:bg-white/5',
  emerald: 'hover:bg-emerald-100 dark:hover:bg-emerald-500/10',
}
var hoverTextMap = {
  amber: 'group-hover:text-amber-600 dark:group-hover:text-amber-400/70',
  red:   'group-hover:text-red-600 dark:group-hover:text-red-400/70',
  blue:  'group-hover:text-blue-600 dark:group-hover:text-blue-400/70',
  green: 'group-hover:text-emerald-600 dark:group-hover:text-emerald-400/70',
  slate: 'group-hover:text-slate-600 dark:group-hover:text-slate-400/70',
  emerald: 'group-hover:text-emerald-600 dark:group-hover:text-emerald-400/70',
}

export default function SmartCard(props) {
  var id = props.id
  var title = props.title || ''
  var subtitle = props.subtitle || ''
  var description = props.description || ''
  var imageUrl = props.imageUrl || null
  var statusBadge = props.statusBadge || null
  var rawWidgets = Array.isArray(props.widgets) ? props.widgets : []
  var primaryAction = props.primaryAction || null
  var secondaryAction = props.secondaryAction || null
  var extraActions = Array.isArray(props.extraActions) ? props.extraActions : []

  // Board-level user tercihleri (SmartBoard'dan gelir)
  var visibleIds = Array.isArray(props.visibleIds) ? props.visibleIds : null
  var order = Array.isArray(props.order) ? props.order : null

  // Kısıt ihlalleri — chip görünürlüğünden bağımsız, tüm widget'lar kontrol edilir
  var violations = useMemo(function() {
    var msgs = []
    rawWidgets.forEach(function(w) {
      if (!w) return
      var msg = getWidgetViolation(w)
      if (msg) msgs.push(msg)
    })
    return msgs
  }, [rawWidgets])

  /**
   * Widget'lari master liste + kullanici tercihlerine gore hazirla.
   * - visibleIds verilmisse: sadece bu id'lerdeki widget'lar
   * - order verilmisse: bu siraya gore dizim
   * - Ikisi de yoksa: rawWidgets oldugu gibi
   */
  var widgets = useMemo(function() {
    // Semantik renk cozumu: colorType/colorValue → color string (SmartWidget.color)
    // Dinamik (colorType=1): colorValue, ayni karttaki baska bir widget'in id'si;
    // o widget'in value'su token olarak okunur.
    var listableRaw = rawWidgets.filter(function(w) { return w != null })
    var valueById = {}
    rawWidgets.forEach(function(w) { if (w && w.id) valueById[w.id] = w.value })
    listableRaw = listableRaw.map(function(w) {
      if (!w) return w
      var token = null
      if (w.colorType === 1) {
        token = w.colorValue ? (valueById[w.colorValue] || null) : null
      } else if (w.colorType === 0) {
        token = w.colorValue || null
      }
      if (!token) return w
      return Object.assign({}, w, { color: String(token) })
    })

    if (!visibleIds && !order) return listableRaw

    // Id → widget map
    var map = {}
    listableRaw.forEach(function(w) { if (w && w.id) map[w.id] = w })

    var result = []
    var usedIds = {}

    // Once order'a gore gez
    if (order) {
      order.forEach(function(wid) {
        if (visibleIds && visibleIds.indexOf(wid) === -1) return
        if (map[wid]) {
          result.push(map[wid])
          usedIds[wid] = true
        }
      })
    } else if (visibleIds) {
      // Order yoksa listableRaw sirasi kullanilir
      listableRaw.forEach(function(w) {
        if (w && visibleIds.indexOf(w.id) !== -1) {
          result.push(w)
          usedIds[w.id] = true
        }
      })
    }

    // Order'da olmayan ama visibleIds'de olan (veya visibleIds yoksa tum geri kalanlar) sona
    listableRaw.forEach(function(w) {
      if (!w || !w.id || usedIds[w.id]) return
      if (visibleIds && visibleIds.indexOf(w.id) === -1) return
      result.push(w)
    })

    return result
  }, [rawWidgets, visibleIds, order])

  var [hovered, setHovered] = useState(false)

  // ── Modal state (fetch-modal ekstra aksiyonlar icin) ──
  var [modalOpen,    setModalOpen]    = useState(false)
  var [modalHtml,    setModalHtml]    = useState('')
  var [modalTitle,   setModalTitle]   = useState('')
  var [modalLoading, setModalLoading] = useState(false)

  // ── Onay modalı (silme vb.) ──
  var [confirmOpen, setConfirmOpen]   = useState(false)
  var [confirmMsg,  setConfirmMsg]    = useState('')
  var confirmCallbackRef = useRef(null)

  function showConfirm(message, callback) {
    setConfirmMsg(message)
    confirmCallbackRef.current = callback
    setConfirmOpen(true)
  }

  useEffect(function() {
    if (!confirmOpen) return
    function onKey(e) { if (e.key === 'Escape') handleConfirmNo() }
    document.addEventListener('keydown', onKey)
    return function() { document.removeEventListener('keydown', onKey) }
  }, [confirmOpen])
  function handleConfirmYes() {
    setConfirmOpen(false)
    if (confirmCallbackRef.current) { confirmCallbackRef.current(); confirmCallbackRef.current = null }
  }
  function handleConfirmNo() {
    setConfirmOpen(false)
    confirmCallbackRef.current = null
  }

  // ── Tema algilama (SmartBoard ile ayni desen) ──
  // Inline style'lari theme-aware yapmak icin body/html class'ini okuyup
  // MutationObserver ile canli tut. Eski kod `rgba(255,255,255,0.06)`
  // hardcoded — light temada neredeyse gorunmuyor. Bunu artik isDark'a gore
  // conditional uretiyoruz.
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

  // ── Yatay scroll state (widget overflow) ──────────────────────
  // Widget sirasi yatay olarak tasabilir — kullanici scroll edebilir, hover'da
  // sol/sag chevron butonlari ve fade maskesi ile gorsel ipucu veririz.
  var scrollRef = useRef(null)
  var [canScrollLeft, setCanScrollLeft] = useState(false)
  var [canScrollRight, setCanScrollRight] = useState(false)

  var updateScrollState = useCallback(function() {
    var el = scrollRef.current
    if (!el) return
    var maxScroll = el.scrollWidth - el.clientWidth
    setCanScrollLeft(el.scrollLeft > 2)
    setCanScrollRight(el.scrollLeft < maxScroll - 2)
  }, [])

  // Widget listesi degistikce veya resize'da yeniden hesapla
  useEffect(function() {
    updateScrollState()
    var el = scrollRef.current
    if (!el || typeof ResizeObserver === 'undefined') return undefined
    var ro = new ResizeObserver(updateScrollState)
    ro.observe(el)
    return function() { ro.disconnect() }
  }, [widgets, updateScrollState])

  function scrollWidgets(direction) {
    var el = scrollRef.current
    if (!el) return
    var amount = Math.max(el.clientWidth * 0.7, 180)
    el.scrollBy({
      left: direction === 'left' ? -amount : amount,
      behavior: 'smooth',
    })
  }

  /**
   * Navigasyon: Workspace frame icinde (iframe) sayfa degisimi.
   * Merkezi workspaceNav utility kullanilir — tum navigasyon buradan gecer.
   */
  function navigateInFrame(url) {
    navigateInWorkspace(url)
  }

  function handlePrimary(e) {
    e.stopPropagation()
    if (primaryAction && primaryAction.url) navigateInFrame(primaryAction.url)
  }

  function executeSecondary() {
    if (!secondaryAction) return
    if (secondaryAction.apiUrl) {
      fetch(secondaryAction.apiUrl, { method: 'POST', credentials: 'same-origin' })
        .then(function(r) { return r.json() })
        .then(function(data) {
          if (data && data.success === false) alert('Hata: ' + (data.message || 'Bilinmeyen'))
          else window.location.reload()
        })
        .catch(function(err) { alert('Hata: ' + err.message) })
    } else if (secondaryAction.url) {
      navigateInFrame(secondaryAction.url)
    }
  }

  function handleSecondary(e) {
    e.stopPropagation()
    if (!secondaryAction) return
    if (secondaryAction.confirm) {
      showConfirm(secondaryAction.confirm, executeSecondary)
    } else {
      executeSecondary()
    }
  }

  // Status badge renderer
  var badgeElement = null
  if (statusBadge && statusBadge.label) {
    var badgePalette = resolveColor(statusBadge.color)
    badgeElement = (
      <span
        className="text-[9px] font-bold px-1.5 py-px rounded-full uppercase tracking-wider"
        style={{
          background: badgePalette.bg,
          border: '1px solid ' + badgePalette.border,
          color: badgePalette.text,
        }}
      >
        {statusBadge.label}
      </span>
    )
  }

  // handleExtraAction — fetch-modal, download, api-post, navigate
  function handleExtraAction(e, action) {
    e.stopPropagation()

    if (action.type === 'fetch-modal') {
      var url = (action.fetchUrl || '').replace('{id}', id)
      setModalTitle(action.modalTitle || '')
      setModalHtml('')
      setModalLoading(true)
      setModalOpen(true)
      fetch(url, { credentials: 'same-origin' })
        .then(function(r) { return r.text() })
        .then(function(html) { setModalHtml(html); setModalLoading(false) })
        .catch(function(err) { setModalHtml('<div class="p-4 text-red-500">Yuklenemedi: ' + err.message + '</div>'); setModalLoading(false) })
      return
    }

    if (action.type === 'download') {
      var dlUrl = (action.url || '').replace('{id}', id)
      var a = document.createElement('a'); a.href = dlUrl; a.download = ''
      document.body.appendChild(a); a.click(); document.body.removeChild(a)
      return
    }

    if (action.type === 'api-post') {
      var postUrl = (action.url || '').replace('{id}', id)
      var token = (document.querySelector('input[name="__RequestVerificationToken"]') || {}).value || ''
      var fd = new FormData()
      if (action.body) Object.keys(action.body).forEach(function(k) { fd.append(k, String(action.body[k])) })
      if (token) fd.append('__RequestVerificationToken', token)
      fetch(postUrl, { method: 'POST', body: fd, credentials: 'same-origin' })
        .then(function(r) { return r.json() })
        .then(function(data) {
          if (data && data.success === false) alert('Hata: ' + (data.message || 'Bilinmeyen'))
          else window.location.reload()
        })
        .catch(function(err) { alert('Hata: ' + err.message) })
      return
    }

    if (action.url) navigateInFrame((action.url).replace('{id}', id))
  }

  // Action button renderer — colorHint uses hoverBgMap/hoverTextMap
  function renderActionButton(action, handlerOrKey, colorHint) {
    if (!action) return null
    var ActionIcon = resolveIcon(action.icon)
    var handler
    if (typeof handlerOrKey === 'function') {
      handler = handlerOrKey
    } else {
      handler = handlerOrKey === 'primary' ? handlePrimary : handleSecondary
    }
    var bgClass   = hoverBgMap[colorHint]   || hoverBgMap.slate
    var textClass = hoverTextMap[colorHint] || hoverTextMap.slate
    return (
      <button
        key={action.label}
        type="button"
        onClick={handler}
        className={'p-2 rounded-xl transition-colors group ' + bgClass}
        title={action.label || ''}
      >
        <ActionIcon
          size={18}
          className={'text-slate-400 dark:text-white/40 transition-colors ' + textClass}
        />
      </button>
    )
  }

  return (
    <motion.div
      initial={{ opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.3, ease: [0.23, 1, 0.32, 1] }}
      onMouseEnter={function() { setHovered(true) }}
      onMouseLeave={function() { setHovered(false) }}
      className="w-full"
    >
      <div
        className={'rounded-2xl overflow-hidden transition-all duration-300 ' +
          (hovered
            ? 'shadow-[0_8px_40px_rgba(0,0,0,0.22)]'
            : 'shadow-[0_2px_12px_rgba(0,0,0,0.1)]')
        }
        style={isDark ? {
          background: 'rgba(255, 255, 255, 0.05)',
          backdropFilter: 'blur(24px)',
          WebkitBackdropFilter: 'blur(24px)',
          border: '1px solid rgba(255, 255, 255, 0.12)',
        } : {
          background: 'rgba(255, 255, 255, 0.95)',
          backdropFilter: 'blur(24px)',
          WebkitBackdropFilter: 'blur(24px)',
          border: '1px solid rgba(15, 23, 42, 0.1)',
        }}
      >
        <div className="flex items-center gap-0">

          {/* Sol: Aksiyonlar */}
          {(primaryAction || secondaryAction || extraActions.length > 0) && (
            <>
              <div className="flex items-center gap-1 px-3 flex-shrink-0">
                {renderActionButton(primaryAction, handlePrimary, 'amber')}
                {renderActionButton(secondaryAction, handleSecondary, 'red')}
                {extraActions.map(function(action, i) {
                  return renderActionButton(action, function(e) { handleExtraAction(e, action) }, action.color || 'slate')
                })}
              </div>
              <div className="w-px h-10 bg-slate-200 dark:bg-white/[0.06] flex-shrink-0" />
            </>
          )}

          {/* Kimlik */}
          <div
            className="flex items-center gap-3.5 pl-3 pr-5 py-3.5 flex-shrink-0 w-[340px] cursor-pointer group"
            onClick={function() {
              if (primaryAction && primaryAction.url) navigateInFrame(primaryAction.url)
            }}
          >
            {imageUrl && (
              <img
                src={imageUrl}
                alt={title}
                className="w-11 h-11 rounded-xl object-cover border border-slate-200 dark:border-white/10 flex-shrink-0"
              />
            )}

            <div className="flex-1 min-w-0">
              {(subtitle || badgeElement || violations.length > 0) && (
                <div className="flex items-center gap-2 mb-0.5">
                  {subtitle && (
                    <span className="text-[10px] font-mono font-semibold tracking-wider text-slate-600 dark:text-white/75 uppercase truncate">
                      {subtitle}
                    </span>
                  )}
                  {badgeElement}
                  {violations.length > 0 && (
                    <span
                      title={'Kısıt ihlali:\n' + violations.join('\n')}
                      className="inline-flex items-center gap-1 px-1.5 py-0.5 rounded-full text-[9px] font-bold uppercase tracking-wide flex-shrink-0"
                      style={{ background: 'rgba(245,158,11,0.15)', color: '#d97706', border: '1px solid rgba(245,158,11,0.35)' }}
                    >
                      <AlertTriangle size={9} strokeWidth={2.5} />
                      {violations.length}
                    </span>
                  )}
                </div>
              )}
              <h3 className="text-sm font-bold text-slate-900 dark:text-white tracking-tight leading-tight truncate transition-colors">
                {title}
              </h3>
              {description && (
                <p className="text-[11px] text-slate-600 dark:text-white/75 truncate mt-0.5 leading-tight">
                  {description}
                </p>
              )}
            </div>
          </div>

          {/* Ayirici */}
          <div className="w-px h-10 bg-slate-200 dark:bg-white/[0.06] flex-shrink-0" />

          {/* Orta: Widget'lar — tek satirlik, yatay scroll, fade + chevron */}
          <div className="relative flex-1 min-w-0">
            {widgets.length > 0 ? (
              <>
                {/* Scrollable widget satiri — mask-image ile kenarlar soluklanir */}
                <div
                  ref={scrollRef}
                  onScroll={updateScrollState}
                  className={
                    'smartcard-widgets-scroll flex items-center gap-1.5 px-3 py-2.5 overflow-x-auto flex-nowrap'
                    + (canScrollLeft && canScrollRight ? ' mask-both'
                       : canScrollRight ? ' mask-right'
                       : canScrollLeft ? ' mask-left' : '')
                  }
                >
                  {widgets.map(function(w) {
                    return <SmartWidget key={w.id} widget={w} />
                  })}
                </div>

                {/* Sol fade overlay — mask-image uzerine ek dumansi katman */}
                <AnimatePresence>
                  {canScrollLeft && (
                    <motion.div
                      key="fade-left"
                      initial={{ opacity: 0 }}
                      animate={{ opacity: 1 }}
                      exit={{ opacity: 0 }}
                      transition={{ duration: 0.18 }}
                      className="smartcard-fade smartcard-fade--left"
                    />
                  )}
                </AnimatePresence>

                {/* Sag fade overlay */}
                <AnimatePresence>
                  {canScrollRight && (
                    <motion.div
                      key="fade-right"
                      initial={{ opacity: 0 }}
                      animate={{ opacity: 1 }}
                      exit={{ opacity: 0 }}
                      transition={{ duration: 0.18 }}
                      className="smartcard-fade smartcard-fade--right"
                    />
                  )}
                </AnimatePresence>

                {/* Sol chevron — hover'da + scroll yapilabiliyorsa */}
                <AnimatePresence>
                  {hovered && canScrollLeft && (
                    <motion.button
                      key="chev-left"
                      type="button"
                      initial={{ opacity: 0, x: -4, scale: 0.9 }}
                      animate={{ opacity: 1, x: 0, scale: 1 }}
                      exit={{ opacity: 0, x: -4, scale: 0.9 }}
                      transition={{ type: 'spring', stiffness: 420, damping: 28 }}
                      whileHover={{ scale: 1.08 }}
                      whileTap={{ scale: 0.94 }}
                      onClick={function(e) { e.stopPropagation(); scrollWidgets('left') }}
                      className="smartcard-chev absolute left-1.5 top-1/2 -translate-y-1/2 w-7 h-7 rounded-full flex items-center justify-center cursor-pointer"
                      title="Onceki widget'lar"
                    >
                      <ChevronLeft size={14} strokeWidth={2.4} />
                    </motion.button>
                  )}
                </AnimatePresence>

                {/* Sag chevron */}
                <AnimatePresence>
                  {hovered && canScrollRight && (
                    <motion.button
                      key="chev-right"
                      type="button"
                      initial={{ opacity: 0, x: 4, scale: 0.9 }}
                      animate={{ opacity: 1, x: 0, scale: 1 }}
                      exit={{ opacity: 0, x: 4, scale: 0.9 }}
                      transition={{ type: 'spring', stiffness: 420, damping: 28 }}
                      whileHover={{ scale: 1.08 }}
                      whileTap={{ scale: 0.94 }}
                      onClick={function(e) { e.stopPropagation(); scrollWidgets('right') }}
                      className="smartcard-chev absolute right-1.5 top-1/2 -translate-y-1/2 w-7 h-7 rounded-full flex items-center justify-center cursor-pointer"
                      title="Sonraki widget'lar"
                    >
                      <ChevronRight size={14} strokeWidth={2.4} />
                    </motion.button>
                  )}
                </AnimatePresence>
              </>
            ) : (
              <div className="px-3 py-2.5">
                <span className="text-[11px] text-slate-400 dark:text-white/40">Widget yok</span>
              </div>
            )}
          </div>


        </div>
      </div>

      {/* Onay modali — portal ile tam ekran ortasında */}
      {confirmOpen && createPortal(
        <div
          style={{ position: 'fixed', inset: 0, zIndex: 10000, background: 'rgba(0,0,0,0.6)', backdropFilter: 'blur(4px)', display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 20 }}
          onClick={handleConfirmNo}
        >
          <div
            style={{ background: '#1e293b', border: '1px solid rgba(255,255,255,0.12)', borderRadius: 16, padding: '32px 28px', maxWidth: 380, width: '90vw', boxShadow: '0 24px 64px rgba(0,0,0,0.5)', display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 12, textAlign: 'center' }}
            onClick={function(e) { e.stopPropagation() }}
          >
            <Trash2 size={26} style={{ color: '#ef4444' }} />
            <h3 style={{ fontSize: '1.05rem', fontWeight: 700, color: '#f1f5f9', margin: 0 }}>Emin misiniz?</h3>
            <p style={{ fontSize: '.84rem', color: '#94a3b8', margin: 0 }}>{confirmMsg}</p>
            <div style={{ display: 'flex', gap: 10, marginTop: 8 }}>
              <button type="button" onClick={handleConfirmNo}
                style={{ padding: '8px 16px', borderRadius: 8, fontSize: '.84rem', fontWeight: 600, background: 'rgba(255,255,255,.07)', color: '#f1f5f9', border: '1px solid rgba(255,255,255,.1)', cursor: 'pointer' }}>
                İptal
              </button>
              <button type="button" onClick={handleConfirmYes}
                style={{ padding: '8px 16px', borderRadius: 8, fontSize: '.84rem', fontWeight: 600, background: 'linear-gradient(135deg,#ef4444,#dc2626)', color: '#fff', border: 'none', cursor: 'pointer', display: 'inline-flex', alignItems: 'center', gap: 6 }}>
                <Trash2 size={13} /> Evet, Sil
              </button>
            </div>
          </div>
        </div>,
        document.body
      )}

      {modalOpen && (
        <div
          className="fixed inset-0 z-[9999] flex items-center justify-center"
          style={{ background: 'rgba(0,0,0,0.55)', backdropFilter: 'blur(4px)' }}
          onClick={function() { setModalOpen(false) }}
        >
          <div
            className="bg-white dark:bg-slate-900 rounded-2xl shadow-2xl w-full max-w-3xl max-h-[80vh] flex flex-col overflow-hidden mx-4"
            onClick={function(e) { e.stopPropagation() }}
          >
            <div className="flex items-center justify-between px-5 py-3 border-b border-slate-200 dark:border-white/[0.08]">
              <h3 className="text-sm font-bold text-slate-800 dark:text-white/90">{modalTitle}</h3>
              <button
                onClick={function() { setModalOpen(false) }}
                className="p-1.5 rounded-lg hover:bg-slate-100 dark:hover:bg-white/5 transition-colors"
              >
                <X size={16} className="text-slate-400" />
              </button>
            </div>
            <div className="flex-1 overflow-y-auto p-4">
              {modalLoading
                ? <div className="flex items-center justify-center py-12 text-slate-400 text-sm">Yukleniyor…</div>
                : <div dangerouslySetInnerHTML={{ __html: modalHtml }} />}
            </div>
          </div>
        </div>
      )}
    </motion.div>
  )
}
