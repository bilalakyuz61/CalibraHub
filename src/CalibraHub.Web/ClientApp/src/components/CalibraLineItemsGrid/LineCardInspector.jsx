/**
 * LineCardInspector — Kart Duzeni editorunun SAG DENETIM RAYI (2026-08-06).
 *
 * Tasarim karari (cok-ajanli tasarim paneli, "Kademeli Derinlik" temeli):
 * tuvalde kalici editor kromu YOKTUR; tum ayarlar bu 296px sabit rayda toplanir.
 * Ray HER ZAMAN mount'tur — alan secmek/birakmak tuvali tek piksel oynatmaz
 * (eskiden panel tuvalin ALTINDA acilip sayfayi kaydiriyordu).
 *
 * Iki durum:
 *   A) Secim yok  → Alan Havuzu (gizli alanlar) + "Nasil Calisir?" ipuclari
 *   B) Alan secili → Genislik · Satir · Gorunurluk · (katlanir) Baslik · [dokunmatik] Sira
 *
 * SAF SUNUM bileseni: kendi state'i yalniz `titleOpen` + havuz arama metni.
 * Tum mutasyonlar props'taki callback'lerle editore delege edilir.
 */
import { useState, useRef } from 'react'
import {
  Plus, Search, GripVertical, MoveHorizontal, MousePointerClick,
  ChevronDown, ChevronUp, ArrowLeft, ArrowRight, X as XIcon,
} from 'lucide-react'
import {
  resolveIcon, CARD_GRID_UNITS, MIN_SPAN, spanLabel,
  LABEL_COLOR_TOKENS, LABEL_COLOR_SWATCH_CLS, LABEL_COLOR_NAMES,
} from './cardLayoutTokens'

/* ── Segment kontrolu — native <select> YERINE (2026-08-06) ────────────────
   Native select dark temada beyaz acilir (color-scheme sinyali gerektirir) ve
   secim 2 tik ister. Segment: 1 tik + tema sorunu yok. a11y borcu ayni yerde
   odenir: role=radiogroup + roving tabindex + ok tuslari. */
export function SegmentedControl(props) {
  var options = props.options || []
  var value = props.value
  var disabled = props.disabled === true
  var refs = useRef([])

  function pick(v) { if (!disabled && typeof props.onChange === 'function') props.onChange(v) }
  function handleKeyDown(e) {
    var idx = options.findIndex(function (o) { return o.value === value })
    if (idx < 0) idx = 0
    var next = null
    if (e.key === 'ArrowRight' || e.key === 'ArrowDown') next = (idx + 1) % options.length
    else if (e.key === 'ArrowLeft' || e.key === 'ArrowUp') next = (idx - 1 + options.length) % options.length
    else if (e.key === 'Home') next = 0
    else if (e.key === 'End') next = options.length - 1
    if (next == null) return
    e.preventDefault()
    e.stopPropagation()
    pick(options[next].value)
    var el = refs.current[next]
    if (el && el.focus) el.focus()
  }

  return (
    <div
      role="radiogroup"
      aria-label={props.ariaLabel}
      onKeyDown={handleKeyDown}
      className={'flex items-stretch gap-1 ' + (disabled ? 'opacity-45 pointer-events-none' : '')}
    >
      {options.map(function (o, i) {
        var on = o.value === value
        return (
          <button
            key={String(o.value)}
            ref={function (el) { refs.current[i] = el }}
            type="button"
            role="radio"
            aria-checked={on}
            tabIndex={on ? 0 : -1}
            disabled={disabled}
            onClick={function () { pick(o.value) }}
            title={o.title || o.label}
            className={'flex-1 h-[26px] px-2 rounded-md text-[11px] font-semibold border transition-colors ' + (
              on
                ? 'bg-indigo-500 text-[#fff] border-indigo-500'
                : 'border-slate-200 text-slate-500 hover:border-indigo-300 hover:text-indigo-600 dark:border-white/10 dark:text-white/55 dark:hover:border-indigo-400/40 dark:hover:text-indigo-300'
            )}
          >
            {o.label}
          </button>
        )
      })}
    </div>
  )
}

