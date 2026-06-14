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
import { DocDesignerApp, DocDesignerList } from './components/DocDesigner'
import AdminWidgetRegistryPanel from './components/AdminWidgetRegistry/AdminWidgetRegistryPanel'
import ShellRedesignDemo from './components/ShellRedesignDemo/ShellRedesignDemo'
import Shell from './components/Shell/Shell'
import CalibraLineItemsGrid from './components/CalibraLineItemsGrid/CalibraLineItemsGrid'
import DynamicWidgetRenderer from './components/DynamicWidgetRenderer/DynamicWidgetRenderer'
import FormManagementPanel from './components/FormManagement/FormManagementPanel'
import FormManagementTree from './components/FormManagement/FormManagementTree'
import NotesWorkspace from './components/NotesWorkspace/NotesWorkspace'
import CompanyUserManagementPanel from './components/CompanyUserManagement/CompanyUserManagementPanel'
import InvoiceDataGrid from './components/InvoiceDataGrid/InvoiceDataGrid'
import OrgChartWorkspace from './components/OrgChart/OrgChartWorkspace'
import WorkflowEditor from './components/WorkflowEditor/WorkflowEditor'
import ApprovalFlowDesigner from './components/ApprovalFlowDesigner/ApprovalFlowDesigner'
import BpmFormDesigner from './components/BpmForm/BpmFormDesigner'
import BpmFormFiller from './components/BpmForm/BpmFormFiller'
import WhatsAppMessenger from './components/WhatsAppMessenger/WhatsAppMessenger'
import './components/WhatsAppMessenger/WhatsAppMessenger.css'
import CardGroupTree from './components/CardGroupTree/CardGroupTree'
import './components/CardGroupTree/CardGroupTree.css'
import LocationTree from './components/LocationTree/LocationTree'
import './components/LocationTree/LocationTree.css'
import IntegrationsList from './components/IntegrationWizard/IntegrationsList'
import IntegrationWizard from './components/IntegrationWizard/IntegrationWizard'
import IntegrationEndpointsList from './components/IntegrationWizard/IntegrationEndpointsList'
import IntegrationsHub from './components/IntegrationWizard/IntegrationsHub'
import IntegrationQueue from './components/IntegrationWizard/IntegrationQueue'
import './components/IntegrationWizard/IntegrationWizard.css'
import RoutingTree from './components/RoutingTree/RoutingTree'
import OperationGrid from './components/OperationGrid/OperationGrid'
import FixedFieldLookupBridge from './components/FixedFieldLookup/FixedFieldLookupBridge'
import ProductCombinations from './components/ProductCombinations/ProductCombinations'
import CombinationPickerModal from './components/CalibraLineItemsGrid/CombinationPickerModal'
import ConvertToOrdersModal from './components/ConvertToOrdersModal/ConvertToOrdersModal'
import ConvertSingleQuoteModal from './components/ConvertToOrdersModal/ConvertSingleQuoteModal'
import PriceGroupContactsModal from './components/PriceGroupContactsModal/PriceGroupContactsModal'
import GuideLookupModal from './components/GuideLookup/GuideLookupModal'
import FieldSettingsForm from './components/CalibraLineItemsGrid/FieldSettingsForm'
import { Settings as SettingsIcon } from 'lucide-react'
import { adaptFormatJson, extractValueDisplay } from './components/GuideLookup/guideLookupAdapters'
import { getRuntimeBindings as getRuntimeBindingsForGuide } from './services/fieldSettingService'
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
        layout: config.layout || 'stacked',
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
 * FormManagementTree mount — Module → SubModule → SmartCard ağaç görünümü.
 */
