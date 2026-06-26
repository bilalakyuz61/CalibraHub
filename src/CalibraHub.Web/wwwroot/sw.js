// 2026-06-08 — CalibraHub Offline Fallback Service Worker.
//
// Tek görev: backend kapalıyken navigation isteklerini cached /offline.html ile karşıla.
// Tarayıcının native ERR_CONNECTION_REFUSED ekranı yerine CalibraHub temalı bekleme
// sayfası gösterilir; offline.html sunucuyu polling ile arayıp ayağa kalktığında
// orijinal URL'e geri yönlendirir.
//
// **Kapsam:** Yalnızca HTML navigation istekleri (mode === 'navigate' veya
// Accept: text/html). API çağrıları, statik asset'ler, fetch() çağrıları HİÇBİR
// şekilde intercept edilmez — onlar normal şekilde başarısız olur ki ön-yüz kendi
// hata mesajını gösterebilsin.
//
// **Cache key versioning:** Yeni offline.html deploy'unda CACHE_VERSION arttırılır;
// activate event eski cache'leri temizler.

const CACHE_VERSION = 'calibra-offline-v18';
const OFFLINE_URL   = '/offline.html';

// Sunucu cevap vermezse bu süre içinde abort edip offline.html'i göster.
// Tarayıcının kendi "bağlantı yok" ekranı 1-2 sn flash etmesin diye agresif kısa.
const FAST_FALLBACK_MS = 600;

// Install — offline.html'i cache'e koy
self.addEventListener('install', function (event) {
    event.waitUntil((async function () {
        const cache = await caches.open(CACHE_VERSION);
        // {cache:'reload'} eski cached kopyayı geçersiz kılar
        await cache.add(new Request(OFFLINE_URL, { cache: 'reload' }));
        // Eski SW'yi beklemeden devreye gir
        await self.skipWaiting();
    })());
});

// Activate — eski cache version'larını temizle, tüm tab'ları sahiplen
self.addEventListener('activate', function (event) {
    event.waitUntil((async function () {
        const keys = await caches.keys();
        await Promise.all(keys.map(function (k) {
            return k.startsWith('calibra-offline-') && k !== CACHE_VERSION
                ? caches.delete(k)
                : Promise.resolve();
        }));
        await self.clients.claim();
    })());
});

// Fetch — yalnızca navigation hatasında offline.html dön
self.addEventListener('fetch', function (event) {
    const req = event.request;

    // Sadece GET + navigation isteklerini ele al
    if (req.method !== 'GET') return;

    // Navigation tespit: explicit mode VEYA Accept: text/html
    const isNav = req.mode === 'navigate'
        || (req.destination === '' && (req.headers.get('accept') || '').includes('text/html'));
    if (!isNav) return;

    // Probe isteklerini intercept etme — offline.html bunu kontrol için kullanıyor
    if (req.headers.get('X-Offline-Probe') === '1') return;

    // PJAX swap fetch'lerini intercept ETME. production-defs-pjax.js içerik swap'i
    // için Accept: text/html ile fetch atar; bu istek yukarıdaki isNav kontrolüne
    // takılır ve FAST_FALLBACK_MS (600ms) içinde board rebuild bitmezse abort edilip
    // offline.html dönüyordu → Edit save sonrası ekran boş kalıyordu. PJAX'ın kendi
    // network-fail fallback'i (full navigation) var; backend gerçekten kapalıysa o
    // full navigation yine SW'ye düşüp offline.html gösterir. Bu yüzden burada geç.
    if (req.headers.get('X-Requested-With') === 'pdt-pjax') return;

    event.respondWith((async function () {
        // FAST_FALLBACK_MS içinde cevap gelmezse abort + offline.html göster.
        // Bu sayede tarayıcının kendi "bağlantı reddedildi" ekranı flash etmez,
        // SW intercept neredeyse hiçbir delay olmadan devreye girer.
        const ctrl = new AbortController();
        const timer = setTimeout(() => ctrl.abort(), FAST_FALLBACK_MS);
        try {
            // Önce network — başarılıysa direkt aktar
            const networkResp = await fetch(req, { signal: ctrl.signal });
            clearTimeout(timer);
            return networkResp;
        } catch (err) {
            clearTimeout(timer);
            // Network fail — orijinal hedefi sessionStorage'a koyacak ki offline.html
            // ayağa kalkınca aynı URL'e geri yönlendirsin. SW'den sessionStorage'a
            // doğrudan yazamayız → URL'i offline page'e query param ile ilet, JS okusun.
            const target = (function () {
                try {
                    const u = new URL(req.url);
                    return u.pathname + u.search;
                } catch (_) { return '/'; }
            })();
            const offlineUrl = OFFLINE_URL + '?to=' + encodeURIComponent(target);
            const cache = await caches.open(CACHE_VERSION);
            // Önce direkt cached offline.html'i al
            const cached = await cache.match(OFFLINE_URL);
            if (cached) {
                // 200 ile dön ama URL'i değiştir — küçük inline bootstrap ile target'ı sessionStorage'a yaz
                // Daha basitçe: offline.html zaten ?to= query'sini kendi okuyor (script eklenecek)
                return new Response(await cached.text(), {
                    status: 200,
                    headers: { 'Content-Type': 'text/html; charset=utf-8', 'X-Calibra-Offline': '1' },
                });
            }
            // Cache boşsa minimal fallback
            return new Response(
                '<!doctype html><meta charset="utf-8"><title>Bağlantı yok</title>' +
                '<body style="font-family:sans-serif;padding:40px;text-align:center;color:#0f172a">' +
                '<h1>Sunucuya bağlanılamıyor</h1>' +
                '<p>Lütfen birazdan tekrar deneyin.</p>' +
                '<button onclick="location.reload()">Tekrar Dene</button></body>',
                { status: 503, headers: { 'Content-Type': 'text/html; charset=utf-8' } }
            );
        }
    })());
});
