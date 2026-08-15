import React from 'react'
import { Server, Plus, Search, ArrowLeft, Plug, Pencil, Power, Trash2, Loader2, CheckCircle2, XCircle } from 'lucide-react'
import { apiGet, apiPost } from './dbiApi'
import DbiConfirm from './DbiConfirm'
import './DbImport.css'

/*
 * DbImportConnections — harici SQL Server kaynak bağlantıları (liste + düzenleme).
 *
 * Parola İSTEMCİYE HİÇ DÖNMEZ (DTO'da yalnız hasPassword bayrağı var). Düzenlemede
 * parola alanı boş bırakılırsa mevcut parola korunur — sunucu tarafı da bu semantiği
 * uygular (SqlExternalDbConnectionRepository.SaveAsync).
 */

const EMPTY = {
  id: 0,
  name: '',
  serverName: '',
  databaseName: '',
  authMode: 'Sql',
  username: '',
  password: '',
  encrypt: true,
  trustServerCertificate: true,
  connectTimeoutSeconds: 15,
  commandTimeoutSeconds: 120,
  isActive: true,
  hasPassword: false,
}

function Switch({ checked, onChange, label }) {
  return (
    <label className="dbi-switch">
      <input type="checkbox" checked={!!checked} onChange={(e) => onChange(e.target.checked)} />
      <span className="dbi-switch-track"><span className="dbi-switch-thumb" /></span>
      <span>{label}</span>
    </label>
  )
}

