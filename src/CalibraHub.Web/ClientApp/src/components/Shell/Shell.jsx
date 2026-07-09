/**
 * Shell — CalibraHub uretim kabugu (Main Layout/Wrapper)
 *
 * Glassmorphism navbar + sidebar + tabs + status bar. Eski _Layout.cshtml
 * kabugunun yerine gecer. Body alani iframe tab'lardir — her tab'in src'si
 * ilgili URL'nin workspace=1 flag'li versiyonudur (server bu moda navbar/sidebar
 * rendir etmez, sadece minimal sayfa icerigi).
 *
 * Props:
 *   config: {
 *     user: { name, email, initials, userKey },
 *     system: { company, year, status },
 *     menu: MenuNode[],                            // MenuDefinition.GetMainMenu sonucu
 *     theme: 'dark' | 'light',
 *     lang: 'tr-TR' | 'en-US',
 *     initialUrl: string,                          // Ilk tab icin acilacak URL
 *     savePreferencesUrl: string,                  // /Account/SaveInterfacePreferences
 *     antiforgeryToken: string,
 *   }
 *
 * MenuNode: { key, label, icon, url, children }
 */
import { useState, useEffect, useRef, useCallback } from 'react'
import { motion, AnimatePresence } from 'framer-motion'
import * as notifApi from '../../services/notificationsService'
// 2026-05-23 — Yapay zeka asistanı (sağ alt floating widget). Top-level Shell altında
// global mount edilir → workspace tab iframe'lerinin DIŞINDA, her sayfada görünür.
import AiFloatingButton from '../AiAssistant/AiFloatingButton'
import SessionIdleGuard from './SessionIdleGuard'
// 2026-06-14 — Ana sayfa özelleştirilebilir pano. Hiç sekme açık değilken
// (isHomePage) EmptyState yerine doğrudan Shell içinde render edilir (iframe yok).
import Dashboard from '../Dashboard/Dashboard'
import {
  // Shell internals
  Sparkles, ChevronLeft, ChevronRight, CircleDot, Bell, BellRing, Moon, Sun, Search,
  Layers, MessageSquare, Languages, UserCircle, LogOut, Bot, Menu,
  X, LayoutGrid, Building2, Check, Home,
  // Menu icons (MenuDefinition'dan gelir)
  LayoutList, FileText, Files, Archive, Truck,
  Package, Folder, Boxes, Sliders, TrendingUp,
  Factory, Network, Coins, Users, Settings2,
  DollarSign, MapPin, Ruler, Tag, Settings,
  Plug, Mail, Database, Zap, UserCog,
  BookOpen, Clock,
  Warehouse, ArrowLeftRight, PackagePlus, PackageMinus, PackageCheck, ClipboardCheck
} from 'lucide-react'

/* ══════════════════════════════════════════════════════════════
   Shell çeviri tablosu — TR / EN metin çiftleri.
   tShell(key, lang) ile kullanılır; lang değeri 'TR' veya 'EN'. */
var SHELL_I18N = {
  // Sidebar
  menu_show:               { TR: 'Menüyü göster',                    EN: 'Show menu' },
  menu_hide:               { TR: 'Menüyü gizle',                     EN: 'Hide menu' },
  search_placeholder:      { TR: 'Menüde ara...',                    EN: 'Search menu...' },
  search_clear:            { TR: 'Aramayı temizle',                  EN: 'Clear search' },
  no_match:                { TR: 'Eşleşme bulunamadı',               EN: 'No matches found' },
  // EmptyState
  no_tabs_title:           { TR: 'Hiçbir sekme açık değil',          EN: 'No tabs open' },
  no_tabs_sub:             { TR: 'Sol menüden bir sayfa açın',       EN: 'Open a page from the left menu' },
  // Notifications (Header)
  notifications:           { TR: 'Bildirimler',                      EN: 'Notifications' },
  unread_notif:            { TR: 'okunmamış bildirim',               EN: 'unread notification(s)' },
  notif_new:               { TR: 'yeni',                             EN: 'new' },
  mark_all_read:           { TR: 'Tümünü okundu say',                EN: 'Mark all as read' },
  no_notifications:        { TR: 'Bildirim yok.',                    EN: 'No notifications.' },
  open_pages:              { TR: 'Açık Sayfalar',                    EN: 'Open Pages' },
  // OpenTabsPopover
  pages_open_suffix:       { TR: 'sayfa açık',                       EN: 'pages open' },
  close_all_title:         { TR: 'Tüm sekmeleri kapat',              EN: 'Close all tabs' },
  close_all_btn:           { TR: 'Tümünü Kapat',                     EN: 'Close All' },
  no_pages:                { TR: 'Hiçbir sayfa açık değil.',         EN: 'No pages open.' },
  unsaved_prefix:          { TR: 'Kaydedilmemiş değişiklik: ',       EN: 'Unsaved changes: ' },
  close_tab:               { TR: 'Sekmeyi kapat',                    EN: 'Close tab' },
  // CloseConfirmModal
  close_all_confirm_title: { TR: 'Tüm Sayfaları Kapat?',            EN: 'Close All Pages?' },
  close_all_dirty_msg:     { TR: ' sayfada kaydedilmemiş değişiklik var. Tüm sekmeleri kapatmak istiyor musunuz?', EN: ' page(s) have unsaved changes. Close all tabs?' },
  close_all_clean_msg:     { TR: 'Tüm sekmeleri kapatmak istiyor musunuz?', EN: 'Close all tabs?' },
  single_close_dirty:      { TR: 'Bu sayfada kaydedilmemiş değişiklik var. Kapatmak istiyor musunuz?', EN: 'This page has unsaved changes. Close anyway?' },
  single_close_title:      { TR: 'Sayfayı Kapat?',                  EN: 'Close Page?' },
  countdown:               { TR: 'saniye içinde iptal edilmezse otomatik kapatılır.', EN: "second(s) — will close automatically if not cancelled." },
  cancel:                  { TR: 'İptal',                            EN: 'Cancel' },
  close:                   { TR: 'Kapat',                            EN: 'Close' },
  // ProfilePopover
  messages:                { TR: 'Mesajlar',                         EN: 'Messages' },
  language:                { TR: 'Dil',                              EN: 'Language' },
  theme:                   { TR: 'Tema',                             EN: 'Theme' },
  theme_dark:              { TR: 'Koyu',                             EN: 'Dark' },
  theme_light:             { TR: 'Açık',                             EN: 'Light' },
  profile_info:            { TR: 'Profil Bilgileri',                 EN: 'Profile' },
  sign_out:                { TR: 'Çıkış Yap',                       EN: 'Sign Out' },
  ai_assistant:            { TR: 'Calibo',                           EN: 'Calibo' },
  // Connection overlay
  conn_lost:               { TR: 'Bağlantı Kesildi',                EN: 'Connection Lost' },
  conn_restored:           { TR: 'Bağlantı Geri Geldi!',            EN: 'Connection Restored!' },
  conn_restored_msg:       { TR: '✓ Sunucu tekrar erişilebilir. Sayfalar yükleniyor...', EN: '✓ Server is reachable again. Pages loading...' },
  conn_lost_msg:           { TR: 'Sunucu ile iletişim kurulamıyor. Bu genellikle kısa süreli bir kesintidir; sunucu hazır olduğunda otomatik bağlanacağız.', EN: "Unable to reach the server. This is usually a brief interruption; we'll reconnect automatically when the server is ready." },
  retrying:                { TR: 'Yeniden deneniyor...',             EN: 'Retrying...' },
  try_now:                 { TR: 'Şimdi Dene',                       EN: 'Try Now' },
  refresh_page:            { TR: 'Sayfayı Yenile',                  EN: 'Refresh Page' },
}

function tShell(key, lang) {
  var entry = SHELL_I18N[key]
  if (!entry) return key
  return entry[lang] || entry.TR || key
}

/* Menu icon name → React bileseni haritasi. Tree-shaking icin named import
   + sabit lookup objesi. Bilinmeyen adda CircleDot fallback. */
var ICON_MAP = {
  // Shell internals (fallback icin de cagrilabilir)
  Sparkles: Sparkles, ChevronRight: ChevronRight, CircleDot: CircleDot,
  Bell: Bell, Moon: Moon, Sun: Sun, Menu: Menu,
  Layers: Layers, MessageSquare: MessageSquare, Languages: Languages,
  UserCircle: UserCircle, LogOut: LogOut, X: X, LayoutGrid: LayoutGrid,
  Building2: Building2,
  // Menu icons
  LayoutList: LayoutList, FileText: FileText, Files: Files,
  Archive: Archive, Truck: Truck, Package: Package, Folder: Folder,
  Boxes: Boxes, Sliders: Sliders, TrendingUp: TrendingUp,
  Factory: Factory, Network: Network, Coins: Coins, Users: Users,
  Settings2: Settings2, DollarSign: DollarSign, MapPin: MapPin,
  Ruler: Ruler, Tag: Tag, Settings: Settings, Plug: Plug,
  Mail: Mail, Database: Database, Zap: Zap, UserCog: UserCog,
  BookOpen: BookOpen, Clock: Clock,
  Warehouse: Warehouse, ArrowLeftRight: ArrowLeftRight,
  PackagePlus: PackagePlus, PackageMinus: PackageMinus,
  PackageCheck: PackageCheck, ClipboardCheck: ClipboardCheck,
}

function resolveIcon(name) {
  if (!name) return CircleDot
  return ICON_MAP[name] || CircleDot
}

/* URL'ye ?workspace=1 flag'i ekle (zaten varsa dokunma). */
function appendWorkspaceFlag(url) {
  if (!url) return '/?workspace=1'
  if (url.indexOf('workspace=1') !== -1) return url
  return url + (url.indexOf('?') !== -1 ? '&' : '?') + 'workspace=1'
}

/* Menuyu dolasarak key → parent key'leri haritasi olustur.
   Aktif bir dugumun parent zincirini expand etmek icin kullanilir. */
function buildParentMap(menu) {
  var out = {}
  function walk(node, parents) {
    out[node.key] = parents.slice()
    if (Array.isArray(node.children)) {
      var nextParents = parents.concat([node.key])
      node.children.forEach(function(c) { walk(c, nextParents) })
    }
  }
  menu.forEach(function(n) { walk(n, []) })
  return out
}

/* Aktif URL'ye karsilik gelen menu node key'ini bul. */
function findKeyByUrl(menu, url) {
  if (!url) return null
  // URL'nin query parametrelerini ignore et (?workspace=1 gibi)
  var cleanUrl = url.split('?')[0].toLowerCase()
  var found = null
  function walk(node) {
    if (found) return
    if (node.url) {
      var nodeUrl = node.url.split('?')[0].toLowerCase()
      if (nodeUrl === cleanUrl) { found = node.key; return }
    }
    if (Array.isArray(node.children)) node.children.forEach(walk)
  }
  menu.forEach(walk)
  return found
}

/* URL'ye karsilik gelen menu node label'ini bul (tab basligi icin). */
function findLabelByUrl(menu, url) {
  if (!url) return null
  var cleanUrl = url.split('?')[0].toLowerCase()
  var found = null
  function walk(node) {
    if (found) return
    if (node.url) {
      var nodeUrl = node.url.split('?')[0].toLowerCase()
      if (nodeUrl === cleanUrl) { found = node.label; return }
    }
    if (Array.isArray(node.children)) node.children.forEach(walk)
  }
  menu.forEach(walk)
  return found
}

/* ══════════════════════════════════════════════════════════════
   Ana Shell bileseni
   ══════════════════════════════════════════════════════════════ */