/* Proje standardi: boolean girisi checkbox degil SWITCH (CLAUDE.md). */
export function SwitchToggle(props) {
  var checked = props.checked === true
  var disabled = props.disabled === true
  function toggle() { if (!disabled && typeof props.onChange === 'function') props.onChange(!checked) }
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={props.ariaLabel}
      aria-describedby={props.describedBy || undefined}
      disabled={disabled}
      onClick={toggle}
      onKeyDown={function (e) { if (e.key === ' ' || e.key === 'Enter') { e.preventDefault(); toggle() } }}
      className={'relative w-[34px] h-[18px] rounded-full transition-colors flex-shrink-0 ' + (
        disabled ? 'opacity-45 cursor-not-allowed ' : 'cursor-pointer '
      ) + (checked ? 'bg-indigo-500' : 'bg-slate-200 dark:bg-white/15')}
    >
      <span
        className="absolute top-[2px] w-[14px] h-[14px] rounded-full bg-[#fff] shadow transition-transform"
        style={{ transform: 'translateX(' + (checked ? 18 : 2) + 'px)' }}
      />
    </button>
  )
}

/* Genislik cubugu — Alan Tanimlama'daki (WidgetBuilderForm) 24 kolonluk
   colSpan slider'inin kart duzenine uyarlanmisi (2026-08-06 kullanici istegi:
   "1/6, 1/4 gibi cipler yerine widget ayarindaki gibi bir bar"). 48 segment;
   tikla, surukle veya ok tuslariyla ayarlanir. */
function SpanBar(props) {
  var barRef = useRef(null)
  var disabled = props.disabled === true

  function valueFromX(clientX) {
    var el = barRef.current
    if (!el) return props.value
    var rect = el.getBoundingClientRect()
    if (rect.width <= 0) return props.value
    var x = Math.min(Math.max(clientX - rect.left, 0), rect.width)
    var v = Math.ceil((x / rect.width) * CARD_GRID_UNITS)
    return Math.min(CARD_GRID_UNITS, Math.max(MIN_SPAN, v))
  }
  function onPointerDown(e) {
    if (disabled || (e.button != null && e.button !== 0)) return
    e.preventDefault()
    try { e.currentTarget.setPointerCapture(e.pointerId) } catch (_) {}
    props.onChange(valueFromX(e.clientX))
  }
  function onPointerMove(e) {
    if (disabled || !e.currentTarget.hasPointerCapture || !e.currentTarget.hasPointerCapture(e.pointerId)) return
    e.preventDefault()
    props.onChange(valueFromX(e.clientX))
  }
  function onPointerEnd(e) {
    try { if (e.currentTarget.hasPointerCapture(e.pointerId)) e.currentTarget.releasePointerCapture(e.pointerId) } catch (_) {}
  }
  function onKeyDown(e) {
    if (disabled) return
    var v = null
    if (e.key === 'ArrowRight' || e.key === 'ArrowUp') v = Math.min(CARD_GRID_UNITS, props.value + 1)
    else if (e.key === 'ArrowLeft' || e.key === 'ArrowDown') v = Math.max(MIN_SPAN, props.value - 1)
    else if (e.key === 'Home') v = MIN_SPAN
    else if (e.key === 'End') v = CARD_GRID_UNITS
    if (v == null) return
    e.preventDefault()
    e.stopPropagation()
    props.onChange(v)
  }

  return (
    <div className="flex flex-col gap-1.5">
      <div
        ref={barRef}
        role="slider"
        tabIndex={disabled ? -1 : 0}
        aria-valuemin={MIN_SPAN}
        aria-valuemax={CARD_GRID_UNITS}
        aria-valuenow={props.value}
        aria-valuetext={props.value + '/' + CARD_GRID_UNITS + ' · ' + spanLabel(props.value)}
        aria-label="Alan genişliği"
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={onPointerEnd}
        onPointerCancel={onPointerEnd}
        onKeyDown={onKeyDown}
        style={{ touchAction: 'none' }}
        className={'flex gap-[1px] h-5 rounded-md overflow-hidden select-none focus:outline-none focus:ring-2 focus:ring-indigo-400/40 ' + (
          disabled ? 'opacity-45' : 'cursor-ew-resize'
        )}
      >
        {Array.from({ length: CARD_GRID_UNITS }).map(function (_, i) {
          var filled = (i + 1) <= props.value
          return (
            <div
              key={i}
              className={'flex-1 transition-colors pointer-events-none ' + (
                filled ? 'bg-indigo-500' : 'bg-slate-200 dark:bg-white/[0.06]'
              )}
            />
          )
        })}
      </div>
      <div className="flex items-center justify-between text-[10px]">
        <span className="font-mono tabular-nums text-slate-600 dark:text-white/60" aria-live="polite">
          {props.value}/{CARD_GRID_UNITS}
        </span>
        <span className="font-semibold text-indigo-600 dark:text-indigo-300">{spanLabel(props.value)}</span>
      </div>
    </div>
  )
}

