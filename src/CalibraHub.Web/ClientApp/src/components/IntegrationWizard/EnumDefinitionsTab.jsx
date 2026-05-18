/**
 * EnumDefinitionsTab — IntegrationsHub içindeki "Enum Tanımları" tab'i.
 *
 * Mevcut /IntegrationDocCatalog/Enums Razor sayfasıyla aynı SmartBoard'u mount eder;
 * config /IntegrationDocCatalog/Enums/BoardEntities endpoint'inden gelir (in-place
 * refresh URL'i de bu — kart üzerinde toggle/delete sonrası SmartBoard kendini günceller).
 *
 * Yeni / düzenle eylemleri /IntegrationDocCatalog/EnumEdit Razor sayfasına navigate eder
 * (bu sayfa zaten var, embed etmedik — modal/full-screen edit Razor tarafında tutuluyor).
 */
import React, { useEffect, useRef, useState } from 'react'
import { Loader2, AlertCircle } from 'lucide-react'

export default function EnumDefinitionsTab() {
  const mountRef = useRef(null)
  const [status, setStatus] = useState('loading')   // loading | ready | error
  const [error, setError]   = useState(null)

  useEffect(() => {
    let cancelled = false

    async function load() {
      try {
        const r = await fetch('/IntegrationDocCatalog/Enums/BoardEntities',
                              { credentials: 'same-origin' })
        if (!r.ok) throw new Error(`HTTP ${r.status}`)
        const config = await r.json()
        if (cancelled) return

        if (!window.CalibraHub?.mountSmartBoard) {
          throw new Error('mountSmartBoard fonksiyonu yok (bundle henuz hazir degil).')
        }
        if (!mountRef.current) return
        // Mount: SmartBoard kendi içinde refreshUrl'yi okur, in-place refresh çalışır
        window.CalibraHub.mountSmartBoard(mountRef.current, config)
        setStatus('ready')
      } catch (ex) {
        if (cancelled) return
        setError(ex?.message || String(ex))
        setStatus('error')
      }
    }

    load()
    return () => { cancelled = true }
  }, [])

  return (
    <div style={{
      display: 'flex', flexDirection: 'column',
      width: '100%', height: '100%', minHeight: 0,
    }}>
      {status === 'loading' && (
        <div style={{
          display: 'flex', alignItems: 'center', justifyContent: 'center',
          gap: 8, padding: 32, color: 'var(--iw-muted, #94a3b8)', fontSize: 13,
        }}>
          <Loader2 size={16} className="iw-spin" />
          <span>Enum tanımları yükleniyor…</span>
        </div>
      )}
      {status === 'error' && (
        <div style={{
          display: 'flex', alignItems: 'center', gap: 8, padding: 16,
          color: '#f87171', fontSize: 13, background: 'rgba(239,68,68,.08)',
          border: '1px solid rgba(239,68,68,.25)', borderRadius: 8, margin: 16,
        }}>
          <AlertCircle size={16} />
          <span>Yüklenemedi: {error}</span>
        </div>
      )}
      <div ref={mountRef}
           style={{
             flex: '1 1 auto', minHeight: 0, overflow: 'hidden',
             display: status === 'ready' ? 'flex' : 'none',
             flexDirection: 'column',
           }} />
    </div>
  )
}
