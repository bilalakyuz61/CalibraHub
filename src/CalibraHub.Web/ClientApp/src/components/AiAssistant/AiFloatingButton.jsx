/**
 * AiFloatingButton — sağ alt köşede sabit AI balonu + tıklayınca açılan slide-out panel.
 *
 * Workspace tab iframe'lerinin DIŞINDA, Shell altında top-level mount edilir.
 * z-index 9999 → tüm iframe'lerin üzerinde görünür.
 *
 * 2026-05-23 — Faz 1.A: sohbet + provider seçim + localStorage history (son 50 mesaj).
 *
 * Sayfa context'i: kullanıcı bir tab'da çalışırken (örn. Sipariş Edit), aktif iframe'in
 * URL path + query string'i AI'ye context olarak gönderilir. Pattern:
 *   {"page": "/Sales/DocumentEdit", "params": {"id": "42"}}
 */
import { useEffect, useRef, useState, useCallback } from 'react'
import { Bot, X, Send, Paperclip, FileText } from 'lucide-react'
import './ai-styles.css'

// 2026-05-24: Dosya ekleme limitleri.
const MAX_FILE_SIZE_BYTES = 5 * 1024 * 1024       // 5 MB / dosya
const MAX_TOTAL_SIZE_BYTES = 15 * 1024 * 1024     // 15 MB toplam
const MAX_FILES = 6

// Text dosya MIME tipleri — sunucu textContent alanini doldurur.
const TEXT_MIME_PREFIXES = ['text/']
const TEXT_EXTENSIONS = [
  '.txt', '.md', '.csv', '.tsv', '.json', '.xml', '.yaml', '.yml',
  '.log', '.sql', '.cs', '.js', '.ts', '.jsx', '.tsx', '.html', '.css',
  '.py', '.go', '.rs', '.java', '.kt', '.sh', '.bat', '.ps1', '.ini', '.toml', '.env',
]

// 2026-05-24: Binary dokuman uzantilari — sunucuda IDocumentTextExtractor ile text'e cevrilir.
// Frontend bunlari base64 olarak yollar; backend extractor mevcut degilse model goz ardi eder.
const BINARY_DOC_EXTENSIONS = ['.xlsx', '.xls', '.pdf', '.docx']

function isImageFile(file) {
  return file.type && file.type.startsWith('image/')
}
function isTextFile(file) {
  if (!file.type) {
    var n = (file.name || '').toLowerCase()
    return TEXT_EXTENSIONS.some(function(ext) { return n.endsWith(ext) })
  }
  return TEXT_MIME_PREFIXES.some(function(p) { return file.type.startsWith(p) })
      || TEXT_EXTENSIONS.some(function(ext) { return (file.name || '').toLowerCase().endsWith(ext) })
}
function isBinaryDocFile(file) {
  var n = (file.name || '').toLowerCase()
  return BINARY_DOC_EXTENSIONS.some(function(ext) { return n.endsWith(ext) })
}

// Dosyayi base64'e cevir (image) veya text'e oku.
function readFileAsAttachment(file) {
  return new Promise(function(resolve, reject) {
    var reader = new FileReader()
    reader.onerror = function() { reject(new Error('Dosya okunamadi: ' + file.name)) }
    if (isImageFile(file)) {
      reader.onload = function() {
        // result: "data:image/png;base64,...." — prefix soyup sadece base64'u tut
        var s = String(reader.result || '')
        var commaIdx = s.indexOf(',')
        var base64 = commaIdx > 0 ? s.slice(commaIdx + 1) : s
        resolve({
          name: file.name,
          mimeType: file.type || 'application/octet-stream',
          base64Data: base64,
          textContent: null,
          _previewUrl: s,   // UI preview icin
          _kind: 'image',
        })
      }
      reader.readAsDataURL(file)
    } else if (isTextFile(file)) {
      reader.onload = function() {
        resolve({
          name: file.name,
          mimeType: file.type || 'text/plain',
          base64Data: null,
          textContent: String(reader.result || ''),
          _previewUrl: null,
          _kind: 'text',
        })
      }
      reader.readAsText(file, 'utf-8')
    } else if (isBinaryDocFile(file)) {
      // 2026-05-24: xlsx/pdf/docx — base64 olarak yolla; backend IDocumentTextExtractor
      // ile text'e cevirir (ClosedXML / PdfPig / OpenXml).
      reader.onload = function() {
        var s = String(reader.result || '')
        var commaIdx = s.indexOf(',')
        var base64 = commaIdx > 0 ? s.slice(commaIdx + 1) : s
        resolve({
          name: file.name,
          mimeType: file.type || 'application/octet-stream',
          base64Data: base64,
          textContent: null,
          _previewUrl: null,
          _kind: 'doc',
        })
      }
      reader.readAsDataURL(file)
    } else {
      reject(new Error('Desteklenmeyen dosya tipi: ' + (file.type || file.name)))
    }
  })
}

