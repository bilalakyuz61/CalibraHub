/**
 * AiProvidersPanel — Şirket Ayarları "Yapay Zeka" tab içeriği.
 *
 * Admin grid + Add/Edit modal + Bağlantı Test butonu.
 * mount: window.CalibraHub.mountAiProvidersPanel(element)
 *
 * 2026-05-23 — Faz 1.D.
 */
import { useEffect, useState, useCallback } from 'react'
import { Plus, Pencil, Trash2, Zap, Check, X, AlertCircle, Loader2 } from 'lucide-react'

/**
 * SwitchKey — CLAUDE.md standardı: Boolean alanlar checkbox değil, toggle switch.
 * Track + sliding thumb pattern.
 */
function SwitchKey({ checked, onChange, label, disabled = false }) {
  return (
    <label className={'ai-pp__switch' + (disabled ? ' is-disabled' : '') + (checked ? ' is-on' : '')}
           onClick={(e) => { if (disabled) e.preventDefault() }}>
      <span className="ai-pp__switch-track" aria-hidden="true">
        <span className="ai-pp__switch-thumb" />
      </span>
      <input type="checkbox" checked={checked} disabled={disabled}
             onChange={e => !disabled && onChange(e.target.checked)}
             style={{ position: 'absolute', opacity: 0, pointerEvents: 'none' }} />
      <span className="ai-pp__switch-label">{label}</span>
    </label>
  )
}

const PROVIDER_CHOICES = [
  { code: 'openai',        label: 'OpenAI',         defaultModel: 'gpt-4o-mini',                requiresEndpoint: false, requiresKey: true,  defaultLabel: 'OpenAI (Şirket)',       endpointPlaceholder: '' },
  { code: 'anthropic',     label: 'Anthropic',      defaultModel: 'claude-3-5-sonnet-20241022', requiresEndpoint: false, requiresKey: true,  defaultLabel: 'Anthropic (Şirket)',    endpointPlaceholder: '' },
  { code: 'azure-openai',  label: 'Azure OpenAI',   defaultModel: 'gpt-4o',                     requiresEndpoint: true,  requiresKey: true,  defaultLabel: 'Azure OpenAI (Şirket)', endpointPlaceholder: 'https://my-resource.openai.azure.com/' },
  { code: 'gemini',        label: 'Google Gemini',  defaultModel: 'gemini-1.5-flash',           requiresEndpoint: false, requiresKey: true,  defaultLabel: 'Gemini (Şirket)',       endpointPlaceholder: '' },
  // 2026-05-23: Ollama lokal LLM — API key opsiyonel (reverse-proxy auth icin), endpoint
  // default http://localhost:11434. Model olarak "llama3.1:8b" (8B parametreli) varsayilan.
  { code: 'ollama',        label: 'Ollama (Lokal)', defaultModel: 'llama3.1:8b',                requiresEndpoint: false, requiresKey: false, defaultLabel: 'Ollama (Lokal)',        endpointPlaceholder: 'http://localhost:11434' },
  // 2026-05-24: DeepSeek — OpenAI-uyumlu API. Ucuz, native tool calling. Endpoint
  // varsayilan https://api.deepseek.com/v1. Modeller: deepseek-chat (V3), deepseek-reasoner (R1), deepseek-coder.
  { code: 'deepseek',      label: 'DeepSeek',       defaultModel: 'deepseek-chat',              requiresEndpoint: false, requiresKey: true,  defaultLabel: 'DeepSeek (Şirket)',     endpointPlaceholder: 'https://api.deepseek.com/v1' },
]

function getCsrf() {
  const el = document.querySelector('input[name="__RequestVerificationToken"]')
  return el ? el.value : ''
}

async function api(path, opts = {}) {
  const resp = await fetch(path, {
    method: opts.method || 'GET',
    credentials: 'same-origin',
    headers: {
      'Content-Type': 'application/json',
      ...(opts.method && opts.method !== 'GET' ? { 'RequestVerificationToken': getCsrf() } : {}),
    },
    body: opts.body ? JSON.stringify(opts.body) : undefined,
  })
  // 2026-05-23: Server JSON yerine HTML dönerse (auth redirect / 403 vs.) sağlam hata
  // mesajı üret — yoksa "Unexpected token '<'" parse exception kullanıcı için faydasız.
  const ct = resp.headers.get('content-type') || ''
  if (!ct.includes('application/json')) {
    let snippet = await resp.text().catch(() => '')
    snippet = snippet.replace(/<[^>]+>/g, ' ').replace(/\s+/g, ' ').trim().slice(0, 120)
    return {
      ok: false,
      error: `Sunucu JSON yerine HTML döndü (HTTP ${resp.status}). ` +
             (resp.status === 401 ? 'Oturum süresi dolmuş — sayfayı yenileyin.'
              : resp.status === 403 ? 'Yetkiniz yok — admin olarak giriş yapmanız gerekir.'
              : ('Cevap: ' + snippet)),
    }
  }
  return resp.json()
}

