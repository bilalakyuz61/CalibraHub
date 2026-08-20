/**
 * StandardFieldsEditor — Form Davranış Katmanı editörü (2026-08-05, Bölüm ayarı 2026-08-19).
 *
 * Üst bilgi (header) STANDART alanlarının davranışını yönetir: Görünür / Zorunlu /
 * Varsayılan Değer / Başlık Metni + Stili / koşullu kurallar (visibleIf, requiredIf)
 * + sol sekmelerin görünürlük / sıra / ad yönetimi + alanın kart üzerindeki BÖLÜMÜ
 * (Kimlik / Şerit 1 / Şerit 2 / …).
 *
 * Bölüm ayarı: belge üst bilgi (Genel Bilgiler) ve kalem kartı ekranları "kimlik +
 * şerit" düzenine göre çizilir; hangi alanın kimlikte, hangisinin kaçıncı şeritte
 * duracağı TEK bu ekrandan ayarlanır (ayrı "Kart Düzeni" editörü kaldırıldı).
 * `cardSection`: 0 = Kimlik, 1..N = Şerit N, null = ayarlanmamış (Varsayılan —
 * belge ekranı kendi varsayılan dağılımını uygular). Backend alanı henüz
 * dönmüyorsa (paralel geliştirme) `cardSection` undefined gelir → burada "Varsayılan"
 * (null) sayılır, hata verilmez.
 *
 * Bölüm İÇİ sıralama (2026-08-20): her alan `cardOrder` (int|null) taşır — aynı
 * `cardSection` içindeki sıra (küçük önce). Sürükle-bırak YOK (kullanıcı tercihi);
 * her satırda yukarı/aşağı ok düğmesi var. Bir grup içinde sıra her değiştiğinde
 * (taşıma veya bölüm değişimi) o grubun TÜM alanları 0,1,2… olacak şekilde
 * yeniden numaralandırılır (kararlı, boşluksuz). Bölüm değişince alan hedef
 * grubun SONUNA eklenir, hem eski hem yeni grup yeniden numaralandırılır.
 * `cardOrder` backend'den henüz dönmüyorsa `undefined` → `null` (varsayılan sıra,
 * katalog sırası korunur).
 *
 * Ekran markup'ı SABİTTİR — burada yalnızca davranış metadata'sı düzenlenir
 * (GET/POST /api/form-behavior). Fail-open: hiçbir davranış tanımlanmamışsa ekran
 * bugünkü haliyle çalışır. Kilitli alanlar (belge no, tarih, cari, para birimi)
 * gizlenemez. Kural ifadeleri widget kural motoru sözdizimiyle aynıdır; scope'ta
 * bu formun alan key'leri bulunur (örn. currency == 'USD', vatIncluded == true).
 */
import { useState, useEffect } from 'react'
import { createPortal } from 'react-dom'
import {
  SlidersHorizontal, X as XIcon, Eye, EyeOff, Lock, ArrowUp, ArrowDown,
  AlertTriangle, Plus, LayoutGrid,
} from 'lucide-react'
// Top govdesine portallanmaz — bkz. LineCardLayoutEditor'daki ayni not: tam ekran
// perde ust menu seridini kilitliyordu. iframe'in kendi body'sine portallanir.

// Bölüm şeritlerinin üst sınırı — pratikte 1-4 şerit yeterli, makul bir tavan
// (kimlik + bu kadar şerit) UI'ın taşmasını engeller.
var MAX_STRIPS = 6

function readCsrfToken() {
  try {
    var input = document.querySelector('input[name="__RequestVerificationToken"]')
    if (input && input.value) return input.value
    var shellCfg = window.__CALIBRA_SHELL_CONFIG__
    if (shellCfg && shellCfg.antiforgeryToken) return shellCfg.antiforgeryToken
    return ''
  } catch (e) { return '' }
}

// API henüz string/integer her ikisini de dönebilir (paralel backend geliştirmesi
// tamamlanmadan) — güvenle integer'a normalize et, tanınmayan değer "Varsayılan" (null).
function normalizeCardSection(v) {
  if (typeof v === 'number' && Number.isFinite(v) && v >= 0) return Math.floor(v)
  if (typeof v === 'string' && v.trim() !== '' && !Number.isNaN(Number(v))) {
    var n = Math.floor(Number(v))
    if (n >= 0) return n
  }
  return null
}

