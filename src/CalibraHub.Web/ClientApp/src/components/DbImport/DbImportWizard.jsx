import React from 'react'
import {
  Database, ArrowLeft, ArrowRight, Save, Play, Loader2, Search,
  CheckCircle2, XCircle, AlertTriangle, KeyRound, Wand2, Trash2, Filter, Plus, ListChecks, X, CalendarClock,
  Lock,
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

/*
 * Zamanlama modalı, mevcut "Zamanlanmış Görev" ekranını (/Admin/ScheduledTaskEdit)
 * gömülü iframe olarak açar (bkz. yorum aşağıda). O ekran GENEL bir form — 7 farklı
 * görev türünü destekler ve kullanıcı "Görev Türü" sekmesinden serbestçe başka bir
 * türe (SQL Prosedür, HTTP API…) geçebilir ya da "Aktarım İşi" seçicisinden başka
 * bir içe aktarım işini seçebilir. Bu wizard'dan açılan zamanlama HER ZAMAN bu işe
 * (jobId) özeldir — tür veya iş değişirse zamanlama sessizce yanlış/kopuk bir göreve
 * dönüşür. Aynı origin (same-origin) iframe olduğundan, sayfa yüklendikten sonra bu
 * iki alanı DOM üzerinden kilitleriz (görsel + etkileşimsiz); ekranın kendi kaydetme
 * mantığı `.value`'yu okuduğu için değer kaydetme payload'ına doğru gider (disabled
 * olsa da .value okunabilir — form submit değil, fetch ile elle toplanıyor).
 * NOT: Bu, DbImport tarafındaki tek taraflı bir görsel kilittir; sunucu tarafında
 * ScheduledTaskController.ScheduledTaskSave şu an taskType/jobId'yi doğrulamıyor —
 * eski sekme veya elle istekle bu kilit atlatılabilir. Kalıcı çözüm (ScheduledTaskEdit
 * ekranına native `locked=1` desteği + server-side sabitleme) backend/ScheduledTask
 * kapsamındadır, bu görevin sınırları dışında — flag'lendi.
 */
function lockScheduleIframeToJob(iframeEl, jobId) {
  try {
    const doc = iframeEl && iframeEl.contentDocument
    if (!doc) return

    // 1) Görev Türü kartlarını kilitle — bu zamanlama yalnızca "Veritabanı Aktarımı" türünde kalmalı.
    const cards = doc.querySelectorAll('.ste-type-card')
    if (cards.length) {
      cards.forEach((card) => {
        const input = card.querySelector('input')
        const isImportCard = !!input && input.value === '8'
        card.style.pointerEvents = 'none'
        card.style.opacity = isImportCard ? '1' : '0.4'
        card.title = isImportCard
          ? 'İçe aktarım görevleri için sabittir.'
          : 'Bu zamanlama bir içe aktarım işine bağlı — görev türü değiştirilemez.'
      })
      const typePanel = doc.getElementById('panel-tasktype')
      if (typePanel && !doc.querySelector('.dbi-schedule-lock-note')) {
        const note = doc.createElement('div')
        note.className = 'dbi-schedule-lock-note'
        note.style.cssText = 'display:flex;align-items:center;gap:8px;margin:0 0 14px;padding:10px 14px;'
          + 'border-radius:8px;background:rgba(99,102,241,.12);border:1px solid rgba(99,102,241,.35);'
          + 'color:#a5b4fc;font-size:13px;line-height:1.5;'
        note.textContent = '🔒 Görev türü ve aktarım işi bu içe aktarım sihirbazından geldiği için sabittir — değiştirilemez.'
        typePanel.insertBefore(note, typePanel.firstChild)
      }
    }

    // 2) Aktarım İşi seçiciyi kilitle — liste iframe içinde async (fetch) doluyor,
    //    dolana kadar kısa aralıklarla dener (üst sınır ~6 sn).
    let tries = 0
    const lockJobSelect = () => {
      const sel = doc.getElementById('ste-dbimport-job')
      if (!sel) return
      if (sel.options.length > 1) {
        sel.value = String(jobId)
        sel.disabled = true
        sel.title = 'Bu zamanlama bu aktarım işine özeldir — iş değiştirilemez.'
        return
      }
      tries += 1
      if (tries < 40) setTimeout(lockJobSelect, 150)
    }
    lockJobSelect()
  } catch (_) {
    // Aynı origin değilse veya DOM henüz erişilebilir değilse sessizce geç —
    // sunucudan gelen prefill (taskType=8&jobId=N) yine de doğru değeri taşır,
    // yalnızca görsel/etkileşim kilidi uygulanmamış olur.
  }
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
    updateExisting: true,
    insertNew: true,
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

  const [filterModal, setFilterModal] = React.useState(false)
  const [scheduleModal, setScheduleModal] = React.useState(false)
  const [valuesField, setValuesField] = React.useState(null)   // izinli değerler modalı
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
              updateExisting: j.updateExisting !== false,
              insertNew: j.insertNew !== false,
              preProcedureName: j.preProcedureName || '',
              preProcedureParamsJson: j.preProcedureParamsJson || '',
              postProcedureName: j.postProcedureName || '',
              postProcedureParamsJson: j.postProcedureParamsJson || '',
              columns: j.columns || [],
            })
            setFilters(parseFilters(j.sourceFilterJson))
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

  // Kurallar YEREL state'te tutulur. Türetilmiş (job.sourceFilterJson'dan hesaplanan)
  // olsaydı yeni eklenen boş kural anında elenir ve ekranda hiç görünmezdi —
  // "Kural Ekle" çalışmıyor gibi olurdu. İşe yazarken eksik kurallar temizlenir.
  const [filters, setFilters] = React.useState(() => parseFilters(job.sourceFilterJson))

  function writeFilters(next) {
    setFilters(next)
    const clean = next.filter((r) => r.field && r.op)
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
  // Ekleme ve güncelleme birlikte kapalıysa aktarım hiçbir şey yazmaz — ilerlemeyi engelle.
  const step2Ok = job.targetEntity && job.columns.length > 0 && matchKeyMapped
    && (job.insertNew !== false || job.updateExisting !== false)

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

            <div className="dbi-card dbi-card--grow">
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
                <div className="dbi-table-wrap" style={{ overflowY: 'auto' }}>
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
            {/* Hedef + politika tek satırda. Azami satır UI'dan kaldırıldı
                (varsayılan 50.000, iş tanımında saklanmaya devam ediyor). */}
            <div className="dbi-card">
              <div style={{ display: 'flex', gap: 16, alignItems: 'flex-end', flexWrap: 'wrap' }}>
                <div className="dbi-field" style={{ margin: 0, minWidth: 260 }}>
                  <span className="dbi-label">Hedef Kayıt Türü <span className="dbi-required">*</span></span>
                  <select className="dbi-select" value={job.targetEntity}
                          onChange={(e) => setJob({ ...job, targetEntity: e.target.value, columns: [], matchKeyFields: [] })}>
                    <option value="">— Seçiniz —</option>
                    {entities.map((e) => <option key={e.entity} value={e.entity}>{e.label}</option>)}
                  </select>
                </div>
                <label className="dbi-switch" style={{ paddingBottom: 6 }}>
                  <input type="checkbox" checked={!!job.deactivateAbsent}
                         onChange={(e) => setJob({ ...job, deactivateAbsent: e.target.checked })} />
                  <span className="dbi-switch-track"><span className="dbi-switch-thumb" /></span>
                  <span>Kaynakta olmayanı pasife al</span>
                </label>
                {/* Yazma politikası: hangi satırın yazılacağını iş tanımı belirler. */}
                <label className="dbi-switch" style={{ paddingBottom: 6 }}>
                  <input type="checkbox" checked={job.insertNew !== false}
                         onChange={(e) => setJob({ ...job, insertNew: e.target.checked })} />
                  <span className="dbi-switch-track"><span className="dbi-switch-thumb" /></span>
                  <span>Yeni kayıt ekle</span>
                </label>
                <label className="dbi-switch" style={{ paddingBottom: 6 }}>
                  <input type="checkbox" checked={job.updateExisting !== false}
                         onChange={(e) => setJob({ ...job, updateExisting: e.target.checked })} />
                  <span className="dbi-switch-track"><span className="dbi-switch-thumb" /></span>
                  <span>Mevcudu güncelle</span>
                </label>

                {/* Kısıt kuralları modalda — kural yokken ekranda yer kaplamasın. */}
                <button type="button" className="dbi-btn" style={{ marginBottom: 2 }}
                        onClick={() => setFilterModal(true)} disabled={!sourceColumns.length}>
                  <Filter size={14} /> Kısıt Kuralları
                  {filters.length > 0 && (
                    <span className="dbi-key-pill" style={{ marginLeft: 4 }}>{filters.length}</span>
                  )}
                </button>
              </div>

              {job.targetEntity && entities.find((e) => e.entity === job.targetEntity)?.supportsUpsert === false && (
                <div className="dbi-alert dbi-alert--warn" style={{ marginTop: 10, marginBottom: 0 }}>
                  <AlertTriangle size={13} /> Bu kayıt türü güncelleme desteklemiyor — her çalıştırma yeni
                  kayıt açar, anahtar alan mükerrer oluşmasını engellemez. Zamanlanmış göreve bağlamayın.
                </div>
              )}
              {job.insertNew === false && job.updateExisting === false && (
                <div className="dbi-alert dbi-alert--err" style={{ marginTop: 10, marginBottom: 0 }}>
                  <AlertTriangle size={13} /> Ekleme ve güncelleme birlikte kapalı — aktarım hiçbir şey yazmaz.
                </div>
              )}
              {job.deactivateAbsent && (
                <div className="dbi-alert dbi-alert--warn" style={{ marginTop: 10, marginBottom: 0 }}>
                  <AlertTriangle size={13} /> Kaynak sorgusu bu kayıt türünün <strong>tamamını</strong> döndürmeli;
                  dar bir sorgu kapsam dışı kayıtları da pasife alır. Kayıt kaynağa dönerse otomatik aktifleşir.
                </div>
              )}
            </div>

            <div className="dbi-card dbi-card--grow">
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
                <div className="dbi-table-wrap" style={{ overflowY: 'auto' }}>
                  <table className="dbi-table">
                    <thead>
                      <tr><th>Hedef Alan</th><th>Kaynak Kolon</th><th style={{ textAlign: 'center' }}>Anahtar</th><th>Tür</th></tr>
                    </thead>
                    <tbody>
                      {targetFields.map((f) => (
                        <tr key={f.key}>
                          <td>
                            {f.label}
                            {f.isRequired && <span className="dbi-required"> *</span>}
                            {/* Kabul edilen değerler modalda — her satıra yayılmış liste
                                tabloyu şişiriyordu; ihtiyaç anında açılır. */}
                            {f.allowedValues && f.allowedValues.length > 0 && (
                              <button type="button" className="dbi-btn dbi-btn--xs dbi-btn--ghost"
                                      style={{ marginLeft: 6, padding: '2px 6px' }}
                                      title="Kabul edilen değerler"
                                      onClick={() => setValuesField(f)}>
                                <ListChecks size={12} /> {f.allowedValues.length}
                              </button>
                            )}
                          </td>
                          <td>
                            <select className="dbi-select" value={mappedByTarget[f.key] || ''}
                                    onChange={(e) => setMapping(f.key, e.target.value)}>
                              <option value="">— Eşlenmedi —</option>
                              {sourceColumns.map((c) => <option key={c.name} value={c.name}>{c.name}</option>)}
                            </select>
                          </td>
                          {/* Anahtar seçimi eşleme satırında: "anahtar seçtim ama eşlemedim"
                              durumu aynı satırda görünür. */}
                          <td style={{ textAlign: 'center' }}>
                            {f.canBeMatchKey ? (
                              <label className="dbi-switch" style={{ justifyContent: 'center' }}
                                     title="Mevcut kaydı bulmak için kullanılır; birden fazla seçerseniz hepsi eşleşmelidir">
                                <input type="checkbox" checked={matchKeys.includes(f.key)}
                                       onChange={() => toggleMatchKey(f.key)} />
                                <span className="dbi-switch-track"><span className="dbi-switch-thumb" /></span>
                              </label>
                            ) : <span className="dbi-hint">-</span>}
                            {matchKeys.includes(f.key) && !mappedByTarget[f.key] && (
                              <div className="dbi-hint" style={{ color: 'var(--dbi-danger)' }}>eşlenmeli</div>
                            )}
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

            {/* Sonuç yokken kalan alanı boş-durum doldurur; aksi halde tek butonun
                altında yüzlerce piksel boşluk kalıyor ve bozukmuş gibi duruyor. */}
            {!preview && (
              <div className="dbi-card dbi-card--grow"
                   style={{ display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                <div className="dbi-empty">
                  <Search size={22} style={{ opacity: 0.5 }} />
                  <div style={{ marginTop: 8 }}>Önizleme henüz çalıştırılmadı.</div>
                  <div className="dbi-hint">Kaynaktan örnek satırlar okunup doğrulanır; kayıt yazılmaz.</div>
                </div>
              </div>
            )}

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
                  <div className="dbi-table-wrap dbi-table-wrap--grow" style={{ overflowY: 'auto' }}>
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
              <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
                <button type="button" className="dbi-btn dbi-btn--primary" onClick={doRun} disabled={busy}>
                  <Play size={14} /> Aktarımı Başlat
                </button>
                {/* Zamanlama, mevcut görev ekranını gömülü açar — arayüz TEK yerde kalır,
                    7 zamanlama tipinin ifade üretimi buraya kopyalanmaz. */}
                <button type="button" className="dbi-btn"
                        onClick={async () => { const id = job.id || await save(); if (id) setScheduleModal(true) }}
                        disabled={busy}>
                  <CalendarClock size={14} /> Zamanla
                </button>
              </div>
              {!job.id && (
                <span className="dbi-hint">Zamanlama için iş önce kaydedilir.</span>
              )}
              <div className="dbi-hint" style={{ display: 'flex', alignItems: 'center', gap: 5, marginTop: 4 }}>
                <Lock size={11} /> Zamanlama açıldığında görev türü ve aktarım işi bu işe göre otomatik işaretlenir ve kilitlenir.
              </div>
            </div>

            {!runResult && (
              <div className="dbi-card dbi-card--grow"
                   style={{ display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                <div className="dbi-empty">
                  <Play size={22} style={{ opacity: 0.5 }} />
                  <div style={{ marginTop: 8 }}>Aktarım henüz çalıştırılmadı.</div>
                  <div className="dbi-hint">Ön prosedür → okuma → yazma → son prosedür sırasıyla işler.</div>
                </div>
              </div>
            )}

            {runResult && runResult.run && (
              <>
                <div className="dbi-stats">
                  <div className="dbi-stat"><div className="dbi-stat-label">Okunan</div><div className="dbi-stat-value">{runResult.run.rowsRead}</div></div>
                  <div className="dbi-stat dbi-stat--ok"><div className="dbi-stat-label">Eklenen</div><div className="dbi-stat-value">{runResult.run.rowsInserted}</div></div>
                  <div className="dbi-stat dbi-stat--ok"><div className="dbi-stat-label">Güncellenen</div><div className="dbi-stat-value">{runResult.run.rowsUpdated}</div></div>
                  <div className="dbi-stat dbi-stat--err"><div className="dbi-stat-label">Hatalı</div><div className="dbi-stat-value">{runResult.run.rowsFailed}</div></div>
                  {runResult.run.rowsSkipped > 0 && (
                    <div className="dbi-stat">
                      <div className="dbi-stat-label">Atlanan</div>
                      <div className="dbi-stat-value">{runResult.run.rowsSkipped}</div>
                    </div>
                  )}
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

      {/* ── Kısıt kuralları modalı ── */}
      {filterModal && (
        <div className="dbi-modal-backdrop"
             onMouseDown={(e) => { if (e.target === e.currentTarget) setFilterModal(false) }}>
          <div className="dbi-modal" role="dialog" aria-modal="true">
            <div className="dbi-modal-head" style={{ display: 'flex', alignItems: 'center' }}>
              <span><Filter size={14} /> Kısıt Kuralları</span>
              <div className="dbi-header-spacer" />
              <button type="button" className="dbi-btn dbi-btn--xs"
                      onClick={() => writeFilters([...filters, { field: '', op: 'eq', value: '' }])}>
                <Plus size={13} /> Kural Ekle
              </button>
            </div>
            <div className="dbi-modal-body">
              {filters.length === 0 && (
                <div className="dbi-hint">Kural yoksa kaynaktaki tüm satırlar aktarılır.</div>
              )}
              {filters.map((r, i) => (
                <div key={i} style={{ display: 'flex', gap: 8, alignItems: 'center', marginBottom: 8 }}>
                  <select className="dbi-select" style={{ maxWidth: 170 }} value={r.field}
                          onChange={(e) => { const n = [...filters]; n[i] = { ...r, field: e.target.value }; writeFilters(n) }}>
                    <option value="">— Kolon —</option>
                    {sourceColumns.map((c) => <option key={c.name} value={c.name}>{c.name}</option>)}
                  </select>
                  <select className="dbi-select" style={{ maxWidth: 160 }} value={r.op}
                          onChange={(e) => { const n = [...filters]; n[i] = { ...r, op: e.target.value }; writeFilters(n) }}>
                    {FILTER_OPS.map((o) => <option key={o.v} value={o.v}>{o.label}</option>)}
                  </select>
                  {!OPS_WITHOUT_VALUE.has(r.op) && (
                    <input className="dbi-input" style={{ maxWidth: 150 }} value={r.value || ''}
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
            <div className="dbi-modal-foot">
              <button type="button" className="dbi-btn dbi-btn--primary" onClick={() => setFilterModal(false)}>
                Tamam
              </button>
            </div>
          </div>
        </div>
      )}

      {/* ── Zamanlama modalı — mevcut Zamanlanmış Görev ekranı gömülü ── */}
      {scheduleModal && job.id > 0 && (
        <div className="dbi-modal-backdrop"
             onMouseDown={(e) => { if (e.target === e.currentTarget) setScheduleModal(false) }}>
          <div className="dbi-modal" style={{ maxWidth: 'min(1100px, calc(100vw - 48px))', height: 'min(760px, calc(100vh - 64px))' }}
               role="dialog" aria-modal="true">
            <div className="dbi-modal-head" style={{ display: 'flex', alignItems: 'center' }}>
              <span><CalendarClock size={14} /> Zamanlanmış Görev — {job.name}</span>
              <div className="dbi-header-spacer" />
              <button type="button" className="dbi-btn dbi-btn--xs dbi-btn--ghost" onClick={() => setScheduleModal(false)}>
                <X size={14} />
              </button>
            </div>
            <div className="dbi-modal-body" style={{ padding: 0 }}>
              <iframe
                title="Zamanlanmış Görev"
                src={`/Admin/ScheduledTaskEdit?taskType=8&jobId=${job.id}&workspace=1`}
                onLoad={(e) => lockScheduleIframeToJob(e.currentTarget, job.id)}
                style={{ width: '100%', height: '100%', border: 0, display: 'block' }} />
            </div>
            <div className="dbi-modal-foot">
              <span className="dbi-hint" style={{ marginRight: 'auto' }}>
                Görev burada kaydedilir; ayrıntı ve geçmiş için Zamanlanmış Görevler ekranı.
              </span>
              <button type="button" className="dbi-btn" onClick={() => setScheduleModal(false)}>Kapat</button>
            </div>
          </div>
        </div>
      )}

      {/* ── Kabul edilen değerler modalı ── */}
      {valuesField && (
        <div className="dbi-modal-backdrop"
             onMouseDown={(e) => { if (e.target === e.currentTarget) setValuesField(null) }}>
          <div className="dbi-modal dbi-modal--sm" role="dialog" aria-modal="true">
            <div className="dbi-modal-head" style={{ display: 'flex', alignItems: 'center' }}>
              <span>{valuesField.label} — Kabul Edilen Değerler</span>
              <div className="dbi-header-spacer" />
              <button type="button" className="dbi-btn dbi-btn--xs dbi-btn--ghost" onClick={() => setValuesField(null)}>
                <X size={14} />
              </button>
            </div>
            <div className="dbi-modal-body">
              {valuesField.dataType === 'bool' ? (
                <>
                  {/* Evet/Hayır listesi Excel şablonu içindir. DB aktarımında kaynaktaki
                      BIT kolon okuyucudan "1"/"0" olarak gelir ve doğrudan çalışır —
                      burada parser'ın gerçekte kabul ettiği küme gösterilir. */}
                  <div className="dbi-hint" style={{ marginBottom: 8 }}>
                    Kaynaktaki <strong>BIT</strong> kolon doğrudan çalışır — dönüştürmeye gerek yok.
                  </div>
                  <div className="dbi-label" style={{ marginBottom: 6 }}>EVET sayılan değerler</div>
                  <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6, marginBottom: 14 }}>
                    {['1', 'true', 'evet', 'e', 'x', 'var', 'yes', '✓'].map((v) => (
                      <span key={v} className="dbi-key-pill dbi-mono">{v}</span>
                    ))}
                  </div>
                  <div className="dbi-alert dbi-alert--warn" style={{ marginBottom: 0 }}>
                    <AlertTriangle size={13} /> Bunların dışındaki <strong>her değer</strong>
                    {' '}(0, false, hayır, boş, tanınmayan metin) <strong>HAYIR</strong> sayılır — hata verilmez.
                    Kaynak farklı bir gösterim kullanıyorsa view'da dönüştürün.
                  </div>
                </>
              ) : (
                <>
                  <div className="dbi-hint" style={{ marginBottom: 10 }}>
                    Kaynak view bu değerlerden birini üretmeli.
                  </div>
                  <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
                    {(valuesField.allowedValues || []).map((v) => (
                      <span key={v} className="dbi-key-pill dbi-mono">{v}</span>
                    ))}
                  </div>
                </>
              )}
            </div>
            <div className="dbi-modal-foot">
              <button type="button" className="dbi-btn dbi-btn--primary" onClick={() => setValuesField(null)}>
                Kapat
              </button>
            </div>
          </div>
        </div>
      )}

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
