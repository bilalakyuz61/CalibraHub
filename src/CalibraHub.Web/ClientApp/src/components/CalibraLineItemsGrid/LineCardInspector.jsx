/**
 * LineCardInspector — Kart Duzeni editorunun alan-satiri yapi taslari (2026-08-19
 * sadelestirme).
 *
 * ONCEKI SURUM (2026-08-06) sag tarafta 296px sabit bir "denetim rayi" ve solda
 * 48-birimlik serbest-yerlesim tuvaliydi. Gercek kart artik CSS-grid degil
 * flex-wrap kompakt bir SERIT oldugu icin (bkz. CalibraLineItemsGrid.jsx,
 * 2026-08-19) o tuval/ray ikilisi YANLIS bir onizleme veriyordu — kaldirildi.
 *
 * Bu dosya artik LineCardLayoutEditor.jsx'in kullandigi KUCUK, SAF (presentational)
 * yapi taslarini barindirir:
 *   - SegmentedControl  — native <select> yerine (tema/erisilebilirlik, degismedi)
 *   - SwitchToggle      — boolean girisleri icin switch (CLAUDE.md zorunlulugu, degismedi)
 *   - TitleOverridePanel— bir SERIT alaninin baslik override'lari (metin/stil/
 *                         boyut/kalinlik/renk) — yalniz serit alanlarinda anlamli,
 *                         kimlik alanlarinda (malzeme kodu/adi/satir toplami)
 *                         gercek kartta HIC uygulanmaz (bkz. CalibraLineItemsGrid.jsx
 *                         kimlik satiri — item.label/labelStyle okunmaz).
 *   - FieldRow (default) — sag serit listesindeki TEK satir: surukle-birak sira +
 *                         yukari/asagi dugmeler + genislik (Dar/Normal/Genis) +
 *                         gorunurluk switch + katlanir Baslik paneli.
 */
import { useState } from 'react'
import {
  GripVertical, ChevronDown, ChevronUp, ChevronsUpDown,
} from 'lucide-react'
import {
  resolveIcon, WIDTH_OPTIONS, widthValueForSpan, spanForWidthValue,
  LABEL_COLOR_TOKENS, LABEL_COLOR_SWATCH_CLS, LABEL_COLOR_NAMES,
} from './cardLayoutTokens'

/* ── Segment kontrolu — native <select> YERINE ─────────────────────────────
   Native select dark temada beyaz acilir (color-scheme sinyali gerektirir) ve
   secim 2 tik ister. Segment: 1 tik + tema sorunu yok. a11y borcu ayni yerde
   odenir: role=radiogroup + roving tabindex + ok tuslari. */
