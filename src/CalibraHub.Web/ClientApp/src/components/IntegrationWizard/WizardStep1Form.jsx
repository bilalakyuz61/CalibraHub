/**
 * Step 1 — Kaynak form seçimi (geliştirilmiş UX).
 *
 * Sol pane:
 *   • Arama kutusu (anlık filtreleme — ad / kod)
 *   • "Son kullanılanlar" sekmesi (localStorage — son 10 form)
 *   • Module/SubModule gruplama (collapse/expand)
 *
 * Sağ pane (seçim sonrası):
 *   • Form özet kartı (ad + kod + alan sayısı)
 *   • Alan listesi (zorunlu/opsiyonel ayrımı, base/widget rozetleri)
 *   • Önerilen endpoint'ler (form bazında daha önce kullanılanlar)
 */
import React, { useState, useEffect, useMemo, useCallback } from 'react'
import { FileText, ChevronRight, Loader2, Search, Clock, Star, Plug, ArrowRight, Layers, Box, Tag } from 'lucide-react'

const RECENT_KEY = 'calibrahub.iw.recent_forms'
const MAX_RECENT = 10

function loadRecent() {
  try {
    const raw = localStorage.getItem(RECENT_KEY)
    return raw ? JSON.parse(raw) : []
  } catch { return [] }
}
function pushRecent(form) {
  if (!form?.formCode) return
  const cur = loadRecent().filter(f => f.formCode !== form.formCode)
  cur.unshift({ formCode: form.formCode, formName: form.formName, module: form.module })
  try { localStorage.setItem(RECENT_KEY, JSON.stringify(cur.slice(0, MAX_RECENT))) } catch { /* quota */ }
}

