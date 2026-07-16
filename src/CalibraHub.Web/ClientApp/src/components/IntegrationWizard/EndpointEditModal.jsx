/**
 * EndpointEditModal — Yeni endpoint yarat veya mevcut endpoint'i düzenle.
 *
 * Layout: sol tab menüsü + sağ icerik + footer (CLAUDE.md tabbed-modal standardı).
 * Body Schema tab'i full sağ alanı kullanır (JSON için bol yer).
 * "⛶" butonu ile tam ekran moduna geçilir (büyük JSON için ideal).
 *
 * Tabs:
 *   1. Temel        — Profile, ad, method, URL, açıklama, aktif
 *   2. Body Schema  — JSON şeması (textarea, monospace, full area)
 *
 * Hem Wizard Step 2'de inline hem admin sayfasında kullanılır.
 */
import React, { useState, useEffect, useLayoutEffect, useRef, useMemo } from 'react'
import { createPortal } from 'react-dom'
import { Save, X, Loader2, Settings2, FileCode, Maximize2, Minimize2, Sparkles, Search, Download, BookmarkPlus, Trash2, ChevronDown, KeyRound } from 'lucide-react'

function getCsrf() {
  const el = document.querySelector('input[name="__RequestVerificationToken"]')
  return el ? el.value : ''
}
function toast(msg, kind) {
  if (window.CalibraHub?.toast) window.CalibraHub.toast(msg, kind || 'info')
}

const HTTP_METHODS = ['POST', 'PUT', 'PATCH', 'GET', 'DELETE']

const PRIMITIVE_TYPES = ['String', 'Int32', 'Int64', 'Boolean', 'Decimal', 'Double', 'DateTime', 'Guid']

/**
 * Endpoint katalogundaki InputType'ı sınıflandır — Otomatik Çek'in nasıl
 * davranması gerektiğini belirler.
 *
 *   "ARPs" | "ItemSlips" | "Items"        → 'resource' (Describe ile alınabilir)
 *   "ARPsRiskParam" | "OrderBalancingPrm" → 'param' (özel parametre, Describe yok)
 *   "SelectFilter"                        → 'filter' (query filter, body değil)
 *   "String" | "Int32"                    → 'primitive' (basit tek değer)
 *   "oAuth2"                              → 'auth' (OAuth flow)
 *   ""/null                               → 'none' (body istemiyor)
 */
function classifyInputType(inputType) {
  if (!inputType) return 'none'
  const raw = String(inputType).trim()
  if (!raw) return 'none'

  // Kompozit InputType: "String, ItemSlips" (PUT ID+body)
  // Tek tek parça için sınıflandır, en "ağır" olanı kazanır:
  // resource > param > filter > primitive > auth > none
  if (raw.includes(',')) {
    const parts = raw.split(',').map(p => p.trim()).filter(Boolean)
    const kinds = parts.map(classifyInputType)
    const priority = ['resource', 'param', 'filter', 'primitive', 'auth', 'none']
    for (const p of priority) if (kinds.includes(p)) return p
    return 'none'
  }

  if (raw === 'SelectFilter') return 'filter'
  if (/^o?Auth/i.test(raw)) return 'auth'
  if (PRIMITIVE_TYPES.includes(raw)) return 'primitive'
  if (/Param$/i.test(raw) || /Prm$/i.test(raw)) return 'param'
  return 'resource'  // Standart entity tipi — Describe ile alınabilir
}

/**
 * Kompozit InputType ("String, ItemSlips") içinden Describe için kullanılacak
 * entity tipini cikarir. classifyInputType bunu 'resource' olarak donduruyor
 * ama Describe URL'i icin entity adina ihtiyac var.
 *
 * "String, ItemSlips" → "ItemSlips" (ilk resource tipini secer)
 * "ItemSlips"         → "ItemSlips"
 * "String"            → null
 */
function extractResourceInputType(inputType) {
  if (!inputType) return null
  const parts = String(inputType).split(',').map(p => p.trim()).filter(Boolean)
  for (const p of parts) if (classifyInputType(p) === 'resource') return p
  return null
}

/** InputType sınıfı için chip rengi */
function inputTypeColor(kind) {
  return ({
    resource:  'emerald',  // Describe ile çekilebilir — yeşil
    param:     'amber',    // Özel parametre, manuel/şablon — kehribar
    filter:    'indigo',   // Filtre, body değil
    primitive: 'slate',    // Basit değer
    auth:      'rose',     // Auth özel
    none:      'slate',
  }[kind] || 'slate')
}

/** İnsan dostu kısa açıklama */
function inputTypeLabel(kind) {
  return ({
    resource:  'Standart entity (Describe ile çekilebilir)',
    param:     'Özel parametre tipi (Describe ile alınamaz)',
    filter:    'Query filtresi (body değil)',
    primitive: 'Tek değer (string/sayı)',
    auth:      'Kimlik doğrulama akışı',
    none:      'Body istemiyor',
  }[kind] || kind)
}

/**
 * Backend'in döndürdüğü çoklu-katman error mesajını parse eder.
 * Format: "Describe: HTTP X | Sample GET: HTTP Y | Probe: HTTP Z"
 * Her katman ayrı satır olarak döner (UI'da liste halinde gösterilir).
 *
 * Tek katman hatası varsa (eski format) tek satır döner.
 */
function parseAttempts(rawError) {
  if (!rawError) return []
  const parts = String(rawError).split('|').map(s => s.trim()).filter(Boolean)
  return parts.map(p => {
    // "LayerName: detay" → ayır
    const colonIdx = p.indexOf(':')
    if (colonIdx > 0 && colonIdx < 25) {
      return { layer: p.substring(0, colonIdx).trim(), detail: p.substring(colonIdx + 1).trim() }
    }
    return { layer: 'Hata', detail: p }
  })
}

/** Katman adına insan dostu Türkçe etiket */
function layerLabel(layer) {
  return ({
    'Describe':   'Describe (alan şeması)',
    'SampleGet':  'Sample GET (mevcut kayıt)',
    'Sample GET': 'Sample GET (mevcut kayıt)',
    'Probe':      'POST Probe (boş body)',
    'Hata':       'Hata',
  }[layer] || layer)
}

/**
 * Otomatik Çek başarısız mesajını analiz et — bilinen pattern varsa
 * Türkçe friendly başlık + sebep listesi döner. Bilinmeyen ise default.
 *
 * Örnekler:
 *   "OAuth2Password token alinamadi (HTTP 400 Bad Request) ... invalid_grant"
 *     → Netsis credentials/DB sorunu
 *   "HTTP 401" → Auth eksik
 *   "HTTP 405" → Describe yok / yanlış method
 *   "HTTP 404" → Endpoint/base URL yanlış
 *   "timeout"  → Bağlanılamadı
 */
function diagnoseResolveError(rawError) {
  if (!rawError) return null
  const e = String(rawError)
  const lower = e.toLowerCase()

  if (lower.includes('invalid_grant') || lower.includes('e_unexpected')) {
    return {
      kind: 'auth',
      title: 'Netsis kimlik doğrulama başarısız',
      lead: 'Token endpoint\'e ulaşıldı ancak kullanıcı/şifre veya DB bağlantı parametreleri kabul edilmedi.',
      reasons: [
        '**DbPassword boş** olabilir — Şirket Ayarları → Entegrasyon API → Netsis Ek Alanlar bölümünü kontrol edin',
        'NetOpenX kullanıcı şifresi yanlış (üst kısımdaki "Sifre" alanı)',
        'DbName / DbUser değerleri Netsis SQL Server\'daki adlarla eşleşmiyor',
        'Netsis NoxRest servisi DB\'ye bağlanamıyor (servis hesabı / firewall)',
      ],
    }
  }
  if (lower.includes('token alinamadi') || lower.includes('oauth2password')) {
    return {
      kind: 'auth',
      title: 'Token alınamadı',
      lead: 'OAuth2 token isteği başarısız. Profile\'ın AuthConfigJson içeriği eksik veya hatalı olabilir.',
      reasons: [
        'Profile auth tipi yanlış (örn. "None" iken Netsis Bearer bekliyor)',
        'tokenEndpoint adresi yanlış / yazım hatası',
        'extraFields (branchcode, dbname, dbuser, dbpassword, dbtype) eksik',
      ],
    }
  }
  if (lower.includes('http 401') || lower.includes('unauthorized')) {
    return {
      kind: 'auth',
      title: 'Kimlik doğrulama gerekli (401)',
      lead: 'Endpoint kimlik doğrulama bekliyor ama profile\'da auth tanımı yok veya geçersiz.',
      reasons: [
        'API Profile AuthType = "None" — Şirket Ayarları → Entegrasyon API\'den Bearer/OAuth ayarlayın',
        'Token süresi dolmuş, yenileme başarısız oldu',
      ],
    }
  }
  if (lower.includes('http 405') || lower.includes('method not allowed')) {
    // Birden fazla katman denendi mi? "|" varsa tüm katmanlar başarısız
    const multipleLayers = e.includes('|')
    return {
      kind: 'endpoint',
      title: multipleLayers
        ? 'Hiçbir yöntem cevap vermedi (405)'
        : 'Endpoint Describe desteklemiyor (405)',
      lead: multipleLayers
        ? 'Üç katman da denendi (Describe POST/GET, Sample GET, POST Probe) — her biri 405 Method Not Allowed döndü. ' +
          'Sunucu adres var ama hiçbir HTTP metoduna izin vermiyor.'
        : 'Sunucu adres var ama POST kabul etmiyor — muhtemelen NetOpenX değil veya farklı bir servis.',
      reasons: [
        'Base URL gerçekten Netsis **NetOpenX REST** sunucusunu işaret ediyor mu? (örn. başka servis veya reverse proxy olabilir)',
        'NoxRest farklı portta olabilir — varsayılan **7070**, ama kurulum farklı olabilir',
        '**Profiller tab → Bağlantıyı Test Et** ile token endpoint çalışıyor mu doğrulayın — token alınabiliyorsa NoxRest canlı demektir',
        'Endpoint URL formatı doğru mu? (`/api/v2/{Resource}` standart)',
        '**Pratik çözüm: Şablon Galerisi**\'nden Müşteri Sipariş / Satış Faturası gibi hazır body kullanın',
      ],
    }
  }
  if (lower.includes('http 404') || lower.includes('not found')) {
    return {
      kind: 'endpoint',
      title: 'Endpoint bulunamadı (404)',
      lead: 'Belirtilen URL\'de hiçbir endpoint yok.',
      reasons: [
        'Base URL veya path yanlış',
        'Netsis NoxRest servisi farklı portta çalışıyor olabilir',
      ],
    }
  }
  if (lower.includes('timeout') || lower.includes('connection refused') || lower.includes('socketexception')) {
    return {
      kind: 'network',
      title: 'Sunucuya bağlanılamadı',
      lead: 'Belirtilen adres yanıt vermiyor.',
      reasons: [
        'Netsis NoxRest servisi çalışıyor mu? (Windows Services → "NoxRestService")',
        'Base URL\'deki port doğru mu? (NoxRest varsayılan: 7070)',
        'Firewall / antivirüs HTTP isteğini engelliyor olabilir',
      ],
    }
  }
  if (lower.includes('describe cevabi parse')) {
    return {
      kind: 'parse',
      title: 'Cevap anlaşılamadı',
      lead: 'Sunucu cevap döndü ama beklenen format değildi.',
      reasons: [
        'Sunucu Netsis NetOpenX REST değil olabilir',
        'NetOpenX versiyonu eski / yeni — Describe formatı değişmiş olabilir',
        'Şablon Galerisi\'nden hazır body kullanın',
      ],
    }
  }
  // Default: bilinmeyen
  return {
    kind: 'unknown',
    title: 'Otomatik çekme başarısız',
    lead: 'Beklenmedik bir hata oluştu.',
    reasons: [
      'Hata detayını aşağıdan inceleyebilirsiniz',
      'Geçici çözüm: Şablon Galerisi\'nden hazır body seçin veya manuel yazın',
    ],
  }
}

