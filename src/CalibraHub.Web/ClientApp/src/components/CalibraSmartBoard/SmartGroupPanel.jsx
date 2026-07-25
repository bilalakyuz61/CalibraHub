/**
 * SmartGroupPanel — CalibraSmartBoard TABLO modu (viewMode:'table') icin cok
 * alanli "Grupla" acilir paneli.
 *
 * Header'daki "Grupla" (Layers) butonuna basilinca acilir kompakt bir dropdown
 * (butonun altina, sagdan hizali). Gruplanabilir alanlari SmartBoard'un
 * gonderdigi `fields` listesinden ([{ id, label, dataType, icon, color }],
 * sayisal-olmayanlar once) render eder.
 *
 * Secim modeli (kullanici tarifi birebir):
 *   - Alan satirina NORMAL tik  → tek alan gruplama, oncekiler temizlenir
 *     (istisna: zaten TEK secili alan yeniden tiklanirsa gruplama kapanir —
 *      panelden ungroup icin dogal kisayol; "Gruplamayi Temizle" de var).
 *   - Ctrl/Cmd + tik            → alani gruplama zincirine EKLER (zaten
 *     zincirdeyse CIKARIR — coklu secimin dogal tersi).
 *   Zincir sirasi = tiklama sirasi = kirilim hiyerarsisi. Secili alanlar 1.,
 *   2., ... sira rozetiyle gosterilir.
 *
 * Kalicilik/uygulama SmartBoard'da (onChange → localStorage + groupBy state);
 * bu bilesen SADECE secim UI'i — kendi state'i yok (searchQuery haric).
 *
 * Tema: hardcoded hex JSX'te YOK; Tailwind light default + dark: override
 * (CLAUDE.md "CSS ve Tema Kurallari"). NOT: bare "border"/"bg-white" yerine
 * "border-[1px]"/"bg-white/100" — bootstrap.min.css'in !important'li ayni
 * isimli utility'leriyle carpismasin diye (bkz. SmartColumnSettings.jsx dosya
 * ustu ayni not).
 */
import { useState, useEffect, useMemo } from 'react'
import { Layers, Search, XCircle } from 'lucide-react'
import { resolveIcon, resolveColor } from './DynamicWidgetFactory'

