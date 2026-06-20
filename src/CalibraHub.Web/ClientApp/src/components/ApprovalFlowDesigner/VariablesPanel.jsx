/**
 * VariablesPanel — surec-scoped degisken tanim editoru (per-flow).
 *
 * Props:
 *   open        bool        — panel acik mi
 *   variables   array       — [{ id, name, typeCode, defaultValue, description, valueSource, sqlQuery, sortOrder }]
 *   onChange    fn(list)    — degisken listesi guncellendiginde cagrilir
 *   onClose     fn()        — panel kapatilirken
 *
 * Type kodlari: int | bool | string | decimal | date
 * valueSource : manual | sql
 */
import React, { useState, useEffect, useRef } from 'react'

var TYPE_OPTIONS = [
  { code: 'int',     label: 'Tam Sayı (int)',    placeholder: '0' },
  { code: 'decimal', label: 'Ondalık (decimal)', placeholder: '0.00' },
  { code: 'bool',    label: 'Mantıksal (bool)',  placeholder: 'false' },
  { code: 'string',  label: 'Metin (string)',    placeholder: '' },
  { code: 'date',    label: 'Tarih (date)',      placeholder: 'YYYY-MM-DD' },
]

var NAME_RE = /^[A-Za-zÇçĞğİıÖöŞşÜü_][A-Za-zÇçĞğİıÖöŞşÜü0-9_]{0,59}$/