const TABS = [
  { id: 'basic',  label: 'Temel',       icon: Settings2 },
  { id: 'schema', label: 'Body Schema', icon: FileCode  },
]

/**
 * TemplateGalleryModal — Hazır JSON body sablonlarini listeler.
 * Kategori + provider + arama filtresiyle daraltir, "Kullan" tiklandiginda
 * sablon body'sini parent'a verir (overwrite onayi parent'ta yapilir).
 */
function TemplateGalleryModal({ onClose, onPick }) {
  const [templates, setTemplates] = useState([])
  const [loading, setLoading]     = useState(true)
  const [search, setSearch]       = useState('')
  const [category, setCategory]   = useState('all')
  const [preview, setPreview]     = useState(null)

  useEffect(() => {
    const h = e => { if (e.key === 'Escape') (preview ? setPreview(null) : onClose()) }
    window.addEventListener('keydown', h)
    return () => window.removeEventListener('keydown', h)
  }, [onClose, preview])

  useEffect(() => {
    let cancelled = false
    ;(async () => {
      try {
        const r = await fetch('/Integrations/api/body-templates', { credentials: 'same-origin' })
        const d = await r.json()
        if (!cancelled && d.success) setTemplates(d.templates || [])
        else if (!cancelled) toast(d.error || 'Sablon listesi alinamadi', 'err')
      } catch (e) {
        if (!cancelled) toast('Sunucu hatasi: ' + e.message, 'err')
      } finally {
        if (!cancelled) setLoading(false)
      }
    })()
    return () => { cancelled = true }
  }, [])

  const categories = React.useMemo(() => {
    const set = new Set(templates.map(t => t.category))
    return ['all', ...Array.from(set).sort()]
  }, [templates])

  const filtered = React.useMemo(() => {
    let list = templates
    if (category !== 'all') list = list.filter(t => t.category === category)
    if (search) {
      const q = search.toLowerCase()
      list = list.filter(t =>
        (t.name || '').toLowerCase().includes(q) ||
        (t.description || '').toLowerCase().includes(q) ||
        (t.tags || '').toLowerCase().includes(q) ||
        (t.docType || '').toLowerCase().includes(q)
      )
    }
    return list
  }, [templates, category, search])

  const handleUse = async (t) => {
    try {
      const r = await fetch(`/Integrations/api/body-templates/use/${t.id}`, {
        method: 'POST', credentials: 'same-origin',
        headers: { RequestVerificationToken: getCsrf() },
      })
      const d = await r.json()
      if (d.success) {
        onPick?.(d.bodyJson, t)
        onClose()
      } else toast(d.error || 'Sablon yuklenemedi', 'err')
    } catch (e) { toast('Sunucu hatasi: ' + e.message, 'err') }
  }

  const handleDelete = async (t, ev) => {
    ev.stopPropagation()
    // Rapor §6.6 — CalibraAlert.confirm fallback
    const ok = window.CalibraAlert && window.CalibraAlert.confirm
      ? await window.CalibraAlert.confirm(`"${t.name}" şablonu silinsin mi?`,
          { title: 'Şablonu Sil', okText: 'Evet, Sil', cancelText: 'Vazgeç', danger: true })
      : window.confirm(`"${t.name}" şablonu silinsin mi?`)
    if (!ok) return
    try {
      const r = await fetch(`/Integrations/api/body-templates/delete/${t.id}`, {
        method: 'POST', credentials: 'same-origin',
        headers: { RequestVerificationToken: getCsrf() },
      })
      const d = await r.json()
      if (d.success) {
        toast('Şablon silindi', 'ok')
        setTemplates(ts => ts.filter(x => x.id !== t.id))
        if (preview?.id === t.id) setPreview(null)
      } else toast(d.error || 'Silinemedi', 'err')
    } catch (e) { toast('Sunucu hatasi: ' + e.message, 'err') }
  }

  const categoryLabel = (c) => ({
    all: 'Tümü', Sales: 'Satış', Purchase: 'Satınalma',
    Customer: 'Cari', Stock: 'Stok', Bank: 'Banka',
    EDocument: 'e-Belge', Custom: 'Özel',
  }[c] || c)

  // Kategori sırası (özel sıralama — chip filtre + grup başlıkları için)
  const CATEGORY_ORDER = ['Sales', 'Purchase', 'Customer', 'Stock', 'Bank', 'EDocument', 'Custom']
  const categorySort = (a, b) => {
    const ai = CATEGORY_ORDER.indexOf(a)
    const bi = CATEGORY_ORDER.indexOf(b)
    if (ai === -1 && bi === -1) return a.localeCompare(b, 'tr')
    if (ai === -1) return 1
    if (bi === -1) return -1
    return ai - bi
  }

  // Filtered listesini kategori bazında grupla — UsageCount'a göre kart içi sıralama
  const grouped = React.useMemo(() => {
    const map = new Map()
    filtered.forEach(t => {
      const key = t.category || 'Custom'
      if (!map.has(key)) map.set(key, [])
      map.get(key).push(t)
    })
    // Her grup içinde popülerlik sırası (zaten backend sıralı geliyor ama emin olalım)
    map.forEach(arr => arr.sort((a, b) => (b.usageCount || 0) - (a.usageCount || 0)))
    return Array.from(map.entries())
      .sort(([a], [b]) => categorySort(a, b))
      .map(([category, items]) => ({ category, items }))
  }, [filtered])

  return (
    <div className="iw-modal-bd" onClick={() => !preview && onClose()}>
      <div className="eem-modal" onClick={e => e.stopPropagation()}
           style={{ width: 880, height: 580, maxHeight: '92vh' }}>
        <div className="eem-header">
          <div className="eem-title">
            <Sparkles size={15} style={{ verticalAlign: 'middle', marginRight: 8, color: 'var(--iw-indigo-color)' }} />
            Şablon Galerisi
            <span className="eem-title-sub"> — {filtered.length} / {templates.length}</span>
          </div>
          <button className="eem-icon-btn" title="Kapat (Esc)" onClick={onClose}>
            <X size={16} />
          </button>
        </div>

        {/* Toolbar */}
        <div style={{
          display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap',
          padding: '10px 16px', borderBottom: '1px solid var(--iw-border)',
          background: 'var(--iw-bg)', flexShrink: 0,
        }}>
          <div className="il-search-wrap" style={{ flex: '0 0 auto' }}>
            <Search size={13} className="il-search-icon" />
            <input className="il-search" placeholder="Şablon ara…"
                   value={search} onChange={e => setSearch(e.target.value)}
                   style={{ width: 220 }} />
          </div>
          <div style={{ display: 'flex', gap: 4 }}>
            {categories.map(c => (
              <button key={c}
                      onClick={() => setCategory(c)}
                      style={{
                        padding: '5px 12px', borderRadius: 6, fontSize: 12, fontWeight: 500,
                        border: '1px solid ' + (category === c ? 'var(--iw-indigo-color)' : 'var(--iw-border)'),
                        background: category === c ? 'var(--iw-indigo-bg)' : 'var(--iw-surface)',
                        color: category === c ? 'var(--iw-indigo-color)' : 'var(--iw-muted)',
                        cursor: 'pointer',
                      }}>
                {categoryLabel(c)}
              </button>
            ))}
          </div>
        </div>

        {/* Liste */}
        <div style={{ flex: 1, overflowY: 'auto', padding: 16 }}>
          {loading && (
            <div className="il-empty">
              <Loader2 className="iw-spin" size={32} /><span>Yükleniyor…</span>
            </div>
          )}
          {!loading && filtered.length === 0 && (
            <div className="il-empty">
              <Sparkles size={48} style={{ opacity: 0.3 }} />
              <span>Şablon bulunamadı.</span>
            </div>
          )}
          {!loading && (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 18 }}>
              {grouped.map(({ category, items }) => (
                <section key={category}>
                  {/* Kategori başlığı — sadece "Tümü" filtresinde gösterilir */}
                  {category === 'all' && (
                    <div style={{
                      display: 'flex', alignItems: 'center', gap: 8,
                      padding: '4px 4px 8px',
                      borderBottom: '1px solid var(--iw-border)',
                      marginBottom: 8,
                    }}>
                      <span style={{
                        padding: '2px 9px', borderRadius: 5, fontSize: 11, fontWeight: 700,
                        background: 'var(--iw-indigo-bg)', color: 'var(--iw-indigo-color)',
                      }}>{categoryLabel(category)}</span>
                      <span style={{ fontSize: 11, color: 'var(--iw-muted)' }}>
                        {items.length} şablon
                      </span>
                    </div>
                  )}

                  {/* Tek-sütun şablon satırları */}
                  <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                    {items.map(t => (
                      <div key={t.id} style={{
                        display: 'grid',
                        gridTemplateColumns: 'minmax(220px, 1fr) minmax(180px, 1.2fr) auto auto',
                        gap: 12, alignItems: 'center',
                        border: '1px solid var(--iw-border)', borderRadius: 8,
                        padding: '10px 14px', background: 'var(--iw-surface)',
                        transition: 'border-color .12s, background .12s',
                      }}
                      onMouseEnter={e => { e.currentTarget.style.borderColor = 'var(--iw-indigo-bdr)'; e.currentTarget.style.background = 'var(--iw-hover)' }}
                      onMouseLeave={e => { e.currentTarget.style.borderColor = 'var(--iw-border)'; e.currentTarget.style.background = 'var(--iw-surface)' }}>
                        {/* Sol: Ad + açıklama + rozetler */}
                        <div style={{ display: 'flex', flexDirection: 'column', gap: 3, minWidth: 0 }}>
                          <div style={{ display: 'flex', alignItems: 'center', gap: 5, flexWrap: 'wrap' }}>
                            <span style={{
                              fontSize: 13, fontWeight: 600, color: 'var(--iw-text)',
                              overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
                            }}>{t.name}</span>
                            {t.docType && (
                              <span style={{
                                padding: '0 6px', borderRadius: 4, fontSize: 10, fontWeight: 600,
                                background: 'var(--iw-bg)', color: 'var(--iw-muted)',
                                fontFamily: 'monospace', flexShrink: 0,
                              }}>{t.docType}</span>
                            )}
                            {!t.isBuiltIn && (
                              <span style={{
                                padding: '0 6px', borderRadius: 4, fontSize: 10, fontWeight: 600,
                                background: 'var(--iw-emerald-bg)', color: 'var(--iw-emerald-color)',
                                flexShrink: 0,
                              }} title="Sizin yarattığınız şablon">Özel</span>
                            )}
                          </div>
                          {t.description && (
                            <div style={{
                              fontSize: 11, color: 'var(--iw-muted)', lineHeight: 1.4,
                              overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
                            }} title={t.description}>{t.description}</div>
                          )}
                        </div>

                        {/* Orta: Endpoint info (URL + method) */}
                        <div style={{ minWidth: 0 }}>
                          {t.urlPattern && (
                            <div style={{
                              fontSize: 11, fontFamily: 'monospace', color: 'var(--iw-muted)',
                              overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
                            }} title={`${t.httpMethod || ''} ${t.urlPattern}`}>
                              <span style={{
                                display: 'inline-block', padding: '1px 6px', borderRadius: 3,
                                fontSize: 10, fontWeight: 700, marginRight: 6,
                                background: 'var(--iw-indigo-bg)', color: 'var(--iw-indigo-color)',
                              }}>{t.httpMethod || 'POST'}</span>
                              {t.urlPattern}
                            </div>
                          )}
                          {t.usageCount > 0 && (
                            <div style={{ fontSize: 10, color: 'var(--iw-muted)', marginTop: 2 }}>
                              ↻ {t.usageCount} kez kullanıldı
                            </div>
                          )}
                        </div>

                        {/* Aksiyonlar */}
                        <div style={{ display: 'flex', gap: 4, flexShrink: 0 }}>
                          <button className="iw-btn-ghost" onClick={() => setPreview(t)}
                                  style={{ padding: '5px 10px', fontSize: 12 }}>
                            Önizle
                          </button>
                          <button className="iw-btn-primary" onClick={() => handleUse(t)}
                                  style={{ padding: '5px 12px', fontSize: 12 }}>
                            Kullan
                          </button>
                        </div>

                        {/* Sil butonu (sadece özel) */}
                        <div style={{ width: 24, display: 'flex', justifyContent: 'center' }}>
                          {!t.isBuiltIn ? (
                            <button onClick={(e) => handleDelete(t, e)}
                                    title="Şablonu sil"
                                    style={{
                                      background: 'transparent', border: 'none', cursor: 'pointer',
                                      padding: 4, color: 'var(--iw-muted)', borderRadius: 4,
                                    }}
                                    onMouseEnter={e => e.currentTarget.style.color = 'var(--iw-rose-color)'}
                                    onMouseLeave={e => e.currentTarget.style.color = 'var(--iw-muted)'}>
                              <Trash2 size={13} />
                            </button>
                          ) : null}
                        </div>
                      </div>
                    ))}
                  </div>
                </section>
              ))}
            </div>
          )}
        </div>

        {/* Önizleme overlay */}
        {preview && (
          <div onClick={() => setPreview(null)}
               style={{
                 position: 'absolute', inset: 0, background: 'rgba(0,0,0,.55)',
                 backdropFilter: 'blur(2px)', display: 'flex', alignItems: 'center', justifyContent: 'center',
                 padding: 20, zIndex: 5,
               }}>
            <div onClick={e => e.stopPropagation()} style={{
              background: 'var(--iw-surface)', borderRadius: 10,
              border: '1px solid var(--iw-border)', width: '100%', maxWidth: 720,
              maxHeight: '90%', display: 'flex', flexDirection: 'column',
            }}>
              <div style={{
                display: 'flex', alignItems: 'center', gap: 8,
                padding: '12px 16px', borderBottom: '1px solid var(--iw-border)',
              }}>
                <div style={{ flex: 1, fontSize: 13, fontWeight: 600 }}>{preview.name}</div>
                <button className="eem-icon-btn" onClick={() => setPreview(null)}><X size={15} /></button>
              </div>
              <pre style={{
                flex: 1, overflow: 'auto', margin: 0, padding: 14,
                fontFamily: "'JetBrains Mono','Consolas',monospace", fontSize: 12,
                background: 'var(--iw-bg)', color: 'var(--iw-text)',
                whiteSpace: 'pre-wrap', wordBreak: 'break-word',
              }}>{preview.bodyJson}</pre>
              <div style={{
                display: 'flex', gap: 8, justifyContent: 'flex-end',
                padding: '12px 16px', borderTop: '1px solid var(--iw-border)',
                background: 'var(--iw-bg)',
              }}>
                <button className="iw-btn-secondary" onClick={() => setPreview(null)}>Kapat</button>
                <button className="iw-btn-primary" onClick={() => handleUse(preview)}>
                  <Sparkles size={13} /> Bunu Kullan
                </button>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}

/**
 * SaveTemplateModal — Mevcut Body Schema'yi galeriye yeni şablon olarak kaydet.
 * Kategori + ad + açıklama + tags + otomatik prefill (endpoint URL/method/profile).
 */
function SaveTemplateModal({ initial, onClose, onSaved }) {
  const [form, setForm] = useState({
    category:    'Custom',
    name:        initial?.name ? `${initial.name} (kopya)` : '',
    docType:     '',
    providerHint:'Custom',
    urlPattern:  initial?.urlTemplate || '',
    httpMethod:  initial?.httpMethod  || 'POST',
    bodyJson:    initial?.bodyJson    || '',
    description: '',
    tags:        '',
  })
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    const h = e => { if (e.key === 'Escape' && !saving) onClose() }
    window.addEventListener('keydown', h)
    return () => window.removeEventListener('keydown', h)
  }, [onClose, saving])

  const upd = (patch) => setForm(s => ({ ...s, ...patch }))

  const handleSave = async () => {
    if (!form.name.trim())     { toast('Şablon adı zorunlu', 'err'); return }
    if (!form.bodyJson.trim()) { toast('Body JSON boş', 'err'); return }

    try { JSON.parse(form.bodyJson) }
    catch (e) { toast('Body geçerli JSON değil: ' + e.message, 'err'); return }

    setSaving(true)
    try {
      const r = await fetch('/Integrations/api/body-templates', {
        method: 'POST', credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json', RequestVerificationToken: getCsrf() },
        body: JSON.stringify(form),
      })
      const d = await r.json()
      if (d.success) {
        toast(`✓ "${form.name}" şablon olarak kaydedildi`, 'ok')
        onSaved?.(d.id)
        onClose()
      } else toast(d.error || 'Kaydedilemedi', 'err')
    } catch (e) { toast('Sunucu hatası: ' + e.message, 'err') }
    finally { setSaving(false) }
  }

  const CATEGORIES = [
    { id: 'Sales',     label: 'Satış' },
    { id: 'Purchase',  label: 'Satınalma' },
    { id: 'Customer',  label: 'Cari' },
    { id: 'Stock',     label: 'Stok' },
    { id: 'Bank',      label: 'Banka' },
    { id: 'EDocument', label: 'e-Belge' },
    { id: 'Custom',    label: 'Özel' },
  ]

  return (
    <div className="iw-modal-bd" onClick={() => !saving && onClose()}>
      <div className="eem-modal" onClick={e => e.stopPropagation()}
           style={{ width: 560, height: 'auto', maxHeight: '92vh' }}>
        <div className="eem-header">
          <div className="eem-title">
            <BookmarkPlus size={15} style={{ verticalAlign: 'middle', marginRight: 8, color: 'var(--iw-indigo-color)' }} />
            Şablon Olarak Kaydet
          </div>
          <button className="eem-icon-btn" onClick={onClose} disabled={saving}><X size={16} /></button>
        </div>

        <div className="eem-content" style={{ padding: '20px 24px' }}>
          <div className="iw-field" style={{ maxWidth: 'none' }}>
            <label>Şablon Adı *</label>
            <input value={form.name} onChange={e => upd({ name: e.target.value })}
                   placeholder="Örn: Özel Müşteri Sipariş Şablonu" maxLength={200} disabled={saving} />
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12, marginTop: 12 }}>
            <div className="iw-field">
              <label>Kategori *</label>
              <select value={form.category} onChange={e => upd({ category: e.target.value })} disabled={saving}>
                {CATEGORIES.map(c => <option key={c.id} value={c.id}>{c.label}</option>)}
              </select>
            </div>
            <div className="iw-field">
              <label>Doc Type (opsiyonel)</label>
              <input value={form.docType} onChange={e => upd({ docType: e.target.value })}
                     placeholder="Örn: ftSSip" maxLength={50} disabled={saving} />
            </div>
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12, marginTop: 12 }}>
            <div className="iw-field">
              <label>Provider</label>
              <input value={form.providerHint} onChange={e => upd({ providerHint: e.target.value })}
                     placeholder="Netsis / Logo / Custom" maxLength={50} disabled={saving} />
            </div>
            <div className="iw-field">
              <label>HTTP Method</label>
              <select value={form.httpMethod} onChange={e => upd({ httpMethod: e.target.value })} disabled={saving}>
                {['POST','PUT','PATCH','GET','DELETE'].map(m => <option key={m} value={m}>{m}</option>)}
              </select>
            </div>
          </div>

          <div className="iw-field" style={{ maxWidth: 'none', marginTop: 12 }}>
            <label>URL Pattern</label>
            <input value={form.urlPattern} onChange={e => upd({ urlPattern: e.target.value })}
                   placeholder="/api/v2/ItemSlips" maxLength={500} disabled={saving} />
            <span className="iw-field-hint">Hangi endpoint için olduğunu belirtir. Sonradan eşleşme önerisinde kullanılır.</span>
          </div>

          <div className="iw-field" style={{ maxWidth: 'none', marginTop: 12 }}>
            <label>Açıklama</label>
            <input value={form.description} onChange={e => upd({ description: e.target.value })}
                   placeholder="Bu şablon ne için?" maxLength={1000} disabled={saving} />
          </div>

          <div className="iw-field" style={{ maxWidth: 'none', marginTop: 12 }}>
            <label>Etiketler (virgülle ayır)</label>
            <input value={form.tags} onChange={e => upd({ tags: e.target.value })}
                   placeholder="ozel,musteri-x,tedarikci-y" maxLength={500} disabled={saving} />
          </div>

          <div style={{
            marginTop: 14, padding: 10, background: 'var(--iw-bg)', borderRadius: 6,
            fontSize: 11, color: 'var(--iw-muted)',
          }}>
            ℹ Body Schema otomatik kaydedilecek ({form.bodyJson.length} karakter).
            JSON geçerliliği kayıt sırasında doğrulanır.
          </div>
        </div>

        <div className="eem-footer">
          <button className="iw-btn-secondary" onClick={onClose} disabled={saving}>Vazgeç</button>
          <button className="iw-btn-primary" onClick={handleSave} disabled={saving}>
            {saving ? <><Loader2 className="iw-spin" size={14} /> Kaydediliyor</> : <><BookmarkPlus size={14} /> Şablon Olarak Kaydet</>}
          </button>
        </div>
      </div>
    </div>
  )
}

