/**
 * LookupCard — Rehber donuslu alanlar icin sunum bileseni (presentational).
 *
 * Tek bir kompakt kartta:
 *   ▎  <editable input>          <actions: ⌕ ✕ ⚙>
 *
 * Kart icine sadece kod yazilir (manuel giris VEYA rehberden secim).
 * Display name (ornek: "ŞUHEDA YÜCEL") kartin DISINDA caller tarafindan
 * gosterilir — ya Razor span'i (fillMap ile dolan) ya da yan tarafta ayri
 * bir element. Bu sayede "kullanici elle kod yapistirsa bile rehbersiz
 * isim cozumlemesi" akisi bozulmaz.
 *
 * Hem `FixedFieldLookupBridge` (sabit alan) hem `LookupFieldInput` (widget)
 * tarafindan kullanilir. State tutmaz — caller secim, temizleme ve manuel
 * giris akislarini yonetir.
 *
 * Props:
 *   value         — kod (controlled)
 *   onChange      — (newValue: string) => void — manuel yazimda tetiklenir
 *   placeholder   — input placeholder
 *   disabled      — true ise eylem yok, surface flat
 *   required      — true + bos ise required-empty class'i
 *   error         — true ise error rengi (shake animasyonu disindan)
 *   size          — 'sm' | 'md' | 'lg' (varsayilan 'md')
 *   onOpen        — rehber acilacaginda
 *   onClear       — degeri temizleme
 *   extraAction   — opsiyonel ek aksiyon dugmesi (ornek: settings)
 *   inputProps    — DOM input'a aktarilacak ek attr'ler (name, id, ref, vs.)
 *   readOnly      — true ise input duzenlenemez (rehber-only mod)
 */
import { Search, X } from 'lucide-react'

export default function LookupCard(props) {
  var value       = props.value == null ? '' : String(props.value)
  var display     = props.display == null ? '' : String(props.display)
  var disabled    = !!props.disabled
  var required    = !!props.required
  var error       = !!props.error
  // displayInline: true (varsayilan, widget akisi) — display name varsa kart
  //   "display modu"na gecer (input readonly, kod yerine ad gosterilir).
  // displayInline: false (sabit alan akisi) — kart hep kodu gosterir, manuel
  //   yazim serbest; ad caller'in ayri input'unda (ornek: ptParentName) gorunur.
  var displayInline = props.displayInline !== false
  var displayMode = displayInline && display.length > 0
  var readOnly    = displayMode || !!props.readOnly
  var size        = props.size || 'md'
  var placeholder = props.placeholder || (readOnly ? 'Seçmek için tıklayın…' : 'Kod girin veya rehberden seçin…')
  var onChange    = props.onChange
  var onOpen      = props.onOpen
  var onClear     = props.onClear
  var extraAction = props.extraAction
  var inputProps  = props.inputProps || {}

  var hasValue = value.length > 0
  // Input'ta gosterilecek metin: display modunda display name, aksi halde value (kod)
  var shownText = displayMode ? display : value

  function handleInputChange(e) {
    if (onChange) onChange(e.target.value)
  }

  function handleSearchClick(e) {
    e.stopPropagation()
    if (disabled || !onOpen) return
    onOpen()
  }

  function handleClearClick(e) {
    e.stopPropagation()
    if (disabled || !onClear) return
    onClear()
  }

  function handleInputKeyDown(e) {
    if (disabled) return
    // F2 veya Alt+ArrowDown — rehberi ac (windows-style lookup convention)
    if (e.key === 'F2' || (e.altKey && e.key === 'ArrowDown')) {
      e.preventDefault()
      if (onOpen) onOpen()
      return
    }
    // ReadOnly modda Enter ile rehberi ac
    if (readOnly && e.key === 'Enter') {
      e.preventDefault()
      if (onOpen) onOpen()
    }
  }

  function handleInputClick() {
    if (disabled) return
    if (readOnly && onOpen) onOpen()
  }

  var classes = ['gl-card', 'gl-card--' + size]
  if (!hasValue)             classes.push('gl-card--empty')
  if (disabled)              classes.push('gl-card--disabled')
  if (error)                 classes.push('gl-card--error')
  if (required && !hasValue) classes.push('gl-card--required-empty')
  if (readOnly)              classes.push('gl-card--readonly')

  return (
    <div className={classes.join(' ')}>
      <span className="gl-card__bar" aria-hidden="true" />

      <input
        type="text"
        className="gl-card__input"
        value={shownText}
        onChange={handleInputChange}
        onKeyDown={handleInputKeyDown}
        onClick={handleInputClick}
        disabled={disabled}
        readOnly={readOnly}
        placeholder={placeholder}
        spellCheck={false}
        autoComplete="off"
        title={displayMode && value ? value : undefined}
        {...inputProps}
      />

      {required && !hasValue && (
        <span className="gl-card__required-dot" aria-hidden="true" title="Zorunlu alan" />
      )}

      <span className="gl-card__actions">
        {extraAction}
        {!disabled && (
          <button
            type="button"
            className="gl-card__btn gl-card__btn--search"
            onClick={handleSearchClick}
            tabIndex={-1}
            title="Rehber aç (F2)"
            aria-label="Rehberi aç"
          >
            <Search size={14} strokeWidth={2} />
          </button>
        )}
        {hasValue && !disabled && (
          <button
            type="button"
            className="gl-card__btn gl-card__btn--clear"
            onClick={handleClearClick}
            tabIndex={-1}
            title="Temizle"
            aria-label="Değeri temizle"
          >
            <X size={14} strokeWidth={2.2} />
          </button>
        )}
      </span>
    </div>
  )
}
