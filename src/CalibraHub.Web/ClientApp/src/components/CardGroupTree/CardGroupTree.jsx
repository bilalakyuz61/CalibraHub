import React, { useState, useMemo, useCallback, useRef, useEffect } from 'react'
import {
  Package, Building2, Search,
  Edit2, Trash2, ChevronRight, ChevronDown, ChevronsDown, Minus,
  AlertTriangle, Layers, Plus, PlusCircle, Check, X,
  GitBranch, Target,
} from 'lucide-react'
import './CardGroupTree.css'

const ICON_MAP = { Package, Building2, Layers }

function getCsrf() {
  const el = document.querySelector('input[name="__RequestVerificationToken"]')
  return el ? el.value : ''
}

function toast(msg, kind) {
  window.CalibraHub?.toast?.(msg, kind)
}

// Collects all nodes at a specific level from a nested tree
function collectAtLevel(nodes, targetLevel) {
  const result = []
  function traverse(ns) {
    for (const n of ns) {
      if (n.level === targetLevel) result.push(n)
      else if (n.level < targetLevel) traverse(n.children || [])
    }
  }
  traverse(nodes || [])
  return result
}

function findNodeById(nodes, id) {
  for (const n of nodes || []) {
    if (n.id === id) return n
    const found = findNodeById(n.children, id)
    if (found) return found
  }
  return null
}

function computeSuggestedCode(nodes) {
  if (!nodes || nodes.length === 0) return ''
  const parsed = nodes
    .map(n => { const m = n.code.match(/^(.*?)(\d+)$/); return m ? { prefix: m[1], num: parseInt(m[2], 10), width: m[2].length } : null })
    .filter(Boolean)
  if (parsed.length === 0) return ''
  const freq = {}
  for (const p of parsed) freq[p.prefix] = (freq[p.prefix] || 0) + 1
  const top = Object.entries(freq).sort((a, b) => b[1] - a[1])[0][0]
  const best = parsed.filter(p => p.prefix === top).reduce((a, b) => a.num > b.num ? a : b)
  return top + String(best.num + 1).padStart(best.width, '0')
}

function nodeOrDescendantMatches(node, q) {
  if (!q) return true
  const lq = q.toLowerCase()
  if (node.code.toLowerCase().includes(lq)) return true
  if ((node.description || '').toLowerCase().includes(lq)) return true
  return (node.children || []).some(c => nodeOrDescendantMatches(c, lq))
}
function nodeMatches(node, q) {
  if (!q) return false
  const lq = q.toLowerCase()
  return node.code.toLowerCase().includes(lq) ||
    (node.description || '').toLowerCase().includes(lq)
}

