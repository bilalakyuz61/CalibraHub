/**
 * CalibraHub WhatsApp Bridge — Baileys edition
 * --------------------------------------------
 * @whiskeysockets/baileys ile WhatsApp Web protokolune dogrudan baglanir.
 * Puppeteer/Chromium yok, daha az bellek + CPU + daha kararli session.
 *
 * Endpoint'ler:
 *   GET  /status              → { state, displayName, phone, qrAvailable }
 *   GET  /qr                  → { qr: "data:image/png;base64,..." | null }
 *   GET  /messages?since=ISO  → kuyruktan gelen+giden mesajlar
 *   GET  /lookup?phone=905... → numarayi WA'ya sor (debug)
 *   GET  /contacts            → kayitli kontaklar (debug)
 *   POST /pairing-code        → { phone } body, 8 haneli kod doner
 *   POST /send                → { to, text } body
 *   POST /logout              → oturumu kapatir
 *
 * Calistirma:
 *   npm install
 *   npm start
 *
 * Default port: 61100
 */

import express from 'express';
import QRCode from 'qrcode';
import pino from 'pino';
import { promises as fs, createReadStream } from 'fs';
import path from 'path';
import {
    default as makeWASocket,
    useMultiFileAuthState,
    DisconnectReason,
    makeCacheableSignalKeyStore,
    fetchLatestBaileysVersion,
    downloadMediaMessage,
} from '@whiskeysockets/baileys';
import { Boom } from '@hapi/boom';

const PORT = process.env.PORT || 61100;
const SESSION_DIR = './session-data';
const MEDIA_DIR = './media';
const MAX_MEDIA_SIZE = 50 * 1024 * 1024; // 50MB üstü indirilmez
const app = express();
app.use(express.json({ limit: '1mb' }));
// NOT: express.raw global degil — sadece /send-media endpoint'inde kullanilir
// Cunku gercek dosyalar image/jpeg, video/mp4 gibi mime tiplerle gelir;
// global olursa /pairing-code gibi JSON endpoint'leri kirilir.

// Media klasörünü hazirla
await fs.mkdir(MEDIA_DIR, { recursive: true }).catch(() => {});

// Mime → uzanti map (yaygin tipler)
const MIME_TO_EXT = {
    'image/jpeg': 'jpg', 'image/png': 'png', 'image/webp': 'webp', 'image/gif': 'gif',
    'video/mp4': 'mp4', 'video/3gpp': '3gp', 'video/quicktime': 'mov',
    'audio/ogg; codecs=opus': 'ogg', 'audio/ogg': 'ogg', 'audio/mpeg': 'mp3',
    'audio/mp4': 'm4a', 'audio/aac': 'aac', 'audio/wav': 'wav',
    'application/pdf': 'pdf',
    'application/vnd.openxmlformats-officedocument.wordprocessingml.document': 'docx',
    'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet': 'xlsx',
    'application/msword': 'doc',
    'application/vnd.ms-excel': 'xls',
    'text/plain': 'txt',
};

function mimeToExt(mime) {
    if (!mime) return 'bin';
    const m = String(mime).split(';')[0].trim();
    return MIME_TO_EXT[mime] || MIME_TO_EXT[m] || (m.split('/')[1] || 'bin');
}

function safeFileName(name) {
    if (!name) return null;
    return String(name).replace(/[^\w\-. ]/g, '_').slice(0, 200);
}

// HTTP header'lari ASCII zorunlu oldugundan .NET tarafi URL-encode eder.
// Cozulemezse (hatali encoding) orijinali doneriz.
function safeDecodeUri(s) {
    if (s === null || s === undefined) return s;
    try { return decodeURIComponent(String(s)); } catch { return s; }
}

// Media metadata cache: msgId → { path, mime, filename, size }
const mediaCache = new Map();

/**
 * Mesaj icindeki medyayi indirir ve diske kaydeder.
 * Buyuk dosyalar (>50MB) atlanir, yine de metadata doner.
 */
