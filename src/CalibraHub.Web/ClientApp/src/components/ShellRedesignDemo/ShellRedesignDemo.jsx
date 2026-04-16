/**
 * ShellRedesignDemo — CalibraHub yeni nesil kabuk (Main Layout/Wrapper) mockup'i
 *
 * Tam ekran interaktif demo. Tum arayuz tek bir config JSON prop'undan beslenir
 * (Aptal Bilesen, Zeki Veri prensibi). Glassmorphism + premium SaaS tasarim dili.
 *
 * BU BILESEN MEVCUT CalibraSmartBoard VE AdminWidgetRegistryPanel'i BOZMAZ —
 * sadece onlari saracak bir kabuk sunar. "Malzeme Kartlari" sekmesinde mevcut
 * SmartBoard render edilir, "Malzeme Karti Duzenle" sekmesinde yeni nesil form
 * render edilir. Sekmeler arasi gecis yaparak hem liste hem form test edilebilir.
 *
 * Props:
 *   config: {
 *     user: { name, email, initials },
 *     system: { status, company, year },
 *     activeTab: string,
 *     tabs: string[],
 *     workspace: { title, id },
 *     formData: { [key]: { label, value, dataType } }
 *   }
 */
import { useState, useEffect, useRef } from 'react'
import { motion, AnimatePresence } from 'framer-motion'
import {
  Package, Boxes, Folder, ChevronRight, Truck, Warehouse,
  Users, FileText, Settings, LayoutGrid, Factory, Coins, Receipt,
  Search, Bell, Sun, Moon, LogOut, UserCircle, MessageSquare,
  Languages, Layers, Plus, X, Save, CircleDot, Hash, Package2,
  Building2, CheckCircle2, Sparkles, Wifi, Clock
} from 'lucide-react'
import { SmartBoard } from '../CalibraSmartBoard'

/* ── Varsayilan config (dev / standalone icin) ──────────────── */
var DEFAULT_CONFIG = {
  user: { name: 'Sistem Admin', email: 'admin@calibra.local', initials: 'SA' },
  system: { status: 'Hazir', company: 'Adamsor', year: '2026' },
  activeTab: 'Cari Kartlar',
  tabs: ['Cari Kartlar', 'Cari Kart Duzenle'],
  workspace: { title: 'Cari Kart Duzenle', id: 'ID: 105 - CH001' },
  sidebarActiveId: 'finance.accounts',
  sidebarExpanded: { finance: true },
  listData: [
    {
      entityId: 'CH001',
      entityTitle: 'Acme Teknoloji A.S.',
      widgets: [
        { id: 'w_bakiye', dataType: 'currency', label: 'Guncel Bakiye',  value: '250.000,00 ₺', variant: 'danger' },
        { id: 'w_risk',   dataType: 'currency', label: 'Risk Limiti',    value: '200.000,00 ₺', variant: 'muted' },
        { id: 'w_vade',   dataType: 'numeric',  label: 'Ort. Vade',      value: '45 Gun',        variant: 'warning' },
        { id: 'w_durum',  dataType: 'text',     label: 'Hesap Durumu',   value: 'Aktif',         variant: 'success' },
      ],
    },
    {
      entityId: 'CH002',
      entityTitle: 'Global Lojistik Ltd.',
      widgets: [
        { id: 'w_bakiye', dataType: 'currency', label: 'Guncel Bakiye',  value: '15.400,00 ₺',  variant: 'success' },
        { id: 'w_risk',   dataType: 'currency', label: 'Risk Limiti',    value: '500.000,00 ₺', variant: 'muted' },
        { id: 'w_vade',   dataType: 'numeric',  label: 'Ort. Vade',      value: '15 Gun',        variant: 'info' },
        { id: 'w_durum',  dataType: 'text',     label: 'Hesap Durumu',   value: 'Aktif',         variant: 'success' },
      ],
    },
  ],
  formData: {
    w_kod:        { label: 'Cari Kodu',    value: 'CH001',              dataType: 'text' },
    w_unvan:      { label: 'Cari Unvani', value: 'Acme Teknoloji A.S.', dataType: 'text' },
    w_vd:         { label: 'Vergi Dairesi', value: 'Bornova',           dataType: 'text' },
    w_vno:        { label: 'Vergi No',    value: '1234567890',          dataType: 'numeric' },
    w_limit:      { label: 'Risk Limiti', value: '200000',              dataType: 'currency' },
    w_kara_liste: { label: 'Kara Liste',  value: false,                 dataType: 'checkbox' },
  },
}

