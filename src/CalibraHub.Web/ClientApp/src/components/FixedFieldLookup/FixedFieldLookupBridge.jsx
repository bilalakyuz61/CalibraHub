/**
 * FixedFieldLookupBridge — Sabit form alanlarina rehber lookup davranisi ekler.
 *
 * Runtime'da mountFixedFieldLookups(formCode) cagrildiginda:
 *   1) /api/field-settings/runtime/{formCode} fetch edilir
 *   2) Her binding icin DOM'da input bulunur
 *   3) DOM input gizlenir (display:none) — sadece form submit icin kalir
 *   4) Bu bilesen LookupCard'i mount eder; React state'i DOM input ile sync
 *
 * Display name (Cari Isim gibi) kart icinde GOSTERILMEZ — caller (Razor sayfasi)
 * disarida `<span id="cariIsim">` veya benzer bir element ile gosterir, fillMap
 * bunu doldurur. Boylece kullanici elle kod yapistirsa bile cozumlemenin nereye
 * gidecegi caller'da belli olur.
 *
 * Arama/secim modal'i icin birlesik `GuideLookupModal` kullanir — Tip 2
 * (widget rehberi) ile ayni davranis ve goruntu setine sahiptir.
 */
import { useState, useEffect, useRef, useCallback } from 'react'
import { Settings } from 'lucide-react'
import { guideResolve } from '../DynamicWidgetRenderer/dynamicWidgetService'
import { getRuntimeBindings } from '../../services/fieldSettingService'
import FieldSettingsForm from '../CalibraLineItemsGrid/FieldSettingsForm'
import GuideLookupModal from '../GuideLookup/GuideLookupModal'
import LookupCard from '../GuideLookup/LookupCard'
import { adaptFormatJson, extractValueDisplay } from '../GuideLookup/guideLookupAdapters'
import { resolveTokens as resolveAllTokens } from '../../utils/fieldTokens'

/**
 * fillTargets — fillMap hedeflerini sender row.cells (veya bos) ile doldurur.
 * INPUT/TEXTAREA/SELECT icin .value, diger elementler icin .textContent.
 */
function fillTargets(cells, fillMap, clear) {
  if (!fillMap) return
  Object.keys(fillMap).forEach(function (selector) {
    var colName = fillMap[selector]
    var target = document.querySelector(selector)
    if (!target) return
    var hasVal = !clear && cells && cells[colName] != null
    var newVal = hasVal ? String(cells[colName]) : ''
    var tag = (target.tagName || '').toUpperCase()
    if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') {
      target.value = newVal
      target.dispatchEvent(new Event('input', { bubbles: true }))
      target.dispatchEvent(new Event('change', { bubbles: true }))
    } else {
      target.textContent = newVal
    }
  })
}

/**
 * Tek bir sabit alan icin lookup davranisi.
 *
 * Props:
 *   inputElement  — Mevcut DOM input elemani (gizli, sadece form submit icin)
 *   fieldKey      — Alan anahtari
 *   guideCode     — Bagli rehber kodu
 *   filterJson    — Opsiyonel constraint JSON
 *   formatJson    — Opsiyonel kolon konfigurasyonu (visibleColumns/columnLabels)
 *   isRequired    — Zorunlu mu
 *   formCode      — FieldSettingsForm icin
 *   fillMap       — { '#otherSelector': 'guideColumnName' } — secim sonrasi doldur
 *   size          — 'sm' | 'md' | 'lg'
 */
/**
 * resolveFilterTokens — FilterJson icindeki tum `{#...}` token'larini runtime degerleriyle
 * replace eder. Sabit alan baglamı icin context bos — sadece DOM lookup
 * (`{#sqCustomerId}`) yoluyla cozumler. Kalem grid'inden cagrildigında
 * LineGridCell once `{#row.*}`/`{#row.combo.*}` token'larini kendi tarafinda cozer,
 * geri kalan DOM token'lari da burada (modal tarafinda) ayrica cozulebilir.
 *
 * Backend'e giden constraint hep tam-resolved olur. Token format dokumani:
 * src/CalibraHub.Web/ClientApp/src/utils/fieldTokens.js
 */
function resolveFilterTokens(filterJson) {
  return resolveAllTokens(filterJson, {})
}

