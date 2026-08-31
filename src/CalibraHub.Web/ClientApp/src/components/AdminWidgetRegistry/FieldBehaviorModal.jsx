/**
 * Alan Davranışı modalı (2026-08-20 — kullanıcı isteği).
 *
 * Standart Alanlar ekranındaki satırlar üç serbest metin kutusu taşıyordu
 * (Varsayılan Değer / Görünürlük Koşulu / Zorunluluk Koşulu). Koşullar NCalc
 * ifadesi olduğu için kullanıcı sözdizimini ezberlemek zorundaydı ve satırlar
 * çok kalabalıktı. Artık satırda tek "Ayarla" butonu var; üçü de bu modalden
 * tanımlanıyor.
 *
 * Koşullar için HİBRİT yaklaşım (projedeki ViewBuilder deseniyle aynı):
 *   - Kurucu (varsayılan): [Alan] [Operatör] [Değer] satırları, VE/VEYA ile bağlanır.
 *   - Gelişmiş: ham ifade kutusu. Mevcut ifade kurucuya çözülemiyorsa (parantez,
 *     bilinmeyen kalıp) otomatik olarak bu moda düşer ve ifade AYNEN korunur —
 *     kullanıcının elle yazdığı kural asla sessizce kaybolmaz.
 *
 * Üretilen ifade sunucuda RuleExpr.Sanitize süzgecinden geçer: yalnız harf/rakam/
 * altçizgi, operatör, parantez, tırnak; en fazla 500 karakter. Kurucu bu sınırlar
 * içinde kalan ifade üretir, uzunluk da canlı gösterilir.
 */
import { useState, useEffect } from 'react'
import { createPortal } from 'react-dom'
import { X, Plus, Trash2, Code2, ListChecks } from 'lucide-react'

var MAX_RULE_LEN = 500   // RuleExpr.MaxRuleLength ile aynı

var OPS = [
  { v: '==', l: 'eşittir' },
  { v: '!=', l: 'eşit değildir' },
  { v: '>',  l: 'büyüktür' },
  { v: '>=', l: 'büyük / eşit' },
  { v: '<',  l: 'küçüktür' },
  { v: '<=', l: 'küçük / eşit' },
]

