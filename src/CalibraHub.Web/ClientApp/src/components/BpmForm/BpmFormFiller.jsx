import { useState, useEffect } from 'react'
import { Send, Loader2, CheckCircle2 } from 'lucide-react'

var BASE = '/BpmForm'

function getCsrf() {
  var el = document.querySelector('input[name="__RequestVerificationToken"]')
  return el ? el.value : ''
}
function postJson(url, body) {
  return fetch(BASE + url, {
    method: 'POST', credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': getCsrf() },
    body: JSON.stringify(body),
  }).then(r => r.json())
}

/* ── Group fields into rows by layoutRow / layoutCol ───────────────────── */
function buildFillerRows(fields) {
  var rowMap = {}
  fields.forEach(function (f) {
    var r = f.layoutRow || 0
    if (!rowMap[r]) rowMap[r] = []
    rowMap[r].push(f)
  })
  var keys = Object.keys(rowMap).map(Number).sort((a, b) => a - b)
  return keys.map(function (k) {
    return rowMap[k].slice().sort((a, b) => (a.layoutCol || 0) - (b.layoutCol || 0))
  })
}

/* ── Individual field renderer ──────────────────────────────────────────── */
function FieldInput({ field, value, onChange }) {
  var cls = 'bpmf-input'
  switch (field.fieldType) {
    case 'Textarea':
      return <textarea className={cls} rows={4} value={value || ''} placeholder={field.placeholder || ''}
        onChange={e => onChange(e.target.value)} />
    case 'Number':
      return <input className={cls} type="number" value={value || ''} placeholder={field.placeholder || ''}
        onChange={e => onChange(e.target.value)} />
    case 'Date':
      return <input className={cls} type="date" value={value || ''}
        onChange={e => onChange(e.target.value)} />
    case 'Dropdown': {
      var opts = []
      try { opts = JSON.parse(field.optionsJson || '[]') } catch (_) {}
      return (
        <select className={cls} value={value || ''} onChange={e => onChange(e.target.value)}>
          <option value="">Seçiniz…</option>
          {opts.map(o => <option key={o} value={o}>{o}</option>)}
        </select>
      )
    }
    case 'YesNo':
      return (
        <div className="bpmf-yesno">
          <button type="button"
            className={'bpmf-yesno__btn' + (value === true || value === 'true' ? ' bpmf-yesno__btn--active' : '')}
            onClick={() => onChange(true)}>Evet</button>
          <button type="button"
            className={'bpmf-yesno__btn' + (value === false || value === 'false' ? ' bpmf-yesno__btn--active' : '')}
            onClick={() => onChange(false)}>Hayır</button>
        </div>
      )
    case 'File':
      return <input className={cls} type="file"
        onChange={e => onChange(e.target.files?.[0]?.name || '')} />
    default:
      return <input className={cls} type="text" value={value || ''} placeholder={field.placeholder || ''}
        onChange={e => onChange(e.target.value)} />
  }
}

