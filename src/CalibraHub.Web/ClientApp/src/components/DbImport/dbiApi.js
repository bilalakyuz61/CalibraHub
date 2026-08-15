/*
 * DbImport ortak API yardımcıları — CSRF global olarak zorunlu
 * (Program.cs AutoValidateAntiforgeryTokenAttribute), bu yüzden her POST
 * token taşır. İki bileşen (bağlantılar + sihirbaz) bunu paylaşır.
 */

export function csrf() {
  const el = document.querySelector('input[name="__RequestVerificationToken"]')
  if (el && el.value) return el.value
  try {
    const cfg = window.__CALIBRA_SHELL_CONFIG__
    if (cfg && typeof cfg.antiforgeryToken === 'string') return cfg.antiforgeryToken
  } catch (_) { /* ignore */ }
  return ''
}

export async function apiGet(url) {
  const r = await fetch(url, { credentials: 'same-origin' })
  return r.json()
}

export async function apiPost(url, body) {
  const r = await fetch(url, {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', RequestVerificationToken: csrf() },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  return r.json()
}
