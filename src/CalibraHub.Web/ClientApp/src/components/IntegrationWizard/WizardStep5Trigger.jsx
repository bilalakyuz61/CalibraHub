/**
 * Step 5 — Tetikleyici tipi + ad/açıklama + kaydet.
 *
 * Multi-trigger destekli. 4 tip:
 *   0 = Manual  (form sayfasında dishli menusunde "ERP'ye Aktar" — aktif)
 *   1 = Cron    (periyodik — aktif)
 *   2 = OnSave  (kayıt edilince arka planda fire — aktif, sales document'lar icin)
 *   3 = Event   (özel olay — aktif, POST /api/integration-events/fire ile fire edilir)
 *
 * Her tip için Config JSON'ı tutulur (buton etiketi, cron expression, vb.).
 */
import React, { useCallback, useState, useEffect, useMemo, useRef } from 'react'
import { MousePointer, Clock, Save as SaveIcon, Zap, Database, Plus, X } from 'lucide-react'

const TRIGGER_DEFS = [
  { type: 0, label: 'Manuel Buton',       icon: MousePointer,
    desc: 'Form ekranında "ERP\'ye Aktar" gibi buton görünür.',
    available: true },
  { type: 1, label: 'Periyodik (Cron)',   icon: Clock,
    desc: 'Belirli aralıklarla otomatik çalıştırır.',
    available: true },
  { type: 2, label: 'Otomatik (Save Sonrası)', icon: SaveIcon,
    desc: 'Form kaydedilince arka planda fire eder.',
    available: true },
  { type: 3, label: 'Özel Event',         icon: Zap,
    desc: 'POST /api/integration-events/fire ile event geldiğinde tetiklenir.',
    available: true },
]

// Post-procedure parametre source tipleri
const PROC_PARAM_SOURCES = [
  { value: 'FormField', label: 'Form Alanı', desc: 'Header/lines verisinden' },
  { value: 'Constant',  label: 'Sabit',     desc: 'Literal değer' },
  { value: 'RunMeta',   label: 'Run Meta',  desc: 'RunId/IntegrationId/StartedAt/SourceRecordId/TriggeredBy' },
  { value: 'Response',  label: 'Response',  desc: 'HTTP response JSON path' },
  { value: 'HttpStatus',label: 'HTTP Status', desc: 'API yanıt kodu (200, vb.)' },
]
const RUN_META_KEYS = ['RunId', 'IntegrationId', 'StartedAt', 'SourceRecordId', 'TriggeredBy']

/** Post-procedure parametreleri JSON parse — bozuksa boş array. */
function parseProcParams(json) {
  if (!json) return []
  try { const arr = JSON.parse(json); return Array.isArray(arr) ? arr : [] } catch { return [] }
}
function stringifyProcParams(arr) {
  const clean = (arr || []).filter(p => p && p.name && (p.name + '').trim().length > 0)
  return clean.length === 0 ? null : JSON.stringify(clean)
}

