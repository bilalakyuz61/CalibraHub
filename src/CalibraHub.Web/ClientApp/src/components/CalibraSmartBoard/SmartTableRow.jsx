/**
 * SmartTableRow — SmartTable icin tek satir (<tr>).
 *
 * SmartCard ile ayni entity JSON sozlesmesini kullanir (id, title, widgets,
 * primaryAction, secondaryAction, extraActions, recordValues). Silme onayi
 * native confirm() DEGIL, SmartCard ile ayni portal-modal deseni (CLAUDE.md
 * "Silme onay standardi").
 *
 * Satir yapisi (2026-07-16 revizyon 4 — kompozit kimlik hucresi kaldirildi):
 *   [Islemler (basliksiz kebab, sticky-left 0'da)] [widget sutunlari...]
 * Onceki ozel "Kod/Ad" kimlik hucresi TAMAMEN KALKTI — entity.title/subtitle/
 * description/imageUrl/statusBadge burada ARTIK OKUNMUYOR (Stok Kodu/Stok
 * Adi artik normal widget sutunu olarak columns[] icinden geliyor, bkz.
 * SmartTable.jsx). Row-level aggregate "violations" rozeti de kimlik
 * hucresiyle birlikte kalkti — per-hucre ihlal gostergesi (TableValueCell
 * icindeki checkConstraintViolation) ETKILENMEDI, o ayri bir mekanizma.
 *
 * Satır tıklaması → Düzenle (primaryAction): kimlik hucresi kalktigi icin
 * artik TUM SATIRA (<tr onClick>) tasindi. Menu/link/guide-list butonlari
 * kendi onClick'lerinde stopPropagation cagirir, bu yuzden satir-tiklamasi
 * ile cakismaz.
 *
 * Tek-satir/kompakt: her deger hucresi tek satir (white-space:nowrap +
 * ellipsis + title tooltip) — bkz. index.css ".cst-value__text" (zaten
 * boyleydi; kimlik hucresinin kalkmasiyla artik TUM satir tek-satirlik oldu).
 *
 * Satir aksiyon menusu — "Islemler": Duzenle (primaryAction) + entity.
 * extraActions[] (GENERIC, hardcode yok — board config'e yeni aksiyon
 * eklendiginde otomatik menude belirir) + en altta, ayracin ardindan, danger
 * renkli **Sil** ogesi (secondaryAction). Sil'e tiklamak mevcut
 * handleSecondary/proceedSecondary/executeSecondary akisini AYNEN tetikler —
 * precheckUrl → ekran-ortasi custom onay modali → apiUrl POST. Sadece tetik
 * NOKTASI degisti (standalone buton yerine menu item), akis mantigi hic
 * degismedi. Dropdown, tablo `overflow` kirpmasindan kacmak icin
 * document.body'ye portal edilir; cross-document (iframe→top) portal
 * senaryosunda CSS class'lari uygulanamayabildigi icin (ayri document, ayri
 * stylesheet) mevcut confirm/alert modallerindeki gibi INLINE stil
 * kullanilir — ama isDark'a gore tema-farkindadir.
 *
 * Per-sutun bicim (SmartColumnSettings.jsx): SmartTable.computeColumns her
 * `column` objesine align/width/pinned/stickyLeft/fontSize/fontWeight/label
 * cozumlenmis olarak ekler; bu dosya sadece render eder (tdStyleFor/
 * justifyFor/fontStyleFor helper'lari).
 *
 * Sticky opaklik: sticky hucreler `--cst-sticky-bg` (TAM OPAK, glassmorphic
 * `--cst-row-bg`'den FARKLI bir token) kullanir — aksi halde (ozellikle dark
 * temada `--cst-row-bg` ~%4 opak oldugu icin) kaydirilan veri sutunlari
 * sticky hucrelerin altindan "seffaf" gorunerek uzerine biniyormus gibi
 * gorunur. Bkz. index.css.
 */