export function SegmentedControl(props) {
  var options = props.options || []
  var value = props.value
  var disabled = props.disabled === true

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
  }

  return (
    <div
      role="radiogroup"
      aria-label={props.ariaLabel}
      onKeyDown={handleKeyDown}
      className={'flex items-stretch gap-1 ' + (disabled ? 'opacity-45 pointer-events-none' : '')}
    >
      {options.map(function (o) {
        var on = o.value === value
        return (
          <button
            key={String(o.value)}
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
      ) + (checked
        ? (props.onClass || 'bg-indigo-500')
        : (props.offClass || 'bg-slate-200 dark:bg-white/15'))}
    >
      {/* left:0 acikca verilir — absolute ogenin "static position" belirsizligi
          bazi tarayicilarda thumb'i track disina tasiriyordu. */}
      <span
        className="absolute top-[2px] left-0 w-[14px] h-[14px] rounded-full bg-[#fff] shadow transition-transform"
        style={{ transform: 'translateX(' + (checked ? 18 : 2) + 'px)' }}
      />
    </button>
  )
}

/* CSS spec yalniz 100'un katlarini tanir — ara deger (560/620) yasak. */
var WEIGHT_NAMES = { 400: 'Normal', 500: 'Orta', 600: 'Yarı Kalın', 700: 'Kalın' }

function SectionLabel(props) {
  return (
    <div className="text-[10px] font-semibold text-slate-500 dark:text-white/45 mb-1.5">{props.children}</div>
  )
}

/* Kademeli ayar cubugu — boyut/kalinlik gibi sirali degerler icin native `range`
   (`accent-color` ile tema uyumlu). */
function SliderRow(props) {
  var disabled = props.disabled === true
  return (
    <div>
      <div className="flex items-center gap-2 mb-1">
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

/**
 * Baslik override paneli — YALNIZ serit alanlari icin anlamli (bkz. dosya basi
 * notu). `item`in labelText/labelStyle/labelSize/labelWeight/labelColor
 * alanlarini duzenler; `onPatch(key, patch)` ile yukari bildirir.
 */
export function TitleOverridePanel(props) {
  var item = props.item
  var canEdit = props.canEdit !== false
  return (
    <div className={'flex flex-col gap-3 ' + (canEdit ? '' : 'opacity-45 pointer-events-none')}>
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
  )
}

/* Bir alanin baslik override'larindan tek satirlik ozet — panel kapaliyken
   "ne degistirilmis" bilgisi kaybolmasin diye. */
function titleSummary(it) {
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

/**
 * Serit listesindeki TEK satir. Surukle-birak SIRALAMA + yukari/asagi dugmeler
 * (erisilebilir alternatif) + Genislik (Dar/Normal/Genis) + Gorunurluk switch +
 * katlanir Baslik paneli.
 *
 * `widthLocked` (ör. İskonto %/KDV %): gercek kartta bu alanlar HER ZAMAN dar
 * gosterilir (bkz. CalibraLineItemsGrid.jsx col.type==='percent' ozel-durumu) —
 * yaniltici bir genislik kontrolu sunmak yerine acikca "Her zaman dar" notu
 * gosterilir (CLAUDE.md sessiz-kirik kurali: yanlis/yanitlayici UI sunma).
 */
export default function FieldRow(props) {
  var item = props.item
  var canEdit = props.canEdit !== false
  var [titleOpen, setTitleOpen] = useState(false)
  var Icon = resolveIcon(item.icon)
  var lockedNoteId = 'lcl-locked-' + item.key
  var labelText = (item.labelText && item.labelText.trim()) ? item.labelText.trim() : item.label

  return (
    <div
      draggable={canEdit && !props.dragDisabled}
      onDragStart={function (e) { if (props.onDragStart) props.onDragStart(e) }}
      onDragOver={function (e) {
        // preventDefault SART — yoksa tarayici drop'u hic tetiklemez (bkz.
        // HTML5 DnD spec). props.onDragOver opsiyoneldir (editor su an
        // kullanmiyor, hedef vurgusu YOK — yalniz drop aninda yeniden siralar).
        e.preventDefault()
        if (props.onDragOver) props.onDragOver(e)
      }}
      onDrop={function (e) { e.preventDefault(); if (props.onDrop) props.onDrop(e) }}
      onDragEnd={function (e) { if (props.onDragEnd) props.onDragEnd(e) }}
      className={'rounded-lg border transition-colors ' + (
        props.isDragging
          ? 'opacity-40 border-indigo-300 dark:border-indigo-400/40'
          : 'border-slate-200 dark:border-white/10 bg-[#fff] dark:bg-white/[0.02]'
      )}
    >
      <div className="flex items-center gap-2 px-2 py-1.5">
        {canEdit && (
          <GripVertical
            size={13}
            strokeWidth={2}
            aria-hidden="true"
            className="text-slate-300 dark:text-white/20 flex-shrink-0 cursor-grab active:cursor-grabbing"
          />
        )}
        {canEdit && (
          <div className="flex flex-col flex-shrink-0 -my-1">
            <button
              type="button"
              disabled={props.isFirst}
              onClick={props.onMoveUp}
              aria-label={'"' + labelText + '" alanını bir yukarı taşı'}
              className="w-4 h-3.5 flex items-center justify-center text-slate-400 hover:text-indigo-600 disabled:opacity-25 disabled:hover:text-slate-400 dark:text-white/35 dark:hover:text-indigo-300"
            ><ChevronUp size={11} strokeWidth={2.4} /></button>
            <button
              type="button"
              disabled={props.isLast}
              onClick={props.onMoveDown}
              aria-label={'"' + labelText + '" alanını bir aşağı taşı'}
              className="w-4 h-3.5 flex items-center justify-center text-slate-400 hover:text-indigo-600 disabled:opacity-25 disabled:hover:text-slate-400 dark:text-white/35 dark:hover:text-indigo-300"
            ><ChevronDown size={11} strokeWidth={2.4} /></button>
          </div>
        )}

        {Icon && <Icon size={13} strokeWidth={1.8} className={(item.isWidget ? 'text-sky-500 dark:text-sky-300' : 'text-slate-400 dark:text-white/35') + ' flex-shrink-0'} />}
        <span className="text-[12.5px] text-slate-700 dark:text-white/80 truncate flex-1 min-w-0">{labelText}</span>
        {item.isWidget && (
          <span className="px-1.5 py-0.5 rounded text-[9.5px] font-semibold bg-sky-50 text-sky-600 border border-sky-200 dark:bg-sky-500/15 dark:text-sky-300 dark:border-sky-400/30 flex-shrink-0">Ek Alan</span>
        )}

        {/* Genislik — Dar/Normal/Genis (span'a eslenir); percent alanlarda sabit. */}
        <div className="flex-shrink-0 w-[168px]">
          {props.widthLocked ? (
            <div className="text-[10.5px] text-slate-400 dark:text-white/35 text-right" title="Bu alan gerçek kartta her zaman dar gösterilir.">
              Her zaman dar
            </div>
          ) : (
            <SegmentedControl
              ariaLabel={labelText + ' genişliği'}
              value={widthValueForSpan(item.span)}
              disabled={!canEdit}
              onChange={function (v) { props.onSetSpan(item.key, spanForWidthValue(v)) }}
              options={WIDTH_OPTIONS}
            />
          )}
        </div>

        {/* Baslik override ac/kapa */}
        <button
          type="button"
          onClick={function () { setTitleOpen(!titleOpen) }}
          aria-expanded={titleOpen}
          title="Başlık metni/stili"
          className={'flex-shrink-0 h-[26px] px-1.5 rounded-md flex items-center gap-1 text-[10.5px] font-semibold transition-colors ' + (
            titleOpen
              ? 'text-indigo-600 bg-indigo-50 dark:text-indigo-300 dark:bg-indigo-500/15'
              : 'text-slate-400 hover:text-indigo-600 hover:bg-indigo-50/60 dark:text-white/35 dark:hover:text-indigo-300 dark:hover:bg-indigo-500/10'
          )}
        >
          <ChevronsUpDown size={11} strokeWidth={2} />
          <span className="hidden sm:inline">Başlık</span>
        </button>

        {/* Gorunurluk */}
        <SwitchToggle
          checked={item.visible}
          disabled={!canEdit || item.locked}
          ariaLabel={labelText + ' kartta görünsün'}
          describedBy={item.locked ? lockedNoteId : null}
          onChange={function () { props.onToggleVisible(item.key) }}
          onClass="bg-emerald-500"
          offClass="bg-rose-400 dark:bg-rose-500/60"
        />
      </div>

      {item.locked && (
        <div id={lockedNoteId} className="px-2 pb-1.5 -mt-1 text-[10.5px] text-rose-500/80 dark:text-rose-300/70">
          Zorunlu alan — gizlenemez.
        </div>
      )}

      {titleOpen && (
        <div className="px-2.5 pb-2.5 pt-1 border-t border-slate-100 dark:border-white/[0.06]">
          <div className="text-[10.5px] text-slate-400 dark:text-white/35 mb-2">{titleSummary(item)}</div>
          <TitleOverridePanel item={item} canEdit={canEdit} onPatch={props.onPatch} />
        </div>
      )}
    </div>
  )
}
