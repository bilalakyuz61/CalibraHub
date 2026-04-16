import React from 'react'
import { AlertTriangle } from 'lucide-react'

export default class ErrorBoundary extends React.Component {
  constructor(props) {
    super(props)
    this.state = { hasError: false, error: null }
  }

  static getDerivedStateFromError(error) {
    return { hasError: true, error }
  }

  componentDidCatch(error, info) {
    console.error('[ErrorBoundary]', error, info.componentStack)
  }

  render() {
    if (this.state.hasError) {
      return (
        <div style={{
          display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
          height: '100%', minHeight: 200, padding: 40, background: '#0c0f1a', color: '#94a3b8',
        }}>
          <AlertTriangle size={32} style={{ color: '#f59e0b', marginBottom: 12 }} />
          <p style={{ fontSize: 14, fontWeight: 600, marginBottom: 4 }}>Bir hata olustu</p>
          <p style={{ fontSize: 12, color: '#64748b', marginBottom: 16 }}>
            {this.state.error?.message || 'Bilinmeyen hata'}
          </p>
          <button
            onClick={() => window.location.reload()}
            style={{
              padding: '8px 20px', borderRadius: 8, border: '1px solid #334155',
              background: '#1e293b', color: '#e2e8f0', fontSize: 13, cursor: 'pointer',
            }}
          >
            Sayfayi Yenile
          </button>
        </div>
      )
    }
    return this.props.children
  }
}
