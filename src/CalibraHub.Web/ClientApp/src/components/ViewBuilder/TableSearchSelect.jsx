/**
 * TableSearchSelect — Görsel kurucuda tablo/view seçimi için type-ahead combobox.
 *
 * Şema listesi büyüdükçe düz <select> ile doğru tabloyu bulmak zorlaşıyordu.
 * Bu bileşen IntegrationWizard/WizardStep3Mapping.jsx içindeki `SearchableCombo`
 * ve EndpointUrlPicker (`.eup-menu`) deseninin aynısını taklit eder:
 *   - body-portal + fixed pozisyon (üst kapsayıcıların overflow:hidden/auto'sunu
 *     bypass eder — .vb-tab-content scroll alanı içinde kırpılmaz)
 *   - tıklayınca açılır, yazınca client-side filtrelenir
 *   - ArrowUp/Down + Enter klavye navigasyonu, Escape ile kapanır
 *
 * Menü document.body'e portallandığı için .vb-root'un DOM alt ağacı DIŞINDAdır —
 * --vb-* değişkenlerini miras alamaz. Bu yüzden .vb-tsel-menu (ViewBuilder.css)
 * aynı değişkenleri kendi üzerinde yeniden tanımlar (bkz. IntegrationWizard.css
 * .eup-menu yorumu — aynı kısıt, aynı çözüm; tek dark selector: body.app-theme-dark).
 *
 * Props:
 *   options   : [{ schema, name, kind }]   kind: 'table' | 'view'
 *   value     : "schema|name" ya da ''     (parent zaten bu formatı kullanıyor)
 *   onChange  : (value: string) => void
 *   disabled, placeholder
 */
import React, { useState, useEffect, useMemo, useRef, useCallback } from 'react'
import { createPortal } from 'react-dom'
import { ChevronDown, Search, X } from 'lucide-react'

