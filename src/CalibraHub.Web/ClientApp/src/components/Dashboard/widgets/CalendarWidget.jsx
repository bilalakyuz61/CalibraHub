/**
 * CalendarWidget — Premium ERP takvim (Tactile baskın + Editorial tipografi + minimal Glass).
 * Tasarım sentezi (2026-06-15):
 *   - Linear paleti, 1px sınır, 8px köşe, tabular-nums gün numarası
 *   - Editorial weight hiyerarşisi (gün 13/500, hafta günü 10/600 uppercase)
 *   - Sol 3px accent bar + soft fill event chip
 *   - Bugün: dolu yuvarlatılmış kare (indigo fill) + minimal halo
 *   - Sol panel: pill toggle (dot + label + count)
 *   - Sağ panel: timeline (sol dikey çizgi + mono saat damgaları + "şu an" çizgisi)
 *   - Klavye: ↑↓←→ (gün gez), T (bugün), M/W (ay/hafta)
 *   - Microinteractions: 150ms ease-out, transform yok (snappy)
 *
 * Props: { size, height, settings, isDark, lang, editMode, onSettingsChange, fullPage }
 *   fullPage=true → sol kaynak paneli + orta ızgara + sağ gün detay paneli
 *   fullPage=false (dashboard widget) → sadece takvim ızgarası
 */
import { useState, useEffect, useMemo, useRef } from 'react'
import { ChevronLeft, ChevronRight, Plus, X, CalendarOff } from 'lucide-react'
import { createPortal } from 'react-dom'
import CalendarEventModal from './CalendarEventModal'

var MONTHS_TR = ['Ocak','Şubat','Mart','Nisan','Mayıs','Haziran',
                 'Temmuz','Ağustos','Eylül','Ekim','Kasım','Aralık']
var DAYS_SHORT   = ['Pzt','Sal','Çar','Per','Cum','Cmt','Paz']
var DAYS_LONG_TR = ['Pazar','Pazartesi','Salı','Çarşamba','Perşembe','Cuma','Cumartesi']

var SOURCES = [
  { id: 'personal',   label: 'Kişisel',       hex: '#6366f1', key: 'indigo'  },
  { id: 'work-order', label: 'İş Emirleri',   hex: '#f59e0b', key: 'amber'   },
  { id: 'birthday',   label: 'Doğum Günleri', hex: '#10b981', key: 'emerald' },
]

// Sentez paleti: Linear ramp + premium ERP tonu. Light & dark için her kaynak
// {bar, fill, text} üçlüsü. Sol 3px accent bar bar rengini, gövde fill rengini,
// metin text rengini alır.
var SOURCE_PALETTE = {
  light: {
    indigo:  { bar: '#6366f1', fill: 'rgba(99,102,241,0.10)',  text: '#3730a3' },
    amber:   { bar: '#f59e0b', fill: 'rgba(245,158,11,0.11)',  text: '#92400e' },
    emerald: { bar: '#10b981', fill: 'rgba(16,185,129,0.11)',  text: '#065f46' },
    rose:    { bar: '#f43f5e', fill: 'rgba(244,63,94,0.10)',   text: '#9f1239' },
    blue:    { bar: '#3b82f6', fill: 'rgba(59,130,246,0.10)',  text: '#1d4ed8' },
    violet:  { bar: '#8b5cf6', fill: 'rgba(139,92,246,0.10)',  text: '#5b21b6' },
    slate:   { bar: '#64748b', fill: 'rgba(100,116,139,0.10)', text: '#334155' },
  },
  dark: {
    indigo:  { bar: '#818cf8', fill: 'rgba(99,102,241,0.18)',  text: '#c7d2fe' },
    amber:   { bar: '#fbbf24', fill: 'rgba(245,158,11,0.18)',  text: '#fcd34d' },
    emerald: { bar: '#34d399', fill: 'rgba(16,185,129,0.18)',  text: '#a7f3d0' },
    rose:    { bar: '#fb7185', fill: 'rgba(244,63,94,0.18)',   text: '#fecdd3' },
    blue:    { bar: '#60a5fa', fill: 'rgba(59,130,246,0.18)',  text: '#bfdbfe' },
    violet:  { bar: '#a78bfa', fill: 'rgba(139,92,246,0.18)',  text: '#ddd6fe' },
    slate:   { bar: '#94a3b8', fill: 'rgba(100,116,139,0.18)', text: '#cbd5e1' },
  },
}

// Surface ramp — app CSS değişkenlerine bağlı; tema + aksan değişince otomatik uyarlanır.
// Accent (indigo) takvime özgü etkileşim rengidir, app aksan rengiyle birebir eşleşmesi şart değil.
function calTokens(isDark) {
  return {
    surface:      'var(--app-surface)',
    cell:         'var(--app-surface)',
    cellHover:    'var(--app-muted-surface)',
    cellToday:    isDark ? 'rgba(99,102,241,0.12)' : 'rgba(99,102,241,0.06)',
    cellSelected: isDark ? 'rgba(99,102,241,0.22)' : 'rgba(99,102,241,0.10)',
    cellOther:    'var(--app-content-bg)',
    headerBg:     'var(--app-muted-surface)',
    border:       'var(--app-border)',
    borderStrong: 'var(--app-border)',
    text:         'var(--app-text)',
    textMuted:    'var(--app-text-muted)',
    textFaint:    isDark ? '#64748b' : '#94a3b8',
    accent:       isDark ? '#818cf8' : '#4f46e5',
    accentFill:   isDark ? '#6366f1' : '#4f46e5',
    accentHalo:   isDark ? 'rgba(99,102,241,0.32)' : 'rgba(79,70,229,0.18)',
    nowLine:      isDark ? '#fb7185' : '#e11d48',
  }
}

// Tipografi tokens — editorial weight hiyerarşisi
var TYPO = {
  tabular:  { fontFeatureSettings: '"tnum" 1, "lnum" 1', fontVariantNumeric: 'tabular-nums' },
  mono:     { fontFamily: 'ui-monospace, Menlo, Consolas, monospace', fontVariantNumeric: 'tabular-nums' },
}

