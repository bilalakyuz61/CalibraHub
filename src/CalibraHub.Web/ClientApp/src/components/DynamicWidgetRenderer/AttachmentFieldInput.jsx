/**
 * AttachmentFieldInput — 'attachment' widget tipi runtime input'u.
 *
 * Deger: merkezi Attachment tablosundaki kayit Id'si (string). Dosya adi/boyutu
 * degil Id saklanir (ID-tabanli eslestirme kurali); gorunum icin meta endpoint'ten
 * dosya adi cekilir.
 *
 * Durumlar:
 *   - Deger yok  → "Dosya Seç" butonu (gizli input[type=file] tetikler)
 *   - Yukleniyor → spinner + dosya adi
 *   - Deger var  → chip: [ikon] dosyaadi (boyut) [indir] [x]
 *     Gorsel content-type ise kucuk onizleme thumbnail'i gosterilir.
 *
 * Props:
 *   widgetDbId  int    — WidgetMas.Id (upload RefId'si)
 *   value       string — attachment Id ('' = bos)
 *   onChange    fn(newValue)
 *   isInvalid   bool   — zorunlu alan hatasi görseli
 *   inputId     string — dyn_{widgetCode} (label/scroll hedefi)
 */
import { useState, useEffect, useRef } from 'react'
import { Paperclip, Download, X, Loader2, Upload } from 'lucide-react'
import {
  uploadWidgetAttachment,
  getWidgetAttachmentMeta,
  deleteWidgetAttachment,
  widgetAttachmentUrl,
} from './dynamicWidgetService'

function formatSize(bytes) {
  var n = Number(bytes)
  if (!Number.isFinite(n) || n <= 0) return ''
  if (n < 1024) return n + ' B'
  if (n < 1024 * 1024) return (n / 1024).toFixed(0) + ' KB'
  return (n / (1024 * 1024)).toFixed(1) + ' MB'
}

export default function AttachmentFieldInput(props) {
  var widgetDbId = props.widgetDbId
  var value      = props.value != null ? String(props.value) : ''
  var onChange   = props.onChange
  var isInvalid  = props.isInvalid
  var inputId    = props.inputId

  var [meta, setMeta]         = useState(null)   // { fileName, fileSize, contentType }
  var [uploading, setUploading] = useState(false)
  var [error, setError]       = useState(null)
  var fileRef = useRef(null)

  // Deger degisince meta cek (kayit acilisinda mevcut dosya adini goster)
  useEffect(function () {
    if (!value) { setMeta(null); return undefined }
    var cancelled = false
    getWidgetAttachmentMeta(value).then(function (m) {
      if (!cancelled) setMeta(m)
    })
    return function () { cancelled = true }
  }, [value])

  async function handlePick(e) {
    var file = e.target.files && e.target.files[0]
    e.target.value = ''   // ayni dosya tekrar secilebilsin
    if (!file) return
    setUploading(true)
    setError(null)
    var oldId = value
    try {
      var res = await uploadWidgetAttachment(widgetDbId, file)
      onChange(String(res.id))
      setMeta({ fileName: res.fileName, fileSize: res.fileSize, contentType: file.type })
      // Eski dosyayi arkada temizle (soft-delete, best-effort)
      if (oldId) deleteWidgetAttachment(oldId)
    } catch (ex) {
      setError(ex.message || 'Dosya yüklenemedi')
    } finally {
      setUploading(false)
    }
  }

  function handleRemove() {
    var oldId = value
    onChange('')
    setMeta(null)
    if (oldId) deleteWidgetAttachment(oldId)
  }

  var isImage = meta && String(meta.contentType || '').indexOf('image/') === 0

  var chipBase = {
    display: 'flex', alignItems: 'center', gap: 8,
    minHeight: 'var(--wf-input-h, 38px)',
    padding: '4px 10px',
    borderRadius: 'var(--wf-radius, 10px)',
    border: '1px solid ' + (isInvalid ? 'var(--wf-error-color, #ef4444)' : 'var(--wf-border, #e2e8f0)'),
    background: 'var(--wf-bg, #fff)',
    color: 'var(--wf-text, #0f172a)',
    fontSize: 12.5,
  }

  return (
    <div id={inputId}>
      <input
        ref={fileRef}
        type="file"
        style={{ display: 'none' }}
        onChange={handlePick}
      />

      {uploading ? (
        <div style={chipBase}>
          <Loader2 size={14} className="animate-spin" style={{ flexShrink: 0, opacity: 0.6 }} />
          <span style={{ opacity: 0.7 }}>Yükleniyor…</span>
        </div>
      ) : value ? (
        <div style={chipBase}>
          {isImage ? (
            <img
              src={widgetAttachmentUrl(value, true)}
              alt=""
              style={{ width: 28, height: 28, objectFit: 'cover', borderRadius: 6, flexShrink: 0 }}
            />
          ) : (
            <Paperclip size={14} style={{ flexShrink: 0, opacity: 0.55 }} />
          )}
          <span
            style={{ flex: 1, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}
            title={meta ? meta.fileName : 'Dosya #' + value}
          >
            {meta ? meta.fileName : 'Dosya #' + value}
            {meta && meta.fileSize > 0 && (
              <span style={{ opacity: 0.5, marginLeft: 6, fontSize: 11 }}>{formatSize(meta.fileSize)}</span>
            )}
          </span>
          <a
            href={widgetAttachmentUrl(value, false)}
            title="İndir"
            style={{ display: 'flex', color: 'inherit', opacity: 0.55, flexShrink: 0 }}
            onMouseEnter={function (e) { e.currentTarget.style.opacity = 1 }}
            onMouseLeave={function (e) { e.currentTarget.style.opacity = 0.55 }}
          >
            <Download size={14} />
          </a>
          <button
            type="button"
            title="Dosyayı kaldır"
            onClick={handleRemove}
            style={{
              display: 'flex', border: 'none', background: 'transparent',
              color: 'inherit', opacity: 0.55, cursor: 'pointer', padding: 0, flexShrink: 0,
            }}
            onMouseEnter={function (e) { e.currentTarget.style.opacity = 1 }}
            onMouseLeave={function (e) { e.currentTarget.style.opacity = 0.55 }}
          >
            <X size={14} />
          </button>
        </div>
      ) : (
        <button
          type="button"
          onClick={function () { if (fileRef.current) fileRef.current.click() }}
          style={Object.assign({}, chipBase, {
            width: '100%', cursor: 'pointer', justifyContent: 'center',
            borderStyle: 'dashed', color: 'var(--wf-placeholder, #94a3b8)',
          })}
        >
          <Upload size={14} />
          Dosya Seç
        </button>
      )}

      {error && (
        <div style={{ marginTop: 4, fontSize: 11, color: 'var(--wf-error-color, #ef4444)' }}>{error}</div>
      )}
    </div>
  )
}