function mountFormManagementTree(element, config) {
  config = config || {}
  if (mountedRoots.has(element)) {
    mountedRoots.get(element).unmount()
    mountedRoots.delete(element)
  }
  var root = createRoot(element)
  mountedRoots.set(element, root)
  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(FormManagementTree, { config: config })
    )
  )
  return {
    unmount: function () { root.unmount(); mountedRoots.delete(element) },
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
 * WorkflowEditor mount — Gorsel workflow tanim editoru.
 */
function mountWorkflowEditor(element, opts) {
  if (mountedRoots.has(element)) {
    mountedRoots.get(element).unmount()
    mountedRoots.delete(element)
  }
  var root = createRoot(element)
  mountedRoots.set(element, root)
  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(WorkflowEditor, { definitionId: (opts || {}).definitionId || null })
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

  // DOM input'u gizle — kart kendi input'unu render eder. DOM input form
  // submit ve fillMap ile uyumluluk icin DOM'da kalir.
  var prevDisplay  = input.style.display
  var prevCursor   = input.style.cursor
  var prevReadOnly = input.hasAttribute('readonly')
  input.style.display = 'none'
  input.removeAttribute('readonly')

  var wrapper = document.createElement('div')
  wrapper.className = 'gl-wrapper'
  input.parentNode.insertBefore(wrapper, input)

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
        resolveOnBlur: true,
      })
    )
  )

  return {
    unmount: function() {
      try { root.unmount() } catch(e) { /* ignore */ }
      if (wrapper.parentNode) wrapper.parentNode.removeChild(wrapper)
      input.style.display = prevDisplay || ''
      input.style.cursor  = prevCursor  || ''
      if (!prevReadOnly) input.removeAttribute('readonly')
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

      // DOM input'u gizle — sadece form submit icin DOM'da kalir.
      // Kullaniciya gorunen kontrol LookupCard (kart icindeki React-input).
      // Onceki cursor/readonly/style override'larini koru ki tema CSS'leri
      // donmus durumdan etkilenmesin.
      var prevDisplay = input.style.display
      var prevCursor  = input.style.cursor
      var prevReadOnly = input.hasAttribute('readonly')
      input.style.display = 'none'

      // Kart wrapper'i — input'un yerine yerlestir (insertBefore: input ondan sonra gizli kalir)
      var wrapper = document.createElement('div')
      wrapper.className = 'gl-wrapper'
      input.parentNode.insertBefore(wrapper, input)

      // React mount
      var root = createRoot(wrapper)
      roots.push({ root: root, wrapper: wrapper, input: input,
                   prevDisplay: prevDisplay, prevCursor: prevCursor, prevReadOnly: prevReadOnly })

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
    })
  } catch (e) {
    console.error('[CalibraHub] mountFixedFieldLookups hata:', e)
  }

  return {
    unmountAll: function () {
      roots.forEach(function (r) {
        try { r.root.unmount() } catch (e) { /* ignore */ }
        if (r.wrapper.parentNode) r.wrapper.parentNode.removeChild(r.wrapper)
        if (r.input) {
          r.input.style.display = r.prevDisplay || ''
          r.input.style.cursor  = r.prevCursor  || ''
          if (!r.prevReadOnly) r.input.removeAttribute('readonly')
        }
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

/**
 * mountLookupForInput — Mevcut DOM input uzerine FixedFieldLookupBridge (LookupCard)
 * mount eder. Tek-input kullanim: dialog/modal icindeki bir input'u "tip 1 rehber"
 * (standart) yapar. mountFixedFieldLookups gibi formCode/binding gerektirmez —
 * caller direkt guideCode verir.
 *
 * Ornek (BOMs Islemler modal'i, Hedef Mamul Kodu):
 *   var lookup = CalibraHub.mountLookupForInput({
 *     inputElement: document.getElementById('ptaTargetCode'),
 *     guideCode: 'ITEMS',
 *     isRequired: true,
 *   })
 *   // sonra: lookup.clear() — bridge state'i temizle, lookup.unmount() — kaldir
 */
function mountLookupForInput(opts) {
  opts = opts || {}
  var input = opts.inputElement
  if (!input) {
    console.warn('[CalibraHub] mountLookupForInput: inputElement gerekli')
    return { unmount: function() {}, clear: function() {} }
  }
  var guideCode = opts.guideCode || ''
  if (!guideCode) {
    console.warn('[CalibraHub] mountLookupForInput: guideCode gerekli')
    return { unmount: function() {}, clear: function() {} }
  }

  var prevDisplay = input.style.display
  input.style.display = 'none'

  var wrapper = document.createElement('div')
  wrapper.className = 'gl-wrapper'
  input.parentNode.insertBefore(wrapper, input)
  var root = createRoot(wrapper)

  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(FixedFieldLookupBridge, {
        inputElement: input,
        formCode: opts.formCode || null,
        fieldKey: opts.fieldKey || null,
        guideCode: guideCode,
        filterJson: opts.filterJson || null,
        formatJson: opts.formatJson || null,
        isRequired: !!opts.isRequired,
      })
    )
  )

  return {
    unmount: function() {
      try { root.unmount() } catch (e) { /* ignore */ }
      if (wrapper.parentNode) wrapper.parentNode.removeChild(wrapper)
      input.style.display = prevDisplay || ''
    },
    clear: function() {
      try { input.dispatchEvent(new CustomEvent('ffl-clear')) } catch (e) { /* ignore */ }
    }
  }
}

window.CalibraHub = window.CalibraHub || {}

// ── Global toast (sağ alt köşe) ─────────────────────────────────────────
// Tüm uyarı/hata mesajları buradan akar. alert()/confirm() yerine kullanılır.
// Kullanım:
//   window.CalibraHub.toast('Kaydedildi', 'ok')           // yeşil
//   window.CalibraHub.toast('Hata: ...', 'err')           // kırmızı
//   window.CalibraHub.toast('Dikkat: ...', 'warn')        // sarı
//   window.CalibraHub.toast('Bilgi', 'info')              // mavi (default)
window.CalibraHub.toast = (function() {
  var hostId = '__calibra_toast_host__';
  var styleId = '__calibra_toast_styles__';

  function ensureStyles() {
    if (document.getElementById(styleId)) return;
    var s = document.createElement('style');
    s.id = styleId;
    s.textContent = (
      '#' + hostId + '{position:fixed;right:20px;bottom:20px;z-index:99999;display:flex;flex-direction:column;gap:10px;align-items:flex-end;pointer-events:none;}' +
      '.calibra-toast{pointer-events:auto;min-width:280px;max-width:420px;padding:12px 16px;border-radius:10px;font-size:14px;font-weight:500;line-height:1.4;display:flex;align-items:flex-start;gap:10px;box-shadow:0 12px 32px rgba(0,0,0,.35);border:1px solid rgba(255,255,255,.06);transform:translateX(20px);opacity:0;transition:transform .22s ease, opacity .22s ease;backdrop-filter:blur(10px);font-family:"Segoe UI",system-ui,-apple-system,sans-serif;}' +
      '.calibra-toast.show{transform:translateX(0);opacity:1;}' +
      '.calibra-toast.leaving{transform:translateX(20px);opacity:0;}' +
      '.calibra-toast .calibra-toast-icon{flex-shrink:0;font-size:18px;line-height:1;margin-top:1px;}' +
      '.calibra-toast .calibra-toast-msg{flex:1;min-width:0;overflow-wrap:break-word;}' +
      '.calibra-toast .calibra-toast-close{flex-shrink:0;background:transparent;border:none;color:inherit;cursor:pointer;font-size:16px;opacity:.6;padding:0 4px;line-height:1;}' +
      '.calibra-toast .calibra-toast-close:hover{opacity:1;}' +
      '.calibra-toast.kind-ok{background:rgba(34,197,94,.96);color:#052e16;}' +
      '.calibra-toast.kind-err{background:rgba(239,68,68,.96);color:#450a0a;}' +
      '.calibra-toast.kind-warn{background:rgba(251,191,36,.96);color:#451a03;}' +
      '.calibra-toast.kind-info{background:rgba(59,130,246,.96);color:#0c1e42;}'
    );
    document.head.appendChild(s);
  }

  function ensureHost() {
    var host = document.getElementById(hostId);
    if (!host) {
      host = document.createElement('div');
      host.id = hostId;
      document.body.appendChild(host);
    }
    return host;
  }

  function iconFor(kind) {
    if (kind === 'ok')   return '✓';
    if (kind === 'err')  return '✕';
    if (kind === 'warn') return '!';
    return 'i';
  }

  function show(message, kind, opts) {
    if (!message) return;
    ensureStyles();
    var host = ensureHost();
    var k = (kind || 'info').toLowerCase();
    if (k !== 'ok' && k !== 'err' && k !== 'warn' && k !== 'info') k = 'info';
    var el = document.createElement('div');
    el.className = 'calibra-toast kind-' + k;
    var icon = document.createElement('span');
    icon.className = 'calibra-toast-icon';
    icon.textContent = iconFor(k);
    var msg = document.createElement('span');
    msg.className = 'calibra-toast-msg';
    msg.textContent = String(message);
    var close = document.createElement('button');
    close.type = 'button';
    close.className = 'calibra-toast-close';
    close.setAttribute('aria-label', 'Kapat');
    close.textContent = '×';
    el.appendChild(icon); el.appendChild(msg); el.appendChild(close);
    host.appendChild(el);
    requestAnimationFrame(function() { el.classList.add('show'); });
    var ttl = (opts && opts.ttl != null) ? opts.ttl : (k === 'err' ? 6000 : 4000);
    var dismissed = false;
    function dismiss() {
      if (dismissed) return;
      dismissed = true;
      el.classList.add('leaving'); el.classList.remove('show');
      setTimeout(function() { if (el.parentNode) el.parentNode.removeChild(el); }, 260);
    }
    close.addEventListener('click', dismiss);
    if (ttl > 0) setTimeout(dismiss, ttl);
    return { dismiss: dismiss };
  }

  return show;
})();

/**
 * unmountAllInside — PJAX swap oncesi cagrilir. Verilen DOM subtree icindeki
 * tum aktif React root'lari guvenle unmount eder. Boylece eski mount'lardan
 * arta kalan event listener / interval / subscription temizlenir.
 *
 * @param {HTMLElement} rootElement
 * @returns {number} unmount edilen root sayisi
 */
function unmountAllInside(rootElement) {
  if (!rootElement) return 0
  var count = 0
  var toRemove = []
  mountedRoots.forEach(function (root, el) {
    if (el === rootElement || (rootElement.contains && rootElement.contains(el))) {
      toRemove.push(el)
    }
  })
  for (var i = 0; i < toRemove.length; i++) {
    var el = toRemove[i]
    try {
      mountedRoots.get(el).unmount()
      count++
    } catch (e) { /* ignore */ }
    mountedRoots.delete(el)
  }
  return count
}
window.CalibraHub.unmountAllInside = unmountAllInside

window.CalibraHub.mountSmartBoard = mountSmartBoard
window.CalibraHub.mountMaterialList = mountMaterialList
window.CalibraHub.mountAdminWidgetRegistry = mountAdminWidgetRegistry
window.CalibraHub.mountShellRedesignDemo = mountShellRedesignDemo
window.CalibraHub.mountShell = mountShell
window.CalibraHub.mountLineItemsGrid = mountLineItemsGrid
window.CalibraHub.mountDynamicWidgetRenderer = mountDynamicWidgetRenderer
window.CalibraHub.mountFormManagement = mountFormManagement
window.CalibraHub.mountFormManagementTree = mountFormManagementTree
window.CalibraHub.mountNotesWorkspace = mountNotesWorkspace
window.CalibraHub.mountCompanyUserManagement = mountCompanyUserManagement
window.CalibraHub.mountInvoiceDataGrid = mountInvoiceDataGrid
window.CalibraHub.mountOrgChart = mountOrgChart
window.CalibraHub.mountWorkflowEditor = mountWorkflowEditor
window.CalibraHub.mountFixedFieldLookups = mountFixedFieldLookups
window.CalibraHub.mountLookupForInput = mountLookupForInput
window.CalibraHub.openCombinationPicker = openCombinationPicker

/**
 * openConvertToOrdersModal — Onayli teklifleri siparise donusturen modal'i acar.
 * SmartBoard toolbar action'i (trigger: "convert-orders-modal") tetikledigi global fonksiyon.
 *
 * @param {{ onSuccess?: function, onClose?: function }} opts
 */
function openConvertToOrdersModal(opts) {
  opts = opts || {}
  var container = document.createElement('div')
  container.setAttribute('data-cb-convert-orders', '')
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

  function handleSuccess(result) {
    if (typeof opts.onSuccess === 'function') opts.onSuccess(result)
  }

  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(ConvertToOrdersModal, {
        onClose: handleClose,
        onSuccess: handleSuccess,
      })
    )
  )

  return { close: handleClose }
}
window.CalibraHub.openConvertToOrdersModal = openConvertToOrdersModal

