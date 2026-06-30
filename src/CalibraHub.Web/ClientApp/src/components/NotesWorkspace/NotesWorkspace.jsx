import { useState, useCallback, useRef, useEffect } from 'react'
import html2pdf from 'html2pdf.js'
import { useEditor, EditorContent } from '@tiptap/react'
import StarterKit from '@tiptap/starter-kit'
import Underline from '@tiptap/extension-underline'
import Link from '@tiptap/extension-link'
import TaskList from '@tiptap/extension-task-list'
import TaskItem from '@tiptap/extension-task-item'
import { Table, TableRow, TableHeader, TableCell } from '@tiptap/extension-table'
import { TextStyle } from '@tiptap/extension-text-style'
import { Color } from '@tiptap/extension-color'
import { Highlight } from '@tiptap/extension-highlight'
import { CodeBlockLowlight } from '@tiptap/extension-code-block-lowlight'
import { Placeholder } from '@tiptap/extension-placeholder'
import { Subscript } from '@tiptap/extension-subscript'
import { Superscript } from '@tiptap/extension-superscript'
import { TextAlign } from '@tiptap/extension-text-align'
import { Image } from '@tiptap/extension-image'
import { Youtube } from '@tiptap/extension-youtube'
import { CharacterCount } from '@tiptap/extension-character-count'
import { Typography } from '@tiptap/extension-typography'
import { Focus } from '@tiptap/extension-focus'
import { common, createLowlight } from 'lowlight'
import { EncryptedMark } from './EncryptedMark'
import { Extension } from '@tiptap/core'
import { Plugin, PluginKey } from '@tiptap/pm/state'
import { Decoration, DecorationSet } from '@tiptap/pm/view'
import { EncryptPromptModal, DecryptPromptModal, FullNoteLockScreen } from './EncryptionModals'
import { AudioRecorder } from './AudioRecorder'
import { DrawingPanel }  from './DrawingPanel'
import { DrawingNodeExtension } from './DrawingNodeExtension'
import { CameraCapture } from './CameraCapture'
import {
  encryptText, decryptText,
  rememberPassword, recallPassword, payloadCacheKey
} from './encryption'
import {
  FolderOpen, FolderClosed, Plus, FileText, Bold, Italic, Underline as UnderlineIcon, List,
  Link as LinkIcon, Trash2, ChevronRight, ChevronDown, StickyNote, Heading1, Heading2,
  Heading3, Quote, ListOrdered, Minus, Code, CheckSquare, Strikethrough,
  PlusCircle, Search, MoreHorizontal, FileDown, Table as TableIcon, Loader2, Pencil,
  Type, Highlighter, Subscript as SubIcon, Superscript as SuperIcon,
  AlignLeft, AlignCenter, AlignRight, AlignJustify, Undo2, Redo2,
  Lock, Unlock,
  ImagePlus, Youtube as YoutubeIcon, RowsIcon, Columns3,
  ArrowUpFromLine, ArrowDownFromLine, ArrowLeftFromLine, ArrowRightFromLine,
  Trash, TableProperties, Pin, ArrowUpDown, Bell, BellRing, X, Upload, Paperclip,
  RotateCcw, Info, Settings,
  Home, Share2, Tag, AlertCircle, BookOpen, Copy,
  Maximize2, Minimize2, Eye, EyeOff, LayoutTemplate,
  RefreshCw, Download,
  Globe, Keyboard,
  Mic, PenLine, Camera
} from 'lucide-react'
import * as api from '../../services/notesService'

var lowlight = createLowlight(common)


/* ══════════════════════════════════════════════════════════
   Search Highlight — ProseMirror Decoration Plugin
   ══════════════════════════════════════════════════════════ */
var searchPluginKey = new PluginKey('nwSearch')

var SearchHighlightExtension = Extension.create({
  name: 'nwSearchHighlight',
  addProseMirrorPlugins: function () {
    return [new Plugin({
      key: searchPluginKey,
      state: {
        init: function () { return { term: '', decorations: DecorationSet.empty } },
        apply: function (tr, old) {
          var meta = tr.getMeta(searchPluginKey)
          var term = (meta !== undefined) ? meta : old.term
          if (term === old.term && !tr.docChanged) return old
          if (!term) return { term: '', decorations: DecorationSet.empty }
          var deco = []
          tr.doc.descendants(function (node, pos) {
            if (!node.isText || !node.text) return
            var lc = node.text.toLowerCase()
            var lt = term.toLowerCase()
            var i = 0
            while ((i = lc.indexOf(lt, i)) !== -1) {
              deco.push(Decoration.inline(pos + i, pos + i + lt.length, { class: 'nw-search-result' }))
              i += 1
            }
          })
          return { term: term, decorations: DecorationSet.create(tr.doc, deco) }
        },
      },
      props: {
        decorations: function (state) { return this.getState(state).decorations },
      },
    })]
  },
})

/* ══════════════════════════════════════════════════════════
   Helpers
   ══════════════════════════════════════════════════════════ */
function formatDate(d) {
  if (!d) return ''
  var months = ['Oca', 'Sub', 'Mar', 'Nis', 'May', 'Haz', 'Tem', 'Agu', 'Eyl', 'Eki', 'Kas', 'Ara']
  return d.getDate() + ' ' + months[d.getMonth()] + ' ' + d.getFullYear()
}

function formatTime(d) {
  if (!d) return ''
  return String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0')
}

function snippet(text, maxLen, isFullyEncrypted) {
  // Mod 2: Tum not sifreli ise icerigi gostermeden kilit ikonu doner
  if (isFullyEncrypted) return '🔒 Sifrelenmis not'
  if (!text) return ''
  // Mod 1: Sifreli bloklari mask ile degistir (icerik sizmasin)
  var masked = text.replace(/<span[^>]*class="[^"]*nw-encrypted[^"]*"[^>]*>[\s\S]*?<\/span>/gi, '🔒')
  var clean = masked.replace(/<table[\s\S]*?<\/table>/gi, '').replace(/<pre[\s\S]*?<\/pre>/gi, '')
  var plain = clean.replace(/<[^>]*>/g, '')
  var decoded = plain.replace(/&nbsp;/gi, ' ').replace(/&amp;/gi, '&').replace(/&lt;/gi, '<').replace(/&gt;/gi, '>').replace(/&quot;/gi, '"')
  decoded = decoded.replace(/\s+/g, ' ').trim()
  return decoded.length > maxLen ? decoded.slice(0, maxLen) + '...' : decoded
}

function getChildren(folders, parentId) {
  return folders.filter(function (f) { return f.parentId === parentId })
}

function countNotesInFolder(notes, folders, folderId) {
  var count = notes.filter(function (n) { return n.folderId === folderId }).length
  var children = getChildren(folders, folderId)
  children.forEach(function (c) { count += countNotesInFolder(notes, folders, c.id) })
  return count
}

function getDescendantIds(folders, parentId) {
  var ids = [parentId]
  var children = getChildren(folders, parentId)
  children.forEach(function (c) {
    ids = ids.concat(getDescendantIds(folders, c.id))
  })
  return ids
}

function sortNotes(list, order) {
  var sorted = list.slice()
  sorted.sort(function (a, b) {
    // pinned notes always first
    if (a.isPinned && !b.isPinned) return -1
    if (!a.isPinned && b.isPinned) return 1
    // then sort by selected order
    switch (order) {
      case 'updatedAsc': return a.updatedAt - b.updatedAt
      case 'titleAsc': return (a.title || '').localeCompare(b.title || '', 'tr')
      case 'titleDesc': return (b.title || '').localeCompare(a.title || '', 'tr')
      case 'createdDesc': return (b.createdAt || b.updatedAt) - (a.createdAt || a.updatedAt)
      default: return b.updatedAt - a.updatedAt // updatedDesc
    }
  })
  return sorted
}

var MAX_FOLDER_DEPTH = 5

function getFolderDepth(folders, folderId) {
  var depth = 0
  var current = folderId
  while (current) {
    var folder = folders.find(function (f) { return f.id === current })
    if (!folder || !folder.parentId) break
    current = folder.parentId
    depth++
  }
  return depth
}

/* ══════════════════════════════════════════════════════════
   FolderTree — recursive component
   ══════════════════════════════════════════════════════════ */
function FolderTree(props) {
  var folders = props.folders
  var notes = props.notes
  var parentId = props.parentId
  var depth = props.depth || 0
  var selectedFolderId = props.selectedFolderId
  var expandedSet = props.expandedSet
  var onSelect = props.onSelect
  var onToggle = props.onToggle
  var renamingId = props.renamingId
  var renamingValue = props.renamingValue
  var renamingRef = props.renamingRef
  var onRenameChange = props.onRenameChange
  var onRenameSubmit = props.onRenameSubmit
  var onRenameCancel = props.onRenameCancel
  var onFolderContextMenu = props.onFolderContextMenu

  var items = getChildren(folders, parentId)
  if (items.length === 0) return null

  return items.map(function (f) {
    var active = selectedFolderId === f.id
    var isRenaming = renamingId === f.id
    var children = getChildren(folders, f.id)
    var hasChildren = children.length > 0
    var expanded = expandedSet.has(f.id)
    var count = notes.filter(function (n) { return n.folderId === f.id }).length

    return (
      <div key={f.id}>
        <div
          className={'nw-folder-item' + (active ? ' nw-folder-item--active' : '')}
          style={{ paddingLeft: 10 + depth * 16 }}
          data-allow-context-menu="1"
          onClick={function () { if (!isRenaming) onSelect(f.id) }}
          onContextMenu={function (e) { onFolderContextMenu && onFolderContextMenu(e, f.id, f.name) }}
        >
          {hasChildren ? (
            <button
              className="nw-folder-chevron"
              onClick={function (e) { e.stopPropagation(); onToggle(f.id) }}
            >
              {expanded
                ? <ChevronDown size={13} />
                : <ChevronRight size={13} />}
            </button>
          ) : (
            <span style={{ width: 18, flexShrink: 0 }} />
          )}
          {expanded || !hasChildren
            ? <FolderOpen size={15} className="nw-folder-ico" />
            : <FolderClosed size={15} className="nw-folder-ico" />}
          {isRenaming ? (
            <input
              ref={renamingRef}
              className="nw-rename-input"
              value={renamingValue}
              onChange={onRenameChange}
              onKeyDown={function (e) {
                if (e.key === 'Enter') onRenameSubmit()
                if (e.key === 'Escape') onRenameCancel()
              }}
              onBlur={onRenameSubmit}
              onClick={function (e) { e.stopPropagation() }}
            />
          ) : (
            <>
              <span className="nw-folder-label">{f.name}</span>
              {count > 0 && <span className="nw-folder-count">{count}</span>}
            </>
          )}
        </div>
        {hasChildren && expanded && (
          <FolderTree
            folders={folders} notes={notes} parentId={f.id}
            depth={depth + 1} selectedFolderId={selectedFolderId}
            expandedSet={expandedSet} onSelect={onSelect} onToggle={onToggle}
            renamingId={renamingId} renamingValue={renamingValue}
            renamingRef={renamingRef} onRenameChange={onRenameChange}
            onRenameSubmit={onRenameSubmit} onRenameCancel={onRenameCancel}
            onFolderContextMenu={onFolderContextMenu}
          />
        )}
      </div>
    )
  })
}

/* ══════════════════════════════════════════════════════════
   InsertMenu — Evernote/Notion "Ekle" dropdown
   ══════════════════════════════════════════════════════════ */
var INSERT_ITEMS = [
  { section: 'Yeni Özellikler' },
  { id: 'callout', label: 'Vurgu',       icon: AlertCircle, action: 'callout',    badge: 'Yeni', desc: 'Stilize bir kutuda ipuçları, uyarılar veya önemli notlar' },
  { section: 'Temel Gereksinimler' },
  { id: 'check',   label: 'Yeni görev',  icon: CheckSquare, action: 'taskList',   shortcut: 'Alt+T' },
  { id: 'table',   label: 'Tablo',       icon: TableIcon,   action: 'table' },
  { id: 'hr',      label: 'Ayrıcı',      icon: Minus,       action: 'hr',         shortcut: '---' },
  { id: 'quote',   label: 'Alıntı',      icon: Quote,       action: 'blockquote', shortcut: '>' },
  { id: 'code',    label: 'Kod bloğu',   icon: Code,        action: 'codeBlock' },
  { id: 'link',    label: 'Bağlantı',    icon: LinkIcon,    action: 'link',       shortcut: 'Ctrl+K' },
  { section: 'Metin Stilleri' },
  { id: 'h1',      label: 'Büyük başlık',icon: Heading1,    action: 'heading1' },
  { id: 'h2',      label: 'Orta başlık', icon: Heading2,    action: 'heading2' },
  { id: 'h3',      label: 'Küçük başlık',icon: Heading3,    action: 'heading3' },
  { section: 'Medya' },
  { id: 'image',   label: 'Resim',       icon: ImagePlus,   action: 'image' },
  { id: 'youtube', label: 'YouTube',     icon: YoutubeIcon, action: 'youtube' },
  { id: 'audio',   label: 'Ses Kaydı',  icon: Mic,         action: 'audio' },
  { id: 'draw',    label: 'Çizim',      icon: PenLine,     action: 'draw' },
  { id: 'camera',  label: 'Kamera',     icon: Camera,      action: 'camera' },
]

function InsertMenu(props) {
  var open = props.open
  var onClose = props.onClose
  var onInsert = props.onInsert
  var btnRef = props.btnRef
  var [search, setSearch] = useState('')
  var searchRef = useRef(null)

  useEffect(function () {
    if (open && searchRef.current) {
      setSearch('')
      setTimeout(function () { searchRef.current && searchRef.current.focus() }, 50)
    }
  }, [open])

  useEffect(function () {
    if (!open) return
    function handleClick(e) {
      if (btnRef && btnRef.current && btnRef.current.contains(e.target)) return
      onClose()
    }
    document.addEventListener('mousedown', handleClick)
    return function () { document.removeEventListener('mousedown', handleClick) }
  }, [open, onClose, btnRef])

  if (!open) return null

  var q = search.toLowerCase()
  var filtered = INSERT_ITEMS.filter(function (item) {
    if (item.section) return true
    return !q || item.label.toLowerCase().indexOf(q) !== -1
  })

  var clean = []
  for (var i = 0; i < filtered.length; i++) {
    if (filtered[i].section) {
      var hasItem = false
      for (var j = i + 1; j < filtered.length; j++) {
        if (filtered[j].section) break
        hasItem = true; break
      }
      if (hasItem) clean.push(filtered[i])
    } else {
      clean.push(filtered[i])
    }
  }

  return (
    <div className="nw-insert-menu" onClick={function (e) { e.stopPropagation() }}>
      <div className="nw-insert-search">
        <Search size={13} className="nw-insert-search-ico" />
        <input
          ref={searchRef}
          className="nw-insert-search-input"
          placeholder="Ekleme seceneklerini ara..."
          value={search}
          onChange={function (e) { setSearch(e.target.value) }}
          onKeyDown={function (e) {
            if (e.key === 'Escape') onClose()
          }}
        />
      </div>
      <div className="nw-insert-list">
        {clean.length === 0 && (
          <div className="nw-insert-empty">Sonuc bulunamadi</div>
        )}
        {clean.map(function (item, idx) {
          if (item.section) {
            return <div key={'s-' + idx} className="nw-insert-section">{item.section}</div>
          }
          var Icon = item.icon
          return (
            <button
              key={item.id}
              className="nw-insert-item"
              onClick={function () { onInsert(item); onClose() }}
            >
              <span className="nw-insert-item-ico"><Icon size={16} /></span>
              <span className="nw-insert-item-body">
                <span className="nw-insert-item-top">
                  <span className="nw-insert-item-label">{item.label}</span>
                  {item.badge && <span className="nw-insert-item-badge">{item.badge}</span>}
                  {item.shortcut && <span className="nw-insert-item-shortcut">{item.shortcut}</span>}
                </span>
                {item.desc && <span className="nw-insert-item-desc">{item.desc}</span>}
              </span>
            </button>
          )
        })}
      </div>
    </div>
  )
}

/* ══════════════════════════════════════════════════════════
   ColorPicker — text/background color dropdown
   ══════════════════════════════════════════════════════════ */
var TEXT_COLORS = [
  { name: 'Varsayilan', value: null },
  { name: 'Siyah',      value: '#1e293b' },
  { name: 'Gri',        value: '#64748b' },
  { name: 'Kirmizi',    value: '#dc2626' },
  { name: 'Turuncu',    value: '#ea580c' },
  { name: 'Yesil',      value: '#16a34a' },
  { name: 'Mavi',       value: '#2563eb' },
  { name: 'Mor',        value: '#9333ea' },
]

var BG_COLORS = [
  { name: 'Yok',     value: null },
  { name: 'Sari',    value: '#fef08a' },
  { name: 'Yesil',   value: '#bbf7d0' },
  { name: 'Mavi',    value: '#bfdbfe' },
  { name: 'Pembe',   value: '#fbcfe8' },
  { name: 'Mor',     value: '#e9d5ff' },
  { name: 'Turuncu', value: '#fed7aa' },
  { name: 'Gri',     value: '#e2e8f0' },
  { name: 'Kirmizi', value: '#fecaca' },
]

function ColorPicker(props) {
  var open = props.open
  var onClose = props.onClose
  var editor = props.editor
  var btnRef = props.btnRef

  useEffect(function () {
    if (!open) return
    function handleClick(e) {
      if (btnRef && btnRef.current && btnRef.current.contains(e.target)) return
      onClose()
    }
    document.addEventListener('mousedown', handleClick)
    return function () { document.removeEventListener('mousedown', handleClick) }
  }, [open, onClose, btnRef])

  if (!open || !editor) return null

  return (
    <div className="nw-color-picker" onClick={function (e) { e.stopPropagation() }}>
      <div className="nw-color-section">
        <div className="nw-color-section-title"><Type size={12} /> Yazi Rengi</div>
        <div className="nw-color-grid">
          {TEXT_COLORS.map(function (c) {
            return (
              <button
                key={c.name}
                className="nw-color-swatch"
                title={c.name}
                style={{ background: c.value || '#1e293b' }}
                onClick={function () {
                  if (c.value) {
                    editor.chain().focus().setColor(c.value).run()
                  } else {
                    editor.chain().focus().unsetColor().run()
                  }
                  onClose()
                }}
              >
                {!c.value && <span className="nw-color-reset">A</span>}
              </button>
            )
          })}
        </div>
      </div>
      <div className="nw-color-sep" />
      <div className="nw-color-section">
        <div className="nw-color-section-title"><Highlighter size={12} /> Arka Plan</div>
        <div className="nw-color-grid">
          {BG_COLORS.map(function (c) {
            return (
              <button
                key={c.name}
                className="nw-color-swatch"
                title={c.name}
                style={{ background: c.value || 'transparent', border: !c.value ? '2px dashed #94a3b8' : undefined }}
                onClick={function () {
                  if (c.value) {
                    editor.chain().focus().setHighlight({ color: c.value }).run()
                  } else {
                    editor.chain().focus().unsetHighlight().run()
                  }
                  onClose()
                }}
              >
                {!c.value && <span className="nw-color-reset">×</span>}
              </button>
            )
          })}
        </div>
      </div>
    </div>
  )
}

/* ══════════════════════════════════════════════════════════
   ReminderPopover — Bell button dropdown (list + add)
   ══════════════════════════════════════════════════════════ */
var RECURRENCE_LABELS = [
  'Tek sefer',
  'Her saat',
  'Her gun',
  'Her hafta',
  'Hafta sonu',
  'Her ay',
  'Haftanin belirli gunleri',
  'Ayin belirli gunleri'
]
var RECURRENCE_NEEDS_DATA = { 6: 'days_of_week', 7: 'days_of_month' }

function pad2(n) { return String(n).padStart(2, '0') }

function defaultReminderAtLocal() {
  // Bir saat sonrasi, yyyy-MM-ddTHH:mm
  var d = new Date(Date.now() + 60 * 60 * 1000)
  return d.getFullYear() + '-' + pad2(d.getMonth() + 1) + '-' + pad2(d.getDate()) +
    'T' + pad2(d.getHours()) + ':' + pad2(d.getMinutes())
}

