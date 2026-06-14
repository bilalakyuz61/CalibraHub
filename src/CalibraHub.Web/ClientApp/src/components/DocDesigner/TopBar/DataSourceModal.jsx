import React, { useState, useEffect, useRef } from 'react'
import { listDbViews, getDbViewColumns } from '../services/docDesignerService'

const MODES = [
  { key: 'db',  label: 'DB View' },
  { key: 'sql', label: 'Ad-hoc SQL' },
]

const TABS = [
  { key: 'source',  label: 'Kaynak',     icon: '▤' },
  { key: 'filter',  label: 'Filtre',     icon: '⛁' },
  { key: 'order',   label: 'Sıralama',   icon: '⇅' },
  { key: 'mapping', label: 'Eşleme',     icon: '⇆' },
]

/**
 * Veri Kaynakları yönetim modalı.
 *
 * AKIŞ:
 *   - Kaynak tab: view seçici + inline "+ Ekle" butonu → seçilen view design'a eklenir.
 *     Eklenmiş kaynaklar altta etiket olarak görünür (× ile silinir).
 *   - Filtre / Sıralama tabları: SEÇİLİ kaynak için WHERE / ORDER BY (ileride etkilemek
 *     üzere UI hazır; şu an view yeniden eklenirken kullanılır).
 *   - Eşleme tab: TAMAMEN BAĞIMSIZ → Ana view + Ana kolon + Alt view + Alt kolon seç,
 *     "+ Ekle" → altta satır oluşur, form temizlenir. Çift tık ile satır editlenir.
 *     Save deyince çocuk view'in (Alt) DocLayoutDs.JoinOn alanı güncellenir.
 *   - Footer: sadece "Kapat" — her şey inline ekleme/silme ile yapılır.
 */
