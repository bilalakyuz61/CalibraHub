/**
 * FormManagementTree — Form Tasarım Ayarları hiyerarşik görünümü
 *
 * 3 seviyeli ağaç: Module  →  SubModule (opsiyonel)  →  SmartCard ızgara
 *
 * Props: { config }  —  AdminController.BuildFormsBoardConfigAsync çıktısı.
 *   config.entities[i] şu ekstra alanları içerir:
 *     module, subModule, sortOrder
 */
import { useState, useMemo, useCallback } from 'react'
import { LayoutGrid, Plus, Search, ChevronRight, X } from 'lucide-react'
import SmartCard from '../CalibraSmartBoard/SmartCard'
import './FormManagementTree.css'

// ─── Yardımcı: modülü sırala (min sortOrder) ─────────────────────────────────
function minSort(forms) {
  return forms.reduce(function (m, f) { return Math.min(m, f.sortOrder || 9999) }, 9999)
}

// ─── Gruplama ─────────────────────────────────────────────────────────────────
function buildGroups(entities) {
  var moduleMap = {}

  entities.forEach(function (e) {
    var mod = (e.module || '').trim() || 'Diğer'
    var sub = (e.subModule || '').trim() || null

    if (!moduleMap[mod]) moduleMap[mod] = { name: mod, direct: [], subs: {} }

    if (sub) {
      if (!moduleMap[mod].subs[sub]) moduleMap[mod].subs[sub] = []
      moduleMap[mod].subs[sub].push(e)
    } else {
      moduleMap[mod].direct.push(e)
    }
  })

  return Object.values(moduleMap).sort(function (a, b) {
    var allA = a.direct.concat(Object.values(a.subs).flat())
    var allB = b.direct.concat(Object.values(b.subs).flat())
    return minSort(allA) - minSort(allB)
  })
}

// ─── Toplam form sayısı ───────────────────────────────────────────────────────
function totalCount(group) {
  return group.direct.length +
    Object.values(group.subs).reduce(function (s, arr) { return s + arr.length }, 0)
}

// ─── Arama filtresi ───────────────────────────────────────────────────────────
function matchesSearch(e, q) {
  if (!q) return true
  return (
    (e.title     || '').toLowerCase().includes(q) ||
    (e.subtitle  || '').toLowerCase().includes(q) ||
    (e.module    || '').toLowerCase().includes(q) ||
    (e.subModule || '').toLowerCase().includes(q) ||
    (e.description || '').toLowerCase().includes(q)
  )
}

// ─── SmartCard ızgara ─────────────────────────────────────────────────────────
function FormGrid({ forms, onRefresh }) {
  if (!forms.length) return null
  return (
    <div className="fmt-grid">
      {forms.map(function (e) {
        return (
          <SmartCard
            key={e.id}
            {...e}
            visibleIds={[]}
            order={[]}
            onRefresh={onRefresh}
          />
        )
      })}
    </div>
  )
}

