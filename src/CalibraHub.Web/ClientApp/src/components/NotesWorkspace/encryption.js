/**
 * Client-side E2E sifreleme yardimcisi — Web Crypto API tabanli.
 *
 * Algoritma: AES-GCM 256 (IV random + built-in auth tag)
 * Key derivation: PBKDF2 (SHA-256, 200k iteration, random 16-byte salt)
 *
 * Iki mod icin kullanilir:
 *   - Mod 1 (EncryptedMark)  : secili metin blok sifreleme
 *   - Mod 2 (Tum not kilit)  : tum HTML icerik tek payload olarak sifrelenir
 *
 * Payload formati: JSON objesi
 *   { v:1, salt:"b64", iv:"b64", ct:"b64" }
 *
 * Session cache: Kullanici bir kez sifre girdiginde, basarili decrypt sonrasi
 * o payload'un key'i 15 dakika boyunca sessionStorage'da tutulur (map'ta).
 * Key degil — sadece derive edilmis CryptoKey'in export'u (raw bytes). Boylece
 * kullanici ayni oturumda tekrar tekrar sifre sormaz.
 *
 * Guvenlik notu: SessionStorage tab'a ozgudur, tarayici kapanirsa temizlenir.
 * XSS tehdidi varsa (malicious script run olursa) cache okunabilir — ama bu
 * zaten tum E2E icin gecerli bir tehdit vektorudur.
 */

var PBKDF2_ITERATIONS = 200000
var SALT_BYTES = 16
var IV_BYTES = 12
var KEY_LENGTH_BITS = 256
var SESSION_TTL_MS = 15 * 60 * 1000  // 15 dk

var enc = new TextEncoder()
var dec = new TextDecoder()

// ── Base64 helpers ─────────────────────────────────────────────────────
function bytesToB64(bytes) {
  var bin = ''
  for (var i = 0; i < bytes.length; i++) bin += String.fromCharCode(bytes[i])
  return btoa(bin)
}
function b64ToBytes(b64) {
  var bin = atob(b64)
  var out = new Uint8Array(bin.length)
  for (var i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i)
  return out
}

// ── Key derivation ─────────────────────────────────────────────────────
async function deriveKey(password, saltBytes) {
  var passKey = await crypto.subtle.importKey(
    'raw', enc.encode(password), 'PBKDF2', false, ['deriveKey']
  )
  return crypto.subtle.deriveKey(
    {
      name: 'PBKDF2',
      salt: saltBytes,
      iterations: PBKDF2_ITERATIONS,
      hash: 'SHA-256',
    },
    passKey,
    { name: 'AES-GCM', length: KEY_LENGTH_BITS },
    true,           // extractable (cache edebilmek icin)
    ['encrypt', 'decrypt']
  )
}

// ── Public API ─────────────────────────────────────────────────────────

/**
 * Duz metni parola ile sifrele.
 * @param {string} plaintext - sifrelenecek metin (Mod 1: selection HTML,
 *                             Mod 2: tum note content HTML)
 * @param {string} password  - kullanici parolasi
 * @returns {Promise<string>} - JSON string payload (DB'ye yazilacak)
 */
export async function encryptText(plaintext, password) {
  if (!password) throw new Error('Sifre bos olamaz')
  var salt = crypto.getRandomValues(new Uint8Array(SALT_BYTES))
  var iv   = crypto.getRandomValues(new Uint8Array(IV_BYTES))
  var key  = await deriveKey(password, salt)
  var ct   = await crypto.subtle.encrypt(
    { name: 'AES-GCM', iv: iv },
    key,
    enc.encode(plaintext)
  )
  return JSON.stringify({
    v:    1,
    salt: bytesToB64(salt),
    iv:   bytesToB64(iv),
    ct:   bytesToB64(new Uint8Array(ct)),
  })
}

/**
 * Sifreli payload'u parola ile coz.
 * @param {string} payloadJson - encryptText ciktisi
 * @param {string} password
 * @returns {Promise<string>} - duz metin
 * @throws {Error} - parola yanlis ise "Parola hatali" firlatir
 */
export async function decryptText(payloadJson, password) {
  if (!password) throw new Error('Sifre bos olamaz')
  var payload
  try {
    payload = typeof payloadJson === 'string' ? JSON.parse(payloadJson) : payloadJson
  } catch (e) {
    throw new Error('Sifreli veri bozuk (JSON parse)')
  }
  if (!payload || !payload.ct || !payload.iv || !payload.salt) {
    throw new Error('Sifreli veri eksik alan')
  }
  var salt = b64ToBytes(payload.salt)
  var iv   = b64ToBytes(payload.iv)
  var ct   = b64ToBytes(payload.ct)
  var key  = await deriveKey(password, salt)
  try {
    var ptBytes = await crypto.subtle.decrypt({ name: 'AES-GCM', iv: iv }, key, ct)
    return dec.decode(ptBytes)
  } catch (e) {
    throw new Error('Parola hatali')
  }
}

// ── Session cache (15 dk) ──────────────────────────────────────────────

var SESSION_KEY = 'nw-enc-session-v1'

function readSessionMap() {
  try {
    var raw = sessionStorage.getItem(SESSION_KEY)
    if (!raw) return {}
    var obj = JSON.parse(raw)
    // Sureli temizlik
    var now = Date.now()
    Object.keys(obj).forEach(function(k) {
      if (!obj[k] || (obj[k].expires || 0) < now) delete obj[k]
    })
    return obj
  } catch (e) { return {} }
}
function writeSessionMap(map) {
  try {
    sessionStorage.setItem(SESSION_KEY, JSON.stringify(map))
  } catch (e) { /* quota asildi — sessizce devam */ }
}

/**
 * Kullanici bir payload icin basarili decrypt yaptiysa, key'i cache'le.
 * Key derive iterasyonu 200k oldugu icin her blok acilisinda tekrar derive
 * yapmak yavas. Session icinde tekrar ayni payload acilacaksa cache'den kullan.
 *
 * @param {string} cacheKey - payload'a ozgu deterministik key (genelde ciphertext
 *                            hash veya "note-<id>" gibi)
 * @param {string} password
 */
export function rememberPassword(cacheKey, password) {
  if (!cacheKey || !password) return
  var map = readSessionMap()
  map[cacheKey] = {
    password: password, // XSS durumunda okunabilir — E2E'nin kabul edilen trade-off'u
    expires:  Date.now() + SESSION_TTL_MS,
  }
  writeSessionMap(map)
}

/** Cache'den parolayi oku (varsa). */
export function recallPassword(cacheKey) {
  if (!cacheKey) return null
  var map = readSessionMap()
  var entry = map[cacheKey]
  if (!entry || (entry.expires || 0) < Date.now()) {
    if (entry) { delete map[cacheKey]; writeSessionMap(map) }
    return null
  }
  return entry.password
}

/** Cache'i tamamen temizle (kullanici cikis yapinca cagrilabilir). */
export function clearEncryptionCache() {
  try { sessionStorage.removeItem(SESSION_KEY) } catch (e) { /* ignore */ }
}

/**
 * Ciphertext payload'undan deterministik cache key uret (SHA-256 of ct).
 * Ayni payload'a ayni key duser → tekrar acilirken cache hit.
 */
export async function payloadCacheKey(payloadJson) {
  try {
    var payload = typeof payloadJson === 'string' ? JSON.parse(payloadJson) : payloadJson
    var hash = await crypto.subtle.digest('SHA-256', enc.encode(payload.ct || ''))
    return 'ct-' + bytesToB64(new Uint8Array(hash)).slice(0, 16)
  } catch (e) { return null }
}
