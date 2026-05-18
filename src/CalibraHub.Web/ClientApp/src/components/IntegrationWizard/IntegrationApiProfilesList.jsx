/**
 * IntegrationApiProfilesList — API Profile yönetim sayfası (Hub'ın 1. tab'ı).
 *
 * Şirket Ayarları → Entegrasyon API ekranının yerine geçer; aynı veriyi (per-company
 * integration_api_profiles tablosu) yönetir. Auth tipleri:
 *   • None
 *   • OAuth2Password — Token URL + Kullanıcı + Şifre + Netsis ek alanlar
 *                      (BranchCode, DbName, DbUser, DbPassword, DbType)
 *   • BearerStatic   — sabit token
 *   • BasicAuth      — kullanıcı + şifre
 *   • ApiKey         — header adı + değer
 *
 * Aksiyonlar:
 *   ✏  Düzenle  (modal)
 *   ⚡ Test    (token endpoint çağır + sonucu göster)
 *   ⏼  Toggle   (POST /api/profiles/toggle/{id})
 *   🗑  Sil     (POST /api/profiles/delete/{id} — FK varsa engellenir)
 */
import React, { useState, useEffect, useCallback, useMemo } from 'react'
import {
  Plug, Plus, Search, Edit2, Trash2, Power, RefreshCw,
  AlertTriangle, Loader2, Check, X as XIcon, X, Save, Settings2,
  KeyRound, Database, Zap,
} from 'lucide-react'

function getCsrf() {
  const el = document.querySelector('input[name="__RequestVerificationToken"]')
  return el ? el.value : ''
}
function toast(msg, kind) {
  if (window.CalibraHub?.toast) window.CalibraHub.toast(msg, kind || 'info')
}

const AUTH_TYPES = [
  { id: 'None',           label: 'Auth Yok',         desc: 'Header eklenmez' },
  { id: 'OAuth2Password', label: 'OAuth2 Password',  desc: 'Token URL + kullanıcı + şifre' },
  { id: 'BearerStatic',   label: 'Bearer Token',     desc: 'Sabit token' },
  { id: 'BasicAuth',      label: 'Basic Auth',       desc: 'Kullanıcı + şifre (Base64)' },
  { id: 'ApiKey',         label: 'API Key',          desc: 'Header adı + değer' },
]

/** AuthConfigJson içeriğini auth tipine göre objeye çevir (modal load için). */
function parseAuthConfig(authType, json) {
  const result = {
    // OAuth2
    tokenEndpoint: '/api/v2/token',
    username: '', password: '',
    branchCode: '0', dbName: '', dbUser: 'sa', dbPassword: '', dbType: '0',
    // Bearer
    bearerToken: '',
    // Basic
    basicUsername: '', basicPassword: '',
    // ApiKey
    apiKeyHeader: 'X-Api-Key', apiKeyValue: '',
  }
  if (!json) return result
  try {
    const c = JSON.parse(json)
    if (authType === 'OAuth2Password') {
      result.tokenEndpoint = c.tokenEndpoint || '/api/v2/token'
      result.username = c.username || ''
      result.password = c.password || ''
      const ef = c.extraFields || {}
      result.branchCode = ef.branchcode || '0'
      result.dbName     = ef.dbname || ''
      result.dbUser     = ef.dbuser || 'sa'
      result.dbPassword = ef.dbpassword || ''
      result.dbType     = ef.dbtype || '0'
    } else if (authType === 'BearerStatic') {
      result.bearerToken = c.token || ''
    } else if (authType === 'BasicAuth') {
      result.basicUsername = c.username || ''
      result.basicPassword = c.password || ''
    } else if (authType === 'ApiKey') {
      result.apiKeyHeader = c.apiKeyHeader || c.headerName || 'X-Api-Key'
      result.apiKeyValue  = c.apiKeyValue  || c.key || ''
    }
  } catch { /* invalid JSON — defaults kalsın */ }
  return result
}

/** Modal state'ini AuthConfigJson string'ine serialize et. */
function buildAuthConfig(authType, st) {
  if (authType === 'None') return null
  if (authType === 'OAuth2Password') {
    return JSON.stringify({
      tokenEndpoint: st.tokenEndpoint,
      tokenField: 'access_token',
      grantType: 'password',
      username: st.username,
      password: st.password,
      extraFields: {
        branchcode: st.branchCode,
        dbname: st.dbName,
        dbuser: st.dbUser,
        dbpassword: st.dbPassword,
        dbtype: st.dbType,
      },
    })
  }
  if (authType === 'BearerStatic') return JSON.stringify({ token: st.bearerToken })
  if (authType === 'BasicAuth')    return JSON.stringify({ username: st.basicUsername, password: st.basicPassword })
  if (authType === 'ApiKey')       return JSON.stringify({ apiKeyHeader: st.apiKeyHeader, apiKeyValue: st.apiKeyValue })
  return null
}

