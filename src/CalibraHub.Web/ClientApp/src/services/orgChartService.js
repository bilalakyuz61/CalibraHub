var BASE = '/OrgChart'

function getCsrf() {
  var el = document.querySelector('input[name="__RequestVerificationToken"]')
  return el ? el.value : ''
}

function postJson(url, body) {
  return fetch(url, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'RequestVerificationToken': getCsrf(),
    },
    credentials: 'same-origin',
    body: JSON.stringify(body),
  }).then(function (r) { return r.json() })
}

function getJson(url) {
  return fetch(url, { credentials: 'same-origin' }).then(function (r) { return r.json() })
}

export function getCharts() {
  return getJson(BASE + '/GetChartsJson')
}

export function getChartDetail(chartId) {
  return getJson(BASE + '/GetChartDetailJson?chartId=' + chartId)
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

// ── Sprint 1 delta endpoints ─────────────────────────────

export function moveNode(payload) {
  // payload: { chartId, nodeId, newParentNodeId, newSortOrder }
  return postJson(BASE + '/MoveNodeJson', payload)
}

export function addNode(payload) {
  // payload: { chartId, nodeType, refId, intRefId, parentNodeId, positionTitle, sortOrder }
  return postJson(BASE + '/AddNodeJson', payload)
}

export function removeNode(payload) {
  // payload: { chartId, nodeId, cascade }
  return postJson(BASE + '/RemoveNodeJson', payload)
}

export function validateChart(chartId) {
  return getJson(BASE + '/ValidateChartJson?chartId=' + chartId)
}
