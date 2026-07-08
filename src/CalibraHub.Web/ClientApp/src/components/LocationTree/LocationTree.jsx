/**
 * LocationTree — Lokasyon Tanımlamaları kart-ağaç görünümü.
 *
 * CardGroupTree pattern'ı sadeleştirilmiş:
 *  - Tek tree (tab yok)
 *  - Her node: tip + kod + ad + (Makine/Depo) chip'leri + aksiyon butonları
 *  - Inline add (root + child) + edit
 *  - Delete confirm modal
 *  - Maks 7 seviye kırılım
 *  - "Makine Parkuru" / "Depolama Alanı" flag'leri SADECE leaf node'da seçilebilir
 *    (alt kırılım eklendiğinde parent flag'leri otomatik kapanır)
 */
import React, { useState, useEffect, useMemo, useCallback, useRef } from 'react'
import {
  ChevronRight, ChevronDown, MapPin, Plus, PlusCircle,
  Edit2, Trash2, Check, X, Search, AlertTriangle, Cog, Boxes, Settings2,
  Filter, Download, Loader2,
} from 'lucide-react'
import SmartBoardConfigPanel from '../CalibraSmartBoard/SmartBoardConfigPanel'
import SmartBoardFilterPanel, { entityMatchesFilters } from '../CalibraSmartBoard/SmartBoardFilterPanel'
import { loadWidgetConfig } from '../../services/widgetConfigService'

function getCsrf() {
  const el = document.querySelector('input[name="__RequestVerificationToken"]')
  return el ? el.value : ''
}
function toast(msg, kind) {
  if (window.CalibraHub?.toast) window.CalibraHub.toast(msg, kind || 'info')
}

function nodeOrDescendantMatches(node, q) {
  if (!q) return true
  const lq = q.toLowerCase()
  if ((node.code || '').toLowerCase().includes(lq)) return true
  if ((node.name || '').toLowerCase().includes(lq)) return true
  return (node.children || []).some(c => nodeOrDescendantMatches(c, lq))
}
function nodeMatches(node, q) {
  if (!q) return false
  const lq = q.toLowerCase()
  return (node.code || '').toLowerCase().includes(lq) ||
         (node.name || '').toLowerCase().includes(lq)
}

// ── Widget chip — backend'den gelen dynamic widget'lari render eder ────────
//   visibleIds + order: SmartBoardConfigPanel'deki kullanici tercihi.
function WidgetChips({ widgets, visibleIds, order }) {
  if (!Array.isArray(widgets) || widgets.length === 0) return null
  let list = widgets
  if (Array.isArray(visibleIds)) {
    const visSet = new Set(visibleIds)
    list = widgets.filter(w => visSet.has(w.id))
  }
  if (Array.isArray(order) && order.length > 0) {
    const pos = {}
    order.forEach((id, i) => { pos[id] = i })
    list = list.slice().sort((a, b) => {
      const pa = pos[a.id] ?? 999, pb = pos[b.id] ?? 999
      return pa - pb
    })
  }
  if (list.length === 0) return null
  return (
    <>
      {list.map((w, i) => {
        const val = (w.value == null || w.value === '') ? '—' : w.value
        const dt  = (w.dataType || '').toLowerCase()
        const detail = w.detail || (dt === 'currency' ? 'TL' : (dt === 'percent' ? '%' : null))
        const colorCls = w.color ? ' lt-tile--' + w.color : ''
        return (
          <div key={(w.id || 'w') + '_' + i} className={'lt-tile' + colorCls} title={w.label}>
            <span className="lt-tile__label">{w.label}</span>
            <span className="lt-tile__value">
              {val}
              {detail && <span className="lt-tile__detail"> {detail}</span>}
            </span>
          </div>
        )
      })}
    </>
  )
}