export default function VariablesPanel({ open, variables, onChange, onClose }) {
  var [list, setList]           = useState(Array.isArray(variables) ? variables : [])
  var [invalidIdx, setInvalidIdx] = useState({ set: {}, nonce: 0 })
  // SQL editor modal state
  var [sqlModal, setSqlModal]   = useState({ open: false, idx: -1, draft: '' })
  var sqlTextareaRef            = useRef(null)

  useEffect(function () {
    setList(Array.isArray(variables) ? variables : [])
  }, [variables])

  function commit(next) {
    setList(next)
    if (typeof onChange === 'function') onChange(next)
  }

  function clearInvalid(idx) {
    setInvalidIdx(function (prev) {
      if (!prev.set[idx]) return prev
      var s = Object.assign({}, prev.set); delete s[idx]
      return { set: s, nonce: prev.nonce }
    })
  }

  function addRow() {
    var nextName = 'yeniDegisken'
    var i = 1
    var taken = function (n) { return list.some(function (v) { return (v.name || '').toLowerCase() === n.toLowerCase() }) }
    while (taken(nextName)) { i++; nextName = 'yeniDegisken' + i }
    commit(list.concat([{
      id: 0, name: nextName, typeCode: 'string', defaultValue: '', description: '',
      valueSource: 'manual', sqlQuery: '', sortOrder: list.length,
    }]))
  }

  function delRow(idx) {
    commit(list.filter(function (_, i) { return i !== idx }))
    clearInvalid(idx)
  }

  function patchRow(idx, patch) {
    var next = list.map(function (v, i) { return i === idx ? Object.assign({}, v, patch) : v })
    if (patch.name != null) clearInvalid(idx)
    commit(next)
  }

  function handleOk() {
    var bad = {}
    var lc = list.map(function (v) { return (v.name || '').toLowerCase() })
    list.forEach(function (v, i) {
      var nm = (v.name || '').trim()
      if (!nm) { bad[i] = true; return }
      if (!NAME_RE.test(nm)) { bad[i] = true; return }
      if (lc.filter(function (x) { return x === nm.toLowerCase() }).length > 1) bad[i] = true
    })
    if (Object.keys(bad).length === 0) {
      if (typeof onClose === 'function') onClose()
      return
    }
    setInvalidIdx(function (prev) { return { set: bad, nonce: prev.nonce + 1 } })
  }

  // SQL modal aç
  function openSqlModal(idx) {
    setSqlModal({ open: true, idx: idx, draft: list[idx]?.sqlQuery || '' })
    setTimeout(function () { if (sqlTextareaRef.current) sqlTextareaRef.current.focus() }, 60)
  }

  // SQL modal kaydet
  function saveSqlModal() {
    patchRow(sqlModal.idx, { sqlQuery: sqlModal.draft })
    setSqlModal({ open: false, idx: -1, draft: '' })
  }

  // Esc — önce SQL modal'ı kapat, yoksa ana panel
  useEffect(function () {
    if (!open) return
    function onKey(e) {
      if (e.key === 'Escape') {
        e.stopPropagation()
        if (sqlModal.open) { setSqlModal({ open: false, idx: -1, draft: '' }); return }
        if (typeof onClose === 'function') onClose()
      }
      if (e.key === 'Enter' && sqlModal.open && (e.ctrlKey || e.metaKey)) {
        e.preventDefault()
        saveSqlModal()
      }
    }
    window.addEventListener('keydown', onKey)
    return function () { window.removeEventListener('keydown', onKey) }
  }, [open, onClose, sqlModal])

  if (!open) return null

  var sqlModalVar = sqlModal.idx >= 0 ? list[sqlModal.idx] : null

  return (
    <>
      <div className="afd-vars-backdrop">
        <div className="afd-vars-card" role="dialog" aria-modal="true">
          <div className="afd-vars-head">
            <div className="afd-vars-title">
              <span style={{ fontSize: 18, marginRight: 6 }}>𝑥</span>
              Süreç Değişkenleri
            </div>
            <button type="button" className="afd-vars-close" onClick={onClose} title="Kapat">×</button>
          </div>

          <div className="afd-vars-hint">
            Bu akış içinde tanımlı değişkenler. "Değişken Ata" düğümünden okunup yazılır; Karar
            koşullarında <code>{'{var:adi}'}</code> referansı ile karşılaştırılır. İsim:
            harfle başlar, harf/rakam/_ devam eder.
          </div>

          <div className="afd-vars-table-wrap">
            <table className="afd-vars-table">
              <thead>
                <tr>
                  <th style={{ width: '22%' }}>Ad</th>
                  <th style={{ width: '18%' }}>Tip</th>
                  <th style={{ width: '16%' }}>Kaynak</th>
                  <th style={{ width: '16%' }}>Varsayılan</th>
                  <th>Açıklama</th>
                  <th style={{ width: 72 }}></th>
                </tr>
              </thead>
              <tbody>
                {list.length === 0 && (
                  <tr><td colSpan="6" className="afd-vars-empty">Henüz değişken tanımı yok. Ekle.</td></tr>
                )}
                {list.map(function (v, idx) {
                  var t = TYPE_OPTIONS.find(function (x) { return x.code === (v.typeCode || 'string') }) || TYPE_OPTIONS[3]
                  var isSql = v.valueSource === 'sql'
                  return (
                    <tr key={idx}>
                      <td>
                        <input
                          key={'nm_' + idx + '_' + (invalidIdx.set[idx] ? invalidIdx.nonce : '0')}
                          className={'afd-vars-input' + (invalidIdx.set[idx] ? ' is-shake' : '')}
                          value={v.name || ''}
                          placeholder="degiskenAdi"
                          onChange={function (e) { patchRow(idx, { name: e.target.value }) }} />
                      </td>
                      <td>
                        <select className="afd-vars-input" value={v.typeCode || 'string'}
                                onChange={function (e) { patchRow(idx, { typeCode: e.target.value }) }}>
                          {TYPE_OPTIONS.map(function (o) {
                            return <option key={o.code} value={o.code}>{o.label}</option>
                          })}
                        </select>
                      </td>
                      <td>
                        <select className="afd-vars-input" value={v.valueSource || 'manual'}
                                onChange={function (e) { patchRow(idx, { valueSource: e.target.value }) }}>
                          <option value="manual">Manuel</option>
                          <option value="sql">SQL Sorgusu</option>
                        </select>
                      </td>
                      <td>
                        {isSql ? (
                          <span className="afd-vars-sql-badge">{v.sqlQuery ? '✓ SQL' : '— SQL'}</span>
                        ) : v.typeCode === 'bool' ? (
                          <select className="afd-vars-input" value={String(v.defaultValue || 'false')}
                                  onChange={function (e) { patchRow(idx, { defaultValue: e.target.value }) }}>
                            <option value="false">false</option>
                            <option value="true">true</option>
                          </select>
                        ) : (
                          <input className="afd-vars-input" value={v.defaultValue || ''}
                                 placeholder={t.placeholder}
                                 onChange={function (e) { patchRow(idx, { defaultValue: e.target.value }) }} />
                        )}
                      </td>
                      <td>
                        <input className="afd-vars-input" value={v.description || ''}
                               placeholder="kısa açıklama"
                               onChange={function (e) { patchRow(idx, { description: e.target.value }) }} />
                      </td>
                      <td style={{ display: 'flex', gap: 4, alignItems: 'center', padding: '4px 6px' }}>
                        {isSql && (
                          <button type="button" className="afd-vars-sql-edit-btn"
                                  title="SQL sorgusunu düzenle"
                                  onClick={function () { openSqlModal(idx) }}>
                            SQL
                          </button>
                        )}
                        <button type="button" className="afd-vars-del" title="Sil"
                                onClick={function () { delRow(idx) }}>×</button>
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>

          <div className="afd-vars-foot">
            <button type="button" className="afd-vars-add" onClick={addRow}>+ Yeni Değişken</button>
            <button type="button" className="afd-vars-ok"  onClick={handleOk}>Tamam</button>
          </div>
        </div>
      </div>

      {/* SQL Editor Modal */}
      {sqlModal.open && (
        <div className="afd-sql-modal-backdrop">
          <div className="afd-sql-modal-card" role="dialog" aria-modal="true">
            <div className="afd-sql-modal-head">
              <div className="afd-sql-modal-title">
                SQL Sorgusu
                {sqlModalVar && <span className="afd-sql-modal-varname"> — {sqlModalVar.name}</span>}
              </div>
              <button type="button" className="afd-vars-close"
                      onClick={function () { setSqlModal({ open: false, idx: -1, draft: '' }) }} title="İptal">×</button>
            </div>
            <div className="afd-sql-modal-hint">
              Skalar değer döndürmeli (ilk satır, ilk sütun). Bağlam değerleri: <code>@documentId</code> · <code>@userId</code> · <code>@departmentId</code> · <code>@amount</code> · <code>@contactId</code> ve diğer süreç değişkenleri de <code>@degiskenAdi</code> olarak kullanılabilir.
              {sqlModalVar && <span className="afd-sql-modal-token"> Bildirimde: <code>{'{var.' + sqlModalVar.name + '}'}</code></span>}
            </div>
            <textarea
              ref={sqlTextareaRef}
              className="afd-sql-modal-textarea"
              rows={14}
              value={sqlModal.draft}
              placeholder={'SELECT TOP 1 i.Code\nFROM Item i\nINNER JOIN Department d ON d.Id = i.DepartmentId\nWHERE d.Id = @departmentId\nORDER BY i.Created DESC'}
              onChange={function (e) { setSqlModal(function (s) { return Object.assign({}, s, { draft: e.target.value }) }) }}
            />
            <div className="afd-sql-modal-foot">
              <span className="afd-sql-modal-shortcut">Ctrl+Enter ile kaydet</span>
              <div style={{ display: 'flex', gap: 8 }}>
                <button type="button" className="afd-vars-add"
                        onClick={function () { setSqlModal({ open: false, idx: -1, draft: '' }) }}>
                  İptal
                </button>
                <button type="button" className="afd-vars-ok" onClick={saveSqlModal}>
                  Kaydet
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
    </>
  )
}
