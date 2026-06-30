/**
 * IntegrationWizard — 5 adımlı entegrasyon kurgu sihirbazı.
 *
 * Steps:
 *   1) Form Picker          — kaynak form seçimi
 *   2) Endpoint Picker      — hedef REST endpoint
 *   3) Mapping Editor       — alan eşleme (en kompleks step)
 *   4) Preview + Test       — sample kayıt + JSON output + dry-run
 *   5) Trigger + Save       — tetikleyici tipi + ad/açıklama + kaydet
 *
 * Edit modu: integrationId verilirse mevcut state yüklenir; yeni modu için boş başlar.
 */
import React, { useState, useEffect, useCallback, useRef } from 'react'
import { createPortal } from 'react-dom'
import {
  ArrowLeft, ArrowRight, Save, X, Loader2,
  FileText, Globe, GitBranch, Eye, Zap, Check, ChevronDown, Filter,
} from 'lucide-react'
import WizardStep1Form from './WizardStep1Form'
import WizardStep2Endpoint from './WizardStep2Endpoint'
import WizardStep3Mapping from './WizardStep3Mapping'
import WizardStepFilters from './WizardStepFilters'
import WizardStep4Preview from './WizardStep4Preview'
import WizardStep5Trigger from './WizardStep5Trigger'

function getCsrf() {
  const el = document.querySelector('input[name="__RequestVerificationToken"]')
  return el ? el.value : ''
}
function toast(msg, kind) {
  if (window.CalibraHub?.toast) window.CalibraHub.toast(msg, kind || 'info')
}

/**
 * Backend artik (2026-05-21) JsonStringEnumConverter ile enum'lari string olarak doner:
 * "Manual" / "Cron" / "OnSave" / "Event". Frontend ise WizardStep5Trigger.toggleTrigger
 * gibi yerlerde triggerType'i numeric (0/1/2/3) olarak kullaniyor. Load akisinda
 * normalize ederiz; aksi halde "OnSave" === 2 karsilastirmasi false donerek toggle
 * isaretsiz gorunur, ayni anda iki kayit olusur, vb.
 */
const TRIGGER_TYPE_NUM = { Manual: 0, Cron: 1, OnSave: 2, Event: 3 }
function normalizeTriggerType(t) {
  if (typeof t === 'number') return t
  if (typeof t === 'string' && t in TRIGGER_TYPE_NUM) return TRIGGER_TYPE_NUM[t]
  // Tanimsiz / beklenmeyen — 0 (Manual) varsayilan
  return 0
}

const SOURCE_TYPE_NUM = { FormField: 0, Constant: 1, Formula: 2, Lookup: 3, Function: 4 }
function normalizeSourceType(t) {
  if (typeof t === 'number') return t
  if (typeof t === 'string' && t in SOURCE_TYPE_NUM) return SOURCE_TYPE_NUM[t]
  return 0
}

const STEPS = [
  // Veri Kaynagi adimini kaldirdik — form secimi context bar'da kompakt combobox.
  // 2026-05-22: "Kısıt Kuralları" (Pre-flight Filter) eklenmesi ile 5 adım.
  { num: 1, label: 'Hedef Sistem',    shortLabel: 'Hedef',    hint: 'Verileri hangi API endpoint\'ine göndereceğiz?', icon: Globe },
  { num: 2, label: 'Alan Eşleme',     shortLabel: 'Eşleme',   hint: 'Form alanlarını hedef API alanlarına bağla.', icon: GitBranch },
  { num: 3, label: 'Kısıt Kuralları', shortLabel: 'Kısıt',    hint: 'Hangi kayıtlar aktarılsın? Koşul yoksa tümü gönderilir.', icon: Filter },
  { num: 4, label: 'Test',            shortLabel: 'Test',     hint: 'Gerçek bir kayıtla dry-run yap, çıktıyı gör.', icon: Eye },
  { num: 5, label: 'Yayına Al',       shortLabel: 'Yayın',    hint: 'Adı, tetikleyiciyi ve sonrası prosedürü belirle.', icon: Zap },
]

/** Boş wizard state — yeni integration */
const emptyState = () => ({
  id: 0,
  name: '',
  description: '',
  sourceFormCode: '',
  targetEndpointId: 0,
  errorBehavior: 0,    // Skip
  retryCount: 0,
  isActive: true,
  mappings: [],
  triggers: [],
  preProcedureName: null,
  preProcedureParamsJson: null,
  postProcedureName: null,
  postProcedureParamsJson: null,
  // Faz O — Sadece Prosedur modu flag'i (UI-only, kalici state). Toggle'i acikca tutmak icin.
  // Save sirasinda bu flag TRUE ise targetEndpointId NULL olarak gonderilir; backend zaten
  // null endpoint ile sadece-prosedur olarak calistirir.
  procedureOnlyMode: false,
  // 2026-05-22 Pre-flight Filter — kayit-basina aktarim kosulu JSON'u (Integration.SourceFilterJson).
  // Bos/null ise filtre yok, tum kayitlar gecer. WizardStepFilters bu alani yazar/okur.
  sourceFilterJson: null,
  // 2026-05-22 Cascade target flag — bu integration baska entegrasyonlar tarafindan cascade
  // hedefi olarak secilebilir mi? Default TRUE — herkes cascade'lenebilir; bilincli kapatma gerek.
  allowAsCascadeTarget: true,
  // Kod bazlı cascade: bu integration cascade hedefi olarak KOD ile çağrıldığında
  // hangi kolona göre entity bulunacak (orn. "CariKod"). NULL = ID bazlı (default).
  sourceCodeColumn: null,
})