/**
 * openConvertSingleQuoteModal — Tek teklifi siparise donusturen modali acar.
 * SmartCard'taki "Siparise Donustur" extraAction (trigger) tarafindan cagrilir.
 *
 * @param {{ quoteId, quoteNumber?, contactName?, grandTotal?, currency?, lineCount?,
 *          onSuccess?, onClose? }} opts
 */
function openConvertSingleQuoteModal(opts) {
  opts = opts || {}
  if (!opts.quoteId) {
    console.warn('[CalibraHub] openConvertSingleQuoteModal: quoteId gerekli')
    return { close: function () {} }
  }
  var container = document.createElement('div')
  container.setAttribute('data-cb-convert-single', '')
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
  function handleSuccess(result) {
    if (typeof opts.onSuccess === 'function') opts.onSuccess(result)
  }

  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(ConvertSingleQuoteModal, {
        quoteId: opts.quoteId,
        quoteNumber: opts.quoteNumber,
        contactName: opts.contactName,
        grandTotal: opts.grandTotal,
        currency: opts.currency,
        lineCount: opts.lineCount,
        onClose: handleClose,
        onSuccess: handleSuccess,
      })
    )
  )

  return { close: handleClose }
}
window.CalibraHub.openConvertSingleQuoteModal = openConvertSingleQuoteModal

