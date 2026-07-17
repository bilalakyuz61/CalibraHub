/**
 * ViewBuilderPreview — Önizleme panelı.
 *
 * POST /preview sonucunu gösterir: hata listesi, kartezyen çarpım uyarısı,
 * üretilen kolon listesi + örnek satırlar (en fazla ~20, backend Top ile sınırlar).
 *
 * Hiçbir kalıcı değişiklik yapmaz — salt görüntüleme bileşenidir.
 */
import React from 'react'
import { AlertTriangle, Loader2, XCircle, TableProperties } from 'lucide-react'

function formatCell(value) {
  if (value === null || value === undefined) {
    return <span className="vb-null-cell">NULL</span>
  }
  if (typeof value === 'boolean') return value ? 'true' : 'false'
  if (typeof value === 'object') {
    try { return JSON.stringify(value) } catch (e) { return String(value) }
  }
  return String(value)
}

export default function ViewBuilderPreview({ preview, loading }) {
  if (loading) {
    return (
      <div className="vb-preview-empty" style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
        <Loader2 size={14} className="vb-spin" /> Önizleme çalıştırılıyor…
      </div>
    )
  }

  if (!preview) {
    return (
      <div className="vb-preview-empty">
        Henüz önizleme yapılmadı. Yukarıdaki <strong>Önizle</strong> butonuna basın.
      </div>
    )
  }

  var errors = preview.errors || []
  var hasErrors = !preview.success || errors.length > 0

  return (
    <div>
      {hasErrors && (
        <div className="vb-preview-errors">
          <div style={{ display: 'flex', alignItems: 'center', gap: 6, fontWeight: 700 }}>
            <XCircle size={14} /> Önizleme başarısız
          </div>
          {errors.length > 0 && (
            <ul>
              {errors.map(function (e, i) { return <li key={i}>{e}</li> })}
            </ul>
          )}
        </div>
      )}

      {preview.success && preview.cartesianWarning && (
        <div className="vb-cartesian-banner">
          <AlertTriangle size={16} style={{ flexShrink: 0, marginTop: 1 }} />
          <span>
            <strong>Kartezyen çarpım riski.</strong> Taban nesne{' '}
            <strong>{preview.baseRowCount ?? '?'}</strong> satır verirken, join sonrası sonuç{' '}
            <strong>{preview.totalRowCount ?? '?'}</strong> satıra çıktı
            {preview.cartesianFactor != null ? <> (≈ <strong>{preview.cartesianFactor}×</strong>)</> : null}.
            ON koşullarını gözden geçirin — eksik/yanlış bir eşleşme kartezyen ürün üretiyor olabilir.
          </span>
        </div>
      )}

      {preview.success && (
        <div className="vb-preview-stats">
          <span>Taban satır: <strong>{preview.baseRowCount ?? '—'}</strong></span>
          <span>Sonuç satır: <strong>{preview.totalRowCount ?? '—'}</strong></span>
          {preview.cartesianFactor != null && <span>Çarpan: <strong>{preview.cartesianFactor}×</strong></span>}
          <span>Kolon: <strong>{(preview.columns || []).length}</strong></span>
          <span>Örnek satır: <strong>{(preview.sampleRows || []).length}</strong></span>
        </div>
      )}

      {preview.success && (preview.columns || []).length > 0 && (
        <div className="vb-preview-table-wrap">
          <table className="vb-preview-table">
            <thead>
              <tr>
                {preview.columns.map(function (c) {
                  return (
                    <th key={c.name}>
                      {c.name}
                      <small>{c.sqlType}{c.isNullable ? ' · null' : ''}</small>
                    </th>
                  )
                })}
              </tr>
            </thead>
            <tbody>
              {(preview.sampleRows || []).map(function (row, ri) {
                return (
                  <tr key={ri}>
                    {preview.columns.map(function (c) {
                      return <td key={c.name}>{formatCell(row[c.name])}</td>
                    })}
                  </tr>
                )
              })}
              {(preview.sampleRows || []).length === 0 && (
                <tr>
                  <td colSpan={preview.columns.length} style={{ color: 'var(--vb-muted)', fontStyle: 'italic' }}>
                    Sonuç kümesi boş — sorgu çalıştı ama satır dönmedi.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      )}

      {preview.success && (preview.columns || []).length === 0 && (
        <div className="vb-preview-empty" style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
          <TableProperties size={14} /> Kolon bulunamadı.
        </div>
      )}
    </div>
  )
}
