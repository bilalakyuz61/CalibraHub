/**
 * FieldSettingsForm — Standart text-field rehber özelleştirme.
 *
 * UI artık ortak `GuideCustomizationModal`'a delege ediliyor. Bu sayede
 * widget tarafıyla aynı görsellik + aynı feature seti — rehber özelleştirme
 * tek noktadan yönetilir.
 *
 * Bu wrapper iki şey yapar:
 *   1. Modal açılınca CANLI binding'i `/api/field-settings/runtime/{formCode}`
 *      üzerinden çeker — caller'in props'undan gelen column objesi save sonrası
 *      stale kalmış olabilir, taze kayıt DB'den okunur.
 *   2. Save sonrası `upsertFieldByFormCode` ile kayıt yapar; ayrıca caller'in
 *      column referansını mutate ederek ileri-uyumluluk korur (geçici).
 *
 * Props:
 *   column            : { key, label, formCode, guideCode, formatJson, filterJson }
 *   isOpen            : boolean
 *   onClose           : function()
 *   extraFieldOptions : array<{ token, label, secondary, group }>
 *     (kalem grid kolonlari + kombinasyon attribute'lari icin caller olusturur — fieldTokens.buildLineExtraOptions)
 */
import { useState, useEffect } from 'react'
import { upsertFieldByFormCode, getRuntimeBindings } from '../../services/fieldSettingService'
import GuideCustomizationModal from '../Common/GuideCustomizationModal'

/**
 * Column-like bir objeden GuideCustomizationModal'a verilecek initialConfig'i kurar.
 * `visibleColumns` ham array olarak ayrıca aktarılır (null = hepsi görünür) —
 * modal merge'i bunu authoritative kabul eder, view default'larını override eder.
 */
function buildInitialConfig(src) {
  let visibleArr = null
  let labels = {}
  let valueColumn = ''
  let displayColumn = ''
  let columnOrder = null

  if (src && src.formatJson) {
    try {
      const p = typeof src.formatJson === 'string' ? JSON.parse(src.formatJson) : src.formatJson
      visibleArr = Array.isArray(p.visibleColumns) ? p.visibleColumns : null
      labels = p.columnLabels || {}
      valueColumn = p.valueColumn || ''
      displayColumn = p.displayColumn || ''
      columnOrder = Array.isArray(p.columnOrder) ? p.columnOrder : null
    } catch { /* ignore */ }
  }

  // İsim setlerinin birleşimi — columnOrder varsa onu kullan (kullanicinin sirasini koru),
  // yoksa visibleColumns + label key'leri (eski format icin geri uyumluluk).
  const allNames = []
  const seen = new Set()
  function add(n) { if (n && !seen.has(n)) { allNames.push(n); seen.add(n) } }
  if (columnOrder) columnOrder.forEach(add)
  if (visibleArr) visibleArr.forEach(add)
  Object.keys(labels).forEach(add)

  const hintColumns = allNames.map(name => ({
    name,
    label: labels[name] || name,
    visible: visibleArr ? visibleArr.includes(name) : true,
  }))

  return {
    viewCode: (src && src.guideCode) || '',
    guideCode: (src && src.guideCode) || '',
    constraint: (src && src.filterJson) || '',
    columns: hintColumns.length > 0 ? hintColumns : null,
    visibleColumns: visibleArr,         // null → hepsi görünür; array → authoritative liste
    columnOrder: columnOrder,           // null → schema sirasi; array → kullanicinin sirasi
    valueColumn:   valueColumn || '',   // boş → modal rehber default'unu kullanır
    displayColumn: displayColumn || '', // boş → valueColumn'a fallback
  }
}

