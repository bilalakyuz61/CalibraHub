/**
 * formBehavior — Form Davranış Katmanı runtime uygulayıcısı (2026-08-05).
 *
 * Sabit Razor markup'ına davranış metadata'sı uygular (GET /api/form-behavior/{formCode}):
 *   - Görünürlük (statik + visibleIf kuralı) → data-field-key konteyneri gizlenir
 *   - Zorunluluk (statik + requiredIf) → etikete kırmızı * + validate() kontrolü
 *   - Varsayılan değer (yalnız YENİ kayıtta, alan boşken; tarih için TODAY())
 *   - Başlık metni + stili (standard = etiket üstte; header satırında varsayılan
 *     etiket soldadır; 'modern' header bağlamında 'standard' gibi davranır —
 *     çoklu-input kombolarında yüzer etiket kırılgan olduğu için bilinçli karar)
 *   - Sekme görünürlük / sıra / ad (data-sqe-tab-btn hedefli)
 *
 * FAIL-OPEN: davranış kaydı yoksa hiçbir şey yapılmaz; kural parse/eval hatası →
 * alan görünür + zorunlu değil (console.warn). Ekran markup'ı asla değiştirilmez.
 *
 * Kullanım (Razor):
 *   window.CalibraHub.applyFormBehavior({
 *     formCode: 'SALES_QUOTE_EDIT',
 *     isNew: !currentQuoteId,
 *     fieldElements: { quoteDate: 'sqQuoteDate', ... },   // key → değer elementi id
 *     onTabHidden: function(tabKey) { ... },              // aktif sekme gizlendiyse
 *   }).then(function(api) { ... })
 *   Kaydet öncesi: window.CalibraHub.formBehaviorValidate() → { ok, missing:[...] }
 */
import { Parser } from 'expr-eval'

var state = null // { fields, byKey, opts }

function coerceValue(el, dataType) {
  if (!el) return ''
  try {
    if (dataType === 'boolean') return el.type === 'checkbox' ? !!el.checked : String(el.value || '') !== ''
    if (dataType === 'numeric') {
      var n = parseFloat(String(el.value || '').replace(',', '.'))
      return isNaN(n) ? 0 : n
    }
    if (dataType === 'currencyCode') {
      var opt = el.options && el.options[el.selectedIndex]
      return (opt && opt.getAttribute('data-code')) || 'TRY'
    }
    return String(el.value || '')
  } catch (_) { return '' }
}

function buildScope() {
  var scope = {}
  state.fields.forEach(function (f) {
    scope[f.key] = coerceValue(f.valueEl, f.dataType)
  })
  return scope
}

/* Kural değerlendirme — widget rule engine ile aynı fail-open sözleşmesi. */
function evalBool(exprText, scope, fallback) {
  if (!exprText) return fallback
  try {
    var result = Parser.parse(exprText).evaluate(scope)
    return result === true
  } catch (e) {
    try { console.warn('[FormBehavior] kural değerlendirilemedi (fail-open):', exprText, e && e.message) } catch (_) {}
    return fallback
  }
}

function resolveDefaultToken(raw, dataType) {
  var v = String(raw || '').trim()
  if (!v) return null
  if (dataType === 'date' && /^TODAY\(\)$/i.test(v)) {
    var d = new Date()
    var mm = String(d.getMonth() + 1).padStart(2, '0')
    var dd = String(d.getDate()).padStart(2, '0')
    return d.getFullYear() + '-' + mm + '-' + dd
  }
  return v
}

function containersFor(key, root) {
  return Array.prototype.slice.call(root.querySelectorAll('[data-field-key="' + key + '"]'))
}

function labelElFor(key, root) {
  // Öncelik: açık işaretli etiket; yoksa konteynerdeki ilk sqe etiket sınıfı.
  var explicit = root.querySelector('[data-field-label-for="' + key + '"]')
  if (explicit) return explicit
  var containers = containersFor(key, root)
  for (var i = 0; i < containers.length; i++) {
    var l = containers[i].querySelector('.sqe-hlabel, .sqe-label')
    if (l) return l
  }
  return null
}

