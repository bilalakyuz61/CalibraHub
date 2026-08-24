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
import { useState, useMemo, useEffect, useLayoutEffect, useRef } from 'react'
import { createPortal } from 'react-dom'
import { AlertTriangle, Trash2, X, ArrowUpRight, List, MoreVertical, ChevronRight, ChevronDown } from 'lucide-react'
import { resolveIcon, resolveColorForTheme, formatValue, resolveBooleanIcon } from './DynamicWidgetFactory'
import { checkConstraintViolation, resolveTokensWithRecord } from './SmartWidget'
import GuideListField from '../DynamicWidgetRenderer/GuideListField'
import { navigateInWorkspace, deriveMatchPathFromUrl } from '../../utils/workspaceNav'
import { getTopBody, getTopFrameOffset, getTopViewportSize, useClosePortalOnFrameHidden } from '../../utils/topPortal'

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

/* ── "Islemler" kebab dropdown — konumlandirma + acilis animasyonu ──────
   Modul seviyesinde saf fonksiyonlar (bilesen closure'una ihtiyac yok).
   Bkz. SmartTableRow bileseni icindeki useLayoutEffect: mount→olc→yerlestir→
   rAF ile reveal deseni kullanir (once gizli/gercek-boyutlu render, sonra
   flip/clamp uygulanmis nihai konum, en son fade+scale gecisi tetiklenir). */
var PREFERS_REDUCED_MOTION = (function () {
  try { return !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches) } catch (e) { return false }
})()

/**
 * Buton + olculmus menu dikdortgenlerinden flip/clamp uygulanmis nihai
 * konumu hesaplar. Varsayilan: butonun altinda, sag kenari butonun sag
 * kenariyla hizali. Viewport altta yer yoksa yukari acilir (ters); solda
 * tasma olacaksa sol kenara sabitlenir — hicbir zaman viewport disina
 * tasmaz ("satirdan/viewport'tan taşmasın" kurali).
 *
 * btnRect/menuRect ve vw/vh AYNI koordinat uzayinda olmalidir — cagiran
 * (asagidaki useLayoutEffect) btnRect'i getTopFrameOffset() ile window.top
 * uzayina cevirir ve vw/vh'i getTopViewportSize()'dan verir, cunku menu
 * getTopBody() araciligiyla window.top govdesine portallanir (bkz. 2026-07-21,
 * PageComment Seq 19 — iframe offset duzeltmesi, dosya ustu topPortal.js kdoc'u).
 */
function computeMenuPlacement(btnRect, menuRect, vw, vh) {
  var margin = 8
  var gap = 6

  // 2026-07-19 — VARSAYILAN: butonun SAĞ-ALT köşesinden açılır (menünün sol kenarı butonun
  // sağ kenarına yaslanır, sağa+aşağı doğru büyür). Önceki sürüm menüyü butonun SAĞ kenarına
  // hizalayıp SOLA doğru açıyordu; "İşlemler" sütunu tablonun en solunda olduğu için menü
  // ekranın soluna kaçıp butonla ilgisiz görünüyordu (kullanıcı bildirimi).
  // 4px üst üste binme kasıtlı: menünün hangi butona ait olduğu görsel olarak bağlansın.
  var anchorLeft = btnRect.right - 4
  var horizontal = 'left'
  var rightVal = Math.max(margin, vw - btnRect.right)
  // Sağa sığmıyorsa eski davranışa dön (butonun sağ kenarına hizala, sola doğru aç).
  if (anchorLeft + menuRect.width > vw - margin) horizontal = 'right'

  var vertical = 'below'
  var fitsBelow = (btnRect.bottom + gap + menuRect.height) <= (vh - margin)
  var fitsAbove = (btnRect.top - gap - menuRect.height) >= margin
  if (!fitsBelow && fitsAbove) vertical = 'above'

  var pos = { vertical: vertical, horizontal: horizontal }
  if (vertical === 'below') pos.top = btnRect.bottom + gap
  else pos.bottom = Math.max(margin, vh - btnRect.top + gap)

  if (horizontal === 'right') pos.right = rightVal
  // anchorLeft = butonun sağ kenarı (4px bindirmeli). Taşmaya karşı yine de kırpılır.
  else pos.left = Math.max(margin, Math.min(anchorLeft, vw - margin - menuRect.width))

  return pos
}

/**
 * menuPos null iken (henuz olculmedi) tamamen gorunmez ama gercek boyutlu bir
 * "olcum gecisi" doner (visibility:hidden — display:none'in aksine
 * getBoundingClientRect gercek boyutu verir). menuPos hazir olunca floating
 * panel stili + fade/scale gecis durumunu (menuVisible) uygular.
 */
