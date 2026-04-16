/**
 * CompanyUserManagementPanel — Şirket ve Kullanıcı Yönetim Ekranı
 * React tabanlı, koyu/açık tema desteği (CSS değişkenleri aracılığıyla)
 */
import { useState, useEffect, useCallback } from 'react'
import {
  Building2, Users, Plus, Pencil, Trash2, X, Check, Loader2,
  Database, Eye, EyeOff, AlertCircle, Search, Wifi, WifiOff,
  RefreshCw, Shield, ServerCrash
} from 'lucide-react'
import {
  getCompaniesJson, saveCompanyJson, deactivateCompanyJson,
  getUsersJson, saveUserJson, deactivateUserJson, testConnectionJson
} from '../../services/companyUserService'

// sqlPassword: '' = değiştirilmedi (sunucu mevcut şifreyi korur), hasPassword: mevcut şifre var mı
var EMPTY_COMPANY = { id: null, name: '', sqlServer: '', sqlDatabase: '', sqlUsername: '', sqlPassword: '', hasPassword: false, isActive: true }
var EMPTY_USER    = { id: null, companyId: null, firstName: '', lastName: '', email: '', password: '', isActive: true }

export default function CompanyUserManagementPanel() {

  // ── Tab ────────────────────────────────────────────────────
  var [activeTab, setActiveTab] = useState('companies')

  // ── Company state ──────────────────────────────────────────
  var [companies,        setCompanies]        = useState([])
  var [compLoading,      setCompLoading]      = useState(true)
  var [compError,        setCompError]        = useState(null)
  var [compSearch,       setCompSearch]       = useState('')
  var [editingComp,      setEditingComp]      = useState(null)   // null=kapalı, 'new', veya id
  var [compForm,         setCompForm]         = useState(EMPTY_COMPANY)
  var [compSaving,       setCompSaving]       = useState(false)
  var [compFormErr,      setCompFormErr]      = useState(null)
  var [connTesting,      setConnTesting]      = useState(false)
  var [connResult,       setConnResult]       = useState(null)
  var [showPwd,          setShowPwd]          = useState(false)
  var [confirmDelComp,   setConfirmDelComp]   = useState(null)

  // ── User state ─────────────────────────────────────────────
  var [users,            setUsers]            = useState([])
  var [userLoading,      setUserLoading]      = useState(true)
  var [userError,        setUserError]        = useState(null)
  var [userSearch,       setUserSearch]       = useState('')
  var [editingUser,      setEditingUser]      = useState(null)
  var [userForm,         setUserForm]         = useState(EMPTY_USER)
  var [userSaving,       setUserSaving]       = useState(false)
  var [userFormErr,      setUserFormErr]      = useState(null)
  var [showUserPwd,      setShowUserPwd]      = useState(false)
  var [confirmDelUser,   setConfirmDelUser]   = useState(null)

  // ── Toast ──────────────────────────────────────────────────
  var [toast, setToast] = useState(null)

  function showToast(type, msg) {
    setToast({ type: type, msg: msg })
    setTimeout(function () { setToast(null) }, 3000)
  }

  // ── Load ───────────────────────────────────────────────────
  var loadCompanies = useCallback(function () {
    setCompLoading(true); setCompError(null)
    getCompaniesJson()
      .then(function (d) { setCompanies(Array.isArray(d) ? d : []) })
      .catch(function (e) { setCompError('Şirketler yüklenemedi: ' + e.message) })
      .finally(function () { setCompLoading(false) })
  }, [])

  var loadUsers = useCallback(function () {
    setUserLoading(true); setUserError(null)
    getUsersJson()
      .then(function (d) { setUsers(Array.isArray(d) ? d : []) })
      .catch(function (e) { setUserError('Kullanıcılar yüklenemedi: ' + e.message) })
      .finally(function () { setUserLoading(false) })
  }, [])

  useEffect(function () { loadCompanies() }, [loadCompanies])
  useEffect(function () { loadUsers() }, [loadUsers])

  // ── Company handlers ───────────────────────────────────────
  function handleNewComp() {
    setEditingComp('new'); setCompForm(EMPTY_COMPANY)
    setCompFormErr(null); setConnResult(null); setShowPwd(false)
  }

  function handleEditComp(c) {
    setEditingComp(c.id)
    setCompForm({ id: c.id, name: c.name, sqlServer: c.sqlServer || '',
      sqlDatabase: c.sqlDatabase || '', sqlUsername: c.sqlUsername || '',
      sqlPassword: '',              // Şifre asla sunucudan gelmez — değiştirilmek istenirse yeni girilir
      hasPassword: !!c.hasPassword, // Sunucu "şifre var" bilgisini gönderir
      isActive: c.isActive })
    setCompFormErr(null); setConnResult(null); setShowPwd(false)
  }

  function handleCancelComp() {
    setEditingComp(null); setCompFormErr(null); setConnResult(null)
  }

  async function handleSaveComp(e) {
    e.preventDefault()
    if (!compForm.name.trim()) { setCompFormErr('Şirket adı zorunludur.'); return }
    setCompSaving(true); setCompFormErr(null)
    var result = await saveCompanyJson(compForm)
    setCompSaving(false)
    if (result.success) {
      showToast('success', 'Şirket kaydedildi.')
      setEditingComp(null); loadCompanies(); loadUsers()
    } else {
      setCompFormErr(result.message || 'Kaydetme başarısız.')
    }
  }

  async function handleDeactivateComp() {
    var id = confirmDelComp; setConfirmDelComp(null)
    var result = await deactivateCompanyJson(id)
    if (result && result.success !== false) {
      showToast('success', 'Şirket pasife alındı.')
      if (editingComp === id) setEditingComp(null)
      loadCompanies()
    } else {
      showToast('error', (result && result.message) || 'İşlem başarısız.')
    }
  }

  async function handleTestConn() {
    if (!compForm.sqlServer.trim()) { setConnResult({ success: false, message: 'SQL Sunucu zorunludur.' }); return }
    setConnTesting(true); setConnResult(null)
    var r = await testConnectionJson({
      id:       compForm.id,
      server:   compForm.sqlServer,
      database: compForm.sqlDatabase,
      username: compForm.sqlUsername,
      password: compForm.sqlPassword   // Boşsa sunucu kayıttaki şifreyi kullanır
    })
    setConnTesting(false); setConnResult(r)
  }

  // ── User handlers ──────────────────────────────────────────
  function handleNewUser() {
    setEditingUser('new'); setUserForm(EMPTY_USER)
    setUserFormErr(null); setShowUserPwd(false)
  }

  function handleCancelUser() { setEditingUser(null); setUserFormErr(null) }

  async function handleSaveUser(e) {
    e.preventDefault()
    if (!userForm.companyId) { setUserFormErr('Şirket seçimi zorunludur.'); return }
    if (!userForm.firstName.trim()) { setUserFormErr('Ad zorunludur.'); return }
    if (!userForm.email.trim()) { setUserFormErr('E-posta zorunludur.'); return }
    if (!userForm.password || userForm.password.length < 8) { setUserFormErr('Şifre en az 8 karakter olmalıdır.'); return }
    setUserSaving(true); setUserFormErr(null)
    var result = await saveUserJson(userForm)
    setUserSaving(false)
    if (result.success) {
      showToast('success', 'Kullanıcı oluşturuldu.')
      setEditingUser(null); loadUsers()
    } else {
      setUserFormErr(result.message || 'Kaydetme başarısız.')
    }
  }

  async function handleDeactivateUser() {
    var id = confirmDelUser; setConfirmDelUser(null)
    var result = await deactivateUserJson(id)
    if (result && result.success !== false) {
      showToast('success', 'Kullanıcı pasife alındı.')
      loadUsers()
    } else {
      showToast('error', (result && result.message) || 'İşlem başarısız.')
    }
  }

  // ── Filtered ───────────────────────────────────────────────
  var filteredComp = companies.filter(function (c) {
    if (!compSearch.trim()) return true
    var q = compSearch.toLowerCase()
    return (c.name || '').toLowerCase().includes(q)
  })

  var filteredUsers = users.filter(function (u) {
    if (!userSearch.trim()) return true
    var q = userSearch.toLowerCase()
    return (u.fullName || '').toLowerCase().includes(q) ||
           (u.email || '').toLowerCase().includes(q) ||
           (u.companyName || '').toLowerCase().includes(q)
  })

  // ── Render ─────────────────────────────────────────────────
  return (
    <div className="cum-root">

      {/* Toast */}
      {toast && (
        <div className={'cum-toast cum-toast--' + toast.type}>
          {toast.type === 'success' ? <Check size={14} /> : <AlertCircle size={14} />}
          <span>{toast.msg}</span>
        </div>
      )}

      {/* Confirm delete company */}
      {confirmDelComp && (
        <div className="cum-overlay">
          <div className="cum-confirm-box">
            <AlertCircle size={22} className="cum-confirm-ico" />
            <p>Bu şirket <strong>pasife</strong> alınacak. Devam edilsin mi?</p>
            <div className="cum-confirm-btns">
              <button type="button" className="cum-btn cum-btn--danger" onClick={handleDeactivateComp}>Pasife Al</button>
              <button type="button" className="cum-btn cum-btn--ghost" onClick={function () { setConfirmDelComp(null) }}>İptal</button>
            </div>
          </div>
        </div>
      )}

      {/* Confirm delete user */}
      {confirmDelUser && (
        <div className="cum-overlay">
          <div className="cum-confirm-box">
            <AlertCircle size={22} className="cum-confirm-ico" />
            <p>Bu kullanıcı <strong>pasife</strong> alınacak. Devam edilsin mi?</p>
            <div className="cum-confirm-btns">
              <button type="button" className="cum-btn cum-btn--danger" onClick={handleDeactivateUser}>Pasife Al</button>
              <button type="button" className="cum-btn cum-btn--ghost" onClick={function () { setConfirmDelUser(null) }}>İptal</button>
            </div>
          </div>
        </div>
      )}

      {/* ── Tab bar ── */}
      <div className="cum-tabs">
        <button type="button" className={'cum-tab' + (activeTab === 'companies' ? ' is-active' : '')}
          onClick={function () { setActiveTab('companies') }}>
          <Building2 size={14} />
          <span>Şirketler</span>
          {companies.length > 0 && <span className="cum-tab-badge">{companies.length}</span>}
        </button>
        <button type="button" className={'cum-tab' + (activeTab === 'users' ? ' is-active' : '')}
          onClick={function () { setActiveTab('users') }}>
          <Users size={14} />
          <span>Kullanıcılar</span>
          {users.length > 0 && <span className="cum-tab-badge">{users.length}</span>}
        </button>
      </div>

      {/* ═══ COMPANIES ═══ */}
      {activeTab === 'companies' && (
        <div className="cum-body">

          {/* List panel */}
          <div className={'cum-list-panel' + (editingComp !== null ? ' cum-list-panel--narrow' : '')}>
            <div className="cum-toolbar">
              <button type="button" className="cum-btn cum-btn--primary" onClick={handleNewComp}>
                <Plus size={14} /> Yeni Şirket
              </button>
              <div className="cum-search-wrap">
                <Search size={13} className="cum-search-ico" />
                <input type="search" className="cum-search" placeholder="Şirket ara..."
                  value={compSearch} onChange={function (e) { setCompSearch(e.target.value) }} />
                {compSearch && <button type="button" className="cum-search-clear" onClick={function () { setCompSearch('') }}>×</button>}
              </div>
              <button type="button" className="cum-btn cum-btn--ghost" onClick={loadCompanies} title="Yenile">
                <RefreshCw size={14} className={compLoading ? 'cum-spin' : ''} />
              </button>
            </div>

            {compError && (
              <div className="cum-alert">
                <AlertCircle size={14} /> {compError}
                <button type="button" onClick={function () { setCompError(null) }}>×</button>
              </div>
            )}

            {compLoading ? (
              <div className="cum-center"><Loader2 size={26} className="cum-spin" /></div>
            ) : filteredComp.length === 0 ? (
              <div className="cum-empty">
                <Building2 size={42} />
                <p>{compSearch ? 'Eşleşen şirket bulunamadı.' : 'Henüz şirket tanımlanmamış.'}</p>
                {!compSearch && (
                  <button type="button" className="cum-btn cum-btn--primary" onClick={handleNewComp}>
                    <Plus size={14} /> İlk Şirketi Ekle
                  </button>
                )}
              </div>
            ) : (
              <div className="cum-table-wrap">
                <table className="cum-table">
                  <thead>
                    <tr>
                      <th>Şirket Adı</th>
                      <th>SQL Bağlantısı</th>
                      <th>Durum</th>
                      <th></th>
                    </tr>
                  </thead>
                  <tbody>
                    {filteredComp.map(function (c) {
                      return (
                        <tr key={c.id} className={editingComp === c.id ? 'is-selected' : ''}>
                          <td className="cum-td-name">
                            <Building2 size={12} style={{ opacity: 0.45, marginRight: 6, flexShrink: 0 }} />
                            {c.name}
                          </td>
                          <td>
                            {c.sqlServer
                              ? <span className="cum-pill cum-pill--blue"><Database size={11} /> {c.sqlServer}</span>
                              : <span className="cum-pill cum-pill--gray">Sistem DB</span>
                            }
                          </td>
                          <td>
                            <span className={'cum-pill ' + (c.isActive ? 'cum-pill--green' : 'cum-pill--red')}>
                              {c.isActive ? 'Aktif' : 'Pasif'}
                            </span>
                          </td>
                          <td className="cum-td-actions">
                            <button type="button" className="cum-action-btn" onClick={function () { handleEditComp(c) }} title="Düzenle">
                              <Pencil size={13} />
                            </button>
                            <button type="button" className="cum-action-btn cum-action-btn--danger"
                              onClick={function () { setConfirmDelComp(c.id) }} title="Pasife Al">
                              <Trash2 size={13} />
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

          {/* Form panel */}
          {editingComp !== null && (
            <div className="cum-form-panel">
              <div className="cum-form-header">
                <h3 className="cum-form-title">
                  <Building2 size={16} />
                  {editingComp === 'new' ? 'Yeni Şirket' : 'Şirket Düzenle'}
                </h3>
                <button type="button" className="cum-close-btn" onClick={handleCancelComp}><X size={16} /></button>
              </div>

              <form className="cum-form" onSubmit={handleSaveComp} autoComplete="off">
                <div className="cum-field">
                  <label className="cum-label">Şirket Adı <span className="cum-req">*</span></label>
                  <input type="text" className="cum-input" value={compForm.name} required
                    onChange={function (e) { setCompForm(Object.assign({}, compForm, { name: e.target.value })) }}
                    placeholder="Şirket adını girin" />
                </div>

                <div className="cum-field">
                  <label className="cum-label">Durum</label>
                  <label className="cum-switch-wrap">
                    <input type="checkbox" className="cum-switch-input" checked={compForm.isActive}
                      onChange={function (e) { setCompForm(Object.assign({}, compForm, { isActive: e.target.checked })) }} />
                    <span className={'cum-switch-track' + (compForm.isActive ? ' is-on' : '')}>
                      <span className="cum-switch-thumb"></span>
                    </span>
                    <span className={'cum-switch-label' + (compForm.isActive ? ' is-active' : '')}>
                      {compForm.isActive ? 'Aktif' : 'Pasif'}
                    </span>
                  </label>
                </div>

                <div className="cum-section-sep">
                  <Database size={12} /> SQL Bağlantısı
                </div>

                <div className="cum-field">
                  <label className="cum-label">SQL Sunucu</label>
                  <input type="text" className="cum-input" value={compForm.sqlServer}
                    onChange={function (e) { setCompForm(Object.assign({}, compForm, { sqlServer: e.target.value })); setConnResult(null) }}
                    placeholder="localhost\SQLEXPRESS" autoComplete="off" />
                </div>

                <div className="cum-field">
                  <label className="cum-label">Veritabanı</label>
                  <input type="text" className="cum-input" value={compForm.sqlDatabase}
                    onChange={function (e) { setCompForm(Object.assign({}, compForm, { sqlDatabase: e.target.value })); setConnResult(null) }}
                    placeholder="CalibraDB" autoComplete="off" />
                </div>

                <div className="cum-field">
                  <label className="cum-label">DB Kullanıcı</label>
                  <input type="text" className="cum-input" value={compForm.sqlUsername}
                    onChange={function (e) { setCompForm(Object.assign({}, compForm, { sqlUsername: e.target.value })); setConnResult(null) }}
                    autoComplete="off" />
                </div>

                <div className="cum-field">
                  <label className="cum-label">
                    DB Şifre
                    {compForm.hasPassword && !compForm.sqlPassword &&
                      <span className="cum-field-hint"> — boş bırakılırsa değiştirilmez</span>}
                  </label>
                  <div className="cum-pw-wrap">
                    <input type={showPwd ? 'text' : 'password'} className="cum-input cum-input--pw"
                      value={compForm.sqlPassword}
                      onChange={function (e) { setCompForm(Object.assign({}, compForm, { sqlPassword: e.target.value })); setConnResult(null) }}
                      placeholder={compForm.hasPassword ? '••••••••  (değiştirilmeyecek)' : 'Şifre girin'}
                      autoComplete="new-password" />
                    <button type="button" className="cum-eye-btn" onClick={function () { setShowPwd(!showPwd) }}>
                      {showPwd ? <EyeOff size={13} /> : <Eye size={13} />}
                    </button>
                  </div>
                </div>

                <div className="cum-test-row">
                  <button type="button" className="cum-btn cum-btn--secondary" onClick={handleTestConn} disabled={connTesting}>
                    {connTesting ? <Loader2 size={13} className="cum-spin" /> : <Wifi size={13} />}
                    {connTesting ? 'Test ediliyor…' : 'Bağlantı Test'}
                  </button>
                  {connResult && (
                    <span className={'cum-conn-result' + (connResult.success ? ' is-ok' : ' is-fail')}>
                      {connResult.success ? <Check size={12} /> : <WifiOff size={12} />}
                      {connResult.message}
                    </span>
                  )}
                </div>

                {compFormErr && (
                  <div className="cum-form-err">
                    <AlertCircle size={13} /> {compFormErr}
                  </div>
                )}

                <div className="cum-form-footer">
                  <button type="submit" className="cum-btn cum-btn--primary" disabled={compSaving}>
                    {compSaving ? <Loader2 size={13} className="cum-spin" /> : <Check size={13} />}
                    {compSaving ? 'Kaydediliyor…' : 'Kaydet'}
                  </button>
                  <button type="button" className="cum-btn cum-btn--ghost" onClick={handleCancelComp}>İptal</button>
                </div>
              </form>
            </div>
          )}
        </div>
      )}

      {/* ═══ USERS ═══ */}
      {activeTab === 'users' && (
        <div className="cum-body">

          {/* List panel */}
          <div className={'cum-list-panel' + (editingUser !== null ? ' cum-list-panel--narrow' : '')}>
            <div className="cum-toolbar">
              <button type="button" className="cum-btn cum-btn--primary" onClick={handleNewUser}>
                <Plus size={14} /> Yeni Kullanıcı
              </button>
              <div className="cum-search-wrap">
                <Search size={13} className="cum-search-ico" />
                <input type="search" className="cum-search" placeholder="Kullanıcı ara..."
                  value={userSearch} onChange={function (e) { setUserSearch(e.target.value) }} />
                {userSearch && <button type="button" className="cum-search-clear" onClick={function () { setUserSearch('') }}>×</button>}
              </div>
              <button type="button" className="cum-btn cum-btn--ghost" onClick={loadUsers} title="Yenile">
                <RefreshCw size={14} className={userLoading ? 'cum-spin' : ''} />
              </button>
            </div>

            {userError && (
              <div className="cum-alert">
                <AlertCircle size={14} /> {userError}
                <button type="button" onClick={function () { setUserError(null) }}>×</button>
              </div>
            )}

            {userLoading ? (
              <div className="cum-center"><Loader2 size={26} className="cum-spin" /></div>
            ) : filteredUsers.length === 0 ? (
              <div className="cum-empty">
                <Users size={42} />
                <p>{userSearch ? 'Eşleşen kullanıcı bulunamadı.' : 'Henüz kullanıcı tanımlanmamış.'}</p>
                {!userSearch && (
                  <button type="button" className="cum-btn cum-btn--primary" onClick={handleNewUser}>
                    <Plus size={14} /> İlk Kullanıcıyı Ekle
                  </button>
                )}
              </div>
            ) : (
              <div className="cum-table-wrap">
                <table className="cum-table">
                  <thead>
                    <tr>
                      <th>Ad Soyad</th>
                      <th>E-posta</th>
                      <th>Şirket</th>
                      <th>Durum</th>
                      <th></th>
                    </tr>
                  </thead>
                  <tbody>
                    {filteredUsers.map(function (u) {
                      return (
                        <tr key={u.id}>
                          <td className="cum-td-name">
                            <Shield size={12} style={{ opacity: 0.4, marginRight: 6, flexShrink: 0 }} />
                            {u.fullName}
                          </td>
                          <td className="cum-td-email">{u.email}</td>
                          <td><span className="cum-pill cum-pill--blue">{u.companyName || '—'}</span></td>
                          <td>
                            <span className={'cum-pill ' + (u.isActive ? 'cum-pill--green' : 'cum-pill--red')}>
                              {u.isActive ? 'Aktif' : 'Pasif'}
                            </span>
                          </td>
                          <td className="cum-td-actions">
                            <button type="button" className="cum-action-btn cum-action-btn--danger"
                              onClick={function () { setConfirmDelUser(u.id) }} title="Pasife Al">
                              <Trash2 size={13} />
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

          {/* Form panel */}
          {editingUser !== null && (
            <div className="cum-form-panel">
              <div className="cum-form-header">
                <h3 className="cum-form-title">
                  <Users size={16} /> Yeni Kullanıcı
                </h3>
                <button type="button" className="cum-close-btn" onClick={handleCancelUser}><X size={16} /></button>
              </div>

              <form className="cum-form" onSubmit={handleSaveUser} autoComplete="off">
                <div className="cum-field">
                  <label className="cum-label">Şirket <span className="cum-req">*</span></label>
                  <select className="cum-select" value={userForm.companyId || ''}
                    onChange={function (e) {
                      setUserForm(Object.assign({}, userForm, { companyId: e.target.value ? parseInt(e.target.value) : null }))
                    }}>
                    <option value="">Seçiniz…</option>
                    {companies.filter(function (c) { return c.isActive }).map(function (c) {
                      return <option key={c.id} value={c.id}>{c.name}</option>
                    })}
                  </select>
                </div>

                <div className="cum-field-row">
                  <div className="cum-field">
                    <label className="cum-label">Ad <span className="cum-req">*</span></label>
                    <input type="text" className="cum-input" value={userForm.firstName}
                      onChange={function (e) { setUserForm(Object.assign({}, userForm, { firstName: e.target.value })) }} />
                  </div>
                  <div className="cum-field">
                    <label className="cum-label">Soyad</label>
                    <input type="text" className="cum-input" value={userForm.lastName}
                      onChange={function (e) { setUserForm(Object.assign({}, userForm, { lastName: e.target.value })) }} />
                  </div>
                </div>

                <div className="cum-field">
                  <label className="cum-label">E-posta <span className="cum-req">*</span></label>
                  <input type="email" className="cum-input" value={userForm.email}
                    onChange={function (e) { setUserForm(Object.assign({}, userForm, { email: e.target.value })) }} />
                </div>

                <div className="cum-field">
                  <label className="cum-label">Şifre <span className="cum-req">*</span></label>
                  <div className="cum-pw-wrap">
                    <input type={showUserPwd ? 'text' : 'password'} className="cum-input cum-input--pw"
                      value={userForm.password}
                      onChange={function (e) { setUserForm(Object.assign({}, userForm, { password: e.target.value })) }}
                      autoComplete="new-password" placeholder="En az 8 karakter" />
                    <button type="button" className="cum-eye-btn" onClick={function () { setShowUserPwd(!showUserPwd) }}>
                      {showUserPwd ? <EyeOff size={13} /> : <Eye size={13} />}
                    </button>
                  </div>
                </div>

                {userFormErr && (
                  <div className="cum-form-err">
                    <AlertCircle size={13} /> {userFormErr}
                  </div>
                )}

                <div className="cum-form-footer">
                  <button type="submit" className="cum-btn cum-btn--primary" disabled={userSaving}>
                    {userSaving ? <Loader2 size={13} className="cum-spin" /> : <Check size={13} />}
                    {userSaving ? 'Oluşturuluyor…' : 'Oluştur'}
                  </button>
                  <button type="button" className="cum-btn cum-btn--ghost" onClick={handleCancelUser}>İptal</button>
                </div>
              </form>
            </div>
          )}
        </div>
      )}

    </div>
  )
}