function formatReminderAt(iso) {
  if (!iso) return ''
  // iso = yyyy-MM-ddTHH:mm:ss (local wall-clock, timezone yok)
  var m = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})/.exec(iso)
  if (!m) return iso
  return m[3] + '.' + m[2] + '.' + m[1] + ' ' + m[4] + ':' + m[5]
}

function ReminderPopover(props) {
  var reminders = props.reminders || []
  var loading = !!props.loading
  var onClose = props.onClose
  var onAdd = props.onAdd
  var onDelete = props.onDelete
  var btnRef = props.btnRef

  var [remindAt, setRemindAt] = useState(defaultReminderAtLocal)
  var [recurrence, setRecurrence] = useState(0)
  var [recurrenceData, setRecurrenceData] = useState('')
  var [submitting, setSubmitting] = useState(false)
  var [deliveryChannel, setDeliveryChannel] = useState(0) // 0=InApp, 1=Email, 2=Both
  var [targetIds, setTargetIds] = useState([])            // bos ise = not sahibi
  var [userPickerOpen, setUserPickerOpen] = useState(false)
  var [userSearch, setUserSearch] = useState('')
  var [companyUsers, setCompanyUsers] = useState([])

  useEffect(function () {
    // Popover ilk acildiginda sirket kullanicilarini yukle
    api.getCompanyUsers().then(function (users) {
      setCompanyUsers(Array.isArray(users) ? users : [])
    }).catch(function () { /* sessizce */ })
  }, [])

  useEffect(function () {
    function handleClick(e) {
      if (btnRef && btnRef.current && btnRef.current.contains(e.target)) return
      onClose()
    }
    document.addEventListener('mousedown', handleClick)
    return function () { document.removeEventListener('mousedown', handleClick) }
  }, [onClose, btnRef])

  function submit() {
    if (!remindAt) return
    var needsData = RECURRENCE_NEEDS_DATA[recurrence]
    if (needsData && !recurrenceData.trim()) return
    setSubmitting(true)
    Promise.resolve(onAdd(
      remindAt + ':00',
      recurrence,
      recurrenceData.trim() || null,
      deliveryChannel,
      targetIds
    ))
      .finally(function () {
        setSubmitting(false)
        setRemindAt(defaultReminderAtLocal())
        setRecurrence(0)
        setRecurrenceData('')
        // Delivery + target reset etmiyoruz — ard arda ayni ayarla ekleme pratik olur
      })
  }

  function addTarget(id) {
    setTargetIds(function (prev) { return prev.indexOf(id) === -1 ? prev.concat([id]) : prev })
    setUserSearch('')
    setUserPickerOpen(false)
  }
  function removeTarget(id) {
    setTargetIds(function (prev) { return prev.filter(function (x) { return x !== id }) })
  }

  var DELIVERY_LABELS = { 0: 'Bildirim', 1: 'E-posta', 2: 'Bildirim + E-posta' }
  var selectedTargetUsers = targetIds
    .map(function (id) { return companyUsers.find(function (u) { return u.id === id }) })
    .filter(Boolean)
  var availableUsers = companyUsers
    .filter(function (u) { return !u.isSelf && targetIds.indexOf(u.id) === -1 })
    .filter(function (u) {
      if (!userSearch) return true
      return (u.fullName || '').toLowerCase().indexOf(userSearch.toLowerCase()) !== -1
    })

  return (
    <div className="nw-reminder-pop" onClick={function (e) { e.stopPropagation() }}>
      <div className="nw-reminder-pop-head">
        <Bell size={14} />
        <span>Hatirlaticilar</span>
      </div>

      <div className="nw-reminder-pop-list">
        {loading && <div className="nw-reminder-empty">Yukleniyor...</div>}
        {!loading && reminders.length === 0 && (
          <div className="nw-reminder-empty">Henuz hatirlatici yok.</div>
        )}
        {!loading && reminders.map(function (r) {
          var isSent = r.isSent
          var dChan = r.deliveryChannel || 0
          var targets = Array.isArray(r.targets) ? r.targets : []
          var meta = DELIVERY_LABELS[dChan] || 'Bildirim'
          if (targets.length > 0) {
            meta += ' → ' + targets.map(function (t) { return t.fullName }).join(', ')
          }
          return (
            <div key={r.id} className={'nw-reminder-row' + (isSent ? ' nw-reminder-row--sent' : '')}>
              <div className="nw-reminder-row-info">
                <div className="nw-reminder-row-when">
                  {isSent ? <BellRing size={12} /> : <Bell size={12} />}
                  <span>{formatReminderAt(r.remindAt)}</span>
                  {isSent && <span className="nw-reminder-sent-tag">gonderildi</span>}
                </div>
                <div className="nw-reminder-row-rec">
                  {meta}
                  {r.recurrenceType > 0 ? ' · ' + (RECURRENCE_LABELS[r.recurrenceType] || '') : ''}
                  {r.recurrenceData ? ' — ' + r.recurrenceData : ''}
                </div>
              </div>
              <button
                className="nw-reminder-del"
                onClick={function () { onDelete(r.id) }}
                title="Sil"
              >
                <X size={12} />
              </button>
            </div>
          )
        })}
      </div>

      <div className="nw-reminder-pop-add">
        <div className="nw-reminder-add-row">
          <label>Zaman</label>
          <input
            type="datetime-local"
            className="nw-reminder-input"
            value={remindAt}
            onChange={function (e) { setRemindAt(e.target.value) }}
          />
        </div>
        <div className="nw-reminder-add-row">
          <label>Tekrar</label>
          <select
            className="nw-reminder-input"
            value={recurrence}
            onChange={function (e) { setRecurrence(parseInt(e.target.value, 10)) }}
          >
            {RECURRENCE_LABELS.map(function (label, idx) {
              return <option key={idx} value={idx}>{label}</option>
            })}
          </select>
        </div>
        {RECURRENCE_NEEDS_DATA[recurrence] && (
          <div className="nw-reminder-add-row">
            <label>{recurrence === 6 ? 'Gunler (0=Paz..6=Cts)' : 'Ayin gunleri (1..31)'}</label>
            <input
              type="text"
              className="nw-reminder-input"
              placeholder={recurrence === 6 ? 'orn: 1,3,5' : 'orn: 1,15'}
              value={recurrenceData}
              onChange={function (e) { setRecurrenceData(e.target.value) }}
            />
          </div>
        )}
        <div className="nw-reminder-add-row">
          <label>Kanal</label>
          <select
            className="nw-reminder-input"
            value={deliveryChannel}
            onChange={function (e) { setDeliveryChannel(parseInt(e.target.value, 10)) }}
          >
            <option value={0}>Bildirim</option>
            <option value={1}>E-posta</option>
            <option value={2}>Bildirim + E-posta</option>
          </select>
        </div>
        <div className="nw-reminder-add-row nw-reminder-add-row--chips">
          <label>Kime</label>
          <div className="nw-reminder-chips-wrap">
            <div className="nw-reminder-chips">
              {selectedTargetUsers.length === 0 && (
                <span className="nw-reminder-chip-placeholder">Kendim</span>
              )}
              {selectedTargetUsers.map(function (u) {
                return (
                  <span key={u.id} className="nw-reminder-chip">
                    {u.fullName}
                    <button
                      type="button"
                      className="nw-reminder-chip-x"
                      onClick={function () { removeTarget(u.id) }}
                      aria-label="Kaldir"
                    >
                      <X size={10} />
                    </button>
                  </span>
                )
              })}
              <button
                type="button"
                className="nw-reminder-chip-add"
                onClick={function () { setUserPickerOpen(function (p) { return !p }) }}
                title="Kisi ekle"
              >
                + Kisi
              </button>
            </div>
            {userPickerOpen && (
              <div className="nw-reminder-picker">
                <input
                  type="text"
                  className="nw-reminder-input nw-reminder-picker-search"
                  placeholder="Ara..."
                  value={userSearch}
                  onChange={function (e) { setUserSearch(e.target.value) }}
                  autoFocus
                />
                <div className="nw-reminder-picker-list">
                  {availableUsers.length === 0 && (
                    <div className="nw-reminder-picker-empty">Eklenebilecek kullanici yok.</div>
                  )}
                  {availableUsers.map(function (u) {
                    return (
                      <button
                        key={u.id}
                        type="button"
                        className="nw-reminder-picker-item"
                        onClick={function () { addTarget(u.id) }}
                      >
                        {u.fullName}
                      </button>
                    )
                  })}
                </div>
              </div>
            )}
          </div>
        </div>
        <button
          className="nw-reminder-add-btn"
          onClick={submit}
          disabled={submitting || !remindAt}
        >
          {submitting ? 'Ekleniyor...' : 'Hatirlatici Ekle'}
        </button>
      </div>
    </div>
  )
}

/* ══════════════════════════════════════════════════════════
   NoteInfoModal — Not meta bilgileri
   ══════════════════════════════════════════════════════════ */
function formatDateLong(d) {
  if (!d) return '—'
  var days = ['Pazar', 'Pazartesi', 'Salı', 'Çarşamba', 'Perşembe', 'Cuma', 'Cumartesi']
  var months = ['Ocak', 'Şubat', 'Mart', 'Nisan', 'Mayıs', 'Haziran', 'Temmuz', 'Ağustos', 'Eylül', 'Ekim', 'Kasım', 'Aralık']
  return d.getDate() + ' ' + months[d.getMonth()] + ' ' + d.getFullYear() + ' ' +
    days[d.getDay()] + ' ' + String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0')
}

function NoteInfoModal({ note, folderName, currentUserName, onClose }) {
  var plain = note.content
    ? note.content.replace(/<[^>]*>/g, ' ').replace(/\s+/g, ' ').trim()
    : ''
  var charCount = plain.length
  var wordCount = plain ? plain.split(/\s+/).filter(Boolean).length : 0
  var sizeKb = Math.max(1, Math.ceil(new Blob([note.content || '']).size / 1024))

  useEffect(function () {
    function onKey(e) { if (e.key === 'Escape') onClose() }
    document.addEventListener('keydown', onKey)
    return function () { document.removeEventListener('keydown', onKey) }
  }, [onClose])

  return (
    <div className="nw-info-backdrop" onClick={onClose}>
      <div className="nw-info-modal" onClick={function (e) { e.stopPropagation() }}>
        <div className="nw-info-header">
          <span>Not Bilgisi</span>
          <button className="nw-info-close" onClick={onClose}><X size={16} /></button>
        </div>
        <div className="nw-info-body">
          {[
            ['Başlık',      note.title || 'Başlıksız Not'],
            ['Konum',       folderName || 'Klasörsüz'],
            ['Oluşturuldu', formatDateLong(note.createdAt)],
            ['Güncellendi', formatDateLong(note.updatedAt)],
            ['Yazar',       currentUserName || '—'],
            ['Boyut',       sizeKb + ' KB · ' + wordCount + ' kelime · ' + charCount + ' karakter'],
          ].map(function (row) {
            return (
              <div key={row[0]} className="nw-info-row">
                <span className="nw-info-label">{row[0]}</span>
                <span className="nw-info-value">{row[1]}</span>
              </div>
            )
          })}
        </div>
      </div>
    </div>
  )
}

/* ════ ShortcutsModal — Klavye kısayolları ════ */
var SHORTCUTS = [
  { group: 'Gezinme',   items: [
    { keys: ['Ctrl', 'P'],   desc: 'Hızlı not arama / geç' },
    { keys: ['Ctrl', 'F'],   desc: 'Bul & Değiştir' },
  ]},
  { group: 'Editör',    items: [
    { keys: ['Ctrl', 'B'],   desc: 'Kalın (Bold)' },
    { keys: ['Ctrl', 'I'],   desc: 'İtalik' },
    { keys: ['Ctrl', 'U'],   desc: 'Altı çizili' },
    { keys: ['Ctrl', 'Z'],   desc: 'Geri al' },
    { keys: ['Ctrl', 'Y'],   desc: 'Yeniden yap' },
    { keys: ['/'],           desc: 'Komut paleti (satır başında)' },
  ]},
  { group: 'Bul & Değiştir', items: [
    { keys: ['Enter'],       desc: 'Sonraki eşleşme' },
    { keys: ['Shift', 'Enter'], desc: 'Önceki eşleşme' },
    { keys: ['Esc'],         desc: 'Kapat' },
  ]},
  { group: 'Görünüm',   items: [
    { keys: ['F11'],         desc: 'Odak modu aç / kapat' },
    { keys: ['Esc'],         desc: 'Odak modundan çık' },
  ]},
]
function ShortcutsModal(props) {
  var onClose = props.onClose
  useEffect(function () {
    function onKey(e) { if (e.key === 'Escape') onClose() }
    document.addEventListener('keydown', onKey)
    return function () { document.removeEventListener('keydown', onKey) }
  }, [onClose])
  return (
    <div className="nw-modal-backdrop" onClick={onClose}>
      <div className="nw-shortcuts-modal" onClick={function (e) { e.stopPropagation() }}>
        <div className="nw-shortcuts-head">
          <Keyboard size={15} />
          <span>Klavye Kısayolları</span>
          <button className="nw-toc-close" onClick={onClose}><X size={14} /></button>
        </div>
        <div className="nw-shortcuts-body">
          {SHORTCUTS.map(function (group) {
            return (
              <div key={group.group} className="nw-shortcuts-group">
                <div className="nw-shortcuts-group-title">{group.group}</div>
                {group.items.map(function (item) {
                  return (
                    <div key={item.desc} className="nw-shortcuts-row">
                      <span className="nw-shortcuts-desc">{item.desc}</span>
                      <span className="nw-shortcuts-keys">
                        {item.keys.map(function (k, i) {
                          return (
                            <span key={k}>
                              <kbd className="nw-kbd">{k}</kbd>
                              {i < item.keys.length - 1 && <span className="nw-kbd-plus">+</span>}
                            </span>
                          )
                        })}
                      </span>
                    </div>
                  )
                })}
              </div>
            )
          })}
        </div>
      </div>
    </div>
  )
}


/* ══════════════════════════════════════════════════════════
   QuickSwitcherModal — Ctrl+P hızlı not arama/geçiş
   ══════════════════════════════════════════════════════════ */
function QuickSwitcherModal(props) {
  var open = props.open
  var notes = props.notes
  var folders = props.folders
  var onSelect = props.onSelect
  var onClose = props.onClose

  var [query, setQuery] = useState('')
  var [activeIdx, setActiveIdx] = useState(0)
  var inputRef = useRef(null)

  useEffect(function () {
    if (open) {
      setQuery('')
      setActiveIdx(0)
      setTimeout(function () { inputRef.current && inputRef.current.focus() }, 40)
    }
  }, [open])

  var filtered = (function () {
    var q = query.trim().toLowerCase()
    if (!q) return notes.slice().sort(function (a, b) { return b.updatedAt - a.updatedAt }).slice(0, 12)
    return notes.filter(function (n) {
      return (n.title || '').toLowerCase().indexOf(q) !== -1
    }).slice(0, 12)
  })()

  useEffect(function () { setActiveIdx(0) }, [query])

  useEffect(function () {
    if (!open) return
    function handleKey(e) {
      if (e.key === 'Escape') { onClose(); return }
      if (e.key === 'ArrowDown') { e.preventDefault(); setActiveIdx(function (i) { return Math.min(i + 1, filtered.length - 1) }) }
      if (e.key === 'ArrowUp') { e.preventDefault(); setActiveIdx(function (i) { return Math.max(i - 1, 0) }) }
      if (e.key === 'Enter') { if (filtered[activeIdx]) onSelect(filtered[activeIdx]) }
    }
    document.addEventListener('keydown', handleKey)
    return function () { document.removeEventListener('keydown', handleKey) }
  }, [open, filtered, activeIdx, onSelect, onClose])

  if (!open) return null

  function getFolderLabel(folderId) {
    if (!folderId) return 'Tüm Notlar'
    var f = folders.find(function (x) { return x.id === folderId })
    return f ? f.name : '—'
  }

  return (
    <div className="nw-qs-backdrop" onClick={onClose}>
      <div className="nw-qs-modal" onClick={function (e) { e.stopPropagation() }}>
        <div className="nw-qs-input-wrap">
          <Search size={14} className="nw-qs-ico" />
          <input
            ref={inputRef}
            className="nw-qs-input"
            placeholder="Not adı ara... (Ctrl+P)"
            value={query}
            onChange={function (e) { setQuery(e.target.value) }}
          />
          {query && (
            <button className="nw-qs-clear" onClick={function () { setQuery(''); inputRef.current && inputRef.current.focus() }}>
              <X size={13} />
            </button>
          )}
        </div>
        <div className="nw-qs-list">
          {filtered.length === 0 && <div className="nw-qs-empty">Not bulunamadı</div>}
          {filtered.map(function (note, idx) {
            return (
              <button
                key={note.id}
                className={'nw-qs-item' + (idx === activeIdx ? ' nw-qs-item--active' : '')}
                onClick={function () { onSelect(note) }}
                onMouseEnter={function () { setActiveIdx(idx) }}
              >
                <FileText size={13} className="nw-qs-item-ico" />
                <span className="nw-qs-item-title">{note.title || 'Başlıksız Not'}</span>
                <span className="nw-qs-item-folder">{getFolderLabel(note.folderId)}</span>
              </button>
            )
          })}
        </div>
        <div className="nw-qs-footer">
          <span>↑↓ seç</span><span>↵ aç</span><span>Esc kapat</span>
        </div>
      </div>
    </div>
  )
}

/* ══════════════════════════════════════════════════════════
   NoteActionsMenu — "..." dropdown (PDF export, delete etc.)
   ══════════════════════════════════════════════════════════ */
function NoteActionsMenu(props) {
  var onClose = props.onClose
  var onExportPdf = props.onExportPdf
  var onDelete = props.onDelete
  var onReminders = props.onReminders
  var reminderCount = props.reminderCount || 0
  var onEncryptWhole = props.onEncryptWhole
  var onLockWhole = props.onLockWhole
  var onRemoveEncryption = props.onRemoveEncryption
  var isEncrypted = !!props.isEncrypted
  var isUnlocked = !!props.isUnlocked
  var onNoteInfo = props.onNoteInfo
  var onTogglePin = props.onTogglePin
  var isPinned = !!props.isPinned
  var btnRef = props.btnRef
  var onClone = props.onClone

  useEffect(function () {
    function handleClick(e) {
      if (btnRef && btnRef.current && btnRef.current.contains(e.target)) return
      onClose()
    }
    document.addEventListener('mousedown', handleClick)
    return function () { document.removeEventListener('mousedown', handleClick) }
  }, [onClose, btnRef])

  return (
    <div className="nw-actions-menu" onClick={function (e) { e.stopPropagation() }}>
      <button className="nw-actions-item" onClick={onNoteInfo}>
        <Info size={15} />
        <span>Not Bilgisi</span>
      </button>
      <button className="nw-actions-item" onClick={onReminders}>
        {reminderCount > 0 ? <BellRing size={15} /> : <Bell size={15} />}
        <span>Hatırlatıcı{reminderCount > 0 ? ' (' + reminderCount + ')' : ''}</span>
      </button>
      <div className="nw-actions-sep" />
      <button className="nw-actions-item" onClick={onTogglePin}>
        <Pin size={15} />
        <span>{isPinned ? 'Sabitlemeyi Kaldır' : 'Deftere Sabitle'}</span>
      </button>
      <div className="nw-actions-sep" />
      <button className="nw-actions-item" onClick={onClone}>
        <Copy size={15} />
        <span>Notu Kopyala</span>
      </button>
      <div className="nw-actions-sep" />
      <button className="nw-actions-item" onClick={onExportPdf}>
        <FileDown size={15} />
        <span>PDF Olarak Dışa Aktar</span>
      </button>
      <div className="nw-actions-sep" />
      {!isEncrypted && (
        <button className="nw-actions-item" onClick={onEncryptWhole}>
          <Lock size={15} />
          <span>Notu Şifrele</span>
        </button>
      )}
      {isEncrypted && isUnlocked && (
        <>
          <button className="nw-actions-item" onClick={onLockWhole}>
            <Lock size={15} />
            <span>Notu Kilitle</span>
          </button>
          <button className="nw-actions-item" onClick={onRemoveEncryption}>
            <Unlock size={15} />
            <span>Şifrelemeyi Kaldır</span>
          </button>
        </>
      )}
      {isEncrypted && !isUnlocked && (
        <button className="nw-actions-item" disabled style={{ opacity: 0.55, cursor: 'not-allowed' }}>
          <Lock size={15} />
          <span>Not şifreli — önce açın</span>
        </button>
      )}
      <div className="nw-actions-sep" />
      <button className="nw-actions-item nw-actions-item--danger" onClick={onDelete}>
        <Trash2 size={15} />
        <span>Çöp Kutusuna Taşı</span>
      </button>
    </div>
  )
}