/* CSS spec yalniz 100'un katlarini tanir — ara deger (560/620) yasak. */
var WEIGHT_NAMES = { 400: 'Normal', 500: 'Orta', 600: 'Yarı Kalın', 700: 'Kalın' }

/* Kademeli ayar cubugu (2026-08-06 kullanici istegi) — boyut/kalinlik gibi
   sirali degerler icin segment/stepper yerine slider. Native `range`:
   `accent-color` ile tema uyumlu, `color-scheme` modal kokunde ayarli. */
function SliderRow(props) {
  var disabled = props.disabled === true
  return (
    <div>
      <div className="flex items-center gap-2 mb-1.5">
        <span className="text-[10px] font-semibold text-slate-500 dark:text-white/45">{props.label}</span>
        <span className="flex-1" />
        <span className="text-[11px] font-mono tabular-nums text-slate-600 dark:text-white/70">
          {props.isDefault ? 'Varsayılan' : props.display}
        </span>
      </div>
      <div className="flex items-center gap-2">
        <input
          type="range"
          min={props.min}
          max={props.max}
          step={props.step}
          value={props.value}
          disabled={disabled}
          aria-label={props.ariaLabel}
          aria-valuetext={props.isDefault ? 'Varsayılan' : props.display}
          onChange={function (e) { props.onChange(parseInt(e.target.value, 10)) }}
          className="flex-1 h-1.5 accent-indigo-500 cursor-pointer disabled:opacity-45"
        />
        {!props.isDefault && (
          <button
            type="button"
            onClick={props.onReset}
            title="Varsayılana dön"
            className="text-[10px] font-semibold text-slate-400 hover:text-indigo-500 dark:text-white/35 dark:hover:text-indigo-300 transition-colors flex-shrink-0"
          >Sıfırla</button>
        )}
      </div>
    </div>
  )
}

function SectionLabel(props) {
  return (
    <div className="text-[10px] font-semibold text-slate-500 dark:text-white/45 mb-1.5">{props.children}</div>
  )
}

/* Bir alanin baslik override'larindan tek satirlik ozet — katlanir bolum
   kapaliyken "ne degistirilmis" bilgisi kaybolmasin diye. */
function titleSummary(it) {
  if (!it) return 'Varsayılan'
  var parts = []
  if (it.labelText && it.labelText.trim()) parts.push('Özel metin')
  if (it.labelStyle === 'modern') parts.push('Modern')
  else if (it.labelStyle === 'inline') parts.push('Sade')
  if (it.labelSize) parts.push(it.labelSize + ' px')
  if (it.labelWeight === 500) parts.push('Orta')
  else if (it.labelWeight === 600) parts.push('Yarı Kalın')
  else if (it.labelWeight === 700) parts.push('Kalın')
  if (it.labelColor) parts.push(LABEL_COLOR_NAMES[it.labelColor] || it.labelColor)
  return parts.length ? parts.join(' · ') : 'Varsayılan'
}