/* ── Sidebar hiyerarsisi ────────────────────────────────────── */
var SIDEBAR_TREE = [
  {
    id: 'logistics',
    label: 'Lojistik',
    icon: Package,
    children: [
      {
        id: 'logistics.masters',
        label: 'Sabit Tanimlamalar',
        icon: Folder,
        children: [
          { id: 'logistics.masters.materials', label: 'Malzeme Kartlari', icon: Boxes },
          { id: 'logistics.masters.groups',    label: 'Malzeme Gruplari', icon: Layers },
          { id: 'logistics.masters.units',     label: 'Olcu Birimleri',   icon: Hash },
        ],
      },
      { id: 'logistics.warehouse',  label: 'Depo Yonetimi',    icon: Warehouse },
      { id: 'logistics.movements',  label: 'Stok Hareketleri', icon: Truck },
    ],
  },
  {
    id: 'finance',
    label: 'Finans',
    icon: Coins,
    children: [
      { id: 'finance.accounts', label: 'Cari Hesaplar', icon: Users },
      { id: 'finance.invoices', label: 'Faturalar',     icon: Receipt },
    ],
  },
  {
    id: 'production',
    label: 'Uretim',
    icon: Factory,
    children: [
      { id: 'production.tree',   label: 'Urun Agaci', icon: Layers },
      { id: 'production.orders', label: 'Is Emirleri', icon: FileText },
    ],
  },
  {
    id: 'settings',
    label: 'Ayarlar',
    icon: Settings,
    children: [
      { id: 'settings.screens', label: 'Ekran Tasarimi', icon: LayoutGrid },
      { id: 'settings.users',   label: 'Kullanicilar',   icon: UserCircle },
    ],
  },
]

/* ── Variant renk haritasi ───────────────────────────────────────
   JSON API'den gelen variant degeri SmartBoard'un color sistemine
   eslestirilir. Renk karari C# tarafinda verilmis; React sadece
   ciziyor. */
var VARIANT_COLOR_MAP = {
  danger:  'rose',
  warning: 'amber',
  success: 'emerald',
  info:    'cyan',
  muted:   'slate',
  primary: 'indigo',
}

/**
 * config.listData[] → SmartBoard boardConfig donusturucusu.
 * Hicbir entity-specifik bilgi yok; tamamen jenerik.
 */
function buildBoardConfigFromListData(config) {
  var listData = config.listData || []
  var firstTab = (config.tabs || [])[0] || 'Liste'
  return {
    boardKey: 'shell-board-' + firstTab.toLowerCase().replace(/\s+/g, '-'),
    title: firstTab,
    subtitle: listData.length + ' kayit',
    icon: 'Building2',
    iconColor: 'cyan',
    searchPlaceholder: firstTab + ' ara...',
    emptyText: 'Henuz kayit eklenmemis',
    actions: [{ id: 'new', label: 'Yeni', icon: 'Plus', variant: 'primary', url: '#' }],
    entities: listData.map(function(item) {
      return {
        id: item.entityId,
        title: item.entityTitle,
        subtitle: item.entityId,
        description: null,
        imageUrl: null,
        statusBadge: null,
        widgets: (item.widgets || []).map(function(w) {
          return {
            id: w.id,
            type: 'data',
            dataType: w.dataType,
            label: w.label,
            value: w.value,
            color: VARIANT_COLOR_MAP[w.variant] || 'slate',
          }
        }),
        primaryAction:   { label: 'Duzenle', icon: 'Edit',   url: '#' },
        secondaryAction: { label: 'Sil',     icon: 'Trash2', url: '#' },
      }
    }),
  }
}

/* ══════════════════════════════════════════════════════════════
   Ana bilesen
   ══════════════════════════════════════════════════════════════ */
