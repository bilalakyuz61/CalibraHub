/**
 * CalibraHub React Widget Mount Point
 *
 * Expose ettigimiz API:
 *   - window.CalibraHub.mountSmartBoard(element, boardConfig)            — Generic liste
 *   - window.CalibraHub.mountAdminWidgetRegistry(element, config)        — Admin widget yonetim
 *   - window.CalibraHub.mountMaterialList(element, options)              — ESKI (backwards compat)
 */
import React from 'react'
import { createRoot } from 'react-dom/client'
import ErrorBoundary from './components/ErrorBoundary'
import MaterialListEmbed from './components/MaterialCard/MaterialListEmbed'
import { SmartBoard } from './components/CalibraSmartBoard'
import AdminWidgetRegistryPanel from './components/AdminWidgetRegistry/AdminWidgetRegistryPanel'
import ShellRedesignDemo from './components/ShellRedesignDemo/ShellRedesignDemo'
import Shell from './components/Shell/Shell'
import CalibraLineItemsGrid from './components/CalibraLineItemsGrid/CalibraLineItemsGrid'
import DynamicWidgetRenderer from './components/DynamicWidgetRenderer/DynamicWidgetRenderer'
import GuideManagementPanel from './components/GuideManagement/GuideManagementPanel'
import FormManagementPanel from './components/FormManagement/FormManagementPanel'
import NotesWorkspace from './components/NotesWorkspace/NotesWorkspace'
import CompanyUserManagementPanel from './components/CompanyUserManagement/CompanyUserManagementPanel'
import InvoiceDataGrid from './components/InvoiceDataGrid/InvoiceDataGrid'
import OrgChartWorkspace from './components/OrgChart/OrgChartWorkspace'
import FixedFieldLookupBridge from './components/FixedFieldLookup/FixedFieldLookupBridge'
import { guideResolve as mountGuideResolve } from './components/DynamicWidgetRenderer/dynamicWidgetService'
import ProductCombinations from './components/ProductCombinations/ProductCombinations'
import CombinationPickerModal from './components/CalibraLineItemsGrid/CombinationPickerModal'
import { getRuntimeBindings } from './services/fieldSettingService'
import './index.css'

var mountedRoots = new Map()

/**
 * Generic SmartBoard mount — tum entity'ler icin kullanilir.
 * @param {HTMLElement} element
 * @param {object} boardConfig - SmartBoard props (title, subtitle, icon, actions, entities, ...)
 */
function mountSmartBoard(element, boardConfig) {
  boardConfig = boardConfig || {}

  if (mountedRoots.has(element)) {
    mountedRoots.get(element).unmount()
    mountedRoots.delete(element)
  }

  var root = createRoot(element)
  mountedRoots.set(element, root)

  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(SmartBoard, boardConfig)
    )
  )

  return {
    unmount: function() { root.unmount(); mountedRoots.delete(element) },
    refresh: function(newConfig) {
      root.render(
        React.createElement(ErrorBoundary, null,
          React.createElement(SmartBoard, Object.assign({}, newConfig || boardConfig, { key: Date.now() }))
        )
      )
    },
  }
}

/**
 * Eski MaterialListEmbed mount — backwards compat icin kalir.
 */
function mountMaterialList(element, options) {
  options = options || {}

  if (mountedRoots.has(element)) {
    mountedRoots.get(element).unmount()
    mountedRoots.delete(element)
  }

  var root = createRoot(element)
  mountedRoots.set(element, root)

  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(MaterialListEmbed, {
        apiUrl: options.apiUrl,
        deleteApiUrl: options.deleteApiUrl,
        onEdit: options.onEdit,
        onNew: options.onNew,
        pageSize: options.pageSize,
      })
    )
  )

  return {
    unmount: function() { root.unmount(); mountedRoots.delete(element) },
    refresh: function() {
      root.render(
        React.createElement(ErrorBoundary, null,
          React.createElement(MaterialListEmbed, Object.assign({}, options, { key: Date.now() }))
        )
      )
    },
  }
}