async function tryDownloadMessageMedia(msg, mediaType) {
    try {
        const messageNode = msg.message?.imageMessage
            || msg.message?.videoMessage
            || msg.message?.audioMessage
            || msg.message?.documentMessage
            || msg.message?.stickerMessage;
        if (!messageNode) return null;

        const mimeType = messageNode.mimetype || null;
        const fileLength = Number(messageNode.fileLength || 0);
        const fileName = safeFileName(messageNode.fileName) || null;

        // Buyuk medyaya izin verme
        if (fileLength > MAX_MEDIA_SIZE) {
            flog(`[Media] BUYUK dosya atlandi (${(fileLength / 1024 / 1024).toFixed(1)}MB > 50MB): ${msg.key.id}`);
            return { skipped: true, reason: 'too_large', mime: mimeType, size: fileLength, fileName };
        }

        const buffer = await downloadMediaMessage(msg, 'buffer', {});
        if (!buffer || buffer.length === 0) return null;

        const ext = mimeToExt(mimeType) || (mediaType === 'document' ? 'bin' : mediaType);
        const safeId = String(msg.key.id || `m-${Date.now()}`).replace(/[^A-Za-z0-9_-]/g, '_');
        const fp = path.join(MEDIA_DIR, `${safeId}.${ext}`);
        await fs.writeFile(fp, buffer);

        const meta = { path: fp, mime: mimeType, filename: fileName, size: buffer.length };
        mediaCache.set(safeId, meta);
        flog(`[Media] OK ${mediaType} ${safeId}.${ext} (${buffer.length} byte)${fileName ? ' ' + fileName : ''}`);
        return meta;
    } catch (err) {
        flog(`[Media] Indirme hatasi: ${err.message}`);
        return null;
    }
}

// ── State ────────────────────────────────────────────────────────────────
let sock = null;                    // Baileys socket
let currentQrDataUrl = null;        // Son QR (base64 data URL)
let pendingPairingCode = null;      // /pairing-code ile alinan 8 haneli kod
let isReady = false;                // open + auth tamam mi
let displayName = null;
let phone = null;
let lastError = null;

// Mesaj kuyrugu (ayni schema)
const MAX_QUEUE_SIZE = 5000;
const messageQueue = [];
function pushQueue(msg) {
    messageQueue.push(msg);
    if (messageQueue.length > MAX_QUEUE_SIZE) {
        messageQueue.splice(0, messageQueue.length - MAX_QUEUE_SIZE);
    }
}

// ── Helpers ──────────────────────────────────────────────────────────────
function flog(line) { process.stdout.write(line + '\n'); }
function isRealPhone(s) { return typeof s === 'string' && /^\d{10,14}$/.test(s); }

/** JID'den telefonu cikar — Baileys'da JID format: "905...@s.whatsapp.net" veya "...@lid" */
function jidToPhone(jid) {
    if (typeof jid !== 'string') return null;
    const m = jid.match(/^(\d+)@/);
    return m ? m[1] : null;
}

/** Telefonu Baileys icin JID'e cevir — modern WA: <phone>@s.whatsapp.net */
function phoneToJid(phone) {
    const cleaned = String(phone).replace(/[^\d]/g, '');
    return `${cleaned}@s.whatsapp.net`;
}

/** Bir JID'i bireysel sohbet mi (grup/broadcast/newsletter degil) */
function isPersonalChat(jid) {
    if (typeof jid !== 'string') return false;
    return jid.endsWith('@s.whatsapp.net') || jid.endsWith('@c.us') || jid.endsWith('@lid');
}

