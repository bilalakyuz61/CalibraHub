import React, { useState } from 'react'

/* Sol filtre rayında tek alan — açılır akordeon + benzersiz değer seçimi.
   values: string[] (benzersiz değerler), selected: string[], onChange(nextSelected) */
export default function FilterField({ label, values, selected, onChange }) {
  const [open, setOpen] = useState(false)
  const [q, setQ] = useState('')

  const ql = q.trim().toLowerCase()
  const shown = ql ? values.filter(v => v.toLowerCase().includes(ql)) : values

  function toggle(v) {
    onChange(selected.includes(v) ? selected.filter(x => x !== v) : [...selected, v])
  }

  return (
    <div className={`rv-filterf${open ? ' rv-filterf--open' : ''}`}>
      <button type="button" className="rv-filterf__head" onClick={() => setOpen(o => !o)}>
        <svg className={`rv-filterf__chev${open ? ' rv-filterf__chev--open' : ''}`} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round" style={{ width: 12, height: 12 }}>
          <polyline points="9 18 15 12 9 6" />
        </svg>
        <span className="rv-filterf__name">{label}</span>
        {selected.length > 0 && <span className="rv-filterf__count">{selected.length}</span>}
      </button>
      {open && (
        <div className="rv-filterf__body">
          {values.length > 8 && (
            <input className="rv-filterf__search" placeholder="Ara…" value={q} onChange={e => setQ(e.target.value)} />
          )}
          <div className="rv-filterf__tools">
            <button
              type="button"
              className="rv-filterf__tool"
              onClick={() => onChange([...new Set([...selected, ...shown])])}
              disabled={shown.length === 0 || shown.every(v => selected.includes(v))}
            >
              {ql ? `Eşleşenleri seç (${shown.length})` : 'Tümünü seç'}
            </button>
            <button
              type="button"
              className="rv-filterf__tool"
              onClick={() => onChange([])}
              disabled={selected.length === 0}
            >
              Temizle{selected.length ? ` (${selected.length})` : ''}
            </button>
          </div>
          <div className="rv-filterf__list">
            {shown.map((v, i) => {
              const on = selected.includes(v)
              return (
                <button key={i} type="button" className={`rv-filterf__item${on ? ' rv-filterf__item--on' : ''}`} onClick={() => toggle(v)}>
                  <span className={`rv-filterf__check${on ? ' rv-filterf__check--on' : ''}`}>
                    {on && (
                      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3.5" strokeLinecap="round" style={{ width: 9, height: 9 }}>
                        <polyline points="20 6 9 17 4 12" />
                      </svg>
                    )}
                  </span>
                  <span className="rv-filterf__label">{v === '' ? '(boş)' : v}</span>
                </button>
              )
            })}
            {shown.length === 0 && <div className="rv-filterf__empty">Değer yok</div>}
          </div>
        </div>
      )}
    </div>
  )
}
