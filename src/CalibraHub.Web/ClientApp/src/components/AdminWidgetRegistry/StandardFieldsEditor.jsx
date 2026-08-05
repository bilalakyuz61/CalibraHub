/**
 * StandardFieldsEditor — Form Davranış Katmanı editörü (2026-08-05).
 *
 * Üst bilgi (header) STANDART alanlarının davranışını yönetir: Görünür / Zorunlu /
 * Varsayılan Değer / Başlık Metni + Stili / koşullu kurallar (visibleIf, requiredIf)
 * + sol sekmelerin görünürlük / sıra / ad yönetimi.
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
  AlertTriangle,
} from 'lucide-react'
import { getTopBody } from '../../utils/topPortal'

function readCsrfToken() {
  try {
    var input = document.querySelector('input[name="__RequestVerificationToken"]')
    if (input && input.value) return input.value
    var shellCfg = window.__CALIBRA_SHELL_CONFIG__
    if (shellCfg && shellCfg.antiforgeryToken) return shellCfg.antiforgeryToken
    return ''
  } catch (e) { return '' }
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

var inputCls = 'w-full px-2 py-1 rounded-md text-[11.5px] border border-slate-200 bg-white text-slate-700 ' +
  'placeholder:text-slate-300 focus:outline-none focus:ring-1 focus:ring-indigo-400 ' +
  'dark:border-white/10 dark:bg-white/[0.04] dark:text-white/85 dark:placeholder:text-white/25'
var ruleInputCls = inputCls + ' font-mono'

export default function StandardFieldsEditor(props) {
  var formCode = props.formCode
  var onClose = props.onClose
  var onSaved = props.onSaved

  var [loading, setLoading] = useState(true)
  var [error, setError] = useState(null)
  var [saving, setSaving] = useState(false)
  var [fields, setFields] = useState([])
  var [tabs, setTabs] = useState([])

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
        setFields((data.fields || []).map(function (f) {
          return {
            key: f.key, label: f.label, tab: f.tab, dataType: f.dataType, locked: f.locked === true,
            isVisible: f.isVisible !== false,
            isRequired: f.isRequired === true,
            defaultValue: f.defaultValue || '',
            labelText: f.labelText || '',
            labelStyle: f.labelStyle || '',
            visibleIf: f.visibleIf || '',
            requiredIf: f.requiredIf || '',
          }
        }))
        setTabs((data.tabs || []).map(function (t) {
          return {
            key: t.key, label: t.label, locked: t.locked === true,
            isVisible: t.isVisible !== false,
            labelText: t.labelText || '',
          }
        }))
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

  // Sekme key'ine göre alanları grupla (katalog sırası korunur)
  var tabOrder = ['general', 'lines', 'conditions', 'notes']
  var tabLabels = { general: 'Genel Bilgiler', lines: 'Kalem Bilgileri', conditions: 'Koşullar', notes: 'Notlar' }
  var scopeKeys = fields.map(function (f) { return f.key }).join(', ')

  return createPortal(
    <div
      onClick={function (e) { if (e.target === e.currentTarget && !saving) onClose() }}
      onKeyDown={function (e) { if (e.key === 'Escape' && !saving) onClose() }}
      className="fixed inset-0 z-[60] flex items-center justify-center p-4"
      style={{ background: 'rgba(15,23,42,0.45)', backdropFilter: 'blur(5px)', WebkitBackdropFilter: 'blur(5px)' }}
    >
      <div
        className="w-full max-w-[1060px] max-h-[90vh] flex flex-col overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-2xl dark:border-white/10 dark:bg-slate-900"
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
            <div className="text-[11px] text-slate-500 dark:text-white/45">
              Görünürlük, zorunluluk, varsayılan değer, başlık ve koşullu kurallar bu formun tüm kullanıcıları için geçerlidir.
            </div>
          </div>
          <button
            type="button"
            onClick={function () { if (!saving) onClose() }}
            className="w-8 h-8 rounded-lg flex items-center justify-center text-slate-400 hover:text-rose-600 hover:bg-rose-50 dark:text-white/40 dark:hover:text-rose-300 dark:hover:bg-rose-500/10 transition-colors"
            title="Kapat (Esc)"
          >
            <XIcon size={15} strokeWidth={2} />
          </button>
        </div>

        {/* Body */}
        <div className="flex-1 min-h-0 overflow-y-auto px-5 py-4">
          {loading && (
            <div className="py-10 text-center text-[12px] text-slate-400 dark:text-white/35">Yükleniyor…</div>
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
                            className="text-slate-400 hover:text-indigo-600 dark:text-white/35 dark:hover:text-indigo-300 disabled:opacity-25">
                            <ArrowUp size={11} strokeWidth={2.2} />
                          </button>
                          <button type="button" onClick={function () { moveTab(idx, 1) }} disabled={idx === tabs.length - 1}
                            className="text-slate-400 hover:text-indigo-600 dark:text-white/35 dark:hover:text-indigo-300 disabled:opacity-25">
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
                          <span title="Bu sekme gizlenemez"><Lock size={12} className="text-slate-300 dark:text-white/25" /></span>
                        ) : (
                          <button
                            type="button"
                            onClick={function () {
                              setTabs(function (prev) { return prev.map(function (x) { return x.key === t.key ? Object.assign({}, x, { isVisible: !x.isVisible }) : x }) })
                            }}
                            title={t.isVisible ? 'Sekmeyi gizle' : 'Sekmeyi göster'}
                            className="text-slate-400 hover:text-indigo-600 dark:text-white/40 dark:hover:text-indigo-300"
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

              {/* ── Alanlar (sekmeye göre gruplu) ── */}
              {tabOrder.map(function (tabKey) {
                var group = fields.filter(function (f) { return f.tab === tabKey })
                if (group.length === 0) return null
                return (
                  <div key={tabKey} className="mb-4">
                    <div className="text-[11.5px] font-bold text-slate-700 dark:text-white/80 mb-1.5">
                      {tabLabels[tabKey] || tabKey}
                    </div>
                    {/* Kolon başlıkları */}
                    <div className="hidden lg:grid grid-cols-[170px_52px_52px_1fr_1fr_100px_1fr_1fr] gap-2 px-2.5 pb-1 text-[9.5px] font-semibold text-slate-400 dark:text-white/35">
                      <span>Alan</span><span>Görünür</span><span>Zorunlu</span>
                      <span>Varsayılan Değer</span><span>Başlık Metni</span><span>Başlık Stili</span>
                      <span>Görünürlük Koşulu</span><span>Zorunluluk Koşulu</span>
                    </div>
                    <div className="flex flex-col gap-1">
                      {group.map(function (f) {
                        return (
                          <div key={f.key} className="grid grid-cols-2 lg:grid-cols-[170px_52px_52px_1fr_1fr_100px_1fr_1fr] gap-2 items-center rounded-lg border border-slate-200 bg-slate-50/60 dark:border-white/10 dark:bg-white/[0.03] px-2.5 py-1.5">
                            <div className="flex items-center gap-1.5 min-w-0">
                              {f.locked && <span title="Çekirdek alan — gizlenemez"><Lock size={11} className="text-slate-300 dark:text-white/25 flex-shrink-0" /></span>}
                              <span className="truncate text-[11.5px] font-semibold text-slate-600 dark:text-white/70" title={f.key}>{f.label}</span>
                            </div>
                            <Switch
                              on={f.isVisible}
                              disabled={f.locked}
                              color="bg-emerald-500/70"
                              title={f.locked ? 'Çekirdek alan — gizlenemez' : 'Görünür'}
                              onToggle={function () { patchField(f.key, { isVisible: !f.isVisible }) }}
                            />
                            <Switch
                              on={f.isRequired}
                              color="bg-red-500/70"
                              title="Zorunlu — boş bırakılırsa kayıt engellenir"
                              onToggle={function () { patchField(f.key, { isRequired: !f.isRequired }) }}
                            />
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
                        )
                      })}
                    </div>
                  </div>
                )
              })}

              <div className="text-[10.5px] text-slate-400 dark:text-white/35">
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
            className="px-3.5 py-1.5 rounded-lg text-[12px] font-semibold border transition-colors bg-white text-slate-600 border-slate-200 hover:bg-slate-100 dark:bg-white/[0.04] dark:text-white/70 dark:border-white/10 dark:hover:bg-white/[0.08]"
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
    getTopBody()
  )
}
