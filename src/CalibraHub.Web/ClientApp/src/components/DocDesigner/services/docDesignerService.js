const BASE = '/api/doc-designer'

export async function listLayouts(docType) {
  const qs = docType ? `?docType=${encodeURIComponent(docType)}` : ''
  const r = await fetch(`${BASE}/layouts${qs}`)
  if (!r.ok) throw new Error(await r.text())
  return r.json()
}

export async function getLayout(id) {
  const r = await fetch(`${BASE}/layouts/${id}`)
  if (!r.ok) throw new Error(await r.text())
  return r.json()
}

export async function saveLayout(req) {
  const r = await fetch(`${BASE}/layouts`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(req),
  })
  if (!r.ok) throw new Error(await r.text())
  return r.json()
}

export async function deleteLayout(id) {
  const r = await fetch(`${BASE}/layouts/${id}`, { method: 'DELETE' })
  if (!r.ok) throw new Error(await r.text())
}

export async function previewLayout(req) {
  const r = await fetch(`${BASE}/preview`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(req),
  })
  if (!r.ok) throw new Error(await r.text())
  const { html } = await r.json()
  return html
}

export async function renderPdf(req) {
  const r = await fetch(`${BASE}/render-pdf`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(req),
  })
  if (!r.ok) throw new Error(await r.text())
  return r.blob()
}

export async function listDocTypes() {
  const r = await fetch(`${BASE}/doc-types`)
  if (!r.ok) throw new Error(await r.text())
  return r.json()
}

export async function listViews() {
  const r = await fetch('/api/reporting/views')
  if (!r.ok) throw new Error(await r.text())
  return r.json()
}

export async function getViewColumns(viewId) {
  const r = await fetch(`/api/reporting/views/${viewId}/discover-columns`)
  if (!r.ok) throw new Error(await r.text())
  return r.json()
}

/** GET /api/database/views → { schema, name, fullName }[] */
export async function listDbViews() {
  const r = await fetch('/api/database/views')
  if (!r.ok) throw new Error(await r.text())
  return r.json()
}

/** GET /api/database/tables/{schemaAndName}/columns → string[] → { colName, displayName }[] */
export async function getDbViewColumns(schemaAndName) {
  const r = await fetch(`/api/database/tables/${encodeURIComponent(schemaAndName)}/columns`)
  if (!r.ok) throw new Error(await r.text())
  const cols = await r.json()  // string[]
  return cols.map(c => ({ colName: c, displayName: c }))
}
