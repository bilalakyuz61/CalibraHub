/**
 * CostViewerModal — Standart Maliyet Goruntuleme modali
 *
 * Bir malzemenin recetesindeki bilesenleri secilen fiyat grubundan fiyatlandirip
 * satir-satir ve toplam maliyetleri gosterir. Tip 1 (sabit alan), Tip 2 (widget),
 * kalem grid'i — uc ekrandan da ayni props ile cagrilabilir; gorsel ve davranis tek
 * noktada (bu component) yasar.
 *
 * Props:
 *   isOpen          : bool
 *   onClose         : fn()
 *   materialCode    : string  (zorunlu — recete uretilecek ana malzeme)
 *   configCode      : string? (varsayilan kombinasyon kodu)
 *   quantity        : number  (varsayilan 1; kalemin miktari geciriliyorsa o)
 *   defaultPriceGroupId : int? (varsayilan secilecek fiyat grubu)
 *   defaultCurrencyId   : int? (varsayilan secilecek para birimi)
 *   defaultPriceType    : "Buy" | "Sell" — varsayilan "Buy"
 *   title           : string? (header'da gosterilen baslik)
 *
 * Backend: GET /Logistics/GetMaterialCost — bos parametre/fiyat olmama hallerini
 * kibarca handle eder (NaN/null fiyat = "—" goster, satir maliyeti sifirla).
 */
import { useState, useEffect, useCallback } from 'react'
import { createPortal } from 'react-dom'
import { Calculator, X, Loader2, AlertCircle } from 'lucide-react'

function useIsLight() {
  var [light, setLight] = useState(function () {
    return typeof document !== 'undefined' && document.body.classList.contains('app-theme-light')
  })
  useEffect(function () {
    var obs = new MutationObserver(function () {
      setLight(document.body.classList.contains('app-theme-light'))
    })
    obs.observe(document.body, { attributes: true, attributeFilter: ['class'] })
    return function () { obs.disconnect() }
  }, [])
  return light
}

function fmt(n, prec) {
  if (n == null || isNaN(n)) return '—'
  var p = prec != null ? prec : 2
  return Number(n).toLocaleString('tr-TR', { minimumFractionDigits: p, maximumFractionDigits: p })
}