// ── Socket lifecycle ─────────────────────────────────────────────────────
async function startSocket() {
    const { state, saveCreds } = await useMultiFileAuthState(SESSION_DIR);
    const { version, isLatest } = await fetchLatestBaileysVersion();
    flog(`[Baileys] WA Web protokol versiyonu: ${version.join('.')} (latest: ${isLatest})`);

    const logger = pino({ level: 'warn' }); // Cok detayli ic loglari kapat

    sock = makeWASocket({
        version,
        auth: {
            creds: state.creds,
            keys: makeCacheableSignalKeyStore(state.keys, logger),
        },
        logger,
        printQRInTerminal: false,           // QR'i biz yonetiyoruz
        browser: ['CalibraHub', 'Chrome', '1.0.0'],
        markOnlineOnConnect: false,
        syncFullHistory: false,
        generateHighQualityLinkPreview: false,
    });

    sock.ev.on('creds.update', saveCreds);

    sock.ev.on('connection.update', async (update) => {
        const { connection, lastDisconnect, qr } = update;

        if (qr) {
            flog('[QR] Yeni QR alindi (Baileys) — telefondan tarayin.');
            try {
                currentQrDataUrl = await QRCode.toDataURL(qr, { width: 320, margin: 2 });
            } catch (err) {
                flog(`[QR] Encode hatasi: ${err.message}`);
                currentQrDataUrl = null;
            }
            isReady = false;
        }

        if (connection === 'open') {
            isReady = true;
            currentQrDataUrl = null;
            pendingPairingCode = null;
            try {
                const me = sock.user;
                phone = me?.id ? jidToPhone(me.id) : null;
                displayName = me?.name || me?.notify || null;
                flog(`[Ready] Baglandi: ${displayName || '?'} (+${phone || '?'})`);
            } catch {
                flog('[Ready] Bagli ama kullanici bilgisi alinamadi');
            }
            lastError = null;
        }

        if (connection === 'close') {
            isReady = false;
            const code = (lastDisconnect?.error instanceof Boom)
                ? lastDisconnect.error.output?.statusCode
                : lastDisconnect?.error?.statusCode;
            const reason = lastDisconnect?.error?.message || 'unknown';
            flog(`[Disconnect] code=${code} reason=${reason}`);
            lastError = `disconnected: ${reason}`;

            // loggedOut → session-data temizle, fresh QR isteyecek
            if (code === DisconnectReason.loggedOut) {
                flog('[Disconnect] loggedOut — yeni baglanma icin QR/pairing gerek');
                displayName = null;
                phone = null;
            } else {
                // Diger nedenler — yeniden bagla (auto-reconnect)
                flog('[Reconnect] 3sn sonra yeniden baglanacak...');
                setTimeout(() => { startSocket().catch(e => flog(`[Reconnect] Hata: ${e.message}`)); }, 3000);
            }
        }
    });

    // ── Gelen mesajlar ────────────────────────────────────────────────────
    sock.ev.on('messages.upsert', async (m) => {
        if (m.type !== 'notify' && m.type !== 'append') return;

        for (const msg of m.messages) {
            try {
                if (!msg.message) continue; // protokol mesajlari (revoke, vs.)
                const remoteJid = msg.key.remoteJid;
                if (!isPersonalChat(remoteJid)) continue;
                if (remoteJid === 'status@broadcast') continue;
                if (remoteJid?.endsWith('@g.us')) continue;
                if (remoteJid?.endsWith('@newsletter')) continue;

                const fromMe = !!msg.key.fromMe;
                const counterpartyJid = remoteJid; // 1-1 sohbette: karsi taraf hep remoteJid
                const counterpartyPhone = jidToPhone(counterpartyJid);
                if (!counterpartyPhone) {
                    flog(`[Recv] Telefon cikarilamadi: ${remoteJid} — atlandi`);
                    continue;
                }

                // Mesaj icerigini cek — text, image caption, document caption vb.
                const body =
                    msg.message?.conversation
                    || msg.message?.extendedTextMessage?.text
                    || msg.message?.imageMessage?.caption
                    || msg.message?.videoMessage?.caption
                    || msg.message?.documentMessage?.caption
                    || '';

                const messageTypes = Object.keys(msg.message || {});
                const mediaType =
                    messageTypes.includes('imageMessage') ? 'image'
                    : messageTypes.includes('videoMessage') ? 'video'
                    : messageTypes.includes('audioMessage') ? 'audio'
                    : messageTypes.includes('documentMessage') ? 'document'
                    : messageTypes.includes('stickerMessage') ? 'sticker'
                    : messageTypes.includes('locationMessage') ? 'location'
                    : 'chat';

                const hasMedia = mediaType !== 'chat' && mediaType !== 'location';

                // Kontak adini cikar (pushName notify mesajlarda gelir).
                // ONEMLI: fromMe=true ise pushName = BIZIM adimizdir, karsi tarafin degil.
                // O yuzden outgoing mesajlarda fromName bos birakilir; karsi tarafin adi
                // sadece gelen mesajlardan veya kontak listesinden cozumlenir.
                let fromName = fromMe ? null : (msg.pushName || null);

                // Medyayi indir (varsa) — kuyruga eklemeden once tamamlasin
                let mediaMeta = null;
                if (hasMedia) {
                    mediaMeta = await tryDownloadMessageMedia(msg, mediaType);
                }

                const safeId = String(msg.key.id || `m-${Date.now()}`).replace(/[^A-Za-z0-9_-]/g, '_');

                const entry = {
                    id: msg.key.id || `incoming-${Date.now()}-${Math.random()}`,
                    from: counterpartyPhone,
                    fromName,
                    fromMe,
                    body,
                    timestamp: (msg.messageTimestamp || Math.floor(Date.now() / 1000)) * 1000,
                    isMedia: hasMedia,
                    mediaType,
                    mediaUrl: mediaMeta?.path ? `/media/${safeId}` : null,
                    mediaMime: mediaMeta?.mime || null,
                    mediaFileName: mediaMeta?.filename || null,
                    mediaSize: mediaMeta?.size || null,
                    mediaSkipped: mediaMeta?.skipped ? mediaMeta.reason : null,
                    ack: null,
                };
                pushQueue(entry);
                const summary = mediaType === 'chat'
                    ? body.slice(0, 80)
                    : `[${mediaType}${mediaMeta?.size ? ' ' + (mediaMeta.size / 1024).toFixed(0) + 'KB' : ''}]${body ? ' ' + body.slice(0, 40) : ''}`;
                flog(`[Recv] ${fromMe ? '(me→) ' : ''}${counterpartyPhone}${fromName ? ' (' + fromName + ')' : ''}: ${summary}`);
            } catch (err) {
                flog(`[Recv] Hata: ${err.message}`);
            }
        }
    });

    return sock;
}

