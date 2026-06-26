/**
 * DataImport — Şablon-tabanlı içe aktarım SİHİRBAZI (AI'sız).
 *
 * Liste (şablon kartları) ayrı: /Import → SmartBoard (C-Grid). Bu bileşen yalnız
 * sihirbazdır ve /Import/Wizard'da mount edilir. URL parametreleri:
 *   ?mode=new                      → yeni şablon
 *   ?id={id}&mode=edit             → şablonu düzenle
 *   ?id={id}&mode=run              → şablonu yükle, dosya adımına atla
 * Adımlar: 1) Şablon  2) Dosya & Eşleme  3) Önizleme  4) Aktar.
 * Yazma backend'de ilgili IImportTargetHandler ile yapılır.
 */
import { useEffect, useMemo, useRef, useState, useCallback } from 'react'
import {
  Upload, Save, Play, AlertTriangle, Loader2, CheckCircle2,
  FileDown, ArrowLeft, ArrowRight,
} from 'lucide-react'
import { navigateInWorkspace } from '../../utils/workspaceNav'
import './data-import.css'

const API = '/Import/api'
const LIST_URL = '/Import'

const TRANSFORMS = [
  { value: '', label: '—' },
  { value: 'trim', label: 'Boşlukları kırp' },
  { value: 'upper', label: 'BÜYÜK harf' },
  { value: 'lower', label: 'küçük harf' },
  { value: 'digits', label: 'Sadece rakam' },
]
const STEPS = [
  { n: 1, label: 'Şablon' },
  { n: 2, label: 'Dosya & Eşleme' },
  { n: 3, label: 'Önizleme' },
  { n: 4, label: 'Aktar' },
]

function csrf() {
  const el = document.querySelector('input[name="__RequestVerificationToken"]')
  if (el && el.value) return el.value
  try {
    const cfg = window.__CALIBRA_SHELL_CONFIG__
    if (cfg && typeof cfg.antiforgeryToken === 'string') return cfg.antiforgeryToken
  } catch (_) { /* ignore */ }
  return ''
}
async function apiGet(url) {
  const r = await fetch(url, { credentials: 'same-origin' })
  return r.json()
}
async function apiPostJson(url, body) {
  const r = await fetch(url, {
    method: 'POST', credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': csrf() },
    body: JSON.stringify(body),
  })
  return r.json()
}
async function apiPostForm(url, formData) {
  try { formData.append('__RequestVerificationToken', csrf()) } catch (_) { /* ignore */ }
  const r = await fetch(url, {
    method: 'POST', credentials: 'same-origin',
    headers: { 'RequestVerificationToken': csrf() },
    body: formData,
  })
  return r.json()
}
function norm(s) {
  return (s || '').toLowerCase()
    .replace(/ı/g, 'i').replace(/ş/g, 's').replace(/ğ/g, 'g')
    .replace(/ü/g, 'u').replace(/ö/g, 'o').replace(/ç/g, 'c')
    .replace(/[^a-z0-9]/g, '')
}
function goList() {
  try { navigateInWorkspace(LIST_URL) } catch (_) { try { window.location.href = LIST_URL } catch (e) { /* ignore */ } }
}