export default function SmartGroupPanel(props) {
  var isOpen = props.isOpen
  var onClose = props.onClose
  var fields = Array.isArray(props.fields) ? props.fields : []
  var groupBy = Array.isArray(props.groupBy) ? props.groupBy : []
  var onChange = props.onChange

  var [searchQuery, setSearchQuery] = useState('')

  // Panel her acildiginda aramayi sifirla (temiz baslangic).
  useEffect(function () { if (isOpen) setSearchQuery('') }, [isOpen])

  // Esc ile kapat — SmartColumnSettings/showExportConfirm ile ayni desen.
  useEffect(function () {
    if (!isOpen) return undefined
    function onKey(e) { if (e.key === 'Escape') onClose && onClose() }
    document.addEventListener('keydown', onKey)
    return function () { document.removeEventListener('keydown', onKey) }
  }, [isOpen, onClose])

  // Secim indeksi (0-tabanli) → 1-tabanli sira rozeti icin hizli arama.
  var selectionIndex = useMemo(function () {
    var m = {}
    groupBy.forEach(function (id, i) { m[id] = i })
    return m
  }, [groupBy])

  var q = searchQuery.trim().toLowerCase()
  var filteredFields = useMemo(function () {
    if (!q) return fields
    return fields.filter(function (f) {
      return (f.label || '').toLowerCase().indexOf(q) !== -1 ||
             (f.id || '').toLowerCase().indexOf(q) !== -1
    })
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [fields, q])

  function handleFieldClick(fieldId, e) {
    var ctrl = !!(e && (e.ctrlKey || e.metaKey))
    var idx = groupBy.indexOf(fieldId)
    var next
    if (ctrl) {
      // Ctrl+tik: zincire EKLE (yoksa) / CIKAR (varsa) — tiklama sirasi korunur.
      if (idx !== -1) next = groupBy.filter(function (x) { return x !== fieldId })
      else next = groupBy.concat([fieldId])
    } else {
      // Normal tik: tek alan (oncekiler temizlenir). Zaten TEK secili bu alansa → kapat.
      if (groupBy.length === 1 && idx === 0) next = []
      else next = [fieldId]
    }
    if (onChange) onChange(next)
  }

  function handleClear() {
    if (onChange) onChange([])
  }

  if (!isOpen) return null

  // Aktif zincirin sirali ozeti — kirilim hiyerarsisini "1. A › 2. B" olarak
  // gosterir (fields'tan etiket cozulur; bilinmeyen id sessizce atlanir).
  var fieldLabelById = {}
  fields.forEach(function (f) { fieldLabelById[f.id] = f.label || f.id })

  return (
    <>
      {/* Disari-tik yakalayici — seffaf, panelin hemen altinda. */}
      <div className="fixed inset-0 z-[9988]" onClick={onClose} />

      {/* Panel — butona gore konumlanir (sarmalayici .relative, bkz. SmartBoard). */}
      <div
        className="absolute right-0 top-full mt-2 z-[9989] w-72 rounded-2xl border-[1px] border-slate-200 dark:border-white/10 bg-white/100 dark:bg-[rgba(10,13,23,0.97)] shadow-[0_16px_44px_-8px_rgba(15,23,42,0.28)] dark:shadow-[0_16px_44px_-8px_rgba(0,0,0,0.6)] backdrop-blur-[24px] overflow-hidden"
        data-nodirty
        onClick={function (e) { e.stopPropagation() }}
      >
        {/* Baslik */}
        <div className="flex items-center justify-between px-3.5 py-3 border-b border-slate-200 dark:border-white/[0.06]">
          <div className="flex items-center gap-2">
            <div className="w-7 h-7 rounded-lg bg-indigo-500/15 border-[1px] border-indigo-400/25 flex items-center justify-center">
              <Layers size={14} className="text-indigo-500 dark:text-indigo-400" />
            </div>
            <div>
              <h3 className="text-[13px] font-bold text-slate-800 dark:text-white/90 leading-tight">Gruplama</h3>
              <p className="text-[10px] text-slate-500 dark:text-white/35 leading-tight">
                {groupBy.length > 0 ? (groupBy.length + ' alana göre kırılım') : 'Alan seçin'}
              </p>
            </div>
          </div>
          {groupBy.length > 0 && (
            <button
              type="button"
              onClick={handleClear}
              className="inline-flex items-center gap-1 px-2 py-1 rounded-lg text-[10.5px] font-semibold text-slate-500 dark:text-white/45 hover:text-rose-500 dark:hover:text-rose-300 hover:bg-rose-50 dark:hover:bg-rose-500/10 transition-colors"
              title="Gruplamayı Temizle"
            >
              <XCircle size={12} />
              Temizle
            </button>
          )}
        </div>

        {/* Aktif kirilim ozeti (sirali) */}
        {groupBy.length > 0 && (
          <div className="px-3.5 py-2 border-b border-slate-200/70 dark:border-white/[0.05] bg-indigo-50/50 dark:bg-indigo-500/[0.05]">
            <div className="flex items-center gap-1 flex-wrap">
              {groupBy.map(function (id, i) {
                return (
                  <span key={id} className="inline-flex items-center gap-1">
                    {i > 0 && <span className="text-slate-400 dark:text-white/30 text-[11px]">›</span>}
                    <span className="inline-flex items-center gap-1 pl-1 pr-2 py-0.5 rounded-full bg-indigo-500/15 border-[1px] border-indigo-400/30 text-[10.5px] font-medium text-indigo-700 dark:text-indigo-200">
                      <span className="w-3.5 h-3.5 rounded-full bg-indigo-500 text-white/100 text-[8.5px] font-bold flex items-center justify-center flex-shrink-0">{i + 1}</span>
                      <span className="truncate max-w-[120px]">{fieldLabelById[id] || id}</span>
                    </span>
                  </span>
                )
              })}
            </div>
          </div>
        )}

        {/* Arama — alan sayisi az degilse goster */}
        {fields.length > 6 && (
          <div className="px-3 pt-2.5 pb-1.5">
            <div className="relative">
              <Search size={13} className="absolute left-2.5 top-1/2 -translate-y-1/2 text-slate-400 dark:text-white/40" />
              <input
                type="text"
                value={searchQuery}
                onChange={function (e) { setSearchQuery(e.target.value) }}
                placeholder="Alan ara…"
                className="w-full pl-8 pr-3 py-1.5 rounded-lg bg-slate-100 dark:bg-white/[0.04] border-[1px] border-slate-200 dark:border-white/[0.06] text-xs text-slate-700 dark:text-white/70 placeholder:text-slate-400 dark:placeholder:text-white/35 focus:outline-none focus:border-indigo-400/50 dark:focus:border-indigo-400/40 transition-colors"
              />
            </div>
          </div>
        )}

        {/* Alan listesi */}
        <div className="max-h-72 overflow-y-auto px-2 py-2 space-y-1">
          {filteredFields.length === 0 ? (
            <div className="px-3 py-6 text-center text-xs text-slate-400 dark:text-white/35">Alan bulunamadı</div>
          ) : (
            filteredFields.map(function (f) {
              var seq = selectionIndex[f.id]
              var selected = seq !== undefined
              var Icon = resolveIcon(f.icon, null, f.dataType)
              var palette = resolveColor(f.color, f.dataType)
              return (
                <button
                  key={f.id}
                  type="button"
                  onClick={function (e) { handleFieldClick(f.id, e) }}
                  className={'w-full flex items-center gap-2.5 px-2.5 py-2 rounded-xl border-[1px] transition-colors text-left ' +
                    (selected
                      ? 'bg-indigo-50 dark:bg-indigo-500/[0.12] border-indigo-300/60 dark:border-indigo-400/30'
                      : 'bg-transparent border-transparent hover:bg-slate-100 dark:hover:bg-white/[0.05] hover:border-slate-200 dark:hover:border-white/[0.06]')
                  }
                  title={selected ? ('Kırılım sırası: ' + (seq + 1) + ' — kaldırmak için tıklayın') : 'Bu alana göre grupla (Ctrl+tık: kırılıma ekle)'}
                >
                  <span
                    className="w-6 h-6 rounded-lg flex items-center justify-center flex-shrink-0"
                    style={{ background: palette.bg, border: '1px solid ' + palette.border }}
                  >
                    <Icon size={12} style={{ color: palette.icon }} strokeWidth={1.8} />
                  </span>
                  <span className="flex-1 min-w-0 text-xs font-medium text-slate-700 dark:text-white/75 truncate">{f.label || f.id}</span>
                  {selected && (
                    <span className="w-5 h-5 rounded-full bg-indigo-500 text-white/100 text-[10px] font-bold flex items-center justify-center flex-shrink-0" title={'Kırılım sırası: ' + (seq + 1)}>
                      {seq + 1}
                    </span>
                  )}
                </button>
              )
            })
          )}
        </div>

        {/* Ipucu */}
        <div className="px-3.5 py-2 border-t border-slate-200 dark:border-white/[0.06] bg-slate-50/80 dark:bg-white/[0.02]">
          <p className="text-[10px] text-slate-500 dark:text-white/35 leading-snug">
            <span className="font-semibold text-slate-600 dark:text-white/50">Normal tık:</span> tek alan
            {'  ·  '}
            <span className="font-semibold text-slate-600 dark:text-white/50">Ctrl+tık:</span> kırılıma ekle
          </p>
        </div>
      </div>
    </>
  )
}