export default function WizardStep5Trigger({ state, update, apiBase }) {
  // Form alanlari (Post-procedure parametre kaynagi icin)
  const [formFields, setFormFields] = useState([])
  useEffect(() => {
    if (!state.sourceFormCode || !apiBase) return
    fetch(`${apiBase}/forms/${encodeURIComponent(state.sourceFormCode)}/fields`,
          { credentials: 'same-origin' })
      .then(r => r.json())
      .then(d => { if (d.success) setFormFields(d.fields || []) })
      .catch(() => {})
  }, [apiBase, state.sourceFormCode])

  // triggerType seçili mi?
  const getTrigger = (type) => state.triggers.find(t => t.triggerType === type)
  const isSelected = (type) => !!getTrigger(type)

  const toggleTrigger = useCallback((type) => {
    const existing = getTrigger(type)
    if (existing) {
      // Kaldır
      update({ triggers: state.triggers.filter(t => t.triggerType !== type) })
    } else {
      // Ekle (default config'lerle)
      const def = {
        triggerType: type,
        isActive: true,
        config: type === 0 ? JSON.stringify({ buttonLabel: 'ERP\'ye Aktar' })
              : type === 1 ? JSON.stringify({ cronExpression: '0 */15 * * * *' })
              : type === 2 ? JSON.stringify({ onlyIfNotSent: true })       // duplicate guard
              : type === 3 ? JSON.stringify({ eventName: '' })
              : null,
      }
      update({ triggers: [...state.triggers, def] })
    }
  }, [state.triggers, update])

  const updateTriggerConfig = useCallback((type, configPatch) => {
    const next = state.triggers.map(t => {
      if (t.triggerType !== type) return t
      let parsed = {}
      try { parsed = JSON.parse(t.config || '{}') } catch { /* */ }
      const merged = { ...parsed, ...configPatch }
      return { ...t, config: JSON.stringify(merged) }
    })
    update({ triggers: next })
  }, [state.triggers, update])

  const getConfigValue = (type, key) => {
    const t = getTrigger(type)
    if (!t?.config) return ''
    try { return JSON.parse(t.config)[key] || '' } catch { return '' }
  }

  return (
    <>
      <h2 className="iw-step-title">Yayına Al</h2>
      <p className="iw-step-help">
        Son adım: entegrasyona ad ver, ne zaman çalışacağını seç. Aynı entegrasyon hem manuel
        butonla hem otomatik (Cron / Save / Event) çalışabilir.
      </p>

      {/* Kompakt ozet — kullanici neyi kaydedeceginin tam anlik resmini gorur */}
      <SaveSummary state={state} formFields={formFields} />


      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 24, maxWidth: 1200 }}>
        {/* Sol: Ad/Açıklama/Aktif/ErrorBehavior */}
        <div>
          <div className="iw-field" style={{ marginBottom: 12 }}>
            <label>Ad *</label>
            <input value={state.name} onChange={e => update({ name: e.target.value })}
                   placeholder="Örn: Satış Teklifi → Netsis" maxLength={200} />
          </div>
          <div className="iw-field" style={{ marginBottom: 12 }}>
            <label>Açıklama</label>
            <textarea value={state.description || ''}
                      onChange={e => update({ description: e.target.value })}
                      placeholder="Bu entegrasyonun ne yaptığını kısaca açıkla"
                      rows={3} maxLength={1000} />
          </div>
          <div className="iw-field" style={{ marginBottom: 12, maxWidth: 300 }}>
            <label>Hata Davranışı</label>
            <select value={state.errorBehavior}
                    onChange={e => update({ errorBehavior: parseInt(e.target.value) })}>
              <option value={0}>Skip — hatayı logla, devam et</option>
              <option value={1}>Retry — N kez tekrar dene</option>
              <option value={2}>Manuel — inceleme kuyruğuna at</option>
            </select>
          </div>
          {state.errorBehavior === 1 && (
            <div className="iw-field" style={{ marginBottom: 12, maxWidth: 200 }}>
              <label>Retry Sayısı</label>
              <input type="number" min="1" max="10" value={state.retryCount}
                     onChange={e => update({ retryCount: parseInt(e.target.value) || 0 })} />
            </div>
          )}
          <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginTop: 16 }}>
            <button className={'iw-switch' + (state.isActive ? ' is-on' : '')}
                    onClick={() => update({ isActive: !state.isActive })}>
              <span className="iw-switch__thumb" />
            </button>
            <span style={{ fontSize: 13, color: 'var(--iw-text)' }}>
              Entegrasyon aktif
            </span>
          </div>

          {/* 2026-05-22 Cascade target toggle — bu integration başka entegrasyonlar
              tarafından cascade hedefi olarak seçilebilir mi? Default açık (true).
              Kapatınca Wizard Step 2 "Bağımlılık" dropdown'larında bu integration listelenmez. */}
          <div style={{ display: 'flex', alignItems: 'flex-start', gap: 10, marginTop: 12 }}>
            <button className={'iw-switch' + (state.allowAsCascadeTarget !== false ? ' is-on' : '')}
                    onClick={() => update({ allowAsCascadeTarget: state.allowAsCascadeTarget === false })}>
              <span className="iw-switch__thumb" />
            </button>
            <div style={{ flex: 1 }}>
              <div style={{ fontSize: 13, color: 'var(--iw-text)' }}>
                Cascade hedefi olarak seçilebilir
              </div>
              <div style={{ fontSize: 11, color: 'var(--iw-muted)', marginTop: 2, lineHeight: 1.4 }}>
                Aktifse: başka bir entegrasyon mapping'inde "Bağımlılık" olarak seçilebilir
                (örn. Sipariş entegrasyonu ItemId için bu Stok entegrasyonunu cascade eder).
                Manuel/Cron/OnSave tetikleyicileriniz bu seçimden etkilenmez.
              </div>
            </div>
          </div>
        </div>

        {/* Sağ: Trigger cards */}
        <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
          {TRIGGER_DEFS.map(def => {
            const Icon = def.icon
            const selected = isSelected(def.type)
            const disabled = !def.available
            return (
              <div key={def.type}
                   className={'iw-trigger-card ' + (selected ? 'is-checked' : '') + (disabled ? ' is-disabled' : '')}>
                <div style={{ paddingTop: 2 }}>
                  <Icon size={20} style={{ color: selected ? 'var(--iw-indigo-color)' : 'var(--iw-muted)' }} />
                </div>
                <div className="iw-trigger-card-body">
                  <div className="iw-trigger-card-title">{def.label}</div>
                  <div className="iw-trigger-card-desc">{def.desc}</div>
                  {def.v2note && (
                    <div style={{ fontSize: 11, color: 'var(--iw-amber-color)', marginTop: 4 }}>
                      ⚙ {def.v2note}
                    </div>
                  )}
                  {/* Trigger config (sadece seçili ve aktif olduğunda) */}
                  {selected && def.type === 0 && (
                    <div className="iw-trigger-config">
                      <div className="iw-trigger-config-row">
                        <label>Buton Etiketi</label>
                        <input value={getConfigValue(0, 'buttonLabel')}
                               onChange={e => updateTriggerConfig(0, { buttonLabel: e.target.value })}
                               placeholder="ERP'ye Aktar" />
                      </div>
                    </div>
                  )}
                  {selected && def.type === 1 && (
                    <div className="iw-trigger-config">
                      <div className="iw-trigger-config-row">
                        <label>Cron İfadesi</label>
                        <input value={getConfigValue(1, 'cronExpression')}
                               onChange={e => updateTriggerConfig(1, { cronExpression: e.target.value })}
                               placeholder="0 */15 * * * *" />
                      </div>
                      <div style={{ fontSize: 11, color: 'var(--iw-muted)' }}>
                        Quartz cron format. Örn: <code>0 0 9 * * *</code> = her gün saat 9.
                      </div>
                    </div>
                  )}
                  {selected && def.type === 2 && (() => {
                    // Default: onlyIfNotSent = true (duplicate guard)
                    // Direct config parse — getConfigValue boolean false icin '' donerek bug yapiyor
                    let onlyIfNotSent = true
                    const _t = getTrigger(2)
                    if (_t?.config) {
                      try {
                        const _cfg = JSON.parse(_t.config)
                        if (typeof _cfg.onlyIfNotSent === 'boolean') onlyIfNotSent = _cfg.onlyIfNotSent
                      } catch {}
                    }
                    return (
                      <div className="iw-trigger-config">
                        <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 8 }}>
                          <button className={'iw-switch' + (onlyIfNotSent ? ' is-on' : '')}
                                  onClick={() => updateTriggerConfig(2, { onlyIfNotSent: !onlyIfNotSent })}
                                  style={{ width: 32, height: 18 }}
                                  title="Belge daha önce gönderilmişse tekrar gönderme">
                            <span className="iw-switch__thumb" />
                          </button>
                          <span style={{ fontSize: 12, color: 'var(--iw-text)' }}>
                            Sadece bir kez gönder <strong style={{ color: 'var(--iw-emerald-color)' }}>(önerilen)</strong>
                          </span>
                        </div>
                        <div style={{ fontSize: 11, color: 'var(--iw-muted)', lineHeight: 1.5 }}>
                          {onlyIfNotSent
                            ? <>Belge ilk kez kaydedildiğinde tetiklenir. <code>document.IntegrationSentAt</code> dolu kayıtlar SKIP edilir — düzenlemeler tekrar göndermez. Yeniden göndermek için belge ekranındaki <em>"Yeniden Gönder"</em> butonu kullanılır.</>
                            : <>⚠️ Her save'de tetiklenir. Aynı belge defalarca ERP'ye gidebilir — dikkatli kullan.</>}
                        </div>
                      </div>
                    )
                  })()}
                  {selected && def.type === 3 && (
                    <div className="iw-trigger-config">
                      <div className="iw-trigger-config-row">
                        <label>Event Adı</label>
                        <input value={getConfigValue(3, 'eventName')}
                               onChange={e => updateTriggerConfig(3, { eventName: e.target.value })}
                               placeholder="DocumentApproved" />
                      </div>
                      <div style={{ fontSize: 11, color: 'var(--iw-muted)' }}>
                        Tetiklemek için: <code>POST /api/integration-events/fire</code><br />
                        Body: <code>{`{ "eventName": "${getConfigValue(3, 'eventName') || 'DocumentApproved'}", "recordId": "..." }`}</code>
                      </div>
                    </div>
                  )}
                </div>
                <button className={'iw-switch' + (selected ? ' is-on' : '')}
                        disabled={disabled}
                        onClick={() => !disabled && toggleTrigger(def.type)}>
                  <span className="iw-switch__thumb" />
                </button>
              </div>
            )
          })}
        </div>
      </div>

      {/* ── Öncesi + Sonrası prosedürler (yan yana) ─────────────────────────── */}
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 16, maxWidth: 1200, marginTop: 24 }}>
        <ProcedureCard phase="pre"  state={state} update={update} formFields={formFields} />
        <ProcedureCard phase="post" state={state} update={update} formFields={formFields} />
      </div>
    </>
  )
}

