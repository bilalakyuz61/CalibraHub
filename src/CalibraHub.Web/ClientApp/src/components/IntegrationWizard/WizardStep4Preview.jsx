/**
 * Step 4 — Önizleme ve Test.
 *
 * State'teki mapping kurallarını sample record üzerine uygular, ortaya çıkan
 * JSON output'u gösterir. "Gerçek test gönder" butonu ile httpExecutor'u tetikler
 * (Step 4'te bile DB'ye Integration kaydetmeden — TestAsync endpoint'i geçici aggregate yaratır).
 */
import React, { useState, useCallback } from 'react'
import { Play, Loader2, CheckCircle2, AlertCircle } from 'lucide-react'

function getCsrf() {
  const el = document.querySelector('input[name="__RequestVerificationToken"]')
  return el ? el.value : ''
}

export default function WizardStep4Preview({ apiBase, state }) {
  const [running, setRunning]   = useState(false)
  const [result, setResult]     = useState(null)
  const [sendReal, setSendReal] = useState(false)

  const runTest = useCallback(async () => {
    setRunning(true)
    setResult(null)
    try {
      const r = await fetch(`${apiBase}/test`, {
        method: 'POST', credentials: 'same-origin',
        headers: {
          'Content-Type': 'application/json',
          RequestVerificationToken: getCsrf(),
        },
        body: JSON.stringify({
          integration: {
            id: state.id,
            name: state.name || 'preview',
            description: state.description,
            sourceFormCode: state.sourceFormCode,
            targetEndpointId: state.targetEndpointId,
            errorBehavior: state.errorBehavior,
            retryCount: state.retryCount,
            isActive: state.isActive,
            mappings: state.mappings,
            triggers: state.triggers,
          },
          sampleRecordId: null,
          sendForReal: sendReal,
        }),
      })
      const d = await r.json()
      setResult(d.result || { success: false, errorMessage: d.error || 'Bilinmeyen hata' })
    } catch (e) {
      setResult({ success: false, errorMessage: 'Sunucu hatası: ' + e.message })
    } finally {
      setRunning(false)
    }
  }, [apiBase, state, sendReal])

  return (
    <>
      <h2 className="iw-step-title">Önizleme ve Test</h2>
      <p className="iw-step-help">
        Mapping kurallarını sistemdeki en son <code>{state.sourceFormCode}</code> kaydı üzerine uygula.
        Sonucu görmek için "Test İste"ye tıkla. <strong>Gerçek gönderim</strong> kutusunu işaretlersen
        endpoint'e gerçek HTTP isteği atılır (yine de IntegrationRun kayıt edilmez — bu sadece preview).
      </p>

      <div style={{ display: 'flex', gap: 8, alignItems: 'center', marginBottom: 16, maxWidth: 900 }}>
        <button className="iw-btn-primary" onClick={runTest} disabled={running || !state.targetEndpointId || state.mappings.length === 0}>
          {running ? <Loader2 className="iw-spin" size={14} /> : <Play size={14} />}
          {running ? 'Çalıştırılıyor…' : 'Test İste'}
        </button>
        <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 12, color: 'var(--iw-text)', cursor: 'pointer' }}>
          <input type="checkbox" checked={sendReal} onChange={e => setSendReal(e.target.checked)} />
          Gerçek gönderim (HTTP isteği gönder)
        </label>
      </div>

      {result && (
        <div className="iw-preview" style={{ maxWidth: 900 }}>
          {/* Sonuç durumu */}
          {result.success ? (
            <div style={{ display: 'flex', alignItems: 'center', gap: 8, color: 'var(--iw-emerald-color)', fontWeight: 600, marginBottom: 12 }}>
              <CheckCircle2 size={16} />
              <span>Başarılı{result.httpStatusCode ? ` — HTTP ${result.httpStatusCode}` : ''}</span>
            </div>
          ) : (
            <div style={{ display: 'flex', alignItems: 'center', gap: 8, color: 'var(--iw-rose-color)', fontWeight: 600, marginBottom: 12 }}>
              <AlertCircle size={16} />
              <span>Hata: {result.errorMessage || 'bilinmeyen'}</span>
            </div>
          )}

          {/* Validation uyarıları */}
          {result.validationWarnings && result.validationWarnings.length > 0 && (
            <div className="iw-preview-warn" style={{ marginBottom: 12 }}>
              <AlertCircle size={14} />
              <div>
                <strong>Uyarılar:</strong>
                <ul style={{ margin: '4px 0 0', paddingLeft: 16 }}>
                  {result.validationWarnings.map((w, i) => <li key={i}>{w}</li>)}
                </ul>
              </div>
            </div>
          )}

          {/* Request Body */}
          {result.requestBody && (
            <>
              <div style={{ fontSize: 11, fontWeight: 600, color: 'var(--iw-muted)', marginBottom: 4, textTransform: 'uppercase', letterSpacing: '.04em' }}>
                Request Body (Mapping çıktısı):
              </div>
              <pre className="iw-preview-json" style={{ marginBottom: 12 }}>
                {prettyJson(result.requestBody)}
              </pre>
            </>
          )}

          {/* Response Body */}
          {result.responseBody && (
            <>
              <div style={{ fontSize: 11, fontWeight: 600, color: 'var(--iw-muted)', marginBottom: 4, textTransform: 'uppercase', letterSpacing: '.04em' }}>
                Response Body:
              </div>
              <pre className="iw-preview-json">
                {prettyJson(result.responseBody)}
              </pre>
            </>
          )}
        </div>
      )}
    </>
  )
}

function prettyJson(s) {
  if (!s) return ''
  try { return JSON.stringify(JSON.parse(s), null, 2) }
  catch { return s }
}