function pad(n) { return n < 10 ? '0' + n : '' + n }
function dateStr(d) {
  return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate())
}

// ISO 8601 hafta numarası — Pazartesi başlangıç
function getISOWeek(date) {
  var d = new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()))
  var dayNum = d.getUTCDay() || 7
  d.setUTCDate(d.getUTCDate() + 4 - dayNum)
  var yearStart = new Date(Date.UTC(d.getUTCFullYear(), 0, 1))
  return Math.ceil((((d.getTime() - yearStart.getTime()) / 86400000) + 1) / 7)
}

function getMonthCells(year, month) {
  var first = new Date(year, month, 1)
  var dow = first.getDay()
  var offset = dow === 0 ? 6 : dow - 1
  var cells = []
  var d = new Date(year, month, 1 - offset)
  for (var i = 0; i < 42; i++) { cells.push(new Date(d.getTime())); d.setDate(d.getDate() + 1) }
  return cells
}

function getWeekCells(anchor) {
  var d = new Date(anchor.getTime())
  var dow = d.getDay()
  var offset = dow === 0 ? 6 : dow - 1
  d.setDate(d.getDate() - offset)
  var cells = []
  for (var i = 0; i < 7; i++) { cells.push(new Date(d.getTime())); d.setDate(d.getDate() + 1) }
  return cells
}

function eventsForDay(events, ds) {
  return events.filter(function(e) {
    var s = e.startDate ? e.startDate.substring(0, 10) : ''
    var end = e.endDate ? e.endDate.substring(0, 10) : s
    return ds >= s && ds <= end
  })
}

function readCsrf() {
  var cfg = window.__CALIBRA_SHELL_CONFIG__
  if (cfg && cfg.antiforgeryToken) return cfg.antiforgeryToken
  var el = document.querySelector('input[name="__RequestVerificationToken"]')
  return el ? el.value : ''
}

// Etkinlik kaynağına göre palette key
function sourcePaletteKey(ev) {
  if (ev.color) return ev.color
  if (ev.source === 'birthday')   return 'emerald'
  if (ev.source === 'work-order') return 'amber'
  if (ev.source === 'holiday')    return 'rose'
  return 'indigo'
}

// HH:MM → minute (00:00 = 0)
function timeToMin(t) {
  if (!t || typeof t !== 'string') return null
  var parts = t.split(':')
  if (parts.length < 2) return null
  var h = parseInt(parts[0], 10), m = parseInt(parts[1], 10)
  if (isNaN(h) || isNaN(m)) return null
  return h * 60 + m
}

