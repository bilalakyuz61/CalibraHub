/**
 * WidgetRegistryCard — Tek widget kart (Admin list satiri)
 *
 * Layout:
 *   [icon kutusu]  fieldLabel                 [toggle] [edit] [pasife al]
 *                  fieldKey · DataType   🔒 VIEW_KEY
 */
import { motion } from 'framer-motion'
import { Pencil, Trash2, Lock, LayoutList } from 'lucide-react'
import { resolveIcon, resolveColor } from '../CalibraSmartBoard/DynamicWidgetFactory'
import { DATA_TYPES } from './DataTypeDropdown'

/* DataType key (Faz B lowercase) → icon/color/label map.
   Legacy Pascal case degerleri (String, Integer, ...) da geriye donuk desteklenir. */
function getTypeDescriptor(dataType) {
  var dt = (dataType || '').toLowerCase()
  // Legacy normalize: 'String'→'text', 'Integer/Decimal'→'numeric', 'Date','Boolean','Dropdown','MultiSelect'
  var legacyMap = {
    'string': 'text',
    'integer': 'numeric',
    'decimal': 'numeric',
    'date': 'date',
    'boolean': 'boolean',
    'dropdown': 'dropdown',
    'multiselect': 'multi-select',
    'multi-select': 'multi-select',
    'text': 'text',
    'numeric': 'numeric',
    'group': 'text', // grup satirlari liste'de field olarak gecmemeli, ama fallback
  }
  var normalized = legacyMap[dt] || dt
  var t = DATA_TYPES.find(function(x) { return x.value === normalized })
  return t || { value: 'text', label: 'Metin', icon: 'FileText', color: 'slate' }
}