export default function DataSourceModal({ existingSources = [], onAdd, onDelete, onUpdate, onClose }) {
  const [tab,  setTab]  = useState('source')
  const [mode, setMode] = useState('db')
  const [dbViews, setDbViews] = useState([])
  const [loadingViews, setLoadingViews] = useState(true)

  // Kaynak (yeni view ekleme) state'i
  const [dbView,   setDbView]   = useState('')
  const [adHocSql, setAdHocSql] = useState('')
  // Varsayılan: belge bazlı filtre — kullanıcı isterse değiştirebilir
  const [whereExtra, setWhereExtra] = useState('BelgeId = @DocumentId')
  const [orderBy,    setOrderBy]    = useState('')

  // Filtre / Sıralama tab'ları HANGİ view için olduğunu kullanıcıya sorar:
  //   "" → yeni eklenecek view için (Kaynak tab'ında "+ Ekle" tıklayınca uygulanır)
  //   "alias" → mevcut bir view; o view'in SQL'i "Uygula" ile yeniden inşa edilir
  const [filterTarget, setFilterTarget] = useState('')
  const [orderTarget,  setOrderTarget]  = useState('')
  // Mevcut view'lar için edit edilen değerler (target değişince yüklenir)
  const [filterValueForExisting, setFilterValueForExisting] = useState('')
  const [orderValueForExisting,  setOrderValueForExisting]  = useState('')

  // Hata / başarı
  const [error, setError] = useState(null)
  const [flash, setFlash] = useState(null)
  const [confirmingAlias, setConfirmingAlias] = useState(null)

  // Eşleme tab state'i
  const [mapAlt,        setMapAlt]        = useState('')   // çocuk view alias
  const [mapAna,        setMapAna]        = useState('')   // ana view alias
  const [mapAnaCol,     setMapAnaCol]     = useState('')
  const [mapAltCol,     setMapAltCol]     = useState('')
  const [mapRows,       setMapRows]       = useState([])   // [{anaAlias, anaCol, altAlias, altCol}]
  const [editingMapIdx, setEditingMapIdx] = useState(-1)

  // View → kolon listesi cache
  const [colCache, setColCache] = useState({})   // alias → string[]

  const existingAliases = existingSources.map(s => s.alias)
  const totalExisting   = existingAliases.length
  const autoRole = totalExisting === 0 ? 'master' : 'detail'

  useEffect(() => {
    setLoadingViews(true)
    listDbViews().catch(() => []).then(db => setDbViews(db)).finally(() => setLoadingViews(false))
  }, [])

  // ── Eşleme satırlarını mevcut data source'lardan yükle ──────────────────────
  // İlk render'da existingSources'tan JoinOn/ParentAlias bilgilerini parse edip mapRows'a koy.
  useEffect(() => {
    const loaded = []
    for (const s of existingSources) {
      if (!s.parentAlias || !s.joinOn) continue
      // joinOn: tek string veya JSON array of {p,c}
      let pairs = []
      const t = s.joinOn.trim()
      if (t.startsWith('[')) {
        try {
          pairs = JSON.parse(t).filter(x => x.p && x.c).map(x => ({ p: x.p, c: x.c }))
        } catch { pairs = [{ p: t, c: t }] }
      } else {
        pairs = [{ p: t, c: t }]
      }
      for (const pr of pairs) {
        loaded.push({ anaAlias: s.parentAlias, anaCol: pr.p, altAlias: s.alias, altCol: pr.c })
      }
    }
    setMapRows(loaded)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])   // yalnızca açılışta

  // ── Yardımcılar ────────────────────────────────────────────────────────────
  const makeUniqueAlias = (base) => {
    let clean = (base || 'source')
      .replace(/[^A-Za-z0-9_]/g, '_').replace(/_+/g, '_').replace(/^_|_$/g, '')
    if (!clean) clean = 'source'
    if (!existingAliases.includes(clean)) return clean
    let i = 2
    while (existingAliases.includes(clean + '_' + i)) i++
    return clean + '_' + i
  }

  const buildSqlForDbView = (viewName, wx = '', ob = '') => {
    if (!viewName) return ''
    const parts = viewName.split('.')
    let s = `SELECT * FROM [${parts[0]}].[${parts[1]}]`
    if (wx.trim()) s += ` WHERE ${wx.trim()}`
    if (ob.trim()) s += ` ORDER BY ${ob.trim()}`
    return s
  }

  const resetSourceForm = () => {
    // whereExtra default değerine geri dön — yeni view eklerken filtre yine
    // hazır gelsin. Eskiden '' yapıyorduk, kullanıcı 2. view'ı WHERE'siz ekliyordu
    // ve "tüm dokümanların satırları geliyor" hatası oluşuyordu.
    setDbView(''); setAdHocSql(''); setWhereExtra('BelgeId = @DocumentId'); setOrderBy('')
    setError(null)
  }

  // Inline "+ Ekle" — Kaynak tab'ında combobox yanından çağrılır.
  // 2026-06-03: WHERE klozunda referans edilen kolonlar view'da gercekten var mi
  // diye dogrulanir; yoksa ekleme yapilmaz, hangi kolonlarin bulunamadigi gosterilir.
  const handleAddSource = async () => {
    setError(null)
    let sql = null, dbViewName = null, aliasBase = ''
    let whereForValidation = ''     // hangi WHERE kullanildi → kolon kontrolu icin
    if (mode === 'db') {
      if (!dbView) { setError('Bir view seçin'); return }
      sql = buildSqlForDbView(dbView, whereExtra, orderBy)
      dbViewName = dbView
      aliasBase = dbView.split('.').slice(-1)[0]
      whereForValidation = whereExtra
    } else {
      if (!adHocSql.trim()) { setError('SQL boş olamaz'); return }
      sql = adHocSql.trim()
      const m = sql.match(/FROM\s+\[?(\w+)\]?\.\[?(\w+)\]?/i) || sql.match(/FROM\s+\[?(\w+)\]?/i)
      aliasBase = m ? (m[2] || m[1]) : 'sql'
      // Ad-hoc SQL'de WHERE'i kullanicinin yazdigi tam metinden cikar
      whereForValidation = parseWhereFromSql(sql)
      dbViewName = m ? (m[2] ? `${m[1]}.${m[2]}` : m[1]) : null
    }

    // ── WHERE kolon validation ───────────────────────────────────────────────
    // <col> = @<param> bind'lerini cikar, view kolon listesi ile karsilastir.
    // dbView yoksa (ad-hoc, FROM parse edilemedi) skip — kullanici sorumlu.
    // Kolon listesi {colName, displayName}[] formatinda doner — colName ile karsilastir.
    const bindings = parseWhereBindings(whereForValidation)
    if (bindings.length > 0 && dbViewName) {
      try {
        const cols = (await getDbViewColumns(dbViewName)) || []
        const colNamesLc = cols.map(c => String(c?.colName || c).toLowerCase())
        const missing = bindings.filter(b => !colNamesLc.includes(b.column.toLowerCase()))
        if (missing.length > 0) {
          const expected = bindings.map(b => b.column).join(', ')
          const missingNames = missing.map(b => b.column).join(', ')
          setError(
            `View "${dbViewName}" bağlanamadı.\n` +
            `Beklenen kolonlar: ${expected}\n` +
            `Eksik: ${missingNames}`
          )
          return
        }
      } catch (e) {
        // Kolon listesi alinamadi → uyari ama bloklamayalim (network hatasi vb.)
        console.warn('[DocDesigner] Kolon listesi alinamadi, validation atlandi:', e)
      }
    }

    const finalAlias = makeUniqueAlias(aliasBase)
    onAdd({
      alias: finalAlias, role: autoRole,
      viewId: null, adHocSql: sql, dbView: mode === 'db' ? dbViewName : null,
      joinOn: null, parentAlias: null, ordinal: totalExisting,
    })
    setFlash(finalAlias); setTimeout(() => setFlash(null), 2000)
    resetSourceForm()
  }

  const handleDeleteSource = alias => {
    if (confirmingAlias !== alias) { setConfirmingAlias(alias); return }
    onDelete?.(alias)
    setConfirmingAlias(null)
    // Eşlemelerden de kaldır
    setMapRows(rs => rs.filter(r => r.altAlias !== alias && r.anaAlias !== alias))
  }

  // ── Eşleme: view seçildiğinde kolonları yükle ──────────────────────────────
  const ensureColumns = async (alias) => {
    if (!alias || colCache[alias]) return
    const src = existingSources.find(s => s.alias === alias)
    if (!src) return
    const viewName = src.dbView ?? extractFromSql(src.adHocSql)
    if (!viewName || viewName === 'ad-hoc SQL') return   // ad-hoc için autocomplete yok
    try {
      const cols = await getDbViewColumns(viewName)
      setColCache(c => ({ ...c, [alias]: cols.map(x => typeof x === 'string' ? x : (x.colName ?? x.name ?? '')) }))
    } catch {}
  }

  useEffect(() => { ensureColumns(mapAna) }, [mapAna])     // eslint-disable-line
  useEffect(() => { ensureColumns(mapAlt) }, [mapAlt])     // eslint-disable-line

  // Eşleme inline "+ Ekle"
  const handleAddMapping = () => {
    setError(null)
    if (!mapAna || !mapAnaCol || !mapAlt || !mapAltCol) {
      setError('Tüm alanları seçin: Ana View / Ana Kolon / Alt View / Alt Kolon')
      return
    }
    if (mapAna === mapAlt) { setError('Ana ve Alt view aynı olamaz'); return }

    const newRow = { anaAlias: mapAna, anaCol: mapAnaCol, altAlias: mapAlt, altCol: mapAltCol }
    if (editingMapIdx >= 0) {
      // Düzenleme modunda mevcut satırı güncelle
      setMapRows(rs => rs.map((r, i) => i === editingMapIdx ? newRow : r))
      setEditingMapIdx(-1)
    } else {
      setMapRows(rs => [...rs, newRow])
    }
    setMapAnaCol(''); setMapAltCol('')
  }

  const handleRemoveMapping = idx => {
    setMapRows(rs => rs.filter((_, i) => i !== idx))
    if (editingMapIdx === idx) {
      setEditingMapIdx(-1); setMapAnaCol(''); setMapAltCol('')
    }
  }

  // Çift tık ile satırı forma yükle
  const handleEditMapping = idx => {
    const r = mapRows[idx]
    setMapAna(r.anaAlias); setMapAnaCol(r.anaCol)
    setMapAlt(r.altAlias); setMapAltCol(r.altCol)
    setEditingMapIdx(idx)
  }

  // Eşlemeleri kaydet (Kapat'a basınca veya manuel "Eşlemeleri Uygula")
  // Her child alias için JoinOn/ParentAlias güncellenir.
  const applyMappings = () => {
    // Alt view bazında grupla
    const byAlt = new Map()
    for (const r of mapRows) {
      if (!byAlt.has(r.altAlias)) byAlt.set(r.altAlias, [])
      byAlt.get(r.altAlias).push(r)
    }
    // existingSources'taki her source için: ya mapRows'ta varsa parent+join güncelle, ya yoksa temizle
    for (const src of existingSources) {
      if (byAlt.has(src.alias)) {
        const grp = byAlt.get(src.alias)
        const anaAlias = grp[0].anaAlias
        // Tüm satırlar aynı anaAlias'a sahip olmalı (UI bunu garantiler)
        const pairs = grp.map(g => ({ p: g.anaCol, c: g.altCol }))
        const joinOn = pairs.length === 1 && pairs[0].p === pairs[0].c
          ? pairs[0].p
          : JSON.stringify(pairs)
        if (src.parentAlias !== anaAlias || src.joinOn !== joinOn) {
          onUpdate?.(src.alias, { parentAlias: anaAlias, joinOn })
        }
      } else {
        // Eşlemesi kaldırıldı
        if (src.parentAlias || src.joinOn) {
          onUpdate?.(src.alias, { parentAlias: null, joinOn: null })
        }
      }
    }
  }

  const handleClose = () => { applyMappings(); onClose() }

  // ── Render: tab içerikleri ─────────────────────────────────────────────────
  const renderSource = () => (
    <>
      <div style={{ display: 'flex', borderBottom: '1px solid var(--dd-border, #e5e7eb)', marginBottom: 14 }}>
        {MODES.map(m => (
          <button key={m.key} type="button" onClick={() => setMode(m.key)} style={{
            padding: '6px 14px', border: 'none', background: 'none', cursor: 'pointer',
            fontSize: 12, fontWeight: mode === m.key ? 700 : 400,
            color: mode === m.key ? 'var(--dd-accent, #6366f1)' : 'var(--dd-text-muted, #6b7280)',
            borderBottom: mode === m.key ? '2px solid var(--dd-accent, #6366f1)' : '2px solid transparent',
            marginBottom: -1,
          }}>{m.label}</button>
        ))}
      </div>

      {mode === 'db' && (
        <>
          <label style={lbl}>Veritabanı View Seç</label>
          {loadingViews
            ? <div style={dim}>Yükleniyor…</div>
            : dbViews.length === 0
              ? <div style={{ ...dim, color: '#f59e0b' }}>Hiç view bulunamadı. Ad-hoc SQL modunu kullanın.</div>
              : <div style={{ display: 'flex', gap: 8, alignItems: 'flex-start' }}>
                  <div style={{ flex: 1 }}>
                    <ViewAutocomplete views={dbViews} selected={dbView} onSelect={setDbView} />
                  </div>
                  <button type="button" onClick={handleAddSource}
                          disabled={!dbView}
                          style={{
                            padding: '7px 16px', borderRadius: 6, border: 'none',
                            background: dbView ? 'var(--dd-accent, #6366f1)' : 'var(--dd-border, #d1d5db)',
                            color: '#fff', cursor: dbView ? 'pointer' : 'not-allowed',
                            fontSize: 12, fontWeight: 600, whiteSpace: 'nowrap',
                          }}>+ Ekle</button>
                </div>
          }
        </>
      )}

      {mode === 'sql' && (
        <>
          <label style={lbl}>SQL (yalnızca SELECT)</label>
          <textarea value={adHocSql} onChange={e => setAdHocSql(e.target.value)} rows={6}
            style={{ ...inp, resize: 'vertical', fontFamily: 'monospace', fontSize: 11 }}
            placeholder={'SELECT * FROM [dbo].[ViewName]\nWHERE BelgeId = @DocumentId'} />
          <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: 8 }}>
            <button type="button" onClick={handleAddSource}
                    disabled={!adHocSql.trim()}
                    style={{
                      padding: '7px 16px', borderRadius: 6, border: 'none',
                      background: adHocSql.trim() ? 'var(--dd-accent, #6366f1)' : 'var(--dd-border, #d1d5db)',
                      color: '#fff', cursor: adHocSql.trim() ? 'pointer' : 'not-allowed',
                      fontSize: 12, fontWeight: 600,
                    }}>+ Ekle</button>
          </div>
        </>
      )}

      {flash && (
        <div style={{
          marginTop: 10, padding: '6px 10px', borderRadius: 6,
          background: 'rgba(16,185,129,0.12)', border: '1px solid rgba(16,185,129,0.4)',
          color: '#047857', fontSize: 11.5,
        }}>✓ <strong>{flash}</strong> eklendi</div>
      )}

      {/* Eklenmiş Kaynaklar — alias chip + uygulanacak WHERE/ORDER BY önizleme.
          2026-06-03: Kullanici view secince hangi filtrenin uygulanacagini gormeli
          (ornek: BelgeId = @DocumentId). Onceden sadece chip vardi — filtre Filtre
          tab'inda gizli kaliyordu. Artik her kaynagin altinda monospace satir gosterir. */}
      {totalExisting > 0 && (
        <div style={{ marginTop: 20, paddingTop: 14, borderTop: '1px solid var(--dd-border, #e5e7eb)' }}>
          <div style={{
            fontSize: 10.5, fontWeight: 700, color: 'var(--dd-text-muted, #6b7280)',
            textTransform: 'uppercase', letterSpacing: '0.5px', marginBottom: 8,
          }}>Eklenmiş Kaynaklar ({totalExisting})</div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
            {existingSources.map(src => {
              const isConfirming = confirmingAlias === src.alias
              const sourceName = src.dbView ?? extractFromSql(src.adHocSql)
              const whereStr = parseWhereFromSql(src.adHocSql)
              const orderStr = parseOrderFromSql(src.adHocSql)
              return (
                <div key={src.alias}
                  style={{
                    display: 'flex', flexDirection: 'column', gap: 4,
                    padding: '8px 10px',
                    background: isConfirming ? 'rgba(220,38,38,0.06)' : 'var(--dd-accent-soft, rgba(99,102,241,0.06))',
                    border: `1px solid ${isConfirming ? '#dc2626' : 'var(--dd-border, #e5e7eb)'}`,
                    borderRadius: 8,
                  }}>
                  {/* Üst satır: alias chip + sil */}
                  <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                    <span title={sourceName + (src.parentAlias ? `  •  ⇆ ${src.parentAlias} (${src.joinOn})` : '')}
                      style={{
                        display: 'inline-flex', alignItems: 'center', gap: 4,
                        padding: '3px 8px',
                        background: 'var(--dd-accent-soft, rgba(99,102,241,0.12))',
                        border: '1px solid var(--dd-accent, #6366f1)',
                        borderRadius: 12, fontSize: 11.5, fontWeight: 600,
                        color: isConfirming ? '#dc2626' : 'var(--dd-accent, #4338ca)',
                        fontFamily: 'ui-monospace, Menlo, Consolas, monospace',
                      }}>{src.alias}</span>
                    <span style={{ flex: 1, fontSize: 10.5, color: 'var(--dd-text-muted, #6b7280)' }}>
                      {sourceName}{src.parentAlias ? `  ⇆ ${src.parentAlias} (${src.joinOn})` : ''}
                    </span>
                    {isConfirming ? (
                      <>
                        <span style={{ fontSize: 10, fontWeight: 600, color: '#dc2626' }}>Sil?</span>
                        <button type="button" onClick={() => handleDeleteSource(src.alias)} title="Onayla"
                                style={{ ...tagBtn, background: '#dc2626', borderColor: '#dc2626', color: '#fff' }}>✓</button>
                        <button type="button" onClick={() => setConfirmingAlias(null)} title="Vazgeç"
                                style={tagBtn}>×</button>
                      </>
                    ) : (
                      <button type="button" onClick={() => setConfirmingAlias(src.alias)} title="Bu kaynağı sil"
                              style={{ ...tagBtn, color: 'var(--dd-text-muted, #9ca3af)' }}
                              onMouseEnter={e => e.currentTarget.style.color = '#dc2626'}
                              onMouseLeave={e => e.currentTarget.style.color = 'var(--dd-text-muted, #9ca3af)'}>×</button>
                    )}
                  </div>
                  {/* Alt satır: WHERE + ORDER BY önizleme + mapping listesi */}
                  <div style={{
                    display: 'flex', flexDirection: 'column', gap: 4,
                    paddingLeft: 2, fontFamily: 'ui-monospace, Menlo, Consolas, monospace',
                    fontSize: 11, lineHeight: 1.45,
                  }}>
                    {whereStr ? (
                      <div title="Bu view sorgulanırken uygulanacak filtre"
                           style={{ color: 'var(--dd-text, #374151)', wordBreak: 'break-word' }}>
                        <span style={{ color: 'var(--dd-accent, #6366f1)', fontWeight: 700 }}>WHERE </span>
                        {whereStr}
                      </div>
                    ) : (
                      <div style={{ color: 'var(--dd-text-muted, #9ca3af)', fontStyle: 'italic' }}>
                        (WHERE yok — tüm satırlar gelir)
                      </div>
                    )}
                    {orderStr && (
                      <div style={{ color: 'var(--dd-text, #374151)', wordBreak: 'break-word' }}>
                        <span style={{ color: 'var(--dd-accent, #6366f1)', fontWeight: 700 }}>ORDER BY </span>
                        {orderStr}
                      </div>
                    )}
                    {/* Mapping listesi: view kolonu ↔ runtime parametresi */}
                    {(() => {
                      const bindings = parseWhereBindings(whereStr)
                      if (bindings.length === 0) return null
                      return (
                        <div style={{
                          marginTop: 4, paddingLeft: 10,
                          borderLeft: '2px solid var(--dd-accent-soft, rgba(99,102,241,0.25))',
                          display: 'flex', flexDirection: 'column', gap: 2,
                        }}>
                          <div style={{
                            fontSize: 9.5, fontWeight: 700, letterSpacing: '0.4px',
                            color: 'var(--dd-text-muted, #6b7280)', textTransform: 'uppercase',
                            fontFamily: 'system-ui, sans-serif',
                          }}>Eşleşmeler</div>
                          {bindings.map((b, i) => {
                            const desc = PARAM_DESCRIPTIONS[b.param.toLowerCase()]
                            return (
                              <div key={i} style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
                                <span style={{
                                  padding: '1px 6px', borderRadius: 4,
                                  background: 'rgba(99,102,241,0.10)',
                                  color: 'var(--dd-accent, #4338ca)', fontWeight: 600,
                                }}>view.{b.column}</span>
                                <span style={{ color: 'var(--dd-text-muted, #9ca3af)', fontSize: 12 }}>↔</span>
                                <span style={{
                                  padding: '1px 6px', borderRadius: 4,
                                  background: 'rgba(16,185,129,0.10)',
                                  color: '#047857', fontWeight: 600,
                                }}>@{b.param}</span>
                                {desc && (
                                  <span style={{
                                    color: 'var(--dd-text-muted, #6b7280)', fontSize: 10.5,
                                    fontFamily: 'system-ui, sans-serif', fontStyle: 'italic',
                                  }}>— {desc}</span>
                                )}
                              </div>
                            )
                          })}
                        </div>
                      )
                    })()}
                  </div>
                </div>
              )
            })}
          </div>
          <div style={{
            marginTop: 8, fontSize: 10.5, color: 'var(--dd-text-muted, #9ca3af)',
            lineHeight: 1.5,
          }}>
            <strong>İpucu:</strong> WHERE'i değiştirmek için <em>Filtre</em> sekmesine geçin.
            {' '}<code style={{ fontFamily: 'ui-monospace, Menlo, Consolas, monospace' }}>@DocumentId</code> /
            {' '}<code style={{ fontFamily: 'ui-monospace, Menlo, Consolas, monospace' }}>@KalemId</code> gibi parametreler runtime'da otomatik dolar.
          </div>
        </div>
      )}
    </>
  )

  // Mevcut view'in adHocSql'inden WHERE/ORDER BY parse — edit için
  const parseWhereFromSql = (sql) => {
    if (!sql) return ''
    // "WHERE ... ORDER BY ..." veya "WHERE ... " (sona kadar)
    const m = sql.match(/\bWHERE\b\s+(.*?)(?:\bORDER\s+BY\b|$)/is)
    return m ? m[1].trim() : ''
  }
  const parseOrderFromSql = (sql) => {
    if (!sql) return ''
    const m = sql.match(/\bORDER\s+BY\b\s+(.*?)$/is)
    return m ? m[1].trim() : ''
  }

  // 2026-06-03: WHERE klozundan "<view_kolonu> = @<runtime_parametresi>" ciftlerini
  // cikar. View kolon validation + UI'da mapping listesi gosterimi icin kullanilir.
  // Desteklenen formatlar:  Id = @DocumentId  |  [Id] = @DocumentId  |  c.Id = @x
  // Sol taraf bir kolon ifadesi (alias.col, [col]); sag taraf @parametre
  const parseWhereBindings = (whereStr) => {
    if (!whereStr) return []
    const re = /(?:\[?(\w+)\]?\.)?\[?(\w+)\]?\s*=\s*@(\w+)/g
    const out = []
    let m
    while ((m = re.exec(whereStr)) !== null) {
      out.push({
        tableAlias: m[1] || '',     // varsa "a.col"un "a"si
        column:     m[2],           // kolon adi
        param:      m[3],           // @parametre adi
      })
    }
    return out
  }

  // Runtime'da otomatik bind edilen parametreler — açıklamaları ipucu için
  const PARAM_DESCRIPTIONS = {
    documentid:   'Belge Id (render edilen master kayıt)',
    kalemid:      'Kalem Id (detail iterasyonunda satır)',
    companyid:    'Aktif şirket Id',
    kullaniciid:  'Giriş yapan kullanıcı Id',
    userid:       'Giriş yapan kullanıcı Id',
  }

  // SQL'i yeniden inşa et: base SELECT (mevcut FROM kısmı korunur) + yeni WHERE + ORDER BY
  const rebuildSqlWith = (originalSql, newWhere, newOrder) => {
    if (!originalSql) return ''
    // FROM kısmını çıkar
    const fromMatch = originalSql.match(/(SELECT[\s\S]*?\bFROM\b\s+\[?\w+\]?\.\[?\w+\]?)/i)
    let base = fromMatch ? fromMatch[1] : originalSql.split(/\bWHERE\b|\bORDER\s+BY\b/i)[0].trim()
    if (newWhere.trim()) base += ` WHERE ${newWhere.trim()}`
    if (newOrder.trim()) base += ` ORDER BY ${newOrder.trim()}`
    return base
  }

  // Filtre target değişince mevcut WHERE değerini yükle
  useEffect(() => {
    if (!filterTarget) { setFilterValueForExisting(''); return }
    const src = existingSources.find(s => s.alias === filterTarget)
    setFilterValueForExisting(parseWhereFromSql(src?.adHocSql))
  }, [filterTarget])   // eslint-disable-line

  useEffect(() => {
    if (!orderTarget) { setOrderValueForExisting(''); return }
    const src = existingSources.find(s => s.alias === orderTarget)
    setOrderValueForExisting(parseOrderFromSql(src?.adHocSql))
  }, [orderTarget])    // eslint-disable-line

  const applyFilterToExisting = () => {
    const src = existingSources.find(s => s.alias === filterTarget)
    if (!src) return
    const existingOrder = parseOrderFromSql(src.adHocSql)
    const newSql = rebuildSqlWith(src.adHocSql, filterValueForExisting, existingOrder)
    onUpdate?.(filterTarget, { adHocSql: newSql })
    setFlash(`${filterTarget} filtresi güncellendi`)
    setTimeout(() => setFlash(null), 2000)
  }

  const applyOrderToExisting = () => {
    const src = existingSources.find(s => s.alias === orderTarget)
    if (!src) return
    const existingWhere = parseWhereFromSql(src.adHocSql)
    const newSql = rebuildSqlWith(src.adHocSql, existingWhere, orderValueForExisting)
    onUpdate?.(orderTarget, { adHocSql: newSql })
    setFlash(`${orderTarget} sıralaması güncellendi`)
    setTimeout(() => setFlash(null), 2000)
  }

  const renderFilter = () => (
    <>
      <label style={lbl}>Hedef View</label>
      <select value={filterTarget} onChange={e => setFilterTarget(e.target.value)}
              style={{ ...inp, cursor: 'pointer', marginBottom: 14 }}>
        <option value="">— Yeni eklenecek view için —</option>
        {existingSources.map(s => (
          <option key={s.alias} value={s.alias}>{s.alias}</option>
        ))}
      </select>

      {filterTarget === '' ? (
        <>
          <label style={lbl}>Ek WHERE koşulu</label>
          <textarea value={whereExtra} onChange={e => setWhereExtra(e.target.value)} rows={6}
            style={{ ...inp, resize: 'vertical', fontFamily: 'monospace', fontSize: 11 }}
            placeholder="BelgeId = @DocumentId AND KalemId = @KalemId" />
          <div style={hintTxt}>
            Bir sonraki "Kaynak → + Ekle" işleminde yeni view'a uygulanacak.
          </div>
        </>
      ) : (
        <>
          <label style={lbl}>WHERE koşulu — <strong>{filterTarget}</strong> için</label>
          <textarea value={filterValueForExisting} onChange={e => setFilterValueForExisting(e.target.value)} rows={6}
            style={{ ...inp, resize: 'vertical', fontFamily: 'monospace', fontSize: 11 }}
            placeholder="BelgeId = @DocumentId" />
          <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: 8 }}>
            <button type="button" onClick={applyFilterToExisting}
                    style={{
                      padding: '7px 16px', borderRadius: 6, border: 'none',
                      background: 'var(--dd-accent, #6366f1)', color: '#fff',
                      cursor: 'pointer', fontSize: 12, fontWeight: 600,
                    }}>Uygula</button>
          </div>
          <div style={hintTxt}>
            Bu view'in SQL'i WHERE kısmı değiştirilerek güncellenir. ORDER BY kısmı korunur.
          </div>
        </>
      )}
    </>
  )

  const renderOrder = () => (
    <>
      <label style={lbl}>Hedef View</label>
      <select value={orderTarget} onChange={e => setOrderTarget(e.target.value)}
              style={{ ...inp, cursor: 'pointer', marginBottom: 14 }}>
        <option value="">— Yeni eklenecek view için —</option>
        {existingSources.map(s => (
          <option key={s.alias} value={s.alias}>{s.alias}</option>
        ))}
      </select>

      {orderTarget === '' ? (
        <>
          <label style={lbl}>ORDER BY</label>
          <input type="text" value={orderBy} onChange={e => setOrderBy(e.target.value)}
            placeholder="KalemSiraNo ASC, SiraNo ASC"
            style={{ ...inp, fontFamily: 'monospace' }} />
          <div style={hintTxt}>
            Bir sonraki "Kaynak → + Ekle" işleminde yeni view'a uygulanacak.
          </div>
        </>
      ) : (
        <>
          <label style={lbl}>ORDER BY — <strong>{orderTarget}</strong> için</label>
          <input type="text" value={orderValueForExisting} onChange={e => setOrderValueForExisting(e.target.value)}
            placeholder="KalemSiraNo ASC, SiraNo ASC"
            style={{ ...inp, fontFamily: 'monospace' }} />
          <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: 8 }}>
            <button type="button" onClick={applyOrderToExisting}
                    style={{
                      padding: '7px 16px', borderRadius: 6, border: 'none',
                      background: 'var(--dd-accent, #6366f1)', color: '#fff',
                      cursor: 'pointer', fontSize: 12, fontWeight: 600,
                    }}>Uygula</button>
          </div>
          <div style={hintTxt}>
            Bu view'in SQL'i ORDER BY kısmı değiştirilerek güncellenir. WHERE kısmı korunur.
          </div>
        </>
      )}
    </>
  )

  const renderMapping = () => {
    if (totalExisting < 2) {
      return (
        <div style={{ ...dim, padding: 24, textAlign: 'center' }}>
          Eşleme için en az iki veri kaynağı eklenmiş olmalı.
          <br/>Şu an: {totalExisting} kaynak.
        </div>
      )
    }
    const anaCols = colCache[mapAna] ?? []
    const altCols = colCache[mapAlt] ?? []

    return (
      <>
        {/* Form bloğu */}
        <div style={{
          padding: '12px',
          background: 'var(--dd-surface-alt, #f8f9fb)',
          border: '1px solid var(--dd-border, #e5e7eb)',
          borderRadius: 8, marginBottom: 14,
        }}>
          <div style={{
            display: 'grid', gridTemplateColumns: '70px 1fr 1fr', gap: 8,
            alignItems: 'center', fontSize: 10.5, color: 'var(--dd-text-muted, #888)',
            fontWeight: 700, textTransform: 'uppercase', letterSpacing: 0.3,
            paddingBottom: 6, marginBottom: 6,
            borderBottom: '1px solid var(--dd-border, #e5e7eb)',
          }}>
            <span/><span>View Adı</span><span>Kolon Adı</span>
          </div>

          {/* Ana satırı */}
          <div style={{ display: 'grid', gridTemplateColumns: '70px 1fr 1fr', gap: 8, alignItems: 'center', marginBottom: 8 }}>
            <span style={{ fontWeight: 700, fontSize: 12, color: 'var(--dd-accent, #4338ca)' }}>Ana</span>
            <select value={mapAna} onChange={e => { setMapAna(e.target.value); setMapAnaCol('') }}
                    style={{ ...inp, cursor: 'pointer' }}>
              <option value="">— Seçin —</option>
              {existingSources.map(s => (
                <option key={s.alias} value={s.alias}>{s.alias}</option>
              ))}
            </select>
            <ColumnAutocomplete columns={anaCols} value={mapAnaCol} onSelect={setMapAnaCol}
                                disabled={!mapAna} placeholder={mapAna ? 'Kolon seç…' : 'Önce view'} />
          </div>

          {/* Alt satırı */}
          <div style={{ display: 'grid', gridTemplateColumns: '70px 1fr 1fr', gap: 8, alignItems: 'center', marginBottom: 10 }}>
            <span style={{ fontWeight: 700, fontSize: 12, color: 'var(--dd-text-muted, #6b7280)' }}>Alt</span>
            <select value={mapAlt} onChange={e => { setMapAlt(e.target.value); setMapAltCol('') }}
                    style={{ ...inp, cursor: 'pointer' }}>
              <option value="">— Seçin —</option>
              {existingSources.filter(s => s.alias !== mapAna).map(s => (
                <option key={s.alias} value={s.alias}>{s.alias}</option>
              ))}
            </select>
            <ColumnAutocomplete columns={altCols} value={mapAltCol} onSelect={setMapAltCol}
                                disabled={!mapAlt} placeholder={mapAlt ? 'Kolon seç…' : 'Önce view'} />
          </div>

          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8 }}>
            {editingMapIdx >= 0 && (
              <button type="button" onClick={() => {
                setEditingMapIdx(-1); setMapAnaCol(''); setMapAltCol('')
              }} style={btnSecondary}>İptal</button>
            )}
            <button type="button" onClick={handleAddMapping}
                    style={{
                      padding: '7px 16px', borderRadius: 6, border: 'none',
                      background: 'var(--dd-accent, #6366f1)', color: '#fff',
                      cursor: 'pointer', fontSize: 12, fontWeight: 600,
                    }}>{editingMapIdx >= 0 ? 'Güncelle' : '+ Ekle'}</button>
          </div>
        </div>

        {/* Eklenmiş eşlemeler tablosu */}
        {mapRows.length > 0 ? (
          <div style={{
            border: '1px solid var(--dd-border, #e5e7eb)',
            borderRadius: 8, overflow: 'hidden',
          }}>
            <div style={{
              display: 'grid', gridTemplateColumns: '1fr 1fr 1fr 1fr 30px',
              padding: '6px 10px',
              background: 'var(--dd-surface-alt, #f8f9fb)',
              borderBottom: '1px solid var(--dd-border, #e5e7eb)',
              fontSize: 10, fontWeight: 700, letterSpacing: 0.3,
              color: 'var(--dd-text-muted, #6b7280)', textTransform: 'uppercase',
            }}>
              <span>Ana View</span><span>Ana Kolon</span>
              <span>Alt View</span><span>Alt Kolon</span><span/>
            </div>
            {mapRows.map((r, i) => (
              <div key={i}
                   onDoubleClick={() => handleEditMapping(i)}
                   title="Çift tıkla düzenle"
                   style={{
                     display: 'grid', gridTemplateColumns: '1fr 1fr 1fr 1fr 30px',
                     padding: '7px 10px', cursor: 'pointer',
                     borderBottom: i < mapRows.length - 1 ? '1px solid var(--dd-border, #f3f4f6)' : 'none',
                     fontSize: 11.5, alignItems: 'center',
                     background: editingMapIdx === i ? 'var(--dd-accent-soft, rgba(99,102,241,0.12))' : 'transparent',
                     fontFamily: 'ui-monospace, Menlo, Consolas, monospace',
                   }}
                   onMouseEnter={e => { if (editingMapIdx !== i) e.currentTarget.style.background = 'var(--dd-surface-alt, #fafbfc)' }}
                   onMouseLeave={e => { if (editingMapIdx !== i) e.currentTarget.style.background = 'transparent' }}>
                <span style={{ color: 'var(--dd-accent, #4338ca)', fontWeight: 600 }}>{r.anaAlias}</span>
                <span>{r.anaCol}</span>
                <span style={{ color: 'var(--dd-text-muted, #6b7280)' }}>{r.altAlias}</span>
                <span>{r.altCol}</span>
                <button type="button" onClick={() => handleRemoveMapping(i)} title="Sil"
                        style={{ border: 'none', background: 'transparent', color: 'var(--dd-text-muted, #9ca3af)',
                                  cursor: 'pointer', fontSize: 14, padding: 0, lineHeight: 1 }}
                        onMouseEnter={e => e.currentTarget.style.color = '#dc2626'}
                        onMouseLeave={e => e.currentTarget.style.color = 'var(--dd-text-muted, #9ca3af)'}>×</button>
              </div>
            ))}
          </div>
        ) : (
          <div style={{ ...dim, textAlign: 'center', padding: 20 }}>
            Henüz eşleme yok. Yukarıdan view + kolon seçip "+ Ekle" deyin.
          </div>
        )}

        <div style={{ ...hintTxt, marginTop: 10 }}>
          Eşleme satırları "Kapat" deyince mevcut veri kaynaklarına uygulanır.
          Aynı Alt view için birden fazla satır → composite key (multi-column join).
          Çift tıkla satırı düzenle.
        </div>
      </>
    )
  }

  return (
    <div
      style={{ position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.55)', zIndex: 1000, display: 'flex', alignItems: 'center', justifyContent: 'center' }}
      onClick={e => { if (e.target === e.currentTarget) handleClose() }}
    >
      <div style={{
        background: 'var(--dd-surface, #fff)', color: 'var(--dd-text, #111)',
        border: '1px solid var(--dd-border, #e5e7eb)',
        borderRadius: 12, width: 780, height: 600,   // sabit yükseklik — tab değişiminde modal büyüyüp küçülmesin
        display: 'flex', flexDirection: 'column',
        boxShadow: '0 20px 60px rgba(0,0,0,0.35)',
      }}>
        <div style={{
          display: 'flex', justifyContent: 'space-between', alignItems: 'center',
          padding: '14px 20px', borderBottom: '1px solid var(--dd-border, #e5e7eb)',
        }}>
          <h3 style={{ margin: 0, fontSize: 15, fontWeight: 700 }}>Veri Kaynakları</h3>
          <button onClick={handleClose} style={{ background: 'none', border: 'none', fontSize: 20, cursor: 'pointer', color: 'var(--dd-text-muted, #999)', lineHeight: 1 }}>×</button>
        </div>

        <div style={{ display: 'flex', flex: 1, minHeight: 0 }}>
          {/* Sol tab menü */}
          <div style={{
            width: 180, borderRight: '1px solid var(--dd-border, #e5e7eb)',
            background: 'var(--dd-surface-alt, #f8f9fb)',
            padding: '12px 8px', display: 'flex', flexDirection: 'column',
          }}>
            {TABS.map(t => {
              const active = tab === t.key
              const disabled = t.key === 'mapping' && totalExisting < 2
              return (
                <button key={t.key} type="button" disabled={disabled} onClick={() => setTab(t.key)}
                        style={{
                          display: 'flex', alignItems: 'center', gap: 8, width: '100%',
                          padding: '8px 12px', marginBottom: 2,
                          background: active ? 'var(--dd-accent-soft, rgba(99,102,241,0.12))' : 'transparent',
                          border: 'none', borderRadius: 6,
                          color: disabled ? 'var(--dd-text-muted, #9ca3af)'
                            : active ? 'var(--dd-accent, #6366f1)' : 'var(--dd-text, #374151)',
                          fontSize: 12.5, fontWeight: active ? 700 : 500,
                          cursor: disabled ? 'not-allowed' : 'pointer',
                          opacity: disabled ? 0.5 : 1, textAlign: 'left',
                          borderLeft: active ? '3px solid var(--dd-accent, #6366f1)' : '3px solid transparent',
                        }}>
                  <span style={{ fontSize: 14, width: 14, textAlign: 'center' }}>{t.icon}</span>
                  <span>{t.label}</span>
                </button>
              )
            })}
            <div style={{
              marginTop: 'auto', padding: '10px 10px 4px',
              fontSize: 10.5, color: 'var(--dd-text-muted, #888)',
              borderTop: '1px solid var(--dd-border, #e5e7eb)',
            }}>
              Sonraki kaynak rolü: <strong style={{ color: 'var(--dd-text, #374151)' }}>{autoRole}</strong>
            </div>
          </div>

          {/* Sağ — form içerik */}
          <div style={{ flex: 1, padding: '16px 20px', overflowY: 'auto', minWidth: 0 }}>
            {tab === 'source'  && renderSource()}
            {tab === 'filter'  && renderFilter()}
            {tab === 'order'   && renderOrder()}
            {tab === 'mapping' && renderMapping()}

            {error && (
              <div style={{
                marginTop: 14, padding: '10px 12px', borderRadius: 6,
                background: 'rgba(220,38,38,0.08)', border: '1px solid rgba(220,38,38,0.4)',
                color: '#dc2626', fontSize: 12, lineHeight: 1.55,
                whiteSpace: 'pre-wrap', fontFamily: 'system-ui, sans-serif',
              }}>{error}</div>
            )}
          </div>
        </div>

        {/* Footer — yalnızca Kapat */}
        <div style={{
          display: 'flex', justifyContent: 'flex-end',
          padding: '12px 20px', borderTop: '1px solid var(--dd-border, #e5e7eb)',
          background: 'var(--dd-surface-alt, #fafbfc)',
          borderBottomLeftRadius: 12, borderBottomRightRadius: 12,
        }}>
          <button type="button" onClick={handleClose} style={btnPrimary}>Kapat</button>
        </div>
      </div>
    </div>
  )
}