export default function CalendarWidget(props) {
  var isDark   = !!props.isDark
  var fullPage = !!props.fullPage
  var t        = calTokens(isDark)
  var palette  = isDark ? SOURCE_PALETTE.dark : SOURCE_PALETTE.light

  var now      = new Date()
  var todayStr = dateStr(now)

  var [view,        setView]        = useState('month')
  var [year,        setYear]        = useState(now.getFullYear())
  var [month,       setMonth]       = useState(now.getMonth())
  var [weekAnchor,  setWeekAnchor]  = useState(function() { return new Date(now.getTime()) })
  var [events,      setEvents]      = useState([])
  var [loading,     setLoading]     = useState(false)
  var [modal,       setModal]       = useState(null)
  var [tick,        setTick]        = useState(0)
  var [selectedDay, setSelectedDay] = useState(null)
  var [pickerOpen,  setPickerOpen]  = useState(false)
  var lastWheelRef = useRef(0)
  var [activeSources, setActiveSources] = useState({
    personal: true, 'work-order': true, birthday: true,
  })

  var cells = view === 'month' ? getMonthCells(year, month) : getWeekCells(weekAnchor)
  var start  = dateStr(cells[0])
  var end    = dateStr(cells[cells.length - 1])

  useEffect(function() {
    setLoading(true)
    fetch('/Calendar/Events?start=' + start + '&end=' + end)
      .then(function(r) { return r.ok ? r.json() : { events: [] } })
      .then(function(d) { setEvents(Array.isArray(d.events) ? d.events : []) })
      .catch(function() { setEvents([]) })
      .finally(function() { setLoading(false) })
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [start, end, tick])

  function refresh() { setTick(function(t) { return t + 1 }) }

  var filteredEvents = useMemo(function() {
    return events.filter(function(e) { return activeSources[e.source] !== false })
  }, [events, activeSources])

  // Kaynak başına event count (sol panel rozet)
  var sourceCounts = useMemo(function() {
    var c = { personal: 0, 'work-order': 0, birthday: 0 }
    events.forEach(function(e) {
      if (c[e.source] !== undefined) c[e.source]++
    })
    return c
  }, [events])

  function toggleSource(id) {
    setActiveSources(function(prev) {
      var next = Object.assign({}, prev)
      next[id] = prev[id] === false
      return next
    })
  }

  function prevPeriod() {
    if (view === 'month') {
      if (month === 0) { setYear(function(y) { return y - 1 }); setMonth(11) }
      else setMonth(function(m) { return m - 1 })
    } else {
      setWeekAnchor(function(a) { var d = new Date(a); d.setDate(d.getDate() - 7); return d })
    }
  }
  function nextPeriod() {
    if (view === 'month') {
      if (month === 11) { setYear(function(y) { return y + 1 }); setMonth(0) }
      else setMonth(function(m) { return m + 1 })
    } else {
      setWeekAnchor(function(a) { var d = new Date(a); d.setDate(d.getDate() + 7); return d })
    }
  }
  function goToday() {
    var n = new Date()
    setYear(n.getFullYear()); setMonth(n.getMonth()); setWeekAnchor(new Date(n.getTime()))
  }

  // Klavye nav — modal/input açıkken devre dışı
  useEffect(function() {
    function onKey(e) {
      if (modal !== null) return
      var tag = (e.target && e.target.tagName) || ''
      if (tag === 'INPUT' || tag === 'TEXTAREA' || (e.target && e.target.isContentEditable)) return
      var k = e.key
      if (k === 'Escape') { if (pickerOpen) { e.preventDefault(); setPickerOpen(false) }; return }
      if (k === 't' || k === 'T') { e.preventDefault(); goToday() }
      else if (k === 'm' || k === 'M') { e.preventDefault(); setView('month') }
      else if (k === 'w' || k === 'W') { e.preventDefault(); setView('week') }
      else if (k === 'ArrowLeft')  { e.preventDefault(); prevPeriod() }
      else if (k === 'ArrowRight') { e.preventDefault(); nextPeriod() }
      else if ((k === 'ArrowUp' || k === 'ArrowDown') && fullPage && selectedDay) {
        e.preventDefault()
        var d = new Date(selectedDay + 'T00:00:00')
        d.setDate(d.getDate() + (k === 'ArrowUp' ? -7 : 7))
        setSelectedDay(dateStr(d))
      }
    }
    window.addEventListener('keydown', onKey)
    return function() { window.removeEventListener('keydown', onKey) }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [modal, fullPage, selectedDay, view, pickerOpen])

  function headerLabel() {
    if (view === 'month') return MONTHS_TR[month] + ' ' + year
    var d0 = cells[0], d6 = cells[6]
    var wn = getISOWeek(d0)
    if (d0.getMonth() === d6.getMonth())
      return 'Hf.' + wn + ' · ' + d0.getDate() + '–' + d6.getDate() + ' ' + MONTHS_TR[d0.getMonth()] + ' ' + d0.getFullYear()
    return 'Hf.' + wn + ' · ' + d0.getDate() + ' ' + MONTHS_TR[d0.getMonth()] + ' – ' +
           d6.getDate() + ' ' + MONTHS_TR[d6.getMonth()]
  }

  async function handleSave(data) {
    var r = await fetch('/Calendar/SaveEvent', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': readCsrf() },
      body: JSON.stringify(data),
    })
    var json = await r.json()
    if (!json.ok) throw new Error(json.error || 'Kayıt başarısız')
    setModal(null); refresh()
  }

  async function handleDelete(id) {
    var r = await fetch('/Calendar/DeleteEvent', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': readCsrf() },
      body: JSON.stringify({ id: id }),
    })
    var json = await r.json()
    if (json.ok) { setModal(null); refresh() }
  }

  function handleDayClick(ds) {
    if (fullPage) setSelectedDay(ds)
    else setModal({ date: ds })
  }
  function handleGridEventClick(ev, ds) {
    if (fullPage) setSelectedDay(ds)
    else if (ev.source === 'personal') setModal({ event: ev })
  }

  // Mouse wheel ile ay/hafta navigasyonu — throttle: 350ms
  function handleWheel(e) {
    var ts = Date.now()
    if (ts - lastWheelRef.current < 350) return
    lastWheelRef.current = ts
    if (e.deltaY > 0) nextPeriod()
    else if (e.deltaY < 0) prevPeriod()
  }

  // Tek tip "küçük buton" stili — header chevron / today / view-segment için
  function iconBtn(active) {
    return {
      display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
      width: 26, height: 26, borderRadius: 6,
      border: '1px solid ' + (active ? t.accent : 'transparent'),
      background: active ? t.cellSelected : 'transparent',
      color: active ? t.accent : t.textMuted,
      cursor: 'pointer', transition: 'all 150ms ease-out',
    }
  }

  return (
    <div style={{
      display: 'flex', flexDirection: 'column', height: '100%', userSelect: 'none',
      color: t.text, background: fullPage ? t.surface : 'transparent',
      borderRadius: fullPage ? 12 : 0,
    }}>

      {/* ── Header ─────────────────────────────────────────────────────── */}
      <div style={{
        display: 'flex', alignItems: 'center', gap: 6, flexShrink: 0,
        padding: fullPage ? '10px 14px' : '0 0 8px', minHeight: 36,
      }}>
        {/* Spacer — tüm kontroller sağa */}
        <div style={{ flex: '1 1 auto' }} />

        {/* ← Ay Yıl → — oklar ve etiket yan yana */}
        <div style={{ display: 'flex', alignItems: 'center', gap: 1, position: 'relative' }}>
          <button type="button" onClick={prevPeriod} style={iconBtn(false)} title="Önceki (←)">
            <ChevronLeft size={14} strokeWidth={2.2} />
          </button>

          <button
            type="button"
            onClick={function() { setPickerOpen(function(o) { return !o }) }}
            style={{
              padding: '4px 10px', borderRadius: 6,
              border: '1px solid ' + (pickerOpen ? t.borderStrong : 'transparent'),
              background: pickerOpen ? t.cellHover : 'transparent',
              color: t.text, cursor: 'pointer',
              fontSize: 14, fontWeight: 600, letterSpacing: '-0.01em',
              opacity: loading ? 0.55 : 1, transition: 'all 150ms ease-out',
              whiteSpace: 'nowrap',
            }}
            title="Dönem seç"
          >
            {headerLabel()}
          </button>

          <button type="button" onClick={nextPeriod} style={iconBtn(false)} title="Sonraki (→)">
            <ChevronRight size={14} strokeWidth={2.2} />
          </button>

          {pickerOpen && (
            <PeriodPicker
              year={year} month={month} tokens={t}
              onSelect={function(y, m) { setYear(y); setMonth(m); setView('month'); setPickerOpen(false) }}
              onClose={function() { setPickerOpen(false) }}
            />
          )}
        </div>

        <button type="button" onClick={goToday} style={{
          padding: '5px 10px', borderRadius: 6, marginLeft: 4,
          border: '1px solid ' + t.border, background: t.cell, color: t.text,
          fontSize: 11, fontWeight: 600, cursor: 'pointer', transition: 'all 150ms ease-out',
          letterSpacing: '0.02em',
        }} title="Bugün (T)">
          Bugün
          <span style={{
            marginLeft: 6, padding: '1px 4px', borderRadius: 3,
            background: t.border, color: t.textFaint, fontSize: 9, fontFamily: TYPO.mono.fontFamily,
          }}>T</span>
        </button>

        <div style={{
          display: 'flex', gap: 1, marginLeft: 4, flexShrink: 0,
          padding: 2, background: t.headerBg, border: '1px solid ' + t.border, borderRadius: 7,
        }}>
          {['month','week'].map(function(v) {
            var active = view === v
            return (
              <button key={v} type="button" onClick={function() { setView(v) }} style={{
                padding: '3px 10px', borderRadius: 5, fontSize: 11, fontWeight: 600,
                border: 'none', cursor: 'pointer', transition: 'all 150ms ease-out',
                background: active ? t.cell : 'transparent',
                color:      active ? t.text : t.textMuted,
                boxShadow:  active ? '0 1px 2px rgba(0,0,0,0.08), 0 0 0 1px ' + t.border : 'none',
                letterSpacing: '0.02em',
              }}
                title={v === 'month' ? 'Ay görünümü (M)' : 'Hafta görünümü (W)'}>
                {v === 'month' ? 'Ay' : 'Hafta'}
              </button>
            )
          })}
        </div>
      </div>

      {/* ── Gövde: 3 kolon (fullPage) veya tek kolon (dashboard) ─────── */}
      <div style={{ display: 'flex', flex: '1 1 auto', minHeight: 0, overflow: 'hidden' }}>

        {fullPage && (
          <SourcePanel
            activeSources={activeSources}
            onToggle={toggleSource}
            tokens={t}
            palette={palette}
            counts={sourceCounts}
          />
        )}

        <div
          onWheel={handleWheel}
          style={{
            flex: '1 1 auto', display: 'flex', flexDirection: 'column', minHeight: 0,
            padding: fullPage ? '0 12px 12px' : '0', overflow: 'hidden',
          }}
        >
          {view === 'month'
            ? <MonthGrid
                cells={cells} curMonth={month} todayStr={todayStr} selectedDay={selectedDay}
                events={filteredEvents} palette={palette} tokens={t} fullPage={fullPage}
                onDayClick={handleDayClick}
                onEventClick={handleGridEventClick}
              />
            : <WeekGrid
                cells={cells} todayStr={todayStr} selectedDay={selectedDay}
                events={filteredEvents} palette={palette} tokens={t} fullPage={fullPage}
                onDayClick={handleDayClick}
                onEventClick={handleGridEventClick}
              />
          }
        </div>

        {fullPage && selectedDay && (
          <DayPanel
            day={selectedDay}
            events={eventsForDay(filteredEvents, selectedDay)}
            todayStr={todayStr}
            tokens={t}
            palette={palette}
            onClose={function() { setSelectedDay(null) }}
            onNewEvent={function() { setModal({ date: selectedDay }) }}
            onEventClick={function(ev) { setModal({ event: ev }) }}
          />
        )}
      </div>

      {modal !== null && createPortal(
        <CalendarEventModal
          open={true}
          initialDate={modal.date || null}
          event={modal.event || null}
          onSave={handleSave}
          onDelete={modal.event && modal.event.source === 'personal' ? handleDelete : null}
          onClose={function() { setModal(null) }}
        />,
        document.body
      )}
    </div>
  )
}

