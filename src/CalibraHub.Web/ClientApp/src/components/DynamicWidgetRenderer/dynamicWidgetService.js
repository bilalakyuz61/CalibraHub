/**
 * dynamicWidgetService — Faz C
 *
 * DynamicWidgetRenderer'in kullandigi API client. Tum cagrilari yeni
 * /api/widgets/forms/{formCode}/records/{recordId} endpoint'leri uzerinden yapar.
 *
 * Endpoint'ler:
 *   GET  /api/widgets/forms/{formCode}/records/{recordId} → WidgetRecordDto
 *   POST /api/widgets/forms/{formCode}/records/{recordId} → { success }
 */

var API_BASE = '/api/widgets'

/**
 * Bir kaydin widget tanimlarini + mevcut degerlerini tek cagride alir.
 * recordId bos ise ('' / null) server sadece widget tanimlarini doner (values=[]).
 *
 * @returns WidgetRecordDto { formId, formCode, formLabel, recordId, widgets }
 */
export async function getRecord(formCode, recordId) {
  if (!formCode) throw new Error('formCode zorunlu')
  var safeRecordId = (recordId != null && String(recordId).length > 0)
    ? String(recordId)
    : '-'  // Server recordId bos karsisina '-' dummy'si gonderiyoruz ki route eslessin;
           // GetRecordByCodeAsync zaten bos recordId'de values bos liste doner.
  var url = API_BASE + '/forms/' + encodeURIComponent(formCode) +
            '/records/' + encodeURIComponent(safeRecordId)
  var resp = await fetch(url, {
    method: 'GET',
    credentials: 'same-origin',
    headers: { 'Accept': 'application/json' },
  })
  if (resp.status === 404) return null
  if (!resp.ok) throw new Error('getRecord HTTP ' + resp.status)
  return await resp.json()
}

/**
 * Kaydin widget degerlerini + grid child satirlarini kaydeder.
 * Faz E — master-detail save payload shape:
 *   {
 *     values: { widgetCode: value, ... },
 *     grids:  { widgetCode: { childFormCode, rows: [{ recordId?, values }] } }
 *   }
 *
 * @param formCode — 'SALES_QUOTE_EDIT', 'CONTACTS', ...
 * @param recordId — parent business key
 * @param payload  — { values, grids? }
 * @returns {{success, message?, grids?}} — backend SaveRecordResponseDto
 */
export async function saveRecord(formCode, recordId, payload) {
  if (!formCode) throw new Error('formCode zorunlu')
  if (!recordId) throw new Error('recordId zorunlu')
  var url = API_BASE + '/forms/' + encodeURIComponent(formCode) +
            '/records/' + encodeURIComponent(recordId)
  var body = (payload && typeof payload === 'object' && ('values' in payload || 'grids' in payload))
    ? { values: payload.values || {}, grids: payload.grids || null }
    : { values: payload || {}, grids: null }
  var resp = await fetch(url, {
    method: 'POST',
    credentials: 'same-origin',
    headers: {
      'Content-Type': 'application/json',
      'Accept': 'application/json',
    },
    body: JSON.stringify(body),
  })
  var data = null
  try { data = await resp.json() } catch (e) { /* not json */ }
  if (!resp.ok) {
    var msg = (data && data.message) || ('saveRecord HTTP ' + resp.status)
    return { success: false, message: msg }
  }
  return {
    success: (data && data.success !== false) ? true : false,
    formId:  data && data.formId,
    recordId: data && data.recordId,
    grids:   data && data.grids,
  }
}

/**
 * Faz E — grid widget icin child form schema'sini cek.
 * Child form'un aktif widget'larini dondurur (grup/grid satirlari haric,
 * sadece gercek form alanlari kolon olarak kullanilir).
 */
export async function widgetSchemaByCode(formCode) {
  if (!formCode) throw new Error('formCode zorunlu')
  var url = API_BASE + '/forms/' + encodeURIComponent(formCode) + '/schema'
  var resp = await fetch(url, {
    method: 'GET', credentials: 'same-origin',
    headers: { 'Accept': 'application/json' },
  })
  if (resp.status === 404) return null
  if (!resp.ok) throw new Error('widgetSchemaByCode HTTP ' + resp.status)
  return await resp.json()
}

