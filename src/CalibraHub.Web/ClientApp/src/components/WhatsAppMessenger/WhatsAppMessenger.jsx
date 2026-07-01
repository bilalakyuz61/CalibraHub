import React, { useEffect, useState, useRef, useCallback } from 'react'
import * as signalR from '@microsoft/signalr'

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
    const [toast, setToast] = useState(null) // { kind: 'error'|'warn'|'info', text } — sayfa ortasinda custom uyari
    // Real-time: typing, presence, delivery ticks
    const [peerIsTyping, setPeerIsTyping] = useState(false)
    const [selectedPresence, setSelectedPresence] = useState(null) // { status, lastSeen }
    const [messageStatusMap, setMessageStatusMap] = useState({}) // { [msgId]: 'sent'|'delivered'|'read' }
    // Faz 3: reply, reaction, search
    const [replyingTo, setReplyingTo] = useState(null) // { id, bridgeMsgId, body, direction } veya null
    const [reactionTarget, setReactionTarget] = useState(null) // { bridgeMsgId, fromMe, phone } veya null
    const [chatSearch, setChatSearch] = useState('') // sohbet içi arama metni
    const [chatSearchActive, setChatSearchActive] = useState(false)
    // Faz 4: filtre sekmesi
    const [filterTab, setFilterTab] = useState('all') // 'all' | 'unread' | 'groups'
    // Faz 5: silme onay modal
    const [deleteConfirm, setDeleteConfirm] = useState(null) // { phone, displayName } | null
    // Aktif mesaj menüsü (tıklanan mesajın id'si)
    const [activeMsgId, setActiveMsgId] = useState(null)
    // Ek dosya menüsü açık mı
    const [attachMenuOpen, setAttachMenuOpen] = useState(false)

    // ── Yeni özellikler: localStorage tabanlı ────────────────────────
    const [starredMsgs, setStarredMsgs] = useState(() => {
        try { return new Set(JSON.parse(localStorage.getItem('wa-starred') || '[]')) } catch { return new Set() }
    })
    const [archivedConvs, setArchivedConvs] = useState(() => {
        try { return new Set(JSON.parse(localStorage.getItem('wa-archived') || '[]')) } catch { return new Set() }
    })
    const [mutedConvs, setMutedConvs] = useState(() => {
        try { return new Set(JSON.parse(localStorage.getItem('wa-muted') || '[]')) } catch { return new Set() }
    })
    const [showArchived, setShowArchived] = useState(false)
    // Sohbet sağ-tık menüsü
    const [convMenu, setConvMenu] = useState(null) // { phone, displayName, x, y } | null
    // Grup bilgi paneli
    const [groupInfoOpen, setGroupInfoOpen] = useState(false)
    const [groupMembers, setGroupMembers] = useState([])
    // Lightbox (tam ekran medya)
    const [lightbox, setLightbox] = useState(null) // { url, type, fileName } | null
    // Ses kaydı
    const [recording, setRecording] = useState(false)
    const [recordingSecs, setRecordingSecs] = useState(0)
    const mediaRecorderRef = useRef(null)
    const recordingTimerRef = useRef(null)
    const recordingChunksRef = useRef([])
    // Mesaj iletme
    const [forwardMsg, setForwardMsg] = useState(null) // { bridgeMsgId, body } | null
    // Mesaj bilgisi
    const [msgInfoMsg, setMsgInfoMsg] = useState(null) // message object | null
    // Seçim modu
    const [selectMode, setSelectMode] = useState(false)
    const [selectedMsgs, setSelectedMsgs] = useState(new Set())
    // Yıldızlı mesajlar paneli
    const [starredPanel, setStarredPanel] = useState(false)
    // Sohbeti temizle onay modalı
    const [clearChatConfirm, setClearChatConfirm] = useState(null) // { phone, displayName } | null
    // Kişi arama sonuçları (mevcut sohbet olmayan kayıtlı kişiler)
    const [contactResults, setContactResults] = useState([])
    const contactSearchTimerRef = useRef(null)

    const threadEndRef = useRef(null)
    const scrollInstantRef = useRef(false) // kişi değişince true → bir sonraki mesaj yüklemesinde anlık scroll
    const awaitingMediaScrollRef = useRef(false) // kişi geçişinden 3sn: medya yüklenince scroll devam et
    const pollerRef = useRef(null)
    const fileInputRef = useRef(null)
    const imageInputRef = useRef(null)
    const textareaRef = useRef(null)
    const toastTimerRef = useRef(null)
    const hubRef = useRef(null)
    const typingTimerRef = useRef(null)
    const selectedPhoneRef = useRef(selectedPhone) // hub callback'lerinde güncel phone'a erişim
    const mutedConvsRef = useRef(mutedConvs) // hub callback'lerinde güncel mutedConvs'a erişim

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
    // Emoji seçince cursor konumuna ekle, textarea focus'u koru
    const insertEmoji = useCallback((emoji) => {
        const el = textareaRef.current
        if (!el) { setComposeText(prev => prev + emoji); return }
        const start = el.selectionStart ?? el.value.length
        const end   = el.selectionEnd   ?? el.value.length
        setComposeText(prev => prev.slice(0, start) + emoji + prev.slice(end))
        requestAnimationFrame(() => {
            el.focus()
            const pos = start + [...emoji].length
            el.selectionStart = el.selectionEnd = pos
        })
    }, [])
    useEffect(() => {
        return () => { if (toastTimerRef.current) clearTimeout(toastTimerRef.current) }
    }, [])

    // mutedConvsRef güncel tut
    useEffect(() => { mutedConvsRef.current = mutedConvs }, [mutedConvs])

    // Dışarı tıklayınca aktif mesaj menüsünü + reaksiyon picker'ı + ek menüsünü + sağ tık menüsünü kapat
    useEffect(() => {
        const close = () => { setActiveMsgId(null); setReactionTarget(null); setAttachMenuOpen(false); setConvMenu(null) }
        document.addEventListener('click', close)
        return () => document.removeEventListener('click', close)
    }, [])

    // Seçili phone ref'ini güncel tut (hub callback closure problemi)
    useEffect(() => { selectedPhoneRef.current = selectedPhone }, [selectedPhone])

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

    // ── Kişi arama (searchTerm değişince tetiklenir) ─────────────────────
    const searchContactsByTerm = useCallback((q, existingConvPhones) => {
        clearTimeout(contactSearchTimerRef.current)
        if (!q || q.trim().length < 1) { setContactResults([]); return }
        contactSearchTimerRef.current = setTimeout(async () => {
            try {
                const r = await fetch('/Whatsapp/ContactSearch?q=' + encodeURIComponent(q.trim()), { credentials: 'same-origin' })
                const data = await r.json()
                // Zaten sohbette olan kişileri çıkar
                const filtered = (Array.isArray(data) ? data : []).filter(c => !existingConvPhones.has(c.phone))
                setContactResults(filtered)
            } catch { setContactResults([]) }
        }, 350)
    }, [])

    // ── SignalR hub bağlantısı ────────────────────────────────────────────
    useEffect(() => {
        const hub = new signalR.HubConnectionBuilder()
            .withUrl('/hubs/whatsapp')
            .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
            .configureLogging(signalR.LogLevel.Warning)
            .build()

        hub.on('MessageReceived', (msg) => {
            const phone = selectedPhoneRef.current
            if (msg.phone === phone) {
                setMessages(prev => {
                    // Dedup: aynı mesaj varsa ekleme.
                    // m.id → DB integer; msg.id → bridge string ID.
                    // loadMessages sonrası DB kayıtları m.bridgeMsgId taşır → onu da kontrol et.
                    if (prev.some(m => m.id === msg.id || m.bridgeMsgId === msg.id)) return prev
                    return [...prev, {
                        id: msg.id, bridgeMsgId: msg.id,
                        direction: msg.direction, body: msg.body,
                        mediaType: msg.mediaType, hasMedia: msg.hasMedia,
                        mediaUrl: msg.mediaUrl, mediaMime: msg.mediaMime,
                        mediaFileName: msg.mediaFileName, mediaSize: msg.mediaSize,
                        at: msg.at, readAt: null,
                    }]
                })
            }
            // Konuşma listesini güncelle (her yeni mesajda)
            loadConversations()
            // Tarayıcı bildirimi — pencere gizliyse ve sessize alınmamışsa
            if (msg.direction === 0 && document.hidden && !mutedConvsRef.current.has(msg.phone)
                && typeof Notification !== 'undefined' && Notification.permission === 'granted') {
                try {
                    new Notification('Yeni WhatsApp mesajı', {
                        body: msg.body || (msg.hasMedia ? `[${msg.mediaType}]` : 'Mesaj'),
                        icon: '/favicon.ico',
                    })
                } catch { /* sessizce geç */ }
            }
        })

        hub.on('ConversationUpdated', () => { loadConversations() })

        hub.on('TypingUpdated', ({ phone, isTyping }) => {
            if (phone === selectedPhoneRef.current) setPeerIsTyping(!!isTyping)
        })

        hub.on('PresenceUpdated', ({ phone, status, lastSeen }) => {
            if (phone === selectedPhoneRef.current) setSelectedPresence({ status, lastSeen })
        })

        hub.on('MessageStatusUpdated', ({ messageId, status }) => {
            setMessageStatusMap(prev => ({ ...prev, [messageId]: status }))
        })

        hub.on('ReactionUpdated', ({ targetMsgId, emoji }) => {
            setMessages(prev => prev.map(msg =>
                msg.bridgeMsgId === targetMsgId
                    ? { ...msg, reactionEmoji: emoji || null }
                    : msg
            ))
        })

        hub.start().catch(err => console.warn('[WaHub] Bağlantı hatası:', err))
        hubRef.current = hub

        return () => {
            hub.stop()
            hubRef.current = null
        }
    }, [loadConversations])

    // ── Tab title: toplam okunmamış sayısı ──────────────────────────────
    useEffect(() => {
        const total = conversations.reduce((s, c) => s + (c.unread || 0), 0)
        document.title = total > 0 ? `(${total}) WhatsApp – CalibraHub` : 'WhatsApp – CalibraHub'
        return () => { document.title = 'CalibraHub' }
    }, [conversations])

    // ── Browser bildirimi: yeni mesaj gelince (pencere gizliyse) ────────
    useEffect(() => {
        if (typeof Notification !== 'undefined' && Notification.permission === 'default') {
            Notification.requestPermission().catch(() => {})
        }
    }, [])

    // ── Polling: her 3sn'de hem listeyi hem acik sohbeti yenile ─────────
    useEffect(() => {
        loadConversations()
        if (selectedPhone) loadMessages(selectedPhone)
        if (pollerRef.current) clearInterval(pollerRef.current)
        // Hub gerçek zamanlı mesajları halleder; polling sadece catch-up senkronizasyonu
        pollerRef.current = setInterval(() => {
            if (document.hidden) return
            loadConversations()
            if (selectedPhone) loadMessages(selectedPhone)
        }, 30000)
        return () => {
            if (pollerRef.current) clearInterval(pollerRef.current)
        }
    }, [selectedPhone, loadConversations, loadMessages])

    // ── Kişi değişince bir sonraki scroll anlık olsun + input'a focus ───
    useEffect(() => {
        scrollInstantRef.current = true
        awaitingMediaScrollRef.current = true
        const t = setTimeout(() => { awaitingMediaScrollRef.current = false }, 3000)
        if (selectedPhone) setTimeout(() => textareaRef.current?.focus(), 50)
        return () => clearTimeout(t)
    }, [selectedPhone])

    // ── Yeni mesaj gelince otomatik aşağı kaydir ────────────────────────
    useEffect(() => {
        if (!threadEndRef.current || !messages.length) return
        const behavior = scrollInstantRef.current ? 'auto' : 'smooth'
        scrollInstantRef.current = false
        threadEndRef.current.scrollIntoView({ behavior, block: 'end' })
    }, [messages])

    // ── Sohbet secince okundu isaretle + state sifirla ──────────────────
    const selectConversation = useCallback(async (phone) => {
        setSelectedPhone(phone)
        setPeerIsTyping(false)
        setSelectedPresence(null)
        setReplyingTo(null)
        setReactionTarget(null)
        setChatSearch('')
        setChatSearchActive(false)
        setGroupInfoOpen(false)
        setGroupMembers([])
        setStarredPanel(false)
        setSelectMode(false)
        setSelectedMsgs(new Set())
        try {
            await fetch('/Whatsapp/MarkRead?phone=' + encodeURIComponent(phone), {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'RequestVerificationToken': csrfToken || '' },
            })
            loadConversations()
        } catch { /* sessizce gec */ }

        // Presence subscribe — bridge'e haber ver, SSE üzerinden güncel durum gelecek
        try {
            await fetch('/Whatsapp/SendTyping?phone=' + encodeURIComponent(phone) + '&isTyping=false', {
                method: 'POST', credentials: 'same-origin',
                headers: { 'RequestVerificationToken': csrfToken || '' },
            })
        } catch { /* opsiyonel */ }
    }, [csrfToken, loadConversations])

    const reallyDeleteConversation = useCallback(async (phone) => {
        // Optimistik: istek tamamlanmadan önce listeden kaldır
        setConversations(prev => prev.filter(c => c.phone !== phone))
        if (selectedPhone === phone) { setSelectedPhone(null); setMessages([]) }
        try {
            const r = await fetch('/Whatsapp/DeleteConversation?phone=' + encodeURIComponent(phone), {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'RequestVerificationToken': csrfToken || '' },
            })
            const d = await r.json()
            if (d.success) {
                loadConversations()
            } else {
                showToast('Silinemedi: ' + (d.message || 'bilinmeyen hata'))
                loadConversations() // başarısız → listeyi orijinal haline getir
            }
        } catch (err) {
            showToast('Ag hatasi: ' + err.message)
            loadConversations()
        }
    }, [csrfToken, loadConversations, selectedPhone, showToast])

    const requestDeleteConversation = useCallback((phone, displayName) => {
        if (!phone) return
        setDeleteConfirm({ phone, displayName: displayName || phone })
    }, [])

    const handleDeleteClick = useCallback((phone, displayName, ev) => {
        if (ev) { ev.stopPropagation(); ev.preventDefault() }
        requestDeleteConversation(phone, displayName)
    }, [requestDeleteConversation])

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

    // ── Sade metin / alıntılı yanıt gönder ─────────────────────────────
    const sendTextOnly = useCallback(async (text) => {
        if (!text || !selectedPhone || sending) return
        setSending(true)
        try {
            const isReply = !!replyingTo?.bridgeMsgId
            const url = isReply ? '/Whatsapp/SendReply' : '/Whatsapp/Send'
            const payload = isReply
                ? { phone: selectedPhone, text, quotedId: replyingTo.bridgeMsgId, quotedBody: replyingTo.body, quotedFromMe: replyingTo.direction === 1 }
                : { phone: selectedPhone, text }
            const r = await fetch(url, {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': csrfToken || '' },
                body: JSON.stringify(payload),
            })
            const d = await r.json()
            if (d.success) {
                setComposeText('')
                setReplyingTo(null)
                setTimeout(() => loadMessages(selectedPhone), 600)
            } else {
                showToast('Mesaj gonderilemedi: ' + (d.message || 'bilinmeyen hata'))
            }
        } catch (err) {
            showToast('Ag hatasi: ' + err.message)
        } finally {
            setSending(false)
        }
    }, [selectedPhone, sending, csrfToken, loadMessages, showToast, replyingTo])

    // ── Reaksiyon gönder ────────────────────────────────────────────────
    const sendReaction = useCallback(async (bridgeMsgId, fromMe, emoji) => {
        if (!selectedPhone) return
        setReactionTarget(null)
        try {
            await fetch('/Whatsapp/SendReaction', {
                method: 'POST', credentials: 'same-origin',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': csrfToken || '' },
                body: JSON.stringify({ phone: selectedPhone, messageId: bridgeMsgId, emoji, fromMe }),
            })
            setMessages(prev => prev.map(m =>
                m.bridgeMsgId === bridgeMsgId ? { ...m, reactionEmoji: emoji || null } : m))
        } catch { /* sessizce geç */ }
    }, [selectedPhone, csrfToken])

    // ── Mesaj sil ───────────────────────────────────────────────────────
    const deleteMessage = useCallback(async (bridgeMsgId, fromMe) => {
        if (!selectedPhone) return
        try {
            await fetch('/Whatsapp/DeleteMessage', {
                method: 'POST', credentials: 'same-origin',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': csrfToken || '' },
                body: JSON.stringify({ phone: selectedPhone, messageId: bridgeMsgId, fromMe }),
            })
            setMessages(prev => prev.map(m =>
                m.bridgeMsgId === bridgeMsgId ? { ...m, isDeleted: true, body: null } : m))
        } catch (err) { showToast('Silinemedi: ' + err.message) }
    }, [selectedPhone, csrfToken, showToast])

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

    // Yazıyor göstergesi gönder (debounced — 3sn sonra "durdu")
    const sendTypingIndicator = useCallback((isTyping) => {
        if (!selectedPhone) return
        fetch('/Whatsapp/SendTyping?phone=' + encodeURIComponent(selectedPhone) + '&isTyping=' + isTyping, {
            method: 'POST', credentials: 'same-origin',
            headers: { 'RequestVerificationToken': csrfToken || '' },
        }).catch(() => {})
    }, [selectedPhone, csrfToken])

    const handleComposeKey = (e) => {
        if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault()
            if (typingTimerRef.current) { clearTimeout(typingTimerRef.current); typingTimerRef.current = null }
            sendTypingIndicator(false)
            sendMessage()
            return
        }
        // Yazıyor sinyali — her tuş basışında timer'ı sıfırla
        if (typingTimerRef.current) clearTimeout(typingTimerRef.current)
        sendTypingIndicator(true)
        typingTimerRef.current = setTimeout(() => {
            sendTypingIndicator(false)
            typingTimerRef.current = null
        }, 3000)
    }

    // ── Yeni helper fonksiyonlar ─────────────────────────────────────────

    // Mesaj bilgisi
    const showMsgInfo = useCallback((msg) => { setMsgInfoMsg(msg) }, [])

    // Yıldızlama
    const toggleStar = useCallback((bridgeMsgId) => {
        setStarredMsgs(prev => {
            const next = new Set(prev)
            if (next.has(bridgeMsgId)) next.delete(bridgeMsgId)
            else next.add(bridgeMsgId)
            localStorage.setItem('wa-starred', JSON.stringify([...next]))
            return next
        })
    }, [])

    // Arşivleme
    const toggleArchive = useCallback((phone) => {
        setArchivedConvs(prev => {
            const next = new Set(prev)
            if (next.has(phone)) next.delete(phone)
            else next.add(phone)
            localStorage.setItem('wa-archived', JSON.stringify([...next]))
            return next
        })
        setConvMenu(null)
    }, [])

    // Sessize alma
    const toggleMute = useCallback((phone) => {
        setMutedConvs(prev => {
            const next = new Set(prev)
            if (next.has(phone)) next.delete(phone)
            else next.add(phone)
            localStorage.setItem('wa-muted', JSON.stringify([...next]))
            return next
        })
        setConvMenu(null)
    }, [])

    // Okunmamış işaretleme
    const markUnread = useCallback(async (phone) => {
        setConvMenu(null)
        try {
            await fetch('/Whatsapp/MarkUnread?phone=' + encodeURIComponent(phone), {
                method: 'POST', credentials: 'same-origin',
                headers: { 'RequestVerificationToken': csrfToken || '' },
            })
            loadConversations()
        } catch { /* sessizce geç */ }
    }, [csrfToken, loadConversations])

    // Sohbeti temizle
    const clearChat = useCallback(async (phone) => {
        setClearChatConfirm(null)
        try {
            const r = await fetch('/Whatsapp/ClearChat?phone=' + encodeURIComponent(phone), {
                method: 'POST', credentials: 'same-origin',
                headers: { 'RequestVerificationToken': csrfToken || '' },
            })
            const d = await r.json()
            if (d.success) {
                if (selectedPhone === phone) setMessages([])
                loadConversations()
            }
        } catch (err) { showToast('Temizlenemedi: ' + err.message) }
    }, [csrfToken, selectedPhone, loadConversations, showToast])

    // Grup üyelerini yükle
    const loadGroupMembers = useCallback(async (groupJid) => {
        if (!groupJid) return
        try {
            const r = await fetch('/Whatsapp/GroupMembers?groupJid=' + encodeURIComponent(groupJid), {
                credentials: 'same-origin'
            })
            const data = await r.json()
            setGroupMembers(Array.isArray(data) ? data : [])
        } catch { setGroupMembers([]) }
    }, [])

    // Ses kaydı başlat
    const startRecording = useCallback(async () => {
        if (recording || !selectedPhone) return
        try {
            const stream = await navigator.mediaDevices.getUserMedia({ audio: true })
            const mr = new MediaRecorder(stream, { mimeType: 'audio/webm' })
            recordingChunksRef.current = []
            mr.ondataavailable = e => { if (e.data.size > 0) recordingChunksRef.current.push(e.data) }
            mr.onstop = async () => {
                stream.getTracks().forEach(t => t.stop())
                const blob = new Blob(recordingChunksRef.current, { type: 'audio/webm' })
                if (blob.size > 0) {
                    const file = new File([blob], 'ses-kaydi.webm', { type: 'audio/webm' })
                    await sendFile(file, '')
                }
                setRecording(false)
                setRecordingSecs(0)
            }
            mr.start()
            mediaRecorderRef.current = mr
            setRecording(true)
            setRecordingSecs(0)
            recordingTimerRef.current = setInterval(() => {
                setRecordingSecs(s => s + 1)
            }, 1000)
        } catch { showToast('Mikrofona erişilemiyor', 'warn') }
    }, [recording, selectedPhone, sendFile, showToast])

    const stopRecording = useCallback(() => {
        if (recordingTimerRef.current) { clearInterval(recordingTimerRef.current); recordingTimerRef.current = null }
        if (mediaRecorderRef.current && mediaRecorderRef.current.state !== 'inactive') {
            mediaRecorderRef.current.stop()
        }
    }, [])

    // Mesajı ilet
    const doForward = useCallback(async (toPhone) => {
        if (!forwardMsg || !toPhone) return
        try {
            const r = await fetch('/Whatsapp/ForwardMessage', {
                method: 'POST', credentials: 'same-origin',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': csrfToken || '' },
                body: JSON.stringify({ toPhone, messageId: forwardMsg.bridgeMsgId }),
            })
            const d = await r.json()
            if (d.success) { showToast('İletildi', 'info'); setForwardMsg(null) }
            else showToast('İletilemedi: ' + (d.message || 'hata'))
        } catch (err) { showToast('Hata: ' + err.message) }
    }, [forwardMsg, csrfToken, showToast])

    // Seçim modu toggle
    const toggleSelectMsg = useCallback((id) => {
        setSelectedMsgs(prev => {
            const next = new Set(prev)
            if (next.has(id)) next.delete(id)
            else next.add(id)
            return next
        })
    }, [])

    const exitSelectMode = useCallback(() => {
        setSelectMode(false)
        setSelectedMsgs(new Set())
    }, [])

    const filtered = conversations.filter(c => {
        // Arşiv filtresi
        const isArchived = archivedConvs.has(c.phone)
        if (!showArchived && isArchived) return false
        if (showArchived && !isArchived) return false
        // Sekme filtresi
        if (filterTab === 'unread' && !(c.unread > 0)) return false
        if (filterTab === 'groups' && !c.isGroup) return false
        // Metin arama
        if (!searchTerm) return true
        const q = searchTerm.toLowerCase()
        return (c.displayName || '').toLowerCase().includes(q)
            || (c.phone || '').includes(q)
            || (c.lastBody || '').toLowerCase().includes(q)
    })

    const selectedConv = conversations.find(c => c.phone === selectedPhone)

    return (
        <div className={'wa-msg-app' + (groupInfoOpen && selectedConv?.isGroup ? ' group-panel-open' : '')}>
            {/* ── CLAUDE.md standart silme onay modal'ı ────────────────────────── */}
            {deleteConfirm && (
                <div className="wa-delete-backdrop" onClick={() => setDeleteConfirm(null)}>
                    <div className="wa-delete-card" role="dialog" aria-modal="true" onClick={e => e.stopPropagation()}
                        onKeyDown={e => e.key === 'Escape' && setDeleteConfirm(null)}>
                        <div className="wa-delete-card__icon" aria-hidden="true">🗑️</div>
                        <div className="wa-delete-card__title">Sohbeti sil</div>
                        <div className="wa-delete-card__msg">
                            <strong>{deleteConfirm.displayName}</strong> ile olan tüm mesajlar silinecek. Bu işlem geri alınamaz.
                        </div>
                        <div className="wa-delete-card__btns">
                            <button className="wa-delete-card__btn wa-delete-card__btn--cancel"
                                onClick={() => setDeleteConfirm(null)}>Vazgeç</button>
                            <button className="wa-delete-card__btn wa-delete-card__btn--danger" autoFocus
                                onClick={async () => {
                                    const { phone } = deleteConfirm
                                    setDeleteConfirm(null)
                                    await reallyDeleteConversation(phone)
                                }}>Sil</button>
                        </div>
                    </div>
                </div>
            )}
            {/* ── Sohbeti temizle onay modalı ──────────────────────────────────── */}
            {clearChatConfirm && (
                <div className="wa-delete-backdrop" onClick={() => setClearChatConfirm(null)}>
                    <div className="wa-delete-card" role="dialog" aria-modal="true" onClick={e => e.stopPropagation()}>
                        <div className="wa-delete-card__icon" aria-hidden="true">🗑️</div>
                        <div className="wa-delete-card__title">Sohbeti Temizle</div>
                        <div className="wa-delete-card__msg">
                            <strong>{clearChatConfirm.displayName}</strong> ile olan tüm mesajlar silinecek, sohbet listede kalacak. Bu işlem geri alınamaz.
                        </div>
                        <div className="wa-delete-card__btns">
                            <button className="wa-delete-card__btn wa-delete-card__btn--cancel"
                                onClick={() => setClearChatConfirm(null)}>Vazgeç</button>
                            <button className="wa-delete-card__btn wa-delete-card__btn--danger" autoFocus
                                onClick={async () => { await clearChat(clearChatConfirm.phone) }}>Temizle</button>
                        </div>
                    </div>
                </div>
            )}
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
            {/* ── Sohbet menüsü ───────────────────────────────────────────────── */}
            {convMenu && (
                <div className="wa-conv-menu" style={{ top: convMenu.y, left: convMenu.x }}
                    onClick={e => e.stopPropagation()}>
                    <button className="wa-conv-menu__item" onClick={() => { toggleArchive(convMenu.phone); setConvMenu(null) }}>
                        <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                            <polyline points="21 8 21 21 3 21 3 8"/><rect x="1" y="3" width="22" height="5"/><line x1="10" y1="12" x2="14" y2="12"/>
                        </svg>
                        <span>{archivedConvs.has(convMenu.phone) ? 'Arşivden çıkar' : 'Sohbeti arşivle'}</span>
                    </button>
                    <button className="wa-conv-menu__item" onClick={() => { toggleMute(convMenu.phone); setConvMenu(null) }}>
                        {mutedConvs.has(convMenu.phone) ? (
                            <><svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                                <path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9"/><path d="M13.73 21a2 2 0 0 1-3.46 0"/>
                            </svg><span>Bildirimleri aç</span></>
                        ) : (
                            <><svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                                <path d="M13.73 21a2 2 0 0 1-3.46 0"/><path d="M18.63 13A17.89 17.89 0 0 1 18 8"/>
                                <path d="M6.26 6.26A5.86 5.86 0 0 0 6 8c0 7-3 9-3 9h14"/><path d="M18 8a6 6 0 0 0-9.33-5"/>
                                <line x1="1" y1="1" x2="23" y2="23"/>
                            </svg><span>Bildirimleri sessize al</span></>
                        )}
                    </button>
                    <button className="wa-conv-menu__item" onClick={() => { markUnread(convMenu.phone) }}>
                        <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                            <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/>
                        </svg>
                        <span>Okunmadı olarak işaretle</span>
                    </button>
                    <div className="wa-conv-menu__divider" />
                    <button className="wa-conv-menu__item" onClick={() => { setClearChatConfirm({ phone: convMenu.phone, displayName: convMenu.displayName }); setConvMenu(null) }}>
                        <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                            <polyline points="3 6 5 6 21 6"/><path d="M19 6l-1 14H6L5 6"/>
                            <path d="M10 11v6"/><path d="M14 11v6"/><path d="M9 6V4h6v2"/>
                        </svg>
                        <span>Sohbeti temizle</span>
                    </button>
                    <button className="wa-conv-menu__item wa-conv-menu__item--danger" onClick={() => { handleDeleteClick(convMenu.phone, convMenu.displayName, null); setConvMenu(null) }}>
                        <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                            <polyline points="3 6 5 6 21 6"/><path d="M19 6l-1 14H6L5 6"/>
                            <path d="M10 11v6"/><path d="M14 11v6"/><path d="M9 6V4h6v2"/>
                        </svg>
                        <span>Sohbeti sil</span>
                    </button>
                </div>
            )}
            {/* ── Mesajı ilet overlay ──────────────────────────────────────────── */}
            {forwardMsg && (
                <div className="wa-forward-overlay" onClick={() => setForwardMsg(null)}>
                    <div className="wa-forward-panel" onClick={e => e.stopPropagation()}>
                        <div className="wa-forward-panel__head">
                            <span>↗ Mesajı İlet</span>
                            <button onClick={() => setForwardMsg(null)}>✕</button>
                        </div>
                        <div className="wa-forward-panel__preview">
                            "{forwardMsg.body?.slice(0, 80) || '[medya]'}"
                        </div>
                        <div className="wa-forward-panel__body">
                            {conversations.map(c => (
                                <div key={c.phone} className="wa-forward-panel__item"
                                    onClick={() => doForward(c.phone)}>
                                    <div className={'wa-msg-conv__avatar' + (c.isGroup ? ' is-group' : '')}>
                                        {c.isGroup ? '👥' : (c.displayName || c.phone).slice(0, 1).toUpperCase()}
                                    </div>
                                    <span>{c.displayName || c.phone}</span>
                                </div>
                            ))}
                        </div>
                    </div>
                </div>
            )}
            {/* ── Mesaj bilgisi modal ───────────────────────────────────────────── */}
            {msgInfoMsg && (() => {
                const status = messageStatusMap[msgInfoMsg.id] || msgInfoMsg.deliveryStatus || 'sent'
                const sentAt = msgInfoMsg.at ? new Date(msgInfoMsg.at) : null
                const fmtFull = (d) => d ? d.toLocaleDateString('tr-TR', { day: '2-digit', month: '2-digit', year: 'numeric' }) + ' ' + d.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' }) : '—'
                return (
                    <div className="wa-msginfo-backdrop" onClick={() => setMsgInfoMsg(null)}>
                        <div className="wa-msginfo-card" onClick={e => e.stopPropagation()}>
                            <div className="wa-msginfo-card__head">
                                <span>Mesaj bilgisi</span>
                                <button className="wa-msginfo-card__close" onClick={() => setMsgInfoMsg(null)}>✕</button>
                            </div>
                            <div className="wa-msginfo-card__preview">
                                {msgInfoMsg.hasMedia
                                    ? <span className="wa-msginfo-card__media-label">[{msgInfoMsg.mediaType}]</span>
                                    : <span>{msgInfoMsg.body}</span>
                                }
                            </div>
                            <div className="wa-msginfo-card__rows">
                                <div className="wa-msginfo-card__row">
                                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round"><polyline points="20 6 9 17 4 12"/></svg>
                                    <span>Gönderildi</span>
                                    <span className="wa-msginfo-card__time">{fmtFull(sentAt)}</span>
                                </div>
                                {(status === 'delivered' || status === 'read') && (
                                    <div className="wa-msginfo-card__row">
                                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round"><polyline points="20 6 9 17 4 12"/><polyline points="17 6 6 17 1 12"/></svg>
                                        <span>İletildi</span>
                                        <span className="wa-msginfo-card__time">—</span>
                                    </div>
                                )}
                                {status === 'read' && (
                                    <div className="wa-msginfo-card__row wa-msginfo-card__row--read">
                                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#53bdeb" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round"><polyline points="20 6 9 17 4 12"/><polyline points="17 6 6 17 1 12"/></svg>
                                        <span>Okundu</span>
                                        <span className="wa-msginfo-card__time">—</span>
                                    </div>
                                )}
                            </div>
                        </div>
                    </div>
                )
            })()}
            {/* ── Lightbox ─────────────────────────────────────────────────────── */}
            {lightbox && (
                <div className="wa-lightbox" onClick={() => setLightbox(null)}>
                    <button className="wa-lightbox__close" onClick={() => setLightbox(null)}>✕</button>
                    <a className="wa-lightbox__download" href={lightbox.url} download={lightbox.fileName || 'resim'}
                        onClick={e => e.stopPropagation()} title="İndir">⬇</a>
                    <img src={lightbox.url} alt="" className="wa-lightbox__img" onClick={e => e.stopPropagation()} />
                </div>
            )}
            {/* ───── Sol panel: sohbet listesi ──────────────────────── */}
            <div className="wa-msg-sidebar">
                <div className="wa-msg-sidebar__head">
                    <div className="wa-msg-sidebar__title-row">
                        <div className="wa-msg-sidebar__title">Sohbetler</div>
                        <div className="wa-sidebar-actions">
                            <button className={'wa-sidebar-action' + (starredPanel ? ' is-active' : '')}
                                onClick={() => setStarredPanel(v => !v)}
                                title="Yıldızlı mesajlar">
                                ⭐
                            </button>
                            <button className={'wa-sidebar-action' + (showArchived ? ' is-active' : '')}
                                onClick={() => setShowArchived(v => !v)}
                                title={showArchived ? 'Normal sohbetler' : 'Arşivlenmiş'}>
                                📁
                            </button>
                        </div>
                    </div>

                    <input
                        type="text"
                        className="wa-msg-search"
                        placeholder="Sohbet veya kişi ara..."
                        value={searchTerm}
                        onChange={(e) => {
                            const q = e.target.value
                            setSearchTerm(q)
                            const existingPhones = new Set(conversations.map(c => c.phone))
                            searchContactsByTerm(q, existingPhones)
                            if (!q) setContactResults([])
                        }}
                    />
                    <div className="wa-filter-chips">
                        {[['all','Tümü'],['unread','Okunmamış'],['groups','Gruplar']].map(([tab, label]) => (
                            <button key={tab}
                                className={'wa-filter-chip' + (filterTab === tab ? ' is-active' : '')}
                                onClick={() => setFilterTab(tab)}>
                                {label}
                                {tab === 'unread' && conversations.filter(c => c.unread > 0).length > 0
                                    ? ` (${conversations.filter(c => c.unread > 0).length})` : ''}
                                {tab === 'groups' && conversations.filter(c => c.isGroup).length > 0
                                    ? ` (${conversations.filter(c => c.isGroup).length})` : ''}
                            </button>
                        ))}
                    </div>
                </div>
                <div className="wa-msg-conv-list">
                    {filtered.length === 0 && (
                        <div className="wa-msg-empty">
                            {conversations.length === 0
                                ? 'Henuz sohbet yok. Bridge calisiyor mu, telefonundan mesaj geldi mi kontrol et.'
                                : showArchived
                                    ? 'Arşivlenmiş sohbet yok.'
                                    : 'Aramayla eslesen sohbet yok.'}
                        </div>
                    )}
                    {filtered.map(c => {
                        return (
                            <div
                                key={c.phone}
                                role="button"
                                tabIndex={0}
                                className={'wa-msg-conv' + (c.phone === selectedPhone ? ' is-active' : '')}
                                onClick={() => selectConversation(c.phone)}
                                onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); selectConversation(c.phone) } }}
                                onContextMenu={e => {
                                    e.preventDefault()
                                    setConvMenu({ phone: c.phone, displayName: c.displayName || c.phone, x: e.clientX, y: e.clientY })
                                }}
                            >
                                <div className={'wa-msg-conv__avatar' + (c.isGroup ? ' is-group' : '')} aria-hidden="true">
                                    {c.isGroup
                                        ? '👥'
                                        : c.isLid && (!c.displayName || c.displayName.startsWith('Bilinmeyen'))
                                            ? '?'
                                            : (c.displayName || formatPhoneOrLid(c.phone) || '?').slice(0, 1).toUpperCase()}
                                </div>
                                <div className="wa-msg-conv__body">
                                    <div className="wa-msg-conv__row">
                                        <span className="wa-msg-conv__name">
                                            {c.displayName || formatPhoneOrLid(c.phone)}
                                            {mutedConvs.has(c.phone) && <span className="wa-mute-icon" title="Sessize alındı">🔇</span>}
                                            {archivedConvs.has(c.phone) && <span className="wa-archive-icon" title="Arşivlendi">📁</span>}
                                        </span>
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
                                    className="wa-msg-conv__menu-btn"
                                    onClick={e => {
                                        e.stopPropagation()
                                        const rect = e.currentTarget.getBoundingClientRect()
                                        setConvMenu({ phone: c.phone, displayName: c.displayName || c.phone, x: rect.right - 210, y: rect.bottom + 4 })
                                    }}
                                    title="Sohbet menüsü"
                                    aria-label={'Sohbet menüsü: ' + (c.displayName || c.phone)}
                                >
                                    ⋮
                                </button>
                            </div>
                        )
                    })}

                    {/* ── Kişiler bölümü (arama terimi varken gösterilir) ── */}
                    {searchTerm && contactResults.length > 0 && (
                        <div className="wa-contact-section">
                            <div className="wa-contact-section__label">Kişiler</div>
                            {contactResults.map(c => (
                                <div key={c.id} className="wa-msg-conv wa-contact-item"
                                    onClick={() => { setSelectedPhone(c.phone); setSearchTerm(''); setContactResults([]) }}>
                                    <div className="wa-msg-conv__avatar">
                                        {(c.displayName || c.phone).slice(0, 1).toUpperCase()}
                                    </div>
                                    <div className="wa-msg-conv__body">
                                        <div className="wa-msg-conv__top">
                                            <span className="wa-msg-conv__name">{c.displayName}</span>
                                        </div>
                                        <div className="wa-msg-conv__snippet">+{c.phone}</div>
                                    </div>
                                </div>
                            ))}
                        </div>
                    )}
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
                            <div className={'wa-msg-thread__avatar' + (selectedConv?.isGroup ? ' is-group' : '')}>
                                {selectedConv?.isGroup
                                    ? '👥'
                                    : selectedConv?.isLid && (!selectedConv?.displayName || selectedConv?.displayName.startsWith('Bilinmeyen'))
                                        ? '?'
                                        : (selectedConv?.displayName || selectedPhone).slice(0, 1).toUpperCase()}
                            </div>
                            <div className="wa-msg-thread__info">
                                <div className="wa-msg-thread__name"
                                    style={selectedConv?.isGroup ? { cursor: 'pointer' } : {}}
                                    onClick={() => {
                                        if (selectedConv?.isGroup) {
                                            setGroupInfoOpen(v => !v)
                                            if (!groupInfoOpen) loadGroupMembers(selectedConv.groupJid)
                                        }
                                    }}
                                    title={selectedConv?.isGroup ? 'Grup bilgisi' : ''}>
                                    {selectedConv?.displayName || selectedConv?.groupSubject || formatPhoneOrLid(selectedPhone)}
                                    {selectedConv?.isGroup && <span className="wa-group-info-hint"> ▸</span>}
                                </div>
                                <div className="wa-msg-thread__status">
                                    {selectedConv?.isGroup
                                        ? <span className="wa-presence wa-presence--muted">
                                            {selectedConv.memberCount > 0 ? `${selectedConv.memberCount} üye` : 'Grup'}
                                          </span>
                                        : peerIsTyping
                                            ? <span className="wa-typing-indicator"><span/><span/><span/></span>
                                            : selectedPresence?.status === 'online'
                                                ? <span className="wa-presence wa-presence--online">çevrimiçi</span>
                                                : selectedPresence?.lastSeen
                                                    ? <span className="wa-presence">son görülme {formatLastSeen(selectedPresence.lastSeen)}</span>
                                                    : <span className="wa-presence wa-presence--muted">{formatPhoneOrLid(selectedPhone)}{selectedConv?.accountCode ? ' · ' + selectedConv.accountCode : ''}</span>
                                    }
                                </div>
                            </div>
                            <button className="wa-thread-action-btn" title="Yıldızlı mesajlar"
                                onClick={() => setStarredPanel(v => !v)}>⭐</button>
                            {selectMode && (
                                <button className="wa-thread-action-btn wa-thread-action-btn--cancel" onClick={exitSelectMode}
                                    title="Seçimi iptal et">✕ İptal</button>
                            )}
                            <button className="wa-thread-search-btn" title="Sohbette ara (Ctrl+F)"
                                onClick={() => setChatSearchActive(a => !a)}>🔍</button>
                        </div>
                        <div className="wa-msg-thread__body" onClick={() => setReactionTarget(null)}>
                            {/* Sohbet içi arama paneli */}
                            {chatSearchActive && (
                                <div className="wa-chat-search">
                                    <input
                                        autoFocus
                                        type="text"
                                        className="wa-chat-search__input"
                                        placeholder="Mesajlarda ara..."
                                        value={chatSearch}
                                        onChange={e => setChatSearch(e.target.value)}
                                    />
                                    <button className="wa-chat-search__close" onClick={() => { setChatSearchActive(false); setChatSearch('') }}>✕</button>
                                </div>
                            )}
                            {messages.length === 0 && !chatSearchActive && (
                                <div className="wa-msg-empty">Bu sohbette henuz mesaj yok.</div>
                            )}
                            {/* Yıldızlı mesajlar overlay */}
                            {starredPanel && (
                                <div className="wa-starred-overlay">
                                    <div className="wa-starred-overlay__head">
                                        <span>⭐ Yıldızlı Mesajlar</span>
                                        <button className="wa-starred-overlay__close" onClick={() => setStarredPanel(false)}>✕</button>
                                    </div>
                                    <div className="wa-starred-overlay__body">
                                        {messages.filter(m => starredMsgs.has(m.bridgeMsgId)).length === 0 ? (
                                            <div className="wa-msg-empty">Yıldızlı mesaj yok. Mesaj menüsünden ⭐ ile yıldızlayabilirsiniz.</div>
                                        ) : (
                                            messages.filter(m => starredMsgs.has(m.bridgeMsgId)).map(m => (
                                                <div key={m.id} className={'wa-starred-item' + (m.direction === 1 ? ' is-me' : '')}>
                                                    <div className="wa-starred-item__meta">
                                                        {m.direction === 1 ? 'Sen' : (selectedConv?.displayName || selectedPhone)}
                                                        <span className="wa-starred-item__time">{formatShortTime(m.at)}</span>
                                                    </div>
                                                    <div className="wa-starred-item__body">
                                                        {m.isDeleted ? <em>Bu mesaj silindi</em> : (m.body || `[${m.mediaType}]`)}
                                                    </div>
                                                    <button className="wa-starred-item__remove" title="Yıldızı kaldır"
                                                        onClick={() => toggleStar(m.bridgeMsgId)}>✕</button>
                                                </div>
                                            ))
                                        )}
                                    </div>
                                </div>
                            )}
                            {(chatSearch.length >= 2
                                ? messages.filter(m => m.body && m.body.toLowerCase().includes(chatSearch.toLowerCase()))
                                : messages
                            ).map((m, i, arr) => {
                                const showDate = i === 0 || !sameDay(m.at, arr[i-1].at)
                                const quotedMsg = m.quotedMsgId ? messages.find(q => q.bridgeMsgId === m.quotedMsgId) : null
                                return (
                                    <React.Fragment key={m.id}>
                                        {showDate && <div className="wa-msg-day">{formatDay(m.at)}</div>}
                                        <div
                                            className={'wa-msg-bubble-wrap' + (m.direction === 1 ? ' is-me' : '') + (selectMode && selectedMsgs.has(m.id) ? ' is-selected' : '')}
                                            onClick={e => {
                                                if (selectMode) { e.stopPropagation(); toggleSelectMsg(m.id); return }
                                                e.stopPropagation()
                                                setActiveMsgId(prev => prev === m.id ? null : m.id)
                                                setReactionTarget(null)
                                            }}
                                        >
                                            {selectMode && (
                                                <div className="wa-select-checkbox">
                                                    <input type="checkbox" checked={selectedMsgs.has(m.id)} onChange={() => toggleSelectMsg(m.id)} onClick={e => e.stopPropagation()} />
                                                </div>
                                            )}
                                            <div className={'wa-msg-bubble' + (m.direction === 1 ? ' is-me' : '') + (m.isDeleted ? ' is-deleted' : '')}>
                                                {/* Yıldız göstergesi */}
                                                {starredMsgs.has(m.bridgeMsgId) && (
                                                    <span className="wa-msg-star" title="Yıldızlı">⭐</span>
                                                )}
                                                {/* Grup gönderen adı */}
                                                {selectedConv?.isGroup && m.direction === 0 && (m.senderName || m.senderJid) && (
                                                    <div className="wa-msg-sender-name">
                                                        {m.senderName || m.senderJid?.split('@')[0]}
                                                    </div>
                                                )}
                                                {/* Alıntı kartı */}
                                                {quotedMsg && !m.isDeleted && (
                                                    <div className={'wa-quoted' + (quotedMsg.direction === 1 ? ' is-me' : '')}>
                                                        <div className="wa-quoted__name">{quotedMsg.direction === 1 ? 'Sen' : (selectedConv?.displayName || selectedPhone)}</div>
                                                        <div className="wa-quoted__body">{quotedMsg.body || (quotedMsg.hasMedia ? `[${quotedMsg.mediaType}]` : '')}</div>
                                                    </div>
                                                )}
                                                {m.isDeleted
                                                    ? <div className="wa-msg-bubble__deleted">🚫 Bu mesaj silindi</div>
                                                    : <>
                                                        {renderMediaContent(m, setLightbox, () => {
                                                            if (awaitingMediaScrollRef.current && threadEndRef.current)
                                                                threadEndRef.current.scrollIntoView({ behavior: 'auto', block: 'end' })
                                                        })}
                                                        {m.body && <div className="wa-msg-bubble__text">{m.body}</div>}
                                                    </>
                                                }
                                                <div className="wa-msg-bubble__meta">
                                                    <span className="wa-msg-bubble__time">{formatShortTime(m.at)}</span>
                                                    {m.direction === 1 && <DeliveryTick status={messageStatusMap[m.id] || m.deliveryStatus || null} />}
                                                </div>
                                                {/* Chevron tetikleyici — bubble içinde sağ üst köşe */}
                                                {!m.isDeleted && (
                                                    <button
                                                        className={'wa-msg-chevron' + (activeMsgId === m.id ? ' is-open' : '')}
                                                        onClick={e => { e.stopPropagation(); setActiveMsgId(prev => prev === m.id ? null : m.id); setReactionTarget(null); }}
                                                        title="Seçenekler"
                                                    >
                                                        <svg width="11" height="11" viewBox="0 0 24 24" fill="currentColor"><path d="M7 10l5 5 5-5z"/></svg>
                                                    </button>
                                                )}
                                            </div>
                                            {/* Dropdown: emoji satırı + işlem listesi */}
                                            {!m.isDeleted && activeMsgId === m.id && (
                                                <div className={'wa-msg-dropdown' + (m.direction === 1 ? ' is-me' : '')} onClick={e => e.stopPropagation()}>
                                                    <div className="wa-msg-dropdown__emojis">
                                                        {['👍','❤️','😂','😮','😢','🙏'].map(emoji => (
                                                            <button key={emoji} className="wa-msg-dropdown__emoji"
                                                                onClick={() => { sendReaction(m.bridgeMsgId, m.direction === 1, emoji); setActiveMsgId(null); }}>
                                                                {emoji}
                                                            </button>
                                                        ))}
                                                    </div>
                                                    <div className="wa-msg-dropdown__sep" />
                                                    {m.direction === 1 && (
                                                        <button className="wa-msg-dropdown__item"
                                                            onClick={e => { e.stopPropagation(); showMsgInfo(m); setActiveMsgId(null); }}>
                                                            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>
                                                            Mesaj bilgisi
                                                        </button>
                                                    )}
                                                    <button className="wa-msg-dropdown__item"
                                                        onClick={e => { e.stopPropagation(); setReplyingTo({ id: m.id, bridgeMsgId: m.bridgeMsgId, body: m.body, direction: m.direction }); setActiveMsgId(null); }}>
                                                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round"><polyline points="9 17 4 12 9 7"/><path d="M20 18v-2a4 4 0 0 0-4-4H4"/></svg>
                                                        Yanıtla
                                                    </button>
                                                    {m.body && (
                                                        <button className="wa-msg-dropdown__item"
                                                            onClick={e => { e.stopPropagation(); navigator.clipboard?.writeText(m.body); showToast('Kopyalandı', 'info'); setActiveMsgId(null); }}>
                                                            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><rect x="9" y="9" width="13" height="13" rx="2"/><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/></svg>
                                                            Kopyala
                                                        </button>
                                                    )}
                                                    <button className="wa-msg-dropdown__item"
                                                        onClick={e => { e.stopPropagation(); setForwardMsg({ bridgeMsgId: m.bridgeMsgId, body: m.body }); setActiveMsgId(null); }}>
                                                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round"><polyline points="15 17 20 12 15 7"/><path d="M4 18v-2a4 4 0 0 1 4-4h12"/></svg>
                                                        İlet
                                                    </button>
                                                    <button className={'wa-msg-dropdown__item' + (starredMsgs.has(m.bridgeMsgId) ? ' is-starred' : '')}
                                                        onClick={e => { e.stopPropagation(); toggleStar(m.bridgeMsgId); setActiveMsgId(null); }}>
                                                        <svg width="14" height="14" viewBox="0 0 24 24" fill={starredMsgs.has(m.bridgeMsgId) ? '#f59e0b' : 'none'} stroke={starredMsgs.has(m.bridgeMsgId) ? '#f59e0b' : 'currentColor'} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"/></svg>
                                                        {starredMsgs.has(m.bridgeMsgId) ? 'Yıldızı kaldır' : 'Yıldız ekle'}
                                                    </button>
                                                    <button className="wa-msg-dropdown__item"
                                                        onClick={e => { e.stopPropagation(); setSelectMode(true); toggleSelectMsg(m.id); setActiveMsgId(null); }}>
                                                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><polyline points="20 6 9 17 4 12"/></svg>
                                                        Seç
                                                    </button>
                                                    <div className="wa-msg-dropdown__sep" />
                                                    <button className="wa-msg-dropdown__item wa-msg-dropdown__item--danger"
                                                        onClick={e => { e.stopPropagation(); deleteMessage(m.bridgeMsgId, m.direction === 1); setActiveMsgId(null); }}>
                                                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><polyline points="3 6 5 6 21 6"/><path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"/><path d="M10 11v6"/><path d="M14 11v6"/><path d="M9 6V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2"/></svg>
                                                        Sil
                                                    </button>
                                                </div>
                                            )}
                                            {/* Reaksiyon badge */}
                                            {m.reactionEmoji && (
                                                <div className="wa-reaction-badge" title="Reaksiyon kaldır"
                                                    onClick={e => { e.stopPropagation(); sendReaction(m.bridgeMsgId, m.direction === 1, '') }}>
                                                    {m.reactionEmoji}
                                                </div>
                                            )}
                                        </div>
                                    </React.Fragment>
                                )
                            })}
                            <div ref={threadEndRef} />
                        </div>
                        {/* Seçim modu alt çubuğu */}
                        {selectMode && (
                            <div className="wa-select-bar">
                                <span className="wa-select-bar__count">{selectedMsgs.size} seçildi</span>
                                <div className="wa-select-bar__actions">
                                    <button className="wa-select-bar__btn" onClick={() => {
                                        const selected = messages.filter(m => selectedMsgs.has(m.id))
                                        selected.forEach(m => toggleStar(m.bridgeMsgId))
                                        exitSelectMode()
                                    }} title="Yıldızla">⭐ Yıldızla</button>
                                    <button className="wa-select-bar__btn wa-select-bar__btn--danger" onClick={() => {
                                        const selected = messages.filter(m => selectedMsgs.has(m.id))
                                        selected.forEach(m => deleteMessage(m.bridgeMsgId, m.direction === 1))
                                        exitSelectMode()
                                    }} title="Sil">🗑 Sil</button>
                                    <button className="wa-select-bar__btn wa-select-bar__btn--cancel" onClick={exitSelectMode}>İptal</button>
                                </div>
                            </div>
                        )}
                        {replyingTo && (
                            <div className="wa-reply-bar">
                                <div className="wa-reply-bar__accent" />
                                <div className="wa-reply-bar__content">
                                    <div className="wa-reply-bar__label">
                                        {replyingTo.direction === 1 ? 'Sen' : (selectedConv?.displayName || selectedPhone)}
                                    </div>
                                    <div className="wa-reply-bar__body">
                                        {replyingTo.body || '[medya]'}
                                    </div>
                                </div>
                                <button className="wa-reply-bar__close" onClick={() => setReplyingTo(null)} title="İptal">✕</button>
                            </div>
                        )}
                        <div className="wa-msg-composer">
                            {/* Belge seçici */}
                            <input
                                ref={fileInputRef}
                                type="file"
                                accept=".pdf,.doc,.docx,.xls,.xlsx,.ppt,.pptx,.txt,.zip,.rar,.csv"
                                style={{ display: 'none' }}
                                onChange={(e) => { stageFile(e.target.files?.[0]); e.target.value = '' }}
                            />
                            {/* Fotoğraf / Video seçici */}
                            <input
                                ref={imageInputRef}
                                type="file"
                                accept="image/*,video/*"
                                style={{ display: 'none' }}
                                onChange={(e) => { stageFile(e.target.files?.[0]); e.target.value = '' }}
                            />
                            {pendingFile && (
                                <div className="wa-msg-composer__pending">
                                    {pendingPreviewUrl ? (
                                        <img src={pendingPreviewUrl} alt="" className="wa-msg-composer__pending-thumb" />
                                    ) : (
                                        <span className="wa-msg-composer__pending-icon"><svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/><line x1="16" y1="13" x2="8" y2="13"/><line x1="16" y1="17" x2="8" y2="17"/></svg></span>
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
                                {/* Ek menüsü: "+" butonu + popup */}
                                <div className="wa-attach-wrap" onClick={e => e.stopPropagation()}>
                                    <button
                                        className={'wa-msg-composer__attach' + (attachMenuOpen ? ' is-open' : '')}
                                        onClick={() => setAttachMenuOpen(v => !v)}
                                        disabled={sending}
                                        title="Dosya ekle"
                                        aria-label="Dosya ekle"
                                    >
                                        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
                                            <line x1="12" y1="5" x2="12" y2="19"/>
                                            <line x1="5" y1="12" x2="19" y2="12"/>
                                        </svg>
                                    </button>
                                    {attachMenuOpen && (
                                        <div className="wa-attach-popup">
                                            {/* ── Dosya bölümü ── */}
                                            <div className="wa-attach-popup__section-title">Ekle</div>
                                            <button
                                                className="wa-attach-popup__item"
                                                onClick={() => { fileInputRef.current?.click(); setAttachMenuOpen(false) }}
                                                disabled={sending}
                                            >
                                                <span className="wa-attach-popup__icon wa-attach-popup__icon--blue">
                                                    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/><line x1="16" y1="13" x2="8" y2="13"/><line x1="16" y1="17" x2="8" y2="17"/></svg>
                                                </span>
                                                <span className="wa-attach-popup__label">Belge</span>
                                            </button>
                                            <button
                                                className="wa-attach-popup__item"
                                                onClick={() => { imageInputRef.current?.click(); setAttachMenuOpen(false) }}
                                                disabled={sending}
                                            >
                                                <span className="wa-attach-popup__icon wa-attach-popup__icon--purple">
                                                    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><rect x="3" y="3" width="18" height="18" rx="2" ry="2"/><circle cx="8.5" cy="8.5" r="1.5"/><polyline points="21 15 16 10 5 21"/></svg>
                                                </span>
                                                <span className="wa-attach-popup__label">Fotoğraflar ve Videolar</span>
                                            </button>
                                            {/* ── Emoji bölümü ── */}
                                            <div className="wa-attach-popup__divider" />
                                            <div className="wa-attach-popup__section-title">Emoji</div>
                                            <div className="wa-attach-popup__emoji-grid">
                                                {['😀','😂','😍','🥰','😊','😎','🤔','😅',
                                                  '😭','😤','😆','🤣','😁','😉','🤙','👋',
                                                  '❤️','🧡','💛','💚','💙','💜','🖤','💔',
                                                  '👍','👎','👏','🙌','💪','🤞','🤝','✌️',
                                                  '🔥','⭐','💯','🎉','✅','❌','⚠️','💡'
                                                ].map(em => (
                                                    <button
                                                        key={em}
                                                        className="wa-attach-popup__emoji-btn"
                                                        onClick={() => insertEmoji(em)}
                                                        title={em}
                                                    >{em}</button>
                                                ))}
                                            </div>
                                        </div>
                                    )}
                                </div>
                                <textarea
                                    ref={textareaRef}
                                    className="wa-msg-composer__input"
                                    placeholder={pendingFile ? "Dosyaya not ekle (opsiyonel)..." : "Mesaj yaz... (Enter: gonder, Shift+Enter: yeni satir)"}
                                    value={composeText}
                                    onChange={(e) => setComposeText(e.target.value)}
                                    onKeyDown={handleComposeKey}
                                    rows={1}
                                    disabled={sending}
                                />
                                {/* Mikrofon butonu */}
                                {!pendingFile && !recording && (
                                    <button className="wa-msg-composer__mic" onClick={startRecording}
                                        disabled={sending} title="Ses kaydı (mikrofon)">
                                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                                            <path d="M12 1a3 3 0 0 0-3 3v8a3 3 0 0 0 6 0V4a3 3 0 0 0-3-3z"/>
                                            <path d="M19 10v2a7 7 0 0 1-14 0v-2"/>
                                            <line x1="12" y1="19" x2="12" y2="23"/>
                                            <line x1="8" y1="23" x2="16" y2="23"/>
                                        </svg>
                                    </button>
                                )}
                                {recording && (
                                    <div className="wa-recording-ui">
                                        <span className="wa-recording-dot" />
                                        <span className="wa-recording-time">{Math.floor(recordingSecs / 60).toString().padStart(2,'0')}:{(recordingSecs % 60).toString().padStart(2,'0')}</span>
                                        <button className="wa-recording-stop" onClick={stopRecording} title="Kaydı bitir ve gönder">
                                            <svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor"><rect x="3" y="3" width="18" height="18" rx="2"/></svg>
                                            Gönder
                                        </button>
                                        <button className="wa-recording-cancel" onClick={() => {
                                            if (recordingTimerRef.current) { clearInterval(recordingTimerRef.current); recordingTimerRef.current = null }
                                            if (mediaRecorderRef.current) {
                                                const mr = mediaRecorderRef.current
                                                mr.ondataavailable = null
                                                mr.onstop = null
                                                if (mr.state !== 'inactive') mr.stop()
                                            }
                                            setRecording(false)
                                            setRecordingSecs(0)
                                            recordingChunksRef.current = []
                                        }} title="İptal">✕</button>
                                    </div>
                                )}
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

            {/* ── Grup bilgi paneli ─────────────────────────────────────────────── */}
            {groupInfoOpen && selectedConv?.isGroup && (
                <div className="wa-group-panel">
                    <div className="wa-group-panel__head">
                        <span>👥 Grup Bilgisi</span>
                        <button onClick={() => setGroupInfoOpen(false)} className="wa-group-panel__close">✕</button>
                    </div>
                    <div className="wa-group-panel__body">
                        <div className="wa-group-panel__name">{selectedConv?.groupSubject || selectedConv?.displayName}</div>
                        {selectedConv?.memberCount > 0 && (
                            <div className="wa-group-panel__count">{selectedConv.memberCount} üye</div>
                        )}
                        {groupMembers.length > 0 && (
                            <div className="wa-group-panel__members">
                                <div className="wa-group-panel__section-title">Üyeler</div>
                                {groupMembers.map((member, i) => (
                                    <div key={i} className="wa-group-panel__member">
                                        <div className="wa-group-panel__member-avatar">
                                            {(member.name || member.jid || '?').slice(0, 1).toUpperCase()}
                                        </div>
                                        <div className="wa-group-panel__member-info">
                                            <div className="wa-group-panel__member-name">{member.name || member.jid?.split('@')[0] || 'Üye'}</div>
                                            {member.role === 'admin' && <div className="wa-group-panel__member-role">Yönetici</div>}
                                            {member.role === 'superadmin' && <div className="wa-group-panel__member-role">Süper Yönetici</div>}
                                        </div>
                                    </div>
                                ))}
                            </div>
                        )}
                        {groupMembers.length === 0 && (
                            <button className="wa-group-panel__sync-btn"
                                onClick={() => loadGroupMembers(selectedConv.groupJid)}>
                                Üyeleri Yükle
                            </button>
                        )}
                    </div>
                </div>
            )}
        </div>
    )
}

// ── Yardimcilar ─────────────────────────────────────────────────────────
function renderMediaContent(m, setLightbox, onMediaLoad) {
    if (!m.hasMedia || !m.mediaUrl) return null
    const url = m.mediaUrl
    const fileName = m.mediaFileName || (m.mediaUrl.split('/').pop() || 'dosya')
    const sizeKb = m.mediaSize ? (m.mediaSize / 1024).toFixed(0) + ' KB' : ''

    switch (m.mediaType) {
        case 'image':
        case 'sticker':
            return (
                <div className="wa-msg-bubble__media wa-msg-bubble__media--image"
                    title="Tam boyut için tıkla"
                    style={{ cursor: 'zoom-in' }}
                    onClick={e => { e.stopPropagation(); if (setLightbox) setLightbox({ url, type: 'image', fileName }) }}>
                    <img src={url} alt="" loading="lazy" onLoad={onMediaLoad} />
                </div>
            )
        case 'video':
            return (
                <video className="wa-msg-bubble__media wa-msg-bubble__media--video" controls preload="metadata"
                    onLoadedMetadata={onMediaLoad}>
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
                    <span className="wa-msg-doc__icon"><svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg></span>
                    <span className="wa-msg-doc__body">
                        <span className="wa-msg-doc__name">{fileName}</span>
                        {sizeKb && <span className="wa-msg-doc__size">{sizeKb}</span>}
                    </span>
                </a>
            )
    }
}

/** Gönderilen mesaj için delivery tick (✓ / ✓✓ / mavi ✓✓) */
function DeliveryTick({ status }) {
    if (!status) return <span className="wa-tick wa-tick--sent" aria-label="gönderildi">✓</span>
    if (status === 'sent') return <span className="wa-tick wa-tick--sent" aria-label="gönderildi">✓</span>
    if (status === 'delivered') return <span className="wa-tick wa-tick--delivered" aria-label="iletildi">✓✓</span>
    if (status === 'read') return <span className="wa-tick wa-tick--read" aria-label="okundu">✓✓</span>
    return null
}

/** "son görülme" için kısa metin */
function formatLastSeen(iso) {
    if (!iso) return ''
    const d = new Date(iso)
    if (isNaN(d.getTime())) return ''
    const now = new Date()
    if (d.toDateString() === now.toDateString())
        return 'bugün ' + d.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })
    const yesterday = new Date(now); yesterday.setDate(yesterday.getDate() - 1)
    if (d.toDateString() === yesterday.toDateString())
        return 'dün ' + d.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })
    return d.toLocaleDateString('tr-TR', { day: 'numeric', month: 'short' })
}

/**
 * Telefon mu LID mi: 10-14 basamak gercek E.164 telefon, 15+ basamak WhatsApp LID.
 * LID icin bos string doner — UI'da yanlis numara gosterme.
 */
function formatPhoneOrLid(s) {
    if (!s) return ''
    const digits = String(s).replace(/[^\d]/g, '')
    if (digits.length >= 10 && digits.length <= 14) return '+' + digits
    return '' // LID — telefon numarasi yok, bos birak
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
