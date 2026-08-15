import React from 'react'
import {
  Database, ArrowLeft, ArrowRight, Save, Play, Loader2, Search,
  CheckCircle2, XCircle, AlertTriangle, KeyRound, Wand2, Trash2, Filter, Plus,
} from 'lucide-react'
import { apiGet, apiPost } from './dbiApi'
import './DbImport.css'

/*
 * DbImportWizard — harici SQL kaynağından içe aktarım işi tanımlama + çalıştırma.
 *
 * Adımlar: Kaynak → Eşleme → Prosedürler → Önizleme → Aktar
 *
 * ANAHTAR ALAN ZORUNLUDUR. Bu iş elle ya da cron ile tekrar tekrar çalışır;
 * anahtarsız bir aktarım her turda kayıtları çoğaltır. UI ilerlemeyi/kaydetmeyi
 * engeller, sunucu da ayrıca reddeder (DataImportJob.Validate).
 *
 * Enum'lar API'den string gelir (JsonStringEnumConverter) — normalize edilir.
 */

const STEPS = [
  { n: 1, label: 'Kaynak' },
  { n: 2, label: 'Eşleme' },
  { n: 3, label: 'Prosedürler' },
  { n: 4, label: 'Önizleme' },
  { n: 5, label: 'Aktar' },
]

const ERROR_BEHAVIOR_NUM = { Lenient: 0, Strict: 1 }
const PROC_TARGET_NUM = { Calibra: 0, Source: 1 }

function normalizeEnum(value, map) {
  if (typeof value === 'number') return value
  if (typeof value === 'string' && value in map) return map[value]
  return 0
}

function qs(name) {
  try { return new URLSearchParams(window.location.search).get(name) } catch (_) { return null }
}

/* Kısıt operatörleri — dışa aktarımdaki "Kısıt Kuralları" ile aynı sözlük. */
const FILTER_OPS = [
  { v: 'eq', label: 'eşittir' },
  { v: 'neq', label: 'eşit değildir' },
  { v: 'gt', label: 'büyüktür' },
  { v: 'gte', label: 'büyük veya eşit' },
  { v: 'lt', label: 'küçüktür' },
  { v: 'lte', label: 'küçük veya eşit' },
  { v: 'contains', label: 'içerir' },
  { v: 'startsWith', label: 'ile başlar' },
  { v: 'in', label: 'listede (virgülle ayır)' },
  { v: 'between', label: 'arasında (iki değer)' },
  { v: 'isnull', label: 'boş' },
  { v: 'notnull', label: 'dolu' },
]

/** Değer istemeyen operatörler. */
const OPS_WITHOUT_VALUE = new Set(['isnull', 'notnull'])

function parseFilters(json) {
  if (!json) return []
  try {
    const a = JSON.parse(json)
    return Array.isArray(a) ? a.filter((r) => r && r.field) : []
  } catch (_) { return [] }
}

/** Kaynak kolon adı ile hedef alanı kabaca eşleştirir (otomatik eşleme önerisi). */
function normKey(s) {
  return String(s || '')
    .toLocaleLowerCase('tr')
    .replace(/[\s_\-.]/g, '')
}