/**
 * openPriceGroupContactsModal — Fiyat grubu ile cari kartlari eslestiren modal'i acar.
 * SmartCard'taki "Cari Eslestir" extraAction (trigger = "price-group-contacts-modal")
 * tarafindan cagrilir.
 *
 * @param {{ groupId, groupCode?, groupName?, onClose?, onChanged? }} opts
 */
function openPriceGroupContactsModal(opts) {
  opts = opts || {}
  if (!opts.groupId) {
    console.warn('[CalibraHub] openPriceGroupContactsModal: groupId gerekli')
    return { close: function () {} }
  }
  var container = document.createElement('div')
  container.setAttribute('data-cb-pg-contacts', '')
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

  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(PriceGroupContactsModal, {
        groupId:   opts.groupId,
        groupCode: opts.groupCode,
        groupName: opts.groupName,
        onClose:   handleClose,
      })
    )
  )

  return { close: handleClose }
}
window.CalibraHub.openPriceGroupContactsModal = openPriceGroupContactsModal

/**
 * GuideLookupHost — openGuideLookup'in icinde mount edilen stateful sarmalayici.
 * 2026-06-02: formCode + fieldKey verilirse header'da "Alan Ayarlari" Settings
 * butonu cikar, tikladiginda FieldSettingsForm acilir (FixedFieldLookupBridge ile
 * ayni pattern). Ayarlar kaydedilince schemaVersion++ ile modal kolonlari yenilenir.
 */
