import React, { useEffect, useRef, useState } from 'react'
import './SessionIdleGuard.css'

/**
 * SessionIdleGuard — oturum atalet (idle) izleyici. Shell (üst pencere) içinde bir kez mount edilir.
 *
 *  - /Account/SessionPolicy → { idleMinutes, warnSeconds }. idleMinutes <= 0 → tamamen devre dışı.
 *  - Aktivite: fare/klavye/scroll/dokunma + iframe sekmelerinden 'calibra:activity' postMessage
 *    (içerik iframe'lerine _Layout küçük bir forwarder enjekte eder) → idle sayacı sıfırlanır.
 *  - Aktivitede throttle'lı /Account/KeepAlive ping → sliding auth cookie tazelenir; böylece aktif
 *    ama sunucuya istek atmayan (okuyan) kullanıcı sunucu backstop'una takılmaz.
 *  - (idle - warnSeconds) noktasında geri sayımlı uyarı modalı + "Devam Et". Süre dolunca
 *    /Account/Logout?returnUrl=... — kullanıcı giriş sonrası kaldığı yere dönebilir.
 *
 * Per-company süre client tarafında burada uygulanır (kesin + uyarılı); sunucu ExpireTimeSpan
 * (appsettings Authentication:IdleMinutes) yalnız coarse backstop'tur.
 */
export default function SessionIdleGuard() {
  const [warnLeft, setWarnLeft] = useState(0)   // > 0 → modal görünür, saniye geri sayımı
  const warnLeftRef = useRef(0)
  const apiRef = useRef({ continue: function () {}, logout: function () {} })

  useEffect(function () { warnLeftRef.current = warnLeft }, [warnLeft])

  useEffect(function () {
    let alive = true
    const cfg = { idleMs: 0, warnMs: 60000 }
    const timers = { warn: null, hard: null, tick: null }
    let lastPing = 0
    let done = false

    function clearTimers() {
      if (timers.warn) clearTimeout(timers.warn)
      if (timers.hard) clearTimeout(timers.hard)
      if (timers.tick) clearInterval(timers.tick)
      timers.warn = timers.hard = timers.tick = null
    }
    function logout() {
      if (done) return
      done = true
      clearTimers()
      const rt = encodeURIComponent(window.location.pathname + window.location.search)
      window.location.href = '/Account/Logout?returnUrl=' + rt
    }
    function keepAlive() {
      if (!cfg.idleMs) return
      const now = Date.now()
      const gap = Math.max(60000, cfg.idleMs / 3)   // idle penceresinin ~1/3'ünden sık ping atma
      if (now - lastPing < gap) return
      lastPing = now
      fetch('/Account/KeepAlive', { method: 'POST', credentials: 'same-origin' }).catch(function () {})
    }
    function beginCountdown() {
      let secs = Math.round(cfg.warnMs / 1000)
      setWarnLeft(secs)
      timers.tick = setInterval(function () {
        secs -= 1
        if (secs <= 0) { logout(); return }
        setWarnLeft(secs)
      }, 1000)
    }
    function reset() {
      if (!cfg.idleMs || done) return
      clearTimers()
      setWarnLeft(0)
      keepAlive()
      timers.warn = setTimeout(beginCountdown, Math.max(0, cfg.idleMs - cfg.warnMs))
      timers.hard = setTimeout(logout, cfg.idleMs + 3000)   // güvenlik ağı
    }
    function onActivity() {
      if (warnLeftRef.current > 0) return   // modal açıkken aktivite yeterli değil — "Devam Et" gerekir
      reset()
    }
    function onMsg(ev) { if (ev && ev.data === 'calibra:activity') onActivity() }

    apiRef.current.continue = function () { setWarnLeft(0); lastPing = 0; reset() }
    apiRef.current.logout = logout

    const evs = ['mousemove', 'mousedown', 'keydown', 'scroll', 'touchstart', 'wheel']

    fetch('/Account/SessionPolicy', { credentials: 'same-origin', headers: { Accept: 'application/json' } })
      .then(function (r) { return r.ok ? r.json() : null })
      .then(function (d) {
        if (!alive || !d) return
        const mins = Number(d.idleMinutes) || 0
        if (mins <= 0) return   // idle timeout kapalı (0)
        cfg.idleMs = mins * 60000
        cfg.warnMs = Math.min((Number(d.warnSeconds) || 60) * 1000, cfg.idleMs - 1000)
        evs.forEach(function (e) { window.addEventListener(e, onActivity, { passive: true }) })
        window.addEventListener('message', onMsg)
        reset()
      })
      .catch(function () { /* sessiz — idle guard devre dışı */ })

    return function () {
      alive = false
      evs.forEach(function (e) { window.removeEventListener(e, onActivity) })
      window.removeEventListener('message', onMsg)
      clearTimers()
    }
  }, [])

  if (warnLeft <= 0) return null
  return (
    <div className="sig-backdrop" role="dialog" aria-modal="true" aria-labelledby="sigTitle">
      <div className="sig-card">
        <div className="sig-ico" aria-hidden="true">
          <svg width="26" height="26" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
            <circle cx="12" cy="12" r="9" /><path d="M12 7v5l3 2" />
          </svg>
        </div>
        <div className="sig-title" id="sigTitle">Oturumunuz sonlanmak üzere</div>
        <div className="sig-msg">
          Uzun süredir işlem yapmadınız. Güvenlik amacıyla <b>{warnLeft}</b> saniye içinde
          oturumunuz otomatik olarak kapatılacak.
        </div>
        <div className="sig-actions">
          <button type="button" className="sig-btn sig-btn--primary"
                  onClick={function () { apiRef.current.continue() }}>
            Devam Et
          </button>
          <button type="button" className="sig-btn sig-btn--ghost"
                  onClick={function () { apiRef.current.logout() }}>
            Çıkış Yap
          </button>
        </div>
      </div>
    </div>
  )
}
