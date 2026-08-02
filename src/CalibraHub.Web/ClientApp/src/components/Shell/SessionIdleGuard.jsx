import React, { useEffect, useRef, useState } from 'react'
import './SessionIdleGuard.css'

/**
 * SessionIdleGuard — oturum atalet (idle) izleyici. Shell (üst pencere) içinde bir kez mount edilir.
 *
 *  - /Account/SessionPolicy → { idleMinutes, warnSeconds }. idleMinutes <= 0 → tamamen devre dışı.
 *  - Aktivite: fare/klavye/scroll/dokunma + iframe sekmelerinden 'calibra:activity' postMessage
 *    (içerik iframe'lerine _Layout küçük bir forwarder enjekte eder) → son-aktivite zamanı güncellenir.
 *  - Aktivitede throttle'lı /Account/KeepAlive ping → sliding auth cookie tazelenir; böylece aktif
 *    ama sunucuya istek atmayan (okuyan) kullanıcı sunucu backstop'una takılmaz.
 *  - (idle - warnSeconds) noktasında geri sayımlı uyarı modalı + "Devam Et". Süre dolunca
 *    /Account/Logout?returnUrl=... — kullanıcı giriş sonrası kaldığı yere dönebilir.
 *
 *  DUVAR-SAATİ (wall-clock) TABANLI (2026-07-31): idle penceresi setTimeout ile DEĞİL, son-aktivite
 *  zaman damgası (lastActivity) + 1 sn'lik interval ile ölçülür. Bilgisayar uykuya girince / sekme
 *  uzun süre arka planda kalınca tarayıcı setTimeout'ları DONDURUR; uyanışta birikmiş timer'lar
 *  neredeyse aynı anda tetiklenip "uyarı bir an parlayıp hemen logout" davranışı yaratıyordu.
 *  Wall-clock'ta uyanışta tek tick GERÇEK geçen süreyi hesaplar: hâlâ uyarı penceresindeyse uyarıyı
 *  düzgün gösterir, süre çoktan aşılmışsa doğrudan logout eder (flaş-uyarı olmaz). visibilitychange
 *  ile sekmeye dönüşte anında değerlendirilir.
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
    let tick = null
    let lastActivity = Date.now()
    let lastPing = 0
    let done = false

    function stop() { if (tick) clearInterval(tick); tick = null }

    function logout() {
      if (done) return
      done = true
      stop()
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
    function onActivity() {
      if (warnLeftRef.current > 0) return   // modal açıkken aktivite yeterli değil — "Devam Et" gerekir
      lastActivity = Date.now()
      keepAlive()
    }
    function onMsg(ev) { if (ev && ev.data === 'calibra:activity') onActivity() }

    // Wall-clock değerlendirme — her tick + görünürlük değişiminde (uyanış) çağrılır.
    function evaluate() {
      if (done || !cfg.idleMs) return
      const elapsed = Date.now() - lastActivity
      if (elapsed >= cfg.idleMs) { logout(); return }
      const remainingMs = cfg.idleMs - elapsed
      if (remainingMs <= cfg.warnMs) {
        setWarnLeft(Math.max(1, Math.ceil(remainingMs / 1000)))
      } else if (warnLeftRef.current !== 0) {
        setWarnLeft(0)
      }
    }

    apiRef.current.continue = function () {
      lastActivity = Date.now()
      lastPing = 0
      setWarnLeft(0)
      keepAlive()
    }
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
        lastActivity = Date.now()
        evs.forEach(function (e) { window.addEventListener(e, onActivity, { passive: true }) })
        window.addEventListener('message', onMsg)
        document.addEventListener('visibilitychange', evaluate)
        tick = setInterval(evaluate, 1000)
      })
      .catch(function () { /* sessiz — idle guard devre dışı */ })

    return function () {
      alive = false
      evs.forEach(function (e) { window.removeEventListener(e, onActivity) })
      window.removeEventListener('message', onMsg)
      document.removeEventListener('visibilitychange', evaluate)
      stop()
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