/* Paste/drop yardımcıları — base64 fallback (not henüz kaydedilmemişse) */
function insertImageNode(view, src, alt, pos) {
  var node = view.state.schema.nodes.image.create({ src: src, alt: alt || '' })
  var tr = pos != null
    ? view.state.tr.insert(pos, node)
    : view.state.tr.replaceSelectionWith(node)
  view.dispatch(tr)
}
function insertImageBase64(view, file, pos) {
  var reader = new FileReader()
  reader.onload = function (ev) { insertImageNode(view, ev.target.result, file.name, pos) }
  reader.readAsDataURL(file)
}

function formatFileSize(bytes) {
  if (!bytes) return '0 B'
  if (bytes < 1024) return bytes + ' B'
  if (bytes < 1048576) return (bytes / 1024).toFixed(1) + ' KB'
  return (bytes / 1048576).toFixed(1) + ' MB'
}

/* ══════════════════════════════════════════════════════════
   AttachmentsPanel
   ══════════════════════════════════════════════════════════ */
function AttachmentsPanel({ open, onToggle, attachments, loading, uploadRef, onUpload, onDelete }) {
  return (
    <div className="nw-attach-panel">
      <div className="nw-attach-header" onClick={onToggle}>
        <Paperclip size={13} className="nw-attach-panel-ico" />
        <span>Ekler</span>
        {attachments.length > 0 && <span className="nw-attach-count">{attachments.length}</span>}
        <input type="file" ref={uploadRef} onChange={onUpload} style={{ display: 'none' }} multiple />
        <button
          className="nw-attach-upload-btn"
          title="Dosya yükle"
          onClick={function (e) { e.stopPropagation(); uploadRef.current && uploadRef.current.click() }}
        >
          <Upload size={12} /> Yükle
        </button>
        <ChevronDown size={13} className={'nw-attach-chevron' + (open ? ' nw-attach-chevron--open' : '')} />
      </div>
      {open && (
        <div className="nw-attach-list">
          {loading && (
            <div className="nw-attach-empty">
              <Loader2 size={13} className="nw-spin" style={{ marginRight: 6 }} /> Yükleniyor…
            </div>
          )}
          {!loading && attachments.length === 0 && (
            <div className="nw-attach-empty">Henüz ek yok. Yükle butonuyla dosya ekleyebilirsiniz.</div>
          )}
          {!loading && attachments.map(function (a) {
            return (
              <div key={a.id} className="nw-attach-item">
                <FileText size={14} className="nw-attach-file-ico" />
                <div className="nw-attach-info">
                  <span className="nw-attach-name" title={a.fileName}>{a.fileName}</span>
                  <span className="nw-attach-meta">{formatFileSize(a.fileSize)} · {a.uploadedAt}</span>
                </div>
                <div className="nw-attach-actions">
                  <a
                    className="nw-attach-act"
                    href={'/Notes/DownloadAttachment?id=' + a.id + '&inline=true'}
                    target="_blank"
                    rel="noreferrer"
                    title="Görüntüle"
                    onClick={function (e) { e.stopPropagation() }}
                  >
                    <Eye size={13} />
                  </a>
                  <a
                    className="nw-attach-act"
                    href={'/Notes/DownloadAttachment?id=' + a.id}
                    download={a.fileName}
                    title="İndir"
                    onClick={function (e) { e.stopPropagation() }}
                  >
                    <Download size={13} />
                  </a>
                  <button
                    className="nw-attach-act nw-attach-act--del"
                    title="Sil"
                    onClick={function (e) { e.stopPropagation(); onDelete(a.id, a.fileName) }}
                  >
                    <Trash2 size={13} />
                  </button>
                </div>
              </div>
            )
          })}
        </div>
      )}
    </div>
  )
}

/* ══════════════════════════════════════════════════════════
   SlashCommandMenu — "/" ile tetiklenen blok-tipi menü
   ══════════════════════════════════════════════════════════ */
function SlashCommandMenu(props) {
  var open = props.open
  var query = props.query
  var pos = props.pos       // { x, y } — viewport koordinatları
  var onSelect = props.onSelect
  var onClose = props.onClose

  var [activeIdx, setActiveIdx] = useState(0)

  var items = INSERT_ITEMS.filter(function (item) {
    if (item.section) return false
    if (!query) return true
    return item.label.toLowerCase().indexOf(query.toLowerCase()) !== -1
  })

  useEffect(function () { setActiveIdx(0) }, [query])

  useEffect(function () {
    if (!open || items.length === 0) return
    function handleKey(e) {
      if (e.key === 'ArrowDown') {
        e.preventDefault(); e.stopPropagation()
        setActiveIdx(function (i) { return (i + 1) % items.length })
      } else if (e.key === 'ArrowUp') {
        e.preventDefault(); e.stopPropagation()
        setActiveIdx(function (i) { return (i - 1 + items.length) % items.length })
      } else if (e.key === 'Enter') {
        e.preventDefault(); e.stopPropagation()
        if (items[activeIdx]) onSelect(items[activeIdx])
      } else if (e.key === 'Escape') {
        e.stopPropagation(); onClose()
      }
    }
    document.addEventListener('keydown', handleKey, true)
    return function () { document.removeEventListener('keydown', handleKey, true) }
  }, [open, items, activeIdx, onSelect, onClose])

  if (!open || items.length === 0) return null

  return (
    <div
      className="nw-slash-menu"
      style={{ left: pos.x, top: pos.y }}
      onMouseDown={function (e) { e.preventDefault() }}
    >
      {items.map(function (item, idx) {
        var Icon = item.icon
        return (
          <button
            key={item.id}
            className={'nw-slash-item' + (idx === activeIdx ? ' nw-slash-item--active' : '')}
            onMouseDown={function (e) { e.preventDefault(); onSelect(item) }}
            onMouseEnter={function () { setActiveIdx(idx) }}
          >
            <span className="nw-slash-item-ico"><Icon size={14} /></span>
            <span className="nw-slash-item-label">{item.label}</span>
            {item.shortcut && <span className="nw-slash-item-shortcut">{item.shortcut}</span>}
          </button>
        )
      })}
    </div>
  )
}

/* ══════════════════════════════════════════════════════════
   NotesWorkspace — Main 3-Pane Component
   ══════════════════════════════════════════════════════════ */