// cardOrder da aynı gerekçeyle normalize edilir (paralel backend string/integer
// her ikisini de dönebilir) — tanınmayan/eksik değer null (sıra ayarlanmamış).
function normalizeCardOrder(v) {
  if (typeof v === 'number' && Number.isFinite(v)) return Math.floor(v)
  if (typeof v === 'string' && v.trim() !== '' && !Number.isNaN(Number(v))) return Math.floor(Number(v))
  return null
}

// Bölüm içi sıralama karşılaştırıcısı: cardOrder küçük önce, null olanlar en
// sona. Eşit (veya iki null) durumda dizideki mevcut konum korunur (Array.sort
// stabil) — bu da katalog sırasına denk gelir çünkü `fields` state'i kendi
// dizi konumunu asla değiştirmez (yalnız cardOrder/cardSection değerleri mutasyona uğrar).
function compareByCardOrder(a, b) {
  var ao = (typeof a.cardOrder === 'number') ? a.cardOrder : null
  var bo = (typeof b.cardOrder === 'number') ? b.cardOrder : null
  if (ao === null && bo === null) return 0
  if (ao === null) return 1
  if (bo === null) return -1
  return ao - bo
}

function Switch(props) {
  var on = props.on
  var disabled = props.disabled
  return (
    <button
      type="button"
      disabled={disabled}
      onClick={props.onToggle}
      title={props.title || ''}
      className={'relative w-9 h-5 rounded-full transition-colors flex-shrink-0 ' + (
        disabled
          ? 'bg-slate-200 dark:bg-white/10 cursor-not-allowed opacity-60'
          : (on ? (props.color || 'bg-indigo-500/70') : 'bg-slate-300 dark:bg-white/10')
      )}
    >
      <span
        className="absolute top-0.5 w-4 h-4 rounded-full bg-white shadow-sm transition-all"
        style={{ left: on ? 18 : 2 }}
      />
    </button>
  )
}

/**
 * Bölüm seçici — native <select> DEĞİL (koyu temada beyaz render sorunu, bkz.
 * CLAUDE.md CSS kuralları). LineCardInspector.jsx'teki SegmentedControl deseniyle
 * aynı mantık (buton grubu, klavye ok tuşlarıyla gezinme) — cross-file import
 * yerine burada bağımsız kopya (bu dosya + CalibraLineItemsGrid paralel değişiyor).
 */
function SectionSegmented(props) {
  var value = props.value // null | 0 | 1..N
  var maxStrip = props.maxStrip
  var disabled = props.disabled === true

  var options = [{ value: null, label: 'Varsayılan', title: 'Ayarlanmamış — belge ekranı varsayılan dağılımı uygular' }]
  options.push({ value: 0, label: 'Kimlik', title: 'Kart başlığı (kimlik satırı)' })
  for (var i = 1; i <= maxStrip; i++) {
    options.push({ value: i, label: 'Ş' + i, title: 'Şerit ' + i })
  }

  function pick(v) { if (!disabled && typeof props.onChange === 'function') props.onChange(v) }
  function handleKeyDown(e) {
    var idx = options.findIndex(function (o) { return o.value === value })
    if (idx < 0) idx = 0
    var next = null
    if (e.key === 'ArrowRight' || e.key === 'ArrowDown') next = (idx + 1) % options.length
    else if (e.key === 'ArrowLeft' || e.key === 'ArrowUp') next = (idx - 1 + options.length) % options.length
    if (next == null) return
    e.preventDefault()
    e.stopPropagation()
    pick(options[next].value)
  }

  function classesFor(o) {
    var on = o.value === value
    if (!on) {
      return 'border-slate-200 text-slate-500 hover:border-indigo-300 hover:text-indigo-600 ' +
        'dark:border-white/10 dark:text-white/55 dark:hover:border-indigo-400/40 dark:hover:text-indigo-300'
    }
    if (o.value === null) return 'bg-slate-400 text-[#fff] border-slate-400 dark:bg-white/25 dark:border-white/25'
    if (o.value === 0) return 'bg-indigo-500 text-[#fff] border-indigo-500'
    return 'bg-violet-500 text-[#fff] border-violet-500'
  }

  return (
    <div
      role="radiogroup"
      aria-label="Bölüm"
      onKeyDown={handleKeyDown}
      className={'flex flex-wrap items-stretch gap-1 ' + (disabled ? 'opacity-45 pointer-events-none' : '')}
    >
      {options.map(function (o) {
        var on = o.value === value
        return (
          <button
            key={String(o.value)}
            type="button"
            role="radio"
            aria-checked={on}
            tabIndex={on ? 0 : -1}
            disabled={disabled}
            onClick={function () { pick(o.value) }}
            title={o.title}
            className={'h-[22px] min-w-[26px] px-1.5 rounded-md text-[10px] font-bold border transition-colors ' + classesFor(o)}
          >
            {o.label}
          </button>
        )
      })}
    </div>
  )
}