function extractFromSql(sql) {
  if (!sql) return ''
  const m = sql.match(/FROM\s+\[?(\w+)\]?\.\[?(\w+)\]?/i) || sql.match(/FROM\s+\[?(\w+)\]?/i)
  return m ? `${m[1]}${m[2] ? '.' + m[2] : ''}` : 'ad-hoc SQL'
}

function ViewAutocomplete({ views, selected, onSelect }) {
  const [query, setQuery]   = useState('')
  const [open,  setOpen]    = useState(false)
  const [focusIdx, setFocusIdx] = useState(0)
  const inputRef = useRef(null)

  const trimmed = query.trim().toLowerCase()
  const filtered = trimmed.length === 0
    ? views.slice(0, 50)
    : views.filter(v => v.fullName.toLowerCase().includes(trimmed)).slice(0, 50)

  const pick = v => { onSelect(v.fullName); setQuery(''); setOpen(false) }
  const clear = () => { onSelect(''); setQuery(''); setOpen(true); setTimeout(() => inputRef.current?.focus(), 0) }

  if (selected) {
    return (
      <div style={{
        display: 'inline-flex', alignItems: 'center', gap: 6,
        padding: '6px 10px', border: '1px solid var(--dd-accent, #6366f1)',
        background: 'var(--dd-accent-soft, rgba(99,102,241,0.08))',
        borderRadius: 6, fontSize: 12.5, color: 'var(--dd-text, #4338ca)',
        fontFamily: 'ui-monospace, Menlo, Consolas, monospace',
      }}>
        <span>{selected}</span>
        <span onClick={clear}
              style={{ cursor: 'pointer', color: 'var(--dd-text-muted, #9ca3af)', fontSize: 16, lineHeight: 1, padding: '0 4px', userSelect: 'none' }}
              onMouseEnter={e => e.currentTarget.style.color = '#ef4444'}
              onMouseLeave={e => e.currentTarget.style.color = 'var(--dd-text-muted, #9ca3af)'}
              title="Temizle">×</span>
      </div>
    )
  }

  return (
    <div style={{ position: 'relative' }}>
      <input ref={inputRef} type="text" value={query} autoComplete="off"
             placeholder="View adı ile ara…"
             onChange={e => { setQuery(e.target.value); setOpen(true); setFocusIdx(0) }}
             onFocus={() => setOpen(true)}
             onBlur={() => setTimeout(() => setOpen(false), 150)}
             onKeyDown={e => {
               if (!open) return
               if (e.key === 'ArrowDown') { e.preventDefault(); setFocusIdx(i => Math.min(filtered.length - 1, i + 1)) }
               else if (e.key === 'ArrowUp') { e.preventDefault(); setFocusIdx(i => Math.max(0, i - 1)) }
               else if (e.key === 'Enter') { e.preventDefault(); if (filtered[focusIdx]) pick(filtered[focusIdx]) }
               else if (e.key === 'Escape') { setOpen(false) }
             }} style={inp} />
      {open && (
        <div style={{
          position: 'absolute', left: 0, right: 0, top: '100%', zIndex: 30,
          background: 'var(--dd-surface, #fff)', border: '1px solid var(--dd-border, #e5e7eb)',
          borderRadius: 6, boxShadow: '0 8px 24px rgba(0,0,0,0.3)',
          maxHeight: 240, overflowY: 'auto', marginTop: 2,
        }}>
          {filtered.length === 0
            ? <div style={{ padding: '10px 12px', color: 'var(--dd-text-muted, #9ca3af)', fontSize: 12, textAlign: 'center' }}>Eşleşen view bulunamadı</div>
            : filtered.map((v, i) => (
                <div key={v.fullName} onMouseDown={e => { e.preventDefault(); pick(v) }} onMouseEnter={() => setFocusIdx(i)}
                     style={{
                       padding: '7px 11px', fontSize: 12.5, cursor: 'pointer',
                       borderBottom: i < filtered.length - 1 ? '1px solid var(--dd-border, #f3f4f6)' : 'none',
                       background: i === focusIdx ? 'var(--dd-accent-soft, rgba(99,102,241,0.08))' : 'transparent',
                       fontFamily: 'ui-monospace, Menlo, Consolas, monospace', color: 'var(--dd-text, #374151)',
                     }}>{v.fullName}</div>
              ))
          }
        </div>
      )}
    </div>
  )
}

