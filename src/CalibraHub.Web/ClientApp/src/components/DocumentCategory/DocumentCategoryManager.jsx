import React, { useState, useEffect, useCallback, useMemo, useRef } from 'react'
import { FolderTree, Tag, Plus, Edit2, Trash2, Check, X, Loader2, Search } from 'lucide-react'
import './DocumentCategoryManager.css'

// ── Yardımcılar ────────────────────────────────────────────────────────────
function getCsrf() {
  var el = document.querySelector('input[name="__RequestVerificationToken"]')
  return el ? el.value : ''
}

function toast(msg, kind) {
  if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast(msg, kind)
}

// Silme onay standardı — global showConfirm (wwwroot/js/confirm-modal.js), Promise<boolean>.
// Native confirm() sadece yükleme başarısız olursa (script yüklenmemişse) son çare fallback.
function confirmDialog(opts) {
  if (typeof window.showConfirm === 'function') return window.showConfirm(opts)
  return Promise.resolve(window.confirm(opts.message))
}

async function postJson(url, body) {
  var resp = await fetch(url, {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', RequestVerificationToken: getCsrf() },
    body: JSON.stringify(body),
  })
  var data = await resp.json().catch(function () { return {} })
  if (!resp.ok || data.ok === false || data.success === false) {
    throw new Error(data.message || data.error || 'İşlem başarısız.')
  }
  return data
}

async function postDelete(url, id) {
  var resp = await fetch(url + '?id=' + id, {
    method: 'POST',
    credentials: 'same-origin',
    headers: { RequestVerificationToken: getCsrf() },
  })
  var data = await resp.json().catch(function () { return {} })
  if (!resp.ok || data.ok === false || data.success === false) {
    throw new Error(data.message || data.error || 'Silinemedi.')
  }
  return data
}

function normalizeList(data, key) {
  if (Array.isArray(data)) return data
  if (data && Array.isArray(data[key])) return data[key]
  if (data && Array.isArray(data.items)) return data.items
  return []
}

// ── Inline satır formu (ekle/düzenle) — yalnız "Ad" girilir, kod alanı yok ──
function RowForm({ initial, onSave, onCancel }) {
  const [name, setName] = useState(initial || '')
  const [saving, setSaving] = useState(false)
  const inputRef = useRef(null)

  useEffect(function () { inputRef.current && inputRef.current.focus() }, [])

  const handleSave = useCallback(function () {
    const trimmed = name.trim()
    if (!trimmed) { toast('Ad boş olamaz.', 'err'); return }
    setSaving(true)
    Promise.resolve(onSave(trimmed))
      .catch(function (e) { toast(e.message || 'Kaydetme başarısız.', 'err') })
      .finally(function () { setSaving(false) })
  }, [name, onSave])

  const handleKeyDown = function (e) {
    if (e.key === 'Enter') handleSave()
    if (e.key === 'Escape') onCancel()
  }

  return (
    <div className="dcm-row dcm-row-form" onKeyDown={handleKeyDown}>
      <input
        ref={inputRef}
        className="dcm-row-input"
        value={name}
        onChange={function (e) { setName(e.target.value) }}
        placeholder="Ad"
        maxLength={200}
        disabled={saving}
      />
      <button className="dcm-row-btn dcm-row-btn-ok" onClick={handleSave} disabled={saving} title="Kaydet (Enter)">
        <Check size={13} />
      </button>
      <button className="dcm-row-btn" onClick={onCancel} disabled={saving} title="İptal (Esc)">
        <X size={13} />
      </button>
    </div>
  )
}

// ── Switch (Aktif/Pasif) — native checkbox yerine track+thumb ──────────────
function ActiveSwitch({ checked, onToggle }) {
  return (
    <label className="dcm-switch-wrap" onClick={function (e) { e.stopPropagation() }} title={checked ? 'Aktif — pasifleştir' : 'Pasif — aktifleştir'}>
      <input type="checkbox" className="dcm-switch-input" checked={checked} onChange={onToggle} />
      <span className={'dcm-switch-track' + (checked ? ' is-on' : '')}>
        <span className="dcm-switch-thumb" />
      </span>
    </label>
  )
}