/* bg-[#fff]: Bootstrap'in .bg-white{...!important} utility'si Tailwind dark:
   varyantini eziyordu (karanlik temada beyaz bloklar) — ayni gorunum, cakismayan ad. */
var inputCls = 'w-full px-2 py-1 rounded-md text-[11.5px] border border-slate-200 bg-[#fff] text-slate-700 ' +
  'placeholder:text-slate-300 focus:outline-none focus:ring-1 focus:ring-indigo-400 ' +
  'dark:border-white/[0.14] dark:bg-slate-900/60 dark:text-white/85 dark:placeholder:text-white/45'
var ruleInputCls = inputCls + ' font-mono'

// Sekme key'ine göre bağlam rozeti (yalnız üst bilgi formunda; kalem formunda
// tek sekme olduğundan gösterilmez).
var tabLabels = { general: 'Genel Bilgiler', lines: 'Kalem Bilgileri', conditions: 'Koşullar', notes: 'Notlar' }

/**
 * Alanları cardSection'a göre grupla: Kimlik(0) → Şerit 1..N → Varsayılan(null) en
 * sonda. Her grup içi ayrıca cardOrder'a göre sıralanır (bkz. compareByCardOrder).
 */
function buildSectionGroups(fields, maxStrip) {
  function bySection(sec) {
    return fields.filter(function (f) { return f.cardSection === sec }).slice().sort(compareByCardOrder)
  }
  var groups = []
  groups.push({ key: 'section-0', label: 'Kimlik', hint: 'Kart başlığı', kind: 'identity', fields: bySection(0) })
  for (var i = 1; i <= maxStrip; i++) {
    (function (n) {
      groups.push({ key: 'section-' + n, label: 'Şerit ' + n, hint: null, kind: 'strip', fields: bySection(n) })
    })(i)
  }
  groups.push({ key: 'section-null', label: 'Varsayılan (Ayarlanmamış)', hint: 'Belge ekranı kendi varsayılan dağılımını uygular',
    kind: 'default', fields: bySection(null) })
  return groups
}