/**
 * Draft autosave — sessionStorage'a yazar, sayfa yenilense bile yarım kalan
 * yeni wizard kaybolmaz. Edit modunda (id > 0) draft kullanılmaz; mevcut
 * entegrasyon her zaman backend'den gelir.
 */
const DRAFT_KEY = 'calibrahub.iw.draft.new'
const loadDraft = () => {
  try { const raw = sessionStorage.getItem(DRAFT_KEY); return raw ? JSON.parse(raw) : null }
  catch { return null }
}
const saveDraft = (state) => {
  try { sessionStorage.setItem(DRAFT_KEY, JSON.stringify(state)) } catch { /* quota */ }
}
const clearDraft = () => {
  try { sessionStorage.removeItem(DRAFT_KEY) } catch { /* noop */ }
}

export default function IntegrationWizard({ config }) {
  const apiBase = config?.apiBase || '/Integrations/api'
  const listUrl = config?.listUrl || '/Integrations'
  const editId  = config?.integrationId || null

  // Yeni mode'da draft varsa onu yükle — kullanıcı yarım kalan wizard'a devam eder
  const [state, setState]     = useState(() => {
    if (editId) return emptyState()
    const draft = loadDraft()
    return draft || emptyState()
  })
  const [step, setStep]       = useState(1)
  const [loading, setLoading] = useState(!!editId)
  const [saving, setSaving]   = useState(false)
  const [draftRestored, setDraftRestored] = useState(() => !editId && !!loadDraft())

  // Edit mode — mevcut entegrasyonu yükle
  useEffect(() => {
    if (!editId) { setLoading(false); return }
    (async () => {
      try {
        const r = await fetch(`${apiBase}/${editId}`, { credentials: 'same-origin' })
        const d = await r.json()
        if (d.success && d.integration) {
          const it = d.integration
          setState({
            id: it.id,
            name: it.name || '',
            description: it.description || '',
            sourceFormCode: it.sourceFormCode || '',
            targetEndpointId: it.targetEndpointId || 0,
            errorBehavior: it.errorBehavior ?? 0,
            retryCount: it.retryCount || 0,
            isActive: it.isActive ?? true,
            mappings: (it.mappings || []).map(m => ({
              targetPath: m.targetPath,
              targetDataType: m.targetDataType,
              sourceType: normalizeSourceType(m.sourceType),
              sourceValue: m.sourceValue,
              lookupSourceField: m.lookupSourceField,
              defaultValue: m.defaultValue,
              formatPattern: m.formatPattern,
              isRequired: m.isRequired,
              sortOrder: m.sortOrder,
              groupKey: m.groupKey,
              sourceSection: m.sourceSection || 'Header',
              lookupFiltersJson: m.lookupFiltersJson || null,
              lookupReturnColumn: m.lookupReturnColumn || null,
              lookupParam: m.lookupParam || null,
              cascadeToIntegrationId: m.cascadeToIntegrationId ?? null,   // 2026-05-22 Cascade
            cascadeByValue: m.cascadeByValue ?? false,
            })),
            // 2026-05-21: Backend artik JsonStringEnumConverter ile string enum
            // ("Manual","Cron","OnSave","Event") doner. Frontend tarafindaki tum
            // karsilastirmalar numeric (0/1/2/3) — burada normalize ediyoruz ki
            // toggle/edit ekraninda secili durum dogru gorunsun.
            triggers: (it.triggers || []).map(t => ({
              triggerType: normalizeTriggerType(t.triggerType),
              config: t.config,
              isActive: t.isActive,
            })),
            preProcedureName:        it.preProcedureName || null,
            preProcedureParamsJson:  it.preProcedureParamsJson || null,
            postProcedureName:       it.postProcedureName || null,
            postProcedureParamsJson: it.postProcedureParamsJson || null,
            // Faz O — endpoint NULL gelmisse "Sadece Prosedur" modunda kaydedilmis demektir
            procedureOnlyMode:       !it.targetEndpointId,
            // 2026-05-22 Pre-flight Filter — backend'den geri yüklenir (Integration.SourceFilterJson)
            sourceFilterJson:        it.sourceFilterJson || null,
            // 2026-05-22 Cascade — backend default true; null gelirse true varsay
            allowAsCascadeTarget:    it.allowAsCascadeTarget ?? true,
            sourceCodeColumn:        it.sourceCodeColumn || null,
          })
        } else {
          toast(d.error || 'Entegrasyon yüklenemedi', 'err')
        }
      } catch (e) {
        toast('Sunucu hatası: ' + e.message, 'err')
      } finally {
        setLoading(false)
      }
    })()
  }, [apiBase, editId])

  // Update state — partial merge
  const update = useCallback((patch) => setState(s => ({ ...s, ...patch })), [])

  // Yeni mode'da her state degisiminde draft'i kaydet (debounce'a gerek yok — sessionStorage hizli)
  useEffect(() => {
    if (!editId) saveDraft(state)
  }, [state, editId])

  // Faz O — "Sadece Prosedür" modu: explicit flag (kullanici toggle eder).
  // Edit modunda yuklenince eger endpoint NULL geldiyse load akisi flag'i otomatik TRUE yapar.
  const isProcedureOnly = state.procedureOnlyMode === true

  // Step navigation — STEPS=4 (eski Step 1 'Veri Kaynagi' silindi, form secimi context bar'da)
  // Form secimi her step'te tum nav icin pre-condition: step ilerleyebilmek icin sourceFormCode dolu olmali.
  const canGoNext = useCallback(() => {
    if (!state.sourceFormCode) return false   // ortak gate: form seçilmeden hicbir adim ilerlemez
    switch (step) {
      case 1: return isProcedureOnly || !!state.targetEndpointId  // Hedef Sistem
      case 2: return isProcedureOnly || state.mappings.length > 0 // Alan Eşleme
      case 3: return true                                          // Kısıt Kuralları (opsiyonel — boş bırakılabilir)
      case 4: return true                                          // Test (önizleme zorunlu değil)
      case 5: return !!state.name && (                             // Yayına Al
        !isProcedureOnly
        || !!state.preProcedureName?.trim()
        || !!state.postProcedureName?.trim()
      )
      default: return false
    }
  }, [step, state, isProcedureOnly])

  /** Bir sonraki adıma neden gidilemediğinin Türkçe açıklaması (tooltip). */
  const nextDisabledReason = useCallback(() => {
    if (!state.sourceFormCode) return 'Üstteki "Form" alanından bir kaynak form seçin.'
    switch (step) {
      case 1: return (state.targetEndpointId || isProcedureOnly) ? null
        : 'Hedef API endpoint\'i seçin veya "Sadece Prosedür" moduna geçin.'
      case 2: return (state.mappings.length > 0 || isProcedureOnly) ? null
        : 'En az bir alan eşlemesi ekleyin.'
      case 3: return null   // Kısıt Kuralları — opsiyonel
      case 4: return null   // Test — opsiyonel
      case 5:
        if (!state.name?.trim()) return 'Entegrasyona bir ad verin.'
        if (isProcedureOnly && !state.preProcedureName?.trim() && !state.postProcedureName?.trim())
          return 'Sadece Prosedür modunda en az bir prosedür (Öncesi veya Sonrası) tanımlayın.'
        return null
      default: return null
    }
  }, [step, state, isProcedureOnly])

  const goPrev = useCallback(() => setStep(s => Math.max(1, s - 1)), [])

  /**
   * goNext — adım ilerletmeden önce 3 katmanlı koruma:
   *   1) Aktif input/select/textarea'yı blur et → pending onChange commit
   *   2) Açık SearchableCombo portal menülerini DOM'dan kaldır → eski stale render kalmasın
   *   3) Step 2 (Mapping)'te TargetPath BOŞ olan satırları otomatik temizle (kullanıcı
   *      "Alan Ekle" basıp doldurmadıysa boş kayıt mapping'e yansımasın)
   * Bu sayede kullanıcı dropdown açıkken/input'ta yazarken İleri'ye basınca veri kaybı olmaz.
   */
  const goNext = useCallback(() => {
    // 1) Aktif element blur — pending onChange commit
    if (typeof document !== 'undefined' && document.activeElement
        && typeof document.activeElement.blur === 'function') {
      try { document.activeElement.blur() } catch { /* */ }
    }
    // 2) Açık portal menülerini kaldır (SearchableCombo, JsonTree vb.)
    if (typeof document !== 'undefined') {
      document.querySelectorAll('[data-sc-menu]').forEach(el => el.remove())
    }
    // 3) Mapping'de boş satırları temizle (Step 2 / yeni numara — mapping editor)
    if (step === 2 && state.mappings?.length > 0) {
      const cleaned = state.mappings.filter(m =>
        (m.targetPath || '').trim().length > 0)
      if (cleaned.length !== state.mappings.length) {
        update({ mappings: cleaned })
      }
    }
    // Microtask gecikme → React batch'i yukarıdaki update'leri commit etsin, sonra step ilerle
    setTimeout(() => setStep(s => Math.min(STEPS.length, s + 1)), 0)
  }, [step, state, update])

  // Klavye kısayolları:
  //   Ctrl/Cmd + ← : Geri
  //   Ctrl/Cmd + → : İleri (validation gecerse)
  //   Ctrl/Cmd + S : Step 5'te ise direkt kaydet, değilse en son adıma git
  // Input/textarea içindeyken ← → tetiklenmemeli (default davranış korunur)
  useEffect(() => {
    const onKey = (e) => {
      if (!e.ctrlKey && !e.metaKey) return
      const tag = (e.target?.tagName || '').toLowerCase()
      const isInput = tag === 'input' || tag === 'textarea' || e.target?.isContentEditable
      if (e.key === 'ArrowLeft') {
        if (isInput) return  // text caret movement korunur
        e.preventDefault()
        if (step > 1 && !saving) goPrev()
      } else if (e.key === 'ArrowRight') {
        if (isInput) return
        e.preventDefault()
        if (step < STEPS.length && canGoNext() && !saving) goNext()
      } else if (e.key === 's' || e.key === 'S') {
        e.preventDefault()
        if (step === STEPS.length && state.name.trim() && !saving) save()
        else if (step < STEPS.length) toast(`Kaydetmek için Step ${STEPS.length}'e ilerleyin`, 'info')
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [step, state, saving, canGoNext, goPrev, goNext])

  const save = async () => {
    if (!state.name.trim()) {
      toast('Ad zorunlu — son adım (Yayına Al)', 'err')
      setStep(STEPS.length)
      return
    }
    setSaving(true)
    try {
      const r = await fetch(`${apiBase}/save`, {
        method: 'POST', credentials: 'same-origin',
        headers: {
          'Content-Type': 'application/json',
          RequestVerificationToken: getCsrf(),
        },
        body: JSON.stringify({
          id: state.id,
          name: state.name.trim(),
          description: state.description?.trim() || null,
          sourceFormCode: state.sourceFormCode,
          targetEndpointId: state.targetEndpointId,
          errorBehavior: state.errorBehavior,
          retryCount: state.retryCount,
          isActive: state.isActive,
          mappings: state.mappings,
          triggers: state.triggers,
          preProcedureName:        state.preProcedureName || null,
          preProcedureParamsJson:  state.preProcedureParamsJson || null,
          postProcedureName:       state.postProcedureName || null,
          postProcedureParamsJson: state.postProcedureParamsJson || null,
          // 2026-05-22 Pre-flight Filter — kaydet (NULL ise filtre yok = tüm kayıtlar geçer)
          sourceFilterJson:        state.sourceFilterJson || null,
          // 2026-05-22 Cascade — bu integration cascade hedefi olarak görünür mü
          allowAsCascadeTarget:    state.allowAsCascadeTarget !== false,
          sourceCodeColumn:        state.sourceCodeColumn || null,
        }),
      })
      const d = await r.json()
      if (d.success) {
        clearDraft()                      // basarili kayittan sonra draft sil
        toast('Kaydedildi', 'ok')
        setTimeout(() => { window.location.href = listUrl }, 600)
      } else {
        toast(d.error || 'Kayıt hatası', 'err')
      }
    } catch (e) {
      toast('Sunucu hatası: ' + e.message, 'err')
    } finally {
      setSaving(false)
    }
  }

  if (loading) {
    return (
      <div className="iw-root" style={{ alignItems: 'center', justifyContent: 'center' }}>
        <Loader2 className="iw-spin" size={32} />
        <span style={{ marginTop: 8, color: 'var(--iw-muted)', fontSize: 13 }}>Yükleniyor…</span>
      </div>
    )
  }

  // Tamamlanma yuzdesi: tamamlanan step / toplam (step 5'i kaydedince %100)
  const completedSteps = step - 1   // step 1 acikken 0 step tamamlanmis
  const progressPct = Math.round((completedSteps / STEPS.length) * 100)

  return (
    <div className="iw-root">
      {/* Draft restored banner — yeni mode'da sessionStorage'dan devam ediliyor */}
      {draftRestored && (
        <div style={{
          padding: '8px 16px', flexShrink: 0,
          background: 'var(--iw-amber-bg)', borderBottom: '1px solid var(--iw-amber-color)',
          color: 'var(--iw-amber-color)', fontSize: 12,
          display: 'flex', alignItems: 'center', gap: 8,
        }}>
          <span>📂 Yarım kalan taslak yüklendi. Devam edebilir veya sıfırlayabilirsiniz.</span>
          <span style={{ flex: 1 }} />
          <button onClick={() => { clearDraft(); setState(emptyState()); setDraftRestored(false); setStep(1) }}
                  className="iw-btn-ghost" style={{ padding: '3px 10px', fontSize: 11 }}>
            Sıfırla
          </button>
          <button onClick={() => setDraftRestored(false)}
                  className="iw-btn-ghost" style={{ padding: '3px 10px', fontSize: 11 }}>
            ✕
          </button>
        </div>
      )}

      {/* Progress bar (üstte sabit) */}
      <div style={{
        height: 3, flexShrink: 0,
        background: 'var(--iw-border)',
      }}>
        <div style={{
          width: `${progressPct}%`, height: '100%',
          background: 'linear-gradient(90deg, var(--iw-indigo-color), #a78bfa)',
          transition: 'width .3s ease',
        }} />
      </div>

      {/* Stepper — eylem-odakli etiket + hint tooltip */}
      <div className="iw-stepper">
        {STEPS.map(s => (
          <button key={s.num}
                  type="button"
                  onClick={() => setStep(s.num)}
                  disabled={s.num > step && !canGoNext()}
                  title={s.num > step
                    ? `${s.label} — ${s.hint}\n\n(Önce mevcut adımı tamamlayın)`
                    : `${s.label} — ${s.hint}`}
                  className={'iw-step ' +
                             (step === s.num ? 'is-active ' : '') +
                             (step > s.num ? 'is-done' : '')}
                  style={{
                    background: 'transparent', border: 'none',
                    cursor: (s.num <= step || canGoNext()) ? 'pointer' : 'not-allowed',
                  }}>
            <div className="iw-step__num">
              {step > s.num ? <Check size={14} /> : s.num}
            </div>
            <div className="iw-step__label">{s.label}</div>
          </button>
        ))}
      </div>

      {/* Context bar — kalici (her zaman gorunur). Form picker burada (eski Step 1 kaldirildi) */}
      <ContextBar state={state} update={update} apiBase={apiBase} />

      {/* Body — step rendering. Eski Step 1 (Veri Kaynagi) kaldirildi; form secimi
           ContextBar'da kompakt combobox. Step numaralari 1 kaydirildi. */}
      <div className="iw-body">
        {step === 1 && <WizardStep2Endpoint apiBase={apiBase} state={state} update={update} />}
        {step === 2 && <WizardStep3Mapping  apiBase={apiBase} state={state} update={update} />}
        {step === 3 && <WizardStepFilters   apiBase={apiBase} state={state} update={update} />}
        {step === 4 && <WizardStep4Preview  apiBase={apiBase} state={state} />}
        {step === 5 && <WizardStep5Trigger  state={state} update={update} apiBase={apiBase} />}
      </div>

      {/* Footer — kompakt: solda Vazgec, ortada step yureyleri, sagda navigasyon */}
      <div className="iw-footer">
        <a href={listUrl} className="iw-btn-ghost" title="İptal et — değişiklikler kaydedilmez">
          Vazgeç
        </a>
        <div className="iw-footer-spacer" />
        <span style={{ fontSize: 11, color: 'var(--iw-muted)', marginRight: 16 }}
              title="Klavye: Ctrl+← Geri · Ctrl+→ İleri · Ctrl+S Kaydet">
          {step} / {STEPS.length}
        </span>
        {step > 1 && (
          <button className="iw-btn-secondary" onClick={goPrev} disabled={saving}
                  title="Geri (Ctrl+←)">
            <ArrowLeft size={14} /> Geri
          </button>
        )}
        {step < STEPS.length && (() => {
          const reason = nextDisabledReason()
          return (
            <button className="iw-btn-primary" onClick={goNext} disabled={!canGoNext() || saving}
                    title={reason ?? 'İleri (Ctrl+→)'}>
              İleri <ArrowRight size={14} />
            </button>
          )
        })()}
        {step === STEPS.length && (() => {
          const reason = state.name?.trim() ? null : 'Entegrasyona bir ad verin'
          return (
            <button className="iw-btn-primary" onClick={save} disabled={saving || !state.name.trim()}
                    title={reason ?? 'Kaydet (Ctrl+S)'}>
              {saving ? <><Loader2 className="iw-spin" size={14} /> Kaydediliyor</> : <><Save size={14} /> Kaydet</>}
            </button>
          )
        })()}
      </div>
    </div>
  )
}

/**
 * ContextBar — wizard'in tamami boyunca secilen kaynak ve hedefi gosterir.
 * Step 2'den itibaren gorunur (sourceFormCode set edildikten sonra).
 * Kullanici "ben hangi entegrasyonun parcasiyim" sorusuna her an cevap bulur.
 *
 * Layout:  📄 SALES_ORDER_EDIT  ─→  🌐 Netsis ItemSlips  ·  12 alan eslesti
 */
// ── Form picker meta — Alan Rehberi (ModuleSelector) pattern'iyle uyumlu ────
// Suffix'ler ('— Üst Bilgi' / '— Kalem Bilgisi') KALDIRILDI — entegrasyon wizard'da
// kalem formları zaten gizli (backend filter), kullanıcıya kısa form adı yeter.
const WIZ_FORM_META = {
  ITEMS:            { label: 'Malzeme Kartları',  icon: 'Box',           color: 'indigo' },
  CONTACTS:         { label: 'Cari Hesaplar',     icon: 'Building2',     color: 'cyan'   },
  SALES_REPS:       { label: 'Satış Temsilcisi',  icon: 'Users',         color: 'cyan'   },
  SALES_QUOTE_EDIT: { label: 'Satış Teklifi',     icon: 'FileText',      color: 'violet' },
  SALES_ORDER_EDIT: { label: 'Satış Siparişi',    icon: 'ShoppingCart',  color: 'emerald'},
  PRODUCT_TREES:    { label: 'Ürün Ağacı',        icon: 'GitBranch',     color: 'emerald'},
  WORK_ORDER_EDIT:  { label: 'İş Emirleri',       icon: 'ClipboardList', color: 'rose'   },
  OPERATION_EDIT:   { label: 'Operasyon',         icon: 'Hammer',        color: 'indigo' },
  ROUTING_EDIT:     { label: 'Rota',              icon: 'Workflow',      color: 'indigo' },
  PERSONNEL_EDIT:   { label: 'Personel',          icon: 'Users',         color: 'indigo' },
  MACHINES:         { label: 'Makineler',         icon: 'Cog',           color: 'slate'  },
  PRODUCT_CONFIG:   { label: 'Ürün Konfigürasyonu', icon: 'Sliders',     color: 'teal'   },
  EINVOICE:         { label: 'e-Fatura',          icon: 'FileText',      color: 'amber'  },
  EARCHIVE:         { label: 'e-Arşiv',           icon: 'FileText',      color: 'amber'  },
  EDISPATCH:        { label: 'e-İrsaliye',        icon: 'Truck',         color: 'amber'  },
}

const ICON_BG = {
  indigo:  { bg: 'rgba(99,102,241,.12)',  border: 'rgba(99,102,241,.30)',  icon: '#6366f1' },
  cyan:    { bg: 'rgba(6,182,212,.12)',   border: 'rgba(6,182,212,.30)',   icon: '#06b6d4' },
  violet:  { bg: 'rgba(139,92,246,.12)',  border: 'rgba(139,92,246,.30)',  icon: '#8b5cf6' },
  emerald: { bg: 'rgba(16,185,129,.12)',  border: 'rgba(16,185,129,.30)',  icon: '#10b981' },
  rose:    { bg: 'rgba(244,63,94,.12)',   border: 'rgba(244,63,94,.30)',   icon: '#f43f5e' },
  amber:   { bg: 'rgba(245,158,11,.12)',  border: 'rgba(245,158,11,.30)',  icon: '#f59e0b' },
  slate:   { bg: 'rgba(100,116,139,.15)', border: 'rgba(100,116,139,.30)', icon: '#64748b' },
  teal:    { bg: 'rgba(20,184,166,.12)',  border: 'rgba(20,184,166,.30)',  icon: '#14b8a6' },
}

function FormPicker({ forms, value, onChange, locked = false, lockReason = null }) {
  const [open, setOpen] = useState(false)
  const wrapRef = useRef(null)
  // FormPickerMenu portal'da render edildigi icin wrapRef.contains() menu icini
  // yakalayamaz. Ayri bir ref ile menu icini takip et — outside-click handler
  // hem button hem de menu icini hesaba katar (aksi halde menu item'ina mousedown
  // gelince menu kapaniyor ve click hic firmiyor).
  const menuRef = useRef(null)

  useEffect(() => {
    if (!open) return
    const onDoc = (e) => {
      const t = e.target
      if (wrapRef.current && wrapRef.current.contains(t)) return
      if (menuRef.current && menuRef.current.contains(t)) return
      setOpen(false)
    }
    document.addEventListener('mousedown', onDoc)
    return () => document.removeEventListener('mousedown', onDoc)
  }, [open])

  // 2026-05-22: Label öncelik sırası — kürateli liste > entity adı > rol adı > kod.
  // Forms tablosundaki formName "Liste" / "Üst Bilgi" gibi alt-form rolüdür; entity
  // adı (Satış Siparişi, Cari Hesap…) subModule kolonunda. Bu yüzden subModule önce
  // gelir, formName en son fallback. Hardcoded WIZ_FORM_META olan core formlar
  // (CONTACTS, SALES_ORDER_EDIT vb.) zaten en doğru ad burada — onu koru.
  const meta = (code) => {
    const apiForm = forms.find(f => f.formCode === code)
    const base = WIZ_FORM_META[code]
    if (base) return base   // kürateli kayıt — direkt kullan (label + icon + color)
    return {
      label: apiForm?.subModule || apiForm?.formName || code,
      icon:  'FileText',
      color: 'slate',
    }
  }
  const selMeta = meta(value)
  const selPal  = ICON_BG[selMeta.color] || ICON_BG.slate

  // 2026-05-22: Form değişikliği invasive (mapping, endpoint, filter, cascade hepsi
  // invalidate olur). Lock edilince button click+dropdown devre dışı, küçük kilit ikonu
  // ve tooltip ile sebep gösterilir. Lock şartı ContextBar tarafında hesaplanır.
  const handleClick = () => {
    if (locked) return   // sessizce ignore — cursor zaten "not-allowed" gösteriyor
    setOpen(o => !o)
  }

  return (
    <div ref={wrapRef} style={{ position: 'relative', minWidth: 240 }}>
      <button type="button"
              onClick={handleClick}
              style={{
                width: '100%', display: 'flex', alignItems: 'center', gap: 8,
                padding: '5px 10px',
                border: '1px solid ' + (value ? 'var(--iw-border)' : 'var(--iw-amber-color)'),
                borderRadius: 6, fontSize: 12, fontWeight: 600,
                background: locked ? 'var(--iw-slate-bg)' : 'var(--iw-surface)',
                color: 'var(--iw-text)',
                cursor: locked ? 'not-allowed' : 'pointer',
                opacity: locked ? 0.85 : 1,
              }}
              title={locked
                ? (lockReason || 'Kaynak form kilitli — değiştirmek isterseniz wizard\'ı sıfırlayın.')
                : (value ? 'Kaynak form — değiştirmek için tıkla' : 'Önce bir form seç')}>
        {value ? (
          <>
            <span style={{
              width: 22, height: 22, borderRadius: 5,
              display: 'flex', alignItems: 'center', justifyContent: 'center',
              background: selPal.bg, border: '1px solid ' + selPal.border, color: selPal.icon,
              fontSize: 11, fontWeight: 700,
            }}>
              {selMeta.label.charAt(0)}
            </span>
            <span style={{ flex: 1, textAlign: 'left' }}>{selMeta.label}</span>
          </>
        ) : (
          <span style={{ flex: 1, textAlign: 'left', color: 'var(--iw-amber-color)' }}>
            — Form seç —
          </span>
        )}
        {/* Locked durumda chevron kaldirilir — disabled goruntu yeterli (cursor: not-allowed
            + dim background). Lock sebebi tooltip'te zaten gosteriliyor. */}
        {!locked && (
          <ChevronDown size={12} style={{
            color: 'var(--iw-muted)',
            transform: open ? 'rotate(180deg)' : 'none',
            transition: 'transform .15s',
          }} />
        )}
      </button>

      {open && createPortal(
        <FormPickerMenu wrapRef={wrapRef} menuRef={menuRef} forms={forms}
          value={value}
          onPick={(code) => { onChange(code); setOpen(false) }}
          meta={meta} />,
        document.body
      )}
    </div>
  )
}

function FormPickerMenu({ wrapRef, menuRef, forms, value, onPick, meta }) {
  // Position: anchor altında
  const [pos, setPos] = useState({ left: 0, top: 0, width: 240 })
  useEffect(() => {
    if (!wrapRef.current) return
    const r = wrapRef.current.getBoundingClientRect()
    setPos({ left: r.left, top: r.bottom + 4, width: Math.max(r.width, 280) })
  }, [wrapRef])

  return (
    <div ref={menuRef} style={{
      position: 'fixed', left: pos.left, top: pos.top, width: pos.width,
      maxHeight: 380, overflowY: 'auto', zIndex: 9999,
      borderRadius: 8, border: '1px solid var(--iw-border)',
      background: 'var(--iw-surface)',
      boxShadow: '0 14px 40px rgba(0,0,0,0.45)',
      backdropFilter: 'blur(14px)',
    }}>
      {forms.length === 0 && (
        <div style={{ padding: '14px 16px', fontSize: 12, color: 'var(--iw-muted)', textAlign: 'center' }}>
          Form bulunamadı
        </div>
      )}
      {forms.map(f => {
        const m = meta(f.formCode)
        const pal = ICON_BG[m.color] || ICON_BG.slate
        const isSel = f.formCode === value
        return (
          <button key={f.formCode}
                  type="button"
                  onClick={() => onPick(f.formCode)}
                  style={{
                    width: '100%', display: 'flex', alignItems: 'center', gap: 10,
                    padding: '8px 12px',
                    background: isSel ? 'var(--iw-indigo-bg)' : 'transparent',
                    border: 'none', borderBottom: '1px solid var(--iw-border)',
                    cursor: 'pointer', textAlign: 'left',
                  }}
                  onMouseEnter={e => { if (!isSel) e.currentTarget.style.background = 'var(--iw-hover)' }}
                  onMouseLeave={e => { if (!isSel) e.currentTarget.style.background = 'transparent' }}>
            <span style={{
              width: 26, height: 26, borderRadius: 6,
              display: 'flex', alignItems: 'center', justifyContent: 'center',
              background: pal.bg, border: '1px solid ' + pal.border, color: pal.icon,
              fontSize: 12, fontWeight: 700, flexShrink: 0,
            }}>
              {m.label.charAt(0)}
            </span>
            <span style={{ flex: 1, fontSize: 13, fontWeight: 600, color: 'var(--iw-text)' }}>
              {m.label}
            </span>
            {isSel && <Check size={14} style={{ color: 'var(--iw-indigo-color)' }} />}
          </button>
        )
      })}
    </div>
  )
}

function ContextBar({ state, update, apiBase }) {
  const [endpointName, setEndpointName] = useState(null)
  const [forms, setForms] = useState([])

  // Tum formlari bir kere cek — combobox icin
  useEffect(() => {
    let cancelled = false
    fetch(`${apiBase}/forms`, { credentials: 'same-origin' })
      .then(r => r.json())
      .then(d => {
        if (cancelled) return
        if (d.success) setForms(d.forms || [])
      })
      .catch(() => {})
    return () => { cancelled = true }
  }, [apiBase])

  // Endpoint adi (chip icin)
  useEffect(() => {
    if (!state.targetEndpointId) { setEndpointName(null); return }
    let cancelled = false
    fetch(`/Integrations/api/endpoints/${state.targetEndpointId}`, { credentials: 'same-origin' })
      .then(r => r.json())
      .then(d => {
        if (cancelled || !d.success || !d.endpoint) return
        const ep = d.endpoint
        setEndpointName(`${ep.name} (${ep.httpMethod} ${ep.urlTemplate})`)
      })
      .catch(() => {})
    return () => { cancelled = true }
  }, [state.targetEndpointId])

  return (
    <div style={{
      flexShrink: 0,
      padding: '8px 16px',
      borderBottom: '1px solid var(--iw-border)',
      background: 'var(--iw-bg)',
      display: 'flex', alignItems: 'center', gap: 10,
      fontSize: 12, color: 'var(--iw-muted)',
      whiteSpace: 'nowrap',
    }}>
      {/* Form picker — Alan Rehberi (ModuleSelector) tarzı custom dropdown.
          2026-05-22: Form değişikliği invasive (mapping/endpoint/filter/cascade
          hepsi invalidate olur). Bu yüzden lock:
            • Edit mode (state.id > 0)          → mevcut integration, dokunma
            • Mapping eklenmişse                → değişirse mapping'ler ölü kalır
            • Endpoint seçilmişse               → form değişirse endpoint uyumsuz
            • Filter (Kısıt Kuralları) yazılmışsa → field'lar form'a özel
          Tek serbest hâl: yepyeni wizard'ın ilk adımı. */}
      {(() => {
        const isEdit = state.id > 0
        const hasMappings = (state.mappings?.length || 0) > 0
        const hasEndpoint = !!state.targetEndpointId
        const hasFilter = !!state.sourceFilterJson
        const locked = !!state.sourceFormCode && (isEdit || hasMappings || hasEndpoint || hasFilter)
        const lockReason = isEdit
          ? 'Mevcut entegrasyon — form değiştirilemez. Yeni form için yeni entegrasyon oluşturun.'
          : hasMappings
            ? `Form değişirse ${state.mappings.length} mapping geçersiz kalır. Önce mapping\'leri silin.`
            : hasEndpoint
              ? 'Form değişirse endpoint uyumsuz kalabilir. Önce endpoint seçimini kaldırın.'
              : hasFilter
                ? 'Form değişirse filtre kuralları geçersiz kalır. Önce Kısıt Kuralları\'nı temizleyin.'
                : null
        return (
          <FormPicker forms={forms} value={state.sourceFormCode}
                      onChange={code => update({ sourceFormCode: code })}
                      locked={locked}
                      lockReason={lockReason} />
        )
      })()}

      {/* Step-specific actions — Step 3 (mapping) toolbar portals into this slot */}
      <span id="iw-step-actions-portal" style={{ display: 'contents' }} />

      {/* Sadece Prosedür switch — dikey stack: switch üstte + label altta (kompakt) */}
      <span style={{ flex: 1 }} />
      <button type="button"
              onClick={() => {
                if (state.procedureOnlyMode) {
                  // Toggle OFF — cache'lenmis endpoint'i geri yukle (varsa)
                  update({
                    procedureOnlyMode: false,
                    targetEndpointId: state._cachedEndpointId || state.targetEndpointId,
                  })
                } else {
                  // Toggle ON — mevcut endpoint'i cache'le, sonra null yap
                  update({
                    procedureOnlyMode: true,
                    _cachedEndpointId: state.targetEndpointId,
                    targetEndpointId: null,
                  })
                }
              }}
              title={state.procedureOnlyMode
                ? 'Sadece Prosedür modu AÇIK — HTTP çağrısı yok, mapping atlanır'
                : 'Açarsan: HTTP çağrısı yapmadan sadece SQL prosedürü çalıştırır (cron/OnSave)'}
              style={{
                display: 'inline-flex', alignItems: 'center', gap: 5,
                padding: '4px 10px', borderRadius: 6, fontSize: 11, fontWeight: 600,
                cursor: 'pointer',
                border: '1px solid ' + (state.procedureOnlyMode ? 'var(--iw-emerald-color)' : 'var(--iw-border)'),
                background: state.procedureOnlyMode ? 'var(--iw-emerald-bg)' : 'transparent',
                color: state.procedureOnlyMode ? 'var(--iw-emerald-color)' : 'var(--iw-muted)',
              }}>
        ⚙ Sadece Prosedür
      </button>
      {state.preProcedureName && (
        <span title="Öncesi prosedür" style={{ marginLeft: 12, color: 'var(--iw-amber-color)' }}>
          ↪ {state.preProcedureName}
        </span>
      )}
      {state.postProcedureName && (
        <span title="Sonrası prosedür" style={{ marginLeft: 12, color: 'var(--iw-emerald-color)' }}>
          ↩ {state.postProcedureName}
        </span>
      )}
    </div>
  )
}
