/**
 * CombinationPickerModal
 *
 * Satış teklifi satırında kombinasyon seçici modal.
 * - "Mevcut" tab: /Sales/GetCombinations?materialCode=X listesinden seçim
 * - "Yeni"   tab: /Sales/GetMaterialFeatures?materialCode=X özelliklerinden seçim
 *                 + her özellik için açıklama
 *                 Uygula → /Sales/ResolveOrCreateCombination
 *                    matched=true  → mevcut koda yönlendir (banner ile bildir)
 *                    matched=false → yeni kod üretildi (toast mesajı)
 *
 * Props:
 *   materialCode   — Seçili malzemenin kodu (boş ise "önce malzeme seç" mesajı)
 *   currentCode    — Satırda şu an atanmış kombinasyon kodu (varsa)
 *   currentDetails — [{ featureName, valueCode, valueName, description }, ...]
 *   onApply(code, details) — Seçim tamamlandığında ve modal kapanırken çağrılır
 *   onClose()
 */
import { useState, useEffect, useCallback } from 'react'
import { createPortal } from 'react-dom'
import { CircleDot, Plus, X, Check, AlertCircle, Loader2 } from 'lucide-react'

function useIsLight() {
  var [light, setLight] = useState(function() {
    return typeof document !== 'undefined' && document.body.classList.contains('app-theme-light')
  })
  useEffect(function() {
    var obs = new MutationObserver(function() {
      setLight(document.body.classList.contains('app-theme-light'))
    })
    obs.observe(document.body, { attributes: true, attributeFilter: ['class'] })
    return function() { obs.disconnect() }
  }, [])
  return light
}

