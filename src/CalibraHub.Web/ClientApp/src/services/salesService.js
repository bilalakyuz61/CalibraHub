/**
 * Sales API Service — SalesController endpoint'leri (siparis donusturme akisi).
 */

var BASE = '/Sales'

function getJson(url) {
  return fetch(url, { credentials: 'same-origin' }).then(function (r) {
    if (!r.ok) throw new Error('HTTP ' + r.status)
    return r.json()
  })
}

function postJson(url, body) {
  return fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'same-origin',
    body: JSON.stringify(body),
  }).then(function (r) {
    if (!r.ok) throw new Error('HTTP ' + r.status)
    return r.json()
  })
}

/**
 * Siparise donusturulebilir teklifleri filtrele (Approved + henuz consume edilmemis).
 * @param {{ fromDate?: string, toDate?: string, contactId?: number, search?: string }} filters
 * @returns {Promise<Array<{id, documentNumber, documentDate, contactName, contactId, currency, grandTotal, status, lineCount}>>}
 */
export function getConvertibleQuotes(filters) {
  filters = filters || {}
  var params = new URLSearchParams()
  if (filters.fromDate)  params.append('fromDate', filters.fromDate)
  if (filters.toDate)    params.append('toDate', filters.toDate)
  if (filters.contactId) params.append('contactId', String(filters.contactId))
  if (filters.search)    params.append('search', filters.search)
  var qs = params.toString()
  return getJson(BASE + '/GetConvertibleQuotes' + (qs ? '?' + qs : ''))
}

/**
 * Secili teklifleri cari bazinda gruplayip siparis(ler) olustur.
 * @param {{ quoteIds: number[], orderDate: string }} req
 * @returns {Promise<{ success: boolean, error?: string, ordersCreated?: number, orderIds?: number[] }>}
 */
export function createOrdersFromQuotes(req) {
  return postJson(BASE + '/CreateOrdersFromQuotes', {
    quoteIds: Array.isArray(req.quoteIds) ? req.quoteIds : [],
    orderDate: req.orderDate,
  })
}

/**
 * Cari listesi — modal autocomplete icin.
 * @returns {Promise<Array<{id, accountCode, accountTitle}>>}
 */
export function getCustomers() {
  return getJson(BASE + '/GetCustomers')
}
