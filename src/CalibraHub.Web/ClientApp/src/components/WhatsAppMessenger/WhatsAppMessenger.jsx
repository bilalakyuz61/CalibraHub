import React, { useEffect, useState, useRef, useCallback } from 'react'

/**
 * WhatsApp Web tarzi sohbet UI:
 *  - Sol panel: konusma listesi (avatar yerine bas harf, son mesaj snippet'i, okunmamis badge)
 *  - Sag panel: secili sohbetin mesajlari (kendim sagda yesil, karsi solda gri) + composer
 *  - Polling: her 3sn'de /Whatsapp/Conversations + acik sohbette /Whatsapp/Messages
 *
 * Veriler tamamen server'dan gelir; bu komponent state tutmaz, sadece render eder.
 */
function WhatsAppMessenger({ initialPhone, csrfToken }) {
    const [conversations, setConversations] = useState([])
    const [selectedPhone, setSelectedPhone] = useState(initialPhone || null)
    const [messages, setMessages] = useState([])
    const [composeText, setComposeText] = useState('')
    const [searchTerm, setSearchTerm] = useState('')
    const [sending, setSending] = useState(false)
    const [bridgeReachable, setBridgeReachable] = useState(true)
    const [pendingFile, setPendingFile] = useState(null)
    const [pendingPreviewUrl, setPendingPreviewUrl] = useState(null)
    const [pendingDelete, setPendingDelete] = useState({}) // { phone: true } — silme geri sayim aktif
    const [toast, setToast] = useState(null) // { kind: 'error'|'warn'|'info', text } — sayfa ortasinda custom uyari
    const threadEndRef = useRef(null)
    const pollerRef = useRef(null)
    const fileInputRef = useRef(null)
    const deleteTimeoutsRef = useRef({}) // { phone: timeoutId }
    const toastTimerRef = useRef(null)
    const DELETE_COUNTDOWN_MS = 3000

    // Toast helper: 4sn sonra otomatik kapat. Yeni toast gelirse oncekiyi iptal et.
    const showToast = useCallback((text, kind = 'error', durationMs = 4000) => {
        if (toastTimerRef.current) clearTimeout(toastTimerRef.current)
        setToast({ kind, text })
        toastTimerRef.current = setTimeout(() => {
            setToast(null)
            toastTimerRef.current = null
        }, durationMs)
    }, [])
    const dismissToast = useCallback(() => {
        if (toastTimerRef.current) { clearTimeout(toastTimerRef.current); toastTimerRef.current = null }
        setToast(null)
    }, [])
    useEffect(() => {
        return () => { if (toastTimerRef.current) clearTimeout(toastTimerRef.current) }
    }, [])

    // ── Sohbet listesini cek ────────────────────────────────────────────
    const loadConversations = useCallback(async () => {
        try {
            const r = await fetch('/Whatsapp/Conversations', { credentials: 'same-origin', cache: 'no-store' })
            if (!r.ok) throw new Error('HTTP ' + r.status)
            const data = await r.json()
            setConversations(Array.isArray(data) ? data : [])
            setBridgeReachable(true)
        } catch (err) {
            console.warn('[WaMessenger] sohbet listesi cekilemedi', err)
        }
    }, [])

    // ── Acik sohbetin mesajlarini cek ───────────────────────────────────
    const loadMessages = useCallback(async (phone) => {
        if (!phone) { setMessages([]); return }
        try {
            const r = await fetch('/Whatsapp/Messages?phone=' + encodeURIComponent(phone), {
                credentials: 'same-origin', cache: 'no-store',
            })
            if (!r.ok) throw new Error('HTTP ' + r.status)
            const data = await r.json()
            setMessages(Array.isArray(data) ? data : [])
        } catch (err) {
            console.warn('[WaMessenger] mesajlar cekilemedi', err)
        }
    }, [])

    // ── Polling: her 3sn'de hem listeyi hem acik sohbeti yenile ─────────
    useEffect(() => {
        loadConversations()
        if (selectedPhone) loadMessages(selectedPhone)
        if (pollerRef.current) clearInterval(pollerRef.current)
        pollerRef.current = setInterval(() => {
            if (document.hidden) return
            loadConversations()
            if (selectedPhone) loadMessages(selectedPhone)
        }, 3000)
        return () => {
            if (pollerRef.current) clearInterval(pollerRef.current)
        }
    }, [selectedPhone, loadConversations, loadMessages])

    // ── Yeni mesaj gelince otomatik aşağı kaydir ────────────────────────
    useEffect(() => {
        if (!threadEndRef.current) return
        threadEndRef.current.scrollIntoView({ behavior: 'smooth', block: 'end' })
    }, [messages, selectedPhone])

    // ── Sohbet secince okundu isaretle ──────────────────────────────────
    const selectConversation = useCallback(async (phone) => {
        setSelectedPhone(phone)
        try {
            await fetch('/Whatsapp/MarkRead?phone=' + encodeURIComponent(phone), {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'RequestVerificationToken': csrfToken || '' },
            })
            // Listeyi guncelle ki badge silinsin
            loadConversations()
        } catch { /* sessizce gec */ }
    }, [csrfToken, loadConversations])

    // ── Sohbeti sil — geri sayim ile (Gmail "Undo" patterni, modal yok) ──
    // ✕ butonuna ilk basis: 3sn geri sayim baslar (kirmizi bar). Tekrar basis: iptal.
    const reallyDeleteConversation = useCallback(async (phone) => {
        try {
            const r = await fetch('/Whatsapp/DeleteConversation?phone=' + encodeURIComponent(phone), {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'RequestVerificationToken': csrfToken || '' },
            })
            const d = await r.json()
            if (d.success) {
                if (selectedPhone === phone) { setSelectedPhone(null); setMessages([]) }
                loadConversations()
            } else {
                showToast('Silinemedi: ' + (d.message || 'bilinmeyen hata'))
            }
        } catch (err) {
            showToast('Ag hatasi: ' + err.message)
        }
    }, [csrfToken, loadConversations, selectedPhone, showToast])

    const cancelPendingDelete = useCallback((phone) => {
        const tid = deleteTimeoutsRef.current[phone]
        if (tid) {
            clearTimeout(tid)
            delete deleteTimeoutsRef.current[phone]
        }
        setPendingDelete(prev => {
            const next = { ...prev }
            delete next[phone]
            return next
        })
    }, [])

    const requestDeleteConversation = useCallback((phone) => {
        if (!phone) return
        // Zaten beklemedeyse: tekrar tikla = iptal
        if (pendingDelete[phone]) {
            cancelPendingDelete(phone)
            return
        }
        setPendingDelete(prev => ({ ...prev, [phone]: true }))
        const tid = setTimeout(async () => {
            delete deleteTimeoutsRef.current[phone]
            setPendingDelete(prev => {
                const next = { ...prev }
                delete next[phone]
                return next
            })
            await reallyDeleteConversation(phone)
        }, DELETE_COUNTDOWN_MS)
        deleteTimeoutsRef.current[phone] = tid
    }, [pendingDelete, cancelPendingDelete, reallyDeleteConversation])

    const handleDeleteClick = useCallback((phone, ev) => {
        if (ev) { ev.stopPropagation(); ev.preventDefault() }
        requestDeleteConversation(phone)
    }, [requestDeleteConversation])

    // Component unmount: bekleyen tum timer'lari temizle
    useEffect(() => {
        return () => {
            Object.values(deleteTimeoutsRef.current).forEach(clearTimeout)
            deleteTimeoutsRef.current = {}
        }
    }, [])

    // ── Dosya secince staged tut, hemen gonderme ─────────────────────────
    const stageFile = useCallback((file) => {
        if (!file) return
        if (file.size > 60 * 1024 * 1024) {
            showToast('Dosya 60MB\'dan büyük olamaz', 'warn')
            if (fileInputRef.current) fileInputRef.current.value = ''
            return
        }
        // Onceki preview URL'ini bosalt
        if (pendingPreviewUrl) URL.revokeObjectURL(pendingPreviewUrl)
        const isImage = file.type?.startsWith('image/')
        setPendingFile(file)
        setPendingPreviewUrl(isImage ? URL.createObjectURL(file) : null)
    }, [pendingPreviewUrl])

    const clearPendingFile = useCallback(() => {
        if (pendingPreviewUrl) URL.revokeObjectURL(pendingPreviewUrl)
        setPendingFile(null)
        setPendingPreviewUrl(null)
        if (fileInputRef.current) fileInputRef.current.value = ''
    }, [pendingPreviewUrl])

    // Component unmount: object URL'i bosalt
    useEffect(() => {
        return () => { if (pendingPreviewUrl) URL.revokeObjectURL(pendingPreviewUrl) }
    }, [pendingPreviewUrl])

    // ── Dosya gonder (caption ile birlikte) ───────────────────────────────
    const sendFile = useCallback(async (file, caption) => {
        if (!file || !selectedPhone || sending) return
        setSending(true)
        try {
            const fd = new FormData()
            fd.append('phone', selectedPhone)
            fd.append('caption', (caption || '').trim())
            fd.append('file', file)
            fd.append('__RequestVerificationToken', csrfToken || '')
            const r = await fetch('/Whatsapp/SendMedia', {
                method: 'POST',
                credentials: 'same-origin',
                body: fd,
            })
            const d = await r.json()
            if (d.success) {
                setComposeText('')
                clearPendingFile()
                setTimeout(() => loadMessages(selectedPhone), 800)
            } else {
                showToast('Dosya gönderilemedi: ' + (d.message || 'bilinmeyen hata'))
            }
        } catch (err) {
            showToast('Ag hatasi: ' + err.message)
        } finally {
            setSending(false)
        }
    }, [selectedPhone, sending, csrfToken, loadMessages, clearPendingFile, showToast])

    // ── Sade metin mesaj gonder ──────────────────────────────────────────
    const sendTextOnly = useCallback(async (text) => {
        if (!text || !selectedPhone || sending) return
        setSending(true)
        try {
            const r = await fetch('/Whatsapp/Send', {
                method: 'POST',
                credentials: 'same-origin',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': csrfToken || '',
                },
                body: JSON.stringify({ phone: selectedPhone, text }),
            })
            const d = await r.json()
            if (d.success) {
                setComposeText('')
                setTimeout(() => loadMessages(selectedPhone), 600)
            } else {
                showToast('Mesaj gonderilemedi: ' + (d.message || 'bilinmeyen hata'))
            }
        } catch (err) {
            showToast('Ag hatasi: ' + err.message)
        } finally {
            setSending(false)
        }
    }, [selectedPhone, sending, csrfToken, loadMessages, showToast])

    // Send butonu: dosya bekliyorsa dosya+caption, yoksa metin gonder
    const sendMessage = useCallback(async () => {
        if (sending || !selectedPhone) return
        const text = composeText.trim()
        if (pendingFile) {
            await sendFile(pendingFile, text)
        } else if (text) {
            await sendTextOnly(text)
        }
    }, [sending, selectedPhone, composeText, pendingFile, sendFile, sendTextOnly])

    const handleComposeKey = (e) => {
        if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault()
            sendMessage()
        }
    }

    const filtered = conversations.filter(c => {
        if (!searchTerm) return true
        const q = searchTerm.toLowerCase()
        return (c.displayName || '').toLowerCase().includes(q)
            || (c.phone || '').includes(q)
            || (c.lastBody || '').toLowerCase().includes(q)
    })

    const selectedConv = conversations.find(c => c.phone === selectedPhone)

    return (
        <div className="wa-msg-app">
            {/* Sayfa ortasinda custom toast — alert() yerine, browser-native popup'a alternatif */}
            {toast && (
                <div
                    className={'wa-msg-toast wa-msg-toast--' + toast.kind}
                    role="alert"
                    aria-live="assertive"
                    onClick={dismissToast}
                >
                    <div className="wa-msg-toast__icon" aria-hidden="true">
                        {toast.kind === 'error' ? '⚠' : toast.kind === 'warn' ? '⚠' : 'ℹ'}
                    </div>
                    <div className="wa-msg-toast__text">{toast.text}</div>
                    <button
                        type="button"
                        className="wa-msg-toast__close"
                        onClick={(e) => { e.stopPropagation(); dismissToast() }}
                        aria-label="Kapat"
                    >
                        ✕
                    </button>
                </div>
            )}
            {/* ───── Sol panel: sohbet listesi ──────────────────────── */}
            <div className="wa-msg-sidebar">
                <div className="wa-msg-sidebar__head">
                    <div className="wa-msg-sidebar__title">Sohbetler</div>
                    <input
                        type="text"
                        className="wa-msg-search"
                        placeholder="Sohbet veya kisi ara..."
                        value={searchTerm}
                        onChange={(e) => setSearchTerm(e.target.value)}
                    />
                </div>
                <div className="wa-msg-conv-list">
                    {filtered.length === 0 && (
                        <div className="wa-msg-empty">
                            {conversations.length === 0
                                ? 'Henuz sohbet yok. Bridge calisiyor mu, telefonundan mesaj geldi mi kontrol et.'
                                : 'Aramayla eslesen sohbet yok.'}
                        </div>
                    )}
                    {filtered.map(c => {
                        const isPending = pendingDelete[c.phone] === true
                        return (
                            <div
                                key={c.phone}
                                role="button"
                                tabIndex={0}
                                className={'wa-msg-conv' + (c.phone === selectedPhone ? ' is-active' : '') + (isPending ? ' is-pending-delete' : '')}
                                onClick={() => { if (!isPending) selectConversation(c.phone) }}
                                onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); if (!isPending) selectConversation(c.phone) } }}
                            >
                                <div className="wa-msg-conv__avatar" aria-hidden="true">
                                    {(c.displayName || formatPhoneOrLid(c.phone) || '?').slice(0, 1).toUpperCase()}
                                </div>
                                <div className="wa-msg-conv__body">
                                    <div className="wa-msg-conv__row">
                                        <span className="wa-msg-conv__name">{c.displayName || formatPhoneOrLid(c.phone)}</span>
                                        <span className="wa-msg-conv__time">{formatShortTime(c.lastAt)}</span>
                                    </div>
                                    <div className="wa-msg-conv__row">
                                        <span className="wa-msg-conv__snippet">
                                            {c.lastFromMe ? <span className="wa-msg-conv__me">Sen: </span> : null}
                                            {snippet(c.lastBody, c.lastMedia)}
                                        </span>
                                        {c.unread > 0 && (
                                            <span className="wa-msg-conv__badge">{c.unread > 99 ? '99+' : c.unread}</span>
                                        )}
                                    </div>
                                    {c.accountCode && (
                                        <div className="wa-msg-conv__cari">{c.accountCode}</div>
                                    )}
                                </div>
                                <button
                                    type="button"
                                    className={'wa-msg-conv__del' + (isPending ? ' is-pending' : '')}
                                    onClick={(e) => handleDeleteClick(c.phone, e)}
                                    title={isPending ? 'Silmeyi iptal et' : 'Sohbeti sil'}
                                    aria-label={isPending ? 'Silmeyi iptal et' : ('Sohbeti sil: ' + (c.displayName || c.phone))}
                                >
                                    {isPending ? '↺' : '✕'}
                                </button>
                                {isPending && (
                                    <div className="wa-msg-conv__deletebar" aria-hidden="true">
                                        <div className="wa-msg-conv__deletebar-fill" style={{ animationDuration: DELETE_COUNTDOWN_MS + 'ms' }} />
                                    </div>
                                )}
                            </div>
                        )
                    })}
                </div>
            </div>

            {/* ───── Sag panel: secili sohbet ──────────────────────── */}
            <div className="wa-msg-thread">
                {!selectedPhone && (
                    <div className="wa-msg-empty-thread">
                        <div className="wa-msg-empty-thread__icon">💬</div>
                        <div className="wa-msg-empty-thread__title">WhatsApp mesajlasma</div>
                        <div className="wa-msg-empty-thread__sub">Sol taraftan bir sohbet sec veya yeni gelen mesajlari bekle.</div>
                    </div>
                )}
                {selectedPhone && (
                    <>
                        <div className="wa-msg-thread__head">
                            <div className="wa-msg-thread__avatar">
                                {(selectedConv?.displayName || selectedPhone).slice(0, 1).toUpperCase()}
                            </div>
                            <div className="wa-msg-thread__info">
                                <div className="wa-msg-thread__name">{selectedConv?.displayName || formatPhoneOrLid(selectedPhone)}</div>
                                <div className="wa-msg-thread__phone">{formatPhoneOrLid(selectedPhone)}{selectedConv?.accountCode ? ' · ' + selectedConv.accountCode : ''}</div>
                            </div>
                        </div>
                        <div className="wa-msg-thread__body">
                            {messages.length === 0 && (
                                <div className="wa-msg-empty">Bu sohbette henuz mesaj yok.</div>
                            )}
                            {messages.map((m, i) => {
                                const showDate = i === 0 || !sameDay(m.at, messages[i-1].at)
                                return (
                                    <React.Fragment key={m.id}>
                                        {showDate && <div className="wa-msg-day">{formatDay(m.at)}</div>}
                                        <div className={'wa-msg-bubble' + (m.direction === 1 ? ' is-me' : '')}>
                                            {renderMediaContent(m)}
                                            {m.body && (
                                                <div className="wa-msg-bubble__text">{m.body}</div>
                                            )}
                                            <div className="wa-msg-bubble__time">{formatShortTime(m.at)}</div>
                                        </div>
                                    </React.Fragment>
                                )
                            })}
                            <div ref={threadEndRef} />
                        </div>
                        <div className="wa-msg-composer">
                            <input
                                ref={fileInputRef}
                                type="file"
                                style={{ display: 'none' }}
                                onChange={(e) => stageFile(e.target.files?.[0])}
                            />
                            {pendingFile && (
                                <div className="wa-msg-composer__pending">
                                    {pendingPreviewUrl ? (
                                        <img src={pendingPreviewUrl} alt="" className="wa-msg-composer__pending-thumb" />
                                    ) : (
                                        <span className="wa-msg-composer__pending-icon">📎</span>
                                    )}
                                    <span className="wa-msg-composer__pending-name" title={pendingFile.name}>
                                        {pendingFile.name}
                                    </span>
                                    <span className="wa-msg-composer__pending-size">
                                        {(pendingFile.size / 1024).toFixed(0)} KB
                                    </span>
                                    <button
                                        type="button"
                                        className="wa-msg-composer__pending-clear"
                                        onClick={clearPendingFile}
                                        disabled={sending}
                                        title="Dosyayi kaldir"
                                        aria-label="Eklenmis dosyayi kaldir"
                                    >
                                        ✕
                                    </button>
                                </div>
                            )}
                            <div className="wa-msg-composer__row">
                                <button
                                    className="wa-msg-composer__attach"
                                    onClick={() => fileInputRef.current?.click()}
                                    disabled={sending}
                                    title="Dosya/Resim ekle"
                                    aria-label="Dosya ekle"
                                >
                                    📎
                                </button>
                                <textarea
                                    className="wa-msg-composer__input"
                                    placeholder={pendingFile ? "Dosyaya not ekle (opsiyonel)..." : "Mesaj yaz... (Enter: gonder, Shift+Enter: yeni satir)"}
                                    value={composeText}
                                    onChange={(e) => setComposeText(e.target.value)}
                                    onKeyDown={handleComposeKey}
                                    rows={1}
                                    disabled={sending}
                                />
                                <button
                                    className="wa-msg-composer__send"
                                    onClick={sendMessage}
                                    disabled={(!composeText.trim() && !pendingFile) || sending}
                                    title={pendingFile ? "Dosyayi gonder" : "Gonder (Enter)"}
                                >
                                    {sending ? '...' : 'Gonder'}
                                </button>
                            </div>
                        </div>
                    </>
                )}
            </div>
        </div>
    )
}