export default function CombinationPickerModal(props) {
  var materialCode   = props.materialCode
  var materialName   = props.materialName || ''
  var currentCode    = props.currentCode
  var currentDetails = Array.isArray(props.currentDetails) ? props.currentDetails : []
  var onApply        = props.onApply
  var onClose        = props.onClose

  var isLight = useIsLight()
  var [tab, setTab] = useState('existing')   // 'existing' | 'new'
  var [existing, setExisting] = useState([])
  var [features, setFeatures] = useState([])  // [{ featureId, featureName, values:[{id,code,name}] }]
  var [selections, setSelections] = useState({})  // featureId -> { valueId, description }
  var [loadingExisting, setLoadingExisting] = useState(false)
  var [loadingFeatures, setLoadingFeatures] = useState(false)
  var [resolving, setResolving] = useState(false)
  var [resolveMsg, setResolveMsg] = useState(null)   // { type:'matched'|'created'|'error', text, code? }
  // Mevcut kombinasyonlardan secilmis aday — kullanici aciklamalari duzenleyip Uygula'ya basar.
  var [pendingExisting, setPendingExisting] = useState(null) // { code, details:[{featureName,valueCode,valueName,description,lineOrder}] }

  // Fetch mevcut kombinasyonlar
  useEffect(function() {
    if (!materialCode) return
    setLoadingExisting(true)
    fetch('/Sales/GetCombinations?materialCode=' + encodeURIComponent(materialCode), { credentials: 'same-origin' })
      .then(function(r) { return r.json() })
      .then(function(data) { setExisting(Array.isArray(data) ? data : []) })
      .catch(function() { setExisting([]) })
      .finally(function() { setLoadingExisting(false) })
  }, [materialCode])

  // Onceden secilmis bir kombinasyon varsa (satirda currentCode dolu), mevcut liste
  // yuklendikten sonra o satiri otomatik "pending" moduna al — boylece kullanici daha
  // once girdigi aciklamalari ve secili halini tek tiklama beklemeden gorur.
  useEffect(function() {
    if (!currentCode || !existing || existing.length === 0) return
    if (pendingExisting) return
    var match = existing.find(function(c) { return c.code === currentCode })
    if (!match) return
    var details = (match.features || []).map(function(fv, idx) {
      var prev = currentDetails.find(function(d) {
        return (d.featureName || '').toLowerCase() === (fv.feature || '').toLowerCase()
      })
      return {
        featureName: fv.feature,
        featureCode: null,
        valueCode: fv.valueCode || '',
        valueName: fv.value || '',
        description: prev ? (prev.description || '') : '',
        lineOrder: idx + 1,
      }
    })
    setPendingExisting({ configId: match.configId, code: match.code, details: details })
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [existing, currentCode])

  // Fetch özellikler (yeni kombinasyon)
  useEffect(function() {
    if (!materialCode) return
    setLoadingFeatures(true)
    fetch('/Sales/GetMaterialFeatures?materialCode=' + encodeURIComponent(materialCode), { credentials: 'same-origin' })
      .then(function(r) { return r.json() })
      .then(function(data) {
        var list = Array.isArray(data) ? data : []
        setFeatures(list)
        // currentDetails varsa ilgili select/description'ları doldur
        var initial = {}
        list.forEach(function(f) {
          var match = currentDetails.find(function(d) {
            return (d.featureName || '').toLowerCase() === (f.featureName || '').toLowerCase()
          })
          if (match) {
            var v = (f.values || []).find(function(x) { return x.code === match.valueCode || x.name === match.valueName })
            if (v) initial[f.featureId] = { valueId: v.id, description: match.description || '' }
          }
        })
        setSelections(initial)
      })
      .catch(function() { setFeatures([]) })
      .finally(function() { setLoadingFeatures(false) })
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [materialCode])

  // Mevcut kombinasyonlardan secim — hemen apply ETMEZ, duzenlenebilir
  // aciklama adimina geciriz. Kullanici isterse aciklama girer, sonra Uygula'ya basar.
  var handleSelectExisting = useCallback(function(combo) {
    var details = (combo.features || []).map(function(fv, idx) {
      var prev = currentDetails.find(function(d) {
        return (d.featureName || '').toLowerCase() === (fv.feature || '').toLowerCase()
      })
      return {
        featureName: fv.feature,
        featureCode: null,
        valueCode: fv.valueCode || '',
        valueName: fv.value || '',
        description: prev ? (prev.description || '') : '',
        lineOrder: idx + 1,
      }
    })
    setPendingExisting({ configId: combo.configId, code: combo.code, details: details })
    setResolveMsg(null)
  }, [currentDetails])

  var handleApplyExistingConfirm = useCallback(function() {
    if (!pendingExisting) return
    onApply(pendingExisting.configId, pendingExisting.code, pendingExisting.details)
  }, [pendingExisting, onApply])

  // Cift tiklama — aciklama girilmeden direkt secim
  var handleDblClickApply = useCallback(function(combo) {
    var details = (combo.features || []).map(function(fv, idx) {
      return {
        featureName: fv.feature,
        featureCode: null,
        valueCode: fv.valueCode || '',
        valueName: fv.value || '',
        description: '',
        lineOrder: idx + 1,
      }
    })
    onApply(combo.configId, combo.code, details)
  }, [onApply])

  function updateExistingDescription(idx, text) {
    setPendingExisting(function(prev) {
      if (!prev) return prev
      var nextDetails = prev.details.slice()
      nextDetails[idx] = Object.assign({}, nextDetails[idx], { description: text })
      return { configId: prev.configId, code: prev.code, details: nextDetails }
    })
  }

  var handleApplyNew = useCallback(async function() {
    if (!materialCode) return
    var selList = []
    for (var i = 0; i < features.length; i++) {
      var f = features[i]
      var sel = selections[f.featureId]
      if (!sel || !sel.valueId) continue
      var val = (f.values || []).find(function(v) { return v.id === sel.valueId })
      if (!val) continue
      selList.push({
        featureName: f.featureName,
        featureId: f.featureId,
        valueId: val.id,
        valueCode: val.code,
        valueName: val.name,
        description: sel.description || null,
      })
    }
    if (selList.length === 0) {
      setResolveMsg({ type: 'error', text: 'En az bir özellik değeri seçmelisiniz.' })
      return
    }

    setResolving(true)
    setResolveMsg(null)
    try {
      var resp = await fetch('/Sales/ResolveOrCreateCombination', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'same-origin',
        body: JSON.stringify({ materialCode: materialCode, selections: selList }),
      })
      var data = await resp.json()
      if (!data.success) {
        setResolveMsg({ type: 'error', text: data.message || 'Kombinasyon oluşturulamadı.' })
        return
      }

      // Detayları apply payload'una dönüştür (kullanıcının girdiği açıklamaları koru)
      var details = selList.map(function(s, idx) {
        return {
          featureName: s.featureName,
          featureCode: null,
          valueCode: s.valueCode,
          valueName: s.valueName,
          description: s.description || '',
          lineOrder: idx + 1,
        }
      })

      if (data.matched) {
        setResolveMsg({ type: 'matched', text: 'Bu kombinasyon zaten kayıtlı: ' + data.code + ' — otomatik seçildi', code: data.code })
        // 900ms sonra modal'ı kapat
        setTimeout(function() { onApply(data.configId, data.code, details) }, 900)
      } else {
        setResolveMsg({ type: 'created', text: 'Yeni kombinasyon oluşturuldu: ' + data.code, code: data.code })
        setTimeout(function() { onApply(data.configId, data.code, details) }, 700)
      }
    } catch (err) {
      setResolveMsg({ type: 'error', text: 'Bağlantı hatası: ' + (err.message || err) })
    } finally {
      setResolving(false)
    }
  }, [materialCode, features, selections, onApply])

  // Stil sınıfları — glassmorphism
  var overlayBg = isLight ? 'rgba(15,23,42,.45)' : 'rgba(0,0,0,.55)'
  var panelBg = isLight ? '#ffffff' : 'rgba(23,26,43,.96)'
  var panelBorder = isLight ? '#e2e8f0' : 'rgba(255,255,255,.08)'
  var textColor = isLight ? '#1e293b' : 'rgba(255,255,255,.88)'
  var mutedText = isLight ? '#64748b' : 'rgba(255,255,255,.55)'
  var subSurface = isLight ? '#f8fafc' : 'rgba(255,255,255,.03)'

  var modalContent = (
    <div
      onClick={onClose}
      style={{
        position: 'fixed', inset: 0, zIndex: 10000,
        background: overlayBg,
        backdropFilter: 'blur(6px)',
        WebkitBackdropFilter: 'blur(6px)',
        display: 'flex', alignItems: 'center', justifyContent: 'center',
        padding: '20px',
        animation: 'cpmFadeIn .15s ease-out',
      }}
    >
      <style>{`
        @keyframes cpmFadeIn { from { opacity: 0; } to { opacity: 1; } }
        @keyframes cpmSlide  { from { transform: translateY(8px) scale(.97); opacity: 0; } to { transform: none; opacity: 1; } }
      `}</style>
      <div
        onClick={function(e) { e.stopPropagation() }}
        style={{
          width: 'min(880px, 95vw)',
          height: 'min(620px, 88vh)',
          background: panelBg,
          border: '1px solid ' + panelBorder,
          borderRadius: 16,
          boxShadow: '0 24px 72px rgba(0,0,0,.5)',
          display: 'flex', flexDirection: 'column',
          overflow: 'hidden',
          color: textColor,
          animation: 'cpmSlide .22s cubic-bezier(.23,1,.32,1)',
        }}
      >
        {/* Header */}
        <div style={{
          padding: '14px 18px',
          borderBottom: '1px solid ' + panelBorder,
          display: 'flex', alignItems: 'center', gap: 10,
          flex: '0 0 auto',
        }}>
          <CircleDot size={16} style={{ color: '#818cf8' }} />
          <div style={{ flex: 1 }}>
            <div style={{ fontSize: '0.92rem', fontWeight: 700 }}>Kombinasyon Seçimi</div>
            <div style={{ fontSize: '0.72rem', color: mutedText, marginTop: 2 }}>
              <strong style={{ color: textColor }}>{materialCode || '—'}</strong>
              {materialName && (
                <span style={{ marginLeft: 6 }}>{materialName}</span>
              )}
              {currentCode && (
                <span style={{ marginLeft: 10 }}>
                  · Seçili: <strong style={{ color: '#a5b4fc' }}>{currentCode}</strong>
                </span>
              )}
            </div>
          </div>
          <button
            onClick={onClose}
            style={{
              background: 'transparent', border: 'none', cursor: 'pointer',
              color: mutedText, padding: 6, borderRadius: 6,
              display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
            }}
            title="Kapat"
          >
            <X size={18} />
          </button>
        </div>

        {/* Main area — sol sidebar (tabs) + sag icerik */}
        <div style={{ flex: 1, display: 'flex', minHeight: 0 }}>

          {/* Sol sidebar — dikey tab butonlari */}
          <div style={{
            width: 180,
            flex: '0 0 180px',
            borderRight: '1px solid ' + panelBorder,
            background: subSurface,
            display: 'flex', flexDirection: 'column',
            padding: '10px 8px',
            gap: 4,
          }}>
            <SideTabButton active={tab === 'existing'} onClick={function() { setTab('existing'); setResolveMsg(null) }} isLight={isLight}>
              <CircleDot size={14} style={{ marginRight: 8, flex: '0 0 auto' }} />
              Mevcut Kombinasyonlar
            </SideTabButton>
            <SideTabButton active={tab === 'new'} onClick={function() { setTab('new'); setResolveMsg(null) }} isLight={isLight}>
              <Plus size={14} style={{ marginRight: 8, flex: '0 0 auto' }} />
              Yeni Kombinasyon
            </SideTabButton>
          </div>

          {/* Sag taraf — icerik + footer */}
          <div style={{ flex: 1, display: 'flex', flexDirection: 'column', minWidth: 0 }}>

            {/* Body */}
            <div style={{ flex: 1, display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
              {!materialCode && (
                <div style={{ padding: 24, textAlign: 'center', color: mutedText, fontSize: '.82rem' }}>
                  Önce malzeme seçin.
                </div>
              )}

              {/* Mevcut Kombinasyonlar — ExistingTab kendi scroll'unu yonetiyor */}
              {materialCode && tab === 'existing' && (
                <ExistingTab
                  loading={loadingExisting}
                  items={existing}
                  currentCode={currentCode}
                  pendingCode={pendingExisting ? pendingExisting.code : null}
                  onSelect={handleSelectExisting}
                  isLight={isLight}
                  mutedText={mutedText}
                  textColor={textColor}
                  panelBorder={panelBorder}
                  panelBg={panelBg}
                  subSurface={subSurface}
                  pendingDetails={pendingExisting ? pendingExisting.details : null}
                  onDescriptionChange={updateExistingDescription}
                  onDblClickApply={handleDblClickApply}
                />
              )}

              {/* Yeni Kombinasyon + banner — tek scrollable alan */}
              {materialCode && tab === 'new' && (
                <div style={{ flex: 1, overflowY: 'auto', padding: '14px 18px' }}>
                  <NewTab
                    loading={loadingFeatures}
                    features={features}
                    selections={selections}
                    onSelectionChange={setSelections}
                    isLight={isLight}
                    mutedText={mutedText}
                    textColor={textColor}
                    panelBorder={panelBorder}
                    subSurface={subSurface}
                  />
                  {/* Banner / Mesaj */}
                  {resolveMsg && (
                    <div style={{
                      marginTop: 14,
                      padding: '10px 14px',
                      borderRadius: 10,
                      display: 'flex', alignItems: 'center', gap: 10,
                      fontSize: '.8rem', fontWeight: 500,
                      background:
                        resolveMsg.type === 'error'   ? (isLight ? '#fef2f2' : 'rgba(239,68,68,.12)') :
                        resolveMsg.type === 'matched' ? (isLight ? '#fef3c7' : 'rgba(245,158,11,.12)') :
                                                         (isLight ? '#dcfce7' : 'rgba(16,185,129,.12)'),
                      border: '1px solid ' + (
                        resolveMsg.type === 'error'   ? 'rgba(239,68,68,.35)' :
                        resolveMsg.type === 'matched' ? 'rgba(245,158,11,.35)' :
                                                         'rgba(16,185,129,.35)'
                      ),
                      color:
                        resolveMsg.type === 'error'   ? (isLight ? '#b91c1c' : '#fca5a5') :
                        resolveMsg.type === 'matched' ? (isLight ? '#92400e' : '#fcd34d') :
                                                         (isLight ? '#065f46' : '#6ee7b7'),
                    }}>
                      {resolveMsg.type === 'error' ? <AlertCircle size={15} /> : <Check size={15} />}
                      <span>{resolveMsg.text}</span>
                    </div>
                  )}
                </div>
              )}
            </div>

            {/* Footer — mevcut tab'da secim yapildiysa Iptal/Uygula */}
            {materialCode && tab === 'existing' && pendingExisting && (
              <div style={{
                padding: '12px 18px',
                borderTop: '1px solid ' + panelBorder,
                display: 'flex', justifyContent: 'flex-end', gap: 10,
                background: subSurface,
              }}>
                <button
                  onClick={function() { setPendingExisting(null) }}
                  style={{
                    padding: '8px 16px', minHeight: 34,
                    borderRadius: 10, cursor: 'pointer',
                    fontSize: '.78rem', fontWeight: 600,
                    background: isLight ? '#fff' : 'rgba(255,255,255,.04)',
                    border: '1px solid ' + (isLight ? '#e2e8f0' : 'rgba(255,255,255,.1)'),
                    color: mutedText,
                  }}
                >Iptal</button>
                <button
                  onClick={handleApplyExistingConfirm}
                  style={{
                    padding: '8px 18px', minHeight: 34,
                    borderRadius: 10, cursor: 'pointer',
                    fontSize: '.78rem', fontWeight: 600,
                    background: '#6366f1',
                    border: '1px solid #4f46e5',
                    color: '#fff',
                    display: 'inline-flex', alignItems: 'center', gap: 6,
                  }}
                >
                  <Check size={13} /> Uygula
                </button>
              </div>
            )}

            {/* Footer (sadece "Yeni" tabında) */}
            {materialCode && tab === 'new' && (
              <div style={{
                padding: '12px 18px',
                borderTop: '1px solid ' + panelBorder,
                display: 'flex', justifyContent: 'flex-end', gap: 10,
                background: subSurface,
              }}>
                <button
                  onClick={onClose}
                  disabled={resolving}
                  style={{
                    padding: '8px 16px', minHeight: 34,
                    borderRadius: 10, cursor: resolving ? 'not-allowed' : 'pointer',
                    fontSize: '.78rem', fontWeight: 600,
                    background: isLight ? '#fff' : 'rgba(255,255,255,.04)',
                    border: '1px solid ' + (isLight ? '#e2e8f0' : 'rgba(255,255,255,.1)'),
                    color: mutedText,
                    opacity: resolving ? .5 : 1,
                  }}
                >İptal</button>
                <button
                  onClick={handleApplyNew}
                  disabled={resolving}
                  style={{
                    padding: '8px 18px', minHeight: 34,
                    borderRadius: 10, cursor: resolving ? 'not-allowed' : 'pointer',
                    fontSize: '.78rem', fontWeight: 600,
                    background: '#6366f1',
                    border: '1px solid #4f46e5',
                    color: '#fff',
                    display: 'inline-flex', alignItems: 'center', gap: 6,
                    opacity: resolving ? .7 : 1,
                  }}
                >
                  {resolving ? <Loader2 size={13} className="animate-spin" /> : <Check size={13} />}
                  {resolving ? 'Kontrol ediliyor...' : 'Uygula'}
                </button>
              </div>
            )}

          </div>

        </div>
      </div>
    </div>
  )

  if (typeof document === 'undefined') return null
  return createPortal(modalContent, document.body)
}

function SideTabButton(props) {
  var active = props.active
  var onClick = props.onClick
  var isLight = props.isLight
  return (
    <button
      onClick={onClick}
      style={{
        width: '100%',
        padding: '10px 12px',
        fontSize: '.78rem', fontWeight: 600,
        border: '1px solid ' + (active ? (isLight ? '#c7d2fe' : 'rgba(99,102,241,.35)') : 'transparent'),
        borderLeft: '3px solid ' + (active ? '#6366f1' : 'transparent'),
        background: active
          ? (isLight ? 'rgba(99,102,241,.08)' : 'rgba(99,102,241,.14)')
          : 'transparent',
        color: active ? (isLight ? '#4f46e5' : '#a5b4fc') : (isLight ? '#64748b' : 'rgba(255,255,255,.65)'),
        cursor: 'pointer',
        display: 'inline-flex', alignItems: 'center',
        justifyContent: 'flex-start',
        borderRadius: 8,
        textAlign: 'left',
        transition: 'color .15s, background .15s, border-color .15s',
      }}
    >{props.children}</button>
  )
}

function ExistingTab(props) {
  var loading = props.loading
  var items = props.items
  var currentCode = props.currentCode
  var pendingCode = props.pendingCode
  var onSelect = props.onSelect
  var onDblClickApply = props.onDblClickApply
  var isLight = props.isLight
  var mutedText = props.mutedText
  var textColor = props.textColor
  var panelBorder = props.panelBorder
  var panelBg = props.panelBg
  var subSurface = props.subSurface
  var pendingDetails = props.pendingDetails
  var onDescriptionChange = props.onDescriptionChange

  var [popup, setPopup] = useState(null)
  var [search, setSearch] = useState('')

  var descInputStyle = {
    padding: '4px 8px', borderRadius: 7,
    background: isLight ? '#fff' : 'rgba(255,255,255,.04)',
    border: '1px solid ' + (isLight ? '#e2e8f0' : 'rgba(255,255,255,.1)'),
    color: textColor, fontSize: '.78rem', outline: 'none', width: '100%',
    colorScheme: isLight ? 'light' : 'dark',
  }

  function openPopup(e, c) {
    e.stopPropagation()
    var rect = e.currentTarget.getBoundingClientRect()
    setPopup({ combo: c, x: rect.left, y: rect.bottom + 4 })
  }

  // Secili olan en uste, geri kalanlar orijinal sirada
  var sortedItems = (items || []).slice().sort(function(a, b) {
    if (a.code === currentCode) return -1
    if (b.code === currentCode) return 1
    return 0
  })

  // Arama filtresi — kod, deger adlari veya aciklama
  var q = search.trim().toLowerCase()
  var filteredItems = q
    ? sortedItems.filter(function(c) {
        if (c.code.toLowerCase().includes(q)) return true
        var vals = (c.features || []).map(function(f) { return (f.value || '') + ' ' + (f.feature || '') }).join(' ').toLowerCase()
        return vals.includes(q)
      })
    : sortedItems

  return (
    <div style={{ flex: 1, display: 'flex', flexDirection: 'column', minHeight: 0 }}>

      {/* Ust bolum: kombinasyon listesi (scrollable) */}
      <div style={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}>

        {/* Arama */}
        {!loading && items && items.length > 1 && (
          <div style={{ padding: '8px 14px 4px', flexShrink: 0 }}>
            <input
              type="text"
              value={search}
              onChange={function(e) { setSearch(e.target.value) }}
              placeholder="Kod veya değer ara..."
              style={{
                width: '100%', padding: '5px 10px', borderRadius: 8,
                background: isLight ? '#fff' : 'rgba(255,255,255,.05)',
                border: '1px solid ' + (isLight ? '#e2e8f0' : 'rgba(255,255,255,.1)'),
                color: textColor, fontSize: '.78rem', outline: 'none',
                colorScheme: isLight ? 'light' : 'dark',
              }}
            />
          </div>
        )}

        <div style={{ flex: 1, minHeight: 0, overflowY: 'auto', padding: '6px 14px 10px' }}>
          {loading && (
            <div style={{ padding: 24, textAlign: 'center', color: mutedText, fontSize: '.82rem' }}>
              <Loader2 size={18} className="animate-spin" style={{ verticalAlign: 'middle', marginRight: 6 }} /> Yükleniyor...
            </div>
          )}
          {!loading && (!items || items.length === 0) && (
            <div style={{ padding: 24, textAlign: 'center', color: mutedText, fontSize: '.82rem' }}>
              Bu malzeme için henüz kombinasyon tanımlanmamış. Yeni kombinasyon oluşturmak için sol menüden "Yeni Kombinasyon" sekmesine geçin.
            </div>
          )}
          {!loading && filteredItems.length === 0 && items && items.length > 0 && (
            <div style={{ padding: 16, textAlign: 'center', color: mutedText, fontSize: '.78rem' }}>
              Arama sonucu bulunamadı.
            </div>
          )}
          {!loading && filteredItems.length > 0 && (
            <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '.8rem' }}>
              <thead>
                <tr style={{ borderBottom: '1px solid ' + panelBorder }}>
                  <th style={{ padding: '5px 8px', textAlign: 'left', color: mutedText, fontSize: '.68rem', textTransform: 'uppercase', letterSpacing: '.04em', whiteSpace: 'nowrap' }}>Kod</th>
                  <th style={{ padding: '5px 8px', textAlign: 'left', color: mutedText, fontSize: '.68rem', textTransform: 'uppercase', letterSpacing: '.04em' }}>Kombinasyon Açıklaması</th>
                  <th style={{ width: 62 }}></th>
                </tr>
              </thead>
              <tbody>
                {filteredItems.map(function(c) {
                  var isCurrent = currentCode && c.code === currentCode
                  var isPending = pendingCode && c.code === pendingCode
                  var valuesSummary = (c.features || []).map(function(f) { return f.value }).join(' · ')
                  return (
                    <tr key={c.code}
                        onClick={function() { onSelect(c) }}
                        style={{
                          cursor: 'pointer',
                          borderBottom: '1px solid ' + panelBorder,
                          background: isPending
                            ? (isLight ? 'rgba(99,102,241,.12)' : 'rgba(99,102,241,.22)')
                            : isCurrent
                              ? (isLight ? 'rgba(16,185,129,.1)' : 'rgba(16,185,129,.18)')
                              : 'transparent',
                        }}>
                      <td style={{ padding: '5px 8px', fontWeight: 700, whiteSpace: 'nowrap' }}>
                        <span
                          onClick={function(e) { openPopup(e, c) }}
                          title="Özellikleri gör"
                          style={{
                            color: isCurrent
                              ? (isLight ? '#059669' : '#6ee7b7')
                              : (isLight ? '#4f46e5' : '#a5b4fc'),
                            cursor: 'pointer',
                            textDecoration: 'underline dotted',
                            textUnderlineOffset: 2,
                          }}
                        >{c.code}</span>
                      </td>
                      <td style={{ padding: '5px 8px', fontSize: '.75rem', color: mutedText }}>
                        {valuesSummary}
                      </td>
                      <td style={{ padding: '5px 8px', textAlign: 'right' }}>
                        <button
                          onClick={function(e) { e.stopPropagation(); onSelect(c) }}
                          onDoubleClick={function(e) { e.stopPropagation(); onDblClickApply && onDblClickApply(c) }}
                          title={isPending ? '' : 'Çift tıkla: açıklama girmeden seç'}
                          style={{
                            padding: '2px 0', borderRadius: 6,
                            width: 54, textAlign: 'center',
                            background: isPending ? '#4f46e5' : '#6366f1',
                            color: '#fff', border: '1px solid #4f46e5',
                            fontSize: '.68rem', fontWeight: 600, cursor: 'pointer',
                            flexShrink: 0,
                          }}
                        >{isPending ? 'Seçildi' : 'Seç'}</button>
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          )}
        </div>
      </div>

      {/* Alt bolum: ozellik aciklamalari — sadece secim yapildiysa */}
      {pendingDetails && pendingDetails.length > 0 && (
        <div style={{
          flex: 1,
          minHeight: 0,
          borderTop: '2px solid ' + panelBorder,
          background: subSurface,
          padding: '6px 14px 8px',
          overflowY: 'auto',
        }}>
          {pendingDetails.map(function(d, idx) {
            return (
              <div key={idx} style={{
                display: 'grid',
                gridTemplateColumns: '120px 140px 1fr',
                gap: 8, alignItems: 'center',
                padding: '2px 0',
              }}>
                <label style={{ fontSize: '.73rem', fontWeight: 600, color: mutedText, textAlign: 'right' }}>
                  {d.featureName}
                </label>
                <div style={{ fontSize: '.73rem', color: textColor, paddingLeft: 4 }}>
                  {d.valueName}
                </div>
                <input
                  type="text"
                  value={d.description || ''}
                  onChange={function(e) { onDescriptionChange(idx, e.target.value) }}
                  placeholder="Açıklama..."
                  style={descInputStyle}
                />
              </div>
            )
          })}
        </div>
      )}

      {/* Kombinasyon detay popup */}
      {popup && (
        <div
          onClick={function() { setPopup(null) }}
          style={{ position: 'fixed', inset: 0, zIndex: 10100 }}
        >
          <div
            onClick={function(e) { e.stopPropagation() }}
            style={{
              position: 'fixed',
              top: Math.min(popup.y, window.innerHeight - 260),
              left: Math.min(popup.x, window.innerWidth - 280),
              zIndex: 10101,
              background: panelBg,
              border: '1px solid ' + panelBorder,
              borderRadius: 10,
              padding: '10px 14px',
              minWidth: 220, maxWidth: 320,
              boxShadow: '0 8px 32px rgba(0,0,0,.35)',
            }}
          >
            <div style={{ fontSize: '.7rem', fontWeight: 700, textTransform: 'uppercase', letterSpacing: '.04em', color: mutedText, marginBottom: 6 }}>
              {popup.combo.code}
            </div>
            {(popup.combo.features || []).map(function(f, i) {
              var desc = pendingCode === popup.combo.code && pendingDetails
                ? (pendingDetails[i] ? pendingDetails[i].description : '')
                : ''
              return (
                <div key={i} style={{ display: 'flex', gap: 6, padding: '2px 0', fontSize: '.78rem' }}>
                  <span style={{ color: mutedText, flex: '0 0 auto' }}>{f.feature}:</span>
                  <span style={{ color: textColor, fontWeight: 600 }}>{f.value}</span>
                  {desc && <span style={{ color: mutedText, fontStyle: 'italic', marginLeft: 4 }}>{desc}</span>}
                </div>
              )
            })}
          </div>
        </div>
      )}
    </div>
  )
}

function NewTab(props) {
  var loading = props.loading
  var features = props.features
  var selections = props.selections
  var onSelectionChange = props.onSelectionChange
  var isLight = props.isLight
  var mutedText = props.mutedText
  var textColor = props.textColor
  var panelBorder = props.panelBorder

  if (loading) {
    return <div style={{ padding: 24, textAlign: 'center', color: mutedText, fontSize: '.82rem' }}>
      <Loader2 size={18} className="animate-spin" style={{ verticalAlign: 'middle', marginRight: 6 }} /> Yükleniyor...
    </div>
  }
  if (!features || features.length === 0) {
    return <div style={{ padding: 24, textAlign: 'center', color: mutedText, fontSize: '.82rem' }}>
      Bu malzemeye bağlı özellik tanımlanmamış. Önce ürün yapılandırmasından özellik ekleyin.
    </div>
  }

  function updateSel(featureId, patch) {
    onSelectionChange(function(prev) {
      var next = Object.assign({}, prev)
      next[featureId] = Object.assign({}, next[featureId] || {}, patch)
      return next
    })
  }

  // Dark modda <select>'in kendisi ve acilan option paneli icin
  // solid koyu arka plan veriyoruz — transparan rgba'da Windows/Chrome
  // bazen light default render ediyor.
  var selectBg = isLight ? '#ffffff' : '#1e293b'
  var selectFg = isLight ? '#1e293b' : '#e2e8f0'
  var inputStyle = {
    padding: '7px 10px', borderRadius: 8,
    background: selectBg,
    border: '1px solid ' + (isLight ? '#e2e8f0' : 'rgba(255,255,255,.14)'),
    color: selectFg,
    fontSize: '.82rem',
    outline: 'none',
    // colorScheme = dark → option paneli de koyu temalı render edilir (browser destekliyorsa).
    colorScheme: isLight ? 'light' : 'dark',
  }
  var optionStyle = { background: selectBg, color: selectFg }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
      <div style={{ fontSize: '.72rem', color: mutedText, marginBottom: 4 }}>
        Her özellik için bir değer seçin. İstediğiniz özellikler için açıklama girebilirsiniz.
      </div>
      {features.map(function(f) {
        var sel = selections[f.featureId] || {}
        return (
          <div key={f.featureId} style={{
            display: 'grid',
            gridTemplateColumns: '130px 180px 1fr',
            gap: 10, alignItems: 'center',
            padding: '8px 0',
            borderBottom: '1px solid ' + panelBorder,
          }}>
            <label style={{ fontSize: '.78rem', fontWeight: 600, color: mutedText, textAlign: 'right' }}>
              {f.featureName}
            </label>
            <select
              value={sel.valueId || ''}
              onChange={function(e) {
                var v = e.target.value ? parseInt(e.target.value, 10) : null
                updateSel(f.featureId, { valueId: v })
              }}
              style={inputStyle}
            >
              <option value="" style={optionStyle}>— Seçiniz —</option>
              {(f.values || []).map(function(v) {
                return <option key={v.id} value={v.id} style={optionStyle}>{v.name}</option>
              })}
            </select>
            <input
              type="text"
              value={sel.description || ''}
              onChange={function(e) { updateSel(f.featureId, { description: e.target.value }) }}
              placeholder="Açıklama (opsiyonel)"
              style={inputStyle}
            />
          </div>
        )
      })}
    </div>
  )
}
