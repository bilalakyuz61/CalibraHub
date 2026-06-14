/**
 * Step 4 — Önizleme ve Test.
 *
 * 2026-05-25: UX revize —
 *   - Uzun aciklama paragrafı kaldırıldı
 *   - "Gerçek gönderim" checkbox'ı SADECE test başarılı olduktan sonra görünür
 *   - Body editable (kullanıcı JSON'ı düzenleyip gerçek gönderim yapabilir)
 *   - "Tümünü kopyala" butonu
 *   - Body tam genişlik (max-width kaldırıldı)
 */
import React, { useState, useCallback, useEffect } from 'react'
import { Play, Loader2, CheckCircle2, AlertCircle, Copy, Send } from 'lucide-react'

function getCsrf() {
  const el = document.querySelector('input[name="__RequestVerificationToken"]')
  return el ? el.value : ''
}

export default function WizardStep4Preview({ apiBase, state }) {
  const [running, setRunning]           = useState(false)
  const [sendingReal, setSendingReal]   = useState(false)
  const [result, setResult]             = useState(null)
  const [editedBody, setEditedBody]     = useState('')   // kullanıcı düzenleyebilir
  const [copyToast, setCopyToast]       = useState(null)
  const [realResult, setRealResult]     = useState(null) // gerçek gönderim sonucu (ayrı)

  // Test sonucu geldiğinde body'yi editable kutuya doldur
  useEffect(() => {
    if (result?.requestBody) {
      setEditedBody(prettyJson(result.requestBody))
    }
  }, [result])

  // Mapping çıktısını oluştur (gerçek gönderim YAPMA — sadece preview)
  const runTest = useCallback(async () => {
    setRunning(true)
    setResult(null)
    setRealResult(null)
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
          sendForReal: false,  // her zaman preview
        }),
      })
      const d = await r.json()
      setResult(d.result || { success: false, errorMessage: d.error || 'Bilinmeyen hata' })
    } catch (e) {
      setResult({ success: false, errorMessage: 'Sunucu hatası: ' + e.message })
    } finally {
      setRunning(false)
    }
  }, [apiBase, state])

  // Gerçek HTTP isteği — kullanıcının düzenlenmiş body'sini gönderir
  const sendForReal = useCallback(async () => {
    if (!editedBody?.trim()) return
    setSendingReal(true)
    setRealResult(null)
    try {
      // Backend test endpoint'i kendi mapping'i çalıştırıyor; biz override için ayrı bir alan
      // (overrideRequestBody) gönderiyoruz. Backend bu alanı görürse onu kullanır.
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
          sendForReal: true,
          overrideRequestBody: editedBody,  // backend bunu read ederse kullanır
        }),
      })
      const d = await r.json()
      setRealResult(d.result || { success: false, errorMessage: d.error || 'Bilinmeyen hata' })
    } catch (e) {
      setRealResult({ success: false, errorMessage: 'Sunucu hatası: ' + e.message })
    } finally {
      setSendingReal(false)
    }
  }, [apiBase, state, editedBody])

  const copyAll = useCallback(async () => {
    try {
      await navigator.clipboard.writeText(editedBody)
      setCopyToast('✓ Kopyalandı')
      setTimeout(() => setCopyToast(null), 1500)
    } catch {
      setCopyToast('Kopyalanamadı')
      setTimeout(() => setCopyToast(null), 1500)
    }
  }, [editedBody])

  return (
    <>
      <h2 className="iw-step-title">Önizleme ve Test</h2>

      {/* Test İste butonu — uzun açıklama kaldırıldı */}
      <div style={{ display: 'flex', gap: 8, alignItems: 'center', marginBottom: 16 }}>
        <button className="iw-btn-primary" onClick={runTest}
                disabled={running || !state.targetEndpointId || state.mappings.length === 0}>
          {running ? <Loader2 className="iw-spin" size={14} /> : <Play size={14} />}
          {running ? 'Çalıştırılıyor…' : 'Test İste'}
        </button>
      </div>

      {result && (
        <div className="iw-preview" style={{ maxWidth: 'none' }}>
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

          {/* Request Body — editable + tam genişlik + kopyala + gerçek gönderim */}
          {result.requestBody && (
            <div style={{ marginBottom: 16 }}>
              <div style={{
                display: 'flex', alignItems: 'center', justifyContent: 'space-between',
                marginBottom: 6,
              }}>
                <div style={{ fontSize: 11, fontWeight: 600, color: 'var(--iw-muted)', textTransform: 'uppercase', letterSpacing: '.04em' }}>
                  Request Body (Mapping çıktısı — düzenleyebilirsiniz)
                </div>
                <div style={{ display: 'flex', gap: 6, alignItems: 'center' }}>
                  {copyToast && <span style={{ fontSize: 11, color: 'var(--iw-emerald-color)' }}>{copyToast}</span>}
                  <button type="button" onClick={copyAll}
                          title="Tümünü kopyala"
                          style={{
                            display: 'inline-flex', alignItems: 'center', gap: 4,
                            padding: '4px 10px', fontSize: 11, fontWeight: 600,
                            border: '1px solid var(--iw-border)', borderRadius: 5,
                            background: 'transparent', color: 'var(--iw-text)', cursor: 'pointer',
                          }}>
                    <Copy size={12} /> Tümünü kopyala
                  </button>
                </div>
              </div>
              <textarea
                value={editedBody}
                onChange={e => setEditedBody(e.target.value)}
                spellCheck={false}
                style={{
                  width: '100%',
                  minHeight: 400,
                  padding: 12,
                  fontFamily: 'ui-monospace, Menlo, Consolas, monospace',
                  fontSize: 12,
                  lineHeight: 1.5,
                  background: 'var(--iw-code-bg, rgba(15, 23, 42, 0.04))',
                  color: 'var(--iw-text)',
                  border: '1px solid var(--iw-border)',
                  borderRadius: 6,
                  resize: 'vertical',
                  outline: 'none',
                  boxSizing: 'border-box',
                }}
              />

              {/* 2026-05-25: Gerçek gönderim SADECE test başarılıysa görünür */}
              {result.success && (
                <div style={{ display: 'flex', gap: 8, alignItems: 'center', marginTop: 10 }}>
                  <button type="button" onClick={sendForReal}
                          disabled={sendingReal || !editedBody.trim()}
                          style={{
                            display: 'inline-flex', alignItems: 'center', gap: 6,
                            padding: '7px 14px', fontSize: 12, fontWeight: 600,
                            border: 'none', borderRadius: 6,
                            background: 'linear-gradient(135deg, #6366f1, #8b5cf6)',
                            color: '#fff', cursor: sendingReal ? 'not-allowed' : 'pointer',
                            opacity: sendingReal || !editedBody.trim() ? 0.5 : 1,
                          }}>
                    {sendingReal ? <Loader2 className="iw-spin" size={13} /> : <Send size={13} />}
                    {sendingReal ? 'Gönderiliyor…' : 'Gerçek isteği gönder'}
                  </button>
                  <span style={{ fontSize: 11, color: 'var(--iw-muted)' }}>
                    Düzenlenmiş body endpoint'e POST edilir. IntegrationRun kaydı oluşmaz — sadece test.
                  </span>
                </div>
              )}
            </div>
          )}

          {/* Response Body (preview test sonucu) */}
          {result.responseBody && (
            <div style={{ marginBottom: 16 }}>
              <div style={{ fontSize: 11, fontWeight: 600, color: 'var(--iw-muted)', marginBottom: 4, textTransform: 'uppercase', letterSpacing: '.04em' }}>
                Response Body (önizleme):
              </div>
              <pre className="iw-preview-json" style={{ maxWidth: 'none' }}>
                {prettyJson(result.responseBody)}
              </pre>
            </div>
          )}

          {/* Gerçek gönderim sonucu — varsa ayrı kart */}
          {realResult && (
            <div style={{
              marginTop: 16, padding: 12,
              border: '1px solid ' + (realResult.success ? 'var(--iw-emerald-color)' : 'var(--iw-rose-color)'),
              borderRadius: 8,
              background: realResult.success ? 'var(--iw-emerald-bg)' : 'var(--iw-rose-bg)',
            }}>
              <div style={{ fontSize: 12, fontWeight: 700, marginBottom: 8,
                            color: realResult.success ? 'var(--iw-emerald-color)' : 'var(--iw-rose-color)' }}>
                {realResult.success
                  ? `✓ Gerçek istek başarılı (HTTP ${realResult.httpStatusCode || '?'})`
                  : `✗ Gerçek istek hatası: ${realResult.errorMessage || 'bilinmeyen'}`}
              </div>
              {realResult.responseBody && (
                <pre className="iw-preview-json" style={{ maxWidth: 'none', margin: 0 }}>
                  {prettyJson(realResult.responseBody)}
                </pre>
              )}
            </div>
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
