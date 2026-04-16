import { useState, useEffect, useCallback, useMemo } from 'react'
import {
  Package, Search, ChevronLeft, ChevronRight, Plus,
} from 'lucide-react'
import MaterialCard from './MaterialCard'
import WidgetConfigPanel from './WidgetConfigPanel'
import widgetRegistry, { DEFAULT_CONFIG, getWidgetById } from './widgetRegistry'
import { loadWidgetConfig } from '../../services/widgetConfigService'

var GRID_KEY = 'logistics-material-cards'

export default function MaterialListEmbed(props) {
  var apiUrl    = props.apiUrl || '/Logistics/GetMaterialCards'
  var deleteApiUrl = props.deleteApiUrl || '/Logistics/DeleteMaterialCardJson'
  var onEdit    = props.onEdit
  var onNew     = props.onNew
  var pageSize  = props.pageSize || 20

  var [items, setItems]         = useState([])
  var [totalCount, setTotalCount] = useState(0)
  var [totalPages, setTotalPages] = useState(0)
  var [page, setPage]           = useState(1)
  var [search, setSearch]       = useState('')
  var [loading, setLoading]     = useState(true)
  var [error, setError]         = useState(null)

  // Widget config state
  var [configOpen, setConfigOpen] = useState(false)
  var [configVersion, setConfigVersion] = useState(0)

  // Her render'da degil, sadece configVersion degistiginde yeniden oku
  var userConfig = useMemo(function() {
    return loadWidgetConfig(GRID_KEY) || DEFAULT_CONFIG
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [configVersion])

  var fetchData = useCallback(function() {
    setLoading(true)
    setError(null)

    var params = new URLSearchParams({
      search: search,
      sortBy: 'MaterialCode',
      sortDirection: 'ASC',
      page: String(page),
      pageSize: String(pageSize),
    })

    fetch(apiUrl + '?' + params.toString(), { credentials: 'same-origin' })
      .then(function(resp) {
        if (resp.redirected) throw new Error('Oturum suresi dolmus olabilir. Sayfayi yenileyin.')
        if (!resp.ok) throw new Error('Sunucu hatasi: HTTP ' + resp.status)
        return resp.json()
      })
      .then(function(data) {
        setItems(data.items || [])
        setTotalCount(data.totalCount || 0)
        setTotalPages(data.totalPages || 0)
        setLoading(false)
      })
      .catch(function(err) {
        console.error('[MaterialList] Fetch error:', err)
        setError(err.message)
        setLoading(false)
      })
  }, [apiUrl, search, page, pageSize])

  useEffect(function() { fetchData() }, [fetchData])

  // Sil
  var handleDelete = useCallback(function(id) {
    fetch(deleteApiUrl + '?id=' + id, { method: 'POST', credentials: 'same-origin' })
      .then(function(resp) { return resp.json() })
      .then(function(data) {
        if (data.success) {
          fetchData()
        } else {
          alert('Silme hatasi: ' + (data.message || 'Bilinmeyen'))
        }
      })
      .catch(function(err) { alert('Silme hatasi: ' + err.message) })
  }, [deleteApiUrl, fetchData])

  // Inline toggle (simule)
  var handleToggleStatus = useCallback(function(id) {
    setItems(function(prev) {
      return prev.map(function(item) {
        if (String(item.id) === String(id)) {
          return Object.assign({}, item, { isActive: !item.isActive })
        }
        return item
      })
    })
    console.log('[MaterialList] Toggle status for id:', id, '(simule)')
  }, [])

  /**
   * API item'ini user config + widget registry kullanarak widget array'ine cevirir.
   * - Sadece user'in secili oldugu widget'lar gosterilir (visibleIds)
   * - order'a gore siralanir
   * - Renk user ayari > widget dinamik rengi > varsayilan rengi seklinde onceliklendirilir
   * - getValue(item) null donuyorsa widget gizlenir
   */
  function mapWidgets(item) {
    var visibleIds = userConfig.visibleIds || []
    var order = userConfig.order || visibleIds
    var colors = userConfig.colors || {}

    var result = []
    for (var i = 0; i < order.length; i++) {
      var id = order[i]
      if (visibleIds.indexOf(id) === -1) continue
      var w = getWidgetById(id)
      if (!w) continue

      var value = w.getValue(item)
      if (value == null || value === '') continue // null/bos gizle

      var color = colors[id]
      if (!color) {
        color = w.getDynamicColor ? w.getDynamicColor(item) : w.defaultColor
      }

      result.push({
        id: w.id,
        icon: w.icon,
        label: w.label,
        value: value,
        detail: w.getDetail ? w.getDetail(item) : '',
        color: color,
      })
    }
    return result
  }

  return (
    <div className="bg-mesh h-full flex flex-col">

      {/* Header */}
      <div className="flex items-center gap-4 px-5 py-3 border-b border-slate-200/60 dark:border-white/[0.06] flex-shrink-0">
        <div className="flex items-center gap-3 flex-shrink-0">
          <div className="w-9 h-9 rounded-xl bg-indigo-500/10 dark:bg-indigo-500/20 border border-indigo-500/20 dark:border-indigo-400/20 flex items-center justify-center">
            <Package size={17} className="text-indigo-600 dark:text-indigo-400/70" />
          </div>
          <div>
            <h1 className="text-base font-bold text-slate-800 dark:text-white/90 tracking-tight leading-tight">Malzeme Kartlari</h1>
            <p className="text-[11px] text-slate-500 dark:text-white/45 leading-tight">{totalCount} malzeme</p>
          </div>
        </div>

        <div className="flex-1 max-w-md">
          <div className="relative">
            <Search size={14} className="absolute left-3.5 top-1/2 -translate-y-1/2 text-slate-400 dark:text-white/40" />
            <input
              type="text"
              value={search}
              onChange={function(e) { setSearch(e.target.value); setPage(1) }}
              placeholder="Malzeme ara... (kod, isim)"
              className="w-full pl-10 pr-4 py-2 rounded-xl bg-white/60 dark:bg-white/[0.04] border border-slate-200 dark:border-white/[0.06] text-sm text-slate-700 dark:text-white/70 placeholder:text-slate-400 dark:placeholder:text-white/20 focus:outline-none focus:border-indigo-400/50 dark:focus:border-white/15 transition-colors"
            />
          </div>
        </div>

        {onNew && (
          <button
            onClick={onNew}
            className="flex items-center gap-2 px-4 py-2 rounded-xl bg-indigo-500 hover:bg-indigo-600 dark:bg-indigo-500/20 dark:hover:bg-indigo-500/30 border border-indigo-500 dark:border-indigo-400/20 text-sm font-semibold text-white dark:text-indigo-300 dark:hover:text-indigo-200 transition-all flex-shrink-0 shadow-sm"
          >
            <Plus size={15} />
            <span>Yeni Malzeme</span>
          </button>
        )}
      </div>

      {/* Liste */}
      <div className="flex-1 overflow-y-auto px-4 py-3 min-h-0">
        {error ? (
          <div className="text-center py-16">
            <p className="text-sm text-red-600 dark:text-red-400/70 mb-2">{error}</p>
            <button onClick={fetchData} className="text-xs text-indigo-600 dark:text-indigo-400/60 hover:text-indigo-700 dark:hover:text-indigo-300 underline">Tekrar dene</button>
          </div>
        ) : loading ? (
          <div className="flex items-center justify-center py-20">
            <div className="w-6 h-6 border-2 border-indigo-300 dark:border-indigo-400/30 border-t-indigo-600 dark:border-t-indigo-400 rounded-full animate-spin" />
          </div>
        ) : items.length === 0 ? (
          <div className="text-center py-20">
            <Package size={36} className="mx-auto text-slate-300 dark:text-white/30 mb-3" />
            <p className="text-sm text-slate-400 dark:text-white/45">Sonuc bulunamadi</p>
          </div>
        ) : (
          <div className="flex flex-col gap-2">
            {items.map(function(item) {
              return (
                <MaterialCard
                  key={item.id}
                  materialId={String(item.id)}
                  materialCode={item.materialCode || ''}
                  materialName={item.materialName || ''}
                  description={item.materialDescription || ''}
                  status={item.isActive ? 'active' : 'passive'}
                  imageUrl={null}
                  widgets={mapWidgets(item)}
                  onEdit={onEdit ? function() { onEdit(item.id) } : undefined}
                  onDelete={function() { handleDelete(item.id) }}
                  onToggleStatus={handleToggleStatus}
                  onOpenConfig={function() { setConfigOpen(true) }}
                />
              )
            })}
          </div>
        )}
      </div>

      {/* Pager */}
      {totalPages > 1 && (
        <div className="flex items-center justify-between px-5 py-2.5 border-t border-slate-200/60 dark:border-white/[0.06] flex-shrink-0">
          <button
            onClick={function() { setPage(function(p) { return Math.max(1, p - 1) }) }}
            disabled={page <= 1}
            className="p-1.5 rounded-lg hover:bg-slate-100 dark:hover:bg-white/5 disabled:opacity-20 transition-colors"
          >
            <ChevronLeft size={14} className="text-slate-500 dark:text-white/40" />
          </button>
          <div className="flex items-center gap-1">
            {Array.from({ length: Math.min(totalPages, 7) }, function(_, i) {
              var pageNum
              if (totalPages <= 7) pageNum = i + 1
              else if (page <= 4) pageNum = i + 1
              else if (page >= totalPages - 3) pageNum = totalPages - 6 + i
              else pageNum = page - 3 + i
              return (
                <button
                  key={pageNum}
                  onClick={function() { setPage(pageNum) }}
                  className={'min-w-[28px] h-7 rounded-lg text-[11px] font-semibold transition-all ' +
                    (page === pageNum
                      ? 'bg-indigo-500/15 dark:bg-indigo-500/20 text-indigo-700 dark:text-indigo-300 border border-indigo-400/30 dark:border-indigo-400/20'
                      : 'text-slate-500 dark:text-white/30 hover:text-slate-700 dark:hover:text-white/50 hover:bg-slate-100 dark:hover:bg-white/5')
                  }
                >
                  {pageNum}
                </button>
              )
            })}
          </div>
          <button
            onClick={function() { setPage(function(p) { return Math.min(totalPages, p + 1) }) }}
            disabled={page >= totalPages}
            className="p-1.5 rounded-lg hover:bg-slate-100 dark:hover:bg-white/5 disabled:opacity-20 transition-colors"
          >
            <ChevronRight size={14} className="text-slate-500 dark:text-white/40" />
          </button>
        </div>
      )}

      {/* Widget Config Panel — tek panel, liste bazli */}
      <WidgetConfigPanel
        isOpen={configOpen}
        onClose={function() { setConfigOpen(false) }}
        gridKey={GRID_KEY}
        onSaved={function() { setConfigVersion(function(v) { return v + 1 }) }}
      />
    </div>
  )
}
