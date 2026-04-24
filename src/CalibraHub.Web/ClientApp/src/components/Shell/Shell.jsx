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
import {
  // Shell internals
  Sparkles, ChevronLeft, ChevronRight, CircleDot, Bell, BellRing, Moon, Sun, Search,
  Layers, MessageSquare, Languages, UserCircle, LogOut,
  X, LayoutGrid, Building2, Check,
  // Menu icons (MenuDefinition'dan gelir)
  LayoutList, FileText, Files, Archive, Truck,
  Package, Folder, Boxes, Sliders, TrendingUp,
  Factory, Network, Coins, Users, Settings2,
  DollarSign, MapPin, Ruler, Tag, Settings,
  Plug, Mail, Database, Zap, UserCog,
  BookOpen, Clock
} from 'lucide-react'

/* Menu icon name → React bileseni haritasi. Tree-shaking icin named import
   + sabit lookup objesi. Bilinmeyen adda CircleDot fallback. */
var ICON_MAP = {
  // Shell internals (fallback icin de cagrilabilir)
  Sparkles: Sparkles, ChevronRight: ChevronRight, CircleDot: CircleDot,
  Bell: Bell, Moon: Moon, Sun: Sun,
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
  var system = config.system || { company: '', year: '', status: 'Hazir' }
  var menu = Array.isArray(config.menu) ? config.menu : []
  var initialUrl = config.initialUrl || '/'
  var savePrefsUrl = config.savePreferencesUrl || '/Account/SaveInterfacePreferences'
  var antiforgery = config.antiforgeryToken || ''

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

  /* ── Bildirim dropdown + polling ─────────────
     ReminderNotificationWorker her 60 sn'de bir bildirim uretebilir;
     biz de 60 sn'de unread count'u tazeleyelim. Dropdown aciksa full list
     yuklenir ve goruntulenir. */
  var [notifOpen, setNotifOpen] = useState(false)
  var [notifItems, setNotifItems] = useState([])
  var [notifUnread, setNotifUnread] = useState(0)
  var notifBtnRef = useRef(null)
  var notifPanelRef = useRef(null)

  useEffect(function () {
    // Baslangic + 60 sn polling
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
    // Acildi — full listeyi cek
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

  /* ── Baglanti durumu izleme ──────────────────────────────────────
     Sunucu duser (ornek dotnet restart) tarayici iframe icinde
     "localhost baglanmayi reddetti" hatasi gosterir — tema-uyumsuz.
     Asagidaki polling ile biz de tespit eder, ustune tema-uyumlu bir
     "baglanti koptu" overlay'i cikaririz. Server geri gelince
     otomatik dismiss + aktif tab'i reload eder. */
  var [connectionLost, setConnectionLost] = useState(false)
  var [reconnecting, setReconnecting] = useState(false)
  var connectionLostRef = useRef(false)
  useEffect(function () {
    connectionLostRef.current = connectionLost
  }, [connectionLost])

  useEffect(function () {
    var cancelled = false
    var controller

    async function ping() {
      if (cancelled) return
      try {
        controller = new AbortController()
        var timer = setTimeout(function () { controller.abort() }, 4000)
        // Lightweight: HEAD istegi ile sadece baglanti kontrolu — body indirmez.
        // Any status (200/302/401) OK — sunucu cevap veriyorsa baglanti var.
        await fetch('/Home/Index', {
          method: 'HEAD',
          signal: controller.signal,
          credentials: 'same-origin',
          cache: 'no-store',
        })
        clearTimeout(timer)
        if (cancelled) return
        if (connectionLostRef.current) {
          // Reconnect — iframe'leri reload et ve overlay'i kapat
          setReconnecting(true)
          setTimeout(function () {
            if (cancelled) return
            Object.values(iframeRefs.current).forEach(function (el) {
              try { if (el && el.src) el.src = el.src } catch (e) {}
            })
            setConnectionLost(false)
            setReconnecting(false)
          }, 400)
        }
      } catch (e) {
        if (cancelled) return
        if (!connectionLostRef.current) setConnectionLost(true)
      }
    }

    // Ilk kontrol hemen, sonra her 5 saniyede bir
    ping()
    var intervalId = setInterval(ping, 5000)

    // Ayrica offline/online browser event'leri
    function onOffline() { setConnectionLost(true) }
    function onOnline()  { ping() }
    window.addEventListener('offline', onOffline)
    window.addEventListener('online', onOnline)

    return function () {
      cancelled = true
      clearInterval(intervalId)
      if (controller) controller.abort()
      window.removeEventListener('offline', onOffline)
      window.removeEventListener('online', onOnline)
    }
  }, [])

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
    if (stored.length === 0) return []

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

  /* ── Menu click → tab ac veya mevcut tab'a gec ── */
  function openNodeAsTab(node) {
    if (!node || !node.url) return
    var existing = tabs.find(function(t) { return t.url === node.url })
    if (existing) {
      setActiveTabKey(existing.key)
      setActiveMenuKey(node.key)
      return
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
    delete iframeRefs.current[key]
  }

  function performCloseAll() {
    setTabs([])
    setActiveTabKey(null)
    setDirtyTabs({})
    iframeRefs.current = {}
  }

  function closeTab(key, e) {
    if (e) e.stopPropagation()
    if (dirtyTabs[key]) {
      var t = tabs.find(function(x) { return x.key === key })
      setCloseConfirm({
        kind: 'single',
        key: key,
        title: 'Sayfayi Kapat?',
        message: (t && t.title ? '"' + t.title + '" sayfasinda ' : 'Bu sayfada ') +
                 'kaydedilmemis degisiklik var. Yine de kapatilsin mi?'
      })
      return
    }
    performCloseSingle(key)
  }

  function closeAllTabs() {
    var dirtyCount = Object.keys(dirtyTabs).length
    setCloseConfirm({
      kind: 'all',
      title: 'Tum Sayfalari Kapat?',
      message: dirtyCount > 0
        ? dirtyCount + ' sayfada kaydedilmemis degisiklik var. Tum sekmeleri kapatmak istiyor musunuz?'
        : 'Tum sekmeleri kapatmak istiyor musunuz?'
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

  /* ── Iframe → parent mesaj dinleyicisi (dirty state) ─────── */
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

  function handleChangeLang(l) {
    setLang(l)
    savePreferences({ languageCode: l === 'TR' ? 'tr-TR' : 'en-US' })
    // Dil degisince server localization icin full reload
    setTimeout(function() { window.location.reload() }, 300)
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

      {/* Sol: Sidebar */}
      <Sidebar
        isDark={isDark}
        menu={menu}
        activeKey={activeMenuKey}
        expandedNodes={expandedNodes}
        onToggleNode={toggleExpand}
        onSelectLeaf={openNodeAsTab}
        system={system}
      />

      {/* Sag: Ana alan */}
      <div className="flex-1 flex flex-col min-w-0 relative z-10">

        <Header
          isDark={isDark}
          user={user}
          tabsCount={tabs.length}
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
                tabs={tabs}
                activeTabKey={activeTabKey}
                dirtyTabs={dirtyTabs}
                onTabClick={function(key) {
                    // Popover ACIK KALSIN — kullanici baska bir sayfaya da hemen gecebilsin.
                    // Kapatma sadece disariya tiklama (backdrop) veya kapat butonu ile olur.
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
          tabs={tabs}
          activeKey={activeTabKey}
          dirtyTabs={dirtyTabs}
          onTabClick={setActiveTabKey}
          onTabClose={closeTab}
        />

        {/* Body: tab iframe'leri (aktif olan visible, digerleri display:none) */}
        <div
          className="flex-1 min-h-0 relative"
          style={{ background: isDark ? '#0a0d17' : '#f8fafc' }}
        >
          {tabs.length === 0 ? (
            <EmptyState isDark={isDark} />
          ) : (
            tabs.map(function(t) {
              return (
                <iframe
                  key={t.key}
                  ref={function(el) { if (el) iframeRefs.current[t.key] = el; else delete iframeRefs.current[t.key] }}
                  onLoad={function() { handleIframeLoad(t.key) }}
                  src={appendWorkspaceFlag(t.url)}
                  title={t.title}
                  className="absolute inset-0 w-full h-full border-0"
                  style={{
                    display: t.key === activeTabKey ? 'block' : 'none',
                    background: isDark ? '#0a0d17' : '#f8fafc',
                  }}
                />
              )
            })
          )}
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
                  {reconnecting ? 'Bağlantı Geri Geldi!' : 'Bağlantı Kesildi'}
                </h3>

                {reconnecting ? (
                  <p className={'text-sm ' + (isDark ? 'text-emerald-300' : 'text-emerald-700')}>
                    ✓ Sunucu tekrar erişilebilir. Sayfalar yükleniyor...
                  </p>
                ) : (
                  <>
                    <p className={'text-sm ' + (isDark ? 'text-white/70' : 'text-slate-600')}>
                      Sunucu ile iletişim kurulamıyor. Bu genellikle kısa süreli bir kesintidir;
                      sunucu hazır olduğunda otomatik bağlanacağız.
                    </p>
                    <div className={'flex items-center gap-2 mt-2 text-xs ' + (isDark ? 'text-white/45' : 'text-slate-500')}>
                      <span className="relative flex h-2.5 w-2.5">
                        <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-amber-400 opacity-75"></span>
                        <span className="relative inline-flex rounded-full h-2.5 w-2.5 bg-amber-500"></span>
                      </span>
                      <span>Yeniden deneniyor...</span>
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
            title={closeConfirm.title}
            message={closeConfirm.message}
            onAccept={handleCloseConfirmAccept}
            onCancel={handleCloseConfirmCancel}
          />
        )}
      </AnimatePresence>
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
            <strong>{seconds}</strong> saniye icinde iptal edilmezse otomatik kapatilir.
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
              Iptal
            </button>
            <button
              type="button"
              onClick={props.onAccept}
              className="flex-1 px-4 py-2 rounded-lg text-sm font-bold text-white bg-gradient-to-r from-rose-500 to-red-600 hover:from-rose-600 hover:to-red-700 shadow-md shadow-rose-500/30 transition-all flex items-center justify-center gap-1.5"
            >
              <X size={14} strokeWidth={2.6} />
              Kapat
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

/* Menuyu recursive filtrele — arama terimine uyan leaf'leri VE onlarin
   ata gruplarini tutar. Parent'lar otomatik acik sayilir (donus degeri
   ikinci element: expandedKeys seti). */
function filterMenuTree(menu, term) {
  if (!term) return { tree: menu, expandKeys: null }
  var t = term.toLowerCase().trim()
  var expand = {}

  function walk(node) {
    var labelHit = (node.label || '').toLowerCase().indexOf(t) !== -1
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
  var borderColor = isDark ? 'border-white/[0.06]' : 'border-slate-200/80'
  var bgColor = isDark ? 'bg-[#0c0f1a]/70' : 'bg-white/70'

  var [searchTerm, setSearchTerm] = useState('')
  var filtered = filterMenuTree(props.menu, searchTerm)
  var displayTree = filtered.tree
  // Arama aktifse tum eslesen zinciri genislet; degilse normal expanded state
  var effectiveExpanded = filtered.expandKeys
    ? Object.assign({}, props.expandedNodes, filtered.expandKeys)
    : props.expandedNodes

  return (
    <aside
      className={
        'relative z-10 flex flex-col w-[260px] flex-shrink-0 border-r backdrop-blur-xl transition-colors duration-500 ' +
        borderColor + ' ' + bgColor
      }
      style={{ userSelect: 'none', WebkitUserSelect: 'none' }}
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
          <p className={'text-[10px] leading-tight ' + (isDark ? 'text-white/55' : 'text-slate-500')}>
            Premium ERP
          </p>
        </div>
      </div>

      {/* Search — menuyu filtreler */}
      <div className="px-3 pt-3 pb-1">
        <div className="relative">
          <Search
            size={13}
            className={'absolute left-3 top-1/2 -translate-y-1/2 pointer-events-none ' + (isDark ? 'text-white/50' : 'text-slate-400')}
          />
          <input
            type="text"
            value={searchTerm}
            onChange={function(e) { setSearchTerm(e.target.value) }}
            placeholder="Menude ara..."
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
              title="Aramayi temizle"
            >
              <X size={10} strokeWidth={2.4} />
            </button>
          )}
        </div>
      </div>

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
              />
            )
          })
        ) : (
          <div className={'text-center py-6 text-[11px] ' + (isDark ? 'text-white/45' : 'text-slate-400')}>
            <Search size={16} className="mx-auto mb-1.5 opacity-60" strokeWidth={1.5} />
            <p>Eslesme bulunamadi</p>
          </div>
        )}
      </nav>

      <div className={'px-4 py-3 border-t ' + borderColor}>
        <div className={'flex items-center gap-2 text-[10px] font-mono ' + (isDark ? 'text-white/55' : 'text-slate-500')}>
          {props.system && props.system.company && (
            <span className="flex items-center gap-1.5 truncate">
              <Building2 size={11} className="flex-shrink-0" />
              <span className="truncate">{props.system.company}</span>
            </span>
          )}
          {props.system && props.system.company && (
            <span className={isDark ? 'text-white/20' : 'text-slate-300'}>·</span>
          )}
          <span className="flex-shrink-0">v1.0.0</span>
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
  var Icon = resolveIcon(node.icon)

  function handleClick() {
    if (hasChildren) {
      props.onToggleNode(node.key)
    } else if (node.url) {
      props.onSelectLeaf(node)
    }
  }

  // Cerceve (border) kaldirildi — dar sidebar'da kart gibi gorunen hat
  // yerine yalnizca arka plan opakligi ile aktif/hover durumu ayirt edilir.
  var base = 'flex items-center gap-2.5 w-full px-3 py-2 rounded-xl text-sm font-medium cursor-pointer transition-all group'
  // Aktif menu item: yesil (acik konumdaki sayfayi vurgular)
  var variant = isActive
    ? (isDark
      ? 'bg-emerald-500/20 text-white ring-1 ring-emerald-500/30'
      : 'bg-emerald-100 text-emerald-700 ring-1 ring-emerald-200')
    : (isDark
      ? 'text-white/60 hover:bg-white/[0.05] hover:text-white'
      : 'text-slate-600 hover:bg-slate-100 hover:text-slate-900')

  return (
    <div>
      <motion.div
        whileTap={{ scale: 0.98 }}
        onClick={handleClick}
        className={base + ' ' + variant + ' select-none'}
        style={{ marginLeft: level * 12, marginBottom: 2, userSelect: 'none', WebkitUserSelect: 'none' }}
      >
        <Icon
          size={15}
          strokeWidth={1.8}
          className={isActive
            ? (isDark ? 'text-emerald-300' : 'text-emerald-600')
            : (isDark ? 'text-white/40 group-hover:text-white/80' : 'text-slate-400 group-hover:text-slate-700')}
        />
        <span className="flex-1 truncate text-[13px] select-none">{node.label}</span>
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
      {/* Spacer — Bell + Profil saga yaslanir */}
      <div className="flex-1" />

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

        <button
          onClick={props.onOpenTabsClick}
          className={
            'relative p-2 rounded-xl transition-colors ' +
            (isDark ? 'hover:bg-white/5 text-white/60 hover:text-white' : 'hover:bg-slate-100 text-slate-500 hover:text-slate-800')
          }
          title="Acik Sayfalar"
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
      className="absolute right-5 top-16 z-40 w-80 rounded-2xl overflow-hidden"
      style={{
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
        <PopoverRow isDark={isDark} icon={MessageSquare} label="Mesajlar" badge="" />

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
            Tema
          </span>
          <span className={
            'text-[11px] font-semibold uppercase tracking-wider ' +
            (isDark ? 'text-white/45' : 'text-slate-500')
          }>
            {isDark ? 'Koyu' : 'Açık'}
          </span>
        </button>

        <PopoverRow isDark={isDark} icon={UserCircle} label="Profil Bilgileri" />
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
          <span>Cikis Yap</span>
        </a>
      </div>
    </motion.div>
  )
}

function OpenTabsPopover(props) {
  var isDark = props.isDark
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
            Acik Sayfalar
          </h3>
          <p className={'text-[11px] ' + (isDark ? 'text-white/45' : 'text-slate-500')}>
            {tabs.length} sayfa acik
          </p>
        </div>
      </div>

      <div className={isDark ? 'h-px bg-white/10' : 'h-px bg-slate-200'} />

      <div className="py-2 px-2 max-h-[420px] overflow-y-auto smartcard-widgets-scroll">
        {tabs.length === 0 && (
          <div className={'px-3 py-6 text-center text-[12px] italic ' + (isDark ? 'text-white/35' : 'text-slate-400')}>
            Hicbir sayfa acik degil.
          </div>
        )}
        {/* Tumunu Kapat — sayfa listesinin en ustunde, ayri bir "sayfa" gorunumunde.
            Diger liste elemanlarinin row stiliyle hizali ama kirmizi/danger tema. */}
        {tabs.length > 0 && (
          <motion.div
            whileHover={{ x: 1 }}
            whileTap={{ scale: 0.985 }}
            onClick={function() { if (props.onCloseAll) props.onCloseAll() }}
            className={
              'group flex items-center gap-2 px-3 py-2 rounded-xl cursor-pointer transition-all mb-1 ' +
              (isDark
                ? 'bg-rose-500/10 hover:bg-rose-500/20 border border-rose-400/25 hover:border-rose-400/50 text-rose-200 hover:text-white'
                : 'bg-rose-50 hover:bg-rose-100 border border-rose-200 hover:border-rose-400 text-rose-700 hover:text-rose-800')
            }
            title="Tum sekmeleri kapat"
          >
            <span
              className="w-5 h-5 rounded flex items-center justify-center flex-shrink-0"
              style={{
                background: isDark ? 'rgba(244,63,94,0.25)' : 'rgba(244,63,94,0.12)',
                boxShadow: '0 0 8px rgba(244,63,94,0.35)',
              }}
            >
              <X size={12} strokeWidth={2.6} />
            </span>
            <span className="flex-1 text-[12.5px] font-semibold">
              Tümünü Kapat
            </span>
            <span className={'text-[10.5px] font-mono tabular-nums ' + (isDark ? 'text-rose-200/70' : 'text-rose-500')}>
              {tabs.length}
            </span>
          </motion.div>
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
              title={isDirty ? 'Kaydedilmemis degisiklik: ' + t.title : t.title}
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
                title="Sekmeyi kapat"
                aria-label="Sekmeyi kapat"
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

  return (
    <div
      className={'flex items-center h-11 border-b flex-shrink-0 relative ' + borderColor}
      style={{ background: isDark ? '#0a0d17' : '#f8fafc' }}
    >
      {canLeft && (
        <button
          type="button"
          onClick={function() { scrollBy(-200) }}
          className={chevronBtn}
          style={{ left: 4 }}
          title="Sola kaydir"
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
          title="Saga kaydir"
        >
          <ChevronRight size={14} strokeWidth={2.2} />
        </button>
      )}
      <div
        ref={scrollRef}
        onWheel={handleWheel}
        className="flex items-center gap-1 flex-1 h-full overflow-x-auto smartcard-widgets-scroll"
        style={{ paddingLeft: canLeft ? 34 : 16, paddingRight: canRight ? 34 : 16 }}
      >
      {props.tabs.map(function(t) {
        var isActive = t.key === props.activeKey
        return (
          <div
            key={t.key}
            data-tab-key={t.key}
            onClick={function() { props.onTabClick(t.key) }}
            onMouseDown={function(e) { e.preventDefault() }}
            className={
              'relative flex items-center gap-2 px-3 py-1.5 rounded-lg text-[12px] font-medium cursor-pointer transition-all flex-shrink-0 max-w-[220px] select-none ' +
              (isActive
                ? (isDark ? 'text-white bg-white/[0.06]' : 'text-slate-900 bg-slate-100')
                : (isDark ? 'text-white/50 hover:text-white/80 hover:bg-white/[0.03]' : 'text-slate-500 hover:text-slate-800 hover:bg-slate-50'))
            }
            title={(props.dirtyTabs && props.dirtyTabs[t.key]) ? 'Kaydedilmemis degisiklik: ' + t.title : t.title}
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
  )
}

function EmptyState(props) {
  var isDark = props.isDark
  return (
    <div className={'h-full flex items-center justify-center ' + (isDark ? 'text-white/50' : 'text-slate-400')}>
      <div className="text-center">
        <LayoutGrid size={48} className="mx-auto mb-3 opacity-40" strokeWidth={1.2} />
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
        {props.system.company && (
          <span className="flex items-center gap-1.5">
            <Building2 size={10} />
            <span>{props.system.company}</span>
          </span>
        )}
        <span className={dividerColor}>·</span>
        <span>v1.0.0</span>
      </div>
    </footer>
  )
}