function GuideLookupHost(props) {
  var [settingsOpen, setSettingsOpen] = React.useState(false)
  var [schemaVersion, setSchemaVersion] = React.useState(0)
  var [formatJson, setFormatJson] = React.useState(props.formatJson || null)
  // 2026-06-02: Admin "Alan Ayarlari"ndan view degistirilirse setGuideCode ile
  // state'i guncelliyoruz → GuideLookupModal yeni guideCode ile re-mount olur.
  // Ilk yüklemede de FldSet binding'i varsa onu kullanir (kullanici kayitli
  // tercihini her acilista almali — props.guideCode sadece "default" suggestion).
  var [guideCode, setGuideCode] = React.useState(props.guideCode)
  var [filterJson, setFilterJson] = React.useState(props.filterJson || null)

  // FormCode + fieldKey verildiyse, mevcut FldSet binding'ini ilk acilista cek;
  // kayitli viewName varsa props.guideCode'un yerine onu kullan.
  React.useEffect(function () {
    if (!props.formCode || !props.fieldKey) return
    var alive = true
    getRuntimeBindingsForGuide(props.formCode)
      .then(function (bindings) {
        if (!alive) return
        var fresh = (bindings || []).find(function (b) { return b && b.fieldKey === props.fieldKey })
        if (!fresh) return
        // PR2+: ViewName primary; guideCode = ViewName aliasi
        var savedView = fresh.viewName || fresh.guideCode
        if (savedView && savedView !== guideCode) setGuideCode(savedView)
        if (fresh.filterJson != null && fresh.filterJson !== filterJson) setFilterJson(fresh.filterJson)
        if (fresh.formatJson != null && fresh.formatJson !== formatJson) setFormatJson(fresh.formatJson)
      })
      .catch(function () { /* sessiz — binding yoksa props.guideCode kalir */ })
    return function () { alive = false }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [props.formCode, props.fieldKey])

  var settingsColumnRef = React.useRef({
    key: props.fieldKey || guideCode,
    label: props.guideLabel || guideCode,
    formCode: props.formCode || null,
    guideCode: guideCode,
    filterJson: filterJson,
    formatJson: formatJson,
  })
  settingsColumnRef.current = {
    key: props.fieldKey || guideCode,
    label: props.guideLabel || guideCode,
    formCode: props.formCode || null,
    guideCode: guideCode,
    filterJson: filterJson,
    formatJson: formatJson,
  }

  var columnsAdapter = function (schemaCols) { return adaptFormatJson(formatJson, schemaCols) }

  var headerActions = props.formCode ? React.createElement(
    'button',
    {
      type: 'button',
      onClick: function () { setSettingsOpen(true) },
      title: 'Alan Ayarları',
      className: 'gl-settings-btn',
    },
    React.createElement(SettingsIcon, { size: 15, strokeWidth: 2 })
  ) : null

  return React.createElement(
    React.Fragment,
    null,
    React.createElement(GuideLookupModal, {
      guideCode: guideCode,
      guideLabel: props.guideLabel || null,
      columnsAdapter: columnsAdapter,
      open: true,
      onClose: props.onClose,
      onPick: props.onPick,
      staticConstraint: filterJson,
      schemaVersion: schemaVersion,
      headerActions: headerActions,
    }),
    props.formCode ? React.createElement(FieldSettingsForm, {
      column: settingsColumnRef.current,
      isOpen: settingsOpen,
      onClose: function () {
        setSettingsOpen(false)
        // FieldSettingsForm Kaydet sonrasi column.guideCode/viewName/filterJson/formatJson
        // alanlarini mutate ediyor (handleSaved end of method). Burada state'e cek:
        var nextView = settingsColumnRef.current.viewName
                    || settingsColumnRef.current.guideCode
        if (nextView && nextView !== guideCode) setGuideCode(nextView)
        if (settingsColumnRef.current.filterJson !== filterJson) {
          setFilterJson(settingsColumnRef.current.filterJson)
        }
        if (settingsColumnRef.current.formatJson !== formatJson) {
          setFormatJson(settingsColumnRef.current.formatJson)
        }
        setSchemaVersion(function (v) { return v + 1 })
      },
    }) : null
  )
}

/**
 * openGuideLookup — Standart rehber (Tip 1) modali'ni acar. Razor sayfalarindan
 * cagrilir; ornek: bir butona tikladiginda mevcut bir kayda navigasyon.
 *
 * @param {string} guideCode  — Standart rehber kodu (orn. 'SALES_ORDERS')
 * @param {{
 *   formatJson?: string,
 *   filterJson?: string,
 *   formCode?: string,     // 2026-06-02: verilirse header'a "Alan Ayarlari" butonu konur
 *   fieldKey?:  string,    // FieldSettingsForm icin field id; yoksa guideCode kullanilir
 *   onPick?: function, onClose?: function, guideLabel?: string
 * }} opts
 *   onPick(row): row = { value, display, cells } — secilen satir
 */
function openGuideLookup(guideCode, opts) {
  opts = opts || {}
  if (!guideCode) {
    console.warn('[CalibraHub] openGuideLookup: guideCode gerekli')
    return { close: function() {} }
  }
  var container = document.createElement('div')
  container.setAttribute('data-cb-guide-lookup', '')
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

  function handlePick(row) {
    cleanup()
    if (typeof opts.onPick === 'function') opts.onPick(row)
  }

  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(GuideLookupHost, {
        guideCode: guideCode,
        guideLabel: opts.guideLabel || null,
        formatJson: opts.formatJson || null,
        filterJson: opts.filterJson || null,
        formCode: opts.formCode || null,
        fieldKey: opts.fieldKey || null,
        onClose: handleClose,
        onPick: handlePick,
      })
    )
  )

  return { close: handleClose }
}
window.CalibraHub.openGuideLookup = openGuideLookup
// extractValueDisplay'i Razor sayfalarina expose — onPick callback'i row.cells'i okurken kolaylik
window.CalibraHub.guideExtractValueDisplay = extractValueDisplay
window.CalibraHub.attachGuide = attachGuide
// Lookup cache temizleme — sekmeler arasi veri senkronizasyonu icin export.
// Ornek: malzeme karti sekmesinde birim degisti → satis teklifi sekmesi focus aldiginda
// bu fonksiyonu cagirip /Sales/GetMaterials + /Sales/GetMaterialUnits cache'lerini invalide eder.
import('./components/CalibraLineItemsGrid/useLookup').then(function (m) {
  window.CalibraHub.clearLookupCache = m.clearLookupCache
})
window.CalibraHub.mountProductCombinations = mountProductCombinations