// ── Yardimcilar ─────────────────────────────────────────────────────────
function renderMediaContent(m) {
    if (!m.hasMedia || !m.mediaUrl) return null
    const url = m.mediaUrl
    const fileName = m.mediaFileName || (m.mediaUrl.split('/').pop() || 'dosya')
    const sizeKb = m.mediaSize ? (m.mediaSize / 1024).toFixed(0) + ' KB' : ''

    switch (m.mediaType) {
        case 'image':
        case 'sticker':
            return (
                <a href={url} target="_blank" rel="noopener" className="wa-msg-bubble__media wa-msg-bubble__media--image" title="Tam boyut için tıkla">
                    <img src={url} alt="" loading="lazy" />
                </a>
            )
        case 'video':
            return (
                <video className="wa-msg-bubble__media wa-msg-bubble__media--video" controls preload="metadata">
                    <source src={url} type={m.mediaMime || 'video/mp4'} />
                </video>
            )
        case 'audio':
            return (
                <audio className="wa-msg-bubble__media wa-msg-bubble__media--audio" controls preload="metadata">
                    <source src={url} type={m.mediaMime || 'audio/ogg'} />
                </audio>
            )
        case 'document':
        default:
            return (
                <a href={url} target="_blank" rel="noopener" download={fileName}
                   className="wa-msg-bubble__media wa-msg-bubble__media--doc">
                    <span className="wa-msg-doc__icon">📎</span>
                    <span className="wa-msg-doc__body">
                        <span className="wa-msg-doc__name">{fileName}</span>
                        {sizeKb && <span className="wa-msg-doc__size">{sizeKb}</span>}
                    </span>
                </a>
            )
    }
}