export default function Shell(props) {
  var config = props.config || {}
  var user = config.user || { name: '—', email: '', initials: '?', userKey: 'anon' }
  var system = config.system || { company: '', year: '', status: 'Hazir', appVersion: '?' }
  var initialUrl = config.initialUrl || '/'
  var savePrefsUrl = config.savePreferencesUrl || '/Account/SaveInterfacePreferences'
  var antiforgery = config.antiforgeryToken || ''

  /* ── Menü state — sayfa yüklemesinde config'den gelir, focus'ta sunucudan tazelenir ── */
  var [menu, setMenu] = useState(function() {
    return Array.isArray(config.menu) ? config.menu : []
  })
  var lastMenuFetchRef = useRef(0)

  /* Menüyü sunucudan çek, değiştiyse state güncelle */
  var refreshMenu = useCallback(function(force) {
    var now = Date.now()
    // En fazla 30sn'de bir istek at (gereksiz yükü önle); force=true throttle'ı atlar
    if (!force && now - lastMenuFetchRef.current < 30000) return
    lastMenuFetchRef.current = now
    fetch('/Account/GetMenuItems', { credentials: 'same-origin' })
      .then(function(r) { return r.ok ? r.json() : null })
      .then(function(data) {
        if (data && Array.isArray(data.menu)) {
          setMenu(data.menu)
        }
      })
      .catch(function() { /* sessiz hata — ağ sorunu, ignore */ })
  }, [])

  /* Sekme öne gelince veya pencere focus kazanınca menüyü tazele */
  useEffect(function() {
    function onVisible() {
      if (!document.hidden) refreshMenu()
    }
    function onFocus() { refreshMenu() }
    document.addEventListener('visibilitychange', onVisible)
    window.addEventListener('focus', onFocus)

    /* BroadcastChannel: Yetki Yönetimi kayıt sonrası tüm sekmelere force-refresh sinyali */
    var bc = null
    try {
      bc = new BroadcastChannel('calibra-menu-refresh')
      bc.onmessage = function() { refreshMenu(true) }
    } catch(e) { /* BroadcastChannel desteklenmiyor */ }

    return function() {
      document.removeEventListener('visibilitychange', onVisible)
      window.removeEventListener('focus', onFocus)
      if (bc) bc.close()
    }
  }, [refreshMenu])

  /* ── Tema / dil ────────────────────────────── */
  var [isDark, setIsDark] = useState(function() {
    return (config.theme || 'dark').toLowerCase() === 'dark'
  })
  var [lang, setLang] = useState(function() {
    return (config.lang || 'tr-TR').toLowerCase().indexOf('en') === 0 ? 'EN' : 'TR'
  })

  /* ── Matruska Guard: Shell aktifken body'yi isaretle ──────────
     mountShellRedesignDemo bu attribute'u gorurse iceri girmez.
     Unmount edilince temizlenir (StrictMode double-invoke'a karsi
     return ile cleanup yapilir). */
  useEffect(function() {
    document.body.setAttribute('data-calibra-shell', 'true')
    return function() {
      document.body.removeAttribute('data-calibra-shell')
    }
  }, []) // yalnizca mount / unmount'ta calis

  /* html.dark + body.app-theme-* sync (site.css override'lari icin)
     Ayrica tum iframe tab'larin body class'ini da senkronize et —
     boylece tema degisiminde sayfa icerigi de (iframe) kendi CSS
     override'larini (body.app-theme-dark .xxx) dogru uygular. */
  useEffect(function() {
    var html = document.documentElement
    if (isDark) html.classList.add('dark')
    else html.classList.remove('dark')
    html.style.colorScheme = isDark ? 'dark' : 'light'
    var body = document.body
    body.classList.toggle('app-theme-dark', isDark)
    body.classList.toggle('app-theme-light', !isDark)

    // Tum iframe'leri same-origin'i dolas ve body class'larini guncelle
    function applyToIframe(f) {
      try {
        var doc = f.contentDocument
        if (!doc || !doc.body) return
        doc.body.classList.toggle('app-theme-dark', isDark)
        doc.body.classList.toggle('app-theme-light', !isDark)
        var fh = doc.documentElement
        if (fh) {
          if (isDark) fh.classList.add('dark')
          else fh.classList.remove('dark')
          fh.style.colorScheme = isDark ? 'dark' : 'light'
        }
      } catch (e) { /* cross-origin — sessiz gec */ }
    }
    var frames = document.querySelectorAll('iframe')
    frames.forEach(applyToIframe)
  }, [isDark])

  /* Yeni iframe load olunca (veya navigasyon sonrasi) tema'yi tekrar uygula.
     Yoksa yeni acilan tab kendi server-side prefs ile farkli tema'da gelebilir. */
  useEffect(function() {
    function onLoad(e) {
      var t = e.target
      if (t && t.tagName === 'IFRAME') {
        try {
          var doc = t.contentDocument
          if (!doc || !doc.body) return
          doc.body.classList.toggle('app-theme-dark', isDark)
          doc.body.classList.toggle('app-theme-light', !isDark)
          var fh = doc.documentElement
          if (fh) {
            if (isDark) fh.classList.add('dark')
            else fh.classList.remove('dark')
          }
        } catch (err) { /* cross-origin */ }
      }
    }
    document.addEventListener('load', onLoad, true)
    return function() { document.removeEventListener('load', onLoad, true) }
  }, [isDark])

  /* ── Profile popover ───────────────────────── */
  var [profileOpen, setProfileOpen] = useState(false)
  var [openTabsOpen, setOpenTabsOpen] = useState(false)
  var [dirtyTabs, setDirtyTabs] = useState({}) // { tabKey: true }
  var iframeRefs = useRef({})

  /* ── Sidebar acik/kapali (kullanici tercihi, localStorage'a yazilir) ── */
  var [sidebarOpen, setSidebarOpen] = useState(function() {
    try {
      if (typeof window !== 'undefined' && window.innerWidth < 768) return false
      var v = localStorage.getItem('calibra.sidebarOpen')
      return v === null ? true : v === '1'
    } catch (e) { return true }
  })
  var [isMobile, setIsMobile] = useState(function() {
    return typeof window !== 'undefined' && window.innerWidth < 768
  })
  useEffect(function() {
    try { localStorage.setItem('calibra.sidebarOpen', sidebarOpen ? '1' : '0') } catch (e) { /* ignore */ }
  }, [sidebarOpen])
  useEffect(function() {
    function onResize() {
      var mobile = window.innerWidth < 768
      setIsMobile(mobile)
      if (mobile) setSidebarOpen(false)
    }
    window.addEventListener('resize', onResize, { passive: true })
    return function() { window.removeEventListener('resize', onResize) }
  }, [])
  function toggleSidebar() { setSidebarOpen(function(v) { return !v }) }

  /* ── Dashboard görünümü — tabları kapatmadan ana sayfaya geçiş ── */
  var [showDashboard, setShowDashboard] = useState(false)

  function handleLogoClick() { setShowDashboard(true) }

  /* Alt+H global kısayolu — input alanında değilse ana sayfaya (Dashboard) geçer.
     Ana Sayfa butonuyla aynı davranış: tab'lar kapanmaz, Dashboard view açılır. */
  useEffect(function () {
    function onAltH(e) {
      if (!e.altKey || e.ctrlKey || e.metaKey || e.shiftKey) return
      if ((e.key || '').toLowerCase() !== 'h') return
      var t = e.target
      var tag = (t && t.tagName) ? t.tagName.toLowerCase() : ''
      if (tag === 'input' || tag === 'textarea' || tag === 'select') return
      if (t && t.isContentEditable) return
      e.preventDefault()
      setShowDashboard(true)
    }
    window.addEventListener('keydown', onAltH)
    return function () { window.removeEventListener('keydown', onAltH) }
  }, [])

  /* F3 — menü arama inputuna odaklan; sidebar kapalıysa önce aç */
  var sidebarSearchRef = useRef(null)
  useEffect(function () {
    function onF3(e) {
      if (e.key !== 'F3') return
      e.preventDefault()
      var wasOpen = sidebarOpen
      if (!wasOpen) setSidebarOpen(true)
      setTimeout(function () {
        if (sidebarSearchRef.current) {
          sidebarSearchRef.current.focus()
          sidebarSearchRef.current.select()
        }
      }, wasOpen ? 0 : 240)
    }
    window.addEventListener('keydown', onF3)
    return function () { window.removeEventListener('keydown', onF3) }
  }, [sidebarOpen])

  /* ── Sidebar tamamen gizle — hangi tab'larin sidebar istegi var ── */
  var [sidebarHideTabKeys, setSidebarHideTabKeys] = useState(function() { return new Set() })
  var forceSidebarHidden = sidebarHideTabKeys.has(activeTabKey)


  /* ── Baglanti durumu izleme — KALDIRILDI (2026-06-08) ─────────────
     Service worker (/sw.js) + /offline.html artık bağlantı koptuğunda
     tek ekran sunuyor: tarayıcı SW intercept'iyle navigation isteklerini
     offline.html'e yönlendiriyor; o sayfa da polling ile sunucu ayağa
     kalkınca otomatik geri dönüyor. İn-app overlay artık gereksiz ve
     kullanıcıyı iki farklı ekranla karşılaştırıyordu. State'ler aşağıda
     constant olarak bırakıldı (downstream JSX referansları için).      */
  var connectionLost = false
  var reconnecting = false
  var manualRetryRef = useRef(function () {})

  /* ── Sidebar expand state ──────────────────── */
  var parentMap = useRef(buildParentMap(menu))
  var initialKey = findKeyByUrl(menu, initialUrl)
  var [expandedNodes, setExpandedNodes] = useState(function() {
    var e = {}
    if (initialKey && parentMap.current[initialKey]) {
      parentMap.current[initialKey].forEach(function(p) { e[p] = true })
    }
    return e
  })
  var [activeMenuKey, setActiveMenuKey] = useState(initialKey)

  function toggleExpand(key) {
    setExpandedNodes(function(prev) {
      var next = Object.assign({}, prev)
      next[key] = !next[key]
      return next
    })
  }

  /* ── Tab state (localStorage) ──────────────────
     Eski site.js ile ayni key formatini korur — backwards compat.

     Onemli: localStorage'da "hic kayit yok" (ilk ziyaret) ile "kayit var
     ama bos array" (kullanici tum tab'lari kapatti) durumlarini AYIRT
     ederiz. Birincisinde initialUrl icin varsayilan tab acariz; ikincisinde
     kullanicinin kapatma niyetine saygi gosterip bos state (EmptyState)
     gosteririz. Aksi halde Ctrl+F5 sonrasi hayalet bir tab tekrar
     uretilir ve kullanici "neden bos bir tab acildi?" der. */
  var tabsStorageKey = 'calibra.workspace.tabs.' + encodeURIComponent(user.userKey || user.email || 'anon')
  var [tabs, setTabs] = useState(function() {
    var rawStored = null
    try { rawStored = localStorage.getItem(tabsStorageKey) } catch (e) { /* quota/private */ }

    // Menu'den URL'ye karsilik gelen label'i bul; yoksa URL path'inden kisa isim uret
    var resolveInitialTitle = function(url) {
      var fromMenu = findLabelByUrl(menu, url)
      if (fromMenu) return fromMenu
      // URL path'inden son segment al, / veya ? ile kes
      var path = url ? url.split('?')[0] : '/'
      if (path === '/' || path === '/Home' || path === '/Home/Index') return 'Ana Sayfa'
      var segs = path.split('/').filter(Boolean)
      return segs.length > 0 ? segs[segs.length - 1] : 'Sayfa'
    }

    var isHomePage = function(url) {
      var p = url ? url.split('?')[0] : '/'
      return p === '/' || p === '/Home' || p === '/Home/Index'
    }

    // Hic kayit yok → ilk ziyaret → ana sayfa ise bos baslat, diger sayfa ise tab ac
    if (rawStored === null) {
      if (isHomePage(initialUrl)) return []
      return [{ key: 'init-' + Date.now(), url: initialUrl, title: resolveInitialTitle(initialUrl) }]
    }

    // Kayit var ama parse edilemiyor → guvenli fallback: bos
    var stored
    try { stored = JSON.parse(rawStored) } catch (e) { stored = [] }
    if (!Array.isArray(stored)) return []

    // Kullanici tum tab'lari kapatmis → kapali kalsin (EmptyState)
    // ANCAK: direkt URL navigasyonu (bookmark, refresh, adres çubuğu) varsa o sayfayı aç.
    // Örn. /Admin/ViewSettings'teyken tüm tabları kap → refresh → Dashboard değil ViewSettings görünmeli.
    if (stored.length === 0) {
      if (!isHomePage(initialUrl)) {
        return [{ key: 'init-' + Date.now(), url: initialUrl, title: resolveInitialTitle(initialUrl) }]
      }
      return []
    }

    // Kayitli tab'lar var; aralarinda mevcut URL var mi? Ana sayfa ise ekleme
    if (!isHomePage(initialUrl)) {
      var hasInitial = stored.some(function(t) { return t.url === initialUrl })
      if (!hasInitial) {
        return stored.concat([{ key: 'init-' + Date.now(), url: initialUrl, title: resolveInitialTitle(initialUrl) }])
      }
    }
    return stored
  })
  var [activeTabKey, setActiveTabKey] = useState(function() {
    if (tabs.length === 0) return null
    var match = tabs.find(function(t) { return t.url === initialUrl })
    return match ? match.key : (tabs[0] && tabs[0].key)
  })

  /* Tabs her degistiginde localStorage'a yaz. */
  useEffect(function() {
    try { localStorage.setItem(tabsStorageKey, JSON.stringify(tabs)) }
    catch (e) { /* quota/private mode — sessiz gec */ }
  }, [tabs, tabsStorageKey])

  /* Aktif tab degistikce tarayicinin outer URL'sini replaceState ile guncelle.
     Boylece Ctrl+F5 kullanicinin su an baktigi sayfayi yeniden yukler (eski
     initial sayfa ya da home degil). pushState degil replaceState — browser
     history stack'i kirlenmesin. Ayrica document.title da guncellenir. */
  useEffect(function() {
    var activeTab = tabs.find(function(t) { return t.key === activeTabKey })
    if (!activeTab || !activeTab.url) return
    try {
      var currentPath = window.location.pathname + window.location.search
      // workspace=1 flag'i iframe'e aittir, outer URL'de olmamali
      var targetUrl = activeTab.url.replace(/([?&])workspace=1(&|$)/, function(_, pre, post) {
        return post === '&' ? pre : (pre === '?' ? '' : '')
      })
      if (currentPath !== targetUrl) {
        window.history.replaceState({}, '', targetUrl)
      }
      if (activeTab.title) {
        document.title = activeTab.title + ' - CalibraHub'
      }
    } catch (e) { /* sessiz gec */ }
  }, [activeTabKey, tabs])

  /* ── Menu click → tab ac veya mevcut tab'a gec ──
     Match stratejisi:
       1) Exact URL match — tam ayni URL'li tab varsa onu aktive et
       2) MatchPath prefix match — node.matchPath set ise, mevcut tablar arasinda
          URL'i bu prefix ile baslayanlari ara. Varsa tab'i AS-IS aktive et
          (URL degistirilmez — kullanicinin edit context'i korunur). Boylece
          ornek: /Logistics/MaterialCardEdit?id=5 acik iken sol menuden
          "Malzeme Kartlari"na tiklayinca yeni tab acmaz, mevcut edit tab'ini aktive eder.
       3) Hicbiri yoksa yeni tab ac. */
  function openNodeAsTab(node) {
    if (!node || !node.url) return
    setShowDashboard(false)
    var existing = tabs.find(function(t) { return t.url === node.url })
    if (existing) {
      setActiveTabKey(existing.key)
      setActiveMenuKey(node.key)
      return
    }
    if (node.matchPath) {
      var prefix = String(node.matchPath).toLowerCase()
      var matched = tabs.find(function(t) {
        try {
          var tPath = (t.url || '').split('?')[0].toLowerCase()
          return tPath === prefix || tPath.indexOf(prefix) === 0
        } catch (_) { return false }
      })
      if (matched) {
        setActiveTabKey(matched.key)
        setActiveMenuKey(node.key)
        return
      }
    }
    var newTab = {
      key: 'tab-' + Date.now() + '-' + Math.floor(Math.random() * 1000),
      url: node.url,
      title: node.label,
    }
    setTabs(function(prev) {
      // Max 24 tab limiti (mevcut site.js ile ayni)
      var next = prev.concat([newTab])
      if (next.length > 24) next = next.slice(next.length - 24)
      return next
    })
    setActiveTabKey(newTab.key)
    setActiveMenuKey(node.key)
  }

  /* ── Ic pencere (iframe) ici tetikleyicilerin kullandigi genel API ──
     Satis teklifi gridinden "Stok Kartina Git" gibi kisayollar,
     window.top.CalibraHub.openWorkspaceTab({ url, title, matchPath })
     cagirarak yeni bir tab acar (mevcut tab'i kapatmadan). matchPath
     verilmisse ayni prefix'e sahip varolan tab varsa URL'i ona aktarir
     (iframe re-mount, yeni ?id=X ile acilir). */
  var openWorkspaceTabRef = useRef(null)
  openWorkspaceTabRef.current = function openWorkspaceTab(arg) {
    if (!arg || !arg.url) return
    var url = String(arg.url)
    var title = arg.title || 'Yeni Sekme'
    var matchPath = arg.matchPath || null
    setShowDashboard(false)

    // 1) Ayni URL ile mevcut tab varsa → sadece aktive et
    var exactExisting = tabs.find(function (t) { return t.url === url })
    if (exactExisting) { setActiveTabKey(exactExisting.key); return }

    // 2) matchPath verilmisse ayni kategoriye ait mevcut tab'i yeni URL ile guncelle
    //    (iframe re-render, kullaniciya yeni belge/id ile ayni tab icinde acilir).
    if (matchPath) {
      var existingByPath = tabs.find(function (t) {
        // Normalize: ?workspace=1 ve query string'i yoksay, path baslangicini kontrol et
        try {
          var tPath = (t.url || '').split('?')[0].toLowerCase()
          var mPath = matchPath.toLowerCase()
          return tPath === mPath || tPath.indexOf(mPath.toLowerCase()) === 0
        } catch (_) { return false }
      })
      if (existingByPath) {
        // URL'i degistir → iframe re-mount, yeni id ile acilir
        setTabs(function (prev) {
          return prev.map(function (t) {
            return t.key === existingByPath.key ? Object.assign({}, t, { url: url, title: title }) : t
          })
        })
        setActiveTabKey(existingByPath.key)
        return
      }
    }

    // 3) Yeni tab ac
    var newTab = {
      key: 'tab-' + Date.now() + '-' + Math.floor(Math.random() * 1000),
      url: url,
      title: title,
    }
    setTabs(function (prev) {
      var next = prev.concat([newTab])
      if (next.length > 24) next = next.slice(next.length - 24)
      return next
    })
    setActiveTabKey(newTab.key)
  }
  // Global API: iframe'den window.top.CalibraHub.openWorkspaceTab(...) ile cagrilir.
  useEffect(function () {
    if (typeof window === 'undefined') return undefined
    window.CalibraHub = window.CalibraHub || {}
    window.CalibraHub.openWorkspaceTab = function (arg) {
      if (openWorkspaceTabRef.current) openWorkspaceTabRef.current(arg)
    }
    return function () {
      if (window.CalibraHub) delete window.CalibraHub.openWorkspaceTab
    }
  }, [])

  // 2026-05-24 — Calibo (AI asistan) navigate event'i: Faz B navigate tool sonucu
  // frontend'e [[CALIBO_NAVIGATE]] marker'i ile gelir → AiFloatingButton bu event'i
  // dispatch eder → burada yakalanip yeni/mevcut workspace tab acilir.
  useEffect(function () {
    function onOpenTab(e) {
      if (!e || !e.detail || !e.detail.url) return
      if (openWorkspaceTabRef.current) {
        openWorkspaceTabRef.current({
          url: e.detail.url,
          title: e.detail.label || 'Calibo',
        })
      }
    }
    window.addEventListener('calibra:open-tab', onOpenTab)
    return function () { window.removeEventListener('calibra:open-tab', onOpenTab) }
  }, [])

  /* ── Tab close ──────────────────────────────── */
  // Ortak kapatma onay modali: kind === 'single' | 'all'
  var [closeConfirm, setCloseConfirm] = useState(null)

  function performCloseSingle(key) {
    setTabs(function(prev) {
      var idx = prev.findIndex(function(t) { return t.key === key })
      var next = prev.filter(function(t) { return t.key !== key })
      if (key === activeTabKey && next.length > 0) {
        var newIdx = Math.max(0, Math.min(idx, next.length - 1))
        setActiveTabKey(next[newIdx].key)
      } else if (next.length === 0) {
        setActiveTabKey(null)
      }
      return next
    })
    setDirtyTabs(function(prev) {
      if (!prev[key]) return prev
      var next = Object.assign({}, prev); delete next[key]; return next
    })
    setSidebarHideTabKeys(function(prev) {
      if (!prev.has(key)) return prev
      var next = new Set(prev); next.delete(key); return next
    })
    delete iframeRefs.current[key]
  }

  function performCloseAll() {
    setTabs([])
    setActiveTabKey(null)
    setDirtyTabs({})
    setSidebarHideTabKeys(new Set())
    iframeRefs.current = {}
  }

  function closeTab(key, e) {
    if (e) e.stopPropagation()
    if (dirtyTabs[key]) {
      var t = tabs.find(function(x) { return x.key === key })
      setCloseConfirm({
        kind: 'single',
        key: key,
        title: tShell('single_close_title', lang),
        message: (t && t.title ? '"' + t.title + '" ' : '') +
                 tShell('single_close_dirty', lang)
      })
      return
    }
    performCloseSingle(key)
  }

  function closeAllTabs() {
    var dirtyCount = Object.keys(dirtyTabs).length
    setCloseConfirm({
      kind: 'all',
      title: tShell('close_all_confirm_title', lang),
      message: dirtyCount > 0
        ? dirtyCount + tShell('close_all_dirty_msg', lang)
        : tShell('close_all_clean_msg', lang)
    })
  }

  function handleCloseConfirmAccept() {
    var c = closeConfirm
    setCloseConfirm(null)
    if (!c) return
    if (c.kind === 'single') performCloseSingle(c.key)
    else if (c.kind === 'all') performCloseAll()
  }
  function handleCloseConfirmCancel() {
    setCloseConfirm(null)
  }

  /* ── Iframe → parent mesaj dinleyicisi (dirty state + sidebar kontrol) ─── */
  useEffect(function() {
    function onMsg(e) {
      var d = e && e.data
      if (!d || typeof d !== 'object') return
      if (d.type === 'calibra:dirty' && d.key) {
        setDirtyTabs(function(prev) {
          var isDirty = !!d.isDirty
          var was = !!prev[d.key]
          if (isDirty === was) return prev
          var next = Object.assign({}, prev)
          if (isDirty) next[d.key] = true; else delete next[d.key]
          return next
        })
      }
      if (d.type === 'calibra:sidebarHide' || d.type === 'calibra:sidebarShow') {
        var sourceKey = null
        var refs = iframeRefs.current
        if (refs) {
          Object.keys(refs).forEach(function(k) {
            var el = refs[k]
            if (el && el.contentWindow && el.contentWindow === e.source) sourceKey = k
          })
        }
        if (sourceKey) {
          setSidebarHideTabKeys(function(prev) {
            var next = new Set(prev)
            if (d.type === 'calibra:sidebarHide') next.add(sourceKey)
            else next.delete(sourceKey)
            return next
          })
        }
      }
    }
    window.addEventListener('message', onMsg)
    return function() { window.removeEventListener('message', onMsg) }
  }, [])

  /* ── Iframe yuklendiginde handshake: tab key'i iframe'e gonder ── */
  function handleIframeLoad(key) {
    var el = iframeRefs.current[key]
    if (!el || !el.contentWindow) return
    try { el.contentWindow.postMessage({ type: 'calibra:init', key: key }, '*') } catch (ex) { /* ignore */ }
  }

  /* ── Alt+N / Insert — "yeni kayit" kisayolunu aktif tab'a forward et ──
     Odak Shell'deyken (sidebar, header) iframe keydown'u duymaz; mesajla
     iletilir. SmartBoard iceride calibra:hotkey mesajini yakalayip primary
     action'i ("Yeni X") calistirir. SmartBoard olmayan sayfalar ignore eder. */
  useEffect(function () {
    function onNewHotkey(e) {
      var isAltN = e.altKey && !e.ctrlKey && !e.metaKey && !e.shiftKey && (e.key || '').toLowerCase() === 'n'
      var isInsert = e.key === 'Insert' && !e.altKey && !e.ctrlKey && !e.metaKey && !e.shiftKey
      if (!isAltN && !isInsert) return
      if (isInsert) {
        var t = e.target
        var tag = (t && t.tagName) ? t.tagName.toLowerCase() : ''
        if (tag === 'input' || tag === 'textarea' || tag === 'select') return
        if (t && t.isContentEditable) return
      }
      if (showDashboard || !activeTabKey) return
      var el = iframeRefs.current[activeTabKey]
      if (el && el.contentWindow) {
        e.preventDefault()
        try { el.contentWindow.postMessage({ type: 'calibra:hotkey', action: 'new' }, '*') } catch (ex) { /* ignore */ }
      }
    }
    window.addEventListener('keydown', onNewHotkey)
    return function () { window.removeEventListener('keydown', onNewHotkey) }
  }, [activeTabKey, showDashboard])

  /* ── Tema/dil tercihlerini backend'e kaydet ───
     Mevcut /Account/SaveInterfacePreferences action'ina FormData POST. */
  var savePreferences = useCallback(async function(updates) {
    try {
      var form = new FormData()
      form.append('__RequestVerificationToken', antiforgery)
      if (updates.theme) form.append('ThemeCode', updates.theme)
      if (updates.languageCode) form.append('LanguageCode', updates.languageCode)
      await fetch(savePrefsUrl, {
        method: 'POST',
        body: form,
        credentials: 'same-origin',
      })
    } catch (e) { console.warn('[Shell] savePreferences:', e) }
  }, [antiforgery, savePrefsUrl])

  function handleToggleTheme() {
    var next = !isDark
    setIsDark(next)
    savePreferences({ theme: next ? 'dark' : 'light' })
  }

  async function handleChangeLang(l) {
    setLang(l)
    await savePreferences({ languageCode: l === 'TR' ? 'tr-TR' : 'en-US' })
    // Dil degisince server localization icin full reload (kayit beklendikten sonra)
    window.location.reload()
  }

  var rootBgClass = isDark ? 'bg-[#0a0d17] text-white' : 'bg-slate-100 text-slate-900'

  return (
    <div className={'fixed inset-0 flex overflow-hidden transition-colors duration-500 ' + rootBgClass}>

      {/* Ambient mesh background */}
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

      {/* Mobil sidebar backdrop — sidebar acikken arka plana tiklaninca kapar */}
      {isMobile && sidebarOpen && !forceSidebarHidden && (
        <div
          style={{
            position: 'fixed', inset: 0, zIndex: 49,
            background: 'rgba(0,0,0,0.5)',
            backdropFilter: 'blur(2px)',
            WebkitBackdropFilter: 'blur(2px)',
          }}
          onClick={function() { setSidebarOpen(false) }}
        />
      )}

      {/* Sol: Sidebar — her zaman gorunur. Collapse sirasinda sadece search+nav
          gizlenir; brand (CalibraHub Premium ERP) ve footer (v1.0.0) sabit kalir.
          Mobilde: position:absolute ile icerik uzerine katmanlanir (flex alanini iskal etmez). */}
      <Sidebar
        isDark={isDark}
        lang={lang}
        menu={menu}
        activeKey={activeMenuKey}
        expandedNodes={expandedNodes}
        onToggleNode={toggleExpand}
        onSelectLeaf={openNodeAsTab}
        system={system}
        onCollapse={toggleSidebar}
        onLogoClick={handleLogoClick}
        collapsed={!sidebarOpen}
        hidden={forceSidebarHidden}
        isMobile={isMobile}
        searchInputRef={sidebarSearchRef}
      />

      {/* Sag: Ana alan */}
      <div className="flex-1 flex flex-col min-w-0 relative z-10">

        <Header
          isDark={isDark}
          lang={lang}
          user={user}
          tabsCount={tabs.length}
          sidebarOpen={sidebarOpen && !forceSidebarHidden}
          onToggleSidebar={toggleSidebar}
          hideSidebarToggle={forceSidebarHidden}
          onProfileClick={function() { setProfileOpen(function(o) { return !o }); setOpenTabsOpen(false) }}
          onOpenTabsClick={function() { setOpenTabsOpen(function(o) { return !o }); setProfileOpen(false) }}
        />

        <AnimatePresence>
          {profileOpen && (
            <>
              <div
                style={{ position: 'fixed', inset: 0, zIndex: 39 }}
                onClick={function() { setProfileOpen(false) }}
              />
              <ProfilePopover
                isDark={isDark}
                user={user}
                lang={lang}
                onLangChange={handleChangeLang}
                onThemeToggle={handleToggleTheme}
                onOpenWorkspaceTab={function(arg) {
                  if (openWorkspaceTabRef.current) openWorkspaceTabRef.current(arg)
                }}
                onClose={function() { setProfileOpen(false) }}
              />
            </>
          )}
          {openTabsOpen && (
            <>
              <div
                style={{ position: 'fixed', inset: 0, zIndex: 39 }}
                onClick={function() { setOpenTabsOpen(false) }}
              />
              <OpenTabsPopover
                isDark={isDark}
                lang={lang}
                tabs={tabs}
                activeTabKey={activeTabKey}
                dirtyTabs={dirtyTabs}
                onTabClick={function(key) {
                    // Popover ACIK KALSIN — kullanici baska bir sayfaya da hemen gecebilsin.
                    // Kapatma sadece disariya tiklama (backdrop) veya kapat butonu ile olur.
                    setShowDashboard(false)
                    setActiveTabKey(key)
                }}
                onTabClose={closeTab}
                onCloseAll={function() { closeAllTabs(); setOpenTabsOpen(false) }}
                onClose={function() { setOpenTabsOpen(false) }}
              />
            </>
          )}
        </AnimatePresence>

        <TabBar
          isDark={isDark}
          lang={lang}
          tabs={tabs}
          activeKey={activeTabKey}
          dirtyTabs={dirtyTabs}
          showDashboard={showDashboard}
          onGoHome={function() { setShowDashboard(true) }}
          onTabClick={function(key) { setShowDashboard(false); setActiveTabKey(key) }}
          onTabClose={closeTab}
        />

        {/* Body: tab iframe'leri (aktif olan visible, digerleri display:none) */}
        <div
          className="flex-1 min-h-0 relative"
          style={{ background: isDark ? '#0a0d17' : '#f8fafc' }}
        >
          {/* Dashboard: tabs yoksa veya home aktifse göster (z-index üste çıkar) */}
          {(tabs.length === 0 || showDashboard) && (
            <div className="absolute inset-0 overflow-auto" style={{ zIndex: 2 }}>
              <Dashboard config={config} />
            </div>
          )}

          {/* iframes: tabs varken HEP mounted (state korunur), aktif+dashboard-değil iken görünür */}
          {tabs.map(function(t) {
            return (
              <iframe
                key={t.key}
                ref={function(el) { if (el) iframeRefs.current[t.key] = el; else delete iframeRefs.current[t.key] }}
                onLoad={function() { handleIframeLoad(t.key) }}
                src={appendWorkspaceFlag(t.url)}
                title={t.title}
                className="absolute inset-0 w-full h-full border-0"
                allow="fullscreen; nfc; microphone; camera"
                allowFullScreen
                style={{
                  display: (!showDashboard && t.key === activeTabKey) ? 'block' : 'none',
                  background: isDark ? '#0a0d17' : '#f8fafc',
                  zIndex: 1,
                }}
              />
            )
          })}
        </div>

      </div>

      {/* ── Baglanti koptu overlay'i — tema uyumlu, sevimli animasyon ──
          Sunucu cevap vermezse (localhost reddetti), iframe'lerin uzerine gelir.
          Server geri gelince otomatik kaybolur + iframe'ler reload edilir. */}
      <AnimatePresence>
        {connectionLost && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.2 }}
            className="fixed inset-0 z-[9500] flex items-center justify-center p-6"
            style={{
              background: isDark ? 'rgba(10,13,23,.85)' : 'rgba(248,250,252,.85)',
              backdropFilter: 'blur(8px)',
              WebkitBackdropFilter: 'blur(8px)',
            }}
          >
            <motion.div
              initial={{ scale: 0.96, y: -8 }}
              animate={{ scale: 1, y: 0 }}
              exit={{ scale: 0.96, y: -8 }}
              transition={{ duration: 0.22, ease: [0.2, 0.8, 0.3, 1] }}
              className={
                'relative w-full max-w-md rounded-2xl overflow-hidden shadow-2xl border ' +
                (isDark
                  ? 'bg-gradient-to-br from-slate-800 to-slate-900 border-white/10'
                  : 'bg-white border-slate-200')
              }
            >
              {/* Ust seridi — kirmizi/amber animasyonlu */}
              <div
                className="h-1"
                style={{
                  background: 'linear-gradient(90deg, #ef4444, #f59e0b, #ef4444)',
                  backgroundSize: '200% 100%',
                  animation: 'shellConnLostShimmer 2s linear infinite',
                }}
              />

              <div className="px-8 py-8 flex flex-col items-center text-center gap-3">
                {/* Sevimli animasyonlu baglanti yok ikonu */}
                <motion.div
                  animate={{ y: [0, -4, 0] }}
                  transition={{ duration: 2, repeat: Infinity, ease: 'easeInOut' }}
                  className={'w-20 h-20 rounded-full flex items-center justify-center ' + (isDark ? 'bg-rose-500/15' : 'bg-rose-50')}
                >
                  <svg
                    width="44" height="44" viewBox="0 0 24 24" fill="none"
                    stroke={isDark ? '#fca5a5' : '#ef4444'}
                    strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"
                  >
                    <path d="M1 1l22 22"/>
                    <path d="M16.72 11.06A10.94 10.94 0 0 1 19 12.55"/>
                    <path d="M5 12.55a10.94 10.94 0 0 1 5.17-2.39"/>
                    <path d="M10.71 5.05A16 16 0 0 1 22.58 9"/>
                    <path d="M1.42 9a15.91 15.91 0 0 1 4.7-2.88"/>
                    <path d="M8.53 16.11a6 6 0 0 1 6.95 0"/>
                    <line x1="12" y1="20" x2="12.01" y2="20"/>
                  </svg>
                </motion.div>

                <h3 className={'text-lg font-bold ' + (isDark ? 'text-white' : 'text-slate-900')}>
                  {reconnecting ? tShell('conn_restored', lang) : tShell('conn_lost', lang)}
                </h3>

                {reconnecting ? (
                  <p className={'text-sm ' + (isDark ? 'text-emerald-300' : 'text-emerald-700')}>
                    {tShell('conn_restored_msg', lang)}
                  </p>
                ) : (
                  <>
                    <p className={'text-sm ' + (isDark ? 'text-white/70' : 'text-slate-600')}>
                      {tShell('conn_lost_msg', lang)}
                    </p>
                    <div className={'flex items-center gap-2 mt-2 text-xs ' + (isDark ? 'text-white/45' : 'text-slate-500')}>
                      <span className="relative flex h-2.5 w-2.5">
                        <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-amber-400 opacity-75"></span>
                        <span className="relative inline-flex rounded-full h-2.5 w-2.5 bg-amber-500"></span>
                      </span>
                      <span>{tShell('retrying', lang)}</span>
                    </div>
                    {/* Manuel kontrol butonlari — polling beklenmeden hemen test eder.
                        Stuck-state durumlarinda kullaniciyi serbest birakir. */}
                    <div className="flex items-center gap-2 mt-4">
                      <button
                        type="button"
                        onClick={function () { if (manualRetryRef.current) manualRetryRef.current() }}
                        className={
                          'px-4 py-2 rounded-lg text-xs font-semibold transition-colors ' +
                          (isDark
                            ? 'bg-indigo-500/20 text-indigo-200 hover:bg-indigo-500/30 border border-indigo-400/30'
                            : 'bg-indigo-50 text-indigo-700 hover:bg-indigo-100 border border-indigo-200')
                        }
                      >
                        {tShell('try_now', lang)}
                      </button>
                      <button
                        type="button"
                        onClick={function () { try { window.location.reload() } catch (_) {} }}
                        className={
                          'px-4 py-2 rounded-lg text-xs font-semibold transition-colors ' +
                          (isDark
                            ? 'bg-white/5 text-white/70 hover:bg-white/10 border border-white/10'
                            : 'bg-slate-100 text-slate-700 hover:bg-slate-200 border border-slate-300')
                        }
                      >
                        {tShell('refresh_page', lang)}
                      </button>
                    </div>
                  </>
                )}
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>

      {/* Shimmer animasyonu — baglanti overlay'i icin. */}
      <style>{`
        @keyframes shellConnLostShimmer {
          0%   { background-position: 0% 0%; }
          100% { background-position: 200% 0%; }
        }
      `}</style>

      {/* Kapatma onay modali — ekran ortasinda, 5 sn geri sayim sonunda kapatir */}
      <AnimatePresence>
        {closeConfirm && (
          <CloseConfirmModal
            isDark={isDark}
            lang={lang}
            title={closeConfirm.title}
            message={closeConfirm.message}
            onAccept={handleCloseConfirmAccept}
            onCancel={handleCloseConfirmCancel}
          />
        )}
      </AnimatePresence>

      {/* 2026-05-23 — Yapay Zeka Asistanı (sağ alt floating widget). Top-level mount —
          workspace tab iframe'lerinin DIŞINDA, her tab altında görünür kalır. */}
      <AiFloatingButton />

      {/* Oturum atalet izleyici — per-company idle timeout + geri sayımlı uyarı + logout.
          Top-level (Shell) mount; iframe aktiviteleri postMessage ile buraya iletilir. */}
      <SessionIdleGuard />
    </div>
  )
}