/** Kolon autocomplete — string[] kolon listesinden filtreleyerek seçim */
function ColumnAutocomplete({ columns, value, onSelect, disabled, placeholder }) {
  const [query, setQuery] = useState('')
  const [open,  setOpen]  = useState(false)
  const [focusIdx, setFocusIdx] = useState(0)
  const inputRef = useRef(null)

  // Parent'tan gelen value değişikliğinde query'i sync et — düzenleme modunda
  // (double-click ile satır yüklenirken) form input doğru değeri gösterir.
  useEffect(() => {
    setQuery(value || '')
  }, [value])   // eslint-disable-line

  const trimmed = query.trim().toLowerCase()
  const filtered = (columns ?? []).filter(c => c && c.toLowerCase().includes(trimmed)).slice(0, 50)

  return (
    <div style={{ position: 'relative' }}>
      <input ref={inputRef} type="text" value={query}
             disabled={disabled} placeholder={placeholder ?? 'Kolon…'}
             autoComplete="off"
             onChange={e => { setQuery(e.target.value); setOpen(true); setFocusIdx(0); onSelect(e.target.value) }}
             onFocus={() => setOpen(true)}
             onBlur={() => setTimeout(() => setOpen(false), 150)}
             onKeyDown={e => {
               if (!open) return
               if (e.key === 'ArrowDown') { e.preventDefault(); setFocusIdx(i => Math.min(filtered.length - 1, i + 1)) }
               else if (e.key === 'ArrowUp') { e.preventDefault(); setFocusIdx(i => Math.max(0, i - 1)) }
               else if (e.key === 'Enter') {
                 e.preventDefault()
                 if (filtered[focusIdx]) { setQuery(filtered[focusIdx]); onSelect(filtered[focusIdx]); setOpen(false) }
               } else if (e.key === 'Escape') { setOpen(false) }
             }}
             style={{ ...inp, fontFamily: 'monospace', opacity: disabled ? 0.5 : 1 }} />
      {open && !disabled && filtered.length > 0 && (
        <div style={{
          position: 'absolute', left: 0, right: 0, top: '100%', zIndex: 30,
          background: 'var(--dd-surface, #fff)', border: '1px solid var(--dd-border, #e5e7eb)',
          borderRadius: 6, boxShadow: '0 8px 24px rgba(0,0,0,0.3)',
          maxHeight: 200, overflowY: 'auto', marginTop: 2,
        }}>
          {filtered.map((c, i) => (
            <div key={c}
                 onMouseDown={e => { e.preventDefault(); setQuery(c); onSelect(c); setOpen(false) }}
                 onMouseEnter={() => setFocusIdx(i)}
                 style={{
                   padding: '6px 10px', fontSize: 12, cursor: 'pointer',
                   borderBottom: i < filtered.length - 1 ? '1px solid var(--dd-border, #f3f4f6)' : 'none',
                   background: i === focusIdx ? 'var(--dd-accent-soft, rgba(99,102,241,0.08))' : 'transparent',
                   fontFamily: 'ui-monospace, Menlo, Consolas, monospace', color: 'var(--dd-text, #374151)',
                 }}>{c}</div>
          ))}
        </div>
      )}
    </div>
  )
}

