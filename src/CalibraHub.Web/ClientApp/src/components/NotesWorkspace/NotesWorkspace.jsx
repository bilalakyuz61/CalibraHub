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
import { EncryptPromptModal, DecryptPromptModal, FullNoteLockScreen } from './EncryptionModals'
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
  Trash, TableProperties, Pin, ArrowUpDown, Bell, BellRing, X
} from 'lucide-react'
import * as api from '../../services/notesService'

var lowlight = createLowlight(common)

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
  { section: 'Bloklar' },
  { id: 'h3',    label: 'Kucuk baslik',   icon: Heading3,    action: 'heading3' },
  { id: 'check', label: 'Onay kutusu',    icon: CheckSquare, action: 'taskList' },
  { id: 'table', label: 'Tablo',          icon: TableIcon,   action: 'table' },
  { id: 'hr',    label: 'Ayirici',        icon: Minus,       action: 'hr' },
  { id: 'code',  label: 'Kod blogu',      icon: Code,        action: 'codeBlock' },
  { section: 'Medya' },
  { id: 'image', label: 'Resim',          icon: ImagePlus,   action: 'image' },
  { id: 'youtube',label:'YouTube video',  icon: YoutubeIcon,  action: 'youtube' },
  { section: 'Diger' },
  { id: 'link',  label: 'Baglanti',       icon: LinkIcon,    action: 'link' },
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
              <span className="nw-insert-item-label">{item.label}</span>
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
   NoteActionsMenu — "..." dropdown (PDF export, delete etc.)
   ══════════════════════════════════════════════════════════ */