// ── HTTP API ─────────────────────────────────────────────────────────────
app.get('/status', (req, res) => {
    res.json({
        state: isReady ? 'ready' : (currentQrDataUrl || pendingPairingCode ? 'awaiting_qr' : 'connecting'),
        displayName,
        phone,
        qrAvailable: !!currentQrDataUrl,
        pairingCode: pendingPairingCode,
        lastError,
    });
});

app.get('/qr', (req, res) => {
    res.json({ qr: currentQrDataUrl });
});

// Medya bytes — CalibraHub.Web polling sirasinda bu URL'den ceker, kendi storage'ina kopyalar
app.get('/media/:id', async (req, res) => {
    try {
        const safeId = String(req.params.id).replace(/[^A-Za-z0-9_-]/g, '_');
        const meta = mediaCache.get(safeId);
        if (!meta) {
            // Cache'de yoksa diskte ara (Bridge restart sonrasi)
            try {
                const files = await fs.readdir(MEDIA_DIR);
                const found = files.find(f => f.startsWith(safeId + '.'));
                if (!found) return res.status(404).json({ ok: false, error: 'medya bulunamadi' });
                const fp = path.join(MEDIA_DIR, found);
                const stat = await fs.stat(fp);
                res.setHeader('Content-Length', stat.size);
                createReadStream(fp).pipe(res);
                return;
            } catch (e) {
                return res.status(500).json({ ok: false, error: e.message });
            }
        }
        if (meta.mime) res.setHeader('Content-Type', meta.mime);
        if (meta.size) res.setHeader('Content-Length', meta.size);
        if (meta.filename) res.setHeader('Content-Disposition', `inline; filename="${meta.filename}"`);
        createReadStream(meta.path).pipe(res);
    } catch (err) {
        res.status(500).json({ ok: false, error: err.message });
    }
});

app.get('/messages', (req, res) => {
    const sinceRaw = req.query.since;
    let sinceMs = 0;
    if (sinceRaw) {
        const t = Date.parse(String(sinceRaw));
        if (!Number.isNaN(t)) sinceMs = t;
    }
    const out = sinceMs > 0
        ? messageQueue.filter(m => m.timestamp > sinceMs)
        : messageQueue.slice(-200);
    res.json({
        messages: out,
        nowIso: new Date().toISOString(),
        queueSize: messageQueue.length,
    });
});

