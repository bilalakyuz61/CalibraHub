/**
 * QuickLinksWidget — Kullanicinin sectigi favori ekranlara ikon kutucuklari.
 * Tiklayinca navigateInWorkspace(url) ile workspace tab acar.
 *
 * settings.items = [{ key, label, url, icon }] (kullanici secimi).
 * Edit mode'da (veya hic secim yoksa) "Kısayol Seç" butonu → QuickLinksPickerModal.
 * Secim onSettingsChange({ items }) ile geri yazilir; Dashboard layout'a isler.
 *
 * Props (widget kontrati):
 *   { size, settings, isDark, lang, editMode, quickLinkOptions, onSettingsChange }
 */
import { useState, useEffect } from 'react'
import { Plus, Zap } from 'lucide-react'
import { resolveIcon } from '../../CalibraSmartBoard/DynamicWidgetFactory'
import { navigateInWorkspace } from '../../../utils/workspaceNav'
import QuickLinksPickerModal from './QuickLinksPickerModal'

export default function QuickLinksWidget(props) {
  var settings = props.settings || {}
  var items = Array.isArray(settings.items) ? settings.items : []
  // null = henüz yüklenmedi (filtre atla), [] = yüklendi ama yetki yok (hepsini gizle)
  var rawOptions = props.quickLinkOptions
  var options = Array.isArray(rawOptions) ? rawOptions : []
  var [pickerOpen, setPickerOpen] = useState(false)

  // 2026-06-19: WidgetFrame'in çark ikonu artık QuickLinks picker'ını açıyor.
  // WidgetGrid props.editIntent boolean'ı set ediyor — bu prop true olduğunda
  // picker'ı aç ve consumed callback ile state'i sıfırla.
  useEffect(function () {
    if (props.editIntent) {
      setPickerOpen(true)
      if (typeof props.onEditIntentConsumed === 'function') props.onEditIntentConsumed()
    }
  }, [props.editIntent])  // eslint-disable-line react-hooks/exhaustive-deps

  function openTab(url, label) {
    if (!url) return
    try {
      if (window.top && window.top.CalibraHub && typeof window.top.CalibraHub.openWorkspaceTab === 'function') {
        window.top.CalibraHub.openWorkspaceTab({ url: url, title: label || 'Yeni Sekme' })
        return
      }
    } catch (e) { /* cross-origin — fallback */ }
    navigateInWorkspace(url)
  }

  function handleApply(nextItems) {
    setPickerOpen(false)
    if (props.onSettingsChange) {
      props.onSettingsChange(Object.assign({}, settings, { items: nextItems }))
    }
  }

  // Yetki kaldırılan kısayolları gizle: options (permission-filtered) ile çapraz kontrol.
  // null = yükleniyor → filtre atla (geçici görüntü)
  // []   = yüklendi, yetki yok → hepsini gizle
  // [...] = yüklendi, yetkili seçenekler → sadece izinli olanları göster
  var optionKeySet = Array.isArray(rawOptions)
    ? new Set(rawOptions.map(function (o) { return o.key }))
    : null
  var visibleItems = optionKeySet !== null
    ? items.filter(function (it) { return optionKeySet.has(it.key) })
    : items

  var selectedKeys = items.map(function (i) { return i.key })

  return (
    <div style={{ height: '100%', display: 'flex', flexDirection: 'column', gap: 12 }}>
      {visibleItems.length === 0 ? (
        <div className="dash-widget-empty">
          <Zap size={22} strokeWidth={1.6} />
          <span>Henüz kısayol eklenmedi.</span>
          <button
            type="button"
            className="dash-tool-btn"
            style={{ marginTop: 6 }}
            onClick={function () { setPickerOpen(true) }}
          >
            <Plus size={14} /> Kısayol Seç
          </button>
        </div>
      ) : (
        <div className="dash-ql-grid">
          {visibleItems.map(function (it) {
            var Icon = resolveIcon(it.icon)
            return (
              <div
                key={it.key}
                className="dash-ql-tile"
                onClick={function () { openTab(it.url, it.label) }}
                title={it.label}
              >
                <Icon size={20} style={{ color: '#6366f1' }} />
                <span className="dash-ql-tile__label">{it.label}</span>
              </div>
            )
          })}
        </div>
      )}

      <QuickLinksPickerModal
        open={pickerOpen}
        options={options}
        selectedKeys={selectedKeys}
        onApply={handleApply}
        onClose={function () { setPickerOpen(false) }}
      />
    </div>
  )
}
