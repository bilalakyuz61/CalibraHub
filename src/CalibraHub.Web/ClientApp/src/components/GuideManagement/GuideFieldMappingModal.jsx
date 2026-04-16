/**
 * GuideFieldMappingModal — Rehber Sabit Alan Eslestirme Modali
 *
 * Layout:
 *   Sol panel (%32): Form agaci — Module > SubModule > Form (acilabilir) > Alanlar
 *   Sag panel (%68): Secili alanin eslestirme detaylari
 *
 * Props:
 *   isOpen    — Modal acik mi
 *   onClose   — Kapatma callback'i
 *   guide     — Eslestirilen rehber objesi { guideCode, guideLabel, ... }
 */
import { useState, useEffect, useCallback } from 'react'
import { createPortal } from 'react-dom'
import {
  X, Search, Link2, Loader2, AlertCircle,
  ChevronRight, ChevronDown, Save
} from 'lucide-react'
import { getForms } from '../../services/formManagementService'
import {
  getFieldsByForm, bulkMapGuide
} from '../../services/fieldSettingService'
import ConstraintBuilder from './ConstraintBuilder'

export default function GuideFieldMappingModal(props) {
  var isOpen = props.isOpen
  var onClose = props.onClose
  var guide = props.guide

  if (!isOpen || !guide) return null

  return createPortal(
    <MappingModalInner guide={guide} onClose={onClose} />,
    document.body
  )
}