/** Değer literal mi (sayı / true / false) yoksa metin mi (tırnaklanmalı) */
function literalize(raw) {
  var v = String(raw == null ? '' : raw).trim()
  if (v === '') return "''"
  if (v === 'true' || v === 'false') return v
  if (/^-?\d+([.,]\d+)?$/.test(v)) return v.replace(',', '.')
  // Tek tırnak ifadeyi bozar → çift tırnağa çevir (Sanitize ikisine de izin verir)
  return "'" + v.replace(/'/g, '"') + "'"
}

/** Kurucudan ifade üret: satırlar join ile zincirlenir (soldan sağa). */
function buildExpr(rows) {
  var parts = []
  rows.forEach(function (r, i) {
    if (!r.field || !r.op) return
    var seg = r.field + ' ' + r.op + ' ' + literalize(r.value)
    parts.push(i === 0 ? seg : (r.join || '&&') + ' ' + seg)
  })
  return parts.join(' ').trim()
}

/**
 * Mevcut ifadeyi kurucu satırlarına çözmeye çalışır.
 * Çözülemezse null döner → çağıran ham moda düşer.
 */
function parseExpr(expr) {
  var s = String(expr == null ? '' : expr).trim()
  if (s === '') return []
  if (s.indexOf('(') >= 0 || s.indexOf(')') >= 0) return null   // parantezli → ham
  var tokens = s.split(/\s*(&&|\|\|)\s*/)
  var rows = []
  for (var i = 0; i < tokens.length; i += 2) {
    var m = /^\s*([A-Za-z_][A-Za-z0-9_]*)\s*(==|!=|>=|<=|>|<)\s*(.+?)\s*$/.exec(tokens[i])
    if (!m) return null
    var val = m[3].trim()
    if ((val.charAt(0) === "'" && val.slice(-1) === "'") ||
        (val.charAt(0) === '"' && val.slice(-1) === '"')) val = val.slice(1, -1)
    rows.push({ field: m[1], op: m[2], value: val, join: i === 0 ? null : tokens[i - 1] })
  }
  return rows
}

/** Tek koşul kurucusu — kurucu/ham mod anahtarı kendi içinde. */
function RuleBuilder(props) {
  var value = props.value            // ham ifade (string)
  var fields = props.fields || []
  var onChange = props.onChange
  var disabled = props.disabled === true

  var parsed = parseExpr(value)
  var [raw, setRaw] = useState(parsed === null)
  var rows = parsed || []

  function emit(nextRows) { onChange(buildExpr(nextRows)) }
  function patchRow(i, patch) {
    var next = rows.map(function (r, idx) { return idx === i ? Object.assign({}, r, patch) : r })
    emit(next)
  }
  function addRow() {
    var f = fields[0]
    emit(rows.concat([{ field: f ? f.key : '', op: '==', value: '', join: rows.length ? '&&' : null }]))
  }
  function removeRow(i) {
    var next = rows.filter(function (_, idx) { return idx !== i })
    if (next.length) next[0] = Object.assign({}, next[0], { join: null })
    emit(next)
  }

  var selCls = 'px-2 py-1 rounded-md text-[11.5px] border bg-[#fff] text-slate-700 border-slate-200 ' +
               'dark:bg-white/[0.04] dark:text-white/80 dark:border-white/10 outline-none'
  var txtCls = selCls + ' flex-1 min-w-0'

  return (
    <div className={disabled ? 'opacity-50 pointer-events-none' : ''}>
      <div className="flex items-center gap-1.5 mb-2">
        <button
          type="button"
          onClick={function () { setRaw(false) }}
          disabled={parsed === null}
          title={parsed === null ? 'Mevcut ifade kurucuya çözülemiyor — önce ham ifadeyi temizleyin' : 'Kurucu'}
          className={'flex items-center gap-1 px-2 py-0.5 rounded text-[10.5px] font-semibold border transition-colors ' + (
            !raw ? 'bg-indigo-50 text-indigo-600 border-indigo-200 dark:bg-indigo-500/15 dark:text-indigo-300 dark:border-indigo-400/30'
                 : 'text-slate-500 border-slate-200 dark:text-white/50 dark:border-white/10'
          ) + (parsed === null ? ' cursor-not-allowed opacity-50' : '')}
        >
          <ListChecks size={11} strokeWidth={2.2} /> Kurucu
        </button>
        <button
          type="button"
          onClick={function () { setRaw(true) }}
          className={'flex items-center gap-1 px-2 py-0.5 rounded text-[10.5px] font-semibold border transition-colors ' + (
            raw ? 'bg-indigo-50 text-indigo-600 border-indigo-200 dark:bg-indigo-500/15 dark:text-indigo-300 dark:border-indigo-400/30'
                : 'text-slate-500 border-slate-200 dark:text-white/50 dark:border-white/10'
          )}
        >
          <Code2 size={11} strokeWidth={2.2} /> Gelişmiş
        </button>
        <div className="flex-1" />
        <span className={'text-[10px] font-mono ' + (String(value || '').length > MAX_RULE_LEN
          ? 'text-rose-500 dark:text-rose-400' : 'text-slate-400 dark:text-white/35')}>
          {String(value || '').length}/{MAX_RULE_LEN}
        </span>
      </div>

      {raw ? (
        <textarea
          rows={3}
          value={value || ''}
          placeholder="örn: currency != 'TRY' && quantity > 0"
          onChange={function (e) { onChange(e.target.value) }}
          className={'w-full px-2 py-1.5 rounded-md text-[11.5px] font-mono border resize-none bg-[#fff] ' +
                     'text-slate-700 border-slate-200 dark:bg-white/[0.04] dark:text-white/80 dark:border-white/10 outline-none'}
        />
      ) : (
        <div className="flex flex-col gap-1.5">
          {rows.length === 0 && (
            <div className="text-[11px] text-slate-400 dark:text-white/35 py-1">
              Koşul yok — alan her zaman {props.emptyMeans || 'geçerli'}.
            </div>
          )}
          {rows.map(function (r, i) {
            return (
              <div key={i} className="flex items-center gap-1.5">
                {i > 0 ? (
                  <select value={r.join || '&&'} onChange={function (e) { patchRow(i, { join: e.target.value }) }}
                          className={selCls + ' w-[70px] flex-shrink-0'}>
                    <option value="&&">VE</option>
                    <option value="||">VEYA</option>
                  </select>
                ) : <span className="w-[70px] flex-shrink-0" />}
                <select value={r.field} onChange={function (e) { patchRow(i, { field: e.target.value }) }}
                        className={selCls + ' flex-1 min-w-0'}>
                  {fields.map(function (f) {
                    return <option key={f.key} value={f.key}>{f.label || f.key}</option>
                  })}
                </select>
                <select value={r.op} onChange={function (e) { patchRow(i, { op: e.target.value }) }}
                        className={selCls + ' w-[120px] flex-shrink-0'}>
                  {OPS.map(function (o) { return <option key={o.v} value={o.v}>{o.l}</option> })}
                </select>
                <input type="text" value={r.value} placeholder="değer"
                       onChange={function (e) { patchRow(i, { value: e.target.value }) }}
                       className={txtCls} />
                <button type="button" onClick={function () { removeRow(i) }} title="Koşulu kaldır"
                        className="flex-shrink-0 p-1 rounded text-slate-400 hover:text-rose-500 dark:text-white/35 dark:hover:text-rose-400">
                  <Trash2 size={13} strokeWidth={2} />
                </button>
              </div>
            )
          })}
          <div>
            <button type="button" onClick={addRow}
                    className="flex items-center gap-1 px-2 py-1 rounded-md text-[10.5px] font-semibold border text-indigo-600 border-indigo-200 bg-indigo-50 hover:bg-indigo-100 dark:text-indigo-300 dark:border-indigo-400/30 dark:bg-indigo-500/10 dark:hover:bg-indigo-500/20">
              <Plus size={11} strokeWidth={2.4} /> Koşul Ekle
            </button>
          </div>
        </div>
      )}

      {/* Üretilen ifade — kurucu modda ne kaydedileceği hep görünür kalsın */}
      {!raw && rows.length > 0 && (
        <div className="mt-2 px-2 py-1.5 rounded-md text-[10.5px] font-mono break-all bg-slate-50 text-slate-500 border border-slate-200 dark:bg-white/[0.03] dark:text-white/45 dark:border-white/10">
          {value}
        </div>
      )}
    </div>
  )
}

export default function FieldBehaviorModal(props) {
  var field = props.field           // { key, label, locked, defaultValue, visibleIf, requiredIf }
  var fields = props.fields || []   // koşullarda kullanılabilir alanlar [{key,label}]
  var onApply = props.onApply
  var onClose = props.onClose

  var [defaultValue, setDefaultValue] = useState('')
  var [visibleIf, setVisibleIf] = useState('')
  var [requiredIf, setRequiredIf] = useState('')
  // Alan kalem kartindan cikip Kalem Detayi modalinda mi cizilsin (yalniz kalem formlarinda).
  var [showInModal, setShowInModal] = useState(false)

  useEffect(function () {
    if (!field) return
    setDefaultValue(field.defaultValue || '')
    setVisibleIf(field.visibleIf || '')
    setRequiredIf(field.requiredIf || '')
    setShowInModal(field.showInModal === true)
  }, [field])

  useEffect(function () {
    function onKey(e) { if (e.key === 'Escape') onClose() }
    document.addEventListener('keydown', onKey)
    return function () { document.removeEventListener('keydown', onKey) }
  }, [onClose])

  if (!field) return null

  function apply() {
    onApply({
      defaultValue: defaultValue,
      visibleIf: visibleIf,
      requiredIf: requiredIf,
      showInModal: showInModal,
    })
    onClose()
  }

  var tooLong = visibleIf.length > MAX_RULE_LEN || requiredIf.length > MAX_RULE_LEN

  return createPortal(
    <div
      onClick={function (e) { if (e.target === e.currentTarget) onClose() }}
      className="fixed inset-0 z-[120] flex items-center justify-center p-4"
      style={{ background: 'rgba(15,23,42,0.55)', backdropFilter: 'blur(4px)', WebkitBackdropFilter: 'blur(4px)' }}
    >
      {/* CLAUDE.md "Modal boyut sabitliği": SABIT genislik + yukseklik, govde kendi
          icinde kaydirilir — kurucuya kosul eklendikce modal buyuyup kucululmez.
          bg-[#fff] (bg-white DEGIL): Bootstrap .bg-white{...!important} dark: varyantini yener. */}
      <div
        onClick={function (e) { e.stopPropagation() }}
        className="flex flex-col rounded-2xl border border-slate-200 bg-[#fff] shadow-2xl dark:border-white/10 dark:bg-[#171c2a] overflow-hidden"
        style={{ width: '100%', maxWidth: 'min(620px, calc(100vw - 48px))', height: 'min(560px, calc(100vh - 64px))' }}
      >
        <div className="flex items-start gap-3 px-5 py-4 border-b border-slate-200 dark:border-white/10 flex-shrink-0">
          <div className="min-w-0 flex-1">
            <div className="text-[14px] font-bold text-slate-800 dark:text-white truncate">
              {field.label || field.key} — Davranış
            </div>
            <div className="text-[11.5px] text-slate-500 dark:text-white/45 mt-0.5">
              Varsayılan değer, görünürlük ve zorunluluk koşulu bu formun tüm kullanıcıları için geçerlidir.
            </div>
          </div>
          <button type="button" onClick={onClose} title="Kapat"
                  className="flex-shrink-0 p-1 rounded-lg text-slate-400 hover:bg-slate-100 dark:text-white/40 dark:hover:bg-white/10">
            <X size={16} strokeWidth={2} />
          </button>
        </div>

        <div className="flex-1 min-h-0 overflow-y-auto px-5 py-4 flex flex-col gap-5">
          {/* Konum — yalniz KALEM formlarinda (*_LINES) anlamli; ust bilgi formunda
              "Kalem Detayi modali" diye bir yer yoktur, o yuzden hic cizilmez.
              Kilitli alanlarda kapali: malzeme kodu/miktar kartta gorunmeden satir
              girilemez (grid tarafinda da ayrica yok sayilir). */}
          {props.allowModalPlacement && (
            <div>
              <div className="text-[11px] font-bold tracking-wide text-slate-600 dark:text-white/60 mb-1.5">
                Konum
              </div>
              <label className={'flex items-center gap-2 text-[12px] ' + (field.locked ? 'opacity-45 cursor-not-allowed' : 'cursor-pointer')}>
                <button
                  type="button"
                  role="switch"
                  aria-checked={showInModal}
                  disabled={field.locked === true}
                  onClick={function () { if (!field.locked) setShowInModal(!showInModal) }}
                  className={'relative w-[34px] h-[19px] rounded-full border transition-colors flex-shrink-0 ' + (
                    showInModal ? 'bg-indigo-500 border-transparent'
                                : 'bg-slate-200 border-slate-300 dark:bg-white/15 dark:border-white/20')}
                >
                  <span
                    className="absolute top-[2px] w-[13px] h-[13px] rounded-full bg-[#fff] transition-all"
                    style={{ left: showInModal ? 17 : 2, boxShadow: '0 1px 2px rgba(0,0,0,.3)' }}
                  />
                </button>
                <span className="text-slate-600 dark:text-white/70">Kalem Detayı modalında göster</span>
              </label>
              <div className="text-[10.5px] text-slate-400 dark:text-white/35 mt-1">
                {field.locked
                  ? 'Çekirdek alan — kartta kalmak zorunda.'
                  : 'Açıkken alan kalem kartından çıkar, satırın ⋮ menüsündeki Kalem Detayı modalında görünür.'}
              </div>
            </div>
          )}

          <div>
            <div className="text-[11px] font-bold tracking-wide text-slate-600 dark:text-white/60 mb-1.5">
              Varsayılan Değer
            </div>
            <input
              type="text"
              value={defaultValue}
              disabled={!!field.locked}
              placeholder={field.locked ? '(kilitli alan — değiştirilemez)' : 'Yeni kayıtta alana yazılacak değer'}
              onChange={function (e) { setDefaultValue(e.target.value) }}
              className={'w-full px-2.5 py-1.5 rounded-md text-[12px] border bg-[#fff] text-slate-700 border-slate-200 ' +
                         'dark:bg-white/[0.04] dark:text-white/80 dark:border-white/10 outline-none' +
                         (field.locked ? ' opacity-50 cursor-not-allowed' : '')}
            />
            <div className="text-[10.5px] text-slate-400 dark:text-white/35 mt-1">
              Yalnızca YENİ kayıtta uygulanır; mevcut kayıtların değeri değişmez.
            </div>
          </div>

          <div>
            <div className="text-[11px] font-bold tracking-wide text-slate-600 dark:text-white/60 mb-1.5">
              Görünürlük Koşulu
            </div>
            <RuleBuilder value={visibleIf} fields={fields} disabled={!!field.locked}
                         emptyMeans="görünür"
                         onChange={function (v) { setVisibleIf(v) }} />
            <div className="text-[10.5px] text-slate-400 dark:text-white/35 mt-1">
              {field.locked
                ? 'Bu alan kilitli — gizlenemez.'
                : 'Boş = her zaman görünür. Koşul false dönerse alan gizlenir.'}
            </div>
          </div>

          <div>
            <div className="text-[11px] font-bold tracking-wide text-slate-600 dark:text-white/60 mb-1.5">
              Zorunluluk Koşulu
            </div>
            <RuleBuilder value={requiredIf} fields={fields}
                         emptyMeans="isteğe bağlı"
                         onChange={function (v) { setRequiredIf(v) }} />
            <div className="text-[10.5px] text-slate-400 dark:text-white/35 mt-1">
              Koşul true dönerse alan zorunlu olur (statik “Zorunlu” ayarına EK).
            </div>
          </div>
        </div>

        <div className="flex items-center gap-2 px-5 py-3.5 border-t border-slate-200 dark:border-white/10 flex-shrink-0">
          {tooLong && (
            <span className="text-[11px] font-semibold text-rose-500 dark:text-rose-400">
              Koşul {MAX_RULE_LEN} karakteri aşıyor
            </span>
          )}
          <div className="flex-1" />
          <button type="button" onClick={onClose}
                  className="px-3.5 py-1.5 rounded-lg text-[12px] font-semibold border bg-[#fff] text-slate-600 border-slate-200 hover:bg-slate-100 dark:bg-white/[0.04] dark:text-white/70 dark:border-white/10 dark:hover:bg-white/[0.08]">
            Vazgeç
          </button>
          <button type="button" onClick={apply} disabled={tooLong}
                  className={'px-3.5 py-1.5 rounded-lg text-[12px] font-bold text-[#fff] ' + (
                    tooLong ? 'bg-slate-300 cursor-not-allowed dark:bg-white/15'
                            : 'bg-indigo-600 hover:bg-indigo-500 dark:bg-indigo-500 dark:hover:bg-indigo-400')}>
            Uygula
          </button>
        </div>
      </div>
    </div>,
    document.body
  )
}