import { useState, useMemo, useEffect, useRef } from 'react'
import { createPortal } from 'react-dom'
import { AlertTriangle, Trash2, X, ArrowUpRight, List, MoreVertical } from 'lucide-react'
import { resolveIcon, resolveColorForTheme, formatValue, resolveBooleanIcon } from './DynamicWidgetFactory'
import { checkConstraintViolation, resolveTokensWithRecord } from './SmartWidget'
import GuideListField from '../DynamicWidgetRenderer/GuideListField'
import { navigateInWorkspace } from '../../utils/workspaceNav'
import { getTopBody } from '../../utils/topPortal'

/* ── Per-sutun render yardimcilari — align/pin/font tum hucre tiplerinde ortak ── */
function tdStyleFor(column) {
  var style = { textAlign: (column && column.align) || 'left' }
  if (column && column.pinned) {
    style.position = 'sticky'
    style.left = column.stickyLeft || 0
  }
  return style
}
function justifyFor(column) {
  var align = column && column.align
  return align === 'center' ? 'center' : align === 'right' ? 'flex-end' : 'flex-start'
}
function fontStyleFor(column) {
  var style = {}
  if (column && column.fontSize) style.fontSize = column.fontSize + 'px'
  if (column && column.fontWeight) style.fontWeight = column.fontWeight
  return style
}

/* ── Deger hucresi — link/guide-list/boolean/default dispatch ──────────── */
function TableValueCell(props) {
  var column = props.column
  var widget = props.widget
  var colorOverride = props.colorOverride
  var recordValues = props.recordValues
  var isDark = props.isDark

  var tdClass = 'cst-td cst-td--value' + (column.pinned ? ' cst-td--pinned' : '')
  var tdStyle = tdStyleFor(column)
  var fontStyle = fontStyleFor(column)
  // Stok Kodu (w_kod) — onceki kimlik hucresindeki kod estetigini korur
  // (monospace); DIGER hicbir ayari (align/width/pin/font/rename) etkilemez,
  // sadece taban font-family secimi — kullanici Boyut/Kalinlik ile serbestce
  // degistirebilir (fontStyle Object.assign ile bunun UZERINE yazar).
  var isCodeStyle = column.id === 'w_kod'

  if (!widget) {
    return (
      <td className={tdClass} style={tdStyle}>
        <span className="cst-value cst-value--empty" style={{ justifyContent: justifyFor(column) }}>—</span>
      </td>
    )
  }

  var dataType = widget.dataType || column.dataType || null
  var dtLower = String(dataType || '').toLowerCase()
  var type = widget.type || 'data'

  if (dtLower === 'guide-list') {
    return (
      <td className={tdClass} style={tdStyle}>
        <GuideListTableTrigger widget={widget} column={column} recordValues={recordValues} isDark={isDark} />
      </td>
    )
  }

  if (type === 'link') {
    return (
      <td className={tdClass} style={tdStyle}>
        <LinkValueCell widget={widget} column={column} isDark={isDark} />
      </td>
    )
  }

  var hasValue = widget.value != null && widget.value !== ''
  var displayValue = hasValue ? (dataType ? formatValue(widget.value, dataType) : String(widget.value)) : '—'
  var violation = checkConstraintViolation(widget)

  if (dtLower === 'boolean') {
    var BoolIcon = resolveBooleanIcon(widget.value)
    var isTrue = (widget.value === true || widget.value === 'true' || widget.value === 1 || widget.value === '1')
    var boolColor = isTrue ? '#10b981' : '#ef4444'
    return (
      <td className={tdClass} style={tdStyle} title={widget.detail || displayValue}>
        <span className="cst-value" style={{ justifyContent: justifyFor(column) }}>
          <BoolIcon size={14} style={{ color: boolColor, flexShrink: 0 }} />
          <span className="cst-value__text" style={Object.assign({ color: boolColor }, fontStyle)}>{displayValue}</span>
        </span>
      </td>
    )
  }

  var palette = resolveColorForTheme(colorOverride || widget.color, dataType, isDark)
  var numericFamily = dtLower === 'numeric' || dtLower === 'currency' || dtLower === 'percent'
  var tooltip = violation ? (displayValue + ' — Kısıt ihlali: ' + violation) : (widget.detail || displayValue)

  return (
    <td className={tdClass} style={tdStyle} title={tooltip}>
      <span className="cst-value" style={{ justifyContent: justifyFor(column) }}>
        {violation && <AlertTriangle size={12} style={{ color: '#f59e0b', flexShrink: 0 }} />}
        <span
          className={'cst-value__text' + ((numericFamily || isCodeStyle) ? ' cst-value--numeric' : '') + (!hasValue ? ' cst-value--empty' : '')}
          style={Object.assign({ color: violation ? '#f59e0b' : (hasValue ? palette.text : undefined) }, fontStyle)}
        >
          {displayValue}
        </span>
      </span>
    </td>
  )
}

