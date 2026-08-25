import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Stage, Layer, Circle, Line, Text, Group } from 'react-konva'
import { Network, Search, RefreshCw, X, Home, Table2 } from 'lucide-react'
import './databaseMap.css'

/**
 * Veritabanı Haritası — tabloların birbiriyle ilişkisini gezilebilir bir "evren" olarak gösterir.
 *
 * İki görünüm var, ikisi de AYNI veriden türer:
 *   · Genel   → tüm tablolar, bağlantı sayısına göre iç içe halkalarda. Çok bağlantılı
 *               ("merkez") tablolar içeride, yaprak tablolar dışarıda.
 *   · Odak    → bir tabloya tıklanınca o tablo merkeze gelir; DOĞRUDAN ilişkili tablolar
 *               1. yörüngeye, onların ilişkileri 2. yörüngeye yerleşir; yollar yanar.
 *
 * Odak modunda YALNIZ iki adım uzaklıktaki tablolar çizilir. Bu hem okunabilirlik hem
 * başarım kararıdır: tüm graf her karede yeniden çizilseydi akış animasyonu kasardı.
 *
 * Tuval (Konva) CSS değişkenlerini okuyamaz — renkler burada palet olarak tutulur ve
 * <body> tema sınıfı değiştiğinde yeniden okunur (bkz. useThemePalette).
 */

const RING1 = 210     // doğrudan ilişkili tabloların yörünge yarıçapı
const RING2 = 395     // ikinci derece
const GOLDEN = 2.399963229728653   // altın açı — halkalarda düğümlerin üst üste binmesini önler

function useThemePalette() {
  const read = () => {
    const dark = typeof document !== 'undefined' &&
      document.body.classList.contains('app-theme-dark')
    return dark
      ? {
          dark: true,
          bg: '#070b16',
          edge: '#1e293b',
          edgeFk: '#6366f1',
          edgeInferred: '#64748b',
          label: '#cbd5e1',
          labelDim: '#475569',
          node: ['#334155', '#4338ca', '#7c3aed', '#c2410c'],
          nodeStroke: '#0b1220',
          focusRing: '#a5b4fc',
        }
      : {
          dark: false,
          bg: '#f8fafc',
          edge: '#e2e8f0',
          edgeFk: '#4f46e5',
          edgeInferred: '#94a3b8',
          label: '#334155',
          labelDim: '#94a3b8',
          node: ['#cbd5e1', '#a5b4fc', '#c4b5fd', '#fdba74'],
          nodeStroke: '#ffffff',
          focusRing: '#4f46e5',
        }
  }
  const [palette, setPalette] = useState(read)
  useEffect(() => {
    // Tema <body> sınıfıyla değişiyor; tuval kendini yeniden boyayabilmek için haber almalı.
    const obs = new MutationObserver(() => setPalette(read()))
    obs.observe(document.body, { attributes: true, attributeFilter: ['class'] })
    return () => obs.disconnect()
  }, [])
  return palette
}

/** Bağlantı sayısına göre düğüm rengi — uydurma modül/grup yok, ölçü gerçek veriden. */
function nodeColor(palette, degree) {
  if (degree >= 12) return palette.node[3]
  if (degree >= 6) return palette.node[2]
  if (degree >= 2) return palette.node[1]
  return palette.node[0]
}

/** Satır sayısı → yarıçap. Logaritmik: 10 satırlık tablo 10 milyonluğun yanında kaybolmasın. */
function nodeRadius(rowCount, degree) {
  const byRows = Math.log10(Math.max(1, rowCount) + 9) * 4.2   // ~4 … ~30
  return Math.max(7, Math.min(26, byRows + Math.min(6, degree * 0.4)))
}

function formatCount(n) {
  return (n || 0).toLocaleString('tr-TR')
}