export default function CostViewerModal(props) {
  var isOpen       = !!props.isOpen
  var onClose      = props.onClose || function () {}
  var materialCode = props.materialCode || ''
  var configCode   = props.configCode || null
  var quantity     = props.quantity != null && !isNaN(props.quantity) ? Number(props.quantity) : 1
  var title        = props.title || ('Maliyet Görüntüleme — ' + materialCode + (configCode ? ' / ' + configCode : ''))

  var isLight = useIsLight()

  // ── Parametreler ──
  var [priceGroups, setPriceGroups]   = useState([])
  var [currencies,  setCurrencies]    = useState([])
  var [priceGroupId, setPriceGroupId] = useState(props.defaultPriceGroupId || null)
  var [currencyId,   setCurrencyId]   = useState(props.defaultCurrencyId || null)
  // PriceType DB konvansiyonu: 'b'=Alis (Buy), 's'=Satis (Sell), 'm'=Maliyet (Cost).
  // PriceListService.IsValidPriceType bu uc tek harfi kabul eder; tam-isim ('Buy' vb.) reddediliyor.
  var [priceType,    setPriceType]    = useState(props.defaultPriceType || 'm')
  // Bilesen gruplama secimleri — checkbox'lar (cogul, dinamik level). [] = duz tablo.
  // Backend her bilesenin `groups: { "1": {code,name}, "2": {...}, "3": {...} }` doner.
  // Frontend tum bilesenleri tarayip benzersiz level'lari cikarir, her biri icin checkbox.
  // Sira: kullanicinin kutu tikladigi sirada — disş seviyeden ic seviyeye gruplandirir.
  var [groupLevels,  setGroupLevels]  = useState([])
  function toggleGroup(level) {
    setGroupLevels(function (prev) {
      var has = prev.indexOf(level) !== -1
      if (has) return prev.filter(function (l) { return l !== level })
      return prev.concat([level])
    })
  }
  var [paramsLoading, setParamsLoading] = useState(false)
  var [data, setData] = useState(null)            // server response
  var [loading, setLoading] = useState(false)
  var [error, setError] = useState(null)

  // Modal acildiginda fiyat grubu ve para birimi listesini cek (parametre comboları icin)
  useEffect(function () {
    if (!isOpen) return undefined
    setParamsLoading(true)
    Promise.all([
      fetch('/PriceList/GetPriceGroups',  { credentials: 'same-origin' }).then(function (r) { return r.ok ? r.json() : [] }).catch(function () { return [] }),
      fetch('/PriceList/GetCurrencies',   { credentials: 'same-origin' }).then(function (r) { return r.ok ? r.json() : [] }).catch(function () { return [] }),
    ]).then(function (results) {
      var pg = Array.isArray(results[0]) ? results[0] : []
      var cu = Array.isArray(results[1]) ? results[1] : []
      setPriceGroups(pg)
      setCurrencies(cu)
      // Default secimler — props verilmediyse ilk eleman
      setPriceGroupId(function (prev) { return prev != null ? prev : (pg[0] ? pg[0].id : null) })
      setCurrencyId(function (prev) { return prev != null ? prev : (cu[0] ? cu[0].id : null) })
    }).finally(function () { setParamsLoading(false) })
  }, [isOpen])

  // Maliyet sorgusu — parametreler veya materialCode degistiginde tetiklenir
  var fetchCost = useCallback(function () {
    if (!materialCode || !priceGroupId || !currencyId) {
      setData(null)
      return
    }
    setLoading(true)
    setError(null)
    // Kalem bazinda CostViewerModal her zaman BIRIM BAZLI maliyet gosterir —
    // teklifte miktar 5 olsa bile 1 birim icin maliyeti hesaplamak istiyoruz.
    // Belge bazli toplam icin QuoteCostSummaryModal her satira gercek quantity gonderir.
    //
    // validOn: belge tarihi (varsa) — backend o tarihte yururlukte olan en yakin
    // fiyati doner (ValidFrom <= validOn AND (ValidTo IS NULL OR ValidTo >= validOn)).
    // Bos ise sunucu bugunun tarihini kullanir.
    var validOn = props.validOn || (function () {
      try {
        var el = (typeof document !== 'undefined') ? document.getElementById('sqQuoteDate') : null
        return (el && el.value) ? el.value : ''
      } catch (_) { return '' }
    })()
    var url = '/Logistics/GetMaterialCost?materialCode=' + encodeURIComponent(materialCode)
            + (configCode ? '&configCode=' + encodeURIComponent(configCode) : '')
            + '&priceGroupId=' + priceGroupId
            + '&currencyId=' + currencyId
            + '&priceType=' + encodeURIComponent(priceType || 'm')
            + '&quantity=1'
            + (validOn ? '&validOn=' + encodeURIComponent(validOn) : '')
    fetch(url, { credentials: 'same-origin' })
      .then(function (r) { return r.ok ? r.json() : Promise.reject(new Error('HTTP ' + r.status)) })
      .then(function (d) { setData(d) })
      .catch(function (e) { setError('Veri alınamadı: ' + (e.message || e)); setData(null) })
      .finally(function () { setLoading(false) })
  }, [materialCode, configCode, priceGroupId, currencyId, priceType])

  useEffect(function () {
    if (!isOpen) return
    fetchCost()
  }, [isOpen, fetchCost])

  if (!isOpen) return null

  // Stil
  var overlayBg = isLight ? 'rgba(15,23,42,.45)' : 'rgba(0,0,0,.55)'
  var panelBg   = isLight ? '#ffffff'           : 'rgba(23,26,43,.96)'
  var panelBdr  = isLight ? '#e2e8f0'           : 'rgba(255,255,255,.08)'
  var textColor = isLight ? '#1e293b'           : 'rgba(255,255,255,.88)'
  var mutedText = isLight ? '#64748b'           : 'rgba(255,255,255,.55)'
  var subSurface= isLight ? '#f8fafc'           : 'rgba(255,255,255,.03)'
  var accentBg  = isLight ? '#fef3c7'           : 'rgba(245,158,11,.12)'
  var accentClr = isLight ? '#92400e'           : '#fcd34d'

  // Native <select>: tarayicilar option panelinde colorScheme'i her zaman onurlandirmaz
  // (Windows Chrome bilinen kusur). Bu yuzden hem select hem option'a explicit
  // background/color veriyoruz — option'lar koyu temada da koyu kalir.
  var selectBg = isLight ? '#ffffff' : '#1e293b'
  var selectFg = isLight ? '#1e293b' : '#e2e8f0'
  // Component satiri render helper — hem duz hem gruplu (N seviye) modda kullanilir.
  // depth: 0=duz/grupsuz, 1+ = grup ic seviyesi (her seviye 14px ek indent)
  function renderCompRow(c, key, depth) {
    var indent = 8 + (depth || 0) * 14
    return (
      <tr key={'r-' + key} style={{ borderBottom: '1px solid ' + panelBdr }}>
        <td style={{ padding: '6px 8px', paddingLeft: indent }}>
          <div style={{ fontWeight: 600, color: textColor }}>{c.code}</div>
          <div style={{ fontSize: '.7rem', color: mutedText }}>{c.name}{c.configCode ? ' · ' + c.configCode : ''}</div>
        </td>
        <td style={{ padding: '6px 8px', textAlign: 'right', fontFamily: 'ui-monospace, Menlo, Consolas, monospace', fontSize: '.78rem' }}>{fmt(c.qty, 4)}</td>
        <td style={{ padding: '6px 8px', textAlign: 'right', fontFamily: 'ui-monospace, Menlo, Consolas, monospace', fontSize: '.78rem' }}>{fmt((c.scrapRatio || 0) * 100, 2)}</td>
        <td style={{ padding: '6px 8px', textAlign: 'right', fontFamily: 'ui-monospace, Menlo, Consolas, monospace', fontSize: '.78rem' }}>{fmt(c.effectiveQty, 4)}</td>
        <td style={{ padding: '6px 8px', textAlign: 'right', fontFamily: 'ui-monospace, Menlo, Consolas, monospace', fontSize: '.78rem', color: c.hasPrice ? textColor : mutedText }}>
          {c.hasPrice ? fmt(c.unitPrice, 2) : '—'} {currencySymbol}
        </td>
        <td style={{ padding: '6px 8px', textAlign: 'right', fontFamily: 'ui-monospace, Menlo, Consolas, monospace', fontSize: '.82rem', fontWeight: 600 }}>
          {fmt(c.lineCost, 2)} {currencySymbol}
        </td>
      </tr>
    )
  }

  var inputStyle = {
    padding: '5px 9px', borderRadius: 7,
    background: selectBg,
    border: '1px solid ' + (isLight ? '#e2e8f0' : 'rgba(255,255,255,.12)'),
    color: selectFg, fontSize: '.78rem', outline: 'none',
    colorScheme: isLight ? 'light' : 'dark',
  }
  var optionStyle = { background: selectBg, color: selectFg }

  var currencySymbol = data && data.currency ? (data.currency.symbol || data.currency.code || '') : ''

  var modalContent = (
    <div onClick={onClose} style={{
      position: 'fixed', inset: 0, zIndex: 10000,
      background: overlayBg, backdropFilter: 'blur(6px)', WebkitBackdropFilter: 'blur(6px)',
      display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 20,
      animation: 'cvmFadeIn .15s ease-out',
    }}>
      <style>{`
        @keyframes cvmFadeIn { from { opacity: 0 } to { opacity: 1 } }
        @keyframes cvmSlide  { from { transform: translateY(8px) scale(.97); opacity: 0 } to { transform: none; opacity: 1 } }
      `}</style>
      <div onClick={function (e) { e.stopPropagation() }} style={{
        width: 'min(1080px, 96vw)', height: 'min(680px, 90vh)',
        background: panelBg, border: '1px solid ' + panelBdr, borderRadius: 16,
        boxShadow: '0 24px 72px rgba(0,0,0,.5)',
        display: 'flex', flexDirection: 'column', overflow: 'hidden',
        color: textColor, animation: 'cvmSlide .22s cubic-bezier(.23,1,.32,1)',
      }}>
        {/* Header */}
        <div style={{
          padding: '14px 18px', borderBottom: '1px solid ' + panelBdr,
          display: 'flex', alignItems: 'center', gap: 10, flexShrink: 0,
        }}>
          <Calculator size={16} style={{ color: '#fbbf24' }} />
          <div style={{ flex: 1 }}>
            <div style={{ fontSize: '0.92rem', fontWeight: 700 }}>{title}</div>
            <div style={{ fontSize: '0.72rem', color: mutedText, marginTop: 2 }}>
              Reçete bileşenlerinin <strong>1 adet için</strong> birim maliyetini secilen fiyat grubundan hesaplar.
            </div>
          </div>
          <button onClick={onClose} title="Kapat" style={{
            background: 'transparent', border: 'none', cursor: 'pointer',
            color: mutedText, padding: 6, borderRadius: 6,
          }}><X size={18} /></button>
        </div>

        {/* Body — sol filtre paneli + sag tablo (st-modal-body--tabbed pattern) */}
        <div style={{ flex: 1, display: 'flex', minHeight: 0, overflow: 'hidden' }}>

          {/* Sol panel: filtreler */}
          <div style={{
            width: 260, flex: '0 0 260px',
            borderRight: '1px solid ' + panelBdr,
            background: subSurface,
            padding: '14px 14px',
            overflowY: 'auto',
            display: 'flex', flexDirection: 'column', gap: 12,
          }}>
            <label style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
              <span style={{ fontSize: 10, fontWeight: 600, color: mutedText, textTransform: 'uppercase', letterSpacing: '.04em' }}>Fiyat Grubu</span>
              <select value={priceGroupId || ''} onChange={function (e) {
                var newId = e.target.value ? parseInt(e.target.value, 10) : null
                setPriceGroupId(newId)
                var ng = priceGroups.find(function (g) { return g.id === newId })
                if (ng) {
                  var allowed = []
                  if (ng.allowsCost)    allowed.push('m')
                  if (ng.allowsBuying)  allowed.push('b')
                  if (ng.allowsSelling) allowed.push('s')
                  setPriceType(function (cur) {
                    return allowed.length > 0 && allowed.indexOf(cur) === -1 ? allowed[0] : cur
                  })
                }
              }} style={inputStyle} disabled={paramsLoading}>
                {priceGroups.length === 0 && <option value="" style={optionStyle}>— Yok —</option>}
                {priceGroups.map(function (g) { return <option key={g.id} value={g.id} style={optionStyle}>{g.code} · {g.name}</option> })}
              </select>
            </label>
            <label style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
              <span style={{ fontSize: 10, fontWeight: 600, color: mutedText, textTransform: 'uppercase', letterSpacing: '.04em' }}>Para Birimi</span>
              <select value={currencyId || ''} onChange={function (e) { setCurrencyId(e.target.value ? parseInt(e.target.value, 10) : null) }} style={inputStyle} disabled={paramsLoading}>
                {currencies.length === 0 && <option value="" style={optionStyle}>— Yok —</option>}
                {currencies.map(function (c) { return <option key={c.id} value={c.id} style={optionStyle}>{c.code} {c.symbol ? '(' + c.symbol + ')' : ''}</option> })}
              </select>
            </label>
            {(function () {
              var selGroup = priceGroups.find(function (g) { return g.id === priceGroupId })
              var typeOpts = selGroup
                ? (function () {
                    var t = []
                    if (selGroup.allowsCost)    t.push({ v: 'm', l: 'Maliyet' })
                    if (selGroup.allowsBuying)  t.push({ v: 'b', l: 'Alış' })
                    if (selGroup.allowsSelling) t.push({ v: 's', l: 'Satış' })
                    return t.length > 0 ? t : [{ v: 'm', l: 'Maliyet' }, { v: 'b', l: 'Alış' }, { v: 's', l: 'Satış' }]
                  })()
                : [{ v: 'm', l: 'Maliyet' }, { v: 'b', l: 'Alış' }, { v: 's', l: 'Satış' }]
              return (
                <label style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
                  <span style={{ fontSize: 10, fontWeight: 600, color: mutedText, textTransform: 'uppercase', letterSpacing: '.04em' }}>Fiyat Tipi</span>
                  <select value={priceType} onChange={function (e) { setPriceType(e.target.value) }} style={inputStyle}>
                    {typeOpts.map(function (t) { return <option key={t.v} value={t.v} style={optionStyle}>{t.l}</option> })}
                  </select>
                </label>
              )
            })()}
            {/* Bilesen Gruplama — coklu secim (checkbox), dinamik level (backend componentlerinden). */}
            <div style={{ display: 'flex', flexDirection: 'column', gap: 6, paddingTop: 6, borderTop: '1px dashed ' + panelBdr, marginTop: 4 }}>
              <span style={{ fontSize: 10, fontWeight: 600, color: mutedText, textTransform: 'uppercase', letterSpacing: '.04em' }}>Bileşen Gruplama</span>
              {(function () {
                // Tum bilesenlerden benzersiz level'lari topla → kucuk-buyuk siralama
                var seen = new Set()
                ;((data && data.components) || []).forEach(function (c) {
                  var groups = c.groups || {}
                  Object.keys(groups).forEach(function (lv) {
                    var n = parseInt(lv, 10)
                    if (!isNaN(n) && groups[lv] && groups[lv].code) seen.add(n)
                  })
                })
                var levels = Array.from(seen).sort(function (a, b) { return a - b })
                if (levels.length === 0) {
                  return <span style={{ fontSize: 10, color: mutedText, fontStyle: 'italic' }}>Bu malzemenin bileşenlerine henüz grup kodu atanmamış.</span>
                }
                return levels.map(function (lv) {
                  var key = String(lv)
                  var idx = groupLevels.indexOf(key)
                  var checked = idx !== -1
                  var order = checked ? (idx + 1) : null
                  return (
                    <label key={key} style={{
                      display: 'flex', alignItems: 'center', gap: 8,
                      padding: '6px 8px', borderRadius: 6,
                      cursor: 'pointer', fontSize: '.78rem',
                      background: checked ? (isLight ? 'rgba(99,102,241,.08)' : 'rgba(99,102,241,.12)') : 'transparent',
                      border: '1px solid ' + (checked ? (isLight ? 'rgba(99,102,241,.25)' : 'rgba(99,102,241,.30)') : 'transparent'),
                      color: checked ? (isLight ? '#4338ca' : '#a5b4fc') : textColor,
                      transition: 'all .12s',
                    }}>
                      <input type="checkbox" checked={checked} onChange={function () { toggleGroup(key) }} style={{ accentColor: '#6366f1' }} />
                      <span style={{ flex: 1 }}>Grup Kodu {lv}</span>
                      {order && (
                        <span style={{
                          fontSize: 9.5, fontWeight: 700, padding: '1px 6px', borderRadius: 4,
                          background: '#6366f1', color: '#fff',
                        }} title={'Seviye sirasi: ' + order}>L{order}</span>
                      )}
                    </label>
                  )
                })
              })()}
              <span style={{ fontSize: 10, color: mutedText, fontStyle: 'italic', marginTop: 2 }}>
                {groupLevels.length === 0 ? 'Grupsuz düz tablo' : (groupLevels.length + ' seviye iç içe gruplama')}
              </span>
            </div>
          </div>

          {/* Sag panel: tablo */}
          <div style={{ flex: 1, overflowY: 'auto', padding: '14px 18px', minWidth: 0 }}>
          {loading && (
            <div style={{ padding: 24, textAlign: 'center', color: mutedText, fontSize: '.82rem' }}>
              <Loader2 size={18} className="animate-spin" style={{ verticalAlign: 'middle', marginRight: 6 }} /> Maliyet hesaplaniyor...
            </div>
          )}
          {!loading && error && (
            <div style={{
              padding: '10px 14px', borderRadius: 10, fontSize: '.82rem',
              display: 'flex', alignItems: 'center', gap: 8,
              background: isLight ? '#fef2f2' : 'rgba(239,68,68,.12)',
              border: '1px solid rgba(239,68,68,.35)',
              color: isLight ? '#b91c1c' : '#fca5a5',
            }}>
              <AlertCircle size={14} /> {error}
            </div>
          )}
          {!loading && !error && data && data.found === false && (
            <div style={{ padding: 24, textAlign: 'center', color: mutedText, fontSize: '.82rem' }}>
              {data.message || 'Bu malzeme icin recete tanimli degil.'}
            </div>
          )}
          {!loading && !error && data && data.found && (
            <div>
              <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '.82rem' }}>
                <thead>
                  <tr style={{ borderBottom: '1px solid ' + panelBdr }}>
                    <th style={{ padding: '6px 8px', textAlign: 'left', color: mutedText, fontSize: '.68rem', textTransform: 'uppercase', letterSpacing: '.04em' }}>Bilesen</th>
                    <th style={{ padding: '6px 8px', textAlign: 'right', color: mutedText, fontSize: '.68rem', textTransform: 'uppercase', letterSpacing: '.04em' }}>Miktar</th>
                    <th style={{ padding: '6px 8px', textAlign: 'right', color: mutedText, fontSize: '.68rem', textTransform: 'uppercase', letterSpacing: '.04em' }}>Fire %</th>
                    <th style={{ padding: '6px 8px', textAlign: 'right', color: mutedText, fontSize: '.68rem', textTransform: 'uppercase', letterSpacing: '.04em' }}>Net Mik.</th>
                    <th style={{ padding: '6px 8px', textAlign: 'right', color: mutedText, fontSize: '.68rem', textTransform: 'uppercase', letterSpacing: '.04em' }}>Birim Fiyat</th>
                    <th style={{ padding: '6px 8px', textAlign: 'right', color: mutedText, fontSize: '.68rem', textTransform: 'uppercase', letterSpacing: '.04em' }}>Satir Maliyeti</th>
                  </tr>
                </thead>
                <tbody>
                  {(function () {
                    var comps = data.components || []
                    if (groupLevels.length === 0 || comps.length === 0) {
                      // Duz tablo
                      return comps.map(function (c, i) { return renderCompRow(c, i, 0) })
                    }
                    // Recursive N-seviye gruplama. groupLevels[0]=disş, [1]=ic, ...
                    // Her level string ('1', '2', '3', ...) — backend `c.groups[level]` ile dondurur.
                    function readGroup(c, level) {
                      var g = (c.groups || {})[level]
                      return g || { code: '', name: '' }
                    }
                    function buildTree(items, levels) {
                      if (levels.length === 0) return null
                      var lv = levels[0]
                      var groups = []
                      var map = new Map()
                      items.forEach(function (c) {
                        var info = readGroup(c, lv)
                        var k = info.code || ''
                        if (!map.has(k)) {
                          map.set(k, { code: k, name: info.name || '', items: [], subtotal: 0 })
                          groups.push(map.get(k))
                        }
                        var g = map.get(k)
                        g.items.push(c)
                        g.subtotal += Number(c.lineCost) || 0
                      })
                      // Alt seviye varsa her grupta tekrar grupla
                      if (levels.length > 1) {
                        groups.forEach(function (g) { g.children = buildTree(g.items, levels.slice(1)) })
                      }
                      return groups
                    }
                    var tree = buildTree(comps, groupLevels)
                    var rows = []
                    function renderGroups(groups, depth, keyPrefix) {
                      groups.forEach(function (g, gi) {
                        var indentPx = 8 + depth * 14
                        var bgIntensity = depth === 0 ? .14 : .08
                        rows.push(
                          <tr key={keyPrefix + '-h' + gi} style={{ background: isLight ? ('rgba(99,102,241,' + (bgIntensity / 1.6) + ')') : ('rgba(99,102,241,' + bgIntensity + ')') }}>
                            <td colSpan={5} style={{
                              padding: '5px 8px', paddingLeft: indentPx,
                              fontWeight: 700, fontSize: depth === 0 ? '.74rem' : '.7rem',
                              color: isLight ? '#4338ca' : '#a5b4fc',
                              textTransform: 'uppercase', letterSpacing: '.04em',
                            }}>
                              <span style={{ opacity: depth === 0 ? 1 : 0.85 }}>
                                {g.code ? (g.code + (g.name ? ' · ' + g.name : '')) : 'Grupsuz'}
                              </span>
                              <span style={{ marginLeft: 8, fontWeight: 500, fontSize: '.7rem', color: mutedText, textTransform: 'none', letterSpacing: 0 }}>
                                ({g.items.length} bileşen)
                              </span>
                            </td>
                            <td style={{ padding: '5px 8px', textAlign: 'right', fontFamily: 'ui-monospace, Menlo, Consolas, monospace', fontSize: '.78rem', fontWeight: 700, color: isLight ? '#4338ca' : '#a5b4fc' }}>
                              {fmt(g.subtotal, 2)} {currencySymbol}
                            </td>
                          </tr>
                        )
                        if (g.children && g.children.length > 0) {
                          renderGroups(g.children, depth + 1, keyPrefix + '-h' + gi)
                        } else {
                          g.items.forEach(function (c, ci) {
                            rows.push(renderCompRow(c, keyPrefix + '-' + gi + '-' + ci, depth + 1))
                          })
                        }
                      })
                    }
                    renderGroups(tree, 0, 'g')
                    return rows
                  })()}
                </tbody>
              </table>

              {/* Eksik fiyat uyarisi — secilen fiyat grubunda hic fiyatlandirilmamis bilesen varsa */}
              {(data.components || []).some(function (c) { return !c.hasPrice }) && (
                <div style={{
                  marginTop: 12, padding: '8px 12px', borderRadius: 8, fontSize: '.74rem',
                  background: accentBg, color: accentClr,
                  border: '1px solid ' + (isLight ? '#fde68a' : 'rgba(245,158,11,.35)'),
                  display: 'flex', alignItems: 'center', gap: 6,
                }}>
                  <AlertCircle size={13} />
                  Bazi bilesenlerin secilen fiyat grubunda fiyati yok — toplam maliyet eksik olabilir.
                </div>
              )}
            </div>
          )}
          </div>
        </div>

        {/* Footer — birim maliyet (1 adet) + bilgi: secili kalem icin extrapolasyon */}
        <div style={{
          padding: '12px 18px', borderTop: '1px solid ' + panelBdr,
          background: subSurface, flexShrink: 0,
          display: 'flex', alignItems: 'center', gap: 10,
        }}>
          <div style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: 2 }}>
            <div style={{ display: 'flex', alignItems: 'baseline', gap: 8 }}>
              <span style={{ fontSize: '.72rem', color: mutedText, textTransform: 'uppercase', letterSpacing: '.04em' }}>
                Birim Maliyet (1 adet)
              </span>
              <span style={{ fontSize: '1.08rem', fontWeight: 700, color: '#fbbf24', fontFamily: 'ui-monospace, Menlo, Consolas, monospace' }}>
                {data && data.found ? fmt(data.totalCost, 2) : '—'} {currencySymbol}
              </span>
            </div>
            {data && data.found && quantity && quantity !== 1 ? (
              <div style={{ fontSize: '.7rem', color: mutedText }}>
                Kalem miktarı: <strong>{fmt(quantity, 2)}</strong> ·
                Kalem toplamı: <strong style={{ color: textColor, fontFamily: 'ui-monospace, Menlo, Consolas, monospace' }}>{fmt(Number(data.totalCost) * Number(quantity), 2)} {currencySymbol}</strong>
              </div>
            ) : null}
          </div>
          <button onClick={onClose} style={{
            padding: '8px 18px', minHeight: 34, borderRadius: 10, cursor: 'pointer',
            fontSize: '.78rem', fontWeight: 600,
            background: '#6366f1', border: '1px solid #4f46e5', color: '#fff',
          }}>Kapat</button>
        </div>
      </div>
    </div>
  )

  if (typeof document === 'undefined') return null
  return createPortal(modalContent, document.body)
}
