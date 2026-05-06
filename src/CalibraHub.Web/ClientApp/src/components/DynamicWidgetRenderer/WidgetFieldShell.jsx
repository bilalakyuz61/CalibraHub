/**
 * WidgetFieldShell — Widget alanlari icin tek-yol-wrapper.
 *
 * DynamicWidgetRenderer artik uc ayri inline-style bloku (plain/modern/standard)
 * yerine bu Shell'e cocuk gecirir. Shell:
 *   • gridColumn: span N (widget.colSpan)  — 24-col grid icinde yer
 *   • Label modu (standard / modern / inline) — `widget.labelStyle`'a gore
 *   • Renkli sol cizgi (widgetColor)
 *   • Readonly wrap (pointer-events: none)
 *   • Required yildizi
 *   • Rule-error mesaji
 *
 * `isPlainField=true` (eski) widget'lar icin labelStyle='inline' olarak ele alinir
 * — geriye donuk uyumluluk; backend kademeli migrasyonla LabelStyle='inline' yazar.
 *
 * Props:
 *   widget        — { widgetId, label, isRequired, dataType, colSpan, labelStyle, isPlainField }
 *   children      — input/select/textarea/lookup elementi (controlled by parent)
 *   isFilled      — modern label icin data-filled attribute (renderer hesaplar)
 *   isDisabled    — readonly wrap'i tetikler
 *   hasFormula    — `wf-field--formula` modifier
 *   ruleErrorMsg  — rule engine hata metni (varsa altina yazilir)
 *   widgetColor   — { border, label } semantik token cozumu (DynamicWidgetRenderer.WIDGET_COLOR_MAP)
 *   classPrefix   — chrome ('mce'|'sqe'|'ca') — readonly wrap class adi icin (legacy)
 */

function safeColSpan(v) {
  return (typeof v === 'number' && v >= 1 && v <= 24) ? v : 12
}

function resolveLabelStyle(widget) {
  // labelStyle benimsenir, isPlainField=true ise 'inline' (back-compat).
  // labelStyle case-insensitive — eski "Modern" gibi degerler de calisir.
  var ls = String(widget.labelStyle || '').toLowerCase()
  if (ls === 'modern' || ls === 'inline' || ls === 'standard') return ls
  if (widget.isPlainField === true) return 'inline'
  return 'standard'
}

export default function WidgetFieldShell(props) {
  var w           = props.widget || {}
  var children    = props.children
  var isFilled    = !!props.isFilled
  var isDisabled  = !!props.isDisabled
  var hasFormula  = !!props.hasFormula
  var ruleErrMsg  = props.ruleErrorMsg
  var widgetColor = props.widgetColor || null
  var prefix      = props.classPrefix || 'wf'

  var span = safeColSpan(w.colSpan)
  var mode = resolveLabelStyle(w)

  // Outer wrapper class set
  var classes = ['wf-field-shell']
  if (mode === 'modern') {
    classes.push('wf-modern-wrap')
    classes.push('calibra-modern-wrap')   // back-compat alias
    classes.push('wf-field--modern')
  } else if (mode === 'inline') {
    classes.push('wf-field')
    classes.push('wf-field--inline')
  } else {
    classes.push('wf-field')
  }
  if (isDisabled) classes.push('wf-field--readonly')
  if (hasFormula) classes.push('wf-field--formula')
  if (ruleErrMsg) classes.push('wf-field--has-rule-error')

  // Outer style — gridColumn span + minWidth + sol cizgi
  var style = { gridColumn: 'span ' + span, minWidth: 0 }
  if (mode === 'modern') {
    // Inline override: modern wrap kendi padding-top'unu CSS'ten alacak ama
    // grid template'i sifirla — DynamicWidgetRenderer .mce-field/.sqe-field
    // gibi inner grid uretmesin.
    style.display = 'block'
    style.gridTemplateColumns = 'none'
  }
  if (widgetColor) {
    style.borderLeft = '3px solid ' + widgetColor.border
    style.paddingLeft = mode === 'inline' ? 8 : 8
    style.borderRadius = '0 ' + (mode === 'modern' ? 6 : 6) + 'px ' +
                         (mode === 'modern' ? 6 : 6) + 'px 0'
  }

  // Label
  var labelEl = null
  if (mode === 'modern') {
    labelEl = (
      <span className="wf-label--modern calibra-modern-label">
        {w.label}
        {w.isRequired && <span style={{ color: '#f87171', marginLeft: 3, fontWeight: 700 }}>*</span>}
      </span>
    )
  } else if (mode === 'inline') {
    labelEl = (
      <label className="wf-label" htmlFor={'dyn_' + w.widgetId}>
        {w.label}
        {w.isRequired && <span style={{ color: '#f87171', marginLeft: 3, fontWeight: 700 }}>*</span>}
      </label>
    )
  } else {
    labelEl = (
      <label className="wf-label" htmlFor={'dyn_' + w.widgetId}>
        {w.label}
        {w.isRequired && <span style={{ color: '#f87171', marginLeft: 3, fontWeight: 700 }}>*</span>}
      </label>
    )
  }

  // Input slot — readonly wrap ile sarilir
  var inputSlot = isDisabled ? (
    <div className={'wf-field__readonly-wrap ' + prefix + '-field__readonly-wrap'}>
      {children}
    </div>
  ) : children

  // Modern modda: inline'ta input slot wrapper'i olmadan dogrudan, klasik/inline'da slot icinde
  if (mode === 'modern') {
    return (
      <div
        className={classes.join(' ')}
        style={style}
        data-label-style="modern"
        data-filled={isFilled ? 'true' : 'false'}
      >
        {labelEl}
        {inputSlot}
        {ruleErrMsg && (
          <div className="wf-rule-error">{ruleErrMsg}</div>
        )}
      </div>
    )
  }

  if (mode === 'inline') {
    return (
      <div
        className={classes.join(' ')}
        style={style}
        data-label-style="inline"
      >
        {labelEl}
        <div className="wf-input-slot" style={{ flex: 1, minWidth: 0 }}>
          {inputSlot}
          {ruleErrMsg && (
            <div className="wf-rule-error">{ruleErrMsg}</div>
          )}
        </div>
      </div>
    )
  }

  // standard
  return (
    <div
      className={classes.join(' ')}
      style={style}
      data-label-style="standard"
    >
      {labelEl}
      {inputSlot}
      {ruleErrMsg && (
        <div className="wf-rule-error" style={{ gridColumn: '1 / -1' }}>{ruleErrMsg}</div>
      )}
    </div>
  )
}