// ── Inline form (add/edit) ────────────────────────────────────────────────
// parentTypeSortOrder: parent lokasyonun tipinin sortOrder'i (yoksa null = root).
//   Tip dropdown'u sadece sortOrder > parentTypeSortOrder olan tipleri gosterir.
// childMaxSortOrder: edit edilen node'un altindaki child'lar varsa, en kucuk
//   child sortOrder. Yeni tip bu degerden KUCUK olmali (= olamaz).
function InlineForm({ initial, types, hasChildren, parentTypeSortOrder = null, childMaxSortOrder = null, onSave, onCancel }) {
  // Tip listesini hiyerarsiye gore filtrele
  const allowedTypes = useMemo(() => {
    return (types || []).filter(t => {
      if (parentTypeSortOrder != null && t.sortOrder <= parentTypeSortOrder) return false
      if (childMaxSortOrder != null && t.sortOrder >= childMaxSortOrder) return false
      return true
    })
  }, [types, parentTypeSortOrder, childMaxSortOrder])

  // Mevcut tip artik allowed degilse ilk allowed'a fallback
  const initialType = (() => {
    if (initial?.typeCode && allowedTypes.some(t => t.code === initial.typeCode)) return initial.typeCode
    return allowedTypes[0]?.code || ''
  })()
  const [typeCode, setTypeCode] = useState(initialType)
  const [code, setCode]         = useState(initial?.code || '')
  const [name, setName]         = useState(initial?.name || '')
  const [isMP, setIsMP]         = useState(!!initial?.isMachinePark)
  const [isSA, setIsSA]         = useState(!!initial?.isStorageArea)
  // Eksi bakiye izni — yalnızca depo (isStorageArea) lokasyonlarda anlamlı, iki durumlu:
  // Açık = bu depoda stok eksiye düşebilir · Kapalı = eksiye düşecek hareket engellenir.
  // Şirket ana anahtarı (Eksi Bakiye Kontrolü) kapalıyken bu ayarın etkisi yoktur.
  const [allowNeg, setAllowNeg] = useState(initial?.allowNegativeBalance === true)
  const [saving, setSaving]     = useState(false)
  const codeRef = useRef(null)
  useEffect(() => { codeRef.current?.focus() }, [])

  // Yeni eklenen veya child'i olmayan = leaf → flag'ler aktif
  // Child'i olan node → flag'ler disabled + false (backend zaten zorlar)
  const flagsAllowed = !hasChildren

  const handleSave = async () => {
    const c = code.trim().toUpperCase()
    if (!c) { toast('Kod boş olamaz.', 'err'); return }
    if (!typeCode) { toast('Lokasyon tipi seçin.', 'err'); return }
    setSaving(true)
    try {
      await onSave({
        typeCode, code: c, name: name.trim(),
        isMachinePark: flagsAllowed && isMP,
        isStorageArea: flagsAllowed && isSA,
        // Eksi bakiye ayarı yalnızca depoda saklanır; depo değilse null (ayar yok → varsayılan engelle).
        allowNegativeBalance: (flagsAllowed && isSA) ? allowNeg : null,
      })
    } finally { setSaving(false) }
  }
  const onKey = e => {
    if (e.key === 'Enter') handleSave()
    if (e.key === 'Escape') onCancel()
  }

  return (
    <div className="lt-form" onKeyDown={onKey}>
      <select className="lt-fi lt-fi-type" value={typeCode}
              onChange={e => setTypeCode(e.target.value)}
              disabled={saving || allowedTypes.length === 0}
              title={allowedTypes.length === 0 ? 'Bu seviye için uygun tip yok (hiyerarşi)' : ''}>
        {allowedTypes.length === 0 && <option value="">— Uygun tip yok —</option>}
        {allowedTypes.map(t => <option key={t.code} value={t.code}>{t.name}</option>)}
      </select>
      <input ref={codeRef} className="lt-fi lt-fi-code" value={code}
             onChange={e => setCode(e.target.value.toUpperCase())} placeholder="KOD *"
             maxLength={50} disabled={saving} />
      <input className="lt-fi lt-fi-name" value={name}
             onChange={e => setName(e.target.value)} placeholder="Ad"
             maxLength={100} disabled={saving} />
      <label className={'lt-fi-sw' + (flagsAllowed ? '' : ' is-disabled') + (isMP ? ' is-on' : '')}
             title={flagsAllowed ? 'Makine parkuru' : 'Alt kırılımı olan lokasyon makine parkuru olamaz'}>
        <span className="lt-fi-sw-track" onClick={() => flagsAllowed && !saving && setIsMP(v => !v)}>
          <span className="lt-fi-sw-thumb" />
        </span>
        <Cog size={11} /> Makine Parkuru
      </label>
      <label className={'lt-fi-sw' + (flagsAllowed ? '' : ' is-disabled') + (isSA ? ' is-on' : '')}
             title={flagsAllowed ? 'Fiziksel depo — stok tutulan gerçek ambar' : 'Alt kırılımı olan lokasyon fiziksel depo olamaz'}>
        <span className="lt-fi-sw-track" onClick={() => flagsAllowed && !saving && setIsSA(v => !v)}>
          <span className="lt-fi-sw-thumb" />
        </span>
        <Boxes size={11} /> Fiziksel Depo
      </label>
      {flagsAllowed && isSA && (
        <label className={'lt-fi-sw' + (allowNeg ? ' is-on' : '')}
               title="Eksi bakiye izni — yalnızca şirket ana ayarı (Eksi Bakiye Kontrolü) açıkken etkindir. Açık: bu depoda stok eksiye düşebilir · Kapalı: eksiye düşecek çıkış / transfer / sarf engellenir.">
          <span className="lt-fi-sw-track" onClick={() => !saving && setAllowNeg(v => !v)}>
            <span className="lt-fi-sw-thumb" />
          </span>
          <AlertTriangle size={11} /> Eksi Bakiye İzni
        </label>
      )}
      <button className="lt-fi-ok" onClick={handleSave} disabled={saving} title="Kaydet (Enter)">
        <Check size={13} />
      </button>
      <button className="lt-fi-cancel" onClick={onCancel} disabled={saving} title="İptal (Esc)">
        <X size={13} />
      </button>
    </div>
  )
}