export default function StandardFieldsEditor(props) {
  var formCode = props.formCode
  var onClose = props.onClose
  var onSaved = props.onSaved

  var [loading, setLoading] = useState(true)
  var [error, setError] = useState(null)
  var [saving, setSaving] = useState(false)
  var [fields, setFields] = useState([])
  var [tabs, setTabs] = useState([])
  var [maxStrip, setMaxStrip] = useState(3)

  useEffect(function () {
    var alive = true
    fetch('/api/form-behavior/' + encodeURIComponent(formCode), { credentials: 'same-origin' })
      .then(function (r) { return r.ok ? r.json() : null })
      .then(function (data) {
        if (!alive) return
        if (!data || data.ok !== true) {
          setError((data && data.error) || 'Davranış tanımı yüklenemedi.')
          return
        }
        var loadedFields = (data.fields || []).map(function (f) {
          return {
            key: f.key, label: f.label, tab: f.tab, dataType: f.dataType, locked: f.locked === true,
            isVisible: f.isVisible !== false,
            isRequired: f.isRequired === true,
            defaultValue: f.defaultValue || '',
            labelText: f.labelText || '',
            labelStyle: f.labelStyle || '',
            visibleIf: f.visibleIf || '',
            requiredIf: f.requiredIf || '',
            cardSection: normalizeCardSection(f.cardSection),
            cardOrder: normalizeCardOrder(f.cardOrder),
          }
        })
        setFields(loadedFields)
        setTabs((data.tabs || []).map(function (t) {
          return {
            key: t.key, label: t.label, locked: t.locked === true,
            isVisible: t.isVisible !== false,
            labelText: t.labelText || '',
          }
        }))
        // Mevcut en yüksek şerit numarasına göre başlangıç şerit sayısı (en az 3,
        // en çok MAX_STRIPS) — kullanıcı zaten atanmış şeritleri görsün.
        var maxAssigned = loadedFields.reduce(function (acc, f) {
          return (typeof f.cardSection === 'number' && f.cardSection > acc) ? f.cardSection : acc
        }, 0)
        setMaxStrip(Math.max(3, Math.min(MAX_STRIPS, maxAssigned)))
      })
      .catch(function (e) { if (alive) setError('Hata: ' + (e && e.message ? e.message : String(e))) })
      .then(function () { if (alive) setLoading(false) })
    return function () { alive = false }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [formCode])

  function patchField(key, patch) {
    setFields(function (prev) {
      return prev.map(function (f) { return f.key === key ? Object.assign({}, f, patch) : f })
    })
  }

  function moveTab(idx, dir) {
    setTabs(function (prev) {
      var next = prev.slice()
      var to = idx + dir
      if (to < 0 || to >= next.length) return prev
      var tmp = next[idx]; next[idx] = next[to]; next[to] = tmp
      return next
    })
  }

  function addStrip() {
    setMaxStrip(function (prev) { return Math.min(MAX_STRIPS, prev + 1) })
  }

  /**
   * Aynı cardSection grubu içinde bir alanı bir konum yukarı/aşağı taşır.
   * Grup, ekranda gösterildiği sırayla (compareByCardOrder) yeniden hesaplanır,
   * iki eleman yer değiştirir, ardından TÜM grup 0..n-1 olacak şekilde
   * yeniden numaralandırılır (boşluksuz, kararlı).
   */
  function moveFieldInSection(key, dir) {
    setFields(function (prev) {
      var field = prev.find(function (f) { return f.key === key })
      if (!field) return prev
      var section = field.cardSection
      var groupFields = prev.filter(function (f) { return f.cardSection === section }).slice().sort(compareByCardOrder)
      var idx = groupFields.findIndex(function (f) { return f.key === key })
      var to = idx + dir
      if (idx < 0 || to < 0 || to >= groupFields.length) return prev
      var reordered = groupFields.slice()
      var tmp = reordered[idx]; reordered[idx] = reordered[to]; reordered[to] = tmp
      var orderMap = {}
      reordered.forEach(function (f, i) { orderMap[f.key] = i })
      return prev.map(function (f) {
        return Object.prototype.hasOwnProperty.call(orderMap, f.key)
          ? Object.assign({}, f, { cardOrder: orderMap[f.key] })
          : f
      })
    })
  }

  /**
   * Bir alanın bölümünü değiştirir. Alan hedef grubun SONUNA eklenir; hem eski
   * (kaynak) hem yeni (hedef) grup 0..n-1 olacak şekilde yeniden numaralandırılır.
   */
  function changeFieldSection(key, newSection) {
    setFields(function (prev) {
      var field = prev.find(function (f) { return f.key === key })
      if (!field || field.cardSection === newSection) return prev
      var oldSection = field.cardSection
      var sourceFields = prev.filter(function (f) { return f.cardSection === oldSection && f.key !== key }).slice().sort(compareByCardOrder)
      var destFields = prev.filter(function (f) { return f.cardSection === newSection }).slice().sort(compareByCardOrder)
      var orderMap = {}
      sourceFields.forEach(function (f, i) { orderMap[f.key] = i })
      destFields.forEach(function (f, i) { orderMap[f.key] = i })
      orderMap[key] = destFields.length
      return prev.map(function (f) {
        if (f.key === key) return Object.assign({}, f, { cardSection: newSection, cardOrder: orderMap[key] })
        return Object.prototype.hasOwnProperty.call(orderMap, f.key)
          ? Object.assign({}, f, { cardOrder: orderMap[f.key] })
          : f
      })
    })
  }

  async function handleSave() {
    if (saving) return
    setSaving(true)
    setError(null)
    try {
      var payload = {
        formCode: formCode,
        fields: fields.map(function (f) {
          return {
            key: f.key,
            isVisible: f.isVisible,
            isRequired: f.isRequired,
            defaultValue: f.defaultValue.trim() || null,
            labelText: f.labelText.trim() || null,
            labelStyle: f.labelStyle || null,
            visibleIf: f.visibleIf.trim() || null,
            requiredIf: f.requiredIf.trim() || null,
            cardSection: (typeof f.cardSection === 'number') ? f.cardSection : null,
            cardOrder: (typeof f.cardOrder === 'number') ? f.cardOrder : null,
          }
        }),
        tabs: tabs.map(function (t, i) {
          return { key: t.key, isVisible: t.isVisible, sortOrder: i, labelText: t.labelText.trim() || null }
        }),
      }
      var resp = await fetch('/api/form-behavior/save', {
        method: 'POST',
        credentials: 'same-origin',
        headers: {
          'Content-Type': 'application/json',
          'Accept': 'application/json',
          'RequestVerificationToken': readCsrfToken(),
        },
        body: JSON.stringify(payload),
      })
      var data = null
      try { data = await resp.json() } catch (_) {}
      if (!resp.ok || !data || data.ok !== true) {
        setError((data && data.error) || ('Kaydedilemedi (HTTP ' + resp.status + ')'))
        return
      }
      if (typeof onSaved === 'function') onSaved()
    } catch (e) {
      setError('Hata: ' + (e && e.message ? e.message : String(e)))
    } finally {
      setSaving(false)
    }
  }

  var scopeKeys = fields.map(function (f) { return f.key }).join(', ')
  var showTabBadge = tabs.length > 0
  var sectionGroups = buildSectionGroups(fields, maxStrip)

  return createPortal(
    <div
      onClick={function (e) { if (e.target === e.currentTarget && !saving) onClose() }}
      onKeyDown={function (e) { if (e.key === 'Escape' && !saving) onClose() }}
      className="fixed inset-0 z-[60] flex items-center justify-center p-4"
      style={{ background: 'rgba(15,23,42,0.45)', backdropFilter: 'blur(5px)', WebkitBackdropFilter: 'blur(5px)' }}
    >
      <div
        className="w-full max-w-[1060px] flex flex-col overflow-hidden rounded-2xl border border-slate-200 bg-[#fff] shadow-2xl dark:border-white/10 dark:bg-slate-900"
        style={{ height: 'min(880px, calc(100vh - 64px))' }}
        role="dialog"
        aria-label="Standart Alanlar"
      >
        {/* Header */}
        <div className="flex items-center gap-3 px-5 py-4 border-b border-slate-200 dark:border-white/[0.08] flex-shrink-0">
          <div className="w-9 h-9 rounded-xl flex items-center justify-center bg-indigo-50 border border-indigo-200 text-indigo-600 dark:bg-indigo-500/15 dark:border-indigo-400/30 dark:text-indigo-300">
            <SlidersHorizontal size={17} strokeWidth={1.9} />
          </div>
          <div className="flex-1 min-w-0">
            <div className="text-[14px] font-bold text-slate-800 dark:text-white/90">Standart Alanlar — Davranış</div>
            <div className="text-[11px] text-slate-600 dark:text-white/60">
              Görünürlük, zorunluluk, varsayılan değer, başlık, koşullu kurallar ve kart Bölümü bu formun tüm kullanıcıları için geçerlidir.
            </div>
          </div>
          <button
            type="button"
            onClick={function () { if (!saving) onClose() }}
            className="w-8 h-8 rounded-lg flex items-center justify-center text-slate-500 hover:text-rose-600 hover:bg-rose-50 dark:text-white/55 dark:hover:text-rose-300 dark:hover:bg-rose-500/10 transition-colors"
            title="Kapat (Esc)"
          >
            <XIcon size={15} strokeWidth={2} />
          </button>
        </div>

        {/* Body */}
        <div className="flex-1 min-h-0 overflow-y-auto px-5 py-4">
          {loading && (
            <div className="py-10 text-center text-[12px] text-slate-500 dark:text-white/55">Yükleniyor…</div>
          )}

          {!loading && !error && (
            <>
              {/* ── Sekmeler (yalnız üst bilgi formlarında — kalem formunda boş gelir) ── */}
              {tabs.length > 0 && (
              <div className="mb-4">
                <div className="text-[11.5px] font-bold text-slate-700 dark:text-white/80 mb-1.5">Sekmeler</div>
                <div className="flex flex-col gap-1">
                  {tabs.map(function (t, idx) {
                    return (
                      <div key={t.key} className="flex items-center gap-2 rounded-lg border border-slate-200 bg-slate-50/60 dark:border-white/10 dark:bg-white/[0.03] px-2.5 py-1.5">
                        <div className="flex flex-col gap-0.5">
                          <button type="button" onClick={function () { moveTab(idx, -1) }} disabled={idx === 0}
                            className="text-slate-500 hover:text-indigo-600 dark:text-white/55 dark:hover:text-indigo-300 disabled:opacity-25">
                            <ArrowUp size={11} strokeWidth={2.2} />
                          </button>
                          <button type="button" onClick={function () { moveTab(idx, 1) }} disabled={idx === tabs.length - 1}
                            className="text-slate-500 hover:text-indigo-600 dark:text-white/55 dark:hover:text-indigo-300 disabled:opacity-25">
                            <ArrowDown size={11} strokeWidth={2.2} />
                          </button>
                        </div>
                        <span className="text-[11.5px] font-semibold text-slate-600 dark:text-white/70 min-w-[110px]">{t.label}</span>
                        <input
                          type="text"
                          value={t.labelText}
                          maxLength={40}
                          placeholder={t.label + ' (varsayılan ad)'}
                          onChange={function (e) {
                            var v = e.target.value
                            setTabs(function (prev) { return prev.map(function (x) { return x.key === t.key ? Object.assign({}, x, { labelText: v }) : x }) })
                          }}
                          className={inputCls + ' max-w-[220px]'}
                        />
                        <div className="flex-1" />
                        {t.locked ? (
                          <span title="Bu sekme gizlenemez"><Lock size={12} className="text-slate-400 dark:text-white/45" /></span>
                        ) : (
                          <button
                            type="button"
                            onClick={function () {
                              setTabs(function (prev) { return prev.map(function (x) { return x.key === t.key ? Object.assign({}, x, { isVisible: !x.isVisible }) : x }) })
                            }}
                            title={t.isVisible ? 'Sekmeyi gizle' : 'Sekmeyi göster'}
                            className="text-slate-500 hover:text-indigo-600 dark:text-white/55 dark:hover:text-indigo-300"
                          >
                            {t.isVisible ? <Eye size={13} strokeWidth={2} /> : <EyeOff size={13} strokeWidth={2} className="text-rose-500 dark:text-rose-300" />}
                          </button>
                        )}
                      </div>
                    )
                  })}
                </div>
              </div>
              )}

              {/* ── Bölüm şeridi araç çubuğu ── */}
              <div className="flex items-center gap-2 mb-2">
                <LayoutGrid size={13} className="text-slate-500 dark:text-white/55 flex-shrink-0" />
                <span className="text-[11.5px] font-bold text-slate-700 dark:text-white/80">Alanlar — Bölüme Göre</span>
                <span className="text-[10.5px] text-slate-500 dark:text-white/50">({maxStrip}/{MAX_STRIPS} şerit)</span>
                <div className="flex-1" />
                <button
                  type="button"
                  onClick={addStrip}
                  disabled={maxStrip >= MAX_STRIPS}
                  title={maxStrip >= MAX_STRIPS ? ('En fazla ' + MAX_STRIPS + ' şerit tanımlanabilir') : 'Yeni şerit ekle'}
                  className={'flex items-center gap-1 px-2.5 py-1 rounded-md text-[10.5px] font-semibold border transition-colors ' + (
                    maxStrip >= MAX_STRIPS
                      ? 'text-slate-300 border-slate-200 cursor-not-allowed dark:text-white/25 dark:border-white/10'
                      : 'text-indigo-600 border-indigo-200 bg-indigo-50 hover:bg-indigo-100 dark:text-indigo-300 dark:border-indigo-400/30 dark:bg-indigo-500/10 dark:hover:bg-indigo-500/20'
                  )}
                >
                  <Plus size={12} strokeWidth={2.4} /> Yeni Şerit Ekle
                </button>
              </div>

              {/* Alan detay kolonlarının ortak başlığı (her grupta tekrarlanmaz) */}
              <div className="hidden lg:grid grid-cols-[1fr_1fr_100px_1fr_1fr] gap-2 px-2.5 pb-1 text-[9.5px] font-semibold text-slate-500 dark:text-white/55">
                <span>Varsayılan Değer</span><span>Başlık Metni</span><span>Başlık Stili</span>
                <span>Görünürlük Koşulu</span><span>Zorunluluk Koşulu</span>
              </div>

              {/* ── Alanlar (bölüme göre gruplu: Kimlik / Şerit 1.. / Varsayılan) ── */}
              {sectionGroups.map(function (group) {
                if (group.fields.length === 0) return null
                return (
                  <div key={group.key} className="mb-4">
                    <div className="flex items-center gap-1.5 mb-1.5">
                      <span className={'px-1.5 py-0.5 rounded text-[9.5px] font-bold border ' + (
                        group.kind === 'identity'
                          ? 'bg-indigo-50 text-indigo-600 border-indigo-200 dark:bg-indigo-500/15 dark:text-indigo-300 dark:border-indigo-400/30'
                          : group.kind === 'strip'
                            ? 'bg-violet-50 text-violet-600 border-violet-200 dark:bg-violet-500/15 dark:text-violet-300 dark:border-violet-400/30'
                            : 'bg-slate-100 text-slate-500 border-slate-200 dark:bg-white/[0.06] dark:text-white/55 dark:border-white/10'
                      )}>
                        {group.label}
                      </span>
                      {group.hint && <span className="text-[10px] text-slate-400 dark:text-white/40">{group.hint}</span>}
                    </div>
                    <div className="flex flex-col gap-1.5">
                      {group.fields.map(function (f, fIdx) {
                        var canMoveUp = fIdx > 0
                        var canMoveDown = fIdx < group.fields.length - 1
                        return (
                          <div key={f.key} className="rounded-lg border border-slate-200 bg-slate-50/60 dark:border-white/10 dark:bg-white/[0.03] px-2.5 py-2 flex items-stretch gap-2">
                            {/* Bölüm içi sıra — sürükle-bırak yok, ok düğmeleriyle taşınır */}
                            <div className="flex flex-col gap-0.5 justify-center flex-shrink-0">
                              <button
                                type="button"
                                onClick={function () { moveFieldInSection(f.key, -1) }}
                                disabled={!canMoveUp}
                                title="Yukarı taşı"
                                className="text-slate-500 hover:text-indigo-600 dark:text-white/55 dark:hover:text-indigo-300 disabled:opacity-25 disabled:cursor-not-allowed"
                              >
                                <ArrowUp size={11} strokeWidth={2.2} />
                              </button>
                              <button
                                type="button"
                                onClick={function () { moveFieldInSection(f.key, 1) }}
                                disabled={!canMoveDown}
                                title="Aşağı taşı"
                                className="text-slate-500 hover:text-indigo-600 dark:text-white/55 dark:hover:text-indigo-300 disabled:opacity-25 disabled:cursor-not-allowed"
                              >
                                <ArrowDown size={11} strokeWidth={2.2} />
                              </button>
                            </div>
                            <div className="flex-1 min-w-0 flex flex-col gap-1.5">
                            {/* Satır 1: alan adı + bağlam rozeti + Görünür/Zorunlu + Bölüm */}
                            <div className="flex items-center gap-2 flex-wrap">
                              <div className="flex items-center gap-1.5 min-w-0">
                                {f.locked && <span title="Çekirdek alan — gizlenemez"><Lock size={11} className="text-slate-400 dark:text-white/45 flex-shrink-0" /></span>}
                                <span className="truncate text-[11.5px] font-semibold text-slate-600 dark:text-white/70" title={f.key}>{f.label}</span>
                                {showTabBadge && tabLabels[f.tab] && (
                                  <span className="px-1.5 py-0.5 rounded text-[9px] font-semibold bg-slate-100 text-slate-500 border border-slate-200 dark:bg-white/[0.05] dark:text-white/50 dark:border-white/10 flex-shrink-0">
                                    {tabLabels[f.tab]}
                                  </span>
                                )}
                              </div>
                              <div className="flex-1" />
                              <div className="flex items-center gap-1.5">
                                <span className="text-[9.5px] font-semibold text-slate-500 dark:text-white/50">Görünür</span>
                                <Switch
                                  on={f.isVisible}
                                  disabled={f.locked}
                                  color="bg-emerald-500/70"
                                  title={f.locked ? 'Çekirdek alan — gizlenemez' : 'Görünür'}
                                  onToggle={function () { patchField(f.key, { isVisible: !f.isVisible }) }}
                                />
                              </div>
                              <div className="flex items-center gap-1.5">
                                <span className="text-[9.5px] font-semibold text-slate-500 dark:text-white/50">Zorunlu</span>
                                <Switch
                                  on={f.isRequired}
                                  color="bg-red-500/70"
                                  title="Zorunlu — boş bırakılırsa kayıt engellenir"
                                  onToggle={function () { patchField(f.key, { isRequired: !f.isRequired }) }}
                                />
                              </div>
                              <div className="flex items-center gap-1.5">
                                <span className="text-[9.5px] font-semibold text-slate-500 dark:text-white/50">Bölüm</span>
                                <SectionSegmented
                                  value={f.cardSection}
                                  maxStrip={maxStrip}
                                  onChange={function (v) { changeFieldSection(f.key, v) }}
                                />
                              </div>
                            </div>

                            {/* Satır 2: mevcut detay ayarları (aynen korunur) */}
                            <div className="grid grid-cols-2 lg:grid-cols-[1fr_1fr_100px_1fr_1fr] gap-2 items-center">
                              <input
                                type="text"
                                value={f.defaultValue}
                                placeholder={f.dataType === 'date' ? 'TODAY()' : '—'}
                                title="Yeni kayıtta alan boşsa uygulanır. Tarih alanında TODAY() kullanılabilir."
                                onChange={function (e) { patchField(f.key, { defaultValue: e.target.value }) }}
                                className={inputCls}
                              />
                              <input
                                type="text"
                                value={f.labelText}
                                maxLength={60}
                                placeholder={f.label}
                                onChange={function (e) { patchField(f.key, { labelText: e.target.value }) }}
                                className={inputCls}
                              />
                              <select
                                value={f.labelStyle}
                                onChange={function (e) { patchField(f.key, { labelStyle: e.target.value }) }}
                                className={inputCls}
                              >
                                <option value="">Standart</option>
                                <option value="modern">Modern</option>
                                <option value="inline">Sade</option>
                              </select>
                              <input
                                type="text"
                                value={f.visibleIf}
                                disabled={f.locked}
                                placeholder={f.locked ? '(kilitli)' : "örn: currency != 'TRY'"}
                                title="Boş = her zaman görünür. İfade false dönerse alan gizlenir."
                                onChange={function (e) { patchField(f.key, { visibleIf: e.target.value }) }}
                                className={ruleInputCls + (f.locked ? ' opacity-50 cursor-not-allowed' : '')}
                              />
                              <input
                                type="text"
                                value={f.requiredIf}
                                placeholder="örn: vatIncluded == true"
                                title="İfade true dönerse alan zorunlu olur (statik Zorunlu ayarına EK)."
                                onChange={function (e) { patchField(f.key, { requiredIf: e.target.value }) }}
                                className={ruleInputCls}
                              />
                            </div>
                            </div>
                          </div>
                        )
                      })}
                    </div>
                  </div>
                )
              })}

              <div className="text-[10.5px] text-slate-500 dark:text-white/55">
                Koşul ifadelerinde kullanılabilir alanlar: <span className="font-mono">{scopeKeys}</span>.
                Örnekler: <span className="font-mono">currency != 'TRY'</span> · <span className="font-mono">vatIncluded == true</span> · <span className="font-mono">discountRate &gt; 0</span>
              </div>
            </>
          )}

          {error && (
            <div className="mt-3 flex items-center gap-2 text-[11.5px] text-rose-600 dark:text-rose-300">
              <AlertTriangle size={13} /> {error}
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="flex items-center gap-2 px-5 py-3.5 border-t border-slate-200 dark:border-white/[0.08] bg-slate-50/60 dark:bg-white/[0.02] flex-shrink-0">
          <div className="flex-1" />
          <button
            type="button"
            onClick={function () { if (!saving) onClose() }}
            disabled={saving}
            className="px-3.5 py-1.5 rounded-lg text-[12px] font-semibold border transition-colors bg-[#fff] text-slate-600 border-slate-200 hover:bg-slate-100 dark:bg-white/[0.04] dark:text-white/70 dark:border-white/10 dark:hover:bg-white/[0.08]"
          >
            Vazgeç
          </button>
          <button
            type="button"
            onClick={handleSave}
            disabled={saving || loading || !!(error && fields.length === 0)}
            className={'px-4 py-1.5 rounded-lg text-[12px] font-bold border transition-colors ' + (
              saving
                ? 'bg-indigo-300 text-white border-indigo-300 cursor-wait dark:bg-indigo-500/40 dark:border-indigo-400/30'
                : 'bg-indigo-600 text-white border-indigo-600 hover:bg-indigo-700 dark:bg-indigo-500 dark:border-indigo-500 dark:hover:bg-indigo-600'
            )}
          >
            {saving ? 'Kaydediliyor…' : 'Kaydet'}
          </button>
        </div>
      </div>
    </div>,
    document.body
  )
}