// Pairing code: telefon numarasi gonder → 8 haneli kod al
app.post('/pairing-code', async (req, res) => {
    if (!sock) return res.status(503).json({ ok: false, error: 'Socket olusturulmadi' });
    if (isReady) return res.status(400).json({ ok: false, error: 'Zaten bagli — onceki oturumu kapatip tekrar deneyin' });

    const { phone: phoneInput } = req.body || {};
    const cleaned = String(phoneInput || '').replace(/[^\d]/g, '');
    if (!isRealPhone(cleaned)) {
        return res.status(400).json({ ok: false, error: 'Gecersiz telefon. Ulke kodu dahil 10-14 basamak (orn: 905338168150)' });
    }

    try {
        // Baileys: requestPairingCode telefon numarasiyla 8 haneli kod doner
        // Telefonda WhatsApp → Bagli Cihazlar → "Telefon numarasiyla bagla" → kodu gir
        const code = await sock.requestPairingCode(cleaned);
        const formatted = code.match(/.{1,4}/g)?.join('-') || code;
        pendingPairingCode = formatted;
        flog(`[Pairing] Code uretildi: ${formatted} (telefon: +${cleaned})`);
        return res.json({ ok: true, code: formatted, phone: cleaned });
    } catch (err) {
        flog(`[Pairing] Hata: ${err.message}`);
        return res.status(500).json({ ok: false, error: err.message });
    }
});

// Tani: belirli bir telefonu WA'ya sor — kayitli mi
app.get('/lookup', async (req, res) => {
    if (!isReady) return res.status(503).json({ ok: false, error: 'Bridge hazir degil.' });
    const phoneIn = String(req.query.phone || '').replace(/[^\d]/g, '');
    if (!phoneIn) return res.status(400).json({ ok: false, error: 'phone query param zorunlu' });
    try {
        // Baileys: onWhatsApp telefon numarasini sorgular
        const result = await sock.onWhatsApp(phoneIn);
        const found = Array.isArray(result) && result.length > 0 ? result[0] : null;
        return res.json({
            ok: true,
            input: phoneIn,
            exists: !!found?.exists,
            jid: found?.jid || null,
            lid: found?.lid || null,
        });
    } catch (err) {
        return res.status(500).json({ ok: false, error: err.message });
    }
});

// Tani: kayitli kontakler — Baileys'da store sock.store yok by default, in-memory tutmadik
app.get('/contacts', async (req, res) => {
    return res.json({
        ok: true,
        note: "Baileys'da kontak listesi sadece messages.upsert + contacts.upsert eventlerinden toplanir. /lookup ile tekil sorgu yapilabilir.",
    });
});

app.post('/send', async (req, res) => {
    if (!isReady) return res.status(503).json({ ok: false, error: 'WhatsApp henuz hazir degil.' });

    const { to, text } = req.body || {};
    if (!to || !text) return res.status(400).json({ ok: false, error: 'to ve text alanlari zorunlu.' });

    const cleaned = String(to).replace(/[^\d]/g, '');
    if (cleaned.length < 10) {
        return res.status(400).json({ ok: false, error: 'Gecersiz telefon: en az 10 basamak.' });
    }

    // 15+ basamak → LID, 10-14 → gercek telefon
    const isLid = cleaned.length >= 15;
    const targetJid = isLid ? `${cleaned}@lid` : `${cleaned}@s.whatsapp.net`;

    try {
        // Telefonsa once WA'da kayitli mi dogrula
        if (!isLid) {
            const result = await sock.onWhatsApp(cleaned);
            const found = Array.isArray(result) && result.length > 0 ? result[0] : null;
            if (!found?.exists) {
                return res.status(400).json({ ok: false, error: `Numara WhatsApp'ta kayitli degil: +${cleaned}` });
            }
        }

        const sent = await sock.sendMessage(targetJid, { text: String(text) });
        const messageId = sent?.key?.id || `local-${Date.now()}`;
        flog(`[Send] OK to=${cleaned} jid=${targetJid} id=${messageId}`);
        return res.json({ ok: true, messageId });
    } catch (err) {
        flog(`[Send] Hata: ${err.message}`);
        return res.status(500).json({ ok: false, error: err.message });
    }
});

/**
 * Medya gonderme. Bytes raw body olarak gonderilir, metadata header'larda:
 *   X-To: 905308768505 (zorunlu)
 *   X-Caption: opsiyonel mesaj metni
 *   X-Filename: dosya adi (document icin onerilir)
 *   Content-Type: image/jpeg | video/mp4 | audio/ogg | application/pdf vb.
 * Dosya tipi (image/video/audio/document) Content-Type'tan otomatik belirlenir.
 */