/**
 * AdminWidgetRegistryPanel mount — Admin widget yonetim ekrani.
 * @param {HTMLElement} element
 * @param {object} config - { screenCode, screenLabel, screenOptions, groups, fields, csrfToken }
 */
function mountAdminWidgetRegistry(element, config) {
  config = config || {}

  if (mountedRoots.has(element)) {
    mountedRoots.get(element).unmount()
    mountedRoots.delete(element)
  }

  var root = createRoot(element)
  mountedRoots.set(element, root)

  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(AdminWidgetRegistryPanel, config)
    )
  )

  return {
    unmount: function() { root.unmount(); mountedRoots.delete(element) },
  }
}

/**
 * ⚠️  SADECE STANDALONE DEMO / PREVIEW ICIN.
 *
 * Bu bilesen kendi icsel kabuğunu (Sidebar + Navbar + TabBar + StatusBar)
 * tamamen render eder. Uretim Shell'i (_Layout.cshtml → Shell.jsx) ile
 * BIRLIKTE KULLANILMAZ — "Matruska" kabuk-icinde-kabuk hatasina yol acar.
 *
 * Uretim sayfalarinda dogru secenekler:
 *   → mountSmartBoard()            liste / vitrin ekranlari
 *   → mountAdminWidgetRegistry()   admin widget yonetim ekrani
 *
 * Bu fonksiyon yanlislikla uretim Shell icinden cagrilirsa,
 * calismayi reddeder ve konsola aciklayici bir hata mesaji dusuler.
 *
 * @param {HTMLElement} element - Montaj noktasi
 * @param {object} config - { user, system, activeTab, tabs, workspace,
 *                            sidebarActiveId, sidebarExpanded,
 *                            listData, formData }
 */
function mountShellRedesignDemo(element, config) {
  // ── MATRUSKA GUARD ────────────────────────────────────────────
  // Shell.jsx mount olunca body[data-calibra-shell] atar.
  // Bu attribute varsa icine baska bir Shell gomulmesini reddet.
  if (document.body.getAttribute('data-calibra-shell') === 'true') {
    console.error(
      '[CalibraHub] \u274C mountShellRedesignDemo ENGELLENDI.\n' +
      'Bu fonksiyon uretim Shell\'i (Shell.jsx) icinde cagrilamaz.\n' +
      'Kabuk-icinde-kabuk (Matruska) hatasini onlemek icin cagri reddedildi.\n' +
      'Liste ekranlari icin mountSmartBoard() kullanin.'
    )
    return { unmount: function() {} }
  }
  // ── / MATRUSKA GUARD ─────────────────────────────────────────

  config = config || {}

  if (mountedRoots.has(element)) {
    mountedRoots.get(element).unmount()
    mountedRoots.delete(element)
  }

  var root = createRoot(element)
  mountedRoots.set(element, root)

  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(ShellRedesignDemo, { config: config })
    )
  )

  return {
    unmount: function() { root.unmount(); mountedRoots.delete(element) },
  }
}

/**
 * Shell mount — Uretim kabugu (Navbar + Sidebar + Tabs + Status Bar).
 * _Layout.cshtml tarafindan cagirilir; authenticated + non-workspace sayfalarda
 * tum ekrani sarar.
 *
 * ⚠️  MATRUSKA GUARD aktif: zaten bir Shell calisiyor ise (data-calibra-shell="true")
 * ikinci bir Shell mount reddedilir. Bu; iframe icinde workspace=1 flag'inin
 * herhangi bir nedenle kaybolmasi durumunu yakalar ve sessizce durdurur.
 *
 * @param {HTMLElement} element
 * @param {object} config
 */