// ── Types yönetim modali (Bölüm Tipleri) ───────────────────────────────────
function TypesModal({ onClose, onChanged }) {
  const [types, setTypes]     = useState([])
  const [loading, setLoading] = useState(false)
  const [editId, setEditId]   = useState(0)        // 0 = yeni
  const [name, setName]       = useState('')
  const [sortOrder, setSort]  = useState(0)
  const [isActive, setActive] = useState(true)
  const [saving, setSaving]   = useState(false)
  const [delTarget, setDel]   = useState(null)     // { id, code, name }
  const nameRef = useRef(null)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const r = await fetch('/Logistics/GetLocationTypes', { credentials: 'same-origin' })
      const d = await r.json()
      setTypes(Array.isArray(d) ? d : [])
    } catch { toast('Tipler yüklenemedi.', 'err') }
    finally { setLoading(false) }
  }, [])

  useEffect(() => { load() }, [load])
  useEffect(() => {
    const h = e => { if (e.key === 'Escape') onClose() }
    window.addEventListener('keydown', h)
    return () => window.removeEventListener('keydown', h)
  }, [onClose])

  const resetForm = () => {
    setEditId(0); setName(''); setSort(0); setActive(true)
    setTimeout(() => nameRef.current?.focus(), 0)
  }
  const startEdit = (t) => {
    setEditId(t.id); setName(t.name || '')
    setSort(t.sortOrder || 0); setActive(!!t.isActive)
    setTimeout(() => nameRef.current?.focus(), 0)
  }

  const save = async () => {
    const n = name.trim()
    if (!n) { toast('Ad boş olamaz.', 'err'); return }
    setSaving(true)
    try {
      const r = await fetch('/Logistics/SaveLocationType', {
        method: 'POST', credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json', RequestVerificationToken: getCsrf() },
        body: JSON.stringify({ id: editId || null, code: null, name: n, sortOrder, isActive }),
      })
      const d = await r.json()
      if (d.success) {
        toast('Kaydedildi.', 'ok')
        resetForm(); await load()
        if (onChanged) onChanged()
      } else {
        toast(d.message || 'Kayıt başarısız.', 'err')
      }
    } catch { toast('Sunucu hatası.', 'err') }
    finally { setSaving(false) }
  }

  const confirmDel = async () => {
    if (!delTarget) return
    try {
      const r = await fetch(`/Logistics/DeleteLocationType?id=${delTarget.id}`, {
        method: 'POST', credentials: 'same-origin',
        headers: { RequestVerificationToken: getCsrf() },
      })
      const d = await r.json()
      if (d.success) {
        toast('Silindi.', 'ok')
        setDel(null)
        if (editId === delTarget.id) resetForm()
        await load()
        if (onChanged) onChanged()
      } else {
        toast(d.message || 'Silinemedi.', 'err'); setDel(null)
      }
    } catch { toast('Sunucu hatası.', 'err'); setDel(null) }
  }

  return (
    <div className="lt-modal-bd" onClick={onClose}>
      <div className="lt-types-modal" onClick={e => e.stopPropagation()}>
        <div className="lt-types-head">
          <Settings2 size={16} />
          <span>Bölüm Tipleri</span>
          <button className="lt-types-close" onClick={onClose} title="Kapat (Esc)">
            <X size={16} />
          </button>
        </div>

        <div className="lt-types-body">
          {/* Sol: form */}
          <div className="lt-types-form">
            <div className="lt-types-form-title">{editId ? 'Tipi Düzenle' : 'Yeni Tip'}</div>
            <label className="lt-types-fl">
              <span>Ad *</span>
              <input ref={nameRef} value={name} maxLength={100}
                     onChange={e => setName(e.target.value)}
                     placeholder="Fabrika" disabled={saving} />
            </label>
            <label className="lt-types-fl">
              <span>Sıra</span>
              <input type="number" value={sortOrder}
                     onChange={e => setSort(parseInt(e.target.value) || 0)}
                     disabled={saving} style={{ width: 80 }} />
            </label>
            <div className="lt-types-fl lt-types-fl-sw">
              <span>Aktif</span>
              <button type="button"
                      className={'lt-sw' + (isActive ? ' is-on' : '')}
                      onClick={() => !saving && setActive(v => !v)}
                      disabled={saving}
                      aria-pressed={isActive}
                      title={isActive ? 'Aktif' : 'Pasif'}>
                <span className="lt-sw-thumb" />
              </button>
            </div>
            <div className="lt-types-form-actions">
              <button className="lt-types-btn-save" onClick={save} disabled={saving}>
                <Check size={13} /> {editId ? 'Güncelle' : 'Ekle'}
              </button>
              {editId !== 0 && (
                <button className="lt-types-btn-cancel" onClick={resetForm} disabled={saving}>
                  Vazgeç
                </button>
              )}
            </div>
          </div>

          {/* Sağ: liste */}
          <div className="lt-types-list">
            {loading ? (
              <div className="lt-types-empty">Yükleniyor…</div>
            ) : types.length === 0 ? (
              <div className="lt-types-empty">Henüz tip tanımlanmamış</div>
            ) : (
              <table className="lt-types-table">
                <thead>
                  <tr>
                    <th>Ad</th>
                    <th style={{ width: 60 }}>Sıra</th>
                    <th style={{ width: 60 }}>Durum</th>
                    <th style={{ width: 70 }}></th>
                  </tr>
                </thead>
                <tbody>
                  {types.map(t => (
                    <tr key={t.id} className={editId === t.id ? 'is-sel' : ''}>
                      <td>{t.name}</td>
                      <td style={{ textAlign: 'center' }}>{t.sortOrder}</td>
                      <td>
                        <span className={'lt-types-status ' + (t.isActive ? 'is-on' : 'is-off')}>
                          {t.isActive ? 'Aktif' : 'Pasif'}
                        </span>
                      </td>
                      <td>
                        <button className="lt-act" title="Düzenle" onClick={() => startEdit(t)}>
                          <Edit2 size={12} />
                        </button>
                        <button className="lt-act lt-act-del" title="Sil"
                                onClick={() => setDel(t)}>
                          <Trash2 size={12} />
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        </div>

        {delTarget && (
          <DeleteModal
            node={{ code: delTarget.code, name: delTarget.name, children: [] }}
            onCancel={() => setDel(null)}
            onConfirm={confirmDel}
            loading={false}
          />
        )}
      </div>
    </div>
  )
}

// ── Delete confirm modal ──────────────────────────────────────────────────
function DeleteModal({ node, onCancel, onConfirm, loading }) {
  useEffect(() => {
    const h = e => { if (e.key === 'Escape') onCancel() }
    window.addEventListener('keydown', h)
    return () => window.removeEventListener('keydown', h)
  }, [onCancel])
  return (
    <div className="lt-modal-bd" onClick={onCancel}>
      <div className="lt-modal" onClick={e => e.stopPropagation()}>
        <div className="lt-modal-icon"><AlertTriangle size={32} /></div>
        <div className="lt-modal-title">Lokasyonu Sil</div>
        <div className="lt-modal-msg">
          <strong>{node.code}</strong>{node.name ? ` — ${node.name}` : ''} silinecek.
          {node.children?.length > 0 && (
            <> Ayrıca <strong>{node.children.length}</strong> alt lokasyon da etkilenir.</>
          )}
          <br />Bu işlem geri alınamaz.
        </div>
        <div className="lt-modal-actions">
          <button className="lt-modal-cancel" onClick={onCancel}>Vazgeç</button>
          <button className="lt-modal-del" onClick={onConfirm} disabled={loading}>
            {loading ? 'Siliniyor…' : 'Sil'}
          </button>
        </div>
      </div>
    </div>
  )
}

// ── Kullanım özeti — stok belge, makine, varlık, malzeme sayıları ─────────
function UsageSummary({ data }) {
  const rows = [
    { label: 'Stok Belgeleri',       count: data.stockDocCount,       samples: data.stockDocSamples,    url: null },
    { label: 'Makineler',            count: data.machineCount,         samples: data.machineSamples,     url: '/Logistics/Machines' },
    { label: 'Varlıklar',            count: data.assetCount,           samples: data.assetSamples,       url: '/Assets/Index' },
    { label: 'Malzeme Lokasyonları', count: data.itemLocationCount,    samples: data.itemLocationSamples, url: '/Logistics/MaterialCards' },
  ].filter(r => r.count > 0)
  if (rows.length === 0) return null
  return (
    <div className="lt-usage-list">
      {rows.map(r => (
        <div key={r.label} className="lt-usage-row">
          {r.url ? (
            <a className="lt-usage-link" href={r.url} target="_blank" rel="noreferrer">
              <span className="lt-usage-label">{r.label}</span>
              <span className="lt-usage-count">{r.count} kayıt</span>
              <span className="lt-usage-arrow">↗</span>
            </a>
          ) : (
            <span className="lt-usage-navi">
              <span className="lt-usage-label">{r.label}</span>
              <span className="lt-usage-count">{r.count} kayıt</span>
            </span>
          )}
          {r.samples?.length > 0 && (
            <span className="lt-usage-samples">({r.samples.filter(Boolean).join(', ')})</span>
          )}
        </div>
      ))}
    </div>
  )
}

// ── Kullanım uyarı modal — silme veya alt-lokasyon ekleme öncesi ──────────
function UsageWarningModal({ type, node, data, onConfirm, onCancel }) {
  useEffect(() => {
    const h = e => { if (e.key === 'Escape') onCancel() }
    window.addEventListener('keydown', h)
    return () => window.removeEventListener('keydown', h)
  }, [onCancel])

  const isDelete = type === 'delete'
  return (
    <div className="lt-modal-bd" onClick={onCancel}>
      <div className="lt-modal lt-modal--usage" onClick={e => e.stopPropagation()}>
        <div className={'lt-modal-icon' + (isDelete ? '' : ' lt-modal-icon--warn')}>
          <AlertTriangle size={32} />
        </div>
        <div className="lt-modal-title">
          {isDelete ? 'Lokasyon Kullanımda' : 'Üst Lokasyon Kullanımda'}
        </div>
        {node && (
          <div className="lt-modal-subtitle">
            <strong>{node.code}</strong>{node.name ? ` — ${node.name}` : ''}
          </div>
        )}
        <div className="lt-modal-msg lt-modal-msg--usage">
          {isDelete
            ? 'Bu lokasyon aşağıdaki kayıtlarda kullanılmaktadır. Lokasyonu silmek bu kayıtlardaki lokasyon bilgisini değiştirmez; yalnızca lokasyon tanımı silinir.'
            : 'Bu lokasyon stok belgelerinde veya diğer kayıtlarda kullanılmaktadır. Alt lokasyon eklendiğinde bu lokasyon artık bir "üst kırılım" konumuna gelecek ve depo / stok seçim listelerinden otomatik olarak kaldırılacaktır.'
          }
        </div>
        <UsageSummary data={data} />
        <div className="lt-modal-actions">
          <button className="lt-modal-cancel" onClick={onCancel}>Vazgeç</button>
          <button className={isDelete ? 'lt-modal-del' : 'lt-modal-ok'} onClick={onConfirm}>
            {isDelete ? 'Yine de Sil' : 'Devam Et'}
          </button>
        </div>
      </div>
    </div>
  )
}

// ── Single tree node ──────────────────────────────────────────────────────
function TreeNode({ node, depth, search, types, handlers, recentIds, maxDepth, parentTypeSortOrder = null, userCfg = null }) {
  const [expanded, setExpanded] = useState(true)
  const hasChildren = (node.children || []).length > 0
  const isEditing      = handlers.editingId === node.id
  const isAddingChild  = handlers.addingFor?.type === 'child'  && handlers.addingFor?.parentId === node.id
  const childrenAtMax  = depth + 1 >= maxDepth
  const isRecent       = recentIds?.has(node.id)

  // Bu node'un kendi tipinin sortOrder'i — child'lara parentTypeSortOrder olarak iletilir
  const myTypeSortOrder = node.typeSortOrder ?? null
  // Eger bu node edit ediliyorsa, alt children'lar arasinda en kucuk sortOrder ne ise
  // yeni tipe izin verilen ust limit (strict <)
  const childMinSortOrder = useMemo(() => {
    const xs = (node.children || []).map(c => c.typeSortOrder).filter(v => typeof v === 'number')
    return xs.length ? Math.min(...xs) : null
  }, [node.children])

  const visibleChildren = useMemo(
    () => (node.children || []).filter(c => nodeOrDescendantMatches(c, search)),
    [node.children, search]
  )
  const forceExpand = !!search
  const isExpanded  = forceExpand || expanded
  const isMatch     = search ? nodeMatches(node, search) : false

  const handleEditSave = useCallback(async (vals) => {
    await handlers.saveNode({
      id: node.id, parentId: node.parentId, ...vals,
    })
  }, [node, handlers])

  const handleAddChildSave = useCallback(async (vals) => {
    await handlers.saveNode({
      id: 0, parentId: node.id, ...vals,
    })
  }, [node, handlers])

  return (
    <div className={'lt-node' + (isRecent ? ' lt-node-new' : '')}>
      {isEditing ? (
        <div className="lt-card lt-card-editing">
          <div className="lt-toggle-ph" />
          <InlineForm
            initial={node}
            types={types}
            hasChildren={hasChildren}
            parentTypeSortOrder={parentTypeSortOrder}
            childMaxSortOrder={childMinSortOrder}
            onSave={handleEditSave}
            onCancel={handlers.cancelEdit}
          />
        </div>
      ) : (
        <div className={'lt-card' + (isMatch ? ' lt-card-match' : '')}>
          {hasChildren ? (
            <button className="lt-toggle" onClick={() => setExpanded(e => !e)}>
              {isExpanded ? <ChevronDown size={13} /> : <ChevronRight size={13} />}
            </button>
          ) : (
            <div className="lt-toggle-ph" />
          )}

          <div className="lt-row__main">
            <span className="lt-code">{node.code}</span>
            <span className="lt-name">{node.name || '—'}</span>
          </div>

          <div className="lt-row__divider" />

          <div className="lt-row__tiles">
            <WidgetChips widgets={node.widgets}
              visibleIds={userCfg?.visibleIds}
              order={userCfg?.order} />
          </div>

          <div className="lt-actions">
            <button className="lt-act" title="Düzenle" onClick={() => handlers.startEdit(node.id)}>
              <Edit2 size={13} />
            </button>
            {!childrenAtMax && (
              <button className="lt-act lt-act-add" title="Alt Lokasyon Ekle"
                      onClick={() => { setExpanded(true); handlers.startAdd({ type: 'child', parentId: node.id }) }}>
                <PlusCircle size={13} />
              </button>
            )}
            <button className="lt-act lt-act-del" title="Sil"
                    onClick={() => handlers.startDelete(node)}>
              <Trash2 size={13} />
            </button>
          </div>
        </div>
      )}

      {(hasChildren || isAddingChild) && isExpanded && (
        <div className="lt-children">
          {isAddingChild && (
            <div className="lt-node">
              <div className="lt-card lt-card-editing">
                <div className="lt-toggle-ph" />
                <InlineForm
                  types={types}
                  hasChildren={false}
                  parentTypeSortOrder={myTypeSortOrder}
                  onSave={handleAddChildSave}
                  onCancel={handlers.cancelAdd}
                />
              </div>
            </div>
          )}
          {visibleChildren.map(child => (
            <TreeNode
              key={child.id}
              node={{ ...child, parentId: node.id }}
              depth={depth + 1}
              search={search}
              types={types}
              handlers={handlers}
              recentIds={recentIds}
              maxDepth={maxDepth}
              parentTypeSortOrder={myTypeSortOrder}
              userCfg={userCfg}
            />
          ))}
        </div>
      )}
    </div>
  )
}

// ── Root component ────────────────────────────────────────────────────────
export default function LocationTree({ config }) {
  const [roots, setRoots]               = useState(config.roots || [])
  const [types, setTypes]               = useState(config.types || [])
  const [search, setSearch]             = useState('')
  const [editingId, setEditingId]       = useState(null)
  const [addingFor, setAddingFor]       = useState(null) // { type:'root'|'child', parentId? }
  const [deleteTarget, setDeleteTarget] = useState(null)
  const [deleting, setDeleting]         = useState(false)
  const [recentIds, setRecentIds]       = useState(() => new Set())
  const [showTypesModal, setShowTypes]  = useState(false)
  const [usageWarning, setUsageWarning]         = useState(null) // { type:'delete'|'addChild', node?, data }
  const [pendingSavePayload, setPendingSavePayload] = useState(null)

  // ── C-Grid standart: widget config + filter + excel ───────────────────────
  const boardKey       = config.boardKey || 'logistics-locations-tree'
  const masterWidgets  = config.masterWidgets || []
  const formCode       = config.formCode || 'LOCATIONS'
  const [configOpen, setConfigOpen] = useState(false)
  const [filterOpen, setFilterOpen] = useState(false)
  const [userCfg, setUserCfg]       = useState(() => loadWidgetConfig(boardKey))
  const [filters, setFilters]       = useState([])
  const [exporting, setExporting]   = useState(false)

  const refreshTree = useCallback(async (highlightId) => {
    try {
      const r = await fetch(config.refreshUrl, { credentials: 'same-origin' })
      if (!r.ok) return
      const data = await r.json()
      setRoots(data.roots || [])
      if (Array.isArray(data.types)) setTypes(data.types)
      if (highlightId != null) {
        setRecentIds(new Set([highlightId]))
        setTimeout(() => setRecentIds(new Set()), 1400)
      }
    } catch { /* sessiz */ }
  }, [config.refreshUrl])

  const saveNode = useCallback(async (payload, opts = {}) => {
    // Yeni child eklenirken üst lokasyonun kullanım kaydı varsa uyar
    if (!opts.skipUsageCheck && payload.id === 0 && payload.parentId != null && config.usageCheckUrl) {
      try {
        const ur = await fetch(`${config.usageCheckUrl}?id=${payload.parentId}`, { credentials: 'same-origin' })
        if (ur.ok) {
          const usage = await ur.json()
          if (usage?.hasUsage) {
            setPendingSavePayload(payload)
            setUsageWarning({ type: 'addChild', data: usage })
            return
          }
        }
      } catch { /* ağ hatası → uyarı atla, kaydet */ }
    }
    const body = {
      id:               payload.id || 0,
      parentId:         payload.parentId ?? null,
      locationTypeCode: payload.typeCode,
      locationCode:     payload.code,
      locationName:     payload.name || null,
      sortOrder:        0,
      isActive:         true,
      isMachinePark:    !!payload.isMachinePark,
      isStorageArea:    !!payload.isStorageArea,
      allowNegativeBalance: payload.allowNegativeBalance ?? null,
      maxWeightCapacity: null,
      volumeCapacity:    null,
    }
    const r = await fetch(config.saveUrl, {
      method: 'POST', credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json', RequestVerificationToken: getCsrf() },
      body: JSON.stringify(body),
    })
    const d = await r.json().catch(() => ({}))
    if (!d.success) {
      toast(d.message || 'Kayıt başarısız.', 'err')
      throw new Error(d.message || 'Kayıt başarısız')
    }
    toast('Kaydedildi.', 'ok')
    setEditingId(null); setAddingFor(null)
    await refreshTree(payload.id || null)
  }, [config.saveUrl, config.usageCheckUrl, refreshTree])

  const confirmDelete = useCallback(async () => {
    if (!deleteTarget) return
    setDeleting(true)
    try {
      const r = await fetch(`${config.deleteUrl}?id=${deleteTarget.id}`, {
        method: 'POST', credentials: 'same-origin',
        headers: { RequestVerificationToken: getCsrf() },
      })
      const d = await r.json()
      if (d.success) { toast('Silindi.', 'ok'); setDeleteTarget(null); await refreshTree() }
      else { toast(d.message || 'Silinemedi.', 'err'); setDeleteTarget(null) }
    } catch { toast('Sunucu hatası.', 'err'); setDeleteTarget(null) }
    finally { setDeleting(false) }
  }, [deleteTarget, config.deleteUrl, refreshTree])

  const confirmAfterUsageWarning = useCallback(async () => {
    if (!usageWarning) return
    const { type, node } = usageWarning
    setUsageWarning(null)
    if (type === 'delete') {
      setDeleteTarget(node)
    } else if (type === 'addChild' && pendingSavePayload) {
      const p = pendingSavePayload
      setPendingSavePayload(null)
      await saveNode(p, { skipUsageCheck: true })
    }
  }, [usageWarning, pendingSavePayload, saveNode])

  const cancelUsageWarning = useCallback(() => {
    setUsageWarning(null)
    setPendingSavePayload(null)
  }, [])

  const handlers = useMemo(() => ({
    editingId, addingFor,
    saveNode,
    startEdit:   id   => { setAddingFor(null); setEditingId(id) },
    cancelEdit:  ()   => setEditingId(null),
    startAdd:    spec => { setEditingId(null); setAddingFor(spec) },
    cancelAdd:   ()   => setAddingFor(null),
    startDelete: async (node) => {
      if (config.usageCheckUrl) {
        try {
          const r = await fetch(`${config.usageCheckUrl}?id=${node.id}`, { credentials: 'same-origin' })
          if (r.ok) {
            const d = await r.json()
            if (d?.hasUsage) { setUsageWarning({ type: 'delete', node, data: d }); return }
          }
        } catch { /* ağ hatası → doğrudan sil modalı */ }
      }
      setDeleteTarget(node)
    },
  }), [editingId, addingFor, saveNode, config.usageCheckUrl])

  // Tum dugumleri duz listeye cevir — filter eslestirmesi + excel export icin
  const flatNodes = useMemo(() => {
    const out = []
    const walk = (n) => {
      out.push(n)
      ;(n.children || []).forEach(walk)
    }
    roots.forEach(walk)
    return out
  }, [roots])

  // Filter eslestirmesi — entityMatchesFilters {id, widgets[]} ister
  const matchedIds = useMemo(() => {
    if (!filters || filters.length === 0) return null
    const set = new Set()
    flatNodes.forEach(n => {
      if (entityMatchesFilters({ id: n.id, widgets: n.widgets || [] }, filters)) set.add(n.id)
    })
    return set
  }, [flatNodes, filters])

  // Filter aktifse: bir node ya kendisi eslesir ya da herhangi bir alt elemani eslesirse gorunur
  const nodeOrDescendantInFilter = useCallback((n) => {
    if (!matchedIds) return true
    if (matchedIds.has(n.id)) return true
    return (n.children || []).some(c => nodeOrDescendantInFilter(c))
  }, [matchedIds])

  const visibleRoots = useMemo(
    () => roots.filter(r => nodeOrDescendantMatches(r, search) && nodeOrDescendantInFilter(r)),
    [roots, search, nodeOrDescendantInFilter]
  )
  const isAddingRoot = addingFor?.type === 'root'
  const handleRootAddSave = useCallback(async (vals) => {
    await saveNode({ id: 0, parentId: null, ...vals })
  }, [saveNode])

  // ── Excel export — duz liste, sistem + dinamik widget kolonlari ──────────
  const handleExportExcel = useCallback(async () => {
    if (exporting) return
    try {
      setExporting(true)
      const visible = (() => {
        const out = []
        const walk = (n, parentLabel) => {
          out.push({ node: n, parentLabel })
          ;(n.children || []).forEach(c => walk(c, n.name || n.code))
        }
        visibleRoots.forEach(r => walk(r, ''))
        return out
      })()
      if (visible.length === 0) { toast('Aktarılacak lokasyon yok.', 'warn'); return }

      const rows = visible.map(({ node, parentLabel }) => {
        const obj = {
          __code: node.code || '',
          __name: node.name || '',
          __parent: parentLabel || '',
        }
        if (Array.isArray(node.widgets)) {
          node.widgets.forEach(w => { if (w && w.id) obj[w.id] = w.value })
        }
        return obj
      })

      const seen = {}
      const widgetCols = []
      masterWidgets.forEach(w => { if (w && w.id && !seen[w.id]) { seen[w.id] = true; widgetCols.push({ id: w.id, label: w.label || w.id }) } })
      visible.forEach(({ node }) => (node.widgets || []).forEach(w => {
        if (w && w.id && !seen[w.id]) { seen[w.id] = true; widgetCols.push({ id: w.id, label: w.label || w.id }) }
      }))

      const headers = [
        { id: '__code',   label: 'Kod' },
        { id: '__name',   label: 'Ad' },
        { id: '__parent', label: 'Üst Kırılım' },
      ].concat(widgetCols)

      const ts = new Date()
      const pad = n => n < 10 ? '0' + n : String(n)
      const stamp = ts.getFullYear() + pad(ts.getMonth()+1) + pad(ts.getDate()) + '_' +
                    pad(ts.getHours()) + pad(ts.getMinutes()) + pad(ts.getSeconds())

      const payload = {
        fileName: 'lokasyonlar_' + stamp + '.xlsx',
        sheetName: 'Lokasyonlar',
        headers, rows,
      }

      const ti = document.querySelector('input[name="__RequestVerificationToken"]')
      const token = ti ? ti.value : ''
      const form = document.createElement('form')
      form.method = 'POST'; form.action = '/api/export/smartboard-excel'
      form.target = '_self'; form.style.display = 'none'
      const hidden = document.createElement('textarea')
      hidden.name = 'payload'; hidden.value = JSON.stringify(payload)
      form.appendChild(hidden)
      if (token) {
        const ti2 = document.createElement('input')
        ti2.type = 'hidden'; ti2.name = '__RequestVerificationToken'; ti2.value = token
        form.appendChild(ti2)
      }
      document.body.appendChild(form)
      form.submit()
      setTimeout(() => { if (form.parentNode) form.parentNode.removeChild(form) }, 1500)
    } catch (e) {
      toast('Aktarma hatası: ' + (e.message || e), 'err')
    } finally {
      setExporting(false)
    }
  }, [exporting, visibleRoots, masterWidgets])

  // Filter panel icin entity formati
  const flatEntities = useMemo(
    () => flatNodes.map(n => ({ id: n.id, title: n.name, subtitle: n.code, widgets: n.widgets || [] })),
    [flatNodes]
  )

  return (
    <div className="lt-root">
      <div className="lt-toolbar">
        <div className="lt-title">
          <MapPin size={15} />
          <span>Lokasyon Tanımlamaları</span>
          <span className="lt-count">{roots.length} kök</span>
        </div>
        <div className="lt-spacer" />
        <div className="lt-search-wrap">
          <Search size={13} className="lt-search-icon" />
          <input className="lt-search" placeholder="Hızlı ara…"
                 value={search} onChange={e => setSearch(e.target.value)} />
        </div>
        <button
          className={'lt-icon-btn' + (filters.length > 0 ? ' lt-icon-btn--active' : '')}
          title={filters.length > 0 ? `${filters.length} filtre aktif` : 'Filtreleme'}
          onClick={() => setFilterOpen(true)}>
          <Filter size={15} />
          {filters.length > 0 && <span className="lt-icon-btn__badge">{filters.length}</span>}
        </button>
        <button className="lt-icon-btn" title={exporting ? 'Aktarılıyor…' : "Excel'e Aktar"}
                onClick={handleExportExcel} disabled={exporting}>
          {exporting ? <Loader2 size={15} className="lt-spin" /> : <Download size={15} />}
        </button>
        <button className="lt-icon-btn" title="Widget Ayarları" onClick={() => setConfigOpen(true)}>
          <Settings2 size={15} />
        </button>
        <button className="lt-btn-types" onClick={() => setShowTypes(true)} title="Bölüm Tiplerini Yönet">
          <Cog size={13} /> Bölüm Tipleri
        </button>
        <button className="lt-btn-new" onClick={() => { setEditingId(null); setAddingFor({ type: 'root' }) }}>
          <Plus size={14} /> Yeni Kök Lokasyon
        </button>
      </div>

      <div className="lt-tree">
        {isAddingRoot && (
          <div className="lt-node">
            <div className="lt-card lt-card-editing">
              <div className="lt-toggle-ph" />
              <InlineForm
                types={types}
                hasChildren={false}
                onSave={handleRootAddSave}
                onCancel={() => setAddingFor(null)}
              />
            </div>
          </div>
        )}

        {visibleRoots.length === 0 && !isAddingRoot ? (
          <div className="lt-empty">
            {search ? `"${search}" için sonuç bulunamadı` : 'Henüz lokasyon tanımlanmamış'}
          </div>
        ) : (
          visibleRoots.map(root => (
            <TreeNode
              key={root.id}
              node={{ ...root, parentId: null }}
              depth={0}
              search={search}
              types={types}
              handlers={handlers}
              recentIds={recentIds}
              maxDepth={config.maxDepth || 7}
              userCfg={userCfg}
            />
          ))
        )}
      </div>

      {usageWarning && (
        <UsageWarningModal
          type={usageWarning.type}
          node={usageWarning.node}
          data={usageWarning.data}
          onConfirm={confirmAfterUsageWarning}
          onCancel={cancelUsageWarning}
        />
      )}

      {deleteTarget && (
        <DeleteModal
          node={deleteTarget}
          onCancel={() => setDeleteTarget(null)}
          onConfirm={confirmDelete}
          loading={deleting}
        />
      )}

      {showTypesModal && (
        <TypesModal
          onClose={() => setShowTypes(false)}
          onChanged={() => refreshTree()}
        />
      )}

      {/* Widget config — tum hiyerarsi seviyeleri ayni boardKey + masterWidgets */}
      <SmartBoardConfigPanel
        isOpen={configOpen}
        onClose={() => setConfigOpen(false)}
        boardKey={boardKey}
        masterWidgets={masterWidgets}
        onSaved={() => setUserCfg(loadWidgetConfig(boardKey))}
      />

      {/* Filter panel — duz liste uzerinden eslestirir, hiyerarsiyi korur */}
      <SmartBoardFilterPanel
        isOpen={filterOpen}
        onClose={() => setFilterOpen(false)}
        boardKey={boardKey}
        formCode={formCode}
        masterWidgets={masterWidgets}
        entities={flatEntities}
        filters={filters}
        onApply={(next) => setFilters(next)}
      />
    </div>
  )
}