const HISTORY_KEY = 'calibrahub.ai.chat.history'
const MAX_HISTORY = 50

function loadHistory() {
  try {
    const raw = localStorage.getItem(HISTORY_KEY)
    return raw ? JSON.parse(raw) : []
  } catch { return [] }
}
function saveHistory(messages) {
  try {
    const trimmed = messages.slice(-MAX_HISTORY)
    localStorage.setItem(HISTORY_KEY, JSON.stringify(trimmed))
  } catch { /* quota */ }
}

function getCsrf() {
  // 2026-05-23: Parent Shell sayfasi `<input name="__RequestVerificationToken">`
  // render etmiyor — token sadece window.__CALIBRA_SHELL_CONFIG__.antiforgeryToken
  // olarak JS config'de bulunuyor. DOM input bulamazsak config'e dus.
  const el = document.querySelector('input[name="__RequestVerificationToken"]')
  if (el && el.value) return el.value
  try {
    const cfg = window.__CALIBRA_SHELL_CONFIG__
    if (cfg && typeof cfg.antiforgeryToken === 'string') return cfg.antiforgeryToken
  } catch (_) { /* ignore */ }
  return ''
}

/**
 * Aktif workspace tab'ın URL'inden basit sayfa bağlamı türet.
 * Shell iframe'lerinin src URL'ini parent window URL'inden okur.
 */
function getActiveContext() {
  try {
    const path = window.location.pathname
    const search = window.location.search
    return JSON.stringify({ page: path, query: search })
  } catch { return null }
}

