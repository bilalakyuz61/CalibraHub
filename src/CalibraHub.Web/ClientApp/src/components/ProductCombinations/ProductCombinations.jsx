import { useState, useEffect, useCallback, useMemo, useRef } from 'react'
import {
  Zap, X, Save, Package, AlertCircle, Loader2,
  Layers, Trash2, ChevronLeft, Pencil, Check,
} from 'lucide-react'
import * as api from '../../services/combinationsService'

/* ─────────────────────────────────────────────────────────────────
   Tema yardımcısı
───────────────────────────────────────────────────────────────── */
function useTheme() {
  const [dark, setDark] = useState(!document.body.classList.contains('app-theme-light'))
  useEffect(() => {
    const upd = () => setDark(!document.body.classList.contains('app-theme-light'))
    upd()
    const obs = new MutationObserver(upd)
    obs.observe(document.body, { attributes: true, attributeFilter: ['class'] })
    return () => obs.disconnect()
  }, [])
  return dark
}

/* ─────────────────────────────────────────────────────────────────
   Renk paleti (dark / light)
───────────────────────────────────────────────────────────────── */
const PALETTE = {
  dark: {
    bg:       '#0d1117',
    toolbar:  '#161b22',
    panel:    '#161b22',
    surface:  '#1c2128',
    hover:    '#1c2128',
    border:   '#30363d',
    borderHi: '#3d444d',
    text:     '#e6edf3',
    textSub:  '#c9d1d9',
    muted:    '#8b949e',
    faint:    '#3d444d',
    // Tile states
    tile:     { bg: '#161b22',              bd: '#30363d',              tx: '#8b949e'  },
    tileSel:  { bg: '#0c2461',              bd: '#3b82f6',              tx: '#93c5fd'  },
    tileLock: { bg: '#0d2417',              bd: 'rgba(34,197,94,.40)',  tx: '#4ade80'  },
    tileHi:   { bg: '#1a0b35',              bd: '#a855f7',              tx: '#d8b4fe',
                sh: '0 0 0 1px #a855f740, 0 0 12px #a855f730' },
    // Grid row states
    rowAct:   { bg: '#140b28', bl: '#a855f7' },
    // Badges
    bGreen:   { bg: '#0d2417', bd: 'rgba(34,197,94,.45)',  tx: '#4ade80'  },
    bAmber:   { bg: '#2d1a03', bd: 'rgba(245,158,11,.50)', tx: '#fcd34d'  },
    bBlue:    { bg: '#0c2461', bd: 'rgba(59,130,246,.50)', tx: '#93c5fd'  },
    bMuted:   { bg: 'transparent', bd: '#3d444d',          tx: '#6e7681'  },
    // Toolbar buttons
    btnGen:   { bg: '#0c2461', bd: '#3b82f6', tx: '#93c5fd', hbg: '#1d4ed8', htx: '#fff' },
    btnClr:   { bg: 'transparent', bd: '#30363d', tx: '#8b949e', hbg: '#1c2128', htx: '#e6edf3' },
    btnSav:   { bg: '#0d2417', bd: '#22c55e', tx: '#4ade80', hbg: '#14532d', htx: '#fff' },
    // Confirm modal
    modalBg:  '#1c2128',
  },
  light: {
    bg:       '#f1f5f9',
    toolbar:  '#ffffff',
    panel:    '#ffffff',
    surface:  '#f8fafc',
    hover:    '#f1f5f9',
    border:   '#e2e8f0',
    borderHi: '#cbd5e1',
    text:     '#0f172a',
    textSub:  '#334155',
    muted:    '#64748b',
    faint:    '#cbd5e1',
    tile:     { bg: '#f8fafc', bd: '#e2e8f0', tx: '#64748b'  },
    tileSel:  { bg: '#eff6ff', bd: '#3b82f6', tx: '#1d4ed8'  },
    tileLock: { bg: '#f0fdf4', bd: '#86efac', tx: '#15803d'  },
    tileHi:   { bg: '#faf5ff', bd: '#a855f7', tx: '#7c3aed',
                sh: '0 0 0 1px #a855f740, 0 0 10px #a855f720' },
    rowAct:   { bg: '#faf5ff', bl: '#a855f7' },
    bGreen:   { bg: '#dcfce7', bd: '#86efac', tx: '#15803d'  },
    bAmber:   { bg: '#fef3c7', bd: '#fcd34d', tx: '#92400e'  },
    bBlue:    { bg: '#dbeafe', bd: '#93c5fd', tx: '#1d4ed8'  },
    bMuted:   { bg: 'transparent', bd: '#e2e8f0', tx: '#94a3b8' },
    btnGen:   { bg: '#eff6ff', bd: '#3b82f6', tx: '#1d4ed8', hbg: '#dbeafe', htx: '#1e40af' },
    btnClr:   { bg: 'transparent', bd: '#e2e8f0', tx: '#64748b', hbg: '#f1f5f9', htx: '#334155' },
    btnSav:   { bg: '#f0fdf4', bd: '#22c55e', tx: '#15803d', hbg: '#dcfce7', htx: '#14532d' },
    modalBg:  '#ffffff',
  },
}