/**
 * EndpointUrlPicker — URL Şablonu için searchable combobox.
 *
 * Combobox davranışı:
 *   • Free-text yazma: kullanıcı kendi URL'ini girebilir
 *   • Dropdown: katalog (mevcut endpoint'lerden distinct) Resource bazında gruplı
 *   • Method filtresi: `httpMethod` prop'u dolu ise yalnız o method'a uyan
 *     şablonlar listelenir (Temel tab'ındaki HTTP Method select'i ile senkron;
 *     metin aramasıyla AND mantığında birleşir)
 *   • Arama: input'a yazılan harf URL/resource/method/name'de filtre
 *   • Seçim: URL + HttpMethod + Description otomatik dolar (parent onPick)
 *   • Dropdown paneli document.body'e PORTAL edilir — modal'in `.eem-content`
 *     (overflow:auto) ve `.eem-body`/`.eem-modal` (overflow:hidden)
 *     kapsayıcılarına hapsolup kırpılmasını önler. Konum trigger'in
 *     getBoundingClientRect()'inden hesaplanır, scroll/resize'da güncellenir.
 *
 * Katalog: GET /Integrations/api/endpoint-catalog (distinct URL+method)
 * Cache: parent component fetch eder, prop olarak verir (modal her açılışta
 * tekrar fetch'lemesin).
 */