function mountShell(element, config) {
  // ── MATRUSKA GUARD ────────────────────────────────────────────
  // Shell.jsx ilk mount olunca body'ye data-calibra-shell="true" atar.
  // Bu attribute gorulurse (iframe icinde bile), ikinci Shell calismaz.
  if (document.body.getAttribute('data-calibra-shell') === 'true') {
    console.error(
      '[CalibraHub] \u274C mountShell ENGELLENDI.\n' +
      'Zaten aktif bir Shell var (data-calibra-shell="true").\n' +
      'iframe icinde ?workspace=1 flag olmadan sayfa yuklenmesi olasidir.\n' +
      'Kontrol: WorkspaceRedirectPreservationMiddleware + appendWorkspaceFlag.'
    )
    return { unmount: function() {} }
  }
  // ── / MATRUSKA GUARD ─────────────────────────────────────────

  config = config || {}

  if (mountedRoots.has(element)) {
    mountedRoots.get(element).unmount()
    mountedRoots.delete(element)
  }

  var root = createRoot(element)
  mountedRoots.set(element, root)

  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(Shell, { config: config })
    )
  )

  return {
    unmount: function() { root.unmount(); mountedRoots.delete(element) },
  }
}

/**
 * CalibraLineItemsGrid mount — Satir-ici duzenlenebilir dinamik kalem grid'i.
 * DocumentEdit gibi formlarda kullanilir. Kolonlar + satirlar tamamen
 * server-side config'ten gelir ("Aptal Bilesen, Zeki Veri").
 *
 * @param {HTMLElement} element
 * @param {object} config - { columns, rows, labels, footer }
 * @param {object} options - { onRowsChange: (rows) => void }
 */
function mountLineItemsGrid(element, config, options) {
  config = config || {}
  options = options || {}

  if (mountedRoots.has(element)) {
    mountedRoots.get(element).unmount()
    mountedRoots.delete(element)
  }

  var root = createRoot(element)
  mountedRoots.set(element, root)

  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(CalibraLineItemsGrid, {
        config: config,
        onRowsChange: options.onRowsChange,
      })
    )
  )

  return {
    unmount: function() { root.unmount(); mountedRoots.delete(element) },
  }
}

/**
 * Faz C — DynamicWidgetRenderer mount helper.
 *
 * Razor edit sayfalari icindeki bir <div>'e EAV widget renderer'i yerlestirir.
 * Imperative API doner: { save, getValues, getHasWidgets, unmount }.
 * Parent sayfa save handler'i renderer'in save()'ini tetikler.
 *
 * Config:
 *   { formCode, recordId, classPrefix }
 */
function mountDynamicWidgetRenderer(element, config) {
  config = config || {}

  if (mountedRoots.has(element)) {
    var existing = mountedRoots.get(element)
    try { existing.unmount() } catch (e) { /* ignore */ }
    mountedRoots.delete(element)
  }

  var handleRef = { current: null }
  var root = createRoot(element)
  mountedRoots.set(element, root)

  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(DynamicWidgetRenderer, {
        formCode: config.formCode,
        recordId: config.recordId || '',
        classPrefix: config.classPrefix || 'mce',
        containerId: element.id,
        onMounted: function (h) { handleRef.current = h },
      })
    )
  )

  return {
    save: function (opts) {
      if (!handleRef.current) return Promise.resolve({ success: false, message: 'renderer hazir degil' })
      return handleRef.current.save(opts)
    },
    validate: function () {
      if (!handleRef.current) return { valid: true, errors: [] }
      return handleRef.current.validate()
    },
    getValues: function () {
      return handleRef.current ? handleRef.current.getValues() : {}
    },
    getHasWidgets: function () {
      return handleRef.current ? handleRef.current.getHasWidgets() : false
    },
    reload: function (opts) {
      if (!handleRef.current) return
      handleRef.current.reload(opts)
    },
    unmount: function () {
      try { root.unmount() } catch (e) { /* ignore */ }
      mountedRoots.delete(element)
    },
  }
}