export default function AiProvidersPanel() {
  const [providers, setProviders] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)
  const [editing, setEditing] = useState(null)        // {Id, ...} | null
  const [testing, setTesting] = useState(0)
  const [testResult, setTestResult] = useState(null)  // {id, ok, msg}
  const [deleteConfirm, setDeleteConfirm] = useState(null)

  const reload = useCallback(async () => {
    setLoading(true)
    try {
      const d = await api('/Admin/AiProviders')
      if (d.ok) {
        setProviders(d.providers || [])
        setError(null)
      } else {
        // Kullanıcıya sade mesaj göster; ham "FORM_CODE:ACTION" teknik string'i değil.
        setError(d.message || d.error || 'Liste alınamadı')
      }
    } catch (e) { setError(e.message) }
    finally { setLoading(false) }
  }, [])

  useEffect(() => { reload() }, [reload])

  const startNew = () => {
    setEditing({
      id: 0, code: 'openai', label: '',
      apiKey: '', endpointUrl: '', defaultModel: '', extraJson: '',
      isActive: true, isDefault: providers.length === 0, sortOrder: providers.length * 10,
    })
  }

  const startEdit = (p) => {
    setEditing({
      id: p.id, code: p.code, label: p.label,
      apiKey: '',  // gerçek key dönmez — kullanıcı değiştirmek isterse yazar
      endpointUrl: p.endpointUrl || '',
      defaultModel: p.defaultModel || '',
      extraJson: p.extraJson || '',
      isActive: p.isActive, isDefault: p.isDefault, sortOrder: p.sortOrder,
      _hasExistingKey: p.hasApiKey,
    })
  }

  const cancelEdit = () => setEditing(null)

  const saveEdit = async () => {
    const choice = PROVIDER_CHOICES.find(c => c.code === editing.code)
    if (choice?.requiresEndpoint && !editing.endpointUrl?.trim()) {
      alert('Azure OpenAI için Endpoint URL zorunlu.')
      return
    }
    if (!editing.label?.trim()) {
      alert('Etiket (Label) zorunlu.')
      return
    }
    // 2026-05-23: Ollama icin API key opsiyonel — lokal kurulumda key gerekmez.
    if (editing.id === 0 && !editing.apiKey?.trim() && choice?.requiresKey !== false) {
      alert('Yeni provider için API Key zorunlu.')
      return
    }
    const body = {
      id: editing.id,
      code: editing.code,
      label: editing.label.trim(),
      apiKey: editing.apiKey?.trim() || null,
      endpointUrl: editing.endpointUrl?.trim() || null,
      defaultModel: editing.defaultModel?.trim() || null,
      extraJson: editing.extraJson?.trim() || null,
      isActive: editing.isActive,
      isDefault: editing.isDefault,
      sortOrder: editing.sortOrder || 0,
    }
    const d = await api('/Admin/AiProviders/save', { method: 'POST', body })
    if (d.ok) { setEditing(null); await reload() }
    else alert('Kaydedilemedi: ' + (d.error || ''))
  }

  const deleteProvider = async (id) => {
    const d = await api('/Admin/AiProviders/delete/' + id, { method: 'POST' })
    if (d.ok) { setDeleteConfirm(null); await reload() }
    else alert('Silinemedi: ' + (d.error || ''))
  }

  const testProvider = async (id) => {
    setTesting(id); setTestResult(null)
    try {
      const d = await api('/Admin/AiProviders/test/' + id, { method: 'POST' })
      setTestResult({
        id,
        ok: !!d.ok,
        msg: d.ok ? ('✓ Başarılı: ' + (d.sample || '').slice(0, 80)) : ('✗ ' + (d.error || 'bilinmeyen hata')),
      })
      setTimeout(() => setTestResult(null), 8000)
    } finally { setTesting(0) }
  }

  if (loading) {
    return <div className="ai-pp-loading"><Loader2 className="ai-spin" size={20} /> Yükleniyor…</div>
  }

  return (
    <div className="ai-pp">
      <div className="ai-pp__header">
        <div className="ai-pp__count">
          <strong>{providers.length}</strong> provider tanımlı
        </div>
        <button type="button" className="ai-pp__btn ai-pp__btn--primary" onClick={startNew}>
          <Plus size={14} /> Provider Ekle
        </button>
      </div>

      {error && <div className="ai-pp__error"><AlertCircle size={14} /> {error}</div>}

      {providers.length === 0 && (
        <div className="ai-pp__empty">
          Henüz provider tanımlanmamış. <em>"Provider Ekle"</em> ile başlayın.
        </div>
      )}

      <div className="ai-pp__grid">
        {providers.map(p => {
          const choice = PROVIDER_CHOICES.find(c => c.code === p.code)
          return (
            <div key={p.id} className={'ai-pp__card' + (p.isActive ? '' : ' is-inactive')}>
              <div className="ai-pp__card-head">
                <div>
                  <div className="ai-pp__card-title">{p.label}</div>
                  <div className="ai-pp__card-code">{choice?.label || p.code}</div>
                </div>
                <div className="ai-pp__chips">
                  {p.isDefault && <span className="ai-pp__chip ai-pp__chip--default">Varsayılan</span>}
                  {p.hasApiKey
                    ? <span className="ai-pp__chip ai-pp__chip--ok">Key ✓</span>
                    : <span className="ai-pp__chip ai-pp__chip--warn">Key yok</span>}
                  {!p.isActive && <span className="ai-pp__chip ai-pp__chip--off">Pasif</span>}
                </div>
              </div>
              <div className="ai-pp__card-body">
                {p.endpointUrl && <div><strong>Endpoint:</strong> {p.endpointUrl}</div>}
                <div><strong>Model:</strong> {p.defaultModel || <em>(provider default)</em>}</div>
              </div>
              <div className="ai-pp__card-actions">
                <button type="button" className="ai-pp__btn ai-pp__btn--ghost"
                        onClick={() => testProvider(p.id)} disabled={testing === p.id || !p.hasApiKey}>
                  {testing === p.id ? <Loader2 className="ai-spin" size={12} /> : <Zap size={12} />}
                  Test
                </button>
                <button type="button" className="ai-pp__btn ai-pp__btn--ghost"
                        onClick={() => startEdit(p)}>
                  <Pencil size={12} /> Düzenle
                </button>
                <button type="button" className="ai-pp__btn ai-pp__btn--danger"
                        onClick={() => setDeleteConfirm(p)}>
                  <Trash2 size={12} /> Sil
                </button>
              </div>
              {testResult && testResult.id === p.id && (
                <div className={'ai-pp__test-result ' + (testResult.ok ? 'is-ok' : 'is-err')}>
                  {testResult.msg}
                </div>
              )}
            </div>
          )
        })}
      </div>

      {/* Edit/Add Modal */}
      {editing && (
        <div className="ai-pp__modal-backdrop" onClick={cancelEdit}>
          <div className="ai-pp__modal" onClick={e => e.stopPropagation()}>
            <div className="ai-pp__modal-head">
              <h3>{editing.id === 0 ? 'Yeni Provider' : 'Provider Düzenle'}</h3>
              <button type="button" className="ai-pp__btn-x" onClick={cancelEdit}><X size={16} /></button>
            </div>
            <div className="ai-pp__modal-body">
              <div className="ai-pp__field">
                <label>Sağlayıcı</label>
                <select
                  value={editing.code}
                  onChange={e => {
                    const code = e.target.value
                    const choice = PROVIDER_CHOICES.find(c => c.code === code)
                    setEditing({
                      ...editing,
                      code,
                      label: editing.label || (choice?.defaultLabel || ''),
                      defaultModel: editing.defaultModel || (choice?.defaultModel || ''),
                    })
                  }}
                  disabled={editing.id > 0}>
                  {PROVIDER_CHOICES.map(c => (
                    <option key={c.code} value={c.code}>{c.label}</option>
                  ))}
                </select>
              </div>

              <div className="ai-pp__field">
                <label>Etiket *</label>
                <input type="text" value={editing.label}
                       onChange={e => setEditing({ ...editing, label: e.target.value })}
                       placeholder="Örn: OpenAI (Şirket)" />
              </div>

              {/* 2026-05-23: Ollama icin Ollama kurulum rehberi info karti. */}
              {editing.code === 'ollama' && (
                <div className="ai-pp__field ai-pp__ollama-info">
                  <div className="ai-pp__ollama-info-title">
                    Ollama Lokal Kurulum Rehberi
                  </div>
                  <ol style={{ paddingLeft: 18, margin: 0 }}>
                    <li>
                      <a href="https://ollama.com/download" target="_blank" rel="noopener noreferrer"
                         className="ai-pp__ollama-link">
                        ollama.com/download
                      </a> &mdash; uygulamayı indirip kurun.
                    </li>
                    <li>
                      Terminalde: <code className="ai-pp__ollama-code">
                        ollama pull llama3.1:8b
                      </code>
                    </li>
                    <li>
                      Ollama servisi <code className="ai-pp__ollama-code">
                        localhost:11434
                      </code>'te otomatik açılır. Endpoint boş bırakılırsa bu adres kullanılır.
                    </li>
                    <li>
                      API Key gerekmez (lokal). Reverse-proxy arkasında Auth varsa key alanına Bearer token yazın.
                    </li>
                  </ol>
                </div>
              )}

              <div className="ai-pp__field">
                <label>
                  API Key {(() => {
                    const ch = PROVIDER_CHOICES.find(c => c.code === editing.code)
                    if (ch && ch.requiresKey === false) {
                      return editing.id > 0 && editing._hasExistingKey ? '(opsiyonel · boş bırak = mevcut korunur)' : '(opsiyonel)'
                    }
                    return editing.id > 0 && editing._hasExistingKey ? '(boş bırak = mevcut korunur)' : '*'
                  })()}
                </label>
                <input type="password" value={editing.apiKey}
                       onChange={e => setEditing({ ...editing, apiKey: e.target.value })}
                       placeholder={editing._hasExistingKey ? '●●●●●●●●' :
                                    editing.code === 'ollama' ? 'Boş bırakın (lokal Ollama)' : 'sk-...'} />
              </div>

              {(() => {
                const ch = PROVIDER_CHOICES.find(c => c.code === editing.code)
                // Endpoint Azure icin zorunlu, Ollama icin opsiyonel (her ikisi de gosterilir),
                // OpenAI/Anthropic/Gemini icin gizli.
                const showEndpoint = ch?.requiresEndpoint || editing.code === 'ollama'
                if (!showEndpoint) return null
                const required = ch?.requiresEndpoint === true
                return (
                  <div className="ai-pp__field">
                    <label>Endpoint URL {required ? '*' : '(opsiyonel · default localhost:11434)'}</label>
                    <input type="text" value={editing.endpointUrl}
                           onChange={e => setEditing({ ...editing, endpointUrl: e.target.value })}
                           placeholder={ch?.endpointPlaceholder || ''} />
                  </div>
                )
              })()}

              <div className="ai-pp__field">
                <label>Varsayılan Model</label>
                <input type="text" value={editing.defaultModel}
                       onChange={e => setEditing({ ...editing, defaultModel: e.target.value })}
                       placeholder={PROVIDER_CHOICES.find(c => c.code === editing.code)?.defaultModel || ''} />
              </div>

              <div className="ai-pp__field">
                <label>Ek Config (JSON, opsiyonel)</label>
                <textarea value={editing.extraJson}
                          onChange={e => setEditing({ ...editing, extraJson: e.target.value })}
                          placeholder={editing.code === 'azure-openai' ? '{"deploymentName": "my-gpt4-deployment", "apiVersion": "2024-08-01-preview"}' : ''}
                          rows={2} />
              </div>

              {/* 2026-05-23 — CLAUDE.md kuralı: Boolean alanlar switchkey (toggle) olmalı.
                  Aktif + Varsayılan native checkbox yerine custom switch component. */}
              <div className="ai-pp__field-row">
                <SwitchKey
                  checked={editing.isActive}
                  onChange={v => setEditing({ ...editing, isActive: v })}
                  label="Aktif" />
                <SwitchKey
                  checked={editing.isDefault}
                  onChange={v => setEditing({ ...editing, isDefault: v })}
                  label="Varsayılan (kullanıcı seçmediğinde)" />
              </div>
            </div>
            <div className="ai-pp__modal-actions">
              <button type="button" className="ai-pp__btn ai-pp__btn--ghost" onClick={cancelEdit}>Vazgeç</button>
              <button type="button" className="ai-pp__btn ai-pp__btn--primary" onClick={saveEdit}>
                <Check size={14} /> Kaydet
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Sil Onay Modal */}
      {deleteConfirm && (
        <div className="ai-pp__modal-backdrop" onClick={() => setDeleteConfirm(null)}>
          <div className="ai-pp__modal ai-pp__modal--narrow" onClick={e => e.stopPropagation()}>
            <div className="ai-pp__modal-head">
              <h3>Provider Sil</h3>
              <button type="button" className="ai-pp__btn-x" onClick={() => setDeleteConfirm(null)}><X size={16} /></button>
            </div>
            <div className="ai-pp__modal-body">
              <p><strong>{deleteConfirm.label}</strong> silinsin mi?</p>
              <p style={{ fontSize: 12, color: '#dc2626' }}>
                Bu provider'ı kullanan tüm kullanıcı override key'leri de silinir (CASCADE).
                Sohbet edenler artık bu provider'ı seçemez. Bu işlem geri alınamaz.
              </p>
            </div>
            <div className="ai-pp__modal-actions">
              <button type="button" className="ai-pp__btn ai-pp__btn--ghost" onClick={() => setDeleteConfirm(null)}>Vazgeç</button>
              <button type="button" className="ai-pp__btn ai-pp__btn--danger" onClick={() => deleteProvider(deleteConfirm.id)}>
                <Trash2 size={14} /> Evet, Sil
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