// ── shared styles ──────────────────────────────────────────────────────────
const lbl = { display: 'block', fontSize: 11, color: 'var(--dd-text-muted, #666)', marginBottom: 4, fontWeight: 500 }
const inp = {
  width: '100%', border: '1px solid var(--dd-border, #e0e0e0)', borderRadius: 6, padding: '7px 10px',
  fontSize: 12, color: 'var(--dd-text, #333)', background: 'var(--dd-surface-alt, #fafafa)', boxSizing: 'border-box',
}
const hintTxt = { fontSize: 10.5, color: 'var(--dd-text-muted, #888)', marginTop: 6, lineHeight: 1.5 }
const dim     = { fontSize: 12, color: 'var(--dd-text-muted, #aaa)', padding: '8px 0' }
const tagBtn = {
  width: 18, height: 18, padding: 0, marginLeft: 2,
  border: '1px solid var(--dd-border, #d1d5db)',
  background: 'var(--dd-surface, #fff)', borderRadius: '50%',
  cursor: 'pointer', fontSize: 11, lineHeight: 1,
  display: 'flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0,
}
const btnSecondary = { padding: '8px 18px', borderRadius: 6, border: '1px solid var(--dd-border, #e0e0e0)', background: 'var(--dd-surface, #fff)', color: 'var(--dd-text, #333)', cursor: 'pointer', fontSize: 12 }
const btnPrimary   = { padding: '8px 18px', borderRadius: 6, border: 'none', background: 'var(--dd-accent, #6366f1)', color: '#fff', cursor: 'pointer', fontSize: 12, fontWeight: 600 }
