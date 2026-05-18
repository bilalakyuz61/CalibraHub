import { useState, useMemo, useEffect, useCallback } from 'react'
import { Plus, Edit2, Trash2, X, Check, Search, Hammer } from 'lucide-react'
import './OperationGrid.css'

function getCsrf() {
  var el = document.querySelector('input[name="__RequestVerificationToken"]')
  return el ? el.value : ''
}

async function apiPost(url, body) {
  var headers = { Accept: 'application/json', RequestVerificationToken: getCsrf() }
  var opts = { method: 'POST', credentials: 'same-origin', headers }
  if (body !== null && body !== undefined) {
    headers['Content-Type'] = 'application/json'
    opts.body = JSON.stringify(body)
  }
  var res = await fetch(url, opts)
  return res.json()
}

async function apiGet(url) {
  var res = await fetch(url, { credentials: 'same-origin', headers: { Accept: 'application/json' } })
  return res.json()
}

// ── Delete modal ───────────────────────────────────────────────────────────
function DeleteModal({ op, onConfirm, onCancel }) {
  useEffect(() => {
    function onKey(e) { if (e.key === 'Escape') onCancel() }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [onCancel])

  return (
    <div className="og-del-backdrop" onClick={e => { if (e.target === e.currentTarget) onCancel() }}>
      <div className="og-del-modal">
        <div className="og-del-modal__head">
          <div className="og-del-modal__icon">
            <Trash2 size={20} style={{ color: '#ef4444' }} />
          </div>
          <div>
            <div className="og-del-modal__title">Operasyonu Sil</div>
            <div className="og-del-modal__label">{op.code} — {op.name}</div>
          </div>
        </div>
        <p className="og-del-modal__body">
          Bu operasyon silinecektir. Bu operasyonu kullanan rota adımları da etkilenebilir.
        </p>
        <div className="og-del-modal__foot">
          <button className="og-btn og-btn--ghost" onClick={onCancel} autoFocus>Vazgeç</button>
          <button className="og-btn og-btn--danger" onClick={onConfirm}>Sil</button>
        </div>
      </div>
    </div>
  )
}

// ── Inline edit form ───────────────────────────────────────────────────────
function OperationRowForm({ initial, onSave, onCancel, saving }) {
  var [code, setCode]       = useState(initial?.code || '')
  var [name, setName]       = useState(initial?.name || '')
  var [desc, setDesc]       = useState(initial?.description || '')
  var [stdDur, setStdDur]   = useState(initial?.standardDuration ?? '')
  var [durUnit, setDurUnit] = useState(initial?.durationUnit ?? 1)
  var [rate, setRate]       = useState(initial?.hourlyRate ?? '')
  var [sort, setSort]       = useState(initial?.sortOrder ?? 0)
  var [active, setActive]   = useState(initial?.isActive ?? true)

  function submit(e) {
    e.preventDefault()
    if (!code.trim() || !name.trim()) return
    onSave({
      id:               initial?.id || 0,
      code:             code.trim(),
      name:             name.trim(),
      description:      desc.trim() || null,
      standardDuration: stdDur !== '' ? parseFloat(String(stdDur).replace(',', '.')) || null : null,
      durationUnit:     parseInt(durUnit, 10) || 1,
      hourlyRate:       rate !== '' ? parseFloat(String(rate).replace(',', '.')) || null : null,
      sortOrder:        parseInt(sort, 10) || 0,
      isActive:         active,
    })
  }

  return (
    <div className="og-form-row">
      <form onSubmit={submit} className="og-inline-form">
        <input autoFocus className="og-fi og-fi--code" value={code}
          onChange={e => setCode(e.target.value)} placeholder="Kod *" required />
        <input className="og-fi og-fi--name" value={name}
          onChange={e => setName(e.target.value)} placeholder="Ad *" required />
        <input className="og-fi og-fi--desc" value={desc}
          onChange={e => setDesc(e.target.value)} placeholder="Açıklama" />
        <input className="og-fi og-fi--num" type="number" step="0.01" min="0"
          value={stdDur} onChange={e => setStdDur(e.target.value)} placeholder="Süre" />
        <select className="og-fi og-fi--unit" value={durUnit} onChange={e => setDurUnit(e.target.value)}>
          <option value={1}>dk</option>
          <option value={2}>saat</option>
        </select>
        <input className="og-fi og-fi--num" type="number" step="0.01" min="0"
          value={rate} onChange={e => setRate(e.target.value)} placeholder="Saatlik" />
        <input className="og-fi og-fi--sort" type="number" min="0"
          value={sort} onChange={e => setSort(e.target.value)} placeholder="Sıra" />
        <label className="og-fi og-fi--check">
          <input type="checkbox" checked={active} onChange={e => setActive(e.target.checked)} />
          <span>Aktif</span>
        </label>
        <div className="og-fi og-fi--actions">
          <button type="button" onClick={onCancel} className="og-act og-act--cancel" title="Vazgeç">
            <X size={14} />
          </button>
          <button type="submit" disabled={saving} className="og-act og-act--save" title="Kaydet">
            {saving ? '…' : <Check size={14} />}
          </button>
        </div>
      </form>
    </div>
  )
}

// ── Operation display row ──────────────────────────────────────────────────
function OperationRow({ op, onEdit, onDelete }) {
  var durLabel = op.durationUnit === 2 ? 'saat' : 'dk'

  return (
    <div className={`og-row${op.isActive ? '' : ' og-row--passive'}`}>
      <div className="og-row__main">
        <span className="og-row__code">{op.code}</span>
        <span className="og-row__name">{op.name}</span>
        {op.description && <span className="og-row__desc">{op.description}</span>}
      </div>

      <div className="og-row__tiles">
        <div className="og-tile">
          <span className="og-tile__label">Std. Süre</span>
          <span className="og-tile__value">
            {op.standardDuration != null
              ? <>{Number(op.standardDuration).toLocaleString('tr-TR', { maximumFractionDigits: 2 })}<span className="og-tile__unit"> {durLabel}</span></>
              : <span className="og-tile__muted">—</span>}
          </span>
        </div>
        <div className="og-tile">
          <span className="og-tile__label">Saatlik</span>
          <span className="og-tile__value">
            {op.hourlyRate != null
              ? <>{Number(op.hourlyRate).toLocaleString('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}<span className="og-tile__unit"> TL</span></>
              : <span className="og-tile__muted">—</span>}
          </span>
        </div>
        <div className={`og-badge ${op.isActive ? 'og-badge--active' : 'og-badge--passive'}`}>
          {op.isActive ? 'Aktif' : 'Pasif'}
        </div>
      </div>

      <div className="og-row__actions">
        <button className="og-act og-act--edit" title="Düzenle" onClick={onEdit}>
          <Edit2 size={13} />
        </button>
        <button className="og-act og-act--del" title="Sil" onClick={onDelete}>
          <Trash2 size={13} />
        </button>
      </div>
    </div>
  )
}

// ── OperationGrid — ana bileşen ────────────────────────────────────────────
export default function OperationGrid({ config }) {
  var urls = config.urls || {}

  var [ops, setOps]             = useState(config.operations || [])
  var [search, setSearch]       = useState('')
  var [addingNew, setAddingNew] = useState(false)
  var [editingId, setEditingId] = useState(null)
  var [deleteTarget, setDelete] = useState(null)
  var [saving, setSaving]       = useState(false)

  var filtered = useMemo(() => {
    if (!search) return ops
    var q = search.toLowerCase()
    return ops.filter(o =>
      (o.code || '').toLowerCase().includes(q) ||
      (o.name || '').toLowerCase().includes(q)
    )
  }, [ops, search])

  var refresh = useCallback(async () => {
    try {
      var data = await apiGet(urls.refresh)
      setOps(data.operations || [])
    } catch { /* sessiz */ }
  }, [urls.refresh])

  async function handleSave(formData) {
    setSaving(true)
    try {
      var res = await apiPost(urls.save, formData)
      if (res.ok) {
        await refresh()
        setAddingNew(false)
        setEditingId(null)
      } else {
        window.CalibraHub?.toast(res.error || 'Kayıt hatası', 'err')
      }
    } finally { setSaving(false) }
  }

  async function handleDelete(op) {
    setSaving(true)
    try {
      var res = await apiPost(`${urls.delete}/${op.id}`, null)
      if (res.ok) {
        setOps(prev => prev.filter(o => o.id !== op.id))
      } else {
        window.CalibraHub?.toast(res.error || 'Silme hatası', 'err')
      }
    } finally { setSaving(false); setDelete(null) }
  }

  return (
    <div className="og-root">

      {/* ── Header ── */}
      <div className="og-header">
        <div className="og-header__id">
          <div className="og-header__icon"><Hammer size={17} /></div>
          <div>
            <div className="og-header__title">Operasyon Tanımlamaları</div>
            <div className="og-header__sub">{filtered.length} operasyon</div>
          </div>
        </div>
        <div className="og-header__search">
          <Search size={13} />
          <input value={search} onChange={e => setSearch(e.target.value)}
            placeholder="Kod veya ada göre ara..." />
          {search && (
            <button className="og-header__clear" onClick={() => setSearch('')}><X size={12} /></button>
          )}
        </div>
        <button className="og-btn og-btn--primary"
          onClick={() => { setAddingNew(true); setEditingId(null) }}>
          <Plus size={14} /> Yeni Operasyon
        </button>
      </div>

      {/* ── List ── */}
      <div className="og-list">

        {addingNew && (
          <OperationRowForm
            onSave={handleSave}
            onCancel={() => setAddingNew(false)}
            saving={saving}
          />
        )}

        {filtered.length === 0 && !addingNew && (
          <div className="og-empty">
            <Hammer size={32} />
            <span>{search ? 'Arama sonucu bulunamadı' : 'Henüz operasyon tanımlanmamış'}</span>
          </div>
        )}

        {filtered.map(op =>
          editingId === op.id ? (
            <OperationRowForm
              key={op.id}
              initial={op}
              onSave={handleSave}
              onCancel={() => setEditingId(null)}
              saving={saving}
            />
          ) : (
            <OperationRow
              key={op.id}
              op={op}
              onEdit={() => { setEditingId(op.id); setAddingNew(false) }}
              onDelete={() => setDelete(op)}
            />
          )
        )}
      </div>

      {/* ── Delete modal ── */}
      {deleteTarget && (
        <DeleteModal
          op={deleteTarget}
          onCancel={() => setDelete(null)}
          onConfirm={() => handleDelete(deleteTarget)}
        />
      )}
    </div>
  )
}