function MappingModalInner(props) {
  var guide = props.guide
  var onClose = props.onClose

  // ── Forms ──
  var [forms, setForms] = useState([])
  var [formsLoading, setFormsLoading] = useState(true)
  var [formSearch, setFormSearch] = useState('')

  // ── Sol panel agac durumu ──
  var [collapsedModules, setCollapsedModules] = useState({})
  var [collapsedSubModules, setCollapsedSubModules] = useState({})
  var [expandedForms, setExpandedForms] = useState({})   // formId -> bool

  // ── Alan verileri (per-form cache) ──
  var [formData, setFormData] = useState({})     // formId -> { fields, loading }
  var [mappings, setMappings] = useState({})     // formId -> { fieldKey -> { mapped, filterJson, isRequired } }

  // ── Secim ──
  var [selectedFormId, setSelectedFormId] = useState(null)
  var [selectedFieldKey, setSelectedFieldKey] = useState(null)

  // ── Kaydetme / mesajlar ──
  var [saving, setSaving] = useState(false)
  var [error, setError] = useState(null)
  var [successMsg, setSuccessMsg] = useState(null)

  // ── Form listesi yukle ──
  useEffect(function () {
    setFormsLoading(true)
    getForms()
      .then(function (data) { setForms(Array.isArray(data) ? data : []) })
      .catch(function (e) { setError('Formlar yuklenemedi: ' + e.message) })
      .finally(function () { setFormsLoading(false) })
  }, [])

  // ── ESC ile kapat ──
  useEffect(function () {
    function onKey(e) { if (e.key === 'Escape') onClose() }
    document.addEventListener('keydown', onKey)
    return function () { document.removeEventListener('keydown', onKey) }
  }, [onClose])

  // ── Form icin alanlari yukle (sadece FldSet'teki kayitli alanlar) ──
  var loadFormFields = useCallback(async function (form) {
    var formId = form.id
    setFormData(function (prev) {
      return Object.assign({}, prev, { [formId]: { fields: (prev[formId] || {}).fields || [], loading: true } })
    })
    try {
      var arr = await getFieldsByForm(formId)
      if (!Array.isArray(arr)) arr = []

      // Mapping state'ini hazirla
      var m = {}
      arr.forEach(function (f) {
        var isMapped = f.guideCode === guide.guideCode
        m[f.fieldKey] = {
          mapped: isMapped,
          filterJson: isMapped ? (f.filterJson || '') : '',
          isRequired: isMapped ? (f.isRequired || false) : false,
        }
      })

      setFormData(function (prev) {
        return Object.assign({}, prev, { [formId]: { fields: arr, loading: false } })
      })
      setMappings(function (prev) {
        return Object.assign({}, prev, { [formId]: m })
      })
    } catch (e) {
      setError('Alanlar yuklenemedi: ' + e.message)
      setFormData(function (prev) {
        return Object.assign({}, prev, { [formId]: { fields: [], loading: false } })
      })
    }
  }, [guide.guideCode])

  // ── Form satirina tiklama: ac/kapat ──
  function handleExpandForm(form) {
    var formId = form.id
    var willExpand = !expandedForms[formId]
    setExpandedForms(function (prev) {
      return Object.assign({}, prev, { [formId]: willExpand })
    })
    // Ilk acilista yukle
    if (willExpand && !formData[formId]) {
      loadFormFields(form)
    }
  }

  // ── Alan secimi ──
  function handleSelectField(formId, fieldKey) {
    setSelectedFormId(formId)
    setSelectedFieldKey(fieldKey)
  }

  // ── Eslestirme toggle ──
  function toggleMapping(formId, fieldKey) {
    setMappings(function (prev) {
      var fm = prev[formId] || {}
      var cur = fm[fieldKey] || { mapped: false, filterJson: '', isRequired: false }
      var newMapped = !cur.mapped
      return Object.assign({}, prev, {
        [formId]: Object.assign({}, fm, {
          [fieldKey]: { mapped: newMapped, filterJson: cur.filterJson, isRequired: newMapped ? cur.isRequired : false }
        })
      })
    })
  }

  // ── Zorunlu toggle ──
  function toggleRequired(formId, fieldKey) {
    setMappings(function (prev) {
      var fm = prev[formId] || {}
      var cur = fm[fieldKey] || { mapped: false, filterJson: '', isRequired: false }
      return Object.assign({}, prev, {
        [formId]: Object.assign({}, fm, {
          [fieldKey]: Object.assign({}, cur, { isRequired: !cur.isRequired })
        })
      })
    })
  }

  // ── FilterJson degistir ──
  function setFilterJson(formId, fieldKey, val) {
    setMappings(function (prev) {
      var fm = prev[formId] || {}
      var cur = fm[fieldKey] || { mapped: false, filterJson: '', isRequired: false }
      return Object.assign({}, prev, {
        [formId]: Object.assign({}, fm, {
          [fieldKey]: Object.assign({}, cur, { filterJson: val })
        })
      })
    })
  }

  // ── Kaydet ──
  async function handleSave() {
    if (!selectedFormId) return
    var fd = formData[selectedFormId] || {}
    var fields = fd.fields || []
    var fm = mappings[selectedFormId] || {}

    var fieldItems = fields.map(function (f) {
      var m = fm[f.fieldKey] || { mapped: false, filterJson: '', isRequired: false }
      return {
        fieldKey: f.fieldKey,
        fieldLabel: f.fieldLabel,
        mapped: m.mapped,
        filterJson: m.filterJson || null,
        isRequired: m.isRequired || false,
      }
    })

    setSaving(true)
    setError(null)
    setSuccessMsg(null)

    var result = await bulkMapGuide({
      guideCode: guide.guideCode,
      formId: selectedFormId,
      fields: fieldItems,
    })

    setSaving(false)
    if (result.success) {
      setSuccessMsg('Eslestirme kaydedildi.')
      setTimeout(function () { setSuccessMsg(null) }, 2000)
      // Formu yeniden yukle
      var form = forms.find(function (f) { return f.id === selectedFormId })
      if (form) loadFormFields(form)
    } else {
      setError('Kaydetme basarisiz: ' + (result.message || ''))
    }
  }

  // ── Hesaplanan degerler ──
  var selectedForm = forms.find(function (f) { return f.id === selectedFormId }) || null
  var selectedFields = (formData[selectedFormId] || {}).fields || []
  var selectedField = selectedFields.find(function (f) { return f.fieldKey === selectedFieldKey }) || null
  var selectedFm = ((mappings[selectedFormId] || {})[selectedFieldKey]) || { mapped: false, filterJson: '', isRequired: false }
  var hasOtherGuide = selectedField && selectedField.guideCode && selectedField.guideCode !== guide.guideCode

  // ── Form arama filtresi (sadece BaseTable tanimli formlar) ──
  var filteredForms = forms.filter(function (f) {
    if (!f.baseTable) return false   // BaseTable olmayan formlar gorunmez
    if (!formSearch.trim()) return true
    var q = formSearch.toLowerCase()
    return (
      (f.formName   || '').toLowerCase().includes(q) ||
      (f.subModule  || '').toLowerCase().includes(q) ||
      (f.module     || '').toLowerCase().includes(q) ||
      (f.formCode   || '').toLowerCase().includes(q)
    )
  })

  // 3 seviyeli gruplama
  var modules = {}
  filteredForms.forEach(function (f) {
    var mod = f.module || 'Diğer'
    if (!modules[mod]) modules[mod] = { standalone: [], subModules: {}, total: 0 }
    modules[mod].total++
    if (f.subModule) {
      if (!modules[mod].subModules[f.subModule]) modules[mod].subModules[f.subModule] = []
      modules[mod].subModules[f.subModule].push(f)
    } else {
      modules[mod].standalone.push(f)
    }
  })
  var moduleKeys = Object.keys(modules).sort()

  // ── Form + alanlarini render et ──
  function renderFormItem(form, indented) {
    var formId = form.id
    var isExpanded = expandedForms[formId]
    var fd = formData[formId]
    var fm = mappings[formId] || {}
    var mappedCount = fd ? fd.fields.filter(function (fld) { return fm[fld.fieldKey] && fm[fld.fieldKey].mapped }).length : 0
    var totalFields = fd ? fd.fields.length : 0

    return (
      <div key={formId} className="gm-tree-form-block">
        {/* Form satiri */}
        <button
          type="button"
          className={'gm-tree-form-btn' + (indented ? ' gm-tree-form-btn--sub' : '') + (selectedFormId === formId ? ' gm-tree-form-btn--active' : '')}
          onClick={function () { handleExpandForm(form) }}
        >
          <span className="gm-tree-form-chevron">
            {isExpanded ? <ChevronDown size={12} /> : <ChevronRight size={12} />}
          </span>
          <span className="gm-tree-form-name">{form.formName}</span>
          {totalFields > 0 && (
            <span className={'gm-tree-form-badge' + (mappedCount > 0 ? ' gm-tree-form-badge--mapped' : '')}>
              {mappedCount > 0 ? mappedCount + '/' + totalFields : totalFields}
            </span>
          )}
        </button>

        {/* Alan listesi */}
        {isExpanded && (
          <div className="gm-tree-field-list">
            {fd && fd.loading ? (
              <div className="gm-tree-status"><Loader2 size={12} className="gm-spin" /> Yukleniyor...</div>
            ) : !fd || fd.fields.length === 0 ? (
              <div className="gm-tree-status gm-tree-status--empty">Alan bulunamadi</div>
            ) : (
              fd.fields.map(function (fld) {
                var m = fm[fld.fieldKey] || { mapped: false }
                var isOther = fld.guideCode && fld.guideCode !== guide.guideCode
                var isSelected = selectedFormId === formId && selectedFieldKey === fld.fieldKey
                return (
                  <button
                    key={fld.fieldKey}
                    type="button"
                    className={'gm-tree-field-item' + (isSelected ? ' gm-tree-field-item--active' : '') + (isOther ? ' gm-tree-field-item--other' : '')}
                    onClick={function () { handleSelectField(formId, fld.fieldKey) }}
                  >
                    <span className={'gm-tree-field-dot' + (m.mapped ? ' gm-tree-field-dot--on' : '') + (isOther ? ' gm-tree-field-dot--other' : '')} />
                    <span className="gm-tree-field-key">{fld.fieldKey}</span>
                    {fld.fieldLabel !== fld.fieldKey && (
                      <span className="gm-tree-field-label">{fld.fieldLabel}</span>
                    )}
                  </button>
                )
              })
            )}
          </div>
        )}
      </div>
    )
  }

  return (
    <div className="gm-mapping-backdrop" onClick={function (e) { if (e.target === e.currentTarget) onClose() }}>
      <div className="gm-mapping-modal" onClick={function (e) { e.stopPropagation() }}>

        {/* ── Header ── */}
        <div className="gm-mapping-header">
          <div className="gm-mapping-header-left">
            <Link2 size={18} />
            <div>
              <h2 className="gm-mapping-title">{guide.guideLabel}</h2>
              <span className="gm-mapping-subtitle">Sabit Alan Eslestirme — {guide.guideCode}</span>
            </div>
          </div>
          <button type="button" className="gm-mapping-close" onClick={onClose}>
            <X size={18} />
          </button>
        </div>

        {/* ── Mesajlar ── */}
        {error && (
          <div className="gm-alert" role="alert">
            <AlertCircle size={15} />
            <span>{error}</span>
            <button type="button" onClick={function () { setError(null) }}>×</button>
          </div>
        )}
        {successMsg && (
          <div className="gm-mapping-success"><span>{successMsg}</span></div>
        )}

        {/* ── Body: Sol agac + Sag detay ── */}
        <div className="gm-mapping-body">

          {/* ── Sol: Form agaci ── */}
          <div className="gm-mapping-left">
            <div className="gm-mapping-left-header">
              <span className="gm-mapping-left-title">Formlar</span>
            </div>
            <div className="gm-mapping-left-search">
              <Search size={13} className="gm-mapping-left-search-ico" />
              <input
                type="search"
                placeholder="Form ara..."
                value={formSearch}
                onChange={function (e) { setFormSearch(e.target.value) }}
              />
            </div>

            {formsLoading ? (
              <div className="gm-mapping-left-loading">
                <Loader2 size={18} className="gm-spin" /> Yukle...
              </div>
            ) : (
              <div className="gm-mapping-left-list">
                {moduleKeys.map(function (mod) {
                  var modData = modules[mod]
                  var isModCollapsed = collapsedModules[mod]
                  var subModuleKeys = Object.keys(modData.subModules).sort()
                  return (
                    <div key={mod} className="gm-mapping-module">
                      <button
                        type="button"
                        className="gm-mapping-module-btn"
                        onClick={function () {
                          setCollapsedModules(function (prev) {
                            return Object.assign({}, prev, { [mod]: !prev[mod] })
                          })
                        }}
                      >
                        {isModCollapsed ? <ChevronRight size={14} /> : <ChevronDown size={14} />}
                        <span>{mod}</span>
                        <span className="gm-mapping-module-count">{modData.total}</span>
                      </button>

                      {!isModCollapsed && (
                        <>
                          {subModuleKeys.map(function (sub) {
                            var subKey = mod + '||' + sub
                            var isSubCollapsed = collapsedSubModules[subKey]
                            return (
                              <div key={sub} className="gm-mapping-submodule">
                                <button
                                  type="button"
                                  className="gm-mapping-submodule-btn"
                                  onClick={function () {
                                    setCollapsedSubModules(function (prev) {
                                      return Object.assign({}, prev, { [subKey]: !prev[subKey] })
                                    })
                                  }}
                                >
                                  {isSubCollapsed ? <ChevronRight size={12} /> : <ChevronDown size={12} />}
                                  <span>{sub}</span>
                                  <span className="gm-mapping-module-count">{modData.subModules[sub].length}</span>
                                </button>
                                {!isSubCollapsed && modData.subModules[sub].map(function (f) {
                                  return renderFormItem(f, true)
                                })}
                              </div>
                            )
                          })}
                          {modData.standalone.map(function (f) {
                            return renderFormItem(f, false)
                          })}
                        </>
                      )}
                    </div>
                  )
                })}
              </div>
            )}
          </div>

          {/* ── Sag: Alan detay paneli ── */}
          <div className="gm-mapping-right">
            {!selectedField ? (
              <div className="gm-mapping-right-empty">
                <Link2 size={40} />
                <p>Sol panelden bir formu açın, ardından bir alan seçin</p>
              </div>
            ) : (
              <div className="gm-field-detail">

                {/* Breadcrumb */}
                <div className="gm-field-detail-crumb">
                  <span className="gm-field-detail-crumb-form">{selectedForm && selectedForm.formName}</span>
                  <ChevronRight size={13} className="gm-field-detail-crumb-sep" />
                  <code className="gm-field-detail-crumb-key">{selectedField.fieldKey}</code>
                  {selectedField.fieldLabel !== selectedField.fieldKey && (
                    <span className="gm-field-detail-crumb-label">— {selectedField.fieldLabel}</span>
                  )}
                </div>

                {hasOtherGuide ? (
                  <div className="gm-field-detail-warn">
                    Bu alan başka bir rehbere (<strong>{selectedField.guideCode}</strong>) bağlı.
                    Değiştirmek için önce o eşleştirmeyi kaldırın.
                  </div>
                ) : (
                  <div className="gm-field-detail-body">

                    {/* Rehber bağla toggle */}
                    <div className="gm-field-detail-row">
                      <div className="gm-field-detail-row-info">
                        <span className="gm-field-detail-row-title">Bu rehberi bağla</span>
                        <code className="gm-field-detail-row-code">{guide.guideCode}</code>
                      </div>
                      <label className="gm-mapping-toggle">
                        <input
                          type="checkbox"
                          checked={selectedFm.mapped}
                          onChange={function () { toggleMapping(selectedFormId, selectedFieldKey) }}
                        />
                        <span className="gm-mapping-toggle-slider" />
                      </label>
                    </div>

                    {selectedFm.mapped && (
                      <>
                        {/* Zorunlu toggle */}
                        <div className="gm-field-detail-row">
                          <div className="gm-field-detail-row-info">
                            <span className="gm-field-detail-row-title">Zorunlu alan</span>
                            <span className="gm-field-detail-row-hint">Kayıt sırasında doldurulması zorunlu</span>
                          </div>
                          <label className="gm-mapping-toggle">
                            <input
                              type="checkbox"
                              checked={selectedFm.isRequired || false}
                              onChange={function () { toggleRequired(selectedFormId, selectedFieldKey) }}
                            />
                            <span className="gm-mapping-toggle-slider" />
                          </label>
                        </div>

                        {/* Kisit / ConstraintBuilder */}
                        <div className="gm-field-detail-constraint">
                          <div className="gm-field-detail-constraint-title">Kısıt (Filtre)</div>
                          <ConstraintBuilder
                            guideCode={guide.guideCode}
                            value={selectedFm.filterJson}
                            onChange={function (json) { setFilterJson(selectedFormId, selectedFieldKey, json) }}
                            formFields={selectedFields}
                          />
                        </div>
                      </>
                    )}
                  </div>
                )}
              </div>
            )}
          </div>
        </div>

        {/* ── Footer ── */}
        <div className="gm-mapping-footer">
          <button type="button" className="gm-btn gm-btn--ghost" onClick={onClose}>
            Kapat
          </button>
          <button
            type="button"
            className="gm-btn gm-btn--primary"
            onClick={handleSave}
            disabled={saving || !selectedFormId}
          >
            {saving
              ? <><Loader2 size={14} className="gm-spin" /> Kaydediliyor...</>
              : <><Save size={14} /> Kaydet</>}
          </button>
        </div>
      </div>
    </div>
  )
}