function NoteActionsMenu(props) {
  var onClose = props.onClose
  var onExportPdf = props.onExportPdf
  var onDelete = props.onDelete
  var onReminders = props.onReminders              // Hatirlaticilar popover'ini ac
  var reminderCount = props.reminderCount || 0     // Aktif hatirlatici sayisi (badge icin)
  var onEncryptWhole = props.onEncryptWhole       // Mod 2: Tum notu sifrele
  var onLockWhole = props.onLockWhole             // Mod 2: Acikken kilitle
  var onRemoveEncryption = props.onRemoveEncryption // Mod 2: Sifrelemeyi kaldir
  var isEncrypted = !!props.isEncrypted            // Not Mod 2 sifreli mi?
  var isUnlocked = !!props.isUnlocked              // Mod 2 acikken true
  var btnRef = props.btnRef

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
      <button className="nw-actions-item" onClick={onReminders}>
        {reminderCount > 0 ? <BellRing size={15} /> : <Bell size={15} />}
        <span>Hatirlatici{reminderCount > 0 ? ' (' + reminderCount + ')' : ''}</span>
      </button>
      <div className="nw-actions-sep" />
      <button className="nw-actions-item" onClick={onExportPdf}>
        <FileDown size={15} />
        <span>PDF olarak aktar</span>
      </button>
      <div className="nw-actions-sep" />
      {!isEncrypted && (
        <button className="nw-actions-item" onClick={onEncryptWhole}>
          <Lock size={15} />
          <span>Notu tamamen sifrele</span>
        </button>
      )}
      {isEncrypted && isUnlocked && (
        <>
          <button className="nw-actions-item" onClick={onLockWhole}>
            <Lock size={15} />
            <span>Notu tekrar kilitle</span>
          </button>
          <button className="nw-actions-item" onClick={onRemoveEncryption}>
            <Unlock size={15} />
            <span>Sifrelemeyi kaldir</span>
          </button>
        </>
      )}
      {isEncrypted && !isUnlocked && (
        <button className="nw-actions-item" disabled style={{ opacity: 0.55, cursor: 'not-allowed' }}>
          <Lock size={15} />
          <span>Not sifreli — ac, sonra ayar gorunur</span>
        </button>
      )}
      <div className="nw-actions-sep" />
      <button className="nw-actions-item nw-actions-item--danger" onClick={onDelete}>
        <Trash2 size={15} />
        <span>Notu sil</span>
      </button>
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
  var contentTimerRef = useRef(null)
  var isSwitchingNoteRef = useRef(false)
  var selectedNoteIdRef = useRef(null)
  selectedNoteIdRef.current = selectedNoteId

  var filteredNotes = selectedFolderId === '__trash'
    ? []
    : selectedFolderId
      ? notes.filter(function (n) { return n.folderId === selectedFolderId })
      : notes

  var selectedNote = notes.find(function (n) { return n.id === selectedNoteId }) || null

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
    ],
    editorProps: {
      attributes: {
        class: 'nw-editor-content',
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
      if (contentTimerRef.current) clearTimeout(contentTimerRef.current)
      contentTimerRef.current = setTimeout(async function () {
        var html = ctx.editor.getHTML()
        var noteId = selectedNoteIdRef.current
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
          // Kullanici sifreleme anindan hemen sonra editor temizlendiginde
          // bu autosave tetiklenebilir ve gercek sifreli icerigi bos bir
          // sifreli payload ile uzerine yazabilir.
          var plainText = (html || '').replace(/<[^>]*>/g, '').trim()
          if (!plainText) return
          var pw = recallPassword('note-' + noteId)
          if (!pw) {
            // Cache expired or not unlocked; skip save (kullanici onceki parolayi girmeli)
            return
          }
          try {
            finalContent = await encryptText(html, pw)
          } catch (e) {
            console.error('Mod 2 auto-encrypt failed:', e); return
          }
        }
        setNotes(function (prev) {
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

  /* Load data from backend */
  useEffect(function () {
    api.getAll()
      .then(function (data) {
        var flds = (data.folders || []).map(function (f) {
          return { id: f.id, name: f.name, parentId: f.parentId || null }
        })
        var nts = (data.notes || []).map(function (n) {
          return {
            id: n.id,
            folderId: n.folderId || null,
            title: n.title || '',
            content: n.content || '',
            updatedAt: new Date(n.updatedAt),
            isPinned: !!n.isPinned,
            isFullyEncrypted: !!n.isFullyEncrypted,
            encryptionHint: n.encryptionHint || null,
            reminderCount: n.reminderCount || 0,
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

  /* Sync Tiptap editor when switching notes */
  useEffect(function () {
    if (editor && selectedNote) {
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
          isSwitchingNoteRef.current = false
        }
      } else {
        var currentContent = editor.getHTML()
        if (currentContent !== selectedNote.content) {
          isSwitchingNoteRef.current = true
          editor.commands.setContent(selectedNote.content || '', false)
          isSwitchingNoteRef.current = false
        }
      }
    } else if (editor && !selectedNote) {
      isSwitchingNoteRef.current = true
      editor.commands.setContent('', false)
      isSwitchingNoteRef.current = false
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedNoteId, editor, unlockedNoteIds])

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
    setFolderCtxMenu(null)
    setConfirmModal({
      title: 'Klasörü Sil',
      text: 'Bu klasör ve içindeki tüm notlar kalıcı olarak silinecek.',
      onConfirm: function () {
        setConfirmModal(null)
        var ids = getDescendantIds(folders, fid)
        api.deleteFolder(fid).catch(function (e) { console.error(e) })
        setFolders(function (prev) { return prev.filter(function (f) { return ids.indexOf(f.id) === -1 }) })
        setNotes(function (prev) { return prev.filter(function (n) { return ids.indexOf(n.folderId) === -1 }) })
        if (selectedFolderId && ids.indexOf(selectedFolderId) !== -1) setSelectedFolderId(null)
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

  var handleSelectNote = useCallback(function (nid) {
    // Not degisirken pending autosave'i hemen iptal et —
    // eski notun plaintext/sifreli icerigi yeni notun icerigi ile karismasin
    if (contentTimerRef.current) { clearTimeout(contentTimerRef.current); contentTimerRef.current = null }
    setSelectedNoteId(nid)
  }, [])

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
          title: '', content: '', updatedAt: new Date(),
        }
        setNotes(function (prev) { return [newNote].concat(prev) })
        setSelectedNoteId(res.id)
      })
      .catch(function (e) { console.error('[NotesWorkspace] saveNote error:', e) })
  }, [selectedFolderId])

  var doDeleteNote = useCallback(function () {
    if (!selectedNoteId) return
    var delId = selectedNoteId
    api.deleteNote(delId).catch(function (e) { console.error(e) })
    setNotes(function (prev) {
      return prev.filter(function (n) { return n.id !== delId })
    })
    setSelectedNoteId(function () {
      var remaining = filteredNotes.filter(function (n) { return n.id !== delId })
      return remaining.length > 0 ? remaining[0].id : null
    })
    setConfirmModal(null)
  }, [selectedNoteId, filteredNotes])

  var handleDeleteNote = useCallback(function () {
    if (!selectedNoteId) return
    var note = notes.find(function (n) { return n.id === selectedNoteId })
    setConfirmModal({
      title: 'Çöp Kutusuna Taşı',
      text: '"' + (note ? note.title || 'Başlıksız Not' : 'Not') + '" çöp kutusuna taşınacak. Devam edilsin mi?',
      onConfirm: doDeleteNote,
    })
  }, [selectedNoteId, notes, doDeleteNote])

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
          alert('Hatirlatici eklenemedi: ' + (r && r.message ? r.message : 'Bilinmeyen hata.'))
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
      .catch(function (e) { alert('Hatirlatici eklenemedi: ' + e.message) })
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
      // Kullaniciya inline bir popup / alert ile goster
      alert('🔓 Sifreli icerik:\n\n' + plaintext)
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
        alert('Sifreleme sirasinda dogrulama basarisiz oldu. Veri degistirilmedi. Lutfen tekrar deneyin.')
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
      alert('Sifreleme hatasi: ' + (e.message || e))
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
      case 'heading3':
        editor.chain().focus().toggleHeading({ level: 3 }).run()
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
        var imgUrl = prompt('Resim URL:')
        if (imgUrl) editor.chain().focus().setImage({ src: imgUrl }).run()
        break
      }
      case 'youtube': {
        var ytUrl = prompt('YouTube URL:')
        if (ytUrl) editor.chain().focus().setYoutubeVideo({ src: ytUrl }).run()
        break
      }
      default:
        break
    }
  }, [editor])

  /* ── Toolbar helper ───────────────────────────────── */
  function tbClass(isActive) {
    return 'nw-toolbar-btn' + (isActive ? ' nw-toolbar-btn--active' : '')
  }

  /* ── Render ───────────────────────────────────────── */
  return (
    <div className="w-full h-full flex flex-row overflow-hidden font-sans text-sm text-slate-800 dark:text-slate-200">

      {loading && (
        <div className="nw-loading">
          <Loader2 size={24} className="nw-spin" />
          <span>Yukleniyor...</span>
        </div>
      )}

      {!loading && <>
      {/* ═══ Column 1: Folders ═══ */}
      <div className="nw-col-folders">
        <div className="nw-folders-header">
          <h4 className="nw-folders-title">Klasorler</h4>
          <button className="nw-folder-add-btn" title="Yeni klasor" onClick={handleNewFolder}>
            <Plus size={13} /> Yeni
          </button>
        </div>
        <div className="nw-folders-scroll">
          <div
            className={'nw-folder-item nw-folder-item--all' + (selectedFolderId === null ? ' nw-folder-item--active' : '')}
            onClick={function () { handleSelectFolder(null) }}
          >
            <span style={{ width: 18, flexShrink: 0 }} />
            <FileText size={15} className="nw-folder-ico" />
            <span className="nw-folder-label">Tum Notlar</span>
            <span className="nw-folder-count">{notes.length}</span>
          </div>

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

          <div className="nw-folder-trash-wrap">
            <div
              className={'nw-folder-item nw-folder-item--trash' + (selectedFolderId === '__trash' ? ' nw-folder-item--active-trash' : '')}
              onClick={function () { handleSelectFolder('__trash') }}
            >
              <span style={{ width: 18, flexShrink: 0 }} />
              <Trash2 size={15} className="nw-folder-ico" />
              <span className="nw-folder-label">Cop Kutusu</span>
            </div>
          </div>
        </div>
      </div>

      {/* ═══ Column 2: Note List ═══ */}
      <div className="nw-col-list">
        <div className="nw-list-header">
          <h4 className="nw-list-title">
            {selectedFolderId === '__trash'
              ? 'Cop Kutusu'
              : selectedFolderId
                ? (folders.find(function (f) { return f.id === selectedFolderId }) || {}).name || 'Notlar'
                : 'Tum Notlar'}
            {filteredNotes.length > 0 && (
              <span className="nw-list-count">({filteredNotes.length})</span>
            )}
          </h4>
          <div className="nw-list-actions">
            <div className="nw-sort-wrap">
              <select
                className="nw-sort-select"
                value={sortOrder}
                onChange={function (e) { setSortOrder(e.target.value) }}
                title="Siralama"
              >
                <option value="updatedDesc">Son Düzenlenen</option>
                <option value="updatedAsc">Eski Düzenlenen</option>
                <option value="titleAsc">Başlık (A-Z)</option>
                <option value="titleDesc">Başlık (Z-A)</option>
                <option value="createdDesc">Oluşturma Tarihi</option>
              </select>
            </div>
            <button className="nw-new-note-btn" onClick={handleNewNote}>
              <Plus size={13} /> Yeni Not
            </button>
          </div>
        </div>
        <div className="nw-list-scroll">
          {filteredNotes.length === 0 ? (
            <div className="nw-empty-state">
              <StickyNote size={36} style={{ opacity: .2 }} />
              <span>Bu klasorde not yok</span>
            </div>
          ) : (
            sortNotes(filteredNotes, sortOrder)
              .map(function (n) {
                var active = n.id === selectedNoteId
                return (
                  <div
                    key={n.id}
                    className={'nw-note-card' + (active ? ' nw-note-card--active' : '') + (n.isPinned ? ' nw-note-card--pinned' : '')}
                    onClick={function () { handleSelectNote(n.id) }}
                  >
                    <div className="nw-note-card-top">
                      <h5 className="nw-note-card-title">{n.title || 'Başlıksız Not'}</h5>
                      <div className="nw-note-card-icons">
                        {n.reminderCount > 0 && (
                          <span
                            className="nw-note-reminder-badge"
                            title={n.reminderCount + ' aktif hatirlatici'}
                          >
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
                      </div>
                    </div>
                    <div className="nw-note-card-date">{formatDate(n.updatedAt)}</div>
                    <div className="nw-note-card-snippet">{snippet(n.content, 120, n.isFullyEncrypted)}</div>
                  </div>
                )
              })
          )}
        </div>
      </div>

      {/* ═══ Column 3: Editor ═══ */}
      <div className="nw-col-editor">
        {selectedNote ? (
          <>
            {/* Meta bar */}
            <div className="nw-editor-meta">
              <span>Son duzenleme: {formatTime(selectedNote.updatedAt)}</span>
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
                    onExportPdf={handleExportPdf}
                    onDelete={function () { setActionsMenuOpen(false); handleDeleteNote() }}
                    onReminders={function () { setActionsMenuOpen(false); setRemindersOpen(true) }}
                    reminderCount={reminders.length}
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

            {/* Title */}
            <input
              key={selectedNoteId + '-title'}
              className="nw-editor-title"
              placeholder="Basliksiz Not"
              defaultValue={selectedNote.title}
              onChange={handleTitleChange}
            />

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

            {/* Character count */}
            {editor && (
              <div className="nw-char-count">
                {editor.storage.characterCount.characters()} karakter · {editor.storage.characterCount.words()} kelime
              </div>
            )}
          </>
        ) : (
          <div className="nw-editor-empty">
            <StickyNote size={52} style={{ opacity: .12 }} />
            <span>Bir not secin veya yeni bir not olusturun</span>
          </div>
        )}
      </div>

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
          <div className="nw-ctx-sep" />
          <button className="nw-ctx-item nw-ctx-item--danger" onClick={handleCtxDelete}>
            <Trash2 size={13} /> Sil
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
                <Trash2 size={13} /> Evet, Sil
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