/* ─────────────────────────────────────────────────────────────────
   Toast renk haritası (her iki temada da koyu arka plan — bottom-right popup)
───────────────────────────────────────────────────────────────── */
const TOAST_STYLES = {
  success: { bg: '#0d2417', bd: 'rgba(34,197,94,.4)',   tx: '#4ade80', icon: '✓' },
  warning: { bg: '#2d1a03', bd: 'rgba(245,158,11,.4)',  tx: '#fcd34d', icon: '⚠' },
  info:    { bg: '#0c2461', bd: 'rgba(59,130,246,.4)',  tx: '#93c5fd', icon: 'i' },
  error:   { bg: '#2d0909', bd: 'rgba(248,113,113,.4)', tx: '#fca5a5', icon: '✕' },
}

/* ─────────────────────────────────────────────────────────────────
   Ana bileşen
───────────────────────────────────────────────────────────────── */
export default function ProductCombinations({ csrfToken, initialStockCode }) {
  const dark = useTheme()
  const c = dark ? PALETTE.dark : PALETTE.light

  /* ── State ── */
  const [stockCodes,     setStockCodes]     = useState([])
  const [stockCode,      setStockCode]      = useState(initialStockCode || '')
  const [features,       setFeatures]       = useState([])
  const [savedCombos,    setSavedCombos]    = useState([])   // DB'den gelen, kilitli
  const [draftCombos,    setDraftCombos]    = useState([])   // yalnızca client, taslak
  const [selectedValues, setSelectedValues] = useState({})   // {featureId: Set<valueId>}
  const [highlighted,    setHighlighted]    = useState(null) // incelenen combo kodu/draftKey
  const [editingId,      setEditingId]      = useState(null) // duzenleme modunda olan kombinasyon id'si
  const [editingDesc,    setEditingDesc]    = useState('')
  const [loading,        setLoading]        = useState(false)
  const [saving,         setSaving]         = useState(false)
  const [status,         setStatus]         = useState('Hazır')
  const [toasts,         setToasts]         = useState([])
  const [confirm,        setConfirm]        = useState(null)
  const toastSeq = useRef(0)
  const draftSeq = useRef(0)

  /* ── Stok adı (sadece isim kısmı) ── */
  const stockName = useMemo(() => {
    const found = stockCodes.find(s => s.value === stockCode)
    if (!found) return ''
    const parts = found.label.split(' - ')
    return parts.length > 1 ? parts.slice(1).join(' - ') : found.label
  }, [stockCodes, stockCode])


  /* ── Tüm grid satırları (kayıtlı + taslak) ── */
  const allRows = useMemo(() => [
    ...savedCombos.map(c => ({ ...c, locked: true,  rowKey: String(c.id) })),
    ...draftCombos.map(d => ({ ...d, locked: false, rowKey: d.draftKey   })),
  ], [savedCombos, draftCombos])

  /* ── İstatistikler ── */
  const savedCount = savedCombos.length
  const draftCount = draftCombos.length
  const totalCount = savedCount + draftCount

  /* ── Grid kolon şablonu ── */
  const gridCols = useMemo(
    () => `64px 150px repeat(${features.length}, minmax(70px,1fr)) minmax(120px,1.2fr) 90px 80px`,
    [features]
  )

  /* ── Stok listesi yükle ── */
  useEffect(() => {
    api.getStockCodes().then(setStockCodes).catch(() => {})
  }, [])

  /* ── Stok değişince veri yükle ── */
  useEffect(() => {
    if (!stockCode) {
      setFeatures([]); setSavedCombos([]); setDraftCombos([])
      setSelectedValues({}); setHighlighted(null)
      return
    }
    setLoading(true)
    api.getCombinationsData(stockCode)
      .then(d => {
        setFeatures(d.features || [])
        setSavedCombos(d.combos   || [])
        setDraftCombos([])
        setSelectedValues({}); setHighlighted(null)
        const n = (d.combos || []).length
        setStatus(n > 0 ? `${n} kayıtlı kombinasyon yüklendi` : 'Henüz kombinasyon yok')
      })
      .catch(() => setStatus('Veri yüklenemedi'))
      .finally(() => setLoading(false))
  }, [stockCode])

  /* ── Toast ── */
  const showToast = useCallback((msg, type = 'info') => {
    const id = ++toastSeq.current
    setToasts(prev => [...prev, { id, msg, type }])
    setTimeout(() => setToasts(prev => prev.filter(t => t.id !== id)), 3600)
  }, [])

  /* ── Tile durumu hesapla ── */
  function getTileState(featureId, valueId) {
    const isSelected = selectedValues[featureId]?.has(valueId) ?? false
    let isHighlighted = false
    if (highlighted) {
      const row = allRows.find(r => r.rowKey === highlighted)
      isHighlighted = row?.cells?.some(cc => cc.featureId === featureId && cc.valueId === valueId) ?? false
    }
    return { isSelected, isHighlighted }
  }

  /* ── Tile seçim toggle ── */
  function toggleTile(featureId, valueId, disabled) {
    if (disabled) {
      showToast('Bu deger bu stok icin haric tutulmus', 'warning')
      return
    }
    setSelectedValues(prev => {
      const set = new Set(prev[featureId] || [])
      if (set.has(valueId)) set.delete(valueId)
      else set.add(valueId)
      return { ...prev, [featureId]: set }
    })
  }

  /* ── Grid satırı inceleme (highlight toggle) ──
     Satır seçilince hücre değerlerini selectedValues'a yükler — kullanıcı
     bu tabandan yeni tile'lar ekleyerek varyant üretebilir. */
  function inspectRow(rowKey) {
    const willHighlight = highlighted !== rowKey
    setHighlighted(willHighlight ? rowKey : null)
    if (willHighlight) {
      const row = allRows.find(r => r.rowKey === rowKey)
      const seed = {}
      for (const cell of row?.cells || []) {
        if (!seed[cell.featureId]) seed[cell.featureId] = new Set()
        seed[cell.featureId].add(cell.valueId)
      }
      setSelectedValues(seed)
      setStatus(`İnceleniyor: ${row?.code || 'Taslak'} · ${row?.locked ? 'Kayıtlı' : 'Taslak kombinasyon'}`)
    } else {
      setSelectedValues({})
      setStatus('Vurgulama kaldırıldı')
    }
  }

  /* ── Seçimi temizle ── */
  function clearSelections() {
    setSelectedValues({})
    setHighlighted(null)
    setStatus('Seçimler temizlendi')
    showToast('Tüm seçimler temizlendi', 'info')
  }

  /* ── Kombinasyon üret (client-side cross-product) ── */
  function generateCombinations() {
    const missing = features.filter(f => !selectedValues[f.id]?.size)
    if (missing.length > 0) {
      showToast('Eksik seçim: ' + missing.map(f => f.name).join(', '), 'warning')
      return
    }

    // Kartezyen çarpım
    let product = [[]]
    for (const f of features) {
      const vals = [...(selectedValues[f.id] || new Set())]
      product = product.flatMap(partial =>
        vals.map(vid => {
          const valObj = f.values.find(v => v.id === vid)
          return [...partial, {
            featureId:        f.id,
            featureName:      f.name,
            valueId:          vid,
            valueCode:        valObj?.code        || '',
            valueDescription: valObj?.description || '',
          }]
        })
      )
    }

    const todayStr = new Date().toLocaleDateString('tr-TR', {
      day: '2-digit', month: '2-digit', year: 'numeric',
    }).replace(/\//g, '.')

    let newCount = 0
    let dupCount = 0
    const newDrafts = []

    for (const cells of product) {
      const valueIds = cells.map(cc => cc.valueId).sort((a, b) => a - b)

      // Mevcut kayıtlı veya taslak ile çakışma kontrolü
      const isDup = allRows.some(row => {
        const ids = (row.cells || []).map(cc => cc.valueId).sort((a, b) => a - b)
        return ids.length === valueIds.length && ids.every((v, i) => v === valueIds[i])
      })

      if (isDup) { dupCount++; continue }

      const draftKey = 'draft-' + (++draftSeq.current)
      newDrafts.push({ draftKey, valueIds, cells, date: todayStr, code: null })
      newCount++
    }

    if (newCount > 0) {
      setDraftCombos(prev => [...prev, ...newDrafts])
      const msg = dupCount > 0
        ? `${newCount} taslak oluşturuldu · ${dupCount} kombinasyon zaten kayıtlı, atlandı`
        : `${newCount} kombinasyon taslak olarak oluşturuldu`
      showToast(msg, 'success')
      setStatus(`${newCount} taslak eklendi — kaydetmeyi unutmayın`)
    } else if (dupCount > 0) {
      showToast(
        dupCount === 1
          ? 'Bu kombinasyon zaten kayıtlı. Farklı değerler seçerek yeni kombinasyon oluşturabilirsiniz.'
          : `Seçilen ${dupCount} kombinasyonun tamamı zaten kayıtlı. Farklı değer seçimlerini deneyin.`,
        'warning'
      )
    }
  }

  /* ── Taslakları kaydet ── */
  async function saveDrafts() {
    if (draftCombos.length === 0 || saving) return
    setSaving(true)
    try {
      const results = await api.saveDraftCombos(
        csrfToken,
        stockCode,
        draftCombos.map(d => ({ valueIds: d.valueIds }))
      )
      const failures  = results.filter(r => !r.ok)
      const okCount   = results.length - failures.length
      const failCount = failures.length

      if (failures.length > 0) {
        console.error('[Kombinasyon Kaydet] Başarısız sonuçlar:', failures)
      }

      // Sunucudan güncel listeyi yeniden yükle
      const d = await api.getCombinationsData(stockCode)
      setSavedCombos(d.combos || [])
      setDraftCombos([])
      setSelectedValues({})
      setHighlighted(null)

      if (failCount === 0) {
        showToast(`${okCount} kombinasyon kaydedildi`, 'success')
        setStatus(`${okCount} kombinasyon kaydedildi ve kilitlendi`)
      } else {
        // İlk hata mesajını göster
        const firstError = failures[0]?.message || 'Bilinmeyen hata'
        showToast(
          okCount > 0
            ? `${okCount} kaydedildi · ${failCount} başarısız: ${firstError}`
            : `Kayıt başarısız: ${firstError}`,
          'error'
        )
        setStatus(`${okCount} kaydedildi · ${failCount} hata: ${firstError}`)
      }
    } catch (e) {
      console.error('[Kombinasyon Kaydet] İstisna:', e)
      showToast('Kaydetme hatası: ' + e.message, 'error')
    } finally {
      setSaving(false)
    }
  }

  /* ── Kayıtlı kombinasyon sil ── */
  async function deleteSaved(combo) {
    const ok = await new Promise(res => setConfirm({
      msg: `"${combo.code}" silinecek. Bu işlem geri alınamaz.`, res,
    }))
    if (!ok) return
    try {
      const d = await api.deleteCombination(csrfToken, combo.id)
      if (!d.success) { showToast(d.message || 'Silinemedi', 'error'); return }
      setSavedCombos(prev => prev.filter(cc => cc.id !== combo.id))
      if (highlighted === String(combo.id)) setHighlighted(null)
      showToast(`${combo.code} silindi`, 'success')
    } catch (e) {
      showToast('Hata: ' + e.message, 'error')
    }
  }

  /* ── Taslak kombinasyon sil ── */
  function deleteDraft(draft) {
    setDraftCombos(prev => prev.filter(d => d.draftKey !== draft.draftKey))
    if (highlighted === draft.draftKey) setHighlighted(null)
  }

  /* ── Kombinasyon aciklama duzenleme ── */
  function startEdit(row) {
    setEditingId(row.id)
    setEditingDesc(row.description || '')
  }
  function cancelEdit() {
    setEditingId(null)
    setEditingDesc('')
  }
  async function saveEdit(row) {
    try {
      const r = await api.updateCombinationDescription(csrfToken, row.id, editingDesc)
      if (!r.success) { showToast(r.message || 'Guncellenemedi', 'error'); return }
      setSavedCombos(prev => prev.map(c => c.id === row.id ? { ...c, description: editingDesc } : c))
      setEditingId(null); setEditingDesc('')
      showToast('Aciklama guncellendi', 'success')
    } catch (e) {
      showToast('Hata: ' + e.message, 'error')
    }
  }

  /* ─────────────────────────────────────────────────────────────
     Render
  ───────────────────────────────────────────────────────────── */
  return (
    <div style={{
      display: 'flex', flexDirection: 'column', height: '100%',
      background: c.bg, color: c.text,
      fontFamily: '-apple-system,BlinkMacSystemFont,"Segoe UI",system-ui,sans-serif',
      fontSize: 13, overflow: 'hidden',
    }}>

      {/* ═══ TOOLBAR ═══════════════════════════════════════════ */}
      <div style={{
        display: 'flex', alignItems: 'center', gap: 8,
        padding: '0 14px', height: 52, flexShrink: 0,
        background: c.toolbar, borderBottom: `1px solid ${c.border}`,
      }}>
        {/* Geri dön */}
        <BackBtn c={c} />

        <Vdiv c={c} />

        {/* Sayfa kimliği */}
        <div style={{ display: 'flex', alignItems: 'center', gap: 7, flexShrink: 0 }}>
          <div style={{
            width: 26, height: 26, background: '#1d4ed8', borderRadius: 7,
            display: 'flex', alignItems: 'center', justifyContent: 'center',
          }}>
            <Layers size={13} color="white" />
          </div>
          <span style={{ fontSize: 12, fontWeight: 700, color: c.text, letterSpacing: '0.03em' }}>
            Kombinasyon Üretimi
          </span>
        </div>

        <Vdiv c={c} />

        {/* Stok seçici */}
        <div style={{ display: 'flex', alignItems: 'center', gap: 7, flexShrink: 0 }}>
          <span style={{ fontSize: 10, fontWeight: 700, color: c.muted, textTransform: 'uppercase', letterSpacing: '0.06em' }}>
            Stok
          </span>
          <StockSelect stockCodes={stockCodes} value={stockCode} onChange={setStockCode} c={c} />
          {stockName && (
            <span style={{
              fontSize: 12, fontWeight: 500, color: c.textSub,
              maxWidth: 220, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
            }}>
              · {stockName}
            </span>
          )}
        </div>

        {/* İstatistik badge'leri */}
        {stockCode && !loading && (
          <>
            <Vdiv c={c} />
            <div style={{ display: 'flex', alignItems: 'center', gap: 5 }}>
              <Badge b={c.bGreen}>{savedCount} kayıtlı</Badge>
              {draftCount > 0 && <Badge b={c.bAmber}>{draftCount} taslak</Badge>}
              <Badge b={c.bMuted}>{totalCount} toplam</Badge>
            </div>
          </>
        )}

        <div style={{ flex: 1 }} />

        {/* Aksiyon butonları */}
        {stockCode && !loading && (
          <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
            <TbBtn b={c.btnGen} onClick={generateCombinations} disabled={saving} icon={<Zap size={12} />}>
              Kombinasyon Üret
            </TbBtn>
            <TbBtn b={c.btnClr} onClick={clearSelections} disabled={saving} icon={<X size={12} />}>
              Seçimi Temizle
            </TbBtn>
            <Vdiv c={c} />
            <TbBtn
              b={c.btnSav}
              onClick={saveDrafts}
              disabled={draftCount === 0 || saving}
              icon={saving
                ? <Loader2 size={12} style={{ animation: 'spin 1s linear infinite' }} />
                : <Save size={12} />}
            >
              Kaydet{draftCount > 0 ? ` (${draftCount})` : ''}
            </TbBtn>
          </div>
        )}
      </div>

      {/* ═══ İÇERİK ════════════════════════════════════════════ */}
      <div style={{ display: 'flex', flex: '1 1 auto', minHeight: 0, overflow: 'hidden' }}>

        {!stockCode ? (
          <EmptyState icon={<Package size={44} strokeWidth={1.2} />} c={c}
            msg="Kombinasyonları görüntülemek için bir stok seçin" />
        ) : loading ? (
          <div style={{ flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 10, color: c.muted }}>
            <Loader2 size={20} style={{ animation: 'spin 1s linear infinite' }} />
            Yükleniyor…
          </div>
        ) : features.length === 0 ? (
          <EmptyState icon={<AlertCircle size={44} strokeWidth={1.2} />} c={c}
            msg="Bu stoka bağlı aktif özellik bulunamadı"
            sub="Önce özellikleri bu stok koduna bağlayın" />
        ) : (
          <>
            {/* ── Sol: Özellik Matrisi ── */}
            <div style={{
              width: 386, flexShrink: 0, display: 'flex', flexDirection: 'column',
              overflow: 'hidden', borderRight: `1px solid ${c.border}`,
            }}>
              <PanelHdr c={c}>
                <span>Özellik Matrisi</span>
                <span style={{ marginLeft: 'auto', fontSize: 10, color: c.muted }}>
                  Çoklu seçim yapabilirsiniz
                </span>
              </PanelHdr>

              <div style={{
                flex: '1 1 auto', overflowY: 'auto',
                padding: '10px 14px', display: 'flex', flexDirection: 'column',
              }}>
                {features.map((feature, idx) => {
                  const selSet   = selectedValues[feature.id] || new Set()
                  const selCount = selSet.size
                  return (
                    <div key={feature.id}>
                      {idx > 0 && <div style={{ height: 1, background: c.border, margin: '4px 0' }} />}
                      <div style={{ padding: '8px 0' }}>
                        {/* Özellik başlığı */}
                        <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginBottom: 7 }}>
                          <span style={{ fontSize: 12.5, fontWeight: 700, color: c.text }}>
                            {feature.name}
                          </span>
                          {selCount > 0
                            ? <Badge b={c.bBlue}>{selCount} seçili</Badge>
                            : <Badge b={c.bMuted}>seçim yok</Badge>}
                        </div>
                        {/* Tile ızgarası (3 sütun) */}
                        <div style={{
                          display: 'grid',
                          gridTemplateColumns: 'repeat(3, 1fr)',
                          gap: 5,
                        }}>
                          {feature.values.map(val => {
                            const { isSelected, isHighlighted } = getTileState(feature.id, val.id)
                            const isDisabled = val.allowed === false
                            return (
                              <Tile
                                key={val.id}
                                label={val.description}
                                isSelected={isSelected}
                                isHighlighted={isHighlighted}
                                isDisabled={isDisabled}
                                c={c}
                                onClick={() => toggleTile(feature.id, val.id, isDisabled)}
                              />
                            )
                          })}
                        </div>
                      </div>
                    </div>
                  )
                })}
              </div>
            </div>

            {/* ── Sağ: DataGrid ── */}
            <div style={{ flex: '1 1 auto', minWidth: 0, display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
              <PanelHdr c={c}>
                <span>Kombinasyonlar</span>
                {totalCount > 0 && (
                  <span style={{ fontSize: 10, color: c.muted }}>— {totalCount} kayıt</span>
                )}
                <span style={{ marginLeft: 'auto', fontSize: 10, color: c.faint }}>
                  Satıra tıklayınca matris üzerinde görselleşir
                </span>
              </PanelHdr>

              {/* Kolon başlıkları */}
              <div style={{
                display: 'grid', gridTemplateColumns: gridCols,
                alignItems: 'center', padding: '0 16px', height: 32,
                background: c.panel, borderBottom: `1px solid ${c.border}`,
                flexShrink: 0, overflowX: 'hidden',
              }}>
                <ColHdr c={c}></ColHdr>
                <ColHdr c={c}>Kombinasyon Kodu</ColHdr>
                {features.map(f => <ColHdr key={f.id} c={c}>{f.name}</ColHdr>)}
                <ColHdr c={c}>Açıklama</ColHdr>
                <ColHdr c={c}>Tarih</ColHdr>
                <ColHdr c={c}>Durum</ColHdr>
              </div>

              {/* Grid gövdesi */}
              <div style={{ flex: '1 1 auto', overflowY: 'auto', overflowX: 'hidden' }}>
                {allRows.length === 0 ? (
                  <div style={{
                    display: 'flex', flexDirection: 'column', alignItems: 'center',
                    justifyContent: 'center', height: 160, gap: 10,
                    color: c.faint, fontSize: 12,
                  }}>
                    <Package size={36} strokeWidth={1.2} style={{ opacity: 0.4 }} />
                    <span>Henüz kombinasyon oluşturulmadı</span>
                  </div>
                ) : allRows.map(row => (
                  <GridRow
                    key={row.rowKey}
                    row={row}
                    features={features}
                    gridCols={gridCols}
                    isActive={highlighted === row.rowKey}
                    c={c}
                    onClick={() => inspectRow(row.rowKey)}
                    onDelete={() => row.locked ? deleteSaved(row) : deleteDraft(row)}
                    onEditStart={() => startEdit(row)}
                    onEditSave={() => saveEdit(row)}
                    onEditCancel={cancelEdit}
                    isEditing={editingId === row.id}
                    editingDesc={editingDesc}
                    setEditingDesc={setEditingDesc}
                  />
                ))}
              </div>
            </div>
          </>
        )}
      </div>

      {/* ═══ STATUS BAR ════════════════════════════════════════ */}
      <div style={{
        display: 'flex', alignItems: 'center', gap: 12, padding: '0 14px',
        height: 26, flexShrink: 0,
        background: c.toolbar, borderTop: `1px solid ${c.border}`,
      }}>
        <div style={{
          width: 6, height: 6, borderRadius: '50%', flexShrink: 0,
          background: saving ? '#f59e0b' : '#238636',
        }} />
        <span style={{ fontSize: 10.5, color: c.muted }}>{status}</span>
        <div style={{ flex: 1 }} />
        <span style={{ fontSize: 10, color: c.faint }}>
          CalibraHub ERP · Ürün Konfigürasyon
        </span>
      </div>

      {/* ═══ TOAST ════════════════════════════════════════════ */}
      <div style={{
        position: 'fixed', bottom: 14, right: 14,
        display: 'flex', flexDirection: 'column', gap: 7,
        zIndex: 9999, pointerEvents: 'none',
      }}>
        {toasts.map(t => (
          <ToastItem
            key={t.id}
            toast={t}
            onDismiss={() => setToasts(prev => prev.filter(x => x.id !== t.id))}
          />
        ))}
      </div>

      {/* ═══ CONFIRM MODAL ════════════════════════════════════ */}
      {confirm && (
        <div
          style={{
            position: 'fixed', inset: 0, zIndex: 9999,
            background: 'rgba(0,0,0,0.55)', backdropFilter: 'blur(4px)',
            display: 'flex', alignItems: 'center', justifyContent: 'center',
          }}
          onClick={() => { confirm.res(false); setConfirm(null) }}
        >
          <div
            style={{
              background: c.modalBg, border: `1px solid ${c.border}`,
              borderRadius: 16, padding: 24, maxWidth: 360, width: '90%',
              boxShadow: '0 24px 64px rgba(0,0,0,.5)',
            }}
            onClick={e => e.stopPropagation()}
          >
            <p style={{ margin: '0 0 8px', fontSize: 15, fontWeight: 700, color: c.text }}>
              Onay Gerekiyor
            </p>
            <p style={{ margin: '0 0 20px', fontSize: 13, color: c.textSub }}>
              {confirm.msg}
            </p>
            <div style={{ display: 'flex', gap: 10 }}>
              <button
                onClick={() => { confirm.res(false); setConfirm(null) }}
                style={{
                  flex: 1, padding: '9px 0', borderRadius: 8,
                  border: `1px solid ${c.border}`, background: c.surface,
                  color: c.text, fontSize: 13, fontWeight: 600, cursor: 'pointer',
                }}
              >
                İptal
              </button>
              <button
                onClick={() => { confirm.res(true); setConfirm(null) }}
                style={{
                  flex: 1, padding: '9px 0', borderRadius: 8,
                  border: '1px solid #dc2626',
                  background: 'linear-gradient(135deg,#ef4444,#dc2626)',
                  color: '#fff', fontSize: 13, fontWeight: 600, cursor: 'pointer',
                }}
              >
                Devam Et
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

/* ─────────────────────────────────────────────────────────────────
   Tile bileşeni
───────────────────────────────────────────────────────────────── */
function Tile({ label, isSelected, isHighlighted, isDisabled, c, onClick }) {
  const [hov, setHov] = useState(false)

  // Hem mevcut kombinasyona ait (mor) hem de yeni seçime dahil (mavi) ise çift durum
  const isBoth = isSelected && isHighlighted

  let colorSet
  if (isDisabled) {
    // Hariç tutulmuş (stok kartında bu özellik için izinli liste dışında bırakılmış)
    colorSet = {
      bg: 'transparent',
      bd: c.faint,
      tx: c.muted,
      sh: undefined,
    }
  }
  else if (isBoth) {
    colorSet = {
      bg: `linear-gradient(135deg, ${c.tileHi.bg} 0%, ${c.tileHi.bg} 48%, ${c.tileSel.bg} 52%, ${c.tileSel.bg} 100%)`,
      bd: c.tileSel.bd,
      tx: c.tileSel.tx,
      sh: c.tileHi.sh,
    }
  }
  else if (isHighlighted) colorSet = c.tileHi
  else if (isSelected) colorSet = c.tileSel
  else if (hov)       colorSet = { ...c.tile, bd: c.borderHi, tx: c.text }
  else                colorSet = c.tile

  return (
    <div
      onClick={onClick}
      onMouseEnter={() => !isDisabled && setHov(true)}
      onMouseLeave={() => setHov(false)}
      title={isDisabled ? 'Bu deger bu stok icin haric tutulmus' : undefined}
      style={{
        display: 'flex', alignItems: 'center', justifyContent: 'center',
        padding: '6px 8px', borderRadius: 7,
        border: isDisabled ? `1px dashed ${colorSet.bd}` : `1px solid ${colorSet.bd}`,
        background: colorSet.bg, color: colorSet.tx,
        fontSize: 11.5, fontWeight: 500,
        cursor: isDisabled ? 'not-allowed' : 'pointer',
        opacity: isDisabled ? 0.45 : 1,
        userSelect: 'none', textAlign: 'center',
        minHeight: 34, lineHeight: 1.3,
        transition: 'all .12s ease',
        textDecoration: isDisabled ? 'line-through' : undefined,
        boxShadow: ((isHighlighted || isBoth) && colorSet.sh) ? colorSet.sh : undefined,
      }}
    >
      <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
        {label}
      </span>
    </div>
  )
}

/* ─────────────────────────────────────────────────────────────────
   Grid satırı
───────────────────────────────────────────────────────────────── */
function GridRow({
  row, features, gridCols, isActive, c, onClick, onDelete,
  onEditStart, onEditSave, onEditCancel, isEditing, editingDesc, setEditingDesc,
}) {
  const [hov, setHov] = useState(false)

  const codeColor  = isActive ? c.tileHi.tx : (row.locked ? c.bGreen.tx : c.bAmber.tx)
  const badgeColor = row.locked ? c.bGreen : c.bAmber
  const badgeText  = row.locked ? 'Kayıtlı' : 'Taslak'

  return (
    <div
      onClick={isEditing ? undefined : onClick}
      onMouseEnter={() => setHov(true)}
      onMouseLeave={() => setHov(false)}
      style={{
        display: 'grid', gridTemplateColumns: gridCols,
        alignItems: 'center', padding: '0 16px', minHeight: 36,
        borderBottom: `1px solid ${c.border}`,
        cursor: isEditing ? 'default' : 'pointer',
        background:  isActive ? c.rowAct.bg  : hov ? c.hover : 'transparent',
        borderLeft:  isActive ? `2px solid ${c.rowAct.bl}` : '2px solid transparent',
        paddingLeft: 14,
        transition:  'background .1s',
      }}
    >
      {/* Aksiyon butonlari (sol) */}
      <span style={{ display: 'inline-flex', alignItems: 'center', gap: 2 }}>
        {row.locked && !isEditing && (
          <button
            onClick={e => { e.stopPropagation(); onEditStart() }}
            title="Aciklamayi duzenle"
            style={{
              display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
              width: 26, height: 26, borderRadius: 6,
              border: 'none', background: 'none', cursor: 'pointer',
              color: c.faint, transition: 'color .12s',
              opacity: hov ? 1 : 0,
            }}
            onMouseEnter={e => { e.currentTarget.style.color = '#60a5fa' }}
            onMouseLeave={e => { e.currentTarget.style.color = c.faint }}
          >
            <Pencil size={13} />
          </button>
        )}
        {isEditing && (
          <>
            <button
              onClick={e => { e.stopPropagation(); onEditSave() }}
              title="Kaydet"
              style={{
                display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
                width: 26, height: 26, borderRadius: 6,
                border: 'none', background: 'none', cursor: 'pointer',
                color: '#34d399', transition: 'color .12s',
              }}
            >
              <Check size={14} />
            </button>
            <button
              onClick={e => { e.stopPropagation(); onEditCancel() }}
              title="Iptal"
              style={{
                display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
                width: 26, height: 26, borderRadius: 6,
                border: 'none', background: 'none', cursor: 'pointer',
                color: c.muted, transition: 'color .12s',
              }}
            >
              <X size={14} />
            </button>
          </>
        )}
        {!isEditing && (
          <button
            onClick={e => { e.stopPropagation(); onDelete() }}
            title={row.locked ? 'Kombinasyonu sil' : 'Taslağı kaldır'}
            style={{
              display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
              width: 26, height: 26, borderRadius: 6,
              border: 'none', background: 'none', cursor: 'pointer',
              color: c.faint, transition: 'color .12s',
              opacity: hov ? 1 : 0,
            }}
            onMouseEnter={e => { e.currentTarget.style.color = '#f87171' }}
            onMouseLeave={e => { e.currentTarget.style.color = c.faint }}
          >
            <Trash2 size={13} />
          </button>
        )}
      </span>

      {/* Kombinasyon kodu */}
      <span style={{
        fontFamily: 'ui-monospace, Menlo, Consolas, monospace',
        fontSize: 11.5, fontWeight: 700, color: codeColor,
        letterSpacing: '.02em', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
      }}>
        {row.locked ? row.code : '— taslak —'}
      </span>

      {/* Özellik değerleri */}
      {features.map(f => {
        const cell = row.cells?.find(cc => cc.featureId === f.id)
        return (
          <span key={f.id} style={{
            fontSize: 12, color: c.textSub,
            overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
            paddingRight: 4,
          }}>
            {cell?.valueDescription || '—'}
          </span>
        )
      })}

      {/* Aciklama */}
      {isEditing ? (
        <input
          type="text"
          value={editingDesc}
          onChange={e => setEditingDesc(e.target.value)}
          onClick={e => e.stopPropagation()}
          onKeyDown={e => {
            if (e.key === 'Enter') { e.preventDefault(); onEditSave() }
            else if (e.key === 'Escape') { e.preventDefault(); onEditCancel() }
          }}
          autoFocus
          placeholder="Açıklama..."
          style={{
            width: '100%', height: 26, padding: '0 8px',
            background: c.surface, border: `1px solid ${c.borderHi}`,
            borderRadius: 4, color: c.text, fontSize: 12, outline: 'none',
            marginRight: 6,
          }}
        />
      ) : (
        <span style={{
          fontSize: 12, color: c.textSub,
          overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
          paddingRight: 4,
        }}>
          {row.description || '—'}
        </span>
      )}

      {/* Tarih */}
      <span style={{ fontSize: 11, color: c.muted }}>{row.date || '—'}</span>

      {/* Durum badge */}
      <span style={{
        display: 'inline-flex', alignItems: 'center',
        padding: '1px 8px', borderRadius: 20, fontSize: 10.5, fontWeight: 700,
        border: `1px solid ${badgeColor.bd}`,
        background: badgeColor.bg, color: badgeColor.tx,
        whiteSpace: 'nowrap',
      }}>
        {badgeText}
      </span>
    </div>
  )
}

/* ─────────────────────────────────────────────────────────────────
   Küçük yardımcı bileşenler
───────────────────────────────────────────────────────────────── */
function BackBtn({ c }) {
  const [hov, setHov] = useState(false)
  function navigate() {
    const a = document.createElement('a')
    a.href = '/Logistics/ProductConfiguration'
    a.setAttribute('data-workspace-link', '')
    document.body.appendChild(a); a.click(); a.remove()
  }
  return (
    <button
      onClick={navigate}
      onMouseEnter={() => setHov(true)}
      onMouseLeave={() => setHov(false)}
      title="Özellik listesine dön"
      style={{
        display: 'inline-flex', alignItems: 'center', gap: 5,
        padding: '5px 10px', borderRadius: 6,
        border: `1px solid ${c.border}`,
        background: hov ? c.hover : 'transparent',
        color: c.muted, fontSize: 12, fontWeight: 600,
        cursor: 'pointer', transition: 'all .12s', outline: 'none', flexShrink: 0,
      }}
    >
      <ChevronLeft size={13} />
      Özellikler
    </button>
  )
}

function StockSelect({ stockCodes, value, onChange, c }) {
  const chevron = encodeURIComponent(
    `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16"><path fill="none" stroke="${c.muted}" stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M2 5l6 6 6-6"/></svg>`
  )
  return (
    <select
      value={value}
      onChange={e => onChange(e.target.value)}
      style={{
        padding: '5px 28px 5px 10px', borderRadius: 7,
        background: `${c.surface} url("data:image/svg+xml,${chevron}") no-repeat right 6px center / 12px 12px`,
        border: `1px solid ${c.border}`, color: c.text,
        fontSize: 12, appearance: 'none', cursor: 'pointer',
        outline: 'none', minWidth: 160,
      }}
    >
      <option value="">— Stok seçin —</option>
      {stockCodes.map(s => (
        <option key={s.value} value={s.value}>{s.value}</option>
      ))}
    </select>
  )
}

function Badge({ children, b }) {
  return (
    <span style={{
      display: 'inline-flex', alignItems: 'center',
      padding: '1px 8px', borderRadius: 20,
      fontSize: 10.5, fontWeight: 700, whiteSpace: 'nowrap',
      border: `1px solid ${b.bd}`, background: b.bg, color: b.tx,
    }}>
      {children}
    </span>
  )
}

function TbBtn({ children, icon, onClick, disabled, b }) {
  const [hov, setHov] = useState(false)
  return (
    <button
      onClick={onClick}
      disabled={disabled}
      onMouseEnter={() => setHov(true)}
      onMouseLeave={() => setHov(false)}
      style={{
        display: 'inline-flex', alignItems: 'center', gap: 5,
        padding: '5px 12px', borderRadius: 6,
        border: `1px solid ${b.bd}`,
        background: (hov && !disabled) ? b.hbg : b.bg,
        color: (hov && !disabled) ? b.htx : b.tx,
        fontSize: 12, fontWeight: 600,
        cursor: disabled ? 'not-allowed' : 'pointer',
        transition: 'all .12s', whiteSpace: 'nowrap',
        opacity: disabled ? 0.38 : 1, outline: 'none',
      }}
    >
      {icon}{children}
    </button>
  )
}

function PanelHdr({ children, c }) {
  return (
    <div style={{
      padding: '9px 16px 8px', borderBottom: `1px solid ${c.border}`,
      flexShrink: 0, display: 'flex', alignItems: 'center', gap: 8,
    }}>
      {children}
    </div>
  )
}

function ColHdr({ children, c }) {
  return (
    <span style={{
      fontSize: 12, fontWeight: 700, color: c.textSub,
    }}>
      {children}
    </span>
  )
}

function Vdiv({ c }) {
  return <div style={{ width: 1, height: 22, background: c.border, flexShrink: 0 }} />
}

function EmptyState({ icon, msg, sub, c }) {
  return (
    <div style={{
      flex: 1, display: 'flex', flexDirection: 'column',
      alignItems: 'center', justifyContent: 'center',
      gap: 12, color: c.muted, fontSize: 13, padding: 40, textAlign: 'center',
    }}>
      <div style={{ opacity: 0.3 }}>{icon}</div>
      <span>{msg}</span>
      {sub && <small style={{ fontSize: 12, opacity: 0.7 }}>{sub}</small>}
    </div>
  )
}

function ToastItem({ toast, onDismiss }) {
  const s = TOAST_STYLES[toast.type] || TOAST_STYLES.info
  return (
    <div
      onClick={onDismiss}
      style={{
        display: 'flex', alignItems: 'center', gap: 9,
        padding: '8px 14px', borderRadius: 8,
        border: `1px solid ${s.bd}`, background: s.bg, color: s.tx,
        fontSize: 12, fontWeight: 500,
        boxShadow: '0 4px 20px rgba(0,0,0,.5)',
        pointerEvents: 'auto', minWidth: 220, maxWidth: 380, cursor: 'pointer',
      }}
    >
      <span style={{ fontSize: 12, fontWeight: 700, width: 14, textAlign: 'center', flexShrink: 0 }}>
        {s.icon}
      </span>
      <span>{toast.msg}</span>
    </div>
  )
}
