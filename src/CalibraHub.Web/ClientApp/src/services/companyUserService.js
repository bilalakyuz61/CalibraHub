/**
 * companyUserService — Şirket + Kullanıcı JSON API
 * Endpoints: /Setup/* (AllowAnonymous + sunucu tarafında RequireAuth)
 */

var BASE = '/Setup'

export async function getCompaniesJson() {
  var resp = await fetch(BASE + '/GetCompaniesJson', {
    credentials: 'same-origin',
    headers: { 'Accept': 'application/json' }
  })
  if (!resp.ok) throw new Error('HTTP ' + resp.status)
  return await resp.json()
}

export async function saveCompanyJson(payload) {
  var resp = await fetch(BASE + '/SaveCompanyJson', {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
    body: JSON.stringify(payload)
  })
  var data = null
  try { data = await resp.json() } catch (e) { /* not json */ }
  if (!resp.ok) return { success: false, message: (data && data.message) || 'HTTP ' + resp.status }
  return data || { success: true }
}

export async function deactivateCompanyJson(id) {
  var resp = await fetch(BASE + '/DeactivateCompanyJson', {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
    body: JSON.stringify({ id: id })
  })
  var data = null
  try { data = await resp.json() } catch (e) { /* not json */ }
  return data || { success: resp.ok }
}

export async function getUsersJson() {
  var resp = await fetch(BASE + '/GetUsersJson', {
    credentials: 'same-origin',
    headers: { 'Accept': 'application/json' }
  })
  if (!resp.ok) throw new Error('HTTP ' + resp.status)
  return await resp.json()
}

export async function saveUserJson(payload) {
  var resp = await fetch(BASE + '/SaveUserJson', {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
    body: JSON.stringify(payload)
  })
  var data = null
  try { data = await resp.json() } catch (e) { /* not json */ }
  if (!resp.ok) return { success: false, message: (data && data.message) || 'HTTP ' + resp.status }
  return data || { success: true }
}

/**
 * Kullanıcıyı pasife alır. email parametresiyle çağrılırsa (multi-company)
 * o e-postaya ait tüm profiller pasife alınır.
 */
export async function deactivateUserJson(email) {
  var resp = await fetch(BASE + '/DeactivateUserJson', {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
    body: JSON.stringify({ id: 0, email: email })
  })
  var data = null
  try { data = await resp.json() } catch (e) { /* not json */ }
  return data || { success: resp.ok }
}

/**
 * Bağlantı testi — şifre sunucuya parçalar halinde gönderilir, düz connection string gönderilmez.
 * @param {{ id?: number|null, server: string, database: string, username: string, password: string }} parts
 */
export async function testConnectionJson(parts) {
  var resp = await fetch(BASE + '/TestCompanyConnection', {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
    body: JSON.stringify({
      id:          parts.id   || null,
      sqlServer:   parts.server   || '',
      sqlDatabase: parts.database || '',
      sqlUsername: parts.username || '',
      sqlPassword: parts.password || ''
    })
  })
  var data = null
  try { data = await resp.json() } catch (e) { /* not json */ }
  return data || { success: false, message: 'Yanıt alınamadı.' }
}
