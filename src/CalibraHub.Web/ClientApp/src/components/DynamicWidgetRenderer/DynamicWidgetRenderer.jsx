/**
 * DynamicWidgetRenderer — Faz C
 *
 * Ortak React bileseni — Razor edit sayfalari (MaterialCardEdit,
 * ContactEdit, DocumentEdit) icinden mount edilir. formCode ve
 * recordId alir, /api/widgets/forms/{formCode}/records/{recordId}
 * endpoint'inden schema+value birlesimi yukler, dataType'a gore input
 * cizer ve kullanici kaydettiginde ayni endpoint'e POST eder.
 *
 * "Zeki Veri, Aptal Bilesen" — bu bilesen hicbir sayfaya ozel is mantigi
 * bilmez. Sadece classPrefix ile mevcut sayfanin CSS class konvansiyonlarini
 * (mce-*, ca-*, sqe-*) takip eder — her edit sayfasinin glassmorphism stili
 * bozulmadan entegre olur.
 *
 * Props:
 *   formCode       string  — 'ITEMS', 'CONTACTS', 'SALES_QUOTE_EDIT', ...
 *   recordId       string  — business key (MaterialCode / AccountCode / ...)
 *                            bos olabilir (yeni kayit); save cagrisinda override
 *                            edilebilir.
 *   classPrefix    string  — 'mce' | 'ca' | 'sqe' (varsayilan 'mce')
 *   containerId    string? — dis container ID'si (renderer bos kalirsa gizleme icin)
 *   onMounted      fn(handle) — mount sonrasi parent'a imperative handle verir:
 *                               { save(opts), getValues(), getHasWidgets() }
 */
import { useState, useEffect, useImperativeHandle, forwardRef, useRef, useCallback } from 'react'
import { Settings, X, History } from 'lucide-react'
import { getRecord, saveRecord, guideResolve } from './dynamicWidgetService'
import LookupFieldInput from './LookupFieldInput'
import GuideListField from './GuideListField'
import GridFieldInput from './GridFieldInput'
import WidgetFieldShell from './WidgetFieldShell'
import WidgetHistoryModal from './WidgetHistoryModal'
import AttachmentFieldInput from './AttachmentFieldInput'
import { buildRuleGraph, recomputeAll, propagateChange } from './ruleEngine'
import { resolveTokens as resolveAllTokens } from '../../utils/fieldTokens'

/* ── localStorage yardımcıları ─────────────────────────────── */
function dwrStorageKey(formCode) { return 'dwrEnabled.' + formCode }
function loadEnabledIds(formCode) {
  try {
    var raw = localStorage.getItem(dwrStorageKey(formCode))
    if (raw) { var arr = JSON.parse(raw); if (Array.isArray(arr)) return arr }
  } catch (e) { /* ignore */ }
  return null // null = henuz secim yapilmamis, tum widget'lar varsayilan etkin
}
function saveEnabledIds(formCode, ids) {
  try { localStorage.setItem(dwrStorageKey(formCode), JSON.stringify(ids)) } catch (e) { /* ignore */ }
}

/**
 * resolveStaticDefault — admin'in tanimladigi sabit varsayilan ifadesini
 * runtime degerine cevirir.
 *
 * Desteklenen ifadeler:
 *   - TODAY()      → bugunun tarihi   (YYYY-MM-DD)
 *   - YESTERDAY()  → dunun tarihi
 *   - TOMORROW()   → yarinin tarihi
 *   - Diger her sey literal degere donusur (string, sayi, vb.)
 *
 * dataType parametresi: ileride 'numeric' icin parse, 'boolean' icin true/false
 * normalize gibi tip-bazli cozumler eklemek icin kullanilabilir.
 */
function resolveStaticDefault(expr, dataType) {
  if (expr == null) return ''
  var v = String(expr).trim()
  if (v === '') return ''
  // Function call'lari case-insensitive yakala, "()" opsiyonel.
  var upper = v.toUpperCase().replace(/\(\)$/, '')
  if (upper === 'TODAY')     return formatIsoDate(new Date())
  if (upper === 'YESTERDAY') return formatIsoDate(new Date(Date.now() - 86400000))
  if (upper === 'TOMORROW')  return formatIsoDate(new Date(Date.now() + 86400000))
  return v
}
function formatIsoDate(d) {
  var y = d.getFullYear()
  var m = String(d.getMonth() + 1).padStart(2, '0')
  var day = String(d.getDate()).padStart(2, '0')
  return y + '-' + m + '-' + day
}

