/**
 * VariablesPanel — surec-scoped degisken tanim editoru (per-flow).
 *
 * Props:
 *   open        bool        — panel acik mi
 *   variables   array       — [{ id, name, typeCode, defaultValue, description, sortOrder }]
 *   onChange    fn(list)    — degisken listesi guncellendiginde cagrilir
 *   onClose     fn()        — panel kapatilirken
 *
 * Type kodlari: int | bool | string | decimal | date
 */
import React, { useState, useEffect } from 'react'

var TYPE_OPTIONS = [
  { code: 'int',     label: 'Tam Sayı (int)',  placeholder: '0' },
  { code: 'decimal', label: 'Ondalık (decimal)', placeholder: '0.00' },
  { code: 'bool',    label: 'Mantıksal (bool)',  placeholder: 'false' },
  { code: 'string',  label: 'Metin (string)',    placeholder: '' },
  { code: 'date',    label: 'Tarih (date)',      placeholder: 'YYYY-MM-DD' },
]

// Variable name kuralları: harf veya _ ile başlar, harf/rakam/_ devam eder; en fazla 60 char.
var NAME_RE = /^[A-Za-z_][A-Za-z0-9_]{0,59}$/

export default function VariablesPanel({ open, variables, onChange, onClose }) {
  var [list, setList] = useState(Array.isArray(variables) ? variables : [])
  // invalidIdx: geçersiz satırların index seti. shakeNonce: shake animasyonunu re-tetiklemek için.
  var [invalidIdx, setInvalidIdx] = useState({ set: {}, nonce: 0 })

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
      id: 0, name: nextName, typeCode: 'int', defaultValue: '0', description: '', sortOrder: list.length,
    }]))
  }

  function delRow(idx) {
    commit(list.filter(function (_, i) { return i !== idx }))
    clearInvalid(idx)
  }

  function patchRow(idx, patch) {
    var next = list.map(function (v, i) { return i === idx ? Object.assign({}, v, patch) : v })
    // Kullanici ad alanini duzenliyorsa o satirin "kotu" isaretini kaldir — yeniden Tamam'da kontrol edilir
    if (patch.name != null) clearInvalid(idx)
    commit(next)
  }

  // Tamam butonu — tum satirlari validate et. Gecersizleri shake animasyonu ile isaretle,
  // panel KAPANMAZ. Hepsi gecerliyse onClose cagrilir.
  function handleOk() {
    var bad = {}
    var lc = list.map(function (v) { return (v.name || '').toLowerCase() })
    list.forEach(function (v, i) {
      var nm = (v.name || '').trim()
      if (!nm) { bad[i] = true; return }
      if (!NAME_RE.test(nm)) { bad[i] = true; return }
      // duplicate?
      if (lc.filter(function (x) { return x === nm.toLowerCase() }).length > 1) bad[i] = true
    })
    var keys = Object.keys(bad)
    if (keys.length === 0) {
      if (typeof onClose === 'function') onClose()
      return
    }
    setInvalidIdx(function (prev) { return { set: bad, nonce: prev.nonce + 1 } })
  }

  // Esc tuşu ile kapatma — sadece açıkken bağla, kapanınca temizle.
  useEffect(function () {
    if (!open) return
    function onKey(e) {
      if (e.key === 'Escape') {
        e.stopPropagation()
        if (typeof onClose === 'function') onClose()
      }
    }
    window.addEventListener('keydown', onKey)
    return function () { window.removeEventListener('keydown', onKey) }
  }, [open, onClose])

  if (!open) return null

  return (
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
                <th style={{ width: '24%' }}>Ad</th>
                <th style={{ width: '20%' }}>Tip</th>
                <th style={{ width: '18%' }}>Varsayılan</th>
                <th>Açıklama</th>
                <th style={{ width: 40 }}></th>
              </tr>
            </thead>
            <tbody>
              {list.length === 0 && (
                <tr><td colSpan="5" className="afd-vars-empty">Henüz değişken tanımı yok. Ekle.</td></tr>
              )}
              {list.map(function (v, idx) {
                var t = TYPE_OPTIONS.find(function (x) { return x.code === (v.typeCode || 'int') }) || TYPE_OPTIONS[0]
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
                      <select className="afd-vars-input" value={v.typeCode || 'int'}
                              onChange={function (e) { patchRow(idx, { typeCode: e.target.value }) }}>
                        {TYPE_OPTIONS.map(function (o) {
                          return <option key={o.code} value={o.code}>{o.label}</option>
                        })}
                      </select>
                    </td>
                    <td>
                      {v.typeCode === 'bool' ? (
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
                    <td>
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
  )
}