/**
 * WhatsAppMessenger mount — WhatsApp Web tarzi sohbet UI'i.
 * @param {HTMLElement} element
 * @param {{ initialPhone?: string, csrfToken?: string }} config
 */
function mountWhatsAppMessenger(element, config) {
  config = config || {}
  if (mountedRoots.has(element)) {
    mountedRoots.get(element).unmount()
    mountedRoots.delete(element)
  }
  var root = createRoot(element)
  mountedRoots.set(element, root)
  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(WhatsAppMessenger, {
        initialPhone: config.initialPhone || null,
        csrfToken: config.csrfToken || '',
      })
    )
  )
  return {
    unmount: function() { root.unmount(); mountedRoots.delete(element) }
  }
}
window.CalibraHub.mountWhatsAppMessenger = mountWhatsAppMessenger

/**
 * CardGroupTree mount — Malzeme/Cari sekmeleri + hiyerarşik ağaç görünümü.
 * @param {HTMLElement} element
 * @param {object} treeConfig - { cardTypes, newUrl, deleteApiUrl }
 */
function mountCardGroupTree(element, treeConfig) {
  treeConfig = treeConfig || {}
  if (mountedRoots.has(element)) {
    mountedRoots.get(element).unmount()
    mountedRoots.delete(element)
  }
  var root = createRoot(element)
  mountedRoots.set(element, root)
  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(CardGroupTree, { config: treeConfig })
    )
  )
  return {
    unmount: function() { root.unmount(); mountedRoots.delete(element) }
  }
}
window.CalibraHub.mountCardGroupTree = mountCardGroupTree

/**
 * DocDesigner mount — band-layout dokuman tasarimcisi (editor).
 */
function mountDocDesigner(element, options) {
  options = options || {}
  var root = createRoot(element)
  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(DocDesignerApp, { layoutId: options.layoutId || 0 })
    )
  )
  return { unmount: function () { root.unmount() } }
}
function mountDocDesignerList(element) {
  var root = createRoot(element)
  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(DocDesignerList, null)
    )
  )
  return { unmount: function () { root.unmount() } }
}
window.CalibraHub.mountDocDesigner = mountDocDesigner
window.CalibraHub.mountDocDesignerList = mountDocDesignerList

/**
 * RoutingTree mount — Rota Tanımlamaları ağaç görünümü (Rota → Operasyonlar).
 * @param {HTMLElement} element
 * @param {object} config - { routings, urls: { save, delete, operationsLookup, refresh } }
 */
function mountRoutingTree(element, config) {
  config = config || {}
  if (mountedRoots.has(element)) {
    mountedRoots.get(element).unmount()
    mountedRoots.delete(element)
  }
  var root = createRoot(element)
  mountedRoots.set(element, root)
  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(RoutingTree, { config: config })
    )
  )
  return {
    unmount: function() { root.unmount(); mountedRoots.delete(element) }
  }
}
window.CalibraHub.mountRoutingTree = mountRoutingTree

/**
 * OperationGrid mount — Operasyon Tanımlamaları kart grid görünümü.
 * @param {HTMLElement} element
 * @param {object} config - { operations: [...], urls: { save, delete, refresh } }
 */
function mountOperationGrid(element, config) {
  config = config || {}
  if (mountedRoots.has(element)) {
    mountedRoots.get(element).unmount()
    mountedRoots.delete(element)
  }
  var root = createRoot(element)
  mountedRoots.set(element, root)
  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(OperationGrid, { config: config })
    )
  )
  return {
    unmount: function() { root.unmount(); mountedRoots.delete(element) }
  }
}
window.CalibraHub.mountOperationGrid = mountOperationGrid

/**
 * LocationTree mount — Lokasyon Tanımlamaları kart-ağaç görünümü.
 * @param {HTMLElement} element
 * @param {object} config - { roots, types, saveUrl, deleteUrl, refreshUrl, maxDepth }
 */