/**
 * FormManagementPanel mount — Form Yöneticisi admin ekranı.
 */
function mountFormManagement(element) {
  if (mountedRoots.has(element)) {
    mountedRoots.get(element).unmount()
    mountedRoots.delete(element)
  }
  var root = createRoot(element)
  mountedRoots.set(element, root)
  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(FormManagementPanel, null)
    )
  )
  return {
    unmount: function () { root.unmount(); mountedRoots.delete(element) },
  }
}

/**
 * GuideManagementPanel mount — Rehber Merkezi admin ekrani.
 */
function mountGuideManagement(element) {
  if (mountedRoots.has(element)) {
    mountedRoots.get(element).unmount()
    mountedRoots.delete(element)
  }
  var root = createRoot(element)
  mountedRoots.set(element, root)
  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(GuideManagementPanel, null)
    )
  )
  return {
    unmount: function () { root.unmount(); mountedRoots.delete(element) },
  }
}

/**
 * NotesWorkspace mount — 3-pane Evernote/Notion UX.
 */
function mountNotesWorkspace(element) {
  if (mountedRoots.has(element)) {
    mountedRoots.get(element).unmount()
    mountedRoots.delete(element)
  }
  var root = createRoot(element)
  mountedRoots.set(element, root)
  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(NotesWorkspace, null)
    )
  )
  return {
    unmount: function () { root.unmount(); mountedRoots.delete(element) },
  }
}

/**
 * InvoiceDataGrid mount — Elektronik Belgeler yoğun veri tablosu.
 * @param {HTMLElement} element
 * @param {{ rows, antiforgeryToken, toggleProcessedUrl }} config
 */
function mountInvoiceDataGrid(element, config) {
  config = config || {}
  if (mountedRoots.has(element)) {
    mountedRoots.get(element).unmount()
    mountedRoots.delete(element)
  }
  var root = createRoot(element)
  mountedRoots.set(element, root)
  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(InvoiceDataGrid, config)
    )
  )
  return {
    unmount: function() { root.unmount(); mountedRoots.delete(element) }
  }
}

function mountCompanyUserManagement(element) {
  if (mountedRoots.has(element)) {
    mountedRoots.get(element).unmount()
    mountedRoots.delete(element)
  }
  var root = createRoot(element)
  mountedRoots.set(element, root)
  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(CompanyUserManagementPanel, null)
    )
  )
  return {
    unmount: function() { root.unmount(); mountedRoots.delete(element) }
  }
}

/**
 * OrgChartWorkspace mount — Organizasyon Semasi.
 */
function mountOrgChart(element) {
  if (mountedRoots.has(element)) {
    mountedRoots.get(element).unmount()
    mountedRoots.delete(element)
  }
  var root = createRoot(element)
  mountedRoots.set(element, root)
  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(OrgChartWorkspace, null)
    )
  )
  return {
    unmount: function () { root.unmount(); mountedRoots.delete(element) },
  }
}

/**
 * attachGuide — Tek bir form alanina dogrudan rehber butonu baglar.
 * DB'ye ihtiyac duymaz; selector ve guideCode dogrudan verilir.
 *
 * Kullanim (Razor sayfasinda @section Scripts icerisinde):
 *   CalibraHub.attachGuide('#CustomerName', 'CUSTOMERS')
 *   CalibraHub.attachGuide('#StockCode', 'MATERIALS', {
 *     fillMap: { '#StockName': 'stockName', '#UnitCode': 'defaultUnit' }
 *   })
 *
 * @param {string|HTMLElement} selectorOrEl  — CSS selector veya DOM element
 * @param {string}             guideCode     — Bagli rehber kodu (orn. 'MATERIALS')
 * @param {object}             [options]
 *   options.filterJson  — Opsiyonel constraint JSON string
 *   options.isRequired  — Zorunlu mu (boolean)
 *   options.fillMap     — { '#otherSelector': 'columnName' } secim sonrasi doldurulacak alanlar
 * @returns {{ unmount: function }}
 */