/* ══════════════════════════════════════════════════════════════
   CloseConfirmModal — 5 sn geri sayim, iptal edilmezse otomatik kapatir
   ══════════════════════════════════════════════════════════════ */
function CloseConfirmModal(props) {
  var isDark = props.isDark
  var DURATION_MS = 5000
  var [remainingMs, setRemainingMs] = useState(DURATION_MS)
  var startRef = useRef(Date.now())
  var timerRef = useRef(null)

  useEffect(function() {
    startRef.current = Date.now()
    function tick() {
      var elapsed = Date.now() - startRef.current
      var rem = Math.max(0, DURATION_MS - elapsed)
      setRemainingMs(rem)
      if (rem <= 0) {
        if (timerRef.current) { clearInterval(timerRef.current); timerRef.current = null }
        props.onAccept()
        return
      }
    }
    timerRef.current = setInterval(tick, 100)
    return function() {
      if (timerRef.current) { clearInterval(timerRef.current); timerRef.current = null }
    }
  }, [])

  useEffect(function() {
    function onKey(e) {
      if (e.key === 'Escape') props.onCancel()
      else if (e.key === 'Enter') props.onAccept()
    }
    document.addEventListener('keydown', onKey)
    return function() { document.removeEventListener('keydown', onKey) }
  }, [])

  var seconds = Math.ceil(remainingMs / 1000)
  var progressPct = (remainingMs / DURATION_MS) * 100

  return (
    <motion.div
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      exit={{ opacity: 0 }}
      transition={{ duration: 0.15 }}
      onClick={props.onCancel}
      className="fixed inset-0 z-[10000] flex items-center justify-center p-5"
      style={{
        background: 'rgba(0,0,0,.55)',
        backdropFilter: 'blur(4px)',
        WebkitBackdropFilter: 'blur(4px)',
      }}
    >
      <motion.div
        initial={{ scale: 0.96, y: -6 }}
        animate={{ scale: 1, y: 0 }}
        exit={{ scale: 0.96, y: -6 }}
        transition={{ duration: 0.18 }}
        onClick={function(e) { e.stopPropagation() }}
        className={
          'w-full max-w-md rounded-2xl overflow-hidden shadow-2xl ' +
          (isDark
            ? 'bg-[#1e293b] border border-white/10 text-white'
            : 'bg-white border border-slate-200 text-slate-900')
        }
      >
        <div className="p-6 flex flex-col items-center text-center gap-3">
          <div className={'w-14 h-14 rounded-full flex items-center justify-center ' + (isDark ? 'bg-rose-500/15' : 'bg-rose-50')}>
            <X size={28} strokeWidth={2.4} className="text-rose-500" />
          </div>
          <h3 className="text-base font-bold">{props.title}</h3>
          <p className={'text-sm ' + (isDark ? 'text-white/70' : 'text-slate-600')}>
            {props.message}
          </p>
          <p className={'text-[11.5px] font-medium mt-1 ' + (isDark ? 'text-white/45' : 'text-slate-500')}>
            <strong>{seconds}</strong> {props.lang === 'EN' ? 'second(s) — will close automatically if not cancelled.' : 'saniye içinde iptal edilmezse otomatik kapatılır.'}
          </p>

          {/* Geri sayim cubugu */}
          <div className={'w-full h-1.5 rounded-full overflow-hidden ' + (isDark ? 'bg-white/8' : 'bg-slate-100')}>
            <div
              style={{
                width: progressPct + '%',
                height: '100%',
                background: 'linear-gradient(90deg,#f43f5e,#ef4444)',
                transition: 'width 100ms linear',
              }}
            />
          </div>

          <div className="flex items-center gap-3 mt-3 w-full">
            <button
              type="button"
              onClick={props.onCancel}
              autoFocus
              className={
                'flex-1 px-4 py-2 rounded-lg text-sm font-bold transition-colors ' +
                (isDark
                  ? 'bg-white/10 text-white border border-white/15 hover:bg-white/20'
                  : 'bg-slate-100 text-slate-800 border border-slate-200 hover:bg-slate-200')
              }
            >
              {tShell('cancel', props.lang)}
            </button>
            <button
              type="button"
              onClick={props.onAccept}
              className="flex-1 px-4 py-2 rounded-lg text-sm font-bold text-white bg-gradient-to-r from-rose-500 to-red-600 hover:from-rose-600 hover:to-red-700 shadow-md shadow-rose-500/30 transition-all flex items-center justify-center gap-1.5"
            >
              <X size={14} strokeWidth={2.6} />
              {tShell('close', props.lang)}
            </button>
          </div>
        </div>
      </motion.div>
    </motion.div>
  )
}

