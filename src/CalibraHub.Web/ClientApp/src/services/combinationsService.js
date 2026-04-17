/**
 * Konfigurasyon Kombinasyon API servisi
 */

export async function getStockCodes() {
  const r = await fetch('/Logistics/StockCodesJson', { credentials: 'same-origin' })
  if (!r.ok) throw new Error('HTTP ' + r.status)
  return r.json()
}

/**
 * Stoka ait özellikler + mevcut kombinasyonlar.
 * Dönen format: { features: [...], combos: [...] }
 * (Eski format `combinations` artık kullanılmıyor — cross-product client-side üretilir.)
 */
export async function getCombinationsData(stockCode) {
  const r = await fetch(
    '/Logistics/CombinationsDataJson?stockCode=' + encodeURIComponent(stockCode),
    { credentials: 'same-origin' }
  )
  if (!r.ok) throw new Error('HTTP ' + r.status)
  return r.json()
}

/** Tek bir kombinasyon oluştur (POST). */
export async function createCombination(csrfToken, stockCode, valueIds) {
  const r = await fetch('/Logistics/AddSingleCombinationJson', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'RequestVerificationToken': csrfToken,
    },
    credentials: 'same-origin',
    body: JSON.stringify({ stockCode, valueIds }),
  })
  return r.json()
}

/**
 * Birden fazla taslak kombinasyonu ardışık olarak kaydet.
 * draftCombos: [{ valueIds: number[] }]
 * Döner: [{ ok: boolean, code?: string, id?: number, message?: string }]
 */
export async function saveDraftCombos(csrfToken, stockCode, draftCombos) {
  const results = []
  for (const draft of draftCombos) {
    try {
      const r = await createCombination(csrfToken, stockCode, draft.valueIds)
      results.push({ ok: r.success === true, code: r.code, id: r.id, message: r.message })
    } catch (e) {
      results.push({ ok: false, message: e.message })
    }
  }
  return results
}

/** Kombinasyon aciklama guncelle (POST, JSON). */
export async function updateCombinationDescription(csrfToken, id, description) {
  const r = await fetch('/Logistics/UpdateCombinationDescriptionJson', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'RequestVerificationToken': csrfToken,
    },
    credentials: 'same-origin',
    body: JSON.stringify({ id, description }),
  })
  return r.json()
}

/** Kombinasyon sil (POST, FormData). */
export async function deleteCombination(csrfToken, id) {
  const fd = new FormData()
  fd.append('id', String(id))
  fd.append('__RequestVerificationToken', csrfToken)
  const r = await fetch('/Logistics/DeleteProductCombination', {
    method: 'POST',
    credentials: 'same-origin',
    body: fd,
  })
  return r.json()
}
