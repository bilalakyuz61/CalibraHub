/**
 * Sifreleme ve cozme modallari (Mod 1 ve Mod 2 ortak).
 *
 * 3 modal:
 *   1. EncryptPromptModal  — kullanici parola belirler (iki kez) + ipucu
 *   2. DecryptPromptModal  — kullanici acma parolasi girer
 *   3. FullNoteLockScreen  — Mod 2 sifreli bir notun "kilit ekrani"
 *                            (editor yerine gorunur, iceride DecryptPrompt tetikler)
 */
import { useState, useEffect, useRef } from 'react'
import { Lock, Unlock, EyeOff, Eye, AlertTriangle } from 'lucide-react'

// ── EncryptPromptModal ────────────────────────────────────────────────
export function EncryptPromptModal(props) {
  var open             = props.open
  var title            = props.title || 'Sifrele'
  var onCancel         = props.onCancel
  var onSubmit         = props.onSubmit           // (password, hint) => void
  var suggestedPassword = props.suggestedPassword  // string | null — mevcut not sifresi

  // mode: 'suggest' (mevcut sifre teklif) | 'form' (yeni sifre gir)
  var [mode, setMode] = useState('form')
  var [pw1, setPw1] = useState('')
  var [pw2, setPw2] = useState('')
  var [hint, setHint] = useState('')
  var [show, setShow] = useState(false)
  var [err, setErr] = useState('')
  var pwRef = useRef(null)

  useEffect(function() {
    if (open) {
      setPw1(''); setPw2(''); setHint(''); setShow(false); setErr('')
      setMode(suggestedPassword ? 'suggest' : 'form')
      if (!suggestedPassword) {
        setTimeout(function() { if (pwRef.current) pwRef.current.focus() }, 50)
      }
    }
  }, [open]) // eslint-disable-line react-hooks/exhaustive-deps

  if (!open) return null

  function submitNew() {
    if (!pw1) { setErr('Parola bos olamaz.'); return }
    if (pw1.length < 4) { setErr('Parola en az 4 karakter olmali.'); return }
    if (pw1 !== pw2) { setErr('Parolalar eslemiyor.'); return }
    onSubmit(pw1, hint || null)
  }

  function switchToForm() {
    setMode('form')
    setTimeout(function() { if (pwRef.current) pwRef.current.focus() }, 50)
  }

  // ── Mod: mevcut şifre teklifi ──────────────────────────────────────
  if (mode === 'suggest') {
    return (
      <div className="nw-enc-backdrop" onMouseDown={onCancel}>
        <div className="nw-enc-modal" onMouseDown={function(e){ e.stopPropagation() }}>
          <div className="nw-enc-header">
            <Lock size={16} /> <span>{title}</span>
          </div>
          <div className="nw-enc-body">
            <div className="nw-enc-reuse-card">
              <div className="nw-enc-reuse-icon"><Lock size={22} /></div>
              <div className="nw-enc-reuse-text">
                <strong>Bu notta şifrelenmiş bölüm var.</strong>
                <span>Aynı şifreyi kullanmak ister misiniz?</span>
              </div>
            </div>
            <button
              type="button"
              className="nw-enc-btn nw-enc-btn--primary nw-enc-btn--full"
              autoFocus
              onClick={function() { onSubmit(suggestedPassword, null) }}
            >
              <Lock size={13} /> Mevcut şifreyi kullan
            </button>
            <button
              type="button"
              className="nw-enc-btn nw-enc-btn--ghost nw-enc-btn--full nw-enc-btn--new"
              onClick={switchToForm}
            >
              Yeni şifre gireceğim
            </button>
          </div>
          <div className="nw-enc-footer">
            <button type="button" className="nw-enc-btn nw-enc-btn--ghost" onClick={onCancel}>İptal</button>
          </div>
        </div>
      </div>
    )
  }

  // ── Mod: yeni şifre formu ──────────────────────────────────────────
  return (
    <div className="nw-enc-backdrop" onMouseDown={onCancel}>
      <div className="nw-enc-modal" onMouseDown={function(e){ e.stopPropagation() }}>
        <div className="nw-enc-header">
          <Lock size={16} /> <span>{title}</span>
        </div>
        <div className="nw-enc-body">
          <div className="nw-enc-warn">
            <AlertTriangle size={13} />
            <span>Parolani unutursan icerik kurtarilamaz. Sunucu bile goremez.</span>
          </div>
          <label className="nw-enc-label">Parola</label>
          <div className="nw-enc-input-wrap">
            <input ref={pwRef} type={show ? 'text' : 'password'} className="nw-enc-input"
              value={pw1} onChange={function(e){ setPw1(e.target.value); setErr('') }}
              onKeyDown={function(e){ if (e.key === 'Enter') submitNew() }} />
            <button type="button" className="nw-enc-eye" onClick={function(){ setShow(!show) }}>
              {show ? <EyeOff size={14} /> : <Eye size={14} />}
            </button>
          </div>
          <label className="nw-enc-label">Parola (tekrar)</label>
          <input type={show ? 'text' : 'password'} className="nw-enc-input"
            value={pw2} onChange={function(e){ setPw2(e.target.value); setErr('') }}
            onKeyDown={function(e){ if (e.key === 'Enter') submitNew() }} />
          <label className="nw-enc-label">Ipucu (opsiyonel)</label>
          <input type="text" className="nw-enc-input" placeholder="orn: annemin ilk evcil hayvani"
            value={hint} onChange={function(e){ setHint(e.target.value) }}
            onKeyDown={function(e){ if (e.key === 'Enter') submitNew() }} />
          {err && <div className="nw-enc-err">{err}</div>}
        </div>
        <div className="nw-enc-footer">
          <button type="button" className="nw-enc-btn nw-enc-btn--ghost" onClick={onCancel}>Iptal</button>
          <button type="button" className="nw-enc-btn nw-enc-btn--primary" onClick={submitNew}>
            <Lock size={13} /> Sifrele
          </button>
        </div>
      </div>
    </div>
  )
}