function mountLocationTree(element, config) {
  config = config || {}
  if (mountedRoots.has(element)) {
    mountedRoots.get(element).unmount()
    mountedRoots.delete(element)
  }
  var root = createRoot(element)
  mountedRoots.set(element, root)
  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(LocationTree, { config: config })
    )
  )
  return {
    unmount: function() { root.unmount(); mountedRoots.delete(element) }
  }
}
window.CalibraHub.mountLocationTree = mountLocationTree

/**
 * IntegrationsList mount — Entegrasyon liste sayfası (SmartBoard-stilinde kart liste).
 * @param {HTMLElement} element
 * @param {object} config - { apiBase, wizardUrlNew, wizardUrlEdit }
 */
function mountIntegrationsList(element, config) {
  config = config || {}
  if (mountedRoots.has(element)) {
    mountedRoots.get(element).unmount()
    mountedRoots.delete(element)
  }
  var root = createRoot(element)
  mountedRoots.set(element, root)
  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(IntegrationsList, { config: config })
    )
  )
  return { unmount: function() { root.unmount(); mountedRoots.delete(element) } }
}
window.CalibraHub.mountIntegrationsList = mountIntegrationsList

/**
 * IntegrationWizard mount — 5-step entegrasyon kurgu sihirbazı.
 * @param {HTMLElement} element
 * @param {object} config - { apiBase, integrationId, listUrl }
 */
function mountIntegrationWizard(element, config) {
  config = config || {}
  if (mountedRoots.has(element)) {
    mountedRoots.get(element).unmount()
    mountedRoots.delete(element)
  }
  var root = createRoot(element)
  mountedRoots.set(element, root)
  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(IntegrationWizard, { config: config })
    )
  )
  return { unmount: function() { root.unmount(); mountedRoots.delete(element) } }
}
window.CalibraHub.mountIntegrationWizard = mountIntegrationWizard

/**
 * IntegrationEndpointsList mount — REST endpoint katalog admin sayfası.
 */
function mountIntegrationEndpointsList(element, config) {
  config = config || {}
  if (mountedRoots.has(element)) {
    mountedRoots.get(element).unmount()
    mountedRoots.delete(element)
  }
  var root = createRoot(element)
  mountedRoots.set(element, root)
  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(IntegrationEndpointsList, { config: config })
    )
  )
  return { unmount: function() { root.unmount(); mountedRoots.delete(element) } }
}
window.CalibraHub.mountIntegrationEndpointsList = mountIntegrationEndpointsList

/**
 * IntegrationsHub mount — Tek sayfa, 2 tab: Entegrasyonlar + Endpointler.
 * @param {HTMLElement} element
 * @param {object} config - { apiBase, wizardUrlNew, wizardUrlEdit, initialTab? }
 */
function mountIntegrationsHub(element, config) {
  config = config || {}
  // 2026-05-22 Null guard — workspace tab sistemi sayfa yeniden yuklerken element
  // DOM'dan kopmus/null gelebilir. createRoot(null) React #299 firlatir; sessizce
  // gec, retry'da dogru element ile yeniden cagrilir.
  if (!element || !(element instanceof Element) || !element.isConnected) {
    return { unmount: function() {} }
  }
  if (mountedRoots.has(element)) {
    mountedRoots.get(element).unmount()
    mountedRoots.delete(element)
  }
  var root = createRoot(element)
  mountedRoots.set(element, root)
  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(IntegrationsHub, { config: config })
    )
  )
  return { unmount: function() { root.unmount(); mountedRoots.delete(element) } }
}
window.CalibraHub.mountIntegrationsHub = mountIntegrationsHub

/**
 * IntegrationQueue mount — Aktarım Kuyruğu sayfası.
 * Manual tetikleyicili entegrasyonların bekleyen kayıtlarını listeler,
 * tekli/toplu aktarım + "Hariç Tut" işlemlerini yönetir.
 * @param {HTMLElement} element
 * @param {object} config - { apiBase }
 */
function mountIntegrationQueue(element, config) {
  config = config || {}
  if (mountedRoots.has(element)) {
    mountedRoots.get(element).unmount()
    mountedRoots.delete(element)
  }
  var root = createRoot(element)
  mountedRoots.set(element, root)
  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(IntegrationQueue, { config: config })
    )
  )
  return { unmount: function() { root.unmount(); mountedRoots.delete(element) } }
}
window.CalibraHub.mountIntegrationQueue = mountIntegrationQueue

/**
 * BpmFormDesigner mount — EBA-tarzı form tasarımcısı (drag-drop alan sürükleme).
 * @param {HTMLElement} element
 * @param {{ formId?: number|null }} opts
 */