export default function WizardStep1Form({ apiBase, state, update }) {
  const [forms, setForms]           = useState([])
  const [fields, setFields]         = useState([])
  const [recommendations, setRecs]  = useState([])
  const [loading, setLoading]       = useState(true)
  const [fieldsLoading, setFL]      = useState(false)
  const [search, setSearch]         = useState('')
  const [view, setView]             = useState('all')   // 'all' | 'recent'
  const [recent, setRecent]         = useState(loadRecent())

  // Form listesi
  useEffect(() => {
    let cancelled = false
    ;(async () => {
      try {
        const r = await fetch(`${apiBase}/forms`, { credentials: 'same-origin' })
        const d = await r.json()
        if (!cancelled && d.success) setForms(d.forms || [])
      } catch { /* sessiz */ }
      finally { if (!cancelled) setLoading(false) }
    })()
    return () => { cancelled = true }
  }, [apiBase])

  // Form seçildiğinde: alanlar + önerilen endpoint'ler (mevcut entegrasyonlardan)
  useEffect(() => {
    if (!state.sourceFormCode) { setFields([]); setRecs([]); return }

    setFL(true)
    fetch(`${apiBase}/forms/${encodeURIComponent(state.sourceFormCode)}/fields`,
          { credentials: 'same-origin' })
      .then(r => r.json())
      .then(d => { if (d.success) setFields(d.fields || []) })
      .catch(() => setFields([]))
      .finally(() => setFL(false))

    // Bu form daha önce hangi endpoint'lere bağlanmış?
    fetch(`${apiBase}/list?includeInactive=true`, { credentials: 'same-origin' })
      .then(r => r.json())
      .then(d => {
        if (d.success) {
          const matching = (d.items || [])
            .filter(it => it.sourceFormCode === state.sourceFormCode)
            .slice(0, 5)
          setRecs(matching)
        }
      })
      .catch(() => setRecs([]))

    // Son kullanılanlara ekle
    const sel = forms.find(f => f.formCode === state.sourceFormCode)
    if (sel) { pushRecent(sel); setRecent(loadRecent()) }
  }, [apiBase, state.sourceFormCode, forms])

  // Arama + Module/SubModule gruplama
  const grouped = useMemo(() => {
    let list = forms
    if (search) {
      const q = search.toLowerCase()
      list = list.filter(f =>
        (f.formName || '').toLowerCase().includes(q) ||
        (f.formCode || '').toLowerCase().includes(q) ||
        (f.module || '').toLowerCase().includes(q)
      )
    }
    const map = {}
    list.forEach(f => {
      const m = f.module || 'Diğer'
      const s = f.subModule || ''
      if (!map[m]) map[m] = {}
      if (!map[m][s]) map[m][s] = []
      map[m][s].push(f)
    })
    return map
  }, [forms, search])

  const recentForms = useMemo(() => {
    if (recent.length === 0) return []
    // Backend listesinden gerçek formu bul (eski kayıtlar artık yoksa atla)
    return recent
      .map(r => forms.find(f => f.formCode === r.formCode) || r)
      .filter(Boolean)
  }, [recent, forms])

  const renderFormButton = useCallback((f) => {
    const active = state.sourceFormCode === f.formCode
    return (
      <button key={f.formCode}
              onClick={() => update({ sourceFormCode: f.formCode })}
              style={{
                display: 'flex', alignItems: 'center', gap: 8,
                width: '100%', padding: '6px 10px', borderRadius: 6,
                background: active ? 'var(--iw-indigo-bg)' : 'transparent',
                border: '1px solid ' + (active ? 'var(--iw-indigo-color)' : 'transparent'),
                color: active ? 'var(--iw-indigo-color)' : 'var(--iw-text)',
                cursor: 'pointer', fontSize: 12, textAlign: 'left',
              }}>
        <FileText size={12} />
        <span style={{ flex: 1, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
          {f.formName || f.formCode}
        </span>
        <span style={{ fontSize: 10, opacity: 0.5, fontFamily: 'ui-monospace, Menlo, Consolas, monospace' }}>{f.formCode}</span>
      </button>
    )
  }, [state.sourceFormCode, update])

  const selectedForm = useMemo(
    () => forms.find(f => f.formCode === state.sourceFormCode),
    [forms, state.sourceFormCode]
  )

  return (
    <>
      <h2 className="iw-step-title">Kaynak Form Seç</h2>
      <p className="iw-step-help">
        Hangi formun kaydı entegrasyon tetikleyecek? Form alanları sonraki adımda
        hedef endpoint'in alanlarına eşleştirilecek.
      </p>

      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 24, maxWidth: 1280 }}>
        {/* Sol: Form picker + arama + recent */}
        <div className="iw-mapping-pane" style={{ minHeight: 480 }}>
          {/* Toolbar — arama + tab */}
          <div style={{ display: 'flex', flexDirection: 'column', gap: 8, marginBottom: 10 }}>
            <div className="il-search-wrap" style={{ width: '100%' }}>
              <Search size={13} className="il-search-icon" />
              <input className="il-search" placeholder="Form ara — ad veya kod…"
                     value={search} onChange={e => setSearch(e.target.value)}
                     style={{ width: '100%' }} />
            </div>
            <div style={{ display: 'flex', gap: 4 }}>
              <button onClick={() => setView('all')}
                      style={tabBtnStyle(view === 'all')}>
                <Star size={11} style={{ verticalAlign: 'middle', marginRight: 4 }} />
                Tüm Formlar
              </button>
              <button onClick={() => setView('recent')}
                      disabled={recentForms.length === 0}
                      style={tabBtnStyle(view === 'recent', recentForms.length === 0)}>
                <Clock size={11} style={{ verticalAlign: 'middle', marginRight: 4 }} />
                Son Kullanılan {recentForms.length > 0 ? `(${recentForms.length})` : ''}
              </button>
            </div>
          </div>

          {loading && (
            <div style={{ padding: 16, textAlign: 'center' }}>
              <Loader2 className="iw-spin" size={20} />
            </div>
          )}

          {!loading && view === 'recent' && (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
              {recentForms.length === 0 && (
                <div style={{ color: 'var(--iw-muted)', fontSize: 12, padding: 12, textAlign: 'center' }}>
                  Henüz form seçilmedi.
                </div>
              )}
              {recentForms.map(renderFormButton)}
            </div>
          )}

          {!loading && view === 'all' && Object.keys(grouped).length === 0 && (
            <div style={{ color: 'var(--iw-muted)', fontSize: 12, padding: 12, textAlign: 'center' }}>
              Aramaya uyan form yok.
            </div>
          )}

          {!loading && view === 'all' && Object.entries(grouped).map(([mod, subs]) => (
            <div key={mod} style={{ marginBottom: 12 }}>
              <div style={{ fontSize: 11, fontWeight: 700, textTransform: 'uppercase',
                            color: 'var(--iw-muted)', letterSpacing: '.05em', marginBottom: 4 }}>
                {mod}
              </div>
              {Object.entries(subs).map(([sub, list]) => (
                <div key={sub} style={{ marginLeft: sub ? 12 : 0, marginBottom: 4 }}>
                  {sub && <div style={{ fontSize: 11, color: 'var(--iw-muted)', marginBottom: 2 }}>{sub}</div>}
                  {list.map(renderFormButton)}
                </div>
              ))}
            </div>
          ))}
        </div>

        {/* Sağ: Seçilen formun alanları + öneriler */}
        <div style={{ display: 'flex', flexDirection: 'column', gap: 14, minHeight: 480 }}>
          {!state.sourceFormCode && (
            <div className="iw-mapping-pane" style={{
              flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center',
              color: 'var(--iw-muted)', fontSize: 13, textAlign: 'center',
            }}>
              <div>
                <FileText size={48} style={{ opacity: 0.2, marginBottom: 12 }} />
                <div>Sol panelden bir form seçin.</div>
                <div style={{ fontSize: 11, marginTop: 4 }}>Alanlar ve önerilen endpoint'ler burada gösterilir.</div>
              </div>
            </div>
          )}

          {state.sourceFormCode && (
            <>
              {/* Özet kart */}
              <div style={{
                background: 'var(--iw-indigo-bg)',
                border: '1px solid var(--iw-indigo-bdr)',
                borderRadius: 10, padding: '12px 14px',
                display: 'flex', alignItems: 'center', gap: 12,
              }}>
                <div style={{
                  width: 36, height: 36, borderRadius: 8,
                  background: 'var(--iw-indigo-color)', color: '#fff',
                  display: 'flex', alignItems: 'center', justifyContent: 'center',
                  flexShrink: 0,
                }}>
                  <FileText size={18} />
                </div>
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div style={{ fontSize: 14, fontWeight: 600, color: 'var(--iw-indigo-color)' }}>
                    {selectedForm?.formName || state.sourceFormCode}
                  </div>
                  <div style={{ fontSize: 11, fontFamily: 'ui-monospace, Menlo, Consolas, monospace', color: 'var(--iw-muted)' }}>
                    {state.sourceFormCode}
                    {selectedForm?.module && <> · {selectedForm.module}{selectedForm.subModule ? ` / ${selectedForm.subModule}` : ''}</>}
                    {fields.length > 0 && <> · {fields.length} alan</>}
                  </div>
                </div>
              </div>

              {/* Önerilen endpoint'ler */}
              {recommendations.length > 0 && (
                <div className="iw-mapping-pane" style={{ minHeight: 0 }}>
                  <h3 style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                    <Plug size={13} /> Bu formla yapılmış entegrasyonlar
                    <span style={{ marginLeft: 'auto', fontSize: 10, color: 'var(--iw-muted)', fontWeight: 400 }}>
                      ({recommendations.length})
                    </span>
                  </h3>
                  <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
                    {recommendations.map(it => (
                      <div key={it.id} style={{
                        display: 'flex', alignItems: 'center', gap: 8,
                        padding: '6px 10px', borderRadius: 6,
                        background: 'var(--iw-bg)', fontSize: 11,
                      }}>
                        <span style={{
                          width: 6, height: 6, borderRadius: '50%',
                          background: it.isActive ? 'var(--iw-emerald-color)' : 'var(--iw-muted)',
                        }} />
                        <span style={{ flex: 1, color: 'var(--iw-text)', fontWeight: 500, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                          {it.name}
                        </span>
                        <ArrowRight size={11} style={{ color: 'var(--iw-muted)' }} />
                        <span style={{ color: 'var(--iw-muted)', fontFamily: 'ui-monospace, Menlo, Consolas, monospace', fontSize: 10 }}>
                          {it.endpointName}
                        </span>
                      </div>
                    ))}
                  </div>
                </div>
              )}

              {/* Master-Detail bilgi şeridi — form'un kalem form'u varsa */}
              {selectedForm?.linesFormCode && (
                <div style={{
                  background: 'var(--iw-emerald-bg)',
                  border: '1px solid var(--iw-emerald-color)',
                  borderRadius: 10, padding: '10px 14px',
                  display: 'flex', gap: 10, alignItems: 'flex-start',
                }}>
                  <span style={{ fontSize: 18, lineHeight: 1, flexShrink: 0 }}>📦</span>
                  <div style={{ flex: 1, fontSize: 12, color: 'var(--iw-text)', lineHeight: 1.6 }}>
                    <div style={{ fontWeight: 600, color: 'var(--iw-emerald-color)' }}>
                      Bu form 2 kademe içeriyor — kalem alanları otomatik kullanılacak
                    </div>
                    <div style={{ marginTop: 4, fontSize: 11 }}>
                      <strong>Header</strong>: <code>{state.sourceFormCode}</code> (üst form)<br/>
                      <strong>Lines</strong>: <code>{selectedForm.linesFormCode}</code> (kalem form, parent FK: <code>{selectedForm.linesParentColumn || '?'}</code>)<br/>
                      <strong>Combination</strong>: kalem kombinasyon kodu (runtime)
                    </div>
                    <div style={{ marginTop: 4, fontSize: 11, color: 'var(--iw-muted)' }}>
                      Aktarım sırasında tek "veri seti" olarak kullanılır → Step 3'te source alanları
                      Header / Lines / Combination grupları halinde gösterilir.
                    </div>
                  </div>
                </div>
              )}

              {/* Alan listesi — Section bazında gruplı (Header / Lines / Combination) */}
              <div className="iw-mapping-pane" style={{ flex: 1, minHeight: 0, overflow: 'auto' }}>
                <h3 style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                  Alanlar
                  {fields.length > 0 && (
                    <span style={{ marginLeft: 'auto', fontSize: 10, color: 'var(--iw-muted)', fontWeight: 400 }}>
                      {fields.filter(f => f.isRequired).length} zorunlu / {fields.length} toplam
                    </span>
                  )}
                </h3>
                {fieldsLoading && (
                  <div style={{ padding: 16, textAlign: 'center' }}>
                    <Loader2 className="iw-spin" size={20} />
                  </div>
                )}
                {!fieldsLoading && fields.length === 0 && (
                  <div style={{ color: 'var(--iw-muted)', fontSize: 12, padding: 12 }}>
                    Bu formun widget alanı bulunamadı. Yine de entegrasyon kurabilirsiniz —
                    Step 3'te kaynak alanı serbest yazılır.
                  </div>
                )}
                {/* Section bazlı render — Header / Lines / Combination ayrı bloklar */}
                {!fieldsLoading && fields.length > 0 && (() => {
                  const SECTION_META = {
                    Header:      { label: 'Üst (Header)',           icon: Layers, color: 'indigo' },
                    Lines:       { label: 'Kalem (Lines)',          icon: Box,    color: 'amber' },
                    Combination: { label: 'Kombinasyon',            icon: Tag,    color: 'emerald' },
                  }
                  const order = ['Header', 'Lines', 'Combination']
                  return order.map(sec => {
                    const items = fields.filter(f => (f.section || 'Header') === sec)
                    if (items.length === 0) return null
                    const meta = SECTION_META[sec]
                    const Icon = meta.icon
                    return (
                      <div key={sec} style={{ marginBottom: 12 }}>
                        <div style={{
                          display: 'flex', alignItems: 'center', gap: 6,
                          padding: '4px 6px', marginBottom: 4,
                          borderBottom: '1px solid var(--iw-border)',
                          fontSize: 11, fontWeight: 700, textTransform: 'uppercase', letterSpacing: '.05em',
                          color: `var(--iw-${meta.color}-color)`,
                        }}>
                          <Icon size={12} />
                          <span>{meta.label}</span>
                          <span style={{ marginLeft: 'auto', opacity: 0.7, fontWeight: 500 }}>{items.length}</span>
                        </div>
                        {items.map(fl => (
                          <div key={`${sec}.${fl.code}`} style={{
                            display: 'flex', gap: 8, padding: '6px 10px', borderRadius: 6,
                            background: 'var(--iw-bg)', marginBottom: 4, fontSize: 12,
                          }}>
                            <ChevronRight size={11} style={{ color: 'var(--iw-muted)', marginTop: 3 }} />
                            <div style={{ flex: 1, minWidth: 0 }}>
                              <div style={{ fontWeight: 600, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                                {fl.label}
                                {fl.isRequired && <span style={{ color: 'var(--iw-rose-color)', marginLeft: 4 }}>*</span>}
                              </div>
                              <div style={{ fontSize: 10, color: 'var(--iw-muted)', fontFamily: 'ui-monospace, Menlo, Consolas, monospace' }}>
                                {fl.code} · {fl.dataType}
                                {fl.isPlainField && <span style={{ marginLeft: 6, opacity: 0.6 }}>(base)</span>}
                              </div>
                            </div>
                          </div>
                        ))}
                      </div>
                    )
                  })
                })()}
              </div>
            </>
          )}
        </div>
      </div>
    </>
  )
}

function tabBtnStyle(active, disabled = false) {
  return {
    flex: 1, padding: '5px 10px', borderRadius: 6, fontSize: 11, fontWeight: 500,
    border: '1px solid ' + (active ? 'var(--iw-indigo-color)' : 'var(--iw-border)'),
    background: active ? 'var(--iw-indigo-bg)' : 'var(--iw-surface)',
    color: active ? 'var(--iw-indigo-color)' : 'var(--iw-muted)',
    cursor: disabled ? 'not-allowed' : 'pointer',
    opacity: disabled ? 0.4 : 1,
  }
}