export default function AiFloatingButton() {
  const [open, setOpen] = useState(false)
  const [messages, setMessages] = useState(loadHistory)
  const [input, setInput] = useState('')
  const [busy, setBusy] = useState(false)
  const [providers, setProviders] = useState([])
  const [selectedProvider, setSelectedProvider] = useState('')
  const [streaming, setStreaming] = useState('')
  // 2026-05-24: Mesaja eklenecek dosyalar (resim + text). Send'den sonra temizlenir.
  const [attachments, setAttachments] = useState([])
  const [attachError, setAttachError] = useState(null)
  const fileInputRef = useRef(null)
  const scrollRef = useRef(null)
  const textareaRef = useRef(null)

  // 2026-05-24: Global event 'calibra:open-ai' — Shell ProfilePopover menusunden tetiklenir.
  // FAB butonu kaldirildi (kullanici profil > menu > AI Asistan akisini istiyor); event ile
  // her yerden tetiklenebilir kalir (ileride farkli yerlerden de cagrilabilir).
  useEffect(() => {
    function onOpenAi() { setOpen(true) }
    window.addEventListener('calibra:open-ai', onOpenAi)
    return () => window.removeEventListener('calibra:open-ai', onOpenAi)
  }, [])

  // ESC ile panel kapanir
  useEffect(() => {
    if (!open) return undefined
    function onKey(e) { if (e.key === 'Escape') setOpen(false) }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [open])

  // 2026-05-24: Mesaj alanina otomatik focus —
  //   1. Panel acildiginda
  //   2. Mesaj gonderildikten sonra (busy → false gecisi) ki kullanici sonraki mesaja
  //      direk yazabilsin (manuel tiklama gerekmesin).
  useEffect(() => {
    if (!open || busy) return undefined
    var raf = requestAnimationFrame(() => {
      if (textareaRef.current) {
        try { textareaRef.current.focus() } catch (_) { /* ignore */ }
      }
    })
    return () => cancelAnimationFrame(raf)
  }, [open, busy])
  const abortRef = useRef(null)

  // Provider listesi — panel açıldığında bir kez fetch
  useEffect(() => {
    if (!open) return
    let cancelled = false
    fetch('/Ai/Providers', { credentials: 'same-origin' })
      .then(r => r.json())
      .then(d => {
        if (cancelled) return
        const list = (d && d.ok && Array.isArray(d.providers)) ? d.providers : []
        setProviders(list)
        // Default provider seç (yoksa ilk)
        const def = list.find(p => p.isDefault) || list[0]
        if (def && !selectedProvider) setSelectedProvider(def.code)
      })
      .catch(() => {})
    return () => { cancelled = true }
  }, [open, selectedProvider])

  // Mesaj eklenince history'e yaz + scroll en alta
  useEffect(() => {
    saveHistory(messages)
    if (scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight
    }
  }, [messages, streaming])

  // 2026-05-24: Panel acildiginda son mesaj gozuksun diye scroll en alta.
  // useEffect'in [open] dependency'si: open false → true gecisinde DOM mount olur,
  // bir frame bekleyip scroll'u en alta cek (layout tamamlandiktan sonra).
  useEffect(() => {
    if (!open) return undefined
    // requestAnimationFrame ile DOM layout tamamlanmasini bekle; scrollHeight ozel
    // olarak ilk frame'de henuz hesaplanmamis olabilir (kucuk modeller / yavas cihaz).
    var raf1 = requestAnimationFrame(function() {
      var raf2 = requestAnimationFrame(function() {
        if (scrollRef.current) {
          scrollRef.current.scrollTop = scrollRef.current.scrollHeight
        }
      })
      // raf2 cleanup
      return function() { cancelAnimationFrame(raf2) }
    })
    return function() { cancelAnimationFrame(raf1) }
  }, [open])

  const clearChat = useCallback(() => {
    setMessages([])
    saveHistory([])
  }, [])

  // 2026-05-24: Dosya secimi — file picker'dan veya drag-drop'tan gelir.
  const handleFiles = useCallback(async (fileList) => {
    if (!fileList || fileList.length === 0) return
    setAttachError(null)

    const incoming = Array.from(fileList)
    if (attachments.length + incoming.length > MAX_FILES) {
      setAttachError(`En fazla ${MAX_FILES} dosya eklenebilir.`)
      return
    }

    const currentTotal = attachments.reduce((s, a) => s + (a._size || 0), 0)
    let runningTotal = currentTotal
    const accepted = []
    for (const f of incoming) {
      if (f.size > MAX_FILE_SIZE_BYTES) {
        setAttachError(`"${f.name}" çok büyük (max ${MAX_FILE_SIZE_BYTES / (1024*1024)} MB / dosya).`)
        return
      }
      runningTotal += f.size
      if (runningTotal > MAX_TOTAL_SIZE_BYTES) {
        setAttachError(`Toplam boyut sınırı ${MAX_TOTAL_SIZE_BYTES / (1024*1024)} MB aşıldı.`)
        return
      }
      accepted.push(f)
    }

    try {
      const results = await Promise.all(accepted.map(readFileAsAttachment))
      // _size bilgisini sakla — sonraki addFiles ile toplam kontrolu icin
      const enriched = results.map((r, idx) => ({ ...r, _size: accepted[idx].size }))
      setAttachments(prev => prev.concat(enriched))
    } catch (err) {
      setAttachError(err.message || 'Dosya okunurken hata.')
    }
  }, [attachments])

  const removeAttachment = useCallback((index) => {
    setAttachments(prev => prev.filter((_, i) => i !== index))
    setAttachError(null)
  }, [])

  const openFilePicker = useCallback(() => {
    if (fileInputRef.current) fileInputRef.current.click()
  }, [])

  // 2026-05-24: Calibo write tool onay/iptal handler'lari.
  // confirmAction: kullanici "Onayla" butonuna basinca /Ai/ConfirmAction'a token gonderir
  // → backend gercek kayit yapar → sonuc mesaji asistan mesaji olarak eklenir.
  const confirmAction = useCallback(async (msgIndex, payload) => {
    if (!payload || !payload.confirmToken) return
    setBusy(true)
    try {
      const resp = await fetch('/Ai/ConfirmAction', {
        method: 'POST',
        credentials: 'same-origin',
        headers: {
          'Content-Type': 'application/json',
          'RequestVerificationToken': getCsrf(),
        },
        body: JSON.stringify({ token: payload.confirmToken }),
      })
      const data = await resp.json().catch(() => ({}))
      // Mesajdaki confirm kartini "uygulandi" durumuna gec
      setMessages(prev => prev.map((m, i) => {
        if (i !== msgIndex) return m
        return { ...m, confirmStatus: data.ok ? 'confirmed' : 'error', confirmResult: data }
      }))
      // Sonuc metnini asistandan yeni bir mesaj olarak ekle
      const summary = data.ok
        ? (data.result?.message || data.result?.error || 'İşlem tamamlandı.')
        : ('İşlem başarısız: ' + (data.error || 'Bilinmeyen hata'))
      setMessages(prev => prev.concat([{ role: 'assistant', content: summary }]))
    } catch (err) {
      setMessages(prev => prev.concat([{ role: 'assistant', content: '(Hata: ' + err.message + ')' }]))
    } finally {
      setBusy(false)
    }
  }, [])

  const cancelAction = useCallback((msgIndex) => {
    setMessages(prev => prev.map((m, i) => i === msgIndex ? { ...m, confirmStatus: 'cancelled' } : m))
  }, [])

  const onFileInputChange = useCallback((e) => {
    handleFiles(e.target.files)
    // Aynı dosyayı tekrar seçebilmek için input'u resetle
    if (e.target) e.target.value = ''
  }, [handleFiles])

  const cancelStream = useCallback(() => {
    if (abortRef.current) {
      abortRef.current.abort()
      abortRef.current = null
    }
  }, [])

  const sendMessage = useCallback(async () => {
    const text = input.trim()
    // 2026-05-24: Bos metin bile attachment varsa gondersin (resim/dosya tek basina anlamli).
    if ((!text && attachments.length === 0) || busy) return

    // Mesaj history'sinde gosterim icin attachment metadata'sini sakla (preview).
    const attachMeta = attachments.map(a => ({
      name: a.name, mimeType: a.mimeType, kind: a._kind,
      previewUrl: a._previewUrl  // sadece UI — payload'a gitmez
    }))
    const userMsg = {
      role: 'user',
      content: text || '(dosya eklendi)',
      attachments: attachMeta,
    }
    const newMessages = [...messages, userMsg]
    setMessages(newMessages)
    setInput('')
    // Attachment'ları temizle — bir sonraki mesaj icin
    const pendingAttachments = attachments
    setAttachments([])
    setAttachError(null)
    setBusy(true)
    setStreaming('')

    const controller = new AbortController()
    abortRef.current = controller

    try {
      // Backend formatina cevir — sadece son user mesaja attachments koy
      const payloadMessages = newMessages.map((m, i) => {
        const obj = { role: m.role, content: m.content }
        if (i === newMessages.length - 1 && pendingAttachments.length > 0) {
          obj.attachments = pendingAttachments.map(a => ({
            name: a.name,
            mimeType: a.mimeType,
            base64Data: a.base64Data,
            textContent: a.textContent,
          }))
        }
        return obj
      })
      const body = {
        providerCode: selectedProvider || null,
        model: null,
        messages: payloadMessages,
        context: getActiveContext(),
      }
      const resp = await fetch('/Ai/Chat', {
        method: 'POST',
        credentials: 'same-origin',
        signal: controller.signal,
        headers: {
          'Content-Type': 'application/json',
          'RequestVerificationToken': getCsrf(),
        },
        body: JSON.stringify(body),
      })

      if (!resp.ok || !resp.body) {
        const errTxt = await resp.text().catch(() => '')
        setMessages([...newMessages, { role: 'assistant', content: `(HTTP ${resp.status}) ${errTxt.slice(0, 300)}` }])
        return
      }

      const reader = resp.body.getReader()
      const decoder = new TextDecoder('utf-8')
      let acc = ''
      let buffer = ''
      while (true) {
        const { done, value } = await reader.read()
        if (done) break
        buffer += decoder.decode(value, { stream: true })
        // SSE parse: lines split by \n\n
        const parts = buffer.split('\n\n')
        buffer = parts.pop() || ''   // son parça eksik olabilir
        for (const part of parts) {
          if (!part.startsWith('data:')) continue
          // 2026-05-24: SSE standardi "data:" sonrasi TEK bos karakter delimiter olur —
          // sadece bir adet bosluk soyulur, payload icindeki bosluklar/yeni satirlar korunur.
          // Onceden .trim() yapilirdi → kelime baslangic/sonu boslugu yiyordu ("merhabasizenasil...").
          let payload = part.slice(5)
          if (payload.startsWith(' ')) payload = payload.slice(1)
          // Trailing \r (CRLF normalizasyonu) — bunu temizle, sondaki gercek bosluklari koru
          if (payload.endsWith('\r')) payload = payload.slice(0, -1)
          if (payload === '[DONE]') continue
          // Server tarafı \n'leri \\n'e escape ediyor — geri çevir
          payload = payload.replace(/\\n/g, '\n')
          acc += payload
          setStreaming(acc)
        }
      }
      // 2026-05-24: Marker'lari parse et:
      //   [[CALIBO_CONFIRM]]{json}[[/CALIBO_CONFIRM]]   → inline onay karti
      //   [[CALIBO_NAVIGATE]]{json}[[/CALIBO_NAVIGATE]] → workspace tab aç/değiştir
      let finalContent = acc
      let confirmPayload = null
      let navigatePayload = null

      const extractMarker = (text, start, end) => {
        const s = text.indexOf(start)
        if (s === -1) return { text, payload: null }
        const e = text.indexOf(end, s + start.length)
        if (e === -1) return { text, payload: null }
        const jsonStr = text.slice(s + start.length, e)
        let payload = null
        try { payload = JSON.parse(jsonStr) } catch (_) { /* malformed */ }
        return {
          text: text.slice(0, s) + text.slice(e + end.length),
          payload,
        }
      }

      // Önce confirm sonra navigate (sıra önemsiz — ikisi de çıkarılır)
      let r = extractMarker(finalContent, '[[CALIBO_CONFIRM]]', '[[/CALIBO_CONFIRM]]')
      finalContent = r.text
      confirmPayload = r.payload

      r = extractMarker(finalContent, '[[CALIBO_NAVIGATE]]', '[[/CALIBO_NAVIGATE]]')
      finalContent = r.text
      navigatePayload = r.payload

      finalContent = finalContent.trim()
      if (!finalContent && (confirmPayload || navigatePayload)) {
        finalContent = confirmPayload ? 'İşlem hazırlandı — onayını bekliyorum:' : ''
      }

      // Navigate varsa: workspace'e tab açma sinyali gönder + UI'a navigate kartı çıkar
      if (navigatePayload && navigatePayload.url) {
        try {
          // Workspace shell'e tab açma istegi — Shell.jsx parent listener'i bu event'i yakalar
          window.dispatchEvent(new CustomEvent('calibra:open-tab', {
            detail: { url: navigatePayload.url, label: navigatePayload.label }
          }))
        } catch (_) { /* ignore */ }
      }

      if (finalContent || confirmPayload || navigatePayload) {
        setMessages([...newMessages, {
          role: 'assistant',
          content: finalContent || '',
          confirmPayload: confirmPayload,
          navigatePayload: navigatePayload,
        }])
      }
      setStreaming('')
    } catch (err) {
      if (err?.name !== 'AbortError') {
        setMessages([...newMessages, { role: 'assistant', content: `(Hata: ${err.message})` }])
      }
      setStreaming('')
    } finally {
      setBusy(false)
      abortRef.current = null
    }
  }, [input, busy, messages, selectedProvider, attachments])

  const onKeyDown = (e) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault()
      sendMessage()
    }
  }

  return (
    <>
      {/* 2026-05-24: FAB kaldirildi — panel artik profil menusunden acilir.
          Sagdan slide-in panel. Backdrop YOK — kullanici Calibo acikken arka sayfayla
          serbestce etkilesime girebilir (menu/tab tiklamalari calisir, blur yok). */}
      {open && (
        <>
          <div className="ai-panel ai-panel--side" role="dialog" aria-label="Calibo AI Asistan">
          <div className="ai-panel__header">
            <div className="ai-panel__title">
              <Bot size={16} /> <span>Calibo</span>
            </div>
            <select
              className="ai-panel__provider"
              value={selectedProvider}
              onChange={e => setSelectedProvider(e.target.value)}
              title="AI sağlayıcı"
            >
              {providers.length === 0 && <option value="">— Provider yok —</option>}
              {providers.map(p => (
                <option key={p.id} value={p.code}>
                  {p.label}{p.isUserOverride ? ' (kendi key)' : ''}
                </option>
              ))}
            </select>
            <button
              type="button"
              className="ai-panel__icon-btn"
              title="Geçmişi temizle"
              onClick={clearChat}
            >🗑</button>
            <button
              type="button"
              className="ai-panel__icon-btn"
              title="Kapat (Esc)"
              onClick={() => setOpen(false)}
            ><X size={16} /></button>
          </div>

          <div className="ai-panel__messages" ref={scrollRef}>
            {messages.length === 0 && !streaming && (
              <div className="ai-panel__empty">
                <Bot size={32} />
                <p>Merhaba, ben Calibo. Sana nasıl yardımcı olabilirim?</p>
                <small>İpucu: Aktif sayfanın bağlamı otomatik gönderilir.</small>
              </div>
            )}
            {messages.map((m, i) => (
              <div key={i} className={'ai-msg ai-msg--' + m.role}>
                <div className="ai-msg__bubble">
                  {/* 2026-05-24: Eklenmis dosyalar mesaj balonunun ustunde rozet olarak gosterilir */}
                  {Array.isArray(m.attachments) && m.attachments.length > 0 && (
                    <div className="ai-msg__attachments">
                      {m.attachments.map(function(a, ai) {
                        if (a.kind === 'image' && a.previewUrl) {
                          return (
                            <img key={ai} src={a.previewUrl} alt={a.name}
                                 className="ai-msg__attachment-thumb" title={a.name} />
                          )
                        }
                        return (
                          <span key={ai} className="ai-msg__attachment-chip" title={a.name}>
                            <FileText size={11} /> {a.name}
                          </span>
                        )
                      })}
                    </div>
                  )}
                  {m.content}
                  {/* 2026-05-24: Navigate badge — link kartı (Faz B). */}
                  {m.navigatePayload && m.navigatePayload.url && (
                    <div className="ai-navigate-card">
                      <span className="ai-navigate-card__icon">↗</span>
                      <span className="ai-navigate-card__url" title={m.navigatePayload.url}>
                        {m.navigatePayload.label || m.navigatePayload.url}
                      </span>
                    </div>
                  )}
                  {/* 2026-05-24: Inline confirm kartı — write tool needsConfirmation:true donerse cizilir.
                      Onayla → /Ai/ConfirmAction; Iptal → sadece kart "iptal edildi" gorunumune gecer. */}
                  {m.confirmPayload && (
                    <div className="ai-confirm-card">
                      <div className="ai-confirm-card__summary">
                        {m.confirmPayload.summary || m.confirmPayload.actionLabel || 'İşlem onayı'}
                      </div>
                      {m.confirmStatus === 'confirmed' ? (
                        <div className="ai-confirm-card__status ai-confirm-card__status--ok">
                          ✓ Onaylandı ve uygulandı
                        </div>
                      ) : m.confirmStatus === 'cancelled' ? (
                        <div className="ai-confirm-card__status ai-confirm-card__status--cancel">
                          ✕ İptal edildi
                        </div>
                      ) : m.confirmStatus === 'error' ? (
                        <div className="ai-confirm-card__status ai-confirm-card__status--err">
                          ⚠ Hata: {m.confirmResult?.error || 'Bilinmeyen'}
                        </div>
                      ) : (
                        <div className="ai-confirm-card__actions">
                          <button
                            type="button"
                            className="ai-confirm-btn ai-confirm-btn--cancel"
                            onClick={() => cancelAction(i)}
                            disabled={busy}
                          >Vazgeç</button>
                          <button
                            type="button"
                            className="ai-confirm-btn ai-confirm-btn--ok"
                            onClick={() => confirmAction(i, m.confirmPayload)}
                            disabled={busy}
                          >Onayla ve Uygula</button>
                        </div>
                      )}
                    </div>
                  )}
                </div>
              </div>
            ))}
            {streaming && (
              <div className="ai-msg ai-msg--assistant">
                <div className="ai-msg__bubble ai-msg__bubble--streaming">{streaming}</div>
              </div>
            )}
            {/* 2026-05-24: Typing indicator — model dusunuyor (busy=true) ama henuz ilk token gelmedi.
                Klasik chat uygulamalarindaki "ucu nokta animasyonu" + iptal butonu. */}
            {busy && !streaming && (
              <div className="ai-msg ai-msg--assistant">
                <div className="ai-msg__bubble ai-msg__bubble--typing" aria-label="Calibo yazıyor">
                  <span className="ai-typing-dots" aria-hidden="true">
                    <span></span><span></span><span></span>
                  </span>
                  <button
                    type="button"
                    className="ai-typing-cancel"
                    onClick={cancelStream}
                    title="Yanıtı iptal et"
                  ><X size={11} /></button>
                </div>
              </div>
            )}
          </div>

          {/* 2026-05-24: Eklenmis dosyalarin preview chip'leri — input'un ustunde */}
          {(attachments.length > 0 || attachError) && (
            <div className="ai-panel__attachments">
              {attachments.map(function(a, i) {
                return (
                  <div key={i} className="ai-attachment-chip" title={a.name}>
                    {a._kind === 'image' && a._previewUrl
                      ? <img src={a._previewUrl} alt={a.name} className="ai-attachment-chip__thumb" />
                      : <FileText size={13} />
                    }
                    <span className="ai-attachment-chip__name">{a.name}</span>
                    <button
                      type="button"
                      className="ai-attachment-chip__remove"
                      onClick={function() { removeAttachment(i) }}
                      title="Kaldir"
                      disabled={busy}
                    ><X size={11} /></button>
                  </div>
                )
              })}
              {attachError && <div className="ai-attachment-error">{attachError}</div>}
            </div>
          )}

          <div className="ai-panel__input">
            <input
              ref={fileInputRef}
              type="file"
              multiple
              accept="image/*,.txt,.md,.csv,.tsv,.json,.xml,.yaml,.yml,.log,.sql,.cs,.js,.ts,.jsx,.tsx,.html,.css,.py,.go,.rs,.java,.kt,.sh,.bat,.ps1,.ini,.toml,.env,text/*,.xlsx,.xls,.pdf,.docx,application/pdf,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet,application/vnd.openxmlformats-officedocument.wordprocessingml.document"
              style={{ display: 'none' }}
              onChange={onFileInputChange}
            />
            <button
              type="button"
              className="ai-panel__attach"
              onClick={openFilePicker}
              disabled={busy || attachments.length >= MAX_FILES}
              title={attachments.length >= MAX_FILES
                ? `En fazla ${MAX_FILES} dosya`
                : 'Dosya/resim ekle'}
            >
              <Paperclip size={16} />
            </button>
            <textarea
              ref={textareaRef}
              value={input}
              onChange={e => setInput(e.target.value)}
              onKeyDown={onKeyDown}
              placeholder=""
              rows={1}
              disabled={busy}
            />
            <button
              type="button"
              className="ai-panel__send"
              onClick={sendMessage}
              disabled={busy || (!input.trim() && attachments.length === 0)}
              title="Gönder (Enter)"
            >
              <Send size={16} />
            </button>
          </div>
        </div>
        </>
      )}
    </>
  )
}