function mountBpmFormDesigner(element, opts) {
  opts = opts || {}
  if (mountedRoots.has(element)) {
    mountedRoots.get(element).unmount()
    mountedRoots.delete(element)
  }
  var root = createRoot(element)
  mountedRoots.set(element, root)
  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(BpmFormDesigner, { formId: opts.formId || null })
    )
  )
  return {
    unmount: function () { root.unmount(); mountedRoots.delete(element) },
  }
}
window.CalibraHub.mountBpmFormDesigner = mountBpmFormDesigner

/**
 * BpmFormFiller mount — Son kullanıcı form doldurma ve gönderim ekranı.
 * @param {HTMLElement} element
 * @param {{ formId: number }} opts
 */
function mountBpmFormFiller(element, opts) {
  opts = opts || {}
  if (mountedRoots.has(element)) {
    mountedRoots.get(element).unmount()
    mountedRoots.delete(element)
  }
  var root = createRoot(element)
  mountedRoots.set(element, root)
  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(BpmFormFiller, { formId: opts.formId })
    )
  )
  return {
    unmount: function () { root.unmount(); mountedRoots.delete(element) },
  }
}
window.CalibraHub.mountBpmFormFiller = mountBpmFormFiller

// 2026-05-23 — Şirket Ayarları "Yapay Zeka" tab içindeki React panel.
// Razor sayfası tab açılınca tek seferlik mount eder (CompanySettings.cshtml içinden çağrılır).
import AiProvidersPanel from './components/AiAssistant/AiProvidersPanel'
import PurchaseRequestWizard from './components/PurchaseRequestWizard/PurchaseRequestWizard'
function mountAiProvidersPanel(element) {
  if (!element) return { unmount: function() {} }
  if (mountedRoots.has(element)) {
    mountedRoots.get(element).unmount()
    mountedRoots.delete(element)
  }
  var root = createRoot(element)
  mountedRoots.set(element, root)
  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(AiProvidersPanel)
    )
  )
  return { unmount: function () { root.unmount(); mountedRoots.delete(element) } }
}
window.CalibraHub.mountAiProvidersPanel = mountAiProvidersPanel

/**
 * ApprovalFlowDesigner mount — Onay Akışı görsel tasarımcısı (React Flow).
 *
 * @param {HTMLElement} element
 * @param {object} opts - {
 *   apiBase: string,
 *   flowId: number,
 *   initialNodes: array,
 *   initialEdges: array,
 *   users: array,
 *   departments: array,
 *   onSave: function(payload)
 * }
 */
function mountApprovalFlowDesigner(element, opts) {
  opts = opts || {}
  if (!element) return { unmount: function() {} }
  if (mountedRoots.has(element)) {
    mountedRoots.get(element).unmount()
    mountedRoots.delete(element)
  }
  var root = createRoot(element)
  mountedRoots.set(element, root)
  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(ApprovalFlowDesigner, {
        apiBase:      opts.apiBase      || '/ApprovalFlow',
        flowId:       opts.flowId       || 0,
        initialNodes: Array.isArray(opts.initialNodes) ? opts.initialNodes : [],
        initialEdges: Array.isArray(opts.initialEdges) ? opts.initialEdges : [],
        initialRules: Array.isArray(opts.initialRules) ? opts.initialRules : [],
        users:        Array.isArray(opts.users) ? opts.users : [],
        departments:  Array.isArray(opts.departments) ? opts.departments : [],
        cariGroups:   Array.isArray(opts.cariGroups) ? opts.cariGroups : [],
        materialGroups: (opts.materialGroups && typeof opts.materialGroups === 'object') ? opts.materialGroups : {},
        sqlQueries:   Array.isArray(opts.sqlQueries) ? opts.sqlQueries : [],
        // Entity-agnostic plugin: backend registry'den entity tipi kataloğu + seçili tip.
        entityTypes:        Array.isArray(opts.entityTypes) ? opts.entityTypes : [],
        currentEntityType:  typeof opts.currentEntityType === 'string' ? opts.currentEntityType : 'Document',
        canUseAdhoc:  opts.canUseAdhoc !== false,
        onSave:       typeof opts.onSave === 'function' ? opts.onSave : null,
      })
    )
  )
  return {
    unmount: function () { root.unmount(); mountedRoots.delete(element) },
  }
}
window.CalibraHub.mountApprovalFlowDesigner = mountApprovalFlowDesigner

/**
 * PurchaseRequestWizard mount — Satın Alma Talebi sihirbazı.
 * @param {HTMLElement} element
 * @param {{ antiForgeryToken: string }} config
 */
function mountPurchaseRequestWizard(element, config) {
  config = config || {}
  if (mountedRoots.has(element)) {
    mountedRoots.get(element).unmount()
    mountedRoots.delete(element)
  }
  var root = createRoot(element)
  mountedRoots.set(element, root)
  root.render(
    React.createElement(ErrorBoundary, null,
      React.createElement(PurchaseRequestWizard, {
        antiForgeryToken: config.antiForgeryToken || '',
      })
    )
  )
  return {
    unmount: function () { root.unmount(); mountedRoots.delete(element) },
  }
}
window.CalibraHub.mountPurchaseRequestWizard = mountPurchaseRequestWizard