export default function DbImportWizard() {
  const jobId = Number(qs('id')) || 0
  const mode = qs('mode') || (jobId > 0 ? 'edit' : 'new')

  const [step, setStep] = React.useState(mode === 'run' ? 4 : 1)
  const [busy, setBusy] = React.useState(false)
  const [error, setError] = React.useState(null)
  const [notice, setNotice] = React.useState(null)

  const [connections, setConnections] = React.useState([])
  const [entities, setEntities] = React.useState([])
  const [objects, setObjects] = React.useState([])
  const [objectSearch, setObjectSearch] = React.useState('')
  const [sourceColumns, setSourceColumns] = React.useState([])
  const [targetFields, setTargetFields] = React.useState([])

  const [job, setJob] = React.useState({
    id: 0,
    name: '',
    connectionId: 0,
    targetEntity: '',
    sourceSchema: 'dbo',
    sourceObject: '',
    matchKeyFields: [],
    maxRows: 50000,
    sourceFilterJson: '',
    deactivateAbsent: false,
    errorBehavior: 0,
    preProcedureName: '',
    preProcedureTarget: 0,
    preProcedureParamsJson: '',
    postProcedureName: '',
    postProcedureTarget: 0,
    postProcedureParamsJson: '',
    columns: [],
    isActive: true,
  })

  const [preview, setPreview] = React.useState(null)
  const [runResult, setRunResult] = React.useState(null)

  // ── İlk yükleme ────────────────────────────────────────────────────
  React.useEffect(() => {
    (async () => {
      setBusy(true)
      try {
        const [connRes, entRes] = await Promise.all([
          apiGet('/DbImport/api/connections?includeInactive=false'),
          apiGet('/DbImport/api/entities'),
        ])
        if (connRes && connRes.success) setConnections(connRes.items || [])
        if (entRes && entRes.success) setEntities(entRes.entities || [])

        if (jobId > 0) {
          const res = await apiGet(`/DbImport/api/jobs/${jobId}`)
          if (res && res.success && res.job) {
            const j = res.job
            setJob({
              ...j,
              errorBehavior: normalizeEnum(j.errorBehavior, ERROR_BEHAVIOR_NUM),
              preProcedureTarget: normalizeEnum(j.preProcedureTarget, PROC_TARGET_NUM),
              postProcedureTarget: normalizeEnum(j.postProcedureTarget, PROC_TARGET_NUM),
              sourceFilterJson: j.sourceFilterJson || '',
              deactivateAbsent: !!j.deactivateAbsent,
              preProcedureName: j.preProcedureName || '',
              preProcedureParamsJson: j.preProcedureParamsJson || '',
              postProcedureName: j.postProcedureName || '',
              postProcedureParamsJson: j.postProcedureParamsJson || '',
              columns: j.columns || [],
            })
          } else {
            setError((res && res.error) || 'Aktarım işi yüklenemedi.')
          }
        }
      } catch (_) {
        setError('Başlangıç verileri yüklenemedi.')
      } finally {
        setBusy(false)
      }
    })()
  }, [jobId])

  // ── Bağlantı seçilince kaynak nesneleri ────────────────────────────
  React.useEffect(() => {
    if (!job.connectionId) { setObjects([]); return }
    let cancelled = false
    ;(async () => {
      const res = await apiGet(`/DbImport/api/source-objects?connectionId=${job.connectionId}`)
      if (cancelled) return
      if (res && res.success) setObjects(res.items || [])
      else setError((res && res.error) || 'Kaynak nesneleri okunamadı.')
    })()
    return () => { cancelled = true }
  }, [job.connectionId])

  // ── Nesne seçilince kolonlar ───────────────────────────────────────
  React.useEffect(() => {
    if (!job.connectionId || !job.sourceObject) { setSourceColumns([]); return }
    let cancelled = false
    ;(async () => {
      const url = `/DbImport/api/source-columns?connectionId=${job.connectionId}`
        + `&schema=${encodeURIComponent(job.sourceSchema)}&obj=${encodeURIComponent(job.sourceObject)}`
      const res = await apiGet(url)
      if (cancelled) return
      if (res && res.success) setSourceColumns(res.items || [])
    })()
    return () => { cancelled = true }
  }, [job.connectionId, job.sourceSchema, job.sourceObject])

  // ── Hedef entity seçilince alan kataloğu ───────────────────────────
  React.useEffect(() => {
    if (!job.targetEntity) { setTargetFields([]); return }
    let cancelled = false
    ;(async () => {
      const res = await apiGet(`/DbImport/api/target-fields?entity=${encodeURIComponent(job.targetEntity)}`)
      if (cancelled) return
      if (res && res.success) setTargetFields(res.fields || [])
    })()
    return () => { cancelled = true }
  }, [job.targetEntity])

  // ── Türetilmiş ─────────────────────────────────────────────────────
  const filteredObjects = React.useMemo(() => {
    const q = objectSearch.trim().toLocaleLowerCase('tr')
    if (!q) return objects
    return objects.filter((o) => `${o.schemaName}.${o.objectName}`.toLocaleLowerCase('tr').includes(q))
  }, [objects, objectSearch])

  const mappedByTarget = React.useMemo(() => {
    const m = {}
    for (const c of job.columns) m[c.targetKey] = c.sourceColumn
    return m
  }, [job.columns])

  const matchKeys = job.matchKeyFields || []
  // Her anahtar bir kaynak kolona eşlenmiş olmalı; biri bile eksikse eşleşme hep başarısız olur.
  const unmappedKeys = matchKeys.filter((k) => !mappedByTarget[k])
  const matchKeyMapped = matchKeys.length > 0 && unmappedKeys.length === 0
  const keyCandidates = targetFields.filter((f) => f.canBeMatchKey)

  function toggleMatchKey(key) {
    const next = matchKeys.includes(key) ? matchKeys.filter((k) => k !== key) : [...matchKeys, key]
    setJob((prev) => ({ ...prev, matchKeyFields: next }))
  }

  const filters = React.useMemo(() => parseFilters(job.sourceFilterJson), [job.sourceFilterJson])

  function writeFilters(next) {
    const clean = next.filter((r) => r.field)
    setJob((prev) => ({ ...prev, sourceFilterJson: clean.length ? JSON.stringify(clean) : '' }))
  }

  function setMapping(targetKey, sourceColumn) {
    setJob((prev) => {
      const rest = prev.columns.filter((c) => c.targetKey !== targetKey)
      if (!sourceColumn) return { ...prev, columns: rest }
      return {
        ...prev,
        columns: [...rest, { targetKey, sourceColumn, sortOrder: rest.length + 1 }],
      }
    })
  }

  /** Ad benzerliğine göre otomatik eşleme — kullanıcı sonrasında düzeltebilir. */
  function autoMap() {
    const bySource = {}
    for (const c of sourceColumns) bySource[normKey(c.name)] = c.name
    const next = []
    for (const f of targetFields) {
      const hit = bySource[normKey(f.key)] || bySource[normKey(f.label)]
      if (hit) next.push({ targetKey: f.key, sourceColumn: hit, sortOrder: next.length + 1 })
    }
    setJob((prev) => ({ ...prev, columns: next }))
    setNotice(`${next.length} alan otomatik eşlendi. Lütfen kontrol edin.`)
  }

  const step1Ok = job.connectionId > 0 && !!job.sourceObject && !!job.name.trim()
  const step2Ok = job.targetEntity && job.columns.length > 0 && matchKeyMapped

  async function save() {
    setBusy(true); setError(null)
    try {
      const res = await apiPost('/DbImport/api/jobs/save', job)
      if (res && res.success) {
        setJob((prev) => ({ ...prev, id: res.id }))
        setNotice('Aktarım işi kaydedildi.')
        return res.id
      }
      setError((res && res.error) || 'Kaydedilemedi.')
      return 0
    } catch (_) {
      setError('Kaydedilemedi.')
      return 0
    } finally {
      setBusy(false)
    }
  }

  async function doPreview() {
    let id = job.id
    if (!id) { id = await save(); if (!id) return }
    setBusy(true); setError(null); setPreview(null)
    try {
      const res = await apiPost(`/DbImport/api/jobs/${id}/preview?sampleRows=50`)
      if (res && res.success) { setPreview(res); setStep(4) }
      else setError((res && res.error) || 'Önizleme yapılamadı.')
    } catch (_) {
      setError('Önizleme yapılamadı.')
    } finally {
      setBusy(false)
    }
  }

  async function doRun() {
    let id = job.id
    if (!id) { id = await save(); if (!id) return }
    setBusy(true); setError(null); setRunResult(null)
    try {
      const res = await apiPost(`/DbImport/api/jobs/${id}/run`)
      setRunResult(res)
      setStep(5)
      if (!res || !res.success) setError((res && res.error) || 'Aktarım başarısız.')
    } catch (_) {
      setError('Aktarım başarısız.')
    } finally {
      setBusy(false)
    }
  }

  // ── Render ─────────────────────────────────────────────────────────
  return (
    <div className="dbi-root">
      <div className="dbi-header">
        <div className="dbi-header-icon"><Database size={19} /></div>
        <div>
          <div className="dbi-header-title">
            {mode === 'run' ? 'Aktarımı Çalıştır' : (job.id > 0 ? 'Aktarım İşini Düzenle' : 'Yeni Aktarım İşi')}
          </div>
          <div className="dbi-header-sub">{job.name || 'Adsız iş'}</div>
        </div>
        <div className="dbi-header-spacer" />
        <div className="dbi-steps">
          {STEPS.map((s) => (
            <button
              key={s.n}
              type="button"
              className={`dbi-step ${step === s.n ? 'dbi-step--active' : (step > s.n ? 'dbi-step--done' : '')}`}
              onClick={() => setStep(s.n)}
            >
              {step > s.n ? <CheckCircle2 size={12} /> : <span>{s.n}</span>} {s.label}
            </button>
          ))}
        </div>
        <a className="dbi-btn" href="/DbImport"><ArrowLeft size={14} /> Listeye Dön</a>
      </div>

      <div className="dbi-body">
        {error && <div className="dbi-alert dbi-alert--err"><XCircle size={13} /> {error}</div>}
        {notice && <div className="dbi-alert dbi-alert--ok"><CheckCircle2 size={13} /> {notice}</div>}
        {busy && <div className="dbi-alert dbi-alert--warn"><Loader2 size={13} /> İşleniyor…</div>}

        {/* ── 1. Kaynak ── */}
        {step === 1 && (
          <>
            <div className="dbi-card">
              <div className="dbi-card-title">İş Tanımı</div>
              <div className="dbi-field">
                <span className="dbi-label">İş Adı <span className="dbi-required">*</span></span>
                <input className="dbi-input" value={job.name}
                       onChange={(e) => setJob({ ...job, name: e.target.value })}
                       placeholder="Örn. Netsis Cari Aktarımı" />
              </div>
              <div className="dbi-field">
                <span className="dbi-label">Kaynak Bağlantı <span className="dbi-required">*</span></span>
                <select className="dbi-select" value={job.connectionId}
                        onChange={(e) => setJob({ ...job, connectionId: Number(e.target.value), sourceObject: '' })}>
                  <option value={0}>— Seçiniz —</option>
                  {connections.map((c) => (
                    <option key={c.id} value={c.id}>{c.name} ({c.serverName} · {c.databaseName})</option>
                  ))}
                </select>
                {connections.length === 0 && (
                  <span className="dbi-hint">Aktif bağlantı yok — <a href="/DbImport/Connections">bağlantı ekleyin</a>.</span>
                )}
              </div>
            </div>

            <div className="dbi-card">
              <div className="dbi-card-title">Kaynak Tablo / View</div>
              <div className="dbi-search" style={{ marginBottom: 10 }}>
                <Search size={14} />
                <input value={objectSearch} onChange={(e) => setObjectSearch(e.target.value)} placeholder="Tablo veya view ara…" />
              </div>
              {!job.connectionId && <div className="dbi-hint">Önce bir kaynak bağlantısı seçin.</div>}
              {job.connectionId > 0 && filteredObjects.length === 0 && (
                <div className="dbi-hint">Okunabilir tablo/view bulunamadı.</div>
              )}
              {filteredObjects.length > 0 && (
                <div className="dbi-table-wrap" style={{ maxHeight: 320, overflowY: 'auto' }}>
                  <table className="dbi-table">
                    <thead>
                      <tr><th>Şema</th><th>Ad</th><th>Tür</th><th /></tr>
                    </thead>
                    <tbody>
                      {filteredObjects.map((o) => {
                        const selected = job.sourceSchema === o.schemaName && job.sourceObject === o.objectName
                        return (
                          <tr key={`${o.schemaName}.${o.objectName}`}>
                            <td className="dbi-mono">{o.schemaName}</td>
                            <td className="dbi-mono">{o.objectName}</td>
                            <td>{o.kind === 'Table' ? 'Tablo' : 'View'}</td>
                            <td>
                              <button type="button"
                                      className={`dbi-btn dbi-btn--xs ${selected ? 'dbi-btn--primary' : ''}`}
                                      onClick={() => setJob({ ...job, sourceSchema: o.schemaName, sourceObject: o.objectName, columns: [] })}>
                                {selected ? 'Seçili' : 'Seç'}
                              </button>
                            </td>
                          </tr>
                        )
                      })}
                    </tbody>
                  </table>
                </div>
              )}
            </div>
          </>
        )}

        {/* ── 2. Eşleme ── */}
        {step === 2 && (
          <>
            <div className="dbi-card">
              <div className="dbi-card-title">Hedef</div>
              <div className="dbi-grid">
                <div className="dbi-field">
                  <span className="dbi-label">Hedef Kayıt Türü <span className="dbi-required">*</span></span>
                  <select className="dbi-select" value={job.targetEntity}
                          onChange={(e) => setJob({ ...job, targetEntity: e.target.value, columns: [], matchKeyFields: [] })}>
                    <option value="">— Seçiniz —</option>
                    {entities.map((e) => <option key={e.entity} value={e.entity}>{e.label}</option>)}
                  </select>
                  {/* Insert-only handler: anahtar alan yok sayılır, tekrar çalıştırma mükerrer üretir. */}
                  {job.targetEntity && entities.find((e) => e.entity === job.targetEntity)?.supportsUpsert === false && (
                    <div className="dbi-alert dbi-alert--warn" style={{ marginTop: 8, marginBottom: 0 }}>
                      <AlertTriangle size={13} /> Bu kayıt türü güncelleme desteklemiyor — her çalıştırma
                      yeni kayıt açar. Anahtar alan mükerrer oluşmasını <strong>engellemez</strong>.
                      Tek seferlik aktarım için kullanın, zamanlanmış göreve bağlamayın.
                    </div>
                  )}
                </div>
                <div className="dbi-field">
                  <span className="dbi-label">Azami Satır</span>
                  <input className="dbi-input" type="number" min="1" value={job.maxRows}
                         onChange={(e) => setJob({ ...job, maxRows: Number(e.target.value) || 50000 })} />
                </div>
              </div>
            </div>

            <div className="dbi-card">
              <div className="dbi-card-title" style={{ display: 'flex', alignItems: 'center' }}>
                <span><Filter size={13} /> Kısıt Kuralları</span>
                <div className="dbi-header-spacer" />
                <button type="button" className="dbi-btn dbi-btn--xs"
                        onClick={() => writeFilters([...filters, { field: '', op: 'eq', value: '' }])}
                        disabled={!sourceColumns.length}>
                  <Plus size={13} /> Kural Ekle
                </button>
              </div>

              {filters.length === 0 && (
                <div className="dbi-hint">Kural yoksa kaynaktaki tüm satırlar aktarılır.</div>
              )}

              {filters.map((r, i) => (
                <div key={i} style={{ display: 'flex', gap: 8, alignItems: 'center', marginBottom: 8 }}>
                  <select className="dbi-select" style={{ maxWidth: 200 }} value={r.field}
                          onChange={(e) => { const n = [...filters]; n[i] = { ...r, field: e.target.value }; writeFilters(n) }}>
                    <option value="">— Kolon —</option>
                    {sourceColumns.map((c) => <option key={c.name} value={c.name}>{c.name}</option>)}
                  </select>
                  <select className="dbi-select" style={{ maxWidth: 190 }} value={r.op}
                          onChange={(e) => { const n = [...filters]; n[i] = { ...r, op: e.target.value }; writeFilters(n) }}>
                    {FILTER_OPS.map((o) => <option key={o.v} value={o.v}>{o.label}</option>)}
                  </select>
                  {!OPS_WITHOUT_VALUE.has(r.op) && (
                    <input className="dbi-input" style={{ maxWidth: 200 }} value={r.value || ''}
                           onChange={(e) => { const n = [...filters]; n[i] = { ...r, value: e.target.value }; writeFilters(n) }}
                           placeholder={r.op === 'between' ? '1,100' : (r.op === 'in' ? 'A,B,C' : 'değer')} />
                  )}
                  <button type="button" className="dbi-btn dbi-btn--xs dbi-btn--danger"
                          onClick={() => writeFilters(filters.filter((_, j) => j !== i))}>
                    <Trash2 size={13} />
                  </button>
                </div>
              ))}
            </div>

            <div className="dbi-card">
              <div className="dbi-card-title">Kaynakta Olmayan Kayıtlar</div>
              <label className="dbi-switch">
                <input type="checkbox" checked={!!job.deactivateAbsent}
                       onChange={(e) => setJob({ ...job, deactivateAbsent: e.target.checked })} />
                <span className="dbi-switch-track"><span className="dbi-switch-thumb" /></span>
                <span>Kaynakta olmayan kayıtları pasife al</span>
              </label>
              {job.deactivateAbsent && (
                <div className="dbi-alert dbi-alert--warn" style={{ marginTop: 8, marginBottom: 0 }}>
                  <AlertTriangle size={13} /> Kaynak sorgusu bu kayıt türünün <strong>tamamını</strong>
                  {' '}döndürmeli. Dar bir sorgu, kapsam dışı kalan kayıtları da pasife alır.
                  Kayıt kaynağa geri dönerse otomatik aktifleşir.
                </div>
              )}
            </div>

            <div className="dbi-card">
              <div className="dbi-card-title">
                <KeyRound size={13} /> Anahtar Alan <span className="dbi-required">*</span>
              </div>
              <div className="dbi-field">
                <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
                  {keyCandidates.length === 0 && <span className="dbi-hint">Önce hedef kayıt türünü seçin.</span>}
                  {keyCandidates.map((f) => {
                    const on = matchKeys.includes(f.key)
                    return (
                      <button key={f.key} type="button"
                              className={`dbi-btn dbi-btn--xs ${on ? 'dbi-btn--primary' : ''}`}
                              onClick={() => toggleMatchKey(f.key)}>
                        {on && <KeyRound size={11} />} {f.label}
                      </button>
                    )
                  })}
                </div>
                <span className="dbi-hint">
                  Mevcut kaydı bulmak için kullanılır. Birden fazla seçerseniz <strong>hepsi</strong> eşleşmelidir.
                </span>
              </div>
              {unmappedKeys.length > 0 && (
                <div className="dbi-alert dbi-alert--err" style={{ marginBottom: 0 }}>
                  <AlertTriangle size={13} /> Şu anahtar alan(lar) bir kaynak kolonuna eşlenmeli:{' '}
                  {unmappedKeys.join(', ')}
                </div>
              )}
            </div>

            <div className="dbi-card">
              <div className="dbi-card-title" style={{ display: 'flex', alignItems: 'center' }}>
                <span>Kolon Eşleme</span>
                <div className="dbi-header-spacer" />
                <button type="button" className="dbi-btn dbi-btn--xs" onClick={autoMap}
                        disabled={!targetFields.length || !sourceColumns.length}>
                  <Wand2 size={13} /> Otomatik Eşle
                </button>
                <button type="button" className="dbi-btn dbi-btn--xs" style={{ marginLeft: 6 }}
                        onClick={() => setJob({ ...job, columns: [] })} disabled={!job.columns.length}>
                  <Trash2 size={13} /> Temizle
                </button>
              </div>

              {!job.targetEntity && <div className="dbi-hint">Önce hedef kayıt türünü seçin.</div>}

              {job.targetEntity && targetFields.length > 0 && (
                <div className="dbi-table-wrap" style={{ maxHeight: 420, overflowY: 'auto' }}>
                  <table className="dbi-table">
                    <thead>
                      <tr><th>Hedef Alan</th><th>Kaynak Kolon</th><th>Tür</th></tr>
                    </thead>
                    <tbody>
                      {targetFields.map((f) => (
                        <tr key={f.key}>
                          <td>
                            {f.label}
                            {f.isRequired && <span className="dbi-required"> *</span>}
                            {matchKeys.includes(f.key) && <span className="dbi-key-pill" style={{ marginLeft: 6 }}><KeyRound size={10} /> Anahtar</span>}
                            {/* Kabul edilen değerler — kaynak view'ı buna göre şekillendirmek için. */}
                            {f.allowedValues && f.allowedValues.length > 0 && (
                              <div className="dbi-hint dbi-mono" style={{ whiteSpace: 'normal', maxWidth: 320 }}
                                   title={f.allowedValues.join(' · ')}>
                                {f.allowedValues.slice(0, 8).join(' · ')}
                                {f.allowedValues.length > 8 ? ` · +${f.allowedValues.length - 8}` : ''}
                              </div>
                            )}
                          </td>
                          <td>
                            <select className="dbi-select" value={mappedByTarget[f.key] || ''}
                                    onChange={(e) => setMapping(f.key, e.target.value)}>
                              <option value="">— Eşlenmedi —</option>
                              {sourceColumns.map((c) => <option key={c.name} value={c.name}>{c.name}</option>)}
                            </select>
                          </td>
                          <td className="dbi-mono">{f.dataType}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>
          </>
        )}

        {/* ── 3. Prosedürler ── */}
        {step === 3 && (
          <>
            <div className="dbi-card">
              <div className="dbi-card-title">Aktarım Öncesi Prosedür</div>
              <div className="dbi-grid">
                <div className="dbi-field">
                  <span className="dbi-label">Prosedür Adı</span>
                  <input className="dbi-input dbi-mono" value={job.preProcedureName}
                         onChange={(e) => setJob({ ...job, preProcedureName: e.target.value })}
                         placeholder="dbo.sp_AktarimHazirla" />
                </div>
                <div className="dbi-field">
                  <span className="dbi-label">Çalışacağı Veritabanı</span>
                  <select className="dbi-select" value={job.preProcedureTarget}
                          onChange={(e) => setJob({ ...job, preProcedureTarget: Number(e.target.value) })}>
                    <option value={0}>CalibraHub Veritabanı</option>
                    <option value={1}>Kaynak Veritabanı (yazma)</option>
                  </select>
                </div>
              </div>
              <div className="dbi-field">
                <span className="dbi-label">Parametreler (JSON)</span>
                <textarea className="dbi-input dbi-mono" rows={3} value={job.preProcedureParamsJson}
                          onChange={(e) => setJob({ ...job, preProcedureParamsJson: e.target.value })}
                          placeholder='[{"name":"@JobId","sourceType":"RunMeta","sourceValue":"JobId"}]' />
                <span className="dbi-hint">Constant · RunMeta (RunId, JobId, JobName, StartedAt, TriggeredBy)</span>
              </div>
            </div>

            <div className="dbi-card">
              <div className="dbi-card-title">Aktarım Sonrası Prosedür</div>
              <div className="dbi-grid">
                <div className="dbi-field">
                  <span className="dbi-label">Prosedür Adı</span>
                  <input className="dbi-input dbi-mono" value={job.postProcedureName}
                         onChange={(e) => setJob({ ...job, postProcedureName: e.target.value })}
                         placeholder="dbo.sp_AktarimTamamlandi" />
                </div>
                <div className="dbi-field">
                  <span className="dbi-label">Çalışacağı Veritabanı</span>
                  <select className="dbi-select" value={job.postProcedureTarget}
                          onChange={(e) => setJob({ ...job, postProcedureTarget: Number(e.target.value) })}>
                    <option value={0}>CalibraHub Veritabanı</option>
                    <option value={1}>Kaynak Veritabanı (yazma)</option>
                  </select>
                </div>
              </div>
              <div className="dbi-field">
                <span className="dbi-label">Parametreler (JSON)</span>
                <textarea className="dbi-input dbi-mono" rows={3} value={job.postProcedureParamsJson}
                          onChange={(e) => setJob({ ...job, postProcedureParamsJson: e.target.value })}
                          placeholder='[{"name":"@Eklenen","sourceType":"Stats","sourceValue":"RowsInserted"}]' />
                <span className="dbi-hint">Constant · RunMeta · Stats (RowsRead, RowsInserted, RowsUpdated, RowsFailed)</span>
              </div>
            </div>

            <div className="dbi-card">
              <div className="dbi-card-title">Hata Davranışı</div>
              <div className="dbi-field">
                <select className="dbi-select" value={job.errorBehavior}
                        onChange={(e) => setJob({ ...job, errorBehavior: Number(e.target.value) })}>
                  <option value={0}>Esnek — son prosedür hatası yalnızca uyarır</option>
                  <option value={1}>Katı — son prosedür hatası aktarımı başarısız sayar</option>
                </select>
              </div>
            </div>
          </>
        )}

        {/* ── 4. Önizleme ── */}
        {step === 4 && (
          <>
            <div className="dbi-card">
              <div className="dbi-card-title">Önizleme</div>
              <button type="button" className="dbi-btn dbi-btn--primary" onClick={doPreview} disabled={busy}>
                <Search size={14} /> Önizlemeyi Çalıştır
              </button>
            </div>

            {preview && preview.preview && (
              <>
                <div className="dbi-stats">
                  <div className="dbi-stat"><div className="dbi-stat-label">Okunan</div><div className="dbi-stat-value">{preview.rowsRead}</div></div>
                  <div className="dbi-stat dbi-stat--ok"><div className="dbi-stat-label">Geçerli</div><div className="dbi-stat-value">{preview.preview.validRows}</div></div>
                  <div className="dbi-stat"><div className="dbi-stat-label">Eklenecek</div><div className="dbi-stat-value">{preview.preview.insertCount}</div></div>
                  <div className="dbi-stat"><div className="dbi-stat-label">Güncellenecek</div><div className="dbi-stat-value">{preview.preview.updateCount}</div></div>
                  <div className="dbi-stat dbi-stat--err"><div className="dbi-stat-label">Hatalı</div><div className="dbi-stat-value">{preview.preview.errorRows}</div></div>
                  {job.deactivateAbsent && (
                    <div className="dbi-stat dbi-stat--warn">
                      <div className="dbi-stat-label">Pasife Alınacak</div>
                      <div className="dbi-stat-value">{preview.deactivateCount ?? 0}</div>
                    </div>
                  )}
                </div>
                {preview.deactivateWarning && (
                  <div className="dbi-alert dbi-alert--warn">{preview.deactivateWarning}</div>
                )}

                {preview.preview.rows && preview.preview.rows.length > 0 && (
                  <div className="dbi-table-wrap" style={{ maxHeight: 380, overflowY: 'auto' }}>
                    <table className="dbi-table">
                      <thead>
                        <tr>
                          <th>#</th>
                          <th>Sonuç</th>
                          {(preview.preview.columnLabels || []).map((l, i) => <th key={i}>{l}</th>)}
                        </tr>
                      </thead>
                      <tbody>
                        {preview.preview.rows.map((r, i) => {
                          // Cells listesi hedef alan anahtarına göre indekslenir; kolon
                          // sırası columnKeys'ten gelir (eşlenmemiş alan boş kalır).
                          const byTarget = {}
                          for (const c of (r.cells || [])) byTarget[c.target] = c.value
                          const errors = r.errors || []
                          return (
                            <tr key={i}>
                              <td>{r.rowNumber}</td>
                              <td className={errors.length ? 'dbi-cell-err' : ''}>
                                {errors.length
                                  ? <span className="dbi-badge dbi-badge--err" title={errors.join(' · ')}>{errors[0]}</span>
                                  : <span className="dbi-badge dbi-badge--ok">{r.action === 'update' ? 'Güncellenecek' : 'Eklenecek'}</span>}
                              </td>
                              {(preview.preview.columnKeys || []).map((k, j) => (
                                <td key={j}>{byTarget[k] ?? ''}</td>
                              ))}
                            </tr>
                          )
                        })}
                      </tbody>
                    </table>
                  </div>
                )}
              </>
            )}
          </>
        )}

        {/* ── 5. Aktar ── */}
        {step === 5 && (
          <>
            <div className="dbi-card">
              <div className="dbi-card-title">Aktarımı Çalıştır</div>
              <button type="button" className="dbi-btn dbi-btn--primary" onClick={doRun} disabled={busy}>
                <Play size={14} /> Aktarımı Başlat
              </button>
            </div>

            {runResult && runResult.run && (
              <>
                <div className="dbi-stats">
                  <div className="dbi-stat"><div className="dbi-stat-label">Okunan</div><div className="dbi-stat-value">{runResult.run.rowsRead}</div></div>
                  <div className="dbi-stat dbi-stat--ok"><div className="dbi-stat-label">Eklenen</div><div className="dbi-stat-value">{runResult.run.rowsInserted}</div></div>
                  <div className="dbi-stat dbi-stat--ok"><div className="dbi-stat-label">Güncellenen</div><div className="dbi-stat-value">{runResult.run.rowsUpdated}</div></div>
                  <div className="dbi-stat dbi-stat--err"><div className="dbi-stat-label">Hatalı</div><div className="dbi-stat-value">{runResult.run.rowsFailed}</div></div>
                  {runResult.run.rowsDeactivated > 0 && (
                    <div className="dbi-stat dbi-stat--warn">
                      <div className="dbi-stat-label">Pasife Alınan</div>
                      <div className="dbi-stat-value">{runResult.run.rowsDeactivated}</div>
                    </div>
                  )}
                  <div className="dbi-stat"><div className="dbi-stat-label">Süre</div><div className="dbi-stat-value">{runResult.run.durationMs ?? 0} ms</div></div>
                </div>

                {runResult.run.preProcedureResult && (
                  <div className="dbi-alert dbi-alert--warn">Ön prosedür — {runResult.run.preProcedureResult}</div>
                )}
                {runResult.run.postProcedureResult && (
                  <div className="dbi-alert dbi-alert--warn">Son prosedür — {runResult.run.postProcedureResult}</div>
                )}
                {runResult.run.errorMessage && (
                  <div className="dbi-alert dbi-alert--err">{runResult.run.errorMessage}</div>
                )}
              </>
            )}
          </>
        )}
      </div>

      {/* ── Alt aksiyon şeridi ── */}
      <div className="dbi-modal-foot" style={{ borderTop: '1px solid var(--dbi-border)', background: 'var(--dbi-surface)' }}>
        <button type="button" className="dbi-btn" onClick={() => setStep(Math.max(1, step - 1))} disabled={step === 1}>
          <ArrowLeft size={14} /> Geri
        </button>
        <div className="dbi-header-spacer" />
        <button type="button" className="dbi-btn" onClick={save} disabled={busy || !step1Ok || !step2Ok}>
          <Save size={14} /> Kaydet
        </button>
        <button type="button" className="dbi-btn dbi-btn--primary"
                onClick={() => setStep(Math.min(5, step + 1))}
                disabled={step === 5 || (step === 1 && !step1Ok) || (step === 2 && !step2Ok)}>
          İleri <ArrowRight size={14} />
        </button>
      </div>
    </div>
  )
}