// ── Inline add/edit form ─────────────────────────────────────────────────────
function InlineForm({ onSave, onCancel, initial }) {
  const [code, setCode] = useState(initial?.code || '')
  const [desc, setDesc] = useState(initial?.description || '')
  const [saving, setSaving] = useState(false)
  const codeRef = useRef(null)

  useEffect(() => { codeRef.current?.focus() }, [])

  const handleKeyDown = e => {
    if (e.key === 'Enter') handleSave()
    if (e.key === 'Escape') onCancel()
  }

  const handleSave = async () => {
    const trimmed = code.trim().toUpperCase()
    if (!trimmed) { toast('Grup kodu boş olamaz.', 'err'); return }
    setSaving(true)
    try {
      await onSave(trimmed, desc.trim())
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="cgt-inline-form" onKeyDown={handleKeyDown}>
      <input
        ref={codeRef}
        className="cgt-if-input cgt-if-code"
        value={code}
        onChange={e => setCode(e.target.value.toUpperCase())}
        placeholder="KOD"
        maxLength={20}
        disabled={saving}
      />
      <input
        className="cgt-if-input cgt-if-desc"
        value={desc}
        onChange={e => setDesc(e.target.value)}
        placeholder="Açıklama (opsiyonel)"
        maxLength={200}
        disabled={saving}
      />
      <button className="cgt-if-btn cgt-if-ok" onClick={handleSave} disabled={saving} title="Kaydet (Enter)">
        <Check size={13} />
      </button>
      <button className="cgt-if-btn cgt-if-cancel" onClick={onCancel} disabled={saving} title="İptal (Esc)">
        <X size={13} />
      </button>
    </div>
  )
}

// ── Delete modal ─────────────────────────────────────────────────────────────
function DeleteModal({ node, onConfirm, onCancel, loading }) {
  useEffect(() => {
    const handler = e => { if (e.key === 'Escape') onCancel() }
    window.addEventListener('keydown', handler)
    return () => window.removeEventListener('keydown', handler)
  }, [onCancel])

  return (
    <div className="cgt-modal-backdrop" onClick={onCancel}>
      <div className="cgt-modal" onClick={e => e.stopPropagation()}>
        <div className="cgt-modal-icon"><AlertTriangle size={36} /></div>
        <div className="cgt-modal-title">Grubu Sil</div>
        <div className="cgt-modal-msg">
          <strong>{node.code}</strong>
          {node.description ? ` — ${node.description}` : ''}
          {' '}grubunu silmek istediğinize emin misiniz?
        </div>
        <div className="cgt-modal-actions">
          <button className="cgt-modal-cancel" onClick={onCancel}>Vazgeç</button>
          <button className="cgt-modal-del" onClick={onConfirm} disabled={loading}>
            {loading ? 'Siliniyor…' : 'Sil'}
          </button>
        </div>
      </div>
    </div>
  )
}

// ── Single tree node ─────────────────────────────────────────────────────────
function TreeNode({ node, cardType, activeColor, depth, search, handlers, recentIds, maxLevel, siblingNodes }) {
  const [expanded, setExpanded] = useState(true)
  const childrenAllowed = node.level < (maxLevel ?? 5)
  const hasChildren = childrenAllowed && node.children && node.children.length > 0
  const isEditing   = handlers.editingId === node.id
  const isAddingChild = handlers.addingFor?.type === 'child' && handlers.addingFor?.nodeId === node.id
  const isAddingSibling = handlers.addingFor?.type === 'sibling' && handlers.addingFor?.nodeId === node.id
  const isRecent  = recentIds?.has(node.id)
  const isPinned  = handlers.focusedId === node.id

  const visibleChildren = useMemo(
    () => childrenAllowed ? (node.children || []).filter(c => nodeOrDescendantMatches(c, search)) : [],
    [node.children, search, childrenAllowed]
  )
  const forceExpand = !!search
  const isExpanded = forceExpand || expanded
  const isMatch = search ? nodeMatches(node, search) : false

  // Save inline edit
  const handleEditSave = useCallback(async (code, desc) => {
    await handlers.saveGroup({
      id: node.id, cardType: node.cardType, level: node.level,
      parentId: node.parentId, code, description: desc,
    })
  }, [node, handlers])

  // Save add-child
  const handleAddChildSave = useCallback(async (code, desc) => {
    await handlers.saveGroup({
      cardType: cardType.id, level: node.level + 1,
      parentId: node.id, code, description: desc,
    })
  }, [node, cardType, handlers])

  // Save add-sibling
  const handleAddSiblingSave = useCallback(async (code, desc) => {
    await handlers.saveGroup({
      cardType: node.cardType, level: node.level,
      parentId: node.parentId ?? null, code, description: desc,
    })
  }, [node, handlers])

  const canAddChild = node.level < 5 && childrenAllowed

  return (
    <div className={`cgt-node${isRecent ? ' cgt-node-new' : ''}`}>
      {/* Sibling inline form — appears above this card */}
      {isAddingSibling && (
        <div className="cgt-inline-form-row">
          <div className="cgt-toggle-ph" />
          <InlineForm
            initial={{ code: handlers.addingFor?.suggestedCode || '' }}
            onSave={handleAddSiblingSave}
            onCancel={handlers.cancelAdd}
          />
        </div>
      )}

      {/* Card row */}
      <div className="cgt-card-row">
        {isEditing ? (
          /* Edit mode — card becomes a form */
          <div className="cgt-card cgt-card-editing">
            <div className="cgt-toggle-ph" />
            <InlineForm
              initial={{ code: node.code, description: node.description }}
              onSave={handleEditSave}
              onCancel={handlers.cancelEdit}
            />
          </div>
        ) : (
          <div
            className={[
              'cgt-card',
              isMatch  ? (activeColor === 'teal' ? 'cgt-card-match-teal' : 'cgt-card-match') : '',
              isPinned ? 'cgt-card-pinned' : '',
            ].filter(Boolean).join(' ')}
          >
            {hasChildren ? (
              <button className="cgt-toggle" onClick={() => setExpanded(e => !e)}>
                {isExpanded ? <ChevronDown size={13} /> : <ChevronRight size={13} />}
              </button>
            ) : (
              <div className="cgt-toggle-ph" />
            )}

            <span className="cgt-code">{node.code}</span>
            {node.description && <span className="cgt-desc">{node.description}</span>}
            <span className={`cgt-badge cgt-badge-${activeColor}`}>Sv.{node.level}</span>

            <div className="cgt-actions">
              {/* Düzenle */}
              <button
                className="cgt-action-btn"
                title="Düzenle"
                onClick={() => handlers.startEdit(node.id)}
              >
                <Edit2 size={13} />
              </button>

              {/* Alt grup ekle */}
              {canAddChild && (
                <button
                  className="cgt-action-btn cgt-action-btn-add"
                  title="Alt Grup Ekle"
                  onClick={() => {
                    setExpanded(true)
                    handlers.startAdd({ type: 'child', nodeId: node.id, suggestedCode: computeSuggestedCode(node.children) })
                  }}
                >
                  <PlusCircle size={13} />
                </button>
              )}

              {/* Paralel grup ekle */}
              <button
                className="cgt-action-btn cgt-action-btn-add"
                title="Paralel Grup Ekle (Aynı seviye)"
                onClick={() => handlers.startAdd({ type: 'sibling', nodeId: node.id, suggestedCode: computeSuggestedCode(siblingNodes) })}
              >
                <GitBranch size={13} />
              </button>

              {/* Odakla */}
              <button
                className={`cgt-action-btn cgt-action-btn-pin${isPinned ? ' cgt-action-btn-pin-active' : ''}`}
                title={isPinned ? 'Odağı kaldır' : 'Bu gruba odaklan'}
                onClick={() => isPinned ? handlers.clearPin() : handlers.pinNode(node)}
              >
                <Target size={13} />
              </button>

              {/* Sil */}
              <button
                className="cgt-action-btn cgt-action-btn-del"
                title="Sil"
                onClick={() => handlers.startDelete(node)}
              >
                <Trash2 size={13} />
              </button>
            </div>
          </div>
        )}
      </div>

      {/* Children */}
      {(hasChildren || isAddingChild) && isExpanded && (
        <div className="cgt-children">
          {/* Add-child inline form at top of children */}
          {isAddingChild && (
            <div className="cgt-node">
              <div className="cgt-card-row">
                <div className="cgt-card cgt-card-editing">
                  <div className="cgt-toggle-ph" />
                  <InlineForm
                    initial={{ code: handlers.addingFor?.suggestedCode || '' }}
                    onSave={handleAddChildSave}
                    onCancel={handlers.cancelAdd}
                  />
                </div>
              </div>
            </div>
          )}
          {visibleChildren.map(child => (
            <TreeNode
              key={child.id}
              node={{ ...child, cardType: cardType.id }}
              cardType={cardType}
              activeColor={activeColor}
              depth={depth + 1}
              search={search}
              handlers={handlers}
              recentIds={recentIds}
              maxLevel={maxLevel}
              siblingNodes={node.children}
            />
          ))}
        </div>
      )}
    </div>
  )
}

// ── Root component ───────────────────────────────────────────────────────────
export default function CardGroupTree({ config }) {
  const [activeTypeIdx, setActiveTypeIdx] = useState(0)
  const [search, setSearch]               = useState('')
  const [deleteTarget, setDeleteTarget]   = useState(null)
  const [deleting, setDeleting]           = useState(false)
  const [editingId, setEditingId]         = useState(null)
  const [addingFor, setAddingFor]         = useState(null) // { type: 'child'|'sibling'|'root', nodeId }
  const [cardTypes, setCardTypes]         = useState(config.cardTypes)
  const [recentIds, setRecentIds]         = useState(() => new Set())
  const [rootLevel, setRootLevel]         = useState(1)
  const [subMode, setSubMode]             = useState('none') // 'none' | 'one' | 'all'
  const [focusedNode, setFocusedNode]     = useState(null)   // { id, code, description }

  const cardType = cardTypes[activeTypeIdx]

  const maxLevel = subMode === 'all' ? 5 : subMode === 'one' ? rootLevel + 1 : rootLevel

  const rootLevelNodes = useMemo(() => {
    if (focusedNode) {
      const found = findNodeById(cardType.roots, focusedNode.id)
      return found ? [found] : []
    }
    return collectAtLevel(cardType.roots, rootLevel)
  }, [cardType.roots, rootLevel, focusedNode])

  const visibleRoots = useMemo(
    () => rootLevelNodes.filter(n => nodeOrDescendantMatches(n, search)),
    [rootLevelNodes, search]
  )

  // ── In-place tree refresh ───────────────────────────────────────────────
  const refreshTree = useCallback(async (highlightId) => {
    try {
      const resp = await fetch('/Definitions/GetCardGroupsJson')
      if (!resp.ok) return
      const data = await resp.json()
      setCardTypes(data.cardTypes)
      if (highlightId != null) {
        setRecentIds(new Set([highlightId]))
        setTimeout(() => setRecentIds(new Set()), 1200)
      }
    } catch {
      // silent — tree still shows previous state
    }
  }, [])

  // ── Save (add or edit) ──────────────────────────────────────────────────
  const saveGroup = useCallback(async (payload) => {
    const resp = await fetch('/Definitions/SaveCardGroup', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        RequestVerificationToken: getCsrf(),
      },
      body: JSON.stringify(payload),
    })
    if (!resp.ok) {
      const err = await resp.json().catch(() => ({}))
      toast(err.error || 'Kayıt başarısız.', 'err')
      throw new Error(err.error || 'Kayıt başarısız.')
    }
    const data = await resp.json().catch(() => ({}))
    toast('Kaydedildi.', 'ok')
    setEditingId(null)
    setAddingFor(null)
    await refreshTree(data.id ?? null)
  }, [refreshTree])

  // ── Delete ──────────────────────────────────────────────────────────────
  const confirmDelete = useCallback(async () => {
    if (!deleteTarget) return
    setDeleting(true)
    try {
      const resp = await fetch(`${config.deleteApiUrl}${deleteTarget.id}`, {
        method: 'POST',
        headers: { RequestVerificationToken: getCsrf() },
      })
      const data = await resp.json()
      if (data.success) {
        toast('Grup silindi.', 'ok')
        setDeleteTarget(null)
        await refreshTree(null)
      } else {
        toast(data.message || 'Silinemedi.', 'err')
        setDeleteTarget(null)
      }
    } catch {
      toast('Sunucu hatası.', 'err')
      setDeleteTarget(null)
    } finally {
      setDeleting(false)
    }
  }, [deleteTarget, config.deleteApiUrl, refreshTree])

  // ── Save root-level add ─────────────────────────────────────────────────
  const handleRootAddSave = useCallback(async (code, desc) => {
    await saveGroup({
      cardType: cardType.id, level: 1,
      parentId: null, code, description: desc,
    })
  }, [cardType.id, saveGroup])  // eslint-disable-line react-hooks/exhaustive-deps

  // ── Handlers object (avoids re-creating per node) ───────────────────────
  const handlers = useMemo(() => ({
    editingId,
    addingFor,
    focusedId: focusedNode?.id ?? null,
    saveGroup,
    startEdit:   id   => { setAddingFor(null); setEditingId(id) },
    cancelEdit:  ()   => setEditingId(null),
    startAdd:    spec => { setEditingId(null); setAddingFor(spec) },
    cancelAdd:   ()   => setAddingFor(null),
    startDelete: node => setDeleteTarget(node),
    pinNode:     node => setFocusedNode({ id: node.id, code: node.code, description: node.description }),
    clearPin:    ()   => setFocusedNode(null),
  }), [editingId, addingFor, focusedNode, saveGroup])

  const handleTabClick = idx => {
    setActiveTypeIdx(idx)
    setSearch('')
    setEditingId(null)
    setAddingFor(null)
    setRootLevel(1)
    setSubMode('none')
    setFocusedNode(null)
  }

  const isAddingRoot = addingFor?.type === 'root'

  return (
    <div className="cgt-root">
      {/* Toolbar */}
      <div className="cgt-toolbar">
        <div className="cgt-tabs">
          {cardTypes.map((ct, i) => {
            const IconComp = ICON_MAP[ct.icon] || Layers
            return (
              <button
                key={ct.id}
                className={`cgt-tab${activeTypeIdx === i ? ` tab-active-${ct.color}` : ''}`}
                onClick={() => handleTabClick(i)}
              >
                <IconComp size={15} />
                {ct.name}
                <span className="cgt-badge" style={{ marginLeft: 2 }}>
                  {(ct.roots || []).length}
                </span>
              </button>
            )
          })}
        </div>

        {!focusedNode && <span className="cgt-toolbar-sep" />}

        {/* Level + sublevel controls — hidden in focus mode */}
        {!focusedNode && <>
          <div className="cgt-seg-group">
            <Layers size={13} className="cgt-seg-prefix-icon" />
            <div className="cgt-seg" role="group" aria-label="Başlangıç seviyesi">
              {[1, 2, 3, 4, 5].map(lv => (
                <button
                  key={lv}
                  className={`cgt-seg-item${rootLevel === lv ? ' cgt-seg-item-active' : ''}`}
                  onClick={() => { setRootLevel(lv); if (lv === 5) setSubMode('none') }}
                  title={`Seviye ${lv}'den başla`}
                >
                  {lv}
                </button>
              ))}
            </div>
          </div>

          {rootLevel < 5 && (
            <div className="cgt-seg" role="group" aria-label="Alt seviye görünümü">
              {[
                { key: 'none', icon: <Minus size={13} />,        label: 'Bu',   title: 'Yalnızca bu seviye' },
                { key: 'one',  icon: <ChevronDown size={13} />,  label: '+1',   title: 'Bir alt seviye dahil' },
                { key: 'all',  icon: <ChevronsDown size={13} />, label: 'Tümü', title: 'Tüm alt seviyeler' },
              ].map(opt => (
                <button
                  key={opt.key}
                  className={`cgt-seg-item${subMode === opt.key ? ' cgt-seg-item-active' : ''}`}
                  onClick={() => setSubMode(opt.key)}
                  title={opt.title}
                >
                  {opt.icon}
                  {opt.label}
                </button>
              ))}
            </div>
          )}
        </>}

        <div className="cgt-spacer" />

        <div className="cgt-search-wrap">
          <span className="cgt-search-icon"><Search size={14} /></span>
          <input
            className="cgt-search"
            type="text"
            placeholder="Hızlı ara…"
            value={search}
            onChange={e => setSearch(e.target.value)}
          />
        </div>

        {rootLevel === 1 && (
          <button
            className="cgt-btn-new"
            onClick={() => { setEditingId(null); setAddingFor({ type: 'root', suggestedCode: computeSuggestedCode(rootLevelNodes) }) }}
          >
            <Plus size={14} />
            Yeni Kök Grup
          </button>
        )}
      </div>

      {/* Focus banner */}
      {focusedNode && (
        <div className="cgt-focus-banner">
          <Target size={13} className="cgt-focus-banner-icon" />
          <span className="cgt-focus-banner-label">
            <strong>{focusedNode.code}</strong>
            {focusedNode.description && <span> — {focusedNode.description}</span>}
          </span>
          <button className="cgt-focus-banner-clear" onClick={() => setFocusedNode(null)} title="Odağı kaldır">
            <X size={13} />
            Odağı kaldır
          </button>
        </div>
      )}

      {/* Tree */}
      <div className="cgt-tree-area">
        {/* Root-level add form */}
        {isAddingRoot && (
          <div className="cgt-node">
            <div className="cgt-card-row">
              <div className="cgt-card cgt-card-editing">
                <div className="cgt-toggle-ph" />
                <InlineForm
                  initial={{ code: addingFor?.suggestedCode || '' }}
                  onSave={handleRootAddSave}
                  onCancel={() => setAddingFor(null)}
                />
              </div>
            </div>
          </div>
        )}

        {visibleRoots.length === 0 && !isAddingRoot ? (
          <div className="cgt-empty">
            {search ? `"${search}" için sonuç bulunamadı` : 'Henüz grup tanımlanmamış'}
          </div>
        ) : (
          visibleRoots.map(root => (
            <TreeNode
              key={root.id}
              node={{ ...root, cardType: cardType.id, parentId: root.parentId ?? null }}
              cardType={cardType}
              activeColor={cardType.color}
              depth={0}
              search={search}
              handlers={handlers}
              recentIds={recentIds}
              maxLevel={maxLevel}
              siblingNodes={rootLevelNodes}
            />
          ))
        )}
      </div>

      {/* Delete modal */}
      {deleteTarget && (
        <DeleteModal
          node={deleteTarget}
          onConfirm={confirmDelete}
          onCancel={() => setDeleteTarget(null)}
          loading={deleting}
        />
      )}
    </div>
  )
}
