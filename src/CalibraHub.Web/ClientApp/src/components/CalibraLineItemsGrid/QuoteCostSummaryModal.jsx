/**
 * QuoteCostSummaryModal — Belge tum kalemlerinin toplu maliyet ekrani
 *
 * Mevcut belgenin tum kalemlerini parametrize fiyat grubu/parabirimi ile
 * tek tabloda fiyatlandirir, satir-satir bilesen detayina genisletilebilir
 * accordion + grand total gosterir.
 *
 * Tetikleyici: window CustomEvent — `quote:open-cost-summary` { detail: { lines, defaultPriceGroupId, defaultCurrencyId } }
 *   - lines: [{ materialCode, materialName, configCode, quantity }]
 *
 * Kullanim:
 *   - cshtml/jQuery taraf: window.dispatchEvent(new CustomEvent('quote:open-cost-summary', {detail: {lines: [...]}}))
 *   - Modal kendiliginden acilir, parametre cubugundan secimle yeniden hesaplar.
 *
 * Backend: GET /Logistics/GetMaterialCost — her kalem icin paralel cagri,
 *          satir maliyeti = (line.qty / 1) * sum(component.lineCost). Yine de
 *          GetMaterialCost'a quantity parametresi geciyoruz; backend zaten
 *          fire-dahil net miktari hesapliyor.
 */
import { useState, useEffect, useCallback, useRef } from 'react'
import { createPortal } from 'react-dom'
import { ClipboardList, X, Loader2, AlertCircle, ChevronDown, ChevronRight } from 'lucide-react'

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

