/**
 * CompanyUserManagementPanel — Şirket ve Kullanıcı Yönetim Ekranı
 * React tabanlı, koyu/açık tema desteği (CSS değişkenleri aracılığıyla)
 * Multi-company: bir kullanıcı birden fazla şirkete bağlanabilir.
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
// companyIds: kullanıcının bağlı olduğu şirket ID'leri listesi (multi-company)
// role: CalibraHub yetkisi — 'Admin' / 'SistemAdmin' / 'User'
var EMPTY_USER = { companyIds: [], firstName: '', lastName: '', email: '', password: '', role: 'User', isActive: true }

export default function CompanyUserManagementPanel() {

  // ── Tab ────────────────────────────────────────────────────
  var [activeTab, setActiveTab] = useState('companies')

  // ── Company state ──────────────────────────────────────────
  var [companies,        setCompanies]        = useState([])
  var [compLoading,      setCompLoading]      = useState(true)
  var [compError,        setCompError]        = useState(null)
  var [compSearch,       setCompSearch]       = useState('')
  var [editingComp,      setEditingComp]      = useState(null)
  var [compForm,         setCompForm]         = useState(EMPTY_COMPANY)
  var [compSaving,       setCompSaving]       = useState(false)
  var [compFormErr,      setCompFormErr]      = useState(null)
  var [connTesting,      setConnTesting]      = useState(false)
  var [connResult,       setConnResult]       = useState(null)
  var [showPwd,          setShowPwd]          = useState(false)
  var [confirmDelComp,   setConfirmDelComp]   = useState(null)

  // ── User state ─────────────────────────────────────────────
  var [users,               setUsers]               = useState([])
  var [userLoading,         setUserLoading]         = useState(true)
  var [userError,           setUserError]           = useState(null)
  var [userSearch,          setUserSearch]          = useState('')
  var [userCompanyFilter,   setUserCompanyFilter]   = useState('all')
  var [editingUser,         setEditingUser]         = useState(null)   // null | 'new' | email string
  var [userForm,            setUserForm]            = useState(EMPTY_USER)
  var [userSaving,          setUserSaving]          = useState(false)
  var [userFormErr,         setUserFormErr]         = useState(null)
  var [showUserPwd,         setShowUserPwd]         = useState(false)
  var [confirmDelUserEmail, setConfirmDelUserEmail] = useState(null)  // email to deactivate

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
      sqlPassword: '', hasPassword: !!c.hasPassword, isActive: c.isActive })
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
      id: compForm.id, server: compForm.sqlServer,
      database: compForm.sqlDatabase, username: compForm.sqlUsername, password: compForm.sqlPassword
    })
    setConnTesting(false); setConnResult(r)
  }

  // ── User handlers ──────────────────────────────────────────
  function handleNewUser() {
    setEditingUser('new'); setUserForm(Object.assign({}, EMPTY_USER, { companyIds: [] }))
    setUserFormErr(null); setShowUserPwd(false)
  }

  function handleEditUser(u) {
    var fullName = (u.fullName || '').trim()
    var spaceIdx = fullName.indexOf(' ')
    var firstName = spaceIdx === -1 ? fullName : fullName.substring(0, spaceIdx)
    var lastName  = spaceIdx === -1 ? ''       : fullName.substring(spaceIdx + 1).trim()
    // companyIds: mevcut şirket bağlantılarından al (companies dizisi)
    var companyIds = (u.companies || []).map(function (c) { return c.id })
    setEditingUser(u.email)
    setUserForm({
      email:      u.email || '',
      companyIds: companyIds,
      firstName:  firstName,
      lastName:   lastName,
      password:   '',
      role:       u.uiRole || 'User',
      isActive:   u.isActive
    })
    setUserFormErr(null); setShowUserPwd(false)
  }

  function handleCancelUser() { setEditingUser(null); setUserFormErr(null) }

  function toggleCompanyId(id) {
    var ids = userForm.companyIds || []
    setUserForm(Object.assign({}, userForm, {
      companyIds: ids.includes(id) ? ids.filter(function (x) { return x !== id }) : ids.concat(id)
    }))
  }

  async function handleSaveUser(e) {
    e.preventDefault()
    if (!userForm.companyIds || userForm.companyIds.length === 0) {
      setUserFormErr('En az bir şirket seçilmelidir.'); return
    }
    if (!userForm.firstName.trim()) { setUserFormErr('Ad zorunludur.'); return }
    if (!userForm.email.trim()) { setUserFormErr('E-posta zorunludur.'); return }
    var isEditing = editingUser !== 'new' && editingUser !== null
    if (!isEditing) {
      if (!userForm.password || userForm.password.length < 8) {
        setUserFormErr('Şifre en az 8 karakter olmalıdır.'); return
      }
    } else if (userForm.password && userForm.password.length < 8) {
      setUserFormErr('Şifre değiştirmek için en az 8 karakter girin (boş bırakırsanız değişmez).'); return
    }
    setUserSaving(true); setUserFormErr(null)
    var payload = {
      email:      userForm.email,
      companyIds: userForm.companyIds,
      firstName:  userForm.firstName,
      lastName:   userForm.lastName,
      password:   userForm.password || null,
      role:       userForm.role,
      isActive:   userForm.isActive
    }
    var result = await saveUserJson(payload)
    setUserSaving(false)
    if (result.success) {
      showToast('success', isEditing ? 'Kullanıcı güncellendi.' : 'Kullanıcı oluşturuldu.')
      setEditingUser(null); loadUsers()
    } else {
      setUserFormErr(result.message || 'Kaydetme başarısız.')
    }
  }

  async function handleDeactivateUser() {
    var email = confirmDelUserEmail; setConfirmDelUserEmail(null)
    var result = await deactivateUserJson(email)
    if (result && result.success !== false) {
      showToast('success', 'Kullanıcı pasife alındı.')
      if (editingUser === email) setEditingUser(null)
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
    if (userCompanyFilter !== 'all') {
      var hasComp = (u.companies || []).some(function (c) { return String(c.id) === String(userCompanyFilter) })
      if (!hasComp) return false
    }
    if (!userSearch.trim()) return true
    var q = userSearch.toLowerCase()
    return (u.fullName || '').toLowerCase().includes(q) ||
           (u.email || '').toLowerCase().includes(q) ||
           (u.companyName || '').toLowerCase().includes(q)
  })

  var activeCompanies = companies.filter(function (c) { return c.isActive })

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

      {/* Confirm deactivate company */}
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

      {/* Confirm deactivate user */}
      {confirmDelUserEmail && (
        <div className="cum-overlay">
          <div className="cum-confirm-box">
            <AlertCircle size={22} className="cum-confirm-ico" />
            <p>Bu kullanıcı <strong>tüm şirket bağlantılarıyla birlikte</strong> pasife alınacak. Devam edilsin mi?</p>
            <div className="cum-confirm-btns">
              <button type="button" className="cum-btn cum-btn--danger" onClick={handleDeactivateUser}>Pasife Al</button>
              <button type="button" className="cum-btn cum-btn--ghost" onClick={function () { setConfirmDelUserEmail(null) }}>İptal</button>
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

          {/* Company form panel */}
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

              <div className="cum-filter-wrap" title="Şirket filtresi">
                <Building2 size={13} className="cum-filter-ico" />
                <select className="cum-filter-select"
                  value={userCompanyFilter}
                  onChange={function (e) { setUserCompanyFilter(e.target.value) }}>
                  <option value="all">Tüm Şirketler ({users.length})</option>
                  {companies.map(function (c) {
                    var cnt = users.filter(function (u) {
                      return (u.companies || []).some(function (uc) { return String(uc.id) === String(c.id) })
                    }).length
                    return <option key={c.id} value={c.id}>{c.name} ({cnt})</option>
                  })}
                </select>
              </div>

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
                <p>
                  {userSearch ? 'Eşleşen kullanıcı bulunamadı.'
                    : userCompanyFilter !== 'all' ? 'Bu şirkette kayıtlı kullanıcı yok.'
                    : 'Henüz kullanıcı tanımlanmamış.'}
                </p>
                {!userSearch && userCompanyFilter === 'all' && (
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
                      <th>Şirketler</th>
                      <th>Durum</th>
                      <th></th>
                    </tr>
                  </thead>
                  <tbody>
                    {filteredUsers.map(function (u) {
                      return (
                        <tr key={u.email} className={editingUser === u.email ? 'is-selected' : ''}>
                          <td className="cum-td-name">
                            <Shield size={12} style={{ opacity: 0.4, marginRight: 6, flexShrink: 0 }} />
                            {u.fullName}
                          </td>
                          <td className="cum-td-email">{u.email}</td>
                          <td className="cum-td-companies">
                            {(u.companies || []).length > 0
                              ? (u.companies || []).map(function (c) {
                                  return (
                                    <span key={c.id} className="cum-pill cum-pill--blue" style={{ marginRight: 3, marginBottom: 2 }}>
                                      {c.name || '—'}
                                    </span>
                                  )
                                })
                              : <span className="cum-pill cum-pill--gray">—</span>
                            }
                          </td>
                          <td>
                            <span className={'cum-pill ' + (u.isActive ? 'cum-pill--green' : 'cum-pill--red')}>
                              {u.isActive ? 'Aktif' : 'Pasif'}
                            </span>
                          </td>
                          <td className="cum-td-actions">
                            <button type="button" className="cum-action-btn" onClick={function () { handleEditUser(u) }} title="Düzenle">
                              <Pencil size={13} />
                            </button>
                            <button type="button" className="cum-action-btn cum-action-btn--danger"
                              onClick={function () { setConfirmDelUserEmail(u.email) }} title="Pasife Al">
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

          {/* User form panel */}
          {editingUser !== null && (
            <div className="cum-form-panel">
              <div className="cum-form-header">
                <h3 className="cum-form-title">
                  <Users size={16} />
                  {editingUser === 'new' ? 'Yeni Kullanıcı' : 'Kullanıcı Düzenle'}
                </h3>
                <button type="button" className="cum-close-btn" onClick={handleCancelUser}><X size={16} /></button>
              </div>

              <form className="cum-form" onSubmit={handleSaveUser} autoComplete="off">

                {/* Şirket çoklu seçim */}
                <div className="cum-field">
                  <label className="cum-label">
                    Şirketler <span className="cum-req">*</span>
                    <span style={{fontWeight:400,fontSize:'.69rem',color:'var(--cum-text-muted)',marginLeft:5,textTransform:'none',letterSpacing:0}}>
                      — birden fazla seçilebilir
                    </span>
                  </label>
                  {activeCompanies.length === 0 ? (
                    <p className="cum-empty-hint">Önce şirket tanımlamalısınız.</p>
                  ) : (
                    <div className="cum-company-checks">
                      {activeCompanies.map(function (c) {
                        var checked = (userForm.companyIds || []).includes(c.id)
                        return (
                          <label key={c.id} className={'cum-check-item' + (checked ? ' is-checked' : '')}
                                 title={checked ? c.name + ' seçili — kaldırmak için tıklayın' : c.name + ' — seçmek için tıklayın'}>
                            <input type="checkbox" checked={checked} style={{display:'none'}}
                              onChange={function () { toggleCompanyId(c.id) }} />
                            <span className="cum-check-dot" aria-hidden="true"></span>
                            <span className="cum-check-label">{c.name}</span>
                            {checked && (
                              <span style={{marginLeft:'auto',fontSize:'.69rem',color:'var(--cum-accent)',fontWeight:700}}>✓</span>
                            )}
                          </label>
                        )
                      })}
                    </div>
                  )}
                  {(userForm.companyIds || []).length > 0 && (
                    <p style={{fontSize:'.71rem',color:'var(--cum-text-muted)',margin:'3px 0 0'}}>
                      {(userForm.companyIds || []).length} şirket seçili
                    </p>
                  )}
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
                    readOnly={editingUser !== 'new'}
                    style={editingUser !== 'new' ? { opacity: 0.7, cursor: 'not-allowed' } : {}}
                    onChange={function (e) { if (editingUser === 'new') setUserForm(Object.assign({}, userForm, { email: e.target.value })) }} />
                </div>

                <div className="cum-field">
                  <label className="cum-label">
                    Şifre {editingUser === 'new' && <span className="cum-req">*</span>}
                    {editingUser !== 'new' && <span className="cum-field-hint"> — boş bırakılırsa değiştirilmez</span>}
                  </label>
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

                {/* CalibraHub Yetkisi */}
                <div className="cum-field">
                  <label className="cum-label">CalibraHub Yetkisi <span className="cum-req">*</span></label>
                  <div className="cum-radio-group" role="radiogroup">
                    {[
                      { v: 'Admin',       t: 'Admin',        d: 'Şirket yöneticisi (kullanıcı/ayar yönetimi)' },
                      { v: 'SistemAdmin', t: 'Sistem Admin', d: 'Tüm sistem üzerinde tam yetki' },
                      { v: 'User',        t: 'User',         d: 'Standart kullanıcı (görüntüleme + temel işlemler)' }
                    ].map(function (opt) {
                      var checked = (userForm.role || 'User') === opt.v
                      return (
                        <label key={opt.v} className={'cum-radio' + (checked ? ' is-checked' : '')}>
                          <input type="radio" name="cum-role"
                            value={opt.v} checked={checked}
                            onChange={function () { setUserForm(Object.assign({}, userForm, { role: opt.v })) }} />
                          <span className="cum-radio-dot" aria-hidden="true"></span>
                          <span className="cum-radio-text">
                            <span className="cum-radio-title">{opt.t}</span>
                            <span className="cum-radio-desc">{opt.d}</span>
                          </span>
                        </label>
                      )
                    })}
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
                    {userSaving ? 'Kaydediliyor…' : (editingUser === 'new' ? 'Oluştur' : 'Kaydet')}
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