function attachGuide(selectorOrEl, guideCode, options) {
  options = options || {}
  var input = typeof selectorOrEl === 'string'
    ? document.querySelector(selectorOrEl)
    : selectorOrEl

  if (!input) {
    console.warn('[CalibraHub] attachGuide: element bulunamadi ->', selectorOrEl)
    return { unmount: function() {} }
  }

  // Elle yazilabilir — readonly kaldiriyoruz
  input.removeAttribute('readonly')
  input.style.cursor = 'text'

  var wrapper = document.createElement('div')
  wrapper.className = 'ffl-wrapper'
  input.parentNode.insertBefore(wrapper, input.nextSibling)

  var root = createRoot(wrapper)
  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(FixedFieldLookupBridge, {
        inputElement: input,
        fieldKey:     input.name || input.id || String(selectorOrEl),
        guideCode:    guideCode,
        formCode:     options.formCode   || null,
        filterJson:   options.filterJson || null,
        isRequired:   options.isRequired || false,
        fillMap:      options.fillMap    || null,
      })
    )
  )

  // Titresim + kirmizi flash + focus geri
  function shakeAndFocus(el) {
    el.classList.add('is-invalid', 'ffl-shake')
    setTimeout(function () { el.focus(); el.select() }, 0)
    // Flash: 3 kez yanip sonme
    var count = 0
    var flashInterval = setInterval(function () {
      el.style.borderColor = count % 2 === 0 ? '#ef4444' : 'transparent'
      count++
      if (count >= 6) {
        clearInterval(flashInterval)
        el.style.borderColor = ''
      }
    }, 150)
    // Shake animasyonu bitince class kaldir
    setTimeout(function () { el.classList.remove('ffl-shake') }, 500)
  }

  // Elle kod girildiginde blur'da resolve et — bulunamazsa focus'u geri ver
  var resolving = false
  input.addEventListener('blur', function (e) {
    var val = (input.value || '').trim()
    if (!val) {
      // Bos deger — temizle
      input.removeAttribute('data-value')
      input.removeAttribute('data-display')
      var fm = options.fillMap
      if (fm) {
        Object.keys(fm).forEach(function (sel) {
          var target = document.querySelector(sel)
          if (!target) return
          var tag = (target.tagName || '').toUpperCase()
          if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') {
            target.value = ''
            target.dispatchEvent(new Event('change', { bubbles: true }))
          } else {
            target.textContent = ''
          }
        })
      }
      return
    }
    if (resolving) return
    resolving = true
    mountGuideResolve(guideCode, val).then(function (result) {
      if (result && result.display) {
        input.setAttribute('data-value', val)
        input.setAttribute('data-display', result.display)
        input.classList.remove('is-invalid')
        // fillMap ile diger alanlari doldur — input'a .value, span/div'e .textContent
        var fm = options.fillMap
        if (fm && result.cells) {
          Object.keys(fm).forEach(function (sel) {
            var target = document.querySelector(sel)
            if (target && result.cells[fm[sel]] != null) {
              var val2 = String(result.cells[fm[sel]])
              var tag = (target.tagName || '').toUpperCase()
              if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') {
                target.value = val2
                target.dispatchEvent(new Event('input', { bubbles: true }))
                target.dispatchEvent(new Event('change', { bubbles: true }))
              } else {
                target.textContent = val2
              }
            }
          })
        }
      } else {
        // Kayit bulunamadi — titresim + kirmizi flash + focus geri
        shakeAndFocus(input)
      }
    }).catch(function () {
      shakeAndFocus(input)
    }).finally(function () { resolving = false })
  })

  return {
    unmount: function() {
      try { root.unmount() } catch(e) { /* ignore */ }
      if (wrapper.parentNode) wrapper.parentNode.removeChild(wrapper)
      input.removeAttribute('readonly')
      input.style.cursor = ''
    }
  }
}