export default function DataImport() {
  const [entities, setEntities] = useState([])
  const [entity, setEntity] = useState('CONTACT')
  const [selId, setSelId] = useState(0)

  const [fields, setFields] = useState([])
  const [name, setName] = useState('')
  const [matchKey, setMatchKey] = useState('')
  const [headerRow, setHeaderRow] = useState(1)
  const [sheetName, setSheetName] = useState('')
  const [mapping, setMapping] = useState({})

  const [file, setFile] = useState(null)
  const [sheets, setSheets] = useState([])
  const [headers, setHeaders] = useState([])

  const [preview, setPreview] = useState(null)
  const [result, setResult] = useState(null)
  const [busy, setBusy] = useState('')
  const [error, setError] = useState('')
  const [confirm, setConfirm] = useState(null)
  const [step, setStep] = useState(1)

  const fileRef = useRef(null)

  const loadFields = useCallback((ent) => {
    apiGet(`${API}/target-fields?entity=${encodeURIComponent(ent || 'CONTACT')}`)
      .then(d => { if (d && d.success) setFields(d.fields || []) })
  }, [])

  async function selectTemplate(id) {
    const d = await apiGet(`${API}/templates/${id}`)
    if (!d || !d.success) { setError(d?.error || 'Şablon yüklenemedi.'); loadFields('CONTACT'); return }
    const t = d.template
    setSelId(t.id); setName(t.name); setMatchKey(t.matchKeyField || '')
    setEntity(t.targetEntity || 'CONTACT'); loadFields(t.targetEntity || 'CONTACT')
    setHeaderRow(t.headerRowIndex || 1); setSheetName(t.sheetName || '')
    const m = {}
    for (const c of (t.columns || [])) {
      m[c.targetKey] = { source: c.sourceColumn || '', transform: c.transform || '', def: c.defaultValue || '' }
    }
    setMapping(m)
  }

  // İlk yükleme — entity listesi + URL'e göre şablon/mod
  useEffect(() => {
    let cancelled = false
    apiGet(`${API}/entities`).then(d => { if (!cancelled && d && d.success) setEntities(d.entities || []) })
    const params = new URLSearchParams(window.location.search)
    const id = params.get('id')
    const mode = params.get('mode') || 'new'
    ;(async () => {
      if (id) {
        await selectTemplate(Number(id))
        if (!cancelled && mode === 'run') setStep(2)
      } else {
        loadFields('CONTACT')
      }
    })()
    return () => { cancelled = true }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const requiredUnmapped = useMemo(
    () => fields.filter(f => f.isRequired && !(mapping[f.key] && (mapping[f.key].source || mapping[f.key].def))).map(f => f.label),
    [fields, mapping]
  )
  const matchOptions = useMemo(() => {
    const opts = [{ value: '', label: 'Yok — her zaman yeni kayıt ekle' }]
    for (const f of fields) if (f.canBeMatchKey) opts.push({ value: f.key, label: `${f.label} — varsa güncelle, yoksa ekle` })
    return opts
  }, [fields])
  const entityLabel = useCallback((code) => (entities.find(e => e.entity === code)?.label) || code || 'Cari', [entities])

  function onEntityChange(ent) {
    setEntity(ent); loadFields(ent)
    setMatchKey(''); setMapping({}); setPreview(null); setResult(null); setError('')
  }

  function downloadBlank() {
    const url = `${API}/blank-template?entity=${encodeURIComponent(entity || 'CONTACT')}` + (selId ? `&templateId=${selId}` : '')
    const a = document.createElement('a')
    a.href = url; a.rel = 'noopener'
    document.body.appendChild(a); a.click(); a.remove()
  }

  async function onPickFile(f) {
    if (!f) return
    setFile(f); setError(''); setPreview(null); setResult(null)
    await readHeaders(f, sheetName, headerRow)
  }
  async function readHeaders(f, sheet, hRow) {
    if (!f) return
    setBusy('read')
    try {
      const fd = new FormData()
      fd.append('file', f)
      if (sheet) fd.append('sheetName', sheet)
      fd.append('headerRowIndex', String(hRow || 1))
      const d = await apiPostForm(`${API}/read-headers`, fd)
      if (!d || !d.success) { setError(d?.error || 'Dosya okunamadı.'); return }
      setSheets(d.sheets || [])
      if (!sheet && d.activeSheet) setSheetName(d.activeSheet)
      const hs = d.headers || []
      setHeaders(hs); autoMap(hs)
    } catch (e) { setError('Dosya okunurken hata: ' + e.message) }
    finally { setBusy('') }
  }
  function autoMap(hs) {
    if (!hs || hs.length === 0) return
    setMapping(prev => {
      const next = { ...prev }
      for (const f of fields) {
        const cur = next[f.key]
        if (cur && cur.source) continue
        const hit = hs.find(h => norm(h) === norm(f.label) || norm(h) === norm(f.key))
          || hs.find(h => norm(h).includes(norm(f.label)) && norm(f.label).length > 2)
        if (hit) next[f.key] = { source: hit, transform: cur?.transform || '', def: cur?.def || '' }
      }
      return next
    })
  }
  function setMap(key, patch) {
    setMapping(prev => ({ ...prev, [key]: { source: '', transform: '', def: '', ...prev[key], ...patch } }))
  }

  function buildSpec() {
    const columns = Object.entries(mapping)
      .filter(([, v]) => v && (v.source || v.def))
      .map(([k, v]) => ({ targetKey: k, sourceColumn: v.source || null, transform: v.transform || null, defaultValue: v.def || null }))
    return {
      id: selId || 0, name: name.trim(), targetEntity: entity || 'CONTACT',
      sheetName: sheetName || null, headerRowIndex: Number(headerRow) || 1,
      matchKeyField: matchKey || null, columns, isActive: true,
    }
  }

  async function saveTemplate() {
    if (!name.trim()) { setError('Şablon adı zorunludur.'); return }
    setBusy('save'); setError('')
    try {
      const d = await apiPostJson(`${API}/templates/save`, buildSpec())
      if (!d || !d.success) { setError(d?.error || 'Kaydedilemedi.'); return }
      setSelId(d.id)
    } finally { setBusy('') }
  }
  async function runPreview() {
    if (!file) { setError('Önce bir Excel/CSV dosyası seçin.'); return }
    setBusy('preview'); setError(''); setResult(null)
    try {
      const fd = new FormData()
      fd.append('file', file); fd.append('spec', JSON.stringify(buildSpec()))
      const d = await apiPostForm(`${API}/preview`, fd)
      if (!d || !d.success) { setError(d?.error || 'Önizleme başarısız.'); return }
      setPreview(d)
    } finally { setBusy('') }
  }
  function askImport() {
    if (!file) { setError('Önce bir Excel/CSV dosyası seçin.'); return }
    const n = preview ? preview.validRows : '?'
    setConfirm({ message: `${n} geçerli satır işlenecek (ekle/güncelle). Devam edilsin mi?`, onOk: runImport })
  }
  async function runImport() {
    setConfirm(null); setBusy('commit'); setError(''); setResult(null)
    try {
      const fd = new FormData()
      fd.append('file', file); fd.append('spec', JSON.stringify(buildSpec()))
      const d = await apiPostForm(`${API}/commit`, fd)
      if (!d || !d.success) { setError(d?.error || 'İçe aktarım başarısız.'); return }
      setResult(d)
    } finally { setBusy('') }
  }

  function onNext() {
    setError('')
    if (step === 1) { setStep(2); return }
    if (step === 2) {
      if (!file) { setError('Önce bir Excel/CSV dosyası seçin.'); return }
      if (requiredUnmapped.length > 0) { setError('Zorunlu alan(lar) eşlenmeli: ' + requiredUnmapped.join(', ')); return }
      setStep(3); runPreview(); return
    }
    if (step === 3) {
      if (!preview || preview.validRows === 0) { setError('Aktarılacak geçerli satır yok.'); return }
      setStep(4); return
    }
  }
  function onBack() {
    setError('')
    if (step > 1) setStep(step - 1)
    else goList()
  }

  return (
    <div className="di-root">
      <div className="di-wizard">
        <div className="di-wizard__head">
          <button className="di-back" onClick={goList}><ArrowLeft size={16} /> Liste</button>
          <span className="di-wizard__title">{selId ? (name || 'Şablon') : 'Yeni Şablon'}</span>
          <div className="di-steps">
            {STEPS.map((s, i) => (
              <div key={s.n} style={{ display: 'flex', alignItems: 'center' }}>
                {i > 0 && <div className="di-step-sep" />}
                <div className={'di-step-item' + (s.n === step ? ' di-step-item--active' : s.n < step ? ' di-step-item--done' : '')}>
                  <span className="di-step-num">{s.n < step ? '✓' : s.n}</span><span>{s.label}</span>
                </div>
              </div>
            ))}
          </div>
        </div>

        <div className="di-wizard__body">
          {error && <div className="di-alert di-alert--err"><AlertTriangle size={15} /> {error}</div>}

          {step === 1 && (
            <section className="di-card">
              <div className="di-card__title">Şablon Bilgisi</div>
              <div className="di-grid">
                <div className="di-field">
                  <label className="di-label">Şablon Adı</label>
                  <input className="di-input" value={name} onChange={e => setName(e.target.value)} placeholder="Örn. Müşteri Listesi İçe Aktarım" />
                </div>
                <div className="di-field">
                  <label className="di-label">Hedef Kayıt Türü</label>
                  <select className="di-select" value={entity} onChange={e => onEntityChange(e.target.value)} disabled={selId > 0}>
                    {entities.map(en => <option key={en.entity} value={en.entity}>{en.label}</option>)}
                  </select>
                </div>
                <div className="di-field">
                  <label className="di-label">Eşleştirme Anahtarı</label>
                  <select className="di-select" value={matchKey} onChange={e => setMatchKey(e.target.value)}>
                    {matchOptions.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
                  </select>
                </div>
                <div className="di-field">
                  <label className="di-label">Başlık Satırı</label>
                  <input className="di-input" type="number" min={1} value={headerRow}
                    onChange={e => setHeaderRow(e.target.value)}
                    onBlur={() => file && readHeaders(file, sheetName, headerRow)} />
                </div>
              </div>
            </section>
          )}

          {step === 2 && (
            <section className="di-card">
              <div className="di-card__title">Dosya ve Kolon Eşleme</div>
              <div className="di-actions" style={{ marginBottom: 12 }}>
                <button className="di-btn" onClick={downloadBlank}><FileDown size={15} /> Boş Excel Şablonu İndir</button>
                <span className="di-map__hint">{selId ? 'Bu şablonun kolonlarıyla' : `Tüm ${entityLabel(entity)} alanlarıyla`} boş Excel — kullanıcı doldurup geri yükler</span>
              </div>

              <input ref={fileRef} type="file" accept=".xlsx,.xls,.csv" style={{ display: 'none' }}
                onChange={e => { onPickFile(e.target.files?.[0]); e.target.value = '' }} />
              <div className="di-drop" onClick={() => fileRef.current?.click()}>
                {busy === 'read' ? <span><Loader2 size={16} className="di-spin" /> Okunuyor…</span>
                  : file ? <span><span className="di-drop__file">{file.name}</span> — değiştirmek için tıkla</span>
                  : <span><Upload size={16} /> Excel (.xlsx) veya CSV dosyası seç</span>}
              </div>

              {sheets.length > 1 && (
                <div className="di-field" style={{ maxWidth: 260, marginTop: 12 }}>
                  <label className="di-label">Sayfa</label>
                  <select className="di-select" value={sheetName}
                    onChange={e => { setSheetName(e.target.value); readHeaders(file, e.target.value, headerRow) }}>
                    {sheets.map(s => <option key={s.name} value={s.name}>{s.name} ({s.rowCount} satır)</option>)}
                  </select>
                </div>
              )}

              <datalist id="di-headers">{headers.map((h, i) => <option key={i} value={h} />)}</datalist>

              <table className="di-map" style={{ marginTop: 14 }}>
                <thead>
                  <tr>
                    <th style={{ width: '26%' }}>Hedef Alan</th>
                    <th style={{ width: '32%' }}>Kaynak Kolon</th>
                    <th style={{ width: '22%' }}>Dönüşüm</th>
                    <th style={{ width: '20%' }}>Varsayılan</th>
                  </tr>
                </thead>
                <tbody>
                  {fields.map(f => {
                    const m = mapping[f.key] || {}
                    return (
                      <tr key={f.key}>
                        <td className="di-map__target">
                          {f.label}{f.isRequired && <span className="di-req" title="Zorunlu">*</span>}
                          {f.hint && <div className="di-map__hint">{f.hint}</div>}
                        </td>
                        <td>
                          <input className="di-input" list="di-headers" value={m.source || ''}
                            placeholder={headers.length ? 'Kolon seç / yaz' : 'Önce dosya yükleyin'}
                            onChange={e => setMap(f.key, { source: e.target.value })} />
                        </td>
                        <td>
                          {f.dataType === 'type'
                            ? <span className="di-map__hint">Müşteri/Satıcı metni otomatik</span>
                            : (
                              <select className="di-select" value={m.transform || ''} onChange={e => setMap(f.key, { transform: e.target.value })}>
                                {TRANSFORMS.map(t => <option key={t.value} value={t.value}>{t.label}</option>)}
                              </select>
                            )}
                        </td>
                        <td><input className="di-input" value={m.def || ''} placeholder="—" onChange={e => setMap(f.key, { def: e.target.value })} /></td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>

              {requiredUnmapped.length > 0 && (
                <div className="di-alert" style={{ marginTop: 12, marginBottom: 0 }}>
                  <AlertTriangle size={15} /> Zorunlu alan(lar) eşlenmeli: {requiredUnmapped.join(', ')} — yoksa satırlar hata verir.
                </div>
              )}
            </section>
          )}

          {step === 3 && (
            <section className="di-card">
              <div className="di-card__title">Önizleme ve Doğrulama</div>
              {busy === 'preview' && <div className="di-map__hint"><Loader2 size={14} className="di-spin" /> Önizleniyor…</div>}
              {preview && (
                <>
                  <div className="di-stats">
                    <div className="di-stat"><div className="di-stat__n">{preview.totalRows}</div><div className="di-stat__l">Toplam Satır</div></div>
                    <div className="di-stat di-stat--ok"><div className="di-stat__n">{preview.validRows}</div><div className="di-stat__l">Geçerli</div></div>
                    <div className="di-stat di-stat--upd"><div className="di-stat__n">{preview.insertCount}/{preview.updateCount}</div><div className="di-stat__l">Yeni / Güncelle</div></div>
                    <div className="di-stat di-stat--err"><div className="di-stat__n">{preview.errorRows}</div><div className="di-stat__l">Hatalı</div></div>
                  </div>
                  <PreviewTable preview={preview} />
                </>
              )}
            </section>
          )}

          {step === 4 && (
            <section className="di-card">
              <div className="di-card__title">Aktar ve Sonuç</div>
              {!result && (
                <>
                  {preview && (
                    <div className="di-stats">
                      <div className="di-stat di-stat--ok"><div className="di-stat__n">{preview.insertCount}</div><div className="di-stat__l">Eklenecek</div></div>
                      <div className="di-stat di-stat--upd"><div className="di-stat__n">{preview.updateCount}</div><div className="di-stat__l">Güncellenecek</div></div>
                      <div className="di-stat di-stat--err"><div className="di-stat__n">{preview.errorRows}</div><div className="di-stat__l">Atlanacak (hata)</div></div>
                    </div>
                  )}
                  <p className="di-map__hint">Aşağıdaki "İçe Aktar" ile {preview?.validRows || 0} geçerli satır işlenecek.</p>
                </>
              )}
              {result && (
                <>
                  <div className="di-alert" style={{ background: 'rgba(5,150,105,.12)', color: 'var(--di-ok)', borderColor: 'rgba(5,150,105,.25)' }}>
                    <CheckCircle2 size={16} /> İçe aktarım tamamlandı.
                  </div>
                  <div className="di-stats">
                    <div className="di-stat di-stat--ok"><div className="di-stat__n">{result.inserted}</div><div className="di-stat__l">Eklendi</div></div>
                    <div className="di-stat di-stat--upd"><div className="di-stat__n">{result.updated}</div><div className="di-stat__l">Güncellendi</div></div>
                    <div className="di-stat di-stat--err"><div className="di-stat__n">{result.failed}</div><div className="di-stat__l">Başarısız</div></div>
                  </div>
                  {result.failed > 0 && (
                    <div className="di-preview-wrap" style={{ maxHeight: 240 }}>
                      <table className="di-preview">
                        <thead><tr><th>Satır</th><th>Hata</th></tr></thead>
                        <tbody>
                          {result.rows.filter(r => !r.ok).map(r => (
                            <tr key={r.rowNumber}><td>{r.rowNumber}</td><td className="di-rowerr">{r.error}</td></tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  )}
                  <div style={{ marginTop: 14 }}>
                    <button className="di-btn di-btn--primary" onClick={goList}><ArrowLeft size={15} /> Listeye Dön</button>
                  </div>
                </>
              )}
            </section>
          )}
        </div>

        <div className="di-wizard__foot">
          <button className="di-btn" onClick={onBack}>
            {step > 1 ? <><ArrowLeft size={15} /> Geri</> : <>Listeye Dön</>}
          </button>
          <button className="di-btn" onClick={saveTemplate} disabled={busy === 'save' || !name.trim()}>
            {busy === 'save' ? <Loader2 size={15} className="di-spin" /> : <Save size={15} />} Şablonu Kaydet
          </button>
          <div className="di-spacer" />
          {step < 4
            ? <button className="di-btn di-btn--primary" onClick={onNext} disabled={busy === 'preview' || busy === 'read'}>İleri <ArrowRight size={15} /></button>
            : !result
              ? <button className="di-btn di-btn--ok" onClick={askImport} disabled={!file || !!busy}>
                  {busy === 'commit' ? <Loader2 size={15} className="di-spin" /> : <Play size={15} />} İçe Aktar
                </button>
              : null}
        </div>
      </div>

      {confirm && (
        <div className="di-modal-backdrop" onClick={() => setConfirm(null)}>
          <div className="di-modal" onClick={e => e.stopPropagation()}>
            <div className="di-modal__icon"><Play size={20} /></div>
            <h3 className="di-modal__title">İçe Aktarımı Onayla</h3>
            <p className="di-modal__msg">{confirm.message}</p>
            <div className="di-modal__actions">
              <button className="di-btn" onClick={() => setConfirm(null)}>Vazgeç</button>
              <button className="di-btn di-btn--ok" onClick={confirm.onOk} autoFocus>İçe Aktar</button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

function PreviewTable({ preview }) {
  const labels = preview.columnLabels || []
  const keys = preview.columnKeys || []
  return (
    <div className="di-preview-wrap">
      <table className="di-preview">
        <thead>
          <tr>
            <th>#</th><th>Durum</th>
            {labels.map((l, i) => <th key={i}>{l}</th>)}
            <th>Not</th>
          </tr>
        </thead>
        <tbody>
          {preview.rows.map(r => {
            const cellMap = {}
            for (const c of r.cells) cellMap[c.target] = c.value
            return (
              <tr key={r.rowNumber}>
                <td>{r.rowNumber}</td>
                <td><span className={'di-tag di-tag--' + r.action}>
                  {r.action === 'insert' ? 'Yeni' : r.action === 'update' ? 'Güncelle' : 'Hata'}
                </span></td>
                {keys.map(k => <td key={k}>{cellMap[k] ?? ''}</td>)}
                <td className="di-rowerr">{(r.errors || []).join('; ')}</td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}
