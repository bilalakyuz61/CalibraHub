/**
 * SmartBoard — Generic entity list container with server-side pagination.
 *
 * Props:
 *   {
 *     boardKey:  'logistics-material-cards',  // localStorage scope
 *     title:     'Malzeme Kartlari',
 *     subtitle:  '4 kayit',
 *     icon:      'Package',
 *     iconColor: 'indigo',
 *     actions:   [ { id, label, icon, variant, url } ],
 *     entities:  [ { ...SmartCardProps, widgets: [master list from C#] } ],
 *     emptyText: 'Sonuc bulunamadi',
 *     // Pagination (optional — omit for client-only mode):
 *     apiUrl:     '/Finance/GetContactAccountsPage',
 *     totalCount: 300000,
 *     pageSize:   50,
 *   }
 */
import { useState, useMemo, useEffect, useCallback, useRef } from 'react'
import { Search, Settings2, Loader2, ChevronDown } from 'lucide-react'
import SmartCard from './SmartCard'
import SmartBoardConfigPanel from './SmartBoardConfigPanel'
import { resolveIcon, resolveColor } from './DynamicWidgetFactory'
import { loadWidgetConfig } from '../../services/widgetConfigService'
import { navigateInWorkspace } from '../../utils/workspaceNav'

export default function SmartBoard(props) {
  var boardKey = props.boardKey || 'default-board'
  var title = props.title || ''
  var iconHint = props.icon || 'CircleDot'
  var iconColor = props.iconColor || 'indigo'
  var actions = Array.isArray(props.actions) ? props.actions : []
  var initialEntities = Array.isArray(props.entities) ? props.entities : []
  var emptyText = props.emptyText || 'Kayit bulunamadi'
  var searchable = props.searchable !== false
  var searchPlaceholder = props.searchPlaceholder || 'Ara...'

  // Pagination props
  var apiUrl = props.apiUrl || null
  var initialTotalCount = props.totalCount || 0
  var pageSize = props.pageSize || 50
  var isPaginated = !!apiUrl

  var HeaderIcon = resolveIcon(iconHint)
  var headerPalette = resolveColor(iconColor)

  var [search, setSearch] = useState('')
  var [configOpen, setConfigOpen] = useState(false)
  var [userConfig, setUserConfig] = useState(null)

  // Pagination state
  var [entities, setEntities] = useState(initialEntities)
  var [totalCount, setTotalCount] = useState(initialTotalCount)
  var [currentPage, setCurrentPage] = useState(1)
  var [loading, setLoading] = useState(false)
  var [hasMore, setHasMore] = useState(isPaginated && initialEntities.length < initialTotalCount)
  var [searchQuery, setSearchQuery] = useState('') // debounced + committed search
  var searchTimerRef = useRef(null)
  var sentinelRef = useRef(null)

  // Theme detection
  var [isDark, setIsDark] = useState(function () {
    if (typeof document === 'undefined') return true
    return document.body.classList.contains('app-theme-dark') ||
           document.documentElement.classList.contains('dark')
  })
  useEffect(function () {
    function sync() {
      setIsDark(
        document.body.classList.contains('app-theme-dark') ||
        document.documentElement.classList.contains('dark')
      )
    }
    sync()
    var obs = new MutationObserver(sync)
    obs.observe(document.body, { attributes: true, attributeFilter: ['class'] })
    obs.observe(document.documentElement, { attributes: true, attributeFilter: ['class'] })
    return function () { obs.disconnect() }
  }, [])

  useEffect(function () {
    var cfg = loadWidgetConfig(boardKey)
    setUserConfig(cfg)
  }, [boardKey])

  // Master widget list
  var masterWidgets = useMemo(function () {
    if (Array.isArray(props.masterWidgets)) {
      return props.masterWidgets.map(function (w) {
        return {
          id: w.id, dbId: w.dbId, type: w.type || 'data',
          dataType: w.dataType, icon: w.icon, label: w.label || w.id,
          color: w.color, isPlainField: w.isPlainField,
        }
      })
    }
    if (entities.length === 0) return []
    var seen = {}
    var master = []
    entities.forEach(function (ent) {
      if (!Array.isArray(ent.widgets)) return
      ent.widgets.forEach(function (w) {
        if (!w || !w.id) return
        if (!seen[w.id]) {
          seen[w.id] = true
          master.push({
            id: w.id, type: w.type || 'data', dataType: w.dataType,
            icon: w.icon, label: w.label || w.id, color: w.color,
          })
        }
      })
    })
    return master
  }, [entities, props.masterWidgets])

  // ── Fetch page from API ──
  var fetchPage = useCallback(function (page, searchTerm, append) {
    if (!apiUrl || loading) return
    setLoading(true)
    var url = apiUrl + '?page=' + page + '&pageSize=' + pageSize
    if (searchTerm) url += '&search=' + encodeURIComponent(searchTerm)

    fetch(url, { credentials: 'same-origin' })
      .then(function (r) { return r.json() })
      .then(function (data) {
        if (data.error) { console.error('[SmartBoard] API error:', data.error); return }
        var newEntities = Array.isArray(data.entities) ? data.entities : []
        var total = data.totalCount || 0

        if (append) {
          setEntities(function (prev) { return prev.concat(newEntities) })
        } else {
          setEntities(newEntities)
        }
        setTotalCount(total)
        setCurrentPage(page)
        var loadedCount = append ? (page * pageSize) : newEntities.length
        setHasMore(loadedCount < total && newEntities.length > 0)
      })
      .catch(function (err) { console.error('[SmartBoard] fetch error:', err) })
      .finally(function () { setLoading(false) })
  }, [apiUrl, pageSize, loading])

  // ── Debounced search → server ──
  useEffect(function () {
    if (!isPaginated) return
    if (searchTimerRef.current) clearTimeout(searchTimerRef.current)
    searchTimerRef.current = setTimeout(function () {
      setSearchQuery(search)
    }, 400)
    return function () { clearTimeout(searchTimerRef.current) }
  }, [search, isPaginated])

  // When searchQuery changes, reset and fetch page 1
  useEffect(function () {
    if (!isPaginated) return
    // searchQuery === '' and currentPage === 1 and no search → initial load already has data
    // But if searchQuery changed we need to refetch
    fetchPage(1, searchQuery, false)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [searchQuery])

  // ── Load more ──
  var handleLoadMore = useCallback(function () {
    if (!hasMore || loading) return
    fetchPage(currentPage + 1, searchQuery, true)
  }, [hasMore, loading, currentPage, searchQuery, fetchPage])

  // ── Intersection observer for infinite scroll ──
  useEffect(function () {
    if (!isPaginated || !sentinelRef.current) return
    var observer = new IntersectionObserver(function (entries) {
      if (entries[0].isIntersecting && hasMore && !loading) {
        handleLoadMore()
      }
    }, { rootMargin: '200px' })
    observer.observe(sentinelRef.current)
    return function () { observer.disconnect() }
  }, [isPaginated, hasMore, loading, handleLoadMore])

  // Client-side filtering (non-paginated mode)
  var filteredEntities = useMemo(function () {
    if (isPaginated) return entities // server handles filtering
    if (!search.trim()) return entities
    var q = search.toLowerCase()
    return entities.filter(function (e) {
      return (
        (e.title && e.title.toLowerCase().indexOf(q) !== -1) ||
        (e.subtitle && e.subtitle.toLowerCase().indexOf(q) !== -1) ||
        (e.description && e.description.toLowerCase().indexOf(q) !== -1)
      )
    })
  }, [search, entities, isPaginated])

  var subtitle = isPaginated
    ? (totalCount > 0 ? totalCount.toLocaleString('tr-TR') + ' cari' : '')
    : (props.subtitle || '')

  var handleActionClick = useCallback(function (action) {
    if (action.url) navigateInWorkspace(action.url)
  }, [])

  var handleConfigSaved = useCallback(function (newConfig) {
    setUserConfig(newConfig)
  }, [])

  var visibleIds = userConfig && Array.isArray(userConfig.visibleIds) ? userConfig.visibleIds : null
  var order = userConfig && Array.isArray(userConfig.order) ? userConfig.order : null

  var meshStyle = isDark
    ? {
        backgroundColor: '#0a0d17',
        backgroundImage:
          'radial-gradient(at 20% 30%, rgba(99,102,241,0.12) 0px, transparent 50%), ' +
          'radial-gradient(at 80% 20%, rgba(14,165,233,0.08) 0px, transparent 50%), ' +
          'radial-gradient(at 50% 80%, rgba(168,85,247,0.08) 0px, transparent 50%), ' +
          'radial-gradient(at 90% 70%, rgba(20,184,166,0.06) 0px, transparent 50%)',
      }
    : {
        backgroundColor: '#f8fafc',
        backgroundImage:
          'radial-gradient(at 20% 30%, rgba(99,102,241,0.05) 0px, transparent 50%), ' +
          'radial-gradient(at 80% 20%, rgba(14,165,233,0.04) 0px, transparent 50%), ' +
          'radial-gradient(at 50% 80%, rgba(168,85,247,0.04) 0px, transparent 50%)',
      }

  return (
    <div className="h-full flex flex-col" style={meshStyle}>

      {/* ── Header ──────────────────────────── */}
      <div className="flex items-center gap-4 px-5 py-3 border-b border-slate-200/60 dark:border-white/[0.06] flex-shrink-0">
        <div className="flex items-center gap-3 flex-shrink-0">
          <div
            className="w-9 h-9 rounded-xl flex items-center justify-center"
            style={{
              background: headerPalette.bg,
              border: '1px solid ' + headerPalette.border,
            }}
          >
            <HeaderIcon size={17} style={{ color: headerPalette.icon }} />
          </div>
          <div>
            <h1 className="text-base font-bold text-slate-800 dark:text-white/90 tracking-tight leading-tight">
              {title}
            </h1>
            {subtitle && (
              <p className="text-[11px] text-slate-500 dark:text-white/45 leading-tight">
                {subtitle}
              </p>
            )}
          </div>
        </div>

        {/* Search */}
        {searchable && (
          <div className="flex-1 max-w-md">
            <div className="relative">
              <Search size={14} className="absolute left-3.5 top-1/2 -translate-y-1/2 text-slate-400 dark:text-white/40" />
              <input
                type="text"
                value={search}
                onChange={function (e) { setSearch(e.target.value) }}
                placeholder={searchPlaceholder}
                className="w-full pl-10 pr-4 py-2 rounded-xl bg-white/60 dark:bg-white/[0.04] border border-slate-200 dark:border-white/[0.06] text-sm text-slate-700 dark:text-white/70 placeholder:text-slate-400 dark:placeholder:text-white/40 focus:outline-none focus:border-indigo-400/50 dark:focus:border-white/15 transition-colors"
              />
              {isPaginated && loading && search && (
                <Loader2 size={14} className="absolute right-3.5 top-1/2 -translate-y-1/2 text-indigo-400 animate-spin" />
              )}
            </div>
          </div>
        )}

        {!searchable && <div className="flex-1" />}

        <button
          onClick={function () { setConfigOpen(true) }}
          className="p-2.5 rounded-xl bg-white/60 dark:bg-white/[0.04] hover:bg-white/80 dark:hover:bg-white/[0.08] border border-slate-200 dark:border-white/[0.06] transition-all group flex-shrink-0"
          title="Widget Ayarlari"
        >
          <Settings2 size={15} className="text-slate-500 dark:text-white/40 group-hover:text-indigo-600 dark:group-hover:text-indigo-400/80 transition-colors" />
        </button>

        {/* Actions */}
        {actions.length > 0 && (
          <div className="flex items-center gap-2 flex-shrink-0">
            {actions.map(function (action) {
              var ActionIcon = resolveIcon(action.icon)
              var isPrimary = action.variant === 'primary'
              return (
                <button
                  key={action.id || action.label}
                  onClick={function () { handleActionClick(action) }}
                  className={'flex items-center gap-2 px-4 py-2 rounded-xl text-sm font-semibold transition-all ' +
                    (isPrimary
                      ? 'bg-indigo-500 hover:bg-indigo-600 dark:bg-indigo-500/20 dark:hover:bg-indigo-500/30 border border-indigo-500 dark:border-indigo-400/20 text-white dark:text-indigo-300 shadow-sm'
                      : 'bg-white/60 dark:bg-white/[0.04] hover:bg-white/80 dark:hover:bg-white/[0.08] border border-slate-200 dark:border-white/[0.06] text-slate-700 dark:text-white/70')
                  }
                >
                  <ActionIcon size={15} />
                  <span>{action.label}</span>
                </button>
              )
            })}
          </div>
        )}
      </div>

      {/* ── Kart Listesi ─────────────────────── */}
      <div className="flex-1 overflow-y-auto px-4 py-3 min-h-0">
        {filteredEntities.length === 0 && !loading ? (
          <div className="text-center py-20">
            <HeaderIcon size={36} className="mx-auto text-slate-300 dark:text-white/30 mb-3" />
            <p className="text-sm text-slate-400 dark:text-white/45">{emptyText}</p>
          </div>
        ) : (
          <div className="flex flex-col gap-2">
            {filteredEntities.map(function (entity) {
              return (
                <SmartCard
                  key={entity.id}
                  {...entity}
                  visibleIds={visibleIds}
                  order={order}
                />
              )
            })}

            {/* Infinite scroll sentinel */}
            {isPaginated && hasMore && (
              <div ref={sentinelRef} className="flex items-center justify-center py-6 gap-3">
                {loading ? (
                  <div className="flex items-center gap-2 text-xs text-slate-400 dark:text-white/40">
                    <Loader2 size={16} className="animate-spin" />
                    <span>Yukleniyor...</span>
                  </div>
                ) : (
                  <button
                    onClick={handleLoadMore}
                    className="flex items-center gap-2 px-5 py-2.5 rounded-xl text-xs font-semibold bg-white/60 dark:bg-white/[0.04] hover:bg-white/80 dark:hover:bg-white/[0.08] border border-slate-200 dark:border-white/[0.06] text-slate-600 dark:text-white/60 transition-all"
                  >
                    <ChevronDown size={14} />
                    <span>Daha Fazla Yukle ({(totalCount - entities.length).toLocaleString('tr-TR')} kalan)</span>
                  </button>
                )}
              </div>
            )}

            {/* Loading indicator for initial/search load */}
            {isPaginated && loading && filteredEntities.length === 0 && (
              <div className="flex items-center justify-center py-20 gap-3">
                <Loader2 size={24} className="animate-spin text-indigo-400" />
                <span className="text-sm text-slate-400 dark:text-white/40">Yukleniyor...</span>
              </div>
            )}
          </div>
        )}
      </div>

      {/* ── Widget Config Panel ─────────────── */}
      <SmartBoardConfigPanel
        isOpen={configOpen}
        onClose={function () { setConfigOpen(false) }}
        boardKey={boardKey}
        masterWidgets={masterWidgets}
        onSaved={handleConfigSaved}
      />
    </div>
  )
}