export default function NotesWorkspace() {
  var [folders, setFolders] = useState([])
  var [notes, setNotes] = useState([])
  var [loading, setLoading] = useState(true)
  var [renamingFolderId, setRenamingFolderId] = useState(null)
  var [renamingValue, setRenamingValue] = useState('')
  var renamingRef = useRef(null)
  var [selectedFolderId, setSelectedFolderId] = useState(null)
  var [selectedNoteId, setSelectedNoteId] = useState(null)
  var [expandedFolders, setExpandedFolders] = useState(function () {
    return new Set()
  })
  var insertBtnRef = useRef(null)
  var [insertMenuOpen, setInsertMenuOpen] = useState(false)
  var colorBtnRef = useRef(null)
  var [colorPickerOpen, setColorPickerOpen] = useState(false)
  var actionsBtnRef = useRef(null)
  var [actionsMenuOpen, setActionsMenuOpen] = useState(false)
  var [confirmModal, setConfirmModal] = useState(null)
  var [remindersOpen, setRemindersOpen] = useState(false)
  var [reminders, setReminders] = useState([])
  var [remindersLoading, setRemindersLoading] = useState(false)
  // ── Sifreleme state'leri (Mod 1 + Mod 2) ──────────────────────────────
  // Mod 1: Secili bolumu sifrele modalı
  var [encryptSelectionOpen, setEncryptSelectionOpen] = useState(false)
  // Mod 1: Sifreli bloga tiklayinca acilan decrypt modali { ct, hint, range }
  var [decryptBlockTarget, setDecryptBlockTarget] = useState(null)
  // Mod 2: Tum notu sifrele modali (not icin)
  var [encryptWholeNoteOpen, setEncryptWholeNoteOpen] = useState(false)
  // Mod 2: Kilitli not acilinca cachelenen parola (cache ile beraber calisir)
  var [unlockedNoteIds, setUnlockedNoteIds] = useState(new Set())
  var [folderCtxMenu, setFolderCtxMenu] = useState(null)
  var [tableCtxMenu, setTableCtxMenu] = useState(null) // { x, y }
  var [sortOrder, setSortOrder] = useState('updatedDesc') // updatedDesc | updatedAsc | titleAsc | titleDesc | createdDesc
  var [contentLoadedTick, setContentLoadedTick] = useState(0) // not seçilince lazy-load tamamlandığında artar
  var [contentLoading, setContentLoading] = useState(false)   // içerik yüklenirken editor read-only
  var notesRef = useRef([])                                    // notes'un güncel değeri (stale closure önlemi)
  var contentTimerRef = useRef(null)
  var isSwitchingNoteRef = useRef(false)
  var isSwitchingResetRef = useRef(null)  // isSwitchingNoteRef'i async sıfırlamak için
  var selectedNoteIdRef = useRef(null)
  selectedNoteIdRef.current = selectedNoteId
  notesRef.current = notes
  var importInputRef = useRef(null)
  var [isImporting, setIsImporting] = useState(false)
  var [importStatus, setImportStatus] = useState(null)   // { ok, msg }
  var gearBtnRef = useRef(null)
  var [gearMenuOpen, setGearMenuOpen] = useState(false)
  // importSubOpen kaldırıldı — direkt buton kullanılıyor
  var [searchQuery, setSearchQuery] = useState('')
  var [audioRecordOpen, setAudioRecordOpen] = useState(false)
  var [cameraOpen,      setCameraOpen]      = useState(false)
  var attachUploadRef = useRef(null)
  var imageInputRef   = useRef(null)
  var [attachmentsOpen, setAttachmentsOpen] = useState(false)
  var [attachments, setAttachments] = useState([])
  var [attachmentsLoading, setAttachmentsLoading] = useState(false)
  var [trashedNotes, setTrashedNotes] = useState([])
  var [trashLoaded, setTrashLoaded] = useState(false)
  var [currentUserName, setCurrentUserName] = useState('')
  var [noteInfoOpen, setNoteInfoOpen] = useState(false)
  var [currentCompanyId, setCurrentCompanyId] = useState(0)

  // ── Quick Switcher / Reading Mode / Tags ──────────────────────────────
  var [quickSwitcherOpen, setQuickSwitcherOpen] = useState(false)
  var [readingMode, setReadingMode] = useState(false)
  var [tagInputOpen, setTagInputOpen] = useState(false)
  var [tagInput, setTagInput] = useState('')
  var tagInputRef = useRef(null)
  var [shortcutsOpen, setShortcutsOpen] = useState(false)
  // ── Share modal ───────────────────────────────────────────────────────
  var [shareIsPublic, setShareIsPublic] = useState(false)
  var [shareToken, setShareToken] = useState(null)
  var [sharePanelOpen, setSharePanelOpen] = useState(false)
  var [shareCopied, setShareCopied] = useState(false)
  var [shareIncludeAttachments, setShareIncludeAttachments] = useState(false)
  // ── User share (kullanıcı bazlı paylaşım) ────────────────────────────
  var [userShares, setUserShares] = useState([])          // [{ shareId, userId, fullName, email, canEdit }]
  var [companyUsers, setCompanyUsers] = useState([])       // tüm şirket kullanıcıları
  var [shareUserSearch, setShareUserSearch] = useState('') // arama filtresi
  var [shareUserDropOpen, setShareUserDropOpen] = useState(false)
  // ── Focus Mode / TOC / Find & Replace / Slash Command ─────────────────
  var [focusMode, setFocusMode] = useState(false)
  var [tocOpen, setTocOpen] = useState(false)
  var [tocItems, setTocItems] = useState([])
  var tocTimerRef = useRef(null)
  var [findOpen, setFindOpen] = useState(false)
  var [findTerm, setFindTerm] = useState('')
  var [replaceTerm, setReplaceTerm] = useState('')
  var [findMatches, setFindMatches] = useState([])
  var [findMatchIdx, setFindMatchIdx] = useState(0)
  var findInputRef = useRef(null)
  var [slashMenuOpen, setSlashMenuOpen] = useState(false)
  var [slashQuery, setSlashQuery] = useState('')
  var [slashPos, setSlashPos] = useState({ x: 0, y: 0 })
  var slashMenuOpenRef = useRef(false)
  slashMenuOpenRef.current = slashMenuOpen

  var filteredNotes = (function () {
    if (selectedFolderId === '__trash') return trashedNotes
    var q = searchQuery.trim().toLowerCase()
    if (q) {
      return notes.filter(function (n) {
        if ((n.title || '').toLowerCase().indexOf(q) !== -1) return true
        var plainContent = (n.content || '').replace(/<[^>]*>/g, ' ').replace(/\s+/g, ' ')
        if (plainContent.toLowerCase().indexOf(q) !== -1) return true
        return (n.ocrText || '').toLowerCase().indexOf(q) !== -1
      })
    }
    return selectedFolderId
      ? notes.filter(function (n) { return n.folderId === selectedFolderId })
      : notes
  })()

  var selectedNote = notes.find(function (n) { return n.id === selectedNoteId }) || trashedNotes.find(function (n) { return n.id === selectedNoteId }) || null

  /* ── Tiptap Editor ─────────────────────────────────── */
  var editor = useEditor({
    extensions: [
      StarterKit.configure({
        codeBlock: false, // CodeBlockLowlight kullanacağız
      }),
      Underline,
      Link.configure({ openOnClick: false }),
      TaskList,
      TaskItem.configure({ nested: true }),
      Table.configure({ resizable: true }),
      TableRow,
      TableHeader,
      TableCell,
      TextStyle,
      Color,
      Highlight.configure({ multicolor: true }),
      CodeBlockLowlight.configure({ lowlight: lowlight }),
      Placeholder.configure({ placeholder: 'Yazmaya baslayin...' }),
      Subscript,
      Superscript,
      TextAlign.configure({ types: ['heading', 'paragraph'] }),
      Image.configure({ inline: false, allowBase64: true }),
      Youtube.configure({ width: 640, height: 360 }),
      CharacterCount,
      Typography,
      Focus.configure({ className: 'has-focus', mode: 'deepest' }),
      EncryptedMark,
      SearchHighlightExtension,
      DrawingNodeExtension,
    ],
    editorProps: {
      attributes: {
        class: 'nw-editor-content',
      },
      /* Pano'dan resim yapıştırma (Ctrl+V / ekran görüntüsü) — base64 inline */
      handlePaste: function (view, event) {
        var items = event.clipboardData && event.clipboardData.items
        if (!items) return false
        var imgItem = null
        for (var i = 0; i < items.length; i++) {
          if (items[i].type.indexOf('image/') === 0) { imgItem = items[i]; break }
        }
        if (!imgItem) return false
        event.preventDefault()
        var file = imgItem.getAsFile()
        if (!file) return false
        insertImageBase64(view, file, null)
        return true
      },
      /* Sürükle-bırak resim desteği — base64 inline */
      handleDrop: function (view, event) {
        var files = event.dataTransfer && event.dataTransfer.files
        if (!files || !files.length) return false
        var imgFile = null
        for (var i = 0; i < files.length; i++) {
          if (files[i].type.indexOf('image/') === 0) { imgFile = files[i]; break }
        }
        if (!imgFile) return false
        event.preventDefault()
        var coords = view.posAtCoords({ left: event.clientX, top: event.clientY })
        var pos = coords ? coords.pos : null
        insertImageBase64(view, imgFile, pos)
        return true
      },
      handleDOMEvents: {
        contextmenu: function (view, event) {
          var target = event.target
          if (target && (target.closest('td') || target.closest('th'))) {
            event.preventDefault()
            setTableCtxMenu({ x: event.clientX, y: event.clientY })
            return true
          }
          return false
        },
        click: function (view, event) {
          // Sifreli span'e tiklandiysa decrypt modalini ac
          var target = event.target
          var encSpan = target && target.closest ? target.closest('.nw-encrypted') : null
          if (encSpan && encSpan.getAttribute('data-open') !== '1') {
            event.preventDefault()
            var ct = encSpan.getAttribute('data-ct') || ''
            var hint = encSpan.getAttribute('data-hint') || ''
            setDecryptBlockTarget({ element: encSpan, ct: ct, hint: hint })
            return true
          }
          return false
        },
      },
    },
    onUpdate: function (ctx) {
      if (isSwitchingNoteRef.current) return

      // noteId'yi ŞIMDI yakala (timer ateşlendiğinde selectedNoteIdRef değişmiş olabilir)
      var capturedNoteId = selectedNoteIdRef.current
      if (!capturedNoteId) return

      // ── Slash command detection ─────────────────────────────────────────
      var $anch = ctx.editor.state.selection.$anchor
      if ($anch.parent.type.name === 'paragraph') {
        var pText = $anch.parent.textContent
        if (pText.startsWith('/')) {
          var slashCoords = ctx.editor.view.coordsAtPos(ctx.editor.state.selection.anchor)
          if (!slashMenuOpenRef.current) {
            setSlashPos({ x: slashCoords.left, y: slashCoords.bottom + 6 })
            setSlashMenuOpen(true)
          }
          setSlashQuery(pText.slice(1))
        } else if (slashMenuOpenRef.current) {
          setSlashMenuOpen(false)
          setSlashQuery('')
        }
      } else if (slashMenuOpenRef.current) {
        setSlashMenuOpen(false)
        setSlashQuery('')
      }

      // ── TOC extraction (debounced 400 ms) ──────────────────────────────
      if (tocTimerRef.current) clearTimeout(tocTimerRef.current)
      tocTimerRef.current = setTimeout(function () {
        var json = ctx.editor.getJSON()
        var toc = []
        function walkNodes(nodes) {
          if (!nodes) return
          nodes.forEach(function (n) {
            if (n.type === 'heading') {
              var txt = (n.content || []).map(function (c) { return c.text || '' }).join('')
              if (txt) toc.push({ level: (n.attrs && n.attrs.level) || 1, text: txt })
            }
            if (n.content) walkNodes(n.content)
          })
        }
        walkNodes(json.content)
        setTocItems(toc)
      }, 400)

      if (contentTimerRef.current) clearTimeout(contentTimerRef.current)
      contentTimerRef.current = setTimeout(async function () {
        var html = ctx.editor.getHTML()
        // capturedNoteId: onUpdate ateşlendiğinde yakalandı.
        // selectedNoteIdRef.current bu noktada farklı bir nota işaret edebilir;
        // capturedNoteId kullanarak her zaman DÜZENLENEN notun kaydedilmesi sağlanır.
        var noteId = capturedNoteId
        // Mod 2: Bu not sifreli ve aciksa, save oncesi tekrar sifrele.
        // Cache'den parolayi al; yoksa save atla (kullanici oturum acmamis).
        var notePrev = null
        try {
          notePrev = (function() {
            var arr = []
            setNotes(function(prev) { arr = prev; return prev })
            return arr.find(function(n) { return n.id === noteId })
          })()
        } catch (e) { notePrev = null }
        var finalContent = html
        var isEnc = notePrev && notePrev.isFullyEncrypted
        if (isEnc) {
          // Guvenlik: Sifreli not + editor bos ise autosave ATLA.
          var plainText = (html || '').replace(/<[^>]*>/g, '').trim()
          if (!plainText) return
          var pw = recallPassword('note-' + noteId)
          if (!pw) { return }
          try {
            finalContent = await encryptText(html, pw)
          } catch (e) {
            console.error('Mod 2 auto-encrypt failed:', e); return
          }
        }
        setNotes(function (prev) {
          var existing = prev.find(function (n) { return n.id === noteId })
          // İçerik değişmediyse güncelleme yok — yalnızca görüntüleme amaçlı tetiklenen
          // onUpdate'lerin (atom node transaction, gapcursor vs.) neden olduğu sahte kayıtları engeller.
          if (existing && (existing.content === null || existing.content === finalContent)) return prev
          var updated = prev.map(function (n) {
            return n.id === noteId ? { ...n, content: finalContent, updatedAt: new Date() } : n
          })
          var note = updated.find(function (n) { return n.id === noteId })
          if (note) api.saveNote(note).catch(function (e) { console.error(e) })
          return updated
        })
      }, 600)
    },
  })

  /* Load data from backend — içerik olmadan hızlı liste; içerik not seçilince lazy-load edilir */
  useEffect(function () {
    api.getList()
      .then(function (data) {
        if (data.currentUserName) setCurrentUserName(data.currentUserName)
        if (data.companyId) setCurrentCompanyId(data.companyId)
        var flds = (data.folders || []).map(function (f) {
          return { id: f.id, name: f.name, parentId: f.parentId || null }
        })
        var nts = (data.notes || []).map(function (n) {
          return {
            id: n.id,
            folderId: n.folderId || null,
            title: n.title || '',
            content: null,  // lazy-loaded — GetContentJson ile seçilince yüklenir
            createdAt: n.createdAt ? new Date(n.createdAt) : new Date(),
            updatedAt: new Date(n.updatedAt),
            isPinned: !!n.isPinned,
            isFullyEncrypted: !!n.isFullyEncrypted,
            encryptionHint: n.encryptionHint || null,
            reminderCount: n.reminderCount || 0,
            tags: (function () { try { return n.tags ? JSON.parse(n.tags) : [] } catch (e) { return [] } })(),
            linkedEntityType:  n.linkedEntityType  || null,
            linkedEntityId:    n.linkedEntityId    || null,
            linkedEntityLabel: n.linkedEntityLabel || null,
            visibility:        n.visibility        || 0,
            isOwner:           n.isOwner !== false,
            shareIsPublic:             !!n.shareIsPublic,
            shareToken:                n.shareToken || null,
            shareIncludeAttachments:   !!n.shareIncludeAttachments,
            ocrText:                   n.ocrText    || null,
          }
        })
        setFolders(flds)
        setNotes(nts)
        var parentIds = new Set()
        flds.forEach(function (f) { if (f.parentId) parentIds.add(f.parentId) })
        setExpandedFolders(parentIds)
        if (nts.length > 0) setSelectedNoteId(nts[0].id)
      })
      .catch(function (e) { console.error('[NotesWorkspace] load error:', e) })
      .finally(function () { setLoading(false) })
  }, [])

  /* Not değişince ekleri sıfırla ve arka planda yükle (panel kapalı olsa bile sayı görünsün) */
  useEffect(function () {
    setAttachments([])
    if (!selectedNoteId) return
    setAttachmentsLoading(true)
    api.getAttachments(selectedNoteId)
      .then(function (data) { setAttachments(data || []) })
      .catch(function () { setAttachments([]) })
      .finally(function () { setAttachmentsLoading(false) })
  }, [selectedNoteId])

  /* İçerik lazy-load — not seçilince content:null ise GetContentJson ile yükle */
  useEffect(function () {
    if (!selectedNoteId) return
    var note = notesRef.current.find(function (n) { return n.id === selectedNoteId })
    if (!note || note.content !== null) return  // zaten yüklü

    var noteId = selectedNoteId
    setContentLoading(true)
    api.getContent(noteId)
      .then(function (data) {
        var loadedContent = data.content || ''
        var loadedOcrText = data.ocrText || null
        setNotes(function (prev) {
          return prev.map(function (n) {
            return n.id === noteId ? { ...n, content: loadedContent, ocrText: loadedOcrText } : n
          })
        })
        setContentLoadedTick(function (t) { return t + 1 })

        // OCR olmayan ama görsel içeren not ise arka planda tetikle
        if (!note.isFullyEncrypted && loadedOcrText === null && loadedContent.indexOf('data:image/') !== -1) {
          var token = (document.cookie.match(/XSRF-TOKEN=([^;]+)/) || [])[1] || ''
          fetch('/Notes/ReOcrNoteJson', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
            body: JSON.stringify({ noteId: noteId }),
          })
            .then(function (r) { return r.ok ? r.json() : null })
            .then(function (res) {
              if (!res || !res.ok || !res.ocrText) return
              setNotes(function (prev) {
                return prev.map(function (x) {
                  return x.id === noteId ? { ...x, ocrText: res.ocrText } : x
                })
              })
            })
            .catch(function () {})
        }
      })
      .catch(function (e) {
        console.error('[NotesWorkspace] content load error:', e)
        // Hata durumunda boş içerikle işaretle — sonsuz retry önlemi
        setNotes(function (prev) {
          return prev.map(function (n) {
            return n.id === noteId ? { ...n, content: '' } : n
          })
        })
        setContentLoadedTick(function (t) { return t + 1 })
      })
      .finally(function () { setContentLoading(false) })
  }, [selectedNoteId])

  /* Çöp kutusu seçilince server'dan yükle */
  useEffect(function () {
    if (selectedFolderId !== '__trash') return
    setTrashLoaded(false)
    api.getTrashed()
      .then(function (data) {
        setTrashedNotes((data || []).map(function (n) {
          return {
            id: n.id, folderId: n.folderId || null,
            title: n.title || '', content: n.content || '',
            createdAt: n.createdAt ? new Date(n.createdAt) : new Date(),
            updatedAt: new Date(n.updatedAt),
            isPinned: false, isFullyEncrypted: !!n.isFullyEncrypted,
            reminderCount: 0,
          }
        }))
        setTrashLoaded(true)
      })
      .catch(function () { setTrashLoaded(true) })
  }, [selectedFolderId])

  /* Klasör değişince ilk notu otomatik seç */
  useEffect(function () {
    var sorted = sortNotes(filteredNotes, sortOrder)
    if (sorted.length > 0) {
      setSelectedNoteId(sorted[0].id)
    } else {
      setSelectedNoteId(null)
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedFolderId])

  /* Çöp kutusu / başkasının notu / içerik yükleniyor → editor read-only */
  useEffect(function () {
    if (editor) {
      var isTrash = selectedFolderId === '__trash'
      var isNotOwner = selectedNote ? selectedNote.isOwner === false : false
      editor.setEditable(!isTrash && !isNotOwner && !contentLoading)
    }
  }, [selectedFolderId, editor, selectedNote, contentLoading])

  /* Sync Tiptap editor when switching notes */
  useEffect(function () {
    if (editor && selectedNote) {
      // İçerik henüz yüklenmedi — lazy load useEffect yükleyip contentLoadedTick'i artıracak
      if (selectedNote.content === null && !selectedNote.isFullyEncrypted) {
        isSwitchingNoteRef.current = true
        editor.commands.setContent('', false)
        clearTimeout(isSwitchingResetRef.current)
        isSwitchingResetRef.current = setTimeout(function() { isSwitchingNoteRef.current = false }, 300)
        return
      }
      // Mod 2: Sifreli not ise editor'a ciphertext yukleme; LockScreen gosterilecek.
      // Ama session'da zaten acilmissa (unlockedNoteIds) cache'den parola al ve decrypt et.
      if (selectedNote.isFullyEncrypted) {
        if (unlockedNoteIds.has(selectedNote.id)) {
          var cachedPw = recallPassword('note-' + selectedNote.id)
          if (cachedPw) {
            isSwitchingNoteRef.current = true
            decryptText(selectedNote.content, cachedPw)
              .then(function(pt) {
                if (editor && !editor.isDestroyed) {
                  editor.commands.setContent(pt || '', false)
                }
              })
              .catch(function() {
                // Cache expired veya key rotate; unlocked'dan cikar
                setUnlockedNoteIds(function(prev) { var n = new Set(prev); n.delete(selectedNote.id); return n })
              })
              .finally(function() { isSwitchingNoteRef.current = false })
          } else {
            // Cache yok artik; unlocked state'i kaldir (kilit ekrani gorunsun)
            setUnlockedNoteIds(function(prev) { var n = new Set(prev); n.delete(selectedNote.id); return n })
          }
        } else {
          // Editor'e dokunma — lock screen gorunecek
          isSwitchingNoteRef.current = true
          editor.commands.setContent('', false)
          clearTimeout(isSwitchingResetRef.current)
          isSwitchingResetRef.current = setTimeout(function() { isSwitchingNoteRef.current = false }, 300)
        }
      } else {
        var currentContent = editor.getHTML()
        if (currentContent !== selectedNote.content) {
          isSwitchingNoteRef.current = true
          editor.commands.setContent(selectedNote.content || '', false)
          // React 18 concurrent mode'da atom node view'ların async commit'i
          // 0ms'de tamamlanmayabilir; 300ms not geçişinin tüm render sürecini kapsar.
          clearTimeout(isSwitchingResetRef.current)
          isSwitchingResetRef.current = setTimeout(function() { isSwitchingNoteRef.current = false }, 300)
        }
      }
    } else if (editor && !selectedNote) {
      isSwitchingNoteRef.current = true
      editor.commands.setContent('', false)
      clearTimeout(isSwitchingResetRef.current)
      isSwitchingResetRef.current = setTimeout(function() { isSwitchingNoteRef.current = false }, 300)
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedNoteId, editor, unlockedNoteIds, contentLoadedTick])

  /* ── Handlers ─────────────────────────────────────── */
  var handleSelectFolder = useCallback(function (fid) { setSelectedFolderId(fid) }, [])

  var handleToggleFolder = useCallback(function (fid) {
    setExpandedFolders(function (prev) {
      var next = new Set(prev)
      if (next.has(fid)) next.delete(fid); else next.add(fid)
      return next
    })
  }, [])

  var handleNewFolder = useCallback(function () {
    var parentId = selectedFolderId && selectedFolderId !== '__trash' ? selectedFolderId : null
    if (parentId && getFolderDepth(folders, parentId) >= MAX_FOLDER_DEPTH - 1) return

    api.saveFolder('Yeni Klasor', parentId)
      .then(function (res) {
        if (!res.success) return
        var newFolder = { id: res.id, name: 'Yeni Klasor', parentId: parentId }
        setFolders(function (prev) { return prev.concat([newFolder]) })
        setSelectedFolderId(res.id)
        if (parentId) {
          setExpandedFolders(function (prev) { var n = new Set(prev); n.add(parentId); return n })
        }
        setRenamingFolderId(res.id)
        setRenamingValue('Yeni Klasor')
        setTimeout(function () { renamingRef.current && renamingRef.current.select() }, 60)
      })
      .catch(function (e) { console.error('[NotesWorkspace] saveFolder error:', e) })
  }, [selectedFolderId, folders])

  var handleImport = useCallback(function (e) {
    var file = e.target.files && e.target.files[0]
    if (!file) return
    e.target.value = ''   // aynı dosya tekrar seçilebilsin
    setIsImporting(true)
    setImportStatus(null)
    var targetFolderId = (selectedFolderId && selectedFolderId !== '__trash') ? selectedFolderId : null
    api.importEvernote(file, targetFolderId)
      .then(function (res) {
        if (!res.success) {
          setImportStatus({ ok: false, msg: res.error || 'Aktarım başarısız.' })
          return
        }
        setImportStatus({ ok: true, msg: res.imported + ' not aktarıldı.' + (res.failed > 0 ? ' ' + res.failed + ' başarısız.' : '') + (res.skippedAttachments > 0 ? ' (' + res.skippedAttachments + ' ek 20 MB sınırı nedeniyle atlandı)' : '') })
        setTimeout(function () { setImportStatus(null) }, 5000)
        // Klasörler ve notları yenile — mevcut notlardaki yüklü içeriği koru
        api.getList().then(function (data) {
          var flds = (data.folders || []).map(function (f) {
            return { id: f.id, name: f.name, parentId: f.parentId || null }
          })
          var nts = (data.notes || []).map(function (n) {
            return {
              id: n.id, folderId: n.folderId || null, title: n.title || '',
              content: null,  // lazy-load; mevcut içerik aşağıda merge edilir
              createdAt: n.createdAt ? new Date(n.createdAt) : new Date(),
              updatedAt: new Date(n.updatedAt),
              isPinned: !!n.isPinned, isFullyEncrypted: !!n.isFullyEncrypted,
              encryptionHint: n.encryptionHint || null, reminderCount: n.reminderCount || 0,
              tags: (function () { try { return n.tags ? JSON.parse(n.tags) : [] } catch (e) { return [] } })(),
              linkedEntityType:  n.linkedEntityType  || null,
              linkedEntityId:    n.linkedEntityId    || null,
              linkedEntityLabel: n.linkedEntityLabel || null,
              visibility:        n.visibility        || 0,
              isOwner:           n.isOwner !== false,
            }
          })
          setFolders(flds)
          // Mevcut yüklü içerikleri merge et (daha önce açılan notlar için tekrar fetch gerekmez)
          setNotes(function (prev) {
            return nts.map(function (newNote) {
              var existing = prev.find(function (p) { return p.id === newNote.id })
              return existing && existing.content !== null
                ? { ...newNote, content: existing.content, ocrText: existing.ocrText }
                : newNote
            })
          })
          if (res.folderId) {
            setSelectedFolderId(res.folderId)
            setExpandedFolders(function (prev) { var s = new Set(prev); s.add(res.folderId); return s })
          }
        }).catch(function (e) { console.error('[NotesWorkspace] reload after import error:', e) })
      })
      .catch(function (e) {
        setImportStatus({ ok: false, msg: 'Aktarım hatası: ' + e.message })
      })
      .finally(function () { setIsImporting(false) })
  }, [selectedFolderId])

  var handleAttachToggle = useCallback(function () {
    setAttachmentsOpen(function (p) { return !p })
  }, [])

  var handleAttachUpload = useCallback(function (e) {
    var fileArr = Array.from(e.target.files || [])
    e.target.value = ''
    if (fileArr.length === 0) { console.warn('[attach upload] no files selected'); return }
    if (!selectedNoteId) { console.warn('[attach upload] no selectedNoteId — note not yet saved?'); return }
    fileArr.forEach(function (file) {
      console.log('[attach upload] uploading', file.name, 'to note', selectedNoteId)
      api.uploadAttachment(selectedNoteId, file)
        .then(function (res) {
          console.log('[attach upload] response', res)
          if (res.success && res.attachment)
            setAttachments(function (prev) { return prev.concat([res.attachment]) })
          else if (!res.success)
            console.error('[attach upload] server refused:', res.error || res.message || JSON.stringify(res))
        })
        .catch(function (err) { console.error('[attach upload] fetch error:', err) })
    })
  }, [selectedNoteId])

  var handleAttachDelete = useCallback(function (id, fileName) {
    setConfirmModal({
      title: 'Eki Sil',
      text: '"' + fileName + '" kalıcı olarak silinecek.',
      onConfirm: function () {
        setConfirmModal(null)
        api.deleteAttachment(id)
          .then(function () {
            setAttachments(function (prev) { return prev.filter(function (a) { return a.id !== id }) })
          })
          .catch(function (err) { console.error('[attach delete]', err) })
      }
    })
  }, [])

  var handleRenameSubmit = useCallback(function () {
    var name = renamingValue.trim()
    var fid = renamingFolderId
    setRenamingFolderId(null)
    if (!name) {
      api.deleteFolder(fid).catch(function (e) { console.error(e) })
      setFolders(function (prev) { return prev.filter(function (f) { return f.id !== fid }) })
      setSelectedFolderId(null)
    } else {
      api.renameFolder(fid, name).catch(function (e) { console.error(e) })
      setFolders(function (prev) {
        return prev.map(function (f) { return f.id === fid ? { ...f, name: name } : f })
      })
    }
  }, [renamingFolderId, renamingValue])

  var handleRenameCancel = useCallback(function () {
    var fid = renamingFolderId
    setRenamingFolderId(null)
    setFolders(function (prev) {
      var f = prev.find(function (x) { return x.id === fid })
      if (f && f.name === 'Yeni Klasor') {
        api.deleteFolder(fid).catch(function (e) { console.error(e) })
        return prev.filter(function (x) { return x.id !== fid })
      }
      return prev
    })
    setSelectedFolderId(null)
  }, [renamingFolderId])

  var handleFolderContextMenu = useCallback(function (e, folderId, folderName) {
    e.preventDefault()
    e.stopPropagation()
    setFolderCtxMenu({ folderId: folderId, folderName: folderName, x: e.clientX, y: e.clientY })
  }, [])

  var handleCtxRename = useCallback(function () {
    if (!folderCtxMenu) return
    var fid = folderCtxMenu.folderId
    var fname = folderCtxMenu.folderName
    setFolderCtxMenu(null)
    setRenamingFolderId(fid)
    setRenamingValue(fname)
    setTimeout(function () { renamingRef.current && renamingRef.current.select() }, 60)
  }, [folderCtxMenu])

  var handleCtxDelete = useCallback(function () {
    if (!folderCtxMenu) return
    var fid = folderCtxMenu.folderId
    var folder = folders.find(function (f) { return f.id === fid })
    var parentId = folder ? (folder.parentId || null) : null
    setFolderCtxMenu(null)
    setConfirmModal({
      title: 'Klasörü Sil',
      text: 'Klasör silinecek. Doğrudan notlar kök dizine, alt klasörler üst klasöre taşınacak.',
      onConfirm: function () {
        setConfirmModal(null)
        api.deleteFolder(fid).catch(function (e) { console.error(e) })
        // Yalnızca bu klasörü kaldır; alt klasörleri üst klasöre yeniden bağla
        setFolders(function (prev) {
          return prev
            .filter(function (f) { return f.id !== fid })
            .map(function (f) { return f.parentId === fid ? Object.assign({}, f, { parentId: parentId }) : f })
        })
        // Bu klasördeki notları root'a taşı — alt klasör notlarına dokunma
        setNotes(function (prev) {
          return prev.map(function (n) { return n.folderId === fid ? Object.assign({}, n, { folderId: null }) : n })
        })
        if (selectedFolderId === fid) setSelectedFolderId(null)
      }
    })
  }, [folderCtxMenu, folders, selectedFolderId])

  useEffect(function () {
    if (!folderCtxMenu) return
    function close() { setFolderCtxMenu(null) }
    document.addEventListener('mousedown', close)
    return function () { document.removeEventListener('mousedown', close) }
  }, [folderCtxMenu])

  // Tablo context menüsünü dışarı tıklayınca kapat
  useEffect(function () {
    if (!tableCtxMenu) return
    function close() { setTableCtxMenu(null) }
    document.addEventListener('mousedown', close)
    return function () { document.removeEventListener('mousedown', close) }
  }, [tableCtxMenu])

  // Gear menüsünü dışarı tıklayınca kapat
  useEffect(function () {
    if (!gearMenuOpen) return
    function close(e) {
      if (gearBtnRef.current && gearBtnRef.current.contains(e.target)) return
      setGearMenuOpen(false)
    }
    document.addEventListener('mousedown', close)
    return function () { document.removeEventListener('mousedown', close) }
  }, [gearMenuOpen])

  // ── Global keyboard shortcuts: Focus Mode / Find / Quick Switcher / Esc ─
  useEffect(function () {
    function onKey(e) {
      if (e.key === 'F11') {
        e.preventDefault()
        setFocusMode(function (p) { return !p })
      } else if (e.key === 'Escape') {
        if (slashMenuOpenRef.current) { setSlashMenuOpen(false); setSlashQuery(''); return }
        setQuickSwitcherOpen(false)
        setFocusMode(false)
        setFindOpen(false)
        setTagInputOpen(false)
      } else if ((e.ctrlKey || e.metaKey) && (e.key === 'f' || e.key === 'F')) {
        e.preventDefault()
        setFindOpen(function (p) { return !p })
      } else if ((e.ctrlKey || e.metaKey) && (e.key === 'p' || e.key === 'P')) {
        e.preventDefault()
        setQuickSwitcherOpen(function (p) { return !p })
      }
    }
    document.addEventListener('keydown', onKey)
    return function () { document.removeEventListener('keydown', onKey) }
  }, [])

  // ── Close share modal on Esc ──────────────────────────────────────────
  useEffect(function () {
    if (!sharePanelOpen) return
    function onKey(e) { if (e.key === 'Escape') setSharePanelOpen(false) }
    document.addEventListener('keydown', onKey)
    return function () { document.removeEventListener('keydown', onKey) }
  }, [sharePanelOpen])

  // ── Auto-focus find input when bar opens ───────────────────────────────
  useEffect(function () {
    if (findOpen) {
      setTimeout(function () { findInputRef.current && findInputRef.current.focus() }, 40)
    }
  }, [findOpen])

  // ── Sync find term → search highlight + collect match positions ─────────
  useEffect(function () {
    if (!editor) return
    var term = (findOpen && findTerm) ? findTerm : ''
    // Update decoration highlights
    try { editor.view.dispatch(editor.state.tr.setMeta(searchPluginKey, term)) } catch (e) {}
    if (!term) { setFindMatches([]); setFindMatchIdx(0); return }
    // Collect positions
    var matches = []
    var lt = term.toLowerCase()
    editor.state.doc.descendants(function (node, pos) {
      if (!node.isText || !node.text) return
      var lc = node.text.toLowerCase()
      var i = 0
      while ((i = lc.indexOf(lt, i)) !== -1) {
        matches.push({ from: pos + i, to: pos + i + lt.length })
        i += 1
      }
    })
    setFindMatches(matches)
    setFindMatchIdx(0)
    if (matches.length > 0) {
      try { editor.chain().setTextSelection(matches[0]).scrollIntoView().run() } catch (e) {}
    }
  }, [editor, findTerm, findOpen])

  // ── Replace term update (just visual state) ─────────────────────────────
  // (no-op — replaceTerm is read at action time; kept for clarity)

  var handleSelectNote = useCallback(function (nid) {
    // Not degisirken pending autosave'i hemen iptal et —
    // eski notun plaintext/sifreli icerigi yeni notun icerigi ile karismasin
    if (contentTimerRef.current) { clearTimeout(contentTimerRef.current); contentTimerRef.current = null }
    setSelectedNoteId(nid)
    var n = notes.find(function (x) { return x.id === nid })
    setShareIsPublic(n ? !!n.shareIsPublic : false)
    setShareToken(n ? (n.shareToken || null) : null)
    setShareIncludeAttachments(n ? !!n.shareIncludeAttachments : false)
  }, [notes])

  var handleTogglePin = useCallback(function (nid, e) {
    if (e) { e.stopPropagation(); e.preventDefault() }
    api.togglePin(nid).then(function (res) {
      if (res.success) {
        setNotes(function (prev) {
          return prev.map(function (n) {
            return n.id === nid ? { ...n, isPinned: res.isPinned } : n
          })
        })
      }
    }).catch(function (err) { console.error('[NotesWorkspace] togglePin error:', err) })
  }, [])

  var saveTitleTimerRef = useRef(null)

  var handleTitleChange = useCallback(function (e) {
    var val = e.target.value
    setNotes(function (prev) {
      return prev.map(function (n) {
        return n.id === selectedNoteId ? { ...n, title: val, updatedAt: new Date() } : n
      })
    })
    if (saveTitleTimerRef.current) clearTimeout(saveTitleTimerRef.current)
    saveTitleTimerRef.current = setTimeout(function () {
      setNotes(function (prev) {
        var note = prev.find(function (n) { return n.id === selectedNoteId })
        if (note) api.saveNote(note).catch(function (e) { console.error(e) })
        return prev
      })
    }, 800)
  }, [selectedNoteId])

  var handleNewNote = useCallback(function () {
    var folderId = selectedFolderId && selectedFolderId !== '__trash' ? selectedFolderId : null
    api.saveNote({ id: null, folderId: folderId, title: '', content: '' })
      .then(function (res) {
        if (!res.success) return
        var newNote = {
          id: res.id,
          folderId: folderId,
          // content: '' (boş string) — null değil; null = "henüz yüklenmedi", '' = "gerçekten boş"
          title: '', content: '', createdAt: new Date(), updatedAt: new Date(),
          isPinned: false, isFullyEncrypted: false, encryptionHint: null,
          reminderCount: 0, tags: [], visibility: 0, isOwner: true,
          ocrText: null,
        }
        setNotes(function (prev) { return [newNote].concat(prev) })
        setSelectedNoteId(res.id)
      })
      .catch(function (e) { console.error('[NotesWorkspace] saveNote error:', e) })
  }, [selectedFolderId])

  var doDeleteNote = useCallback(function (targetId) {
    var delId = targetId || selectedNoteId
    if (!delId) return
    var note = notes.find(function (n) { return n.id === delId })
    api.deleteNote(delId).catch(function (e) { console.error(e) })
    setNotes(function (prev) { return prev.filter(function (n) { return n.id !== delId }) })
    if (note) {
      setTrashedNotes(function (prev) {
        return [note].concat(prev.filter(function (n) { return n.id !== delId }))
      })
    }
    if (delId === selectedNoteId) {
      setSelectedNoteId(function () {
        var remaining = filteredNotes.filter(function (n) { return n.id !== delId })
        return remaining.length > 0 ? remaining[0].id : null
      })
    }
    setConfirmModal(null)
  }, [selectedNoteId, filteredNotes, notes])

  var handleDeleteNote = useCallback(function (noteId) {
    var targetId = noteId || selectedNoteId
    if (!targetId) return
    var note = notes.find(function (n) { return n.id === targetId })
    setConfirmModal({
      title: 'Çöp Kutusuna Taşı',
      text: '"' + (note ? note.title || 'Başlıksız Not' : 'Not') + '" çöp kutusuna taşınacak.',
      okLabel: 'Çöp Kutusuna Taşı',
      onConfirm: function () { doDeleteNote(targetId) },
    })
  }, [selectedNoteId, notes, doDeleteNote])

  var handleRestoreNote = useCallback(function (id) {
    var note = trashedNotes.find(function (n) { return n.id === id })
    api.restoreNote(id).catch(function (e) { console.error(e) })
    setTrashedNotes(function (prev) { return prev.filter(function (n) { return n.id !== id }) })
    if (note) setNotes(function (prev) { return [note].concat(prev) })
  }, [trashedNotes])

  var handlePermanentDelete = useCallback(function (id) {
    var note = trashedNotes.find(function (n) { return n.id === id })
    setConfirmModal({
      title: 'Kalıcı Sil',
      text: '"' + (note ? note.title || 'Başlıksız Not' : 'Not') + '" kalıcı olarak silinecek, geri alınamaz.',
      okLabel: 'Kalıcı Sil',
      onConfirm: function () {
        api.permanentDeleteNote(id).catch(function (e) { console.error(e) })
        setTrashedNotes(function (prev) { return prev.filter(function (n) { return n.id !== id }) })
        setConfirmModal(null)
      }
    })
  }, [trashedNotes])

  var handleCtxTrashAll = useCallback(function () {
    if (!folderCtxMenu) return
    var fid = folderCtxMenu.folderId
    var fname = folderCtxMenu.folderName
    setFolderCtxMenu(null)
    var ids = getDescendantIds(folders, fid)
    var toTrash = notes.filter(function (n) { return ids.indexOf(n.folderId) !== -1 })
    if (toTrash.length === 0) return
    setConfirmModal({
      title: 'Klasörü Çöp Kutusuna Taşı',
      text: '"' + fname + '" içindeki ' + toTrash.length + ' not çöp kutusuna taşınacak.',
      okLabel: 'Çöp Kutusuna Taşı',
      onConfirm: function () {
        toTrash.forEach(function (n) { api.deleteNote(n.id).catch(function (e) { console.error(e) }) })
        var trashIds = toTrash.map(function (n) { return n.id })
        setNotes(function (prev) { return prev.filter(function (n) { return trashIds.indexOf(n.id) === -1 }) })
        setTrashedNotes(function (prev) {
          return toTrash.concat(prev.filter(function (n) { return trashIds.indexOf(n.id) === -1 }))
        })
        if (selectedNoteId && trashIds.indexOf(selectedNoteId) !== -1) setSelectedNoteId(null)
        setConfirmModal(null)
      }
    })
  }, [folderCtxMenu, folders, notes, selectedNoteId])

  /* ── Reminders (hatirlaticilar) ─────────────────────────────
     Secilen notun hatirlaticilari ReminderNotificationWorker tarafindan
     her 60 sn'de bir kontrol edilir; RemindAt <= now ise toast + (varsa)
     recurrence'a gore sonraki occurrence'i olusturur. */
  useEffect(function () {
    if (!selectedNoteId) { setReminders([]); return }
    var cancelled = false
    setRemindersLoading(true)
    api.getReminders(selectedNoteId)
      .then(function (data) {
        if (cancelled) return
        setReminders((data && data.reminders) || [])
      })
      .catch(function () { if (!cancelled) setReminders([]) })
      .finally(function () { if (!cancelled) setRemindersLoading(false) })
    return function () { cancelled = true }
  }, [selectedNoteId])

  var handleAddReminder = useCallback(function (remindAtIso, recurrenceType, recurrenceData, deliveryChannel, targetUserIds) {
    if (!selectedNoteId) return Promise.resolve()
    var nid = selectedNoteId
    return api.addReminder(nid, remindAtIso, recurrenceType, recurrenceData, deliveryChannel, targetUserIds)
      .then(function (r) {
        if (!r || !r.success) {
          // Rapor §6.6 — toast fallback
          var msg = 'Hatirlatici eklenemedi: ' + (r && r.message ? r.message : 'Bilinmeyen hata.')
          if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast(msg, 'err')
          else alert(msg)
          return
        }
        setReminders(function (prev) {
          var next = prev.concat([r.reminder])
          next.sort(function (a, b) { return (a.remindAt || '').localeCompare(b.remindAt || '') })
          return next
        })
        // Not listesindeki rozet icin count'u guncelle — yeni eklenen is_sent=0 (aktif).
        setNotes(function (prev) {
          return prev.map(function (n) {
            return n.id === nid ? { ...n, reminderCount: (n.reminderCount || 0) + 1 } : n
          })
        })
      })
      .catch(function (e) {
        var em = 'Hatirlatici eklenemedi: ' + e.message
        if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast(em, 'err')
        else alert(em)
      })
  }, [selectedNoteId])

  var handleDeleteReminder = useCallback(function (reminderId) {
    if (!selectedNoteId) return
    var nid = selectedNoteId
    // Aktif reminder'i (is_sent=0) silerken count da dussun. Gonderilmis olanin silinmesi
    // count'u etkilemez cunku zaten aktif degil.
    var wasActive = reminders.some(function (r) { return r.id === reminderId && !r.isSent })
    api.deleteReminder(reminderId, nid)
      .then(function (r) {
        if (r && r.success) {
          setReminders(function (prev) { return prev.filter(function (x) { return x.id !== reminderId }) })
          if (wasActive) {
            setNotes(function (prev) {
              return prev.map(function (n) {
                return n.id === nid ? { ...n, reminderCount: Math.max(0, (n.reminderCount || 0) - 1) } : n
              })
            })
          }
        }
      })
      .catch(function () { /* sessizce yutma */ })
  }, [selectedNoteId, reminders])

  var handleExportPdf = useCallback(function () {
    if (!selectedNote || !editor) return
    setActionsMenuOpen(false)
    var title = selectedNote.title || 'Basliksiz Not'
    var content = editor.getHTML()
    var date = formatDate(selectedNote.updatedAt) + ' ' + formatTime(selectedNote.updatedAt)

    var container = document.createElement('div')
    container.style.cssText = 'font-family:Inter,-apple-system,BlinkMacSystemFont,Segoe UI,sans-serif;' +
      'max-width:700px;padding:0;color:#1e293b;font-size:14px;line-height:1.7;'
    container.innerHTML =
      '<h1 style="font-size:24px;font-weight:700;margin:0 0 6px;color:#0f172a;">' + title + '</h1>' +
      '<div style="font-size:11px;color:#94a3b8;margin-bottom:20px;padding-bottom:12px;border-bottom:1px solid #e2e8f0;">' +
        'Son duzenleme: ' + date + '</div>' +
      '<div style="font-size:14px;line-height:1.7;">' + content + '</div>'

    container.querySelectorAll('h2').forEach(function (el) {
      el.style.cssText = 'font-size:18px;font-weight:600;margin:20px 0 8px;'
    })
    container.querySelectorAll('h3').forEach(function (el) {
      el.style.cssText = 'font-size:15px;font-weight:600;margin:16px 0 6px;'
    })
    container.querySelectorAll('blockquote').forEach(function (el) {
      el.style.cssText = 'margin:14px 0;padding:10px 16px;border-left:3px solid #6366f1;' +
        'background:#f8fafc;border-radius:0 8px 8px 0;color:#475569;font-style:italic;'
    })
    container.querySelectorAll('code').forEach(function (el) {
      el.style.cssText = 'font-family:monospace;font-size:12px;padding:2px 5px;background:#f1f5f9;border-radius:3px;'
    })
    container.querySelectorAll('a').forEach(function (el) { el.style.color = '#6366f1' })

    var fileName = title.replace(/[^a-zA-Z0-9\u00C0-\u024F\u0400-\u04FF ._-]/g, '').trim() || 'not'

    html2pdf().set({
      margin: [15, 15, 15, 15],
      filename: fileName + '.pdf',
      image: { type: 'jpeg', quality: 0.95 },
      html2canvas: { scale: 2, useCORS: true, logging: false },
      jsPDF: { unit: 'mm', format: 'a4', orientation: 'portrait' },
    }).from(container).save()
  }, [selectedNote, editor])


  /* ══════════════════════════════════════════════════════════
     ŞIFRELEME — Mod 1 (Secili bolum) + Mod 2 (Tum not)
     ══════════════════════════════════════════════════════════ */

  // Toolbar "🔒 Sifrele" butonu (akilli):
  //   - Secim varsa  → Mod 1 (secili aralik)
  //   - Secim yoksa → Mod 2 (tum not)
  var handleOpenEncryptSelection = useCallback(function() {
    if (!editor || !selectedNote) return
    var sel = editor.state.selection
    if (sel && !sel.empty) {
      setEncryptSelectionOpen(true)
    } else {
      // Zaten sifreli ise tekrar sifreleme teklif etme
      if (selectedNote.isFullyEncrypted) {
        showMsg('Bu not zaten tamamen sifreli.', false)
        return
      }
      setEncryptWholeNoteOpen(true)
    }
  }, [editor, selectedNote])

  // Mod 1: EncryptPromptModal submit → secili aralik ciphertext'e cevrilir + EncryptedMark sar
  var handleEncryptSelectionSubmit = useCallback(async function(password, hint) {
    if (!editor) { setEncryptSelectionOpen(false); return }
    var sel = editor.state.selection
    if (!sel || sel.empty) { setEncryptSelectionOpen(false); return }
    try {
      // Secilen metnin plain text + HTML'ini al (HTML kullan — formatlama korunsun)
      var selectedHtml = ''
      try {
        var slice = editor.state.doc.cut(sel.from, sel.to)
        // DOMSerializer kullanacagiz; daha basit: editor.getJSON'dan getHTML
        var tmp = document.createElement('div')
        // Tiptap'in kendi serializer'i yerine textBetween yeterli cogu durumda
        tmp.textContent = editor.state.doc.textBetween(sel.from, sel.to, '\n')
        selectedHtml = tmp.textContent
      } catch (e) {
        selectedHtml = editor.state.doc.textBetween(sel.from, sel.to, '\n')
      }
      var payload = await encryptText(selectedHtml, password)
      // Secili metni sil ve yerine "🔒 Sifreli" EncryptedMark'li bir span insert et
      editor.chain().focus()
        .deleteSelection()
        .insertContent({
          type: 'text',
          text: '🔒 Sifreli bolum',
          marks: [{ type: 'encrypted', attrs: { ct: payload, hint: hint || null } }]
        })
        .run()
      setEncryptSelectionOpen(false)
    } catch (e) {
      showMsg('Sifreleme hatasi: ' + (e.message || e), false)
    }
  }, [editor])

  // Mod 1: Sifreli bloga tiklayinca → decrypt
  var handleDecryptBlockSubmit = useCallback(async function(password) {
    var target = decryptBlockTarget
    if (!target || !editor) return false
    try {
      var plaintext = await decryptText(target.ct, password)
      // DOM span'i "acilmis" olarak isaretle (renderlama geri donerse yine kilit goster)
      try { target.element.setAttribute('data-open', '1') } catch (e) {}
      // Session cache
      var ck = await payloadCacheKey(target.ct)
      if (ck) rememberPassword(ck, password)
      // Kullaniciya inline bir popup / alert ile goster — Rapor §6.6 CalibraAlert.info fallback
      if (window.CalibraAlert && window.CalibraAlert.info) window.CalibraAlert.info(plaintext, { title: '🔓 Sifreli icerik' })
      else alert('🔓 Sifreli icerik:\n\n' + plaintext)
      setDecryptBlockTarget(null)
      return true
    } catch (e) {
      return false
    }
  }, [decryptBlockTarget, editor])

  // Mod 2: Actions menu'den "Notu Tamamen Sifrele" tiklanir
  var handleOpenEncryptWholeNote = useCallback(function() {
    if (!selectedNote) return
    setActionsMenuOpen(false)
    setEncryptWholeNoteOpen(true)
  }, [selectedNote])

  // Mod 2: EncryptPromptModal submit → tum content encrypt + note flag'i set + save
  var handleEncryptWholeNoteSubmit = useCallback(async function(password, hint) {
    if (!selectedNote || !editor) { setEncryptWholeNoteOpen(false); return }
    // Pending autosave timer'i iptal et
    if (contentTimerRef.current) { clearTimeout(contentTimerRef.current); contentTimerRef.current = null }
    try {
      var html = editor.getHTML()
      var payload = await encryptText(html, password)

      // ROUND-TRIP DOGRULAMA — ayni parolayla hemen coz. Basarisizsa save ATLA.
      // Bu, crypto katmanindaki olasi bug'lari veya serializasyon sorunlarini yakalar.
      var verified = null
      try { verified = await decryptText(payload, password) } catch (e) { verified = null }
      if (verified !== html) {
        console.error('[NW enc] Round-trip FAILED — sifreleme guvenilmez, iptal.')
        console.error('  original len:', (html || '').length,
                      ' verified len:', (verified || '').length)
        var em1 = 'Sifreleme sirasinda dogrulama basarisiz oldu. Veri degistirilmedi. Lutfen tekrar deneyin.'
        if (window.CalibraAlert && window.CalibraAlert.error) window.CalibraAlert.error(em1)
        else if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast(em1, 'err')
        else alert(em1)
        return
      }

      var noteId = selectedNote.id
      // Session cache
      rememberPassword('note-' + noteId, password)
      // Editor'u temizle — onUpdate'i bastir
      isSwitchingNoteRef.current = true
      try { editor.commands.setContent('', false) } catch (e) {}
      isSwitchingNoteRef.current = false
      setUnlockedNoteIds(function(prev) { var n = new Set(prev); n.delete(noteId); return n })
      // State + backend save
      setNotes(function(prev) {
        var updated = prev.map(function(n) {
          if (n.id !== noteId) return n
          return {
            ...n,
            content: payload,
            isFullyEncrypted: true,
            encryptionHint: hint || null,
            updatedAt: new Date()
          }
        })
        var note = updated.find(function(n) { return n.id === noteId })
        if (note) {
          console.log('[NW enc] saving encrypted note', noteId, ' payload len:', payload.length, ' preview:', payload.slice(0, 60))
          api.saveNote(note).catch(function(e){ console.error('[NW enc] saveNote err', e) })
        }
        return updated
      })
      setEncryptWholeNoteOpen(false)
    } catch (e) {
      console.error('[NW enc] encrypt err', e)
      var em2 = 'Sifreleme hatasi: ' + (e.message || e)
      if (window.CalibraAlert && window.CalibraAlert.error) window.CalibraAlert.error(em2)
      else if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast(em2, 'err')
      else alert(em2)
    }
  }, [selectedNote, editor])

  // Mod 2: Lock screen submit → decrypt + editor'a yukle (sessiondan hatirlanacak)
  var handleUnlockFullNote = useCallback(async function(password) {
    if (!selectedNote) return false
    // En guncel content'i notes state'inden cek (selectedNote stale olabilir)
    var freshNote = null
    setNotes(function(prev) {
      freshNote = prev.find(function(n) { return n.id === selectedNote.id })
      return prev
    })
    var ct = (freshNote ? freshNote.content : selectedNote.content) || ''
    console.log('[NW dec] unlock note', selectedNote.id, ' payload len:', ct.length, ' preview:', ct.slice(0, 60))
    try {
      var plaintext = await decryptText(ct, password)
      console.log('[NW dec] ok, plaintext len:', plaintext.length)
      // Editor'e yukle
      isSwitchingNoteRef.current = true
      if (editor) editor.commands.setContent(plaintext, false)
      isSwitchingNoteRef.current = false
      // Session cache + unlocked set
      rememberPassword('note-' + selectedNote.id, password)
      setUnlockedNoteIds(function(prev) { var n = new Set(prev); n.add(selectedNote.id); return n })
      return true
    } catch (e) {
      console.error('[NW dec] decrypt FAILED:', e && e.message)
      // Payload gercekten parse edilebilen bir JSON mu?
      try {
        var parsed = JSON.parse(ct)
        console.error('[NW dec] payload structure:', Object.keys(parsed), ' ct-len:', (parsed.ct || '').length)
      } catch (p) {
        console.error('[NW dec] payload JSON PARSE FAILED — icerik beklenen formatta degil.')
      }
      return false
    }
  }, [selectedNote, editor])

  // Mod 2: Actions menu'den "Notu Tekrar Kilitle" (acikken)
  var handleLockFullNote = useCallback(function() {
    if (!selectedNote) return
    setActionsMenuOpen(false)
    // Pending autosave'i iptal et — bos content uzerine yazmasin
    if (contentTimerRef.current) { clearTimeout(contentTimerRef.current); contentTimerRef.current = null }
    // Editor'i temizle (onUpdate tetiklenmesin)
    isSwitchingNoteRef.current = true
    try { if (editor) editor.commands.setContent('', false) } catch (e) {}
    isSwitchingNoteRef.current = false
    setUnlockedNoteIds(function(prev) { var n = new Set(prev); n.delete(selectedNote.id); return n })
  }, [selectedNote, editor])

  // Mod 2: Actions menu'den "Sifrelemeyi Kaldir" (acikken)
  var handleRemoveFullEncryption = useCallback(function() {
    if (!selectedNote) return
    setActionsMenuOpen(false)
    if (!editor) return
    // Pending autosave'i iptal et — yarisi kalmis state ile cakismasin
    if (contentTimerRef.current) { clearTimeout(contentTimerRef.current); contentTimerRef.current = null }
    var html = editor.getHTML()
    var noteId = selectedNote.id
    setNotes(function(prev) {
      var updated = prev.map(function(n) {
        if (n.id !== noteId) return n
        return {
          ...n, content: html, isFullyEncrypted: false, encryptionHint: null,
          updatedAt: new Date()
        }
      })
      var note = updated.find(function(n) { return n.id === noteId })
      if (note) api.saveNote(note).catch(function(e){ console.error(e) })
      return updated
    })
    // Unlocked set'ten de cikar, cache'i temizle
    setUnlockedNoteIds(function(prev) { var n = new Set(prev); n.delete(noteId); return n })
    showMsg('Sifreleme kaldirildi.', true)
  }, [selectedNote, editor])

  // Helper: mesaj bar (mevcut showMsg yoksa basit alert fallback)
  function showMsg(msg, ok) {
    // NotesWorkspace'te toast yoksa console'a düş; editor üstü minik toast ileride eklenebilir
    console.log((ok ? '[OK] ' : '[ERR] ') + msg)
  }

  /* ── Insert handler (Tiptap commands) ──────────────── */
  var handleInsert = useCallback(function (item) {
    if (!editor) return
    editor.chain().focus()
    switch (item.action) {
      case 'heading1':
        editor.chain().focus().toggleHeading({ level: 1 }).run()
        break
      case 'heading2':
        editor.chain().focus().toggleHeading({ level: 2 }).run()
        break
      case 'heading3':
        editor.chain().focus().toggleHeading({ level: 3 }).run()
        break
      case 'blockquote':
        editor.chain().focus().toggleBlockquote().run()
        break
      case 'callout':
        editor.chain().focus()
          .insertContent('<blockquote class="nw-callout"><p>💡 Buraya notunuzu yazın…</p></blockquote>')
          .run()
        break
      case 'taskList':
        editor.chain().focus().toggleTaskList().run()
        break
      case 'table':
        editor.chain().focus().insertTable({ rows: 3, cols: 3, withHeaderRow: true }).run()
        break
      case 'hr':
        editor.chain().focus().setHorizontalRule().run()
        break
      case 'codeBlock':
        editor.chain().focus().toggleCodeBlock().run()
        break
      case 'link': {
        var url = prompt('Link URL:')
        if (url) editor.chain().focus().setLink({ href: url }).run()
        break
      }
      case 'image': {
        imageInputRef.current && imageInputRef.current.click()
        break
      }
      case 'youtube': {
        var ytUrl = prompt('YouTube URL:')
        if (ytUrl) editor.chain().focus().setYoutubeVideo({ src: ytUrl }).run()
        break
      }
      case 'audio':  setAudioRecordOpen(true); break
      case 'draw':   editor.chain().focus().insertDrawingCanvas().run(); break
      case 'camera': setCameraOpen(true);       break
      default:
        break
    }
  }, [editor])

  /* ── TOC: scroll editor to a heading ─────────────── */
  var scrollToHeading = useCallback(function (text) {
    if (!editor) return
    var headings = editor.view.dom.querySelectorAll('h1,h2,h3,h4,h5,h6')
    for (var i = 0; i < headings.length; i++) {
      if (headings[i].textContent.trim() === text) {
        headings[i].scrollIntoView({ behavior: 'smooth', block: 'start' })
        break
      }
    }
  }, [editor])

  /* ── Find & Replace handlers ──────────────────────── */
  var handleFindNext = useCallback(function () {
    if (!editor || findMatches.length === 0) return
    var next = (findMatchIdx + 1) % findMatches.length
    setFindMatchIdx(next)
    try { editor.chain().setTextSelection(findMatches[next]).scrollIntoView().run() } catch (e) {}
  }, [editor, findMatches, findMatchIdx])

  var handleFindPrev = useCallback(function () {
    if (!editor || findMatches.length === 0) return
    var prev = (findMatchIdx - 1 + findMatches.length) % findMatches.length
    setFindMatchIdx(prev)
    try { editor.chain().setTextSelection(findMatches[prev]).scrollIntoView().run() } catch (e) {}
  }, [editor, findMatches, findMatchIdx])

  var handleReplaceOne = useCallback(function () {
    if (!editor || findMatches.length === 0 || findMatchIdx >= findMatches.length) return
    var match = findMatches[findMatchIdx]
    try {
      editor.chain().setTextSelection(match).insertContent(replaceTerm).run()
      // Recalculate after replace
      var lt = findTerm.toLowerCase()
      var newMatches = []
      editor.state.doc.descendants(function (node, pos) {
        if (!node.isText || !node.text) return
        var lc = node.text.toLowerCase()
        var i = 0
        while ((i = lc.indexOf(lt, i)) !== -1) {
          newMatches.push({ from: pos + i, to: pos + i + lt.length })
          i += 1
        }
      })
      setFindMatches(newMatches)
      var nextIdx = Math.min(findMatchIdx, newMatches.length - 1)
      setFindMatchIdx(Math.max(0, nextIdx))
      try { editor.view.dispatch(editor.state.tr.setMeta(searchPluginKey, findTerm)) } catch (e) {}
    } catch (e) {}
  }, [editor, findMatches, findMatchIdx, replaceTerm, findTerm])

  var handleReplaceAll = useCallback(function () {
    if (!editor || !findTerm) return
    try {
      var lt = findTerm.toLowerCase()
      // Collect in reverse so positions stay valid after each replace
      var all = []
      editor.state.doc.descendants(function (node, pos) {
        if (!node.isText || !node.text) return
        var lc = node.text.toLowerCase()
        var i = 0
        while ((i = lc.indexOf(lt, i)) !== -1) {
          all.push({ from: pos + i, to: pos + i + lt.length })
          i += 1
        }
      })
      all.reverse().forEach(function (m) {
        editor.chain().setTextSelection(m).insertContent(replaceTerm).run()
      })
      setFindMatches([])
      setFindMatchIdx(0)
      try { editor.view.dispatch(editor.state.tr.setMeta(searchPluginKey, '')) } catch (e) {}
    } catch (e) {}
  }, [editor, findTerm, replaceTerm])

  /* ── Slash command: select item ───────────────────── */
  var handleSlashSelect = useCallback(function (item) {
    setSlashMenuOpen(false)
    setSlashQuery('')
    if (!editor) return
    // Delete the "/" and any query text typed after it
    var $anchor = editor.state.selection.$anchor
    var pText = $anchor.parent.textContent
    if (pText.startsWith('/')) {
      try {
        editor.chain().focus()
          .deleteRange({ from: $anchor.start(), to: editor.state.selection.anchor })
          .run()
      } catch (e) {}
    }
    setTimeout(function () { handleInsert(item) }, 10)
  }, [editor, handleInsert])

  /* ── Quick Switcher: navigate to selected note ─────── */
  var handleQuickSelect = useCallback(function (note) {
    setQuickSwitcherOpen(false)
    if (note.folderId) {
      setSelectedFolderId(note.folderId)
      setExpandedFolders(function (prev) { var s = new Set(prev); s.add(note.folderId); return s })
    } else {
      setSelectedFolderId(null)
    }
    setSelectedNoteId(note.id)
  }, [])

  /* ── Clone Note ─────────────────────────────────────── */
  var handleCloneNote = useCallback(function () {
    if (!selectedNoteId) return
    setActionsMenuOpen(false)
    api.cloneNote(selectedNoteId)
      .then(function (res) {
        if (!res.ok) { console.error('Clone failed:', res.error); return }
        var n = res.note
        var newNote = {
          id: n.id, folderId: n.folderId || null,
          title: n.title || '', content: n.content || '',
          tags: (function () { try { return n.tags ? JSON.parse(n.tags) : [] } catch (e) { return [] } })(),
          updatedAt: n.updatedAt ? new Date(n.updatedAt) : new Date(),
          createdAt: n.createdAt ? new Date(n.createdAt) : new Date(),
          isPinned: false, isFullyEncrypted: false, encryptionHint: null, reminderCount: 0,
        }
        setNotes(function (prev) { return [newNote].concat(prev) })
        setSelectedNoteId(newNote.id)
      })
      .catch(function (e) { console.error('[cloneNote]', e) })
  }, [selectedNoteId])


  /* ── Tags ────────────────────────────────────────────── */
  var handleTagAdd = useCallback(function (rawTag) {
    var tag = rawTag.trim().toLowerCase().replace(/\s+/g, '-').slice(0, 40)
    if (!tag || !selectedNoteId) return
    setNotes(function (prev) {
      return prev.map(function (n) {
        if (n.id !== selectedNoteId) return n
        var existing = n.tags || []
        if (existing.indexOf(tag) !== -1) return n
        var newTags = existing.concat([tag])
        api.saveNote({ ...n, tags: JSON.stringify(newTags) }).catch(console.error)
        return { ...n, tags: newTags }
      })
    })
    setTagInput('')
    setTagInputOpen(false)
  }, [selectedNoteId])

  var handleTagRemove = useCallback(function (tag) {
    if (!selectedNoteId) return
    setNotes(function (prev) {
      return prev.map(function (n) {
        if (n.id !== selectedNoteId) return n
        var newTags = (n.tags || []).filter(function (t) { return t !== tag })
        api.saveNote({ ...n, tags: JSON.stringify(newTags) }).catch(console.error)
        return { ...n, tags: newTags }
      })
    })
  }, [selectedNoteId])

  var handleVisibilityToggle = useCallback(function () {
    if (!selectedNoteId) return
    setNotes(function (prev) {
      return prev.map(function (n) {
        if (n.id !== selectedNoteId) return n
        var updated = Object.assign({}, n, { visibility: n.visibility === 1 ? 0 : 1 })
        api.saveNote(updated)
        return updated
      })
    })
  }, [selectedNoteId])

  /* ── Share: public link toggle ──────────────────────── */
  var handleSetSharePublic = useCallback(async function (isPublic, includeAttachments) {
    if (!selectedNoteId) return
    var incl = includeAttachments !== undefined ? includeAttachments : shareIncludeAttachments
    var token = await fetch('/Notes/SetSharePublicJson', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'RequestVerificationToken': document.querySelector('meta[name="csrf-token"]')?.content ?? document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? ''
      },
      body: JSON.stringify({ noteId: selectedNoteId, isPublic, shareIncludeAttachments: incl }),
    }).then(function (r) { return r.json() })
    if (token.ok) {
      setShareIsPublic(isPublic)
      setShareToken(token.shareToken)
      setShareIncludeAttachments(incl)
      setNotes(function (prev) {
        return prev.map(function (n) {
          return n.id === selectedNoteId
            ? { ...n, shareIsPublic: isPublic, shareToken: token.shareToken, shareIncludeAttachments: incl }
            : n
        })
      })
    }
  }, [selectedNoteId, shareIncludeAttachments])

  /* ── User share helpers ───────────────────────────── */
  var csrfToken = function () {
    return document.querySelector('meta[name="csrf-token"]')?.content
        ?? document.querySelector('input[name="__RequestVerificationToken"]')?.value
        ?? ''
  }

  var loadNoteShares = useCallback(async function (noteId) {
    if (!noteId) return
    try {
      var r = await fetch('/Notes/GetNoteSharesJson?noteId=' + noteId)
      var j = await r.json()
      if (j.ok) setUserShares(j.shares || [])
    } catch {}
  }, [])

  var loadCompanyUsers = useCallback(async function () {
    if (companyUsers.length > 0) return
    try {
      var r = await fetch('/Notes/CompanyUsersJson')
      var list = await r.json()
      setCompanyUsers(Array.isArray(list) ? list : [])
    } catch {}
  }, [companyUsers.length])

  var handleAddUserShare = useCallback(async function (userId, canEdit) {
    if (!selectedNoteId) return
    try {
      var r = await fetch('/Notes/SaveUserShareJson', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': csrfToken() },
        body: JSON.stringify({ noteId: selectedNoteId, userId, canEdit }),
      })
      var j = await r.json()
      if (j.ok) {
        setUserShares(function (prev) {
          var existing = prev.findIndex(function (s) { return s.userId === userId })
          if (existing >= 0) {
            var updated = [...prev]; updated[existing] = j.share; return updated
          }
          return [...prev, j.share]
        })
        setShareUserSearch('')
        setShareUserDropOpen(false)
      }
    } catch {}
  }, [selectedNoteId])

  var handleToggleShareEdit = useCallback(async function (shareId, noteId, canEdit) {
    try {
      var r = await fetch('/Notes/UpdateSharePermissionJson', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': csrfToken() },
        body: JSON.stringify({ noteId, shareId, canEdit }),
      })
      var j = await r.json()
      if (j.ok) {
        setUserShares(function (prev) {
          return prev.map(function (s) { return s.shareId === shareId ? { ...s, canEdit } : s })
        })
      }
    } catch {}
  }, [])

  var handleRemoveUserShare = useCallback(async function (shareId, noteId) {
    try {
      var r = await fetch('/Notes/RemoveUserShareJson', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': csrfToken() },
        body: JSON.stringify({ noteId, shareId }),
      })
      var j = await r.json()
      if (j.ok) {
        setUserShares(function (prev) { return prev.filter(function (s) { return s.shareId !== shareId }) })
      }
    } catch {}
  }, [])

  /* ── Toolbar helper ───────────────────────────────── */
  function tbClass(isActive) {
    return 'nw-toolbar-btn' + (isActive ? ' nw-toolbar-btn--active' : '')
  }

  /* ── Render ───────────────────────────────────────── */
  return (
    <div className="w-full h-full flex flex-col font-sans text-sm text-slate-800 dark:text-slate-200">

      {loading && (
        <div className="nw-loading">
          <Loader2 size={24} className="nw-spin" />
          <span>Yukleniyor...</span>
        </div>
      )}

      {!loading && <>

      <input type="file" accept=".enex" ref={importInputRef} onChange={handleImport} style={{ display: 'none' }} />
      <input type="file" accept="image/*" ref={imageInputRef} style={{ display: 'none' }}
        onChange={function (e) {
          var file = e.target.files && e.target.files[0]
          e.target.value = ''
          if (!file || !editor) return
          var reader = new FileReader()
          reader.onload = function (ev) {
            editor.chain().focus().setImage({ src: ev.target.result, alt: file.name }).run()
          }
          reader.readAsDataURL(file)
        }}
      />

      {/* ═══ 3-Column Content ═══ */}
      <div className={'nw-columns-wrap' + (focusMode ? ' nw-focus-mode' : '')}>

      {/* ═══ Column 1: Sidebar ═══ */}
      <div className="nw-col-folders">
        {/* CTA — Yeni Not */}
        <div className="nw-sidebar-cta">
          <button
            className="nw-sidebar-new-btn"
            onClick={handleNewNote}
            disabled={selectedFolderId === '__trash'}
          >
            <Plus size={16} />
            <span>Not</span>
          </button>
        </div>

        {/* Üst Navigasyon */}
        <div className="nw-sidebar-nav">
          <div
            className={'nw-sidebar-nav-item' + (selectedFolderId === null && !searchQuery ? ' nw-sidebar-nav-item--active' : '')}
            onClick={function () { handleSelectFolder(null); setSearchQuery('') }}
          >
            <Home size={14} className="nw-folder-ico" />
            <span className="nw-folder-label">Tüm Notlar</span>
            <span className="nw-folder-count">{notes.length}</span>
          </div>
        </div>

        {/* Not Defterleri başlık */}
        <div className="nw-sidebar-section-hdr">
          <span>Not Defterleri</span>
          <button className="nw-sidebar-section-add" title="Yeni defter" onClick={handleNewFolder}>
            <Plus size={12} />
          </button>
        </div>

        {/* Klasör ağacı */}
        <div className="nw-folders-scroll">
          <FolderTree
            folders={folders} notes={notes} parentId={null} depth={0}
            selectedFolderId={selectedFolderId} expandedSet={expandedFolders}
            onSelect={handleSelectFolder} onToggle={handleToggleFolder}
            renamingId={renamingFolderId} renamingValue={renamingValue}
            renamingRef={renamingRef}
            onRenameChange={function (e) { setRenamingValue(e.target.value) }}
            onRenameSubmit={handleRenameSubmit}
            onRenameCancel={handleRenameCancel}
            onFolderContextMenu={handleFolderContextMenu}
          />
        </div>

        {/* Alt: Ayarlar + Çöp Kutusu */}
        <div className="nw-sidebar-bottom">
          <div className="nw-gear-wrap" ref={gearBtnRef}>
            <button
              className={'nw-sidebar-settings-btn' + (gearMenuOpen ? ' nw-sidebar-settings-btn--open' : '')}
              onClick={function () { setGearMenuOpen(function (p) { return !p }) }}
              title="Ayarlar"
              disabled={isImporting}
            >
              {isImporting ? <Loader2 size={14} className="nw-spin" /> : <Settings size={14} />}
              <span>Ayarlar</span>
            </button>
            {gearMenuOpen && (
              <div className="nw-gear-dropdown nw-gear-dropdown--up">
                <button
                  className="nw-gear-item"
                  onClick={function () {
                    setGearMenuOpen(false)
                    importInputRef.current && importInputRef.current.click()
                  }}
                >
                  <Upload size={13} />
                  <span>İçe Aktar (Evernote)</span>
                </button>
                <button
                  className="nw-gear-item"
                  onClick={function () { setGearMenuOpen(false); setShortcutsOpen(true) }}
                >
                  <Keyboard size={13} />
                  <span>Klavye Kısayolları</span>
                </button>
              </div>
            )}
          </div>
          {importStatus && (
            <div className={'nw-import-status nw-import-toast--' + (importStatus.ok ? 'ok' : 'err')}>
              {importStatus.msg}
            </div>
          )}
          <div
            className={'nw-folder-item nw-folder-item--trash' + (selectedFolderId === '__trash' ? ' nw-folder-item--active-trash' : '')}
            onClick={function () { handleSelectFolder('__trash') }}
          >
            <span style={{ width: 18, flexShrink: 0 }} />
            <Trash2 size={15} className="nw-folder-ico" />
            <span className="nw-folder-label">Çöp Kutusu</span>
          </div>
        </div>
      </div>

      {/* ═══ Column 2: Note List ═══ */}
      <div className="nw-col-list">
        <div className="nw-list-header">
          <div className="nw-list-header-title-wrap">
            <h4 className="nw-list-title">
              {selectedFolderId === '__trash'
                ? 'Çöp Kutusu'
                : selectedFolderId
                  ? (folders.find(function (f) { return f.id === selectedFolderId }) || {}).name || 'Notlar'
                  : 'Tüm Notlar'}
            </h4>
            {filteredNotes.length > 0 && (
              <span className="nw-list-count">{filteredNotes.length}</span>
            )}
          </div>
          <div className="nw-list-actions">
            <div className="nw-sort-wrap">
              <select
                className="nw-sort-select"
                value={sortOrder}
                onChange={function (e) { setSortOrder(e.target.value) }}
                title="Sıralama"
              >
                <option value="updatedDesc">Son Düzenlenen</option>
                <option value="updatedAsc">Eski Düzenlenen</option>
                <option value="titleAsc">Başlık (A-Z)</option>
                <option value="titleDesc">Başlık (Z-A)</option>
                <option value="createdDesc">Oluşturma Tarihi</option>
              </select>
            </div>
            {selectedFolderId !== '__trash' && (
              <button className="nw-list-new-btn" onClick={handleNewNote} title="Yeni not">
                <Plus size={14} />
              </button>
            )}
          </div>
        </div>
        <div className="nw-search-bar">
          <Search size={13} className="nw-search-ico" />
          <input
            className="nw-search-input"
            placeholder="Başlık veya içerik ara..."
            value={searchQuery}
            onChange={function (e) { setSearchQuery(e.target.value) }}
          />
          {searchQuery && (
            <button className="nw-search-clear" onClick={function () { setSearchQuery('') }}>
              <X size={13} />
            </button>
          )}
        </div>
        <div className="nw-list-scroll">
          {filteredNotes.length === 0 ? (
            <div className="nw-empty-state">
              <StickyNote size={36} style={{ opacity: .2 }} />
              <span>
                {selectedFolderId === '__trash'
                  ? (trashLoaded ? 'Çöp kutusu boş' : 'Yükleniyor...')
                  : searchQuery.trim() ? 'Sonuç bulunamadı' : 'Bu klasörde not yok'}
              </span>
            </div>
          ) : (
            sortNotes(filteredNotes, sortOrder)
              .map(function (n) {
                var isTrash = selectedFolderId === '__trash'
                var active = n.id === selectedNoteId
                var folderName = searchQuery.trim()
                  ? (folders.find(function (f) { return f.id === n.folderId }) || {}).name || null
                  : null
                return (
                  <div
                    key={n.id}
                    className={'nw-note-card' + (active ? ' nw-note-card--active' : '') + (n.isPinned && !isTrash ? ' nw-note-card--pinned' : '') + (isTrash ? ' nw-note-card--trash' : '')}
                    onClick={function () { handleSelectNote(n.id) }}
                  >
                    <div className="nw-note-card-top">
                      <h5 className="nw-note-card-title">{n.title || 'Başlıksız Not'}</h5>
                      <div className="nw-note-card-icons">
                        {isTrash ? (
                          <>
                            <button
                              className="nw-pin-btn"
                              onClick={function (e) { e.stopPropagation(); handleRestoreNote(n.id) }}
                              title="Geri Yükle"
                            >
                              <RotateCcw size={12} />
                            </button>
                            <button
                              className="nw-pin-btn nw-pin-btn--danger"
                              onClick={function (e) { e.stopPropagation(); handlePermanentDelete(n.id) }}
                              title="Kalıcı Sil"
                            >
                              <Trash2 size={12} />
                            </button>
                          </>
                        ) : (
                          <>
                            {n.reminderCount > 0 && (
                              <span className="nw-note-reminder-badge" title={n.reminderCount + ' aktif hatirlatici'}>
                                <BellRing size={11} />
                                {n.reminderCount > 1 && <span>{n.reminderCount}</span>}
                              </span>
                            )}
                            <button
                              className={'nw-pin-btn' + (n.isPinned ? ' nw-pin-btn--active' : '')}
                              onClick={function (e) { handleTogglePin(n.id, e) }}
                              title={n.isPinned ? 'Sabitlemeyi kaldir' : 'Sabitle'}
                            >
                              <Pin size={12} />
                            </button>
                            <button
                              className="nw-card-action nw-card-del"
                              title="Notu sil"
                              onClick={function (e) { e.stopPropagation(); handleDeleteNote(n.id) }}
                            >
                              <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><polyline points="3 6 5 6 21 6"/><path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"/><path d="M10 11v6M14 11v6"/><path d="M9 6V4h6v2"/></svg>
                            </button>
                          </>
                        )}
                      </div>
                    </div>
                    <div className="nw-note-card-date">
                      {formatDate(n.updatedAt)}
                      {folderName && <span className="nw-note-card-folder"> · {folderName}</span>}
                    </div>
                    {n.linkedEntityLabel && (
                      <span className="nw-list-entity-badge">{n.linkedEntityLabel}</span>
                    )}
                    <div className="nw-note-card-snippet">{snippet(n.content, 120, n.isFullyEncrypted)}</div>
                  </div>
                )
              })
          )}
        </div>
      </div>

      {/* ═══ Column 3: Editor ═══ */}
      <div className={'nw-col-editor' + (readingMode ? ' nw-reading-mode' : '')}>
        {selectedNote ? (
          <>
            {/* Editor Top Bar — breadcrumb + actions */}
            <div className="nw-editor-topbar">
              <div className="nw-breadcrumb">
                <span
                  className="nw-breadcrumb-folder"
                  onClick={function () {
                    handleSelectFolder(selectedNote.folderId || null)
                  }}
                >
                  <BookOpen size={12} style={{ flexShrink: 0 }} />
                  {selectedNote.folderId
                    ? (folders.find(function (f) { return f.id === selectedNote.folderId }) || {}).name || 'Notlar'
                    : 'Tüm Notlar'}
                </span>
                <ChevronRight size={11} className="nw-breadcrumb-sep" />
                <span className="nw-breadcrumb-note">{selectedNote.title || 'İsimsiz'}</span>
              </div>
              <div className="nw-editor-topbar-right">
                <span className="nw-topbar-date">
                  Düzenlendi {formatDate(selectedNote.updatedAt)}
                </span>
                <button
                  className={'nw-topbar-icon-btn' + (findOpen ? ' nw-topbar-icon-btn--active' : '')}
                  title="Bul / Değiştir (Ctrl+F)"
                  onClick={function () { setFindOpen(function (p) { return !p }) }}
                >
                  <Search size={14} />
                </button>
                <button
                  className={'nw-topbar-icon-btn' + (tocOpen ? ' nw-topbar-icon-btn--active' : '')}
                  title="İçindekiler"
                  onClick={function () { setTocOpen(function (p) { return !p }) }}
                >
                  <List size={14} />
                </button>
                <button
                  className={'nw-topbar-icon-btn' + (readingMode ? ' nw-topbar-icon-btn--active' : '')}
                  title={readingMode ? 'Okuma modundan çık' : 'Okuma modu'}
                  onClick={function () { setReadingMode(function (p) { return !p }) }}
                >
                  {readingMode ? <EyeOff size={14} /> : <Eye size={14} />}
                </button>
                <button
                  className={'nw-topbar-icon-btn' + (focusMode ? ' nw-topbar-icon-btn--active' : '')}
                  title={focusMode ? 'Odak modundan çık (F11 / Esc)' : 'Odak modu (F11)'}
                  onClick={function () { setFocusMode(function (p) { return !p }) }}
                >
                  {focusMode ? <Minimize2 size={14} /> : <Maximize2 size={14} />}
                </button>
                <button
                  className={'nw-topbar-share-btn' + (sharePanelOpen ? ' nw-topbar-share-btn--active' : '')}
                  title="Paylaş"
                  onClick={function () {
                    setSharePanelOpen(true)
                    setShareUserSearch('')
                    setShareUserDropOpen(false)
                    if (selectedNoteId) { loadNoteShares(selectedNoteId); loadCompanyUsers() }
                  }}
                >
                  <Share2 size={14} />
                  <span>Paylaş</span>
                </button>
                <div className="nw-actions-wrap" ref={actionsBtnRef}>
                  <button
                    className={'nw-actions-btn' + (actionsMenuOpen ? ' nw-actions-btn--open' : '')}
                    onClick={function () { setActionsMenuOpen(function (p) { return !p }) }}
                  >
                    <MoreHorizontal size={16} />
                  </button>
                  {actionsMenuOpen && (
                    <NoteActionsMenu
                      onClose={function () { setActionsMenuOpen(false) }}
                      onNoteInfo={function () { setActionsMenuOpen(false); setNoteInfoOpen(true) }}
                      onExportPdf={handleExportPdf}
                      onDelete={function () { setActionsMenuOpen(false); handleDeleteNote() }}
                      onReminders={function () { setActionsMenuOpen(false); setRemindersOpen(true) }}
                      onTogglePin={function () { setActionsMenuOpen(false); handleTogglePin(selectedNoteId) }}
                      onClone={handleCloneNote}
                      reminderCount={reminders.length}
                      isPinned={selectedNote && selectedNote.isPinned}
                      onEncryptWhole={handleOpenEncryptWholeNote}
                      onLockWhole={handleLockFullNote}
                      onRemoveEncryption={handleRemoveFullEncryption}
                      isEncrypted={!!selectedNote && !!selectedNote.isFullyEncrypted}
                      isUnlocked={!!selectedNote && unlockedNoteIds.has(selectedNote.id)}
                      btnRef={actionsBtnRef}
                    />
                  )}
                  {remindersOpen && (
                    <ReminderPopover
                      reminders={reminders}
                      loading={remindersLoading}
                      onClose={function () { setRemindersOpen(false) }}
                      onAdd={handleAddReminder}
                      onDelete={handleDeleteReminder}
                      btnRef={actionsBtnRef}
                    />
                  )}
                </div>
              </div>
            </div>

            {/* Readonly banner — başkasının notu */}
            {selectedNote && selectedNote.isOwner === false && (
              <div className="nw-readonly-banner">
                <Lock size={12} /> Başka bir kullanıcının notu — salt okunur
              </div>
            )}

            {/* Title */}
            <input
              key={selectedNoteId + '-title'}
              className="nw-editor-title"
              placeholder="Başlık"
              defaultValue={selectedNote.title}
              onChange={handleTitleChange}
              readOnly={selectedFolderId === '__trash' || selectedNote.isOwner === false}
            />

            {/* Çöp kutusu: Geri Yükle / Kalıcı Sil */}
            {selectedFolderId === '__trash' && (
              <div className="nw-trash-actions">
                <button className="nw-trash-restore-btn" onClick={function () { handleRestoreNote(selectedNoteId) }}>
                  <RefreshCw size={13} /> Geri Yükle
                </button>
                <button className="nw-trash-del-btn" onClick={function () { handlePermanentDelete(selectedNoteId) }}>
                  <Trash2 size={13} /> Kalıcı Sil
                </button>
              </div>
            )}

            {/* Toolbar */}
            <div className="nw-toolbar">
              {/* Ekle button with dropdown */}
              <div className="nw-insert-wrap" ref={insertBtnRef}>
                <button
                  className={'nw-insert-btn' + (insertMenuOpen ? ' nw-insert-btn--open' : '')}
                  onClick={function () { setInsertMenuOpen(function (p) { return !p }) }}
                >
                  <PlusCircle size={15} />
                  <span>Ekle</span>
                  <ChevronDown size={12} />
                </button>
                <InsertMenu
                  open={insertMenuOpen}
                  onClose={function () { setInsertMenuOpen(false) }}
                  onInsert={handleInsert}
                  btnRef={insertBtnRef}
                />
              </div>
              <div className="nw-toolbar-sep" />
              {/* Undo / Redo */}
              <button className="nw-toolbar-btn" onClick={function () { editor && editor.chain().focus().undo().run() }} title="Geri al">
                <Undo2 size={15} />
              </button>
              <button className="nw-toolbar-btn" onClick={function () { editor && editor.chain().focus().redo().run() }} title="Yinele">
                <Redo2 size={15} />
              </button>
              <div className="nw-toolbar-sep" />
              {/* Text formatting */}
              <button className={tbClass(editor && editor.isActive('bold'))} onClick={function () { editor && editor.chain().focus().toggleBold().run() }} title="Kalin">
                <Bold size={15} />
              </button>
              <button className={tbClass(editor && editor.isActive('italic'))} onClick={function () { editor && editor.chain().focus().toggleItalic().run() }} title="Italik">
                <Italic size={15} />
              </button>
              <button className={tbClass(editor && editor.isActive('underline'))} onClick={function () { editor && editor.chain().focus().toggleUnderline().run() }} title="Alti cizili">
                <UnderlineIcon size={15} />
              </button>
              <button className={tbClass(editor && editor.isActive('strike'))} onClick={function () { editor && editor.chain().focus().toggleStrike().run() }} title="Ustu cizili">
                <Strikethrough size={15} />
              </button>
              <button className={tbClass(editor && editor.isActive('code'))} onClick={function () { editor && editor.chain().focus().toggleCode().run() }} title="Satir ici kod">
                <Code size={15} />
              </button>
              <button className={tbClass(editor && editor.isActive('subscript'))} onClick={function () { editor && editor.chain().focus().toggleSubscript().run() }} title="Alt simge">
                <SubIcon size={15} />
              </button>
              <button className={tbClass(editor && editor.isActive('superscript'))} onClick={function () { editor && editor.chain().focus().toggleSuperscript().run() }} title="Ust simge">
                <SuperIcon size={15} />
              </button>
              <div className="nw-toolbar-sep" />
              {/* Color */}
              <div className="nw-insert-wrap" ref={colorBtnRef}>
                <button
                  className={'nw-toolbar-btn nw-color-trigger' + (colorPickerOpen ? ' nw-toolbar-btn--active' : '')}
                  onClick={function () { setColorPickerOpen(function (p) { return !p }) }}
                  title="Yazi ve arka plan rengi"
                >
                  <span className="nw-color-a">A</span>
                </button>
                <ColorPicker
                  open={colorPickerOpen}
                  onClose={function () { setColorPickerOpen(false) }}
                  editor={editor}
                  btnRef={colorBtnRef}
                />
              </div>
              <div className="nw-toolbar-sep" />
              {/* Headings */}
              <button className={tbClass(editor && editor.isActive('heading', { level: 1 }))} onClick={function () { editor && editor.chain().focus().toggleHeading({ level: 1 }).run() }} title="Baslik 1">
                <Heading1 size={15} />
              </button>
              <button className={tbClass(editor && editor.isActive('heading', { level: 2 }))} onClick={function () { editor && editor.chain().focus().toggleHeading({ level: 2 }).run() }} title="Baslik 2">
                <Heading2 size={15} />
              </button>
              <div className="nw-toolbar-sep" />
              {/* Lists & quote */}
              <button className={tbClass(editor && editor.isActive('bulletList'))} onClick={function () { editor && editor.chain().focus().toggleBulletList().run() }} title="Madde listesi">
                <List size={15} />
              </button>
              <button className={tbClass(editor && editor.isActive('orderedList'))} onClick={function () { editor && editor.chain().focus().toggleOrderedList().run() }} title="Numarali liste">
                <ListOrdered size={15} />
              </button>
              <button className={tbClass(editor && editor.isActive('blockquote'))} onClick={function () { editor && editor.chain().focus().toggleBlockquote().run() }} title="Alinti">
                <Quote size={15} />
              </button>
              <div className="nw-toolbar-sep" />
              {/* Text alignment */}
              <button className={tbClass(editor && editor.isActive({ textAlign: 'left' }))} onClick={function () { editor && editor.chain().focus().setTextAlign('left').run() }} title="Sola hizala">
                <AlignLeft size={15} />
              </button>
              <button className={tbClass(editor && editor.isActive({ textAlign: 'center' }))} onClick={function () { editor && editor.chain().focus().setTextAlign('center').run() }} title="Ortala">
                <AlignCenter size={15} />
              </button>
              <button className={tbClass(editor && editor.isActive({ textAlign: 'right' }))} onClick={function () { editor && editor.chain().focus().setTextAlign('right').run() }} title="Saga hizala">
                <AlignRight size={15} />
              </button>
              <button className={tbClass(editor && editor.isActive({ textAlign: 'justify' }))} onClick={function () { editor && editor.chain().focus().setTextAlign('justify').run() }} title="Iki yana yasla">
                <AlignJustify size={15} />
              </button>
              <div className="nw-toolbar-sep" />
              {/* Sifrele: secim varsa secili bolum, yoksa tum not */}
              <button
                className="nw-toolbar-btn nw-toolbar-btn--encrypt"
                onClick={handleOpenEncryptSelection}
                title={editor && editor.state.selection && !editor.state.selection.empty
                  ? 'Secili metni sifrele'
                  : 'Notu tamamen sifrele'}
              >
                <Lock size={15} />
              </button>
            </div>

            {/* ── Find & Replace Bar ── */}
            {findOpen && (
              <div className="nw-find-bar">
                <Search size={12} className="nw-find-ico" />
                <input
                  ref={findInputRef}
                  className="nw-find-input"
                  placeholder="Ara..."
                  value={findTerm}
                  onChange={function (e) { setFindTerm(e.target.value) }}
                  onKeyDown={function (e) {
                    if (e.key === 'Enter') { e.shiftKey ? handleFindPrev() : handleFindNext() }
                    if (e.key === 'Escape') { setFindOpen(false); setFindTerm('') }
                  }}
                />
                {findMatches.length > 0 && (
                  <span className="nw-find-count">{findMatchIdx + 1}/{findMatches.length}</span>
                )}
                {findTerm && findMatches.length === 0 && (
                  <span className="nw-find-count nw-find-count--none">0 sonuç</span>
                )}
                <button className="nw-find-nav" onClick={handleFindPrev} title="Önceki (Shift+Enter)">
                  <ChevronDown size={12} style={{ transform: 'rotate(180deg)' }} />
                </button>
                <button className="nw-find-nav" onClick={handleFindNext} title="Sonraki (Enter)">
                  <ChevronDown size={12} />
                </button>
                <div className="nw-find-sep" />
                <RotateCcw size={12} className="nw-find-ico" />
                <input
                  className="nw-find-input"
                  placeholder="Değiştir..."
                  value={replaceTerm}
                  onChange={function (e) { setReplaceTerm(e.target.value) }}
                  onKeyDown={function (e) { if (e.key === 'Enter') handleReplaceOne() }}
                />
                <button className="nw-find-action" onClick={handleReplaceOne}>Değiştir</button>
                <button className="nw-find-action" onClick={handleReplaceAll}>Tümünü</button>
                <button className="nw-find-close" onClick={function () { setFindOpen(false); setFindTerm('') }}>
                  <X size={13} />
                </button>
              </div>
            )}

            {/* Mod 2 Lock Screen VEYA Tiptap Editor */}
            {selectedNote.isFullyEncrypted && !unlockedNoteIds.has(selectedNote.id) ? (
              <FullNoteLockScreen
                note={{ id: selectedNote.id, title: selectedNote.title, encryptionHint: selectedNote.encryptionHint }}
                onUnlock={handleUnlockFullNote}
              />
            ) : (
              <div className="nw-editor-scroll-wrap">
                <EditorContent editor={editor} />
              </div>
            )}

            {/* Ekler paneli (bottombar'ın üzerinde açılır) */}
            <AttachmentsPanel
              open={attachmentsOpen}
              onToggle={handleAttachToggle}
              attachments={attachments}
              loading={attachmentsLoading}
              uploadRef={attachUploadRef}
              onUpload={handleAttachUpload}
              onDelete={handleAttachDelete}
            />

            {/* ── TOC Panel (absolute overlay, right side) ── */}
            {tocOpen && tocItems.length > 0 && (
              <div className="nw-toc-panel">
                <div className="nw-toc-head">
                  <span>İçindekiler</span>
                  <button className="nw-toc-close" onClick={function () { setTocOpen(false) }}>
                    <X size={13} />
                  </button>
                </div>
                <div className="nw-toc-list">
                  {tocItems.map(function (item, i) {
                    return (
                      <button
                        key={i}
                        className="nw-toc-item"
                        style={{ paddingLeft: 12 + (item.level - 1) * 12 }}
                        onClick={function () { scrollToHeading(item.text) }}
                      >
                        <span className="nw-toc-level">H{item.level}</span>
                        <span className="nw-toc-text">{item.text}</span>
                      </button>
                    )
                  })}
                </div>
              </div>
            )}

            {/* Bottom Bar */}
            <div className="nw-bottombar">
              <div className="nw-bottombar-left">
                <button
                  className={'nw-bottombar-btn' + (remindersOpen ? ' nw-bottombar-btn--active' : '')}
                  title="Hatırlatıcılar"
                  onClick={function () { setRemindersOpen(function (p) { return !p }) }}
                >
                  {reminders.length > 0 ? <BellRing size={14} /> : <Bell size={14} />}
                  {reminders.length > 0 && <span className="nw-bottombar-badge">{reminders.length}</span>}
                </button>
                <button
                  className={'nw-bottombar-btn' + (attachmentsOpen ? ' nw-bottombar-btn--active' : '')}
                  title="Ekler"
                  onClick={handleAttachToggle}
                >
                  <Paperclip size={14} />
                  {attachments.length > 0 && <span className="nw-bottombar-badge">{attachments.length}</span>}
                </button>
                {/* Görünürlük butonu */}
                {selectedNote && selectedNote.isOwner !== false && (
                  <button
                    className={'nw-bottombar-btn' + (selectedNote.visibility === 1 ? ' nw-bottombar-btn--active' : '')}
                    title={selectedNote.visibility === 1 ? 'Şirket geneline paylaşıldı — tıkla gizle' : 'Özel not — tıkla paylaş'}
                    onClick={handleVisibilityToggle}
                  >
                    {selectedNote.visibility === 1 ? <Globe size={14} /> : <Lock size={14} />}
                  </button>
                )}
                {/* Etiketler */}
                {(selectedNote.tags || []).map(function (tag) {
                  return (
                    <span key={tag} className="nw-tag-pill">
                      {tag}
                      {selectedFolderId !== '__trash' && (
                        <button className="nw-tag-pill-rm" onClick={function () { handleTagRemove(tag) }}>×</button>
                      )}
                    </span>
                  )
                })}
                {selectedFolderId !== '__trash' && (
                  tagInputOpen ? (
                    <input
                      ref={tagInputRef}
                      className="nw-tag-input"
                      placeholder="Etiket..."
                      value={tagInput}
                      onChange={function (e) { setTagInput(e.target.value) }}
                      onKeyDown={function (e) {
                        if (e.key === 'Enter' || e.key === ',') { e.preventDefault(); handleTagAdd(tagInput) }
                        if (e.key === 'Escape') { setTagInputOpen(false); setTagInput('') }
                      }}
                      onBlur={function () { if (tagInput.trim()) handleTagAdd(tagInput); else { setTagInputOpen(false); setTagInput('') } }}
                      autoFocus
                    />
                  ) : (
                    <button
                      className="nw-bottombar-tag-btn"
                      title="Etiket ekle"
                      onClick={function () { setTagInputOpen(true) }}
                    >
                      <Tag size={13} />
                      <span>Etiket ekle</span>
                    </button>
                  )
                )}
              </div>
              <div className="nw-bottombar-right">
                {editor && (
                  <span className="nw-bottombar-chars">
                    {editor.storage.characterCount.words()} kelime
                  </span>
                )}
              </div>
            </div>
          </>
        ) : (
          <div className="nw-editor-empty">
            <StickyNote size={48} className="nw-editor-empty-ico" />
            <span className="nw-editor-empty-title">Not seçilmedi</span>
            <span className="nw-editor-empty-sub">Soldaki listeden bir not seçin ya da yeni not oluşturun</span>
            <button className="nw-editor-empty-btn" onClick={handleNewNote}>
              <Plus size={14} /> Yeni Not
            </button>
          </div>
        )}
      </div>

      </div>{/* /nw-columns-wrap */}

      </>}

      {/* ═══ Klasör Sağ-Tık Menüsü ═══ */}
      {folderCtxMenu && (
        <div
          className="nw-ctx-menu"
          style={{ top: folderCtxMenu.y, left: folderCtxMenu.x }}
          onMouseDown={function (e) { e.stopPropagation() }}
        >
          <button className="nw-ctx-item" onClick={handleCtxRename}>
            <Pencil size={13} /> Yeniden Adlandır
          </button>
          <button className="nw-ctx-item" onClick={handleCtxTrashAll}>
            <Trash size={13} /> Notları Çöp Kutusuna Taşı
          </button>
          <div className="nw-ctx-sep" />
          <button className="nw-ctx-item nw-ctx-item--danger" onClick={handleCtxDelete}>
            <Trash2 size={13} /> Klasörü Sil
          </button>
        </div>
      )}

      {/* ═══ Tablo Sağ-Tık Menüsü ═══ */}
      {tableCtxMenu && editor && (
        <div
          className="nw-table-ctx-menu"
          style={{ top: tableCtxMenu.y, left: tableCtxMenu.x }}
          onMouseDown={function (e) { e.stopPropagation() }}
        >
          <button className="nw-table-ctx-item" onClick={function () { editor.chain().focus().addRowBefore().run(); setTableCtxMenu(null) }}>
            <ArrowUpFromLine size={14} /> Ustune satir ekle
          </button>
          <button className="nw-table-ctx-item" onClick={function () { editor.chain().focus().addRowAfter().run(); setTableCtxMenu(null) }}>
            <ArrowDownFromLine size={14} /> Altina satir ekle
          </button>
          <div className="nw-table-ctx-sep" />
          <button className="nw-table-ctx-item" onClick={function () { editor.chain().focus().addColumnBefore().run(); setTableCtxMenu(null) }}>
            <ArrowLeftFromLine size={14} /> Sola sutun ekle
          </button>
          <button className="nw-table-ctx-item" onClick={function () { editor.chain().focus().addColumnAfter().run(); setTableCtxMenu(null) }}>
            <ArrowRightFromLine size={14} /> Saga sutun ekle
          </button>
          <div className="nw-table-ctx-sep" />
          <button className="nw-table-ctx-item" onClick={function () { editor.chain().focus().toggleHeaderRow().run(); setTableCtxMenu(null) }}>
            <TableProperties size={14} /> Baslik satiri ac/kapat
          </button>
          <div className="nw-table-ctx-sep" />
          <button className="nw-table-ctx-item nw-table-ctx-item--danger" onClick={function () { editor.chain().focus().deleteRow().run(); setTableCtxMenu(null) }}>
            <Trash size={14} /> Satiri sil
          </button>
          <button className="nw-table-ctx-item nw-table-ctx-item--danger" onClick={function () { editor.chain().focus().deleteColumn().run(); setTableCtxMenu(null) }}>
            <Trash size={14} /> Sutunu sil
          </button>
          <button className="nw-table-ctx-item nw-table-ctx-item--danger" onClick={function () { editor.chain().focus().deleteTable().run(); setTableCtxMenu(null) }}>
            <Trash2 size={14} /> Tabloyu sil
          </button>
        </div>
      )}

      {/* ═══ Sifreleme Modallari (Mod 1 + Mod 2) ═══ */}
      <EncryptPromptModal
        open={encryptSelectionOpen}
        title="Secili metni sifrele"
        onCancel={function() { setEncryptSelectionOpen(false) }}
        onSubmit={handleEncryptSelectionSubmit}
      />
      <EncryptPromptModal
        open={encryptWholeNoteOpen}
        title="Notu tamamen sifrele"
        onCancel={function() { setEncryptWholeNoteOpen(false) }}
        onSubmit={handleEncryptWholeNoteSubmit}
      />
      <DecryptPromptModal
        open={!!decryptBlockTarget}
        title="Sifreli bolumu ac"
        hint={decryptBlockTarget ? decryptBlockTarget.hint : null}
        onCancel={function() { setDecryptBlockTarget(null) }}
        onSubmit={handleDecryptBlockSubmit}
      />

      {/* ═══ Not Bilgisi Modal ═══ */}
      {noteInfoOpen && selectedNote && (
        <NoteInfoModal
          note={selectedNote}
          folderName={(folders.find(function (f) { return f.id === selectedNote.folderId }) || {}).name || null}
          currentUserName={currentUserName}
          onClose={function () { setNoteInfoOpen(false) }}
        />
      )}

      {/* ═══ Quick Switcher (Ctrl+P) ═══ */}
      {shortcutsOpen && (
        <ShortcutsModal onClose={function () { setShortcutsOpen(false) }} />
      )}

      {quickSwitcherOpen && (
        <QuickSwitcherModal
          open={quickSwitcherOpen}
          notes={notes}
          folders={folders}
          onSelect={handleQuickSelect}
          onClose={function () { setQuickSwitcherOpen(false) }}
        />
      )}

      {/* ═══ Slash Command Menu (fixed, at cursor) ═══ */}
      {slashMenuOpen && (
        <SlashCommandMenu
          open={slashMenuOpen}
          query={slashQuery}
          pos={slashPos}
          onSelect={handleSlashSelect}
          onClose={function () { setSlashMenuOpen(false); setSlashQuery('') }}
        />
      )}

      {/* ═══ Share Modal ═══ */}
      {sharePanelOpen && (
        <div className="nw-modal-backdrop" onClick={function () { setSharePanelOpen(false) }}>
          <div className="nw-share-modal" onClick={function (e) { e.stopPropagation() }}>
            <div className="nw-share-modal-head">
              <div className="nw-share-modal-title">
                <Share2 size={16} />
                <span>Not Paylaşımı</span>
              </div>
              <button className="nw-share-modal-close-x" onClick={function () { setSharePanelOpen(false) }}>
                <X size={16} />
              </button>
            </div>
            {selectedNote && (
              <div className="nw-share-modal-note-name">
                {selectedNote.title || 'Adsız Not'}
              </div>
            )}
            <div className="nw-share-modal-divider" />
            <div className="nw-share-modal-row">
              <div className="nw-share-modal-row-info">
                <span className="nw-share-modal-row-label">Herkese Açık Bağlantı</span>
                <span className="nw-share-modal-row-desc">
                  {shareIsPublic
                    ? 'Bağlantıya sahip herkes bu notu görüntüleyebilir.'
                    : 'Aktifleştirince herkese açık bir bağlantı oluşturulur.'}
                </span>
              </div>
              <button
                className={'nw-share-toggle' + (shareIsPublic ? ' active' : '')}
                onClick={function () { handleSetSharePublic(!shareIsPublic) }}
                title={shareIsPublic ? 'Paylaşımı kapat' : 'Herkese aç'}
              >
                <span className="nw-share-toggle-track"><span className="nw-share-toggle-thumb" /></span>
              </button>
            </div>
            {shareIsPublic && attachments.length > 0 && (
              <div className="nw-share-modal-row">
                <div className="nw-share-modal-row-info">
                  <span className="nw-share-modal-row-label">
                    <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" style={{marginRight:'5px',verticalAlign:'middle'}}><path d="M21.44 11.05l-9.19 9.19a6 6 0 0 1-8.49-8.49l9.19-9.19a4 4 0 0 1 5.66 5.66L9.41 17.41A2 2 0 0 1 6.59 14.59l8.49-8.49"/></svg>
                    Ekleri de Paylaş
                  </span>
                  <span className="nw-share-modal-row-desc">
                    {shareIncludeAttachments
                      ? attachments.length + ' ek bağlantı üzerinden indirilebilir.'
                      : 'Ziyaretçiler ' + attachments.length + ' eki indirebilsin.'}
                  </span>
                </div>
                <button
                  className={'nw-share-toggle' + (shareIncludeAttachments ? ' active' : '')}
                  onClick={function () { handleSetSharePublic(shareIsPublic, !shareIncludeAttachments) }}
                  title={shareIncludeAttachments ? 'Ek paylaşımını kapat' : 'Ekleri de paylaş'}
                >
                  <span className="nw-share-toggle-track"><span className="nw-share-toggle-thumb" /></span>
                </button>
              </div>
            )}
            {shareIsPublic && shareToken && (
              <div className="nw-share-modal-link-section">
                <div className="nw-share-link-row">
                  <input
                    readOnly
                    className="nw-share-link-input"
                    value={window.location.origin + '/Notes/Public?cid=' + currentCompanyId + '&t=' + shareToken}
                    onFocus={function (e) { e.target.select() }}
                  />
                  <button
                    className={'nw-share-copy-btn' + (shareCopied ? ' copied' : '')}
                    onClick={function () {
                      navigator.clipboard.writeText(window.location.origin + '/Notes/Public?cid=' + currentCompanyId + '&t=' + shareToken)
                      setShareCopied(true)
                      setTimeout(function () { setShareCopied(false) }, 1800)
                    }}
                    title={shareCopied ? 'Kopyalandı!' : 'Kopyala'}
                  >
                    {shareCopied
                      ? <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5"><polyline points="20 6 9 17 4 12"/></svg>
                      : <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><rect x="9" y="9" width="13" height="13" rx="2"/><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/></svg>
                    }
                  </button>
                </div>
              </div>
            )}
            {/* ── Kullanıcı ile Paylaş ──────────────────────────────── */}
            <div className="nw-share-modal-divider" />
            <div className="nw-share-section-header">
              <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/></svg>
              Kullanıcı ile Paylaş
            </div>

            {/* Kullanıcı arama */}
            <div className="nw-share-user-search-wrap">
              <span className="nw-share-user-search-ico">
                <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5"><circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/></svg>
              </span>
              <input
                className="nw-share-user-search"
                placeholder="Kullanıcı ara…"
                value={shareUserSearch}
                onChange={function (e) { setShareUserSearch(e.target.value); setShareUserDropOpen(true) }}
                onFocus={function () { setShareUserDropOpen(true) }}
                onBlur={function () { setTimeout(function () { setShareUserDropOpen(false) }, 180) }}
                autoComplete="off"
              />
              {shareUserDropOpen && (function () {
                var alreadyIds = new Set(userShares.map(function (s) { return s.userId }))
                var q = shareUserSearch.toLowerCase()
                var filtered = companyUsers.filter(function (u) {
                  return !u.isSelf && !alreadyIds.has(u.id)
                    && (u.fullName.toLowerCase().indexOf(q) >= 0 || u.email.toLowerCase().indexOf(q) >= 0)
                })
                if (!filtered.length) return null
                return (
                  <div className="nw-share-user-dropdown">
                    {filtered.map(function (u) {
                      var initials = (u.fullName || '?').split(' ').slice(0, 2).map(function (w) { return w[0] }).join('').toUpperCase()
                      return (
                        <div
                          key={u.id}
                          className="nw-share-user-opt"
                          onMouseDown={function (e) { e.preventDefault(); handleAddUserShare(u.id, false) }}
                        >
                          <div className="nw-share-user-opt-avatar">{initials}</div>
                          <div>
                            <div className="nw-share-user-opt-name">{u.fullName}</div>
                            <div className="nw-share-user-opt-email">{u.email}</div>
                          </div>
                        </div>
                      )
                    })}
                  </div>
                )
              })()}
            </div>

            {/* Paylaşılan kullanıcı listesi */}
            <div className="nw-share-user-list">
              {userShares.length === 0
                ? <div className="nw-share-user-item-empty">Henüz kimseyle paylaşılmadı.</div>
                : userShares.map(function (s) {
                    var initials = (s.fullName || '?').split(' ').slice(0, 2).map(function (w) { return w[0] }).join('').toUpperCase()
                    return (
                      <div key={s.shareId} className="nw-share-user-item">
                        <div className="nw-share-user-item-avatar">{initials}</div>
                        <div className="nw-share-user-item-info">
                          <div className="nw-share-user-item-name">{s.fullName}</div>
                          <div className="nw-share-user-item-email">{s.email}</div>
                        </div>
                        <div className="nw-share-user-item-perm">
                          <span style={{marginRight:'3px'}}>{s.canEdit ? 'Düzenleyebilir' : 'Görüntüleyebilir'}</span>
                          <button
                            className={'nw-share-toggle nw-share-toggle--sm' + (s.canEdit ? ' active' : '')}
                            title={s.canEdit ? 'Düzenlemeyi kapat' : 'Düzenlemeye izin ver'}
                            onClick={function () { handleToggleShareEdit(s.shareId, selectedNoteId, !s.canEdit) }}
                          >
                            <span className="nw-share-toggle-track" style={{width:'30px',height:'18px',borderRadius:'9px'}}>
                              <span className="nw-share-toggle-thumb" style={{width:'12px',height:'12px',top:'3px',left:'3px'}} />
                            </span>
                          </button>
                        </div>
                        <button
                          className="nw-share-user-item-remove"
                          title="Paylaşımı kaldır"
                          onClick={function () { handleRemoveUserShare(s.shareId, selectedNoteId) }}
                        >
                          <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
                        </button>
                      </div>
                    )
                  })
              }
            </div>

            <div className="nw-share-modal-footer">
              <button className="nw-share-modal-close-btn" onClick={function () { setSharePanelOpen(false) }}>
                Kapat
              </button>
            </div>
          </div>
        </div>
      )}

      {/* ═══ Ses Kaydı ═══ */}
      <AudioRecorder
        open={audioRecordOpen}
        onClose={function () { setAudioRecordOpen(false) }}
        noteId={selectedNoteId}
        onAttachmentAdded={function () {
          if (selectedNoteId) {
            api.getAttachments(selectedNoteId)
              .then(function (data) { setAttachments(data || []) })
          }
        }}
      />


      {/* ═══ Kamera ═══ */}
      <CameraCapture
        open={cameraOpen}
        onClose={function () { setCameraOpen(false) }}
        noteId={selectedNoteId}
        onInsertImage={function (dataUrl) {
          if (editor) {
            editor.chain().focus().setImage({ src: dataUrl, alt: 'fotoğraf' }).run()
          }
        }}
        onAttachmentAdded={function () {
          if (selectedNoteId) {
            api.getAttachments(selectedNoteId)
              .then(function (data) { setAttachments(data || []) })
          }
        }}
      />

      {/* ═══ Confirm Modal ═══ */}
      {confirmModal && (
        <div className="nw-modal-backdrop" onClick={function () { setConfirmModal(null) }}>
          <div className="nw-modal-confirm" onClick={function (e) { e.stopPropagation() }}>
            <div className="nw-modal-ico"><Trash2 size={26} /></div>
            <h3 className="nw-modal-title">{confirmModal.title}</h3>
            <p className="nw-modal-text">{confirmModal.text}</p>
            <div className="nw-modal-btns">
              <button className="nw-modal-btn nw-modal-btn--ghost" onClick={function () { setConfirmModal(null) }}>Iptal</button>
              <button className="nw-modal-btn nw-modal-btn--danger" onClick={confirmModal.onConfirm}>
                <Trash2 size={13} /> {confirmModal.okLabel || 'Evet, Sil'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