function setFieldVisible(f, visible, root) {
  containersFor(f.key, root).forEach(function (el) {
    el.style.display = visible ? '' : 'none'
  })
  f.currentlyVisible = visible
}

function setRequiredStar(f, required) {
  if (!f.labelEl) { f.currentlyRequired = required; return }
  var star = f.labelEl.querySelector('.ffb-req-star')
  if (required && !star) {
    star = document.createElement('span')
    star.className = 'ffb-req-star'
    star.textContent = ' *'
    star.style.color = '#ef4444'
    star.style.fontWeight = '700'
    f.labelEl.appendChild(star)
  } else if (!required && star) {
    star.remove()
  }
  f.currentlyRequired = required
}

function reevaluate(root) {
  if (!state) return
  var scope = buildScope()
  state.fields.forEach(function (f) {
    var visible = f.isVisible && evalBool(f.visibleIf, scope, true)
    if (visible !== f.currentlyVisible) setFieldVisible(f, visible, root)
    var required = f.isRequired || evalBool(f.requiredIf, scope, false)
    if (required !== f.currentlyRequired) setRequiredStar(f, required)
  })
  publishStdScope(scope)
}

/* 2026-08-05 — Standart alan degerlerini global registry'ye yayinla + degisim
   event'i (debounce). Widget kural motoru (DWR) bu registry'yi dis kapsam olarak
   okur: widget kurallari standart alanlara (quoteDate, currency, vatIncluded...)
   referans verebilir ve deger degisince kurallar canli yeniden degerlendirilir. */
var __publishTimer = null
function publishStdScope(scope) {
  try {
    if (typeof window === 'undefined' || !state || !state.opts || !state.opts.formCode) return
    window.__CALIBRA_STD_FIELDS_BY_FORM__ = window.__CALIBRA_STD_FIELDS_BY_FORM__ || {}
    window.__CALIBRA_STD_FIELDS_BY_FORM__[state.opts.formCode] = scope || buildScope()
    if (__publishTimer) clearTimeout(__publishTimer)
    __publishTimer = setTimeout(function () {
      try { window.dispatchEvent(new CustomEvent('calibra:std-fields-changed')) } catch (_) {}
    }, 150)
  } catch (_) { /* sessiz */ }
}

function applyTabs(tabs, root, onTabHidden) {
  var nav = root.querySelector('.sqe-tab-nav')
  if (!nav) return
  tabs.forEach(function (t, i) {
    var btn = nav.querySelector('[data-sqe-tab-btn="' + t.key + '"]')
    if (!btn) return
    btn.style.order = String(i)
    if (t.labelText) btn.textContent = t.labelText
    if (t.isVisible === false && !t.locked) {
      btn.style.display = 'none'
      if (btn.classList.contains('is-active') && typeof onTabHidden === 'function') onTabHidden(t.key)
    }
  })
  // order calissin diye nav flex olmali — sqe-tab-nav zaten flex; garanti icin:
  try { if (getComputedStyle(nav).display.indexOf('flex') < 0) nav.style.display = 'flex' } catch (_) {}
}

