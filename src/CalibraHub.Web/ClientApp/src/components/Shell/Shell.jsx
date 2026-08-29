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
import { useState, useEffect, useRef, useCallback, useMemo } from 'react'
import { createPortal } from 'react-dom'
import { motion, AnimatePresence } from 'framer-motion'
import * as notifApi from '../../services/notificationsService'
// 2026-05-23 — Yapay zeka asistanı (sağ alt floating widget). Top-level Shell altında
// global mount edilir → workspace tab iframe'lerinin DIŞINDA, her sayfada görünür.
import AiFloatingButton from '../AiAssistant/AiFloatingButton'
import SessionIdleGuard from './SessionIdleGuard'
// 2026-06-14 — Ana sayfa özelleştirilebilir pano. Hiç sekme açık değilken
// (isHomePage) EmptyState yerine doğrudan Shell içinde render edilir (iframe yok).
import Dashboard from '../Dashboard/Dashboard'
// 2026-07-16 — Header hızlı-erişim (kısayol) çubuğu kalıcılık katmanı.
import { loadShellShortcuts, saveShellShortcuts } from '../../services/shellShortcutsService'
// PageComment Seq 1123 (2026-08-27) — açık sekmeler şirket+kullanıcı bazında sunucuda
// (UiConfigController) kalıcı; localStorage yalnızca hızlı ilk boyama + offline aynadır.
import { fetchWorkspaceTabs, saveWorkspaceTabs } from '../../services/workspaceTabsService'
import {
  // Shell internals
  Sparkles, ChevronLeft, ChevronRight, CircleDot, Bell, BellRing, Moon, Sun, Search,
  Layers, MessageSquare, Languages, UserCircle, LogOut, Bot, Menu,
  X, LayoutGrid, Building2, Check, Home, Plus, Pencil, Pin, PinOff, Trash2,
  HelpCircle,
  // Menu icons (MenuDefinition'dan gelir)
  LayoutList, FileText, Files, Archive, Truck,
  Package, Folder, Boxes, Sliders, TrendingUp,
  Factory, Network, Coins, Users, Settings2,
  DollarSign, MapPin, Ruler, Tag, Settings,
  Plug, Mail, Database, Zap, UserCog,
  BookOpen, Clock,
  Warehouse, ArrowLeftRight, PackagePlus, PackageMinus, PackageCheck, ClipboardCheck,
  // 2026-07-16 — Kısayol çubuğu picker'ı MenuDefinition'daki TÜM ikonları çözebilmeli.
  // Önceden ICON_MAP'te eksikti (Sidebar'da da bu ikonlar sessizce CircleDot fallback'e
  // düşüyordu) — bu ekleme her iki yeri de düzeltir.
  BarChart3, CheckCircle2, FlaskConical, FileStack, Upload, CalendarDays,
  MessageCircle, LayoutDashboard, PenLine, Inbox, GitBranch, Grid3X3,
  ShoppingCart, ShoppingBag, ClipboardList, Tablet, FileUp, PenSquare,
  SlidersHorizontal, ShieldCheck, EyeOff, ScrollText, Activity,
  CornerDownRight, ChevronDown,
  // Şirket geçiş modalı — veritabanına ulaşılamayan şirket satırı (2026-08-24).
  AlertTriangle
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
  notif_pin:               { TR: 'Üste tuttur',                      EN: 'Pin to top' },
  notif_unpin:             { TR: 'Sabitlemeyi kaldır',               EN: 'Unpin' },
  notif_mark_read:         { TR: 'Okundu işaretle',                  EN: 'Mark as read' },
  notif_delete:            { TR: 'Sil',                              EN: 'Delete' },
  notif_delete_confirm:    { TR: 'Bildirim silinsin mi?',            EN: 'Delete this notification?' },
  notif_delete_yes:        { TR: 'Sil',                              EN: 'Delete' },
  notif_tab_unread:        { TR: 'Okunmayan',                        EN: 'Unread' },
  notif_tab_read:          { TR: 'Okunan',                           EN: 'Read' },
  notif_tab_all:           { TR: 'Tümü',                             EN: 'All' },
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
  // Nested tab (PageComment Seq 1063, 2026-08-03, Bulgu 2): parent sekmenin kendisi
  // temiz olsa bile altındaki child sekmelerden biri dirty ise (parent kapanınca
  // kaskad kapanacağı için) kullanıcı bilgilendirilir.
  single_close_dirty_children: { TR: 'Bu sekmenin altındaki bir alt sekmede kaydedilmemiş değişiklik var. Kapatırsanız kaybolur. Kapatmak istiyor musunuz?', EN: 'A nested tab under this one has unsaved changes that will be lost if you close it. Close anyway?' },
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
  switch_company:          { TR: 'Şirket Değiştir',                  EN: 'Switch Company' },
  switch_company_loading:  { TR: 'Yükleniyor…',                      EN: 'Loading…' },
  switch_company_empty:    { TR: 'Başka şirket yetkiniz yok.',       EN: 'No other company available.' },
  switch_company_error:    { TR: 'Şirketler alınamadı.',             EN: 'Could not load companies.' },
  ai_assistant:            { TR: 'Calibo',                           EN: 'Calibo' },
  // Connection overlay
  conn_lost:               { TR: 'Bağlantı Kesildi',                EN: 'Connection Lost' },
  conn_restored:           { TR: 'Bağlantı Geri Geldi!',            EN: 'Connection Restored!' },
  conn_restored_msg:       { TR: '✓ Sunucu tekrar erişilebilir. Sayfalar yükleniyor...', EN: '✓ Server is reachable again. Pages loading...' },
  conn_lost_msg:           { TR: 'Sunucu ile iletişim kurulamıyor. Bu genellikle kısa süreli bir kesintidir; sunucu hazır olduğunda otomatik bağlanacağız.', EN: "Unable to reach the server. This is usually a brief interruption; we'll reconnect automatically when the server is ready." },
  retrying:                { TR: 'Yeniden deneniyor...',             EN: 'Retrying...' },
  try_now:                 { TR: 'Şimdi Dene',                       EN: 'Try Now' },
  refresh_page:            { TR: 'Sayfayı Yenile',                  EN: 'Refresh Page' },
  // Kısayol çubuğu (hızlı erişim) — 2026-07-16
  go_home:                       { TR: 'Ana sayfaya git',              EN: 'Go to home' },
  shortcuts_edit:                 { TR: 'Kısayolları düzenle',          EN: 'Edit shortcuts' },
  shortcuts_save:                  { TR: 'Kaydet ve çık',                EN: 'Save & exit' },
  shortcuts_add:                    { TR: 'Kısayol ekle',                 EN: 'Add shortcut' },
  shortcuts_remove:                  { TR: 'Kaldır',                       EN: 'Remove' },
  shortcuts_shownames:                { TR: 'İsimler',                     EN: 'Names' },
  shortcuts_picker_title:              { TR: 'Kısayol Seç',                 EN: 'Select Shortcut' },
  shortcuts_picker_search:              { TR: 'Ekran ara…',                  EN: 'Search screens…' },
  shortcuts_picker_empty:                { TR: 'Eşleşen ekran bulunamadı.',   EN: 'No matching screens found.' },
  shortcuts_picker_apply:                  { TR: 'Uygula',                      EN: 'Apply' },
  shortcuts_picker_cancel:                  { TR: 'Vazgeç',                      EN: 'Cancel' },
  shortcuts_picker_selected_suffix:          { TR: 'seçili',                      EN: 'selected' },
  // İşlemler menüsü — 2026-08-01
  actions:                       { TR: 'İşlemler',                     EN: 'Actions' },
  help:                          { TR: 'Yardım',                       EN: 'Help' },
  help_none:                     { TR: 'Bu sayfa için yardım bulunmuyor.', EN: 'No help available for this page.' },
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
  Building2: Building2, Plus: Plus, Pencil: Pencil,
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
  // 2026-07-16 — MenuDefinition.cs'de kullanılan ama önceden haritada olmayan ikonlar
  // (kısayol picker'ı + Sidebar için tam kapsama).
  BarChart3: BarChart3, CheckCircle2: CheckCircle2, FlaskConical: FlaskConical,
  FileStack: FileStack, Upload: Upload, CalendarDays: CalendarDays,
  MessageCircle: MessageCircle, LayoutDashboard: LayoutDashboard, PenLine: PenLine,
  Inbox: Inbox, GitBranch: GitBranch, Grid3X3: Grid3X3,
  ShoppingCart: ShoppingCart, ShoppingBag: ShoppingBag, ClipboardList: ClipboardList,
  Tablet: Tablet, FileUp: FileUp, PenSquare: PenSquare,
  SlidersHorizontal: SlidersHorizontal, ShieldCheck: ShieldCheck, EyeOff: EyeOff,
  ScrollText: ScrollText, Activity: Activity,
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

/* Max 24 tab limiti (mevcut site.js ile ayni) — en eski sekmeler onden atilir.
   Nested tab (PageComment Seq 1063, 2026-08-03, Bulgu 3): atilan sekmeler
   arasinda bir PARENT varsa, altindaki child'lar "oksuz" (parentKey dolu ama
   parent tabs'ta yok) kalip TabBar'da hicbir satirda render edilemez + kapatilamaz
   hale gelirdi. Bu yuzden atilan bir tab'in child'i olan sekmeler top-level'a
   terfi edilir (parentKey: null) — sekme kaybolmaz, sadece grubu dagilir. */
function capTabsAtLimit(list, maxCount) {
  if (!list || list.length <= maxCount) return list
  var dropped = list.slice(0, list.length - maxCount)
  var kept = list.slice(list.length - maxCount)
  if (dropped.length === 0) return kept
  var droppedKeys = dropped.map(function(t) { return t.key })
  return kept.map(function(t) {
    return (t.parentKey && droppedKeys.indexOf(t.parentKey) !== -1)
      ? Object.assign({}, t, { parentKey: null })
      : t
  })
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

/* Menu agacini gezip URL'i olan yapraklari duzlestirir — kisayol cubugu
   picker'inin veri kaynagi. HomeDashboardController.FlattenLeaves (Dashboard'un
   "Kısayol Seç" widget'i icin kullandigi C# metodu) ile BIREBIR ayni algoritma:
   groupLabel = en yakin ata grup basligi (top-level degil — orn. "e-Fatura"nin
   grubu "Elektronik Belgeler", "Onay İşlemleri" degil). Boylece iki "kisayol
   sec" ekrani (Ana Sayfa Panosu + Header cubugu) ayni gruplamayi gosterir. */
function flattenMenuLeaves(menu) {
  var out = []
  function walk(nodes, parentLabel) {
    (nodes || []).forEach(function(node) {
      var hasChildren = Array.isArray(node.children) && node.children.length > 0
      if (hasChildren) {
        walk(node.children, node.label)
      } else if (node.url) {
        out.push({
          key: node.key,
          label: node.label,
          url: node.url,
          icon: node.icon,
          matchPath: node.matchPath,
          groupLabel: parentLabel || node.label,
        })
      }
    })
  }
  walk(menu, null)
  return out
}

/* ══════════════════════════════════════════════════════════════
   Ana Shell bileseni
   ══════════════════════════════════════════════════════════════ */
export default function Shell(props) {
  var config = props.config || {}
  var user = config.user || { name: '—', email: '', initials: '?', userKey: 'anon' }
  // Sirket adi STATE: sunucudan render edilir ama oturum icinde degisebilir — Sistem
  // Saglik Kontrolu test ortami kurduktan sonra oturumu test sirketine gecirir ve
  // sayfayi yeniden yuklemez. Sabit prop olsaydi ust serit ESKI sirketi gostermeye
  // devam ederdi; kullanicinin hangi sirkette oldugunu yanlis bilmesi gercek bir
  // risktir (test sirketi sanip canli veriye dokunmak). Bu yuzden state + setter.
  var [system, setSystem] = useState(function () {
    return config.system || { company: '', year: '', status: 'Hazir', appVersion: '?' }
  })
  var initialUrl = config.initialUrl || '/'
  var savePrefsUrl = config.savePreferencesUrl || '/Account/SaveInterfacePreferences'
  var antiforgery = config.antiforgeryToken || ''
  // Şirket değiştirme modalı — popover'ın İÇİNDE değil Shell kökünde tutulur,
  // yoksa menü kapanınca modal da kapanır.
  var [companySwitchOpen, setCompanySwitchOpen] = useState(false)

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

  /* ── Tab state (sunucu + localStorage ayna) ──────────────────
     PageComment Seq 1123 (2026-08-27): açık sekmeler artık ŞİRKET+KULLANICI
     bazında sunucuda (UiConfigController) kalıcıdır — kullanıcı aynı şirkete
     tekrar girdiğinde veya şirket değiştirdiğinde açık ekranlarını aynen
     bulur. localStorage KALDIRILMADI: ilk boyamayı hızlandırmak (sunucu
     yanıtı beklemeden) ve sunucuya erişilemediğinde çalışmaya devam etmek
     icin yerel ayna olarak kalır — kaynak-of-truth sunucudur.

     Onemli: "hic kayit yok" (ilk ziyaret) ile "kayit var ama bos array"
     (kullanici tum tab'lari kapatti) durumlarini AYIRT ederiz. Birincisinde
     initialUrl icin varsayilan tab acariz; ikincisinde kullanicinin kapatma
     niyetine saygi gosterip bos state (EmptyState) gosteririz. Aksi halde
     Ctrl+F5 sonrasi hayalet bir tab tekrar uretilir ve kullanici "neden bos
     bir tab acildi?" der. Bu ayrım hem localStorage hem sunucu (saved:false
     vs saved:true+tabs:[]) katmanında korunur. */

  // Menu'den URL'ye karsilik gelen label'i bul; yoksa URL path'inden kisa isim uret
  function resolveInitialTabTitle(url) {
    var fromMenu = findLabelByUrl(menu, url)
    if (fromMenu) return fromMenu
    // URL path'inden son segment al, / veya ? ile kes
    var path = url ? url.split('?')[0] : '/'
    if (path === '/' || path === '/Home' || path === '/Home/Index') return 'Ana Sayfa'
    var segs = path.split('/').filter(Boolean)
    return segs.length > 0 ? segs[segs.length - 1] : 'Sayfa'
  }

  function isHomePageUrl(url) {
    var p = url ? url.split('?')[0] : '/'
    return p === '/' || p === '/Home' || p === '/Home/Index'
  }

  /* Depolanmis (localStorage veya sunucu) sekme listesini gecerli initialUrl
     ile uzlastirir — "hic kayit yok" / "kayit var ama bos" ayrimini koruyarak.
     Hem yerel ilk-yukleme hem sunucu senkronu AYNI mantigi kullanir (tek
     kaynak — iki yerde kopyalanmiş kod hata üretirdi). */
  function reconcileStoredTabs(stored, url) {
    if (!Array.isArray(stored)) return []
    if (stored.length === 0) {
      if (!isHomePageUrl(url)) {
        return [{ key: 'init-' + Date.now(), url: url, title: resolveInitialTabTitle(url) }]
      }
      return []
    }
    // Kayitli tab'lar var; aralarinda mevcut URL var mi? Ana sayfa ise ekleme
    if (!isHomePageUrl(url)) {
      var hasInitial = stored.some(function(t) { return t.url === url })
      if (!hasInitial) {
        return stored.concat([{ key: 'init-' + Date.now(), url: url, title: resolveInitialTabTitle(url) }])
      }
    }
    return stored
  }

  /* Sunucudan gelen ham sekme nesnesini savunmali normalize eder — sunucu
     yalnizca belirli alanlari saklayabilir, eksik alana dayanikli olunur. */
  function normalizeServerTab(t) {
    if (!t || typeof t !== 'object' || !t.url) return null
    return {
      key: t.key ? String(t.key) : ('tab-' + Date.now() + '-' + Math.floor(Math.random() * 100000)),
      url: String(t.url),
      title: t.title ? String(t.title) : resolveInitialTabTitle(String(t.url)),
      parentKey: t.parentKey ? String(t.parentKey) : null,
    }
  }

  // Sirket + kullanici kapsamli yerel anahtar — sirket degisince FARKLI bir
  // kovaya yazilir, aksi halde bir sirketin sekmeleri digerinde gorunurdu.
  var tabsStorageKey = 'calibra.workspace.tabs.' +
    encodeURIComponent(String((system && system.companyId) || 'anon')) + '.' +
    encodeURIComponent(user.userKey || user.email || 'anon')

  var [tabs, setTabs] = useState(function() {
    var rawStored = null
    try { rawStored = localStorage.getItem(tabsStorageKey) } catch (e) { /* quota/private */ }

    // Hic kayit yok → ilk ziyaret → ana sayfa ise bos baslat, diger sayfa ise tab ac
    if (rawStored === null) {
      if (isHomePageUrl(initialUrl)) return []
      return [{ key: 'init-' + Date.now(), url: initialUrl, title: resolveInitialTabTitle(initialUrl) }]
    }

    // Kayit var ama parse edilemiyor → guvenli fallback: bos
    var stored
    try { stored = JSON.parse(rawStored) } catch (e) { stored = [] }
    return reconcileStoredTabs(stored, initialUrl)
  })
  var [activeTabKey, setActiveTabKey] = useState(function() {
    if (tabs.length === 0) return null
    var match = tabs.find(function(t) { return t.url === initialUrl })
    return match ? match.key : (tabs[0] && tabs[0].key)
  })

  /* Sunucudan ilk senkron denemesi tamamlanana kadar POST atilmasin — yoksa
     henuz sunucudan gelmemis "eski" yerel state, sunucudaki gercek kaydin
     onune gecebilir (ping-pong). Basarili/basarisiz fark etmeksizin bir kez
     denendikten sonra true olur. */
  var tabsReadyRef = useRef(false)
  var tabsSaveTimerRef = useRef(null)
  // Her render'da guncel tabs degerini tasir — GET senkronu asenkron tamamlandiginda
  // "kullanici hic tab degistirmedi" durumunda dahi guncel local state'e erisim saglar.
  var tabsRef = useRef(tabs)
  tabsRef.current = tabs

  /* Mount'ta sunucudaki kayitli sekme durumunu oku ve uzlastir.
     saved:false → hic kayit yok. Yerel "ilk ziyaret" state zaten dogru; ustelik
     bu ilk state bir kez sunucuya yazdirilir ki kullanici hic tab degistirmese
     bile bir sonraki giriste ayni sekme bulunsun (aksi halde tabs referansi
     hic degismedigi icin asagidaki debounce-save effect'i tetiklenmeyecekti).
     saved:true  → sunucu kazanir (bos dizi dahil — bilincli kapatma niyeti
     baska bir cihaz/oturumda verilmis olabilir). Sunucuya erisilemezse yerel
     kopya ile calismaya devam edilir (fail-open, veri kaybi yok). */
  useEffect(function() {
    var cancelled = false
    var neverSavedOnServer = false
    fetchWorkspaceTabs()
      .then(function(res) {
        if (cancelled) return
        if (!res.saved) { neverSavedOnServer = true; return }
        var normalized = res.tabs.map(normalizeServerTab).filter(Boolean)
        var reconciled = reconcileStoredTabs(normalized, initialUrl)
        setTabs(reconciled)
        setActiveTabKey(function(prevKey) {
          if (reconciled.length === 0) return null
          var stillThere = reconciled.some(function(t) { return t.key === prevKey })
          if (stillThere) return prevKey
          var match = reconciled.find(function(t) { return t.url === initialUrl })
          return match ? match.key : reconciled[0].key
        })
      })
      .catch(function(err) {
        // Sunucudan okunamadi — yerel kopya zaten yuklendi, sessizce devam.
        // Kullaniciyi rahatsiz eden bir uyari YOK (arka plan senkronu).
        console.warn('[Shell] Açık sekmeler sunucudan okunamadı, yerel kopya kullanılıyor.', err)
      })
      .finally(function() {
        tabsReadyRef.current = true
        if (cancelled || !neverSavedOnServer) return
        // Ilk-oturum durumunu tek seferlik sunucuya yaz (kullanici tab hic degistirmese bile).
        saveWorkspaceTabs(tabsRef.current).catch(function(err) {
          console.warn('[Shell] Açık sekmeler ilk senkronda sunucuya yazılamadı, yerel kopya korunuyor.', err)
        })
      })
    return function() { cancelled = true }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  /* Tabs her degistiginde localStorage'a hemen yaz (garanti + hizli yeniden
     acilis), sunucuya debounce'li POST at (her ac/kapa/siralamada istek
     atmak yazma firtinasi yaratir). */
  useEffect(function() {
    try { localStorage.setItem(tabsStorageKey, JSON.stringify(tabs)) }
    catch (e) { /* quota/private mode — sessiz gec */ }

    if (!tabsReadyRef.current) return
    if (tabsSaveTimerRef.current) clearTimeout(tabsSaveTimerRef.current)
    tabsSaveTimerRef.current = setTimeout(function() {
      saveWorkspaceTabs(tabs).catch(function(err) {
        // Sunucu yazimi basarisiz — yerel kopya zaten guncel, veri kaybi
        // YOK; bir sonraki degisiklikte tekrar denenir. Sessiz yutma degil,
        // konsola anlamli uyari (kullaniciyi rahatsiz eden kutu YOK).
        console.warn('[Shell] Açık sekmeler sunucuya kaydedilemedi, yerel kopya korunuyor.', err)
      })
    }, 1200)
    return function() { if (tabsSaveTimerRef.current) clearTimeout(tabsSaveTimerRef.current) }
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
      return capTabsAtLimit(prev.concat([newTab]), 24)
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
    // Nested (child) tab destegi (PageComment Seq 1063, 2026-08-03) — arg.asChild
    // true gelince yeni tab'in parentKey'i acikca verilmis arg.parentKey, yoksa
    // cagiran tarafin o an aktif sekmesi olur. parentKey/asChild verilmeyen
    // cagrilar ESKISI GIBI duz ust-seviye tab acar (regresyon yok).
    var parentKey = arg.parentKey || (arg.asChild ? activeTabKey : null) || null
    setShowDashboard(false)

    // 1) Ayni URL ile mevcut tab varsa → sadece aktive et (parent/child ayrimi
    //    yapmadan; "ayni malzeme ikinci kez tiklanirsa mevcut child'a odaklan"
    //    davranisi bu satirla saglanir)
    var exactExisting = tabs.find(function (t) { return t.url === url })
    if (exactExisting) {
      setActiveTabKey(exactExisting.key)
      // 2026-08-08 fix: iframe ICERIDEN baska bir sayfaya kaymis olabilir — ornegin kit/malzeme
      // edit ekranindan "Geri" ile listeye donulunce sekmenin url'i hala edit, ama ekranda liste
      // duruyor. O halde ayni kaydi listeden tekrar secmek HICBIR SEY yapmiyordu (sekme zaten
      // aktif). Gercek konum farkli bir PATH'e kaymissa iframe'i sekmenin url'ine geri yukle.
      // Yalniz PATH karsilastirilir; ayni ekranda query farki (kaydedilmemis form) bosuna
      // reload edilmesin diye dokunulmaz.
      try {
        var exEl = iframeRefs.current[exactExisting.key]
        if (exEl) {
          var curPath = ''
          try { curPath = exEl.contentWindow.location.pathname } catch (_) { curPath = '' }
          var wantPath = String(url).split('?')[0]
          if (curPath && curPath.toLowerCase() !== wantPath.toLowerCase()) {
            try { exEl.contentWindow.location.replace(url) } catch (_) { exEl.src = url }
          }
        }
      } catch (_) { /* cross-origin / henuz mount degil — yoksay */ }
      return
    }

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

    // 3) Yeni tab ac — parentKey verilmisse (ve tabs icinde hala mevcutsa) child
    //    olarak isaretlenir; degilse eskisi gibi duz ust-seviye tab.
    //    GRANDCHILD ONLENIR (Bulgu 4, 2026-08-03 adversarial review): cozulen
    //    parent'in KENDISI bir child ise (parentTab.parentKey dolu), TabBar iki
    //    seviyeyi dogru gosteremedigi icin dedenin key'i kullanilir — nesting
    //    HER ZAMAN tek seviyeye kelepcelenir.
    var parentTab = parentKey ? tabs.find(function (t) { return t.key === parentKey }) : null
    var resolvedParentKey = parentTab ? (parentTab.parentKey || parentTab.key) : null
    var newTab = {
      key: 'tab-' + Date.now() + '-' + Math.floor(Math.random() * 1000),
      url: url,
      title: title,
      parentKey: resolvedParentKey,
    }
    setTabs(function (prev) {
      // Bulgu 3: 24-tab limitinde atilan bir parent'in child'lari oksuz kalmasin.
      return capTabsAtLimit(prev.concat([newTab]), 24)
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
    // Aktif sirket adini sayfa yenilemeden guncelle (iframe icindeki ekranlar
    // window.top.CalibraHub.setActiveCompanyName(...) ile cagirir).
    window.CalibraHub.setActiveCompanyName = function (name) {
      if (!name) return
      setSystem(function (prev) {
        if (prev && prev.company === name) return prev
        return Object.assign({}, prev, { company: name })
      })
    }
    return function () {
      if (window.CalibraHub) {
        delete window.CalibraHub.openWorkspaceTab
        delete window.CalibraHub.setActiveCompanyName
      }
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
    // Nested tab kurali (PageComment Seq 1063, 2026-08-03): bir ust (parent) sekme
    // kapaninca altindaki tum child sekmeler de kapanir. Child sekme kapaninca
    // (aktifse) parent aktif kalir — baska bir child'a degil.
    var closedTab = tabs.find(function(t) { return t.key === key })
    var childKeys = tabs.filter(function(t) { return t.parentKey === key }).map(function(t) { return t.key })
    var removeKeys = [key].concat(childKeys)
    var wasActive = removeKeys.indexOf(activeTabKey) !== -1
    var fallbackParentKey = (closedTab && closedTab.parentKey && removeKeys.indexOf(closedTab.parentKey) === -1)
      ? closedTab.parentKey
      : null

    setTabs(function(prev) {
      var idx = prev.findIndex(function(t) { return t.key === key })
      var next = prev.filter(function(t) { return removeKeys.indexOf(t.key) === -1 })
      if (wasActive) {
        if (fallbackParentKey && next.some(function(t) { return t.key === fallbackParentKey })) {
          setActiveTabKey(fallbackParentKey)
        } else if (next.length > 0) {
          var newIdx = Math.max(0, Math.min(idx, next.length - 1))
          setActiveTabKey(next[newIdx].key)
        } else {
          setActiveTabKey(null)
        }
      }
      return next
    })
    setDirtyTabs(function(prev) {
      var changed = false
      var next = Object.assign({}, prev)
      removeKeys.forEach(function(k) { if (next[k]) { delete next[k]; changed = true } })
      return changed ? next : prev
    })
    setSidebarHideTabKeys(function(prev) {
      var changed = false
      var next = new Set(prev)
      removeKeys.forEach(function(k) { if (next.has(k)) { next.delete(k); changed = true } })
      return changed ? next : prev
    })
    removeKeys.forEach(function(k) { delete iframeRefs.current[k] })
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
    // Nested tab (PageComment Seq 1063, 2026-08-03, Bulgu 2): key kapanınca
    // performCloseSingle altındaki TÜM child'ları kaskad kapatır. Bu yüzden dirty
    // kontrolü yalnızca kapatılan sekmenin kendisiyle sınırlı kalamaz — dirty bir
    // child sessizce (onaysız) kaybolmasın diye child'lar da taranır.
    var isSelfDirty = !!dirtyTabs[key]
    var hasDirtyChild = tabs.some(function(x) { return x.parentKey === key && !!dirtyTabs[x.key] })
    if (isSelfDirty || hasDirtyChild) {
      var t = tabs.find(function(x) { return x.key === key })
      setCloseConfirm({
        kind: 'single',
        key: key,
        title: tShell('single_close_title', lang),
        message: (t && t.title ? '"' + t.title + '" ' : '') +
                 tShell(isSelfDirty ? 'single_close_dirty' : 'single_close_dirty_children', lang)
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

  /* ── F8 — "yeni kayit" kisayolunu aktif tab'a forward et ──
     Odak Shell'deyken (sidebar, header) iframe keydown'u duymaz; mesajla
     iletilir. SmartBoard iceride calibra:hotkey mesajini yakalayip primary
     action'i ("Yeni X") calistirir. SmartBoard olmayan sayfalar ignore eder. */
  useEffect(function () {
    function onNewHotkey(e) {
      var isF8 = (e.key === 'F8' || e.keyCode === 119) && !e.altKey && !e.ctrlKey && !e.metaKey && !e.shiftKey
      if (!isF8) return
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

  /* ── Yardım — AKTİF sekmenin yardımını aç ───────────────────────
     Aktif iframe içindeki #calibra-help div'inden (data-help-key) okur;
     üst dokümandaki modal helper ile açar. Böylece hangi sayfa açıksa
     onun yardımı gelir (sekme değişince doğru içerik). */
  var openActiveHelp = useCallback(function () {
    var key = null, title = ''
    if (showDashboard) {
      key = 'home'; title = lang === 'EN' ? 'Home' : 'Ana Sayfa'    // pano iframe değil → sabit key
    } else {
      try {
        if (activeTabKey) {
          var el = iframeRefs.current[activeTabKey]
          var doc = el && el.contentDocument
          var hd = doc && doc.getElementById('calibra-help')
          if (hd) { key = hd.getAttribute('data-help-key'); title = hd.getAttribute('data-page-title') || '' }
        }
      } catch (ex) { /* same-origin değilse veya yüklenmediyse yardım yok */ }
    }
    if (window.calibraOpenHelpFor) window.calibraOpenHelpFor(key, title, lang === 'EN' ? 'en' : 'tr')
  }, [activeTabKey, showDashboard, lang])

  /* F1 — odak Shell chrome'undayken aktif sayfanın yardımını aç.
     (Odak iframe içindeyken F1'i sayfanın kendi site.js'i yakalar.) */
  useEffect(function () {
    function onHelpKey(e) {
      if (e.key !== 'F1') return
      e.preventDefault()
      openActiveHelp()
    }
    window.addEventListener('keydown', onHelpKey)
    return function () { window.removeEventListener('keydown', onHelpKey) }
  }, [openActiveHelp])

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
          menu={menu}
          onNavigate={openNodeAsTab}
          onGoHome={handleLogoClick}
          onOpenHelp={openActiveHelp}
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
                antiforgery={antiforgery}
                onOpenCompanySwitch={function() { setCompanySwitchOpen(true) }}
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
          style={{ background: 'var(--app-content-bg)' }}
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
                  background: 'var(--app-content-bg)',
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

      {/* Şirket değiştirme modalı — kullanıcı menüsündeki butondan açılır; menü
          kapansa da açık kalsın diye Shell kökünde render edilir. */}
      <AnimatePresence>
        {companySwitchOpen && (
          <CompanySwitchModal
            isDark={isDark}
            lang={lang}
            antiforgery={antiforgery}
            onClose={function() { setCompanySwitchOpen(false) }}
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
  // Okunmayan / Okunan / Tümü segment filtresi — client-side (backend her zaman tam listeyi döner).
  // Iğnelenen (pinned) bildirimler filtreden bağımsız her zaman görünür kalır (aşağıdaki filteredNotifItems).
  var [notifFilter, setNotifFilter] = useState('unread')
  // Panel-ici mini sil-onayi (tam ekran modal degil) — pending id, panel kapaninca sifirlanir.
  var [confirmDeleteId, setConfirmDeleteId] = useState(null)
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
    if (!notifOpen) {
      setConfirmDeleteId(null) // panel kapaninca bekleyen sil-onayi banner'i temizlenir
      return
    }
    notifApi.list(30).then(function (d) {
      setNotifItems(sortNotifItems((d && d.items) || []))
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

  // Backend siralamasiyla ayni kural (pinned DESC, okunmamis once, tarih DESC) — pin/okundu
  // toggle sonrasi in-place liste guncellenirken client-side yeniden uygulanir.
  function sortNotifItems(list) {
    return list.slice().sort(function (a, b) {
      if (!!a.isPinned !== !!b.isPinned) return a.isPinned ? -1 : 1
      if (!!a.isRead !== !!b.isRead) return a.isRead ? 1 : -1
      if (a.createdAt === b.createdAt) return 0
      return a.createdAt < b.createdAt ? 1 : -1
    })
  }

  // Segment filtresine göre görünür liste — iğnelenmiş bildirim filtreden bağımsız her zaman görünür.
  var filteredNotifItems = notifItems.filter(function (n) {
    if (n.isPinned) return true
    if (notifFilter === 'unread') return !n.isRead
    if (notifFilter === 'read') return !!n.isRead
    return true
  })

  function handleNotifClick(n) {
    setConfirmDeleteId(null)
    if (!n.isRead) {
      notifApi.markRead(n.id)
      setNotifItems(function (prev) { return prev.map(function (x) { return x.id === n.id ? { ...x, isRead: true } : x }) })
      setNotifUnread(function (c) { return Math.max(0, c - 1) })
    }
    if (n.link) window.location.href = n.link
  }

  /* Satir hover aksiyonlari: uste tuttur / okundu isaretle / sil.
     Her biri stopPropagation ile satirin kendi onClick'ini (markRead+navigate) engeller. */
  function handleTogglePinClick(n, e) {
    e.stopPropagation()
    var nextPinned = !n.isPinned
    setNotifItems(function (prev) {
      return sortNotifItems(prev.map(function (x) { return x.id === n.id ? { ...x, isPinned: nextPinned } : x }))
    })
    notifApi.togglePin(n.id).then(function (res) {
      if (!res || !res.success) return
      setNotifItems(function (prev) {
        return sortNotifItems(prev.map(function (x) { return x.id === n.id ? { ...x, isPinned: !!res.isPinned } : x }))
      })
    })
  }

  function handleMarkReadClick(n, e) {
    e.stopPropagation()
    if (n.isRead) return
    notifApi.markRead(n.id)
    setNotifItems(function (prev) {
      return sortNotifItems(prev.map(function (x) { return x.id === n.id ? { ...x, isRead: true } : x }))
    })
    setNotifUnread(function (c) { return Math.max(0, c - 1) })
  }

  function handleDeleteClick(n, e) {
    e.stopPropagation()
    setConfirmDeleteId(n.id)
  }

  function handleCancelDelete(e) {
    e.stopPropagation()
    setConfirmDeleteId(null)
  }

  function handleConfirmDelete(n, e) {
    e.stopPropagation()
    setConfirmDeleteId(null)
    notifApi.deleteNotification(n.id)
    setNotifItems(function (prev) { return prev.filter(function (x) { return x.id !== n.id }) })
    if (!n.isRead) setNotifUnread(function (c) { return Math.max(0, c - 1) })
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

      {/* Hızlı erişim (kısayol) çubuğu — kalan orta alanı kaplar, Bell + Profil sağa yaslanır */}
      <ShortcutsBar
        isDark={isDark}
        lang={lang}
        menu={props.menu}
        onNavigate={props.onNavigate}
        onGoHome={props.onGoHome}
        onOpenHelp={props.onOpenHelp}
      />

      <div className="flex items-center gap-2 flex-shrink-0">
        {/* Sayfa-içi Yorum butonu yuvası — page-comments-widget.js (vanilla, admin-only)
            kendi ✏️ butonunu ve panelini bu boş div'in İÇİNE taşır (ensureDocked).
            React bu div'in çocuklarını render etmez/yönetmez; içi hep "boş" görünür. */}
        <div id="pcHeaderSlot" className="relative flex-shrink-0"></div>
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
                <div className={'flex items-center gap-1 px-3 py-2 border-b ' + (isDark ? 'border-white/10' : 'border-slate-100')}>
                  {[
                    { key: 'unread', label: tShell('notif_tab_unread', lang) },
                    { key: 'read', label: tShell('notif_tab_read', lang) },
                    { key: 'all', label: tShell('notif_tab_all', lang) },
                  ].map(function (opt) {
                    var isActive = notifFilter === opt.key
                    return (
                      <button
                        key={opt.key}
                        onClick={function () { setNotifFilter(opt.key) }}
                        className={
                          'px-2.5 py-1 rounded-lg text-[11px] font-semibold transition-colors ' +
                          (isActive
                            ? 'text-white'
                            : (isDark ? 'text-white/50 hover:text-white hover:bg-white/5' : 'text-slate-500 hover:text-slate-800 hover:bg-slate-100'))
                        }
                        style={isActive ? { background: 'linear-gradient(135deg,#6366f1,#8b5cf6)' } : undefined}
                      >
                        {opt.label}
                      </button>
                    )
                  })}
                </div>
                <div className="flex-1 overflow-y-auto">
                  {filteredNotifItems.length === 0 && (
                    <div className={'px-4 py-10 text-center text-[12px] italic ' + (isDark ? 'text-white/40' : 'text-slate-400')}>
                      {tShell('no_notifications', lang)}
                    </div>
                  )}
                  {filteredNotifItems.map(function (n) {
                    var isConfirmingDelete = confirmDeleteId === n.id
                    return (
                      <div
                        key={n.id}
                        onClick={function () { if (!isConfirmingDelete) handleNotifClick(n) }}
                        className={
                          'group relative px-4 py-3 border-b transition-colors ' +
                          (isConfirmingDelete ? '' : 'cursor-pointer ') +
                          (isDark ? 'border-white/5' : 'border-slate-100') +
                          (!isConfirmingDelete ? (isDark ? ' hover:bg-white/5' : ' hover:bg-slate-50') : '') +
                          (!n.isRead ? (isDark ? ' bg-indigo-500/5' : ' bg-indigo-50/60') : '') +
                          (n.isPinned ? (isDark ? ' bg-amber-500/5' : ' bg-amber-50/50') : '')
                        }
                      >
                        <div className="flex items-start gap-2">
                          {!n.isRead && (
                            <span className="w-1.5 h-1.5 mt-1.5 rounded-full bg-indigo-500 shadow-[0_0_6px_rgba(99,102,241,0.7)] flex-shrink-0" />
                          )}
                          <div className="flex-1 min-w-0">
                            <div className="flex items-center gap-1">
                              {n.isPinned && (
                                <Pin size={10} strokeWidth={2.2} className={(isDark ? 'text-amber-400' : 'text-amber-500') + ' flex-shrink-0'} />
                              )}
                              <div className={'text-[12.5px] font-semibold leading-snug truncate ' + (n.isRead ? (isDark ? 'text-white/70' : 'text-slate-600') : '')}>
                                {n.title}
                              </div>
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

                          {/* Hover aksiyonlari: uste tuttur / okundu isaretle / sil */}
                          {!isConfirmingDelete && (
                            <div className="flex items-center gap-0.5 opacity-0 group-hover:opacity-100 transition-opacity flex-shrink-0">
                              <button
                                onClick={function (e) { handleTogglePinClick(n, e) }}
                                title={n.isPinned ? tShell('notif_unpin', lang) : tShell('notif_pin', lang)}
                                className={
                                  'p-1 rounded-md transition-colors ' +
                                  (n.isPinned
                                    ? (isDark ? 'text-amber-400 hover:bg-white/10' : 'text-amber-500 hover:bg-amber-100')
                                    : (isDark ? 'text-white/40 hover:text-white hover:bg-white/10' : 'text-slate-400 hover:text-slate-700 hover:bg-slate-100'))
                                }
                              >
                                {n.isPinned ? <PinOff size={12} /> : <Pin size={12} />}
                              </button>
                              {!n.isRead && (
                                <button
                                  onClick={function (e) { handleMarkReadClick(n, e) }}
                                  title={tShell('notif_mark_read', lang)}
                                  className={
                                    'p-1 rounded-md transition-colors ' +
                                    (isDark ? 'text-white/40 hover:text-emerald-400 hover:bg-white/10' : 'text-slate-400 hover:text-emerald-600 hover:bg-slate-100')
                                  }
                                >
                                  <Check size={12} />
                                </button>
                              )}
                              <button
                                onClick={function (e) { handleDeleteClick(n, e) }}
                                title={tShell('notif_delete', lang)}
                                className={
                                  'p-1 rounded-md transition-colors ' +
                                  (isDark ? 'text-white/40 hover:text-rose-400 hover:bg-white/10' : 'text-slate-400 hover:text-rose-600 hover:bg-slate-100')
                                }
                              >
                                <Trash2 size={12} />
                              </button>
                            </div>
                          )}
                        </div>

                        {/* Panel-ici mini sil-onayi — tam ekran modal degil (kucuk yuzey). */}
                        {isConfirmingDelete && (
                          <div
                            onClick={function (e) { e.stopPropagation() }}
                            className={
                              'mt-2 flex items-center justify-between gap-2 pl-2 pr-1.5 py-1.5 rounded-lg text-[11px] ' +
                              (isDark ? 'bg-rose-500/10 text-rose-300' : 'bg-rose-50 text-rose-700')
                            }
                          >
                            <span className="font-medium">{tShell('notif_delete_confirm', lang)}</span>
                            <div className="flex items-center gap-1 flex-shrink-0">
                              <button
                                onClick={function (e) { handleCancelDelete(e) }}
                                className={
                                  'px-2 py-0.5 rounded-md font-medium transition-colors ' +
                                  (isDark ? 'hover:bg-white/10 text-white/60' : 'hover:bg-slate-200 text-slate-600')
                                }
                              >
                                {tShell('cancel', lang)}
                              </button>
                              <button
                                onClick={function (e) { handleConfirmDelete(n, e) }}
                                className="px-2 py-0.5 rounded-md font-semibold text-white bg-rose-600 hover:bg-rose-500 transition-colors"
                              >
                                {tShell('notif_delete_yes', lang)}
                              </button>
                            </div>
                          </div>
                        )}
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
            style={{ border: '2px solid var(--app-surface)' }}
          />
        </motion.button>
      </div>
    </header>
  )
}

/* ══════════════════════════════════════════════════════════════
   MiniSwitch — CLAUDE.md standardı: boolean alan = toggle switch,
   checkbox değil. Header kısayol çubuğu + kısayol picker'ı ortak kullanır.
   ══════════════════════════════════════════════════════════════ */
function MiniSwitch(props) {
  var isDark = props.isDark
  var checked = !!props.checked
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={props.label}
      onClick={props.onClick}
      className={
        'relative inline-flex items-center h-4 w-7 rounded-full transition-colors flex-shrink-0 ' +
        (checked ? 'bg-indigo-500' : (isDark ? 'bg-white/15' : 'bg-slate-300'))
      }
    >
      <span
        className="inline-block h-3 w-3 rounded-full bg-white shadow transform transition-transform"
        style={{ transform: checked ? 'translateX(14px)' : 'translateX(2px)' }}
      />
    </button>
  )
}

/* ══════════════════════════════════════════════════════════════
   ShortcutsBar — header hızlı erişim çubuğu.
   Normal mod : 🏠 + kullanıcı kısayolları (ikon; "İsimler" açıksa ikon+ad) + ✏️
   Düzenleme  : kısayollar ad+X (kaldır) + "+" (picker) + "İsimler" switch + ✓ (kaydet)
   Kalıcılık  : services/shellShortcutsService.js (user_settings → yoksa localStorage).
   ══════════════════════════════════════════════════════════════ */
// PageComment Seq 1117 (2026-08-26): "Islemler" menusu animasyonlu acilir.
// Istek Malzeme Karti ekraninda dogdu; menu global Shell parcasi oldugu icin
// once ekran-tespitli dar bir scope yazilmisti. Kullanici animasyonun TUM
// ekranlarda standart olmasini istedi (2026-08-26) -> tespit kodu kaldirildi:
// URL/DOM sezgisine dayali kontroller adres degisince sessizce devre disi
// kalir, tek davranis olmasi hem tutarli hem bakimi kolay.

function ShortcutsBar(props) {
  var isDark = props.isDark
  var lang = props.lang || 'TR'
  var menu = props.menu || []
  var onNavigate = props.onNavigate
  var onGoHome = props.onGoHome
  var onOpenHelp = props.onOpenHelp

  var gearRef = useRef(null)
  var [actionsOpen, setActionsOpen] = useState(false)
  var [actionsPos, setActionsPos] = useState({ top: 0, left: 0 })
  var [shortcutKeys, setShortcutKeys] = useState([])

  // İşlemler menüsü: butonun altına konumlandır (portal ile body'ye render → overflow kırpmaz)
  function toggleActions() {
    if (!actionsOpen && gearRef.current) {
      var r = gearRef.current.getBoundingClientRect()
      setActionsPos({ top: Math.round(r.bottom + 6), left: Math.round(r.left) })
    }
    setActionsOpen(function(v) { return !v })
  }
  // Esc → İşlemler menüsünü kapat
  useEffect(function() {
    if (!actionsOpen) return undefined
    function onKey(e) { if (e.key === 'Escape') setActionsOpen(false) }
    window.addEventListener('keydown', onKey)
    return function() { window.removeEventListener('keydown', onKey) }
  }, [actionsOpen])
  var [showNames, setShowNames] = useState(false)
  var [loaded, setLoaded] = useState(false)
  var [editMode, setEditMode] = useState(false)
  var [pickerOpen, setPickerOpen] = useState(false)
  var savedSnapshotRef = useRef({ ids: [], showNames: false })

  var options = useMemo(function() { return flattenMenuLeaves(menu) }, [menu])
  var optionIndex = useMemo(function() {
    var m = {}
    options.forEach(function(o) { m[o.key] = o })
    return m
  }, [options])

  // ── İlk yükleme — kullanıcının kayıtlı kısayolları ──
  useEffect(function() {
    var alive = true
    loadShellShortcuts().then(function(cfg) {
      if (!alive) return
      setShortcutKeys(cfg.ids)
      setShowNames(cfg.showNames)
      savedSnapshotRef.current = { ids: cfg.ids, showNames: cfg.showNames }
      setLoaded(true)
    })
    return function() { alive = false }
  }, [])

  function enterEditMode() {
    savedSnapshotRef.current = { ids: shortcutKeys, showNames: showNames }
    setEditMode(true)
  }
  function commitEdit() {
    var next = { ids: shortcutKeys, showNames: showNames }
    savedSnapshotRef.current = next
    saveShellShortcuts(next)
    setEditMode(false)
    setPickerOpen(false)
  }
  function cancelEdit() {
    setShortcutKeys(savedSnapshotRef.current.ids)
    setShowNames(savedSnapshotRef.current.showNames)
    setEditMode(false)
    setPickerOpen(false)
  }

  // Esc → düzenleme modundan (picker kapalıyken) çık, kaydedilmemiş değişiklikleri at
  useEffect(function() {
    if (!editMode) return undefined
    function onKey(e) {
      if (e.key !== 'Escape' || pickerOpen) return
      cancelEdit()
    }
    document.addEventListener('keydown', onKey)
    return function() { document.removeEventListener('keydown', onKey) }
  }, [editMode, pickerOpen, shortcutKeys, showNames]) // eslint-disable-line react-hooks/exhaustive-deps

  function removeShortcut(key) {
    setShortcutKeys(function(prev) { return prev.filter(function(k) { return k !== key }) })
  }
  function applyPicker(nextKeys) {
    setShortcutKeys(nextKeys)
    setPickerOpen(false)
  }

  var resolved = shortcutKeys.map(function(k) { return optionIndex[k] }).filter(Boolean)

  return (
    <div className="flex-1 min-w-0 flex items-center gap-1.5">
      {loaded && (
        <div className={'w-px h-5 flex-shrink-0 ' + (isDark ? 'bg-white/10' : 'bg-slate-200')} />
      )}

      {/* İşlemler — çark ikonu (yazısız), Ana Sayfa'nın SOLUNDA. Menü portal ile
          body'ye render edilir → ShortcutsBar overflow'u kırpmaz, buton altından açılır. */}
      <div className="flex-shrink-0">
        <button
          ref={gearRef}
          type="button"
          onClick={toggleActions}
          title={tShell('actions', lang)}
          aria-label={tShell('actions', lang)}
          className={
            'flex items-center justify-center w-8 h-8 rounded-lg flex-shrink-0 transition-colors ' +
            (isDark ? 'text-white/60 hover:bg-white/[0.06] hover:text-white' : 'text-slate-500 hover:bg-slate-100 hover:text-slate-900')
          }
        >
          <Settings size={15} strokeWidth={1.8} />
        </button>
      </div>
      {actionsOpen && createPortal(
        <>
          <style>{
            '.shell-actions-menu{animation:shellActionsMenuIn 160ms cubic-bezier(0.16,1,0.3,1);transform-origin:top left;}' +
            '@keyframes shellActionsMenuIn{from{opacity:0;transform:translateY(-6px) scale(0.97);}to{opacity:1;transform:translateY(0) scale(1);}}' +
            '@media (prefers-reduced-motion: reduce){.shell-actions-menu{animation:none;}}'
          }</style>
          <div style={{ position: 'fixed', top: 0, left: 0, right: 0, bottom: 0, zIndex: 1000 }} onClick={function() { setActionsOpen(false) }} />
          <div
            className={
              'shell-actions-menu min-w-[164px] rounded-lg border overflow-hidden py-0.5 ' +
              (isDark ? 'bg-[#15182b] border-white/10 text-white' : 'bg-white border-slate-200 text-slate-800')
            }
            style={{ position: 'fixed', top: actionsPos.top, left: actionsPos.left, zIndex: 1001, boxShadow: '0 10px 32px rgba(0,0,0,0.32)' }}
          >
            <button
              type="button"
              onClick={function() { setActionsOpen(false); if (onOpenHelp) onOpenHelp() }}
              className={
                'w-full flex items-center gap-2 px-2.5 py-1.5 text-[12px] transition-colors ' +
                (isDark ? 'hover:bg-white/[0.06]' : 'hover:bg-slate-100')
              }
            >
              <HelpCircle size={13} strokeWidth={1.8} className="text-indigo-400" />
              <span>{tShell('help', lang)}</span>
              <kbd className={'ml-auto text-[9px] px-1 py-0.5 rounded border ' + (isDark ? 'border-white/15 text-white/50' : 'border-slate-300 text-slate-400')}>F1</kbd>
            </button>
          </div>
        </>,
        document.body
      )}

      {/* Ana Sayfa — sabit, kısayol listesinden bağımsız, kaldırılamaz */}
      <button
        type="button"
        onClick={onGoHome}
        title={tShell('go_home', lang)}
        aria-label={tShell('go_home', lang)}
        className={
          'flex items-center justify-center w-8 h-8 rounded-lg flex-shrink-0 transition-colors ' +
          (isDark ? 'text-white/60 hover:bg-white/[0.06] hover:text-white' : 'text-slate-500 hover:bg-slate-100 hover:text-slate-900')
        }
      >
        <Home size={15} strokeWidth={1.8} />
      </button>

      {/* Kısayol düzenleme kontrolleri (kalem / Kaydet + Ekle + İsimler) — ANA SAYFA'NIN
          SAĞINDA (kullanıcı isteği, 2026-08-29). Daha önce çubuğun sol başındaydı; oradan
          da taşınmıştı çünkü sağ uçta sayfa yorumu (annotation) kalemiyle yan yana düşüp
          karışıyordu. Buradaki yeri her ikisinden de uzak: çark/ana sayfa ikilisinin
          hemen sağında, kısayol chip'lerinden önce. */}
      {loaded && (
        <div className="flex-shrink-0">
          {editMode ? (
            <button
              type="button"
              onClick={commitEdit}
              title={tShell('shortcuts_save', lang)}
              aria-label={tShell('shortcuts_save', lang)}
              className="w-8 h-8 rounded-xl flex items-center justify-center text-white transition-transform hover:scale-105"
              style={{ background: 'linear-gradient(135deg,#22c55e,#16a34a)', boxShadow: '0 4px 12px rgba(34,197,94,0.35)' }}
            >
              <Check size={15} strokeWidth={2.6} />
            </button>
          ) : (
            <button
              type="button"
              onClick={enterEditMode}
              title={tShell('shortcuts_edit', lang)}
              aria-label={tShell('shortcuts_edit', lang)}
              className={
                'w-8 h-8 rounded-xl flex items-center justify-center transition-colors ' +
                (isDark ? 'text-white/40 hover:text-white hover:bg-white/[0.06]' : 'text-slate-400 hover:text-slate-800 hover:bg-slate-100')
              }
            >
              <Pencil size={14} strokeWidth={2} />
            </button>
          )}
        </div>
      )}

      {loaded && editMode && (
        <button
          type="button"
          onClick={function() { setPickerOpen(true) }}
          title={tShell('shortcuts_add', lang)}
          className={
            'flex items-center justify-center gap-1 w-8 h-8 rounded-lg flex-shrink-0 border border-dashed transition-colors ' +
            (isDark ? 'border-white/20 text-white/60 hover:text-white hover:border-white/40 hover:bg-white/[0.05]'
                    : 'border-slate-300 text-slate-500 hover:text-slate-800 hover:border-slate-400 hover:bg-slate-50')
          }
        >
          <Plus size={13} strokeWidth={2.4} />
        </button>
      )}

      {loaded && editMode && (
        <label
          className={
            'flex items-center gap-1.5 h-8 px-2 rounded-lg flex-shrink-0 cursor-pointer select-none ' +
            (isDark ? 'text-white/60 hover:text-white' : 'text-slate-500 hover:text-slate-800')
          }
          title={tShell('shortcuts_shownames', lang)}
        >
          <span className="text-[11px] font-medium whitespace-nowrap">{tShell('shortcuts_shownames', lang)}</span>
          <MiniSwitch
            isDark={isDark}
            checked={showNames}
            label={tShell('shortcuts_shownames', lang)}
            onClick={function() { setShowNames(function(v) { return !v }) }}
          />
        </label>
      )}


      {loaded && (resolved.length > 0 || editMode) && (
        <div className={'w-px h-5 flex-shrink-0 ' + (isDark ? 'bg-white/10' : 'bg-slate-200')} />
      )}

      {/* Kısayol chip'leri — TAŞMA YALNIZ BURADA olur (sekme şeridiyle aynı ok
          yapısı). Önceden tüm çubuk kayıyordu; kısayol eklendikçe sağdaki
          Ekle / İsimler / Kaydet kontrolleri görüntüden çıkıyordu. Bu alan
          flex-1 olduğu için kısayol yokken de sağ kontrolleri sağa yaslar. */}
      <TabScrollArea
        isDark={isDark}
        itemCount={resolved.length}
        className="flex-1 min-w-0 h-8"
        gapClass="gap-1.5"
        padLeft={0}
        padRight={0}
        chevronBg={isDark ? '#0a0d17' : '#ffffff'}
      >
        {loaded && resolved.map(function(item) {
          return (
            <ShortcutChip
              key={item.key}
              isDark={isDark}
              item={item}
              editMode={editMode}
              showLabel={showNames || editMode}
              onClick={function() { if (!editMode && onNavigate) onNavigate(item) }}
              onRemove={function() { removeShortcut(item.key) }}
              removeLabel={tShell('shortcuts_remove', lang)}
            />
          )
        })}
      </TabScrollArea>

      <AnimatePresence>
        {pickerOpen && (
          <ShortcutPickerModal
            isDark={isDark}
            lang={lang}
            options={options}
            selectedKeys={shortcutKeys}
            onApply={applyPicker}
            onClose={function() { setPickerOpen(false) }}
          />
        )}
      </AnimatePresence>
    </div>
  )
}

/* Tek bir kısayol çubuğu öğesi — normal modda ikon (+ opsiyonel ad),
   düzenleme modunda her zaman ikon+ad+kaldır (x) butonu. */
function ShortcutChip(props) {
  var isDark = props.isDark
  var item = props.item
  var editMode = props.editMode
  var showLabel = props.showLabel
  var Icon = resolveIcon(item.icon)

  var chipClasses =
    'group relative flex items-center h-8 rounded-lg flex-shrink-0 transition-colors select-none cursor-pointer gap-1.5 ' +
    (showLabel ? ('pl-2.5 ' + (editMode ? 'pr-6' : 'pr-2.5')) : 'w-8 justify-center') + ' ' +
    (isDark ? 'text-white/70 hover:bg-white/[0.06] hover:text-white' : 'text-slate-600 hover:bg-slate-100 hover:text-slate-900')

  return (
    <div onClick={props.onClick} title={item.label} className={chipClasses}>
      <Icon size={15} strokeWidth={1.8} className="flex-shrink-0" />
      {showLabel && (
        <span className="text-[12px] font-medium truncate max-w-[130px]">{item.label}</span>
      )}
      {editMode && (
        <button
          type="button"
          onClick={function(e) { e.stopPropagation(); props.onRemove() }}
          title={props.removeLabel}
          aria-label={props.removeLabel}
          className={
            'absolute right-1 top-1/2 -translate-y-1/2 w-4 h-4 rounded-full flex items-center justify-center transition-colors ' +
            (isDark ? 'bg-rose-500/20 text-rose-300 hover:bg-rose-500/40' : 'bg-rose-100 text-rose-500 hover:bg-rose-200')
          }
        >
          <X size={9} strokeWidth={3} />
        </button>
      )}
    </div>
  )
}

/* ══════════════════════════════════════════════════════════════
   ShortcutPickerModal — menüden yeni kısayol seçme paneli.
   QuickLinksPickerModal (Dashboard/widgets) ile aynı UX sözleşmesi: arama +
   groupLabel'a göre gruplama + switch toggle + Uygula/Vazgeç. Görsel dil
   Shell.jsx'in kendi popover'larıyla (ProfilePopover/OpenTabsPopover) tutarlı.
   ══════════════════════════════════════════════════════════════ */
function ShortcutPickerModal(props) {
  var isDark = props.isDark
  var lang = props.lang || 'TR'
  var options = props.options || []
  var [selected, setSelected] = useState(function() { return new Set(props.selectedKeys || []) })
  var [search, setSearch] = useState('')
  var searchRef = useRef(null)

  useEffect(function() {
    var t = setTimeout(function() { if (searchRef.current) searchRef.current.focus() }, 60)
    return function() { clearTimeout(t) }
  }, [])

  useEffect(function() {
    function onKey(e) { if (e.key === 'Escape') { if (props.onClose) props.onClose() } }
    document.addEventListener('keydown', onKey)
    return function() { document.removeEventListener('keydown', onKey) }
  }, []) // eslint-disable-line react-hooks/exhaustive-deps

  function toggle(key) {
    setSelected(function(prev) {
      var next = new Set(prev)
      if (next.has(key)) next.delete(key); else next.add(key)
      return next
    })
  }

  var q = search.trim().toLocaleLowerCase('tr-TR')
  var filtered = q
    ? options.filter(function(o) {
        return (o.label || '').toLocaleLowerCase('tr-TR').indexOf(q) !== -1 ||
               (o.groupLabel || '').toLocaleLowerCase('tr-TR').indexOf(q) !== -1
      })
    : options
  var groupsMap = {}
  var groupOrder = []
  filtered.forEach(function(o) {
    var g = o.groupLabel || o.label
    if (!groupsMap[g]) { groupsMap[g] = []; groupOrder.push(g) }
    groupsMap[g].push(o)
  })

  var panelBg = 'var(--app-surface)'
  var panelBorder = '1px solid var(--app-border)'

  return (
    <motion.div
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      exit={{ opacity: 0 }}
      transition={{ duration: 0.15 }}
      onClick={props.onClose}
      className="fixed inset-0 z-[10010] flex items-start justify-center p-4"
      style={{ background: 'rgba(0,0,0,.45)', backdropFilter: 'blur(3px)', WebkitBackdropFilter: 'blur(3px)', paddingTop: '10vh' }}
    >
      <motion.div
        initial={{ opacity: 0, y: -8, scale: 0.97 }}
        animate={{ opacity: 1, y: 0, scale: 1 }}
        exit={{ opacity: 0, y: -8, scale: 0.97 }}
        transition={{ duration: 0.16 }}
        onClick={function(e) { e.stopPropagation() }}
        role="dialog"
        aria-modal="true"
        className={'w-full max-w-md rounded-2xl overflow-hidden flex flex-col ' + (isDark ? 'text-white' : 'text-slate-900')}
        style={{ maxHeight: '70vh', background: panelBg, border: panelBorder, boxShadow: '0 24px 70px rgba(0,0,0,0.45)' }}
      >
        <div className={'flex items-center gap-2.5 px-4 py-3.5 border-b flex-shrink-0 ' + (isDark ? 'border-white/10' : 'border-slate-200')}>
          <div
            className="w-8 h-8 rounded-lg flex items-center justify-center flex-shrink-0"
            style={{ background: 'linear-gradient(135deg,#6366f1,#8b5cf6)', boxShadow: '0 6px 14px rgba(99,102,241,0.3)' }}
          >
            <Zap size={15} className="text-white" strokeWidth={2} />
          </div>
          <h3 className="flex-1 text-sm font-bold">{tShell('shortcuts_picker_title', lang)}</h3>
          <button
            type="button"
            onClick={props.onClose}
            aria-label={tShell('cancel', lang)}
            className={
              'w-7 h-7 rounded-lg flex items-center justify-center transition-colors ' +
              (isDark ? 'hover:bg-white/10 text-white/50 hover:text-white' : 'hover:bg-slate-100 text-slate-400 hover:text-slate-700')
            }
          >
            <X size={14} strokeWidth={2.4} />
          </button>
        </div>

        <div className="px-4 pt-3 pb-1 flex-shrink-0">
          <div className="relative">
            <Search size={13} className={'absolute left-3 top-1/2 -translate-y-1/2 pointer-events-none ' + (isDark ? 'text-white/45' : 'text-slate-400')} />
            <input
              ref={searchRef}
              type="text"
              value={search}
              onChange={function(e) { setSearch(e.target.value) }}
              placeholder={tShell('shortcuts_picker_search', lang)}
              style={{ userSelect: 'text', WebkitUserSelect: 'text' }}
              className={
                'w-full pl-8 pr-3 py-1.5 rounded-lg text-[12.5px] outline-none transition-colors ' +
                (isDark
                  ? 'bg-white/[0.05] border border-white/10 text-white placeholder:text-white/40 focus:border-indigo-400/50'
                  : 'bg-slate-50 border border-slate-200 text-slate-800 placeholder:text-slate-400 focus:border-indigo-400/60')
              }
            />
          </div>
        </div>

        <div className="flex-1 overflow-y-auto px-2 py-2 smartcard-widgets-scroll">
          {groupOrder.length === 0 && (
            <div className={'text-center py-8 text-[12px] ' + (isDark ? 'text-white/40' : 'text-slate-400')}>
              {tShell('shortcuts_picker_empty', lang)}
            </div>
          )}
          {groupOrder.map(function(g) {
            return (
              <div key={g} className="mb-1">
                <div className={'px-2.5 pt-2 pb-1 text-[10px] font-bold uppercase tracking-wider ' + (isDark ? 'text-white/35' : 'text-slate-400')}>
                  {g}
                </div>
                {groupsMap[g].map(function(o) {
                  var Icon = resolveIcon(o.icon)
                  var on = selected.has(o.key)
                  return (
                    <div
                      key={o.key}
                      onClick={function() { toggle(o.key) }}
                      className={
                        'flex items-center gap-2.5 px-2.5 py-2 rounded-lg cursor-pointer transition-colors ' +
                        (isDark ? 'hover:bg-white/[0.05]' : 'hover:bg-slate-100')
                      }
                    >
                      <Icon size={15} strokeWidth={1.8} className={'flex-shrink-0 ' + (isDark ? 'text-white/50' : 'text-slate-500')} />
                      <span className="flex-1 text-[12.5px] font-medium truncate">{o.label}</span>
                      <MiniSwitch
                        isDark={isDark}
                        checked={on}
                        label={o.label}
                        onClick={function(e) { e.stopPropagation(); toggle(o.key) }}
                      />
                    </div>
                  )
                })}
              </div>
            )
          })}
        </div>

        <div className={'flex items-center gap-2 px-4 py-3 border-t flex-shrink-0 ' + (isDark ? 'border-white/10' : 'border-slate-200')}>
          <span className={'text-[11px] font-medium mr-auto ' + (isDark ? 'text-white/45' : 'text-slate-500')}>
            {selected.size} {tShell('shortcuts_picker_selected_suffix', lang)}
          </span>
          <button
            type="button"
            onClick={props.onClose}
            className={
              'px-3.5 py-1.5 rounded-lg text-[12.5px] font-semibold transition-colors ' +
              (isDark ? 'bg-white/10 text-white hover:bg-white/15' : 'bg-slate-100 text-slate-700 hover:bg-slate-200')
            }
          >
            {tShell('shortcuts_picker_cancel', lang)}
          </button>
          <button
            type="button"
            onClick={function() { if (props.onApply) props.onApply(Array.from(selected)) }}
            className="px-3.5 py-1.5 rounded-lg text-[12.5px] font-bold text-white transition-all"
            style={{ background: 'linear-gradient(135deg,#6366f1,#8b5cf6)', boxShadow: '0 4px 14px rgba(99,102,241,0.35)' }}
          >
            {tShell('shortcuts_picker_apply', lang)}
          </button>
        </div>
      </motion.div>
    </motion.div>
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

  var glassBg = 'var(--app-surface)'
  var glassBorder = '1px solid var(--app-border)'

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

      {/* Alt şerit: Şirket Değiştir + Çıkış Yap AYNI SATIRDA.
          Şirket değiştirme modalı Shell kökünde açılır (bu popover kapandığında
          modalın da kapanmaması için) — burada yalnız tetikleyici var. */}
      <div className="p-2 flex items-center gap-2">
        <button
          type="button"
          onClick={function() {
            if (props.onOpenCompanySwitch) props.onOpenCompanySwitch()
            if (props.onClose) props.onClose()
          }}
          className={
            'flex-1 flex items-center justify-center gap-2 px-3 py-2.5 rounded-xl text-[13px] font-semibold transition-all ' +
            (isDark
              ? 'text-white/80 bg-white/[0.05] hover:bg-white/[0.09]'
              : 'text-slate-700 bg-slate-100 hover:bg-slate-200')
          }
        >
          <Building2 size={15} strokeWidth={2} />
          <span>{tShell('switch_company', props.lang || 'TR')}</span>
        </button>
        <a
          href="/Account/Logout"
          onClick={function(e) {
            // 2026-08-25 kullanici istegi: cikis ONAY ister. Proje standardi geregi
            // native confirm() DEGIL, ekran-ortasi ozel modal (window.showConfirm,
            // _Layout'ta global yuklenir). Helper yoksa (beklenmedik durum) baglanti
            // normal calisir — fail-open: kullanici cikamaz halde kalmasin.
            if (typeof window.showConfirm !== 'function') return
            e.preventDefault()
            var tr = (props.lang || 'TR') === 'TR'
            window.showConfirm({
              title: tr ? 'Çıkış Yap' : 'Sign Out',
              message: tr
                ? 'Oturumunuz kapatılacak. Çıkış yapmak istediğinize emin misiniz?'
                : 'Your session will be closed. Are you sure you want to sign out?',
              okLabel: tr ? 'Evet, Çıkış Yap' : 'Yes, Sign Out',
              cancelLabel: tr ? 'Vazgeç' : 'Cancel',
              danger: true,
            }).then(function(ok) {
              if (ok) window.location.href = '/Account/Logout'
            })
          }}
          className={
            'flex-1 flex items-center justify-center gap-2 px-3 py-2.5 rounded-xl text-[13px] font-semibold transition-all no-underline ' +
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

  var glassBg = 'var(--app-surface)'
  var glassBorder = '1px solid var(--app-border)'

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
            : (isActive ? 'linear-gradient(135deg,#6366f1,#8b5cf6)' : 'var(--app-text-muted)')
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

/**
 * CompanySwitchModal — kullanıcının YETKİLİ olduğu şirketler arasında geçiş.
 *
 * GET  /Account/MyCompanies    → [{ id, name, isCurrent }]
 * POST /Account/SwitchCompany  → { companyId }
 *
 * Parola İSTENMEZ. Yetki kapısı sunucudadır: hedef şirkette aynı e-postaya ait
 * AKTİF kullanıcı kaydı yoksa istek reddedilir (AccountController.SwitchCompany).
 * Buradaki liste yalnızca gösterimdir — güvenlik ona dayanmaz.
 *
 * Geçiş sonrası TAM SAYFA yenileme şart: menü, yetkiler ve açık workspace
 * iframe'lerinin hepsi yeni şirkete göre yeniden kurulmalı.
 */
function CompanySwitchModal(props) {
  var isDark = props.isDark
  var lang = props.lang || 'TR'
  var [state, setState] = useState({ loading: true, error: null, items: null })
  var [busyId, setBusyId] = useState(0)

  useEffect(function() {
    function onKey(e) { if (e.key === 'Escape' && !busyId && props.onClose) props.onClose() }
    document.addEventListener('keydown', onKey)
    return function() { document.removeEventListener('keydown', onKey) }
  }, [busyId]) // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(function() {
    fetch('/Account/MyCompanies', { credentials: 'same-origin', headers: { Accept: 'application/json' } })
      .then(function(r) { return r.json() })
      .then(function(list) {
        setState({ loading: false, error: null, items: Array.isArray(list) ? list : [] })
      })
      .catch(function() {
        setState({ loading: false, error: tShell('switch_company_error', lang), items: null })
      })
  }, []) // eslint-disable-line react-hooks/exhaustive-deps

  function pick(c) {
    if (c.isCurrent || busyId) return
    // Veritabanına ulaşılamayan şirket seçilemez (sunucu da ayrıca reddeder — bu yalnız
    // kullanıcıyı boş bir denemeden ve ham hata sayfasından korur).
    if (c.available === false) return
    setBusyId(c.id)
    fetch('/Account/SwitchCompany', {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': props.antiforgery || '' },
      body: JSON.stringify({ companyId: c.id }),
    })
      .then(function(r) { return r.json() })
      .then(function(d) {
        if (d && d.ok) { window.location.href = '/'; return }
        setBusyId(0)
        setState(function(p) { return { loading: false, error: (d && d.error) || 'Şirket değiştirilemedi.', items: p.items } })
      })
      .catch(function() {
        setBusyId(0)
        setState(function(p) { return { loading: false, error: 'Bağlantı hatası.', items: p.items } })
      })
  }

  var items = state.items || []

  // Sirket satirindaki veritabani bilgi satiri — mevcut sirket ve digerleri
  // ayni islevi kullanir (DRY). Rozet yalniz sameDbAsCurrent === false ise cikar.
  function dbLine(c) {
    var name = c.databaseName || 'varsayılan veritabanı'
    var diff = c.sameDbAsCurrent === false
    return (
      <span className="flex items-center gap-1.5 mt-0.5">
        <span
          className={'text-[10.5px] truncate ' + (isDark ? 'text-white/40' : 'text-slate-500')}
          style={{ fontFamily: 'ui-monospace, Menlo, Consolas, monospace' }}
          title={diff ? ('Farklı veritabanı: ' + name) : name}
        >
          {name}
        </span>
        {diff && (
          /* Metin rozeti yerine VERİTABANI İKONU (2026-08-23 kullanıcı isteği) — satırın
             solundaki şirket ikonu yerinde kalır, fark bu amber ikonla belirtilir. */
          <Database
            size={11}
            strokeWidth={2}
            className={'flex-shrink-0 ' + (isDark ? 'text-amber-300' : 'text-amber-600')}
            aria-label="Farklı veritabanı"
          />
        )}
      </span>
    )
  }

  // Panel SAYFANIN ORTASINDA acilir (2026-08-25 kullanici tercihi). Baloncuk hissi
  // konumdan degil, ANIMASYONDAN gelir: asagidaki canli yay ile panel kucukten
  // buyuyup hedefi biraz asarak yerine oturur.
  var reduceMotion = typeof window !== 'undefined'
    && window.matchMedia
    && window.matchMedia('(prefers-reduced-motion: reduce)').matches

  // Yay: iOS baglam menusune yakin his. 2026-08-25 kullanici istegi uzerine DAHA CANLI
  // ayarlandi — sonumleme (damping) dusuruldu, sertlik artirildi: panel hedefi bir miktar
  // asip geri oturuyor (gorunur "zipla" etkisi). reduceMotion'da animasyon yine sade kalir.
  var bubbleIn = reduceMotion
    ? { duration: 0.12 }
    : { type: 'spring', stiffness: 620, damping: 17, mass: 0.8, restDelta: 0.001 }

  return (
    <motion.div
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      exit={{ opacity: 0 }}
      transition={{ duration: 0.15 }}
      onClick={function() { if (!busyId && props.onClose) props.onClose() }}
      className="fixed inset-0 z-[10010] flex items-start justify-center p-4"
      style={{ background: 'rgba(0,0,0,.45)', backdropFilter: 'blur(3px)', WebkitBackdropFilter: 'blur(3px)', paddingTop: '14vh' }}
    >
      <motion.div
        initial={reduceMotion ? { opacity: 0 } : { opacity: 0, scale: 0.72 }}
        animate={reduceMotion ? { opacity: 1 } : { opacity: 1, scale: 1 }}
        exit={reduceMotion ? { opacity: 0 } : { opacity: 0, scale: 0.88, transition: { duration: 0.13 } }}
        transition={bubbleIn}
        onClick={function(e) { e.stopPropagation() }}
        role="dialog"
        aria-modal="true"
        className={'w-full max-w-sm rounded-2xl overflow-hidden flex flex-col ' + (isDark ? 'text-white' : 'text-slate-900')}
        style={{ maxHeight: '70vh', background: 'var(--app-surface)', border: '1px solid var(--app-border)', boxShadow: '0 24px 70px rgba(0,0,0,0.45)' }}
      >
        <div className={'flex items-center gap-2.5 px-4 py-3.5 border-b flex-shrink-0 ' + (isDark ? 'border-white/10' : 'border-slate-200')}>
          <div
            className="w-8 h-8 rounded-lg flex items-center justify-center flex-shrink-0"
            style={{ background: 'linear-gradient(135deg,#6366f1,#8b5cf6)', boxShadow: '0 6px 14px rgba(99,102,241,0.3)' }}
          >
            <Building2 size={15} strokeWidth={2} className="text-white" />
          </div>
          <span className="flex-1 text-[14px] font-bold">{tShell('switch_company', lang)}</span>
          <button
            type="button"
            onClick={function() { if (!busyId && props.onClose) props.onClose() }}
            className={'w-7 h-7 rounded-lg flex items-center justify-center transition-colors ' +
              (isDark ? 'text-white/45 hover:text-white hover:bg-white/[0.08]' : 'text-slate-400 hover:text-slate-800 hover:bg-slate-100')}
            aria-label="Kapat"
          >
            <X size={14} strokeWidth={2} />
          </button>
        </div>

        <div className="flex-1 min-h-0 overflow-y-auto p-2 flex flex-col gap-1">
          {state.loading && (
            <span className={'text-[12.5px] px-2 py-3 text-center ' + (isDark ? 'text-white/45' : 'text-slate-500')}>
              {tShell('switch_company_loading', lang)}
            </span>
          )}
          {state.error && (
            <span className="text-[12.5px] px-2 py-2 text-rose-400">{state.error}</span>
          )}
          {!state.loading && items.length === 0 && !state.error && (
            <span className={'text-[12.5px] px-2 py-3 text-center ' + (isDark ? 'text-white/45' : 'text-slate-500')}>
              {tShell('switch_company_empty', lang)}
            </span>
          )}
          {items.map(function(c) {
            if (c.isCurrent) {
              return (
                <span
                  key={c.id}
                  className={'flex items-center gap-2 text-[13px] px-3 py-2.5 rounded-xl font-semibold ' +
                    (isDark ? 'text-indigo-300 bg-indigo-500/10' : 'text-indigo-600 bg-indigo-50')}
                >
                  <Check size={14} strokeWidth={2.5} />
                  <span className="flex-1 min-w-0">
                    <span className="block truncate">{c.name}</span>
                    {dbLine(c)}
                  </span>
                </span>
              )
            }
            /* Veritabanı silinmiş/erişilemez şirket (2026-08-24): geçiş sunucuda zaten
               reddediliyor, burada da SEÇİLEMEZ gösterilir — kullanıcı ulaşılamayan bir
               şirketi denemek zorunda kalmasın. `available` alanı gelmiyorsa (eski yanıt)
               satır normal davranır: fail-open. */
            var unavailable = c.available === false
            return (
              <button
                key={c.id}
                type="button"
                disabled={!!busyId || unavailable}
                onClick={function() { pick(c) }}
                title={unavailable ? (c.unavailableReason || 'Veritabanına ulaşılamıyor') : undefined}
                className={
                  'w-full flex items-center gap-2 text-left text-[13px] px-3 py-2.5 rounded-xl transition-colors ' +
                  (busyId === c.id ? 'opacity-60 ' : '') +
                  (unavailable ? 'opacity-55 cursor-not-allowed ' : (busyId ? 'cursor-not-allowed ' : '')) +
                  (unavailable
                    ? (isDark ? 'text-white/45' : 'text-slate-400')
                    : (isDark ? 'text-white/80 hover:bg-white/[0.06]' : 'text-slate-700 hover:bg-slate-100'))
                }
              >
                {unavailable
                  ? <AlertTriangle size={14} strokeWidth={1.8} className="text-amber-500" />
                  : <Building2 size={14} strokeWidth={1.8} className={isDark ? 'text-white/40' : 'text-slate-400'} />}
                <span className="flex-1 min-w-0">
                  <span className="block truncate">{c.name}</span>
                  {unavailable ? (
                    <span className={'block text-[10.5px] font-medium ' + (isDark ? 'text-amber-300/70' : 'text-amber-600')}>
                      {c.unavailableReason || 'Veritabanına ulaşılamıyor'}
                    </span>
                  ) : dbLine(c)}
                </span>
              </button>
            )
          })}
        </div>
      </motion.div>
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
   Yatay kaydirilabilir sekme seridi (tasma oklari)
   Ust satir ve child (nested) satir AYNI bileseni kullanir: sag/sol ok,
   tekerlek→yatay kaydirma ve aktif sekmeyi gorunur kilma tek yerdedir.
   ══════════════════════════════════════════════════════════════ */
function TabScrollArea(props) {
  var isDark = props.isDark
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

  // Sekme listesi degistiginde / aktif sekme degistiginde tasma yeniden
  // hesaplanir ve aktif sekme gorunur hale getirilir.
  useEffect(function() {
    recomputeOverflow()
    var el = scrollRef.current
    if (!el || !props.activeKey) return
    var active = el.querySelector('[data-tab-key="' + props.activeKey + '"]')
    if (active && active.scrollIntoView) {
      active.scrollIntoView({ behavior: 'smooth', block: 'nearest', inline: 'nearest' })
    }
  }, [props.itemCount, props.activeKey])

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
    (isDark ? 'border border-white/10 text-white/60 hover:text-white hover:bg-white/[0.06]'
            : 'border border-slate-200 text-slate-500 hover:text-slate-900 hover:bg-slate-100')
  // Ok butonunun zemini seridin zeminiyle ayni olmali ki altindaki sekme
  // metni ok'un arkasindan sizmasin (ust satir icerik zemini, child satir
  // muted zemin kullanir).
  var chevronBg = props.chevronBg || 'var(--app-content-bg)'
  var padLeft = typeof props.padLeft === 'number' ? props.padLeft : 8
  var padRight = typeof props.padRight === 'number' ? props.padRight : 16

  return (
    <div className={'relative overflow-hidden ' + (props.className || '')}>
      {canLeft && (
        <button
          type="button"
          onClick={function() { scrollBy(-200) }}
          className={chevronBtn}
          style={{ left: 4, background: chevronBg }}
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
          style={{ right: 4, background: chevronBg }}
          title="Sağa kaydır"
        >
          <ChevronRight size={14} strokeWidth={2.2} />
        </button>
      )}
      <div
        ref={scrollRef}
        onWheel={handleWheel}
        className={'flex items-center h-full overflow-x-auto smartcard-widgets-scroll ' + (props.gapClass || 'gap-1')}
        style={{ paddingLeft: canLeft ? 34 : padLeft, paddingRight: canRight ? 34 : padRight }}
      >
        {props.children}
      </div>
    </div>
  )
}

/* ══════════════════════════════════════════════════════════════
   Tab bar
   ══════════════════════════════════════════════════════════════ */
function TabBar(props) {
  var isDark = props.isDark
  var lang = props.lang || 'TR'
  var borderColor = isDark ? 'border-white/[0.06]' : 'border-slate-200/80'

  var showDash = !!props.showDashboard

  // Sekme yokken şerit tamamen gizlenir — boş 44px bant bırakma (ana sayfa
  // doğrudan üst çubuğun altından başlar; home ikonu zaten kısayol çubuğunda).
  if (!props.tabs || props.tabs.length === 0) return null

  // Nested (child) tab gruplama (PageComment Seq 1063, 2026-08-03) — üst satır
  // her zaman sadece üst-seviye (parentKey'siz) sekmeleri gösterir; aktif sekmenin
  // (veya aktif sekme bir child ise onun parent'ının) child'ları ikinci bir satırda
  // gösterilir. parentKey hiç kullanılmayan ekranlarda childTabs her zaman boş
  // kalır → görsel olarak ESKİSİ GİBİ tek satır (regresyon yok).
  var allTabs = props.tabs
  var topTabs = allTabs.filter(function(t) { return !t.parentKey })
  var activeTabObj = allTabs.find(function(t) { return t.key === props.activeKey })
  var activeGroupKey = activeTabObj ? (activeTabObj.parentKey || activeTabObj.key) : null
  var childTabs = activeGroupKey ? allTabs.filter(function(t) { return t.parentKey === activeGroupKey }) : []

  function renderTabChip(t, isChild, isAnchor) {
    var isActive = !showDash && (isChild ? t.key === props.activeKey : (t.key === props.activeKey || t.key === activeGroupKey))
    return (
      <div
        key={t.key}
        data-tab-key={t.key}
        onClick={function() { props.onTabClick(t.key) }}
        onMouseDown={function(e) { e.preventDefault() }}
        className={
          'relative flex items-center gap-2 cursor-pointer transition-all flex-shrink-0 select-none ' +
          (isChild ? 'px-2.5 py-1 rounded-md text-[12px] font-medium max-w-[200px] ' : 'px-3 py-1.5 rounded-lg text-[13px] font-medium max-w-[220px] ') +
          (isActive
            ? (isDark ? 'text-white bg-white/[0.06]' : 'text-slate-900 bg-slate-100')
            : (isDark ? 'text-white/50 hover:text-white/80 hover:bg-white/[0.03]' : 'text-slate-500 hover:text-slate-800 hover:bg-slate-50'))
        }
        title={(props.dirtyTabs && props.dirtyTabs[t.key]) ? tShell('unsaved_prefix', lang) + t.title : t.title}
      >
        {isChild && !isAnchor && (
          <CornerDownRight
            size={11}
            strokeWidth={2.2}
            className={'flex-shrink-0 ' + (isDark ? 'text-white/30' : 'text-slate-400')}
          />
        )}
        {isAnchor && (
          <LayoutList
            size={12}
            strokeWidth={2.2}
            className={'flex-shrink-0 ' + (isDark ? 'text-indigo-300/75' : 'text-indigo-500')}
          />
        )}
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
        {/* Liste ankuru (pinli) kapatilamaz — kapatma ust satirdaki ayni sekmeden yapilir. */}
        {!isAnchor && (
          <button
            onClick={function(e) { props.onTabClose(t.key, e) }}
            className={
              'w-4 h-4 rounded flex items-center justify-center transition-colors flex-shrink-0 ' +
              (isDark ? 'hover:bg-white/10 text-white/50 hover:text-white/80' : 'hover:bg-slate-200 text-slate-400 hover:text-slate-700')
            }
          >
            <X size={10} strokeWidth={2.4} />
          </button>
        )}

        {isActive && (
          <motion.div
            layoutId={isChild ? 'child-tab-underline' : 'tab-underline'}
            className="absolute left-2 right-2 -bottom-[6px] h-0.5 rounded-full"
            style={{
              background: 'linear-gradient(90deg, #6366f1 0%, #8b5cf6 100%)',
              boxShadow: '0 0 10px rgba(99,102,241,0.7)',
            }}
          />
        )}
      </div>
    )
  }

  return (
    <div className="flex flex-col flex-shrink-0">
      <div
        className={'flex items-center h-11 border-b ' + borderColor}
        style={{ background: 'var(--app-content-bg)' }}
      >
        {/* Scrollable tab alanı */}
        <TabScrollArea
          isDark={isDark}
          activeKey={props.activeKey}
          itemCount={topTabs.length}
          className="flex-1 h-full"
          padLeft={8}
          padRight={16}
        >
          {topTabs.map(function(t) { return renderTabChip(t, false) })}
        </TabScrollArea>
      </div>

      {/* Child (nested) sekme satırı — sadece aktif grubun altında, üst-seviye
          sekme (liste ekranı gibi) hep görünür kalır. Üst şeritle AYNI taşma
          okları: çok kayıt açıldığında satır sığmıyor, ok olmadan kaydırılamıyordu. */}
      {childTabs.length > 0 && (
        <div
          className={'flex items-center h-9 border-b ' + borderColor}
          style={{ background: 'var(--app-muted-surface)' }}
        >
          <TabScrollArea
            isDark={isDark}
            activeKey={props.activeKey}
            itemCount={childTabs.length}
            className="flex-1 h-full"
            padLeft={18}
            padRight={16}
            chevronBg="var(--app-muted-surface)"
          >
            {/* Liste sekmesi (grup parent'i) her zaman en solda SABİT anchor olarak
                — kayıtlardayken listeye tek tıkla dönülür (kullanıcı isteği 2026-08-03). */}
            {(function() {
              var listTab = allTabs.find(function(x) { return x.key === activeGroupKey })
              return listTab ? renderTabChip(listTab, true, true) : null
            })()}
            {childTabs.map(function(t) { return renderTabChip(t, true) })}
          </TabScrollArea>
        </div>
      )}
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