/* ════════════════════════════════════════════════════════════════════
   SourcePanel — pill toggle: dot + label + count
   ════════════════════════════════════════════════════════════════════ */
function SourcePanel({ activeSources, onToggle, tokens, palette, counts }) {
  return (
    <div style={{
      width: 200, flexShrink: 0, borderRight: '1px solid ' + tokens.border,
      padding: '6px 10px 12px', display: 'flex', flexDirection: 'column', gap: 4,
      overflowY: 'auto',
    }}>
      <div style={Object.assign({
        fontSize: 10, fontWeight: 700, color: tokens.textFaint,
        textTransform: 'uppercase', letterSpacing: '0.08em', padding: '8px 6px 6px',
      }, TYPO.tabular)}>
        Kaynaklar
      </div>
      {SOURCES.map(function(src) {
        var active = activeSources[src.id] !== false
        var col = palette[src.key]
        var count = counts[src.id] || 0
        return (
          <button
            key={src.id}
            type="button"
            onClick={function() { onToggle(src.id) }}
            style={{
              display: 'flex', alignItems: 'center', gap: 9, padding: '7px 9px',
              borderRadius: 6, cursor: 'pointer', width: '100%', textAlign: 'left',
              transition: 'all 150ms ease-out',
              background: active ? col.fill : 'transparent',
              border: '1px solid ' + (active ? col.bar + '40' : 'transparent'),
              color: active ? col.text : tokens.textMuted,
            }}
            onMouseEnter={function(e) {
              if (!active) e.currentTarget.style.background = tokens.cellHover
            }}
            onMouseLeave={function(e) {
              if (!active) e.currentTarget.style.background = 'transparent'
            }}
            title={(active ? 'Gizle' : 'Göster') + ': ' + src.label}
          >
            <span style={{
              width: 9, height: 9, borderRadius: '50%', flexShrink: 0,
              background: active ? col.bar : 'transparent',
              border: '2px solid ' + col.bar,
              boxSizing: 'border-box',
              transition: 'background 150ms ease-out',
            }} />
            <span style={{
              flex: '1 1 auto', fontSize: 12.5, fontWeight: 500,
            }}>
              {src.label}
            </span>
            <span style={Object.assign({
              fontSize: 10, fontWeight: 600,
              padding: '1px 5px', borderRadius: 4,
              background: active ? 'rgba(255,255,255,0.18)' : tokens.border,
              color: active ? col.text : tokens.textFaint,
              minWidth: 18, textAlign: 'center',
            }, TYPO.tabular)}>
              {count}
            </span>
          </button>
        )
      })}
    </div>
  )
}

/* ════════════════════════════════════════════════════════════════════
   MonthGrid — 1px gridlines, tabular-nums, dolu kare bugün, accent bar chip
   ════════════════════════════════════════════════════════════════════ */
