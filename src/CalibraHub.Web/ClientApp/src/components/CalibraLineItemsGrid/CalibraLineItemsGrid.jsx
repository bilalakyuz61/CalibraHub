/**
 * CalibraLineItemsGrid — Dinamik, satir-ici duzenlenebilir kalem grid'i
 *
 * "Aptal Bilesen, Zeki Veri": Kolonlar + satirlar C#'tan gelen JSON
 * (BuildDocumentLineGridConfig) ile dinamik cizilir. React icinde hardcoded
 * alan ismi / siralama YOK.
 *
 * Glassmorphism container + Tailwind + framer-motion satir animasyonlari.
 *
 * Props:
 *   config: { schemaVersion, columns, rows, labels, footer }
 *   onRowsChange: function(rows) — her degisiklikte cagirilir (vanilla JS bridge)
 *
 * Imperative API (window.CalibraHub.salesLineGrid):
 *   setRows(rows) — initial data load icin (AJAX'tan gelen lines)
 *   getRows()     — save flow icin
 */
import { useState, useCallback, useEffect, useRef, useMemo } from 'react'
import { createPortal } from 'react-dom'
import { motion, AnimatePresence } from 'framer-motion'
import {
  Plus, Trash2, Pencil, Hash, FileText, Ruler, Sigma, DollarSign,
  Percent, Calculator, StickyNote, CircleDot, Lock,
} from 'lucide-react'
import LineGridCell from './LineGridCell'
import { evaluate } from './formulaEvaluator'

/* Lucide icon haritasi — C#'taki icon string'ini React bilesenine cevirir */
var ICON_MAP = {
  Hash: Hash,
  FileText: FileText,
  Ruler: Ruler,
  Sigma: Sigma,
  DollarSign: DollarSign,
  Percent: Percent,
  Calculator: Calculator,
  StickyNote: StickyNote,
}
function resolveIcon(name) {
  return ICON_MAP[name] || CircleDot
}

/* Satir icin benzersiz _uid uret (React key ve yerel takip icin) */
var uidCounter = 0
function makeUid() {
  uidCounter += 1
  return 'row-' + Date.now() + '-' + uidCounter
}

/* Her satir icin computed hucreleri hesaplayip satira gomer.
   Satir save'de ayni sekilde gonderilecektir — server yine kendi hesaplayacak. */
function applyComputed(row, columns) {
  var result = Object.assign({}, row)
  columns.forEach(function(col) {
    if (col.computed && col.formula) {
      result[col.key] = evaluate(col.formula, result)
    }
  })
  return result
}

function TR_FMT(n, precision) {
  if (n == null || isNaN(n)) return '0,00'
  return Number(n).toLocaleString('tr-TR', {
    minimumFractionDigits: precision != null ? precision : 2,
    maximumFractionDigits: precision != null ? precision : 2,
  })
}