// ── DecryptPromptModal ────────────────────────────────────────────────
export function DecryptPromptModal(props) {
  var open = props.open
  var title = props.title || 'Sifreyi Coz'
  var hint = props.hint
  var onCancel = props.onCancel
  var onSubmit = props.onSubmit // (password) => Promise<bool>
  var [pw, setPw] = useState('')
  var [show, setShow] = useState(false)
  var [err, setErr] = useState('')
  var [busy, setBusy] = useState(false)
  var pwRef = useRef(null)

  useEffect(function() {
    if (open) {
      setPw(''); setShow(false); setErr(''); setBusy(false)
      setTimeout(function() { if (pwRef.current) pwRef.current.focus() }, 50)
    }
  }, [open])

  if (!open) return null

  async function submit() {
    if (!pw) { setErr('Parola girin.'); return }
    setBusy(true); setErr('')
    try {
      var ok = await onSubmit(pw)
      if (!ok) { setErr('Parola hatali.'); setPw(''); setTimeout(function(){ if (pwRef.current) pwRef.current.focus() }, 10) }
    } catch (e) {
      setErr(e && e.message ? e.message : 'Cozme hatasi.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="nw-enc-backdrop" onMouseDown={onCancel}>
      <div className="nw-enc-modal" onMouseDown={function(e){ e.stopPropagation() }}>
        <div className="nw-enc-header">
          <Unlock size={16} /> <span>{title}</span>
        </div>
        <div className="nw-enc-body">
          {hint && (
            <div className="nw-enc-hint">
              <strong>Ipucu:</strong> {hint}
            </div>
          )}
          <label className="nw-enc-label">Parola</label>
          <div className="nw-enc-input-wrap">
            <input ref={pwRef} type={show ? 'text' : 'password'} className="nw-enc-input"
              value={pw} onChange={function(e){ setPw(e.target.value); setErr('') }}
              disabled={busy}
              onKeyDown={function(e){ if (e.key === 'Enter') submit() }} />
            <button type="button" className="nw-enc-eye" onClick={function(){ setShow(!show) }}>
              {show ? <EyeOff size={14} /> : <Eye size={14} />}
            </button>
          </div>
          {err && <div className="nw-enc-err">{err}</div>}
        </div>
        <div className="nw-enc-footer">
          <button type="button" className="nw-enc-btn nw-enc-btn--ghost" onClick={onCancel} disabled={busy}>Iptal</button>
          <button type="button" className="nw-enc-btn nw-enc-btn--primary" onClick={submit} disabled={busy}>
            <Unlock size={13} /> {busy ? 'Aciliyor...' : 'Ac'}
          </button>
        </div>
      </div>
    </div>
  )
}

// ── FullNoteLockScreen (Mod 2) ─────────────────────────────────────────
// Editor yerine tam ekran kilit — kullanici parolayi girmeden content goremez.
export function FullNoteLockScreen(props) {
  var note = props.note  // { id, title, encryptionHint }
  var onUnlock = props.onUnlock  // (password) => Promise<bool>

  var [promptOpen, setPromptOpen] = useState(false)

  return (
    <div className="nw-lockscreen">
      <div className="nw-lockscreen-inner">
        <div className="nw-lockscreen-icon">
          <Lock size={48} />
        </div>
        <h2 className="nw-lockscreen-title">Bu not sifrelenmis</h2>
        {note && note.title && <div className="nw-lockscreen-note-title">{note.title}</div>}
        {note && note.encryptionHint && (
          <div className="nw-lockscreen-hint">
            <strong>Ipucu:</strong> {note.encryptionHint}
          </div>
        )}
        <button type="button" className="nw-lockscreen-btn" onClick={function(){ setPromptOpen(true) }}>
          <Unlock size={16} /> Parolayi gir ve ac
        </button>
        <div className="nw-lockscreen-foot">
          Parolan unutulursa icerik kurtarilamaz. Sunucu bile goremez.
        </div>
      </div>
      <DecryptPromptModal
        open={promptOpen}
        title="Notu Ac"
        hint={note && note.encryptionHint}
        onCancel={function(){ setPromptOpen(false) }}
        onSubmit={async function(pw) {
          var ok = await onUnlock(pw)
          if (ok) setPromptOpen(false)
          return ok
        }}
      />
    </div>
  )
}