// ─── Ana bileşen ─────────────────────────────────────────────────────────────
export default function FormManagementTree({ config }) {
  var cfg        = config || {}
  var refreshUrl = cfg.refreshUrl || null
  var newUrl     = '/Admin/FormEdit'
  if (Array.isArray(cfg.actions)) {
    var newAction = cfg.actions.find(function (a) { return a.id === 'new' })
    if (newAction && newAction.url) newUrl = newAction.url
  }

  var [entities,          setEntities]          = useState(Array.isArray(cfg.entities) ? cfg.entities : [])
  var [searchTerm,        setSearchTerm]        = useState('')
  var [expandedModules,   setExpandedModules]   = useState(null) // null = all open
  var [expandedSubMods,   setExpandedSubMods]   = useState(null) // null = all open

  // ── Filtre ──────────────────────────────────────────────────────────────────
  var q = searchTerm.trim().toLowerCase()

  var filtered = useMemo(function () {
    if (!q) return entities
    return entities.filter(function (e) { return matchesSearch(e, q) })
  }, [entities, q])

  // ── Gruplar ─────────────────────────────────────────────────────────────────
  var groups = useMemo(function () { return buildGroups(filtered) }, [filtered])

  // ── Expand/collapse yardımcıları ─────────────────────────────────────────────
  function isModExpanded(mod) {
    if (expandedModules === null) return true   // varsayılan: hepsi açık
    return expandedModules.has(mod)
  }
  function isSubExpanded(mod, sub) {
    if (expandedSubMods === null) return true
    return expandedSubMods.has(mod + '::' + sub)
  }

  function toggleMod(mod) {
    setExpandedModules(function (prev) {
      // ilk toggle'da mevcut durumu somutlaştır
      var base = prev !== null ? new Set(prev) : new Set(groups.map(function (g) { return g.name }))
      if (base.has(mod)) base.delete(mod); else base.add(mod)
      return base
    })
  }

  function toggleSub(mod, sub) {
    setExpandedSubMods(function (prev) {
      var allKeys = []
      groups.forEach(function (g) {
        Object.keys(g.subs).forEach(function (s) { allKeys.push(g.name + '::' + s) })
      })
      var base = prev !== null ? new Set(prev) : new Set(allKeys)
      var key  = mod + '::' + sub
      if (base.has(key)) base.delete(key); else base.add(key)
      return base
    })
  }

  // Arama varsa tüm section'lar açık olsun
  function isEffectivelyOpen(mod) { return q ? true : isModExpanded(mod) }
  function isSubEffectivelyOpen(mod, sub) { return q ? true : isSubExpanded(mod, sub) }

  // ── Refresh (silme sonrası) ──────────────────────────────────────────────────
  var handleRefresh = useCallback(function () {
    if (!refreshUrl) return
    fetch(refreshUrl, { credentials: 'same-origin' })
      .then(function (r) { return r.ok ? r.json() : null })
      .then(function (data) {
        if (data && Array.isArray(data.entities)) setEntities(data.entities)
      })
      .catch(function () {})
  }, [refreshUrl])

  // ── Render ───────────────────────────────────────────────────────────────────
  var totalForms = entities.length
  var moduleCount = useMemo(function () {
    return buildGroups(entities).length
  }, [entities])

  return (
    <div className="fmt-root">

      {/* ── Üst bar ─────────────────────────────────────────────────────── */}
      <div className="fmt-topbar">
        <div className="fmt-topbar-left">
          <div className="fmt-topbar-icon">
            <LayoutGrid size={20} />
          </div>
          <div>
            <h1 className="fmt-title">Form Tasarım Ayarları</h1>
            <p className="fmt-subtitle">{totalForms} form · {moduleCount} modül</p>
          </div>
        </div>
        <div className="fmt-topbar-right">
          <div className="fmt-search-wrap">
            <Search size={14} className="fmt-search-ico" />
            <input
              type="search"
              className="fmt-search"
              placeholder={cfg.searchPlaceholder || 'Form kodu, adı, modül ara…'}
              value={searchTerm}
              onChange={function (e) { setSearchTerm(e.target.value) }}
            />
            {searchTerm && (
              <button type="button" className="fmt-search-clear" onClick={function () { setSearchTerm('') }}>
                <X size={13} />
              </button>
            )}
          </div>
          <a href={newUrl} className="fmt-btn fmt-btn--primary">
            <Plus size={14} /> Yeni Form
          </a>
        </div>
      </div>

      {/* ── İçerik ──────────────────────────────────────────────────────── */}
      <div className="fmt-body">

        {groups.length === 0 && (
          <div className="fmt-empty">
            <LayoutGrid size={40} />
            <p>{q ? 'Arama sonucu bulunamadı.' : (cfg.emptyText || 'Henüz form tanımlanmamış.')}</p>
            {!q && (
              <a href={newUrl} className="fmt-btn fmt-btn--primary" style={{ marginTop: 4 }}>
                <Plus size={14} /> İlk Formu Ekle
              </a>
            )}
          </div>
        )}

        {groups.map(function (group) {
          var modOpen = isEffectivelyOpen(group.name)
          var cnt     = totalCount(group)

          return (
            <div key={group.name} className="fmt-section-wrap">

              {/* Module başlığı */}
              <div
                className={'fmt-section-header' + (modOpen ? ' fmt-section-header--open' : '')}
                onClick={function () { if (!q) toggleMod(group.name) }}
                role="button"
                aria-expanded={modOpen}
              >
                <ChevronRight
                  size={15}
                  className={'fmt-chevron' + (modOpen ? ' fmt-chevron--open' : '')}
                />
                <span className="fmt-section-name">{group.name}</span>
                <span className="fmt-section-count">{cnt} form</span>
              </div>

              {/* Module içeriği */}
              {modOpen && (
                <div className="fmt-section-content">

                  {/* SubModule'suz direkt formlar */}
                  {group.direct.length > 0 && (
                    <FormGrid forms={group.direct} onRefresh={handleRefresh} />
                  )}

                  {/* SubModule grupları */}
                  {Object.keys(group.subs).sort().map(function (sub) {
                    var subForms = group.subs[sub]
                    var subOpen  = isSubEffectivelyOpen(group.name, sub)

                    return (
                      <div key={sub} className="fmt-subsection-wrap">
                        <div
                          className={'fmt-subsection-header' + (subOpen ? ' fmt-subsection-header--open' : '')}
                          onClick={function () { if (!q) toggleSub(group.name, sub) }}
                          role="button"
                          aria-expanded={subOpen}
                        >
                          <ChevronRight
                            size={13}
                            className={'fmt-subchr' + (subOpen ? ' fmt-subchr--open' : '')}
                          />
                          <span className="fmt-subsection-name">{sub}</span>
                          <span className="fmt-section-count">{subForms.length} form</span>
                        </div>

                        {subOpen && (
                          <div className="fmt-subsection-content">
                            <FormGrid forms={subForms} onRefresh={handleRefresh} />
                          </div>
                        )}
                      </div>
                    )
                  })}
                </div>
              )}
            </div>
          )
        })}
      </div>
    </div>
  )
}
