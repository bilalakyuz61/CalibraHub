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
import { CircleDot, ChevronLeft, ChevronRight, X, AlertTriangle, Trash2, Check, Loader2 } from 'lucide-react'
import SmartWidget from './SmartWidget'
import { resolveColor, resolveIcon } from './DynamicWidgetFactory'
import { navigateInWorkspace } from '../../utils/workspaceNav'
import { getTopBody } from '../../utils/topPortal'

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
  amber:   'hover:bg-amber-100 dark:hover:bg-amber-500/10',
  red:     'hover:bg-red-100 dark:hover:bg-red-500/10',
  blue:    'hover:bg-blue-100 dark:hover:bg-blue-500/10',
  green:   'hover:bg-emerald-100 dark:hover:bg-emerald-500/10',
  slate:   'hover:bg-slate-100 dark:hover:bg-white/5',
  emerald: 'hover:bg-emerald-100 dark:hover:bg-emerald-500/10',
  orange:  'hover:bg-orange-100 dark:hover:bg-orange-500/10',
}
var hoverTextMap = {
  amber:   'group-hover:text-amber-600 dark:group-hover:text-amber-400/70',
  red:     'group-hover:text-red-600 dark:group-hover:text-red-400/70',
  blue:    'group-hover:text-blue-600 dark:group-hover:text-blue-400/70',
  green:   'group-hover:text-emerald-600 dark:group-hover:text-emerald-400/70',
  orange:  'group-hover:text-orange-600 dark:group-hover:text-orange-400/70',
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
  // Record values — backend tarafindan entity'ye konulan DB kolon-adi (snake_case)
  // → deger map'i. SmartWidget guide-list popup'i bunu token resolve icin kullanir
  // ('{#code}' → recordValues.code). DOM lookup'i bypass ederek liste sayfasinda
  // dogru kart-bagli filtre saglar.
  var recordValues = (props.recordValues && typeof props.recordValues === 'object')
    ? props.recordValues : {}
  var primaryAction = props.primaryAction || null
  var secondaryAction = props.secondaryAction || null
  var extraActions = Array.isArray(props.extraActions) ? props.extraActions : []
  var onRefresh = typeof props.onRefresh === 'function' ? props.onRefresh : null
  var isHighlighted = !!props.isHighlighted

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
    // displayScope filtresi — guide-list widget'lari icin: 'form' olanlar SmartCard'da
    // gorunmez (sadece edit ekraninda); 'card' ve 'both' kart uzerinde gosterilir.
    // Diger tipler her zaman gorunur.
    var listableRaw = rawWidgets.filter(function(w) {
      if (!w) return false
      var dt = String(w.dataType || '').toLowerCase()
      if (dt !== 'guide-list') return true
      var scope = String((w.metadata && w.metadata.displayScope) || 'both').toLowerCase()
      return scope !== 'form'
    })
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

    // 2026-05-23: Backend "alwaysVisible: true" ile isaretledigi sistem widget'lari
    // visibleIds filtresinden muaftir — kullanici saved config'inde olmasa bile
    // her zaman gosterilir (Durum, KDV, Tarih gibi standart alanlar icin).
    function isAlwaysVisible(w) {
      return w && (w.alwaysVisible === true)
    }

    // Id → widget map
    var map = {}
    listableRaw.forEach(function(w) { if (w && w.id) map[w.id] = w })

    var result = []
    var usedIds = {}

    // Once order'a gore gez
    if (order) {
      order.forEach(function(wid) {
        if (visibleIds && visibleIds.indexOf(wid) === -1 && !isAlwaysVisible(map[wid])) return
        if (map[wid]) {
          result.push(map[wid])
          usedIds[wid] = true
        }
      })
    } else if (visibleIds) {
      // Order yoksa listableRaw sirasi kullanilir
      listableRaw.forEach(function(w) {
        if (w && (visibleIds.indexOf(w.id) !== -1 || isAlwaysVisible(w))) {
          result.push(w)
          usedIds[w.id] = true
        }
      })
    }

    // Order'da olmayan ama visibleIds'de olan (veya visibleIds yoksa tum geri kalanlar) sona
    // alwaysVisible olanlar da burada yakalanir
    listableRaw.forEach(function(w) {
      if (!w || !w.id || usedIds[w.id]) return
      if (visibleIds && visibleIds.indexOf(w.id) === -1 && !isAlwaysVisible(w)) return
      result.push(w)
    })

    return result
  }, [rawWidgets, visibleIds, order])

  var [hovered, setHovered] = useState(false)
  var [loadingActions, setLoadingActions] = useState({})

  // ── Modal state (fetch-modal ekstra aksiyonlar icin) ──
  var [modalOpen,    setModalOpen]    = useState(false)
  var [modalHtml,    setModalHtml]    = useState('')
  var [modalTitle,   setModalTitle]   = useState('')
  var [modalLoading, setModalLoading] = useState(false)

  // ── Onay modalı (silme vb.) ──
  var [confirmOpen, setConfirmOpen]   = useState(false)
  var [confirmMsg,  setConfirmMsg]    = useState('')
  var [confirmOpts, setConfirmOpts]   = useState(null)  // { okLabel, variant: 'danger'|'primary' }
  var confirmCallbackRef = useRef(null)

  // ── Uyarı modalı (api-post hata mesajları — sayfa ortasında) ──
  var [alertOpen, setAlertOpen] = useState(false)
  var [alertMsg,  setAlertMsg]  = useState('')

  function showAlert(message) {
    setAlertMsg(message)
    setAlertOpen(true)
  }

  useEffect(function() {
    if (!alertOpen) return
    function onKey(e) { if (e.key === 'Escape' || e.key === 'Enter') setAlertOpen(false) }
    document.addEventListener('keydown', onKey)
    return function() { document.removeEventListener('keydown', onKey) }
  }, [alertOpen])

  // ── Inline KITT aksiyonu (modal yerine kart altinda acilan dar form seridi) ──
  // Mail gonderme, hizli not, durum degistirme gibi "kucuk aksiyonlar" modal
  // acmadan ayni kart icinde cozulur. State:
  //   null                     → kapali
  //   { action, values, status, message }
  //     status: 'idle' | 'sending' | 'success' | 'error'
  var [kitt, setKitt] = useState(null)

  // Modal content — dangerouslySetInnerHTML <script> etiketlerini execute
  // etmedigi icin node'a manuel inject ediyoruz. ref callback yerine stable
  // ref + useEffect kullaniyoruz — aksi halde kart hover state degisiminde
  // ref callback her render'da tetiklenip iframe'i yeniden mount ediyor
  // (kullaniciya "fare gezdirdikce sayfa yenileniyor" gibi gorunur).
  var modalContentRef = useRef(null)
  useEffect(function () {
    if (!modalOpen || modalLoading) return
    var node = modalContentRef.current
    if (!node) return
    // Ayni HTML'i tekrar inject etmeyelim — mount bir kez yapilsin
    if (node.getAttribute('data-sm-modal-html-hash') === String(modalHtml.length + ':' + modalHtml.slice(0, 32))) return
    node.innerHTML = modalHtml
    node.setAttribute('data-sm-modal-html-hash', String(modalHtml.length + ':' + modalHtml.slice(0, 32)))
    // <script> tag'lerini yeni element ile replaceChild ederek browser execute eder
    var scripts = node.querySelectorAll('script')
    scripts.forEach(function (oldScript) {
      var newScript = document.createElement('script')
      for (var i = 0; i < oldScript.attributes.length; i++) {
        var attr = oldScript.attributes[i]
        newScript.setAttribute(attr.name, attr.value)
      }
      newScript.textContent = oldScript.textContent
      oldScript.parentNode.replaceChild(newScript, oldScript)
    })
  }, [modalOpen, modalLoading, modalHtml])

  function showConfirm(message, callback, opts) {
    setConfirmMsg(message)
    setConfirmOpts(opts || null)
    confirmCallbackRef.current = callback
    setConfirmOpen(true)
  }

  useEffect(function() {
    if (!confirmOpen) return
    function onKey(e) { if (e.key === 'Escape') handleConfirmNo() }
    document.addEventListener('keydown', onKey)
    return function() { document.removeEventListener('keydown', onKey) }
  }, [confirmOpen])

  // 'close-sc-modal' custom event'i — partial view'lar kaydet+kapat sonrasi gonderir.
  // window.dispatchEvent(new CustomEvent('close-sc-modal', { detail: { refresh: true } }))
  useEffect(function () {
    if (!modalOpen) return
    function onClose(e) {
      setModalOpen(false)
      var refresh = e && e.detail && e.detail.refresh
      if (refresh && props.onRefresh) props.onRefresh(null)
    }
    window.addEventListener('close-sc-modal', onClose)
    return function () { window.removeEventListener('close-sc-modal', onClose) }
  }, [modalOpen, props.onRefresh])
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

  /**
   * Aksiyon dispatch — action.openInTab varsa Shell.openWorkspaceTab API'siyle
   * yeni/mevcut tab'da acar (caller tab'i kapatmaz). Aksi halde aynı tab'da navigate.
   * openInTab: { title: 'Malzeme Kartlari', matchPath: '/Logistics/MaterialCard' }
   */
  function dispatchActionUrl(action) {
    if (!action || !action.url) return
    if (action.openInTab) {
      try {
        if (window.top && window.top.CalibraHub && typeof window.top.CalibraHub.openWorkspaceTab === 'function') {
          window.top.CalibraHub.openWorkspaceTab({
            url: action.url,
            title: action.openInTab.title || action.label || 'Yeni Sekme',
            matchPath: action.openInTab.matchPath || null,
          })
          return
        }
      } catch (e) { /* cross-origin — fallback */ }
    }
    // Hash-only URL'lerde workspace nav'i bypass et: navigateInWorkspace iframe icinde
    // ?workspace=1 ekledigi icin hash icine query string sokuyor (#detail-1?workspace=1),
    // bu da host sayfanin hashchange listener regex'ini bozuyor. Pure hash icin direct set.
    if (typeof action.url === 'string' && action.url.charAt(0) === '#') {
      try { window.location.hash = action.url } catch (e) { /* fallback */ }
      // Ayni hash zaten setli ise hashchange fire etmez — manuel tetikle.
      try { window.dispatchEvent(new HashChangeEvent('hashchange')) } catch (e) { /* ignore */ }
      return
    }
    navigateInFrame(action.url)
  }

  function handlePrimary(e) {
    e.stopPropagation()
    if (primaryAction) dispatchActionUrl(primaryAction)
  }

  function executeSecondary() {
    if (!secondaryAction) return
    if (secondaryAction.apiUrl) {
      // Anti-forgery token (Razor'in hidden input'u — _Layout veya partial _ValidationScripts injekte eder)
      var tokenEl = document.querySelector('input[name="__RequestVerificationToken"]')
      var token = tokenEl ? tokenEl.value : ''
      var method = (secondaryAction.apiMethod || 'POST').toUpperCase()
      var hasBody = secondaryAction.apiBody != null
      var headers = { 'Accept': 'application/json' }
      if (token) headers['RequestVerificationToken'] = token
      if (hasBody) headers['Content-Type'] = 'application/json'

      var fetchOpts = { method: method, credentials: 'same-origin', headers: headers }
      if (hasBody) fetchOpts.body = JSON.stringify(secondaryAction.apiBody)

      fetch(secondaryAction.apiUrl, fetchOpts)
        .then(function(r) {
          // 200/4xx fark etmeksizin body'yi text olarak al — JSON parse'ı manuel dene
          // (boş body veya HTML hata sayfasında JSON.parse "Unexpected end of JSON input" patlamasını engeller).
          return r.text().then(function(txt) { return { status: r.status, ok: r.ok, txt: txt } })
        })
        .then(function(res) {
          var data = null
          if (res.txt) { try { data = JSON.parse(res.txt) } catch (_) { /* JSON değil — HTML hata sayfası vb. */ } }

          // Backend konvansiyonu: { ok: true } veya { ok: false, error: "..." }
          // Eski/farklı endpoint'ler için: { success: true/false, message: "..." } da destekli.
          var serverOk = data && (data.ok === true || data.success === true)
          var serverFailMsg = data && (data.error || data.message)

          if (!res.ok || (data && (data.ok === false || data.success === false))) {
            var msg = serverFailMsg || ('İstek başarısız (HTTP ' + res.status + ')')
            if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast(msg, 'err')
            else alert('Hata: ' + msg)
            return
          }
          if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast('İşlem tamamlandı.', 'ok')
          if (onRefresh) setTimeout(function () { onRefresh(id) }, 400)
          else setTimeout(function () { window.location.reload() }, 600)
        })
        .catch(function(err) {
          if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast('Hata: ' + err.message, 'err')
          else alert('Hata: ' + err.message)
        })
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

  // ── Inline KITT handlers ──
  // openKitt: aksiyonu kart altinda ac, fields icindeki defaultValue'lari al
  function openKitt(action) {
    var values = {}
    var fields = Array.isArray(action.fields) ? action.fields : []
    fields.forEach(function(f) {
      values[f.name] = (f.defaultValue != null) ? String(f.defaultValue) : ''
    })
    setKitt({ action: action, values: values, status: 'idle', message: '' })
  }
  function closeKitt() { setKitt(null) }
  function updateKittField(name, value) {
    setKitt(function(k) {
      if (!k) return k
      var nv = Object.assign({}, k.values); nv[name] = value
      return Object.assign({}, k, { values: nv })
    })
  }
  function submitKitt(ev) {
    if (ev && ev.preventDefault) ev.preventDefault()
    if (!kitt) return
    var action = kitt.action
    var apiUrl = (action.apiUrl || '').replace('{id}', id)
    // body = action.body (sabitler) + kullanici degerleri
    var body = Object.assign({}, action.body || {}, kitt.values)
    var token = (document.querySelector('input[name="__RequestVerificationToken"]') || {}).value || ''
    var fd = new FormData()
    Object.keys(body).forEach(function(k) { fd.append(k, String(body[k] == null ? '' : body[k])) })
    if (token) fd.append('__RequestVerificationToken', token)
    setKitt(function(k) { return k ? Object.assign({}, k, { status: 'sending', message: '' }) : null })
    fetch(apiUrl, { method: 'POST', body: fd, credentials: 'same-origin' })
      .then(function(r) { return r.json() })
      .then(function(d) {
        if (d && d.success) {
          setKitt(function(k) {
            return k ? Object.assign({}, k, {
              status: 'success',
              message: d.message || action.successMessage || 'Tamamlandi'
            }) : null
          })
          // Kullanici kisa bir onay flashi gorur, sonra serit otomatik kapanir.
          setTimeout(function() { setKitt(null) }, 1100)
        } else {
          setKitt(function(k) {
            return k ? Object.assign({}, k, {
              status: 'error',
              message: (d && d.message) || 'Hata'
            }) : null
          })
        }
      })
      .catch(function(err) {
        setKitt(function(k) {
          return k ? Object.assign({}, k, { status: 'error', message: err.message || 'Hata' }) : null
        })
      })
  }

  // handleExtraAction — fetch-modal, download, api-post, navigate, inline-kitt
  function handleExtraAction(e, action) {
    e.stopPropagation()

    // inline-kitt: kart altinda dar seritte form — modal acmaz.
    if (action.type === 'inline-kitt') {
      // Ayni aksiyona tekrar tiklandiysa kapat (toggle davranisi)
      if (kitt && kitt.action === action) closeKitt()
      else openKitt(action)
      return
    }

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

    // trigger: window.CalibraHub.openXxxModal({ payload }) seklinde global helper'a delege.
    // Server-side config'te { type: 'trigger', trigger: 'convert-single-quote-modal', payload: {...} }
    if (action.type === 'trigger') {
      var ch = (typeof window !== 'undefined') && window.CalibraHub
      var triggerName = action.trigger || ''
      if (triggerName === 'convert-single-quote-modal' && ch && typeof ch.openConvertSingleQuoteModal === 'function') {
        ch.openConvertSingleQuoteModal(Object.assign({}, action.payload || {}, {
          onSuccess: function() {
            try { window.location.reload() } catch (e) { /* ignore */ }
          },
        }))
      } else if (triggerName === 'convert-orders-modal' && ch && typeof ch.openConvertToOrdersModal === 'function') {
        ch.openConvertToOrdersModal({
          onSuccess: function() { try { window.location.reload() } catch (e) { /* ignore */ } }
        })
      } else if (triggerName === 'price-group-contacts-modal' && ch && typeof ch.openPriceGroupContactsModal === 'function') {
        // Cariler eslestirme modali — payload: { groupId, groupCode, groupName }
        ch.openPriceGroupContactsModal(Object.assign({}, action.payload || {}))
      } else {
        console.warn('[SmartCard] Unknown trigger:', triggerName)
      }
      return
    }

    if (action.type === 'api-post') {
      if (action.confirm) {
        showConfirm(action.confirm,
          function() { handleExtraAction(e, Object.assign({}, action, { confirm: null })) },
          { okLabel: action.confirmOkLabel, variant: action.confirmVariant })
        return
      }
      var postUrl = (action.url || '').replace('{id}', id)
      var token = (document.querySelector('input[name="__RequestVerificationToken"]') || {}).value || ''
      var fd = new FormData()
      if (action.body) Object.keys(action.body).forEach(function(k) { fd.append(k, String(action.body[k])) })
      if (token) fd.append('__RequestVerificationToken', token)
      var actionKey = action.id || action.label || 'api-post'
      setLoadingActions(function(prev) { var n = Object.assign({}, prev); n[actionKey] = true; return n })
      fetch(postUrl, { method: 'POST', body: fd, credentials: 'same-origin' })
        .then(function(r) { return r.json() })
        .then(function(data) {
          setLoadingActions(function(prev) { var n = Object.assign({}, prev); delete n[actionKey]; return n })
          // Hata: iki yanıt şekli de tanınır — { success:false, message } ve { ok:false, error }.
          // Mesaj sayfa ortasında uyarı modalıyla gösterilir (toast değil).
          if (data && (data.success === false || data.ok === false)) {
            showAlert(data.message || data.error || 'İşlem gerçekleştirilemedi.')
          }
          else if (onRefresh) onRefresh(id)
          else window.location.reload()
        })
        .catch(function(err) {
          setLoadingActions(function(prev) { var n = Object.assign({}, prev); delete n[actionKey]; return n })
          showAlert('Bağlantı hatası: ' + err.message)
        })
      return
    }

    if (action.url) navigateInFrame((action.url).replace('{id}', id))
  }

  // Action button renderer — colorHint uses hoverBgMap/hoverTextMap
  // action.hideButton === true → aksiyon var ama sol seritte buton render
  // edilmez (ornegin "Duzenle" — sadece kartin kimlik alanina tiklayinca
  // devreye giren URL icin kullanilir).
  function renderActionButton(action, handlerOrKey, colorHint) {
    if (!action || action.hideButton) return null
    var ActionIcon = resolveIcon(action.icon)
    var isDisabled = !!action.disabled
    var actionKey = action.id || action.label || 'api-post'
    var isLoading = !!loadingActions[actionKey]
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
        onClick={(isDisabled || isLoading) ? function(e) { e.stopPropagation() } : handler}
        disabled={isDisabled || isLoading}
        className={'p-2 rounded-xl transition-colors group ' + ((isDisabled || isLoading) ? 'opacity-50 cursor-not-allowed' : bgClass)}
        title={isLoading ? 'İşleniyor…' : (action.label || '')}
      >
        {isLoading
          ? <Loader2 size={18} className="text-slate-400 dark:text-white/40 animate-spin" />
          : <ActionIcon
              size={18}
              className={'text-slate-400 dark:text-white/40 transition-colors ' + (isDisabled ? '' : textClass)}
            />
        }
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
          border: isHighlighted ? '1px solid rgba(99,102,241,0.7)' : '1px solid rgba(255, 255, 255, 0.12)',
          boxShadow: isHighlighted ? '0 0 0 3px rgba(99,102,241,0.18), 0 8px 32px rgba(99,102,241,0.12)' : undefined,
          transition: 'border-color 0.4s ease, box-shadow 0.4s ease',
        } : {
          background: 'rgba(255, 255, 255, 0.95)',
          backdropFilter: 'blur(24px)',
          WebkitBackdropFilter: 'blur(24px)',
          border: isHighlighted ? '1px solid rgba(99,102,241,0.6)' : '1px solid rgba(15, 23, 42, 0.1)',
          boxShadow: isHighlighted ? '0 0 0 3px rgba(99,102,241,0.12), 0 4px 24px rgba(99,102,241,0.1)' : undefined,
          transition: 'border-color 0.4s ease, box-shadow 0.4s ease',
        }}
      >
        <div
          className="flex items-center gap-0"
          // hideButton:true demek "buton goster ama tum kart govdesi navigate edilebilir".
          // Bu durumda widget alanini + bos alani da tiklanabilir hale getiriyoruz.
          // Action butonlari (handlePrimary/handleSecondary/handleExtraAction) zaten
          // stopPropagation yapiyor — onlara tiklayinca buradaki click dispatch olmaz.
          onClick={(primaryAction && primaryAction.hideButton)
            ? function (e) {
                // Iclerden gelen butonlar stopPropagation yaptigi icin burada bubble
                // edilmis click sadece "bos alan / kimlik disi / widget alani" demek.
                dispatchActionUrl(primaryAction)
              }
            : undefined}
          style={(primaryAction && primaryAction.hideButton) ? { cursor: 'pointer' } : undefined}
        >

          {/* Sol: Aksiyonlar */}
          {(primaryAction || secondaryAction || extraActions.length > 0) && (
            <>
              <div className="flex items-center gap-1 px-3 flex-shrink-0">
                {renderActionButton(primaryAction, handlePrimary, 'amber')}
                {renderActionButton(secondaryAction, handleSecondary, 'red')}
                {extraActions.map(function(action, i) {
                  if (!action) return <span key={'sp' + i} style={{display:'inline-block',width:34,height:34,flexShrink:0}} />
                  return renderActionButton(action, function(e) { handleExtraAction(e, action) }, action.color || 'slate')
                })}
              </div>
              <div className="w-px h-10 bg-slate-200 dark:bg-white/[0.06] flex-shrink-0" />
            </>
          )}

          {/* Kimlik — belge numarasi (subtitle) + cari ismi (title) tiklanabilir;
              primaryAction (Duzenle) sayfasina gider. Hover'da hafif bg
              vurgusu + title altinda indigo renk degisimi ile tiklanabilir
              oldugu nettir. */}
          <div
            className="flex items-center gap-3.5 pl-3 pr-5 py-3.5 flex-shrink-0 w-[340px] cursor-pointer group transition-colors hover:bg-slate-100/70 dark:hover:bg-white/[0.04]"
            role="button"
            tabIndex={0}
            title={primaryAction && primaryAction.label
              ? (primaryAction.label + ' — ' + (title || ''))
              : (title || '')}
            onClick={function(e) {
              e.preventDefault()
              e.stopPropagation()
              if (primaryAction) dispatchActionUrl(primaryAction)
            }}
            onKeyDown={function(e) {
              if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault()
                if (primaryAction) dispatchActionUrl(primaryAction)
              }
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
                    // 2026-05-26: Subtitle'da email varsa uppercase YAPMA — okunabilirlik.
                    // Belge no/kod gibi alanlar uppercase kalir (mevcut davranis).
                    // `subtitleCase` prop'u 'normal' verilmisse de uppercase iptal edilir.
                    (function () {
                      var preserveCase = props.subtitleCase === 'normal'
                        || (typeof subtitle === 'string' && subtitle.indexOf('@') !== -1)
                      return (
                        <span className={
                          'text-[13px] font-mono font-semibold tracking-wide text-slate-700 dark:text-white/85 truncate group-hover:text-indigo-600 dark:group-hover:text-indigo-300 transition-colors'
                          + (preserveCase ? '' : ' uppercase')
                        }>
                          {subtitle}
                        </span>
                      )
                    })()
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
              <h3 className="text-sm font-bold text-slate-900 dark:text-white tracking-tight leading-tight truncate transition-colors group-hover:text-indigo-600 dark:group-hover:text-indigo-300">
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
                    return <SmartWidget key={w.id} widget={w} recordValues={recordValues} />
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

        {/* ── Inline KITT aksiyon seridi — ana satirin altinda, ayni kart icinde.
            Mail gonderme gibi "kucuk aksiyonlar" modal acmadan burada cozulur.
            Basari durumunda animasyonla kapanir (1.1s sonra setKitt(null)). */}
        <AnimatePresence>
          {kitt && (
            <motion.div
              key="smartcard-kitt"
              initial={{ height: 0, opacity: 0 }}
              animate={{ height: 'auto', opacity: 1 }}
              exit={{ height: 0, opacity: 0 }}
              transition={{ duration: 0.22, ease: [0.23, 1, 0.32, 1] }}
              style={{ overflow: 'hidden' }}
            >
              <div
                className={'smartcard-kitt__row ' + (isDark ? 'smartcard-kitt__row--dark' : 'smartcard-kitt__row--light')}
                data-status={kitt.status}
              >
                <div className="smartcard-kitt__scanner" aria-hidden="true" />
                <form onSubmit={submitKitt} className="smartcard-kitt__form">
                  {(kitt.action.fields || []).map(function(f) {
                    return (
                      <input
                        key={f.name}
                        type={f.type || 'text'}
                        name={f.name}
                        placeholder={f.placeholder || ''}
                        required={f.required ? true : undefined}
                        value={kitt.values[f.name] || ''}
                        onChange={function(ev) { updateKittField(f.name, ev.target.value) }}
                        disabled={kitt.status === 'sending' || kitt.status === 'success'}
                        className="smartcard-kitt__input"
                        style={{ flex: f.flex || 1 }}
                      />
                    )
                  })}
                  <button
                    type="submit"
                    className="smartcard-kitt__btn smartcard-kitt__btn--primary"
                    disabled={kitt.status === 'sending' || kitt.status === 'success'}
                  >
                    {kitt.status === 'sending'
                      ? 'Gonderiliyor…'
                      : kitt.status === 'success'
                        ? (<><Check size={13} strokeWidth={2.6} style={{ marginRight: 4 }} />Gonderildi</>)
                        : (kitt.action.submitLabel || 'Gonder')}
                  </button>
                  <button
                    type="button"
                    onClick={closeKitt}
                    className="smartcard-kitt__btn smartcard-kitt__btn--ghost"
                    aria-label="Kapat"
                    title="Kapat"
                  >
                    <X size={14} strokeWidth={2.5} />
                  </button>
                </form>
                {kitt.status === 'error' && (
                  <div className="smartcard-kitt__error">
                    <AlertTriangle size={12} /> {kitt.message}
                  </div>
                )}
              </div>
            </motion.div>
          )}
        </AnimatePresence>
      </div>

      {/* Onay modali — portal ile tam ekran ortasında */}
      {confirmOpen && createPortal(
        (function() {
          var isPrimary = confirmOpts && confirmOpts.variant === 'primary'
          var okLabel   = (confirmOpts && confirmOpts.okLabel) || (isPrimary ? 'Evet, Devam' : 'Evet, Sil')
          var OkIcon    = isPrimary ? Check : Trash2
          var okBg      = isPrimary ? 'linear-gradient(135deg,#10b981,#059669)' : 'linear-gradient(135deg,#ef4444,#dc2626)'
          return (
        <div
          style={{ position: 'fixed', inset: 0, zIndex: 10000, background: 'rgba(0,0,0,0.6)', backdropFilter: 'blur(4px)', display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 20 }}
          onClick={handleConfirmNo}
        >
          <div
            style={{ background: '#1e293b', border: '1px solid rgba(255,255,255,0.12)', borderRadius: 16, padding: '32px 28px', maxWidth: 380, width: '90vw', boxShadow: '0 24px 64px rgba(0,0,0,0.5)', display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 12, textAlign: 'center' }}
            onClick={function(e) { e.stopPropagation() }}
          >
            {isPrimary
              ? <Check size={26} style={{ color: '#10b981' }} />
              : <Trash2 size={26} style={{ color: '#ef4444' }} />}
            <h3 style={{ fontSize: '1.05rem', fontWeight: 700, color: '#f1f5f9', margin: 0 }}>Emin misiniz?</h3>
            <p style={{ fontSize: '.84rem', color: '#94a3b8', margin: 0 }}>{confirmMsg}</p>
            <div style={{ display: 'flex', gap: 10, marginTop: 8 }}>
              <button type="button" onClick={handleConfirmNo}
                style={{ padding: '8px 16px', borderRadius: 8, fontSize: '.84rem', fontWeight: 600, background: 'rgba(255,255,255,.07)', color: '#f1f5f9', border: '1px solid rgba(255,255,255,.1)', cursor: 'pointer' }}>
                İptal
              </button>
              <button type="button" onClick={handleConfirmYes}
                style={{ padding: '8px 16px', borderRadius: 8, fontSize: '.84rem', fontWeight: 600, background: okBg, color: '#fff', border: 'none', cursor: 'pointer', display: 'inline-flex', alignItems: 'center', gap: 6 }}>
                <OkIcon size={13} /> {okLabel}
              </button>
            </div>
          </div>
        </div>
          )
        })(),
        getTopBody()
      )}

      {/* Uyarı modali — api-post hataları sayfa ortasında (toast değil) */}
      {alertOpen && createPortal(
        <div
          style={{ position: 'fixed', inset: 0, zIndex: 10000, background: 'rgba(0,0,0,0.6)', backdropFilter: 'blur(4px)', display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 20 }}
          onClick={function() { setAlertOpen(false) }}
        >
          <div
            style={{ background: '#1e293b', border: '1px solid rgba(255,255,255,0.12)', borderRadius: 16, padding: '32px 28px', maxWidth: 400, width: '90vw', boxShadow: '0 24px 64px rgba(0,0,0,0.5)', display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 12, textAlign: 'center' }}
            onClick={function(e) { e.stopPropagation() }}
          >
            <AlertTriangle size={26} style={{ color: '#f59e0b' }} />
            <h3 style={{ fontSize: '1.05rem', fontWeight: 700, color: '#f1f5f9', margin: 0 }}>İşlem Yapılamadı</h3>
            <p style={{ fontSize: '.84rem', color: '#94a3b8', margin: 0, lineHeight: 1.5 }}>{alertMsg}</p>
            <button type="button" onClick={function() { setAlertOpen(false) }} autoFocus
              style={{ padding: '8px 22px', borderRadius: 8, fontSize: '.84rem', fontWeight: 600, marginTop: 8, background: 'linear-gradient(135deg,#6366f1,#4f46e5)', color: '#fff', border: 'none', cursor: 'pointer' }}>
              Tamam
            </button>
          </div>
        </div>,
        getTopBody()
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
                : <div ref={modalContentRef} data-sm-modal-content />}
            </div>
          </div>
        </div>
      )}
    </motion.div>
  )
}