export default function TableSearchSelect({ options, value, onChange, disabled, placeholder }) {
  var [open, setOpen] = useState(false)
  var [query, setQuery] = useState('')
  var [focusIdx, setFocusIdx] = useState(0)
  var [menuRect, setMenuRect] = useState(null)
  var wrapRef = useRef(null)
  var inputRef = useRef(null)

  var sorted = useMemo(function () {
    return (options || []).slice().sort(function (a, b) {
      var ka = a.schema + '.' + a.name, kb = b.schema + '.' + b.name
      return ka < kb ? -1 : ka > kb ? 1 : 0
    })
  }, [options])

  var selected = useMemo(function () {
    if (!value) return null
    return sorted.find(function (t) { return (t.schema + '|' + t.name) === value }) || null
  }, [sorted, value])

  var displayLabel = selected ? (selected.schema + '.' + selected.name) : ''
  var trimmed = query.trim().toLowerCase()
  var filtered = useMemo(function () {
    if (!trimmed) return sorted
    return sorted.filter(function (t) { return (t.schema + '.' + t.name).toLowerCase().indexOf(trimmed) !== -1 })
  }, [sorted, trimmed])

  // Açık iken pozisyonu input'un viewport koordinatına kilitle (position: fixed) —
  // .vb-tab-content'in overflow:auto'sunu bypass eder, menü kırpılmaz.
  var recomputeRect = useCallback(function () {
    if (!wrapRef.current) return
    var r = wrapRef.current.getBoundingClientRect()
    var spaceBelow = window.innerHeight - r.bottom
    var openUp = spaceBelow < 260 && r.top > 260
    setMenuRect({
      left: r.left,
      width: r.width,
      top: openUp ? undefined : (r.bottom + 4),
      bottom: openUp ? (window.innerHeight - r.top + 4) : undefined,
    })
  }, [])

  useEffect(function () {
    if (!open) { setMenuRect(null); return }
    recomputeRect()
    function onScroll() { recomputeRect() }
    function onResize() { recomputeRect() }
    window.addEventListener('scroll', onScroll, true)
    window.addEventListener('resize', onResize)
    return function () {
      window.removeEventListener('scroll', onScroll, true)
      window.removeEventListener('resize', onResize)
    }
  }, [open, recomputeRect])

  // Dışarıda tıklama → kapat (input + portal menüyü de kapsar)
  useEffect(function () {
    if (!open) return
    function handler(e) {
      var inWrap = wrapRef.current && wrapRef.current.contains(e.target)
      var inMenu = e.target.closest && e.target.closest('[data-vb-tsel-menu]')
      if (!inWrap && !inMenu) setOpen(false)
    }
    document.addEventListener('mousedown', handler)
    return function () { document.removeEventListener('mousedown', handler) }
  }, [open])

  useEffect(function () { setFocusIdx(0) }, [trimmed, open])

  function openMenu() {
    if (disabled) return
    setQuery('')
    setOpen(true)
  }
  function pick(t) {
    onChange(t ? (t.schema + '|' + t.name) : '')
    setQuery('')
    setOpen(false)
  }
  function handleKeyDown(e) {
    if (disabled) return
    if (!open) {
      if (e.key === 'ArrowDown' || e.key === 'Enter') { e.preventDefault(); openMenu() }
      return
    }
    if (e.key === 'ArrowDown') { e.preventDefault(); setFocusIdx(function (i) { return Math.min(filtered.length - 1, i + 1) }) }
    else if (e.key === 'ArrowUp') { e.preventDefault(); setFocusIdx(function (i) { return Math.max(0, i - 1) }) }
    else if (e.key === 'Enter') { e.preventDefault(); if (filtered[focusIdx]) pick(filtered[focusIdx]) }
    else if (e.key === 'Escape') { setOpen(false) }
  }

  var menu = (open && !disabled && menuRect) ? (
    <div data-vb-tsel-menu="1" className="vb-tsel-menu" style={{
      position: 'fixed', left: menuRect.left, width: menuRect.width, maxHeight: 260, zIndex: 9999,
      top: menuRect.top, bottom: menuRect.bottom,
    }}>
      {filtered.length === 0 && <div className="vb-tsel-empty">Eşleşen tablo/view yok.</div>}
      {filtered.map(function (t, i) {
        var v = t.schema + '|' + t.name
        return (
          <div key={v}
               className={'vb-tsel-option' + (i === focusIdx ? ' active' : '') + (v === value ? ' selected' : '')}
               onMouseDown={function (e) { e.preventDefault(); pick(t) }}
               onMouseEnter={function () { setFocusIdx(i) }}>
            <span className="vb-tsel-option-name">{t.schema}.{t.name}</span>
            <span className={'vb-tsel-kind-badge' + (t.kind === 'view' ? ' view' : '')}>{t.kind === 'view' ? 'view' : 'tablo'}</span>
          </div>
        )
      })}
    </div>
  ) : null

  return (
    <div ref={wrapRef} className={'vb-tsel' + (disabled ? ' vb-tsel-disabled' : '')}>
      <div className="vb-tsel-control" onClick={function () { openMenu(); if (inputRef.current) inputRef.current.focus() }}>
        <Search size={12} className="vb-tsel-icon" />
        <input
          ref={inputRef} type="text" className="vb-tsel-input" autoComplete="off"
          disabled={disabled}
          placeholder={open ? (displayLabel || placeholder || 'Ara…') : (placeholder || 'Seçiniz…')}
          value={open ? query : displayLabel}
          onChange={function (e) { setQuery(e.target.value); if (!open) setOpen(true) }}
          onFocus={openMenu}
          onKeyDown={handleKeyDown}
        />
        {selected && !disabled && !open && (
          <button type="button" className="vb-tsel-clear"
                  onMouseDown={function (e) { e.preventDefault() }}
                  onClick={function (e) { e.stopPropagation(); pick(null) }}
                  title="Temizle">
            <X size={11} />
          </button>
        )}
        <ChevronDown size={13} className="vb-tsel-chevron" />
      </div>
      {menu && createPortal(menu, document.body)}
    </div>
  )
}