export default function ShellRedesignDemo(props) {
  var config = props.config || DEFAULT_CONFIG

  var [activeTab, setActiveTab] = useState(config.activeTab)
  var [tabs, setTabs] = useState(config.tabs)
  var [profileOpen, setProfileOpen] = useState(false)
  var [isDark, setIsDark] = useState(true)
  var [lang, setLang] = useState('TR')
  var [activeSidebarId, setActiveSidebarId] = useState(
    config.sidebarActiveId || 'logistics.masters.materials'
  )
  var [expandedNodes, setExpandedNodes] = useState(
    config.sidebarExpanded || { logistics: true, 'logistics.masters': true }
  )

  var [formValues, setFormValues] = useState(function() {
    var out = {}
    Object.keys(config.formData).forEach(function(k) { out[k] = config.formData[k].value })
    return out
  })

  // Tema degisince html.dark class'ini da senkronize et — SmartBoard
  // (mevcut bilesen) Tailwind dark: prefix'ini kullaniyor
  useEffect(function() {
    var html = document.documentElement
    if (isDark) html.classList.add('dark')
    else html.classList.remove('dark')
  }, [isDark])

  function setFieldValue(key, value) {
    setFormValues(function(prev) {
      var next = Object.assign({}, prev)
      next[key] = value
      return next
    })
  }

  function toggleExpand(id) {
    setExpandedNodes(function(prev) {
      var next = Object.assign({}, prev)
      next[id] = !next[id]
      return next
    })
  }

  function handleCloseTab(t, e) {
    e.stopPropagation()
    setTabs(function(prev) {
      var next = prev.filter(function(x) { return x !== t })
      if (t === activeTab && next.length > 0) {
        setActiveTab(next[next.length - 1])
      }
      return next
    })
  }

  var rootBgClass = isDark ? 'bg-[#0a0d17] text-white' : 'bg-slate-100 text-slate-900'

  // ── Jenerik sekme icerik resolver ────────────────────────────
  // Hardcoded isim kontrolu yok. Hangi sekme aktifse:
  //   1. Ilk sekme (index 0) ve listData varsa → SmartBoard
  //   2. Diger sekmeler ve formData varsa    → WorkspaceBody
  //   3. Hicbiri → EmptyTab
  var firstTab  = (config.tabs || [])[0]
  var hasListData = Array.isArray(config.listData) && config.listData.length > 0
  var hasFormData = config.formData && Object.keys(config.formData).length > 0
  var boardConfig = hasListData ? buildBoardConfigFromListData(config) : null

  function renderTabBody() {
    if (activeTab === firstTab && hasListData) {
      return (
        <div className="h-full">
          <SmartBoard {...boardConfig} />
        </div>
      )
    }
    if (hasFormData) {
      return (
        <WorkspaceBody
          isDark={isDark}
          workspace={config.workspace}
          formData={config.formData}
          formValues={formValues}
          onFieldChange={setFieldValue}
        />
      )
    }
    return <EmptyTab isDark={isDark} />
  }

  return (
    <div className={'fixed inset-0 flex overflow-hidden transition-colors duration-500 ' + rootBgClass}>

      {/* ── Ambient mesh gradient background ───────────────── */}
      <div
        className="pointer-events-none absolute inset-0 transition-opacity duration-500"
        style={{
          opacity: isDark ? 1 : 0.5,
          backgroundImage:
            'radial-gradient(at 12% 8%, rgba(99,102,241,0.22) 0px, transparent 50%),' +
            'radial-gradient(at 88% 12%, rgba(14,165,233,0.14) 0px, transparent 50%),' +
            'radial-gradient(at 50% 100%, rgba(168,85,247,0.14) 0px, transparent 50%),' +
            'radial-gradient(at 95% 85%, rgba(20,184,166,0.1) 0px, transparent 50%)',
        }}
      />

      {/* ── Sol: Sidebar ─────────────────────────────────── */}
      <Sidebar
        isDark={isDark}
        tree={SIDEBAR_TREE}
        expandedNodes={expandedNodes}
        activeId={activeSidebarId}
        onToggle={toggleExpand}
        onSelect={setActiveSidebarId}
      />

      {/* ── Sag: Ana alan ────────────────────────────────── */}
      <div className="flex-1 flex flex-col min-w-0 relative z-10">

        <Header
          isDark={isDark}
          user={config.user}
          onProfileClick={function() { setProfileOpen(function(o) { return !o }) }}
        />

        {/* Profile popover */}
        <AnimatePresence>
          {profileOpen && (
            <ProfilePopover
              isDark={isDark}
              user={config.user}
              lang={lang}
              onLangChange={setLang}
              onThemeToggle={function() { setIsDark(function(v) { return !v }) }}
              onClose={function() { setProfileOpen(false) }}
            />
          )}
        </AnimatePresence>

        <TabBar
          isDark={isDark}
          tabs={tabs}
          activeTab={activeTab}
          onTabClick={setActiveTab}
          onTabClose={handleCloseTab}
        />

        {/* Body — jenerik sekme resolver */}
        <div className="flex-1 min-h-0 relative overflow-hidden">
          {renderTabBody()}
        </div>

        <StatusBar isDark={isDark} system={config.system} user={config.user} />
      </div>
    </div>
  )
}

/* ══════════════════════════════════════════════════════════════
   Sidebar
   ══════════════════════════════════════════════════════════════ */
function Sidebar(props) {
  var isDark = props.isDark
  var borderColor = isDark ? 'border-white/[0.06]' : 'border-slate-200/80'
  var bgColor = isDark ? 'bg-[#0c0f1a]/70' : 'bg-white/70'

  return (
    <aside
      className={
        'relative z-10 flex flex-col w-[260px] flex-shrink-0 border-r backdrop-blur-xl transition-colors duration-500 ' +
        borderColor + ' ' + bgColor
      }
    >
      {/* Brand */}
      <div className={'flex items-center gap-2.5 px-5 h-14 border-b ' + borderColor}>
        <div
          className="w-8 h-8 rounded-xl flex items-center justify-center"
          style={{
            background: 'linear-gradient(135deg, #6366f1 0%, #8b5cf6 100%)',
            boxShadow: '0 6px 18px rgba(99,102,241,0.4)',
          }}
        >
          <Sparkles size={15} className="text-white" strokeWidth={2.2} />
        </div>
        <div className="flex-1 min-w-0">
          <h1 className={'text-sm font-bold tracking-tight leading-tight ' + (isDark ? 'text-white' : 'text-slate-900')}>
            CalibraHub
          </h1>
          <p className={'text-[10px] leading-tight ' + (isDark ? 'text-white/35' : 'text-slate-500')}>
            Premium ERP
          </p>
        </div>
      </div>

      {/* Menu tree */}
      <nav className="flex-1 overflow-y-auto py-3 px-3 smartcard-widgets-scroll">
        {props.tree.map(function(node) {
          return (
            <SidebarNode
              key={node.id}
              node={node}
              level={0}
              isDark={isDark}
              expandedNodes={props.expandedNodes}
              activeId={props.activeId}
              onToggle={props.onToggle}
              onSelect={props.onSelect}
            />
          )
        })}
      </nav>

      {/* Bottom mini status */}
      <div className={'px-4 py-3 border-t ' + borderColor}>
        <div className={'flex items-center gap-2 text-[10px] ' + (isDark ? 'text-white/35' : 'text-slate-500')}>
          <div className="w-1.5 h-1.5 rounded-full bg-emerald-500 shadow-[0_0_6px_rgba(16,185,129,0.8)]" />
          <span>Sistem calisiyor</span>
        </div>
      </div>
    </aside>
  )
}

