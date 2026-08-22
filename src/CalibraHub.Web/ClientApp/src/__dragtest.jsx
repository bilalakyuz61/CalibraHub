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

/* Testin okudugu ayna — URETIM KODUNA DOKUNMADAN, DOM sirasindan turetilir.
   Bolum rozetleri ("Kimlik" / "Serit N") ve alan adi span'lari (title=key)
   belge sirasiyla gezilir. Surukleme SIRASINDA da okunabilir → canli
   onizlemenin (komsularin yer acmasi) calisip calismadigini gosterir. */
window.__dragProbe = function () {
  var secRe = /^(Kimlik|Şerit \d+|Var[sş]ay[iı]lan)/
  var cur = null
  var out = []
  var all = document.querySelectorAll('span, div')
  for (var i = 0; i < all.length; i++) {
    var el = all[i]
    var t = (el.childElementCount === 0 ? (el.textContent || '') : '').trim()
    if (t && secRe.test(t) && t.length < 30) { cur = { name: t, fields: [] }; out.push(cur); continue }
    var key = el.getAttribute && el.getAttribute('title')
    if (key && el.tagName === 'SPAN' && el.classList.contains('truncate') && cur) {
      if (cur.fields.indexOf(key) < 0) cur.fields.push(key)
    }
  }
  return out.map(function (g) { return g.name + ': [' + g.fields.join(',') + ']' }).join('  |  ')
}

createRoot(document.getElementById('root')).render(
  <StandardFieldsEditor formCode="SALES_QUOTE_LINE" onClose={function () {}} onSaved={function () {}} />
)