export default function FieldSettingsForm({ column, isOpen, onClose, extraFieldOptions }) {
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState(null)
  const [initialConfig, setInitialConfig] = useState(null)

  // ESC ile kapanma — CAPTURE phase'de listener: parent GuideLookupModal'in
  // dokuman-level (bubble) listener'i tetiklenmesin. stopPropagation ile bubble
  // engellenir → sadece bu form kapanir, alttaki rehber modal'i acik kalir.
  useEffect(() => {
    if (!isOpen) return undefined
    function handleEsc(e) {
      if (e.key !== 'Escape') return
      e.stopPropagation()
      e.preventDefault()
      if (typeof onClose === 'function') onClose()
    }
    document.addEventListener('keydown', handleEsc, true) // capture phase
    return () => document.removeEventListener('keydown', handleEsc, true)
  }, [isOpen, onClose])

  // Modal açıldığında: önce column'dan sync olarak başla, sonra DB'den taze çek.
  // Save sonrası caller'in props'u stale kaldığı için DB taze değer'i authoritative.
  useEffect(() => {
    if (!isOpen) { setInitialConfig(null); return }
    // 1) Stale column'dan ilk render için anlık config
    setInitialConfig(buildInitialConfig(column))
    if (!column || !column.formCode || !column.key) return
    // 2) Canlı binding'i çek, override
    let alive = true
    getRuntimeBindings(column.formCode)
      .then(bindings => {
        if (!alive) return
        const fresh = (bindings || []).find(b => b && b.fieldKey === column.key)
        if (fresh) {
          setInitialConfig(buildInitialConfig({
            guideCode: fresh.guideCode,
            filterJson: fresh.filterJson,
            formatJson: fresh.formatJson,
          }))
        }
      })
      .catch(() => { /* sessizce devam — column'daki stale veri kullanılır */ })
    return () => { alive = false }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isOpen, column && column.formCode, column && column.key])

  async function handleSaved(config) {
    setSaving(true); setError(null)
    try {
      const allCols = Array.isArray(config.columns) ? config.columns : []
      const visible = allCols.filter(c => c.visible).map(c => c.name)
      const allVisible = allCols.length === 0 || allCols.every(c => c.visible)
      const labels = {}
      allCols.forEach(c => { if (c.label && c.label.trim() && c.label.trim() !== c.name) labels[c.name] = c.label.trim() })
      // columnOrder — modal'da kullanicinin ↑/↓ ile yaptigi tam sira (gorunmez kolonlar dahil)
      const columnOrder = allCols.map(c => c.name)

      // displayColumn'i her zaman explicit kaydet — valueColumn ile esit olsa bile.
      // Aksi halde modal sonradan acildiginda default'a duser ve kullanici secimi
      // (ornek: Cari Kod alani icin display = kod) kaybolur.
      const formatJson = JSON.stringify({
        visibleColumns: allVisible ? null : visible,
        columnLabels: Object.keys(labels).length > 0 ? labels : undefined,
        columnOrder:   columnOrder.length > 0 ? columnOrder : undefined,
        valueColumn:   config.valueColumn   || undefined,
        displayColumn: config.displayColumn || undefined,
      })

      const result = await upsertFieldByFormCode({
        formCode: column.formCode,
        fieldKey: column.key,
        fieldLabel: column.label || column.key,
        // PR 2+: ViewName primary; guideCode = ViewName aliasi (back-compat).
        guideCode: config.viewName || config.viewCode || config.guideCode,
        viewName:  config.viewName || config.viewCode || config.guideCode,
        filterJson: (config.constraint && config.constraint.trim()) || null,
        isRequired: false,
        formatJson: formatJson,
      })

      setSaving(false)
      if (!result.success) {
        setError(result.message || 'Kayıt başarısız.')
        return
      }
      // Caller'in stale referansını da güncelle — sayfayı yenilemeden ikinci kez
      // açıldığında doğru değer gelsin (canlı fetch zaten çalışır, bu ek güvence).
      column.guideCode = config.viewName || config.viewCode || config.guideCode
      column.viewName  = config.viewName || config.viewCode || config.guideCode
      column.filterJson = (config.constraint && config.constraint.trim()) || null
      column.formatJson = formatJson
      onClose()
    } catch (e) {
      setSaving(false)
      setError('Kayıt hatası: ' + e.message)
    }
  }

  return (
    <GuideCustomizationModal
      isOpen={isOpen}
      onClose={onClose}
      onSaved={handleSaved}
      fieldLabel={column.label || column.key}
      initialConfig={initialConfig}
      saving={saving}
      error={error}
      extraFieldOptions={extraFieldOptions}
    />
  )
}