/* ══════════════════════════════════════════════════════════════
   Sidebar
   ══════════════════════════════════════════════════════════════ */

/* Gorunur node listesi + parent haritasi — klavye navigasyonu icin */
function buildNavMeta(tree, expandedNodes) {
  var visibleNodes = []
  var parentMap = {}
  function walk(nodes, parentKey) {
    nodes.forEach(function(node) {
      parentMap[node.key] = parentKey
      visibleNodes.push(node)
      var hasC = Array.isArray(node.children) && node.children.length > 0
      if (hasC && expandedNodes[node.key]) walk(node.children, node.key)
    })
  }
  walk(tree, null)
  return { visibleNodes: visibleNodes, parentMap: parentMap }
}

/* Menuyu recursive filtrele — arama terimine uyan leaf'leri VE onlarin
   ata gruplarini tutar. Parent'lar otomatik acik sayilir (donus degeri
   ikinci element: expandedKeys seti).
   toLocaleLowerCase('tr-TR') kullanilir: i/İ ve ı/I Turkce eslesir. */
function filterMenuTree(menu, term) {
  if (!term) return { tree: menu, expandKeys: null }
  var t = term.toLocaleLowerCase('tr-TR').trim()
  var expand = {}

  function walk(node) {
    var labelHit = (node.label || '').toLocaleLowerCase('tr-TR').indexOf(t) !== -1
    var filteredChildren = []
    if (Array.isArray(node.children)) {
      node.children.forEach(function(c) {
        var kept = walk(c)
        if (kept) filteredChildren.push(kept)
      })
    }
    if (labelHit || filteredChildren.length > 0) {
      // Grup icin children varsa genisletilmis duruma cek
      if (filteredChildren.length > 0) expand[node.key] = true
      return Object.assign({}, node, {
        children: filteredChildren.length > 0 ? filteredChildren : node.children,
      })
    }
    return null
  }

  var filtered = []
  menu.forEach(function(n) {
    var kept = walk(n)
    if (kept) {
      if (Array.isArray(kept.children) && kept.children.length > 0) expand[kept.key] = true
      filtered.push(kept)
    }
  })
  return { tree: filtered, expandKeys: expand }
}