/* ── Main form filler ───────────────────────────────────────────────────── */
export default function BpmFormFiller({ formId }) {
  var [def, setDef]           = useState(null)
  var [fields, setFields]     = useState([])
  var [values, setValues]     = useState({})
  var [errors, setErrors]     = useState({})
  var [loading, setLoading]   = useState(true)
  var [submitting, setSubmitting] = useState(false)
  var [submitted, setSubmitted]   = useState(false)
  var [submitError, setSubmitError] = useState(null)

  useEffect(function () {
    fetch(BASE + '/GetDefinitionJson?id=' + formId, { credentials: 'same-origin' })
      .then(r => r.json())
      .then(function (res) {
        if (!res.success) return
        setDef(res.data.definition)
        var flds = res.data.fields || []
        setFields(flds)
        var init = {}
        flds.forEach(function (f) {
          init[f.key] = f.defaultValue || (f.fieldType === 'YesNo' ? '' : '')
        })
        setValues(init)
      })
      .catch(e => console.error('[BpmFormFiller] load', e))
      .finally(() => setLoading(false))
  }, [formId])

  function validate() {
    var errs = {}
    fields.forEach(function (f) {
      if (!f.isRequired) return
      var v = values[f.key]
      if (v === null || v === undefined || v === '') errs[f.key] = 'Bu alan zorunludur'
    })
    setErrors(errs)
    return Object.keys(errs).length === 0
  }

  async function handleSubmit(e) {
    e.preventDefault()
    if (!validate()) return
    setSubmitting(true)
    setSubmitError(null)
    try {
      var entries = Object.entries(values).map(function ([key, value]) {
        return { fieldKey: key, value: String(value ?? '') }
      })
      var res = await postJson('/SubmitFormJson', {
        formDefinitionId: formId,
        values: entries,
      })
      if (res.success) {
        setSubmitted(true)
      } else {
        setSubmitError(res.message || 'Gönderim başarısız')
      }
    } catch (ex) {
      setSubmitError('Bağlantı hatası: ' + ex.message)
    } finally {
      setSubmitting(false)
    }
  }

  if (loading) return (
    <div className="bpmf-state">
      <Loader2 size={24} className="nw-spin" />
      <span>Yükleniyor…</span>
    </div>
  )

  if (!def) return (
    <div className="bpmf-state bpmf-state--error">Form bulunamadı.</div>
  )

  if (submitted) return (
    <div className="bpmf-state bpmf-state--success">
      <CheckCircle2 size={44} />
      <div className="bpmf-success-title">Talebiniz Gönderildi</div>
      <div className="bpmf-success-sub">Form başarıyla gönderildi. Onay süreci başlatıldı.</div>
      <a href="/BpmForm/MySubmissions" className="bpmf-submit-btn" style={{ textDecoration: 'none', marginTop: 8 }}>
        Taleplerim →
      </a>
    </div>
  )

  return (
    <div className="bpmf-root">
      <form className="bpmf-card" onSubmit={handleSubmit} noValidate>
        <div className="bpmf-header">
          <h1 className="bpmf-title">{def.name}</h1>
          {def.description && <p className="bpmf-desc">{def.description}</p>}
        </div>

        <div className="bpmf-fields">
          {fields.length === 0 && (
            <div className="bpmf-no-fields">Bu formda henüz alan tanımlanmamış.</div>
          )}
          {buildFillerRows(fields).map(function (rowFields, rowIdx) {
            return (
              <div key={rowIdx} className="bpmf-row">
                {rowFields.map(function (f) {
                  var err = errors[f.key]
                  var span = f.layoutColSpan || 12
                  return (
                    <div key={f.key}
                      className={'bpmf-field' + (err ? ' bpmf-field--error' : '')}
                      style={{ flex: `0 0 ${(span / 12 * 100).toFixed(2)}%`,
                               maxWidth: `${(span / 12 * 100).toFixed(2)}%` }}>
                      <label className="bpmf-label">
                        {f.label}
                        {f.isRequired && <span className="bpmf-req"> *</span>}
                      </label>
                      <FieldInput
                        field={f}
                        value={values[f.key]}
                        onChange={function (v) {
                          setValues(vs => ({ ...vs, [f.key]: v }))
                          if (errors[f.key]) setErrors(es => { var n = { ...es }; delete n[f.key]; return n })
                        }}
                      />
                      {err && <div className="bpmf-err-msg">{err}</div>}
                    </div>
                  )
                })}
              </div>
            )
          })}
        </div>

        {submitError && <div className="bpmf-submit-error">{submitError}</div>}

        <div className="bpmf-footer">
          <button type="submit" className="bpmf-submit-btn" disabled={submitting}>
            {submitting
              ? <><Loader2 size={15} className="nw-spin" /> Gönderiliyor…</>
              : <><Send size={15} /> Gönder</>}
          </button>
        </div>
      </form>
    </div>
  )
}
