import React from 'react'

export default function PreviewModal({ html, onClose }) {
  return (
    <div style={{
      position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.6)', zIndex: 2000,
      display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
      padding: 24,
    }} onClick={e => { if (e.target === e.currentTarget) onClose() }}>
      <div style={{
        background: '#fff', borderRadius: 12, width: '100%', maxWidth: 900,
        height: '90vh', display: 'flex', flexDirection: 'column',
        boxShadow: '0 24px 80px rgba(0,0,0,0.25)',
      }}>
        <div style={{
          padding: '12px 16px', borderBottom: '1px solid #e5e7eb',
          display: 'flex', alignItems: 'center', justifyContent: 'space-between',
        }}>
          <span style={{ fontSize: 14, fontWeight: 600 }}>Belge Önizleme</span>
          <button onClick={onClose}
            style={{ background: 'none', border: 'none', fontSize: 20, cursor: 'pointer', color: '#6b7280' }}>×</button>
        </div>
        <iframe
          srcDoc={html}
          style={{ flex: 1, border: 'none', borderRadius: '0 0 12px 12px' }}
          title="Belge Önizleme"
          sandbox="allow-same-origin"
        />
      </div>
    </div>
  )
}