// ─── Delete onay modali ──────────────────────────────────────────────────
function DeleteModal({ name, onCancel, onConfirm, loading }) {
  useEffect(() => {
    const h = e => { if (e.key === 'Escape') onCancel() }
    window.addEventListener('keydown', h)
    return () => window.removeEventListener('keydown', h)
  }, [onCancel])
  return (
    <div className="iw-modal-bd" onClick={onCancel}>
      <div className="iw-modal" onClick={e => e.stopPropagation()}>
        <div className="iw-modal-icon"><AlertTriangle size={32} /></div>
        <div className="iw-modal-title">Profile'ı Sil</div>
        <div className="iw-modal-msg">
          <strong>{name}</strong> silinecek. Bu profile'a bağlı endpoint(ler) varsa
          işlem reddedilir; önce o endpoint'leri kaldırın veya başka profile'a taşıyın.
        </div>
        <div className="iw-modal-actions">
          <button className="iw-modal-cancel" onClick={onCancel}>Vazgeç</button>
          <button className="iw-modal-del" onClick={onConfirm} disabled={loading}>
            {loading ? 'Siliniyor…' : 'Sil'}
          </button>
        </div>
      </div>
    </div>
  )
}

// ─── ProfileEditModal — sol tab + sağ içerik ─────────────────────────────
function ProfileEditModal({ profileId, onClose, onSaved }) {
  const isNew = !profileId
  const [tab, setTab]         = useState('basic')
  const [loading, setLoading] = useState(!isNew)
  const [saving, setSaving]   = useState(false)
  const [testing, setTesting] = useState(false)
  const [testResult, setTestResult] = useState(null)
  const [form, setForm]       = useState({
    id: profileId || null,
    name: '',
    baseUrl: 'http://localhost:7070',
    authType: 'OAuth2Password',
    isActive: true,
    providerCode: 'Netsis',
  })
  const [providers, setProviders] = useState([])
  const [showProviderModal, setShowProviderModal] = useState(false)
  const reloadProviders = useCallback(() => {
    return fetch('/Integrations/api/doc-catalog/providers', { credentials: 'same-origin' })
      .then(r => r.ok ? r.json() : null)
      .then(d => {
        if (d && Array.isArray(d.items)) { setProviders(d.items); return d.items }
        return []
      })
      .catch(() => [])
  }, [])
  useEffect(() => { reloadProviders() }, [reloadProviders])
  const [authState, setAuthState] = useState(parseAuthConfig('OAuth2Password', null))

  useEffect(() => {
    if (isNew) return
    setLoading(true)
    fetch(`/Integrations/api/profiles/${profileId}`, { credentials: 'same-origin' })
      .then(r => r.json())
      .then(d => {
        if (d.success) {
          const p = d.profile
          setForm({ id: p.id, name: p.name, baseUrl: p.baseUrl, authType: p.authType || 'None', isActive: p.isActive, providerCode: p.providerCode || 'Netsis' })
          setAuthState(parseAuthConfig(p.authType || 'None', p.authConfigJson))
        } else toast(d.error || 'Profile yüklenemedi', 'err')
      })
      .catch(e => toast('Sunucu hatası: ' + e.message, 'err'))
      .finally(() => setLoading(false))
  }, [profileId, isNew])

  // Esc ile kapat
  useEffect(() => {
    const h = e => { if (e.key === 'Escape' && !saving) onClose() }
    window.addEventListener('keydown', h)
    return () => window.removeEventListener('keydown', h)
  }, [onClose, saving])

  const upd  = (patch) => setForm(s => ({ ...s, ...patch }))
  const updA = (patch) => setAuthState(s => ({ ...s, ...patch }))

  const handleSave = async () => {
    if (!form.name.trim())    { toast('Profil adı zorunlu', 'err'); setTab('basic'); return }
    if (!form.baseUrl.trim()) { toast('Base URL zorunlu', 'err'); setTab('basic'); return }
    setSaving(true)
    try {
      const r = await fetch('/Integrations/api/profiles/save', {
        method: 'POST', credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json', RequestVerificationToken: getCsrf() },
        body: JSON.stringify({
          id: form.id,
          name: form.name.trim(),
          baseUrl: form.baseUrl.trim(),
          authType: form.authType,
          authConfigJson: buildAuthConfig(form.authType, authState),
          isActive: form.isActive,
          providerCode: form.providerCode || 'Netsis',
        }),
      })
      const d = await r.json()
      if (d.success) {
        toast(isNew ? 'Profile oluşturuldu' : 'Profile güncellendi', 'ok')
        onSaved?.(d.id)
      } else toast(d.error || 'Kayıt hatası', 'err')
    } catch (e) {
      toast('Sunucu hatası: ' + e.message, 'err')
    } finally {
      setSaving(false)
    }
  }

  const handleTest = async () => {
    if (!form.id) { toast('Önce kaydedin, sonra test edin', 'err'); return }
    setTesting(true)
    setTestResult(null)
    try {
      const r = await fetch(`/Integrations/api/profiles/test/${form.id}`, {
        method: 'POST', credentials: 'same-origin',
        headers: { RequestVerificationToken: getCsrf() },
      })
      const d = await r.json()
      setTestResult(d)
      if (d.success) toast('✓ ' + (d.message || 'Bağlantı başarılı'), 'ok')
      else toast('✗ ' + (d.error || 'Bağlantı başarısız'), 'err')
    } catch (e) {
      setTestResult({ success: false, error: e.message })
      toast('Sunucu hatası: ' + e.message, 'err')
    } finally {
      setTesting(false)
    }
  }

  const TABS = [
    { id: 'basic', label: 'Genel',         icon: Settings2 },
    { id: 'auth',  label: 'Kimlik Doğrulama', icon: KeyRound },
    ...(form.authType === 'OAuth2Password' ? [
      { id: 'netsis', label: 'Netsis Ek Alanlar', icon: Database }
    ] : []),
  ]

  return (
    <div className="iw-modal-bd" onClick={() => !saving && onClose()}>
      <div className="eem-modal" onClick={e => e.stopPropagation()}
           style={{ width: 760, height: 580, maxHeight: '92vh' }}>
        <div className="eem-header">
          <div className="eem-title">
            {isNew ? 'Yeni API Profile' : 'API Profile Düzenle'}
            {form.name && <span className="eem-title-sub"> — {form.name}</span>}
          </div>
          <button className="eem-icon-btn" onClick={onClose} disabled={saving} title="Kapat (Esc)">
            <X size={16} />
          </button>
        </div>

        {loading && (
          <div style={{ flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
            <Loader2 className="iw-spin" size={32} />
          </div>
        )}

        {!loading && (
          <div className="eem-body">
            {/* Sol — tab menü */}
            <nav className="eem-tabs">
              {TABS.map(t => {
                const Icon = t.icon
                return (
                  <button key={t.id}
                          className={'eem-tab' + (tab === t.id ? ' is-active' : '')}
                          onClick={() => setTab(t.id)}>
                    <Icon size={14} /><span>{t.label}</span>
                  </button>
                )
              })}
            </nav>

            {/* Sağ — içerik */}
            <div className="eem-content">
              {tab === 'basic' && (
                <div className="eem-tab-pane">
                  <div className="iw-field">
                    <label>Ad *</label>
                    <input value={form.name} onChange={e => upd({ name: e.target.value })}
                           placeholder="Örn: Netsis NetOpenX REST" maxLength={200} disabled={saving} />
                  </div>
                  <div className="iw-field">
                    <label>Provider</label>
                    <div style={{ display: 'flex', gap: 6, alignItems: 'stretch' }}>
                      <select value={form.providerCode || 'Netsis'}
                              onChange={e => upd({ providerCode: e.target.value })} disabled={saving}
                              style={{ flex: '1 1 auto' }}>
                        {providers.length === 0
                          ? <option value="Netsis">Netsis</option>
                          : providers.map(p => (
                              <option key={p.code} value={p.code}>
                                {p.label} ({p.code}) — {p.enumCount} enum, {p.fieldDocCount} alan
                              </option>
                            ))}
                      </select>
                      <button type="button" onClick={() => setShowProviderModal(true)} disabled={saving}
                              title="Yeni Provider Ekle (ör. SAP, Logo)"
                              style={{
                                padding: '0 14px', fontSize: 18, lineHeight: 1, fontWeight: 300,
                                border: '1px dashed var(--iw-border, #334155)',
                                background: 'transparent', color: 'var(--iw-accent, #6366f1)',
                                borderRadius: 8, cursor: saving ? 'not-allowed' : 'pointer',
                                flex: '0 0 auto',
                              }}>+</button>
                    </div>
                    <span className="iw-field-hint">
                      Wizard'da alan açıklamalarını/enumları bu provider'ın katalogundan çeker.
                    </span>
                  </div>
                  {showProviderModal && (
                    <ProviderCreateModal
                      providers={providers}
                      currentCode={form.providerCode}
                      onClose={() => setShowProviderModal(false)}
                      onSaved={async (savedCode) => {
                        await reloadProviders()
                        upd({ providerCode: savedCode })
                        setShowProviderModal(false)
                      }}
                      onDeleted={async (deletedCode) => {
                        const fresh = await reloadProviders()
                        // Silinmis provider seciliyse ilk kalan'a fallback
                        if (form.providerCode === deletedCode) {
                          upd({ providerCode: fresh[0]?.code || 'Netsis' })
                        }
                        setShowProviderModal(false)
                      }} />
                  )}
                  <div className="iw-field">
                    <label>Base URL *</label>
                    <input value={form.baseUrl} onChange={e => upd({ baseUrl: e.target.value })}
                           placeholder="http://localhost:7070" maxLength={500} disabled={saving} />
                    <span className="iw-field-hint">
                      Endpoint URL şablonları (`/api/v2/...`) bu adresin sonuna eklenir.
                    </span>
                  </div>
                  <div className="iw-field">
                    <label>Auth Tipi</label>
                    <select value={form.authType} onChange={e => upd({ authType: e.target.value })} disabled={saving}>
                      {AUTH_TYPES.map(a => <option key={a.id} value={a.id}>{a.label} — {a.desc}</option>)}
                    </select>
                    <span className="iw-field-hint">
                      Detaylar "Kimlik Doğrulama" sekmesinde
                      {form.authType === 'OAuth2Password' && ' + Netsis Ek Alanlar'}.
                    </span>
                  </div>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginTop: 8 }}>
                    <button className={'iw-switch' + (form.isActive ? ' is-on' : '')}
                            onClick={() => upd({ isActive: !form.isActive })} disabled={saving}>
                      <span className="iw-switch__thumb" />
                    </button>
                    <span style={{ fontSize: 13 }}>Aktif</span>
                  </div>
                </div>
              )}

              {tab === 'auth' && (
                <div className="eem-tab-pane">
                  {form.authType === 'None' && (
                    <div style={{
                      padding: 16, background: 'var(--iw-bg)', borderRadius: 8,
                      fontSize: 12, color: 'var(--iw-muted)', lineHeight: 1.6,
                    }}>
                      Bu profile için kimlik doğrulama yapılandırması yok. HTTP isteklerine
                      hiçbir auth header eklenmez. Kimlik doğrulama gerektiren endpoint'ler
                      için "Genel" sekmesinden başka bir auth tipi seçin.
                    </div>
                  )}

                  {form.authType === 'OAuth2Password' && (
                    <>
                      <div className="iw-field">
                        <label>Token URL</label>
                        <input value={authState.tokenEndpoint}
                               onChange={e => updA({ tokenEndpoint: e.target.value })}
                               placeholder="/api/v2/token" disabled={saving} />
                        <span className="iw-field-hint">Base URL'in sonuna eklenir.</span>
                      </div>
                      <div className="iw-field">
                        <label>Kullanıcı</label>
                        <input value={authState.username}
                               onChange={e => updA({ username: e.target.value })}
                               placeholder="bilal" disabled={saving} />
                      </div>
                      <div className="iw-field">
                        <label>Şifre</label>
                        <input type="password" value={authState.password}
                               onChange={e => updA({ password: e.target.value })}
                               disabled={saving} />
                      </div>
                    </>
                  )}

                  {form.authType === 'BearerStatic' && (
                    <div className="iw-field">
                      <label>Bearer Token</label>
                      <input value={authState.bearerToken}
                             onChange={e => updA({ bearerToken: e.target.value })}
                             placeholder="eyJhbGciOiJIUzI1NiIs..." disabled={saving} />
                    </div>
                  )}

                  {form.authType === 'BasicAuth' && (
                    <>
                      <div className="iw-field">
                        <label>Kullanıcı</label>
                        <input value={authState.basicUsername}
                               onChange={e => updA({ basicUsername: e.target.value })} disabled={saving} />
                      </div>
                      <div className="iw-field">
                        <label>Şifre</label>
                        <input type="password" value={authState.basicPassword}
                               onChange={e => updA({ basicPassword: e.target.value })} disabled={saving} />
                      </div>
                    </>
                  )}

                  {form.authType === 'ApiKey' && (
                    <>
                      <div className="iw-field">
                        <label>Header Adı</label>
                        <input value={authState.apiKeyHeader}
                               onChange={e => updA({ apiKeyHeader: e.target.value })}
                               placeholder="X-Api-Key" disabled={saving} />
                      </div>
                      <div className="iw-field">
                        <label>Değer</label>
                        <input value={authState.apiKeyValue}
                               onChange={e => updA({ apiKeyValue: e.target.value })} disabled={saving} />
                      </div>
                    </>
                  )}

                  {/* Test sonucu paneli */}
                  {testResult && (
                    <div style={{
                      marginTop: 14, padding: '8px 12px', borderRadius: 6, fontSize: 12,
                      background: testResult.success ? 'var(--iw-emerald-bg)' : 'var(--iw-rose-bg)',
                      color: testResult.success ? 'var(--iw-emerald-color)' : 'var(--iw-rose-color)',
                      border: `1px solid var(--iw-${testResult.success ? 'emerald' : 'rose'}-color)`,
                    }}>
                      <strong>{testResult.success ? '✓ Bağlantı başarılı' : '✗ Bağlantı başarısız'}</strong>
                      <div style={{ marginTop: 4, color: 'var(--iw-text)' }}>
                        {testResult.message || testResult.error}
                      </div>
                    </div>
                  )}
                </div>
              )}

              {tab === 'netsis' && form.authType === 'OAuth2Password' && (
                <div className="eem-tab-pane">
                  <div style={{
                    padding: 8, marginBottom: 14, fontSize: 11,
                    background: 'var(--iw-indigo-bg)', color: 'var(--iw-indigo-color)',
                    borderRadius: 6, lineHeight: 1.5,
                  }}>
                    Netsis NetOpenX REST token isteğinin form body'sine eklenen ek alanlar.
                    DbName/DbUser/DbPassword Netsis SQL Server bağlantısı için.
                  </div>
                  <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
                    <div className="iw-field">
                      <label>BranchCode</label>
                      <input value={authState.branchCode}
                             onChange={e => updA({ branchCode: e.target.value })} disabled={saving} />
                    </div>
                    <div className="iw-field">
                      <label>DbType</label>
                      <input value={authState.dbType}
                             onChange={e => updA({ dbType: e.target.value })} disabled={saving} />
                    </div>
                  </div>
                  <div className="iw-field">
                    <label>DbName</label>
                    <input value={authState.dbName}
                           onChange={e => updA({ dbName: e.target.value })}
                           placeholder="ÖRN: OCAKLTD2026" disabled={saving} />
                  </div>
                  <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
                    <div className="iw-field">
                      <label>DbUser</label>
                      <input value={authState.dbUser}
                             onChange={e => updA({ dbUser: e.target.value })} disabled={saving} />
                    </div>
                    <div className="iw-field">
                      <label>DbPassword</label>
                      <input type="password" value={authState.dbPassword}
                             onChange={e => updA({ dbPassword: e.target.value })} disabled={saving} />
                    </div>
                  </div>
                </div>
              )}
            </div>
          </div>
        )}

        {/* Footer */}
        <div className="eem-footer">
          {form.id && (
            <button className="iw-btn-secondary" onClick={handleTest} disabled={saving || testing}>
              {testing
                ? <><Loader2 className="iw-spin" size={13} /> Test ediliyor</>
                : <><Zap size={13} /> Bağlantıyı Test Et</>}
            </button>
          )}
          <span style={{ flex: 1 }} />
          <button className="iw-btn-secondary" onClick={onClose} disabled={saving}>Vazgeç</button>
          <button className="iw-btn-primary" onClick={handleSave} disabled={saving}>
            {saving ? <><Loader2 className="iw-spin" size={14} /> Kaydediliyor</> : <><Save size={14} /> Kaydet</>}
          </button>
        </div>
      </div>
    </div>
  )
}