export default function LineCardInspector(props) {
  var item = props.item || null
  var rowInfo = props.rowInfo || null
  var hiddenItems = props.hiddenItems || []
  var canEdit = props.canEdit !== false
  var coarse = props.coarse === true

  var [titleOpen, setTitleOpen] = useState(false)
  var [poolQuery, setPoolQuery] = useState('')

  // ── DURUM A: secim yok → Alan Havuzu + ipuclari ──────────────────────────
  if (!item) {
    var q = poolQuery.trim().toLocaleLowerCase('tr')
    var pool = q
      ? hiddenItems.filter(function (h) { return String(h.label || '').toLocaleLowerCase('tr').indexOf(q) >= 0 })
      : hiddenItems

    return (
      <div className="flex flex-col gap-4">
        {!canEdit && (
          <div className="text-[11px] text-amber-600 dark:text-amber-300">
            Bu düzeni değiştirme yetkiniz yok — yalnızca görüntülüyorsunuz.
          </div>
        )}

        {hiddenItems.length > 0 ? (
          <div>
            <div className="flex items-center gap-1.5 mb-1">
              <span className="text-[12px] font-semibold text-slate-700 dark:text-white/80">Alan Havuzu</span>
              <span className="text-[10px] font-mono tabular-nums text-slate-400 dark:text-white/35">({hiddenItems.length})</span>
            </div>
            <div className="text-[11px] text-slate-500 dark:text-white/45 mb-2">Karta eklemek için tıklayın.</div>

            {hiddenItems.length > 8 && (
              <div className="relative mb-2">
                <Search size={12} className="absolute left-2.5 top-1/2 -translate-y-1/2 text-slate-400 dark:text-white/35" />
                <input
                  type="text"
                  value={poolQuery}
                  onChange={function (e) { setPoolQuery(e.target.value) }}
                  placeholder="Alan ara…"
                  className="w-full h-7 pl-8 pr-2 rounded-lg text-[11.5px] border border-slate-200 bg-[#fff] text-slate-700 placeholder:text-slate-300 focus:outline-none focus:ring-1 focus:ring-indigo-400 dark:border-white/10 dark:bg-white/[0.04] dark:text-white/85 dark:placeholder:text-white/25"
                />
              </div>
            )}

            <div className="flex flex-col gap-1">
              {pool.map(function (h) {
                var Icon = resolveIcon(h.icon)
                return (
                  <button
                    key={h.key}
                    type="button"
                    disabled={!canEdit}
                    onClick={function () { props.onShowHidden(h.key) }}
                    className="w-full h-7 flex items-center gap-2 px-2 rounded-lg text-[11.5px] border border-transparent transition-colors text-slate-600 hover:border-indigo-300 hover:bg-indigo-50 dark:text-white/70 dark:hover:border-indigo-400/40 dark:hover:bg-indigo-500/10 disabled:opacity-45"
                  >
                    <Icon size={12} strokeWidth={1.8} className={h.isWidget ? 'text-sky-500 dark:text-sky-300 flex-shrink-0' : 'text-slate-400 dark:text-white/35 flex-shrink-0'} />
                    <span className="truncate flex-1 text-left">{h.label}</span>
                    <Plus size={14} strokeWidth={2} className="flex-shrink-0 opacity-60" />
                  </button>
                )
              })}
              {!pool.length && (
                <div className="text-[11px] text-slate-400 dark:text-white/35 px-2 py-1">Eşleşen alan yok.</div>
              )}
            </div>
            <div className="mt-4 border-t border-slate-200 dark:border-white/[0.08]" />
          </div>
        ) : (
          <div className="text-[11px] text-slate-500 dark:text-white/45">Tüm alanlar kartta.</div>
        )}

        <div>
          <SectionLabel>Nasıl Çalışır?</SectionLabel>
          <div className="flex flex-col">
            {[
              { Icon: GripVertical, text: 'Sürükleyip bırakın — sıra' },
              { Icon: MoveHorizontal, text: 'Sağ kenardan çekin — genişlik' },
              { Icon: MousePointerClick, text: 'Bir alana tıklayın — başlık' },
            ].map(function (r, i) {
              var I = r.Icon
              return (
                <div key={i} className="h-7 flex items-center gap-2">
                  <I size={14} strokeWidth={1.8} className="text-slate-400 dark:text-white/35 flex-shrink-0" />
                  <span className="text-[12px] text-slate-600 dark:text-white/70">{r.text}</span>
                </div>
              )
            })}
          </div>
        </div>

        <div className="border-t border-slate-200 dark:border-white/[0.08] pt-3 flex flex-col gap-1">
          <div className="text-[11px] text-slate-400 dark:text-white/35">Her satır {CARD_GRID_UNITS} birimdir; sığmayan alan alta akar.</div>
          <div className="text-[11px] text-slate-400 dark:text-white/35">Dar ekranlarda kart varsayılan ızgaraya döner.</div>
        </div>
      </div>
    )
  }

  // ── DURUM B: alan secili ─────────────────────────────────────────────────
  var Icon = resolveIcon(item.icon)
  var lockedNoteId = 'lcl-locked-note'

  return (
    <div className="flex flex-col gap-3.5">
      {!canEdit && (
        <div className="text-[11px] text-amber-600 dark:text-amber-300">
          Bu düzeni değiştirme yetkiniz yok — yalnızca görüntülüyorsunuz.
        </div>
      )}

      {/* B0 — baslik seridi */}
      <div className="h-10 flex items-center gap-2 border-b border-slate-200 dark:border-white/[0.08]">
        <Icon size={14} strokeWidth={1.8} className={item.isWidget ? 'text-sky-500 dark:text-sky-300 flex-shrink-0' : 'text-slate-400 dark:text-white/35 flex-shrink-0'} />
        <span className="text-[12px] font-semibold text-slate-700 dark:text-white/85 truncate flex-1">{item.label}</span>
        {item.isWidget && (
          <span className="px-1.5 py-0.5 rounded text-[10px] font-semibold bg-sky-50 text-sky-600 border border-sky-200 dark:bg-sky-500/15 dark:text-sky-300 dark:border-sky-400/30 flex-shrink-0">Ek Alan</span>
        )}
        <button
          type="button"
          onClick={props.onClearSelection}
          aria-label="Seçimi kaldır"
          className="w-6 h-6 rounded-md flex items-center justify-center text-slate-400 hover:text-slate-600 hover:bg-slate-100 dark:text-white/35 dark:hover:text-white/70 dark:hover:bg-white/[0.07] flex-shrink-0"
        >
          <XIcon size={13} strokeWidth={2} />
        </button>
      </div>

      {/* B1 — Genislik (segment bar; hazir kesir cipleri kaldirildi) */}
      <div>
        <SectionLabel>Genişlik</SectionLabel>
        <SpanBar
          value={item.span}
          disabled={!canEdit}
          onChange={function (v) { props.onSetSpan(item.key, v) }}
        />
        <div className="text-[11px] text-slate-400 dark:text-white/35 mt-1.5">Alt+←/→ ince ayar · Ctrl+←/→ sıra</div>
      </div>

      {/* B2 — Satir */}
      {rowInfo && (
        <div>
          <SectionLabel>Satır</SectionLabel>
          <div className="text-[11px] text-slate-600 dark:text-white/70 mb-2">
            Satır {rowInfo.index + 1} · {rowInfo.free > 0 ? rowInfo.free + ' birim boş' : 'dolu'}
          </div>
          <div className={'flex items-center gap-1.5 ' + (canEdit ? '' : 'opacity-45 pointer-events-none')}>
            <button
              type="button"
              disabled={!rowInfo.canDistribute}
              onClick={props.onDistributeRow}
              className={'h-[26px] px-2.5 rounded-md text-[11px] font-semibold border transition-colors ' + (
                rowInfo.canDistribute
                  ? 'border-slate-200 text-slate-600 hover:border-indigo-300 hover:text-indigo-600 dark:border-white/10 dark:text-white/70 dark:hover:border-indigo-400/40 dark:hover:text-indigo-300'
                  : 'opacity-40 cursor-not-allowed border-slate-200 text-slate-400 dark:border-white/10 dark:text-white/30'
              )}
            >Eşit Dağıt</button>
            {rowInfo.free > 0 && (
              <button
                type="button"
                onClick={props.onFillRow}
                className="h-[26px] px-2.5 rounded-md text-[11px] font-semibold border border-slate-200 text-slate-600 hover:border-indigo-300 hover:text-indigo-600 dark:border-white/10 dark:text-white/70 dark:hover:border-indigo-400/40 dark:hover:text-indigo-300 transition-colors"
              >Boşluğu Doldur</button>
            )}
          </div>
          {/* Sessiz no-op YASAK (CLAUDE.md kural 3): buton pasifse gerekcesi yazili. */}
          {!rowInfo.canDistribute && (
            <div className="text-[11px] text-rose-500/80 dark:text-rose-300/70 mt-1.5">
              Bu satırda çok fazla alan var — eşit bölünemez.
            </div>
          )}
        </div>
      )}

      {/* B3 — Gizle. Yalniz STANDART alanlar icin (2026-08-06 kullanici karari):
          ek alanlarin kartta gorunurlugu Alan Yonetimi'ndeki "Kartta Göster"
          switch'i (WidgetMas.ShowOnCard) ile yonetilir — ayni ayarin iki yerde
          durmasi cift yonetim ve celiskili durum uretir. */}
      {item.isWidget ? (
        <div>
          <SectionLabel>Görünürlük</SectionLabel>
          <div className="text-[11px] text-slate-500 dark:text-white/45">
            Ek alanların kartta görünürlüğü Alan Yönetimi → “Kartta Göster” ile yönetilir.
          </div>
        </div>
      ) : (
        <div>
          <SectionLabel>Görünürlük</SectionLabel>
          <div className="flex items-center gap-2">
            <SwitchToggle
              checked={!item.visible}
              disabled={!canEdit || item.locked}
              ariaLabel="Alanı gizle"
              describedBy={item.locked ? lockedNoteId : null}
              onChange={function () { props.onToggleVisible(item.key) }}
            />
            <span className="text-[12px] text-slate-600 dark:text-white/70">Gizle</span>
          </div>
          {item.locked && (
            <div id={lockedNoteId} className="text-[11px] text-rose-500/80 dark:text-rose-300/70 mt-1.5">
              Zorunlu alan — gizlenemez.
            </div>
          )}
        </div>
      )}

      {/* B4 — Baslik (katlanir, varsayilan KAPALI) */}
      <div className="border-t border-slate-200 dark:border-white/[0.08] pt-3">
        <button
          type="button"
          onClick={function () { setTitleOpen(!titleOpen) }}
          aria-expanded={titleOpen}
          aria-controls="lcl-title-section"
          className="w-full flex items-center gap-2 text-left"
        >
          <span className="text-[12px] font-semibold text-slate-700 dark:text-white/80">Başlık</span>
          <span className="flex-1 text-[11px] text-slate-400 dark:text-white/35 truncate text-right">{titleSummary(item)}</span>
          {titleOpen
            ? <ChevronUp size={14} strokeWidth={2} className="text-slate-400 dark:text-white/35 flex-shrink-0" />
            : <ChevronDown size={14} strokeWidth={2} className="text-slate-400 dark:text-white/35 flex-shrink-0" />}
        </button>

        {titleOpen && (
          <div id="lcl-title-section" className={'flex flex-col gap-3 mt-3 ' + (canEdit ? '' : 'opacity-45 pointer-events-none')}>
            <div>
              <SectionLabel>Metin</SectionLabel>
              <input
                type="text"
                value={item.labelText}
                maxLength={60}
                placeholder={item.label}
                onChange={function (e) { props.onPatch(item.key, { labelText: e.target.value }) }}
                className="w-full h-8 px-2.5 rounded-lg text-[12px] border border-slate-200 bg-[#fff] text-slate-700 placeholder:text-slate-300 focus:outline-none focus:ring-1 focus:ring-indigo-400 dark:border-white/10 dark:bg-white/[0.04] dark:text-white/85 dark:placeholder:text-white/25"
              />
            </div>

            <div>
              <SectionLabel>Stil</SectionLabel>
              <SegmentedControl
                ariaLabel="Başlık stili"
                value={item.labelStyle || ''}
                onChange={function (v) { props.onPatch(item.key, { labelStyle: v || null }) }}
                options={[
                  { value: '', label: 'Standart', title: 'Başlık kutunun üstünde' },
                  { value: 'modern', label: 'Modern', title: 'Başlık alanın üst kenarında yüzer' },
                  { value: 'inline', label: 'Sade', title: 'Başlık solda, alanla aynı satırda' },
                ]}
              />
            </div>

            <SliderRow
              label="Boyut"
              min={9}
              max={14}
              step={1}
              value={item.labelSize || 10}
              isDefault={!item.labelSize}
              display={(item.labelSize || 10) + ' px'}
              ariaLabel="Başlık boyutu"
              onChange={function (v) { props.onPatch(item.key, { labelSize: v }) }}
              onReset={function () { props.onPatch(item.key, { labelSize: null }) }}
            />

            <SliderRow
              label="Kalınlık"
              min={400}
              max={700}
              step={100}
              value={item.labelWeight || 700}
              isDefault={!item.labelWeight}
              display={WEIGHT_NAMES[item.labelWeight || 700]}
              ariaLabel="Başlık kalınlığı"
              onChange={function (v) { props.onPatch(item.key, { labelWeight: v }) }}
              onReset={function () { props.onPatch(item.key, { labelWeight: null }) }}
            />

            <div>
              <SectionLabel>Renk</SectionLabel>
              <div className="flex items-center gap-1.5 flex-wrap">
                <button
                  type="button"
                  onClick={function () { props.onPatch(item.key, { labelColor: null }) }}
                  title="Varsayılan renk"
                  aria-label="Varsayılan renk"
                  className={'w-5 h-5 rounded-full border-2 flex items-center justify-center text-[9px] font-bold text-slate-400 dark:text-white/40 ' + (
                    !item.labelColor ? 'border-indigo-500' : 'border-slate-200 dark:border-white/15'
                  )}
                >—</button>
                {LABEL_COLOR_TOKENS.map(function (tok) {
                  var on = item.labelColor === tok
                  return (
                    <button
                      key={tok}
                      type="button"
                      onClick={function () { props.onPatch(item.key, { labelColor: tok }) }}
                      title={LABEL_COLOR_NAMES[tok]}
                      aria-label={LABEL_COLOR_NAMES[tok]}
                      className={'w-5 h-5 rounded-full ' + LABEL_COLOR_SWATCH_CLS[tok] + (
                        on ? ' ring-2 ring-indigo-500 ring-offset-1 dark:ring-offset-slate-900' : ''
                      )}
                    />
                  )
                })}
              </div>
            </div>

            {(item.labelText || item.labelSize || item.labelWeight || item.labelColor || item.labelStyle) && (
              <button
                type="button"
                onClick={function () {
                  props.onPatch(item.key, { labelText: '', labelSize: null, labelWeight: null, labelColor: null, labelStyle: null })
                }}
                className="self-start text-[11px] text-slate-500 hover:text-rose-600 dark:text-white/45 dark:hover:text-rose-300 transition-colors"
              >Başlık ayarlarını temizle</button>
            )}
          </div>
        )}
      </div>

      {/* B5 — dokunmatik: suruklemeye erisilebilir alternatif */}
      {coarse && canEdit && (
        <div className="border-t border-slate-200 dark:border-white/[0.08] pt-3">
          <SectionLabel>Sıra</SectionLabel>
          <div className="flex items-center gap-1.5">
            <button
              type="button"
              onClick={function () { props.onMove(-1) }}
              aria-label="Önceki sıraya al"
              className="w-7 h-7 rounded-md border border-slate-200 text-slate-500 flex items-center justify-center hover:border-indigo-300 hover:text-indigo-600 dark:border-white/10 dark:text-white/55"
            ><ArrowLeft size={14} strokeWidth={2} /></button>
            <button
              type="button"
              onClick={function () { props.onMove(1) }}
              aria-label="Sonraki sıraya al"
              className="w-7 h-7 rounded-md border border-slate-200 text-slate-500 flex items-center justify-center hover:border-indigo-300 hover:text-indigo-600 dark:border-white/10 dark:text-white/55"
            ><ArrowRight size={14} strokeWidth={2} /></button>
          </div>
        </div>
      )}

      {/* Gizli alanlar secim varken de gozden kaybolmasin (sessiz kayip yok) */}
      {hiddenItems.length > 0 && (
        <button
          type="button"
          onClick={props.onClearSelection}
          className="self-start text-[10px] font-mono tabular-nums text-slate-400 hover:text-indigo-500 dark:text-white/35 dark:hover:text-indigo-300 transition-colors"
          title="Alan Havuzu'nu aç"
        >Havuz: {hiddenItems.length}</button>
      )}
    </div>
  )
}