export default function CalibraLineItemsGrid(props) {
  var config = props.config || { columns: [], rows: [], labels: {}, footer: {} }
  var allColumns = Array.isArray(config.columns) ? config.columns : []
  // Kolonlari yerlesime gore ayir: satir icinde mi, yoksa satirin altinda mi render edilecek?
  var columns = allColumns.filter(function(c) { return c.placement !== 'row-below' })
  var belowColumns = allColumns.filter(function(c) { return c.placement === 'row-below' })
  var labels = config.labels || {}
  var footer = config.footer || {}
  var onRowsChange = props.onRowsChange

  // ── Silme onay modali ──
  var [deleteTarget, setDeleteTarget] = useState(null)
  // ── Duzeltme modu per satir (kilit/unlock mantigi icin altyapi) ──
  var [editingRowUid, setEditingRowUid] = useState(null)
  // ── "Not ekle" ile acilan satirlar (row-below kolonlarini gostermek icin) ──
  var [openNoteRows, setOpenNoteRows] = useState(function() { return {} })

  // ── State: satirlar ──
  var [rows, setRows] = useState(function() {
    return (config.rows || []).map(function(r) {
      return applyComputed(Object.assign({ _uid: makeUid() }, r), allColumns)
    })
  })

  // Dis tarafa her degisiklikte notify (bridge)
  useEffect(function() {
    if (typeof onRowsChange === 'function') {
      // _uid bridge'in disina sizmasin
      var clean = rows.map(function(r) {
        var copy = Object.assign({}, r)
        delete copy._uid
        return copy
      })
      onRowsChange(clean)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [rows])

  /* ── Imperative API (vanilla JS bridge) ──
     window.CalibraHub.salesLineGrid.{setRows,getRows} */
  useEffect(function() {
    var api = {
      setRows: function(newRows) {
        var next = (newRows || []).map(function(r) {
          return applyComputed(Object.assign({ _uid: makeUid() }, r), allColumns)
        })
        setRows(next)
      },
      getRows: function() {
        return rows.map(function(r) {
          var copy = Object.assign({}, r)
          delete copy._uid
          return copy
        })
      },
    }
    window.CalibraHub = window.CalibraHub || {}
    window.CalibraHub.salesLineGrid = api
    return function() {
      if (window.CalibraHub && window.CalibraHub.salesLineGrid === api) {
        window.CalibraHub.salesLineGrid = null
      }
    }
  }, [rows, columns])

  // ── Hucre degisikligi ──
  var handleCellChange = useCallback(function(rowUid, columnKey, newValue, fillPatch) {
    setRows(function(prev) {
      return prev.map(function(r) {
        if (r._uid !== rowUid) return r
        var next = Object.assign({}, r)
        next[columnKey] = newValue
        if (fillPatch) {
          Object.keys(fillPatch).forEach(function(k) { next[k] = fillPatch[k] })
        }
        return applyComputed(next, allColumns)
      })
    })
  }, [allColumns])

  // ── Yeni satir ekle ──
  function handleAddRow() {
    setRows(function(prev) {
      var blank = { _uid: makeUid() }
      allColumns.forEach(function(c) {
        if (c.type === 'number' || c.type === 'currency' || c.type === 'percent') {
          blank[c.key] = 0
        } else {
          blank[c.key] = ''
        }
      })
      return prev.concat([applyComputed(blank, allColumns)])
    })
  }

  // ── Satir sil ──
  function handleDeleteRow(rowUid) {
    setDeleteTarget(rowUid)
  }
  function confirmDelete() {
    if (!deleteTarget) return
    setRows(function(prev) { return prev.filter(function(r) { return r._uid !== deleteTarget }) })
    setDeleteTarget(null)
  }
  function cancelDelete() {
    setDeleteTarget(null)
  }

  // ── Satir duzelt (kilit/unlock altyapisi) ──
  // Su an icin: duzelt butonu satirin ilk editable input'una fokus verir.
  // row.__canEdit === false ise buton pasif; ileride condition'a gore set edilir.
  function handleEditRow(rowUid) {
    setEditingRowUid(function(prev) { return prev === rowUid ? null : rowUid })
    // İlk editable cell'e fokus (requestAnimationFrame ile DOM hazir olsun)
    requestAnimationFrame(function() {
      var rowEl = document.querySelector('[data-row-uid="' + rowUid + '"]')
      if (!rowEl) return
      var firstInput = rowEl.querySelector('input:not([disabled]), select:not([disabled]), textarea:not([disabled])')
      if (firstInput && typeof firstInput.focus === 'function') firstInput.focus()
    })
  }

  // Row-level flag helpers — default: her ikisi de true
  function canEdit(row) { return row.__canEdit !== false }
  function canDelete(row) { return row.__canDelete !== false }

  // ── Not paneli toggle ──
  // Panel acik: manuel acildi (openNoteRows[uid]) VEYA below kolonlardan en az birinin value'su dolu
  function hasAnyBelowValue(row) {
    for (var i = 0; i < belowColumns.length; i++) {
      var v = row[belowColumns[i].key]
      if (v != null && String(v).trim() !== '') return true
    }
    return false
  }
  function isNoteOpen(row) {
    return openNoteRows[row._uid] === true || hasAnyBelowValue(row)
  }
  function toggleNote(rowUid) {
    setOpenNoteRows(function(prev) {
      var next = Object.assign({}, prev)
      if (next[rowUid]) delete next[rowUid]
      else next[rowUid] = true
      return next
    })
    // Acildiginda below cell'in ilk input'una fokus
    requestAnimationFrame(function() {
      var rowEl = document.querySelector('[data-row-uid="' + rowUid + '"]')
      if (!rowEl) return
      var input = rowEl.querySelector('[data-below-cell] input, [data-below-cell] textarea')
      if (input && typeof input.focus === 'function') input.focus()
    })
  }

  // ── Footer subtotal hesapla ──
  var subtotals = useMemo(function() {
    var out = {}
    if (footer.showSubtotal && Array.isArray(footer.subtotalColumns)) {
      footer.subtotalColumns.forEach(function(colKey) {
        var sum = 0
        rows.forEach(function(r) {
          var v = r[colKey]
          if (typeof v === 'number') sum += v
          else if (v != null && v !== '') {
            var n = parseFloat(String(v).replace(',', '.'))
            if (!isNaN(n)) sum += n
          }
        })
        out[colKey] = sum
      })
    }
    return out
  }, [rows, footer])

  var totalSum = Object.values(subtotals).reduce(function(a, b) { return a + b }, 0)

  // ── Kolon genisligi → CSS width ──
  function widthCss(col) {
    if (col.width === 'flex' || col.width === '*' || !col.width) return { flex: '1 1 0', minWidth: '120px' }
    return { width: col.width + 'px', flex: '0 0 ' + col.width + 'px' }
  }

  return (
    <div className="calibra-line-grid rounded-2xl overflow-hidden border border-slate-200 bg-white/70 dark:bg-white/[0.04] dark:border-white/10 backdrop-blur-xl shadow-sm">
      {/* Header row */}
      <div className="flex items-center border-b border-slate-200 bg-slate-50/80 dark:bg-white/[0.03] dark:border-white/[0.08]">
        <div className="w-[104px] flex-shrink-0 px-2 py-2.5 text-[10px] font-bold uppercase tracking-wider text-slate-500 dark:text-white/50 text-center">
          Islem
        </div>
        {columns.map(function(col) {
          var Icon = resolveIcon(col.icon)
          var align =
            col.align === 'right'  ? 'justify-end'  :
            col.align === 'center' ? 'justify-center' : 'justify-start'
          return (
            <div
              key={col.key}
              className={'flex items-center gap-1.5 px-2.5 py-2.5 text-[10px] font-bold uppercase tracking-wider text-slate-600 dark:text-white/60 ' + align}
              style={widthCss(col)}
            >
              <Icon size={11} strokeWidth={1.8} className="text-slate-400 dark:text-white/40 flex-shrink-0" />
              <span className="truncate">{col.label}</span>
              {col.required && <span className="text-rose-500 dark:text-rose-400">*</span>}
            </div>
          )
        })}
      </div>

      {/* Data rows */}
      <div>
        {rows.length === 0 ? (
          <div className="px-6 py-10 text-center text-[12px] text-slate-400 dark:text-white/30">
            {labels.emptyText || 'Henuz kalem eklenmemis'}
          </div>
        ) : (
          <AnimatePresence initial={false}>
            {rows.map(function(row) {
              return (
                <motion.div
                  key={row._uid}
                  data-row-uid={row._uid}
                  initial={{ opacity: 0, y: -4 }}
                  animate={{ opacity: 1, y: 0 }}
                  exit={{ opacity: 0, y: -4, height: 0 }}
                  transition={{ duration: 0.18 }}
                  className="border-b border-slate-100 hover:bg-slate-50/70 dark:border-white/[0.05] dark:hover:bg-white/[0.02] transition-colors"
                >
                  <div className="flex items-stretch">
                    <div className="w-[104px] flex-shrink-0 flex items-center justify-center gap-1 border-r border-slate-100 dark:border-white/[0.04]">
                      <button
                        type="button"
                        onClick={function() { if (canEdit(row)) handleEditRow(row._uid) }}
                        disabled={!canEdit(row)}
                        className={'w-7 h-7 rounded-lg flex items-center justify-center transition-colors ' + (
                          !canEdit(row)
                            ? 'text-slate-300 dark:text-white/15 cursor-not-allowed'
                            : (editingRowUid === row._uid
                                ? 'text-indigo-600 bg-indigo-50 dark:text-indigo-300 dark:bg-indigo-500/15'
                                : 'text-slate-400 hover:text-indigo-500 hover:bg-indigo-50 dark:text-white/30 dark:hover:text-indigo-300 dark:hover:bg-indigo-500/10')
                        )}
                        title={canEdit(row) ? 'Duzelt' : 'Bu satir duzeltilemez'}
                      >
                        {canEdit(row) ? <Pencil size={13} strokeWidth={1.8} /> : <Lock size={12} strokeWidth={1.8} />}
                      </button>
                      {belowColumns.length > 0 && (
                        <button
                          type="button"
                          onClick={function() { toggleNote(row._uid) }}
                          className={'w-7 h-7 rounded-lg flex items-center justify-center transition-colors ' + (
                            isNoteOpen(row)
                              ? 'text-amber-600 bg-amber-50 dark:text-amber-300 dark:bg-amber-500/15'
                              : 'text-slate-400 hover:text-amber-500 hover:bg-amber-50 dark:text-white/30 dark:hover:text-amber-300 dark:hover:bg-amber-500/10'
                          )}
                          title={isNoteOpen(row) ? (hasAnyBelowValue(row) ? 'Not dolu — gizle' : 'Notu kapat') : 'Not ekle'}
                        >
                          <StickyNote size={13} strokeWidth={1.8} />
                        </button>
                      )}
                      <button
                        type="button"
                        onClick={function() { if (canDelete(row)) handleDeleteRow(row._uid) }}
                        disabled={!canDelete(row)}
                        className={'w-7 h-7 rounded-lg flex items-center justify-center transition-colors ' + (
                          !canDelete(row)
                            ? 'text-slate-300 dark:text-white/15 cursor-not-allowed'
                            : 'text-slate-400 hover:text-rose-500 hover:bg-rose-50 dark:text-white/30 dark:hover:text-rose-400 dark:hover:bg-rose-500/10'
                        )}
                        title={canDelete(row) ? 'Sil' : 'Bu satir silinemez'}
                      >
                        {canDelete(row) ? <Trash2 size={13} strokeWidth={1.8} /> : <Lock size={12} strokeWidth={1.8} />}
                      </button>
                    </div>
                    {columns.map(function(col) {
                      return (
                        <div
                          key={col.key}
                          className="flex items-center border-r border-slate-100 last:border-r-0 dark:border-white/[0.04]"
                          style={widthCss(col)}
                        >
                          <LineGridCell
                            column={col}
                            row={row}
                            value={row[col.key]}
                            onChange={function(k, v, fill) { handleCellChange(row._uid, k, v, fill) }}
                          />
                        </div>
                      )
                    })}
                  </div>

                  {/* Satir alti kolonlar (placement: row-below) — ornegin "Not".
                      Panel sadece kullanici "Not ekle" butonuna basinca VEYA not doluysa gorunur. */}
                  {belowColumns.length > 0 && isNoteOpen(row) && (
                    <div className="flex flex-col gap-1 pl-3 pr-3 pb-2 pt-1 border-t border-slate-100 dark:border-white/[0.06]">
                      {belowColumns.map(function(col) {
                        var Icon = resolveIcon(col.icon)
                        return (
                          <div
                            key={col.key}
                            data-below-cell
                            className="flex items-center gap-2 rounded-md border border-slate-100 bg-slate-50/60 dark:border-white/[0.06] dark:bg-white/[0.02]"
                          >
                            <div className="flex items-center gap-1.5 pl-2.5 py-1.5 text-[10px] font-semibold uppercase tracking-wider text-slate-500 dark:text-white/50 flex-shrink-0">
                              <Icon size={11} strokeWidth={1.8} className="text-slate-400 dark:text-white/40 flex-shrink-0" />
                              <span>{col.label}</span>
                            </div>
                            <div className="flex-1 min-w-0">
                              <LineGridCell
                                column={col}
                                row={row}
                                value={row[col.key]}
                                onChange={function(k, v, fill) { handleCellChange(row._uid, k, v, fill) }}
                              />
                            </div>
                          </div>
                        )
                      })}
                    </div>
                  )}
                </motion.div>
              )
            })}
          </AnimatePresence>
        )}
      </div>

      {/* Footer: Yeni kalem + toplam */}
      <div className="flex items-center justify-between px-3 py-2.5 border-t border-slate-200 bg-slate-50/60 dark:bg-white/[0.02] dark:border-white/[0.08]">
        <motion.button
          type="button"
          whileTap={{ scale: 0.97 }}
          onClick={handleAddRow}
          className="flex items-center gap-2 px-3 py-1.5 rounded-lg text-[12px] font-semibold bg-indigo-50 text-indigo-600 border border-indigo-200 hover:bg-indigo-100 dark:bg-indigo-500/15 dark:text-indigo-300 dark:border-indigo-400/30 dark:hover:bg-indigo-500/25 transition-colors"
        >
          <Plus size={13} strokeWidth={2.2} />
          <span>{labels.addRow || 'Yeni Kalem'}</span>
        </motion.button>

        {footer.showSubtotal && rows.length > 0 && (
          <div className="flex items-center gap-3 text-[12px]">
            <span className="text-slate-500 dark:text-white/40 uppercase tracking-wider font-semibold text-[10px]">
              {labels.totalLabel || 'Toplam'}
            </span>
            <span className="font-mono tabular-nums text-amber-600 dark:text-amber-300 text-[15px] font-bold">
              {TR_FMT(totalSum, 2)} ₺
            </span>
          </div>
        )}
      </div>

      {/* Silme onay modali — portal ile document.body'e render edilir */}
      <AnimatePresence>
        {deleteTarget && createPortal(
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            style={{
              position: 'fixed', inset: 0, zIndex: 9999,
              background: 'rgba(0,0,0,.55)', backdropFilter: 'blur(4px)',
              display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 20,
            }}
            onClick={cancelDelete}
          >
            <motion.div
              initial={{ scale: 0.95, y: -8 }}
              animate={{ scale: 1, y: 0 }}
              exit={{ scale: 0.95, y: -8 }}
              onClick={function(e) { e.stopPropagation() }}
              className="rounded-2xl overflow-hidden text-center"
              style={{
                background: 'var(--lig-modal-bg, #1e293b)',
                border: '1px solid rgba(255,255,255,.12)',
                padding: '32px 28px', maxWidth: 380, width: '90vw',
                boxShadow: '0 24px 64px rgba(0,0,0,.5)',
                display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 12,
              }}
            >
              <Trash2 size={26} style={{ color: '#ef4444' }} />
              <h3 style={{ fontSize: '1.05rem', fontWeight: 700, color: '#f1f5f9', margin: 0 }}>
                Kalemi Sil
              </h3>
              <p style={{ fontSize: '.84rem', color: '#94a3b8', margin: 0 }}>
                {labels.deleteConfirm || 'Bu kalem silinecek. Devam edilsin mi?'}
              </p>
              <div style={{ display: 'flex', gap: 10, marginTop: 8 }}>
                <button
                  type="button"
                  onClick={cancelDelete}
                  style={{
                    padding: '8px 16px', borderRadius: 8, fontSize: '.84rem', fontWeight: 600,
                    background: 'rgba(255,255,255,.07)', color: '#f1f5f9',
                    border: '1px solid rgba(255,255,255,.1)', cursor: 'pointer',
                  }}
                >
                  İptal
                </button>
                <button
                  type="button"
                  onClick={confirmDelete}
                  style={{
                    padding: '8px 16px', borderRadius: 8, fontSize: '.84rem', fontWeight: 600,
                    background: 'linear-gradient(135deg,#ef4444,#dc2626)', color: '#fff',
                    border: 'none', cursor: 'pointer',
                    display: 'inline-flex', alignItems: 'center', gap: 6,
                  }}
                >
                  <Trash2 size={13} /> Evet, Sil
                </button>
              </div>
            </motion.div>
          </motion.div>,
          document.body
        )}
      </AnimatePresence>
    </div>
  )
}
