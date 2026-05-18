/**
 * IntegrationsHub — Tek sayfada 6 tab (tum entegrasyon yonetimi tek nokta):
 *   1) Profiller        — API Profile (auth + base URL) yönetimi
 *   2) Endpointler      — REST endpoint kataloğu (list + CRUD)
 *   3) Entegrasyonlar   — mevcut entegrasyon kurguları (Wizard 5-step ayri sayfa)
 *   4) Aktarım Kuyruğu  — manual trigger + bekleyen/hatalı kayıt yönetimi
 *   5) Enum Tanımları   — IntegrationDocCatalog SmartBoard (kullanim tooltip kaynagi)
 *   6) Çalıştırma Logu  — IntegrationRun audit
 *
 * Seçili tab URL hash'iyle paylaşılabilir: /Integrations#queue, /Integrations#enums
 */
import React, { useState, useEffect, useCallback } from 'react'
import { Plug, Globe, Activity, KeyRound, Send, Sigma } from 'lucide-react'
import IntegrationApiProfilesList from './IntegrationApiProfilesList'
import IntegrationsList from './IntegrationsList'
import IntegrationEndpointsList from './IntegrationEndpointsList'
import IntegrationRunsList from './IntegrationRunsList'
import IntegrationQueue from './IntegrationQueue'
import EnumDefinitionsTab from './EnumDefinitionsTab'

const TABS = [
  { id: 'profiles',     label: 'Profiller',           icon: KeyRound },
  { id: 'endpoints',    label: 'Endpointler',         icon: Globe },
  { id: 'integrations', label: 'Entegrasyonlar',      icon: Plug },
  { id: 'queue',        label: 'Aktarım Kuyruğu',     icon: Send },
  { id: 'enums',        label: 'Enum Tanımları',      icon: Sigma },
  { id: 'runs',         label: 'Çalıştırma Logu',     icon: Activity },
]

export default function IntegrationsHub({ config }) {
  // İlk tab seçimi: config.initialTab > URL hash > "profiles"
  const initial = (() => {
    if (config?.initialTab && TABS.find(t => t.id === config.initialTab)) return config.initialTab
    const hash = window.location.hash.replace('#', '')
    if (TABS.find(t => t.id === hash)) return hash
    return 'profiles'
  })()
  const [tab, setTab] = useState(initial)

  // Tab değişince URL hash'i güncelle (back/forward + paylaşılabilirlik için)
  useEffect(() => {
    const newHash = '#' + tab
    if (window.location.hash !== newHash) {
      // pushState yerine replaceState — back butonu sayfaları kirletmesin
      window.history.replaceState(null, '', window.location.pathname + newHash)
    }
  }, [tab])

  // Browser back/forward → tab senkron
  useEffect(() => {
    const onPop = () => {
      const h = window.location.hash.replace('#', '')
      if (TABS.find(t => t.id === h)) setTab(h)
    }
    window.addEventListener('popstate', onPop)
    return () => window.removeEventListener('popstate', onPop)
  }, [])

  return (
    <div className="ih-root">
      {/* Tab switcher — sayfa üstünde geniş, ortada */}
      <div className="ih-tabs">
        {TABS.map(t => {
          const Icon = t.icon
          return (
            <button key={t.id}
                    className={'ih-tab' + (tab === t.id ? ' is-active' : '')}
                    onClick={() => setTab(t.id)}>
              <Icon size={14} />
              <span>{t.label}</span>
            </button>
          )
        })}
      </div>

      {/* Tab content — her tab kendi component'ini render eder; key ile re-mount edilir */}
      <div className="ih-content">
        {tab === 'profiles' && (
          <IntegrationApiProfilesList key="profiles" config={config} />
        )}
        {tab === 'endpoints' && (
          <IntegrationEndpointsList key="endpoints" config={config} />
        )}
        {tab === 'integrations' && (
          <IntegrationsList key="integrations" config={config} />
        )}
        {tab === 'queue' && (
          <IntegrationQueue key="queue" config={config} />
        )}
        {tab === 'enums' && (
          <EnumDefinitionsTab key="enums" />
        )}
        {tab === 'runs' && (
          <IntegrationRunsList key="runs" config={config} />
        )}
      </div>
    </div>
  )
}