export default function QuoteCostSummaryModal() {
  var isLight = useIsLight()
  var [open, setOpen]             = useState(false)
  var [lines, setLines]           = useState([])             // [{ materialCode, materialName, configCode, quantity }]
  var [validOn, setValidOn]       = useState('')             // ISO yyyy-MM-dd; belge tarihi
  var [results, setResults]       = useState([])             // her line icin: { found, components, totalCost, error }
  var [loading, setLoading]       = useState(false)
  var [expanded, setExpanded]     = useState({})             // { idx: bool }
  var [priceGroups, setPriceGroups]   = useState([])
  var [currencies,  setCurrencies]    = useState([])
  var [priceGroupId, setPriceGroupId] = useState(null)
  var [currencyId,   setCurrencyId]   = useState(null)
  // PriceType DB konvansiyonu: 'b'=Alis, 's'=Satis, 'm'=Maliyet — service bu tek harfleri bekler.
  var [priceType,    setPriceType]    = useState('m')
  // Bilesen gruplama secimleri (cogul). Ic ice gruplama icin sira onemli — kullanici
  // tikladigi sirada level olusur: ['g1','g2'] → L1=g1, L2=g2 (ic ice)
  var [groupLevels,  setGroupLevels]  = useState([])
  function toggleGroup(level) {
    setGroupLevels(function (prev) {
      var has = prev.indexOf(level) !== -1
      if (has) return prev.filter(function (l) { return l !== level })
      return prev.concat([level])
    })
  }
  var [paramsLoading, setParamsLoading] = useState(false)
  var [error, setError] = useState(null)
  var lastRunRef = useRef(0)

  // Window event'i ile dis dunyadan acilir
  useEffect(function () {
    function onOpen(e) {
      var d = (e && e.detail) || {}
      setLines(Array.isArray(d.lines) ? d.lines : [])
      setExpanded({})
      setResults([])
      setError(null)
      if (d.defaultPriceGroupId) setPriceGroupId(d.defaultPriceGroupId)
      if (d.defaultCurrencyId)   setCurrencyId(d.defaultCurrencyId)
      if (d.defaultPriceType)    setPriceType(d.defaultPriceType)
      // validOn: belge tarihi — event detail'inde varsa, yoksa #sqQuoteDate'tan oku.
      // Backend bu tarihte yururlukte olan en yakin fiyati getirir.
      var v = d.validOn || (function () {
        try { var el = document.getElementById('sqQuoteDate'); return (el && el.value) ? el.value : '' } catch (_) { return '' }
      })()
      setValidOn(v || '')
      setOpen(true)
    }
    window.addEventListener('quote:open-cost-summary', onOpen)
    return function () { window.removeEventListener('quote:open-cost-summary', onOpen) }
  }, [])

  function close() { setOpen(false) }

  // Modal acildiginda parametre comboları yukle
  useEffect(function () {
    if (!open) return undefined
    setParamsLoading(true)
    Promise.all([
      fetch('/PriceList/GetPriceGroups',  { credentials: 'same-origin' }).then(function (r) { return r.ok ? r.json() : [] }).catch(function () { return [] }),
      fetch('/PriceList/GetCurrencies',   { credentials: 'same-origin' }).then(function (r) { return r.ok ? r.json() : [] }).catch(function () { return [] }),
    ]).then(function (results) {
      var pg = Array.isArray(results[0]) ? results[0] : []
      var cu = Array.isArray(results[1]) ? results[1] : []
      setPriceGroups(pg)
      setCurrencies(cu)
      setPriceGroupId(function (prev) { return prev != null ? prev : (pg[0] ? pg[0].id : null) })
      setCurrencyId(function (prev) { return prev != null ? prev : (cu[0] ? cu[0].id : null) })
    }).finally(function () { setParamsLoading(false) })
  }, [open])

  // Maliyet sorgusu — parametre ya da lines degistiginde
  var fetchAll = useCallback(function () {
    if (!open || !priceGroupId || !currencyId || lines.length === 0) {
      setResults([])
      return
    }
    setLoading(true)
    setError(null)
    var runId = ++lastRunRef.current
    var promises = lines.map(function (l) {
      var url = '/Logistics/GetMaterialCost?materialCode=' + encodeURIComponent(l.materialCode)
              + (l.configCode ? '&configCode=' + encodeURIComponent(l.configCode) : '')
              + '&priceGroupId=' + priceGroupId
              + '&currencyId=' + currencyId
              + '&priceType=' + encodeURIComponent(priceType || 'm')
              + '&quantity=' + (l.quantity || 1)
              + (validOn ? '&validOn=' + encodeURIComponent(validOn) : '')
              // Belge bazli toplam: her satir kendi miktariyla carpilir (qty * birim maliyet).
              // Backend GetMaterialCost.effectiveQty = recipe_qty * quantity * (1+scrap)
              // validOn ile belge tarihindeki yururlukteki en yakin fiyat dondurulur.
      return fetch(url, { credentials: 'same-origin' })
        .then(function (r) { return r.ok ? r.json() : Promise.reject(new Error('HTTP ' + r.status)) })
        .catch(function (e) { return { found: false, message: 'HATA: ' + (e.message || e), components: [], totalCost: 0 } })
    })
    Promise.all(promises).then(function (arr) {
      if (runId !== lastRunRef.current) return  // eski sonuc — yoksay
      setResults(arr)
    }).finally(function () { if (runId === lastRunRef.current) setLoading(false) })
  }, [open, lines, priceGroupId, currencyId, priceType, validOn])

  useEffect(function () { fetchAll() }, [fetchAll])

  // Stil
  var overlayBg = isLight ? 'rgba(15,23,42,.45)' : 'rgba(0,0,0,.55)'
  var panelBg   = isLight ? '#ffffff'           : 'rgba(23,26,43,.96)'
  var panelBdr  = isLight ? '#e2e8f0'           : 'rgba(255,255,255,.08)'
  var textColor = isLight ? '#1e293b'           : 'rgba(255,255,255,.88)'
  var mutedText = isLight ? '#64748b'           : 'rgba(255,255,255,.55)'
  var subSurface= isLight ? '#f8fafc'           : 'rgba(255,255,255,.03)'
  // Native option panelinin koyu temada koyu render edilmesi icin explicit
  // background/color (CombinationPickerModal ile ayni pattern).
  var selectBg = isLight ? '#ffffff' : '#1e293b'
  var selectFg = isLight ? '#1e293b' : '#e2e8f0'
  var inputStyle = {
    padding: '5px 9px', borderRadius: 7,
    background: selectBg,
    border: '1px solid ' + (isLight ? '#e2e8f0' : 'rgba(255,255,255,.12)'),
    color: selectFg, fontSize: '.78rem', outline: 'none',
    colorScheme: isLight ? 'light' : 'dark',
  }
  var optionStyle = { background: selectBg, color: selectFg }

  if (!open) return null

  // Toplam — sadece found olanlari topla
  var grandTotal = (results || []).reduce(function (acc, r) {
    return acc + (r && r.found && r.totalCost ? Number(r.totalCost) : 0)
  }, 0)
  var currency = currencies.find(function (c) { return c.id === currencyId })
  var currencySymbol = currency ? (currency.symbol || currency.code || '') : ''
  var missingBomCount = (results || []).filter(function (r) { return r && !r.found }).length

  var modalContent = (
    <div onClick={close} style={{
      position: 'fixed', inset: 0, zIndex: 10000,
      background: overlayBg, backdropFilter: 'blur(6px)', WebkitBackdropFilter: 'blur(6px)',
      display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 20,
      animation: 'qcsFadeIn .15s ease-out',
    }}>
      <style>{`
        @keyframes qcsFadeIn { from { opacity: 0 } to { opacity: 1 } }
        @keyframes qcsSlide  { from { transform: translateY(8px) scale(.97); opacity: 0 } to { transform: none; opacity: 1 } }
      `}</style>
      <div onClick={function (e) { e.stopPropagation() }} style={{
        width: 'min(1200px, 97vw)', height: 'min(720px, 92vh)',
        background: panelBg, border: '1px solid ' + panelBdr, borderRadius: 16,
        boxShadow: '0 24px 72px rgba(0,0,0,.5)',
        display: 'flex', flexDirection: 'column', overflow: 'hidden',
        color: textColor, animation: 'qcsSlide .22s cubic-bezier(.23,1,.32,1)',
      }}>
        <div style={{
          padding: '14px 18px', borderBottom: '1px solid ' + panelBdr,
          display: 'flex', alignItems: 'center', gap: 10, flexShrink: 0,
        }}>
          <ClipboardList size={16} style={{ color: '#fbbf24' }} />
          <div style={{ flex: 1 }}>
            <div style={{ fontSize: '0.92rem', fontWeight: 700 }}>Tüm Ürünlerin Maliyeti</div>
            <div style={{ fontSize: '0.72rem', color: mutedText, marginTop: 2 }}>
              Belgedeki <strong>{lines.length}</strong> kalemin recetelerini fiyat grubuna gore toplam maliyetlendirir.
            </div>
          </div>
          <button onClick={close} title="Kapat" style={{
            background: 'transparent', border: 'none', cursor: 'pointer',
            color: mutedText, padding: 6, borderRadius: 6,
          }}><X size={18} /></button>
        </div>

        {/* Body — sol filtre paneli + sag tablo */}
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
            {/* Bilesen Gruplama — coklu secim (checkbox), dinamik level (tum kalemlerin
                bilesenlerinden derlenmis benzersiz seviyeler). */}
            <div style={{ display: 'flex', flexDirection: 'column', gap: 6, paddingTop: 6, borderTop: '1px dashed ' + panelBdr, marginTop: 4 }}>
              <span style={{ fontSize: 10, fontWeight: 600, color: mutedText, textTransform: 'uppercase', letterSpacing: '.04em' }}>Bileşen Gruplama</span>
              {(function () {
                var seen = new Set()
                ;(results || []).forEach(function (r) {
                  if (!r || !r.found) return
                  ;(r.components || []).forEach(function (c) {
                    var groups = c.groups || {}
                    Object.keys(groups).forEach(function (lv) {
                      var n = parseInt(lv, 10)
                      if (!isNaN(n) && groups[lv] && groups[lv].code) seen.add(n)
                    })
                  })
                })
                var levels = Array.from(seen).sort(function (a, b) { return a - b })
                if (levels.length === 0) {
                  return <span style={{ fontSize: 10, color: mutedText, fontStyle: 'italic' }}>Bileşenlere henüz grup kodu atanmamış.</span>
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

          {/* Sag panel: kalem listesi (her satir genisletilebilir) */}
          <div style={{ flex: 1, overflowY: 'auto', padding: '8px 18px 14px', minWidth: 0 }}>
          {loading && (
            <div style={{ padding: 16, textAlign: 'center', color: mutedText, fontSize: '.82rem' }}>
              <Loader2 size={18} className="animate-spin" style={{ verticalAlign: 'middle', marginRight: 6 }} /> Maliyet hesaplanıyor...
            </div>
          )}
          {!loading && error && (
            <div style={{ padding: '10px 14px', borderRadius: 10, fontSize: '.82rem',
              background: isLight ? '#fef2f2' : 'rgba(239,68,68,.12)',
              border: '1px solid rgba(239,68,68,.35)',
              color: isLight ? '#b91c1c' : '#fca5a5',
              display: 'flex', alignItems: 'center', gap: 8,
            }}><AlertCircle size={14} /> {error}</div>
          )}
          {!loading && !error && (
            <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '.82rem' }}>
              <thead style={{ position: 'sticky', top: 0, background: panelBg, zIndex: 1 }}>
                <tr style={{ borderBottom: '1px solid ' + panelBdr }}>
                  <th style={{ width: 24 }}></th>
                  <th style={{ padding: '6px 8px', textAlign: 'left', color: mutedText, fontSize: '.68rem', textTransform: 'uppercase', letterSpacing: '.04em' }}>Kalem</th>
                  <th style={{ padding: '6px 8px', textAlign: 'right', color: mutedText, fontSize: '.68rem', textTransform: 'uppercase', letterSpacing: '.04em' }}>Adet</th>
                  <th style={{ padding: '6px 8px', textAlign: 'right', color: mutedText, fontSize: '.68rem', textTransform: 'uppercase', letterSpacing: '.04em' }}>Bilesen #</th>
                  <th style={{ padding: '6px 8px', textAlign: 'right', color: mutedText, fontSize: '.68rem', textTransform: 'uppercase', letterSpacing: '.04em' }}>Toplam Maliyet</th>
                </tr>
              </thead>
              <tbody>
                {lines.map(function (l, i) {
                  var res = results[i]
                  var isExp = !!expanded[i]
                  var isFound = res && res.found
                  var compCount = isFound ? (res.components || []).length : 0
                  var rowBg = isFound ? 'transparent' : (isLight ? 'rgba(245,158,11,.04)' : 'rgba(245,158,11,.06)')
                  return (
                    <>
                      <tr key={'r-' + i} onClick={function () { if (isFound && compCount > 0) setExpanded(function (p) { var n = Object.assign({}, p); n[i] = !p[i]; return n }) }}
                        style={{ borderBottom: '1px solid ' + panelBdr, background: rowBg, cursor: (isFound && compCount > 0) ? 'pointer' : 'default' }}>
                        <td style={{ padding: '6px 4px', color: mutedText }}>
                          {isFound && compCount > 0
                            ? (isExp ? <ChevronDown size={14} /> : <ChevronRight size={14} />)
                            : null}
                        </td>
                        <td style={{ padding: '6px 8px' }}>
                          <div style={{ fontWeight: 600 }}>{l.materialCode}{l.configCode ? <span style={{ color: mutedText, fontWeight: 400 }}> · {l.configCode}</span> : null}</div>
                          <div style={{ fontSize: '.7rem', color: mutedText }}>{l.materialName || ''}</div>
                        </td>
                        <td style={{ padding: '6px 8px', textAlign: 'right', fontFamily: 'ui-monospace, Menlo, Consolas, monospace', fontSize: '.78rem' }}>{fmt(l.quantity, 2)}</td>
                        <td style={{ padding: '6px 8px', textAlign: 'right', fontFamily: 'ui-monospace, Menlo, Consolas, monospace', fontSize: '.78rem', color: isFound ? textColor : mutedText }}>
                          {isFound ? compCount : (res && !res.found ? 'recete yok' : '—')}
                        </td>
                        <td style={{ padding: '6px 8px', textAlign: 'right', fontFamily: 'ui-monospace, Menlo, Consolas, monospace', fontWeight: 600, color: isFound ? textColor : mutedText }}>
                          {isFound ? (fmt(res.totalCost, 2) + ' ' + currencySymbol) : '—'}
                        </td>
                      </tr>
                      {isExp && isFound && (
                        <tr key={'e-' + i}>
                          <td colSpan={5} style={{ padding: '0 8px 8px 28px', background: subSurface }}>
                            <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '.74rem' }}>
                              <thead>
                                <tr>
                                  <th style={{ padding: '4px 6px', textAlign: 'left', color: mutedText, fontSize: '.64rem' }}>Bilesen</th>
                                  <th style={{ padding: '4px 6px', textAlign: 'right', color: mutedText, fontSize: '.64rem' }}>Net Mik.</th>
                                  <th style={{ padding: '4px 6px', textAlign: 'right', color: mutedText, fontSize: '.64rem' }}>Birim Fiyat</th>
                                  <th style={{ padding: '4px 6px', textAlign: 'right', color: mutedText, fontSize: '.64rem' }}>Satir</th>
                                </tr>
                              </thead>
                              <tbody>
                                {(function () {
                                  var comps = res.components || []
                                  function rowFor(c, key, depth) {
                                    var indent = 6 + (depth || 0) * 12
                                    return (
                                      <tr key={'cr-' + key}>
                                        <td style={{ padding: '3px 6px', paddingLeft: indent }}>
                                          <span style={{ fontWeight: 600 }}>{c.code}</span>
                                          <span style={{ color: mutedText, marginLeft: 6 }}>{c.name}</span>
                                        </td>
                                        <td style={{ padding: '3px 6px', textAlign: 'right', fontFamily: 'ui-monospace, Menlo, Consolas, monospace' }}>{fmt(c.effectiveQty, 4)}</td>
                                        <td style={{ padding: '3px 6px', textAlign: 'right', fontFamily: 'ui-monospace, Menlo, Consolas, monospace', color: c.hasPrice ? textColor : mutedText }}>
                                          {c.hasPrice ? fmt(c.unitPrice, 2) : '—'}
                                        </td>
                                        <td style={{ padding: '3px 6px', textAlign: 'right', fontFamily: 'ui-monospace, Menlo, Consolas, monospace', fontWeight: 600 }}>{fmt(c.lineCost, 2)}</td>
                                      </tr>
                                    )
                                  }
                                  if (groupLevels.length === 0 || comps.length === 0) {
                                    return comps.map(function (c, ci) { return rowFor(c, ci, 0) })
                                  }
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
                                      if (!map.has(k)) { map.set(k, { code: k, name: info.name || '', items: [], subtotal: 0 }); groups.push(map.get(k)) }
                                      var g = map.get(k); g.items.push(c); g.subtotal += Number(c.lineCost) || 0
                                    })
                                    if (levels.length > 1) groups.forEach(function (g) { g.children = buildTree(g.items, levels.slice(1)) })
                                    return groups
                                  }
                                  var tree = buildTree(comps, groupLevels)
                                  var rows = []
                                  function walk(groups, depth, prefix) {
                                    groups.forEach(function (g, gi) {
                                      var indent = 4 + depth * 12
                                      rows.push(
                                        <tr key={prefix + '-h' + gi} style={{ background: isLight ? 'rgba(99,102,241,.06)' : 'rgba(99,102,241,.10)' }}>
                                          <td colSpan={3} style={{ padding: '3px 6px', paddingLeft: indent, fontWeight: 700, color: isLight ? '#4338ca' : '#a5b4fc', fontSize: '.66rem', textTransform: 'uppercase', letterSpacing: '.04em' }}>
                                            {g.code ? (g.code + (g.name ? ' · ' + g.name : '')) : 'Grupsuz'}
                                            <span style={{ marginLeft: 6, fontWeight: 500, color: mutedText, textTransform: 'none', letterSpacing: 0 }}>({g.items.length})</span>
                                          </td>
                                          <td style={{ padding: '3px 6px', textAlign: 'right', fontFamily: 'ui-monospace, Menlo, Consolas, monospace', fontWeight: 700, color: isLight ? '#4338ca' : '#a5b4fc' }}>
                                            {fmt(g.subtotal, 2)}
                                          </td>
                                        </tr>
                                      )
                                      if (g.children && g.children.length > 0) walk(g.children, depth + 1, prefix + '-h' + gi)
                                      else g.items.forEach(function (c, ci) { rows.push(rowFor(c, prefix + '-' + gi + '-' + ci, depth + 1)) })
                                    })
                                  }
                                  walk(tree, 0, 'l-' + i)
                                  return rows
                                })()}
                              </tbody>
                            </table>
                          </td>
                        </tr>
                      )}
                    </>
                  )
                })}
              </tbody>
            </table>
          )}
          {!loading && missingBomCount > 0 && (
            <div style={{
              marginTop: 12, padding: '8px 12px', borderRadius: 8, fontSize: '.74rem',
              background: isLight ? '#fef3c7' : 'rgba(245,158,11,.12)',
              color: isLight ? '#92400e' : '#fcd34d',
              border: '1px solid ' + (isLight ? '#fde68a' : 'rgba(245,158,11,.35)'),
              display: 'flex', alignItems: 'center', gap: 6,
            }}>
              <AlertCircle size={13} />
              {missingBomCount} kalemin recetesi yok — toplam maliyet eksik olabilir.
            </div>
          )}
          </div>
        </div>

        <div style={{
          padding: '12px 18px', borderTop: '1px solid ' + panelBdr,
          background: subSurface, flexShrink: 0,
          display: 'flex', alignItems: 'center', gap: 10,
        }}>
          <div style={{ flex: 1, display: 'flex', alignItems: 'baseline', gap: 8 }}>
            <span style={{ fontSize: '.72rem', color: mutedText, textTransform: 'uppercase', letterSpacing: '.04em' }}>
              Belge Toplam Maliyeti
            </span>
            <span style={{ fontSize: '1.12rem', fontWeight: 700, color: '#fbbf24', fontFamily: 'ui-monospace, Menlo, Consolas, monospace' }}>
              {fmt(grandTotal, 2)} {currencySymbol}
            </span>
          </div>
          <button onClick={close} style={{
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
