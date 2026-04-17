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
import { Settings, X } from 'lucide-react'
import { getRecord, saveRecord, guideResolve } from './dynamicWidgetService'
import LookupFieldInput from './LookupFieldInput'
import GridFieldInput from './GridFieldInput'
import { buildRuleGraph, recomputeAll, propagateChange } from './ruleEngine'

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

var DynamicWidgetRenderer = forwardRef(function DynamicWidgetRenderer(props, ref) {
  var formCode    = props.formCode
  var initialRecordId = props.recordId || ''
  var classPrefix = props.classPrefix || 'mce'
  var containerId = props.containerId
  var onMounted   = props.onMounted
  // Faz E — grid row modal embed senaryosu: mevcut satir degerleri onceden
  // doldurulmus olarak renderer'a verilir, server'a save yapilmaz, ref.getValues()
  // ile parent (GridFieldInput) degerleri cekip kendi grid state'ine pop eder.
  var initialValues = props.initialValues && typeof props.initialValues === 'object'
    ? props.initialValues : null

  var [loading, setLoading]   = useState(true)
  var [widgets, setWidgets]   = useState([])      // WidgetRenderDto[]
  var [values, setValues]     = useState({})      // { widgetCode: value }
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
  //   ruleErrors: { [code]: string }          — widget altinda inline hata mesaji
  //   cycleError:  string | null              — mount-time cycle → form cizilmez
  var [visibility, setVisibility] = useState({})
  var [disabledMap, setDisabledMap] = useState({})
  var [ruleErrors, setRuleErrors] = useState({})
  var [cycleError, setCycleError] = useState(null)
  // Hangi widget'ların edit formunda görüneceği — localStorage'dan yükle
  // Tum aktif widget'lar her zaman gorunur — enabledIds kaldirildi
  function isWidgetEnabled() { return true }
  var [configOpen, setConfigOpen] = useState(false)
  var configPanelRef = useRef(null)
  // Zorunlu alan validasyonu — save denemesinde bos kalan zorunlu widgetId listesi.
  // handleChange'de temizlenir; save basarili olunca da sifirlanir.
  var [saveAttemptErrors, setSaveAttemptErrors] = useState([])
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
          dict[w.widgetId] = v
        })
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
          setValues(dict)
          setGrids(gDict)
          setVisibility({})
          setDisabledMap({})
          setRuleErrors({})
          recordIdRef.current = activeRecordId
        } else if (graph.fatalErrors.length > 0) {
          setCycleError(graph.fatalErrors.join(' | '))
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
            setValues(patch.values)
            setVisibility(patch.visibility)
            setDisabledMap(patch.disabled)
            setRuleErrors(patch.errors)
          } else {
            setValues(dict)
            setVisibility({})
            setDisabledMap({})
            setRuleErrors({})
          }
          setGrids(gDict)
          recordIdRef.current = activeRecordId
        }

        // Lookup widget'lari icin display cozumleme — paralel fetch, hata sessiz.
        // Guide yoksa / deger bos ise skipped.
        var lookupJobs = ws
          .filter(function (w) {
            return String(w.dataType || '').toLowerCase() === 'lookup'
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

      // Zorunlu alan validasyonu
      var requiredErrors = []    // label listesi (hata mesaji icin)
      var requiredErrorIds = []  // widgetId listesi (gorsel hata icin)
      currentWidgets.forEach(function (w) {
        if (!w.isRequired) return
        if (String(w.dataType || '').toLowerCase() === 'group') return
        var val = currentValues[w.widgetId]
        var isEmpty = val === null || val === undefined || val === ''
          || (Array.isArray(val) && val.length === 0)
        if (isEmpty) {
          requiredErrors.push(w.label || w.widgetId)
          requiredErrorIds.push(w.widgetId)
        }
      })
      if (requiredErrors.length > 0) {
        setSaveAttemptErrors(requiredErrorIds)
        return Promise.resolve({
          success: false,
          message: 'Zorunlu alanlar bos birakilamaz: ' + requiredErrors.join(', '),
          requiredErrors: requiredErrors,
        })
      }
      setSaveAttemptErrors([])
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
      var requiredErrors    = []
      var requiredErrorIds  = []
      currentWidgets.forEach(function (w) {
        if (!w.isRequired) return
        if (String(w.dataType || '').toLowerCase() === 'group') return
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
        if (String(w.dataType || '').toLowerCase() === 'group') return
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
        setSaveAttemptErrors(requiredErrorIds)
        return { valid: false, errors: requiredErrors, errorIds: requiredErrorIds }
      }
      setSaveAttemptErrors([])
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
      errors: errorsRef.current,
    }
    var patch = propagateChange(widgetCode, newValue, graph, currentState)
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

  return (
    <div className={classPrefix + '-dyn-root'} data-widget-renderer>

      {/* Grup'lara gore ayri kartlar */}
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
            <div className={classPrefix + '-grid-2'} style={{ marginTop: 6 }}>
              {children.map(function (w) {
                return renderField(w, values[w.widgetId], handleChange, classPrefix, displays, setDisplays, grids, setGrids, visibility, disabledMap, ruleErrors, saveAttemptErrors, values)
              })}
            </div>
          </details>
        )
      })}

      {/* Grupsuz field'lar — ayri kart */}
      {childrenByParent['__ungrouped'] && childrenByParent['__ungrouped'].length > 0 && (
        <details className={classPrefix + '-card'} open
          data-dyn-group-id="__ungrouped"
          data-dyn-group-label="Ek Alanlar"
          style={{ marginBottom: 16, borderRadius: 14, overflow: 'hidden' }}>
          <summary className={classPrefix + '-card-title'} style={{ cursor: 'pointer', listStyle: 'none', userSelect: 'none' }}>
            Ek Alanlar
          </summary>
          <div className={classPrefix + '-grid-2'} style={{ marginTop: 6 }}>
            {childrenByParent['__ungrouped'].map(function (w) {
              return renderField(w, values[w.widgetId], handleChange, classPrefix, displays, setDisplays, grids, setGrids, visibility, disabledMap, ruleErrors, saveAttemptErrors, values)
            })}
          </div>
        </details>
      )}
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
  // is-invalid CSS sinifi — .mce-input.is-invalid { border:red; animation:mceShake }
  var reqCls = hasReqError ? ' is-invalid' : ''

  // Semantik renk coz — token yoksa null (renksiz, normal gorunum)
  var widgetColor = resolveWidgetColor(w, allValues)

  var labelEl = (
    <label
      className={prefix + '-label'}
      htmlFor={'dyn_' + w.widgetId}
      style={widgetColor ? { color: widgetColor.label } : undefined}
    >
      {w.label}
      {w.isRequired && <span style={{ color: '#f87171', marginLeft: 3, fontWeight: 700 }}>*</span>}
    </label>
  )

  var inputEl = null
  switch (dt) {
    case 'text': {
      var textGuide = (w.metadata && w.metadata.guideCode) || ''
      if (textGuide) {
        // Text + rehber → lookup davranisi
        var textDisplay = (displays && displays[w.widgetId]) || ''
        // Constraints resolve: {w_xxx} tokenlarini form degerlerinden coz
        var textConstraintsRaw = (w.metadata && w.metadata.constraints) || ''
        var resolvedConstraints = null
        if (textConstraintsRaw) {
          try {
            var cStr = typeof textConstraintsRaw === 'string' ? textConstraintsRaw : JSON.stringify(textConstraintsRaw)
            // {w_xxx} tokenlarini degerlerle degistir
            cStr = cStr.replace(/\{(\w+)\}/g, function(match, wid) {
              var v = allValues && allValues[wid]
              return v != null ? String(v) : ''
            })
            resolvedConstraints = cStr
          } catch(e) { /* ignore */ }
        }
        inputEl = (
          <LookupFieldInput
            widgetId={w.widgetId}
            guideCode={textGuide}
            value={value != null ? String(value) : ''}
            display={textDisplay}
            constraints={resolvedConstraints}
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
            className={prefix + '-input' + reqCls}
            data-widget-code={w.widgetId}
            value={value != null ? value : ''}
            onChange={function (e) { onChange(w.widgetId, e.target.value) }}
            maxLength={w.maxLength > 0 ? w.maxLength : undefined}
          />
        )
      }
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
            className={prefix + '-input' + reqCls}
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
          className={prefix + '-input' + reqCls}
          data-widget-code={w.widgetId}
          value={dateVal}
          onChange={function (e) { onChange(w.widgetId, e.target.value) }}
        />
      )
      break
    }
    case 'boolean': {
      var isOn = value === true || value === 'true' || value === '1'
      inputEl = (
        <label
          htmlFor={'dyn_' + w.widgetId}
          style={{ display: 'inline-flex', alignItems: 'center', gap: 8, marginTop: 4, cursor: 'pointer' }}
        >
          <input
            id={'dyn_' + w.widgetId}
            type="checkbox"
            data-widget-code={w.widgetId}
            checked={isOn}
            onChange={function (e) { onChange(w.widgetId, e.target.checked) }}
          />
          <span style={{ fontSize: '0.8rem' }}>{isOn ? 'Evet' : 'Hayir'}</span>
        </label>
      )
      break
    }
    case 'dropdown': {
      var opts = Array.isArray(w.options) ? w.options : []
      inputEl = (
        <select
          id={'dyn_' + w.widgetId}
          className={prefix + '-select' + reqCls}
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
      inputEl = (
        <div className={prefix + '-multicheck' + reqCls} data-widget-code={w.widgetId}>
          {msOpts.map(function (o) {
            var isChecked = selected.indexOf(o) !== -1
            return (
              <label key={o} className={prefix + '-multicheck-item' + (isChecked ? ' is-checked' : '')}>
                <input
                  type="checkbox"
                  checked={isChecked}
                  onChange={function () {
                    var next = isChecked
                      ? selected.filter(function (v) { return v !== o })
                      : selected.concat([o])
                    onChange(w.widgetId, next)
                  }}
                />
                <span>{o}</span>
              </label>
            )
          })}
          {msOpts.length === 0 && (
            <span className={prefix + '-multicheck-empty'}>Secenek tanimlanmamis</span>
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
        <div className={prefix + '-link-group'}>
          <input
            id={'dyn_' + w.widgetId}
            type="text"
            className={prefix + '-input' + reqCls}
            data-widget-code={w.widgetId}
            value={linkCurrent}
            onChange={function (e) { onChange(w.widgetId, e.target.value) }}
            placeholder={hasLinkTpl ? w.label : 'URL sablonu tanimlanmamis'}
          />
          <a
            className={prefix + '-link-btn' + (linkDisabled ? ' is-disabled' : '')}
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
    case 'lookup': {
      // EAV lookup — LookupFieldInput tum UX'i yonetir:
      //   readonly input + search butonu → debounced arama + infinite scroll modal
      // Metadata'dan guideCode alinir; guideCode yoksa widget inactive gorunur.
      var guideCode = (w.metadata && w.metadata.guideCode) || ''
      var displayValue = (displays && displays[w.widgetId]) || ''
      inputEl = (
        <LookupFieldInput
          widgetId={w.widgetId}
          guideCode={guideCode}
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
        <div key={key} className={prefix + '-field ' + prefix + '-field--grid'} style={{ gridColumn: '1 / -1' }}>
          <label className={prefix + '-label'}>{w.label}</label>
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
          className={prefix + '-input' + reqCls}
          data-widget-code={w.widgetId}
          value={value != null ? value : ''}
          onChange={function (e) { onChange(w.widgetId, e.target.value) }}
        />
      )
    }
  }

  // isPlainField: grup wrapper olmadan sade label + input — yatay hizalı düzen.
  // gridColumn:1/-1 ile 2-sutunlu grid'de tam genislik kaplar; tum plainField
  // satirlarinin inputlari ayni X noktasindan baslar (sabit 160px label sutunu).
  // Zorunlu hata: is-invalid sinifi input'a eklendi (reqCls ile), wrapper div yok.
  var inputWithError = inputEl

  if (w.isPlainField) {
    return (
      <div
        key={key}
        style={Object.assign({
          display: 'flex',
          alignItems: 'center',
          gap: 12,
          gridColumn: '1 / -1',
          minWidth: 0,
          padding: '4px 0 4px 6px',
        }, widgetColor ? {
          borderLeft: '3px solid ' + widgetColor.border,
          paddingLeft: 8,
          borderRadius: '0 4px 4px 0',
        } : {})}
      >
        <label
          className={prefix + '-label'}
          htmlFor={'dyn_' + w.widgetId}
          style={{ width: 160, minWidth: 160, flexShrink: 0, textAlign: 'left', marginBottom: 0 }}
        >
          {w.label}
          {w.isRequired && <span style={{ color: '#f87171', marginLeft: 3, fontWeight: 700 }}>*</span>}
        </label>
        <div style={{ flex: 1, minWidth: 0 }}>
          {isDisabled ? (
            <div className={prefix + '-field__readonly-wrap'} style={{ pointerEvents: 'none' }}>
              {inputWithError}
            </div>
          ) : inputWithError}
          {ruleErrorMsg && (
            <div className={prefix + '-rule-error'}>{ruleErrorMsg}</div>
          )}
        </div>
      </div>
    )
  }

  // Faz G — field wrapper'a disabled/formula sinifi ekle, ruleError varsa inline metin goster
  var wrapperCls = prefix + '-field'
  if (isDisabled) wrapperCls += ' ' + prefix + '-field--readonly'
  if (hasFormula) wrapperCls += ' ' + prefix + '-field--formula'
  if (ruleErrorMsg) wrapperCls += ' ' + prefix + '-field--has-rule-error'

  return (
    <div
      key={key}
      className={wrapperCls}
      style={widgetColor ? {
        borderLeft: '3px solid ' + widgetColor.border,
        paddingLeft: 8,
        borderRadius: '0 6px 6px 0',
      } : undefined}
    >
      {labelEl}
      {/* pointer-events: none + opacity CSS ile readonly efekti — tum input tipleri icin tek noktada */}
      {isDisabled ? (
        <div className={prefix + '-field__readonly-wrap'} style={{ pointerEvents: 'none' }}>
          {inputWithError}
        </div>
      ) : inputWithError}
      {ruleErrorMsg && (
        <div className={prefix + '-rule-error'}>{ruleErrorMsg}</div>
      )}
    </div>
  )
}

export default DynamicWidgetRenderer