export default function FixedFieldLookupBridge(props) {
  var inputEl    = props.inputElement
  var defaultGuideCode = props.guideCode
  var filterJson = props.filterJson || null

  var formCode   = props.formCode || null
  var fieldKey   = props.fieldKey || null
  var isRequired = !!props.isRequired
  var size       = props.size || 'md'
  var resolveOnBlur = props.resolveOnBlur !== false  // varsayilan: true

  var initialVal = (inputEl && inputEl.value) || ''
  var initialDisp = (inputEl && inputEl.getAttribute('data-display')) || ''

  var [value, setValue]               = useState(initialVal)
  var [display, setDisplay]           = useState(initialDisp)
  var [resolving, setResolving]       = useState(false)
  var [error, setError]               = useState(false)
  var [modalOpen, setModalOpen]       = useState(false)
  var [settingsOpen, setSettingsOpen] = useState(false)
  var [formatJson, setFormatJson]     = useState(props.formatJson || null)
  var [schemaVersion, setSchemaVersion] = useState(0)
  // FldSet binding'i admin Alan Ayarlari'ndan farkli bir view'a baglandiysa,
  // hardcoded prop guideCode'u override et. PR sonrasi: hatali bile baglansa
  // admin'in tercihine saygi gosterilir (PR 5'te tag-based soft warning eklenecek).
  var [guideCodeOverride, setGuideCodeOverride] = useState(null)
  var guideCode = guideCodeOverride || defaultGuideCode
  var lastResolvedRef = useRef(initialVal && initialDisp ? initialVal : null)

  // FieldSettingsForm column objesini mutate eder — ref ile referansi sabit tut
  var settingsColumnRef = useRef({
    key: fieldKey, label: fieldKey, formCode: formCode,
    guideCode: guideCode, filterJson: filterJson, formatJson: formatJson,
  })
  settingsColumnRef.current = {
    key: fieldKey, label: fieldKey, formCode: formCode,
    guideCode: guideCode, filterJson: filterJson, formatJson: formatJson,
  }

  // DOM input ile state senkronu — tum mutasyonlar bu fonksiyondan gecer
  var syncToDom = useCallback(function (val, disp) {
    if (!inputEl) return
    inputEl.value = val || ''
    if (val) {
      inputEl.setAttribute('data-value', val)
    } else {
      inputEl.removeAttribute('data-value')
    }
    if (disp) {
      inputEl.setAttribute('data-display', disp)
    } else {
      inputEl.removeAttribute('data-display')
    }
    inputEl.dispatchEvent(new Event('input', { bubbles: true }))
    inputEl.dispatchEvent(new Event('change', { bubbles: true }))
  }, [inputEl])

  // ── Form reset support: caller `ffl-clear` custom event dispatch ederse
  // bridge state'i temizler (DOM input.value=null tek basina React state'i
  // resetlemiyor; dis kodun bu kanalla bridge'a sinyal vermesi gerekiyor).
  useEffect(function () {
    if (!inputEl) return
    function onExternalClear() {
      setValue('')
      setDisplay('')
      setError(false)
      setResolving(false)
      lastResolvedRef.current = null
      try {
        inputEl.value = ''
        inputEl.removeAttribute('data-value')
        inputEl.removeAttribute('data-display')
      } catch (_) { /* ignore */ }
    }
    inputEl.addEventListener('ffl-clear', onExternalClear)
    return function () { inputEl.removeEventListener('ffl-clear', onExternalClear) }
  }, [inputEl])

  // ── External SET support: caller DOM input.value/data-* attribute'larini
  // programatik degistirdiyse (orn. sqLoadQuote async fetch sonrasi), bu event
  // ile bridge React state'ini DOM'dan tazeler. Mount edildikten sonra DOM'a
  // yazilan deger state'e yansimadigi icin Cari Kod input'u bos goruluyordu.
  useEffect(function () {
    if (!inputEl) return
    function onExternalSync() {
      var v  = inputEl.value || inputEl.getAttribute('data-value') || ''
      var d  = inputEl.getAttribute('data-display') || ''
      setValue(v)
      setDisplay(d)
      setError(false)
      setResolving(false)
      // Caller deger zaten resolved set ediyor → blur'da tekrar resolve etme
      if (v) lastResolvedRef.current = v
    }
    inputEl.addEventListener('ffl-sync', onExternalSync)
    return function () { inputEl.removeEventListener('ffl-sync', onExternalSync) }
  }, [inputEl])

  // ── Sayfa yuklendiginde mevcut kod icin display'i resolve et ──
  // Caller data-display'i onceden set etmisse, ona saygi gosteririz.
  useEffect(function () {
    if (!inputEl || !guideCode) return
    var currentVal = inputEl.value
    if (!currentVal) return
    if (initialDisp) return  // caller zaten doldurmus
    var alive = true
    setResolving(true)
    guideResolve(guideCode, currentVal)
      .then(function (result) {
        if (!alive) return
        if (result && result.display) {
          inputEl.setAttribute('data-display', result.display)
          inputEl.setAttribute('data-value', currentVal)
          lastResolvedRef.current = currentVal
          setDisplay(result.display)
        }
      })
      .catch(function () { /* sessizce devam */ })
      .finally(function () { if (alive) setResolving(false) })
    return function () { alive = false }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [inputEl, guideCode])

  // ── Mount/formCode degistiginde FldSet binding'i fetch et ──
  // Admin Alan Ayarlari'nda farkli bir view'a baglamissa, prop'taki hardcoded
  // guideCode override edilir. formatJson da binding'ten yuklenir.
  useEffect(function () {
    if (!formCode || !fieldKey) return
    var alive = true
    getRuntimeBindings(formCode)
      .then(function (bindings) {
        if (!alive) return
        var binding = (bindings || []).find(function (b) { return b.fieldKey === fieldKey })
        if (!binding) return
        // FormatJson her zaman uygulanir (gorunur kolonlar, label override, vs.)
        if (binding.formatJson) {
          setFormatJson(binding.formatJson)
        }
        // ViewName/GuideCode override: admin baska bir view secmisse
        var override = binding.viewName || (binding.guideCode && binding.guideCode !== '' ? binding.guideCode : null)
        if (override && override !== defaultGuideCode) {
          setGuideCodeOverride(override)
        }
      })
      .catch(function () { /* sessizce devam */ })
    return function () { alive = false }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [formCode, fieldKey])

  var columnsAdapter = useCallback(function (schemaCols) {
    return adaptFormatJson(formatJson, schemaCols)
  }, [formatJson])

  function openModal() {
    if (!guideCode) return
    setModalOpen(true)
  }
  function closeModal() {
    setModalOpen(false)
    // Modal kapaninca odagi LookupCard'in visible input'una geri al — pickRow icinde
    // degil, closeModal'de yapiyoruz cunku modal hala mount iken focus trap aktif.
    // setTimeout(0) re-render sonrasi, modal unmount edildikten sonra calisir.
    // LookupCard forwardRef kullanmadigi icin ref erisemiyoruz; data-field-key ile bul.
    setTimeout(function() {
      try {
        var node = fieldKey
          ? document.querySelector('input[data-field-key="' + String(fieldKey).replace(/"/g, '\\"') + '"]')
          : null
        if (node && typeof node.focus === 'function') node.focus()
      } catch (_) {}
    }, 0)
  }

  // Manuel yazim — kullanici karta klavyeden kod giriyor.
  // Display ve fillMap target'leri temizlenir (cozumleme blur'da yapilir).
  function handleChange(newVal) {
    setValue(newVal)
    setDisplay('')
    setError(false)
    syncToDom(newVal, '')
    fillTargets(null, props.fillMap, true)
  }

  // Blur'da: kullanici elle bir kod yapistirdiysa cozumle (rehberden secim degil).
  // Cozumlenirse display set edilir + fillMap doldurulur. Bulunamazsa error state.
  function handleBlur() {
    if (!resolveOnBlur || !guideCode) return
    var trimmed = (value || '').trim()
    if (!trimmed) { setError(false); return }
    if (lastResolvedRef.current === trimmed) return  // tekrar resolve etme
    setResolving(true)
    var alive = true
    guideResolve(guideCode, trimmed)
      .then(function (result) {
        if (!alive) return
        if (result && result.display) {
          setError(false)
          setDisplay(result.display)
          lastResolvedRef.current = trimmed
          syncToDom(trimmed, result.display)
          if (result.cells) fillTargets(result.cells, props.fillMap, false)
        } else {
          setError(true)
          setDisplay('')
          lastResolvedRef.current = null
          // Hatali kod → odak geri ver, kullanici alandan cikamasin
          forceFocusBackToCard()
        }
      })
      .catch(function () {
        if (alive) {
          setError(true)
          forceFocusBackToCard()
        }
      })
      .finally(function () { if (alive) setResolving(false) })
  }

  // Hatali kod sonrasi LookupCard'in input'una odagi geri al — kullanici alandan
  // cikip diger islemleri yapamasin (sabit alanlar form submit'i bloklayacak ama
  // yine de odak korumasi yapiyoruz UX icin).
  function forceFocusBackToCard() {
    setTimeout(function () {
      try {
        var node = fieldKey
          ? document.querySelector('input[data-field-key="' + String(fieldKey).replace(/"/g, '\\"') + '"]')
          : null
        if (node && typeof node.focus === 'function') {
          node.focus()
          if (typeof node.select === 'function') node.select()
        }
      } catch (_) { /* ignore */ }
    }, 0)
  }

  function pickRow(row) {
    var override = extractValueDisplay(formatJson)
    var val = (override.valueColumn && row.cells && row.cells[override.valueColumn] != null)
      ? String(row.cells[override.valueColumn])
      : (row.value || '')
    var disp = (override.displayColumn && row.cells && row.cells[override.displayColumn] != null)
      ? String(row.cells[override.displayColumn])
      : (row.display || '')

    setValue(val)
    setDisplay(disp)
    setError(false)
    lastResolvedRef.current = val
    syncToDom(val, disp)
    fillTargets(row.cells, props.fillMap, false)
    // Odak yonetimi closeModal'de yapiliyor (modal-mount iken focus trap aktif).
  }

  function clearValue() {
    setValue('')
    setDisplay('')
    setError(false)
    lastResolvedRef.current = null
    syncToDom('', '')
    fillTargets(null, props.fillMap, true)
  }

  // GuideLookupModal'a header'a sigdirilan ek buton — Alan Ayarlari
  var headerActions = formCode ? (
    <button
      type="button"
      onClick={function () { setSettingsOpen(true) }}
      title="Alan Ayarları"
      className="gl-settings-btn"
    >
      <Settings size={15} strokeWidth={2} />
    </button>
  ) : null

  return (
    <>
      <LookupCard
        value={value}
        display={display}
        displayInline={false}
        onChange={handleChange}
        onOpen={openModal}
        onClear={clearValue}
        required={isRequired}
        loading={resolving}
        error={error}
        size={size}
        inputProps={{
          'data-field-key': fieldKey || undefined,
          onBlur: handleBlur,
        }}
      />

      <GuideLookupModal
        guideCode={guideCode}
        columnsAdapter={columnsAdapter}
        open={modalOpen}
        onClose={closeModal}
        onPick={pickRow}
        staticConstraint={resolveFilterTokens(filterJson)}
        schemaVersion={schemaVersion}
        headerActions={headerActions}
      />

      {formCode && (
        <FieldSettingsForm
          column={settingsColumnRef.current}
          isOpen={settingsOpen}
          onClose={function () {
            setSettingsOpen(false)
            // FormatJson refresh — gorunur kolonlar/label'lar
            if (settingsColumnRef.current.formatJson !== formatJson) {
              setFormatJson(settingsColumnRef.current.formatJson)
            }
            // GuideCode/ViewName refresh — admin farkli view sectiyse override guncellenir,
            // ayni default'a dondurduyse override sifirlanir. Iframe yenileme gerekmez.
            var newCode = settingsColumnRef.current.viewName || settingsColumnRef.current.guideCode
            if (newCode && newCode !== defaultGuideCode) {
              setGuideCodeOverride(newCode)
            } else {
              setGuideCodeOverride(null)
            }
            // Schema cache'i sifirla — modal bir sonraki acilista yeni view'in schema'sini cekecek
            setSchemaVersion(function (v) { return v + 1 })
          }}
        />
      )}
    </>
  )
}