function Sidebar(props) {
  var isDark = props.isDark
  var lang = props.lang || 'TR'
  var collapsed = !!props.collapsed
  var hidden = !!props.hidden
  var borderColor = isDark ? 'border-white/[0.06]' : 'border-slate-200/80'
  var bgColor = isDark ? 'bg-[#0c0f1a]/70' : 'bg-white/70'

  var [searchTerm, setSearchTerm] = useState('')
  var [focusedKey, setFocusedKey] = useState(null)
  var localSearchRef = useRef(null)
  var searchRef = props.searchInputRef || localSearchRef

  var filtered = filterMenuTree(props.menu, searchTerm)
  var displayTree = filtered.tree
  // Arama aktifse tum eslesen zinciri genislet; degilse normal expanded state
  var effectiveExpanded = filtered.expandKeys
    ? Object.assign({}, props.expandedNodes, filtered.expandKeys)
    : props.expandedNodes

  var navMeta = buildNavMeta(displayTree, effectiveExpanded)
  var visibleNodes = navMeta.visibleNodes
  var navParentMap = navMeta.parentMap

  // Arama degisince klavye odagini sifirla
  useEffect(function() { setFocusedKey(null) }, [searchTerm])

  // focusedKey degisince ilgili DOM elementini odakla (animasyon icin 260ms retry)
  useEffect(function() {
    if (!focusedKey) return
    var el = document.querySelector('[data-nodeid="' + focusedKey + '"]')
    if (el) {
      el.focus({ preventScroll: false })
    } else {
      var tid = setTimeout(function() {
        var el2 = document.querySelector('[data-nodeid="' + focusedKey + '"]')
        if (el2) el2.focus({ preventScroll: false })
      }, 260)
      return function() { clearTimeout(tid) }
    }
  }, [focusedKey])

  var isMobile = !!props.isMobile

  return (
    <aside
      className={
        (isMobile
          ? 'absolute z-[50] flex flex-col flex-shrink-0 border-r backdrop-blur-xl transition-colors duration-500 '
          : 'relative z-10 flex flex-col flex-shrink-0 border-r backdrop-blur-xl transition-colors duration-500 ') +
        borderColor + ' ' + bgColor
      }
      style={{
        userSelect: 'none',
        WebkitUserSelect: 'none',
        overflow: 'hidden',
        width: (hidden || collapsed) ? 0 : 260,
        transition: 'width 0.22s cubic-bezier(0.4,0,0.2,1)',
        borderRightWidth: (hidden || collapsed) ? 0 : undefined,
        ...(isMobile ? { top: 0, bottom: 0, left: 0, height: '100%' } : {}),
      }}
    >
      {/* Brand — collapsed iken sadece logo + toggle (dikey), aksi halde
          logo + isim + toggle (yatay). Calibra branding (logo) her durumda gorunur. */}
      <div className={
        'border-b flex-shrink-0 ' + borderColor + ' ' +
        (collapsed
          ? 'flex flex-col items-center gap-1.5 py-3'
          : 'flex items-center gap-2.5 px-5 h-14')
      }>
        <div
          className="w-8 h-8 rounded-xl flex items-center justify-center flex-shrink-0"
          style={{
            background: 'linear-gradient(135deg, #6366f1 0%, #8b5cf6 100%)',
            boxShadow: '0 6px 18px rgba(99,102,241,0.4)',
            cursor: props.onLogoClick ? 'pointer' : undefined,
          }}
          onClick={props.onLogoClick}
          title={props.onLogoClick ? 'Ana sayfaya dön' : undefined}
        >
          <Sparkles size={15} className="text-white" strokeWidth={2.2} />
        </div>
        {!collapsed && (
          <div
            className="flex-1 min-w-0"
            onClick={props.onLogoClick}
            style={{ cursor: props.onLogoClick ? 'pointer' : undefined }}
            title={props.onLogoClick ? 'Ana sayfaya dön' : undefined}
          >
            <h1 className={'text-sm font-bold tracking-tight leading-tight ' + (isDark ? 'text-white' : 'text-slate-900')}>
              CalibraHub
            </h1>
            <p className={'text-[10px] leading-tight ' + (isDark ? 'text-white/55' : 'text-slate-500')}>
              Premium ERP
            </p>
          </div>
        )}
        {props.onCollapse && (
          <button
            onClick={props.onCollapse}
            className={
              'p-1.5 rounded-lg transition-colors flex-shrink-0 ' +
              (isDark ? 'hover:bg-white/10 text-white/55 hover:text-white' : 'hover:bg-slate-100 text-slate-500 hover:text-slate-800')
            }
            title={collapsed ? tShell('menu_show', lang) : tShell('menu_hide', lang)}
            aria-label={collapsed ? tShell('menu_show', lang) : tShell('menu_hide', lang)}
          >
            {collapsed
              ? <ChevronRight size={14} strokeWidth={2} />
              : <ChevronLeft size={14} strokeWidth={2} />}
          </button>
        )}
      </div>

      {/* Search + Nav — sadece bu kisim collapse'a tabidir */}
      {!collapsed && (
        <div className="px-3 pt-3 pb-1 flex-shrink-0">
          <div className="relative">
            <Search
              size={13}
              className={'absolute left-3 top-1/2 -translate-y-1/2 pointer-events-none ' + (isDark ? 'text-white/50' : 'text-slate-400')}
            />
            <input
              ref={searchRef}
              type="text"
              value={searchTerm}
              onChange={function(e) { setSearchTerm(e.target.value) }}
              onKeyDown={function(e) {
                if (e.key === 'ArrowDown') {
                  e.preventDefault()
                  if (visibleNodes.length > 0) setFocusedKey(visibleNodes[0].key)
                } else if (e.key === 'Enter') {
                  e.preventDefault()
                  // Klavyeyle odaklanan varsa onu, yoksa ilk görünür düğümü aç
                  var target = focusedKey ? visibleNodes.find(function(n) { return n.key === focusedKey }) : null
                  if (!target && visibleNodes.length > 0) target = visibleNodes[0]
                  if (target) {
                    var hasC = Array.isArray(target.children) && target.children.length > 0
                    if (hasC) { props.onToggleNode && props.onToggleNode(target.key); setFocusedKey(target.key) }
                    else if (target.url) { props.onSelectLeaf && props.onSelectLeaf(target) }
                  }
                } else if (e.key === 'Escape') {
                  e.preventDefault()
                  if (searchTerm) { setSearchTerm(''); setFocusedKey(null) }
                  else if (searchRef.current) searchRef.current.blur()
                }
              }}
              placeholder={tShell('search_placeholder', lang)}
              style={{ userSelect: 'text', WebkitUserSelect: 'text' }}
              className={
                'w-full pl-9 pr-8 py-1.5 rounded-lg text-[12px] transition-all focus:outline-none ' +
                (isDark
                  ? 'bg-white/[0.04] border border-white/[0.08] text-white placeholder:text-white/50 focus:border-indigo-400/50 focus:bg-white/[0.06]'
                  : 'bg-white/70 border border-slate-200 text-slate-800 placeholder:text-slate-400 focus:border-indigo-400/60')
              }
            />
            {searchTerm && (
              <button
                type="button"
                onClick={function() { setSearchTerm('') }}
                className={
                  'absolute right-2 top-1/2 -translate-y-1/2 w-4 h-4 rounded flex items-center justify-center transition-colors ' +
                  (isDark ? 'text-white/40 hover:text-white/80 hover:bg-white/10' : 'text-slate-400 hover:text-slate-700 hover:bg-slate-200')
                }
                title={tShell('search_clear', lang)}
              >
                <X size={10} strokeWidth={2.4} />
              </button>
            )}
          </div>
        </div>
      )}

      {!collapsed && (
        <nav className="flex-1 overflow-y-auto py-2 px-3 smartcard-widgets-scroll">
          {displayTree.length > 0 ? (
            displayTree.map(function(node) {
              return (
                <SidebarNode
                  key={node.key}
                  node={node}
                  level={0}
                  isDark={isDark}
                  activeKey={props.activeKey}
                  expandedNodes={effectiveExpanded}
                  onToggleNode={props.onToggleNode}
                  onSelectLeaf={props.onSelectLeaf}
                  focusedKey={focusedKey}
                  setFocusedKey={setFocusedKey}
                  visibleNodes={visibleNodes}
                  navParentMap={navParentMap}
                  searchInputRef={searchRef}
                />
              )
            })
          ) : (
            <div className={'text-center py-6 text-[11px] ' + (isDark ? 'text-white/45' : 'text-slate-400')}>
              <Search size={16} className="mx-auto mb-1.5 opacity-60" strokeWidth={1.5} />
              <p>{tShell('no_match', lang)}</p>
            </div>
          )}
        </nav>
      )}

      {/* Footer — page footer gibi her zaman en altta sabit (mt-auto). Collapsed
          iken sadece "v1.0.0" kompakt gorunur, dar moda sigsin. */}
      <div className={'mt-auto border-t flex-shrink-0 ' + borderColor + ' ' +
        (collapsed ? 'px-2 py-2.5' : 'px-4 py-3')}>
        <div className={'flex items-center text-[10px] font-mono ' +
          (isDark ? 'text-white/55' : 'text-slate-500') + ' ' +
          (collapsed ? 'justify-center' : 'gap-2')}>
          {!collapsed && props.system && props.system.company && (
            <>
              <span className="flex items-center gap-1.5 truncate">
                <Building2 size={11} className="flex-shrink-0" />
                <span className="truncate">{props.system.company}</span>
              </span>
              <span className={isDark ? 'text-white/20' : 'text-slate-300'}>·</span>
            </>
          )}
          <span className="flex-shrink-0">{'v' + ((props.system && props.system.appVersion) || '?')}</span>
          {props.system && props.system.runMode && (
            <span className={
              'flex-shrink-0 px-1 rounded text-[9px] font-bold tracking-wide border font-mono ' +
              (props.system.runMode === 'DEV'
                ? (isDark ? 'bg-amber-500/20 text-amber-400 border-amber-500/40' : 'bg-amber-100 text-amber-700 border-amber-300')
                : (isDark ? 'bg-indigo-500/20 text-indigo-400 border-indigo-500/40' : 'bg-indigo-100 text-indigo-700 border-indigo-300'))
            }>{props.system.runMode}</span>
          )}
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
  var expanded = !!props.expandedNodes[node.key]
  var isActive = props.activeKey === node.key
  var isFocused = props.focusedKey === node.key
  var Icon = resolveIcon(node.icon)

  function handleClick() {
    if (hasChildren) {
      props.onToggleNode(node.key)
    } else if (node.url) {
      props.onSelectLeaf(node)
    }
  }

  function handleKeyDown(e) {
    var key = e.key
    var nodes = props.visibleNodes || []
    var idx = nodes.findIndex(function(n) { return n.key === node.key })
    if (key === 'ArrowDown') {
      e.preventDefault()
      if (idx < nodes.length - 1) props.setFocusedKey && props.setFocusedKey(nodes[idx + 1].key)
    } else if (key === 'ArrowUp') {
      e.preventDefault()
      if (idx > 0) {
        props.setFocusedKey && props.setFocusedKey(nodes[idx - 1].key)
      } else {
        props.setFocusedKey && props.setFocusedKey(null)
        if (props.searchInputRef && props.searchInputRef.current) props.searchInputRef.current.focus()
      }
    } else if (key === 'ArrowRight') {
      e.preventDefault()
      if (hasChildren) {
        if (!expanded) props.onToggleNode && props.onToggleNode(node.key)
        if (Array.isArray(node.children) && node.children.length > 0) {
          props.setFocusedKey && props.setFocusedKey(node.children[0].key)
        }
      }
    } else if (key === 'ArrowLeft') {
      e.preventDefault()
      if (hasChildren && expanded) {
        props.onToggleNode && props.onToggleNode(node.key)
      } else {
        var pk = props.navParentMap && props.navParentMap[node.key]
        if (pk) props.setFocusedKey && props.setFocusedKey(pk)
      }
    } else if (key === 'Escape') {
      e.preventDefault()
      props.setFocusedKey && props.setFocusedKey(null)
      if (props.searchInputRef && props.searchInputRef.current) props.searchInputRef.current.focus()
    } else if (key === 'Enter' || key === ' ') {
      e.preventDefault()
      handleClick()
    }
  }

  // Top-level (level 0) ust kategori — biraz daha buyuk ve kalin.
  var base = level === 0
    ? 'flex items-center gap-2.5 w-full px-3 py-2.5 rounded-xl text-[15px] font-semibold cursor-pointer transition-all group'
    : 'flex items-center gap-2.5 w-full px-3 py-2 rounded-xl text-sm font-medium cursor-pointer transition-all group'
  // Aktif menu item: yesil (acik konumdaki sayfayi vurgular)
  var variant = isActive
    ? (isDark
      ? 'bg-emerald-500/20 text-white ring-1 ring-emerald-500/30'
      : 'bg-emerald-100 text-emerald-700 ring-1 ring-emerald-200')
    : (isDark
      ? 'text-white/60 hover:bg-white/[0.05] hover:text-white'
      : 'text-slate-600 hover:bg-slate-100 hover:text-slate-900')
  var focusRing = isFocused ? ' ring-2 ring-inset ring-indigo-400/60' : ''

  return (
    <div>
      <motion.div
        whileTap={{ scale: 0.98 }}
        onClick={handleClick}
        tabIndex={-1}
        data-nodeid={node.key}
        onKeyDown={handleKeyDown}
        onFocus={function() { props.setFocusedKey && props.setFocusedKey(node.key) }}
        className={base + ' ' + variant + focusRing + ' select-none focus:outline-none'}
        style={{
          marginLeft: level * 12,
          // w-full margin'i hesaba katmaz → girintili öğe sağdan taşar ve aktif
          // vurgu çerçevesi sidebar kenarında kırpılır. Genişlik girinti kadar kısılır.
          width: 'calc(100% - ' + (level * 12) + 'px)',
          marginBottom: 2, userSelect: 'none', WebkitUserSelect: 'none',
        }}
      >
        <Icon
          size={level === 0 ? 17 : 15}
          strokeWidth={1.8}
          className={isActive
            ? (isDark ? 'text-emerald-300' : 'text-emerald-600')
            : (isDark ? 'text-white/40 group-hover:text-white/80' : 'text-slate-400 group-hover:text-slate-700')}
        />
        <span className={'flex-1 truncate select-none ' + (level === 0 ? 'text-[15px]' : 'text-[13px]')}>{node.label}</span>
        {hasChildren && (
          <motion.span
            animate={{ rotate: expanded ? 90 : 0 }}
            transition={{ duration: 0.18 }}
            className={isDark ? 'text-white/50' : 'text-slate-400'}
          >
            <ChevronRight size={13} />
          </motion.span>
        )}
        {isActive && !hasChildren && (
          <div className="w-1.5 h-1.5 rounded-full bg-emerald-400 shadow-[0_0_8px_rgba(16,185,129,0.8)]" />
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
                  key={c.key}
                  node={c}
                  level={level + 1}
                  isDark={isDark}
                  activeKey={props.activeKey}
                  expandedNodes={props.expandedNodes}
                  onToggleNode={props.onToggleNode}
                  onSelectLeaf={props.onSelectLeaf}
                  focusedKey={props.focusedKey}
                  setFocusedKey={props.setFocusedKey}
                  visibleNodes={props.visibleNodes}
                  navParentMap={props.navParentMap}
                  searchInputRef={props.searchInputRef}
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
  var lang = props.lang || 'TR'
  var user = props.user
  var borderColor = isDark ? 'border-white/[0.06]' : 'border-slate-200/80'
  var bgColor = isDark ? 'bg-[#0a0d17]/70' : 'bg-white/70'

  /* ── Bildirim dropdown + polling ─────────────
     ReminderNotificationWorker her 60 sn'de bildirim uretebilir; unread
     count'u da 60 sn'de tazeleyelim. Dropdown aciksa full list fetch. */
  var [notifOpen, setNotifOpen] = useState(false)
  var [notifItems, setNotifItems] = useState([])
  var [notifUnread, setNotifUnread] = useState(0)
  var notifBtnRef = useRef(null)
  var notifPanelRef = useRef(null)

  useEffect(function () {
    function refreshCount() {
      notifApi.unreadCount().then(function (d) {
        setNotifUnread((d && d.unreadCount) || 0)
      })
    }
    refreshCount()
    var tid = setInterval(refreshCount, 60000)
    return function () { clearInterval(tid) }
  }, [])

  useEffect(function () {
    if (!notifOpen) return
    notifApi.list(30).then(function (d) {
      setNotifItems((d && d.items) || [])
      setNotifUnread((d && d.unreadCount) || 0)
    })
    function handleOutside(e) {
      if (notifBtnRef.current && notifBtnRef.current.contains(e.target)) return
      if (notifPanelRef.current && notifPanelRef.current.contains(e.target)) return
      setNotifOpen(false)
    }
    document.addEventListener('mousedown', handleOutside)
    return function () { document.removeEventListener('mousedown', handleOutside) }
  }, [notifOpen])

  function handleNotifClick(n) {
    if (!n.isRead) {
      notifApi.markRead(n.id)
      setNotifItems(function (prev) { return prev.map(function (x) { return x.id === n.id ? { ...x, isRead: true } : x }) })
      setNotifUnread(function (c) { return Math.max(0, c - 1) })
    }
    if (n.link) window.location.href = n.link
  }

  function handleMarkAllRead() {
    notifApi.markAllRead()
    setNotifItems(function (prev) { return prev.map(function (x) { return { ...x, isRead: true } }) })
    setNotifUnread(0)
  }

  function formatNotifTime(iso) {
    if (!iso) return ''
    var m = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})/.exec(iso)
    if (!m) return iso
    var now = new Date()
    var y = parseInt(m[1], 10), mo = parseInt(m[2], 10), d = parseInt(m[3], 10)
    if (now.getFullYear() === y && (now.getMonth() + 1) === mo && now.getDate() === d) {
      return m[4] + ':' + m[5]
    }
    return d + '.' + m[2] + '.' + y + ' ' + m[4] + ':' + m[5]
  }

  return (
    <header
      className={
        'relative z-20 flex items-center gap-4 h-14 px-5 border-b backdrop-blur-xl flex-shrink-0 transition-colors duration-500 ' +
        borderColor + ' ' + bgColor
      }
    >
      {/* Mobil hamburger menü — 768px altında görünür, sidebar açar/kapar */}
      {!props.hideSidebarToggle && props.onToggleSidebar && (
        <button
          onClick={props.onToggleSidebar}
          className={
            (props.sidebarOpen ? 'md:hidden ' : '') +
            'p-2 rounded-xl transition-colors flex-shrink-0 ' +
            (isDark ? 'hover:bg-white/5 text-white/60 hover:text-white' : 'hover:bg-slate-100 text-slate-500 hover:text-slate-800')
          }
          aria-label={props.sidebarOpen ? 'Menüyü kapat' : 'Menüyü aç'}
        >
          <Menu size={16} strokeWidth={2} />
        </button>
      )}

      {/* Spacer — Bell + Profil saga yaslanir */}
      <div className="flex-1" />

      <div className="flex items-center gap-2">
        <div className="relative" ref={notifBtnRef}>
          <button
            onClick={function () { setNotifOpen(function (p) { return !p }) }}
            className={
              'relative p-2 rounded-xl transition-colors ' +
              (isDark ? 'hover:bg-white/5 text-white/60 hover:text-white' : 'hover:bg-slate-100 text-slate-500 hover:text-slate-800')
            }
            title={notifUnread > 0 ? (notifUnread + ' ' + tShell('unread_notif', lang)) : tShell('notifications', lang)}
          >
            {notifUnread > 0 ? <BellRing size={15} strokeWidth={1.8} /> : <Bell size={15} strokeWidth={1.8} />}
            {notifUnread > 0 && (
              <span
                className="absolute -top-0.5 -right-0.5 min-w-[16px] h-[16px] px-1 rounded-full text-[9px] font-bold flex items-center justify-center"
                style={{
                  background: 'linear-gradient(135deg,#f43f5e,#e11d48)',
                  color: '#fff',
                  boxShadow: '0 2px 6px rgba(244,63,94,0.45)',
                }}
              >
                {notifUnread > 99 ? '99+' : notifUnread}
              </span>
            )}
          </button>
          <AnimatePresence>
            {notifOpen && (
              <motion.div
                ref={notifPanelRef}
                initial={{ opacity: 0, y: -6 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: -6 }}
                transition={{ duration: 0.15 }}
                className={
                  'absolute right-0 mt-2 max-h-[480px] rounded-xl border overflow-hidden z-50 flex flex-col ' +
                  (isDark ? 'bg-[#15182b] border-white/10 text-white' : 'bg-white border-slate-200 text-slate-800')
                }
                style={{ width: 'min(360px, calc(100vw - 32px))', boxShadow: '0 12px 40px rgba(0,0,0,0.35)' }}
              >
                <div className={'flex items-center justify-between px-4 py-3 border-b ' + (isDark ? 'border-white/10' : 'border-slate-100')}>
                  <div className="flex items-center gap-2">
                    <Bell size={14} />
                    <span className="text-sm font-semibold">{tShell('notifications', lang)}</span>
                    {notifUnread > 0 && (
                      <span className="text-[10px] font-bold px-1.5 py-0.5 rounded bg-rose-500/15 text-rose-500">
                        {notifUnread} {tShell('notif_new', lang)}
                      </span>
                    )}
                  </div>
                  {notifUnread > 0 && (
                    <button
                      onClick={handleMarkAllRead}
                      className={'text-[11px] px-2 py-1 rounded-md transition-colors inline-flex items-center gap-1 ' +
                        (isDark ? 'hover:bg-white/10 text-white/60 hover:text-white' : 'hover:bg-slate-100 text-slate-500 hover:text-slate-800')}
                    >
                      <Check size={11} />
                      {tShell('mark_all_read', lang)}
                    </button>
                  )}
                </div>
                <div className="flex-1 overflow-y-auto">
                  {notifItems.length === 0 && (
                    <div className={'px-4 py-10 text-center text-[12px] italic ' + (isDark ? 'text-white/40' : 'text-slate-400')}>
                      {tShell('no_notifications', lang)}
                    </div>
                  )}
                  {notifItems.map(function (n) {
                    return (
                      <div
                        key={n.id}
                        onClick={function () { handleNotifClick(n) }}
                        className={
                          'px-4 py-3 cursor-pointer border-b transition-colors ' +
                          (isDark ? 'border-white/5 hover:bg-white/5' : 'border-slate-100 hover:bg-slate-50') +
                          (!n.isRead ? (isDark ? ' bg-indigo-500/5' : ' bg-indigo-50/60') : '')
                        }
                      >
                        <div className="flex items-start gap-2">
                          {!n.isRead && (
                            <span className="w-1.5 h-1.5 mt-1.5 rounded-full bg-indigo-500 shadow-[0_0_6px_rgba(99,102,241,0.7)] flex-shrink-0" />
                          )}
                          <div className="flex-1 min-w-0">
                            <div className={'text-[12.5px] font-semibold leading-snug ' + (n.isRead ? (isDark ? 'text-white/70' : 'text-slate-600') : '')}>
                              {n.title}
                            </div>
                            {n.body && (
                              <div className={'text-[11px] mt-0.5 leading-snug line-clamp-2 ' + (isDark ? 'text-white/50' : 'text-slate-500')}>
                                {n.body}
                              </div>
                            )}
                            <div className={'text-[10px] mt-1 ' + (isDark ? 'text-white/35' : 'text-slate-400')}>
                              {formatNotifTime(n.createdAt)}
                            </div>
                          </div>
                        </div>
                      </div>
                    )
                  })}
                </div>
              </motion.div>
            )}
          </AnimatePresence>
        </div>

        <button
          onClick={props.onOpenTabsClick}
          className={
            'relative p-2 rounded-xl transition-colors ' +
            (isDark ? 'hover:bg-white/5 text-white/60 hover:text-white' : 'hover:bg-slate-100 text-slate-500 hover:text-slate-800')
          }
          title={tShell('open_pages', lang)}
        >
          <Layers size={15} strokeWidth={1.8} />
          {props.tabsCount > 0 && (
            <span
              className="absolute -top-0.5 -right-0.5 min-w-[16px] h-[16px] px-1 rounded-full text-[9px] font-bold flex items-center justify-center"
              style={{
                background: 'linear-gradient(135deg,#6366f1,#8b5cf6)',
                color: '#fff',
                boxShadow: '0 2px 6px rgba(99,102,241,0.45)',
              }}
            >
              {props.tabsCount}
            </span>
          )}
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
          {user.initials || '?'}
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
    function onKey(e) {
      if (e.key === 'Escape') props.onClose()
    }
    var t = setTimeout(function() { document.addEventListener('mousedown', onDoc) }, 10)
    document.addEventListener('keydown', onKey)
    return function() {
      clearTimeout(t)
      document.removeEventListener('mousedown', onDoc)
      document.removeEventListener('keydown', onKey)
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
      className="absolute right-2 top-16 z-40 rounded-2xl overflow-hidden"
      style={{
        width: 'min(320px, calc(100vw - 16px))',
        background: glassBg,
        backdropFilter: 'blur(28px) saturate(140%)',
        WebkitBackdropFilter: 'blur(28px) saturate(140%)',
        border: glassBorder,
        boxShadow: '0 20px 60px rgba(0,0,0,0.5)',
      }}
    >
      <div className="p-5 pb-4">
        <div className="flex items-center gap-3">
          <div
            className="w-11 h-11 rounded-xl flex items-center justify-center text-white font-bold text-base"
            style={{
              background: 'linear-gradient(135deg, #6366f1 0%, #8b5cf6 100%)',
              boxShadow: '0 6px 16px rgba(99,102,241,0.3)',
            }}
          >
            {user.initials || '?'}
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

      <div className="py-2 px-2">
        {/* 2026-05-24: Yapay Zeka Asistanı — global custom event ile AI panel'i acar.
            AiFloatingButton component'i bu event'i dinler. Panel sagdan slide-in olur. */}
        <button
          type="button"
          onClick={function() {
            try { window.dispatchEvent(new CustomEvent('calibra:open-ai')) } catch (_) {}
            if (props.onClose) props.onClose()
          }}
          className={
            'w-full flex items-center gap-3 px-3 py-2 rounded-xl transition-colors text-left ' +
            (isDark ? 'hover:bg-white/[0.05]' : 'hover:bg-slate-100')
          }
        >
          <Bot size={15} strokeWidth={1.8} className={isDark ? 'text-indigo-300' : 'text-indigo-500'} />
          <span className={'flex-1 text-[13px] font-medium ' + (isDark ? 'text-white/85' : 'text-slate-700')}>
            Calibo
          </span>
          <span className={
            'text-[10px] font-bold uppercase tracking-wider px-1.5 py-0.5 rounded ' +
            (isDark ? 'bg-indigo-500/20 text-indigo-300' : 'bg-indigo-100 text-indigo-600')
          }>
            AI
          </span>
        </button>

        <PopoverRow isDark={isDark} icon={MessageSquare} label={tShell('messages', props.lang || 'TR')} badge="" />

        {/* Language switch */}
        <div className={
          'flex items-center gap-3 px-3 py-2 rounded-xl ' +
          (isDark ? 'hover:bg-white/[0.04]' : 'hover:bg-slate-100')
        }>
          <Languages size={15} strokeWidth={1.8} className={isDark ? 'text-white/50' : 'text-slate-500'} />
          <span className={'flex-1 text-[13px] font-medium ' + (isDark ? 'text-white/80' : 'text-slate-700')}>
            {tShell('language', props.lang || 'TR')}
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

        {/* Theme — switch yerine tiklanabilir ikon satiri.
            Ikon "hedef durumu" gosterir: dark iken Sun (tiklayinca light'a gec),
            light iken Moon (tiklayinca dark'a gec). */}
        <button
          type="button"
          onClick={props.onThemeToggle}
          className={
            'w-full flex items-center gap-3 px-3 py-2 rounded-xl transition-colors text-left ' +
            (isDark ? 'hover:bg-white/[0.05]' : 'hover:bg-slate-100')
          }
        >
          {isDark
            ? <Sun  size={16} strokeWidth={2} className="text-amber-400" />
            : <Moon size={16} strokeWidth={2} className="text-slate-700" />}
          <span className={'flex-1 text-[13px] font-medium ' + (isDark ? 'text-white/85' : 'text-slate-700')}>
            {tShell('theme', props.lang || 'TR')}
          </span>
          <span className={
            'text-[11px] font-semibold uppercase tracking-wider ' +
            (isDark ? 'text-white/45' : 'text-slate-500')
          }>
            {isDark ? tShell('theme_dark', props.lang || 'TR') : tShell('theme_light', props.lang || 'TR')}
          </span>
        </button>

        <PopoverRow isDark={isDark} icon={UserCircle} label={tShell('profile_info', props.lang || 'TR')}
                    onClick={function() {
                      if (props.onOpenWorkspaceTab) {
                        props.onOpenWorkspaceTab({ url: '/Account/Profile', title: tShell('profile_info', props.lang || 'TR') })
                      }
                      if (props.onClose) props.onClose()
                    }} />
      </div>

      <div className={isDark ? 'h-px bg-white/10' : 'h-px bg-slate-200'} />

      {/* Logout — gercek endpoint'e yonlendir */}
      <div className="p-2">
        <a
          href="/Account/Logout"
          className={
            'w-full flex items-center gap-3 px-3 py-2.5 rounded-xl text-[13px] font-semibold transition-all no-underline ' +
            (isDark
              ? 'text-rose-400 hover:bg-rose-500/15 hover:text-rose-300'
              : 'text-rose-600 hover:bg-rose-50')
          }
        >
          <LogOut size={15} strokeWidth={2} />
          <span>{tShell('sign_out', props.lang || 'TR')}</span>
        </a>
      </div>
    </motion.div>
  )
}

function OpenTabsPopover(props) {
  var isDark = props.isDark
  var lang = props.lang || 'TR'
  var tabs = props.tabs || []
  var ref = useRef(null)

  useEffect(function() {
    function onDoc(e) {
      if (ref.current && !ref.current.contains(e.target)) props.onClose()
    }
    function onKey(e) {
      if (e.key === 'Escape') props.onClose()
    }
    var t = setTimeout(function() { document.addEventListener('mousedown', onDoc) }, 10)
    document.addEventListener('keydown', onKey)
    return function() {
      clearTimeout(t)
      document.removeEventListener('mousedown', onDoc)
      document.removeEventListener('keydown', onKey)
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
      className="absolute right-20 top-16 z-40 w-64 rounded-2xl overflow-hidden"
      style={{
        background: glassBg,
        backdropFilter: 'blur(28px) saturate(140%)',
        WebkitBackdropFilter: 'blur(28px) saturate(140%)',
        border: glassBorder,
        boxShadow: '0 20px 60px rgba(0,0,0,0.5)',
      }}
    >
      <div className="p-3 pb-2 flex items-center gap-2.5">
        <div
          className="w-8 h-8 rounded-lg flex items-center justify-center flex-shrink-0"
          style={{
            background: 'linear-gradient(135deg, #6366f1 0%, #8b5cf6 100%)',
            boxShadow: '0 6px 14px rgba(99,102,241,0.3)',
          }}
        >
          <Layers size={15} strokeWidth={2} className="text-white" />
        </div>
        <div className="flex-1 min-w-0">
          <h3 className={'text-sm font-bold ' + (isDark ? 'text-white' : 'text-slate-900')}>
            {tShell('open_pages', lang)}
          </h3>
          <p className={'text-[11px] ' + (isDark ? 'text-white/45' : 'text-slate-500')}>
            {tabs.length} {tShell('pages_open_suffix', lang)}
          </p>
        </div>
      </div>

      <div className={isDark ? 'h-px bg-white/10' : 'h-px bg-slate-200'} />

      {/* Tumunu Kapat — STATIK: scroll alaninin DISINDA, asagi kaydirinca kaybolmaz.
          Liste arttiginda hep gorunur kalir; danger/rose tema ile listeden ayrisir. */}
      {tabs.length > 0 && (
        <div className="px-2 pt-2 pb-1">
          <motion.div
            whileHover={{ x: 1 }}
            whileTap={{ scale: 0.985 }}
            onClick={function() { if (props.onCloseAll) props.onCloseAll() }}
            className={
              'group flex items-center gap-2 px-3 py-2 rounded-xl cursor-pointer transition-all ' +
              (isDark
                ? 'bg-rose-500/10 hover:bg-rose-500/20 border border-rose-400/25 hover:border-rose-400/50 text-rose-200 hover:text-white'
                : 'bg-rose-50 hover:bg-rose-100 border border-rose-200 hover:border-rose-400 text-rose-700 hover:text-rose-800')
            }
            title={tShell('close_all_title', lang)}
          >
            <span className="flex-1 text-[12.5px] font-semibold">
              {tShell('close_all_btn', lang)}
            </span>
            <span className={'text-[10.5px] font-mono tabular-nums ' + (isDark ? 'text-rose-200/70' : 'text-rose-500')}>
              {tabs.length}
            </span>
          </motion.div>
        </div>
      )}

      <div className="pb-2 px-2 max-h-[420px] overflow-y-auto smartcard-widgets-scroll">
        {tabs.length === 0 && (
          <div className={'px-3 py-6 text-center text-[12px] italic ' + (isDark ? 'text-white/35' : 'text-slate-400')}>
            {tShell('no_pages', lang)}
          </div>
        )}
        {tabs.map(function(t) {
          var isActive = t.key === props.activeTabKey
          var isDirty = !!(props.dirtyTabs && props.dirtyTabs[t.key])
          var dotBg = isDirty
            ? '#22c55e'
            : (isActive ? 'linear-gradient(135deg,#6366f1,#8b5cf6)' : (isDark ? 'rgba(255,255,255,0.25)' : '#cbd5e1'))
          var dotShadow = isDirty
            ? '0 0 8px rgba(34,197,94,0.95), 0 0 14px rgba(34,197,94,0.55)'
            : (isActive ? '0 0 8px rgba(99,102,241,0.8)' : 'none')
          return (
            <div
              key={t.key}
              onClick={function() { if (props.onTabClick) props.onTabClick(t.key) }}
              className={
                'group flex items-center gap-2 px-3 py-2 rounded-xl cursor-pointer transition-colors ' +
                (isActive
                  ? (isDark ? 'bg-indigo-500/15 text-white' : 'bg-indigo-50 text-indigo-900')
                  : (isDark ? 'hover:bg-white/[0.04] text-white/70' : 'hover:bg-slate-100 text-slate-600'))
              }
              title={isDirty ? tShell('unsaved_prefix', lang) + t.title : t.title}
            >
              <span
                className={'w-1.5 h-1.5 rounded-full flex-shrink-0 ' + (isDirty ? 'calibra-dirty-dot' : '')}
                style={{ background: dotBg, boxShadow: dotShadow }}
              />
              <span className="flex-1 truncate text-[12.5px] font-medium">
                {t.title}
              </span>
              <button
                type="button"
                onClick={function(e) {
                  e.stopPropagation()
                  if (props.onTabClose) props.onTabClose(t.key, e)
                }}
                className={
                  'w-6 h-6 rounded flex items-center justify-center transition-all ' +
                  (isDark
                    ? 'bg-rose-500/15 hover:bg-rose-500/30 border border-rose-400/30 text-rose-300 hover:text-rose-100'
                    : 'bg-rose-50 hover:bg-rose-100 border border-rose-200 text-rose-500 hover:text-rose-700')
                }
                title={tShell('close_tab', lang)}
                aria-label={tShell('close_tab', lang)}
              >
                <X size={13} strokeWidth={3} />
              </button>
            </div>
          )
        })}
      </div>
    </motion.div>
  )
}

function PopoverRow(props) {
  var Icon = props.icon
  var isDark = props.isDark
  var className =
    'w-full flex items-center gap-3 px-3 py-2 rounded-xl transition-colors no-underline ' +
    (isDark ? 'hover:bg-white/[0.04]' : 'hover:bg-slate-100')
  var content = (
    <>
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
    </>
  )
  // href verilirse anchor (tarayicida tam sayfa navigasyon); yoksa eski button davranisi.
  if (props.href) {
    return <a href={props.href} className={className}>{content}</a>
  }
  return (
    <button type="button" onClick={props.onClick} className={className}>
      {content}
    </button>
  )
}

/* ══════════════════════════════════════════════════════════════
   Tab bar
   ══════════════════════════════════════════════════════════════ */
function TabBar(props) {
  var isDark = props.isDark
  var lang = props.lang || 'TR'
  var borderColor = isDark ? 'border-white/[0.06]' : 'border-slate-200/80'
  var scrollRef = useRef(null)
  var [canLeft, setCanLeft] = useState(false)
  var [canRight, setCanRight] = useState(false)

  function recomputeOverflow() {
    var el = scrollRef.current
    if (!el) return
    var l = el.scrollLeft
    var max = el.scrollWidth - el.clientWidth
    setCanLeft(l > 1)
    setCanRight(max - l > 1)
  }

  useEffect(function() {
    recomputeOverflow()
    var el = scrollRef.current
    if (!el) return
    el.addEventListener('scroll', recomputeOverflow)
    window.addEventListener('resize', recomputeOverflow)
    return function() {
      el.removeEventListener('scroll', recomputeOverflow)
      window.removeEventListener('resize', recomputeOverflow)
    }
  }, [])

  // Tab listesi degistiginde / aktif tab degistiginde overflow yeniden hesapla
  // ve aktif tab'i gorunur hale getir.
  useEffect(function() {
    recomputeOverflow()
    var el = scrollRef.current
    if (!el || !props.activeKey) return
    var active = el.querySelector('[data-tab-key="' + props.activeKey + '"]')
    if (active && active.scrollIntoView) {
      active.scrollIntoView({ behavior: 'smooth', block: 'nearest', inline: 'nearest' })
    }
  }, [props.tabs.length, props.activeKey])

  function scrollBy(dx) {
    var el = scrollRef.current
    if (!el) return
    el.scrollBy({ left: dx, behavior: 'smooth' })
  }

  function handleWheel(e) {
    // Vertical wheel → horizontal scroll (shift+wheel veya trackpad zaten dogal)
    if (e.deltaY !== 0 && e.deltaX === 0) {
      var el = scrollRef.current
      if (!el) return
      el.scrollLeft += e.deltaY
      e.preventDefault()
    }
  }

  var chevronBtn = 'absolute top-1/2 -translate-y-1/2 z-10 w-6 h-6 rounded-md flex items-center justify-center transition-colors ' +
    (isDark ? 'bg-[#0a0d17] border border-white/10 text-white/60 hover:text-white hover:bg-white/[0.06]'
            : 'bg-white border border-slate-200 text-slate-500 hover:text-slate-900 hover:bg-slate-100')

  var showDash = !!props.showDashboard
  var homeActive = showDash || props.tabs.length === 0

  return (
    <div
      className={'flex items-center h-11 border-b flex-shrink-0 ' + borderColor}
      style={{ background: isDark ? '#0a0d17' : '#f8fafc' }}
    >
      {/* Ana Sayfa butonu — sol tarafta sabit, tab'lar sağa doğru büyür */}
      {typeof props.onGoHome === 'function' && (
        <button
          type="button"
          onClick={props.onGoHome}
          title="Ana Sayfa"
          className={
            'h-full px-3 flex items-center justify-center flex-shrink-0 border-r transition-colors ' +
            borderColor + ' ' +
            (homeActive
              ? (isDark ? 'text-indigo-400 bg-indigo-500/10' : 'text-indigo-600 bg-indigo-50/80')
              : (isDark ? 'text-white/40 hover:text-white/70 hover:bg-white/[0.04]'
                        : 'text-slate-400 hover:text-slate-700 hover:bg-slate-50'))
          }
        >
          <Home size={15} strokeWidth={1.8} />
        </button>
      )}

      {/* Scrollable tab alanı */}
      <div className="relative flex-1 overflow-hidden h-full">
        {canLeft && (
          <button
            type="button"
            onClick={function() { scrollBy(-200) }}
            className={chevronBtn}
            style={{ left: 4 }}
            title="Sola kaydır"
          >
            <ChevronLeft size={14} strokeWidth={2.2} />
          </button>
        )}
        {canRight && (
          <button
            type="button"
            onClick={function() { scrollBy(200) }}
            className={chevronBtn}
            style={{ right: 4 }}
            title="Sağa kaydır"
          >
            <ChevronRight size={14} strokeWidth={2.2} />
          </button>
        )}
        <div
          ref={scrollRef}
          onWheel={handleWheel}
          className="flex items-center gap-1 h-full overflow-x-auto smartcard-widgets-scroll"
          style={{ paddingLeft: canLeft ? 34 : 8, paddingRight: canRight ? 34 : 16 }}
        >
      {props.tabs.map(function(t) {
        var isActive = t.key === props.activeKey && !showDash
        return (
          <div
            key={t.key}
            data-tab-key={t.key}
            onClick={function() { props.onTabClick(t.key) }}
            onMouseDown={function(e) { e.preventDefault() }}
            className={
              'relative flex items-center gap-2 px-3 py-1.5 rounded-lg text-[13px] font-medium cursor-pointer transition-all flex-shrink-0 max-w-[220px] select-none ' +
              (isActive
                ? (isDark ? 'text-white bg-white/[0.06]' : 'text-slate-900 bg-slate-100')
                : (isDark ? 'text-white/50 hover:text-white/80 hover:bg-white/[0.03]' : 'text-slate-500 hover:text-slate-800 hover:bg-slate-50'))
            }
            title={(props.dirtyTabs && props.dirtyTabs[t.key]) ? tShell('unsaved_prefix', lang) + t.title : t.title}
          >
            {props.dirtyTabs && props.dirtyTabs[t.key] && (
              <span
                className="calibra-dirty-dot"
                style={{
                  width: 7, height: 7, borderRadius: 9999, flexShrink: 0,
                  background: '#22c55e',
                  boxShadow: '0 0 8px rgba(34,197,94,0.95), 0 0 14px rgba(34,197,94,0.55)',
                }}
              />
            )}
            <span className="truncate select-none">{t.title}</span>
            <button
              onClick={function(e) { props.onTabClose(t.key, e) }}
              className={
                'w-4 h-4 rounded flex items-center justify-center transition-colors flex-shrink-0 ' +
                (isDark ? 'hover:bg-white/10 text-white/50 hover:text-white/80' : 'hover:bg-slate-200 text-slate-400 hover:text-slate-700')
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
        </div>
      </div>
    </div>
  )
}

function EmptyState(props) {
  var isDark = props.isDark
  var lang = props.lang || 'TR'
  return (
    <div className={'h-full flex items-center justify-center ' + (isDark ? 'text-white/50' : 'text-slate-400')}>
      <div className="text-center">
        <LayoutGrid size={48} className="mx-auto mb-3 opacity-40" strokeWidth={1.2} />
        <p className="text-sm">{tShell('no_tabs_title', lang)}</p>
        <p className="text-[11px] mt-1">{tShell('no_tabs_sub', lang)}</p>
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
        {props.system.company && (
          <span className="flex items-center gap-1.5">
            <Building2 size={10} />
            <span>{props.system.company}</span>
          </span>
        )}
        <span className={dividerColor}>·</span>
        <span>{'v' + ((props.system && props.system.appVersion) || '?')}</span>
        {props.system && props.system.runMode && (
          <span className={
            'px-1 rounded text-[9px] font-bold tracking-wide border ' +
            (props.system.runMode === 'DEV'
              ? (isDark ? 'bg-amber-500/20 text-amber-400 border-amber-500/40' : 'bg-amber-100 text-amber-700 border-amber-300')
              : (isDark ? 'bg-indigo-500/20 text-indigo-400 border-indigo-500/40' : 'bg-indigo-100 text-indigo-700 border-indigo-300'))
          }>{props.system.runMode}</span>
        )}
      </div>
    </footer>
  )
}