function MonthGrid(props) {
  var cells = props.cells, curMonth = props.curMonth, todayStr = props.todayStr
  var selectedDay = props.selectedDay, t = props.tokens, palette = props.palette
  var events = props.events, fullPage = props.fullPage
  var onDayClick = props.onDayClick, onEventClick = props.onEventClick

  // grid: 36px hafta + 7 × gün
  var items = []

  items.push(<div key="hf-head" style={{
    background: t.headerBg, display: 'flex', alignItems: 'center', justifyContent: 'center',
    fontSize: 10, fontWeight: 700, color: t.textFaint,
    letterSpacing: '0.06em', textTransform: 'uppercase',
  }}>Hf</div>)
  DAYS_SHORT.forEach(function(d, i) {
    var isWeekend = i >= 5
    items.push(<div key={'dh-' + d} style={{
      background: t.headerBg, display: 'flex', alignItems: 'center', justifyContent: 'center',
      fontSize: 10, fontWeight: 700,
      color: isWeekend ? t.textMuted : t.text,
      textTransform: 'uppercase', letterSpacing: '0.06em',
    }}>{d}</div>)
  })

  cells.reduce(function(acc, day, idx) {
    var ds = dateStr(day)
    var colIdx = idx % 7
    var rowIdx = Math.floor(idx / 7)

    if (colIdx === 0) {
      items.push(<div key={'wn-' + rowIdx} style={Object.assign({
        background: t.headerBg, display: 'flex', alignItems: 'center', justifyContent: 'center',
        fontSize: 10, fontWeight: 600, color: t.textFaint,
      }, TYPO.tabular)}>{getISOWeek(day)}</div>)
    }

    var isToday = ds === todayStr
    var isSelected = fullPage && ds === selectedDay
    var otherMonth = day.getMonth() !== curMonth
    var isWeekend = colIdx >= 5
    var dayEvs = eventsForDay(events, ds)
    var maxShown = fullPage ? 3 : 2
    var extra = Math.max(0, dayEvs.length - maxShown)
    var bg = isSelected ? t.cellSelected : (isToday ? t.cellToday : (otherMonth ? t.cellOther : t.cell))

    items.push(
      <div
        key={ds}
        onClick={function() { onDayClick(ds) }}
        style={{
          background: bg, padding: '6px 7px 4px', cursor: 'pointer',
          display: 'flex', flexDirection: 'column', gap: 3, overflow: 'hidden',
          transition: 'background 150ms ease-out',
          opacity: otherMonth ? 0.5 : 1,
          position: 'relative',
        }}
        onMouseEnter={function(e) { if (!isSelected) e.currentTarget.style.background = t.cellHover }}
        onMouseLeave={function(e) { if (!isSelected) e.currentTarget.style.background = bg }}
      >
        {isSelected && (
          <span style={{
            position: 'absolute', left: 0, top: 0, bottom: 0, width: 2,
            background: t.accent,
          }} />
        )}
        <div style={{
          display: 'flex', alignItems: 'center', justifyContent: 'space-between',
          minHeight: 22, flexShrink: 0,
        }}>
          {isToday ? (
            <span style={Object.assign({
              display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
              width: 22, height: 22, borderRadius: 5,
              background: t.accentFill, color: '#fff',
              fontSize: 12, fontWeight: 700,
              boxShadow: '0 0 0 3px ' + t.accentHalo,
            }, TYPO.tabular)}>
              {day.getDate()}
            </span>
          ) : (
            <span style={Object.assign({
              fontSize: 12.5, fontWeight: 500,
              color: isWeekend ? t.textMuted : t.text,
              padding: '0 4px',
            }, TYPO.tabular)}>
              {day.getDate()}
            </span>
          )}
        </div>
        {dayEvs.slice(0, maxShown).map(function(ev) {
          var col = palette[sourcePaletteKey(ev)] || palette.indigo
          return (
            <div
              key={ev.id + ':' + ev.source}
              onClick={function(e) {
                if (!fullPage) { e.stopPropagation(); if (ev.source === 'personal') onEventClick(ev, ds) }
              }}
              title={ev.title + (ev.startTime ? ' · ' + ev.startTime : '')}
              style={{
                position: 'relative',
                fontSize: 10.5, fontWeight: 500, padding: '2px 6px 2px 9px',
                borderRadius: 3, background: col.fill, color: col.text,
                whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis',
                cursor: (!fullPage && ev.source === 'personal') ? 'pointer' : 'default',
                flexShrink: 0, lineHeight: 1.35,
              }}
            >
              <span style={{
                position: 'absolute', left: 0, top: 2, bottom: 2, width: 3,
                background: col.bar, borderRadius: '0 0 0 0',
              }} />
              {ev.title}
            </div>
          )
        })}
        {extra > 0 && (
          <div style={Object.assign({
            fontSize: 9.5, fontWeight: 600, color: t.textMuted,
            paddingLeft: 4, flexShrink: 0, letterSpacing: '0.01em',
          }, TYPO.tabular)}>
            +{extra} daha
          </div>
        )}
      </div>
    )
    return acc
  }, null)

  return (
    <div style={{
      flex: '1 1 auto', minHeight: 0, overflow: 'hidden',
      display: 'grid',
      gridTemplateColumns: '32px repeat(7, 1fr)',
      gridTemplateRows: '28px repeat(6, 1fr)',
      background: t.border, gap: 1,
      border: '1px solid ' + t.border, borderRadius: 8,
    }}>
      {items}
    </div>
  )
}

/* ════════════════════════════════════════════════════════════════════
   WeekGrid — 7 sütun, gün adı + dolu kare bugün + event listesi
   ════════════════════════════════════════════════════════════════════ */