var DynamicWidgetRenderer = forwardRef(function DynamicWidgetRenderer(props, ref) {
  var formCode    = props.formCode
  var initialRecordId = props.recordId || ''
  var classPrefix = props.classPrefix || 'mce'
  var containerId = props.containerId
  var onMounted   = props.onMounted
  // Faz I — layout modu: 'stacked' (varsayilan, mevcut davranis) veya 'sidetabs'
  // (sol nav + sag content, sadece secili grubun field'lari render edilir).
  var layoutMode  = props.layout === 'sidetabs' ? 'sidetabs' : 'stacked'
  // Sidetabs aktif grup state'i — ilk groupKey'e set edilir (load sonrasi).
  var [activeGroupKey, setActiveGroupKey] = useState(null)
  // Faz E — grid row modal embed senaryosu: mevcut satir degerleri onceden
  // doldurulmus olarak renderer'a verilir, server'a save yapilmaz, ref.getValues()
  // ile parent (GridFieldInput) degerleri cekip kendi grid state'ine pop eder.
  var initialValues = props.initialValues && typeof props.initialValues === 'object'
    ? props.initialValues : null

  var [loading, setLoading]   = useState(true)
  var [widgets, setWidgets]   = useState([])      // WidgetRenderDto[]
  var [values, setValues]     = useState({})      // { widgetCode: value }

  // ── Global widget registry (Tip 1 sabit alan rehberlerinde widget alanlarina erisim icin) ──
  // Sayfada DWR mount oldugunda window.__CALIBRA_WIDGETS__'a schema + values yansitilir.
  // FixedFieldLookupBridge / GuideCustomizationModal @ dropdown bu registry'den
  // "Widget Alanları" grubunu uretip {#widget.WCODE} secilebilir hale getirir.
  // Runtime'da resolveTokens widget branch'i de bu registry'den canli degeri okur.
  useEffect(function () {
    if (typeof window === 'undefined') return undefined
    var schema = (widgets || []).map(function (w) {
      return { code: w.widgetCode, label: w.label || w.fieldLabel || w.widgetCode }
    }).filter(function (s) { return !!s.code })
    window.__CALIBRA_WIDGETS__ = { formCode: formCode, schema: schema, values: values || {} }
    return function () {
      // Sadece bu DWR yayini ise temizle (baska bir DWR mount olduysa onu bozma)
      if (window.__CALIBRA_WIDGETS__ && window.__CALIBRA_WIDGETS__.formCode === formCode) {
        delete window.__CALIBRA_WIDGETS__
      }
    }
  }, [widgets, values, formCode])

  // Faz E — grid widget'lari icin her biri bir rows list state'i:
  // { [gridWidgetCode]: { childFormCode, rows: [{ recordId, values, displays? }] } }
  var [grids, setGrids]       = useState({})
  // Lookup widget'lari icin display cache — { widgetCode: 'DisplayColumn degeri' }.
  // Sayfa load'da guideResolve ile doldurulur; kullanici bir satir sectiginde
  // LookupFieldInput.onPick bu map'i gunceller.
  var [displays, setDisplays] = useState({})
  // Faz G — Rule Engine state'leri:
  //   visibility: { [code]: boolean }         — false ise widget UI'da gizlenir
  //   disabledMap: { [code]: boolean }        — true ise widget readonly
  //   requiredMap: { [code]: boolean }        — true ise widget zorunlu (statik IsRequired'i override eder)
  //   ruleErrors: { [code]: string }          — widget altinda inline hata mesaji
  //   cycleError:  string | null              — mount-time cycle → form cizilmez
  var [visibility, setVisibility] = useState({})
  var [disabledMap, setDisabledMap] = useState({})
  var [requiredMap, setRequiredMap] = useState({})
  var [ruleErrors, setRuleErrors] = useState({})
  var [cycleError, setCycleError] = useState(null)
  // Hangi widget'ların edit formunda görüneceği — localStorage'dan yükle
  // Tum aktif widget'lar her zaman gorunur — enabledIds kaldirildi
  function isWidgetEnabled() { return true }
  var [configOpen, setConfigOpen] = useState(false)
  var configPanelRef = useRef(null)
  // Alan bazli degisiklik gecmisi (audit) modali — sadece kayitli (recordId dolu)
  // kayitlarda tetiklenebilir; yeni kayitta gecmis olamaz.
  var [historyOpen, setHistoryOpen] = useState(false)
  // Zorunlu alan validasyonu — save denemesinde bos kalan zorunlu widgetId listesi.
  // handleChange'de temizlenir; save basarili olunca da sifirlanir.
  var [saveAttemptErrors, setSaveAttemptErrors] = useState([])
  // Toast bildirimleri — save denemesinde bos zorunlu alanlar icin sag alt
  // koseden cikar. Her item: { id, title, detail }. Auto-dismiss 4.5s.
  var [requiredToasts, setRequiredToasts] = useState([])
  function dismissToast(id) {
    setRequiredToasts(function(prev) { return prev.filter(function(t) { return t.id !== id }) })
  }
  // Graph ref — buildRuleGraph mount'ta cagrilir, keystroke'lar tek parsed AST'yi kullanir
  var ruleGraphRef = useRef(null)
  // State refs — handleChange useCallback stable kalmasi icin guncel state'i ref'ten okur
  // Ayni zamanda save() cagrisinda stale closure sorununu onler: handleRef.current
  // her render'da yeniden atanir ama onMounted ile dis tarafa ilk render'daki obje
  // gonderilir; ref'lerden okuyunca her zaman guncel degere ulasiliriz.
  var valuesRef     = useRef({})
  var gridsRef      = useRef({})
  var widgetsRef    = useRef([])
  var visibilityRef = useRef({})
  var disabledRef   = useRef({})
  var requiredRef   = useRef({})
  var errorsRef     = useRef({})
  var [error, setError]       = useState(null)
  // activeRecordId — handle.reload() ile degisebilir
  var [activeRecordId, setActiveRecordId] = useState(initialRecordId)
  var recordIdRef = useRef(initialRecordId)
  // reloadTick — AdminWidgetRegistry'den schema degisince artar, useEffect yeniden tetiklenir
  var [reloadTick, setReloadTick] = useState(0)

  // Admin panel'de widget schema degisince (toggle/ekle/sil) otomatik yeniden yukle.
  // localStorage 'storage' event'i same-origin iframe'ler arasi yayilir (cross-tab/cross-frame).
  useEffect(function () {
    function onStorageChange(e) {
      if (e.key === 'calibra:widget-schema-changed') {
        setReloadTick(function(t) { return t + 1 })
      }
    }
    window.addEventListener('storage', onStorageChange)
    return function () { window.removeEventListener('storage', onStorageChange) }
  }, [])

  // ── Load widgets + values — formCode veya activeRecordId degisiminde ──
  useEffect(function () {
    var cancelled = false
    async function load() {
      setLoading(true)
      setError(null)
      try {
        var data = await getRecord(formCode, activeRecordId)
        if (cancelled) return
        if (!data) {
          setWidgets([])
          setValues({})
          return
        }
        var ws = Array.isArray(data.widgets) ? data.widgets : []
        // widgetsRef'i useEffect beklemeden hemen senkron tut — Kaydet
        // anlik basildiginda valid currentWidgets snapshot'i bulsun.
        widgetsRef.current = ws
        setWidgets(ws)
        // Initial values dict — widgetCode → value (null -> '')
        var dict = {}
        // Initial grids dict — widgetCode → { childFormCode, rows: [...] }
        var gDict = {}
        ws.forEach(function (w) {
          var dt = String(w.dataType || '').toLowerCase()
          if (dt === 'group') return
          if (dt === 'grid') {
            var childFormCode = (w.metadata && w.metadata.childFormCode) || ''
            var rows = Array.isArray(w.gridRows) ? w.gridRows.map(function (r) {
              return { recordId: r.recordId, values: Object.assign({}, r.values || {}) }
            }) : []
            gDict[w.widgetId] = { childFormCode: childFormCode, rows: rows }
            return
          }
          var v = w.value
          if (v == null) v = ''
          // Varsayilan deger (rules.defaultValue) uygulama — sadece BOS alan +
          // YENI kayit senaryosunda. Mevcut kayit acildiginda DB'den okunan
          // gercek deger ezilmez. Tarih alanlarinda TODAY()/YESTERDAY()/TOMORROW()
          // gibi function call'lar runtime'da YYYY-MM-DD'ye cozulur.
          if (v === '' && w.rules && w.rules.defaultValue) {
            var kind = (w.rules.defaultValueKind || 'static').toLowerCase()
            if (kind === 'static') {
              var resolved = resolveStaticDefault(String(w.rules.defaultValue), dt)
              console.log('[DWR] default applied', { widgetId: w.widgetId, dataType: dt, raw: w.rules.defaultValue, resolved: resolved })
              v = resolved
            }
            // 'formula' kind icin rule engine zaten dependency-driven olarak
            // formula degerini hesaplayacak; burada manual cozmuyoruz.
          }
          dict[w.widgetId] = v
        })
        console.log('[DWR] initial dict snapshot', JSON.parse(JSON.stringify(dict)))
        // Faz E — embedded grid row modal: initialValues ile override et
        if (initialValues) {
          Object.keys(initialValues).forEach(function (k) {
            var iv = initialValues[k]
            dict[k] = iv == null ? '' : iv
          })
        }

        // Faz G — Rule Engine mount-time setup
        // 1) Graph insa et (parse + dep map + cycle detection + topo sort)
        // 2) Cycle varsa form render etme, banner goster
        // 3) Cycle yoksa initial recomputeAll — tum formula/visibility/disabled hesapla
        var graph = buildRuleGraph(ws)
        ruleGraphRef.current = graph
        if (graph.cycle) {
          setCycleError('Sonsuz dongu: ' + graph.cycle.join(' → '))
          // valuesRef'i useEffect beklemeden hemen guncel tut — kullanici load
          // biter bitmez Kaydet'e basabilir; ref senkronizasyonu render+useEffect
          // sirasini bekleyemez (varsayilan deger validasyon-anında bos gorunmesin).
          valuesRef.current = dict
          gridsRef.current  = gDict
          setValues(dict)
          setGrids(gDict)
          setVisibility({})
          setDisabledMap({})
          setRuleErrors({})
          recordIdRef.current = activeRecordId
        } else if (graph.fatalErrors.length > 0) {
          setCycleError(graph.fatalErrors.join(' | '))
          valuesRef.current = dict
          gridsRef.current  = gDict
          setValues(dict)
          setGrids(gDict)
          setVisibility({})
          setDisabledMap({})
          setRuleErrors({})
          recordIdRef.current = activeRecordId
        } else {
          setCycleError(null)
          if (graph.hasRules) {
            var patch = recomputeAll(graph, dict)
            valuesRef.current      = patch.values
            requiredRef.current    = patch.required || {}
            visibilityRef.current  = patch.visibility
            disabledRef.current    = patch.disabled
            errorsRef.current      = patch.errors
            setRequiredMap(patch.required || {})
            setValues(patch.values)
            setVisibility(patch.visibility)
            setDisabledMap(patch.disabled)
            setRuleErrors(patch.errors)
          } else {
            valuesRef.current = dict
            setValues(dict)
            setVisibility({})
            setDisabledMap({})
            setRuleErrors({})
          }
          gridsRef.current = gDict
          setGrids(gDict)
          recordIdRef.current = activeRecordId
        }

        // Rehber bagli widget'lar icin display cozumleme — paralel fetch, hata sessiz.
        // Hem `lookup` hem `text+rehber` widget'lari kapsanir; ikisinde de
        // metadata.guideCode dolu ve LookupFieldInput render edilir.
        // Guide yoksa / deger bos ise skipped.
        var lookupJobs = ws
          .filter(function (w) {
            var dt = String(w.dataType || '').toLowerCase()
            return (dt === 'lookup' || dt === 'text')
              && dict[w.widgetId]
              && w.metadata
              && w.metadata.guideCode
          })
          .map(function (w) {
            return guideResolve(w.metadata.guideCode, dict[w.widgetId])
              .then(function (r) { return { widgetId: w.widgetId, display: r && r.display ? r.display : '' } })
              .catch(function () { return { widgetId: w.widgetId, display: '' } })
          })
        if (lookupJobs.length > 0) {
          Promise.all(lookupJobs).then(function (results) {
            if (cancelled) return
            var dMap = {}
            results.forEach(function (r) { dMap[r.widgetId] = r.display })
            setDisplays(dMap)
          })
        } else {
          setDisplays({})
        }
      } catch (e) {
        if (!cancelled) setError(e.message || 'Yuklenemedi')
      } finally {
        if (!cancelled) setLoading(false)
      }
    }
    load()
    return function () { cancelled = true }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [formCode, activeRecordId, reloadTick])

  // Config panel dışına tıklayınca kapat
  useEffect(function () {
    if (!configOpen) return
    function handleClick(e) {
      if (configPanelRef.current && configPanelRef.current.contains(e.target)) return
      setConfigOpen(false)
    }
    document.addEventListener('mousedown', handleClick)
    return function () { document.removeEventListener('mousedown', handleClick) }
  }, [configOpen])

  // ── Dis container'i gosterme/gizleme ──
  // hasWidgets: tanımlı field widget varsa kart görünür
  // (tüm widget'lar kapalı olsa bile dişli çark erişilebilir olsun)
  var hasWidgets = widgets.some(function (w) {
    return String(w.dataType || '').toLowerCase() !== 'group'
  })
  useEffect(function () {
    if (!containerId) return
    var container = document.getElementById(containerId)
    if (!container) return
    // Parent kart wrapper'i: en yakin '.{prefix}-card' veya container'in parent'i
    var card = container.closest('.' + classPrefix + '-card') || container.parentElement
    if (!card) return
    if (loading || hasWidgets) {
      card.style.display = ''
    } else {
      card.style.display = 'none'
    }
  }, [loading, hasWidgets, containerId, classPrefix])

  // ── onMounted callback (mount sonrasi) ──
  // Stable proxy: her zaman handleRef.current'a delege eder.
  // onMounted ile dis tarafa ilk render'in objesi degil proxy gonderilir;
  // save/getValues/reload cagrilari her zaman guncel closure'a ulasir.
  useEffect(function () {
    if (!onMounted) return
    var proxy = {
      save:          function (opts) { return handleRef.current ? handleRef.current.save(opts) : Promise.resolve({ success: false, message: 'renderer hazir degil' }) },
      validate:      function ()     { return handleRef.current ? handleRef.current.validate() : { valid: true, errors: [] } },
      getValues:     function ()     { return handleRef.current ? handleRef.current.getValues() : {} },
      getGrids:      function ()     { return handleRef.current ? handleRef.current.getGrids() : {} },
      getHasWidgets: function ()     { return handleRef.current ? handleRef.current.getHasWidgets() : false },
      reload:        function (opts) { if (handleRef.current) handleRef.current.reload(opts) },
    }
    onMounted(proxy)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // Handle'i ref uzerinden expose et (mount.jsx tarafinda kullanilir)
  var handleRef = useRef(null)
  handleRef.current = {
    save: function (opts) {
      var recordId = (opts && opts.recordId) || recordIdRef.current || activeRecordId
      if (!recordId) {
        return Promise.resolve({ success: false, message: 'recordId bos — once master kaydedilmeli.' })
      }
      recordIdRef.current = recordId

      // Stale closure'dan kac: state yerine her zaman guncel ref'leri kullan
      var currentValues  = valuesRef.current
      var currentGrids   = gridsRef.current
      var currentWidgets = widgetsRef.current

      // Zorunlu alan validasyonu — opts.skipValidation=true ise atla (otomatik kayit)
      var currentRequired = requiredRef.current || {}
      var requiredErrors = []    // label listesi (hata mesaji icin)
      var requiredErrorIds = []  // widgetId listesi (gorsel hata icin)
      console.log('[DWR] save() validation start — currentValues:', JSON.parse(JSON.stringify(currentValues)))
      if (!(opts && opts.skipValidation)) {
        currentWidgets.forEach(function (w) {
          var dt = String(w.dataType || '').toLowerCase()
          if (dt === 'group' || dt === 'guide-list') return
          var effReq = w.isRequired || currentRequired[w.widgetId] === true
          if (!effReq) return
          var val = currentValues[w.widgetId]
          var isEmpty = val === null || val === undefined || val === ''
            || (Array.isArray(val) && val.length === 0)
          if (effReq) console.log('[DWR] required check', { id: w.widgetId, label: w.label, val: val, isEmpty: isEmpty })
          if (isEmpty) {
            requiredErrors.push(w.label || w.widgetId)
            requiredErrorIds.push(w.widgetId)
          }
        })
      }
      if (requiredErrors.length > 0) {
        // Shake animasyonunu yeniden tetiklemek icin: bir frame icin saveAttemptErrors'i
        // bosalt → React class'i kaldirir → animation reset olur → sonraki tick'te
        // yeniden setlenir → animation tekrar oynar. Aksi halde "is-invalid" zaten
        // setli oldugu durumda animation tetiklenmez (className referans degismedikce).
        setSaveAttemptErrors([])
        var nextIds = requiredErrorIds.slice()
        var nextLabels = requiredErrors.slice()
        setTimeout(function() {
          setSaveAttemptErrors(nextIds)
          // Sag alt kose toast'lari — her bos zorunlu alan icin bir kart.
          // Onceki listeyi temizle (eski hatalar duruyor olabilir), yenisini koy.
          var stamp = Date.now()
          var toasts = nextLabels.map(function(lbl, i) {
            return {
              id: 'req-' + stamp + '-' + i,
              title: lbl,
              detail: 'Bu alan zorunlu, lütfen doldurun.',
            }
          })
          setRequiredToasts(toasts)
          // Auto-dismiss 4.5s sonra
          toasts.forEach(function(t) {
            setTimeout(function() {
              setRequiredToasts(function(prev) { return prev.filter(function(x) { return x.id !== t.id }) })
            }, 4500)
          })
        }, 0)
        return Promise.resolve({
          success: false,
          message: 'Zorunlu alanlar bos birakilamaz: ' + requiredErrors.join(', '),
          requiredErrors: requiredErrors,
        })
      }
      setSaveAttemptErrors([])
      setRequiredToasts([])
      // Faz E — grids state'ini SaveRecordRequest shape'ine serialize et
      var gridsPayload = null
      var gridKeys = Object.keys(currentGrids || {})
      if (gridKeys.length > 0) {
        gridsPayload = {}
        gridKeys.forEach(function (k) {
          var g = currentGrids[k]
          gridsPayload[k] = {
            childFormCode: g.childFormCode,
            rows: (g.rows || []).map(function (r) {
              return { recordId: r.recordId || null, values: r.values || {} }
            }),
          }
        })
      }
      return saveRecord(formCode, recordId, { values: currentValues, grids: gridsPayload })
    },
    // validate() — kaydetmeden zorunlu alan kontrolu. { valid, errors[] } doner.
    // MaterialCardEdit mceSave() icin: ana form fetch'inden ONCE cagrilir.
    validate: function () {
      var currentValues  = valuesRef.current
      var currentWidgets = widgetsRef.current
      var currentRequired = requiredRef.current || {}
      var requiredErrors    = []
      var requiredErrorIds  = []
      currentWidgets.forEach(function (w) {
        var dt = String(w.dataType || '').toLowerCase()
        if (dt === 'group' || dt === 'guide-list') return
        var effReq = w.isRequired || currentRequired[w.widgetId] === true
        if (!effReq) return
        var val = currentValues[w.widgetId]
        var isEmpty = val === null || val === undefined || val === ''
          || (Array.isArray(val) && val.length === 0)
        if (isEmpty) {
          requiredErrors.push(w.label || w.widgetId)
          requiredErrorIds.push(w.widgetId)
        }
      })
      // Kısıtlama kontrolleri (dolu değerler üzerinde)
      currentWidgets.forEach(function(w) {
        var dtc = String(w.dataType || '').toLowerCase()
        if (dtc === 'group' || dtc === 'guide-list') return
        // Aynı widget'a zaten isRequired hatası eklenmiş ise atla (duplicate önle)
        if (requiredErrorIds.indexOf(w.widgetId) !== -1) return
        var val = currentValues[w.widgetId]
        var str = val != null ? String(val) : ''
        if (str.length === 0) return  // boş dolu kontrolü isRequired'a bırakıldı

        if (w.dataType === 'text') {
          if (w.minLength && str.length < w.minLength) {
            requiredErrors.push((w.label || w.widgetId) + ' en az ' + w.minLength + ' karakter olmalı (şu an: ' + str.length + ')')
            requiredErrorIds.push(w.widgetId)
          } else if (w.expectedLength && str.length !== w.expectedLength) {
            requiredErrors.push((w.label || w.widgetId) + ' tam ' + w.expectedLength + ' karakter olmalı (şu an: ' + str.length + ')')
            requiredErrorIds.push(w.widgetId)
          } else if (w.maxLength && str.length > w.maxLength) {
            requiredErrors.push((w.label || w.widgetId) + ' en fazla ' + w.maxLength + ' karakter olabilir')
            requiredErrorIds.push(w.widgetId)
          }
        }

        if (w.dataType === 'numeric') {
          var num = parseFloat(str.replace(',', '.'))
          if (!isNaN(num)) {
            if (w.minValue != null && num < w.minValue) {
              requiredErrors.push((w.label || w.widgetId) + ' en az ' + w.minValue + ' olmalı')
              requiredErrorIds.push(w.widgetId)
            } else if (w.maxValue != null && num > w.maxValue) {
              requiredErrors.push((w.label || w.widgetId) + ' en fazla ' + w.maxValue + ' olabilir')
              requiredErrorIds.push(w.widgetId)
            }
          }
        }
      })

      if (requiredErrors.length > 0) {
        // save() ile ayni pattern: shake animasyonunu yeniden tetiklemek icin
        // saveAttemptErrors'i bir frame icin bosalt → React class'i kaldirir
        // → animation reset olur → sonraki tick'te yeniden setlenir.
        // Toast'lar sag alt koseden cikar, 4.5s sonra otomatik kaybolur.
        setSaveAttemptErrors([])
        var nextIdsV = requiredErrorIds.slice()
        var nextLabelsV = requiredErrors.slice()
        setTimeout(function() {
          setSaveAttemptErrors(nextIdsV)
          var stamp = Date.now()
          var toasts = nextLabelsV.map(function(lbl, i) {
            return {
              id: 'req-' + stamp + '-' + i,
              title: lbl,
              detail: 'Bu alan zorunlu, lütfen doldurun.',
            }
          })
          setRequiredToasts(toasts)
          toasts.forEach(function(t) {
            setTimeout(function() {
              setRequiredToasts(function(prev) { return prev.filter(function(x) { return x.id !== t.id }) })
            }, 4500)
          })
        }, 0)
        return { valid: false, errors: requiredErrors, errorIds: requiredErrorIds }
      }
      setSaveAttemptErrors([])
      setRequiredToasts([])
      return { valid: true, errors: [], errorIds: [] }
    },
    getValues: function () { return Object.assign({}, valuesRef.current) },
    getGrids:  function () { return gridsRef.current },
    getHasWidgets: function () { return hasWidgets },
    // Faz C: reload({ recordId }) — sayfa JS'i yeni kayit yukledikten sonra
    // renderer'a yeni recordId'yi iletir, useEffect tetiklenir ve schema+values
    // yeniden cekilir.
    reload: function (opts) {
      var newId = (opts && opts.recordId) || ''
      setActiveRecordId(newId)
    },
  }
  useImperativeHandle(ref, function () { return handleRef.current })

  // ── State → ref senkronizasyonu ──
  useEffect(function () { valuesRef.current     = values       }, [values])
  useEffect(function () { gridsRef.current      = grids        }, [grids])
  useEffect(function () { widgetsRef.current    = widgets      }, [widgets])
  useEffect(function () { visibilityRef.current = visibility   }, [visibility])
  useEffect(function () { disabledRef.current   = disabledMap  }, [disabledMap])
  useEffect(function () { requiredRef.current   = requiredMap  }, [requiredMap])
  useEffect(function () { errorsRef.current     = ruleErrors   }, [ruleErrors])

  // ── Value change handler ──
  // Faz G: her edit'te rule engine'in propagateChange'ini calistir, etkilenen
  // formula/visibility/disabled widget'larini batch olarak guncelle. Rule yoksa
  // hafif hizli yol (sadece setValues).
  var handleChange = useCallback(function (widgetCode, newValue) {
    // Zorunlu alan hatasini temizle — kullanici yazinca kirmizili cerceve kalkar
    setSaveAttemptErrors(function (prev) { return prev.filter(function (id) { return id !== widgetCode }) })
    var graph = ruleGraphRef.current
    if (!graph || !graph.hasRules || graph.cycle) {
      setValues(function (prev) {
        var next = Object.assign({}, prev)
        next[widgetCode] = newValue
        return next
      })
      return
    }
    var currentState = {
      values: valuesRef.current,
      visibility: visibilityRef.current,
      disabled: disabledRef.current,
      required: requiredRef.current,
      errors: errorsRef.current,
    }
    var patch = propagateChange(widgetCode, newValue, graph, currentState)
    setRequiredMap(patch.required || {})
    setValues(patch.values)
    setVisibility(patch.visibility)
    setDisabledMap(patch.disabled)
    setRuleErrors(patch.errors)
  }, [])

  // ── Tüm field widget'ları (grup hariç) — config paneli için
  var allFieldWidgets = widgets.filter(function (w) {
    return String(w.dataType || '').toLowerCase() !== 'group'
  }).sort(function (a, b) { return (a.sortOrder || 0) - (b.sortOrder || 0) })

  // ── Grup hiyerarsisi: widgets'i gruplara bucket'la ──
  // Sadece enabledIds içindeki widget'lar gösterilir
  var groupWidgets = widgets.filter(function (w) {
    return String(w.dataType || '').toLowerCase() === 'group'
  }).sort(function (a, b) { return (a.sortOrder || 0) - (b.sortOrder || 0) })

  var childrenByParent = {}
  widgets.forEach(function (w) {
    if (String(w.dataType || '').toLowerCase() === 'group') return
    if (!isWidgetEnabled(w.widgetId)) return // etkin değilse atla
    var pid = w.parentId != null ? w.parentId : '__ungrouped'
    if (!childrenByParent[pid]) childrenByParent[pid] = []
    childrenByParent[pid].push(w)
  })
  Object.keys(childrenByParent).forEach(function (k) {
    childrenByParent[k].sort(function (a, b) { return (a.sortOrder || 0) - (b.sortOrder || 0) })
  })

  // ── Sidetabs layout için tab listesi (group + ungrouped sahte tab) ──
  // ungrouped → en başta "Genel" virtual tab; gerçek group'lar sortOrder'a göre.
  // Sadece icinde gorunur (visibility ile filtrelenmis) child'i olan tab'lar listelenir.
  var sideTabs = []
  if (childrenByParent['__ungrouped'] && childrenByParent['__ungrouped'].length > 0) {
    var visibleUngrouped = childrenByParent['__ungrouped'].filter(function (w) {
      return !visibility || visibility[w.widgetId] !== false
    })
    if (visibleUngrouped.length > 0) {
      sideTabs.push({ key: '__ungrouped', label: 'Genel', children: childrenByParent['__ungrouped'] })
    }
  }
  groupWidgets.forEach(function (g) {
    var childs = childrenByParent[g.id] || []
    if (childs.length === 0) return
    var visChilds = childs.filter(function (w) {
      return !visibility || visibility[w.widgetId] !== false
    })
    if (visChilds.length === 0) return
    sideTabs.push({ key: String(g.id), label: g.label, children: childs })
  })

  // İlk render'da activeGroupKey null ise ilk tab'a düş.
  useEffect(function () {
    if (layoutMode !== 'sidetabs') return
    if (activeGroupKey != null) return
    if (sideTabs.length > 0) setActiveGroupKey(sideTabs[0].key)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [layoutMode, sideTabs.length, activeGroupKey])

  // saveAttemptErrors değişince → ilk hatalı tab'a otomatik geç + ilk hatalı alana scroll.
  useEffect(function () {
    if (layoutMode !== 'sidetabs') return
    if (!saveAttemptErrors || saveAttemptErrors.length === 0) return
    var firstErrId = saveAttemptErrors[0]
    var errWidget = widgets.find(function (w) { return w.widgetId === firstErrId })
    if (!errWidget) return
    var errKey = errWidget.parentId != null ? String(errWidget.parentId) : '__ungrouped'
    if (errKey !== activeGroupKey) setActiveGroupKey(errKey)
    // Bir frame sonra DOM'da hatalı alan render olmuş olur → scroll.
    setTimeout(function () {
      var el = document.getElementById('dyn_' + firstErrId)
      if (el && el.scrollIntoView) {
        try { el.scrollIntoView({ behavior: 'smooth', block: 'center' }) } catch (e) { /* ignore */ }
        if (el.focus) { try { el.focus({ preventScroll: true }) } catch (e) { /* ignore */ } }
      }
    }, 80)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [saveAttemptErrors, layoutMode])

  // Her tab için eksik zorunlu alan sayısı — badge için.
  function countMissingRequiredInTab(tab) {
    var n = 0
    var errSet = {}
    ;(saveAttemptErrors || []).forEach(function (id) { errSet[id] = true })
    tab.children.forEach(function (w) {
      if (errSet[w.widgetId]) n++
    })
    return n
  }


  // ── Loading / empty / error states ──
  if (loading) {
    return (
      <div className={classPrefix + '-dyn-loading'} style={{ padding: 16, textAlign: 'center', fontSize: 12, opacity: 0.6 }}>
        Yukleniyor...
      </div>
    )
  }
  if (error) {
    return (
      <div className={classPrefix + '-dyn-error'} style={{ padding: 12, fontSize: 12, color: '#f87171' }}>
        Hata: {error}
      </div>
    )
  }
  // Faz G: Mount-time cycle / fatal rule hata → form cizilmez
  if (cycleError) {
    return (
      <div className={classPrefix + '-dyn-error ' + classPrefix + '-rule-cycle'} style={{ padding: 16 }}>
        <strong style={{ display: 'block', marginBottom: 6, fontSize: 13 }}>
          ⚠ Kural motoru — form yuklenemiyor
        </strong>
        <code style={{ fontSize: 12, opacity: 0.85 }}>{cycleError}</code>
        <p style={{ fontSize: 11, opacity: 0.65, marginTop: 8, marginBottom: 0 }}>
          Admin panelinden hatali kurali iceren widget'i duzeltene kadar form erisilemez.
        </p>
      </div>
    )
  }
  if (!hasWidgets) {
    // Parent container useEffect ile display:none yapar — burada null dondur
    return null
  }

  // Degisiklik gecmisi tetigi — sadece mevcut (kaydedilmis) kayitta anlamli.
  // '-' dummy recordId'si (yeni kayit route eslesmesi) gecmissiz sayilir.
  var canShowHistory = !!activeRecordId && activeRecordId !== '-'
  var historyModal = canShowHistory ? (
    <WidgetHistoryModal
      isOpen={historyOpen}
      onClose={function () { setHistoryOpen(false) }}
      formCode={formCode}
      recordId={activeRecordId}
    />
  ) : null

  // ── Sidetabs layout: sol nav (grup adlari) + sag content (aktif grup field'lari) ──
  if (layoutMode === 'sidetabs') {
    var activeTab = sideTabs.find(function (t) { return t.key === activeGroupKey }) || sideTabs[0] || null
    return (
      <div className={classPrefix + '-dyn-root dwr-sidetabs'} data-widget-renderer>
        <aside className="dwr-sidetabs__nav" role="tablist" aria-orientation="vertical">
          {sideTabs.map(function (tab) {
            var isActive = activeTab && activeTab.key === tab.key
            var missing = countMissingRequiredInTab(tab)
            return (
              <button
                key={tab.key}
                type="button"
                role="tab"
                aria-selected={isActive ? 'true' : 'false'}
                className={'dwr-sidetabs__tab' + (isActive ? ' is-active' : '')}
                onClick={function () { setActiveGroupKey(tab.key) }}
              >
                <span className="dwr-sidetabs__tab-label">{tab.label}</span>
                {missing > 0 && (
                  <span className="dwr-sidetabs__badge" title={missing + ' eksik zorunlu alan'}>{missing}</span>
                )}
              </button>
            )
          })}
          {canShowHistory && (
            <button
              type="button"
              className="wf-history-trigger"
              style={{ marginTop: 'auto', justifyContent: 'center' }}
              onClick={function () { setHistoryOpen(true) }}
              title="Ek alanlarda yapılan değişikliklerin geçmişi"
            >
              <History size={13} />
              Değişiklik Geçmişi
            </button>
          )}
        </aside>
        <section className="dwr-sidetabs__content" role="tabpanel">
          {activeTab ? (
            <div className="wf-grid">
              {activeTab.children.map(function (w) {
                return renderField(w, values[w.widgetId], handleChange, classPrefix, displays, setDisplays, grids, setGrids, visibility, disabledMap, ruleErrors, saveAttemptErrors, values)
              })}
            </div>
          ) : (
            <div className="dwr-sidetabs__empty">Görüntülenecek alan yok.</div>
          )}
        </section>

        {/* Toast'lar sidetabs modunda da aynı şekilde */}
        {requiredToasts.length > 0 && (
          <div className="wf-toast-host" role="status" aria-live="polite">
            {requiredToasts.map(function(t) {
              return (
                <div key={t.id} className="wf-toast">
                  <span className="wf-toast-icon" aria-hidden="true">!</span>
                  <div className="wf-toast-body">
                    <div className="wf-toast-title">{t.title}</div>
                    {t.detail && <div className="wf-toast-detail">{t.detail}</div>}
                  </div>
                  <button
                    type="button"
                    className="wf-toast-close"
                    onClick={function() { dismissToast(t.id) }}
                    aria-label="Kapat"
                  >×</button>
                </div>
              )
            })}
          </div>
        )}

        {historyModal}
      </div>
    )
  }

  return (
    <div className={classPrefix + '-dyn-root'} data-widget-renderer>

      {/* Degisiklik gecmisi — sag ustte kompakt tetik (yalniz kayitli kayitta) */}
      {canShowHistory && (
        <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: 8 }}>
          <button
            type="button"
            className="wf-history-trigger"
            onClick={function () { setHistoryOpen(true) }}
            title="Ek alanlarda yapılan değişikliklerin geçmişi"
          >
            <History size={13} />
            Değişiklik Geçmişi
          </button>
        </div>
      )}

      {/* Grup'lara gore ayri kartlar — `${classPrefix}-card` page chrome (cam efekti),
          `wf-grid` ise widget renderer'in kendi 24-col grid'i. */}
      {groupWidgets.map(function (g) {
        var children = childrenByParent[g.id] || []
        if (children.length === 0) return null
        return (
          <details key={g.id} className={classPrefix + '-card'} open
            data-dyn-group-id={String(g.id)}
            data-dyn-group-label={g.label}
            style={{ marginBottom: 16, borderRadius: 14, overflow: 'hidden' }}>
            <summary className={classPrefix + '-card-title'} style={{ cursor: 'pointer', listStyle: 'none', userSelect: 'none' }}>
              {g.label}
            </summary>
            <div className="wf-grid" style={{ marginTop: 6 }}>
              {children.map(function (w) {
                return renderField(w, values[w.widgetId], handleChange, classPrefix, displays, setDisplays, grids, setGrids, visibility, disabledMap, ruleErrors, saveAttemptErrors, values)
              })}
            </div>
          </details>
        )
      })}

      {/* Grupsuz field'lar — admin'de "Genel" secildiginde veya grup bos birakildiginda
          buraya duser. Etiket "Genel" olarak gosterilir (eskiden "Ek Alanlar" idi). */}
      {childrenByParent['__ungrouped'] && childrenByParent['__ungrouped'].length > 0 && (
        <details className={classPrefix + '-card'} open
          data-dyn-group-id="__ungrouped"
          data-dyn-group-label="Genel"
          style={{ marginBottom: 16, borderRadius: 14, overflow: 'hidden' }}>
          {/* Sol panel sekme navigasyonunda zaten "Genel" basligi gosterildigi icin
              sagda tekrar etmiyoruz — sade gorunum. <details> open kalir cunku
              icerik daima goruntulenmeli; <summary> tag'i HTML gerekligi sebebiyle
              ekleniyor ama hidden. */}
          <summary className={classPrefix + '-card-title'} style={{ display: 'none' }}>Genel</summary>
          <div className="wf-grid" style={{ marginTop: 6 }}>
            {childrenByParent['__ungrouped'].map(function (w) {
              return renderField(w, values[w.widgetId], handleChange, classPrefix, displays, setDisplays, grids, setGrids, visibility, disabledMap, ruleErrors, saveAttemptErrors, values)
            })}
          </div>
        </details>
      )}

      {/* Sag alt kose toast bildirimleri — save denemesinde bos zorunlu alanlar.
          Her bos alan icin tek kart; tikla X ile dismiss veya 4.5s sonra otomatik. */}
      {requiredToasts.length > 0 && (
        <div className="wf-toast-host" role="status" aria-live="polite">
          {requiredToasts.map(function(t) {
            return (
              <div key={t.id} className="wf-toast">
                <span className="wf-toast-icon" aria-hidden="true">!</span>
                <div className="wf-toast-body">
                  <div className="wf-toast-title">{t.title}</div>
                  {t.detail && <div className="wf-toast-detail">{t.detail}</div>}
                </div>
                <button
                  type="button"
                  className="wf-toast-close"
                  onClick={function() { dismissToast(t.id) }}
                  aria-label="Kapat"
                >×</button>
              </div>
            )
          })}
        </div>
      )}

      {historyModal}
    </div>
  )
})

// ── Semantik Renk Mimarisi ────────────────────────────────────
// DB'de asla HEX kodu tutulmaz — sadece token kelimeleri.
// Token → RGBA renk ciftleri (dark/light ortak semi-transparent degerler).
var WIDGET_COLOR_MAP = {
  slate:   { border: 'rgba(148,163,184,0.55)', label: 'rgba(148,163,184,0.85)' },
  blue:    { border: 'rgba(96,165,250,0.60)',  label: 'rgba(96,165,250,0.90)'  },
  emerald: { border: 'rgba(52,211,153,0.60)',  label: 'rgba(52,211,153,0.90)'  },
  amber:   { border: 'rgba(251,191,36,0.65)',  label: 'rgba(251,191,36,0.90)'  },
  red:     { border: 'rgba(248,113,113,0.60)', label: 'rgba(248,113,113,0.90)' },
  indigo:  { border: 'rgba(129,140,248,0.65)', label: 'rgba(129,140,248,0.90)' },
}

/**
 * Semantik renk token'ini coz.
 * colorType=0 → colorValue direkt token ('amber', 'red' vb.)
 * colorType=1 → colorValue baska bir widget'in kodu; o widget'in mevcut
 *               degeri token olarak okunur (dinamik/SQL modu).
 * Gecersiz/tanimsiz token'lar icin null doner.
 */
function resolveWidgetColor(w, allValues) {
  var token = null
  if (w.colorType === 1) {
    token = (w.colorValue && allValues) ? (allValues[w.colorValue] || null) : null
  } else {
    token = w.colorValue || null
  }
  if (!token || !WIDGET_COLOR_MAP[token]) return null
  return WIDGET_COLOR_MAP[token]
}

/**
 * Tek widget input'u ciz.
 * classPrefix kullanimi ile her sayfanin mevcut glassmorphism stilini
 * taklit eder — yeni CSS yazmaya gerek yok.
 * allValues: tum formdaki mevcut degerler haritasi (dinamik renk cozumu icin).
 */
function renderField(w, value, onChange, prefix, displays, setDisplays, grids, setGrids, visibility, disabledMap, ruleErrors, saveAttemptErrors, allValues) {
  var dt = String(w.dataType || '').toLowerCase()
  var key = w.id + '-' + w.widgetId

  // Faz G — Rule Engine: visibility / disabled / formula readonly
  // visibility map'inde explicit false ise widget render edilmez (null)
  if (visibility && visibility[w.widgetId] === false) {
    return null
  }
  // Formula olan widget'lar her zaman readonly — kullanici elle yazamaz
  var hasFormula = !!(w.rules && w.rules.formula)
  // disabledIf true ise readonly — formula readonly'yi birlesir (hem formula hem disabledIf → readonly)
  var isDisabled = hasFormula || (disabledMap && disabledMap[w.widgetId] === true)
  var ruleErrorMsg = (ruleErrors && ruleErrors[w.widgetId]) || null
  // Zorunlu alan bos bırakıldıysa gorsel hata
  var hasReqError = Array.isArray(saveAttemptErrors) && saveAttemptErrors.indexOf(w.widgetId) !== -1
  // is-invalid CSS sinifi — .wf-input.is-invalid { border:red; box-shadow:red-ring }
  var reqCls = hasReqError ? ' is-invalid' : ''

  // Semantik renk coz — token yoksa null (renksiz, normal gorunum)
  var widgetColor = resolveWidgetColor(w, allValues)

  // Label artik WidgetFieldShell tarafindan uretiliyor (uc mod tek noktada).

  var inputEl = null
  switch (dt) {
    case 'text': {
      var textGuide = (w.metadata && w.metadata.guideCode) || ''
      if (textGuide) {
        // Text + rehber → lookup davranisi
        var textDisplay = (displays && displays[w.widgetId]) || ''
        // Constraints resolve: iki paralel format
        //  1) Legacy: `{w_xxx}` veya `{xxx}` (\w+) → allValues[xxx] (eski widget tokenleri)
        //  2) Standart: `{#widget.WCODE}`, `{#sqCustomerId}` → fieldTokens.resolveTokens
        // Iki regex cakismaz: `\w+` `#` ve `.` icermez; `{#...}` standart sadece nokta-prefiksli.
        var textConstraintsRaw = (w.metadata && w.metadata.constraints) || ''
        var resolvedConstraints = null
        if (textConstraintsRaw) {
          try {
            var cStr = typeof textConstraintsRaw === 'string' ? textConstraintsRaw : JSON.stringify(textConstraintsRaw)
            // 1) Legacy: {w_xxx} / {xxx}
            cStr = cStr.replace(/\{(\w+)\}/g, function(match, wid) {
              var v = allValues && allValues[wid]
              return v != null ? String(v) : ''
            })
            // 2) Standart: {#widget.WCODE} + {#sqCustomerId} (DOM) — ortak resolveTokens
            cStr = resolveAllTokens(cStr, { widgets: allValues || {} })
            resolvedConstraints = cStr
          } catch(e) { /* ignore */ }
        }
        // guideConfig: GuideSettingsModal ile widget bazinda kaydedilen JSON
        // ({ viewCode, columns:[{name,label,visible,distinct}], constraint })
        var textGuideConfig = (w.metadata && w.metadata.guideConfig) || null
        inputEl = (
          <LookupFieldInput
            widgetId={w.widgetId}
            guideCode={textGuide}
            guideConfig={textGuideConfig}
            value={value != null ? String(value) : ''}
            display={textDisplay}
            constraints={resolvedConstraints}
            isInvalid={hasReqError}
            onPick={function (picked, disp) {
              onChange(w.widgetId, picked)
              if (setDisplays) {
                setDisplays(function (prev) {
                  var next = Object.assign({}, prev || {})
                  next[w.widgetId] = disp
                  return next
                })
              }
            }}
            classPrefix={prefix}
          />
        )
      } else {
        inputEl = (
          <input
            id={'dyn_' + w.widgetId}
            type="text"
            className={'wf-input' + reqCls}
            data-widget-code={w.widgetId}
            value={value != null ? value : ''}
            onChange={function (e) { onChange(w.widgetId, e.target.value) }}
            maxLength={w.maxLength > 0 ? w.maxLength : undefined}
          />
        )
      }
      break
    }
    case 'attachment': {
      // Dosya/gorsel ek — deger merkezi Attachment tablosunun Id'si. Upload,
      // meta ve indirme AttachmentFieldInput icinde; burada sadece baglama.
      inputEl = (
        <AttachmentFieldInput
          inputId={'dyn_' + w.widgetId}
          widgetDbId={w.id}
          value={value != null ? value : ''}
          isInvalid={hasReqError}
          onChange={function (v) { onChange(w.widgetId, v) }}
        />
      )
      break
    }
    case 'textarea': {
      // Uzun metin — cok satirli. Yukseklik auto-resize yok (sabit 3 satir
      // baslangic); kullanici tarayicinin native resize tutamacini kullanabilir.
      inputEl = (
        <textarea
          id={'dyn_' + w.widgetId}
          className={'wf-textarea' + reqCls}
          data-widget-code={w.widgetId}
          value={value != null ? value : ''}
          onChange={function (e) { onChange(w.widgetId, e.target.value) }}
          maxLength={w.maxLength > 0 ? w.maxLength : undefined}
          rows={3}
          style={{ height: 'auto', minHeight: 84, resize: 'vertical', paddingTop: 8, paddingBottom: 8 }}
        />
      )
      break
    }
    case 'numeric': {
      var nMeta = (w.metadata) || {}
      var nFmt = nMeta.numericFormat || 'number'
      var nDec = parseInt(nMeta.decimalPlaces, 10)
      if (isNaN(nDec)) nDec = (nFmt === 'decimal4' ? 4 : nFmt === 'decimal2' || nFmt === 'decimal' || nFmt === 'currency' || nFmt === 'percent' ? 2 : 0)
      var nStep = nDec > 0 ? (1 / Math.pow(10, nDec)).toFixed(nDec) : '1'
      var nPrefix = nFmt === 'currency' ? '₺ ' : ''
      var nSuffix = nFmt === 'percent' ? ' %' : ''
      inputEl = (
        <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
          {nPrefix && <span style={{ fontSize: '0.82rem', fontWeight: 600, opacity: 0.5, flexShrink: 0 }}>{nPrefix}</span>}
          <input
            id={'dyn_' + w.widgetId}
            type="number"
            step={nStep}
            className={'wf-input' + reqCls}
            data-widget-code={w.widgetId}
            value={value != null && value !== '' ? value : ''}
            onChange={function (e) { onChange(w.widgetId, e.target.value) }}
            min={w.minValue != null ? w.minValue : undefined}
            max={w.maxValue != null ? w.maxValue : undefined}
            style={{ flex: 1 }}
          />
          {nSuffix && <span style={{ fontSize: '0.82rem', fontWeight: 600, opacity: 0.5, flexShrink: 0 }}>{nSuffix}</span>}
        </div>
      )
      break
    }
    case 'date': {
      var dateVal = value || ''
      // Server format "yyyy-MM-dd"; input type=date expects the same
      inputEl = (
        <input
          id={'dyn_' + w.widgetId}
          type="date"
          className={'wf-input' + reqCls}
          data-widget-code={w.widgetId}
          value={dateVal}
          onChange={function (e) { onChange(w.widgetId, e.target.value) }}
        />
      )
      break
    }
    case 'boolean': {
      var isOn = value === true || value === 'true' || value === '1'
      // Switch toggle — iOS-style. Sadece MODERN baslik stilinde tutarli goruntu
      // icin cerceveli container'a alinir (textbox gibi). Aksi halde minimal
      // (baska input'lari taklit eden cerceve olmadan). "Evet/Hayir" metni
      // gosterilmez — switch zaten durumu tasiyor.
      var booleanIsModern = w.labelStyle === 'modern'
      inputEl = (
        <label
          htmlFor={'dyn_' + w.widgetId}
          data-widget-code={w.widgetId}
          className={(booleanIsModern ? 'wf-input ' : '') + reqCls}
          style={Object.assign({
            display: 'inline-flex',
            alignItems: 'center',
            gap: 0,
            cursor: 'pointer',
            userSelect: 'none',
            boxSizing: 'border-box',
          }, booleanIsModern ? {
            width: '100%',
            minHeight: 36,
            height: 'auto',
            padding: '6px 12px',
          } : {
            marginTop: 4,
          })}
        >
          <input
            id={'dyn_' + w.widgetId}
            type="checkbox"
            data-widget-code={w.widgetId}
            checked={isOn}
            onChange={function (e) { onChange(w.widgetId, e.target.checked) }}
            style={{
              position: 'absolute',
              width: 1, height: 1, padding: 0, margin: -1,
              overflow: 'hidden', clip: 'rect(0,0,0,0)',
              whiteSpace: 'nowrap', border: 0,
            }}
          />
          <span
            aria-hidden="true"
            style={{
              position: 'relative',
              display: 'inline-block',
              width: 50, height: 28,
              borderRadius: 999,
              background: isOn ? '#10b981' : 'rgba(148, 163, 184, 0.38)',
              transition: 'background-color 0.18s ease',
              flexShrink: 0,
              boxShadow: 'inset 0 1px 2px rgba(0,0,0,0.18)',
            }}
          >
            <span
              style={{
                position: 'absolute',
                top: 2, left: isOn ? 24 : 2,
                width: 24, height: 24,
                borderRadius: '50%',
                background: '#ffffff',
                boxShadow: '0 2px 5px rgba(0,0,0,0.28)',
                transition: 'left 0.18s ease',
              }}
            />
          </span>
        </label>
      )
      break
    }
    case 'dropdown': {
      var opts = Array.isArray(w.options) ? w.options : []
      inputEl = (
        <select
          id={'dyn_' + w.widgetId}
          className={'wf-select' + reqCls}
          data-widget-code={w.widgetId}
          value={value != null ? value : ''}
          onChange={function (e) { onChange(w.widgetId, e.target.value) }}
        >
          <option value="">— Secim —</option>
          {opts.map(function (o) { return <option key={o} value={o}>{o}</option> })}
        </select>
      )
      break
    }
    case 'multi-select': {
      var msOpts = Array.isArray(w.options) ? w.options : []
      var selected = Array.isArray(value)
        ? value
        : (typeof value === 'string' && value ? value.split(',') : [])
      // Chip / pill-tabanli coklu secim — native checkbox yerine klikli rozetler.
      // wf-multicheck token uyumlu container (kendi bg/border/radius'u).
      inputEl = (
        <div
          className={'wf-multicheck' + reqCls}
          data-widget-code={w.widgetId}
        >
          {msOpts.map(function (o) {
            var isChecked = selected.indexOf(o) !== -1
            function toggle() {
              var next = isChecked
                ? selected.filter(function (v) { return v !== o })
                : selected.concat([o])
              onChange(w.widgetId, next)
            }
            return (
              <span
                key={o}
                role="button"
                tabIndex={0}
                aria-pressed={isChecked}
                onClick={toggle}
                onKeyDown={function(e) {
                  if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault()
                    toggle()
                  }
                }}
                style={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  gap: 6,
                  height: 28,
                  padding: '0 12px',
                  fontSize: '0.78rem',
                  fontWeight: 600,
                  borderRadius: 999,
                  cursor: 'pointer',
                  userSelect: 'none',
                  transition: 'background-color 0.15s, border-color 0.15s, color 0.15s',
                  border: isChecked
                    ? '1px solid rgba(99,102,241,0.55)'
                    : '1px solid rgba(148,163,184,0.35)',
                  background: isChecked
                    ? 'rgba(99,102,241,0.20)'
                    : 'transparent',
                  color: isChecked
                    ? 'rgba(165,180,252,0.98)'
                    : 'rgba(148,163,184,0.85)',
                }}
              >
                {isChecked && (
                  <span aria-hidden="true" style={{
                    display: 'inline-block', width: 6, height: 6, borderRadius: '50%',
                    background: '#a5b4fc',
                  }} />
                )}
                {o}
              </span>
            )
          })}
          {msOpts.length === 0 && (
            <span className="wf-multicheck-empty" style={{ fontSize: '0.78rem', opacity: 0.5 }}>
              Seçenek tanımlanmamış
            </span>
          )}
        </div>
      )
      break
    }
    case 'link': {
      // Link widget — input + "Git" butonu input-group.
      // options[0] = URL sablonu, {value} runtime'da kullanicinin girdigi ile
      // yer degistirir. Input bos veya sablon yok ise buton disabled.
      var linkTemplate = (Array.isArray(w.options) && w.options[0]) || ''
      var linkCurrent  = value != null ? String(value) : ''
      var hasLinkTpl   = linkTemplate.length > 0
      var linkHref     = hasLinkTpl && linkCurrent.length > 0
        ? linkTemplate.replace('{value}', encodeURIComponent(linkCurrent))
        : ''
      var linkDisabled = !hasLinkTpl || linkCurrent.length === 0
      inputEl = (
        <div className="wf-link-group">
          <input
            id={'dyn_' + w.widgetId}
            type="text"
            className={'wf-input' + reqCls}
            data-widget-code={w.widgetId}
            value={linkCurrent}
            onChange={function (e) { onChange(w.widgetId, e.target.value) }}
            placeholder={hasLinkTpl ? w.label : 'URL sablonu tanimlanmamis'}
          />
          <a
            className={'wf-link-btn' + (linkDisabled ? ' is-disabled' : '')}
            href={linkDisabled ? undefined : linkHref}
            target="_blank"
            rel="noopener noreferrer"
            aria-disabled={linkDisabled}
            onClick={linkDisabled ? function (e) { e.preventDefault() } : undefined}
            title={hasLinkTpl
              ? (linkCurrent ? 'Git: ' + linkHref : 'Once bir deger girin')
              : 'URL sablonu tanimli degil'}
          >
            <span>Git</span>
            <span aria-hidden="true" style={{ fontSize: '0.8em', lineHeight: 1 }}>↗</span>
          </a>
        </div>
      )
      break
    }
    case 'guide-list': {
      // displayScope: 'card' ise form'da render etme (SmartCard popup'a ozel).
      // 'form' ise akordion default ACIK gelir; 'both' default kapali.
      var glScope = String((w.metadata && w.metadata.displayScope) || 'both').toLowerCase()
      if (glScope === 'card') return null
      // Salt okunur akordion rehber listesi — kullanici secim yapmaz, DB'ye
      // deger yazilmaz. WHERE constraint form alanlarindan token resolve ile
      // runtime'da uretilir; akordion lazy fetch yapar.
      // Constraint kaynagi: metadata.guideConfig (JSON string) → parse → .constraint
      // GuideCustomizationModal serializeRawSql() ile [{rawSql,logic}] JSON yazar.
      var glGuide = (w.metadata && w.metadata.guideCode) || ''
      var glConfigRaw = (w.metadata && w.metadata.guideConfig) || null
      var glConfig = null
      try {
        glConfig = (typeof glConfigRaw === 'string') ? JSON.parse(glConfigRaw) : glConfigRaw
      } catch (e) { glConfig = null }
      var glConstraintsRaw = (glConfig && glConfig.constraint) || ''
      // Constraint, [{rawSql,logic}] JSON array string'i. Token resolve string
      // uzerinde calistirilir — backend mergeConstraints yine JSON.parse eder.
      var glResolvedConstraints = null
      if (glConstraintsRaw) {
        try {
          var glStr = typeof glConstraintsRaw === 'string' ? glConstraintsRaw : JSON.stringify(glConstraintsRaw)
          // Legacy: {w_xxx} / {xxx}
          glStr = glStr.replace(/\{(\w+)\}/g, function(match, wid) {
            var v = allValues && allValues[wid]
            return v != null ? String(v) : ''
          })
          // Standart: {#widget.WCODE} / {#sqCustomerId} → fieldTokens.resolveTokens
          glStr = resolveAllTokens(glStr, { widgets: allValues || {} })
          glResolvedConstraints = glStr
        } catch (e) { /* ignore */ }
      }
      // Akordion kendi <details><summary>'si ile baslik tasir; widget Shell'in
      // label'i yerine direkt full-width div ile cikar.
      // Form'da render edilen tum guide-list'ler default ACIK gelir (sandwich
      // tiklamaya gerek yok). 'card' scope'ta zaten yukarida null donduk.
      return (
        <div key={key} className="wf-field wf-field--full" style={{ gridColumn: '1 / -1' }}>
          <GuideListField
            widgetId={w.widgetId}
            label={w.label}
            guideCode={glGuide}
            guideConfig={glConfig}
            constraints={glResolvedConstraints}
            classPrefix={prefix}
            alwaysOpen={true}
          />
        </div>
      )
    }
    case 'lookup': {
      // EAV lookup — LookupFieldInput tum UX'i yonetir:
      //   readonly input + search butonu → debounced arama + infinite scroll modal
      // Metadata'dan guideCode alinir; guideCode yoksa widget inactive gorunur.
      var guideCode = (w.metadata && w.metadata.guideCode) || ''
      var displayValue = (displays && displays[w.widgetId]) || ''
      // guideConfig: GuideSettingsModal ile widget bazinda kaydedilen JSON
      // ({ viewCode, columns:[{name,label,visible,distinct}], constraint })
      var guideConfigStr = (w.metadata && w.metadata.guideConfig) || null
      inputEl = (
        <LookupFieldInput
          widgetId={w.widgetId}
          guideCode={guideCode}
          guideConfig={guideConfigStr}
          value={value != null ? String(value) : ''}
          display={displayValue}
          onPick={function (picked, disp) {
            onChange(w.widgetId, picked)
            if (setDisplays) {
              setDisplays(function (prev) {
                var next = Object.assign({}, prev || {})
                next[w.widgetId] = disp
                return next
              })
            }
          }}
          classPrefix={prefix}
        />
      )
      break
    }
    case 'grid': {
      // Faz E — master-detail grid widget.
      // GridFieldInput tum UX'i yonetir: card-tabanli satir listesi + satir
      // ekleme/duzenleme/silme + embedded DynamicWidgetRenderer (satir modal'i).
      // grids state'inden mevcut rows gelir; ekleme/silme setGrids ile yansir.
      var childFormCode = (w.metadata && w.metadata.childFormCode) || ''
      var gridState = (grids && grids[w.widgetId]) || { childFormCode: childFormCode, rows: [] }
      return (
        <div key={key} className="wf-field wf-field--full" style={{ gridColumn: '1 / -1' }}>
          <label className="wf-label">{w.label}</label>
          <GridFieldInput
            widgetId={w.widgetId}
            label={w.label}
            childFormCode={childFormCode}
            rows={gridState.rows || []}
            onRowsChange={function (nextRows) {
              if (!setGrids) return
              setGrids(function (prev) {
                var next = Object.assign({}, prev || {})
                next[w.widgetId] = { childFormCode: childFormCode, rows: nextRows }
                return next
              })
            }}
            classPrefix={prefix}
          />
        </div>
      )
    }
    default: {
      // Bilinmeyen tip — text fallback
      inputEl = (
        <input
          id={'dyn_' + w.widgetId}
          type="text"
          className={'wf-input' + reqCls}
          data-widget-code={w.widgetId}
          value={value != null ? value : ''}
          onChange={function (e) { onChange(w.widgetId, e.target.value) }}
        />
      )
    }
  }

  // Modern label "isFilled" hesabi — Shell'e gecirilir, data-filled attribute icin.
  // Boolean/multi-select/grid/lookup/dropdown/link/date... gibi her zaman dolu kabul edilen
  // tipler true; text/number gibi tipler value'ya gore.
  var modernDataType = String(w.dataType || '').toLowerCase()
  var alwaysFilledTypes = ['boolean', 'multi-select', 'grid', 'lookup', 'dropdown', 'link',
                            'date', 'datetime', 'datetime-local', 'time']
  var isFilled
  if (alwaysFilledTypes.indexOf(modernDataType) !== -1) {
    isFilled = true
  } else if (value == null) {
    isFilled = false
  } else if (typeof value === 'string') {
    isFilled = value.trim() !== ''
  } else if (Array.isArray(value)) {
    isFilled = value.length > 0
  } else {
    isFilled = true
  }

  // Tek-yol-wrapper: WidgetFieldShell uc label modunu (standard/modern/inline)
  // tek yerde yonetir. labelStyle, isPlainField, colSpan, isRequired Shell icinde
  // resolve edilir. classPrefix readonly-wrap class adi icin (legacy uyumluluk).
  return (
    <WidgetFieldShell
      key={key}
      widget={w}
      isFilled={isFilled}
      isDisabled={isDisabled}
      hasFormula={hasFormula}
      ruleErrorMsg={ruleErrorMsg}
      widgetColor={widgetColor}
      classPrefix={prefix}
    >
      {inputEl}
    </WidgetFieldShell>
  )
}

export default DynamicWidgetRenderer