// /send-media: raw body parser tum binary mime tiplerini kabul eder (image/*, video/*, audio/*, application/*)
app.post('/send-media', express.raw({ type: () => true, limit: '60mb' }), async (req, res) => {
    if (!isReady) return res.status(503).json({ ok: false, error: 'WhatsApp henuz hazir degil.' });

    const to = req.headers['x-to'];
    // .NET tarafi caption/filename'i URL-encode ederek gonderir (Turkce karakterler icin).
    // Burada decodeURIComponent ile geri ceviriyoruz; cozulemezse ham hali kullanilir.
    const caption = safeDecodeUri(req.headers['x-caption'] || '');
    const filename = safeDecodeUri(req.headers['x-filename'] || null);
    const contentType = (req.headers['content-type'] || 'application/octet-stream').split(';')[0].trim();

    if (!to) return res.status(400).json({ ok: false, error: 'X-To header zorunlu' });
    if (!Buffer.isBuffer(req.body) || req.body.length === 0)
        return res.status(400).json({ ok: false, error: 'Bos body — bytes bekleniyor' });

    const cleaned = String(to).replace(/[^\d]/g, '');
    if (cleaned.length < 10) return res.status(400).json({ ok: false, error: 'Gecersiz telefon' });

    const isLid = cleaned.length >= 15;
    const targetJid = isLid ? `${cleaned}@lid` : `${cleaned}@s.whatsapp.net`;

    // Content-Type'a gore mesaj icerigi
    let messageContent;
    if (contentType.startsWith('image/')) {
        messageContent = { image: req.body, mimetype: contentType, caption: String(caption) };
    } else if (contentType.startsWith('video/')) {
        messageContent = { video: req.body, mimetype: contentType, caption: String(caption) };
    } else if (contentType.startsWith('audio/')) {
        messageContent = { audio: req.body, mimetype: contentType, ptt: false };
    } else {
        // document
        messageContent = {
            document: req.body,
            mimetype: contentType || 'application/octet-stream',
            fileName: filename || 'dosya.bin',
            caption: String(caption),
        };
    }

    try {
        // Phone-based: WA'da kayitli mi dogrula
        if (!isLid) {
            const result = await sock.onWhatsApp(cleaned);
            const found = Array.isArray(result) && result.length > 0 ? result[0] : null;
            if (!found?.exists) {
                return res.status(400).json({ ok: false, error: `Numara WhatsApp'ta kayitli degil: +${cleaned}` });
            }
        }

        const sent = await sock.sendMessage(targetJid, messageContent);
        const messageId = sent?.key?.id || `local-${Date.now()}`;
        flog(`[SendMedia] OK to=${cleaned} type=${contentType} size=${req.body.length} id=${messageId}`);
        return res.json({ ok: true, messageId });
    } catch (err) {
        flog(`[SendMedia] Hata: ${err.message}`);
        return res.status(500).json({ ok: false, error: err.message });
    }
});

app.post('/logout', async (req, res) => {
    try {
        if (sock) await sock.logout('user-requested');
        isReady = false;
        currentQrDataUrl = null;
        pendingPairingCode = null;
        displayName = null;
        phone = null;
        return res.json({ ok: true });
    } catch (err) {
        return res.status(500).json({ ok: false, error: err.message });
    }
});

// Health check
app.get('/', (req, res) => {
    res.json({
        service: 'CalibraHub WhatsApp Bridge (Baileys)',
        version: '2.0.0',
        state: isReady ? 'ready' : (currentQrDataUrl || pendingPairingCode ? 'awaiting_qr' : 'connecting'),
    });
});

app.listen(PORT, () => {
    flog(`[HTTP] Bridge dinliyor: http://localhost:${PORT}`);
    flog(`        Endpoint'ler: GET /status, GET /qr, POST /pairing-code, POST /send, POST /logout`);
});

// Socket'i baslat
flog('[Init] Baileys socket baslatiliyor...');
startSocket().catch((err) => {
    flog(`[Init] Hata: ${err.message}`);
    lastError = `init: ${err.message}`;
});

// Graceful shutdown
process.on('SIGINT', async () => {
    flog('\n[Shutdown] Kapatiliyor...');
    try { if (sock) await sock.end(); } catch {}
    process.exit(0);
});