/* ── Link tipi widget — kisa yol butonu, deger yoksa label gosterir ─────── */
function LinkValueCell(props) {
  var widget = props.widget
  var column = props.column || {}
  var isDark = props.isDark
  var palette = resolveColorForTheme(widget.color, widget.dataType, isDark)
  var Icon = resolveIcon(widget.icon, ArrowUpRight, widget.dataType)
  var hasValue = widget.value != null && widget.value !== ''
  var text = hasValue ? formatValue(widget.value, widget.dataType) : (widget.label || 'Git')
  var textStyle = Object.assign({ color: palette.text }, fontStyleFor(column))

  function handleClick(e) {
    e.stopPropagation()
    if (widget.url) window.location.href = widget.url
  }

  return (
    <button type="button" onClick={handleClick} className="cst-link" title={widget.detail || (widget.url ? ('Git: ' + widget.url) : '')}>
      <Icon size={13} style={{ color: palette.icon, flexShrink: 0 }} />
      <span style={textStyle}>{text}</span>
    </button>
  )
}

/* ── Rehber Listesi (guide-list) — ayni portal-modal mekanizmasi, kompakt
     tetikleyici (SmartWidget'taki GuideListWidgetButton'in tablo icin sade
     versiyonu — sutun basliginda label zaten var, hucrede tekrar etmez). ── */