/**
 * mountFixedFieldLookups — Sabit form alanlarina rehber lookup davranisi ekler.
 *
 * Runtime'da bir Razor sayfasindan cagirilir:
 *   CalibraHub.mountFixedFieldLookups('CONTACTS')
 *
 * 1) /api/field-settings/runtime/{formCode} fetch edilir
 * 2) Her binding icin DOM'da input bulunur (name, id, veya data-field-key ile)
 * 3) Input readonly yapilir, wrapper eklenir
 * 4) FixedFieldLookupBridge mount edilir (arama butonu + modal)
 *
 * @param {string} formCode — dbo.Forms.FormCode
 * @returns {Promise<{unmountAll: function}>}
 */
async function mountFixedFieldLookups(formCode) {
  if (!formCode) {
    console.warn('[CalibraHub] mountFixedFieldLookups: formCode gerekli')
    return { unmountAll: function () {} }
  }

  var roots = []

  try {
    var bindings = await getRuntimeBindings(formCode)
    if (!bindings || bindings.length === 0) return { unmountAll: function () {} }

    bindings.forEach(function (binding) {
      // Input'u bul — farkli selector stratejileri
      var input =
        document.querySelector('input[name="' + binding.fieldKey + '"]') ||
        document.querySelector('input[id="' + binding.fieldKey + '"]') ||
        document.querySelector('[data-field-key="' + binding.fieldKey + '"]')

      if (!input) {
        console.warn('[CalibraHub] mountFixedFieldLookups: input bulunamadi → ' + binding.fieldKey)
        return
      }

      // Input'a elle yazi girilmesine izin veriliyor mu?
      // data-cb-allow-typing="true" ise readonly ve input-click-to-open davranisi
      // eklenmez — kullanici ya butona tiklayarak ya da klavyeyle deger girebilir.
      var allowTyping = input.getAttribute('data-cb-allow-typing') === 'true'

      if (!allowTyping) {
        input.setAttribute('readonly', 'readonly')
        input.style.cursor = 'pointer'
      }

      // Wrapper olustur — flash'i onlemek icin gizli baslatilir,
      // wireBridgeBtn lookup btn'u gizledikten sonra gosterir
      var wrapper = document.createElement('div')
      wrapper.className = 'ffl-wrapper'
      wrapper.style.display = 'none'
      input.parentNode.insertBefore(wrapper, input.nextSibling)

      // React mount
      var root = createRoot(wrapper)
      roots.push({ root: root, wrapper: wrapper })

      root.render(
        React.createElement(ErrorBoundary, null,
          React.createElement(FixedFieldLookupBridge, {
            inputElement: input,
            formCode: formCode,
            fieldKey: binding.fieldKey,
            guideCode: binding.guideCode,
            filterJson: binding.filterJson,
            isRequired: binding.isRequired,
          })
        )
      )

      // Readonly alanlarda input'a tiklayinca da modal acilsin.
      // Elle yazilabilir alanlarda bu eklenmez — yoksa kullanici her tikladiginda
      // modal aciliyor.
      if (!allowTyping) {
        input.addEventListener('click', function () {
          var btn = wrapper.querySelector('.ffl-lookup-btn')
          if (btn) btn.click()
        })
      }
    })
  } catch (e) {
    console.error('[CalibraHub] mountFixedFieldLookups hata:', e)
  }

  return {
    unmountAll: function () {
      roots.forEach(function (r) {
        try { r.root.unmount() } catch (e) { /* ignore */ }
        if (r.wrapper.parentNode) r.wrapper.parentNode.removeChild(r.wrapper)
      })
      roots = []
    },
  }
}

/**
 * ProductCombinations mount — Konfigurasyon Tanimlama ekrani.
 * @param {HTMLElement} element
 * @param {{ csrfToken: string, initialStockCode?: string }} config
 */