export default function WidgetRegistryCard(props) {
  var field = props.field
  var onEdit = props.onEdit
  var onToggle = props.onToggle
  var onDelete = props.onDelete
  var onPlainFieldToggle = props.onPlainFieldToggle
  var onListableToggle = props.onListableToggle
  var isSaving = props.isSaving
  var isEditing = props.isEditing

  if (!field) return null

  var type = getTypeDescriptor(field.dataType)
  var Icon = resolveIcon(type.icon)
  var palette = resolveColor(type.color)
  var isSystem = field.isSystem === true
  var isActive = field.isActive !== false
  var hasPermission = !!(field.permissionKey && field.permissionKey.trim())
  var isPlainField = field.isPlainField === true
  var isRequired = field.isRequired === true

  return (
    <motion.div
      layout
      initial={{ opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      exit={{ opacity: 0, y: -8 }}
      transition={{ duration: 0.28, ease: [0.23, 1, 0.32, 1] }}
      className={
        'glass rounded-2xl overflow-hidden transition-all duration-300 ' +
        (isEditing
          ? 'ring-2 ring-indigo-400/50 shadow-[0_8px_40px_rgba(99,102,241,0.25)]'
          : 'hover:shadow-[0_8px_40px_rgba(0,0,0,0.22)] shadow-[0_2px_12px_rgba(0,0,0,0.1)]') +
        (isActive ? '' : ' opacity-60')
      }
    >
      <div className="flex items-center gap-3 px-4 py-3">

        {/* Icon kutusu */}
        <div
          className="w-11 h-11 rounded-xl flex items-center justify-center flex-shrink-0"
          style={{ background: palette.bg, border: '1px solid ' + palette.border }}
        >
          <Icon size={18} style={{ color: palette.icon }} strokeWidth={1.8} />
        </div>

        {/* Orta: Label + meta */}
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2 mb-0.5">
            <h4 className="text-sm font-bold text-slate-800 dark:text-white/90 truncate">
              {field.label || field.fieldLabel || field.widgetCode || field.fieldKey}
            </h4>
            {isSystem && (
              <span
                className="text-[9px] font-bold px-1.5 py-px rounded-full uppercase tracking-wider flex-shrink-0"
                style={{
                  background: 'rgba(245, 158, 11, 0.15)',
                  border: '1px solid rgba(245, 158, 11, 0.3)',
                  color: '#fcd34d',
                }}
                title="Sistem alani"
              >
                Sistem
              </span>
            )}
            {!isActive && (
              <span
                className="text-[9px] font-bold px-1.5 py-px rounded-full uppercase tracking-wider flex-shrink-0"
                style={{
                  background: 'rgba(100, 116, 139, 0.15)',
                  border: '1px solid rgba(100, 116, 139, 0.3)',
                  color: '#cbd5e1',
                }}
              >
                Pasif
              </span>
            )}
            {isPlainField && (
              <span
                className="text-[9px] font-bold px-1.5 py-px rounded-full uppercase tracking-wider flex-shrink-0"
                style={{
                  background: 'rgba(71, 85, 105, 0.15)',
                  border: '1px solid rgba(71, 85, 105, 0.35)',
                  color: '#94a3b8',
                }}
                title="Sadece alan olarak gösterilir (grup kutusu yok)"
              >
                Düz
              </span>
            )}
            {isRequired && (
              <span
                className="text-[9px] font-bold px-1.5 py-px rounded-full uppercase tracking-wider flex-shrink-0"
                style={{
                  background: 'rgba(239, 68, 68, 0.12)',
                  border: '1px solid rgba(239, 68, 68, 0.3)',
                  color: '#fca5a5',
                }}
                title="Zorunlu alan — boş bırakılamaz"
              >
                Zorunlu
              </span>
            )}
          </div>
          <div className="flex items-center gap-2 text-[11px]">
            <span className="font-mono text-slate-500 dark:text-white/50 truncate">
              {field.widgetCode || field.fieldKey}
            </span>
            <span className="text-slate-400 dark:text-white/40">·</span>
            <span style={{ color: palette.text }} className="font-semibold">
              {type.label}
            </span>
            {field.minLength > 0 && (
              <>
                <span className="text-slate-400 dark:text-white/40">·</span>
                <span className="font-mono text-slate-500 dark:text-white/50" title={'Minimum ' + field.minLength + ' karakter'}>
                  {'≥' + field.minLength + 'k'}
                </span>
              </>
            )}
            {field.expectedLength > 0 && (
              <>
                <span className="text-slate-400 dark:text-white/40">·</span>
                <span className="font-mono text-slate-500 dark:text-white/50" title={'Tam ' + field.expectedLength + ' karakter olmalı'}>
                  {'=' + field.expectedLength + 'k'}
                </span>
              </>
            )}
            {field.maxLength > 0 && (
              <>
                <span className="text-slate-400 dark:text-white/40">·</span>
                <span
                  className="font-mono text-slate-500 dark:text-white/50"
                  title={'Maksimum ' + field.maxLength + ' karakter'}
                >
                  {'≤' + field.maxLength}
                </span>
              </>
            )}
            {field.minValue != null && (
              <>
                <span className="text-slate-400 dark:text-white/40">·</span>
                <span className="font-mono text-slate-500 dark:text-white/50" title={'Minimum değer: ' + field.minValue}>
                  {'≥' + field.minValue}
                </span>
              </>
            )}
            {field.maxValue != null && (
              <>
                <span className="text-slate-400 dark:text-white/40">·</span>
                <span className="font-mono text-slate-500 dark:text-white/50" title={'Maksimum değer: ' + field.maxValue}>
                  {'≤' + field.maxValue}
                </span>
              </>
            )}
          </div>
        </div>

        {/* Sag: Actions */}
        <div className="flex items-center gap-1 flex-shrink-0">
          {/* Toggle switch */}
          <button
            type="button"
            disabled={isSaving}
            onClick={function() { if (onToggle) onToggle(field) }}
            className={
              'relative w-10 h-5 rounded-full transition-colors mr-1 ' +
              (isActive ? 'bg-emerald-500/70' : 'bg-slate-300 dark:bg-white/10') +
              (isSaving ? ' opacity-50 cursor-wait' : ' cursor-pointer')
            }
            title={isActive ? 'Pasife al' : 'Aktif et'}
          >
            <motion.div
              className="absolute top-0.5 w-4 h-4 rounded-full bg-white shadow-sm"
              animate={{ left: isActive ? 22 : 2 }}
              transition={{ type: 'spring', stiffness: 500, damping: 30 }}
            />
          </button>

          {/* Plain field toggle — group tipi haric */}
          {String(field.dataType || '').toLowerCase() !== 'group' && (
            <button
              type="button"
              disabled={isSaving}
              onClick={function() { if (onPlainFieldToggle) onPlainFieldToggle(field) }}
              className={
                'p-2 rounded-xl transition-colors group disabled:opacity-50 ' +
                (isPlainField
                  ? 'bg-slate-200/60 dark:bg-white/10 hover:bg-slate-300/60 dark:hover:bg-white/15'
                  : 'hover:bg-slate-100 dark:hover:bg-white/5')
              }
              title={isPlainField ? 'Düz alan modu aktif — tıkla gruplu moda geç' : 'Gruplu modda — tıkla düz alan moduna geç'}
            >
              <LayoutList
                size={14}
                className={
                  'transition-colors ' +
                  (isPlainField
                    ? 'text-slate-600 dark:text-white/60'
                    : 'text-slate-400 dark:text-white/45 group-hover:text-slate-600 dark:group-hover:text-white/50')
                }
              />
            </button>
          )}

          {/* Edit */}
          <button
            type="button"
            disabled={isSaving}
            onClick={function() { if (onEdit) onEdit(field) }}
            className="p-2 rounded-xl hover:bg-slate-100 dark:hover:bg-white/5 transition-colors group disabled:opacity-50"
            title="Duzenle"
          >
            <Pencil
              size={14}
              className="text-slate-400 dark:text-white/45 group-hover:text-amber-600 dark:group-hover:text-amber-400/80 transition-colors"
            />
          </button>

          {/* Delete */}
          {isSystem ? (
            <div
              className="p-2 rounded-xl text-slate-300 dark:text-white/40"
              title="Sistem alani korunuyor"
            >
              <Lock size={13} />
            </div>
          ) : (
            <button
              type="button"
              disabled={isSaving}
              onClick={function() { if (onDelete) onDelete(field) }}
              className="p-2 rounded-xl hover:bg-red-100 dark:hover:bg-red-500/15 border border-transparent hover:border-red-400/30 transition-all group disabled:opacity-30 disabled:cursor-not-allowed"
              title="Sil"
            >
              <Trash2
                size={14}
                className="text-slate-400 dark:text-white/50 group-hover:text-red-600 dark:group-hover:text-red-400/90 transition-colors"
              />
            </button>
          )}
        </div>
      </div>
    </motion.div>
  )
}
