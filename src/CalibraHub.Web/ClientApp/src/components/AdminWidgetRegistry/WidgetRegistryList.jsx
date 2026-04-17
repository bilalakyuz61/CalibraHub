/**
 * WidgetRegistryList — Sag kolon: Widget'lari grup adina gore
 * katlanabilir bolumlere ayirir.
 *
 * Her grup bir <details> block'u olarak acilir; header'da grup etiketi +
 * kayit sayisi. "Genel" bolumu groupId=null olan field'lar icin otomatik.
 * Acik/kapali durum localStorage'da tutulur (reload sonrasi ayni pozisyon).
 *
 * Props:
 *   fields    [{id, fieldKey, fieldLabel, groupId, ...}]
 *   groups    [{id, groupKey, groupLabel, displayOrder}]
 *   onEdit, onToggle, onDelete, editingId, savingId — WidgetRegistryCard'a forward
 */
import { useState, useEffect, useMemo } from 'react'
import { AnimatePresence } from 'framer-motion'
import { LayoutGrid, ChevronDown, Layers, Trash2, ArrowUp, ArrowDown } from 'lucide-react'
import WidgetRegistryCard from './WidgetRegistryCard'

var STORAGE_KEY = 'calibra.admin.widgetRegistry.openGroups'

function loadOpenGroups() {
  try {
    var raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) return null
    return JSON.parse(raw)
  } catch (e) { return null }
}

function saveOpenGroups(map) {
  try { localStorage.setItem(STORAGE_KEY, JSON.stringify(map)) } catch (e) { /* quota */ }
}