function mountProductCombinations(element, config) {
  config = config || {}
  if (mountedRoots.has(element)) {
    mountedRoots.get(element).unmount()
    mountedRoots.delete(element)
  }
  var root = createRoot(element)
  mountedRoots.set(element, root)
  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(ProductCombinations, {
        csrfToken: config.csrfToken || '',
        initialStockCode: config.initialStockCode || '',
      })
    )
  )
  return {
    unmount: function() { root.unmount(); mountedRoots.delete(element) }
  }
}

/**
 * openCombinationPicker — Global API. Razor sayfalarindan kombinasyon rehberi modalini acar.
 *
 * Ornek (BOM ekrani):
 *   CalibraHub.openCombinationPicker('MAT123', {
 *     currentCode: 'CFG-42',
 *     onApply: function(configId, code, details) { ... },
 *   })
 *
 * @param {string} materialCode  — Secili malzemenin kodu (bos ise "once malzeme sec" mesaji)
 * @param {{ currentCode?: string, currentDetails?: object[], onApply?: function, onClose?: function }} opts
 * @returns {{ close: function }}
 */
function openCombinationPicker(materialCode, opts) {
  opts = opts || {}
  var container = document.createElement('div')
  container.setAttribute('data-cb-combo-picker', '')
  document.body.appendChild(container)
  var root = createRoot(container)

  function cleanup() {
    try { root.unmount() } catch (e) { /* ignore */ }
    if (container.parentNode) container.parentNode.removeChild(container)
  }

  function handleClose() {
    cleanup()
    if (typeof opts.onClose === 'function') opts.onClose()
  }

  function handleApply(configId, code, details) {
    cleanup()
    if (typeof opts.onApply === 'function') opts.onApply(configId, code, details)
  }

  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(CombinationPickerModal, {
        materialCode: materialCode || '',
        materialName: opts.materialName || '',
        currentCode: opts.currentCode || null,
        currentDetails: Array.isArray(opts.currentDetails) ? opts.currentDetails : [],
        onApply: handleApply,
        onClose: handleClose,
      })
    )
  )

  return { close: handleClose }
}

window.CalibraHub = window.CalibraHub || {}
window.CalibraHub.mountSmartBoard = mountSmartBoard
window.CalibraHub.mountMaterialList = mountMaterialList
window.CalibraHub.mountAdminWidgetRegistry = mountAdminWidgetRegistry
window.CalibraHub.mountShellRedesignDemo = mountShellRedesignDemo
window.CalibraHub.mountShell = mountShell
window.CalibraHub.mountLineItemsGrid = mountLineItemsGrid
window.CalibraHub.mountDynamicWidgetRenderer = mountDynamicWidgetRenderer
window.CalibraHub.mountGuideManagement = mountGuideManagement
window.CalibraHub.mountFormManagement = mountFormManagement
window.CalibraHub.mountNotesWorkspace = mountNotesWorkspace
window.CalibraHub.mountCompanyUserManagement = mountCompanyUserManagement
window.CalibraHub.mountInvoiceDataGrid = mountInvoiceDataGrid
window.CalibraHub.mountOrgChart = mountOrgChart
window.CalibraHub.mountFixedFieldLookups = mountFixedFieldLookups
window.CalibraHub.openCombinationPicker = openCombinationPicker
window.CalibraHub.attachGuide = attachGuide
// Lookup cache temizleme — sekmeler arasi veri senkronizasyonu icin export.
// Ornek: malzeme karti sekmesinde birim degisti → satis teklifi sekmesi focus aldiginda
// bu fonksiyonu cagirip /Sales/GetMaterials + /Sales/GetMaterialUnits cache'lerini invalide eder.
import('./components/CalibraLineItemsGrid/useLookup').then(function (m) {
  window.CalibraHub.clearLookupCache = m.clearLookupCache
})
window.CalibraHub.mountProductCombinations = mountProductCombinations
