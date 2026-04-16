import { useState, useMemo } from 'react'
import { motion, AnimatePresence } from 'framer-motion'
import { Search, ChevronDown, ChevronLeft, ChevronsLeft, ChevronsRight, Hexagon } from 'lucide-react'
import menuData from './menuData'

/* ── Recursive menu item ─────────────────────── */
function MenuItem({ item, depth = 0, collapsed, activePath, onNavigate }) {
  const [open, setOpen] = useState(false)
  const hasChildren = item.children && item.children.length > 0
  const isActive = activePath === item.href
  const isChildActive = hasChildren && item.children.some(
    c => c.href === activePath || (c.children && c.children.some(cc => cc.href === activePath))
  )
  const Icon = item.icon

  const handleClick = () => {
    if (hasChildren) {
      setOpen(prev => !prev)
    } else if (item.href) {
      onNavigate?.(item.href)
    }
  }

  // Collapsed mode: only top-level icons
  if (collapsed && depth === 0) {
    return (
      <motion.button
        whileHover={{ scale: 1.08 }}
        whileTap={{ scale: 0.95 }}
        onClick={handleClick}
        className={`relative w-full flex items-center justify-center py-2.5 rounded-xl transition-colors group ${
          isActive || isChildActive
            ? 'bg-indigo-500/15 text-indigo-300'
            : 'text-white/35 hover:text-white/60 hover:bg-white/[0.04]'
        }`}
        title={item.label}
      >
        {(isActive || isChildActive) && (
          <motion.div
            layoutId="sidebar-active"
            className="absolute left-0 top-1/2 -translate-y-1/2 w-[3px] h-5 rounded-r-full bg-indigo-400"
            transition={{ type: 'spring', stiffness: 400, damping: 28 }}
          />
        )}
        <Icon size={18} strokeWidth={1.7} />
      </motion.button>
    )
  }

  if (collapsed) return null

  const paddingLeft = 12 + depth * 14

  return (
    <div>
      <button
        onClick={handleClick}
        className={`relative w-full flex items-center gap-2.5 py-2 px-3 rounded-xl text-left transition-all duration-150 group ${
          isActive
            ? 'bg-indigo-500/12 text-indigo-300'
            : isChildActive
              ? 'text-white/70'
              : 'text-white/40 hover:text-white/65 hover:bg-white/[0.03]'
        }`}
        style={{ paddingLeft }}
      >
        {/* Active indicator */}
        {isActive && (
          <motion.div
            layoutId="sidebar-active"
            className="absolute left-0 top-1/2 -translate-y-1/2 w-[3px] h-5 rounded-r-full bg-indigo-400 shadow-[0_0_8px_rgba(99,102,241,0.5)]"
            transition={{ type: 'spring', stiffness: 400, damping: 28 }}
          />
        )}

        <Icon size={16} strokeWidth={1.7} className="flex-shrink-0" />
        <span className="flex-1 text-[13px] font-medium truncate">{item.label}</span>

        {hasChildren && (
          <motion.div
            animate={{ rotate: open ? 180 : 0 }}
            transition={{ duration: 0.2 }}
            className="flex-shrink-0"
          >
            <ChevronDown size={13} className="text-white/20" />
          </motion.div>
        )}
      </button>

      {/* Children accordion */}
      <AnimatePresence initial={false}>
        {hasChildren && open && (
          <motion.div
            initial={{ height: 0, opacity: 0 }}
            animate={{ height: 'auto', opacity: 1 }}
            exit={{ height: 0, opacity: 0 }}
            transition={{ duration: 0.25, ease: [0.25, 0.1, 0.25, 1] }}
            className="overflow-hidden"
          >
            <div className="py-0.5">
              {item.children.map(child => (
                <MenuItem
                  key={child.id}
                  item={child}
                  depth={depth + 1}
                  collapsed={false}
                  activePath={activePath}
                  onNavigate={onNavigate}
                />
              ))}
            </div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  )
}

/* ── Sidebar ─────────────────────────────────── */
export default function Sidebar({ collapsed, onToggleCollapse, activePath, onNavigate }) {
  const [search, setSearch] = useState('')

  const filteredMenu = useMemo(() => {
    if (!search.trim()) return menuData
    const q = search.toLowerCase()
    const filterItems = (items) => {
      return items.reduce((acc, item) => {
        if (item.label.toLowerCase().includes(q)) {
          acc.push(item)
        } else if (item.children) {
          const filtered = filterItems(item.children)
          if (filtered.length > 0) acc.push({ ...item, children: filtered })
        }
        return acc
      }, [])
    }
    return filterItems(menuData)
  }, [search])

  return (
    <motion.aside
      animate={{ width: collapsed ? 64 : 240 }}
      transition={{ type: 'spring', stiffness: 350, damping: 32, mass: 0.8 }}
      className="h-full flex flex-col flex-shrink-0 border-r border-white/[0.06] overflow-hidden"
      style={{
        background: 'rgba(8, 11, 22, 0.85)',
        backdropFilter: 'blur(24px)',
        WebkitBackdropFilter: 'blur(24px)',
      }}
    >
      {/* ── Brand ─────────────────── */}
      <div className={`flex items-center gap-3 px-4 pt-4 pb-3 flex-shrink-0 ${collapsed ? 'justify-center px-2' : ''}`}>
        <div className="w-8 h-8 rounded-xl bg-indigo-500/20 border border-indigo-400/20 flex items-center justify-center flex-shrink-0">
          <Hexagon size={16} className="text-indigo-400" strokeWidth={1.8} />
        </div>
        {!collapsed && (
          <motion.div
            initial={{ opacity: 0, x: -8 }}
            animate={{ opacity: 1, x: 0 }}
            exit={{ opacity: 0, x: -8 }}
            className="min-w-0"
          >
            <h2 className="text-sm font-bold text-white/85 tracking-tight leading-none">CalibraHub</h2>
            <p className="text-[10px] text-white/25 mt-0.5">ERP Platform</p>
          </motion.div>
        )}
      </div>

      {/* ── Search ─────────────────── */}
      {!collapsed && (
        <motion.div
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          className="px-3 pb-2 flex-shrink-0"
        >
          <div className="relative group">
            <Search size={13} className="absolute left-3 top-1/2 -translate-y-1/2 text-white/20 group-focus-within:text-indigo-400/50 transition-colors" />
            <input
              type="text"
              value={search}
              onChange={e => setSearch(e.target.value)}
              placeholder="Menude ara..."
              className="w-full pl-8 pr-3 py-1.5 rounded-lg bg-white/[0.04] border border-white/[0.06] text-xs text-white/60 placeholder:text-white/20 focus:outline-none focus:border-indigo-400/30 focus:bg-white/[0.06] focus:shadow-[0_0_0_3px_rgba(99,102,241,0.08)] transition-all duration-200"
            />
          </div>
        </motion.div>
      )}

      {/* ── Divider ────────────────── */}
      <div className={`mx-3 h-px bg-white/[0.05] flex-shrink-0 ${collapsed ? 'mx-2' : ''}`} />

      {/* ── Menu ──────────────────── */}
      <nav className={`flex-1 overflow-y-auto overflow-x-hidden py-2 ${collapsed ? 'px-2' : 'px-2.5'}`}>
        <div className={`flex flex-col ${collapsed ? 'gap-1 items-center' : 'gap-0.5'}`}>
          {filteredMenu.map(item => (
            <MenuItem
              key={item.id}
              item={item}
              collapsed={collapsed}
              activePath={activePath}
              onNavigate={onNavigate}
            />
          ))}
        </div>
      </nav>

      {/* ── Collapse toggle ────────── */}
      <div className={`flex-shrink-0 border-t border-white/[0.05] p-2 ${collapsed ? 'flex justify-center' : ''}`}>
        <button
          onClick={onToggleCollapse}
          className="flex items-center gap-2 px-3 py-2 rounded-xl w-full text-white/25 hover:text-white/50 hover:bg-white/[0.04] transition-colors"
        >
          {collapsed ? (
            <ChevronsRight size={16} className="mx-auto" />
          ) : (
            <>
              <ChevronsLeft size={16} />
              <span className="text-xs font-medium">Daralt</span>
            </>
          )}
        </button>
      </div>
    </motion.aside>
  )
}