function EndpointUrlPicker({ value, onChange, onPick, catalog, disabled, httpMethod }) {
  const [open, setOpen]       = useState(false)
  const [query, setQuery]     = useState('')
  const [menuPos, setMenuPos] = useState({ top: 0, left: 0, width: 280, maxHeight: 360 })
  const wrapRef  = useRef(null)   // trigger (input + chevron buton)
  const inputRef = useRef(null)
  const menuRef  = useRef(null)   // portal edilen panel — dışarı-tık algısına dahil edilmeli

  // Panel konumunu trigger'in bounding rect'inden hesapla. Alt tarafta yeterli
  // yer yoksa (modal alt kenarına yakınsa) yukarı açılır (flip) — ekranı taşmaz.
  function calcPos() {
    if (!wrapRef.current) return
    const r = wrapRef.current.getBoundingClientRect()
    const gap = 4
    const spaceBelow = window.innerHeight - r.bottom - 12
    const spaceAbove = r.top - 12
    const preferred = 360
    const width = Math.max(r.width, 260)
    if (spaceBelow >= 200 || spaceBelow >= spaceAbove) {
      setMenuPos({ top: r.bottom + gap, left: r.left, width, maxHeight: Math.max(160, Math.min(preferred, spaceBelow)) })
    } else {
      const maxHeight = Math.max(160, Math.min(preferred, spaceAbove))
      setMenuPos({ top: r.top - gap - maxHeight, left: r.left, width, maxHeight })
    }
  }

  // Açılışta konumu senkron hesapla (paint öncesi) — 0,0'da flash olmasın
  useLayoutEffect(() => {
    if (!open) return
    calcPos()
  }, [open])

  // Scroll (capture — iç içe scroll container'lar için) / resize'da yeniden hesapla
  useEffect(() => {
    if (!open) return undefined
    const onReposition = () => calcPos()
    window.addEventListener('scroll', onReposition, true)
    window.addEventListener('resize', onReposition)
    return () => {
      window.removeEventListener('scroll', onReposition, true)
      window.removeEventListener('resize', onReposition)
    }
  }, [open])

  // Dışarı tık → kapat. Panel portal ile document.body'e render edildiği için
  // hem trigger hem de portal panel ref'i "içeri" sayılmalı.
  useEffect(() => {
    if (!open) return undefined
    const handler = (e) => {
      if (wrapRef.current && wrapRef.current.contains(e.target)) return
      if (menuRef.current && menuRef.current.contains(e.target)) return
      setOpen(false)
    }
    document.addEventListener('mousedown', handler)
    return () => document.removeEventListener('mousedown', handler)
  }, [open])

  // Esc → kapat
  useEffect(() => {
    if (!open) return undefined
    const h = (e) => { if (e.key === 'Escape') setOpen(false) }
    window.addEventListener('keydown', h)
    return () => window.removeEventListener('keydown', h)
  }, [open])

  // Method bazlı ön-filtre (Fix 1) — httpMethod boşsa tüm şablonlar geçer
  const methodFiltered = useMemo(() => {
    const list = catalog || []
    if (!httpMethod) return list
    const m = String(httpMethod).toUpperCase()
    return list.filter(it => (it.httpMethod || 'POST').toUpperCase() === m)
  }, [catalog, httpMethod])

  // Metin filtresi (method-filtreli liste üzerinde AND) + Resource bazlı grupla
  const grouped = useMemo(() => {
    const q = query.trim().toLowerCase()
    const filtered = methodFiltered.filter(it => {
      if (!q) return true
      return (it.urlTemplate || '').toLowerCase().includes(q)
        || (it.resource    || '').toLowerCase().includes(q)
        || (it.methodName  || '').toLowerCase().includes(q)
        || (it.name        || '').toLowerCase().includes(q)
    })

    const map = new Map()
    filtered.forEach(it => {
      const key = it.resource || '(diğer)'
      if (!map.has(key)) map.set(key, [])
      map.get(key).push(it)
    })
    return Array.from(map.entries())
      .sort(([a], [b]) => a.localeCompare(b, 'tr'))
      .map(([resource, items]) => ({ resource, items }))
  }, [methodFiltered, query])

  const totalCount    = (catalog || []).length
  const methodCount   = methodFiltered.length
  const filteredCount = grouped.reduce((sum, g) => sum + g.items.length, 0)

  return (
    <div ref={wrapRef} style={{ position: 'relative' }}>
      <div style={{ display: 'flex', gap: 0, alignItems: 'stretch' }}>
        <input
          ref={inputRef}
          value={value || ''}
          onChange={e => onChange(e.target.value)}
          onFocus={() => setOpen(true)}
          placeholder="/api/v2/ItemSlips — yazın veya listeden seçin"
          maxLength={500}
          disabled={disabled}
          style={{ flex: 1, borderTopRightRadius: 0, borderBottomRightRadius: 0 }}
        />
        <button type="button"
                onClick={() => { setOpen(o => !o); inputRef.current?.focus() }}
                disabled={disabled}
                style={{
                  padding: '0 10px', borderRadius: '0 8px 8px 0',
                  border: '1px solid var(--iw-border)', borderLeft: 'none',
                  background: open ? 'var(--iw-indigo-bg)' : 'var(--iw-bg)',
                  color: open ? 'var(--iw-indigo-color)' : 'var(--iw-muted)',
                  cursor: disabled ? 'not-allowed' : 'pointer',
                  display: 'flex', alignItems: 'center',
                }}
                title="Katalogdan seç">
          <ChevronDown size={14} style={{
            transform: open ? 'rotate(180deg)' : 'rotate(0)',
            transition: 'transform .15s',
          }} />
        </button>
      </div>

      {open && createPortal(
        <div ref={menuRef} className="eup-menu" style={{
          top: menuPos.top, left: menuPos.left, width: menuPos.width, maxHeight: menuPos.maxHeight,
        }}>
          {/* Sticky search */}
          <div className="eup-menu-search">
            <Search size={12} />
            <input
              autoFocus
              value={query}
              onChange={e => setQuery(e.target.value)}
              placeholder={`Resource / URL ara... (${filteredCount} / ${methodCount})`}
              className="eup-menu-search-input"
            />
          </div>

          {/* Method filtre bilgisi — hangi method'a göre daraltıldığını gösterir */}
          {httpMethod && totalCount > 0 && (
            <div className="eup-menu-filter-note">
              <span className="eup-menu-filter-badge">{httpMethod}</span>
              method'una uygun {methodCount} şablon{methodCount !== totalCount ? ` (toplam ${totalCount})` : ''}
            </div>
          )}

          {/* Liste */}
          <div className="eup-menu-list">
            {totalCount === 0 && (
              <div className="eup-menu-empty">
                Katalog boş. Hub → Endpointler tab'ından "Toplu Import" ile
                NetsisRestEndpoints.csv yükleyebilirsiniz.
              </div>
            )}
            {totalCount > 0 && methodCount === 0 && (
              <div className="eup-menu-empty">
                <strong>{httpMethod}</strong> method'una uygun şablon yok. Yukarıdaki inputa
                yazdığınız değer custom URL olarak kalır.
              </div>
            )}
            {totalCount > 0 && methodCount > 0 && filteredCount === 0 && (
              <div className="eup-menu-empty">
                Eşleşen endpoint yok. Yukarıdaki inputa yazdığınız değer custom URL olarak kalır.
              </div>
            )}
            {grouped.map(({ resource, items }) => (
              <div key={resource}>
                <div className="eup-menu-group-header">
                  {resource} <span>({items.length})</span>
                </div>
                {items.map((it, i) => {
                  const itKind = classifyInputType(it.inputType)
                  const itColor = inputTypeColor(itKind)
                  return (
                    <button key={`${resource}-${i}`}
                            type="button"
                            className="eup-menu-item"
                            onClick={() => { onPick(it); setOpen(false); setQuery('') }}>
                      <span className="eup-menu-item-method">{(it.httpMethod || 'POST').toUpperCase()}</span>
                      <div className="eup-menu-item-body">
                        <div className="eup-menu-item-url">{it.urlTemplate}</div>
                        {(it.summary || it.methodName || it.name) && (
                          <div className="eup-menu-item-sub">
                            {it.summary || it.methodName || it.name}
                          </div>
                        )}
                      </div>
                      {/* InputType rozeti — kullanıcıya body tipini göster */}
                      {it.inputType && (
                        <span title={`InputType: ${it.inputType} — ${inputTypeLabel(itKind)}`}
                              className="eup-menu-item-badge"
                              style={{
                                background: `var(--eup-${itColor}-bg)`,
                                color: `var(--eup-${itColor}-color)`,
                              }}>
                          {it.inputType}
                        </span>
                      )}
                    </button>
                  )
                })}
              </div>
            ))}
          </div>
        </div>,
        document.body
      )}
    </div>
  )
}

