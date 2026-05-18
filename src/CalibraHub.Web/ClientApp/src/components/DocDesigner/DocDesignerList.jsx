import React, { useEffect, useState } from 'react'
import { listLayouts, deleteLayout } from './services/docDesignerService'

const DOC_TYPE_LABELS = {
  sales_quote:     'Satış Teklifi',
  sales_order:     'Satış Siparişi',
  purchase_order:  'Satın Alma Siparişi',
  delivery_note:   'İrsaliye',
  invoice:         'Fatura',
  expense_note:    'Gider Fişi',
  custom:          'Özel',
}

export default function DocDesignerList() {
  const [layouts, setLayouts] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError]     = useState(null)

  const load = () => {
    setLoading(true)
    listLayouts(null)
      .then(data => { setLayouts(data); setLoading(false) })
      .catch(ex  => { setError(ex.message); setLoading(false) })
  }

  useEffect(() => { load() }, [])

  const handleDelete = async id => {
    // Rapor §6.6 — CalibraAlert.confirm fallback
    const ok = window.CalibraAlert && window.CalibraAlert.confirm
      ? await window.CalibraAlert.confirm('Bu şablonu silmek istiyor musunuz?',
          { title: 'Şablonu Sil', okText: 'Evet, Sil', cancelText: 'Vazgeç', danger: true })
      : confirm('Bu şablonu silmek istiyor musunuz?')
    if (!ok) return
    try { await deleteLayout(id); load() }
    catch (ex) {
      if (window.CalibraAlert && window.CalibraAlert.error) window.CalibraAlert.error(ex.message)
      else if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast(ex.message, 'err')
      else alert(ex.message)
    }
  }

  return (
    <div style={{ padding: 24 }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 20 }}>
        <div>
          <h2 style={{ margin: 0, fontSize: 20, fontWeight: 700 }}>Belge Tasarımcısı</h2>
          <p style={{ margin: '4px 0 0', color: '#6b7280', fontSize: 13 }}>
            Belge şablonlarını yönetin ve tasarlayın
          </p>
        </div>
        <a href="/DocDesigner/New"
          style={{
            display: 'inline-flex', alignItems: 'center', gap: 6, padding: '8px 16px',
            background: '#6366f1', color: '#fff', borderRadius: 8, textDecoration: 'none',
            fontSize: 13, fontWeight: 600,
          }}>
          + Yeni Şablon
        </a>
      </div>

      {loading && <div style={{ color: '#9ca3af', fontSize: 13 }}>Yükleniyor...</div>}
      {error   && <div style={{ color: '#dc2626', fontSize: 13 }}>Hata: {error}</div>}

      {!loading && !error && layouts.length === 0 && (
        <div style={{
          textAlign: 'center', padding: '60px 20px', color: '#9ca3af',
          border: '2px dashed #e5e7eb', borderRadius: 12,
        }}>
          <div style={{ fontSize: 32, marginBottom: 12 }}>📄</div>
          <div style={{ fontSize: 14, fontWeight: 600 }}>Henüz şablon yok</div>
          <div style={{ fontSize: 12, marginTop: 4 }}>Yeni Şablon butonuna tıklayarak başlayın</div>
        </div>
      )}

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(280px, 1fr))', gap: 16 }}>
        {layouts.map(l => (
          <div key={l.id} style={{
            background: '#fff', border: '1px solid #e5e7eb', borderRadius: 10,
            padding: 16, boxShadow: '0 1px 3px rgba(0,0,0,0.05)',
            display: 'flex', flexDirection: 'column', gap: 8,
          }}>
            <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between' }}>
              <div>
                <div style={{ fontWeight: 700, fontSize: 14 }}>{l.name}</div>
                <div style={{ color: '#6b7280', fontSize: 11, marginTop: 2 }}>{l.code}</div>
              </div>
              <span style={{
                fontSize: 10, padding: '2px 8px', borderRadius: 12,
                background: '#ede9fe', color: '#6366f1', fontWeight: 600, whiteSpace: 'nowrap',
              }}>
                {DOC_TYPE_LABELS[l.docType] ?? l.docType}
              </span>
            </div>

            <div style={{ color: '#9ca3af', fontSize: 11 }}>
              Son güncelleme: {new Date(l.updatedAt).toLocaleDateString('tr-TR')}
            </div>

            <div style={{ display: 'flex', gap: 8, marginTop: 4 }}>
              <a href={`/DocDesigner/Edit/${l.id}`}
                style={{
                  flex: 1, textAlign: 'center', padding: '6px 0', borderRadius: 6,
                  background: '#f3f4f6', color: '#374151', textDecoration: 'none', fontSize: 12,
                }}>
                Düzenle
              </a>
              <button onClick={() => handleDelete(l.id)}
                style={{
                  padding: '6px 12px', borderRadius: 6, border: 'none',
                  background: '#fee2e2', color: '#dc2626', cursor: 'pointer', fontSize: 12,
                }}>
                Sil
              </button>
            </div>
          </div>
        ))}
      </div>
    </div>
  )
}
