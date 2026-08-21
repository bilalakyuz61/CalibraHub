/* GECICI sürükleme test kosumu — kalici DEGIL, dogrulamadan sonra silinir.
   Gercek StandardFieldsEditor'i yetkili oturum olmadan calistirmak icin
   /api/form-behavior GET'ini sahte veriyle karsilar. */
import { createRoot } from 'react-dom/client'
import StandardFieldsEditor from './components/AdminWidgetRegistry/StandardFieldsEditor'

var FAKE = {
  ok: true,
  layoutMode: 'free',
  defaultCardWidth: 3,
  tabs: [{ key: 'lines', label: 'Kalem Bilgileri', locked: true, isVisible: true }],
  stripHeights: [],
  fields: [
    { key: 'materialCode', label: 'Malzeme Kodu', tab: 'lines', locked: true, isVisible: true, cardSection: 0, cardOrder: 0 },
    { key: 'materialName', label: 'Malzeme Adı', tab: 'lines', isVisible: true, cardSection: 0, cardOrder: 1 },
    { key: 'unit', label: 'Birim', tab: 'lines', isVisible: true, cardSection: 1, cardOrder: 0 },
    { key: 'quantity', label: 'Miktar', tab: 'lines', locked: true, isVisible: true, cardSection: 1, cardOrder: 1 },
    { key: 'unitPrice', label: 'Birim Fiyat', tab: 'lines', isVisible: true, cardSection: 1, cardOrder: 2 },
    { key: 'discountRate2', label: 'İskonto %', tab: 'lines', isVisible: true, cardSection: 1, cardOrder: 3 },
    { key: 'taxRate', label: 'KDV %', tab: 'lines', isVisible: true, cardSection: 1, cardOrder: 4 },
    { key: 'lineTotal', label: 'Satır Toplamı', tab: 'lines', isVisible: true, cardSection: 1, cardOrder: 5 },
    { key: 'lotNo', label: 'Parti No', tab: 'lines', isVisible: true, cardSection: 2, cardOrder: 0 },
  ],
}

var realFetch = window.fetch
window.fetch = function (url, opts) {
  var u = String(url)
  if (u.indexOf('/api/form-behavior/') === 0) {
    return Promise.resolve({ ok: true, status: 200, json: function () { return Promise.resolve(FAKE) } })
  }
  if (u.indexOf('/api/form-behavior/save') === 0) {
    return Promise.resolve({ ok: true, status: 200, json: function () { return Promise.resolve({ ok: true }) } })
  }
  return realFetch.apply(window, arguments)
}

/* Testin okudugu ayna: her alanin (sekme, bolum, sira) durumu.
   Surukleme SIRASINDA da guncellenir → canli onizlemenin calistigini gosterir. */
window.__dragProbe = function () {
  var out = []
  document.querySelectorAll('[data-probe-section]').forEach(function (sec) {
    var names = []
    sec.querySelectorAll('[data-probe-field]').forEach(function (r) { names.push(r.getAttribute('data-probe-field')) })
    out.push(sec.getAttribute('data-probe-section') + ': [' + names.join(', ') + ']')
  })
  return out.join(' | ')
}

createRoot(document.getElementById('root')).render(
  <StandardFieldsEditor formCode="SALES_QUOTE_LINE" onClose={function () {}} onSaved={function () {}} />
)