/**
 * Telefon mu LID mi: 10-14 basamak gercek E.164 telefon, 15+ basamak WhatsApp LID.
 * UI'da telefon gibi gostermek (+208...) yanlis bilgi verir, bu yuzden ayri etiket dondururuz.
 */
function formatPhoneOrLid(s) {
    if (!s) return ''
    const digits = String(s).replace(/[^\d]/g, '')
    if (digits.length >= 10 && digits.length <= 14) return '+' + digits
    return '(WhatsApp ID)'
}

function snippet(body, mediaType) {
    if (body && body.trim()) return body.length > 60 ? body.slice(0, 60) + '...' : body
    if (mediaType && mediaType !== 'chat') {
        const labels = {
            image: '📷 Resim', video: '🎥 Video', audio: '🎵 Ses',
            document: '📎 Doküman', sticker: '😀 Sticker', location: '📍 Konum',
        }
        return labels[mediaType] || '[' + mediaType + ']'
    }
    return ''
}

function formatShortTime(iso) {
    if (!iso) return ''
    const d = new Date(iso)
    if (isNaN(d.getTime())) return ''
    const now = new Date()
    const sameDayCheck = d.toDateString() === now.toDateString()
    if (sameDayCheck) {
        return d.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })
    }
    const yesterday = new Date(now); yesterday.setDate(yesterday.getDate() - 1)
    if (d.toDateString() === yesterday.toDateString()) return 'Dun'
    const dayDiff = (now - d) / (1000 * 60 * 60 * 24)
    if (dayDiff < 7) {
        return d.toLocaleDateString('tr-TR', { weekday: 'short' })
    }
    return d.toLocaleDateString('tr-TR', { day: '2-digit', month: '2-digit' })
}

function formatDay(iso) {
    if (!iso) return ''
    const d = new Date(iso)
    const now = new Date()
    if (d.toDateString() === now.toDateString()) return 'Bugun'
    const yesterday = new Date(now); yesterday.setDate(yesterday.getDate() - 1)
    if (d.toDateString() === yesterday.toDateString()) return 'Dun'
    return d.toLocaleDateString('tr-TR', { day: 'numeric', month: 'long', year: 'numeric' })
}

function sameDay(a, b) {
    if (!a || !b) return false
    return new Date(a).toDateString() === new Date(b).toDateString()
}

export default WhatsAppMessenger
