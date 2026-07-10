/**
 * CalibraLineItemsGrid — Dinamik, satir-ici duzenlenebilir kalem grid'i
 *
 * "Aptal Bilesen, Zeki Veri": Kolonlar + satirlar C#'tan gelen JSON
 * (BuildDocumentLineGridConfig) ile dinamik cizilir. React icinde hardcoded
 * alan ismi / siralama YOK.
 *
 * Glassmorphism container + Tailwind + framer-motion satir animasyonlari.
 *
 * Props:
 *   config: { schemaVersion, columns, rows, labels, footer }
 *   onRowsChange: function(rows) — her degisiklikte cagirilir (vanilla JS bridge)
 *
 * Imperative API (window.CalibraHub.salesLineGrid):
 *   setRows(rows) — initial data load icin (AJAX'tan gelen lines)
 *   getRows()     — save flow icin
 */
import { useState, useCallback, useEffect, useRef, useMemo } from 'react'
import { createPortal } from 'react-dom'
import { motion, AnimatePresence } from 'framer-motion'
import {
  Plus, Trash2, Pencil, Hash, FileText, Ruler, Sigma, DollarSign,
  Percent, Calculator, StickyNote, CircleDot, Lock, Pin, PinOff,
  Settings, X as XIcon, GitBranch, History, AlertTriangle,
  MoreHorizontal, ExternalLink, ChevronRight, Tag, Barcode,
} from 'lucide-react'
import { navigateInWorkspace } from '../../utils/workspaceNav'
import LineGridCell, { CombinationLookupCell } from './LineGridCell'
import CostViewerModal from './CostViewerModal'
import QuoteCostSummaryModal from './QuoteCostSummaryModal'
import { evaluate } from './formulaEvaluator'
import { getTopBody } from '../../utils/topPortal'
import DynamicWidgetRenderer from '../DynamicWidgetRenderer/DynamicWidgetRenderer'
import { loadDecimalSettings, resolveColumnDecimals, roundTo, onDecimalSettingsChanged } from '../../utils/decimalSettings'

/* Lucide icon haritasi — C#'taki icon string'ini React bilesenine cevirir */
var ICON_MAP = {
  Hash: Hash,
  FileText: FileText,
  Ruler: Ruler,
  Sigma: Sigma,
  DollarSign: DollarSign,
  Percent: Percent,
  Calculator: Calculator,
  StickyNote: StickyNote,
  Tag: Tag,
  Barcode: Barcode,
}
function resolveIcon(name) {
  return ICON_MAP[name] || CircleDot
}

/* Satir icin benzersiz _uid uret (React key ve yerel takip icin) */
var uidCounter = 0
function makeUid() {
  uidCounter += 1
  return 'row-' + Date.now() + '-' + uidCounter
}

/* Her satir icin computed hucreleri hesaplayip satira gomer.
   Satir save'de ayni sekilde gonderilecektir — server yine kendi hesaplayacak.
   Kolonun precision'i (ondalik ayarindan override edilmis olabilir) hesap
   SONUCUNA uygulanir — gosterim degil, saklanan deger yuvarlanir. */
function applyComputed(row, columns) {
  var result = Object.assign({}, row)
  columns.forEach(function(col) {
    if (col.computed && col.formula) {
      var v = evaluate(col.formula, result)
      result[col.key] = (col.precision != null) ? roundTo(v, col.precision) : v
    }
  })
  return result
}

function TR_FMT(n, precision) {
  if (n == null || isNaN(n)) return '0,00'
  return Number(n).toLocaleString('tr-TR', {
    minimumFractionDigits: precision != null ? precision : 2,
    maximumFractionDigits: precision != null ? precision : 2,
  })
}