/**
 * ProcedureCard — entegrasyon ÖNCESİ veya SONRASI çalıştırılan SQL SP kartı.
 * phase = 'pre'  → integration.PreProcedureName / PreProcedureParamsJson
 *         'post' → integration.PostProcedureName / PostProcedureParamsJson
 *
 * Pre özellikleri:
 *   - HTTP'den ÖNCE çalışır
 *   - Hata = entegrasyon iptal (HTTP hiç çağrılmaz, run Failed)
 *   - Response/HttpStatus parametre kaynağı YOK (henüz yanıt yok)
 * Post özellikleri:
 *   - HTTP başarılı ise çalışır
 *   - Hata = run sonucu Success kalır ama ErrorMessage'a not eklenir
 *   - Tüm parametre kaynakları geçerli
 */
function ProcedureCard({ phase, state, update, formFields }) {
  const isPre = phase === 'pre'
  const procNameKey   = isPre ? 'preProcedureName'        : 'postProcedureName'
  const procParamsKey = isPre ? 'preProcedureParamsJson'  : 'postProcedureParamsJson'
  const procName      = state[procNameKey]
  const procParams    = state[procParamsKey]

  const title       = isPre ? 'Öncesi Prosedür (Opsiyonel)'    : 'Sonrası Prosedür (Opsiyonel)'
  const subtitle    = isPre
    ? 'API çağrısından ÖNCE çalışır. Hata olursa entegrasyon iptal edilir (örn. lock atma, ön-validasyon).'
    : 'API çağrısı BAŞARILI olduktan sonra çalışır (örn. kaydı "Aktarıldı" işaretle, log tut).'
  const accentColor = isPre ? 'var(--iw-amber-color)'  : 'var(--iw-emerald-color)'
  const accentBg    = isPre ? 'var(--iw-amber-bg)'     : 'var(--iw-emerald-bg)'
  // Pre'de Response + HttpStatus YOK
  const allowedSources = isPre
    ? PROC_PARAM_SOURCES.filter(s => s.value !== 'Response' && s.value !== 'HttpStatus')
    : PROC_PARAM_SOURCES

  const enabled = !!(procName && procName.trim())
  const params = useMemo(() => parseProcParams(procParams), [procParams])
  const inputRef = useRef(null)
  const cardRef  = useRef(null)

  useEffect(() => {
    if (enabled && cardRef.current) {
      cardRef.current.scrollIntoView({ behavior: 'smooth', block: 'nearest' })
      setTimeout(() => inputRef.current?.focus(), 200)
    }
  }, [enabled])

  const setEnabled = (on) => {
    if (on) {
      update({ [procNameKey]: procName || 'dbo.', [procParamsKey]: procParams })
    } else {
      update({ [procNameKey]: null, [procParamsKey]: null })
    }
  }
  const setProcName = (v) => update({ [procNameKey]: v })
  const updateParams = (next) => update({ [procParamsKey]: stringifyProcParams(next) })
  const addParam = () => updateParams([...params, { name: '@Param', sourceType: 'Constant', sourceValue: '' }])
  const patchParam = (i, patch) => updateParams(params.map((p, j) => i === j ? { ...p, ...patch } : p))
  const removeParam = (i) => updateParams(params.filter((_, j) => i !== j))

  return (
    <div ref={cardRef} style={{
      maxWidth: 1200,
      border: '1px solid ' + (enabled ? accentColor : 'var(--iw-border)'),
      borderRadius: 10, background: 'var(--iw-surface)', overflow: 'visible',
    }}>
      <div style={{
        display: 'flex', alignItems: 'center', gap: 10,
        padding: '10px 14px', background: enabled ? accentBg : 'var(--iw-bg)',
      }}>
        <Database size={18} style={{ color: enabled ? accentColor : 'var(--iw-muted)' }} />
        <div style={{ flex: 1 }}>
          <div style={{ fontSize: 13, fontWeight: 600, color: 'var(--iw-text)',
                        display: 'flex', alignItems: 'center', gap: 6 }}>
            {title}
            <span style={{
              fontSize: 9, padding: '1px 5px', borderRadius: 3, fontWeight: 700,
              background: accentColor, color: '#fff',
            }}>{isPre ? 'PRE' : 'POST'}</span>
          </div>
          <div style={{ fontSize: 11, color: 'var(--iw-muted)', marginTop: 2 }}>
            {subtitle}
          </div>
        </div>
        <button className={'iw-switch' + (enabled ? ' is-on' : '')}
                onClick={() => setEnabled(!enabled)}>
          <span className="iw-switch__thumb" />
        </button>
      </div>

      {enabled && (
        <div style={{ padding: 14 }}>
          {/* Procedure adı — inline-styled input (iw-field bagimliligi yok) */}
          <div style={{ marginBottom: 14 }}>
            <label style={{
              display: 'block', fontSize: 12, fontWeight: 600, marginBottom: 4,
              color: 'var(--iw-text)',
            }}>Prosedür Adı</label>
            <input ref={inputRef}
                   value={procName || ''}
                   onChange={e => setProcName(e.target.value)}
                   placeholder={isPre ? 'dbo.LockDocument' : 'dbo.MarkAsExported'}
                   autoComplete="off"
                   style={{
                     width: '100%', boxSizing: 'border-box', display: 'block',
                     padding: '8px 10px', fontSize: 13, lineHeight: 1.4, height: 'auto',
                     border: '1px solid #475569', borderRadius: 6,
                     background: 'rgba(15,23,42,0.6)', color: '#e2e8f0', outline: 'none',
                     fontFamily: 'ui-monospace, Menlo, Consolas, monospace',
                   }} />
            <div style={{ fontSize: 11, color: 'var(--iw-muted)', marginTop: 4 }}>
              Format: <code>schema.proc</code> veya <code>[schema].[proc]</code>
            </div>
          </div>

          {/* Parametreler */}
          <div style={{ fontSize: 11, fontWeight: 600, color: 'var(--iw-muted)', marginBottom: 6,
                        textTransform: 'uppercase', letterSpacing: 0.4 }}>
            Parametreler
          </div>
          {params.length === 0 && (
            <div style={{
              fontSize: 11, color: 'var(--iw-muted)', textAlign: 'center', padding: 12,
              border: '1px dashed var(--iw-border)', borderRadius: 6, marginBottom: 8,
            }}>
              Henüz parametre yok. Aşağıdaki butonla ekleyin (parametresiz SP de çalıştırılabilir).
            </div>
          )}
          {params.map((p, i) => (
            <div key={i} style={{
              display: 'grid',
              gridTemplateColumns: '160px 110px 1fr 28px',
              gap: 6, marginBottom: 6, alignItems: 'center',
            }}>
              {/* Param adı (örn. @DocumentId) */}
              <input value={p.name || ''}
                     onChange={e => patchParam(i, { name: e.target.value })}
                     placeholder="@ParamName"
                     style={{ padding: '6px 8px', fontSize: 12, border: '1px solid var(--iw-border)',
                       borderRadius: 4, background: 'var(--iw-bg)', color: 'var(--iw-text)',
                       fontFamily: 'ui-monospace, Menlo, Consolas, monospace' }} />
              {/* Source type */}
              <select value={p.sourceType || 'Constant'}
                      onChange={e => patchParam(i, { sourceType: e.target.value, sourceValue: '' })}
                      style={{ padding: '6px 8px', fontSize: 11, border: '1px solid var(--iw-border)',
                        borderRadius: 4, background: 'var(--iw-bg)', color: 'var(--iw-text)' }}>
                {allowedSources.map(s => (
                  <option key={s.value} value={s.value} title={s.desc}>{s.label}</option>
                ))}
              </select>
              {/* Source value (mod'a göre dropdown veya input) */}
              {p.sourceType === 'FormField' ? (
                <select value={p.sourceValue || ''}
                        onChange={e => patchParam(i, { sourceValue: e.target.value })}
                        style={{ padding: '6px 8px', fontSize: 12, border: '1px solid var(--iw-border)',
                          borderRadius: 4, background: 'var(--iw-bg)', color: 'var(--iw-text)' }}>
                  <option value="">— Form alanı seç —</option>
                  {formFields.map(f => (
                    <option key={`${f.section || 'Header'}.${f.code}`} value={f.code}>
                      [{(f.section || 'Header')[0]}] {f.label} ({f.code})
                    </option>
                  ))}
                </select>
              ) : p.sourceType === 'RunMeta' ? (
                <select value={p.sourceValue || ''}
                        onChange={e => patchParam(i, { sourceValue: e.target.value })}
                        style={{ padding: '6px 8px', fontSize: 12, border: '1px solid var(--iw-border)',
                          borderRadius: 4, background: 'var(--iw-bg)', color: 'var(--iw-text)' }}>
                  <option value="">— Run meta key —</option>
                  {RUN_META_KEYS.map(k => <option key={k} value={k}>{k}</option>)}
                </select>
              ) : p.sourceType === 'HttpStatus' ? (
                <input value="(otomatik: HTTP yanıt kodu)" disabled
                       style={{ padding: '6px 8px', fontSize: 12, border: '1px solid var(--iw-border)',
                         borderRadius: 4, background: 'var(--iw-bg)', color: 'var(--iw-muted)',
                         fontStyle: 'italic' }} />
              ) : (
                <input value={p.sourceValue || ''}
                       onChange={e => patchParam(i, { sourceValue: e.target.value })}
                       placeholder={p.sourceType === 'Response' ? 'JSON path (örn: data.id)' : 'Sabit değer'}
                       style={{ padding: '6px 8px', fontSize: 12, border: '1px solid var(--iw-border)',
                         borderRadius: 4, background: 'var(--iw-bg)', color: 'var(--iw-text)' }} />
              )}
              {/* Sil */}
              <button type="button" onClick={() => removeParam(i)}
                      style={{ background: 'transparent', border: 'none', cursor: 'pointer',
                        padding: 4, color: 'var(--iw-muted)', borderRadius: 3 }}
                      onMouseEnter={e => e.currentTarget.style.color = 'var(--iw-rose-color)'}
                      onMouseLeave={e => e.currentTarget.style.color = 'var(--iw-muted)'}
                      title="Parametreyi sil">
                <X size={14} />
              </button>
            </div>
          ))}
          <button type="button" onClick={addParam}
                  style={{
                    marginTop: 4, padding: '6px 12px', fontSize: 12, cursor: 'pointer',
                    border: '1px dashed var(--iw-border)', borderRadius: 6,
                    background: 'transparent', color: 'var(--iw-indigo-color)',
                    display: 'inline-flex', alignItems: 'center', gap: 6,
                  }}>
            <Plus size={12} /> Parametre Ekle
          </button>

          <div style={{
            marginTop: 12, padding: 8, fontSize: 11, color: 'var(--iw-muted)',
            background: 'var(--iw-bg)', borderRadius: 6, border: '1px solid var(--iw-border)',
            fontFamily: 'ui-monospace, Menlo, Consolas, monospace',
          }}>
            <strong style={{ color: accentColor }}>Engine:</strong>{' '}
            EXEC {procName || (isPre ? 'dbo.YourPreProc' : 'dbo.YourPostProc')}
            {params.length > 0 && (
              <> {params.map(p => `${p.name || '@p'}=<${p.sourceType}:${p.sourceValue || ''}>`).join(', ')}</>
            )}
          </div>
        </div>
      )}
    </div>
  )
}

