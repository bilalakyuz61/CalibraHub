/**
 * WelcomeWidget — Kullanici + sirket + tarih/saat karti. Veri cekmez; Shell'in
 * config'inden gelen user/system bilgisini prop olarak alir (Dashboard aktarir).
 *
 * Saat dilimine gore selamlama (Günaydın / İyi günler / İyi akşamlar / İyi geceler).
 * Tarih + saat tr-TR formatinda, saat her dakika tazelenir.
 *
 * Props (widget kontrati): { size, settings, isDark, lang, user, system }
 */
import { useState, useEffect } from 'react'
import { Building2, CalendarDays } from 'lucide-react'

function greeting(hour) {
  if (hour >= 5 && hour < 12) return 'Günaydın'
  if (hour >= 12 && hour < 18) return 'İyi günler'
  if (hour >= 18 && hour < 22) return 'İyi akşamlar'
  return 'İyi geceler'
}

function formatDate(d) {
  try {
    return new Intl.DateTimeFormat('tr-TR', {
      weekday: 'long', year: 'numeric', month: 'long', day: 'numeric',
    }).format(d)
  } catch (e) {
    return d.toLocaleDateString()
  }
}

function formatTime(d) {
  try {
    return new Intl.DateTimeFormat('tr-TR', { hour: '2-digit', minute: '2-digit' }).format(d)
  } catch (e) {
    return d.toLocaleTimeString()
  }
}

export default function WelcomeWidget(props) {
  var user = props.user || {}
  var system = props.system || {}
  var [now, setNow] = useState(function () { return new Date() })

  useEffect(function () {
    var id = setInterval(function () { setNow(new Date()) }, 30000)
    return function () { clearInterval(id) }
  }, [])

  var name = user.name || '—'
  var initials = user.initials || (name && name !== '—' ? name.trim().charAt(0).toUpperCase() : '?')

  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 16, flexWrap: 'wrap' }}>
      <div
        style={{
          width: 56, height: 56, borderRadius: 16, flexShrink: 0,
          display: 'flex', alignItems: 'center', justifyContent: 'center',
          color: '#fff', fontWeight: 800, fontSize: 22,
          background: 'linear-gradient(135deg, #6366f1 0%, #8b5cf6 100%)',
          boxShadow: '0 8px 20px rgba(99,102,241,0.35)',
        }}
      >
        {initials}
      </div>
      <div style={{ flex: '1 1 220px', minWidth: 0 }}>
        <div className="dash-stat-label" style={{ marginBottom: 2 }}>
          {greeting(now.getHours())},
        </div>
        <div className="dash-row__title" style={{ fontSize: 19, fontWeight: 800, marginBottom: 6 }}>
          {name}
        </div>
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: '4px 16px' }}>
          {system.company && (
            <span className="dash-row__sub" style={{ display: 'inline-flex', alignItems: 'center', gap: 5 }}>
              <Building2 size={13} />
              {system.company}{system.year ? ' · ' + system.year : ''}
            </span>
          )}
          <span className="dash-row__sub" style={{ display: 'inline-flex', alignItems: 'center', gap: 5 }}>
            <CalendarDays size={13} />
            {formatDate(now)} · {formatTime(now)}
          </span>
        </div>
      </div>
    </div>
  )
}
