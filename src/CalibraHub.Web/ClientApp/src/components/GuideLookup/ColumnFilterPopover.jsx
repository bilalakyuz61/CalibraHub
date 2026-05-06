/**
 * ColumnFilterPopover — Excel-tarzi kolon basligi distinct filtre popover'i.
 *
 * GuideLookupModal'in kolon basligindaki huni ikonu tiklaninca acilan
 * floating panel. Iceriginde:
 *   - autoFocus arama input'u (popover icindeki degerleri filtreler)
 *   - checkbox listesi (distinct degerler)
 *   - Tumu / Temizle / Iptal / Uygula butonlari
 *
 * Konumlandirma anchor'in alt-sol kosesinden 4px asagi; ekrana sigmiyorsa
 * sola/yukari kaydirir.
 *
 * Props:
 *   column          — kolon adi (key)
 *   colLabel        — gosterilecek baslik
 *   values          — string[] (sunucudan cekilmis distinct degerler)
 *   selected        — string[] (mevcut secim — popover acildiginda baz alinir)
 *   anchorRect      — { top, left, right, bottom } (acan butonun rect'i)
 *   onApply(newSel)
 *   onClose()
 *   onSearchChange  — opsiyonel; her tus vurusunda raw search'u disariya bildirir
 *                     (modal debounce edip sunucudan yeni values fetch eder)
 *   loading         — opsiyonel; sunucudan distinct yenileniyor mi
 */
import { useState, useEffect, useRef, useMemo } from 'react'
import { Search, Filter, Check, Loader2 } from 'lucide-react'

var POPOVER_W = 240
var POPOVER_H = 360

export default function ColumnFilterPopover(props) {
  var values         = props.values || []
  var selected       = props.selected || []
  var anchorRect     = props.anchorRect
  var onApply        = props.onApply
  var onClose        = props.onClose
  var onSearchChange = props.onSearchChange
  var loading        = !!props.loading

  var [search, setSearch]   = useState('')
  var [tempSel, setTempSel] = useState(selected.slice())

  var filtered = useMemo(function () {
    if (!search.trim()) return values
    var q = search.toLowerCase()
    return values.filter(function (v) { return String(v).toLowerCase().indexOf(q) !== -1 })
  }, [values, search])

  function toggle(val) {
    setTempSel(function (prev) {
      var idx = prev.indexOf(val)
      return idx === -1 ? prev.concat([val]) : prev.filter(function (x) { return x !== val })
    })
  }
  function selectAllVisible() {
    setTempSel(function (prev) {
      var setNew = new Set(prev)
      filtered.forEach(function (v) { setNew.add(v) })
      return Array.from(setNew)
    })
  }
  function clearAll() { setTempSel([]) }
  function apply() { onApply(tempSel) }

  // Pozisyon: anchor'in alt-sol kosesinden 4px asagi.
  // Ekran disina tasiyorsa sola/yukari kaydir.
  var top  = anchorRect ? anchorRect.bottom + 4 : 100
  var left = anchorRect ? anchorRect.left      : 100
  if (typeof window !== 'undefined') {
    if (left + POPOVER_W > window.innerWidth - 8) {
      left = Math.max(8, window.innerWidth - POPOVER_W - 8)
    }
    if (top + POPOVER_H > window.innerHeight - 8) {
      top = anchorRect ? Math.max(8, anchorRect.top - POPOVER_H - 4) : 8
    }
  }

  var popoverStyle = {
    position: 'fixed',
    top:    top  + 'px',
    left:   left + 'px',
    width:  POPOVER_W + 'px',
    // GuideLookupModal backdrop z-index 10100 — popover MODAL'IN USTUNDE olmali,
    // yoksa tiklamalar backdrop'a carpar ve popover'in outside-click listener'i
    // hemen kapatir → filtreler tepki vermez. 10101 = backdrop+1.
    // (GuideCustomizationModal 10200; popover ile ayar modal'i ayni anda acilmaz.)
    zIndex: 10101,
  }

  var rootRef = useRef(null)
  useEffect(function () {
    function onDocClick(e) {
      if (rootRef.current && !rootRef.current.contains(e.target)) onClose()
    }
    // Bir tick gecikmeyle ekle ki popover'i acan tiklama hemen kapatmasin.
    var h = setTimeout(function () { document.addEventListener('mousedown', onDocClick) }, 0)
    return function () {
      clearTimeout(h)
      document.removeEventListener('mousedown', onDocClick)
    }
  }, [onClose])

  function onKey(e) {
    if (e.key === 'Enter') { e.preventDefault(); apply() }
  }

  return (
    <div ref={rootRef} className="gl-popover" style={popoverStyle} onKeyDown={onKey}>
      <div className="gl-popover-title">
        <Filter size={11} strokeWidth={2.4} />
        <span>{props.colLabel}</span>
      </div>

      <div className="gl-popover-search-wrap">
        {loading
          ? <Loader2 size={12} strokeWidth={2.4} className="gl-popover-search-ico spin" />
          : <Search   size={12} strokeWidth={2.4} className="gl-popover-search-ico" />}
        <input
          type="text"
          autoFocus
          value={search}
          onChange={function (e) {
            var v = e.target.value
            setSearch(v)
            if (onSearchChange) onSearchChange(v)
          }}
          placeholder="Ara..."
          className="gl-popover-search"
        />
      </div>

      <div className="gl-popover-actions">
        <button type="button" onClick={selectAllVisible}>Tümü</button>
        <span className="gl-popover-divider">·</span>
        <button type="button" onClick={clearAll}>Temizle</button>
        <span className="gl-popover-count">
          {tempSel.length}/{values.length}
        </span>
      </div>

      <div className="gl-popover-list">
        {filtered.length === 0 && (
          <div className="gl-popover-empty">Sonuç yok</div>
        )}
        {filtered.map(function (v) {
          var checked = tempSel.indexOf(v) !== -1
          return (
            <div
              key={String(v)}
              role="button"
              tabIndex={0}
              className={'gl-popover-row ' + (checked ? 'gl-popover-row--checked' : '')}
              title={String(v)}
              onClick={function () { toggle(v) }}
              onKeyDown={function (e) {
                if (e.key === ' ' || e.key === 'Enter') { e.preventDefault(); toggle(v) }
              }}
            >
              <span className="gl-popover-checkbox">
                {checked && <Check size={10} strokeWidth={3} />}
              </span>
              <span className="gl-popover-row-label">{String(v)}</span>
            </div>
          )
        })}
      </div>

      <div className="gl-popover-footer">
        <button
          type="button"
          onClick={onClose}
          className="gl-popover-btn gl-popover-btn--ghost"
        >İptal</button>
        <button
          type="button"
          onClick={apply}
          className="gl-popover-btn gl-popover-btn--primary"
        >Uygula</button>
      </div>
    </div>
  )
}