export default function DatabaseMap({ apiBase = '/api/database' }) {
  const palette = useThemePalette()

  const [data, setData] = useState(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)
  const [focus, setFocus] = useState(null)     // odaklanılan tablo adı (null = genel görünüm)
  const [hover, setHover] = useState(null)
  const [term, setTerm] = useState('')
  const [size, setSize] = useState({ w: 900, h: 600 })
  const [view, setView] = useState({ x: 0, y: 0, scale: 1 })

  const wrapRef = useRef(null)
  const posRef = useRef(new Map())             // ad → {x, y} (animasyonun ANLIK konumu)
  const rafRef = useRef(0)
  const [, forceTick] = useState(0)

  // ── Veri ────────────────────────────────────────────────────────────────
  const load = useCallback(() => {
    setLoading(true)
    fetch(apiBase + '/map', { credentials: 'same-origin' })
      .then(r => r.json())
      .then(d => {
        if (d && d.ok) { setData(d); setError(null) }
        else setError((d && d.message) || 'Harita verisi alınamadı.')
      })
      .catch(() => setError('Harita verisi alınamadı (ağ hatası).'))
      .finally(() => setLoading(false))
  }, [apiBase])

  useEffect(() => { load() }, [load])

  // ── Tuval ölçüsü ────────────────────────────────────────────────────────
  useEffect(() => {
    const el = wrapRef.current
    if (!el) return undefined
    const apply = () => setSize({ w: el.clientWidth || 900, h: el.clientHeight || 600 })
    apply()
    const ro = new ResizeObserver(apply)
    ro.observe(el)
    return () => ro.disconnect()
  }, [])

  // ── Graf modeli ─────────────────────────────────────────────────────────
  const graph = useMemo(() => {
    const tables = (data && data.tables) || []
    const edges = (data && data.edges) || []
    const byName = new Map()
    tables.forEach(t => byName.set(t.name, { ...t, degree: 0 }))

    const adj = new Map()      // ad → Set(komşu ad)
    const valid = []
    edges.forEach(e => {
      if (!byName.has(e.from) || !byName.has(e.to)) return
      valid.push(e)
      if (!adj.has(e.from)) adj.set(e.from, new Set())
      if (!adj.has(e.to)) adj.set(e.to, new Set())
      // Kendine referans (ör. ParentId) komşu SAYILMAZ: yörüngesi kendisi olurdu.
      if (e.from !== e.to) {
        adj.get(e.from).add(e.to)
        adj.get(e.to).add(e.from)
      }
    })
    byName.forEach((t, name) => { t.degree = adj.has(name) ? adj.get(name).size : 0 })
    return { byName, edges: valid, adj }
  }, [data])

  // ── Yerleşim: hangi tablo nerede duracak (HEDEF konum) ──────────────────
  const layout = useMemo(() => {
    const targets = new Map()
    const names = Array.from(graph.byName.keys())
    if (names.length === 0) return { targets, visible: [], ring1: new Set(), ring2: new Set() }

    if (!focus) {
      // Genel görünüm: bağlantısı çok olan içeride. Halka kapasitesi yarıçapla büyür.
      const sorted = names.slice().sort((a, b) => {
        const d = graph.byName.get(b).degree - graph.byName.get(a).degree
        return d !== 0 ? d : a.localeCompare(b, 'tr')
      })
      let i = 0, ring = 0
      while (i < sorted.length) {
        const radius = 120 + ring * 92
        const capacity = Math.max(6, Math.round((2 * Math.PI * radius) / 78))
        const count = Math.min(capacity, sorted.length - i)
        for (let k = 0; k < count; k++) {
          const angle = (k / count) * Math.PI * 2 + ring * GOLDEN
          targets.set(sorted[i + k], { x: Math.cos(angle) * radius, y: Math.sin(angle) * radius })
        }
        i += count
        ring++
      }
      return { targets, visible: sorted, ring1: new Set(), ring2: new Set() }
    }

    const ring1 = new Set(graph.adj.get(focus) || [])
    ring1.delete(focus)
    const ring2 = new Set()
    ring1.forEach(n => {
      (graph.adj.get(n) || new Set()).forEach(m => {
        if (m !== focus && !ring1.has(m)) ring2.add(m)
      })
    })

    targets.set(focus, { x: 0, y: 0 })
    const place = (set, radius, offset) => {
      const arr = Array.from(set).sort((a, b) => a.localeCompare(b, 'tr'))
      arr.forEach((n, k) => {
        const angle = (k / Math.max(1, arr.length)) * Math.PI * 2 + offset
        targets.set(n, { x: Math.cos(angle) * radius, y: Math.sin(angle) * radius })
      })
    }
    place(ring1, RING1, -Math.PI / 2)
    place(ring2, RING2, -Math.PI / 2 + GOLDEN)

    return { targets, visible: [focus, ...ring1, ...ring2], ring1, ring2 }
  }, [graph, focus])

  // ── Konumları hedefe doğru yumuşat (gezegenler yörüngeye otursun) ───────
  useEffect(() => {
    const step = () => {
      let moving = false
      layout.targets.forEach((t, name) => {
        const cur = posRef.current.get(name)
        if (!cur) {
          // Yeni giren düğüm merkezden doğar — "içeri süzülme" hissi.
          posRef.current.set(name, { x: t.x * 0.2, y: t.y * 0.2 })
          moving = true
          return
        }
        const dx = t.x - cur.x
        const dy = t.y - cur.y
        if (Math.abs(dx) < 0.4 && Math.abs(dy) < 0.4) {
          cur.x = t.x; cur.y = t.y
          return
        }
        cur.x += dx * 0.16
        cur.y += dy * 0.16
        moving = true
      })
      forceTick(v => v + 1)
      rafRef.current = moving ? requestAnimationFrame(step) : 0
    }
    cancelAnimationFrame(rafRef.current)
    rafRef.current = requestAnimationFrame(step)
    return () => cancelAnimationFrame(rafRef.current)
  }, [layout])

  // Odak değişince görünümü ortala — kullanıcı kaybolmasın.
  useEffect(() => { setView({ x: 0, y: 0, scale: focus ? 1 : 0.78 }) }, [focus])

  // ── Çizilecek kenarlar ──────────────────────────────────────────────────
  const visibleSet = useMemo(() => new Set(layout.visible), [layout])
  const drawEdges = useMemo(() => {
    return graph.edges.filter(e =>
      e.from !== e.to && visibleSet.has(e.from) && visibleSet.has(e.to))
  }, [graph, visibleSet])

  const activeName = hover || focus
  const isLit = useCallback((e) => {
    if (!activeName) return false
    return e.from === activeName || e.to === activeName
  }, [activeName])

  // ── Odaklanılan tablonun ilişki dökümü (yan panel) ──────────────────────
  const detail = useMemo(() => {
    if (!focus) return null
    const t = graph.byName.get(focus)
    if (!t) return null
    const outgoing = graph.edges.filter(e => e.from === focus)
    const incoming = graph.edges.filter(e => e.to === focus && e.from !== focus)
    return { table: t, outgoing, incoming }
  }, [graph, focus])

  const suggestions = useMemo(() => {
    const q = term.trim().toLocaleLowerCase('tr')
    if (!q) return []
    return Array.from(graph.byName.keys())
      .filter(n => n.toLocaleLowerCase('tr').includes(q))
      .slice(0, 12)
  }, [graph, term])

  const summary = (data && data.summary) || null

  const pick = (name) => { setFocus(name); setTerm(''); setHover(null) }

  // ── Tuval etkileşimi: tekerlek ile yakınlaş/uzaklaş ─────────────────────
  const onWheel = (e) => {
    e.evt.preventDefault()
    const stage = e.target.getStage()
    const old = view.scale
    const next = Math.max(0.25, Math.min(2.4, old * (e.evt.deltaY > 0 ? 0.92 : 1.08)))
    const pointer = stage.getPointerPosition()
    if (!pointer) { setView(v => ({ ...v, scale: next })); return }
    // İmlecin gösterdiği noktayı sabit tut — yoksa yakınlaşma sürekli merkeze kaçar.
    const cx = size.w / 2, cy = size.h / 2
    const mx = (pointer.x - cx - view.x) / old
    const my = (pointer.y - cy - view.y) / old
    setView({ scale: next, x: pointer.x - cx - mx * next, y: pointer.y - cy - my * next })
  }

  const pos = (name) => posRef.current.get(name) || { x: 0, y: 0 }

  return (
    <div className="dbm-root">
      <div className="dbm-header">
        <div className="dbm-header-icon"><Network size={19} /></div>
        <div>
          <div className="dbm-title">Veritabanı Haritası</div>
          <div className="dbm-sub">
            {summary
              ? `${formatCount(summary.tableCount)} tablo · ${formatCount(summary.fkCount)} tanımlı ilişki · ${formatCount(summary.inferredCount)} çıkarım`
              : (loading ? 'Yükleniyor…' : '—')}
          </div>
        </div>

        <span style={{ flex: 1 }} />

        <div style={{ position: 'relative' }}>
          <div className="dbm-search">
            <Search size={14} />
            <input placeholder="Tablo ara…" value={term}
              onChange={e => setTerm(e.target.value)}
              onKeyDown={e => { if (e.key === 'Enter' && suggestions.length) pick(suggestions[0]) }} />
            {term ? <X size={13} style={{ cursor: 'pointer' }} onClick={() => setTerm('')} /> : null}
          </div>
          {suggestions.length > 0 && (
            <div className="dbm-suggest">
              {suggestions.map(n => (
                <button key={n} type="button" onClick={() => pick(n)}>{n}</button>
              ))}
            </div>
          )}
        </div>

        <button type="button" className="dbm-btn" onClick={() => setFocus(null)} disabled={!focus}>
          <Home size={14} /> Genel Görünüm
        </button>
        <button type="button" className="dbm-btn" onClick={load} disabled={loading} title="Yenile">
          <RefreshCw size={14} className={loading ? 'dbm-spin' : ''} />
        </button>
      </div>

      <div className="dbm-body">
        <div className="dbm-canvas-wrap" ref={wrapRef}>
          {error ? (
            <div className="dbm-empty">{error}</div>
          ) : (
            <Stage width={size.w} height={size.h} onWheel={onWheel}
              onMouseDown={e => { if (e.target === e.target.getStage()) setFocus(null) }}>
              <Layer>
                <Group x={size.w / 2 + view.x} y={size.h / 2 + view.y}
                  scaleX={view.scale} scaleY={view.scale}>

                  {/* Kenarlar — yanan yollar üstte kalsın diye iki geçişte çizilir. */}
                  {drawEdges.map((e, i) => {
                    if (isLit(e)) return null
                    const a = pos(e.from), b = pos(e.to)
                    return (
                      <Line key={'d' + i} points={[a.x, a.y, b.x, b.y]}
                        stroke={palette.edge}
                        strokeWidth={1}
                        opacity={activeName ? 0.25 : 0.6}
                        dash={e.kind === 'inferred' ? [4, 5] : undefined}
                        listening={false} />
                    )
                  })}
                  {drawEdges.map((e, i) => {
                    if (!isLit(e)) return null
                    const a = pos(e.from), b = pos(e.to)
                    return (
                      <Line key={'l' + i} points={[a.x, a.y, b.x, b.y]}
                        stroke={e.kind === 'fk' ? palette.edgeFk : palette.edgeInferred}
                        strokeWidth={e.kind === 'fk' ? 2.2 : 1.6}
                        dash={e.kind === 'inferred' ? [5, 5] : undefined}
                        shadowColor={palette.edgeFk}
                        shadowBlur={palette.dark ? 14 : 6}
                        shadowOpacity={0.85}
                        listening={false} />
                    )
                  })}

                  {/* Düğümler */}
                  {layout.visible.map(name => {
                    const t = graph.byName.get(name)
                    if (!t) return null
                    const p = pos(name)
                    const r = nodeRadius(t.rowCount, t.degree)
                    const isFocus = name === focus
                    const isHot = name === activeName ||
                      (activeName && graph.adj.get(activeName) && graph.adj.get(activeName).has(name))
                    const dim = !!activeName && !isHot
                    return (
                      <Group key={name} x={p.x} y={p.y}
                        onClick={() => pick(name)}
                        onTap={() => pick(name)}
                        onMouseEnter={e => {
                          setHover(name)
                          const c = e.target.getStage().container()
                          if (c) c.style.cursor = 'pointer'
                        }}
                        onMouseLeave={e => {
                          setHover(null)
                          const c = e.target.getStage().container()
                          if (c) c.style.cursor = 'default'
                        }}>
                        {isFocus && (
                          <Circle radius={r + 9} stroke={palette.focusRing} strokeWidth={1.6}
                            opacity={0.75} listening={false} />
                        )}
                        <Circle
                          radius={r}
                          fill={nodeColor(palette, t.degree)}
                          stroke={palette.nodeStroke}
                          strokeWidth={1.2}
                          opacity={dim ? 0.28 : 1}
                          shadowColor={nodeColor(palette, t.degree)}
                          shadowBlur={isHot ? (palette.dark ? 26 : 12) : (palette.dark ? 10 : 0)}
                          shadowOpacity={dim ? 0 : 0.9} />
                        <Text
                          text={name}
                          fontSize={11}
                          fontFamily="ui-monospace, Menlo, Consolas, monospace"
                          fill={dim ? palette.labelDim : palette.label}
                          opacity={dim ? 0.5 : 1}
                          align="center"
                          width={150}
                          offsetX={75}
                          y={r + 5}
                          listening={false} />
                      </Group>
                    )
                  })}
                </Group>
              </Layer>
            </Stage>
          )}

          <div className="dbm-hint">
            {focus
              ? 'Yörüngedeki tabloya tıklayın · boşluğa tıklayın = genel görünüm · tekerlek = yakınlaştır'
              : 'Bir tabloya tıklayın — ilişkileri yörüngeye dizilir · tekerlek = yakınlaştır'}
          </div>
          <div className="dbm-legend">
            <div className="dbm-legend-row"><span className="dbm-legend-line" /> Tanımlı ilişki (FK)</div>
            <div className="dbm-legend-row"><span className="dbm-legend-line dbm-legend-line--dashed" /> Ad benzerliğinden çıkarım</div>
          </div>
        </div>

        {/* Yan panel her zaman mount — seçim değiştikçe tuval genişliği zıplamasın. */}
        <div className="dbm-side">
          {detail ? (
            <>
              <div className="dbm-side-head">
                <div className="dbm-side-title">{detail.table.name}</div>
                <div className="dbm-side-meta">
                  {formatCount(detail.table.rowCount)} satır · {detail.table.columnCount} kolon · {detail.table.degree} bağlantı
                </div>
              </div>
              <div className="dbm-side-body">
                <div className="dbm-group-title">BU TABLONUN REFERANSLARI ({detail.outgoing.length})</div>
                {detail.outgoing.length === 0
                  ? <div className="dbm-rel-col" style={{ padding: '2px 8px' }}>Yok — başka tabloya bağlanmıyor.</div>
                  : detail.outgoing.map((e, i) => (
                    <button key={'o' + i} type="button" className="dbm-rel" onClick={() => pick(e.to)}>
                      <span className="dbm-rel-name">{e.to}</span>
                      <span className="dbm-rel-col">{e.fromColumn}</span>
                      <span className={'dbm-rel-badge' + (e.kind === 'fk' ? ' dbm-rel-badge--fk' : '')}>
                        {e.kind === 'fk' ? 'FK' : 'çıkarım'}
                      </span>
                    </button>
                  ))}

                <div className="dbm-group-title">BU TABLOYA REFERANS VERENLER ({detail.incoming.length})</div>
                {detail.incoming.length === 0
                  ? <div className="dbm-rel-col" style={{ padding: '2px 8px' }}>Yok — hiçbir tablo bu tabloyu göstermiyor.</div>
                  : detail.incoming.map((e, i) => (
                    <button key={'i' + i} type="button" className="dbm-rel" onClick={() => pick(e.from)}>
                      <span className="dbm-rel-name">{e.from}</span>
                      <span className="dbm-rel-col">{e.fromColumn}</span>
                      <span className={'dbm-rel-badge' + (e.kind === 'fk' ? ' dbm-rel-badge--fk' : '')}>
                        {e.kind === 'fk' ? 'FK' : 'çıkarım'}
                      </span>
                    </button>
                  ))}
              </div>
            </>
          ) : (
            <div className="dbm-empty">
              <Table2 size={30} />
              <div>Bir tablo seçin — ilişkileri burada dökümlenir.</div>
              {summary && summary.unmatchedIdColumns > 0 && (
                <div className="dbm-note">
                  Haritanın kapsamı: {formatCount(summary.fkCount)} ilişki veritabanında tanımlı,
                  {' '}{formatCount(summary.inferredCount)} ilişki kolon adından çıkarıldı.
                  {' '}{formatCount(summary.unmatchedIdColumns)} adet <b>*Id</b> kolonu hiçbir tabloya
                  eşleşmedi ve <b>çizilmedi</b> — harita tüm ilişkileri gösterdiğini iddia etmez.
                </div>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