export default function DbImportConnections() {
  const [items, setItems] = React.useState([])
  const [loading, setLoading] = React.useState(true)
  const [error, setError] = React.useState(null)
  const [search, setSearch] = React.useState('')

  const [editing, setEditing] = React.useState(null)      // form state veya null
  const [saving, setSaving] = React.useState(false)
  const [testing, setTesting] = React.useState(false)
  const [testResult, setTestResult] = React.useState(null)
  const [confirmTarget, setConfirmTarget] = React.useState(null)

  const load = React.useCallback(async () => {
    setLoading(true)
    try {
      const res = await apiGet('/DbImport/api/connections?includeInactive=true')
      if (res && res.success) { setItems(res.items || []); setError(null) }
      else setError((res && res.error) || 'Bağlantılar yüklenemedi.')
    } catch (_) {
      setError('Bağlantılar yüklenemedi.')
    } finally {
      setLoading(false)
    }
  }, [])

  React.useEffect(() => { load() }, [load])

  const filtered = React.useMemo(() => {
    const q = search.trim().toLocaleLowerCase('tr')
    if (!q) return items
    return items.filter((c) =>
      [c.name, c.serverName, c.databaseName, c.username]
        .filter(Boolean)
        .some((v) => String(v).toLocaleLowerCase('tr').includes(q)))
  }, [items, search])

  function openNew() { setTestResult(null); setEditing({ ...EMPTY }) }
  function openEdit(c) {
    setTestResult(null)
    setEditing({ ...EMPTY, ...c, password: '' })
  }

  async function save() {
    if (!editing) return
    setSaving(true)
    try {
      const res = await apiPost('/DbImport/api/connections/save', editing)
      if (res && res.success) { setEditing(null); await load() }
      else setError((res && res.error) || 'Kaydedilemedi.')
    } catch (_) {
      setError('Kaydedilemedi.')
    } finally {
      setSaving(false)
    }
  }

  async function testDraft() {
    if (!editing) return
    setTesting(true); setTestResult(null)
    try {
      const res = await apiPost('/DbImport/api/connections/test-draft', editing)
      setTestResult(res || { success: false, error: 'Sonuç alınamadı.' })
    } catch (_) {
      setTestResult({ success: false, error: 'Test yapılamadı.' })
    } finally {
      setTesting(false)
    }
  }

  async function testSaved(id) {
    setError(null)
    const res = await apiPost(`/DbImport/api/connections/test/${id}`)
    if (res && res.success) setError(null)
    setTestResult({ ...(res || {}), forId: id })
  }

  async function toggle(id) {
    await apiPost(`/DbImport/api/connections/toggle/${id}`)
    await load()
  }

  async function doDelete() {
    const target = confirmTarget
    setConfirmTarget(null)
    if (!target) return
    const res = await apiPost(`/DbImport/api/connections/delete/${target.id}`)
    if (res && res.success) await load()
    else setError((res && res.error) || 'Silinemedi.')
  }

  const isIntegrated = editing && editing.authMode === 'Integrated'
  const canSave = editing
    && editing.name.trim()
    && editing.serverName.trim()
    && editing.databaseName.trim()
    && (isIntegrated || editing.username.trim())
    && (isIntegrated || editing.id > 0 || editing.password.trim())

  return (
    <div className="dbi-root">
      <div className="dbi-header">
        <div className="dbi-header-icon"><Server size={19} /></div>
        <div>
          <div className="dbi-header-title">Kaynak Bağlantıları</div>
          <div className="dbi-header-sub">{items.length} harici veritabanı</div>
        </div>
        <div className="dbi-header-spacer" />
        <div className="dbi-search">
          <Search size={14} />
          <input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Bağlantı ara…" />
        </div>
        <a className="dbi-btn" href="/DbImport"><ArrowLeft size={14} /> Aktarım İşleri</a>
        <button type="button" className="dbi-btn dbi-btn--primary" onClick={openNew}><Plus size={14} /> Yeni Bağlantı</button>
      </div>

      <div className="dbi-body">
        {error && <div className="dbi-alert dbi-alert--err">{error}</div>}

        {testResult && testResult.forId && (
          <div className={`dbi-alert ${testResult.success ? 'dbi-alert--ok' : 'dbi-alert--err'}`}>
            {testResult.success
              ? `Bağlantı başarılı${testResult.elapsedMs != null ? ` (${testResult.elapsedMs} ms)` : ''}. ${testResult.serverVersion || ''}`
              : `Bağlantı başarısız: ${testResult.error || ''}`}
          </div>
        )}

        {loading && <div className="dbi-empty"><Loader2 size={18} className="dbi-spin" /> Yükleniyor…</div>}

        {!loading && filtered.length === 0 && (
          <div className="dbi-empty">
            {items.length === 0
              ? 'Henüz kaynak bağlantısı yok — sağ üstten “Yeni Bağlantı” ile ekleyin.'
              : 'Aramayla eşleşen bağlantı yok.'}
          </div>
        )}

        {!loading && filtered.length > 0 && (
          <div className="dbi-list">
            {filtered.map((c) => (
              <div className="dbi-row" key={c.id}>
                <div className="dbi-row-main">
                  <div className="dbi-row-title">
                    {c.name}{' '}
                    <span className={`dbi-badge ${c.isActive ? 'dbi-badge--ok' : 'dbi-badge--off'}`}>
                      {c.isActive ? 'Aktif' : 'Pasif'}
                    </span>
                  </div>
                  <div className="dbi-row-sub dbi-mono">
                    {c.serverName} · {c.databaseName} ·{' '}
                    {c.authMode === 'Integrated' ? 'Windows kimlik doğrulama' : `SQL · ${c.username || ''}`}
                    {c.encrypt ? ' · TLS' : ''}
                  </div>
                </div>
                <div className="dbi-row-actions">
                  <button type="button" className="dbi-btn dbi-btn--xs" onClick={() => testSaved(c.id)}><Plug size={13} /> Test</button>
                  <button type="button" className="dbi-btn dbi-btn--xs" onClick={() => openEdit(c)}><Pencil size={13} /> Düzenle</button>
                  <button type="button" className="dbi-btn dbi-btn--xs" onClick={() => toggle(c.id)}><Power size={13} /> {c.isActive ? 'Pasifleştir' : 'Aktifleştir'}</button>
                  <button type="button" className="dbi-btn dbi-btn--xs dbi-btn--danger" onClick={() => setConfirmTarget(c)}><Trash2 size={13} /></button>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>

      {editing && (
        <div className="dbi-modal-backdrop" onMouseDown={(e) => { if (e.target === e.currentTarget) setEditing(null) }}>
          <div className="dbi-modal" role="dialog" aria-modal="true">
            <div className="dbi-modal-head">{editing.id > 0 ? 'Bağlantıyı Düzenle' : 'Yeni Bağlantı'}</div>
            <div className="dbi-modal-body">
              <div className="dbi-field">
                <span className="dbi-label">Bağlantı Adı <span className="dbi-required">*</span></span>
                <input className="dbi-input" value={editing.name}
                       onChange={(e) => setEditing({ ...editing, name: e.target.value })}
                       placeholder="Örn. Netsis Canlı" />
              </div>

              <div className="dbi-grid">
                <div className="dbi-field">
                  <span className="dbi-label">Sunucu <span className="dbi-required">*</span></span>
                  <input className="dbi-input dbi-mono" value={editing.serverName}
                         onChange={(e) => setEditing({ ...editing, serverName: e.target.value })}
                         placeholder="SRV01\SQLEXPRESS" />
                </div>
                <div className="dbi-field">
                  <span className="dbi-label">Veritabanı <span className="dbi-required">*</span></span>
                  <input className="dbi-input dbi-mono" value={editing.databaseName}
                         onChange={(e) => setEditing({ ...editing, databaseName: e.target.value })} />
                </div>
              </div>

              <div className="dbi-field">
                <span className="dbi-label">Kimlik Doğrulama</span>
                <select className="dbi-select" value={editing.authMode}
                        onChange={(e) => setEditing({ ...editing, authMode: e.target.value })}>
                  <option value="Sql">SQL Server (kullanıcı adı + parola)</option>
                  <option value="Integrated">Windows (uygulama servis hesabı)</option>
                </select>
              </div>

              {!isIntegrated && (
                <div className="dbi-grid">
                  <div className="dbi-field">
                    <span className="dbi-label">Kullanıcı Adı <span className="dbi-required">*</span></span>
                    <input className="dbi-input" value={editing.username}
                           onChange={(e) => setEditing({ ...editing, username: e.target.value })} />
                  </div>
                  <div className="dbi-field">
                    <span className="dbi-label">
                      Parola {editing.id > 0 ? '' : <span className="dbi-required">*</span>}
                    </span>
                    <input className="dbi-input" type="password" value={editing.password}
                           onChange={(e) => setEditing({ ...editing, password: e.target.value })}
                           placeholder={editing.hasPassword ? '••••••  (değiştirmek için yazın)' : ''} />
                    {editing.id > 0 && <span className="dbi-hint">Boş bırakılırsa değişmez.</span>}
                  </div>
                </div>
              )}

              <div className="dbi-card" style={{ marginTop: 4 }}>
                <div className="dbi-card-title">Bağlantı Seçenekleri</div>
                <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
                  <Switch checked={editing.encrypt} label="Şifreli Bağlantı (TLS)"
                          onChange={(v) => setEditing({ ...editing, encrypt: v })} />
                  <Switch checked={editing.trustServerCertificate} label="Sunucu Sertifikasına Güven"
                          onChange={(v) => setEditing({ ...editing, trustServerCertificate: v })} />
                  <Switch checked={editing.isActive} label="Aktif"
                          onChange={(v) => setEditing({ ...editing, isActive: v })} />
                </div>
                <div className="dbi-grid" style={{ marginTop: 12 }}>
                  <div className="dbi-field">
                    <span className="dbi-label">Bağlantı Zaman Aşımı (sn)</span>
                    <input className="dbi-input" type="number" min="1" value={editing.connectTimeoutSeconds}
                           onChange={(e) => setEditing({ ...editing, connectTimeoutSeconds: Number(e.target.value) || 15 })} />
                  </div>
                  <div className="dbi-field">
                    <span className="dbi-label">Sorgu Zaman Aşımı (sn)</span>
                    <input className="dbi-input" type="number" min="1" value={editing.commandTimeoutSeconds}
                           onChange={(e) => setEditing({ ...editing, commandTimeoutSeconds: Number(e.target.value) || 120 })} />
                  </div>
                </div>
              </div>

              {testResult && !testResult.forId && (
                <div className={`dbi-alert ${testResult.success ? 'dbi-alert--ok' : 'dbi-alert--err'}`}>
                  {testResult.success
                    ? <><CheckCircle2 size={13} /> Bağlantı başarılı{testResult.elapsedMs != null ? ` (${testResult.elapsedMs} ms)` : ''}. <span className="dbi-mono">{testResult.serverVersion || ''}</span></>
                    : <><XCircle size={13} /> {testResult.error || 'Bağlantı kurulamadı.'}</>}
                </div>
              )}
            </div>
            <div className="dbi-modal-foot">
              <button type="button" className="dbi-btn" onClick={testDraft} disabled={testing || !canSave}>
                {testing ? <Loader2 size={14} /> : <Plug size={14} />} Bağlantıyı Test Et
              </button>
              <div className="dbi-header-spacer" />
              <button type="button" className="dbi-btn dbi-btn--ghost" onClick={() => setEditing(null)}>Vazgeç</button>
              <button type="button" className="dbi-btn dbi-btn--primary" onClick={save} disabled={!canSave || saving}>
                {saving ? 'Kaydediliyor…' : 'Kaydet'}
              </button>
            </div>
          </div>
        </div>
      )}

      <DbiConfirm
        open={!!confirmTarget}
        title="Bağlantı Silinsin mi?"
        message={confirmTarget
          ? `'${confirmTarget.name}' bağlantısı kalıcı olarak silinecek. Bu bağlantıyı kullanan aktarım işleri çalışamaz hale gelir.`
          : ''}
        onCancel={() => setConfirmTarget(null)}
        onConfirm={doDelete}
      />
    </div>
  )
}