export default function WidgetRegistryList(props) {
  var fields = Array.isArray(props.fields) ? props.fields : []
  var groups = Array.isArray(props.groups) ? props.groups : []
  var onEdit = props.onEdit
  var onToggle = props.onToggle
  var onDelete = props.onDelete
  var onPlainFieldToggle = props.onPlainFieldToggle
  var onListableToggle = props.onListableToggle
  var onReorder = props.onReorder
  var editingId = props.editingId
  var savingId = props.savingId
  var searchQuery = (props.searchQuery || '').trim().toLowerCase()

  // Arama aktifse field'lari filtrele
  var filteredFields = useMemo(function() {
    if (!searchQuery) return fields
    return fields.filter(function(f) {
      var label = (f.label || f.fieldLabel || '').toLowerCase()
      var code  = (f.widgetCode || f.fieldKey || '').toLowerCase()
      return label.indexOf(searchQuery) >= 0 || code.indexOf(searchQuery) >= 0
    })
  }, [fields, searchQuery])

  var total = fields.length
  var filteredTotal = filteredFields.length

  // Field'lari gruplara bucket'la — Faz B: parentId kullan.
  // Hem eski (groupId) hem yeni (parentId) field aliasini destekler.
  // searchQuery aktifse filteredFields kullanilir.
  var sections = useMemo(function () {
    var sourceFields = searchQuery ? filteredFields : fields
    var bucketByGroupId = {}
    groups.forEach(function (g) {
      bucketByGroupId[g.id] = { group: g, fields: [] }
    })
    var ungrouped = []
    sourceFields.forEach(function (f) {
      var parent = f.parentId !== undefined ? f.parentId : f.groupId
      if (parent && bucketByGroupId[parent]) {
        bucketByGroupId[parent].fields.push(f)
      } else {
        ungrouped.push(f)
      }
    })
    // Gruplari sortOrder/displayOrder'a gore sirala.
    // Arama aktifse bos gruplari gizle; arama yoksa goster
    // (bos grup da admin icin anlamli — "sonradan widget eklerim")
    var sorted = groups
      .slice()
      .sort(function (a, b) {
        var ao = a.sortOrder != null ? a.sortOrder : (a.displayOrder || 0)
        var bo = b.sortOrder != null ? b.sortOrder : (b.displayOrder || 0)
        return ao - bo
      })
      .map(function (g) { return bucketByGroupId[g.id] })
      .filter(function (s) {
        if (s == null) return false
        if (searchQuery && s.fields.length === 0) return false   // arama aktifken bos gruplar gizle
        return true
      })

    // Genel bolumunu sona ekle (varsa)
    if (ungrouped.length > 0) {
      sorted.push({
        group: { id: '__ungrouped', groupKey: '__ungrouped', groupLabel: 'Genel' },
        fields: ungrouped,
      })
    }
    return sorted
  }, [fields, filteredFields, groups, searchQuery])

  // Open/closed state — localStorage'dan hydrate et, sonrasinda state'te tut
  var [openMap, setOpenMap] = useState(function () {
    var stored = loadOpenGroups()
    return stored || {}
  })

  // Yeni grup gorunduginde default olarak acik say
  useEffect(function () {
    setOpenMap(function (prev) {
      var next = Object.assign({}, prev)
      var changed = false
      sections.forEach(function (s) {
        if (next[s.group.id] === undefined) {
          next[s.group.id] = true
          changed = true
        }
      })
      return changed ? next : prev
    })
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sections.length])

  function toggleGroup(groupId) {
    setOpenMap(function (prev) {
      var next = Object.assign({}, prev, {})
      next[groupId] = !prev[groupId]
      saveOpenGroups(next)
      return next
    })
  }

  return (
    <div className="flex flex-col h-full min-h-0">
      <div className="flex-1 overflow-y-auto min-h-0 pr-1">
        {total === 0 && sections.length === 0 ? (
          <div className="flex flex-col items-center justify-center py-20 text-center">
            <LayoutGrid size={36} className="text-slate-300 dark:text-white/30 mb-3" />
            <p className="text-sm text-slate-400 dark:text-white/45">Henüz widget tanımlanmamış</p>
            <p className="text-[11px] text-slate-400 dark:text-white/40 mt-1">
              Sol formdan yeni widget ekleyin
            </p>
          </div>
        ) : searchQuery && filteredTotal === 0 ? (
          <div className="flex flex-col items-center justify-center py-20 text-center">
            <LayoutGrid size={36} className="text-slate-300 dark:text-white/30 mb-3" />
            <p className="text-sm text-slate-400 dark:text-white/45">Sonuç bulunamadı</p>
            <p className="text-[11px] text-slate-400 dark:text-white/40 mt-1">
              "<span className="font-mono">{searchQuery}</span>" ile eşleşen widget yok
            </p>
          </div>
        ) : (
          <div className="flex flex-col gap-3">
            {searchQuery && (
              <div className="text-[10px] text-slate-500 dark:text-white/50 px-1 pb-0.5">
                {filteredTotal} / {total} widget eşleşti
              </div>
            )}
            {sections.map(function (section) {
              var g = section.group
              var isOpen = openMap[g.id] !== false // default acik
              return (
                <div
                  key={g.id}
                  className={'rounded-xl border overflow-hidden ' + (g.isActive === false
                    ? 'border-slate-200/40 dark:border-white/[0.04] bg-white/20 dark:bg-white/[0.01] opacity-60'
                    : 'border-slate-200/70 dark:border-white/[0.06] bg-white/40 dark:bg-white/[0.02]')}
                >
                  {/* Grup header — tiklanabilir div (delete butonu nested button olamaz) */}
                  <div
                    role="button"
                    tabIndex={0}
                    onClick={function () { toggleGroup(g.id) }}
                    onKeyDown={function (e) { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); toggleGroup(g.id) } }}
                    className="w-full flex items-center gap-2.5 px-3 py-2.5 text-left hover:bg-slate-50/70 dark:hover:bg-white/[0.03] transition-colors cursor-pointer select-none"
                  >
                    <div
                      className="w-6 h-6 rounded-lg flex items-center justify-center flex-shrink-0"
                      style={{
                        background: g.id === '__ungrouped'
                          ? 'rgba(100,116,139,0.12)'
                          : g.isActive === false
                            ? 'rgba(100,116,139,0.10)'
                            : 'rgba(20,184,166,0.12)',
                        border: '1px solid ' + (g.id === '__ungrouped'
                          ? 'rgba(100,116,139,0.25)'
                          : g.isActive === false
                            ? 'rgba(100,116,139,0.20)'
                            : 'rgba(20,184,166,0.25)'),
                      }}
                    >
                      <Layers
                        size={12}
                        style={{ color: g.id === '__ungrouped' || g.isActive === false ? '#94a3b8' : '#2dd4bf' }}
                        strokeWidth={1.8}
                      />
                    </div>
                    <span className="flex-1 text-[12px] font-bold uppercase tracking-wider text-slate-700 dark:text-white/80 truncate">
                      {g.groupLabel}
                    </span>
                    {g.isActive === false && (
                      <span className="text-[9px] font-semibold text-slate-400 dark:text-white/45 bg-slate-100 dark:bg-white/[0.04] px-1.5 py-0.5 rounded-full uppercase tracking-wide flex-shrink-0">
                        Pasif
                      </span>
                    )}
                    <span className="text-[10px] font-semibold text-slate-400 dark:text-white/50 bg-slate-100 dark:bg-white/[0.04] px-2 py-0.5 rounded-full">
                      {section.fields.length}
                    </span>
                    {/* Grup sil butonu — sadece gercek gruplar (Genel sanal bucket haric) */}
                    {g.id !== '__ungrouped' && onDelete && (
                      <button
                        type="button"
                        onClick={function (e) {
                          e.stopPropagation()
                          onDelete({ id: g.id, label: g.groupLabel, widgetCode: g.groupKey, dataType: 'group' })
                        }}
                        className="w-6 h-6 flex items-center justify-center rounded-md text-slate-400 hover:text-red-500 hover:bg-red-500/10 dark:hover:bg-red-500/15 transition-colors flex-shrink-0"
                        title="Grubu sil (icinde widget varsa izin verilmez)"
                      >
                        <Trash2 size={12} strokeWidth={2} />
                      </button>
                    )}
                    <ChevronDown
                      size={15}
                      className={
                        'text-slate-400 dark:text-white/50 transition-transform duration-200 flex-shrink-0 ' +
                        (isOpen ? 'rotate-0' : '-rotate-90')
                      }
                      strokeWidth={2}
                    />
                  </div>

                  {/* Grup body — katlanir */}
                  {isOpen && (
                    <div className="p-2 pt-0 flex flex-col gap-1.5 border-t border-slate-200/70 dark:border-white/[0.06]">
                      <div className="h-2" />
                      <AnimatePresence>
                        {section.fields
                          .slice()
                          .sort(function (a, b) {
                            var ao = a.sortOrder != null ? a.sortOrder : 0
                            var bo = b.sortOrder != null ? b.sortOrder : 0
                            return ao - bo
                          })
                          .map(function (field, idx, arr) {
                          var fid = field.id || field.widgetCode || field.fieldKey
                          return (
                            <div key={fid} className="flex items-start gap-1">
                              <div className="flex flex-col gap-0.5 pt-2.5 flex-shrink-0">
                                <button
                                  type="button"
                                  disabled={idx === 0}
                                  onClick={function () { if (onReorder) onReorder(field, arr[idx - 1]) }}
                                  className="w-5 h-5 flex items-center justify-center rounded text-slate-400 hover:text-indigo-500 hover:bg-indigo-500/10 dark:hover:bg-indigo-500/15 transition-colors disabled:opacity-20 disabled:pointer-events-none"
                                  title="Yukarı taşı"
                                >
                                  <ArrowUp size={11} strokeWidth={2.5} />
                                </button>
                                <button
                                  type="button"
                                  disabled={idx === arr.length - 1}
                                  onClick={function () { if (onReorder) onReorder(field, arr[idx + 1]) }}
                                  className="w-5 h-5 flex items-center justify-center rounded text-slate-400 hover:text-indigo-500 hover:bg-indigo-500/10 dark:hover:bg-indigo-500/15 transition-colors disabled:opacity-20 disabled:pointer-events-none"
                                  title="Aşağı taşı"
                                >
                                  <ArrowDown size={11} strokeWidth={2.5} />
                                </button>
                              </div>
                              <div className="flex-1 min-w-0">
                                <WidgetRegistryCard
                                  field={field}
                                  onEdit={onEdit}
                                  onToggle={onToggle}
                                  onDelete={onDelete}
                                  onPlainFieldToggle={onPlainFieldToggle}
                                  onListableToggle={onListableToggle}
                                  isEditing={editingId && editingId === fid}
                                  isSaving={savingId && savingId === fid}
                                />
                              </div>
                            </div>
                          )
                        })}
                      </AnimatePresence>
                    </div>
                  )}
                </div>
              )
            })}
          </div>
        )}
      </div>
    </div>
  )
}
