const BASE = '/api/doc-designer'

export async function listLayouts(docType) {
  const url = docType ? `${BASE}/layouts?docType=${encodeURIComponent(docType)}` : `${BASE}/layouts`
  const res = await fetch(url, { credentials: 'include' })
  if (!res.ok) throw new Error(await res.text())
  return res.json()
}

export async function getLayout(id) {
  const res = await fetch(`${BASE}/layouts/${id}`, { credentials: 'include' })
  if (!res.ok) throw new Error(await res.text())
  return res.json()
}

export async function saveLayout(req) {
  const res = await fetch(`${BASE}/layouts`, {
    method: 'PUT',
    credentials: 'include',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(req)
  })
  if (!res.ok) throw new Error(await res.text())
  return res.json()
}

export async function deleteLayout(id) {
  const res = await fetch(`${BASE}/layouts/${id}`, { method: 'DELETE', credentials: 'include' })
  if (!res.ok) throw new Error(await res.text())
}

export async function previewHtml(layoutId, documentId) {
  const res = await fetch(`${BASE}/preview`, {
    method: 'POST',
    credentials: 'include',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ layoutId, documentId: documentId ?? null, paramOverrides: null })
  })
  if (!res.ok) throw new Error(await res.text())
  const data = await res.json()
  return data.html
}

export async function renderPdfBlob(layoutId, documentId) {
  const res = await fetch(`${BASE}/render-pdf`, {
    method: 'POST',
    credentials: 'include',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ layoutId, documentId: documentId ?? null, paramOverrides: null })
  })
  if (!res.ok) throw new Error(await res.text())
  return res.blob()
}

export async function listDocTypes() {
  const res = await fetch(`${BASE}/doc-types`, { credentials: 'include' })
  if (!res.ok) throw new Error(await res.text())
  return res.json()
}

export async function listViews() {
  const res = await fetch('/api/reporting/views', { credentials: 'include' })
  if (!res.ok) throw new Error(await res.text())
  return res.json()
}

export async function getViewColumns(viewId) {
  const res = await fetch(`/api/reporting/views/${viewId}`, { credentials: 'include' })
  if (!res.ok) throw new Error(await res.text())
  const data = await res.json()
  return data.columns ?? []
}