export default function CalibraLineItemsGrid(props) {
  var config = props.config || { columns: [], rows: [], labels: {}, footer: {} }
  // 2026-06-01: documentTypeCode = "alis_talebi" (İhtiyaç Kaydı) ise satir context
  // menusunden Fiyat Geçmişi + Maliyet Gör + Revize Et gizlenir — talep ic hareket;
  // fiyatlandirma teklif/siparis asamasinda olusur, revize akisi gerekmez.
  var __docTypeCode = String(config.documentTypeCode || '').toLowerCase()
  var __isPurchaseRequest = __docTypeCode === 'alis_talebi'
  // 2026-06-02: Satir ek alanlari icin form code'u config'ten al — daha once
  // hardcoded 'SALES_QUOTE_LINES' idi. Ihtiyac Kaydi (alis_talebi) icin dogru
  // kod 'PURCHASE_REQUEST_LINES' — hardcoded olunca modal YANLIS form'un
  // widget'larini gosteriyor + Kaydet YANLIS form tablosuna yaziyordu
  // (gear kirmizi kaliyordu cunku backend dogru formu kontrol edip eksik
  // goruyordu). Config'ten gelmezse legacy 'SALES_QUOTE_LINES' fallback.
  var __lineFormCode = String(config.lineFormCode || 'SALES_QUOTE_LINES')

  // ── Ondalık ayarları (form bazında) — kolon precision'larını override eder ──
  // Ayar formu: config.decimalFormCode (açık bildirim) → lineFormCode fallback.
  // Yüklenene kadar C# config'indeki precision'lar geçerli kalır (görsel fark
  // en fazla ilk render'da olur; ayar gelince kolonlar + hesaplar güncellenir).
  var [decimalCfg, setDecimalCfg] = useState(null)
  useEffect(function () {
    var fc = config.decimalFormCode || config.lineFormCode || 'SALES_QUOTE_LINES'
    var alive = true
    function loadIt() {
      loadDecimalSettings(fc).then(function (dec) { if (alive) setDecimalCfg(dec) })
    }
    loadIt()
    // Ondalık Ayarları ekranında kayıt → broadcast → açık grid canlı tazelenir
    var off = onDecimalSettingsChanged(loadIt)
    return function () { alive = false; off() }
  }, [config.decimalFormCode, config.lineFormCode])

  var allColumns = useMemo(function () {
    var src = Array.isArray(config.columns) ? config.columns : []
    if (!decimalCfg) return src
    return src.map(function (c) {
      var p = resolveColumnDecimals(c, decimalCfg)
      return p == null ? c : Object.assign({}, c, { precision: p })
    })
  }, [config.columns, decimalCfg])
  // Kolonlari yerlesime gore ayir:
  //   - row-below  : satirin altinda (ornek: Not)
  //   - inline     : satir icinde cell olarak
  //   - action     : Islem kolonuna buton olarak (combination-lookup burada)
  var columns = allColumns.filter(function(c) {
    return c.placement !== 'row-below' && c.type !== 'combination-lookup'
  })
  var belowColumns = allColumns.filter(function(c) { return c.placement === 'row-below' })
  var actionLookupColumns = allColumns.filter(function(c) { return c.type === 'combination-lookup' })
  var labels = config.labels || {}
  var footer = config.footer || {}
  var onRowsChange = props.onRowsChange

  // ── Silme: modal yerine satir-ici geri sayim (Gmail "Undo" patterni) ──
  // pendingDelete[rowUid] = true ise satir 3 saniye icinde silinir; kullanici
  // "İptal" tuşuna basarsa silme iptal edilir. Timeout ID'leri ref'te tutulur.
  var DELETE_COUNTDOWN_MS = 3000
  var [pendingDelete, setPendingDelete] = useState(function () { return {} })
  var deleteTimeoutsRef = useRef({})
  // ── Duzeltme modu per satir (kilit/unlock mantigi icin altyapi) ──
  var [editingRowUid, setEditingRowUid] = useState(null)
  // ── "Not ekle" ile acilan satirlar (row-below kolonlarini gostermek icin) ──
  var [openNoteRows, setOpenNoteRows] = useState(function() { return {} })
  // ── Satir-basi "Ek Alanlar" modali icin hedef satir ──
  //   row.id > 0 olan (kayitli) satirlar icin SALES_QUOTE_LINES formundaki
  //   dinamik alanlari DynamicWidgetRenderer ile gosterir.
  var [extrasModalRow, setExtrasModalRow] = useState(null)
  // ── Zorunlu widget eksik olan satir ID'leri — ⚙ butonu rengini belirler
  //   (kirmizi = eksik, yesil = saved & OK, sky = unsaved).
  var [invalidLineIds, setInvalidLineIds] = useState(function() { return [] })
  var [shakeTick, setShakeTick] = useState(0)
  var [extrasSaving, setExtrasSaving] = useState(false)
  var [extrasToast, setExtrasToast] = useState(null) // { type: 'ok'|'err', text }
  var extrasRendererRef = useRef(null)
  // ── Revize modal — satir bazli revizyon surec destegi ──
  //   Kullanici satir aksiyon seridindeki Revize butonuna bastiginda acilir.
  //   2 sekme: "Revize Et" (yeni revize olustur) + "Gecmis Revizeler" (zincir).
  //   Yeni revize "Revize Olustur" ile eklenir; orjinal satir degismez, yeni
  //   satir revised_from_id = secili satirin id'si ile eklenir.
  var [reviseModal, setReviseModal] = useState(null) // { row, tab: 'revise'|'history', draft:{...} }

  // ── Belge para birimi (#sqCurrency'den okunur, change'de senkron) ──
  // Toplam alaninin sag tarafinda ve para birimi gosterilen yerlerde
  // kullanilir. Default TRY; programatik set sonrasi 'sq:currency' window
  // event'i ile de senkronlanir (DocumentEdit yukleyici tarafinda dispatch).
  var [docCurrency, setDocCurrency] = useState(function () {
    if (typeof document === 'undefined') return 'TRY'
    var el = document.getElementById('sqCurrency')
    return (el && el.value) ? el.value : 'TRY'
  })
  useEffect(function () {
    var el = (typeof document !== 'undefined') ? document.getElementById('sqCurrency') : null
    function syncFromEl() {
      if (el && el.value) setDocCurrency(el.value)
    }
    function onCustom(e) {
      var code = (e && e.detail && e.detail.code) || (el && el.value) || 'TRY'
      setDocCurrency(code)
    }
    if (el) el.addEventListener('change', syncFromEl)
    window.addEventListener('sq:currency', onCustom)
    // Mount'tan sonra select'in degeri (sqLoadQuote ile) sonradan setlenebilir —
    // kucuk bir polling ile ilk degeri yakala (bir kerelik).
    var attempts = 0
    var poll = setInterval(function () {
      if (attempts++ > 20) { clearInterval(poll); return }
      var v = el && el.value
      if (v && v !== docCurrency) { setDocCurrency(v); clearInterval(poll) }
    }, 150)
    return function () {
      if (el) el.removeEventListener('change', syncFromEl)
      window.removeEventListener('sq:currency', onCustom)
      clearInterval(poll)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])
  var currencySymbol = ({ TRY: '₺', USD: '$', EUR: '€', GBP: '£' })[docCurrency] || docCurrency

  // ── Satir kisayol menusu (•••) ───────────────────────────
  //   Aksiyon seridinin basindaki MoreHorizontal butonuna basilinca acilan liste.
  //   Suan tek item: "Stok Kartina Git". Ileride ek ozellikler (kart bilgisi,
  //   fiyat gecmisi, barkod bas, vb.) bu listeye eklenir. Portal ile butonun
  //   altinda konumlanir, dis click veya Esc ile kapanir.
  //   State: null veya { row, pos:{top,left,width} }
  var [shortcutsMenu, setShortcutsMenu] = useState(null)
  // Maliyet Goruntuleme — kisayol menusunden acilan standart modal.
  // null veya { materialCode, configCode, quantity, materialName }
  var [costViewer, setCostViewer] = useState(null)
  // Split-pane: modal body icinde solda grup listesi, sagda secili grubun alanlari.
  // DynamicWidgetRenderer her grup icin [data-dyn-group-id] karti render eder;
  // MutationObserver ile bu kartlari yakalayip grup listesini olusturuyoruz.
  // Not: Onceki tab-layout icin kullanilan extrasGroups / extrasActiveGroup
  // state'leri kaldirildi. Artik butun gruplar dikey alt alta stacked olarak
  // gorunuyor (sqe-widget-wrap CSS'i). Invalid alana tiklamada scroll-into-view
  // kullaniyoruz, grup secimi yapmiyoruz.
  var extrasBodyRef = useRef(null)

  function closeExtrasModal() {
    setExtrasModalRow(null)
    setExtrasSaving(false)
    setExtrasToast(null)
  }

  // Zorunlu ama bos alan tespit edildiginde kisa bir shake animasyonu oynatilir;
  // renderer save() sonucunda .is-invalid class'i zaten input'a ekleniyor — biz
  // ustune .cb-invalid-shake sinifini reflow ile yeniden uygulayip titreşimi
  // tetikleriz (yeniden save'de tekrar tetiklenmesi icin her seferinde kaldir-ekle).
  function shakeInvalidInputs() {
    var host = extrasBodyRef.current
    if (!host) return
    var nodes = host.querySelectorAll('.is-invalid')
    if (!nodes || nodes.length === 0) return
    nodes.forEach(function(el) {
      el.classList.remove('cb-invalid-shake')
      // reflow — animasyonu yeniden baslat
      void el.offsetWidth
      el.classList.add('cb-invalid-shake')
    })
    setTimeout(function() {
      nodes.forEach(function(el) { el.classList.remove('cb-invalid-shake') })
    }, 500)
  }

  async function handleExtrasSave() {
    if (!extrasModalRow || !extrasRendererRef.current) return
    setExtrasSaving(true)
    setExtrasToast(null)
    try {
      var savedLineId = extrasModalRow.id != null && Number(extrasModalRow.id) > 0 ? Number(extrasModalRow.id) : null
      // 2026-06-01 diagnostic: hangi yola gidildigini + valuesRef icerigini yaz
      try {
        var __dbgValues = extrasRendererRef.current.getValues ? extrasRendererRef.current.getValues() : '(no getValues)'
        console.log('[CL-EXTRAS] handleExtrasSave', {
          savedLineId: savedLineId,
          path: savedLineId == null ? 'local-pending' : 'backend',
          rowUid: extrasModalRow._uid,
          rowMaterialCode: extrasModalRow.materialCode,
          getValuesSnapshot: __dbgValues,
        })
      } catch (_) {}
      // Kaydedilmemis satirda backend'e gitmiyoruz — validate edip degerleri
      // row.__extras'a local olarak yaziyoruz. Ana sqSave satirlari kaydedip
      // id aldiktan sonra widget API'siyle senkron eder.
      if (savedLineId == null) {
        var v = extrasRendererRef.current.validate()
        if (!v.valid) {
          var firstLabel = (v.errors && v.errors[0]) || 'Zorunlu alan bos'
          setExtrasToast({ type: 'err', text: 'Zorunlu alanlar bos: ' + (v.errors || []).join(', ') })
          // Renderer.validate() saveAttemptErrors state'ini set etmiyor — save() ediyor.
          // Gorsel shake icin save'i cagirip hata donmesini bekleyelim.
          var forcedResult = await extrasRendererRef.current.save({ recordId: '__pending__' })
          // recordId olsa da bizim local yol oldugu icin sonucun success'ini umursamiyoruz;
          // save() en azindan is-invalid class'ini widget input'larina ekliyor.
          void forcedResult
          setTimeout(shakeInvalidInputs, 30)
          // Alt alta stacked layout'ta grup tab'i yok — direkt hatali alana kaydir.
          setTimeout(function() {
            var host = extrasBodyRef.current
            if (!host) return
            var firstInvalid = host.querySelector('.is-invalid')
            if (!firstInvalid) return
            try { firstInvalid.scrollIntoView({ behavior: 'smooth', block: 'center' }) }
            catch (_) { firstInvalid.scrollIntoView() }
          }, 40)
          return
        }
        // Gecerli — degerleri row.__extras'a yaz, modali kapat.
        var localValues = extrasRendererRef.current.getValues() || {}
        console.log('[CL-EXTRAS] local path → row.__extras yaziliyor', {
          uid: extrasModalRow._uid,
          values: localValues,
          keyCount: Object.keys(localValues).length,
        })
        if (Object.keys(localValues).length === 0) {
          console.warn('[CL-EXTRAS] UYARI: getValues bos dondu — varsayilan degerler valuesRef\'e gecmemis olabilir')
        }
        setRows(function(prev) {
          return prev.map(function(r) {
            if (r._uid !== extrasModalRow._uid) return r
            return Object.assign({}, r, { __extras: Object.assign({}, localValues) })
          })
        })
        setExtrasToast({ type: 'ok', text: 'Ek alanlar hazir — satiri Kaydet ile kesinlestirin' })
        setTimeout(function() { closeExtrasModal() }, 650)
        return
      }

      // Kaydedilmis satir — mevcut backend save akisi.
      console.log('[CL-EXTRAS] backend path — save() cagrisi', { savedLineId: savedLineId })
      var result = await extrasRendererRef.current.save({ recordId: String(savedLineId) })
      console.log('[CL-EXTRAS] backend path — save() result', result)
      if (result && result.success === false) {
        console.warn('[CL-EXTRAS] backend save FAIL — gear KIRMIZI kalacak', {
          savedLineId: savedLineId,
          message: result.message,
          requiredErrors: result.requiredErrors,
        })
        setExtrasToast({ type: 'err', text: result.message || 'Kayit basarisiz.' })
        // Eksik zorunlu alan varsa kirmizi shake — .is-invalid DOM'a islenene kadar
        // minik bir gecikme; React render sonrasi class'lar yerinde olur.
        if (result.requiredErrors && result.requiredErrors.length > 0) {
          setTimeout(shakeInvalidInputs, 30)
          // Alt alta stacked layout'ta grup tab'i yok — direkt hatali alana kaydir.
          setTimeout(function() {
            var host = extrasBodyRef.current
            if (!host) return
            var firstInvalid = host.querySelector('.is-invalid')
            if (!firstInvalid) return
            try { firstInvalid.scrollIntoView({ behavior: 'smooth', block: 'center' }) }
            catch (_) { firstInvalid.scrollIntoView() }
          }, 40)
        }
      } else {
        console.log('[CL-EXTRAS] backend save OK — invalidLineIds\'den ' + savedLineId + ' cikarilıyor')
        setExtrasToast({ type: 'ok', text: 'Kaydedildi' })
        // Bu satirin widget'lari dolmus olabilir — invalid listesinden cikar (yesile dons).
        setInvalidLineIds(function(prev) {
          var next = prev.filter(function(x) { return x !== savedLineId })
          console.log('[CL-EXTRAS] invalidLineIds: ' + JSON.stringify(prev) + ' → ' + JSON.stringify(next))
          return next
        })
        // __extras varsa temizle — artik backend source of truth
        setRows(function(prev) {
          return prev.map(function(r) {
            if (r._uid !== extrasModalRow._uid || !r.__extras) return r
            var copy = Object.assign({}, r)
            delete copy.__extras
            return copy
          })
        })
        setTimeout(function() { closeExtrasModal() }, 650)
      }
    } catch (e) {
      setExtrasToast({ type: 'err', text: 'Hata: ' + (e && e.message ? e.message : String(e)) })
    } finally {
      setExtrasSaving(false)
    }
  }

  // ── State: satirlar ──
  var [rows, setRows] = useState(function() {
    return (config.rows || []).map(function(r) {
      return applyComputed(Object.assign({ _uid: makeUid() }, r), allColumns)
    })
  })

  // Dis tarafa her degisiklikte notify (bridge)
  useEffect(function() {
    if (typeof onRowsChange === 'function') {
      // _uid bridge'in disina sizmasin
      var clean = rows.map(function(r) {
        var copy = Object.assign({}, r)
        delete copy._uid
        return copy
      })
      onRowsChange(clean)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [rows])

  // Extras modal acikken: DynamicWidgetRenderer'in render ettigi grup kartlarini
  // (data-dyn-group-id) izle ve sol panele tab listesi olarak yansit. Kartlar
  // shakeTick degistiginde invalidLineIds'deki satirlarin ⚙ butonlarina
  // 'cb-invalid-shake' class'i ekle — 600ms sonra kaldir.
  useEffect(function() {
    if (shakeTick === 0) return
    var selectors = invalidLineIds.map(function(id) { return '[data-extras-line-id="' + id + '"]' })
    if (selectors.length === 0) return
    var els = document.querySelectorAll(selectors.join(','))
    els.forEach(function(el) {
      el.classList.remove('cb-invalid-shake')
      // reflow
      void el.offsetWidth
      el.classList.add('cb-invalid-shake')
    })
    var timer = setTimeout(function() {
      els.forEach(function(el) { el.classList.remove('cb-invalid-shake') })
    }, 650)
    return function() { clearTimeout(timer) }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [shakeTick])

  /* ── Imperative API (vanilla JS bridge) ──
     window.CalibraHub.salesLineGrid.{setRows,getRows}
     setRows ile yuklenen satirlar _locked:true isaretlenir — kullanici
     Duzelt butonuna basmadan hucreler ve Sil butonu pasif kalir. */
  useEffect(function() {
    var api = {
      setRows: function(newRows) {
        // _uid korumasi: ayni satiri yeniden eslestirerek AnimatePresence'in exit+enter
        // animasyonuyla gorunum karisikligina yol acmasini engelle. Siralama:
        //   1) line.Id eslesmesi (UPDATE edilmis var olan satirlar)
        //   2) Pozisyon (index) eslesmesi (sadece fresh INSERT sonrasi id gelsin diye)
        //   Her iki yontem uymazsa yeni _uid uretilir (gercekten yeni satir).
        setRows(function(prevRows) {
          var prevById = {}
          prevRows.forEach(function(pr) {
            if (pr.id != null && pr.id !== '' && Number(pr.id) > 0) {
              prevById[String(pr.id)] = pr
            }
          })
          var usedUids = Object.create(null)
          var nextArr = (newRows || []).map(function(r, idx) {
            var idKey = r.id != null && r.id !== '' && Number(r.id) > 0 ? String(r.id) : null
            var existing = idKey ? prevById[idKey] : null
            // ID match yoksa pozisyona gore prev'i al (ayni index)
            if (!existing && idx < prevRows.length) {
              var posMatch = prevRows[idx]
              if (posMatch && !usedUids[posMatch._uid]) existing = posMatch
            }
            var uid = existing ? existing._uid : makeUid()
            usedUids[uid] = true
            return applyComputed(Object.assign({ _uid: uid, _locked: true }, r), allColumns)
          })
          return nextArr
        })
      },
      getRows: function() {
        return rows.map(function(r) {
          var copy = Object.assign({}, r)
          delete copy._uid
          delete copy._locked
          return copy
        })
      },
      // Kayit yanitindaki satirlari (LineNo sirasinda) grid'in dolu satirlarina
      // pozisyonel eslestirip YALNIZCA id alanini yazar. setRows'tan farki:
      // kullanicinin devam eden duzenlemelerini ezmez, _locked durumunu degistirmez.
      // Id'siz kalan satir bir sonraki kayitta DELETE+INSERT edilir ve satir bazli
      // widget (WidgetTra) kayitlari orphan kalir — bu merge o acigi kapatir.
      // Dolu satir predicate'i sqSave'in rowsFilled filtresiyle ayni olmali
      // (materialCode || materialName) — payload'a giden siralama korunur.
      mergeSavedLineIds: function(savedLines) {
        var arr = Array.isArray(savedLines) ? savedLines : []
        if (arr.length === 0) return
        setRows(function(prev) {
          var cursor = 0
          return prev.map(function(r) {
            if (!r || !(r.materialCode || r.materialName)) return r
            if (cursor >= arr.length) return r
            var sl = arr[cursor]
            cursor++
            var hasId = r.id != null && r.id !== '' && Number(r.id) > 0
            if (hasId || !sl || !(Number(sl.id) > 0)) return r
            // Guvenlik: ucus sirasinda satir degistiyse yanlis Id yazmamak icin
            // itemId tutarliligi aranir — uymazsa satir Id'siz birakilir.
            var slItem  = sl.itemId != null ? Number(sl.itemId) : null
            var rowItem = r.stockCardId != null && r.stockCardId !== '' ? Number(r.stockCardId)
                        : (r.itemId != null && r.itemId !== '' ? Number(r.itemId) : null)
            if (slItem == null || rowItem == null || slItem !== rowItem) return r
            return Object.assign({}, r, { id: Number(sl.id) })
          })
        })
      },
      // Satirlardaki eksik zorunlu widget state'i — ⚙ rengini kirmizi yapar.
      setInvalidLines: function(ids) {
        var arr = Array.isArray(ids) ? ids.map(function(n) { return Number(n) }).filter(function(n) { return n > 0 }) : []
        setInvalidLineIds(arr)
      },
      // Listeyi set et + kirmizilari titrett.
      flashInvalidLines: function(ids) {
        var arr = Array.isArray(ids) ? ids.map(function(n) { return Number(n) }).filter(function(n) { return n > 0 }) : []
        setInvalidLineIds(arr)
        setShakeTick(function(t) { return t + 1 })
      },
    }
    window.CalibraHub = window.CalibraHub || {}
    window.CalibraHub.salesLineGrid = api
    return function() {
      if (window.CalibraHub && window.CalibraHub.salesLineGrid === api) {
        window.CalibraHub.salesLineGrid = null
      }
    }
  }, [rows, columns])

  // ── Hucre degisikligi ──
  var handleCellChange = useCallback(function(rowUid, columnKey, newValue, fillPatch) {
    setRows(function(prev) {
      return prev.map(function(r) {
        if (r._uid !== rowUid) return r
        var next = Object.assign({}, r)
        next[columnKey] = newValue
        if (fillPatch) {
          Object.keys(fillPatch).forEach(function(k) { next[k] = fillPatch[k] })
        }
        return applyComputed(next, allColumns)
      })
    })
  }, [allColumns])

  // ── Yeni satir ekle ──
  // Guided workflow: satir eklendikten sonra stok rehberi otomatik acilir.
  // Stok secilince, o malzeme kombinasyon takipli ise kombinasyon modal'i
  // acilir; kombinasyon secilince (ya da malzeme combo izlemeyen ise)
  // ek alanlar (SALES_QUOTE_LINES widget'lari) modal'i acilir. Chain global
  // event "lineGrid:autoOpenStage" uzerinden yurur — dispatcherler:
  // handleAddRow (material) → handlePick (combo/extras) → CombinationLookupCell
  // onApply (extras) → grid useEffect (extras modal open).
  function handleAddRow() {
    var newUid = makeUid()
    setRows(function(prev) {
      var blank = { _uid: newUid }
      allColumns.forEach(function(c) {
        if (c.type === 'number' || c.type === 'currency' || c.type === 'percent') {
          blank[c.key] = 0
        } else {
          blank[c.key] = ''
        }
      })
      return prev.concat([applyComputed(blank, allColumns)])
    })
    // React state commit + cell mount sonrasinda listener'lar hazir olsun diye kisa gecikme.
    // requestAnimationFrame 1 frame bekler; bu surede useEffect(mount) tetiklenmis olur.
    requestAnimationFrame(function () {
      requestAnimationFrame(function () {
        try {
          window.dispatchEvent(new CustomEvent('lineGrid:autoOpenStage', {
            detail: { rowUid: newUid, stage: 'material' }
          }))
        } catch (_) { /* older browsers: no-op */ }
      })
    })
  }

  // ── Satir sil — geri sayim ile (modal yok) ──
  function handleDeleteRow(rowUid) {
    // Zaten beklemede ise tekrar baslatmaya gerek yok
    if (pendingDelete[rowUid]) return
    setPendingDelete(function (prev) {
      var next = Object.assign({}, prev)
      next[rowUid] = true
      return next
    })
    // Sure dolunca gercek silmeyi yap.
    // ONEMLI: Silinen satir aktif (revisedFromId=null) satirsa, ona isaret eden
    // eski revizyon satirlari da zincir boyunca silinmeli. Aksi halde kayit
    // silinince eski surum gorunur hale gelir. Save sirasinda da getRows()
    // listesinden cikan tum id'ler backend tarafindan DELETE edilir.
    var tid = setTimeout(function () {
      setRows(function (prev) {
        var target = prev.find(function (r) { return r._uid === rowUid })
        if (!target) return prev
        // Zinciri BFS ile topla: hedef + hedefin id'sine isaret eden eski surumleri bul
        var removeUids = {}
        removeUids[target._uid] = true
        var queue = [target]
        var guard = 0
        while (queue.length > 0 && guard < 50) {
          var cur = queue.shift()
          guard++
          if (!cur.id || Number(cur.id) <= 0) continue
          var curId = Number(cur.id)
          prev.forEach(function (r) {
            if (r.revisedFromId != null && Number(r.revisedFromId) === curId && !removeUids[r._uid]) {
              removeUids[r._uid] = true
              queue.push(r)
            }
          })
        }
        return prev.filter(function (r) { return !removeUids[r._uid] })
      })
      setPendingDelete(function (prev) {
        var next = Object.assign({}, prev)
        delete next[rowUid]
        return next
      })
      delete deleteTimeoutsRef.current[rowUid]
    }, DELETE_COUNTDOWN_MS)
    deleteTimeoutsRef.current[rowUid] = tid
  }
  function cancelPendingDelete(rowUid) {
    if (deleteTimeoutsRef.current[rowUid]) {
      clearTimeout(deleteTimeoutsRef.current[rowUid])
      delete deleteTimeoutsRef.current[rowUid]
    }
    setPendingDelete(function (prev) {
      var next = Object.assign({}, prev)
      delete next[rowUid]
      return next
    })
  }
  // Component unmount oldugunda tum bekleyen timer'lari temizle
  useEffect(function () {
    return function () {
      Object.values(deleteTimeoutsRef.current).forEach(clearTimeout)
      deleteTimeoutsRef.current = {}
    }
  }, [])

  // Shortcuts menu — Esc kapatir, scroll pozisyondan ayrilma sorunu yasatmamak
  // icin scroll'da da kapatilir (yeniden konumlamaktansa kapatmak daha tutarli).
  useEffect(function () {
    if (!shortcutsMenu) return undefined
    function onKey(e) { if (e.key === 'Escape') setShortcutsMenu(null) }
    function onScroll() { setShortcutsMenu(null) }
    document.addEventListener('keydown', onKey)
    window.addEventListener('scroll', onScroll, true)
    return function () {
      document.removeEventListener('keydown', onKey)
      window.removeEventListener('scroll', onScroll, true)
    }
  }, [shortcutsMenu])

  // ── Auto-chain: guided new-line workflow ──
  // handleAddRow -> 'material' lookup -> (trackCombinations ise) 'combo' ->
  // 'extras' (⚙ ek alanlar modali). Material/combo asamalarini ilgili cell
  // kendisi dinleyip acar; 'extras' asamasi GRID seviyesinde handle edilir
  // cunku extrasModalRow state'i burada. Rows degistikce ref'i guncel tut.
  var rowsRef = useRef(rows)
  useEffect(function () { rowsRef.current = rows }, [rows])

  // SALES_QUOTE_LINES formundaki zorunlu widget durumu — chain sonunda extras
  // modal'i SADECE zorunlu alan varsa otomatik acilir. Yoksa kullanici akistan
  // takilmadan satira devam eder; ihtiyaci olursa ⚙ butonuyla manuel acabilir.
  // Schema bir kez yuklenir ve cache'lenir (ref ile listener'a senkron tutulur).
  var hasRequiredLineWidgetsRef = useRef(false)
  useEffect(function () {
    var alive = true
    fetch('/api/widgets/forms/' + encodeURIComponent(__lineFormCode) + '/schema', { credentials: 'same-origin' })
      .then(function (r) { return r.ok ? r.json() : null })
      .then(function (schema) {
        if (!alive || !schema || !Array.isArray(schema.widgets)) return
        // ASP.NET Core JSON camelCase'e cevirir; Pascal fallback'i de dusuruyoruz.
        var any = schema.widgets.some(function (w) {
          return w && (w.isRequired === true || w.IsRequired === true)
        })
        hasRequiredLineWidgetsRef.current = any
      })
      .catch(function () { /* sessiz — schema yoksa otomatik acma yapma */ })
    return function () { alive = false }
  }, [])

  useEffect(function () {
    function onAutoOpen(e) {
      var d = e.detail || {}
      if (d.stage !== 'extras') return
      // Zorunlu widget yoksa otomatik acma — guided chain burada sessizce biter.
      if (!hasRequiredLineWidgetsRef.current) return
      var target = (rowsRef.current || []).find(function (r) { return r._uid === d.rowUid })
      if (target) setExtrasModalRow(target)
    }
    window.addEventListener('lineGrid:autoOpenStage', onAutoOpen)
    return function () { window.removeEventListener('lineGrid:autoOpenStage', onAutoOpen) }
  }, [])


  // ── Satir duzelt (kilit/unlock sistemi) ──
  // setRows ile yuklenen satirlar _locked:true olarak gelir. Kullanici Duzelt
  // butonuna basmadan hucreler + Sil + Kombinasyon + Not butonlari pasif.
  // Buton click: _locked toggle; acildiginda ilk editable input'a focus.
  function handleEditRow(rowUid) {
    setRows(function (prev) {
      return prev.map(function (r) {
        if (r._uid !== rowUid) return r
        return Object.assign({}, r, { _locked: !r._locked })
      })
    })
    setEditingRowUid(function (prev) { return prev === rowUid ? null : rowUid })
    // Unlock sonrasi ilk editable hucreye focus
    requestAnimationFrame(function() {
      var rowEl = document.querySelector('[data-row-uid="' + rowUid + '"]')
      if (!rowEl) return
      var firstInput = rowEl.querySelector('input:not([disabled]), select:not([disabled]), textarea:not([disabled])')
      if (firstInput && typeof firstInput.focus === 'function') firstInput.focus()
    })
  }

  // Row-level flag helpers
  // canEdit: Duzelt butonu her zaman aktif (kilit toggle icin)
  // canDelete / canModify: _locked false olmali (ve server-side __canDelete engellemedigi surece)
  function canEdit(row) { return row.__canEdit !== false }
  // Kilitleme gecici olarak devre disi — kalem ikonu kaldi, kilit etkisi yok.
  function isRowLocked(row) { return false }
  function canDelete(row) { return !isRowLocked(row) && row.__canDelete !== false }
  function canModify(row) { return !isRowLocked(row) }

  // ── Not paneli toggle ──
  // Panel acik: manuel acildi (openNoteRows[uid]) VEYA satir pinli (row.notesPinned)
  // ONEMLI: Yalniz dolu olmak panele otomatik acilma saglamaz — kullanici not simgesiyle acar.
  function hasAnyBelowValue(row) {
    for (var i = 0; i < belowColumns.length; i++) {
      var v = row[belowColumns[i].key]
      if (v != null && String(v).trim() !== '') return true
    }
    return false
  }
  function isNoteOpen(row) {
    return openNoteRows[row._uid] === true || row.notesPinned === true
  }
  function toggleNote(rowUid) {
    setOpenNoteRows(function(prev) {
      var next = Object.assign({}, prev)
      if (next[rowUid]) delete next[rowUid]
      else next[rowUid] = true
      return next
    })
    // Acildiginda below cell'in ilk input'una fokus
    requestAnimationFrame(function() {
      var rowEl = document.querySelector('[data-row-uid="' + rowUid + '"]')
      if (!rowEl) return
      var input = rowEl.querySelector('[data-below-cell] input, [data-below-cell] textarea')
      if (input && typeof input.focus === 'function') input.focus()
    })
  }
  // Satir notu icin pin toggle — true ise belge acilislarinda otomatik acik gelir
  function toggleNotePin(rowUid) {
    setRows(function(prev) {
      return prev.map(function(r) {
        if (r._uid !== rowUid) return r
        return Object.assign({}, r, { notesPinned: !r.notesPinned })
      })
    })
  }

  // ── Footer subtotal hesapla ──
  // ÖNEMLI: Revize edilmis (superseded) parent satirlar UI'da gizli — bunlari
  // topluya KATMA. Aksi halde revize sonrasi toplam ciftleyip kullaniciya
  // "neden eski kalemi de sayiyor?" hissi verir.
  var subtotals = useMemo(function() {
    var out = {}
    if (footer.showSubtotal && Array.isArray(footer.subtotalColumns)) {
      // Aktif satirlar: revisedFromId bos — eski (superseded) satirlar kendi revisedFromId'sini tasir
      var liveRows = rows.filter(function (r) {
        return r.revisedFromId == null || Number(r.revisedFromId) <= 0
      })
      footer.subtotalColumns.forEach(function(colKey) {
        var sum = 0
        liveRows.forEach(function(r) {
          var v = r[colKey]
          if (typeof v === 'number') sum += v
          else if (v != null && v !== '') {
            var n = parseFloat(String(v).replace(',', '.'))
            if (!isNaN(n)) sum += n
          }
        })
        out[colKey] = sum
      })
    }
    return out
  }, [rows, footer])

  var totalSum = Object.values(subtotals).reduce(function(a, b) { return a + b }, 0)

  // ── Kolon genisligi → CSS width ──
  function widthCss(col) {
    if (col.width === 'flex' || col.width === '*' || !col.width) return { flex: '1 1 0', minWidth: '120px' }
    return { width: col.width + 'px', flex: '0 0 ' + col.width + 'px' }
  }

  // Keyboard navigasyonu:
  //   Tab         → yatayda (browser default — mudahale yok)
  //   Enter       → yatayda (Tab gibi) — bir sonraki odaklanabilir elemana gecer
  //   Ctrl+Enter  → dikeyde — alt satirin ayni kolonuna git. Son satirda ise
  //                 window.sqSave() tetikle (validation DocumentEdit tarafinda).
  var gridRootRef = useRef(null)

  function handleGridKeyDown(e) {
    if (e.key !== 'Enter') return
    var t = e.target
    if (!t || t.tagName !== 'INPUT') return
    // IME compose sirasinda Enter'i isleme
    if (e.isComposing || e.keyCode === 229) return
    if (t.type === 'checkbox' || t.type === 'radio') return

    var isVerticalNav = e.ctrlKey || e.metaKey  // Ctrl (win/linux) veya Cmd (mac)

    var cell = t.closest('[data-cell-key]')
    var rowEl = t.closest('[data-row-uid]')
    if (!cell || !rowEl) return

    e.preventDefault()
    t.blur()

    if (isVerticalNav) {
      // Ctrl+Enter: alt satirin ayni kolonuna git (veya son satirda save)
      var colKey = cell.getAttribute('data-cell-key')
      var currentUid = rowEl.getAttribute('data-row-uid')
      var rowIdx = rows.findIndex(function (r) { return r._uid === currentUid })
      if (rowIdx < 0) return

      if (rowIdx < rows.length - 1) {
        var nextUid = rows[rowIdx + 1]._uid
        var root = gridRootRef.current || document
        var nextInput = root.querySelector(
          '[data-row-uid="' + nextUid + '"] [data-cell-key="' + colKey + '"] input, ' +
          '[data-row-uid="' + nextUid + '"] [data-cell-key="' + colKey + '"] select, ' +
          '[data-row-uid="' + nextUid + '"] [data-cell-key="' + colKey + '"] textarea'
        )
        if (nextInput) {
          setTimeout(function () {
            nextInput.focus()
            if (typeof nextInput.select === 'function') nextInput.select()
          }, 0)
        }
      } else {
        if (typeof window.sqSave === 'function') {
          setTimeout(function () { window.sqSave() }, 0)
        }
      }
    } else {
      // Plain Enter: Tab gibi — DOM sirasinda bir sonraki odaklanabilir elemana git
      var root2 = gridRootRef.current || document
      var focusables = Array.prototype.slice.call(root2.querySelectorAll(
        'input:not([disabled]):not([type="hidden"]), select:not([disabled]), textarea:not([disabled]), button:not([disabled])'
      )).filter(function (el) {
        // tabIndex -1 olanlari ve gozukmeyenleri atla
        if (el.tabIndex < 0) return false
        var rect = el.getBoundingClientRect()
        return rect.width > 0 && rect.height > 0
      })
      var idx = focusables.indexOf(t)
      if (idx >= 0 && idx < focusables.length - 1) {
        var next2 = focusables[idx + 1]
        setTimeout(function () {
          next2.focus()
          if (typeof next2.select === 'function' && (next2.tagName === 'INPUT' || next2.tagName === 'TEXTAREA')) next2.select()
        }, 0)
      }
    }
  }

  return (
    <div
      ref={gridRootRef}
      onKeyDown={handleGridKeyDown}
      className="calibra-line-grid rounded-2xl overflow-hidden border border-slate-200 bg-white/70 dark:bg-white/[0.04] dark:border-white/10 backdrop-blur-xl shadow-sm">
      {/* Header row */}
      <div className="flex items-center border-b border-slate-200 bg-slate-50/80 dark:bg-white/[0.03] dark:border-white/[0.08]">
        <div className="w-[140px] flex-shrink-0 px-2 py-2.5 text-[10px] font-bold uppercase tracking-wider text-slate-500 dark:text-white/50 text-center">
          Islem
        </div>
        {columns.map(function(col) {
          var Icon = resolveIcon(col.icon)
          var align =
            col.align === 'right'  ? 'justify-end'  :
            col.align === 'center' ? 'justify-center' : 'justify-start'
          return (
            <div
              key={col.key}
              className={'flex items-center gap-1.5 px-2.5 py-2.5 text-[10px] font-bold uppercase tracking-wider text-slate-600 dark:text-white/60 ' + align}
              style={widthCss(col)}
            >
              <Icon size={11} strokeWidth={1.8} className="text-slate-400 dark:text-white/40 flex-shrink-0" />
              <span className="truncate">{col.label}</span>
              {col.required && <span className="text-rose-500 dark:text-rose-400">*</span>}
            </div>
          )
        })}
      </div>

      {/* Data rows */}
      <div>
        {/* ── Revize zinciri: superseded satirlari GIZLE ─────────────────
            Bir satir X'i revize ederek yeni satir Y eklenince, X "kapanmis"
            sayilir (Y.revisedFromId = X.id). Grid yalnizca zincirin en
            sonundaki (henuz revize edilmemis) satirlari gosterir. Eski revizeler
            DB'de silinmez — save akisinda hepsi korunur, sadece UI'da gizlenir.
            Revize modal'i icinde "Gecmis Revizeler" sekmesinde tamami gorulur. */}
        {rows.length === 0 ? (
          <div className="px-6 py-10 text-center text-[12px] text-slate-400 dark:text-white/30">
            {labels.emptyText || 'Henuz kalem eklenmemis'}
          </div>
        ) : (
          <AnimatePresence initial={false}>
            {(function () {
              // Aktif satirlar: revisedFromId bos — eski (superseded) satirlarda revisedFromId dolu
              var visibleRows = rows.filter(function (r) {
                return r.revisedFromId == null || Number(r.revisedFromId) <= 0
              })
              if (visibleRows.length === 0) {
                // Tum satirlar revize edilmis (edge case) — bos mesaji goster
                return (
                  <div className="px-6 py-6 text-center text-[12px] text-slate-400 dark:text-white/30">
                    Gorunur satir yok (tumu revize edilmis)
                  </div>
                )
              }
              return visibleRows.map(function(row) {
              var isPending = pendingDelete[row._uid] === true
              return (
                <motion.div
                  key={row._uid}
                  data-row-uid={row._uid}
                  initial={{ opacity: 0, y: -4 }}
                  animate={{ opacity: 1, y: 0 }}
                  exit={{ opacity: 0, y: -4, height: 0 }}
                  transition={{ duration: 0.18 }}
                  className="border-b border-slate-100 hover:bg-slate-50/70 dark:border-white/[0.05] dark:hover:bg-white/[0.02] transition-colors"
                  style={{ position: 'relative' }}
                >
                  <div className="flex items-stretch" style={{ position: 'relative' }}>
                    {/* Aksiyon seridi (sadelesti): ••• kisayol + Kombinasyon + Ek Alanlar (⚙) + Sil.
                        Not ve Revize butonlari ••• icine tasindi — tek dropdown'dan erisilir. */}
                    <div className="w-[140px] flex-shrink-0 flex items-center justify-center gap-1 border-r border-slate-100 dark:border-white/[0.04]">
                      {/* Satir kisayol menusu — MoreHorizontal ikonu, tiklayinca liste acilir */}
                      <button
                        type="button"
                        onClick={function (e) {
                          // Butonun ekrandaki pozisyonunu al, menuyu onun altina konumla.
                          var rect = e.currentTarget.getBoundingClientRect()
                          setShortcutsMenu({
                            row: row,
                            pos: { top: rect.bottom + 4, left: rect.left, width: 200 },
                          })
                        }}
                        className="w-7 h-7 rounded-lg flex items-center justify-center transition-colors text-slate-400 hover:text-indigo-500 hover:bg-indigo-50 dark:text-white/30 dark:hover:text-indigo-300 dark:hover:bg-indigo-500/10"
                        title="Kisayollar / satir islemleri"
                        aria-label="Kisayol menusu"
                        aria-haspopup="menu"
                        aria-expanded={!!(shortcutsMenu && shortcutsMenu.row && shortcutsMenu.row._uid === row._uid)}
                      >
                        <MoreHorizontal size={14} strokeWidth={2} />
                      </button>
                      {actionLookupColumns.map(function(col) {
                        // Kilitli satirda Kombinasyon butonu da pasif — CombinationLookupCell'in
                        // kendi iki hali var (secili/eksik), burada locked ozel durumu DOM'da
                        // pointer-events: none ile disari kapatiyoruz.
                        return (
                          <div key={col.key} style={isRowLocked(row) ? { opacity: 0.45, pointerEvents: 'none' } : {}}>
                            <CombinationLookupCell
                              compact={true}
                              column={col}
                              row={row}
                              value={row[col.key]}
                              onChange={function(k, v, fill) { handleCellChange(row._uid, k, v, fill) }}
                            />
                          </div>
                        )
                      })}
                      {/* Not butonu aksiyon seridinden cikartildi — artik ••• kisayol
                          menusunun icinde "Not Ekle / Goster / Gizle" olarak yer aliyor. */}
                      {/* Ek Alanlar (SALES_QUOTE_LINES widget'lari) — sadece kayitli satirlarda.
                          Renk kuralı:
                            - Satir kaydedilmemis → sky (notr)
                            - Kaydedilmis + invalidLineIds'de → kirmizi (eksik zorunlu widget)
                            - Kaydedilmis + invalidLineIds'de degil → yesil (OK) */}
                      {(function() {
                        var savedLineId = row.id != null && row.id !== '' && Number(row.id) > 0 ? Number(row.id) : null
                        // Kayitli olmasa da ⚙ butonu aktif — kullanici ek alanlari
                        // girip "Kaydet" deyince degerler row.__extras icinde local
                        // tutulur; ana Kaydet'te satir DB'ye islendikten sonra
                        // extras widget API'siyle satir id'sine senkron edilir.
                        // Sadece kilitli satirda pasif (canModify=false).
                        var disabled = !canModify(row)
                        var hasPending = row.__extras && Object.keys(row.__extras).length > 0
                        var isInvalid = savedLineId != null && invalidLineIds.indexOf(savedLineId) !== -1
                        var colorClass
                        if (disabled) {
                          colorClass = 'text-slate-300 dark:text-white/15 cursor-not-allowed'
                        } else if (isInvalid) {
                          // Boyut sabit kalsin diye ring yok — sadece zemin + metin rengi.
                          colorClass = 'text-white bg-rose-600 hover:bg-rose-500 dark:bg-rose-500/80 dark:hover:bg-rose-500'
                        } else if (savedLineId == null && !hasPending) {
                          // Kaydedilmemis + ek alan doldurulmamis → notr (sky)
                          colorClass = 'text-sky-600 bg-sky-50 hover:bg-sky-100 dark:text-sky-300 dark:bg-sky-500/15 dark:hover:bg-sky-500/25'
                        } else {
                          // Kaydedilmis + gecerli, veya kaydedilmemis ama local __extras dolu → yesil
                          colorClass = 'text-emerald-600 bg-emerald-50 hover:bg-emerald-100 dark:text-emerald-300 dark:bg-emerald-500/15 dark:hover:bg-emerald-500/25'
                        }
                        return (
                          <button
                            type="button"
                            data-extras-line-id={savedLineId || ''}
                            onClick={function() {
                              if (disabled) return
                              setExtrasModalRow(row)
                            }}
                            disabled={disabled}
                            className={'w-7 h-7 rounded-lg flex items-center justify-center transition-colors ' + colorClass}
                            title={disabled
                              ? 'Once kilidi acin'
                              : (isInvalid
                                  ? 'Zorunlu ek alanlar eksik — doldurun'
                                  : (savedLineId == null
                                      ? (hasPending ? 'Ek alan girildi — satiri Kaydet ile kesinlestirin' : 'Bu satir icin ek alan gir (Kaydet ile kesinlesir)')
                                      : 'Satira ait ek alanlari duzenle'))}
                          >
                            <Settings size={13} strokeWidth={1.8} />
                          </button>
                        )
                      })()}
                      {/* Revize butonu aksiyon seridinden cikartildi — artik ••• kisayol
                          menusunde "Revize Et" olarak yer aliyor (hala revised zincir
                          rozetini satir uzerinde gostermiyoruz; modal icinde zincir var). */}
                      <button
                        type="button"
                        onClick={function() {
                          // Silme bekleme konumunda tekrar basarsa silme IPTAL
                          if (isPending) { cancelPendingDelete(row._uid); return }
                          if (canDelete(row)) handleDeleteRow(row._uid)
                        }}
                        disabled={!canDelete(row) && !isPending}
                        className={'w-7 h-7 rounded-lg flex items-center justify-center transition-colors ' + (
                          (!canDelete(row) && !isPending)
                            ? 'text-slate-300 dark:text-white/15 cursor-not-allowed'
                            : (isPending
                                ? 'text-white bg-rose-600 ring-2 ring-rose-400 animate-pulse'
                                : 'text-rose-500 hover:text-white hover:bg-rose-500 dark:text-rose-400 dark:hover:text-white dark:hover:bg-rose-500')
                        )}
                        title={isPending ? 'Silmeyi iptal et'
                               : (isRowLocked(row) ? 'Once kilidi acin' : (row.__canDelete === false ? 'Bu satir silinemez' : 'Sil'))}
                      >
                        {canDelete(row) || isPending ? <Trash2 size={13} strokeWidth={2} /> : <Lock size={12} strokeWidth={1.8} />}
                      </button>
                    </div>
                    {columns.map(function(col) {
                      // Kilitli satirda tum hucrelere pointer-events: none — sadece gorsel, tiklanmaz
                      var lockedStyle = isRowLocked(row) ? { opacity: 0.75, pointerEvents: 'none' } : {}
                      return (
                        <div
                          key={col.key}
                          data-cell-key={col.key}
                          className="flex items-center border-r border-slate-100 last:border-r-0 dark:border-white/[0.04]"
                          style={Object.assign({}, widthCss(col), { position: 'relative' }, lockedStyle)}
                        >
                          <LineGridCell
                            column={col}
                            row={row}
                            value={row[col.key]}
                            onChange={function(k, v, fill) { handleCellChange(row._uid, k, v, fill) }}
                            siblingColumns={allColumns}
                          />
                        </div>
                      )
                    })}
                    {/* Silme geri sayim cubugu — satir seviyesinde, kod kolonundan baslar,
                        170px aksiyon alanini atlar, satirin sag ucuna kadar uzanir.
                        Bar 3 saniyede 0'a kuculur (sagdan sola). */}
                    {isPending && (
                      <div
                        style={{
                          position: 'absolute', left: 140, right: 0, bottom: 0,
                          height: 3, zIndex: 5, pointerEvents: 'none',
                          background: 'rgba(239,68,68,.15)',
                          overflow: 'hidden',
                        }}
                      >
                        <div
                          style={{
                            height: '100%',
                            background: '#ef4444',
                            animation: 'lgDeleteCountdown ' + DELETE_COUNTDOWN_MS + 'ms linear forwards',
                            boxShadow: '0 0 8px rgba(239,68,68,.8)',
                          }}
                        />
                      </div>
                    )}
                  </div>

                  {/* Satir alti kolonlar (placement: row-below) — ornegin "Not".
                      Panel sadece kullanici "Not ekle" butonuna basinca VEYA not doluysa gorunur. */}
                  {belowColumns.length > 0 && isNoteOpen(row) && (
                    <div className="flex flex-col gap-1 pl-3 pr-3 pb-2 pt-1 border-t border-slate-100 dark:border-white/[0.06]">
                      {belowColumns.map(function(col) {
                        var Icon = resolveIcon(col.icon)
                        return (
                          <div
                            key={col.key}
                            data-below-cell
                            className="flex items-center gap-2 rounded-md border border-slate-100 bg-slate-50/60 dark:border-white/[0.06] dark:bg-white/[0.02]"
                          >
                            <button
                              type="button"
                              onClick={function() { if (canModify(row)) toggleNotePin(row._uid) }}
                              disabled={!canModify(row)}
                              className={'ml-1.5 w-6 h-6 rounded-md flex items-center justify-center transition-colors flex-shrink-0 ' + (
                                !canModify(row)
                                  ? 'text-slate-300 dark:text-white/15 cursor-not-allowed'
                                  : (row.notesPinned
                                      ? 'text-indigo-600 bg-indigo-50 dark:text-indigo-300 dark:bg-indigo-500/15'
                                      : 'text-slate-400 hover:text-indigo-500 hover:bg-indigo-50 dark:text-white/40 dark:hover:text-indigo-300 dark:hover:bg-indigo-500/10')
                              )}
                              title={!canModify(row) ? 'Once kilidi acin' : (row.notesPinned ? 'Pini cikar — belge acilisinda not gizli gelir' : 'Pinle — belge acilisinda not otomatik acilir')}
                            >
                              {row.notesPinned
                                ? <Pin size={12} strokeWidth={2} />
                                : <PinOff size={12} strokeWidth={1.8} />}
                            </button>
                            <div className="flex items-center gap-1.5 py-1.5 text-[10px] font-semibold uppercase tracking-wider text-slate-500 dark:text-white/50 flex-shrink-0">
                              <Icon size={11} strokeWidth={1.8} className="text-slate-400 dark:text-white/40 flex-shrink-0" />
                              <span>{col.label}</span>
                            </div>
                            <div className="flex-1 min-w-0">
                              <LineGridCell
                                column={col}
                                row={row}
                                value={row[col.key]}
                                onChange={function(k, v, fill) { handleCellChange(row._uid, k, v, fill) }}
                              />
                            </div>
                          </div>
                        )
                      })}
                    </div>
                  )}
                </motion.div>
              )
              })
            })()}
          </AnimatePresence>
        )}
      </div>

      {/* Footer: Yeni kalem + toplam */}
      <div className="flex items-center justify-between px-3 py-2.5 border-t border-slate-200 bg-slate-50/60 dark:bg-white/[0.02] dark:border-white/[0.08]">
        {(function() {
          // Stok kodu bos olan satir var ise Yeni Kalem pasif — once mevcut bos satiri doldur.
          var hasEmptyRow = rows.some(function(r) {
            return !r.materialCode || String(r.materialCode).trim() === ''
          })
          return (
            <motion.button
              type="button"
              whileTap={hasEmptyRow ? undefined : { scale: 0.97 }}
              onClick={hasEmptyRow ? undefined : handleAddRow}
              disabled={hasEmptyRow}
              title={hasEmptyRow ? 'Once mevcut bos satira stok kodu girin' : ''}
              className={'flex items-center gap-2 px-3 py-1.5 rounded-lg text-[12px] font-semibold border transition-colors ' + (
                hasEmptyRow
                  ? 'bg-slate-100 text-slate-400 border-slate-200 cursor-not-allowed dark:bg-white/[0.04] dark:text-white/30 dark:border-white/[0.08]'
                  : 'bg-indigo-50 text-indigo-600 border-indigo-200 hover:bg-indigo-100 dark:bg-indigo-500/15 dark:text-indigo-300 dark:border-indigo-400/30 dark:hover:bg-indigo-500/25'
              )}
            >
              <Plus size={13} strokeWidth={2.2} />
              <span>{labels.addRow || 'Yeni Kalem'}</span>
            </motion.button>
          )
        })()}

        {footer.showSubtotal && rows.length > 0 && (
          <div className="flex items-center gap-3 text-[12px]">
            <span className="text-slate-500 dark:text-white/40 uppercase tracking-wider font-semibold text-[10px]">
              {labels.totalLabel || 'Toplam'}
            </span>
            <span className="font-mono tabular-nums text-amber-600 dark:text-amber-300 text-[15px] font-bold">
              {TR_FMT(totalSum, decimalCfg ? decimalCfg.amount : 2)} {currencySymbol}
            </span>
          </div>
        )}
      </div>

      {/* Modal kaldirildi — silme artik satir-ici geri sayim ile yapiliyor.
          Bkz. handleDeleteRow + pendingDelete state + row icindeki overlay. */}

      {/* Satir-basi Ek Alanlar modali — SALES_QUOTE_LINES formunun widget'lari.
          Sadece kayitli satirlarda (row.id > 0) acilir; recordId = line.id.
          Portal: .sqe-tab-content icine absolute konumlanir — app shell (ust bar,
          sol menu, alt panel) ve SQE sol tab navi gizlenmez, sadece icerik alani
          ortulur. */}
      {extrasModalRow && (function () {
        // Tema detection — kisayol menusuyle ayni chain (iframe parent fallback, default light).
        var __isLight = (function () {
          if (typeof document === 'undefined') return true
          try {
            if (document.body.classList.contains('app-theme-light')) return true
            if (document.body.classList.contains('app-theme-dark'))  return false
            if (window.parent && window.parent !== window && window.parent.document && window.parent.document.body) {
              if (window.parent.document.body.classList.contains('app-theme-light')) return true
              if (window.parent.document.body.classList.contains('app-theme-dark'))  return false
            }
          } catch (_) {}
          return true
        })()
        var __overlayBg  = __isLight
          ? 'radial-gradient(at 20% 10%, rgba(99,102,241,0.06) 0%, transparent 45%), radial-gradient(at 85% 85%, rgba(168,85,247,0.05) 0%, transparent 45%), rgba(15,23,42,0.35)'
          : 'radial-gradient(at 20% 10%, rgba(99,102,241,0.12) 0%, transparent 45%), radial-gradient(at 85% 85%, rgba(168,85,247,0.10) 0%, transparent 45%), rgba(3,6,15,0.72)'
        var __panelBg     = __isLight
          ? 'linear-gradient(180deg, #ffffff 0%, #f8fafc 100%)'
          : 'linear-gradient(180deg, rgba(23,28,42,0.98) 0%, rgba(15,19,30,0.98) 100%)'
        var __panelBorder = __isLight ? '1px solid #e2e8f0'                 : '1px solid rgba(255,255,255,0.10)'
        var __panelShadow = __isLight
          ? '0 16px 48px rgba(15,23,42,0.18), 0 0 0 1px rgba(99,102,241,0.08)'
          : '0 32px 96px rgba(0,0,0,0.65), 0 0 0 1px rgba(99,102,241,0.08)'
        var __textColor   = __isLight ? '#0f172a' : 'rgba(255,255,255,0.92)'
        var __mutedText   = __isLight ? '#64748b' : 'rgba(255,255,255,0.5)'
        var __subtleText  = __isLight ? '#94a3b8' : 'rgba(255,255,255,0.35)'
        var __sepColor    = __isLight ? '#e2e8f0' : 'rgba(255,255,255,0.06)'
        var __headTitle   = __isLight ? '#0f172a' : '#fff'
        var __chipBg      = __isLight ? 'rgba(99,102,241,0.08)'  : 'rgba(99,102,241,0.12)'
        var __chipText    = __isLight ? '#4338ca'                : '#a5b4fc'
        var __chipBorder  = __isLight ? 'rgba(99,102,241,0.20)'  : 'rgba(99,102,241,0.22)'
        var __closeBtnBg  = __isLight ? 'rgba(15,23,42,0.04)'    : 'rgba(255,255,255,0.04)'
        var __closeBtnBdr = __isLight ? 'rgba(15,23,42,0.08)'    : 'rgba(255,255,255,0.08)'
        var __closeBtnClr = __isLight ? '#475569'                : 'rgba(255,255,255,0.7)'
        var __footerBg    = __isLight ? 'rgba(15,23,42,0.02)'    : 'rgba(0,0,0,0.18)'
        var __cancelBtnBg = __isLight ? '#fff'                    : 'rgba(255,255,255,0.04)'
        var __cancelBdr   = __isLight ? '#e2e8f0'                : 'rgba(255,255,255,0.10)'
        var __cancelClr   = __isLight ? '#475569'                : 'rgba(255,255,255,0.8)'
        var __cancelBgHov = __isLight ? '#f1f5f9'                : 'rgba(255,255,255,0.08)'
        var __bodyTint    = __isLight
          ? 'linear-gradient(180deg, rgba(99,102,241,0.02) 0%, transparent 40%)'
          : 'linear-gradient(180deg, rgba(255,255,255,0.008) 0%, transparent 40%)'

        return createPortal(
        <div
          onClick={function(e) { if (e.target === e.currentTarget && !extrasSaving) closeExtrasModal() }}
          style={{
            position: 'absolute', inset: 0,
            background: __overlayBg,
            backdropFilter: 'blur(6px)', WebkitBackdropFilter: 'blur(6px)',
            display: 'flex', alignItems: 'center', justifyContent: 'center',
            zIndex: 50, padding: 16,
            animation: 'sqExtrasFade 160ms ease-out',
          }}
        >
          <style>{
            '@keyframes sqExtrasFade{from{opacity:0}to{opacity:1}}' +
            '@keyframes sqExtrasPop{from{opacity:0;transform:translateY(8px) scale(.985)}to{opacity:1;transform:translateY(0) scale(1)}}'
          }</style>
          <div style={{
            width: '92%', maxWidth: 820, maxHeight: '88vh',
            display: 'flex', flexDirection: 'column', overflow: 'hidden',
            borderRadius: 18,
            background: __panelBg,
            border: __panelBorder,
            boxShadow: __panelShadow,
            color: __textColor,
            animation: 'sqExtrasPop 220ms cubic-bezier(.2,.8,.3,1)',
          }}>
            {/* Ust gradient serit */}
            <div style={{
              height: 3,
              background: 'linear-gradient(90deg, #6366f1 0%, #a855f7 50%, #6366f1 100%)',
              backgroundSize: '200% 100%',
              animation: 'sqExtrasShimmer 3s linear infinite',
            }} />
            <style>{'@keyframes sqExtrasShimmer{0%{background-position:0% 0%}100%{background-position:200% 0%}}'}</style>

            {/* Header */}
            <div style={{
              display: 'flex', alignItems: 'center', gap: 14,
              padding: '16px 22px',
              borderBottom: '1px solid ' + __sepColor,
              flexShrink: 0,
            }}>
              <div style={{
                width: 40, height: 40, borderRadius: 12,
                display: 'flex', alignItems: 'center', justifyContent: 'center',
                background: 'linear-gradient(135deg, rgba(99,102,241,0.25) 0%, rgba(168,85,247,0.20) 100%)',
                border: '1px solid rgba(99,102,241,0.35)',
                boxShadow: '0 4px 16px rgba(99,102,241,0.18)',
                flexShrink: 0,
              }}>
                <Settings size={18} strokeWidth={1.8} style={{ color: __isLight ? '#4f46e5' : '#a5b4fc' }} />
              </div>
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{ fontSize: 15, fontWeight: 700, letterSpacing: '-0.012em', color: __headTitle }}>
                  Kalem Ek Alanları
                </div>
                <div style={{ fontSize: 11.5, color: __mutedText, marginTop: 2, display: 'flex', alignItems: 'center', gap: 8 }}>
                  <span style={{
                    fontFamily: "'JetBrains Mono','Consolas',monospace",
                    fontSize: 10.5, fontWeight: 700, letterSpacing: '.04em',
                    padding: '2px 8px', borderRadius: 6,
                    background: __chipBg, color: __chipText,
                    border: '1px solid ' + __chipBorder,
                  }}>
                    {extrasModalRow.materialCode || '—'}
                  </span>
                  <span style={{ opacity: 0.55 }}>·</span>
                  <span>Satır #{extrasModalRow.id || '—'}</span>
                </div>
              </div>
              <button
                type="button"
                onClick={function() { if (!extrasSaving) closeExtrasModal() }}
                disabled={extrasSaving}
                style={{
                  background: __closeBtnBg, border: '1px solid ' + __closeBtnBdr,
                  color: __closeBtnClr, cursor: extrasSaving ? 'not-allowed' : 'pointer',
                  width: 32, height: 32, borderRadius: 10,
                  display: 'flex', alignItems: 'center', justifyContent: 'center',
                  transition: 'all .12s',
                  opacity: extrasSaving ? 0.4 : 1,
                }}
                onMouseEnter={function(e) { if (!extrasSaving) { e.currentTarget.style.background='rgba(239,68,68,0.12)'; e.currentTarget.style.color = (__isLight ? '#b91c1c' : '#fca5a5'); e.currentTarget.style.borderColor='rgba(239,68,68,0.25)' } }}
                onMouseLeave={function(e) { e.currentTarget.style.background = __closeBtnBg; e.currentTarget.style.color = __closeBtnClr; e.currentTarget.style.borderColor = __closeBtnBdr }}
                title="Kapat (Esc)"
              >
                <XIcon size={15} strokeWidth={2} />
              </button>
            </div>

            {/* Body — ust bilgiler ek alanlari paneli gibi: sol tab yok,
                butun gruplar dikey alt alta stacked, her grubun kendi
                section basligi gorunur. sqe-widget-wrap class'i ile hedeflenen
                CSS (DocumentEdit.cshtml) bu duzeni saglar. */}
            <div
              ref={extrasBodyRef}
              className="sqe-widget-wrap"
              style={{
                flex: 1, minHeight: 0, overflowY: 'auto', padding: '18px 24px',
                background: __bodyTint,
              }}
            >
              <DynamicWidgetRenderer
                ref={extrasRendererRef}
                formCode={__lineFormCode}
                /* Kaydedilmemis satirda recordId bos; renderer schema'yi yukler ama
                   server'dan value getirmez. initialValues ile daha once bu satira
                   girilmis local degerler pre-fill edilir (row.__extras). */
                recordId={extrasModalRow.id != null && Number(extrasModalRow.id) > 0 ? String(extrasModalRow.id) : ''}
                initialValues={extrasModalRow.__extras || null}
                classPrefix="sqe"
              />
            </div>

            {/* Footer */}
            <div style={{
              display: 'flex', alignItems: 'center', justifyContent: 'space-between',
              gap: 12, padding: '14px 22px',
              borderTop: '1px solid ' + __sepColor,
              background: __footerBg,
              flexShrink: 0,
            }}>
              <div style={{ fontSize: 11.5, minHeight: 18 }}>
                {extrasToast && extrasToast.type === 'ok' && (
                  <span style={{ color: __isLight ? '#047857' : '#86efac', display: 'flex', alignItems: 'center', gap: 6 }}>
                    <span style={{ width: 6, height: 6, borderRadius: '50%', background: '#22c55e', boxShadow: '0 0 8px #22c55e' }} />
                    {extrasToast.text}
                  </span>
                )}
                {extrasToast && extrasToast.type === 'err' && (
                  <span style={{ color: __isLight ? '#b91c1c' : '#fca5a5' }}>{extrasToast.text}</span>
                )}
                {!extrasToast && (
                  <span style={{ color: __subtleText }}>
                    Değişiklikler kaydedildiğinde satıra işlenir
                  </span>
                )}
              </div>
              <div style={{ display: 'flex', gap: 8 }}>
                <button
                  type="button"
                  onClick={function() { if (!extrasSaving) closeExtrasModal() }}
                  disabled={extrasSaving}
                  style={{
                    padding: '8px 16px', borderRadius: 10,
                    background: __cancelBtnBg,
                    border: '1px solid ' + __cancelBdr,
                    color: __cancelClr,
                    fontSize: 12.5, fontWeight: 600,
                    cursor: extrasSaving ? 'not-allowed' : 'pointer',
                    opacity: extrasSaving ? 0.5 : 1,
                    transition: 'all .12s',
                  }}
                  onMouseEnter={function(e) { if (!extrasSaving) e.currentTarget.style.background = __cancelBgHov }}
                  onMouseLeave={function(e) { e.currentTarget.style.background = __cancelBtnBg }}
                >
                  İptal
                </button>
                <button
                  type="button"
                  onClick={handleExtrasSave}
                  disabled={extrasSaving}
                  style={{
                    display: 'inline-flex', alignItems: 'center', gap: 7,
                    padding: '8px 18px', borderRadius: 10,
                    background: extrasSaving
                      ? 'rgba(99,102,241,0.4)'
                      : 'linear-gradient(135deg, #6366f1 0%, #4f46e5 100%)',
                    border: '1px solid rgba(99,102,241,0.55)',
                    color: '#fff', fontSize: 12.5, fontWeight: 700,
                    cursor: extrasSaving ? 'wait' : 'pointer',
                    boxShadow: '0 4px 16px rgba(99,102,241,0.35)',
                    transition: 'all .15s',
                  }}
                  onMouseEnter={function(e) { if (!extrasSaving) e.currentTarget.style.transform='translateY(-1px)' }}
                  onMouseLeave={function(e) { e.currentTarget.style.transform='translateY(0)' }}
                >
                  {extrasSaving ? (
                    <>
                      <span style={{
                        width: 12, height: 12, border: '2px solid rgba(255,255,255,0.35)',
                        borderTopColor: '#fff', borderRadius: '50%',
                        animation: 'sqExtrasSpin 0.7s linear infinite',
                      }} />
                      Kaydediliyor…
                    </>
                  ) : 'Kaydet'}
                </button>
                <style>{'@keyframes sqExtrasSpin{to{transform:rotate(360deg)}}'}</style>
              </div>
            </div>
          </div>
        </div>,
        // Portal: satis teklif formunun body'sine (.sqe-body) absolute konumlanir.
        // .sqe-body zaten position:relative oldugu icin modal tam ortaya oturur.
        // Boylece app shell (ust bar/sol menu/alt panel) ve SQE action bar
        // modal tarafindan ortulmez, sadece sol tab navi + sag icerik ortulur.
        (document.querySelector('.sqe-body') || document.body)
      )
      })()}

      {/* ── Kisayol menusu (••• butonu dropdown'i) ────────────────────
          Butonun altinda absolute konumlanmis kucuk liste. Dis click veya
          Esc kapatir. Her item kendi tiklamasinda menuyu kapatir (navigasyon
          sonrasi state hizli temizlensin). Portal ile .sqe-body'ye cizilir
          (action bar container'inin overflow'una takilmamasi icin). */}
      {/* Portal hep mount; AnimatePresence dropdown'in giris/cikis animasyonunu handle eder */}
      {createPortal(
        <AnimatePresence>{shortcutsMenu && (function () {
          var srow = shortcutsMenu.row
          var pos = shortcutsMenu.pos || { top: 0, left: 0, width: 200 }
          var itemId = srow && (srow.stockCardId || srow.itemId)

          function close() { setShortcutsMenu(null) }
          // Shell API'sine guvenli erisim — iframe icindeyken window.top.CalibraHub
          // varsa onu, yoksa fallback olarak yerel navigation'i kullan.
          function openInWorkspaceTab(url, title, matchPath) {
            try {
              var topWin = window.top || window
              if (topWin && topWin.CalibraHub && typeof topWin.CalibraHub.openWorkspaceTab === 'function') {
                topWin.CalibraHub.openWorkspaceTab({ url: url, title: title, matchPath: matchPath })
                return
              }
            } catch (_) { /* cross-origin */ }
            navigateInWorkspace(url)
          }
          function goToStockCard() {
            close()
            if (!itemId) {
              // Rapor §6.6 — toast fallback
              var m = 'Bu satirda malzeme secilmedi — stok kartina gidilemedi.'
              if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast(m, 'warn')
              else alert(m)
              return
            }
            openInWorkspaceTab(
              '/Logistics/MaterialCardEdit?id=' + itemId,
              'Malzeme Kartlari',
              '/Logistics/MaterialCard',
            )
          }
          function goToPriceList() {
            close()
            // Fiyat listesi — materialCode hint olarak gecirilir, PriceList sayfasi
            // ileride bu paramayi kullanip ilgili satirin fiyat gecmisini auto-expand
            // edebilir. Suan icin fiyat girisi/listeleme sayfasi yeni tab'ta acilir.
            var matCode = srow && srow.materialCode ? String(srow.materialCode) : ''
            var url = '/PriceList/PriceList' + (matCode ? '?stockCode=' + encodeURIComponent(matCode) : '')
            openInWorkspaceTab(url, 'Fiyat Listesi', '/PriceList/PriceList')
          }
          function toggleNoteFromMenu() {
            close()
            if (!srow || !canModify(srow)) return
            toggleNote(srow._uid)
          }
          function openReviseFromMenu() {
            close()
            if (!srow) return
            setReviseModal({
              row: srow,
              tab: 'revise',
              // Sadelestirilmis revize akisi: kullanici sadece ACIKLAMA girer
              // (ESKI satira not olarak eklenir). Yeni satir aynen kopyalanarak
              // alta eklenir; degisiklikler gridden yapilir.
              draft: { notes: '' },
            })
          }
          function openCostViewerFromMenu() {
            close()
            if (!srow || !srow.materialCode) return
            setCostViewer({
              materialCode: srow.materialCode,
              configCode:   srow.combinationCode || null,
              quantity:     Number(srow.quantity) || 1,
              materialName: srow.materialName || '',
            })
          }

          // Not durumuna gore label + ikon degisir.
          var noteDisabled = !srow || !canModify(srow) || (belowColumns.length === 0)
          var noteHasContent = srow ? hasAnyBelowValue(srow) : false
          var noteOpen = srow ? isNoteOpen(srow) : false
          var noteLabel = noteOpen
            ? 'Notu Gizle'
            : (noteHasContent ? 'Notu Goster' : 'Not Ekle')
          var noteDisabledTitle = (!srow || !canModify(srow))
            ? 'Once kilidi acin'
            : (belowColumns.length === 0 ? 'Bu grid icin not alani tanimli degil' : '')

          // Revize: satir kilidinden bagimsiz calisir. Kayitsiz (id yok) satir icin de
          // menu acilir ama createRevision icinde id yoksa uyari veriliyor — UX tutarli.
          var reviseHasParent = srow && srow.id != null && Number(srow.id) > 0 &&
            rows.some(function (r) { return r.revisedFromId != null && Number(r.revisedFromId) === Number(srow.id) })
          var reviseLabel = reviseHasParent ? 'Revize Et / Gecmisi Goster' : 'Revize Et'

          // Item tanimi — ileride yeni kisayollar buraya eklenir (barkod bas,
          // stok hareketleri vb.). Her item bir aksent renge sahip (icon pill
          // ve hover vurgusu icin), ve grup ayraci `groupBefore` ile baslayan
          // ilk item'larda belirir. Aksiyonlar 2 grupta: Navigasyon (kart/fiyat)
          // ve Satir Islemi (not/revize).
          var items = [
            {
              key: 'stock-card',
              label: 'Stok Kartina Git',
              hint: 'Yeni sekme',
              icon: ExternalLink,
              accent: 'indigo',
              hasArrow: true,
              onClick: goToStockCard,
              disabled: !itemId,
              disabledTitle: 'Once malzeme seciniz',
            },
            {
              key: 'price-list',
              label: 'Fiyat Gecmisi',
              hint: 'Fiyat Listesi',
              icon: History,
              accent: 'emerald',
              hasArrow: true,
              onClick: goToPriceList,
              disabled: false,
            },
            {
              key: 'cost-view',
              label: 'Maliyet Gör',
              hint: 'Reçete fiyat',
              icon: Calculator,
              accent: 'amber',
              groupBefore: true,
              onClick: openCostViewerFromMenu,
              disabled: !srow || !srow.materialCode,
              disabledTitle: 'Önce malzeme seçin',
            },
            {
              key: 'note',
              label: noteLabel,
              icon: StickyNote,
              accent: 'amber',
              onClick: toggleNoteFromMenu,
              disabled: noteDisabled,
              disabledTitle: noteDisabledTitle,
            },
            {
              key: 'revise',
              label: reviseLabel,
              icon: GitBranch,
              accent: 'violet',
              onClick: openReviseFromMenu,
              disabled: false,
            },
          ]
          // İhtiyaç Kaydi (alis_talebi): sadece Stok Kartina Git + Not Ekle.
          // Fiyat / Maliyet / Revize talep asamasinda bir karsiligi yok.
          if (__isPurchaseRequest) {
            items = items.filter(function (it) {
              return it.key === 'stock-card' || it.key === 'note'
            })
          }
          // Aksent renk haritasi — icon pill bg / text + hover bg.
          // Light/dark farkli paletler. Tema body class'i ile alginirir; iframe icinde
          // mount edilmis ise parent dokumanin body class'ina da bakilir (workspace
          // iframe'leri tema sinifini bazen biraz sonra alir — fallback chain). Default light.
          var __isLight = (function () {
            if (typeof document === 'undefined') return true
            try {
              if (document.body.classList.contains('app-theme-light')) return true
              if (document.body.classList.contains('app-theme-dark'))  return false
              if (window.parent && window.parent !== window && window.parent.document && window.parent.document.body) {
                if (window.parent.document.body.classList.contains('app-theme-light')) return true
                if (window.parent.document.body.classList.contains('app-theme-dark'))  return false
              }
            } catch (_) {}
            return true   // hicbir class yoksa light varsayilan
          })()
          var accentMap = __isLight ? {
            indigo:  { bg: 'rgba(99,102,241,0.10)',  text: '#4f46e5', hoverBg: 'rgba(99,102,241,0.10)',  hoverShadow: 'rgba(99,102,241,0.22)' },
            emerald: { bg: 'rgba(16,185,129,0.10)',  text: '#047857', hoverBg: 'rgba(16,185,129,0.10)',  hoverShadow: 'rgba(16,185,129,0.22)' },
            amber:   { bg: 'rgba(245,158,11,0.12)',  text: '#b45309', hoverBg: 'rgba(245,158,11,0.10)',  hoverShadow: 'rgba(245,158,11,0.25)' },
            violet:  { bg: 'rgba(139,92,246,0.10)',  text: '#6d28d9', hoverBg: 'rgba(139,92,246,0.10)',  hoverShadow: 'rgba(139,92,246,0.22)' },
            sky:     { bg: 'rgba(14,165,233,0.10)',  text: '#0369a1', hoverBg: 'rgba(14,165,233,0.10)',  hoverShadow: 'rgba(14,165,233,0.22)' },
            slate:   { bg: 'rgba(148,163,184,0.14)', text: '#475569', hoverBg: 'rgba(148,163,184,0.10)', hoverShadow: 'rgba(148,163,184,0.22)' },
          } : {
            indigo:  { bg: 'rgba(99,102,241,0.18)',  text: '#a5b4fc', hoverBg: 'rgba(99,102,241,0.12)',  hoverShadow: 'rgba(99,102,241,0.20)' },
            emerald: { bg: 'rgba(16,185,129,0.18)',  text: '#6ee7b7', hoverBg: 'rgba(16,185,129,0.10)',  hoverShadow: 'rgba(16,185,129,0.18)' },
            amber:   { bg: 'rgba(245,158,11,0.18)',  text: '#fcd34d', hoverBg: 'rgba(245,158,11,0.10)',  hoverShadow: 'rgba(245,158,11,0.18)' },
            violet:  { bg: 'rgba(139,92,246,0.20)',  text: '#c4b5fd', hoverBg: 'rgba(139,92,246,0.10)',  hoverShadow: 'rgba(139,92,246,0.20)' },
            sky:     { bg: 'rgba(14,165,233,0.18)',  text: '#7dd3fc', hoverBg: 'rgba(14,165,233,0.10)',  hoverShadow: 'rgba(14,165,233,0.18)' },
            slate:   { bg: 'rgba(148,163,184,0.18)', text: '#cbd5e1', hoverBg: 'rgba(148,163,184,0.10)', hoverShadow: 'rgba(148,163,184,0.18)' },
          }
          // Tema-bagli surface/text degerleri — paneli ve item rengini saran ortak palet
          var __menuBg     = __isLight ? 'linear-gradient(180deg, #ffffff 0%, #f8fafc 100%)' : 'linear-gradient(180deg, rgba(28,32,48,0.97) 0%, rgba(20,24,38,0.97) 100%)'
          var __menuBorder = __isLight ? '1px solid rgba(99,102,241,0.18)' : '1px solid rgba(255,255,255,0.08)'
          var __menuShadow = __isLight
            ? '0 12px 36px rgba(15,23,42,0.14), 0 4px 14px rgba(99,102,241,0.10), inset 0 1px 0 rgba(255,255,255,0.6)'
            : '0 20px 60px rgba(0,0,0,0.55), 0 6px 20px rgba(99,102,241,0.18), inset 0 1px 0 rgba(255,255,255,0.06)'
          var __sepColor   = __isLight ? 'rgba(15,23,42,0.08)' : 'rgba(255,255,255,0.10)'
          var __textColor      = __isLight ? '#0f172a'             : 'rgba(255,255,255,0.92)'
          var __textColorDis   = __isLight ? '#94a3b8'             : 'rgba(255,255,255,0.35)'
          var __hintColor      = __isLight ? '#64748b'             : 'rgba(255,255,255,0.42)'
          var __hintColorDis   = __isLight ? '#cbd5e1'             : 'rgba(255,255,255,0.25)'
          var __chevronColor   = __isLight ? '#94a3b8'             : 'rgba(255,255,255,0.30)'

          return (
            <>
              {/* Gorunmez overlay — dis click ile kapat */}
              <motion.div
                key="sm-shortcuts-overlay"
                onClick={close}
                initial={{ opacity: 0 }}
                animate={{ opacity: 1 }}
                exit={{ opacity: 0 }}
                transition={{ duration: 0.12 }}
                style={{
                  position: 'fixed', inset: 0, zIndex: 9998,
                  background: 'transparent',
                }}
              />
              {/* Menu — butonun altina konumla, spring entrance + child stagger */}
              <motion.div
                key="sm-shortcuts-menu"
                role="menu"
                aria-label="Satir kisayol menusu"
                onKeyDown={function (e) { if (e.key === 'Escape') close() }}
                initial={{ opacity: 0, y: -10, scale: 0.94 }}
                animate={{ opacity: 1, y: 0, scale: 1 }}
                exit={{ opacity: 0, y: -8, scale: 0.96 }}
                transition={{ type: 'spring', stiffness: 420, damping: 28, mass: 0.6 }}
                style={{
                  position: 'fixed',
                  top: pos.top, left: pos.left,
                  minWidth: Math.max(pos.width, 240),
                  zIndex: 9999,
                  borderRadius: 14,
                  padding: 6,
                  // Glassmorphism — tema-bagli zemin + ic isik + cam efekti
                  background: __menuBg,
                  border: __menuBorder,
                  boxShadow: __menuShadow,
                  backdropFilter: 'blur(18px) saturate(140%)',
                  WebkitBackdropFilter: 'blur(18px) saturate(140%)',
                  display: 'flex', flexDirection: 'column',
                  transformOrigin: 'top left',
                }}
              >
                {/* Renkli ust aksent cizgisi — cam yansimasi gibi durur. Light temada
                    daha hafif tonda, dark temada daha belirgin. */}
                <div aria-hidden="true" style={{
                  position: 'absolute', top: 0, left: 14, right: 14, height: 1,
                  background: __isLight
                    ? 'linear-gradient(90deg, transparent 0%, rgba(99,102,241,0.40) 30%, rgba(168,85,247,0.40) 70%, transparent 100%)'
                    : 'linear-gradient(90deg, transparent 0%, rgba(99,102,241,0.55) 30%, rgba(168,85,247,0.55) 70%, transparent 100%)',
                  pointerEvents: 'none',
                }} />
                {items.map(function (it, idx) {
                  var Icon = it.icon
                  var pal = accentMap[it.accent] || accentMap.slate
                  // Custom render with stagger via per-item motion
                  return (
                    <span key={it.key} style={{ display: 'contents' }}>
                      {it.groupBefore && (
                        <motion.div
                          aria-hidden="true"
                          initial={{ opacity: 0, scaleX: 0.6 }}
                          animate={{ opacity: 1, scaleX: 1 }}
                          transition={{ delay: 0.04 * idx + 0.05, duration: 0.18, ease: [0.23, 1, 0.32, 1] }}
                          style={{
                            height: 1, margin: '4px 8px',
                            background: 'linear-gradient(90deg, transparent, ' + __sepColor + ', transparent)',
                            transformOrigin: 'left center',
                          }}
                        />
                      )}
                      <motion.button
                        type="button"
                        role="menuitem"
                        disabled={!!it.disabled}
                        onClick={it.onClick}
                        title={it.disabled ? (it.disabledTitle || '') : (it.title || '')}
                        initial={{ opacity: 0, x: -6 }}
                        animate={{ opacity: 1, x: 0 }}
                        transition={{ delay: 0.04 * idx + 0.06, duration: 0.22, ease: [0.23, 1, 0.32, 1] }}
                        whileHover={!it.disabled ? { x: 2 } : {}}
                        whileTap={!it.disabled ? { scale: 0.985 } : {}}
                        style={{
                          display: 'flex', alignItems: 'center', gap: 11,
                          padding: '8px 10px 8px 8px',
                          fontSize: 12.75, fontWeight: 600, letterSpacing: '-0.005em',
                          color: it.disabled ? __textColorDis : __textColor,
                          background: 'transparent', border: 'none',
                          borderRadius: 9,
                          cursor: it.disabled ? 'not-allowed' : 'pointer',
                          textAlign: 'left',
                          transition: 'background .14s ease, box-shadow .14s ease, color .14s ease',
                          position: 'relative',
                        }}
                        onMouseEnter={function (e) {
                          if (it.disabled) return
                          e.currentTarget.style.background = pal.hoverBg
                          e.currentTarget.style.boxShadow = '0 0 0 1px ' + pal.hoverShadow + ' inset'
                          e.currentTarget.style.color = pal.text
                        }}
                        onMouseLeave={function (e) {
                          e.currentTarget.style.background = 'transparent'
                          e.currentTarget.style.boxShadow = 'none'
                          e.currentTarget.style.color = it.disabled ? __textColorDis : __textColor
                        }}
                      >
                        {/* Icon pill — accent renkli kucuk yuvarlak kare */}
                        <span style={{
                          flexShrink: 0,
                          width: 26, height: 26,
                          borderRadius: 8,
                          display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
                          background: it.disabled ? (__isLight ? 'rgba(148,163,184,0.18)' : 'rgba(148,163,184,0.10)') : pal.bg,
                          color: it.disabled ? __textColorDis : pal.text,
                          border: '1px solid ' + (it.disabled ? (__isLight ? 'rgba(148,163,184,0.20)' : 'rgba(148,163,184,0.10)') : 'transparent'),
                          transition: 'transform .14s ease',
                        }}>
                          <Icon size={14} strokeWidth={1.9} />
                        </span>
                        {/* Label + opsiyonel ipucu metni */}
                        <span style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: 1, minWidth: 0 }}>
                          <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                            {it.label}
                          </span>
                          {it.hint && (
                            <span style={{
                              fontSize: 10, fontWeight: 500, letterSpacing: '.02em',
                              color: it.disabled ? __hintColorDis : __hintColor,
                              overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
                            }}>
                              {it.hint}
                            </span>
                          )}
                        </span>
                        {/* Sag chevron — navigasyon item'larini isaret eder */}
                        {it.hasArrow && !it.disabled && (
                          <ChevronRight size={13} strokeWidth={2} style={{
                            flexShrink: 0,
                            color: __chevronColor,
                            transition: 'transform .14s ease, color .14s ease',
                          }} />
                        )}
                      </motion.button>
                    </span>
                  )
                })}
              </motion.div>
            </>
          )
        })()}</AnimatePresence>,
        (document.querySelector('.sqe-body') || document.body)
      )}

      {/* ── Revize modal'i ─────────────────────────────────────────────
          Satir aksiyon seridindeki Revize butonuna basildiginda acilir.
          Iki sekme:
            - Revize Et: miktar/birim fiyat/iskonto/not degisiklikleri icin form
            - Gecmis Revizeler: revised_from_id zinciri geriye takip edilerek listelenir
          Revize Olustur tiklaninca yeni bir satir eklenir — orijinal satir
          degismez; yeni satirin revisedFromId alani secili satirin id'sine
          set edilir (satir kayitsizsa uyari: once ana belgeyi kaydet). */}
      {reviseModal && createPortal(
        (function () {
          var row = reviseModal.row
          var activeTab = reviseModal.tab || 'revise'
          var draft = reviseModal.draft || {}

          // Revize zinciri — kok (orijinal) -> son revizelere dogru
          // Yeni yon: eski satirlarda revisedFromId = daha yeni satirin id'si
          // row (aktif, revisedFromId=null) baslangic; gerideki surumleri bul
          var chain = []
          var seen = {}
          var cur = row
          if (cur) {
            seen[cur._uid] = true
            if (cur.id) seen['id:' + cur.id] = true
            chain.push(cur)
          }
          var guard = 0
          while (cur && cur.id && Number(cur.id) > 0 && guard < 50) {
            guard++
            var curId = Number(cur.id)
            var predecessor = rows.find(function (r) {
              return r.revisedFromId != null && Number(r.revisedFromId) === curId && !seen[r._uid]
            })
            if (!predecessor) break
            seen[predecessor._uid] = true
            if (predecessor.id) seen['id:' + predecessor.id] = true
            chain.push(predecessor)
            cur = predecessor
          }
          chain.reverse() // orijinal en basta, bu satir en altta
          var chainIndex = chain.length - 1 // bu satirin pozisyonu (0 = orijinal)
          var hasRevisionParent = chain.length > 1

          // Tarih/para formatlamak icin kisa yardimci — satir icinde inline
          var fmtNum = function (n) {
            if (n == null || n === '') return '-'
            var x = Number(n)
            if (!isFinite(x)) return String(n)
            return x.toLocaleString('tr-TR', { maximumFractionDigits: 4 })
          }

          function close() { setReviseModal(null) }
          function setDraft(key, val) {
            setReviseModal(function (m) {
              if (!m) return m
              var nd = Object.assign({}, m.draft); nd[key] = val
              return Object.assign({}, m, { draft: nd })
            })
          }
          // Cok alanli guncellemeler icin — CombinationLookupCell onChange
          // (key, value, fill) imzasinda fill ile ek alanlar dolduruyor;
          // setDraft ile ayri ayri yaparsak React batching sorunu olusabilir.
          function mergeDraft(patch) {
            setReviseModal(function (m) {
              if (!m) return m
              var nd = Object.assign({}, m.draft, patch || {})
              return Object.assign({}, m, { draft: nd })
            })
          }
          function setTab(tab) {
            setReviseModal(function (m) { return m ? Object.assign({}, m, { tab: tab }) : m })
          }
          function createRevision() {
            var parentId = Number(row.id)
            if (!parentId || parentId <= 0) {
              // Rapor §6.6 — toast fallback
              var m0 = 'Once ana belgeyi kaydedin — kayitli olmayan satir revize edilemez.'
              if (window.CalibraAlert && window.CalibraAlert.warn) window.CalibraAlert.warn(m0)
              else if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast(m0, 'warn')
              else alert(m0)
              return
            }
            // Server-side atomik revize — /Sales/ReviseLine tek transaction:
            //   1) Eski satirin notes'unu @Description ile guncelle
            //   2) Yeni satiri INSERT (eski'nin birebir kopyasi + revised_from_id)
            //   3) Kombinasyon detaylari + widget/alan degerleri de kopyalanir
            // Basari halinde grid sunucudan taze verilerle yeniden yuklenir.
            var reviseNote = (draft.notes != null ? String(draft.notes) : '').trim()
            setReviseModal(function (m) { return m ? Object.assign({}, m, { saving: true }) : m })

            var token = (document.querySelector('input[name="__RequestVerificationToken"]') || {}).value || ''
            var headers = { 'Content-Type': 'application/json' }
            if (token) headers['RequestVerificationToken'] = token

            fetch('/Sales/ReviseLine', {
              method: 'POST',
              credentials: 'same-origin',
              headers: headers,
              body: JSON.stringify({ parentLineId: parentId, description: reviseNote })
            })
              .then(function (resp) { return resp.json() })
              .then(function (data) {
                if (!data || data.success !== true) {
                  // Rapor §6.6 — toast fallback
                  var m1 = 'Revize basarisiz: ' + (data && data.message ? data.message : 'bilinmeyen hata')
                  if (window.CalibraAlert && window.CalibraAlert.error) window.CalibraAlert.error(m1)
                  else if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast(m1, 'err')
                  else alert(m1)
                  setReviseModal(function (m) { return m ? Object.assign({}, m, { saving: false }) : m })
                  return
                }
                close()
                // Grid'i sunucudan yeniden yukle — sayfa helper'i varsa oradan, yoksa elle.
                if (typeof window.sqReloadLinesFromServer === 'function') {
                  window.sqReloadLinesFromServer()
                  return
                }
                var docId = (rows.find(function (r) { return Number(r.documentId) > 0 }) || {}).documentId || null
                if (!docId) return
                fetch('/Sales/GetQuote?id=' + docId, { credentials: 'same-origin' })
                  .then(function (r2) { return r2.json() })
                  .then(function (q) {
                    if (!q || !Array.isArray(q.lines)) return
                    var mats = (typeof window !== 'undefined' && Array.isArray(window.__SQ_MATERIALS__)) ? window.__SQ_MATERIALS__ : []
                    var synced = q.lines.map(function (ln) {
                      var m = mats.find(function (x) { return x.id === ln.itemId })
                      return Object.assign({}, ln, {
                        stockCardId:       ln.itemId,
                        trackCombinations: m ? m.trackCombinations === true : false,
                        taxRate:           ln.taxRate != null ? ln.taxRate : (m && m.taxRate != null ? m.taxRate : 20),
                      })
                    })
                    setRows(synced)
                  })
                  .catch(function () { /* swallow */ })
              })
              .catch(function (err) {
                var m2 = 'Revize hatasi: ' + (err && err.message ? err.message : String(err))
                if (window.CalibraAlert && window.CalibraAlert.error) window.CalibraAlert.error(m2)
                else if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast(m2, 'err')
                else alert(m2)
                setReviseModal(function (m) { return m ? Object.assign({}, m, { saving: false }) : m })
              })
          }

          return (
            <div
              onClick={function (e) { if (e.target === e.currentTarget) close() }}
              style={{
                position: 'absolute', inset: 0,
                background: 'radial-gradient(at 20% 10%, rgba(139,92,246,0.12) 0%, transparent 45%), radial-gradient(at 85% 85%, rgba(99,102,241,0.10) 0%, transparent 45%), rgba(3,6,15,0.72)',
                backdropFilter: 'blur(6px)', WebkitBackdropFilter: 'blur(6px)',
                display: 'flex', alignItems: 'center', justifyContent: 'center',
                zIndex: 55, padding: 16,
                animation: 'sqExtrasFade 160ms ease-out',
              }}
            >
              <div style={{
                width: '92%', maxWidth: 720, maxHeight: '88vh',
                display: 'flex', flexDirection: 'column', overflow: 'hidden',
                borderRadius: 18,
                background: 'linear-gradient(180deg, rgba(23,28,42,0.98) 0%, rgba(15,19,30,0.98) 100%)',
                border: '1px solid rgba(255,255,255,0.10)',
                boxShadow: '0 32px 96px rgba(0,0,0,0.65), 0 0 0 1px rgba(139,92,246,0.10)',
                color: 'rgba(255,255,255,0.92)',
                animation: 'sqExtrasPop 220ms cubic-bezier(.2,.8,.3,1)',
              }}>
                {/* Ust gradient serit — mor/indigo tonlari */}
                <div style={{
                  height: 3,
                  background: 'linear-gradient(90deg, #8b5cf6 0%, #6366f1 50%, #8b5cf6 100%)',
                  backgroundSize: '200% 100%',
                  animation: 'sqExtrasShimmer 3s linear infinite',
                }} />

                {/* Header */}
                <div style={{
                  display: 'flex', alignItems: 'center', gap: 14,
                  padding: '16px 22px',
                  borderBottom: '1px solid rgba(255,255,255,0.06)',
                  flexShrink: 0,
                }}>
                  <div style={{
                    width: 40, height: 40, borderRadius: 12,
                    display: 'flex', alignItems: 'center', justifyContent: 'center',
                    background: 'linear-gradient(135deg, rgba(139,92,246,0.25) 0%, rgba(99,102,241,0.20) 100%)',
                    border: '1px solid rgba(139,92,246,0.35)',
                    boxShadow: '0 4px 16px rgba(139,92,246,0.18)',
                    flexShrink: 0,
                  }}>
                    <GitBranch size={18} strokeWidth={1.9} style={{ color: '#c4b5fd' }} />
                  </div>
                  <div style={{ flex: 1, minWidth: 0 }}>
                    <div style={{ fontSize: 15, fontWeight: 700, letterSpacing: '-0.012em', color: '#fff' }}>
                      Satir Revizyonu
                    </div>
                    <div style={{ fontSize: 11.5, color: 'rgba(255,255,255,0.5)', marginTop: 2, display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
                      <span style={{
                        fontFamily: "'JetBrains Mono','Consolas',monospace",
                        fontSize: 10.5, fontWeight: 700, letterSpacing: '.04em',
                        padding: '2px 8px', borderRadius: 6,
                        background: 'rgba(139,92,246,0.14)', color: '#c4b5fd',
                        border: '1px solid rgba(139,92,246,0.25)',
                      }}>
                        {row.materialCode || '—'}
                      </span>
                      <span style={{ opacity: 0.6 }}>·</span>
                      <span>{row.materialName || '—'}</span>
                      {hasRevisionParent && (
                        <>
                          <span style={{ opacity: 0.5 }}>·</span>
                          <span style={{
                            fontSize: 10.5, fontWeight: 700, letterSpacing: '.04em',
                            padding: '2px 7px', borderRadius: 6,
                            background: 'rgba(99,102,241,0.15)', color: '#a5b4fc',
                            border: '1px solid rgba(99,102,241,0.28)',
                          }}>
                            {chain.length - 1 > 0 ? (chain.length - 1) + '. Revize' : 'Orijinal'}
                          </span>
                        </>
                      )}
                    </div>
                  </div>
                  <button
                    type="button"
                    onClick={close}
                    style={{
                      background: 'rgba(255,255,255,0.04)', border: '1px solid rgba(255,255,255,0.08)',
                      color: 'rgba(255,255,255,0.7)', cursor: 'pointer',
                      width: 32, height: 32, borderRadius: 10,
                      display: 'flex', alignItems: 'center', justifyContent: 'center',
                      transition: 'all .12s',
                    }}
                    onMouseEnter={function (e) { e.currentTarget.style.background='rgba(239,68,68,0.12)'; e.currentTarget.style.color='#fca5a5'; e.currentTarget.style.borderColor='rgba(239,68,68,0.25)' }}
                    onMouseLeave={function (e) { e.currentTarget.style.background='rgba(255,255,255,0.04)'; e.currentTarget.style.color='rgba(255,255,255,0.7)'; e.currentTarget.style.borderColor='rgba(255,255,255,0.08)' }}
                    title="Kapat (Esc)"
                  >
                    <XIcon size={15} strokeWidth={2} />
                  </button>
                </div>

                {/* Tab bar */}
                <div style={{
                  display: 'flex', gap: 6,
                  padding: '10px 22px 0',
                  borderBottom: '1px solid rgba(255,255,255,0.04)',
                  flexShrink: 0,
                }}>
                  {[
                    { k: 'revise', label: 'Revize Et', icon: GitBranch },
                    { k: 'history', label: 'Gecmis Revizeler', icon: History, badge: chain.length > 1 ? chain.length : null },
                  ].map(function (t) {
                    var T = t.icon
                    var active = activeTab === t.k
                    return (
                      <button
                        key={t.k}
                        type="button"
                        onClick={function () { setTab(t.k) }}
                        style={{
                          display: 'inline-flex', alignItems: 'center', gap: 7,
                          padding: '9px 14px',
                          border: 'none',
                          borderBottom: active ? '2px solid #c4b5fd' : '2px solid transparent',
                          background: 'transparent',
                          color: active ? '#fff' : 'rgba(255,255,255,0.55)',
                          fontSize: 12.5, fontWeight: active ? 700 : 600,
                          letterSpacing: '-0.005em',
                          cursor: 'pointer',
                          transition: 'color .15s, border-color .15s',
                          marginBottom: -1,
                        }}
                      >
                        <T size={13} strokeWidth={2} />
                        {t.label}
                        {t.badge && (
                          <span style={{
                            fontSize: 10, fontWeight: 700,
                            padding: '1px 6px', borderRadius: 8,
                            background: active ? 'rgba(139,92,246,0.25)' : 'rgba(255,255,255,0.08)',
                            color: active ? '#ddd6fe' : 'rgba(255,255,255,0.65)',
                          }}>{t.badge}</span>
                        )}
                      </button>
                    )
                  })}
                </div>

                {/* Body */}
                <div style={{ flex: 1, overflowY: 'auto', padding: '18px 22px' }}>
                  {activeTab === 'revise' && (
                    <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
                      <div style={{ fontSize: 12, color: 'rgba(255,255,255,0.55)', lineHeight: 1.55 }}>
                        Yazacaginiz aciklama <strong style={{ color: '#c4b5fd' }}>bu (eski) satira</strong>
                        not olarak eklenir — eski halinin niye revize edildigini anlatir.
                        <strong style={{ color: '#c4b5fd' }}> Revize Et</strong> dediginizde mevcut kalem
                        aynen kopyalanarak alta yeni bir satir olarak eklenir; miktar, fiyat, iskonto ve
                        kombinasyon degisikliklerini <strong style={{ color: '#c4b5fd' }}>yeni satir uzerinde</strong>
                        gridden yapabilirsiniz. Eski revize bilgileri "Gecmis Revizeler" sekmesinden
                        goruntulenebilir.
                      </div>
                      <label style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
                        <span style={{ fontSize: 10.5, fontWeight: 700, letterSpacing: '.04em', textTransform: 'uppercase', color: 'rgba(255,255,255,0.55)' }}>
                          Aciklama (eski kaleme ait)
                        </span>
                        <textarea
                          rows={5}
                          autoFocus
                          value={draft.notes != null ? draft.notes : ''}
                          onChange={function (e) { setDraft('notes', e.target.value) }}
                          style={{
                            font: 'inherit', fontSize: 13,
                            padding: '10px 12px',
                            borderRadius: 10, resize: 'vertical', minHeight: 120,
                            border: '1px solid rgba(255,255,255,0.14)',
                            background: 'rgba(10,14,24,0.55)',
                            color: 'rgba(255,255,255,0.95)',
                            outline: 'none', lineHeight: 1.55,
                          }}
                          onFocus={function (e) { e.currentTarget.style.borderColor = 'rgba(139,92,246,0.65)'; e.currentTarget.style.boxShadow = '0 0 0 3px rgba(139,92,246,0.18)' }}
                          onBlur={function (e) { e.currentTarget.style.borderColor = 'rgba(255,255,255,0.14)'; e.currentTarget.style.boxShadow = 'none' }}
                          placeholder="Ornek: Musteri ilk basta 10 adet istemisti, sonradan artirdi…"
                        />
                      </label>
                      {/* Bu satirin ONCEKI notu varsa (ornegin daha onceki revizyondan kalan) bilgi olarak goster */}
                      {row.notes && (
                        <div style={{
                          padding: '8px 12px', borderRadius: 9,
                          background: 'rgba(255,255,255,0.025)',
                          border: '1px dashed rgba(255,255,255,0.10)',
                          fontSize: 11.5, color: 'rgba(255,255,255,0.5)', lineHeight: 1.5,
                        }}>
                          <span style={{ fontWeight: 700, opacity: 0.75 }}>Mevcut not:</span>
                          <span style={{ fontStyle: 'italic', marginLeft: 6 }}>"{row.notes}"</span>
                          <span style={{ display: 'block', marginTop: 3, opacity: 0.55, fontSize: 10.5 }}>
                            Revize et dediginizde bu metin yazdiklariniz ile degistirilecek.
                          </span>
                        </div>
                      )}
                    </div>
                  )}

                  {activeTab === 'history' && (
                    <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
                      {chain.length === 0 && (
                        <div style={{ padding: 24, textAlign: 'center', color: 'rgba(255,255,255,0.45)', fontSize: 12.5 }}>
                          Bu satirin revizyon zinciri bulunamadi.
                        </div>
                      )}
                      {chain.length > 0 && (
                        <div style={{ fontSize: 12, color: 'rgba(255,255,255,0.55)', marginBottom: 6 }}>
                          Zincir uzunlugu: <strong style={{ color: '#c4b5fd' }}>{chain.length}</strong>
                          {chain.length > 1 ? ' kayit (orijinal + ' + (chain.length - 1) + ' revize)' : ' kayit (orijinal)'}
                        </div>
                      )}
                      {chain.map(function (item, idx) {
                        var isCurrent = item._uid === row._uid
                        var isOriginal = idx === 0
                        var label = isOriginal ? 'Orijinal' : (idx + '. Revize')
                        return (
                          <div
                            key={item._uid || ('chain-' + idx)}
                            style={{
                              display: 'flex', alignItems: 'stretch', gap: 12,
                              padding: '12px 14px',
                              borderRadius: 12,
                              background: isCurrent
                                ? 'linear-gradient(135deg, rgba(139,92,246,0.14), rgba(99,102,241,0.10))'
                                : 'rgba(255,255,255,0.025)',
                              border: '1px solid ' + (isCurrent ? 'rgba(139,92,246,0.35)' : 'rgba(255,255,255,0.06)'),
                            }}
                          >
                            <div style={{
                              width: 44, flexShrink: 0,
                              display: 'flex', alignItems: 'center', justifyContent: 'center',
                              fontSize: 11, fontWeight: 700, letterSpacing: '.03em',
                              padding: '4px 6px', borderRadius: 8,
                              background: isOriginal ? 'rgba(34,197,94,0.14)' : 'rgba(139,92,246,0.14)',
                              color: isOriginal ? '#86efac' : '#c4b5fd',
                              border: '1px solid ' + (isOriginal ? 'rgba(34,197,94,0.28)' : 'rgba(139,92,246,0.28)'),
                              textAlign: 'center',
                            }}>
                              {isOriginal ? 'ORJ' : '#' + idx}
                            </div>
                            <div style={{ flex: 1, minWidth: 0 }}>
                              <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap', marginBottom: 4 }}>
                                <span style={{ fontSize: 12.5, fontWeight: 700, color: '#fff' }}>{label}</span>
                                {isCurrent && (
                                  <span style={{
                                    fontSize: 10, fontWeight: 700,
                                    padding: '1px 7px', borderRadius: 999,
                                    background: 'rgba(139,92,246,0.28)', color: '#ddd6fe',
                                  }}>bu satir</span>
                                )}
                                <span style={{ fontSize: 10.5, color: 'rgba(255,255,255,0.4)', fontFamily: "'JetBrains Mono','Consolas',monospace" }}>
                                  {item.id ? '#' + item.id : '(kayit bekliyor)'}
                                </span>
                              </div>
                              <div style={{ display: 'flex', gap: 16, fontSize: 12, color: 'rgba(255,255,255,0.75)', flexWrap: 'wrap' }}>
                                <span><span style={{ opacity: 0.55 }}>Miktar:</span> <strong>{fmtNum(item.quantity)}</strong></span>
                                <span><span style={{ opacity: 0.55 }}>B.Fiyat:</span> <strong>{fmtNum(item.unitPrice)}</strong></span>
                                <span><span style={{ opacity: 0.55 }}>Isk%:</span> <strong>{fmtNum(item.discountRate)}</strong></span>
                                {/* Kombinasyon — bir onceki revizyonla farkliysa "Degisti" rozeti ile vurgula.
                                    Kullanici zincirde hangi adimda kombinasyonun degistigini tek bakista gorur. */}
                                {item.combinationCode && (function () {
                                  var prev = idx > 0 ? chain[idx - 1] : null
                                  var changed = prev && prev.combinationCode && prev.combinationCode !== item.combinationCode
                                  return (
                                    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
                                      <span style={{ opacity: 0.55 }}>Kombinasyon:</span>
                                      <strong style={{ fontFamily: "'JetBrains Mono','Consolas',monospace", fontSize: 11.5 }}>
                                        {item.combinationCode}
                                      </strong>
                                      {changed && (
                                        <span style={{
                                          fontSize: 9.5, fontWeight: 700,
                                          padding: '1px 6px', borderRadius: 8,
                                          background: 'rgba(234,179,8,0.20)', color: '#fde68a',
                                          border: '1px solid rgba(234,179,8,0.40)',
                                          letterSpacing: '.04em', textTransform: 'uppercase',
                                        }} title={'Onceki: ' + prev.combinationCode}>
                                          Degisti
                                        </span>
                                      )}
                                    </span>
                                  )
                                })()}
                                {item.notes && (
                                  <span style={{ flexBasis: '100%', color: 'rgba(255,255,255,0.55)', fontStyle: 'italic', marginTop: 2 }}>
                                    "{item.notes}"
                                  </span>
                                )}
                              </div>
                            </div>
                          </div>
                        )
                      })}
                    </div>
                  )}
                </div>

                {/* Footer */}
                <div style={{
                  display: 'flex', justifyContent: 'flex-end', gap: 10,
                  padding: '14px 22px',
                  borderTop: '1px solid rgba(255,255,255,0.06)',
                  flexShrink: 0,
                }}>
                  <button
                    type="button"
                    onClick={close}
                    style={{
                      padding: '9px 18px',
                      borderRadius: 9,
                      fontSize: 12.5, fontWeight: 700,
                      color: 'rgba(255,255,255,0.82)',
                      background: 'rgba(255,255,255,0.04)',
                      border: '1px solid rgba(255,255,255,0.12)',
                      cursor: 'pointer',
                      transition: 'all .12s',
                    }}
                    onMouseEnter={function (e) { e.currentTarget.style.background = 'rgba(255,255,255,0.09)'; e.currentTarget.style.color = '#fff' }}
                    onMouseLeave={function (e) { e.currentTarget.style.background = 'rgba(255,255,255,0.04)'; e.currentTarget.style.color = 'rgba(255,255,255,0.82)' }}
                  >
                    Iptal
                  </button>
                  {activeTab === 'revise' && (
                    <button
                      type="button"
                      onClick={createRevision}
                      disabled={!!reviseModal.saving}
                      style={{
                        padding: '9px 20px',
                        borderRadius: 9,
                        fontSize: 12.5, fontWeight: 700,
                        color: '#fff',
                        background: 'linear-gradient(135deg, #8b5cf6, #6366f1)',
                        border: 'none',
                        cursor: reviseModal.saving ? 'not-allowed' : 'pointer',
                        opacity: reviseModal.saving ? 0.7 : 1,
                        boxShadow: '0 4px 14px rgba(139,92,246,0.32)',
                        display: 'inline-flex', alignItems: 'center', gap: 7,
                        transition: 'transform .1s, filter .12s, opacity .12s',
                      }}
                      onMouseEnter={function (e) { if (!reviseModal.saving) { e.currentTarget.style.filter = 'brightness(1.08)'; e.currentTarget.style.transform = 'translateY(-1px)' } }}
                      onMouseLeave={function (e) { e.currentTarget.style.filter = 'none'; e.currentTarget.style.transform = 'none' }}
                    >
                      <GitBranch size={13} strokeWidth={2.2} />
                      {reviseModal.saving ? 'Kaydediliyor…' : 'Revize Et'}
                    </button>
                  )}
                </div>
              </div>
            </div>
          )
        })(),
        (document.querySelector('.sqe-body') || document.body)
      )}

      {/* ── Belge Toplu Maliyet Modal'i ────────────────────────────────────
          Window CustomEvent `quote:open-cost-summary` ile dis dunyadan acilir
          (cshtml'deki Islemler dropdown'undaki "Tüm Ürünlerin Maliyeti"). Her
          kalem icin paralel /Logistics/GetMaterialCost cagriyla toplam hesaplanir. */}
      <QuoteCostSummaryModal />

      {/* ── Maliyet Goruntuleme modal ──────────────────────────────────────
          Standart yeniden-kullanilabilir modal: kalem grid'inden satir kisayol
          menusu ile cagrilir; ileride Tip 1 (sabit alan) veya Tip 2 (widget)
          icindeki "Maliyetini Gor" aksiyonlari da ayni component'i kullanir. */}
      <CostViewerModal
        isOpen={!!costViewer}
        onClose={function () { setCostViewer(null) }}
        materialCode={costViewer ? costViewer.materialCode : ''}
        configCode={costViewer ? costViewer.configCode : null}
        quantity={costViewer ? costViewer.quantity : 1}
        title={costViewer
          ? ('Maliyet Görüntüleme — ' + costViewer.materialCode
              + (costViewer.materialName ? ' (' + costViewer.materialName + ')' : '')
              + (costViewer.configCode ? ' / ' + costViewer.configCode : ''))
          : ''}
      />
    </div>
  )
}