function WeekGrid(props) {
  var cells = props.cells, todayStr = props.todayStr, selectedDay = props.selectedDay
  var t = props.tokens, palette = props.palette
  var events = props.events, fullPage = props.fullPage
  var onDayClick = props.onDayClick, onEventClick = props.onEventClick

  return (
    <div style={{
      flex: '1 1 auto', minHeight: 0, overflow: 'hidden',
      display: 'grid', gridTemplateColumns: 'repeat(7, 1fr)',
      background: t.border, gap: 1,
      border: '1px solid ' + t.border, borderRadius: 8,
    }}>
      {cells.map(function(day, colIdx) {
        var ds = dateStr(day)
        var isToday = ds === todayStr
        var isSelected = fullPage && ds === selectedDay
        var isWeekend = colIdx >= 5
        var dayEvs = eventsForDay(events, ds)
        var bg = isSelected ? t.cellSelected : (isToday ? t.cellToday : t.cell)

        return (
          <div
            key={ds}
            onClick={function() { onDayClick(ds) }}
            style={{
              background: bg, display: 'flex', flexDirection: 'column',
              padding: '8px 5px', cursor: 'pointer', overflow: 'hidden',
              transition: 'background 150ms ease-out',
              position: 'relative',
            }}
            onMouseEnter={function(e) { if (!isSelected) e.currentTarget.style.background = t.cellHover }}
            onMouseLeave={function(e) { if (!isSelected) e.currentTarget.style.background = bg }}
          >
            {isSelected && (
              <span style={{
                position: 'absolute', left: 0, top: 0, bottom: 0, width: 2,
                background: t.accent,
              }} />
            )}
            <div style={{
              display: 'flex', flexDirection: 'column', alignItems: 'center',
              flexShrink: 0, gap: 4, marginBottom: 6,
            }}>
              <span style={{
                fontSize: 10, fontWeight: 700,
                color: isWeekend ? t.textMuted : t.textFaint,
                textTransform: 'uppercase', letterSpacing: '0.06em',
              }}>
                {DAYS_SHORT[colIdx]}
              </span>
              {isToday ? (
                <span style={Object.assign({
                  display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
                  width: 26, height: 26, borderRadius: 6,
                  background: t.accentFill, color: '#fff',
                  fontSize: 13, fontWeight: 700,
                  boxShadow: '0 0 0 3px ' + t.accentHalo,
                }, TYPO.tabular)}>
                  {day.getDate()}
                </span>
              ) : (
                <span style={Object.assign({
                  fontSize: 14, fontWeight: 600,
                  color: isWeekend ? t.textMuted : t.text,
                }, TYPO.tabular)}>
                  {day.getDate()}
                </span>
              )}
            </div>
            <div style={{
              flex: '1 1 auto', display: 'flex', flexDirection: 'column',
              gap: 3, overflow: 'hidden',
            }}>
              {dayEvs.map(function(ev) {
                var col = palette[sourcePaletteKey(ev)] || palette.indigo
                return (
                  <div
                    key={ev.id + ':' + ev.source}
                    onClick={function(e) {
                      if (!fullPage) { e.stopPropagation(); if (ev.source === 'personal') onEventClick(ev, ds) }
                    }}
                    title={ev.title}
                    style={{
                      position: 'relative',
                      fontSize: 10.5, fontWeight: 500, padding: '3px 6px 3px 9px',
                      borderRadius: 4, background: col.fill, color: col.text,
                      whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis',
                      cursor: (!fullPage && ev.source === 'personal') ? 'pointer' : 'default',
                      flexShrink: 0,
                    }}
                  >
                    <span style={{
                      position: 'absolute', left: 0, top: 3, bottom: 3, width: 3,
                      background: col.bar, borderRadius: 2,
                    }} />
                    {ev.title}
                  </div>
                )
              })}
            </div>
          </div>
        )
      })}
    </div>
  )
}

/* ════════════════════════════════════════════════════════════════════
   DayPanel — timeline (sol dikey çizgi + mono saat + "şu an" çizgisi)
   ════════════════════════════════════════════════════════════════════ */
var SRC_LABELS = { personal: 'Kişisel', 'work-order': 'İş Emri', birthday: 'Doğum Günü' }
var HOUR_PX = 44   // her saat 44px yükseklik
var DAY_START_H = 7  // 07:00'den başla
var DAY_END_H   = 21 // 21:00'de bit (15 saat × 44 = 660px)