// ─── Ana liste ───────────────────────────────────────────────────────────
export default function IntegrationApiProfilesList({ config }) {
  const [profiles, setProfiles]     = useState([])
  const [loading, setLoading]       = useState(true)
  const [search, setSearch]         = useState('')
  const [editing, setEditing]       = useState(null)   // null | 'new' | profileId
  const [deleteTarget, setDelete]   = useState(null)
  const [deleting, setDeleting]     = useState(false)

  const refresh = useCallback(async () => {
    setLoading(true)
    try {
      const r = await fetch('/Integrations/api/profiles?includeInactive=true', { credentials: 'same-origin' })
      const d = await r.json()
      if (d.success) setProfiles(d.profiles || [])
      else toast(d.error || 'Liste alınamadı', 'err')
    } catch (e) {
      toast('Sunucu hatası: ' + e.message, 'err')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { refresh() }, [refresh])

  const filtered = useMemo(() => {
    if (!search) return profiles
    const q = search.toLowerCase()
    return profiles.filter(p =>
      (p.name || '').toLowerCase().includes(q) ||
      (p.baseUrl || '').toLowerCase().includes(q) ||
      (p.authType || '').toLowerCase().includes(q) ||
      (p.username || '').toLowerCase().includes(q)
    )
  }, [profiles, search])

  const handleToggle = async (p) => {
    try {
      const r = await fetch(`/Integrations/api/profiles/toggle/${p.id}`, {
        method: 'POST', credentials: 'same-origin',
        headers: { RequestVerificationToken: getCsrf() },
      })
      const d = await r.json()
      if (d.success) { toast(d.isActive ? 'Aktif edildi' : 'Pasif edildi', 'ok'); refresh() }
      else toast(d.error || 'Hata', 'err')
    } catch (e) { toast('Sunucu hatası: ' + e.message, 'err') }
  }

  const handleDelete = async () => {
    if (!deleteTarget) return
    setDeleting(true)
    try {
      const r = await fetch(`/Integrations/api/profiles/delete/${deleteTarget.id}`, {
        method: 'POST', credentials: 'same-origin',
        headers: { RequestVerificationToken: getCsrf() },
      })
      const d = await r.json()
      if (d.success) { toast('Silindi', 'ok'); setDelete(null); refresh() }
      else { toast(d.error || 'Silinemedi', 'err'); setDelete(null) }
    } catch (e) {
      toast('Sunucu hatası: ' + e.message, 'err')
      setDelete(null)
    } finally {
      setDeleting(false)
    }
  }

  return (
    <div className="il-root">
      <div className="il-toolbar">
        <div className="il-title">
          <Plug size={16} />
          <span>API Profilleri</span>
          <span className="il-count">{filtered.length} / {profiles.length}</span>
        </div>
        <div className="il-spacer" />
        <div className="il-search-wrap">
          <Search size={13} className="il-search-icon" />
          <input className="il-search" placeholder="Ad, URL veya auth ara…"
                 value={search} onChange={e => setSearch(e.target.value)} />
        </div>
        <button className="iw-btn-secondary" onClick={refresh} title="Yenile">
          <RefreshCw size={13} />
        </button>
        <button className="il-btn-primary" onClick={() => setEditing('new')}>
          <Plus size={14} /> Yeni Profile
        </button>
      </div>

      <div className="il-list">
        {loading && (
          <div className="il-empty">
            <Loader2 className="iw-spin" size={32} />
            <span>Yükleniyor…</span>
          </div>
        )}
        {!loading && filtered.length === 0 && (
          <div className="il-empty">
            <Plug size={48} style={{ opacity: 0.3 }} />
            <span>{profiles.length === 0 ? 'Henüz profile yok.' : 'Aramaya uyan kayıt yok.'}</span>
            {profiles.length === 0 && (
              <button className="il-btn-primary" onClick={() => setEditing('new')}>
                <Plus size={14} /> İlk profile'ı oluştur
              </button>
            )}
          </div>
        )}
        {!loading && filtered.map(p => (
          <div key={p.id} className="il-card">
            <div className="il-actions il-actions--leading">
              <button className="il-act il-act-edit" title="Düzenle"
                      onClick={() => setEditing(p.id)}>
                <Edit2 size={14} />
              </button>
              <button className="il-act" title="Aktif/Pasif"
                      onClick={() => handleToggle(p)}>
                <Power size={14} />
              </button>
              <button className="il-act il-act-del" title="Sil"
                      onClick={() => setDelete(p)}>
                <Trash2 size={14} />
              </button>
            </div>
            <div className="il-card-main">
              <div className="il-card-name">{p.name}</div>
              <div className="il-card-desc" style={{ fontFamily: 'monospace' }}>{p.baseUrl}</div>
            </div>
            <div className="il-card-flow">
              <span>Auth</span>
              <span style={{ fontWeight: 500, color: 'var(--iw-text)' }}>
                {p.authSummary || p.authType || 'None'}
              </span>
            </div>
            <div className="il-chips">
              <span className={'il-chip ' + ((p.authType || '').toLowerCase() === 'oauth2password' ? 'il-chip-trigger' : 'il-chip-runs')}>
                {p.authType || 'None'}
              </span>
              {p.extraFieldsCount > 0 && (
                <span className="il-chip il-chip-runs" title="Netsis ek alanlar">
                  +{p.extraFieldsCount} ek
                </span>
              )}
            </div>
          </div>
        ))}
      </div>

      {editing && (
        <ProfileEditModal
          profileId={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => { setEditing(null); refresh() }}
        />
      )}
      {deleteTarget && (
        <DeleteModal
          name={deleteTarget.name}
          onCancel={() => setDelete(null)}
          onConfirm={handleDelete}
          loading={deleting}
        />
      )}
    </div>
  )
}

/**
 * ProviderCreateModal — Provider create/edit/delete tek modal.
 *
 * Kullanici Kod alanina mevcut bir provider kodu yazarsa:
 *   - Diger alanlar otomatik dolar (kullanici zaten yazdiysa override yapilmaz)
 *   - "Kaydet" -> "Guncelle"
 *   - "Sil" butonu gorunur (soft delete - IsActive=false)
 * Aksi halde: yeni create akisi.
 *
 * onSaved(code): yeni veya guncellenmis provider'in code'u
 * onDeleted(code): silinen provider'in code'u
 */
function ProviderCreateModal({ providers = [], currentCode, onClose, onSaved, onDeleted }) {
  const [code, setCode]     = useState('')
  const [label, setLabel]   = useState('')
  const [color, setColor]   = useState('indigo')
  const [desc, setDesc]     = useState('')
  const [saving, setSaving] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const [error, setError]   = useState(null)
  const [confirmDel, setConfirmDel] = useState(false)

  // Code -> matched provider (case-insensitive)
  const matched = useMemo(() => {
    const typed = code.trim().toLowerCase()
    if (!typed) return null
    return providers.find(p => (p.code || '').toLowerCase() === typed) || null
  }, [code, providers])

  // Matched degisirse autofill (sadece kullanicinin BOS biraktigi alanlar — overwrite yok)
  useEffect(() => {
    if (matched) {
      setLabel(prev => prev.trim() ? prev : (matched.label || ''))
      setDesc(prev => prev.trim() ? prev : (matched.description || ''))
      setColor(matched.iconColor || 'indigo')
    }
  }, [matched])

  useEffect(() => {
    const onKey = (e) => { if (e.key === 'Escape') onClose() }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [onClose])

  const save = async () => {
    const c = code.trim(), l = label.trim()
    if (!c) { setError('Kod zorunlu'); return }
    if (!l) { setError('Etiket zorunlu'); return }
    setError(null); setSaving(true)
    try {
      const r = await fetch('/Integrations/api/doc-catalog/providers/save', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'same-origin',
        body: JSON.stringify({
          id: matched?.id || null,    // null => INSERT, dolu => UPDATE
          code: c, label: l,
          description: desc.trim() || null,
          iconColor: color,
          sortOrder: matched?.sortOrder ?? 100,
          isActive: true,
        }),
      })
      const d = await r.json()
      if (!d || !d.success) { setError(d?.error || 'Kayıt başarısız'); return }
      onSaved(c)
    } catch (ex) {
      setError('Sunucu hatası: ' + (ex?.message || ex))
    } finally {
      setSaving(false)
    }
  }

  const del = async () => {
    if (!matched?.id) return
    setDeleting(true); setError(null)
    try {
      const r = await fetch(`/Integrations/api/doc-catalog/providers/delete/${matched.id}`,
                            { method: 'POST', credentials: 'same-origin' })
      const d = await r.json()
      if (!d || !d.success) { setError(d?.error || 'Silinemedi'); return }
      onDeleted(matched.code)
    } catch (ex) {
      setError('Sunucu hatası: ' + (ex?.message || ex))
    } finally {
      setDeleting(false)
    }
  }

  const overlay = {
    position: 'fixed', inset: 0, zIndex: 9990,
    background: 'rgba(0,0,0,.55)', backdropFilter: 'blur(4px)',
    display: 'flex', alignItems: 'center', justifyContent: 'center',
  }
  const modal = {
    width: '92%', maxWidth: 460,
    background: 'var(--iw-surface, #0d1323)',
    border: '1px solid var(--iw-border, #1e293b)',
    borderRadius: 14, padding: '22px 24px',
    boxShadow: '0 20px 60px rgba(0,0,0,.3)',
    color: 'var(--iw-text, #e2e8f0)',
  }
  const row = { display: 'grid', gridTemplateColumns: '110px 1fr', gap: '8px 12px', alignItems: 'center', marginBottom: 10 }
  const lbl = { fontSize: 12, color: 'var(--iw-muted, #94a3b8)', fontWeight: 600 }
  const inp = {
    padding: '7px 10px', borderRadius: 6,
    background: 'var(--iw-surface-2, #0a1020)',
    border: '1px solid var(--iw-border, #1e293b)',
    color: 'var(--iw-text, #e2e8f0)', fontSize: 12.5, outline: 'none',
  }
  const btn = (variant) => ({
    padding: '7px 16px', borderRadius: 7, fontSize: 12.5, fontWeight: 600,
    cursor: saving ? 'not-allowed' : 'pointer',
    border: '1px solid var(--iw-border, #1e293b)',
    background: variant === 'primary' ? '#6366f1' : 'transparent',
    borderColor: variant === 'primary' ? '#4f46e5' : 'var(--iw-border, #1e293b)',
    color: variant === 'primary' ? '#fff' : 'var(--iw-muted, #94a3b8)',
    opacity: saving ? .6 : 1,
  })

  const busy = saving || deleting
  const dangerBtn = {
    padding: '7px 16px', borderRadius: 7, fontSize: 12.5, fontWeight: 600,
    cursor: busy ? 'not-allowed' : 'pointer',
    border: '1px solid rgba(239,68,68,.35)', background: 'transparent', color: '#ef4444',
    opacity: busy ? .6 : 1,
  }

  return (
    <div style={overlay} onClick={(e) => { if (e.target === e.currentTarget && !busy) onClose() }} role="dialog" aria-modal="true">
      <div style={modal}>
        <h2 style={{ margin: '0 0 4px', fontSize: 16, fontWeight: 700 }}>
          {matched ? `Provider Düzenle — ${matched.label}` : 'Yeni Provider Ekle'}
        </h2>
        <div style={{ fontSize: 11.5, color: 'var(--iw-muted, #94a3b8)', marginBottom: 16 }}>
          {matched
            ? `"${matched.code}" mevcut. Etiket / İkon Rengi / Açıklama değiştirilebilir. "Sil" pasife alır (enum/field-doc kayıtları etkilenmez).`
            : 'Yeni ERP / API kaynağı (ör. SAP, Logo, Custom REST). Mevcut bir Kod yazarsanız düzenleme moduna geçer.'}
        </div>
        <div style={row}>
          <label style={lbl} htmlFor="pmCode">Kod *</label>
          <input id="pmCode" style={{ ...inp, fontFamily: 'ui-monospace,Menlo,Consolas,monospace' }}
                 value={code} onChange={e => setCode(e.target.value)}
                 placeholder="SAP" maxLength={50} autoFocus disabled={busy} />
        </div>
        <div style={row}>
          <label style={lbl} htmlFor="pmLabel">Etiket *</label>
          <input id="pmLabel" style={inp} value={label} onChange={e => setLabel(e.target.value)}
                 placeholder="SAP S/4HANA" maxLength={120} disabled={busy} />
        </div>
        <div style={row}>
          <label style={lbl} htmlFor="pmColor">İkon Rengi</label>
          <select id="pmColor" style={inp} value={color} onChange={e => setColor(e.target.value)} disabled={busy}>
            <option value="indigo">indigo (mor)</option>
            <option value="emerald">emerald (yeşil)</option>
            <option value="blue">blue (mavi)</option>
            <option value="amber">amber (turuncu)</option>
            <option value="rose">rose (kırmızı)</option>
            <option value="violet">violet (mor)</option>
            <option value="slate">slate (gri)</option>
          </select>
        </div>
        <div style={row}>
          <label style={lbl} htmlFor="pmDesc">Açıklama</label>
          <input id="pmDesc" style={inp} value={desc} onChange={e => setDesc(e.target.value)}
                 placeholder="SAP REST entegrasyonu" maxLength={500} disabled={busy} />
        </div>
        {error && (
          <div style={{
            marginTop: 10, padding: '8px 12px', borderRadius: 6,
            background: 'rgba(239,68,68,.12)', color: '#fca5a5',
            border: '1px solid rgba(239,68,68,.3)', fontSize: 12,
          }}>{error}</div>
        )}
        {confirmDel && (
          <div style={{
            marginTop: 10, padding: '10px 12px', borderRadius: 6,
            background: 'rgba(239,68,68,.08)', color: '#fca5a5',
            border: '1px dashed rgba(239,68,68,.4)', fontSize: 12,
            display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8,
          }}>
            <span>"<strong>{matched?.code}</strong>" silinsin mi? Bu provider'ı kullanan enum/profile kayıtları bozulmaz, sadece dropdown'dan kaybolur.</span>
            <span style={{ display: 'flex', gap: 6, flexShrink: 0 }}>
              <button type="button" style={{ ...btn('ghost'), padding: '4px 10px', fontSize: 11 }}
                      onClick={() => setConfirmDel(false)} disabled={busy}>İptal</button>
              <button type="button" style={{ ...dangerBtn, padding: '4px 10px', fontSize: 11 }}
                      onClick={del} disabled={busy}>{deleting ? '…' : 'Evet, sil'}</button>
            </span>
          </div>
        )}
        <div style={{ display: 'flex', gap: 8, justifyContent: 'space-between', marginTop: 16 }}>
          <div>
            {matched && !confirmDel && (
              <button type="button" style={dangerBtn} onClick={() => setConfirmDel(true)} disabled={busy}>Sil</button>
            )}
          </div>
          <div style={{ display: 'flex', gap: 8 }}>
            <button type="button" style={btn('ghost')} onClick={onClose} disabled={busy}>Vazgeç</button>
            <button type="button" style={btn('primary')} onClick={save} disabled={busy}>
              {saving ? 'Kaydediliyor…' : (matched ? 'Güncelle' : 'Kaydet')}
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}