export default function EndpointEditModal({ profileId, profiles, endpoint, onClose, onSaved }) {
  const [form, setForm] = useState({
    id: endpoint?.id || 0,
    apiProfileId: endpoint?.apiProfileId || profileId || '',
    name: endpoint?.name || '',
    httpMethod: endpoint?.httpMethod || 'POST',
    urlTemplate: endpoint?.urlTemplate || '',
    bodySchema: endpoint?.bodySchema || '',
    description: endpoint?.description || '',
    isActive: endpoint?.isActive ?? true,
  })
  const [tab, setTab] = useState('basic')
  const [saving, setSaving] = useState(false)
  const [fullscreen, setFullscreen] = useState(false)
  const [schemaError, setSchemaError] = useState(null)
  const [showGallery, setShowGallery] = useState(false)
  const [showSaveAs, setShowSaveAs]   = useState(false)
  const [resolving, setResolving]     = useState(false)
  const [resolveError, setResolveErr] = useState(null)   // { title, lead, reasons[], rawError, source, durationMs }
  const [catalog, setCatalog]         = useState([])     // [{ urlTemplate, httpMethod, resource, methodName, name, description, inputType, returnType, summary }]
  // İlk yüklenen state (referans) — değişiklik tespiti için
  const initialFormRef = useRef(null)
  if (initialFormRef.current === null) {
    initialFormRef.current = JSON.stringify({
      apiProfileId: endpoint?.apiProfileId || profileId || '',
      name: endpoint?.name || '',
      httpMethod: endpoint?.httpMethod || 'POST',
      urlTemplate: endpoint?.urlTemplate || '',
      bodySchema: endpoint?.bodySchema || '',
      description: endpoint?.description || '',
      isActive: endpoint?.isActive ?? true,
    })
  }
  // Picker'dan seçilen son endpoint metadata'sı — InputType/Summary için info etiketi
  // ve Otomatik Çek'in akıllı yönlendirmesi için kullanılır.
  // null = manuel yazıldı / katalogtan değil. Mevcut endpoint düzenleniyorsa modal
  // ilk açıldığında catalog'tan URL eşleşeni yakalanır (aşağıdaki useEffect).
  const [pickedCatalogItem, setPickedCatalogItem] = useState(null)
  const [resolvedFields, setResolvedFields]       = useState(null)   // [{ path, type, required, maxLength, enum }]

  // Modal açılırken mevcut form.urlTemplate'i katalogla eşleştir — düzenleme
  // akışında "seçili endpoint" etiketi otomatik dolar.
  useEffect(() => {
    if (!form.urlTemplate || catalog.length === 0) return
    if (pickedCatalogItem) return
    const match = catalog.find(c =>
      c.urlTemplate === form.urlTemplate &&
      (c.httpMethod || '').toUpperCase() === (form.httpMethod || '').toUpperCase()
    )
    if (match) setPickedCatalogItem(match)
  }, [form.urlTemplate, form.httpMethod, catalog])

  // Endpoint katalogunu modal acildiginda bir kez cek (CSV import edilmis 320+ endpoint)
  useEffect(() => {
    let cancelled = false
    fetch('/Integrations/api/endpoint-catalog', { credentials: 'same-origin' })
      .then(r => r.json())
      .then(d => { if (!cancelled && d.success) setCatalog(d.items || []) })
      .catch(() => { /* sessiz — katalog opsiyonel */ })
    return () => { cancelled = true }
  }, [])

  // Edit modunda — endpoint detayını fetch et (BodySchema dahil!)
  // Liste API'si BodySchema dondurmuyor, modal acilinca detay cekmeden form
  // bodySchema='' baslar (placeholder gosterilir) — bu yuzden onceki kayit
  // varmis gibi gorunmez. Burada DB'den gercek deger gelir.
  useEffect(() => {
    if (!endpoint?.id) return
    let cancelled = false
    fetch(`/Integrations/api/endpoints/${endpoint.id}`, { credentials: 'same-origin' })
      .then(r => r.json())
      .then(d => {
        if (cancelled || !d.success || !d.endpoint) return
        const ep = d.endpoint
        setForm(s => ({
          ...s,
          // Detayli alanlari guncelle (liste'de olmayanlar)
          bodySchema:  ep.bodySchema  ?? s.bodySchema,
          description: ep.description ?? s.description,
        }))
        // Initial referansi da guncelle ki "kaydedilmemis degisiklik" yanlis trigger olmasin
        initialFormRef.current = JSON.stringify({
          apiProfileId: ep.apiProfileId,
          name:         ep.name,
          httpMethod:   ep.httpMethod,
          urlTemplate:  ep.urlTemplate,
          bodySchema:   ep.bodySchema  ?? '',
          description:  ep.description ?? '',
          isActive:     ep.isActive,
        })
      })
      .catch(e => console.error('[EndpointEditModal] detay yuklenemedi:', e))
    return () => { cancelled = true }
  }, [endpoint?.id])

  // Esc ile kapat (fullscreen iken sadece fullscreen'i kapat)
  useEffect(() => {
    const h = e => {
      if (e.key !== 'Escape' || saving) return
      if (fullscreen) setFullscreen(false)
      else onClose()
    }
    window.addEventListener('keydown', h)
    return () => window.removeEventListener('keydown', h)
  }, [onClose, saving, fullscreen])

  const update = (patch) => setForm(s => ({ ...s, ...patch }))

  // Değişiklik tespiti — modal açılışındaki state ile karşılaştır (id hariç)
  const hasChanges = useMemo(() => {
    if (!initialFormRef.current) return false
    const current = JSON.stringify({
      apiProfileId: form.apiProfileId,
      name: form.name,
      httpMethod: form.httpMethod,
      urlTemplate: form.urlTemplate,
      bodySchema: form.bodySchema,
      description: form.description,
      isActive: form.isActive,
    })
    return current !== initialFormRef.current
  }, [form])

  // JSON validation (canlı, kullanıcı yazarken)
  useEffect(() => {
    if (!form.bodySchema?.trim()) { setSchemaError(null); return }
    try { JSON.parse(form.bodySchema); setSchemaError(null) }
    catch (e) { setSchemaError(e.message) }
  }, [form.bodySchema])

  const handleSave = async () => {
    if (!form.name.trim())   { toast('Endpoint adı zorunlu', 'err'); setTab('basic'); return }
    if (!form.urlTemplate.trim()) { toast('URL şablonu zorunlu', 'err'); setTab('basic'); return }
    if (!form.apiProfileId)  { toast('API Profile seçin', 'err'); setTab('basic'); return }

    setSaving(true)
    console.log('[handleSave] form gönderiliyor:', {
      id: form.id, name: form.name, bodySchemaLength: (form.bodySchema || '').length,
    })
    try {
      const r = await fetch('/Integrations/api/endpoints/save', {
        method: 'POST', credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json', RequestVerificationToken: getCsrf() },
        body: JSON.stringify(form),
      })
      console.log('[handleSave] HTTP', r.status, r.statusText)
      let d
      const txt = await r.text()
      try { d = JSON.parse(txt) }
      catch (parseErr) {
        console.error('[handleSave] JSON parse fail. Raw response:', txt.substring(0, 500))
        toast(`Sunucu HTML/HTML cevap döndü (HTTP ${r.status}) — auth veya middleware sorunu olabilir`, 'err')
        return
      }
      console.log('[handleSave] cevap:', d)
      if (d.success) {
        // Initial referansı güncelle — kaydedildikten sonra hasChanges false olsun
        initialFormRef.current = JSON.stringify({
          apiProfileId: form.apiProfileId,
          name: form.name,
          httpMethod: form.httpMethod,
          urlTemplate: form.urlTemplate,
          bodySchema: form.bodySchema,
          description: form.description,
          isActive: form.isActive,
        })
        toast(form.id ? '✓ Endpoint güncellendi' : '✓ Endpoint oluşturuldu', 'ok')
        onSaved?.(d.id)
      } else {
        toast('✗ Kayıt hatası: ' + (d.error || 'bilinmeyen'), 'err')
      }
    } catch (e) {
      console.error('[handleSave] fetch error:', e)
      toast('Sunucu hatası: ' + e.message, 'err')
    } finally {
      setSaving(false)
    }
  }

  /**
   * Otomatik Body Çek — backend BodySchemaResolver'i devreye sok.
   * Endpoint kaydedilmemis olsa bile calisir (api profile + url ile).
   *
   * Akıllı davranış: Picker'dan seçilen katalog item'ının InputType'ına bakar:
   *   • 'resource' (ARPs, ItemSlips, Items...)  → Describe çağrısı yap
   *   • 'param'    (ARPsRiskParam, *Prm)        → Describe ile alınamaz, Şablon Galerisi öner
   *   • 'filter'   (SelectFilter)               → Body değil, query filter
   *   • 'primitive' (String/Int32)              → Body istenmiyor (tek değer)
   *   • 'auth'     (oAuth2)                     → OAuth flow, body değil
   *   • 'none'     (boş)                        → Body istemiyor (Describe metodu vs)
   */
  const handleAutoResolve = async () => {
    if (!form.apiProfileId) { toast('Önce API Profile seçin', 'err'); setTab('basic'); return }
    if (!form.urlTemplate?.trim()) { toast('Önce URL Şablonu girin', 'err'); setTab('basic'); return }

    // InputType bazlı erken yönlendirme — backend'e gitmeden kullanıcıya net mesaj
    if (pickedCatalogItem?.inputType) {
      const kind = classifyInputType(pickedCatalogItem.inputType)
      if (kind !== 'resource') {
        const messages = {
          param: {
            title: 'Bu metot için Otomatik Çek yapılamaz',
            lead: `Endpoint **${pickedCatalogItem.inputType}** adında özel bir parametre tipi bekliyor. ` +
                  'Netsis NoxRest "Describe" mekanizması yalnızca standart entity tipleri için çalışır.',
            reasons: [
              'Şablon Galerisi\'nden ilgili belge tipi için hazır body deneyin',
              'Manuel olarak JSON body yazın — InputType referansını dokümantasyondan kontrol edin',
              `Bu endpoint genelde küçük bir parametre objesi alır (örn. ${pickedCatalogItem.resource} kodu, tarih aralığı vb.)`,
            ],
          },
          filter: {
            title: 'Bu endpoint body değil, filtre bekliyor',
            lead: `**${pickedCatalogItem.inputType}** — query parametre tipidir, body olarak gönderilmez.`,
            reasons: [
              'Filtre bilgisi URL query string olarak iletilir (örn. ?Top=10&Skip=0)',
              'Body Schema\'yı boş bırakabilirsiniz; Wizard bu metot için body göndermeyecek',
            ],
          },
          primitive: {
            title: 'Bu endpoint tek bir değer bekliyor',
            lead: `**${pickedCatalogItem.inputType}** — basit primitif tip (string/sayı). JSON şeması gerektirmez.`,
            reasons: [
              'Body olarak doğrudan değeri JSON-encoded gönderirsiniz: "MyValue" veya 123',
              'Kompleks Body Schema yerine doğrudan tek değer yeterli',
            ],
          },
          auth: {
            title: 'Bu endpoint kimlik doğrulama akışı',
            lead: `**${pickedCatalogItem.inputType}** — OAuth/auth flow için kullanılır.`,
            reasons: [
              'Token yönetimi otomatik handler tarafından yapılır',
              'Bu endpoint\'i manuel entegrasyon olarak eklemenize genelde gerek yoktur',
            ],
          },
          none: {
            title: 'Bu endpoint body istemiyor',
            lead: `**${pickedCatalogItem.methodName || 'Bu metot'}** body parametresi almaz.`,
            reasons: [
              'Body Schema\'yı boş bırakabilirsiniz',
              'Genelde Describe / Get gibi metotlar bu kategoriye girer',
            ],
          },
        }
        const msg = messages[kind]
        if (msg) {
          setResolveErr({ kind, ...msg, rawError: `InputType=${pickedCatalogItem.inputType}` })
          toast(`✗ ${msg.title}`, 'err')
          return
        }
      }
    }

    if (form.bodySchema?.trim()) {
      // Rapor §6.6 — CalibraAlert.confirm fallback
      const ok = window.CalibraAlert && window.CalibraAlert.confirm
        ? await window.CalibraAlert.confirm('Mevcut Body Schema\'nın üzerine otomatik çekilen şema yazılacak. Devam edilsin mi?',
            { title: 'Body Schema Üzerine Yaz', okText: 'Devam', cancelText: 'Vazgeç', danger: true })
        : window.confirm('Mevcut Body Schema\'nın üzerine otomatik çekilen şema yazılacak. Devam edilsin mi?')
      if (!ok) return
    }

    setResolving(true)
    setResolveErr(null)
    try {
      const r = await fetch('/Integrations/api/endpoints/resolve-body', {
        method: 'POST', credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json', RequestVerificationToken: getCsrf() },
        body: JSON.stringify({
          apiProfileId: form.apiProfileId,
          httpMethod: form.httpMethod,
          urlTemplate: form.urlTemplate,
        }),
      })
      const d = await r.json()
      if (d.success && d.bodyJson) {
        update({ bodySchema: d.bodyJson })
        setResolveErr(null)
        setResolvedFields(d.fields || null)
        // Hatırlatma: çekilen şema henüz DB'ye yazılmadı, Kaydet butonu lazım
        const reqCount = (d.fields || []).filter(f => f.required).length
        const totalCount = (d.fields || []).length
        const msg = totalCount > 0
          ? `✓ Şema çekildi (${d.source}, ${d.durationMs}ms) — ${totalCount} alan, ${reqCount} zorunlu · Kaydet'e basmayı unutmayın`
          : `✓ Şema çekildi (${d.source}, ${d.durationMs}ms) — Kaydet'e basmayı unutmayın`
        toast(msg, 'ok')
      } else {
        // Friendly hata paneli için diagnose et
        const diag = diagnoseResolveError(d.error)
        setResolveErr({
          ...diag,
          rawError: d.error,
          source: d.source,
          durationMs: d.durationMs,
          httpStatusCode: d.httpStatusCode,
        })
        toast(`✗ ${diag.title}`, 'err')
      }
    } catch (e) {
      const diag = diagnoseResolveError(e.message)
      setResolveErr({ ...diag, rawError: e.message })
      toast('Sunucu hatası: ' + e.message, 'err')
    } finally {
      setResolving(false)
    }
  }

  const formatJson = () => {
    if (!form.bodySchema?.trim()) return
    try {
      const parsed = JSON.parse(form.bodySchema)
      update({ bodySchema: JSON.stringify(parsed, null, 2) })
    } catch {
      toast('Geçersiz JSON — formatlanamadı', 'err')
    }
  }

  return (
    <div className="iw-modal-bd" onClick={() => !saving && !fullscreen && onClose()}>
      <div className={'eem-modal' + (fullscreen ? ' is-fullscreen' : '')}
           onClick={e => e.stopPropagation()}>
        {/* Header */}
        <div className="eem-header">
          <div className="eem-title">
            {form.id ? 'Endpoint Düzenle' : 'Yeni Endpoint'}
            {form.name && <span className="eem-title-sub"> — {form.name}</span>}
            {hasChanges && (
              <span title="Kaydedilmemiş değişiklik var"
                    style={{
                      display: 'inline-block', marginLeft: 8,
                      width: 8, height: 8, borderRadius: '50%',
                      background: 'var(--iw-amber-color)',
                      boxShadow: '0 0 0 3px rgba(245,158,11,.25)',
                    }} />
            )}
          </div>
          <button className="eem-icon-btn" title={fullscreen ? 'Küçült' : 'Tam Ekran'}
                  onClick={() => setFullscreen(f => !f)} disabled={saving}>
            {fullscreen ? <Minimize2 size={15} /> : <Maximize2 size={15} />}
          </button>
          <button className="eem-icon-btn" title="Kapat (Esc)"
                  onClick={onClose} disabled={saving}>
            <X size={16} />
          </button>
        </div>

        {/* Body — sol tab + sağ içerik */}
        <div className="eem-body">
          {/* Sol — tab menüsü */}
          <nav className="eem-tabs">
            {TABS.map(t => {
              const Icon = t.icon
              return (
                <button key={t.id}
                        className={'eem-tab' + (tab === t.id ? ' is-active' : '')}
                        onClick={() => setTab(t.id)}>
                  <Icon size={14} />
                  <span>{t.label}</span>
                </button>
              )
            })}
          </nav>

          {/* Sağ — içerik */}
          <div className="eem-content">
            {tab === 'basic' && (
              <div className="eem-tab-pane">
                <div className="iw-field">
                  <label>API Profile *</label>
                  <select value={form.apiProfileId}
                          onChange={e => update({ apiProfileId: e.target.value })} disabled={saving}>
                    <option value="">— Seçin —</option>
                    {(profiles || []).map(p => (
                      <option key={p.id} value={p.id}>{p.name} — {p.baseUrl}</option>
                    ))}
                  </select>
                </div>

                <div className="iw-field">
                  <label>Ad *</label>
                  <input value={form.name} onChange={e => update({ name: e.target.value })}
                         placeholder="Örn: Netsis Sipariş POST" maxLength={200} disabled={saving} />
                </div>

                <div style={{ display: 'grid', gridTemplateColumns: '120px 1fr', gap: 12 }}>
                  <div className="iw-field">
                    <label>HTTP Method</label>
                    <select value={form.httpMethod}
                            onChange={e => update({ httpMethod: e.target.value })} disabled={saving}>
                      {HTTP_METHODS.map(m => <option key={m} value={m}>{m}</option>)}
                    </select>
                  </div>
                  <div className="iw-field">
                    <label>URL Şablonu *</label>
                    <EndpointUrlPicker
                      value={form.urlTemplate}
                      onChange={(v) => {
                        // Manuel yazıldı — picked metadata sıfırla (artık katalogtan değil)
                        if (v !== form.urlTemplate) setPickedCatalogItem(null)
                        update({ urlTemplate: v })
                      }}
                      onPick={(it) => {
                        // URL değişiyor mu? Body schema doluysa kullanıcıya sor
                        const urlChanged = it.urlTemplate !== form.urlTemplate
                        let clearBody = false
                        if (urlChanged && form.bodySchema?.trim()) {
                          clearBody = window.confirm(
                            `URL değişiyor (${form.urlTemplate || '(boş)'} → ${it.urlTemplate}).\n\n` +
                            `Mevcut Body Schema yeni endpoint'e uymayabilir. Body Schema sıfırlansın mı?\n\n` +
                            `• Tamam: Body sıfırlanır (Şablon Galerisi veya Otomatik Çek ile yeniden doldurun)\n` +
                            `• İptal: Mevcut body korunur (manuel düzenleme gerekebilir)`
                          )
                        }
                        // Seçilen katalog satırının URL+method+description+name'i otomatik dolar
                        update({
                          urlTemplate: it.urlTemplate,
                          httpMethod:  (it.httpMethod || form.httpMethod).toUpperCase(),
                          // Boş Ad: Resource + Summary'den türet (örn. "ARPs Yeni Kayıt Oluştur")
                          name: form.name?.trim()
                            ? form.name
                            : [it.resource, it.summary || it.methodName].filter(Boolean).join(' '),
                          // Kullanıcı henüz açıklama yazmadıysa katalog tarifini koy
                          description: form.description?.trim()
                            ? form.description
                            : (it.summary || it.description || (it.methodName ? `${it.resource} ${it.methodName}` : null) || null),
                          // Body sıfırla onaylandıysa
                          ...(clearBody ? { bodySchema: '' } : {}),
                        })
                        setPickedCatalogItem(it)
                        setResolveErr(null)  // önceki hata mesajını temizle
                      }}
                      catalog={catalog}
                      disabled={saving}
                      httpMethod={form.httpMethod}
                    />
                    <span className="iw-field-hint">
                      Profile'ın base URL'i sonuna eklenir. Sağdaki ▾ ile katalogdan seçin
                      ({catalog.length} endpoint), veya custom URL yazın — liste soldaki HTTP
                      Method'a göre daraltılır.
                    </span>
                  </div>
                </div>

                <div className="iw-field">
                  <label>Açıklama</label>
                  <input value={form.description || ''}
                         onChange={e => update({ description: e.target.value })}
                         placeholder="Bu endpoint ne yapıyor?" maxLength={1000} disabled={saving} />
                </div>

                <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginTop: 8 }}>
                  <button className={'iw-switch' + (form.isActive ? ' is-on' : '')}
                          onClick={() => update({ isActive: !form.isActive })} disabled={saving}>
                    <span className="iw-switch__thumb" />
                  </button>
                  <span style={{ fontSize: 13 }}>Aktif</span>
                </div>
              </div>
            )}

            {tab === 'schema' && (
              <div className="eem-tab-pane eem-tab-pane--schema">
                <div className="eem-schema-toolbar">
                  <span style={{ fontSize: 12, color: 'var(--iw-muted)' }}>
                    Step 3 alan eşlemesinde target alanları olarak gösterilecek JSON şeması.
                  </span>
                  <span style={{ flex: 1 }} />
                  <button className="iw-btn-ghost" onClick={handleAutoResolve}
                          disabled={saving || resolving}
                          title="Endpoint'e bağlanıp Describe ile body şemasını otomatik çek">
                    {resolving
                      ? <><Loader2 className="iw-spin" size={13} style={{ verticalAlign: 'middle', marginRight: 4 }} /> Çekiliyor…</>
                      : <><Download size={13} style={{ verticalAlign: 'middle', marginRight: 4 }} /> Otomatik Çek</>}
                  </button>
                  <button className="iw-btn-ghost" onClick={() => setShowGallery(true)}
                          disabled={saving}
                          title="Hazır body şablonlarından seç">
                    <Sparkles size={13} style={{ verticalAlign: 'middle', marginRight: 4 }} />
                    Şablon Galerisi
                  </button>
                  <button className="iw-btn-ghost" onClick={() => setShowSaveAs(true)}
                          disabled={saving || !form.bodySchema?.trim() || !!schemaError}
                          title="Bu Body Schema'yı şablon olarak galeriye kaydet">
                    <BookmarkPlus size={13} style={{ verticalAlign: 'middle', marginRight: 4 }} />
                    Şablon Kaydet
                  </button>
                  <button className="iw-btn-ghost" onClick={formatJson}
                          disabled={saving || !form.bodySchema?.trim()}
                          title="JSON'u prettify et (girintile)">
                    Formatla
                  </button>
                </div>

                {/* Seçili endpoint info etiketi — InputType + Summary + uygun çek/şablon önerisi */}
                {pickedCatalogItem && (() => {
                  const kind = classifyInputType(pickedCatalogItem.inputType)
                  const color = inputTypeColor(kind)
                  const isAutoResolveable = kind === 'resource'
                  const isComposite = (pickedCatalogItem.inputType || '').includes(',')
                  const resourceType = extractResourceInputType(pickedCatalogItem.inputType)
                  return (
                    <div style={{
                      display: 'flex', alignItems: 'flex-start', gap: 10,
                      padding: '8px 12px', borderRadius: 8,
                      background: `var(--iw-${color}-bg)`,
                      border: `1px solid var(--iw-${color}-color)`,
                      fontSize: 11,
                    }}>
                      <span style={{ fontSize: 14, lineHeight: 1, paddingTop: 1 }}>
                        {isAutoResolveable ? '✓' : kind === 'none' ? 'ℹ' : '⚠'}
                      </span>
                      <div style={{ flex: 1, minWidth: 0, lineHeight: 1.5 }}>
                        <div style={{ color: 'var(--iw-text)', fontWeight: 600, marginBottom: 2 }}>
                          {pickedCatalogItem.resource}
                          {pickedCatalogItem.methodName && (
                            <span style={{ fontWeight: 400, opacity: 0.85 }}>
                              {' · '}{pickedCatalogItem.summary || pickedCatalogItem.methodName}
                            </span>
                          )}
                        </div>
                        <div style={{ color: `var(--iw-${color}-color)`, fontSize: 10 }}>
                          {pickedCatalogItem.inputType ? (
                            <>
                              <strong style={{ fontFamily: 'monospace' }}>InputType: {pickedCatalogItem.inputType}</strong>
                              {' — '}
                              {isComposite ? (
                                <>
                                  Kompozit parametre
                                  {resourceType && <> (Describe için <code>{resourceType}</code> kullanılır)</>}
                                </>
                              ) : inputTypeLabel(kind)}
                              {isAutoResolveable && !isComposite && ' — "Otomatik Çek" çalışır'}
                              {kind === 'param' && ' — Şablon Galerisi öneriliyor'}
                            </>
                          ) : (
                            <>Body parametresi yok ({inputTypeLabel(kind)})</>
                          )}
                        </div>
                      </div>
                    </div>
                  )
                })()}

                {/* Zorunlu Alanlar paneli — Services/Definitions katmanından gelen field meta */}
                {resolvedFields && resolvedFields.length > 0 && !resolveError && (() => {
                  const required = resolvedFields.filter(f => f.required)
                  const optional = resolvedFields.filter(f => !f.required)
                  return (
                    <details open={required.length > 0} style={{
                      padding: '8px 12px',
                      background: 'var(--iw-emerald-bg)',
                      border: '1px solid var(--iw-emerald-color)',
                      borderRadius: 8,
                      fontSize: 12,
                    }}>
                      <summary style={{
                        cursor: 'pointer', fontWeight: 600,
                        color: 'var(--iw-emerald-color)', display: 'flex',
                        alignItems: 'center', gap: 6,
                      }}>
                        ✓ Şema çıkarıldı — {resolvedFields.length} alan
                        {required.length > 0 && (
                          <span style={{
                            padding: '1px 7px', borderRadius: 4, fontSize: 10, fontWeight: 700,
                            background: 'var(--iw-rose-color)', color: '#fff',
                          }}>
                            {required.length} ZORUNLU
                          </span>
                        )}
                      </summary>
                      <div style={{ marginTop: 8, color: 'var(--iw-text)' }}>
                        {required.length > 0 && (
                          <div style={{ marginBottom: 8 }}>
                            <div style={{ fontSize: 10, fontWeight: 700, color: 'var(--iw-rose-color)', marginBottom: 4, textTransform: 'uppercase' }}>
                              Zorunlu alanlar ({required.length}) — Step 3'te eşleştirilmesi gerekli
                            </div>
                            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))', gap: 4 }}>
                              {required.map((f, i) => (
                                <div key={i} style={{
                                  display: 'flex', alignItems: 'center', gap: 5,
                                  padding: '3px 6px', background: 'var(--iw-rose-bg)',
                                  border: '1px solid var(--iw-rose-color)', borderRadius: 4,
                                  fontFamily: 'monospace', fontSize: 10,
                                }}
                                title={`${f.type}${f.maxLength ? ` (max ${f.maxLength})` : ''}${f.enum ? ` enum: ${f.enum}` : ''}`}>
                                  <span style={{ color: 'var(--iw-rose-color)', fontWeight: 700 }}>*</span>
                                  <span style={{ flex: 1, color: 'var(--iw-text)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                                    {f.path}
                                  </span>
                                  <span style={{ color: 'var(--iw-muted)', fontSize: 9 }}>{f.type}</span>
                                </div>
                              ))}
                            </div>
                          </div>
                        )}
                        {optional.length > 0 && (
                          <details style={{ marginTop: 4 }}>
                            <summary style={{ cursor: 'pointer', fontSize: 10, color: 'var(--iw-muted)' }}>
                              + {optional.length} opsiyonel alan
                            </summary>
                            <div style={{
                              marginTop: 6, display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))', gap: 3,
                              maxHeight: 180, overflow: 'auto',
                            }}>
                              {optional.map((f, i) => (
                                <div key={i} style={{
                                  display: 'flex', alignItems: 'center', gap: 5,
                                  padding: '2px 6px', background: 'var(--iw-bg)',
                                  border: '1px solid var(--iw-border)', borderRadius: 4,
                                  fontFamily: 'monospace', fontSize: 10,
                                }}
                                title={`${f.type}${f.maxLength ? ` (max ${f.maxLength})` : ''}${f.enum ? ` enum: ${f.enum}` : ''}`}>
                                  <span style={{ flex: 1, color: 'var(--iw-text)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                                    {f.path}
                                  </span>
                                  <span style={{ color: 'var(--iw-muted)', fontSize: 9 }}>{f.type}</span>
                                </div>
                              ))}
                            </div>
                          </details>
                        )}
                      </div>
                    </details>
                  )
                })()}

                {/* Otomatik Çek hata paneli — friendly mesaj + çözüm önerileri */}
                {resolveError && (
                  <div style={{
                    padding: '10px 12px',
                    background: 'var(--iw-rose-bg)',
                    border: '1px solid var(--iw-rose-color)',
                    borderRadius: 8,
                    fontSize: 12,
                    display: 'flex', flexDirection: 'column', gap: 6,
                  }}>
                    <div style={{
                      display: 'flex', alignItems: 'center', justifyContent: 'space-between',
                      gap: 8,
                    }}>
                      <strong style={{ color: 'var(--iw-rose-color)', fontSize: 13 }}>
                        ⚠ {resolveError.title}
                        {resolveError.httpStatusCode && (
                          <span style={{ marginLeft: 8, fontFamily: 'monospace', fontWeight: 'normal', opacity: 0.85 }}>
                            HTTP {resolveError.httpStatusCode}
                          </span>
                        )}
                      </strong>
                      <button onClick={() => setResolveErr(null)}
                              style={{
                                background: 'transparent', border: 'none', cursor: 'pointer',
                                color: 'var(--iw-rose-color)', padding: 2,
                              }}
                              title="Kapat">
                        <X size={13} />
                      </button>
                    </div>
                    <div style={{ color: 'var(--iw-text)', lineHeight: 1.5 }}>
                      {resolveError.lead}
                    </div>

                    {/* Denenenler — backend'in çoklu-katman cevabını parse et */}
                    {(() => {
                      const attempts = parseAttempts(resolveError.rawError)
                      if (attempts.length < 2) return null  // tek hata → liste gereksiz
                      return (
                        <div style={{
                          background: 'var(--iw-bg)', border: '1px solid var(--iw-border)',
                          borderRadius: 6, padding: '6px 10px',
                          fontSize: 11, lineHeight: 1.6,
                        }}>
                          <div style={{ fontWeight: 600, color: 'var(--iw-muted)', marginBottom: 3 }}>
                            Denenen yöntemler ({attempts.length}):
                          </div>
                          {attempts.map((a, i) => (
                            <div key={i} style={{
                              display: 'grid', gridTemplateColumns: '180px 1fr', gap: 8,
                              color: 'var(--iw-text)',
                            }}>
                              <span style={{ color: 'var(--iw-muted)' }}>
                                {i + 1}. {layerLabel(a.layer)}
                              </span>
                              <span style={{ fontFamily: 'monospace', fontSize: 10 }}>
                                {a.detail}
                              </span>
                            </div>
                          ))}
                        </div>
                      )
                    })()}

                    {resolveError.reasons?.length > 0 && (
                      <ul style={{
                        margin: 0, paddingLeft: 18, color: 'var(--iw-text)',
                        display: 'flex', flexDirection: 'column', gap: 3, lineHeight: 1.5,
                      }}>
                        {resolveError.reasons.map((r, i) => (
                          <li key={i} dangerouslySetInnerHTML={{
                            __html: r.replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>'),
                          }} />
                        ))}
                      </ul>
                    )}
                    <div style={{ display: 'flex', gap: 8, marginTop: 4, alignItems: 'center', flexWrap: 'wrap' }}>
                      <button className="iw-btn-secondary" onClick={() => setShowGallery(true)}
                              style={{ padding: '4px 10px', fontSize: 11 }}>
                        <Sparkles size={11} style={{ verticalAlign: 'middle', marginRight: 4 }} />
                        Şablon Galerisi'ni aç
                      </button>
                      <a href="/Integrations#profiles" target="_blank" rel="noopener"
                         className="iw-btn-secondary"
                         style={{ padding: '4px 10px', fontSize: 11, textDecoration: 'none' }}
                         title="Profili düzenle / Bağlantıyı Test Et">
                        <KeyRound size={11} style={{ verticalAlign: 'middle', marginRight: 4 }} />
                        Profili kontrol et
                      </a>
                      <span style={{ flex: 1 }} />
                      <details style={{ fontSize: 10, color: 'var(--iw-muted)' }}>
                        <summary style={{ cursor: 'pointer' }}>Ham hata detayı</summary>
                        <pre style={{
                          margin: '6px 0 0', padding: 8, background: 'var(--iw-bg)',
                          border: '1px solid var(--iw-border)', borderRadius: 4,
                          fontFamily: "'JetBrains Mono','Consolas',monospace", fontSize: 10,
                          whiteSpace: 'pre-wrap', wordBreak: 'break-word', maxHeight: 140,
                          overflow: 'auto', color: 'var(--iw-text)',
                        }}>{resolveError.rawError}</pre>
                      </details>
                    </div>
                  </div>
                )}

                <textarea
                  value={form.bodySchema || ''}
                  onChange={e => update({ bodySchema: e.target.value })}
                  placeholder='{ "FatUst": { "CariKod": "", "Tarih": "", "TIPI": "" }, "Kalemler": [{ "StokKod": "", "Miktar": 0 }] }'
                  disabled={saving}
                  className={'eem-schema-editor' + (schemaError ? ' has-error' : '')}
                  spellCheck={false}
                />
                {schemaError && (
                  <div className="eem-schema-error">⚠ JSON parse hatası: {schemaError}</div>
                )}
              </div>
            )}
          </div>
        </div>

        {/* Footer */}
        <div className="eem-footer">
          {hasChanges && !saving && (
            <span style={{
              fontSize: 11, color: 'var(--iw-amber-color)',
              display: 'flex', alignItems: 'center', gap: 5,
            }}>
              <span style={{
                display: 'inline-block', width: 8, height: 8, borderRadius: '50%',
                background: 'var(--iw-amber-color)',
              }} />
              Kaydedilmemiş değişiklik var
            </span>
          )}
          <span style={{ flex: 1 }} />
          <button className="iw-btn-secondary" onClick={onClose} disabled={saving}>
            Vazgeç
          </button>
          <button className="iw-btn-primary" onClick={handleSave} disabled={saving}
                  style={hasChanges ? {
                    boxShadow: '0 0 0 2px var(--iw-amber-color), 0 0 14px rgba(245,158,11,.35)',
                  } : undefined}>
            {saving ? <><Loader2 className="iw-spin" size={14} /> Kaydediliyor</> : <><Save size={14} /> Kaydet</>}
          </button>
        </div>

        {/* Şablon Galerisi modal — body schema'yi overwrite eder */}
        {showGallery && (
          <TemplateGalleryModal
            onClose={() => setShowGallery(false)}
            onPick={(bodyJson, tpl) => {
              if (form.bodySchema?.trim() && !window.confirm(
                `Mevcut Body Schema'nın üzerine "${tpl.name}" şablonu yazılacak. Devam edilsin mi?`
              )) return
              update({ bodySchema: bodyJson })
              toast(`✓ "${tpl.name}" şablonu yüklendi`, 'ok')
              setTab('schema')
            }}
          />
        )}

        {/* Şablon Olarak Kaydet modal */}
        {showSaveAs && (
          <SaveTemplateModal
            initial={{
              name: form.name,
              urlTemplate: form.urlTemplate,
              httpMethod: form.httpMethod,
              bodyJson: form.bodySchema || '',
            }}
            onClose={() => setShowSaveAs(false)}
            onSaved={() => { /* opsiyonel: galeri açılırsa refresh */ }}
          />
        )}
      </div>
    </div>
  )
}
