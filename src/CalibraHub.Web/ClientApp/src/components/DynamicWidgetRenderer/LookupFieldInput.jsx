/**
 * LookupFieldInput — Widget-bazli (DataType=lookup) rehber input bileseni.
 *
 * Birlesik `LookupCard` kullanir — bu sayede sabit alanlar (Tip 1) ve
 * widget alanlari (Tip 2) ayni gorunume sahip olur. Display name kart icinde
 * gosterilmez, caller (DynamicWidgetRenderer) yan tarafta ayri bir element
 * ile gosterir.
 *
 * Modal davranis ozelliklerinin tamami (debounced arama, infinite scroll,
 * Excel-tarzi kolon filtre popover'i, cift-tiklama secim, ESC, vb.) ortak
 * `GuideLookupModal` icindedir.
 *
 * Props:
 *   widgetId, guideCode,
 *   value (kayitli value), display (cozumlenmis gosterim — kart goz ardi eder),
 *   guideConfig ({ viewCode, columns:[{name,label,visible,distinct}], constraint }),
 *   constraints (string — DynamicWidgetRenderer'da {w_xxx} resolve edilmis),
 *   onPick(value, display),
 *   classPrefix ('mce' | 'ca' | 'sqe')   — kart 'size' variantina map'lenir
 */
import { useState, useCallback, useMemo } from 'react'
import GuideLookupModal from '../GuideLookup/GuideLookupModal'
import LookupCard from '../GuideLookup/LookupCard'
import { adaptGuideConfig, extractValueDisplay } from '../GuideLookup/guideLookupAdapters'

function parseGuideConfig(raw) {
  if (!raw) return null
  if (typeof raw === 'object') return raw
  try {
    var p = JSON.parse(raw)
    return (p && typeof p === 'object') ? p : null
  } catch (e) { return null }
}

// classPrefix → kart boyut varianti
function prefixToSize(prefix) {
  if (prefix === 'sqe') return 'sm'
  if (prefix === 'ca')  return 'md'
  return 'md'
}

export default function LookupFieldInput(props) {
  var widgetId    = props.widgetId
  var guideCode   = props.guideCode || ''
  var value       = props.value || ''
  var onPick      = props.onPick
  var constraints = props.constraints || null
  var prefix      = props.classPrefix || 'mce'

  var configObj = useMemo(function () { return parseGuideConfig(props.guideConfig) }, [props.guideConfig])
  var staticConstraint = configObj && configObj.constraint ? configObj.constraint : null

  var [modalOpen, setModalOpen] = useState(false)

  var columnsAdapter = useCallback(function (schemaCols) {
    return adaptGuideConfig(configObj, schemaCols)
  }, [configObj])

  function openModal() {
    if (!guideCode) return
    setModalOpen(true)
  }
  function closeModal() { setModalOpen(false) }

  function handlePick(row) {
    var override = extractValueDisplay(configObj)
    var val = (override.valueColumn && row.cells && row.cells[override.valueColumn] != null)
      ? String(row.cells[override.valueColumn])
      : (row.value || '')
    var disp = (override.displayColumn && row.cells && row.cells[override.displayColumn] != null)
      ? String(row.cells[override.displayColumn])
      : (row.display || '')
    if (onPick) onPick(val, disp)
  }

  function handleManualChange(newVal) {
    // Manuel yazim — display bilinmiyor, parent'ta cozumleme baska bir mekanizma
    // ile yapilabilir; biz simdilik bos display gondeririz.
    if (onPick) onPick(newVal, '')
  }

  function handleClear() {
    if (onPick) onPick('', '')
  }

  var placeholder = guideCode ? 'Kod girin veya rehberden seçin…' : 'Rehber tanımlı değil'

  var wrapClass = 'wf-lookup-group' + (props.isInvalid ? ' is-invalid' : '')
  return (
    <div className={wrapClass}>
      <LookupCard
        value={value}
        display={props.display || ''}
        onChange={handleManualChange}
        onOpen={openModal}
        onClear={handleClear}
        disabled={!guideCode}
        size={prefixToSize(prefix)}
        placeholder={placeholder}
        inputProps={{
          id: 'dyn_' + widgetId,
          'data-widget-code': widgetId,
        }}
      />

      <GuideLookupModal
        guideCode={guideCode}
        columnsAdapter={columnsAdapter}
        open={modalOpen}
        onClose={closeModal}
        onPick={handlePick}
        staticConstraint={staticConstraint}
        runtimeConstraint={constraints}
      />
    </div>
  )
}