// ── Kök bileşen ──────────────────────────────────────────────────────────────
// config: { categories?, categoriesUrl, typesUrl, saveUrl, deleteUrl }
export default function DocumentCategoryManager({ config }) {
  config = config || {}
  const categoriesUrl = config.categoriesUrl || '/DocumentCategory/Categories'
  const typesUrl      = config.typesUrl      || '/DocumentCategory/Types'
  const saveUrl        = config.saveUrl       || '/DocumentCategory/Save'
  const deleteUrl       = config.deleteUrl     || '/DocumentCategory/Delete'

  const [categories, setCategories]     = useState(config.categories || [])
  const [loadingCats, setLoadingCats]   = useState(!(config.categories && config.categories.length))
  const [selectedId, setSelectedId]     = useState(null)
  const [types, setTypes]               = useState([])
  const [loadingTypes, setLoadingTypes] = useState(false)
  const [search, setSearch]             = useState('')

  const [catEditingId, setCatEditingId] = useState(null)
  const [catAdding, setCatAdding]       = useState(false)
  const [typeEditingId, setTypeEditingId] = useState(null)
  const [typeAdding, setTypeAdding]     = useState(false)

  const loadCategories = useCallback(function () {
    setLoadingCats(true)
    fetch(categoriesUrl, { credentials: 'same-origin' })
      .then(function (r) { return r.json() })
      .then(function (data) { setCategories(normalizeList(data, 'categories')) })
      .catch(function () { toast('Kategoriler yüklenemedi.', 'err') })
      .finally(function () { setLoadingCats(false) })
  }, [categoriesUrl])

  useEffect(function () {
    loadCategories()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const loadTypes = useCallback(function (catId) {
    if (catId == null) { setTypes([]); return }
    setLoadingTypes(true)
    fetch(typesUrl + '?parentId=' + catId, { credentials: 'same-origin' })
      .then(function (r) { return r.json() })
      .then(function (data) { setTypes(normalizeList(data, 'types')) })
      .catch(function () { toast('Tipler yüklenemedi.', 'err') })
      .finally(function () { setLoadingTypes(false) })
  }, [typesUrl])

  useEffect(function () {
    loadTypes(selectedId)
  }, [selectedId, loadTypes])

  // İlk yüklemede otomatik ilk kategoriyi seç
  useEffect(function () {
    if (selectedId == null && categories.length > 0) setSelectedId(categories[0].id)
  }, [categories, selectedId])

  const selectedCategory = useMemo(function () {
    return categories.filter(function (c) { return c.id === selectedId })[0] || null
  }, [categories, selectedId])

  const filteredCategories = useMemo(function () {
    if (!search) return categories
    const q = search.toLowerCase()
    return categories.filter(function (c) { return (c.name || '').toLowerCase().indexOf(q) !== -1 })
  }, [categories, search])

  // ── Kategori (L1) kaydet ──────────────────────────────────────────────
  const saveCategory = useCallback(function (id, name) {
    const existing = id ? categories.filter(function (c) { return c.id === id })[0] : null
    const payload = {
      id: id || 0,
      parentId: null,
      name: name,
      sortOrder: existing ? existing.sortOrder : categories.length,
      isActive: existing ? existing.isActive : true,
    }
    return postJson(saveUrl, payload).then(function (data) {
      toast('Kategori kaydedildi.', 'ok')
      setCatEditingId(null); setCatAdding(false)
      loadCategories()
      if (!id && data && data.id != null) setSelectedId(data.id)
    })
  }, [categories, saveUrl, loadCategories])

  // ── Tip (L2) kaydet ─────────────────────────────────────────────────────
  const saveType = useCallback(function (id, name) {
    if (selectedId == null) return Promise.resolve()
    const existing = id ? types.filter(function (t) { return t.id === id })[0] : null
    const payload = {
      id: id || 0,
      parentId: selectedId,
      name: name,
      sortOrder: existing ? existing.sortOrder : types.length,
      isActive: existing ? existing.isActive : true,
    }
    return postJson(saveUrl, payload).then(function () {
      toast('Tip kaydedildi.', 'ok')
      setTypeEditingId(null); setTypeAdding(false)
      loadTypes(selectedId)
    })
  }, [types, selectedId, saveUrl, loadTypes])

  const toggleCategoryActive = useCallback(function (cat) {
    postJson(saveUrl, { id: cat.id, parentId: null, name: cat.name, sortOrder: cat.sortOrder, isActive: !cat.isActive })
      .then(function () {
        toast(cat.isActive ? 'Kategori pasifleştirildi.' : 'Kategori aktifleştirildi.', 'ok')
        loadCategories()
      })
      .catch(function (e) { toast(e.message, 'err') })
  }, [saveUrl, loadCategories])

  const toggleTypeActive = useCallback(function (t) {
    postJson(saveUrl, { id: t.id, parentId: selectedId, name: t.name, sortOrder: t.sortOrder, isActive: !t.isActive })
      .then(function () {
        toast(t.isActive ? 'Tip pasifleştirildi.' : 'Tip aktifleştirildi.', 'ok')
        loadTypes(selectedId)
      })
      .catch(function (e) { toast(e.message, 'err') })
  }, [saveUrl, selectedId, loadTypes])

  const deleteCategory = useCallback(function (cat) {
    confirmDialog({
      title: 'Kategoriyi Sil',
      message: '"' + cat.name + '" kategorisini silmek istediğinize emin misiniz? Altındaki tüm tipler de etkilenir.',
      okLabel: 'Sil', cancelLabel: 'Vazgeç', danger: true,
    }).then(function (ok) {
      if (!ok) return
      postDelete(deleteUrl, cat.id).then(function () {
        toast('Kategori silindi.', 'ok')
        if (selectedId === cat.id) setSelectedId(null)
        loadCategories()
      }).catch(function (e) { toast(e.message, 'err') })
    })
  }, [deleteUrl, selectedId, loadCategories])

  const deleteType = useCallback(function (t) {
    confirmDialog({
      title: 'Tipi Sil',
      message: '"' + t.name + '" tipini silmek istediğinize emin misiniz?',
      okLabel: 'Sil', cancelLabel: 'Vazgeç', danger: true,
    }).then(function (ok) {
      if (!ok) return
      postDelete(deleteUrl, t.id).then(function () {
        toast('Tip silindi.', 'ok')
        loadTypes(selectedId)
      }).catch(function (e) { toast(e.message, 'err') })
    })
  }, [deleteUrl, selectedId, loadTypes])

  return (
    <div className="dcm-root">
      {/* ── Sol: Kategoriler (L1) ──────────────────────────────────────── */}
      <div className="dcm-col dcm-col-cats">
        <div className="dcm-col-header">
          <FolderTree size={16} className="dcm-col-header-icon" />
          <span className="dcm-col-title">Kategoriler</span>
          <span className="dcm-count">{categories.length}</span>
          <button className="dcm-btn-add" onClick={function () { setCatAdding(true); setCatEditingId(null) }} title="Yeni Kategori">
            <Plus size={14} />
          </button>
        </div>
        <div className="dcm-search-wrap">
          <Search size={13} className="dcm-search-icon" />
          <input
            className="dcm-search"
            placeholder="Kategori ara…"
            value={search}
            onChange={function (e) { setSearch(e.target.value) }}
          />
        </div>
        <div className="dcm-list">
          {catAdding && (
            <RowForm onSave={function (name) { return saveCategory(null, name) }} onCancel={function () { setCatAdding(false) }} />
          )}
          {loadingCats ? (
            <div className="dcm-loading"><Loader2 size={16} className="dcm-spin" /></div>
          ) : filteredCategories.length === 0 && !catAdding ? (
            <div className="dcm-empty">{search ? 'Sonuç bulunamadı' : 'Henüz kategori tanımlanmamış'}</div>
          ) : (
            filteredCategories.map(function (cat) {
              if (catEditingId === cat.id) {
                return (
                  <RowForm
                    key={cat.id}
                    initial={cat.name}
                    onSave={function (name) { return saveCategory(cat.id, name) }}
                    onCancel={function () { setCatEditingId(null) }}
                  />
                )
              }
              return (
                <div
                  key={cat.id}
                  className={'dcm-row dcm-row-cat' + (selectedId === cat.id ? ' is-selected' : '') + (!cat.isActive ? ' is-inactive' : '')}
                  onClick={function () { setSelectedId(cat.id) }}
                >
                  <span className="dcm-row-name">{cat.name}</span>
                  <ActiveSwitch checked={!!cat.isActive} onToggle={function () { toggleCategoryActive(cat) }} />
                  <button
                    className="dcm-row-btn"
                    title="Düzenle"
                    onClick={function (e) { e.stopPropagation(); setCatEditingId(cat.id); setCatAdding(false) }}
                  >
                    <Edit2 size={13} />
                  </button>
                  <button
                    className="dcm-row-btn dcm-row-btn-del"
                    title="Sil"
                    onClick={function (e) { e.stopPropagation(); deleteCategory(cat) }}
                  >
                    <Trash2 size={13} />
                  </button>
                </div>
              )
            })
          )}
        </div>
      </div>

      {/* ── Sağ: Tipler (L2 — seçili kategorinin alt tipleri) ────────────── */}
      <div className="dcm-col dcm-col-types">
        <div className="dcm-col-header">
          <Tag size={16} className="dcm-col-header-icon" />
          <span className="dcm-col-title">
            Tipler{selectedCategory ? <span className="dcm-col-title-sub"> — {selectedCategory.name}</span> : null}
          </span>
          <span className="dcm-count">{types.length}</span>
          <button
            className="dcm-btn-add"
            disabled={selectedId == null}
            onClick={function () { setTypeAdding(true); setTypeEditingId(null) }}
            title="Yeni Tip"
          >
            <Plus size={14} />
          </button>
        </div>
        <div className="dcm-list">
          {selectedId == null ? (
            <div className="dcm-empty">Tipleri görmek için soldan bir kategori seçin.</div>
          ) : (
            <>
              {typeAdding && (
                <RowForm onSave={function (name) { return saveType(null, name) }} onCancel={function () { setTypeAdding(false) }} />
              )}
              {loadingTypes ? (
                <div className="dcm-loading"><Loader2 size={16} className="dcm-spin" /></div>
              ) : types.length === 0 && !typeAdding ? (
                <div className="dcm-empty">Bu kategori altında henüz tip tanımlanmamış.</div>
              ) : (
                types.map(function (t) {
                  if (typeEditingId === t.id) {
                    return (
                      <RowForm
                        key={t.id}
                        initial={t.name}
                        onSave={function (name) { return saveType(t.id, name) }}
                        onCancel={function () { setTypeEditingId(null) }}
                      />
                    )
                  }
                  return (
                    <div key={t.id} className={'dcm-row' + (!t.isActive ? ' is-inactive' : '')}>
                      <span className="dcm-row-name">{t.name}</span>
                      <ActiveSwitch checked={!!t.isActive} onToggle={function () { toggleTypeActive(t) }} />
                      <button
                        className="dcm-row-btn"
                        title="Düzenle"
                        onClick={function () { setTypeEditingId(t.id); setTypeAdding(false) }}
                      >
                        <Edit2 size={13} />
                      </button>
                      <button className="dcm-row-btn dcm-row-btn-del" title="Sil" onClick={function () { deleteType(t) }}>
                        <Trash2 size={13} />
                      </button>
                    </div>
                  )
                })
              )}
            </>
          )}
        </div>
      </div>
    </div>
  )
}