function DayPanel({ day, events, todayStr, tokens, palette, onClose, onNewEvent, onEventClick }) {
  var t = tokens
  var d = new Date(day + 'T00:00:00')
  var dayName  = DAYS_LONG_TR[d.getDay()]
  var dateLabel = d.getDate() + ' ' + MONTHS_TR[d.getMonth()] + ' ' + d.getFullYear()
  var isToday  = day === todayStr

  var bodyRef = useRef(null)
  var [now, setNow] = useState(new Date())
  useEffect(function() {
    var id = setInterval(function() { setNow(new Date()) }, 60000)
    return function() { clearInterval(id) }
  }, [])

  // İlk açılışta "şu an" çizgisine scroll et (sadece bugün için)
  useEffect(function() {
    if (!isToday || !bodyRef.current) return
    var nowMin = now.getHours() * 60 + now.getMinutes()
    var topPx = ((nowMin - DAY_START_H * 60) / 60) * HOUR_PX
    bodyRef.current.scrollTop = Math.max(0, topPx - 80)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [day])

  // "Tüm gün" + zaman aralıklı eventler ayır
  var allDayEvents  = events.filter(function(e) { return e.isAllDay || !e.startTime })
  var timedEvents   = events.filter(function(e) { return !e.isAllDay && !!e.startTime })

  var hours = []
  for (var h = DAY_START_H; h <= DAY_END_H; h++) hours.push(h)

  var nowMin = now.getHours() * 60 + now.getMinutes()
  var nowTopPx = ((nowMin - DAY_START_H * 60) / 60) * HOUR_PX
  var nowVisible = isToday && nowMin >= DAY_START_H * 60 && nowMin <= DAY_END_H * 60

  return (
    <div style={{
      width: 340, flexShrink: 0, borderLeft: '1px solid ' + t.border,
      display: 'flex', flexDirection: 'column', overflow: 'hidden',
      background: t.surface,
    }}>
      {/* Header */}
      <div style={{
        display: 'flex', alignItems: 'flex-start', padding: '14px 16px 12px',
        borderBottom: '1px solid ' + t.border, flexShrink: 0, gap: 8,
      }}>
        <div style={{ flex: '1 1 auto' }}>
          <div style={{
            fontSize: 11, fontWeight: 600, color: t.textMuted,
            textTransform: 'uppercase', letterSpacing: '0.08em',
          }}>{dayName}</div>
          <div style={Object.assign({
            fontSize: 22, fontWeight: 600, color: t.text, marginTop: 2,
            letterSpacing: '-0.015em',
          }, TYPO.tabular)}>
            {dateLabel}
          </div>
        </div>
        <button type="button" onClick={onClose} title="Kapat (Esc)" style={{
          width: 28, height: 28, borderRadius: 6, border: '1px solid transparent',
          background: 'transparent', color: t.textMuted, cursor: 'pointer',
          display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
          transition: 'all 150ms ease-out', flexShrink: 0,
        }}
          onMouseEnter={function(e) { e.currentTarget.style.background = t.cellHover; e.currentTarget.style.color = t.text }}
          onMouseLeave={function(e) { e.currentTarget.style.background = 'transparent'; e.currentTarget.style.color = t.textMuted }}>
          <X size={14} />
        </button>
      </div>

      {/* Tüm gün etkinlikleri */}
      {allDayEvents.length > 0 && (
        <div style={{
          padding: '10px 16px', borderBottom: '1px solid ' + t.border, flexShrink: 0,
          display: 'flex', flexDirection: 'column', gap: 4,
        }}>
          <div style={{
            fontSize: 10, fontWeight: 700, color: t.textFaint,
            textTransform: 'uppercase', letterSpacing: '0.08em', marginBottom: 2,
          }}>Tüm gün</div>
          {allDayEvents.map(function(ev) {
            var col = palette[sourcePaletteKey(ev)] || palette.indigo
            var canEdit = ev.source === 'personal'
            return (
              <div key={ev.id + ':' + ev.source}
                onClick={canEdit ? function() { onEventClick(ev) } : undefined}
                style={{
                  position: 'relative', padding: '6px 10px 6px 12px', borderRadius: 5,
                  background: col.fill, color: col.text, cursor: canEdit ? 'pointer' : 'default',
                  fontSize: 12, fontWeight: 500,
                }}>
                <span style={{ position: 'absolute', left: 0, top: 5, bottom: 5, width: 3, background: col.bar, borderRadius: 2 }} />
                {ev.title}
              </div>
            )
          })}
        </div>
      )}

      {/* Timeline gövdesi */}
      <div ref={bodyRef} style={{
        flex: '1 1 auto', overflowY: 'auto', position: 'relative',
        padding: '8px 0 14px',
      }}>
        {events.length === 0 ? (
          <div style={{
            display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
            height: '100%', minHeight: 240, gap: 10, color: t.textMuted,
          }}>
            <CalendarOff size={28} strokeWidth={1.6} style={{ color: t.textFaint }} />
            <span style={{ fontSize: 12.5, fontWeight: 500 }}>Bu gün için etkinlik yok</span>
            <button type="button" onClick={onNewEvent} style={{
              padding: '6px 12px', borderRadius: 6,
              border: '1px solid ' + t.border, background: t.cell, color: t.text,
              fontSize: 11.5, fontWeight: 600, cursor: 'pointer',
              display: 'inline-flex', alignItems: 'center', gap: 5,
              transition: 'all 150ms ease-out',
            }}>
              <Plus size={12} /> Yeni etkinlik
            </button>
          </div>
        ) : (
          <div style={{ position: 'relative', height: (DAY_END_H - DAY_START_H + 1) * HOUR_PX + 8 }}>
            {/* Saat çizgileri */}
            {hours.map(function(h, i) {
              var topPx = i * HOUR_PX
              return (
                <div key={'h-' + h} style={{
                  position: 'absolute', left: 0, right: 0, top: topPx,
                  display: 'flex', alignItems: 'flex-start',
                }}>
                  <span style={Object.assign({
                    width: 52, flexShrink: 0, textAlign: 'right', paddingRight: 8,
                    fontSize: 10.5, color: t.textFaint, fontWeight: 500,
                    paddingTop: 0, lineHeight: 1,
                  }, TYPO.mono)}>
                    {pad(h)}:00
                  </span>
                  <div style={{
                    flex: '1 1 auto', height: 1,
                    background: t.border, marginRight: 12, marginTop: 5,
                  }} />
                </div>
              )
            })}

            {/* "Şu an" çizgisi */}
            {nowVisible && (
              <div style={{
                position: 'absolute', left: 0, right: 12, top: nowTopPx,
                display: 'flex', alignItems: 'center', pointerEvents: 'none', zIndex: 2,
              }}>
                <span style={Object.assign({
                  width: 52, flexShrink: 0, textAlign: 'right', paddingRight: 8,
                  fontSize: 10.5, color: t.nowLine, fontWeight: 700, lineHeight: 1,
                }, TYPO.mono)}>
                  {pad(now.getHours()) + ':' + pad(now.getMinutes())}
                </span>
                <div style={{
                  width: 8, height: 8, borderRadius: '50%', background: t.nowLine,
                  flexShrink: 0, boxShadow: '0 0 0 3px ' + t.nowLine + '33',
                }} />
                <div style={{ flex: '1 1 auto', height: 2, background: t.nowLine }} />
              </div>
            )}

            {/* Eventler — başlama saatine göre yatay başlar, süre × HOUR_PX yüksekliği */}
            {timedEvents.map(function(ev) {
              var col = palette[sourcePaletteKey(ev)] || palette.indigo
              var startMin = timeToMin(ev.startTime)
              var endMin   = timeToMin(ev.endTime) || (startMin + 60)
              if (startMin === null) return null
              var topPx    = ((startMin - DAY_START_H * 60) / 60) * HOUR_PX
              var heightPx = Math.max(28, ((endMin - startMin) / 60) * HOUR_PX - 2)
              var canEdit  = ev.source === 'personal'
              return (
                <div
                  key={ev.id + ':' + ev.source}
                  onClick={canEdit ? function() { onEventClick(ev) } : undefined}
                  style={{
                    position: 'absolute', left: 60, right: 12, top: topPx, height: heightPx,
                    borderRadius: 6, background: col.fill, color: col.text,
                    padding: '5px 8px 4px 11px', cursor: canEdit ? 'pointer' : 'default',
                    overflow: 'hidden', transition: 'box-shadow 150ms ease-out, background 150ms ease-out',
                    boxShadow: 'inset 0 0 0 1px ' + col.bar + '22',
                  }}
                  onMouseEnter={canEdit ? function(e) {
                    e.currentTarget.style.boxShadow = 'inset 0 0 0 1px ' + col.bar + '55, 0 2px 8px rgba(0,0,0,0.06)'
                  } : undefined}
                  onMouseLeave={canEdit ? function(e) {
                    e.currentTarget.style.boxShadow = 'inset 0 0 0 1px ' + col.bar + '22'
                  } : undefined}
                  title={ev.title + (ev.description ? '\n' + ev.description : '')}
                >
                  <span style={{ position: 'absolute', left: 0, top: 0, bottom: 0, width: 3, background: col.bar }} />
                  <div style={{
                    fontSize: 12, fontWeight: 600, color: col.text,
                    whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis',
                  }}>
                    {ev.title}
                  </div>
                  <div style={Object.assign({
                    fontSize: 10.5, color: col.text, opacity: 0.78, marginTop: 1,
                  }, TYPO.mono)}>
                    {ev.startTime}{ev.endTime ? ' – ' + ev.endTime : ''}
                  </div>
                  {heightPx > 56 && ev.description && (
                    <div style={{
                      fontSize: 11, color: col.text, opacity: 0.7, marginTop: 3, lineHeight: 1.4,
                      overflow: 'hidden',
                    }}>
                      {ev.description}
                    </div>
                  )}
                  {heightPx > 70 && (
                    <div style={{
                      position: 'absolute', bottom: 4, left: 11,
                      fontSize: 9, fontWeight: 700, color: col.text, opacity: 0.55,
                      textTransform: 'uppercase', letterSpacing: '0.06em',
                    }}>
                      {SRC_LABELS[ev.source] || ev.source}
                    </div>
                  )}
                </div>
              )
            })}
          </div>
        )}
      </div>

      {/* Footer — yeni etkinlik */}
      {events.length > 0 && (
        <div style={{ padding: '10px 16px', borderTop: '1px solid ' + t.border, flexShrink: 0 }}>
          <button type="button" onClick={onNewEvent} style={{
            width: '100%', padding: '8px 12px', borderRadius: 7,
            border: '1px solid ' + t.border, background: t.accentFill, color: '#fff',
            fontSize: 12, fontWeight: 600, cursor: 'pointer',
            display: 'inline-flex', alignItems: 'center', justifyContent: 'center', gap: 6,
            transition: 'filter 150ms ease-out',
          }}
            onMouseEnter={function(e) { e.currentTarget.style.filter = 'brightness(1.08)' }}
            onMouseLeave={function(e) { e.currentTarget.style.filter = 'brightness(1)' }}>
            <Plus size={13} /> Yeni etkinlik
          </button>
        </div>
      )}
    </div>
  )
}

/* ════════════════════════════════════════════════════════════════════
   PeriodPicker — yıl/ay seçim dropdown'u (← 2026 →  +  12 ay butonu)
   ════════════════════════════════════════════════════════════════════ */
var MONTHS_SHORT = ['Oca','Şub','Mar','Nis','May','Haz','Tem','Ağu','Eyl','Eki','Kas','Ara']

function PeriodPicker({ year, month, tokens, onSelect, onClose }) {
  var t = tokens
  var [pYear, setPYear] = useState(year)
  var ref = useRef(null)

  // Dışarı tıklanınca kapat
  useEffect(function() {
    function onDown(e) {
      if (ref.current && !ref.current.contains(e.target)) onClose()
    }
    document.addEventListener('mousedown', onDown)
    return function() { document.removeEventListener('mousedown', onDown) }
  }, [onClose])

  var navBtnSt = {
    display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
    width: 28, height: 28, borderRadius: 6,
    border: '1px solid ' + t.border, background: t.cellHover,
    color: t.textMuted, cursor: 'pointer', transition: 'all 120ms ease-out',
  }

  return (
    <div
      ref={ref}
      style={{
        position: 'absolute', top: 'calc(100% + 8px)', left: '50%',
        transform: 'translateX(-50%)',
        zIndex: 300,
        background: t.cell,
        border: '1px solid ' + t.borderStrong,
        borderRadius: 12,
        padding: '14px 16px 16px',
        minWidth: 240,
        boxShadow: '0 12px 32px rgba(0,0,0,0.22), 0 3px 8px rgba(0,0,0,0.12)',
      }}
    >
      {/* Yıl navigasyon */}
      <div style={{
        display: 'flex', alignItems: 'center', justifyContent: 'space-between',
        marginBottom: 12,
      }}>
        <button type="button" style={navBtnSt}
          onClick={function() { setPYear(function(y) { return y - 1 }) }}>
          <ChevronLeft size={14} strokeWidth={2.2} />
        </button>
        <span style={Object.assign({
          fontSize: 15, fontWeight: 700, color: t.text, letterSpacing: '-0.01em',
        }, TYPO.tabular)}>
          {pYear}
        </span>
        <button type="button" style={navBtnSt}
          onClick={function() { setPYear(function(y) { return y + 1 }) }}>
          <ChevronRight size={14} strokeWidth={2.2} />
        </button>
      </div>

      {/* 4 × 3 ay grid */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 4 }}>
        {MONTHS_SHORT.map(function(m, i) {
          var isSelected = pYear === year && i === month
          return (
            <button
              key={i}
              type="button"
              onClick={function() { onSelect(pYear, i) }}
              style={{
                padding: '8px 4px', borderRadius: 8, fontSize: 12, fontWeight: 600,
                cursor: 'pointer', textAlign: 'center', border: 'none',
                background: isSelected ? t.accentFill : 'transparent',
                color: isSelected ? '#fff' : t.text,
                boxShadow: isSelected ? '0 2px 8px ' + t.accentHalo : 'none',
                transition: 'all 120ms ease-out',
              }}
              onMouseEnter={function(e) {
                if (!isSelected) e.currentTarget.style.background = t.cellHover
              }}
              onMouseLeave={function(e) {
                if (!isSelected) e.currentTarget.style.background = 'transparent'
              }}
            >
              {m}
            </button>
          )
        })}
      </div>
    </div>
  )
}
