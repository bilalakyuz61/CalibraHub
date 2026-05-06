# CalibraHub WhatsApp Bridge

WhatsApp Web QR yöntemiyle CalibraHub'ı telefon WhatsApp'ı ile köprülendiren Node.js servisi. CalibraHub.Web HTTP üzerinden bu servise mesaj gönderir; servis WhatsApp Web protokolü ile arka planda mesajı iletir.

## Mimari

```
[CalibraHub.Web :61001] ──HTTP─→ [Bridge :61100] ──WebSocket─→ WhatsApp Web servers
                                       ↑
                                  (telefondan QR taranmış oturum)
```

Bridge **sadece loopback (127.0.0.1)** üzerinde çalışır. Dış dünyaya açık değildir; firewall'da port açmaya gerek yoktur.

## Otomatik Kurulum (CalibraHub Setup ile)

`CalibraHubSetup-vX.Y.Z.exe` çalıştırıldığında Bridge **otomatik olarak Windows Service** kayıt edilir:
- Servis adı: `CalibraHubWhatsAppBridge`
- Otomatik başlama: evet (boot ile)
- Çökme durumunda: 3 kere otomatik yeniden başlatma

Kurulum sonrası Sistem Ayarları → Şirket Ayarları → WhatsApp sekmesinden QR kodunu telefondan tarayıp bağlanırsın.

## Manuel Kurulum (geliştirme veya tek başına)

### Gereksinim
- **Node.js 18+** (https://nodejs.org)
- Windows admin yetkisi (servis kuruluğu için)

### Adımlar

```powershell
# 1) Bağımlılıkları yükle (~80 MB Chromium dahil ilk kurulum)
npm install

# 2) Direkt çalıştır (servis kurmadan)
npm start
```

Açılan terminal'de QR kodu çıkar → telefondan WhatsApp → Bağlı Cihazlar → Cihaz Bağla → tara.

### Windows Service olarak kurmak

```powershell
# Admin PowerShell:
node install-service.js   # CalibraHubWhatsAppBridge servisi kayıt + başlatma

# Kaldırmak:
node uninstall-service.js
```

## HTTP API

| Endpoint | Metod | Body | Cevap |
|---|---|---|---|
| `/status` | GET | — | `{ state, displayName, phone, qrAvailable }` |
| `/qr` | GET | — | `{ qr: "data:image/png;base64,..." \| null }` |
| `/send` | POST | `{ to, text }` | `{ ok, messageId }` veya `{ ok:false, error }` |
| `/logout` | POST | — | `{ ok }` |

`state` değerleri:
- `connecting` — Puppeteer/WhatsApp Web başlatılıyor
- `awaiting_qr` — QR oluştu, telefondan taranması bekleniyor
- `ready` — bağlandı, mesaj gönderebilir

## Port

Default `61100`. Değiştirmek için:
```powershell
$env:PORT = "61105"
npm start
```

CalibraHub.Web tarafında **Şirket Ayarları → WhatsApp → Bridge URL** alanına `http://localhost:61105` yazılır.

## Oturum Persistance

`session-data/` klasöründe LocalAuth saklı. Bridge restart olunca QR taramaya gerek yok, telefon bağlı kalır (telefonun online olması yeterli).

Yeni telefon bağlamak için: `npm start` ile çalıştır → `/logout` endpoint'i çağır → yeni QR çıkar.

## Sorun Giderme

| Belirti | Sebep | Çözüm |
|---|---|---|
| `npm install` Chromium download yarıda kalıyor | Network/proxy | `set PUPPETEER_DOWNLOAD_HOST=https://npmmirror.com/mirrors` sonra tekrar |
| Servis başlamıyor (`Get-Service CalibraHubWhatsAppBridge`) | Node.js PATH'te yok | Servis user'ı (LocalSystem) için Node.js yolunu ayarla |
| `state: connecting` 60sn sürüyor | Chromium ilk açılış | Normal, ilk başlatma yavaş |
| `state: awaiting_qr` ama QR'a erişilemiyor | CalibraHub.Web Bridge'e bağlanamıyor | Bridge URL doğru mu? Servis çalışıyor mu? |
| Mesaj gitmiyor "Bridge zaman asimi" | Telefon offline / WhatsApp logout | Telefonun WhatsApp'ı online olmalı |
| Geçici "session disconnected" | WhatsApp Web koruma protokolü | LocalAuth ile auto-reconnect; 1-2 dk içinde geri gelir |

## Uyarı: ToS

WhatsApp resmi olmayan otomasyon için Service Terms'i ihlal sayar. Düşük hacimli kullanım (günlük 50-200 mesaj) genelde tespit edilmez ama:
- ❌ Toplu pazarlama → hızla ban
- ❌ Saniyede 10+ mesaj → spam algoritması
- ❌ Cold/uzun mesajlaşmamış numaralara ardışık → şüpheli

CalibraHub Safety Layer (rate limit + cooldown + identik mesaj koruma) bu riskleri minimize eder ama tamamen ortadan kaldırmaz. Production müşterileri için **Cloud API** önerilir.
