var BASE = '/OrgChart'

function postJson(url, body) {
  return fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'same-origin',
    body: JSON.stringify(body),
  }).then(function (r) { return r.json() })
}

export function getCharts() {
  return fetch(BASE + '/GetChartsJson', { credentials: 'same-origin' })
    .then(function (r) { return r.json() })
}

export function getChartDetail(chartId) {
  return fetch(BASE + '/GetChartDetailJson?chartId=' + chartId, { credentials: 'same-origin' })
    .then(function (r) { return r.json() })
}

export function saveChart(payload) {
  return postJson(BASE + '/SaveChartJson', payload)
}

export function deleteChart(id) {
  return postJson(BASE + '/DeleteChartJson', { id: id })
}

export function setDefaultChart(id) {
  return postJson(BASE + '/SetDefaultChartJson', { id: id })
}

export function saveNodes(chartId, nodes) {
  return postJson(BASE + '/SaveNodesJson', { chartId: chartId, nodes: nodes })
}

export function deleteNode(id) {
  return postJson(BASE + '/DeleteNodeJson', { id: id })
}

export function generateDefaultChart() {
  return postJson(BASE + '/GenerateDefaultChartJson', {})
}
