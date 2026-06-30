/**
 * WidgetRegistryCard — Tek widget kart (Admin list satiri)
 *
 * Layout:
 *   [icon kutusu]  fieldLabel                 [toggle] [edit] [pasife al]
 *                  fieldKey · DataType   🔒 VIEW_KEY
 */
import { motion } from 'framer-motion'
import { Pencil, Trash2, Lock, ToggleRight, ToggleLeft } from 'lucide-react'
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
      <div className="flex items-center gap-3 px-4 py-2.5">

        {/* Icon kutusu */}
        <div
          className="w-9 h-9 rounded-lg flex items-center justify-center flex-shrink-0"
          style={{ background: palette.bg, border: '1px solid ' + palette.border }}
        >
          <Icon size={16} style={{ color: palette.icon }} strokeWidth={1.8} />
        </div>

        {/* Orta: Tek satirda label + tip + rozetler — daha kompakt gorunum.
            widgetCode kaldirildi (kullanici istegi: widget gosterimi daraltildi). */}
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2 min-w-0">
            <h4 className="text-[13px] font-bold text-slate-800 dark:text-white/90 truncate">
              {field.label || field.fieldLabel || field.widgetCode || field.fieldKey}
            </h4>
            <span className="text-slate-400 dark:text-white/30">·</span>
            <span style={{ color: palette.text }} className="text-[11px] font-semibold whitespace-nowrap">
              {type.label}
            </span>
            {field.minLength > 0 && (
              <>
                <span className="text-slate-400 dark:text-white/30">·</span>
                <span className="font-mono text-[10px] text-slate-500 dark:text-white/50 whitespace-nowrap" title={'Minimum ' + field.minLength + ' karakter'}>
                  {'≥' + field.minLength + 'k'}
                </span>
              </>
            )}
            {field.expectedLength > 0 && (
              <>
                <span className="text-slate-400 dark:text-white/30">·</span>
                <span className="font-mono text-[10px] text-slate-500 dark:text-white/50 whitespace-nowrap" title={'Tam ' + field.expectedLength + ' karakter olmalı'}>
                  {'=' + field.expectedLength + 'k'}
                </span>
              </>
            )}
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
            {/* Modern etiket tipi rozeti */}
            {field.labelStyle === 'modern' && (
              <span
                className="text-[9px] font-bold px-1.5 py-px rounded-full uppercase tracking-wider flex-shrink-0"
                style={{
                  background: 'rgba(99, 102, 241, 0.12)',
                  border: '1px solid rgba(99, 102, 241, 0.3)',
                  color: '#a5b4fc',
                }}
                title="Başlık stili: Modern (floating label)"
              >
                Modern
              </span>
            )}
          </div>
          {/* Genislik mini-preview — 24-col grid'deki span'i gorsel cubuk.
              Kart yuksekligini artirmamak icin cok ince (3px). */}
          {(function() {
            var cs = parseInt(field.colSpan, 10)
            if (isNaN(cs) || cs < 1) cs = 24
            if (cs > 24) cs = 24
            var pct = (cs / 24) * 100
            return (
              <div
                className="mt-1 flex items-center gap-1.5"
                title={'Genişlik: ' + cs + '/24 (' +
                  (cs === 24 ? 'Tam satır' :
                   cs === 18 ? '3/4' :
                   cs === 16 ? '2/3' :
                   cs === 12 ? '1/2' :
                   cs === 8  ? '1/3' :
                   cs === 6  ? '1/4' :
                   cs === 4  ? '1/6' :
                   cs === 3  ? '1/8' : cs + '/24') + ')'
                }
              >
                <div className="flex-1 h-[3px] rounded-full overflow-hidden bg-slate-200/40 dark:bg-white/[0.05]">
                  <div
                    className="h-full bg-emerald-500 dark:bg-emerald-400/80 rounded-full transition-all"
                    style={{ width: pct + '%' }}
                  />
                </div>
                <span className="font-mono text-[9px] text-slate-400 dark:text-white/35 w-8 text-right flex-shrink-0">
                  {cs}/24
                </span>
              </div>
            )
          })()}
        </div>

        {/* Sag: Actions — üç buton eşit boyut (p-2 rounded-xl, 14px ikon) */}
        <div className="flex items-center gap-1 flex-shrink-0">

          {/* Aktif/Pasif toggle */}
          <button
            type="button"
            disabled={isSaving}
            onClick={function() { if (onToggle) onToggle(field) }}
            className={
              'p-2 rounded-xl border border-transparent transition-all group disabled:opacity-50 disabled:cursor-wait ' +
              (isActive
                ? 'hover:bg-emerald-100 dark:hover:bg-emerald-500/15 hover:border-emerald-400/30'
                : 'hover:bg-slate-100 dark:hover:bg-white/5')
            }
            title={isActive ? 'Pasife al' : 'Aktif et'}
          >
            {isActive
              ? <ToggleRight size={14} className="text-emerald-500 dark:text-emerald-400/80 transition-colors" />
              : <ToggleLeft  size={14} className="text-slate-400 dark:text-white/35 transition-colors" />
            }
          </button>

          {/* Düzenle */}
          <button
            type="button"
            disabled={isSaving}
            onClick={function() { if (onEdit) onEdit(field) }}
            className="p-2 rounded-xl border border-transparent hover:bg-slate-100 dark:hover:bg-white/5 transition-colors group disabled:opacity-50"
            title="Düzenle"
          >
            <Pencil
              size={14}
              className="text-slate-400 dark:text-white/45 group-hover:text-amber-600 dark:group-hover:text-amber-400/80 transition-colors"
            />
          </button>

          {/* Sil */}
          {isSystem ? (
            <div
              className="p-2 rounded-xl text-slate-300 dark:text-white/40"
              title="Sistem alani korunuyor"
            >
              <Lock size={14} />
            </div>
          ) : (
            <button
              type="button"
              disabled={isSaving}
              onClick={function() { if (onDelete) onDelete(field) }}
              className="p-2 rounded-xl border border-transparent hover:bg-red-100 dark:hover:bg-red-500/15 hover:border-red-400/30 transition-all group disabled:opacity-30 disabled:cursor-not-allowed"
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