function buildMenuFloatingStyle(menuPos, menuVisible, isDark) {
  if (!menuPos) return { position: 'fixed', top: 0, left: 0, visibility: 'hidden', pointerEvents: 'none' }

  var originY = menuPos.vertical === 'above' ? 'bottom' : 'top'
  var originX = menuPos.horizontal === 'left' ? 'left' : 'right'
  var slideY = menuPos.vertical === 'above' ? 6 : -6

  var style = {
    position: 'fixed',
    zIndex: 10010,
    minWidth: 190,
    maxWidth: 260,
    padding: 5,
    borderRadius: 12,
    background: 'var(--app-surface)',
    border: '1px solid var(--app-border)',
    boxShadow: isDark
      ? '0 4px 10px rgba(0,0,0,0.35), 0 20px 44px -10px rgba(0,0,0,0.65)'
      : '0 4px 10px rgba(15,23,42,0.06), 0 16px 36px -8px rgba(15,23,42,0.22)',
    backdropFilter: 'blur(18px)',
    WebkitBackdropFilter: 'blur(18px)',
    transformOrigin: originY + ' ' + originX,
    opacity: menuVisible ? 1 : 0,
    transform: menuVisible ? 'scale(1) translateY(0)' : ('scale(0.95) translateY(' + slideY + 'px)'),
    transition: PREFERS_REDUCED_MOTION ? 'none' : 'opacity 140ms ease-out, transform 140ms ease-out',
    pointerEvents: menuVisible ? 'auto' : 'none',
  }
  if (menuPos.top != null) style.top = menuPos.top
  else style.bottom = menuPos.bottom
  if (menuPos.left != null) style.left = menuPos.left
  else style.right = menuPos.right
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
            style={{ width: '100%', maxWidth: 1080, maxHeight: '85vh', background: 'var(--app-surface)', border: '1px solid var(--app-border)', borderRadius: 14, boxShadow: '0 16px 48px rgba(0,0,0,0.55)', display: 'flex', flexDirection: 'column', overflow: 'hidden' }}
          >
            <div style={{ display: 'flex', alignItems: 'center', gap: 10, padding: '14px 18px', borderBottom: '1px solid var(--app-border)' }}>
              <div style={{ width: 30, height: 30, borderRadius: 8, display: 'flex', alignItems: 'center', justifyContent: 'center', background: palette.bg, border: '1px solid ' + palette.border }}>
                <Icon size={15} style={{ color: palette.icon }} strokeWidth={1.8} />
              </div>
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{ fontSize: 14, fontWeight: 700, color: 'var(--app-text)' }}>{column.label}</div>
                {meta.guideCode && (
                  <div style={{ fontSize: 11, fontFamily: 'ui-monospace, Menlo, Consolas, monospace', color: 'var(--app-text-muted)', marginTop: 2 }}>
                    {meta.guideCode}
                  </div>
                )}
              </div>
              <button
                type="button"
                onClick={function () { setOpen(false) }}
                aria-label="Kapat"
                style={{ width: 30, height: 30, borderRadius: 8, cursor: 'pointer', background: 'var(--app-muted-surface)', border: '1px solid var(--app-border)', color: 'var(--app-text-muted)', display: 'inline-flex', alignItems: 'center', justifyContent: 'center' }}
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
  // Satir secimi + acilir detay (opsiyonel — board config'te acilmadikca
  // hicbiri render edilmez, mevcut board'lar icin davranis birebir korunur).
  var selectable = !!props.selectable
  var selected = !!props.selected
  var onToggleSelect = typeof props.onToggleSelect === 'function' ? props.onToggleSelect : null
  var selectDisabledReason = props.selectDisabledReason || null
  var expandable = !!props.expandable
  var expanded = !!props.expanded
  var onToggleExpand = typeof props.onToggleExpand === 'function' ? props.onToggleExpand : null
  var detailNode = props.detailNode
  var colSpanTotal = typeof props.colSpanTotal === 'number' ? props.colSpanTotal : (columns.length + 1)

  var id = entity.id
  var rawWidgets = Array.isArray(entity.widgets) ? entity.widgets : []
  var recordValues = (entity.recordValues && typeof entity.recordValues === 'object') ? entity.recordValues : {}
  var primaryAction = entity.primaryAction || null
  var secondaryAction = entity.secondaryAction || null
  // Forward-looking, generic — bugun board config'lerinde gonderilmiyor ama
  // SmartCard'daki extraActions ile ayni sozlesme; "Islemler" menusu bunu
  // otomatik listeler (hardcode yok, bkz. dosya ustu aciklama).
  // extraActions sozlesmesi `tooltip` tasir, `label` TASIMAZ (SmartBoardBuilder.AddExtraAction).
  // Kart gorunumunde bu yeterli (aksiyonlar ikon butonu), ama "Islemler" MENUSU metni
  // `label` alanindan okur — normalize edilmezse menu ogeleri ETIKETSIZ cikar (yalniz ikon)
  // ve asagidaki tekrar-eleme de label karsilastirdigi icin calismaz: ayni "Duzenle"
  // hem primaryAction hem extraAction olarak iki kez listelenirdi (2026-08-24, Olcu Birimleri).
  var extraActions = (Array.isArray(entity.extraActions) ? entity.extraActions.filter(function (a) { return !!a }) : [])
    .map(function (a) {
      return (a.label && a.label.length) ? a : Object.assign({}, a, { label: a.tooltip || '' })
    })
  // Menude AYNI aksiyon iki kez gorunmesin. Bazi board config'leri "Duzenle"yi hem
  // primaryAction (satir tiklamasi + hideButton) hem de extraActions icinde tanimliyor —
  // kart gorunumunde ikisi de gerekli (kartta primaryAction butonu gizli), ama tablo
  // menusu ikisini birden listeleyince ayni ogeden iki tane cikiyordu
  // (PageComment Seq 1109). Ayni etiket + ayni hedef ise ikincisi elenir; etiketi ayni
  // ama hedefi farkli olan aksiyonlar KORUNUR.
  var menuActions = (primaryAction ? [primaryAction] : []).concat(extraActions)
    .filter(function (a, i, arr) {
      return arr.findIndex(function (b) {
        return (b.label || '') === (a.label || '')
          && (b.url || '') === (a.url || '')
          && (b.apiUrl || '') === (a.apiUrl || '')
          && (b.fetchUrl || '') === (a.fetchUrl || '')
      }) === i
    })

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

  // ── fetch-modal state ── Sunucudan HTML partial cekip modalda gosteren aksiyon
  // tipi (ornegin Zamanlanmis Gorevler > "Gecmis"). Bu tip SADECE SmartCard'da
  // uygulanmisti; tablo gorunumunde aksiyon dispatchActionUrl'e dusuyor ve `url`
  // olmadigi icin SESSIZCE hicbir sey olmuyordu — kullanici "ekran acilmiyor"
  // olarak bildirdi (PageComment Seq 1103).
  var [modalOpen,    setModalOpen]    = useState(false)
  var [modalHtml,    setModalHtml]    = useState('')
  var [modalTitle,   setModalTitle]   = useState('')
  var [modalLoading, setModalLoading] = useState(false)
  var modalContentRef = useRef(null)
  var modalPortalRef  = useRef(null)

  var [confirmOpen, setConfirmOpen] = useState(false)
  var [confirmMsg, setConfirmMsg] = useState('')
  // { okLabel, variant } — silme disi onaylarda "Evet, Sil" yazmasin (SmartCard paritesi).
  var [confirmOpts, setConfirmOpts] = useState(null)
  var [alertOpen, setAlertOpen] = useState(false)
  var [alertMsg, setAlertMsg] = useState('')
  var [busy, setBusy] = useState(false)

  // ── "Islemler" dropdown menusu ──
  // menuPos: null iken portal "olcum gecisi" (visibility:hidden) render eder;
  // asagidaki useLayoutEffect gercek menu boyutunu olcup flip/clamp uygulanmis
  // nihai konumu yazar, ardindan menuVisible bir rAF sonra true olup fade+scale
  // gecisini tetikler (mount→olc→yerlestir→reveal deseni, bkz. dosya ustu
  // computeMenuPlacement/buildMenuFloatingStyle).
  var [menuOpen, setMenuOpen] = useState(false)
  var [menuPos, setMenuPos] = useState(null)
  var [menuVisible, setMenuVisible] = useState(false)
  var menuBtnRef = useRef(null)
  var menuRef = useRef(null)
  // "status-menu" tipi aksiyonlar (ör. Durum Değiştir) İşlemler menüsü İÇİNDE
  // NESTED bir seçenek listesi açar — ayrı sekmeye gitmez (PageComment Seq 1071,
  // 2026-08-03). Hangi status-menu ögesinin açık olduğunu tutar (action anahtarı);
  // menü kapanınca (toggleMenu/dış tık/Escape) sıfırlanır.
  var [statusSubOpenKey, setStatusSubOpenKey] = useState(null)
  // confirm/alert portal'larinin ust-duzey DOM node'lari — sadece
  // useClosePortalOnFrameHidden'in hard-navigasyon (pagehide) senaryosunda
  // dogrudan DOM'dan sokmesi icin (bkz. asagidaki hook cagrilari + dosya
  // ustu portal aciklamasi).
  var confirmPortalRef = useRef(null)
  var alertPortalRef = useRef(null)
  // Onay modali artik yalnizca "Sil"e degil, confirm tanimli HERHANGI bir menu
  // aksiyonuna hizmet ediyor — "Evet" tusuna basilinca calisacak isi burada tutar.
  var confirmCallbackRef = useRef(null)

  // Cekilen HTML'i DOM'a bas ve icindeki <script>'leri calistirilabilir hale getir
  // (SmartCard ile ayni desen). innerHTML ile eklenen script tarayicida calismaz.
  useEffect(function () {
    if (!modalOpen || modalLoading) return
    var node = modalContentRef.current
    if (!node) return
    var hash = String(modalHtml.length + ':' + modalHtml.slice(0, 32))
    if (node.getAttribute('data-sm-modal-html-hash') === hash) return
    node.innerHTML = modalHtml
    node.setAttribute('data-sm-modal-html-hash', hash)
    node.querySelectorAll('script').forEach(function (oldScript) {
      var newScript = document.createElement('script')
      for (var i = 0; i < oldScript.attributes.length; i++) {
        var attr = oldScript.attributes[i]
        newScript.setAttribute(attr.name, attr.value)
      }
      newScript.textContent = oldScript.textContent
      oldScript.parentNode.replaceChild(newScript, oldScript)
    })
  }, [modalOpen, modalLoading, modalHtml])

  useEffect(function () {
    if (!modalOpen) return
    function onKey(e) { if (e.key === 'Escape') setModalOpen(false) }
    document.addEventListener('keydown', onKey)
    return function () { document.removeEventListener('keydown', onKey) }
  }, [modalOpen])

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

  // ── window.top govdesine portallanmis "Islemler" menusu + onay/uyari
  //    modallerinin iki "sessiz yetim kalma" senaryosuna karsi kendini
  //    kapatmasi — bkz. utils/topPortal.js useClosePortalOnFrameHidden kdoc'u
  //    (2026-07-23, PageComment Seq 25): (1) iframe icinde baska sayfaya
  //    hard-navigasyon, (2) Shell sekme/sol menu ile baska tab'a gecis
  //    (iframe display:none olur, portal top dokumaninda gorunur kalirdi). ──
  useClosePortalOnFrameHidden(menuOpen, function () { setMenuOpen(false) }, menuRef)
  useClosePortalOnFrameHidden(modalOpen, function () { setModalOpen(false) }, modalPortalRef)
  useClosePortalOnFrameHidden(confirmOpen, handleConfirmNo, confirmPortalRef)
  useClosePortalOnFrameHidden(alertOpen, function () { setAlertOpen(false) }, alertPortalRef)

  // İşlemler menüsü her kapandığında (dış tık, Escape, aksiyon seçimi, tab
  // gizlenmesi) status-menu alt-listesi de sıfırlanır — bir dahaki açılışta
  // yanlışlıkla açık kalmasın.
  useEffect(function () {
    if (!menuOpen) setStatusSubOpenKey(null)
  }, [menuOpen])

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

  // Mount→olc→yerlestir→reveal: menu acildiginda once gizli (ama gercek
  // boyutlu) render edilir, burada gercek genislik/yukseklik butonun
  // dikdortgeniyle birlikte olculup flip/clamp uygulanmis nihai konum
  // yazilir (senkron, boyanmadan once — useLayoutEffect). Ardindan bir sonraki
  // animasyon karesinde menuVisible true olur ve CSS transition (opacity+
  // transform) devreye girer — boylece "ANINDA sert belirme" yerine kisa bir
  // fade+scale gecisi olusur, kullanici hicbir zaman yanlis/olculmemis konumu
  // gormez (visibility:hidden asamasi boyanmaz).
  useLayoutEffect(function () {
    if (!menuOpen) return undefined
    var btn = menuBtnRef.current
    var menuEl = menuRef.current
    if (!btn || !menuEl) return undefined
    // btn, bu bilesenin CALISTIGI dokumanda (workspace tab iframe'i) olculur —
    // getBoundingClientRect iframe-local koordinat doner. menuEl ise getTopBody()
    // ile window.top govdesine PORTALLANMIS bir node — getBoundingClientRect'i
    // zaten window.top viewport'una gore doner (kendi dokumaninin metodu).
    // Ikisini AYNI uzayda kiyaslayabilmek icin btn dikdortgeni, iframe'in top
    // penceredeki offset'i (Sidebar genisligi + header/tab-bar yuksekligi) kadar
    // kaydirilir — aksi halde menu, butonun gercek ekran konumundan o offset
    // kadar sola/yukari kaymis acilirdi (2026-07-21, PageComment Seq 19).
    var rawBtnRect = btn.getBoundingClientRect()
    var frameOffset = getTopFrameOffset()
    var btnRect = {
      top: rawBtnRect.top + frameOffset.y,
      bottom: rawBtnRect.bottom + frameOffset.y,
      left: rawBtnRect.left + frameOffset.x,
      right: rawBtnRect.right + frameOffset.x,
    }
    var menuRect = menuEl.getBoundingClientRect()
    var viewport = getTopViewportSize()
    setMenuPos(computeMenuPlacement(btnRect, menuRect, viewport.width, viewport.height))
    var raf = requestAnimationFrame(function () { setMenuVisible(true) })
    return function () { cancelAnimationFrame(raf) }
  }, [menuOpen])

  function dispatchActionUrl(action) {
    if (!action || !action.url) return
    if (action.openInTab) {
      try {
        if (window.top && window.top.CalibraHub && typeof window.top.CalibraHub.openWorkspaceTab === 'function') {
          // matchPath GENEL kurali (2026-07-21) — bkz. SmartCard.jsx dispatchActionUrl
          // (birebir ayni mantik): alan acikca verilmemisse (undefined) URL path'inden
          // turetilir; acikca null/false verilmis istisnalar KIRILMAZ.
          //
          // asChild ISTISNASI (Bulgu 1, 2026-08-03 adversarial review) — bkz. SmartCard.jsx
          // dispatchActionUrl (birebir ayni mantik): backend'in matchPath'i JSON'da
          // gonderip gondermedigine guvenilmez (WhenWritingNull null'i dusurebiliyor),
          // bu yuzden asChild=true iken matchPath JS tarafinda HER ZAMAN null'a
          // kelepcelenir — sadece exact-URL eslesmesi gecerli olur.
          var isAsChild = !!action.openInTab.asChild
          var matchPath
          if (isAsChild) {
            matchPath = null
          } else {
            var explicitMatchPath = action.openInTab.matchPath
            matchPath = (explicitMatchPath !== undefined)
              ? (explicitMatchPath || null)
              : deriveMatchPathFromUrl(action.url)
          }
          window.top.CalibraHub.openWorkspaceTab({
            url: action.url,
            title: action.openInTab.title || action.label || 'Yeni Sekme',
            matchPath: matchPath,
            // Nested (child) tab destegi (PageComment Seq 1063, 2026-08-03) — bkz.
            // SmartCard.jsx dispatchActionUrl (birebir ayni mantik).
            asChild: isAsChild,
            parentKey: action.openInTab.parentKey || null,
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
    if (secondaryAction.confirm) { askConfirm(secondaryAction.confirm, executeSecondary) }
    else executeSecondary()
  }

  // Ortak onay kapisi: mesaji goster, kullanici onaylarsa callback'i calistir.
  function askConfirm(message, callback, opts) {
    confirmCallbackRef.current = callback
    setConfirmMsg(message)
    setConfirmOpts(opts || null)
    setConfirmOpen(true)
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

  function handleConfirmYes() {
    setConfirmOpen(false)
    var cb = confirmCallbackRef.current
    confirmCallbackRef.current = null
    if (cb) cb(); else executeSecondary()
  }
  function handleConfirmNo() { setConfirmOpen(false); confirmCallbackRef.current = null }

  var confirmIsPrimary = !!(confirmOpts && confirmOpts.variant === 'primary')
  var confirmOkLabel   = (confirmOpts && confirmOpts.okLabel) || (confirmIsPrimary ? 'Evet, Devam' : 'Evet, Sil')

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
        // Sunucu bir sonuc URL'i dondurduyse (orn. belge kopyalama → yeni belgenin
        // duzenleme ekrani) o URL AYRI bir workspace sekmesinde acilir — liste
        // sekmesi yerinde kalir, board asagida normal sekilde yenilenir. matchPath
        // BILINCLI olarak null: her aksiyon kendi hedefine (ornegin yeni kopyalanan
        // belge Id'sine) gitmeli, ayni path'teki BASKA bir acik sekmeyi (ilgisiz bir
        // belge duzenleme sekmesi) HIJACK ETMEMELI (2026-07-21, PageComment Seq 19).
        if (data && data.redirectUrl) {
          dispatchActionUrl({ url: data.redirectUrl, openInTab: { title: data.redirectTitle || action.label || 'Yeni Sekme', matchPath: null } })
        }
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
    // Config'de `confirm` tanimliysa ONAY ZORUNLU. Onceden bu destek yalnizca
    // Sil/secondaryAction akisinda vardi; menuden calisan extraActions'ta confirm
    // SESSIZCE yok sayiliyordu — Zamanlanmis Gorevler'de "Sil" (extraAction +
    // confirm) tablo gorunumunde hicbir sey sormadan siliyordu (PageComment Seq 1101).
    if (action.confirm) {
      askConfirm(action.confirm, function () {
        dispatchMenuAction(Object.assign({}, action, { confirm: null }))
      }, { okLabel: action.confirmOkLabel, variant: action.confirmVariant })
      return
    }
    // type:'event' — POST YOK. Bu satirin id'si DOM olayi ile host sayfaya
    // verilir; ek girdi toplayan akislar (miktar/depo/tarih soran modal) boyle
    // baglanir. SmartBoard.jsx executeBulkAction'daki type:'event' toplu-aksiyon
    // sozlesmesiyle AYNI desen (bkz. o dosyadaki 2026-08 kdoc'u) — burada tek
    // farkla, secili id listesi yerine bu SATIRIN id'si gonderilir. Host sayfa
    // isi bitince window.CalibraSmartBoard.refresh(boardKey) ile board'u tazeler.
    if (action.type === 'event') {
      try {
        window.dispatchEvent(new CustomEvent(action.event || 'smartboard:row', {
          detail: { actionId: action.id || null, ids: [id], id: id },
        }))
      } catch (e) { /* CustomEvent desteklenmiyorsa sessiz gec */ }
      setMenuOpen(false)
      return
    }
    // fetch-modal: sunucudan HTML partial cek, modalda goster (SmartCard paritesi).
    if (action.type === 'fetch-modal') {
      var fetchUrl = (action.fetchUrl || '').replace('{id}', id)
      if (!fetchUrl) {
        if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast('Bu islem icin adres tanimli degil.', 'err')
        return
      }
      setModalTitle(action.modalTitle || action.label || '')
      setModalHtml('')
      setModalLoading(true)
      setModalOpen(true)
      fetch(fetchUrl, { credentials: 'same-origin' })
        .then(function (r) {
          if (!r.ok) throw new Error('HTTP ' + r.status)
          return r.text()
        })
        .then(function (html) { setModalHtml(html); setModalLoading(false) })
        .catch(function (err) {
          setModalHtml('<div style="padding:24px;text-align:center;color:#dc2626;font-size:13px;">Yüklenemedi: ' + err.message + '</div>')
          setModalLoading(false)
        })
      return
    }
    if (action.type === 'download' && action.url) {
      var dlUrl = String(action.url).replace('{id}', id)
      var a = document.createElement('a'); a.href = dlUrl; a.download = ''
      document.body.appendChild(a); a.click(); document.body.removeChild(a)
      return
    }
    if (action.apiUrl) { runMenuApiAction(action); return }
    // SmartCard sözleşmesi: extraActions'ta POST `type:'api-post'` + `url` ile de
    // verilebiliyor. Burada tanınmazsa aksiyon SESSİZCE navigasyona düşüyor ve
    // kullanıcı API adresine gidip hata sayfası görüyor (DbImport "Pasifleştir"
    // bu şekilde kırıldı). İki bileşen aynı config'i kabul etsin.
    if (action.type === 'api-post' && action.url) {
      runMenuApiAction(Object.assign({}, action, { apiUrl: action.url }))
      return
    }
    dispatchActionUrl(action)
  }

  // "status-menu" tipi aksiyonun (Durum Değiştir) nested seçeneğine tıklanınca
  // — runMenuApiAction'ın AYNI POST/toast/onRefresh akışını kullanır, tek fark
  // body'nin { id, status } ile burada kurulması. Menü + alt-liste kapatılır.
  function runStatusChange(action, value) {
    if (busy) return
    setMenuOpen(false)
    setStatusSubOpenKey(null)
    runMenuApiAction(Object.assign({}, action, { apiBody: { id: id, status: value } }))
  }

  function toggleMenu(e) {
    if (e) e.stopPropagation()
    if (busy) return
    if (menuOpen) { setMenuOpen(false); return }
    // Nihai konum useLayoutEffect'te gercek menu boyutu olculerek hesaplanir
    // (flip/clamp dahil) — burada sadece "olcum gecisi"ni baslatiyoruz, bkz.
    // buildMenuFloatingStyle/computeMenuPlacement (dosya ustu).
    setMenuPos(null)
    setMenuVisible(false)
    setMenuOpen(true)
  }

  var clickableRow = !!primaryAction

  return (
    <>
      <tr
        className={'cst-row' + (isHighlighted ? ' cst-row--highlight' : '') + (clickableRow ? ' cst-row--clickable' : '')
          + (selected ? ' cst-row--selected' : '') + (expanded ? ' cst-row--expanded' : '')}
        onClick={clickableRow ? handlePrimary : undefined}
        title={primaryAction && primaryAction.label ? primaryAction.label : undefined}
      >
        {selectable && (
          // Onay kutusu satir tiklamasini (Duzenle) TETIKLEMEMELI — stopPropagation sart.
          <td className="cst-td cst-td--pick" onClick={function (e) { e.stopPropagation() }}>
            <input
              type="checkbox"
              className="cst-pick"
              checked={selected}
              title={selectDisabledReason || undefined}
              onChange={function (e) { if (onToggleSelect) onToggleSelect(id, e.target.checked) }}
              aria-label="Satırı seç"
            />
          </td>
        )}
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

        {expandable && (
          <td className="cst-td cst-td--exp" onClick={function (e) { e.stopPropagation() }}>
            <button
              type="button"
              className={'cst-exp-btn' + (expanded ? ' is-open' : '')}
              onClick={function () { if (onToggleExpand) onToggleExpand(id) }}
              title={expanded ? 'Detayı kapat' : 'Detayı aç'}
              aria-expanded={expanded}
            >
              <ChevronDown size={13} strokeWidth={2.5} />
            </button>
          </td>
        )}
      </tr>

      {expandable && expanded && (
        <tr className="cst-row-detail">
          <td colSpan={colSpanTotal}>
            <div className="cst-detail">{detailNode}</div>
          </td>
        </tr>
      )}

      {/* "Islemler" dropdown — cross-document portal oldugu icin (getTopBody
          iframe→top pencereye tasabilir, ayri stylesheet) INLINE stil, ama
          isDark'a gore tema-farkinda. Duzenle + extraActions + (varsa) ayrac
          + Sil (danger). Acilis: mount→olc→yerlestir→reveal (bkz. dosya ustu
          useLayoutEffect) — fade+hafif scale/slide, ~140ms, transform-origin
          acilan koseye gore (flip'e duyarli); prefers-reduced-motion'da
          gecis kapanir (PREFERS_REDUCED_MOTION). */}
      {menuOpen && createPortal(
        <div
          ref={menuRef}
          onClick={function (e) { e.stopPropagation() }}
          style={buildMenuFloatingStyle(menuPos, menuVisible, isDark)}
        >
          {menuActions.length === 0 && !secondaryAction ? (
            <div style={{ padding: '10px 12px', fontSize: 12, color: 'var(--app-text-muted)' }}>
              Aksiyon yok
            </div>
          ) : (
            <>
              {menuActions.map(function (action, i) {
                var ActionIcon = resolveIcon(action.icon)
                var actionKey = action.id || action.label || i

                // "status-menu" — Durum Değiştir (PageComment Seq 1071, 2026-08-03):
                // ayrı sekmeye gitmez, İşlemler menüsü İÇİNDE açılan nested bir
                // seçenek listesi sunar. Seçim runStatusChange ile inline POST
                // edilir (aynı ChangeQuoteStatus endpoint'i, aynı state machine —
                // sadece tetikleme yolu değişti).
                if (action.type === 'status-menu' && Array.isArray(action.options)) {
                  var isSubOpen = statusSubOpenKey === actionKey
                  // Alt-menü YANA açılır (inline/aşağı değil — kullanıcı isteği 2026-08-03).
                  // Yön: ana menü ekranın sağ kenarına yakın (horizontal === 'right', sola
                  // doğru) açıldıysa alt-menü de SOLA; aksi halde SAĞA fışkırır.
                  var flyoutLeft = !(menuPos && menuPos.horizontal === 'right')
                  var subPanelStyle = {
                    position: 'absolute', top: -5, zIndex: 10011,
                    minWidth: 168, maxWidth: 240, padding: 5, borderRadius: 12,
                    background: 'var(--app-surface)',
                    border: '1px solid var(--app-border)',
                    boxShadow: isDark
                      ? '0 4px 10px rgba(0,0,0,0.35), 0 20px 44px -10px rgba(0,0,0,0.65)'
                      : '0 4px 10px rgba(15,23,42,0.06), 0 16px 36px -8px rgba(15,23,42,0.22)',
                    backdropFilter: 'blur(18px)', WebkitBackdropFilter: 'blur(18px)',
                    display: 'flex', flexDirection: 'column', gap: 1,
                  }
                  if (flyoutLeft) { subPanelStyle.left = '100%'; subPanelStyle.marginLeft = 5 }
                  else { subPanelStyle.right = '100%'; subPanelStyle.marginRight = 5 }
                  return (
                    <div key={actionKey} style={{ position: 'relative' }}>
                      <button
                        type="button"
                        onClick={function (e) { e.stopPropagation(); setStatusSubOpenKey(isSubOpen ? null : actionKey) }}
                        style={{
                          display: 'flex', alignItems: 'center', gap: 7, width: '100%',
                          padding: '6px 8px', borderRadius: 7, border: 'none',
                          background: isSubOpen ? 'var(--app-muted-surface)' : 'transparent',
                          cursor: 'pointer', fontSize: 13, fontWeight: 500, lineHeight: 1.3, textAlign: 'left',
                          color: 'var(--app-text)',
                          transition: 'background-color 0.1s ease',
                        }}
                        onMouseEnter={function (e) { if (!isSubOpen) e.currentTarget.style.background = 'var(--app-muted-surface)' }}
                        onMouseLeave={function (e) { if (!isSubOpen) e.currentTarget.style.background = 'transparent' }}
                      >
                        <ActionIcon size={13} style={{ flexShrink: 0 }} />
                        <span style={{ flex: 1 }}>{action.label}</span>
                        <ChevronRight
                          size={12}
                          style={{ flexShrink: 0, opacity: 0.6, transform: flyoutLeft ? 'none' : 'rotate(180deg)' }}
                        />
                      </button>
                      {isSubOpen && (
                        <div style={subPanelStyle}>
                          {action.options.map(function (opt) {
                            var dotColor = resolveColorForTheme(opt.color, null, isDark).icon
                            return (
                              <button
                                key={opt.value}
                                type="button"
                                disabled={busy}
                                onClick={function (e) { e.stopPropagation(); runStatusChange(action, opt.value) }}
                                style={{
                                  display: 'flex', alignItems: 'center', gap: 7, width: '100%',
                                  padding: '5px 8px', borderRadius: 6, border: 'none', background: 'transparent',
                                  cursor: busy ? 'not-allowed' : 'pointer', fontSize: 12.5, fontWeight: 500,
                                  lineHeight: 1.3, textAlign: 'left', opacity: busy ? 0.5 : 1,
                                  color: 'var(--app-text-muted)',
                                  transition: 'background-color 0.1s ease',
                                }}
                                onMouseEnter={function (e) { if (!busy) e.currentTarget.style.background = 'var(--app-muted-surface)' }}
                                onMouseLeave={function (e) { e.currentTarget.style.background = 'transparent' }}
                              >
                                <span style={{ width: 7, height: 7, borderRadius: 999, background: dotColor, flexShrink: 0 }} />
                                {opt.label}
                              </button>
                            )
                          })}
                        </div>
                      )}
                    </div>
                  )
                }

                return (
                  <button
                    key={actionKey}
                    type="button"
                    onClick={function (e) { e.stopPropagation(); setMenuOpen(false); dispatchMenuAction(action) }}
                    style={{
                      display: 'flex', alignItems: 'center', gap: 7, width: '100%',
                      padding: '6px 8px', borderRadius: 7, border: 'none', background: 'transparent',
                      cursor: 'pointer', fontSize: 13, fontWeight: 500, lineHeight: 1.3, textAlign: 'left',
                      color: 'var(--app-text)',
                      transition: 'background-color 0.1s ease',
                    }}
                    onMouseEnter={function (e) { e.currentTarget.style.background = 'var(--app-muted-surface)' }}
                    onMouseLeave={function (e) { e.currentTarget.style.background = 'transparent' }}
                  >
                    <ActionIcon size={13} style={{ flexShrink: 0 }} />
                    {action.label}
                  </button>
                )
              })}

              {secondaryAction && (
                <>
                  <div style={{ height: 1, margin: '3px 5px', background: 'var(--app-border)' }} />
                  <button
                    type="button"
                    onClick={handleMenuDelete}
                    disabled={busy}
                    style={{
                      display: 'flex', alignItems: 'center', gap: 7, width: '100%',
                      padding: '6px 8px', borderRadius: 7, border: 'none', background: 'transparent',
                      cursor: busy ? 'not-allowed' : 'pointer', fontSize: 13, fontWeight: 500, lineHeight: 1.3, textAlign: 'left',
                      color: '#ef4444', opacity: busy ? 0.5 : 1,
                      transition: 'background-color 0.1s ease',
                    }}
                    onMouseEnter={function (e) { if (!busy) e.currentTarget.style.background = isDark ? 'rgba(239,68,68,0.14)' : 'rgba(239,68,68,0.08)' }}
                    onMouseLeave={function (e) { e.currentTarget.style.background = 'transparent' }}
                  >
                    <Trash2 size={13} style={{ flexShrink: 0 }} />
                    {secondaryAction.label || 'Sil'}
                  </button>
                </>
              )}
            </>
          )}
        </div>,
        getTopBody()
      )}

      {modalOpen && createPortal(
        <div
          ref={modalPortalRef}
          style={{ position: 'fixed', inset: 0, zIndex: 10000, background: 'rgba(0,0,0,0.55)', backdropFilter: 'blur(4px)', display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 20 }}
          onClick={function () { setModalOpen(false) }}
        >
          <div
            style={{ background: 'var(--app-surface)', border: '1px solid var(--app-border)', borderRadius: 16, width: '100%', maxWidth: 780, height: 'min(620px, calc(100vh - 96px))', display: 'flex', flexDirection: 'column', overflow: 'hidden', boxShadow: '0 24px 64px rgba(0,0,0,0.5)' }}
            onClick={function (e) { e.stopPropagation() }}
          >
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '12px 18px', borderBottom: '1px solid var(--app-border)' }}>
              <h3 style={{ fontSize: '.9rem', fontWeight: 700, color: 'var(--app-text)', margin: 0 }}>{modalTitle}</h3>
              <button type="button" onClick={function () { setModalOpen(false) }}
                style={{ background: 'transparent', border: 'none', cursor: 'pointer', padding: 6, borderRadius: 8, display: 'inline-flex' }}
                aria-label="Kapat">
                <X size={16} style={{ color: 'var(--app-text-muted)' }} />
              </button>
            </div>
            <div style={{ flex: 1, minHeight: 0, overflowY: 'auto', padding: 16 }}>
              {modalLoading
                ? <div style={{ padding: '48px 0', textAlign: 'center', color: 'var(--app-text-muted)', fontSize: '.84rem' }}>Yükleniyor…</div>
                : <div ref={modalContentRef} data-sm-modal-content />}
            </div>
          </div>
        </div>,
        getTopBody()
      )}

      {confirmOpen && createPortal(
        <div
          ref={confirmPortalRef}
          style={{ position: 'fixed', inset: 0, zIndex: 10000, background: 'rgba(0,0,0,0.6)', backdropFilter: 'blur(4px)', display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 20 }}
          onClick={handleConfirmNo}
        >
          <div
            style={{ background: 'var(--app-surface)', border: '1px solid var(--app-border)', borderRadius: 16, padding: '32px 28px', maxWidth: 380, width: '90vw', boxShadow: '0 24px 64px rgba(0,0,0,0.5)', display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 12, textAlign: 'center' }}
            onClick={function (e) { e.stopPropagation() }}
          >
            <Trash2 size={26} style={{ color: confirmIsPrimary ? 'var(--app-accent, #6366f1)' : '#ef4444' }} />
            <h3 style={{ fontSize: '1.05rem', fontWeight: 700, color: 'var(--app-text)', margin: 0 }}>Emin misiniz?</h3>
            <p style={{ fontSize: '.84rem', color: 'var(--app-text-muted)', margin: 0 }}>{confirmMsg}</p>
            <div style={{ display: 'flex', gap: 10, marginTop: 8 }}>
              <button type="button" onClick={handleConfirmNo}
                style={{ padding: '8px 16px', borderRadius: 8, fontSize: '.84rem', fontWeight: 600, background: 'var(--app-muted-surface)', color: 'var(--app-text)', border: '1px solid var(--app-border)', cursor: 'pointer' }}>
                İptal
              </button>
              <button type="button" onClick={handleConfirmYes} autoFocus
                style={{ padding: '8px 16px', borderRadius: 8, fontSize: '.84rem', fontWeight: 600, background: confirmIsPrimary ? 'linear-gradient(135deg,#6366f1,#4f46e5)' : 'linear-gradient(135deg,#ef4444,#dc2626)', color: '#fff', border: 'none', cursor: 'pointer', display: 'inline-flex', alignItems: 'center', gap: 6 }}>
                {confirmIsPrimary ? null : <Trash2 size={13} />} {confirmOkLabel}
              </button>
            </div>
          </div>
        </div>,
        getTopBody()
      )}

      {alertOpen && createPortal(
        <div
          ref={alertPortalRef}
          style={{ position: 'fixed', inset: 0, zIndex: 10000, background: 'rgba(0,0,0,0.6)', backdropFilter: 'blur(4px)', display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 20 }}
          onClick={function () { setAlertOpen(false) }}
        >
          <div
            style={{ background: 'var(--app-surface)', border: '1px solid var(--app-border)', borderRadius: 16, padding: '32px 28px', maxWidth: 400, width: '90vw', boxShadow: '0 24px 64px rgba(0,0,0,0.5)', display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 12, textAlign: 'center' }}
            onClick={function (e) { e.stopPropagation() }}
          >
            <AlertTriangle size={26} style={{ color: '#f59e0b' }} />
            <h3 style={{ fontSize: '1.05rem', fontWeight: 700, color: 'var(--app-text)', margin: 0 }}>İşlem Yapılamadı</h3>
            <p style={{ fontSize: '.84rem', color: 'var(--app-text-muted)', margin: 0, lineHeight: 1.5 }}>{alertMsg}</p>
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
