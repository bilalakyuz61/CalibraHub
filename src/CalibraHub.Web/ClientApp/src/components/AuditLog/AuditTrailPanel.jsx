import React, { useCallback, useEffect, useState } from 'react'
import {
  History, RefreshCw, PlusCircle, PencilLine, Trash2, LogIn, LogOut,
  ShieldAlert, Sparkles,
} from 'lucide-react'
import './auditLog.css'
import { ACTION_META, formatTs } from './auditShared'

/**
 * Tek kaydın değişiklik geçmişi zaman çizelgesi — belge/tanım düzenleme
 * ekranlarındaki "Değişiklik Geçmişi" sekmesinde mount edilir
 * (Views/Shared/_AuditTrailHost.cshtml üzerinden).
 *
 * props:
 *   entity          — audit entity kodu (ör. "satis_siparisi", "Item")
 *   recordId        — kayıt Id
 *   widgetFormCode  — opsiyonel; verilirse Ek Alanlar (WidgetTraLog) geçmişi de gelir
 */
export default function AuditTrailPanel({ entity, recordId, widgetFormCode, apiBase = '/AuditLog' }) {
  const [items, setItems] = useState(null)
  const [loading, setLoading] = useState(false)
  const [shown, setShown] = useState(30)

  const load = useCallback(() => {
    if (!entity || !recordId) { setItems([]); return }
    setLoading(true)
    const p = new URLSearchParams({ entity: entity, id: String(recordId) })
    if (widgetFormCode) p.set('widgetFormCode', widgetFormCode)
    fetch(apiBase + '/Record?' + p.toString(), { credentials: 'same-origin' })
      .then(r => r.json())
      .then(d => setItems(d && d.ok ? (d.items || []) : []))
      .catch(() => setItems([]))
      .finally(() => setLoading(false))
  }, [entity, recordId, widgetFormCode, apiBase])

  useEffect(() => { load() }, [load])

  const actionIcon = (a) => {
    switch ((a || '').toLowerCase()) {
      case 'insert': return <PlusCircle size={12} />
      case 'update': return <PencilLine size={12} />
      case 'delete': return <Trash2 size={12} />
      case 'login': return <LogIn size={12} />
      case 'loginfailed': return <ShieldAlert size={12} />
      case 'logout': return <LogOut size={12} />
      default: return <Sparkles size={12} />
    }
  }

  if (!recordId) {
    return (
      <div className="al-root al-trail">
        <div className="al-trail-empty">Kayıt henüz oluşturulmadı — geçmiş, ilk kayıttan sonra burada görünür.</div>
      </div>
    )
  }

  const list = items || []
  const visible = list.slice(0, shown)

  return (
    <div className="al-root al-trail">
      <div className="al-trail-head">
        <History size={15} />
        <strong>Değişiklik Geçmişi</strong>
        <span>{items === null ? '' : list.length + ' işlem'}</span>
        <button type="button" className="al-btn" style={{ marginLeft: 'auto', padding: '4px 8px' }}
          onClick={load} disabled={loading} title="Yenile">
          <RefreshCw size={13} className={loading ? 'al-spin' : ''} />
        </button>
      </div>

      {items === null || loading ? (
        <div className="al-trail-empty">Yükleniyor…</div>
      ) : list.length === 0 ? (
        <div className="al-trail-empty">Bu kayıt için log bulunamadı.</div>
      ) : (
        <>
          <div className="al-trail-list">
            {visible.map((e, i) => {
              const meta = ACTION_META[(e.action || '').toLowerCase()] || ACTION_META.event
              return (
                <div className="al-trail-item" key={e.ts + '|' + i}>
                  <span className={'al-trail-dot al-trail-dot--' + meta.dot} />
                  <div className="al-trail-card">
                    <div className="al-trail-card-head">
                      <span className={'al-badge al-badge--' + meta.cls}>
                        {actionIcon(e.action)} {e.actionLabel || e.action}
                      </span>
                      <span className="al-trail-user">{e.user || '—'}</span>
                      {e.src && e.src !== 'Web' ? <span className="al-trail-src">({e.src})</span> : null}
                      <span className="al-trail-time">{formatTs(e.ts)}</span>
                    </div>
                    {e.changes && e.changes.length > 0 && (
                      <div className="al-trail-changes">
                        {e.changes.map((c, ci) => (
                          <div className="al-trail-chg" key={ci}>
                            <span className="al-trail-chg-label">{c.label || c.field}</span>
                            {c.old != null && c.old !== ''
                              ? <span className="al-diff-old">{c.old}</span>
                              : <span className="al-diff-empty">boş</span>}
                            <span className="al-trail-arrow">→</span>
                            {c.new != null && c.new !== ''
                              ? <span className="al-diff-new">{c.new}</span>
                              : <span className="al-diff-empty">boş</span>}
                          </div>
                        ))}
                      </div>
                    )}
                    {e.detail ? <div className="al-trail-detail">{e.detail}</div> : null}
                  </div>
                </div>
              )
            })}
          </div>
          {list.length > shown && (
            <button type="button" className="al-btn al-trail-more" onClick={() => setShown(s => s + 30)}>
              Daha eski kayıtları göster ({list.length - shown})
            </button>
          )}
        </>
      )}
    </div>
  )
}