function GuideListTableTrigger(props) {
  var widget = props.widget
  var column = props.column
  var recordValues = props.recordValues || {}
  var isDark = props.isDark
  var meta = (widget && widget.metadata) || {}
  var palette = resolveColorForTheme(widget.color, 'guide-list', isDark)
  var Icon = resolveIcon(column.icon, List, 'guide-list')
  var [open, setOpen] = useState(false)

  var guideConfigParsed = null
  try {
    guideConfigParsed = (typeof meta.guideConfig === 'string') ? JSON.parse(meta.guideConfig) : (meta.guideConfig || null)
  } catch (e) { guideConfigParsed = null }
  var rawConstraint = (guideConfigParsed && guideConfigParsed.constraint) || ''
  var resolvedConstraint = resolveTokensWithRecord(rawConstraint, recordValues)

  useEffect(function () {
    if (!open) return undefined
    function onKey(e) { if (e.key === 'Escape') setOpen(false) }
    document.addEventListener('keydown', onKey)
    return function () { document.removeEventListener('keydown', onKey) }
  }, [open])

  return (
    <>
      <button
        type="button"
        onClick={function (e) { e.stopPropagation(); e.preventDefault(); setOpen(true) }}
        className="cst-link"
        title={meta.guideCode ? ('Rehber: ' + meta.guideCode) : 'Rehber tanımlı değil'}
      >
        <Icon size={13} style={{ color: palette.icon, flexShrink: 0 }} />
        <span style={fontStyleFor(column)}>Aç</span>
      </button>
      {open && createPortal(
        <div
          onClick={function () { setOpen(false) }}
          style={{ position: 'fixed', inset: 0, zIndex: 10300, background: 'rgba(0,0,0,0.55)', backdropFilter: 'blur(2px)', display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 16 }}
        >
          <div
            onClick={function (e) { e.stopPropagation() }}
            style={{ width: '100%', maxWidth: 1080, maxHeight: '85vh', background: 'rgba(13,17,27,0.98)', border: '1px solid rgba(255,255,255,0.12)', borderRadius: 14, boxShadow: '0 16px 48px rgba(0,0,0,0.55)', display: 'flex', flexDirection: 'column', overflow: 'hidden' }}
          >
            <div style={{ display: 'flex', alignItems: 'center', gap: 10, padding: '14px 18px', borderBottom: '1px solid rgba(255,255,255,0.08)' }}>
              <div style={{ width: 30, height: 30, borderRadius: 8, display: 'flex', alignItems: 'center', justifyContent: 'center', background: palette.bg, border: '1px solid ' + palette.border }}>
                <Icon size={15} style={{ color: palette.icon }} strokeWidth={1.8} />
              </div>
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{ fontSize: 14, fontWeight: 700, color: 'rgba(255,255,255,0.92)' }}>{column.label}</div>
                {meta.guideCode && (
                  <div style={{ fontSize: 11, fontFamily: 'ui-monospace, Menlo, Consolas, monospace', color: 'rgba(255,255,255,0.5)', marginTop: 2 }}>
                    {meta.guideCode}
                  </div>
                )}
              </div>
              <button
                type="button"
                onClick={function () { setOpen(false) }}
                aria-label="Kapat"
                style={{ width: 30, height: 30, borderRadius: 8, cursor: 'pointer', background: 'rgba(255,255,255,0.04)', border: '1px solid rgba(255,255,255,0.08)', color: 'rgba(255,255,255,0.65)', display: 'inline-flex', alignItems: 'center', justifyContent: 'center' }}
              >
                <X size={14} />
              </button>
            </div>
            <div style={{ flex: 1, minHeight: 0, overflow: 'auto', padding: 14 }}>
              <GuideListField
                widgetId={widget.widgetId || widget.id}
                label={column.label}
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

export default function SmartTableRow(props) {
  var entity = props.entity || {}
  var columns = Array.isArray(props.columns) ? props.columns : []
  var onRefresh = typeof props.onRefresh === 'function' ? props.onRefresh : null
  var isHighlighted = !!props.isHighlighted
  var isDark = !!props.isDark

  var id = entity.id
  var rawWidgets = Array.isArray(entity.widgets) ? entity.widgets : []
  var recordValues = (entity.recordValues && typeof entity.recordValues === 'object') ? entity.recordValues : {}
  var primaryAction = entity.primaryAction || null
  var secondaryAction = entity.secondaryAction || null
  // Forward-looking, generic — bugun board config'lerinde gonderilmiyor ama
  // SmartCard'daki extraActions ile ayni sozlesme; "Islemler" menusu bunu
  // otomatik listeler (hardcode yok, bkz. dosya ustu aciklama).
  var extraActions = Array.isArray(entity.extraActions) ? entity.extraActions.filter(function (a) { return !!a }) : []
  var menuActions = (primaryAction ? [primaryAction] : []).concat(extraActions)

  var widgetById = useMemo(function () {
    var map = {}
    rawWidgets.forEach(function (w) { if (w && w.id) map[w.id] = w })
    return map
  }, [rawWidgets])

  var valueById = useMemo(function () {
    var map = {}
    rawWidgets.forEach(function (w) { if (w && w.id) map[w.id] = w.value })
    return map
  }, [rawWidgets])

  // colorType: 1=dinamik (baska widget'in degerini renk adi olarak kullan), 0=statik override.
  function resolveColorOverride(w) {
    if (!w) return null
    if (w.colorType === 1) return w.colorValue ? (valueById[w.colorValue] || null) : null
    if (w.colorType === 0) return w.colorValue || null
    return null
  }

  var [confirmOpen, setConfirmOpen] = useState(false)
  var [confirmMsg, setConfirmMsg] = useState('')
  var [alertOpen, setAlertOpen] = useState(false)
  var [alertMsg, setAlertMsg] = useState('')
  var [busy, setBusy] = useState(false)

  // ── "Islemler" dropdown menusu ──
  var [menuOpen, setMenuOpen] = useState(false)
  var [menuPos, setMenuPos] = useState(null)
  var menuBtnRef = useRef(null)
  var menuRef = useRef(null)

  useEffect(function () {
    if (!confirmOpen) return
    function onKey(e) { if (e.key === 'Escape') setConfirmOpen(false) }
    document.addEventListener('keydown', onKey)
    return function () { document.removeEventListener('keydown', onKey) }
  }, [confirmOpen])

  useEffect(function () {
    if (!alertOpen) return
    function onKey(e) { if (e.key === 'Escape' || e.key === 'Enter') setAlertOpen(false) }
    document.addEventListener('keydown', onKey)
    return function () { document.removeEventListener('keydown', onKey) }
  }, [alertOpen])

  useEffect(function () {
    if (!menuOpen) return undefined
    function onDocDown(e) {
      if (menuRef.current && menuRef.current.contains(e.target)) return
      if (menuBtnRef.current && menuBtnRef.current.contains(e.target)) return
      setMenuOpen(false)
    }
    function onKey(e) { if (e.key === 'Escape') setMenuOpen(false) }
    document.addEventListener('mousedown', onDocDown)
    document.addEventListener('keydown', onKey)
    return function () {
      document.removeEventListener('mousedown', onDocDown)
      document.removeEventListener('keydown', onKey)
    }
  }, [menuOpen])

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
    if (typeof action.url === 'string' && action.url.charAt(0) === '#') {
      try { window.location.hash = action.url } catch (e) { /* fallback */ }
      try { window.dispatchEvent(new HashChangeEvent('hashchange')) } catch (e) { /* ignore */ }
      return
    }
    navigateInWorkspace(action.url)
  }

  function handlePrimary(e) {
    if (e) e.stopPropagation()
    if (primaryAction) dispatchActionUrl(primaryAction)
  }

  // ── Sil (secondaryAction) akisi — DEGISMEDI, sadece tetik noktasi (artik
  // menu item'i) degisti. precheckUrl → confirm modal → apiUrl POST. ──
  function executeSecondary() {
    if (!secondaryAction) return
    if (secondaryAction.apiUrl) {
      var tokenEl = document.querySelector('input[name="__RequestVerificationToken"]')
      var token = tokenEl ? tokenEl.value : ''
      var method = (secondaryAction.apiMethod || 'POST').toUpperCase()
      var hasBody = secondaryAction.apiBody != null
      var headers = { 'Accept': 'application/json' }
      if (token) headers['RequestVerificationToken'] = token
      if (hasBody) headers['Content-Type'] = 'application/json'

      var fetchOpts = { method: method, credentials: 'same-origin', headers: headers }
      if (hasBody) fetchOpts.body = JSON.stringify(secondaryAction.apiBody)

      setBusy(true)
      fetch(secondaryAction.apiUrl, fetchOpts)
        .then(function (r) { return r.text().then(function (txt) { return { status: r.status, ok: r.ok, txt: txt } }) })
        .then(function (res) {
          var data = null
          if (res.txt) { try { data = JSON.parse(res.txt) } catch (_) { /* JSON degil */ } }
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
        .catch(function (err) {
          if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast('Hata: ' + err.message, 'err')
          else alert('Hata: ' + err.message)
        })
        .finally(function () { setBusy(false) })
    } else if (secondaryAction.url) {
      dispatchActionUrl(secondaryAction)
    }
  }

  function proceedSecondary() {
    if (secondaryAction.confirm) { setConfirmMsg(secondaryAction.confirm); setConfirmOpen(true) }
    else executeSecondary()
  }

  function handleSecondary(e) {
    if (e) e.stopPropagation()
    if (!secondaryAction) return
    if (!secondaryAction.precheckUrl) { proceedSecondary(); return }
    fetch(secondaryAction.precheckUrl, { credentials: 'same-origin', headers: { 'Accept': 'application/json' } })
      .then(function (r) { return r.text() })
      .then(function (txt) {
        var data = null
        if (txt) { try { data = JSON.parse(txt) } catch (_) { /* JSON degil */ } }
        if (data && data.ok === false) { setAlertMsg(data.reason || 'Bu belge silinemez.'); setAlertOpen(true); return }
        proceedSecondary()
      })
      .catch(function () { proceedSecondary() })
  }

  function handleConfirmYes() { setConfirmOpen(false); executeSecondary() }
  function handleConfirmNo() { setConfirmOpen(false) }

  // Menu'den Sil — mevcut handleSecondary akisini AYNEN tetikler, sadece
  // menuyu de kapatir.
  function handleMenuDelete(e) {
    if (busy) return
    setMenuOpen(false)
    handleSecondary(e)
  }

  // ── "Islemler" menusundeki jenerik aksiyon calistirici (Duzenle +
  // extraActions) — url ise dispatchActionUrl, apiUrl ise basit POST
  // (confirm/precheck destegi bugun sadece Sil/secondaryAction icin var —
  // menude ilerde confirm gereken baska bir aksiyon eklenirse buraya
  // tasinabilir). ──
  function runMenuApiAction(action) {
    var tokenEl = document.querySelector('input[name="__RequestVerificationToken"]')
    var token = tokenEl ? tokenEl.value : ''
    var method = (action.apiMethod || 'POST').toUpperCase()
    var hasBody = action.apiBody != null
    var headers = { 'Accept': 'application/json' }
    if (token) headers['RequestVerificationToken'] = token
    if (hasBody) headers['Content-Type'] = 'application/json'

    var fetchOpts = { method: method, credentials: 'same-origin', headers: headers }
    if (hasBody) fetchOpts.body = JSON.stringify(action.apiBody)

    setBusy(true)
    fetch(action.apiUrl, fetchOpts)
      .then(function (r) { return r.text().then(function (txt) { return { status: r.status, ok: r.ok, txt: txt } }) })
      .then(function (res) {
        var data = null
        if (res.txt) { try { data = JSON.parse(res.txt) } catch (_) { /* JSON degil */ } }
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
      .catch(function (err) {
        if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast('Hata: ' + err.message, 'err')
        else alert('Hata: ' + err.message)
      })
      .finally(function () { setBusy(false) })
  }

  function dispatchMenuAction(action) {
    if (!action || busy) return
    if (action.apiUrl) { runMenuApiAction(action); return }
    dispatchActionUrl(action)
  }

  function toggleMenu(e) {
    if (e) e.stopPropagation()
    if (busy) return
    if (menuOpen) { setMenuOpen(false); return }
    var el = menuBtnRef.current
    if (el) {
      var rect = el.getBoundingClientRect()
      setMenuPos({ top: rect.bottom + 6, right: Math.max(8, window.innerWidth - rect.right) })
    }
    setMenuOpen(true)
  }

  var clickableRow = !!primaryAction

  return (
    <>
      <tr
        className={'cst-row' + (isHighlighted ? ' cst-row--highlight' : '') + (clickableRow ? ' cst-row--clickable' : '')}
        onClick={clickableRow ? handlePrimary : undefined}
        title={primaryAction && primaryAction.label ? primaryAction.label : undefined}
      >
        <td className="cst-td cst-td--menu">
          <div className="flex items-center justify-center">
            <button
              ref={menuBtnRef}
              type="button"
              onClick={toggleMenu}
              disabled={busy}
              className={'p-1.5 rounded-lg transition-colors group ' +
                (busy ? 'opacity-50 cursor-not-allowed'
                  : menuOpen ? 'bg-indigo-100 dark:bg-indigo-500/15' : 'hover:bg-slate-100 dark:hover:bg-white/5')
              }
              title="İşlemler"
              aria-label="İşlemler"
            >
              <MoreVertical
                size={15}
                className={menuOpen
                  ? 'text-indigo-600 dark:text-indigo-400'
                  : 'text-slate-400 dark:text-white/40 group-hover:text-slate-600 dark:group-hover:text-white/60 transition-colors'}
              />
            </button>
          </div>
        </td>

        {columns.map(function (col) {
          var w = widgetById[col.id]
          return (
            <TableValueCell
              key={col.id}
              column={col}
              widget={w}
              colorOverride={resolveColorOverride(w)}
              recordValues={recordValues}
              isDark={isDark}
            />
          )
        })}
      </tr>

      {/* "Islemler" dropdown — cross-document portal oldugu icin (getTopBody
          iframe→top pencereye tasabilir, ayri stylesheet) INLINE stil, ama
          isDark'a gore tema-farkinda. Duzenle + extraActions + (varsa) ayrac
          + Sil (danger). */}
      {menuOpen && menuPos && createPortal(
        <div
          ref={menuRef}
          onClick={function (e) { e.stopPropagation() }}
          style={{
            position: 'fixed', top: menuPos.top, right: menuPos.right, zIndex: 10010,
            minWidth: 190, maxWidth: 260, padding: 6, borderRadius: 12,
            background: isDark ? '#1e293b' : '#ffffff',
            border: isDark ? '1px solid rgba(255,255,255,0.12)' : '1px solid #e2e8f0',
            boxShadow: isDark ? '0 12px 32px rgba(0,0,0,0.5)' : '0 12px 32px rgba(15,23,42,0.18)',
          }}
        >
          {menuActions.length === 0 && !secondaryAction ? (
            <div style={{ padding: '10px 12px', fontSize: 12, color: isDark ? 'rgba(255,255,255,0.35)' : '#94a3b8' }}>
              Aksiyon yok
            </div>
          ) : (
            <>
              {menuActions.map(function (action, i) {
                var ActionIcon = resolveIcon(action.icon)
                return (
                  <button
                    key={action.id || action.label || i}
                    type="button"
                    onClick={function (e) { e.stopPropagation(); setMenuOpen(false); dispatchMenuAction(action) }}
                    style={{
                      display: 'flex', alignItems: 'center', gap: 8, width: '100%',
                      padding: '8px 10px', borderRadius: 8, border: 'none', background: 'transparent',
                      cursor: 'pointer', fontSize: 12.5, fontWeight: 600, textAlign: 'left',
                      color: isDark ? 'rgba(255,255,255,0.82)' : '#334155',
                    }}
                    onMouseEnter={function (e) { e.currentTarget.style.background = isDark ? 'rgba(255,255,255,0.06)' : '#f1f5f9' }}
                    onMouseLeave={function (e) { e.currentTarget.style.background = 'transparent' }}
                  >
                    <ActionIcon size={14} />
                    {action.label}
                  </button>
                )
              })}

              {secondaryAction && (
                <>
                  <div style={{ height: 1, margin: '4px 6px', background: isDark ? 'rgba(255,255,255,0.08)' : '#e2e8f0' }} />
                  <button
                    type="button"
                    onClick={handleMenuDelete}
                    disabled={busy}
                    style={{
                      display: 'flex', alignItems: 'center', gap: 8, width: '100%',
                      padding: '8px 10px', borderRadius: 8, border: 'none', background: 'transparent',
                      cursor: busy ? 'not-allowed' : 'pointer', fontSize: 12.5, fontWeight: 600, textAlign: 'left',
                      color: '#ef4444', opacity: busy ? 0.5 : 1,
                    }}
                    onMouseEnter={function (e) { if (!busy) e.currentTarget.style.background = isDark ? 'rgba(239,68,68,0.14)' : 'rgba(239,68,68,0.08)' }}
                    onMouseLeave={function (e) { e.currentTarget.style.background = 'transparent' }}
                  >
                    <Trash2 size={14} />
                    {secondaryAction.label || 'Sil'}
                  </button>
                </>
              )}
            </>
          )}
        </div>,
        getTopBody()
      )}

      {confirmOpen && createPortal(
        <div
          style={{ position: 'fixed', inset: 0, zIndex: 10000, background: 'rgba(0,0,0,0.6)', backdropFilter: 'blur(4px)', display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 20 }}
          onClick={handleConfirmNo}
        >
          <div
            style={{ background: '#1e293b', border: '1px solid rgba(255,255,255,0.12)', borderRadius: 16, padding: '32px 28px', maxWidth: 380, width: '90vw', boxShadow: '0 24px 64px rgba(0,0,0,0.5)', display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 12, textAlign: 'center' }}
            onClick={function (e) { e.stopPropagation() }}
          >
            <Trash2 size={26} style={{ color: '#ef4444' }} />
            <h3 style={{ fontSize: '1.05rem', fontWeight: 700, color: '#f1f5f9', margin: 0 }}>Emin misiniz?</h3>
            <p style={{ fontSize: '.84rem', color: '#94a3b8', margin: 0 }}>{confirmMsg}</p>
            <div style={{ display: 'flex', gap: 10, marginTop: 8 }}>
              <button type="button" onClick={handleConfirmNo}
                style={{ padding: '8px 16px', borderRadius: 8, fontSize: '.84rem', fontWeight: 600, background: 'rgba(255,255,255,.07)', color: '#f1f5f9', border: '1px solid rgba(255,255,255,.1)', cursor: 'pointer' }}>
                İptal
              </button>
              <button type="button" onClick={handleConfirmYes} autoFocus
                style={{ padding: '8px 16px', borderRadius: 8, fontSize: '.84rem', fontWeight: 600, background: 'linear-gradient(135deg,#ef4444,#dc2626)', color: '#fff', border: 'none', cursor: 'pointer', display: 'inline-flex', alignItems: 'center', gap: 6 }}>
                <Trash2 size={13} /> Evet, Sil
              </button>
            </div>
          </div>
        </div>,
        getTopBody()
      )}

      {alertOpen && createPortal(
        <div
          style={{ position: 'fixed', inset: 0, zIndex: 10000, background: 'rgba(0,0,0,0.6)', backdropFilter: 'blur(4px)', display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 20 }}
          onClick={function () { setAlertOpen(false) }}
        >
          <div
            style={{ background: '#1e293b', border: '1px solid rgba(255,255,255,0.12)', borderRadius: 16, padding: '32px 28px', maxWidth: 400, width: '90vw', boxShadow: '0 24px 64px rgba(0,0,0,0.5)', display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 12, textAlign: 'center' }}
            onClick={function (e) { e.stopPropagation() }}
          >
            <AlertTriangle size={26} style={{ color: '#f59e0b' }} />
            <h3 style={{ fontSize: '1.05rem', fontWeight: 700, color: '#f1f5f9', margin: 0 }}>İşlem Yapılamadı</h3>
            <p style={{ fontSize: '.84rem', color: '#94a3b8', margin: 0, lineHeight: 1.5 }}>{alertMsg}</p>
            <button type="button" onClick={function () { setAlertOpen(false) }} autoFocus
              style={{ padding: '8px 22px', borderRadius: 8, fontSize: '.84rem', fontWeight: 600, marginTop: 8, background: 'linear-gradient(135deg,#6366f1,#4f46e5)', color: '#fff', border: 'none', cursor: 'pointer' }}>
              Tamam
            </button>
          </div>
        </div>,
        getTopBody()
      )}
    </>
  )
}