/**
 * SaveSummary — Step 5'in en üstünde duran kompakt özet kartı.
 * Kullanıcı "Kaydet" demeden önce ne yarattığının tam resmini görür.
 *
 * Renkli ipucu: yeşil = hazır, sarı = eksik (Ad veya tetikleyici yok).
 */
function SaveSummary({ state, formFields }) {
  const triggerLabels = {
    0: 'Manuel Buton', 1: 'Cron', 2: 'Save Sonrası', 3: 'Özel Event',
  }
  const activeTriggers = (state.triggers || []).filter(t => t.isActive)
  // "Sadece Prosedür" modu — kullanici hedef endpoint secmeden sadece pre/post
  // prosedurleri kullaniyor olabilir. O zaman targetEndpointId ve mapping zorunlu degil.
  const isProcedureOnly = !state.targetEndpointId
                       && (!!state.preProcedureName?.trim() || !!state.postProcedureName?.trim())

  // Hazirlik kontrolu — wizard'in actual save logic'iyle ayni:
  //   - name zorunlu (her durumda)
  //   - sourceFormCode zorunlu (her durumda)
  //   - procedure-only ise: pre veya post prosedur tanimli olmali (zaten check edildi)
  //   - normal modda: endpoint + en az 1 mapping
  //   - tetikleyici: en az 1 aktif (her iki modda da)
  const ready =
    !!state.name?.trim() &&
    !!state.sourceFormCode &&
    activeTriggers.length > 0 &&
    (isProcedureOnly
       ? true                                                  // pre/post zaten dolu (yukarida check edildi)
       : (!!state.targetEndpointId && (state.mappings?.length || 0) > 0))

  const issues = []
  if (!state.name?.trim()) issues.push('Ad zorunlu')
  if (!state.sourceFormCode) issues.push('Kaynak form yok (Step 1)')
  if (activeTriggers.length === 0) issues.push('En az bir tetikleyici seç')
  if (!isProcedureOnly) {
    if (!state.targetEndpointId) issues.push('Hedef endpoint yok (Step 2)')
    if ((state.mappings?.length || 0) === 0) issues.push('Eşleme yok (Step 3)')
  }

  const accent = ready ? 'var(--iw-emerald-color)' : 'var(--iw-amber-color)'
  const accentBg = ready ? 'var(--iw-emerald-bg)' : 'var(--iw-amber-bg)'

  return (
    <div style={{
      maxWidth: 1200, marginBottom: 18,
      border: `1px solid ${accent}`, borderRadius: 10,
      background: accentBg,
      padding: '10px 14px',
      display: 'grid',
      gridTemplateColumns: 'auto 1fr auto',
      gap: 14, alignItems: 'center',
    }}>
      {/* Sol: durum ikonu */}
      <div style={{
        width: 36, height: 36, borderRadius: '50%',
        background: accent, color: '#fff',
        display: 'flex', alignItems: 'center', justifyContent: 'center',
        fontWeight: 700, fontSize: 16,
      }}>
        {ready ? '✓' : '!'}
      </div>

      {/* Orta: gereksinim ozeti */}
      <div>
        <div style={{ fontSize: 13, fontWeight: 600, color: 'var(--iw-text)', marginBottom: 3 }}>
          {ready ? 'Kaydetmeye hazır' : 'Birkaç eksik var'}
        </div>
        <div style={{ fontSize: 11, color: 'var(--iw-muted)', display: 'flex', flexWrap: 'wrap', gap: 12 }}>
          <span><strong>Form:</strong> {state.sourceFormCode || '—'}</span>
          <span>·</span>
          <span><strong>Endpoint:</strong> #{state.targetEndpointId || '—'}</span>
          <span>·</span>
          <span><strong>Eşleme:</strong> {state.mappings?.length || 0}</span>
          <span>·</span>
          <span><strong>Tetik:</strong> {activeTriggers.length === 0 ? '—'
            : activeTriggers.map(t => triggerLabels[t.triggerType] || '?').join(', ')}</span>
          {state.preProcedureName && (
            <>
              <span>·</span>
              <span style={{ color: 'var(--iw-amber-color)' }} title="Öncesi prosedür">
                ↪ <code>{state.preProcedureName}</code>
              </span>
            </>
          )}
          {state.postProcedureName && (
            <>
              <span>·</span>
              <span style={{ color: 'var(--iw-emerald-color)' }} title="Sonrası prosedür">
                ↩ <code>{state.postProcedureName}</code>
              </span>
            </>
          )}
        </div>
        {!ready && issues.length > 0 && (
          <div style={{
            marginTop: 6, fontSize: 11, color: 'var(--iw-amber-color)',
            display: 'flex', flexWrap: 'wrap', gap: 8,
          }}>
            {issues.map((s, i) => (
              <span key={i} style={{
                padding: '2px 6px', borderRadius: 3,
                background: 'var(--iw-amber-bg)', border: '1px solid var(--iw-amber-color)',
              }}>⚠ {s}</span>
            ))}
          </div>
        )}
      </div>

      {/* Sag: status badge */}
      <div style={{
        padding: '4px 10px', borderRadius: 12, fontSize: 11, fontWeight: 700,
        background: accent, color: '#fff', whiteSpace: 'nowrap',
      }}>
        {ready ? 'HAZIR' : `${issues.length} EKSİK`}
      </div>
    </div>
  )
}