export function applyFormBehavior(opts) {
  var formCode = opts && opts.formCode
  var root = (opts && opts.root) || document
  if (!formCode) return Promise.resolve(null)

  return fetch('/api/form-behavior/' + encodeURIComponent(formCode), { credentials: 'same-origin' })
    .then(function (r) { return r.ok ? r.json() : null })
    .then(function (data) {
      if (!data || data.ok !== true) return null

      var fieldEls = (opts && opts.fieldElements) || {}
      var fields = (data.fields || []).map(function (f) {
        var elId = fieldEls[f.key]
        return {
          key: f.key,
          label: f.label,
          tab: f.tab,
          dataType: f.dataType,
          locked: f.locked === true,
          isVisible: f.isVisible !== false,
          isRequired: f.isRequired === true,
          defaultValue: f.defaultValue || null,
          labelText: f.labelText || null,
          labelStyle: f.labelStyle || null,
          visibleIf: f.visibleIf || null,
          requiredIf: f.requiredIf || null,
          valueEl: elId ? root.querySelector('#' + elId) : null,
          labelEl: labelElFor(f.key, root),
          currentlyVisible: true,
          currentlyRequired: false,
        }
      }).filter(function (f) {
        // DOM'da karşılığı olmayan alan (belge tipine göre render edilmemiş) atlanır.
        return containersFor(f.key, root).length > 0 || f.valueEl
      })

      state = { fields: fields, opts: opts, root: root }

      // ── Statik uygulamalar ──
      fields.forEach(function (f) {
        // Başlık metni
        if (f.labelText && f.labelEl) {
          // Link etiketlerde (Temsilci) textContent'i degistirmek href'i korur.
          f.labelEl.childNodes.forEach(function (n) {
            if (n.nodeType === 3) n.textContent = ''
          })
          f.labelEl.insertBefore(document.createTextNode(f.labelText), f.labelEl.firstChild)
        }
        // Başlık stili: 'standard' → etiket üstte (header satırı flex-column'a döner).
        // 'modern' header bağlamında 'standard' gibi davranır; 'inline' = mevcut düzen.
        if ((f.labelStyle === 'standard' || f.labelStyle === 'modern')) {
          containersFor(f.key, root).forEach(function (c) {
            if (c.classList.contains('sqe-hrow')) {
              c.style.flexDirection = 'column'
              c.style.alignItems = 'stretch'
              c.style.gap = '2px'
            }
          })
        }
        // Varsayılan değer — yalnız yeni kayıtta, alan boşken.
        if (opts && opts.isNew && f.defaultValue && f.valueEl) {
          var current = f.valueEl.type === 'checkbox' ? (f.valueEl.checked ? '1' : '') : String(f.valueEl.value || '')
          if (!current) {
            var resolved = resolveDefaultToken(f.defaultValue, f.dataType)
            if (resolved != null) {
              if (f.valueEl.type === 'checkbox') {
                f.valueEl.checked = resolved === '1' || /^true$/i.test(resolved)
              } else {
                f.valueEl.value = resolved
              }
              try {
                f.valueEl.dispatchEvent(new Event('input', { bubbles: true }))
                f.valueEl.dispatchEvent(new Event('change', { bubbles: true }))
              } catch (_) {}
            }
          }
        }
      })

      // Sekmeler
      applyTabs(data.tabs || [], root, opts && opts.onTabHidden)

      // İlk değerlendirme + canlı takip (input/change delege)
      reevaluate(root)
      var onAnyInput = function () { reevaluate(root) }
      root.addEventListener('input', onAnyInput, true)
      root.addEventListener('change', onAnyInput, true)

      return { reevaluate: function () { reevaluate(root) } }
    })
    .catch(function (e) {
      try { console.warn('[FormBehavior] yükleme hatası (fail-open):', e && e.message) } catch (_) {}
      return null
    })
}

/**
 * Kaydet öncesi zorunlu-alan kontrolü. Görünür + zorunlu (statik veya requiredIf)
 * alanlardan boş olanları döndürür; boş yoksa { ok:true }.
 */
export function validateFormBehavior() {
  if (!state) return { ok: true, missing: [] }
  var scope = buildScope()
  var missing = []
  state.fields.forEach(function (f) {
    var visible = f.isVisible && evalBool(f.visibleIf, scope, true)
    if (!visible) return
    var required = f.isRequired || evalBool(f.requiredIf, scope, false)
    if (!required || !f.valueEl) return
    var val = f.valueEl.type === 'checkbox' ? (f.valueEl.checked ? '1' : '') : String(f.valueEl.value || '').trim()
    if (!val) {
      missing.push({ key: f.key, label: f.labelText || f.label, tab: f.tab, el: f.valueEl })
    }
  })
  return { ok: missing.length === 0, missing: missing }
}