// ──────────────────────────────────────────────────────────────
// Guide (SQL View-based Lookup) API helpers — /api/guides
// ──────────────────────────────────────────────────────────────

var GUIDE_BASE = '/api/guides'

// PR 3: guideCatalog() helper kaldirildi — UI artik /api/guides/views uzerinden
// fiziksel SQL view listesini direkt cekiyor (OptionsModal/GuideCustomizationModal).
// GUIDE_BASE su an sadece /api/guides/{code}/schema|search|resolve|distinct icin
// kullaniliyor; {code} parametresi GuideCode VEYA ViewName kabul eder
// (SqlGuideRepository.GetByCodeAsync iki kolonda da eslestirir).

/** Tek bir rehberin schema'si — kolon header'lari + default sort */
export async function guideSchema(guideCode) {
  if (!guideCode) throw new Error('guideCode zorunlu')
  var resp = await fetch(GUIDE_BASE + '/' + encodeURIComponent(guideCode) + '/schema', {
    method: 'GET', credentials: 'same-origin',
    headers: { 'Accept': 'application/json' },
  })
  if (resp.status === 404) return null
  if (!resp.ok) throw new Error('guideSchema HTTP ' + resp.status)
  return await resp.json()
}

/**
 * Arama + sayfalama. Parametreler:
 *   search, page (default 1), pageSize (default 50), sortColumn, sortDirection
 * Returns: { rows, page, pageSize, hasMore }
 */
export async function guideSearch(guideCode, opts) {
  if (!guideCode) throw new Error('guideCode zorunlu')
  var o = opts || {}
  var params = new URLSearchParams()
  if (o.search) params.set('search', o.search)
  if (o.page != null) params.set('page', String(o.page))
  if (o.pageSize != null) params.set('pageSize', String(o.pageSize))
  if (o.sortColumn) params.set('sortColumn', o.sortColumn)
  if (o.sortDirection) params.set('sortDirection', o.sortDirection)
  if (o.constraints) params.set('constraints', o.constraints)
  var qs = params.toString()
  var url = GUIDE_BASE + '/' + encodeURIComponent(guideCode) + (qs ? '?' + qs : '')
  var resp = await fetch(url, {
    method: 'GET', credentials: 'same-origin',
    headers: { 'Accept': 'application/json' },
  })
  if (resp.status === 404) return null
  if (!resp.ok) {
    var detail = ''
    try {
      var body = await resp.json()
      if (body && body.message) detail = ': ' + body.message
    } catch (_) { /* body JSON degil */ }
    throw new Error('guideSearch HTTP ' + resp.status + detail)
  }
  return await resp.json()
}

/**
 * Bir kolonun DISTINCT degerlerini doner (max 200) — runtime'da rehber popup'inda
 * distinct filtre cipleri icin. search non-empty ise sunucu-tarafi LIKE filtresi
 * (Turkish_CI_AI) uygulanir → alfabetik kuyrukta gizli kalmis 200+ degerlere
 * de ulasilabilir. Returns: string[]
 */
export async function guideDistinct(guideCode, column, search) {
  if (!guideCode) throw new Error('guideCode zorunlu')
  if (!column) throw new Error('column zorunlu')
  var url = GUIDE_BASE + '/' + encodeURIComponent(guideCode) +
            '/distinct/' + encodeURIComponent(column)
  if (search && String(search).trim().length > 0) {
    url += '?q=' + encodeURIComponent(String(search).trim())
  }
  var resp = await fetch(url, {
    method: 'GET', credentials: 'same-origin',
    headers: { 'Accept': 'application/json' },
  })
  if (resp.status === 404) return []
  if (!resp.ok) throw new Error('guideDistinct HTTP ' + resp.status)
  return await resp.json()
}

/** Tek value → display cozumleme (sayfa load senaryosu) */
export async function guideResolve(guideCode, value) {
  if (!guideCode) throw new Error('guideCode zorunlu')
  if (value == null || value === '') return null
  var url = GUIDE_BASE + '/' + encodeURIComponent(guideCode) + '/resolve?value=' + encodeURIComponent(value)
  var resp = await fetch(url, {
    method: 'GET', credentials: 'same-origin',
    headers: { 'Accept': 'application/json' },
  })
  if (resp.status === 404) return null
  if (!resp.ok) throw new Error('guideResolve HTTP ' + resp.status)
  return await resp.json()
}