function SidebarNode(props) {
  var node = props.node
  var level = props.level
  var isDark = props.isDark
  var hasChildren = Array.isArray(node.children) && node.children.length > 0
  var expanded = !!props.expandedNodes[node.id]
  var isActive = props.activeId === node.id
  var Icon = node.icon || CircleDot

  function handleClick() {
    if (hasChildren) props.onToggle(node.id)
    else props.onSelect(node.id)
  }

  var base = 'flex items-center gap-2.5 w-full px-3 py-2 rounded-xl text-sm font-medium cursor-pointer transition-all group'
  var variant = isActive
    ? (isDark
      ? 'bg-indigo-500/15 text-white border border-indigo-400/30'
      : 'bg-indigo-50 text-indigo-700 border border-indigo-200')
    : (isDark
      ? 'text-white/60 hover:bg-white/[0.04] hover:text-white border border-transparent'
      : 'text-slate-600 hover:bg-slate-100 hover:text-slate-900 border border-transparent')

  return (
    <div>
      <motion.div
        whileTap={{ scale: 0.98 }}
        onClick={handleClick}
        className={base + ' ' + variant}
        style={{ marginLeft: level * 12, marginBottom: 2 }}
      >
        <Icon
          size={15}
          strokeWidth={1.8}
          className={isActive
            ? (isDark ? 'text-indigo-300' : 'text-indigo-600')
            : (isDark ? 'text-white/40 group-hover:text-white/80' : 'text-slate-400 group-hover:text-slate-700')}
        />
        <span className="flex-1 truncate text-[13px]">{node.label}</span>
        {hasChildren && (
          <motion.span
            animate={{ rotate: expanded ? 90 : 0 }}
            transition={{ duration: 0.18 }}
            className={isDark ? 'text-white/30' : 'text-slate-400'}
          >
            <ChevronRight size={13} />
          </motion.span>
        )}
        {isActive && !hasChildren && (
          <div className="w-1.5 h-1.5 rounded-full bg-indigo-400 shadow-[0_0_8px_rgba(99,102,241,0.8)]" />
        )}
      </motion.div>

      <AnimatePresence initial={false}>
        {hasChildren && expanded && (
          <motion.div
            initial={{ opacity: 0, height: 0 }}
            animate={{ opacity: 1, height: 'auto' }}
            exit={{ opacity: 0, height: 0 }}
            transition={{ duration: 0.22, ease: [0.23, 1, 0.32, 1] }}
            className="overflow-hidden"
          >
            {node.children.map(function(c) {
              return (
                <SidebarNode
                  key={c.id}
                  node={c}
                  level={level + 1}
                  isDark={isDark}
                  expandedNodes={props.expandedNodes}
                  activeId={props.activeId}
                  onToggle={props.onToggle}
                  onSelect={props.onSelect}
                />
              )
            })}
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  )
}

/* ══════════════════════════════════════════════════════════════
   Header (Navbar)
   ══════════════════════════════════════════════════════════════ */
function Header(props) {
  var isDark = props.isDark
  var user = props.user
  var borderColor = isDark ? 'border-white/[0.06]' : 'border-slate-200/80'
  var bgColor = isDark ? 'bg-[#0a0d17]/70' : 'bg-white/70'

  return (
    <header
      className={
        'relative z-20 flex items-center gap-4 h-14 px-5 border-b backdrop-blur-xl flex-shrink-0 transition-colors duration-500 ' +
        borderColor + ' ' + bgColor
      }
    >
      {/* Spacer — Bell ve Profil butonlarini saga yaslar */}
      <div className="flex-1" />

      {/* Right actions */}
      <div className="flex items-center gap-2">
        <button
          className={
            'relative p-2 rounded-xl transition-colors ' +
            (isDark ? 'hover:bg-white/5 text-white/60 hover:text-white' : 'hover:bg-slate-100 text-slate-500 hover:text-slate-800')
          }
          title="Bildirimler"
        >
          <Bell size={15} strokeWidth={1.8} />
          <span className="absolute top-1.5 right-1.5 w-1.5 h-1.5 rounded-full bg-rose-500 shadow-[0_0_6px_rgba(244,63,94,0.8)]" />
        </button>

        <div className={'w-px h-6 ' + (isDark ? 'bg-white/10' : 'bg-slate-200')} />

        <motion.button
          whileTap={{ scale: 0.96 }}
          onClick={props.onProfileClick}
          className="relative w-9 h-9 rounded-xl flex items-center justify-center font-bold text-sm text-white"
          style={{
            background: 'linear-gradient(135deg, #6366f1 0%, #8b5cf6 100%)',
            boxShadow: '0 6px 16px rgba(99,102,241,0.35)',
          }}
        >
          {user.initials}
          <span
            className="absolute bottom-0 right-0 w-2 h-2 rounded-full bg-emerald-400"
            style={{ border: '2px solid ' + (isDark ? '#0a0d17' : '#ffffff') }}
          />
        </motion.button>
      </div>
    </header>
  )
}

/* ══════════════════════════════════════════════════════════════
   Profile popover
   ══════════════════════════════════════════════════════════════ */
function ProfilePopover(props) {
  var isDark = props.isDark
  var user = props.user
  var ref = useRef(null)

  useEffect(function() {
    function onDoc(e) {
      if (ref.current && !ref.current.contains(e.target)) props.onClose()
    }
    // Ufak gecikme — mount'ta ayni click'i yakalamasin
    var t = setTimeout(function() { document.addEventListener('mousedown', onDoc) }, 10)
    return function() {
      clearTimeout(t)
      document.removeEventListener('mousedown', onDoc)
    }
  }, [])

  var glassBg = isDark ? 'rgba(12, 15, 26, 0.92)' : 'rgba(255, 255, 255, 0.96)'
  var glassBorder = isDark ? '1px solid rgba(255, 255, 255, 0.14)' : '1px solid rgba(15, 23, 42, 0.1)'

  return (
    <motion.div
      ref={ref}
      initial={{ opacity: 0, y: -8, scale: 0.96 }}
      animate={{ opacity: 1, y: 0, scale: 1 }}
      exit={{ opacity: 0, y: -8, scale: 0.96 }}
      transition={{ type: 'spring', stiffness: 400, damping: 28 }}
      className="absolute right-5 top-16 z-40 w-80 rounded-2xl overflow-hidden"
      style={{
        background: glassBg,
        backdropFilter: 'blur(28px) saturate(140%)',
        WebkitBackdropFilter: 'blur(28px) saturate(140%)',
        border: glassBorder,
        boxShadow: '0 20px 60px rgba(0,0,0,0.5)',
      }}
    >
      {/* User header */}
      <div className="p-5 pb-4">
        <div className="flex items-center gap-3">
          <div
            className="w-11 h-11 rounded-xl flex items-center justify-center text-white font-bold text-base"
            style={{
              background: 'linear-gradient(135deg, #6366f1 0%, #8b5cf6 100%)',
              boxShadow: '0 6px 16px rgba(99,102,241,0.3)',
            }}
          >
            {user.initials}
          </div>
          <div className="flex-1 min-w-0">
            <h3 className={'text-sm font-bold truncate ' + (isDark ? 'text-white' : 'text-slate-900')}>
              {user.name}
            </h3>
            <p className={'text-[11px] truncate ' + (isDark ? 'text-white/45' : 'text-slate-500')}>
              {user.email}
            </p>
          </div>
        </div>
      </div>

      <div className={isDark ? 'h-px bg-white/10' : 'h-px bg-slate-200'} />

      {/* Items */}
      <div className="py-2 px-2">
        <PopoverRow isDark={isDark} icon={Layers} label="Acik Sayfalar" badge="3" />
        <PopoverRow isDark={isDark} icon={MessageSquare} label="Mesajlar" badge="2" />

        {/* Language switch */}
        <div className={
          'flex items-center gap-3 px-3 py-2 rounded-xl ' +
          (isDark ? 'hover:bg-white/[0.04]' : 'hover:bg-slate-100')
        }>
          <Languages size={15} strokeWidth={1.8} className={isDark ? 'text-white/50' : 'text-slate-500'} />
          <span className={'flex-1 text-[13px] font-medium ' + (isDark ? 'text-white/80' : 'text-slate-700')}>
            Dil
          </span>
          <div className={'flex items-center gap-0.5 p-1 rounded-lg ' + (isDark ? 'bg-white/[0.06]' : 'bg-slate-200/80')}>
            {['TR', 'EN'].map(function(l) {
              var sel = props.lang === l
              return (
                <button
                  key={l}
                  onClick={function() { props.onLangChange(l) }}
                  className={
                    'px-2 py-0.5 rounded-md text-[10px] font-bold transition-all ' +
                    (sel
                      ? (isDark ? 'bg-white text-slate-900 shadow-sm' : 'bg-indigo-500 text-white shadow-sm')
                      : (isDark ? 'text-white/50 hover:text-white' : 'text-slate-500 hover:text-slate-800'))
                  }
                >
                  {l}
                </button>
              )
            })}
          </div>
        </div>

        {/* Theme switch */}
        <div className={
          'flex items-center gap-3 px-3 py-2 rounded-xl ' +
          (isDark ? 'hover:bg-white/[0.04]' : 'hover:bg-slate-100')
        }>
          {isDark
            ? <Moon size={15} strokeWidth={1.8} className="text-white/50" />
            : <Sun size={15} strokeWidth={1.8} className="text-amber-500" />}
          <span className={'flex-1 text-[13px] font-medium ' + (isDark ? 'text-white/80' : 'text-slate-700')}>
            Tema
          </span>
          <button
            onClick={props.onThemeToggle}
            className={'relative w-10 h-5 rounded-full transition-colors ' + (isDark ? 'bg-indigo-500/60' : 'bg-slate-300')}
          >
            <motion.div
              className="absolute top-0.5 w-4 h-4 rounded-full bg-white shadow-sm"
              animate={{ left: isDark ? 22 : 2 }}
              transition={{ type: 'spring', stiffness: 500, damping: 30 }}
            />
          </button>
        </div>

        <PopoverRow isDark={isDark} icon={UserCircle} label="Profil Bilgileri" />
      </div>

      <div className={isDark ? 'h-px bg-white/10' : 'h-px bg-slate-200'} />

      {/* Logout */}
      <div className="p-2">
        <button
          className={
            'w-full flex items-center gap-3 px-3 py-2.5 rounded-xl text-[13px] font-semibold transition-all ' +
            (isDark
              ? 'text-rose-400 hover:bg-rose-500/15 hover:text-rose-300'
              : 'text-rose-600 hover:bg-rose-50')
          }
        >
          <LogOut size={15} strokeWidth={2} />
          <span>Cikis Yap</span>
        </button>
      </div>
    </motion.div>
  )
}

function PopoverRow(props) {
  var Icon = props.icon
  var isDark = props.isDark
  return (
    <button
      className={
        'w-full flex items-center gap-3 px-3 py-2 rounded-xl transition-colors ' +
        (isDark ? 'hover:bg-white/[0.04]' : 'hover:bg-slate-100')
      }
    >
      <Icon size={15} strokeWidth={1.8} className={isDark ? 'text-white/50' : 'text-slate-500'} />
      <span className={'flex-1 text-left text-[13px] font-medium ' + (isDark ? 'text-white/80' : 'text-slate-700')}>
        {props.label}
      </span>
      {props.badge && (
        <span
          className="text-[9px] font-bold px-1.5 py-0.5 rounded-full"
          style={{
            background: 'rgba(99,102,241,0.2)',
            color: '#a5b4fc',
            border: '1px solid rgba(99,102,241,0.35)',
          }}
        >
          {props.badge}
        </span>
      )}
    </button>
  )
}

/* ══════════════════════════════════════════════════════════════
   Tab bar
   ══════════════════════════════════════════════════════════════ */
function TabBar(props) {
  var isDark = props.isDark
  var borderColor = isDark ? 'border-white/[0.06]' : 'border-slate-200/80'
  var bgColor = isDark ? 'bg-[#0a0d17]/40' : 'bg-white/40'

  return (
    <div className={'flex items-center gap-1 px-4 h-11 border-b backdrop-blur-xl flex-shrink-0 relative ' + borderColor + ' ' + bgColor}>
      {props.tabs.map(function(t) {
        var isActive = t === props.activeTab
        return (
          <div
            key={t}
            onClick={function() { props.onTabClick(t) }}
            className={
              'relative flex items-center gap-2 px-3 py-1.5 rounded-lg text-[12px] font-medium cursor-pointer transition-all ' +
              (isActive
                ? (isDark ? 'text-white bg-white/[0.06]' : 'text-slate-900 bg-slate-100')
                : (isDark ? 'text-white/50 hover:text-white/80 hover:bg-white/[0.03]' : 'text-slate-500 hover:text-slate-800 hover:bg-slate-50'))
            }
          >
            <span>{t}</span>
            <button
              onClick={function(e) { props.onTabClose(t, e) }}
              className={
                'w-4 h-4 rounded flex items-center justify-center transition-colors ' +
                (isDark ? 'hover:bg-white/10 text-white/30 hover:text-white/80' : 'hover:bg-slate-200 text-slate-400 hover:text-slate-700')
              }
            >
              <X size={10} strokeWidth={2.4} />
            </button>

            {isActive && (
              <motion.div
                layoutId="tab-underline"
                className="absolute left-2 right-2 -bottom-[6px] h-0.5 rounded-full"
                style={{
                  background: 'linear-gradient(90deg, #6366f1 0%, #8b5cf6 100%)',
                  boxShadow: '0 0 10px rgba(99,102,241,0.7)',
                }}
              />
            )}
          </div>
        )
      })}

      <button
        className={
          'ml-1 w-6 h-6 rounded-lg flex items-center justify-center transition-colors ' +
          (isDark ? 'text-white/30 hover:text-white/80 hover:bg-white/[0.04]' : 'text-slate-400 hover:text-slate-700 hover:bg-slate-100')
        }
        title="Yeni sekme"
      >
        <Plus size={13} strokeWidth={2.2} />
      </button>
    </div>
  )
}

/* ══════════════════════════════════════════════════════════════
   Workspace — form body ("Malzeme Karti Duzenle" sekmesi)
   ══════════════════════════════════════════════════════════════ */
function WorkspaceBody(props) {
  var isDark = props.isDark
  var workspace = props.workspace
  var formData = props.formData
  var formValues = props.formValues
  var onFieldChange = props.onFieldChange
  var keys = Object.keys(formData)

  return (
    <main className="h-full overflow-y-auto px-6 py-5 min-h-0">
      <div className="max-w-4xl mx-auto">
        {/* Workspace header */}
        <div className="flex items-center justify-between mb-5">
          <div className="flex items-center gap-3">
            <div
              className="w-11 h-11 rounded-2xl flex items-center justify-center"
              style={{
                background: isDark ? 'rgba(99,102,241,0.15)' : 'rgba(99,102,241,0.1)',
                border: isDark ? '1px solid rgba(99,102,241,0.3)' : '1px solid rgba(99,102,241,0.25)',
              }}
            >
              <Package2 size={19} style={{ color: '#a5b4fc' }} strokeWidth={1.8} />
            </div>
            <div>
              <h1 className={'text-lg font-bold tracking-tight ' + (isDark ? 'text-white' : 'text-slate-900')}>
                {workspace.title}
              </h1>
              <p className={'text-[11px] font-mono mt-0.5 ' + (isDark ? 'text-white/40' : 'text-slate-500')}>
                {workspace.id}
              </p>
            </div>
          </div>
          <div className="flex items-center gap-2">
            <ActionButton isDark={isDark} icon={Save} label="Kaydet" variant="primary" />
            <ActionButton isDark={isDark} icon={Plus} label="Yeni" variant="secondary" />
          </div>
        </div>

        {/* Genel Bilgiler */}
        <FormCard isDark={isDark} title="Genel Bilgiler">
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            {keys.map(function(k) {
              var field = formData[k]
              return (
                <FormField
                  key={k}
                  isDark={isDark}
                  field={field}
                  value={formValues[k]}
                  onChange={function(v) { onFieldChange(k, v) }}
                />
              )
            })}
          </div>
        </FormCard>

        {/* Placeholder: admin panelden gelen dinamik gruplar */}
        <FormCard isDark={isDark} title="Ek Alanlar">
          <p className={'text-[12px] ' + (isDark ? 'text-white/40' : 'text-slate-500')}>
            Dinamik alanlar Admin → Ekran Tasarimi ekranindan eklendiginde burada katlanir gruplar
            halinde gorunecek (Stok Bilgileri, Finansal Bilgiler, vb.).
          </p>
        </FormCard>
      </div>
    </main>
  )
}

function FormCard(props) {
  var isDark = props.isDark
  var cardStyle = isDark
    ? { background: 'rgba(255, 255, 255, 0.04)', border: '1px solid rgba(255, 255, 255, 0.08)' }
    : { background: 'rgba(255, 255, 255, 0.75)', border: '1px solid rgba(15, 23, 42, 0.08)', boxShadow: '0 2px 10px rgba(15,23,42,0.04)' }

  return (
    <div
      className="rounded-2xl p-5 mb-4"
      style={Object.assign({
        backdropFilter: 'blur(16px)',
        WebkitBackdropFilter: 'blur(16px)',
      }, cardStyle)}
    >
      <p className={
        'text-[10px] font-bold uppercase tracking-[0.08em] mb-4 flex items-center gap-2 ' +
        (isDark ? 'text-white/40' : 'text-slate-500')
      }>
        <span
          className="w-1.5 h-1.5 rounded-full"
          style={{ background: '#6366f1', boxShadow: '0 0 6px rgba(99,102,241,0.8)' }}
        />
        {props.title}
      </p>
      {props.children}
    </div>
  )
}

function FormField(props) {
  var field = props.field
  var isDark = props.isDark
  var value = props.value
  var onChange = props.onChange

  if (field.dataType === 'checkbox') {
    return (
      <div className={
        'flex items-center justify-between px-4 py-3 rounded-xl md:col-span-2 ' +
        (isDark ? 'bg-white/[0.03] border border-white/[0.08]' : 'bg-slate-50 border border-slate-200')
      }>
        <div>
          <label className={'text-[13px] font-semibold ' + (isDark ? 'text-white/85' : 'text-slate-800')}>
            {field.label}
          </label>
          <p className={'text-[10px] mt-0.5 ' + (isDark ? 'text-white/35' : 'text-slate-500')}>
            Cesit/varyant bazli stok takibi yapilsin
          </p>
        </div>
        <button
          onClick={function() { onChange(!value) }}
          className={
            'relative w-11 h-6 rounded-full transition-colors ' +
            (value ? 'bg-emerald-500/75' : (isDark ? 'bg-white/10' : 'bg-slate-300'))
          }
        >
          <motion.div
            className="absolute top-0.5 w-5 h-5 rounded-full bg-white shadow"
            animate={{ left: value ? 22 : 2 }}
            transition={{ type: 'spring', stiffness: 500, damping: 30 }}
          />
        </button>
      </div>
    )
  }

  var isNumeric = field.dataType === 'numeric'

  return (
    <div>
      <label className={
        'block text-[10px] font-bold uppercase tracking-wider mb-1.5 ' +
        (isDark ? 'text-white/40' : 'text-slate-500')
      }>
        {field.label}
      </label>
      <div className="relative">
        {isNumeric && (
          <Hash size={12} className={'absolute left-3 top-1/2 -translate-y-1/2 ' + (isDark ? 'text-white/30' : 'text-slate-400')} />
        )}
        <input
          type="text"
          value={value == null ? '' : String(value)}
          onChange={function(e) { onChange(e.target.value) }}
          className={
            'w-full py-2.5 rounded-xl text-sm font-medium transition-all focus:outline-none ' +
            (isNumeric ? 'pl-9 pr-3 font-mono' : 'px-3.5') + ' ' +
            (isDark
              ? 'bg-white/[0.04] border border-white/10 text-white placeholder:text-white/25 focus:border-indigo-400/50 focus:bg-white/[0.06] focus:shadow-[0_0_0_3px_rgba(99,102,241,0.15)]'
              : 'bg-white border border-slate-200 text-slate-800 focus:border-indigo-400/80 focus:shadow-[0_0_0_3px_rgba(99,102,241,0.15)]')
          }
        />
      </div>
    </div>
  )
}

function ActionButton(props) {
  var Icon = props.icon
  var isDark = props.isDark
  var isPrimary = props.variant === 'primary'

  var classes = isPrimary
    ? 'text-white'
    : (isDark
      ? 'text-white/70 hover:text-white border border-white/10 bg-white/[0.04] hover:bg-white/[0.08]'
      : 'text-slate-700 hover:text-slate-900 border border-slate-200 bg-white hover:bg-slate-50')

  var primaryStyle = isPrimary
    ? {
        background: 'linear-gradient(135deg, #6366f1 0%, #8b5cf6 100%)',
        boxShadow: '0 6px 16px rgba(99,102,241,0.35)',
      }
    : {}

  return (
    <motion.button
      whileTap={{ scale: 0.97 }}
      className={'flex items-center gap-2 px-4 py-2 rounded-xl text-[12px] font-semibold transition-all ' + classes}
      style={primaryStyle}
    >
      <Icon size={13} strokeWidth={2.2} />
      <span>{props.label}</span>
    </motion.button>
  )
}

function EmptyTab(props) {
  var isDark = props.isDark
  return (
    <div className={'h-full flex items-center justify-center ' + (isDark ? 'text-white/30' : 'text-slate-400')}>
      <div className="text-center">
        <Boxes size={48} className="mx-auto mb-3 opacity-40" strokeWidth={1.2} />
        <p className="text-sm">Hicbir sekme acik degil</p>
        <p className="text-[11px] mt-1">Sol menuden bir sayfa acin</p>
      </div>
    </div>
  )
}

/* ══════════════════════════════════════════════════════════════
   Status bar
   ══════════════════════════════════════════════════════════════ */
function StatusBar(props) {
  var isDark = props.isDark
  var borderColor = isDark ? 'border-white/[0.06]' : 'border-slate-200/80'
  var bgColor = isDark ? 'bg-[#0a0d17]/70' : 'bg-white/70'
  var textColor = isDark ? 'text-white/40' : 'text-slate-500'
  var dividerColor = isDark ? 'text-white/15' : 'text-slate-300'

  return (
    <footer
      className={
        'flex items-center justify-between px-5 h-6 text-[10px] border-t backdrop-blur-xl flex-shrink-0 font-mono tracking-wide ' +
        borderColor + ' ' + bgColor + ' ' + textColor
      }
    >
      <div className="flex items-center gap-3">
        <span className="flex items-center gap-1.5">
          <CheckCircle2 size={10} className="text-emerald-400" />
          <span>{props.system.status}</span>
        </span>
        <span className={dividerColor}>|</span>
        <span className="flex items-center gap-1.5">
          <Wifi size={10} className="text-emerald-400" />
          <span>Baglanti aktif</span>
        </span>
      </div>

      <div className="flex items-center gap-3">
        <span className="flex items-center gap-1.5">
          <Building2 size={10} />
          <span>{props.system.company}</span>
        </span>
        <span className={dividerColor}>·</span>
        <span>{props.system.year}</span>
      </div>

      <div className="flex items-center gap-3">
        <span className="flex items-center gap-1.5">
          <Clock size={10} />
          <span>26/04/10</span>
        </span>
        <span className={dividerColor}>|</span>
        <span className="flex items-center gap-1.5">
          <UserCircle size={10} />
          <span>{props.user.name}</span>
        </span>
      </div>
    </footer>
  )
}
