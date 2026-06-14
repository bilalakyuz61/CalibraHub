# WhatsApp Web Parity — Geliştirme Planı

CalibraHub'taki Baileys tabanlı WhatsApp entegrasyonunu **WhatsApp Web kullanıcı deneyimine** taşımak için yol haritası.

> Mevcut altyapı: `WhatsAppBridge` (Node + Baileys 6.7.18 sidecar, default port 61100), `WhatsAppService` (provider dispatcher), `WhatsAppInboxPollingService` (3 sn polling), `WaInboxMessage` + `wa_inbox` tablosu, `ClientApp/src/components/WhatsAppMessenger/WhatsAppMessenger.jsx` React shell.

---

## 0. Akut sorunlar (Faz öncesi acil düzeltme)

Şu an üretimdeki ekranda görünen iki belirgin hata. Bu iki sorun **diğer hiçbir faza geçilmeden** önce çözülmeli, çünkü tüm WhatsApp Web parity işi `wa_contact` üzerinden konuşacak.

### 0.1 LID (Linked ID) görünüm sorunu

**Belirti:** Sohbet listesinde `178477668003861` gibi 15-16 haneli, telefon numarası olmayan dizgi görünüyor.

**Sebep:** WhatsApp 2024'ten itibaren **`@lid` JID** formatını yaygınlaştırdı (privacy). Yeni kişiler ve özellikle gruplardan gelen mesajlar `xxx@lid` JID'i ile düşüyor. Bridge'in döndüğü `chatId` doğrudan `wa_inbox.contact_phone` kolonuna yazılıyor — telefon olmadığı için ekranda olduğu gibi görünüyor.

**Çözüm:**
1. Bridge tarafında `lidMapping.getLIDsForPN()` / `lidMapping.getPNForLID()` API'lerini kullanarak LID ↔ telefon eşlemesi tut.
2. Baileys event'i `lid-mapping.update` dinle, mapping'leri DB'ye yaz (`WaContact` tablosu, aşağıda).
3. Inbound mesaj yazılırken: önce LID'i resolve etmeyi dene → telefon bulunursa o JID ile kaydet; bulunamazsa LID ile kaydet ama `is_lid = 1` flag'i tutulsun.
4. UI'da LID görünmesin: contact resolve olmazsa display olarak `"Bilinmeyen kişi"` + alt satırda kısaltılmış LID (`…3861`). Çıplak LID **asla başlık olarak gösterilmez**.

### 0.2 Aynı kişi için birden çok sohbet

**Belirti:** Aynı kişi (örn. Bilal Akyüz) hem telefon-JID hem LID üzerinden geldiğinde iki ayrı sohbet kartı oluşuyor; ya da numara farklı formatta normalize edildiği için (`+90...`, `90...`, `0...`) duplicate düşüyor.

**Sebep:**
- `WhatsAppService.NormalizePhone` ve controller'lardaki normalize farklı davranıyor (4 yerde duplicate kod).
- `wa_inbox` kayıtları `contact_phone` üzerinden gruplanıyor — string match, normalize farkı = ayrı sohbet.
- `wa_inbox.contact_id` kolonu var ama FK constraint yok ve doldurulmuyor.

**Çözüm:**
1. `WaContact` master tablosu (aşağıdaki şema). Bir kişi bir kayıt; alternatif JID'leri (telefon JID + LID + diğer cihazlar) child tabloda tutulur.
2. `wa_inbox.contact_phone` artık **display field**; runtime gruplama her zaman `wa_inbox.ContactId` (INT FK) üzerinden yapılır. CLAUDE.md "ID tabanlı eşleştirme" kuralına uyumlu.
3. Tek `WaPhoneNormalizer` utility'si (E.164 formatına çevirir, baştaki `+`, `00`, `0` kırpar, sadece rakam bırakır). Tüm controller/service/checker buna delege eder. Mevcut 4 farklı normalize **silinir**.
4. Inbound pipeline: `normalize(jid) → WaContactResolver.GetOrCreateAsync(jid, pushName) → wa_inbox.ContactId = contact.Id`.

---

## 1. Faz 1 — Contact Unification & DB temeli

Tüm sonraki fazların önkoşulu. Şema değişiklikleri, idempotent migration ile uygulanır (`CalibraDatabaseInitializer`'a `EnsureWaContactTablesAsync` benzeri pass).

### 1.1 Yeni tablolar (PascalCase — CLAUDE.md kuralı)

```sql
CREATE TABLE dbo.WaContact (
    Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_WaContact PRIMARY KEY,
    PrimaryPhone    NVARCHAR(32)  NULL,                      -- E.164, normalize edilmiş; LID-only ise NULL
    DisplayName     NVARCHAR(200) NULL,                      -- pushName veya CalibraHub kişi rehberinden
    ProfilePicUrl   NVARCHAR(500) NULL,
    LastSeen        DATETIME2     NULL,
    PresenceStatus  NVARCHAR(20)  NULL,                      -- online | offline | composing | recording
    LinkedContactId INT           NULL,                      -- CalibraHub iç kişi/müşteri FK (varsa)
    IsBlocked       BIT           NOT NULL CONSTRAINT DF_WaContact_IsBlocked DEFAULT 0,
    IsActive        BIT           NOT NULL CONSTRAINT DF_WaContact_IsActive DEFAULT 1,
    CreatedBy       NVARCHAR(120) NULL,
    Created         DATETIME2     NOT NULL CONSTRAINT DF_WaContact_Created DEFAULT SYSUTCDATETIME(),
    UpdatedBy       NVARCHAR(120) NULL,
    Updated         DATETIME2     NULL,
    CONSTRAINT UX_WaContact_PrimaryPhone UNIQUE (PrimaryPhone) WHERE PrimaryPhone IS NOT NULL
);

CREATE TABLE dbo.WaContactJid (
    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_WaContactJid PRIMARY KEY,
    ContactId   INT          NOT NULL CONSTRAINT FK_WaContactJid_WaContact REFERENCES dbo.WaContact(Id),
    Jid         NVARCHAR(80) NOT NULL,                       -- 905xxx@s.whatsapp.net | xxx@lid | xxx@g.us
    JidType     NVARCHAR(20) NOT NULL,                       -- phone | lid | group | broadcast
    IsPrimary   BIT          NOT NULL CONSTRAINT DF_WaContactJid_IsPrimary DEFAULT 0,
    Created     DATETIME2    NOT NULL CONSTRAINT DF_WaContactJid_Created DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UX_WaContactJid_Jid UNIQUE (Jid)
);
CREATE INDEX IX_WaContactJid_Contact ON dbo.WaContactJid(ContactId);
```

### 1.2 `wa_inbox` üzerinde değişiklikler

```sql
-- mevcut kolon vardı, FK constraint ekle ve doldur
ALTER TABLE dbo.wa_inbox
    ADD CONSTRAINT FK_wa_inbox_WaContact FOREIGN KEY (contact_id) REFERENCES dbo.WaContact(Id);

CREATE INDEX IX_wa_inbox_contact_received
    ON dbo.wa_inbox(contact_id, received_at DESC);
```

Backfill migration: mevcut `wa_inbox.contact_phone` distinct alınır, normalize edilir, `WaContact` satırları üretilir, eski rowlar `contact_id` ile güncellenir.

> **Naming kural notu:** `wa_inbox` legacy listede — yeni kolon eklerken o tablonun stiline uyuluyor (snake_case kalır). Ama yeni tablolar (`WaContact`, `WaContactJid`) tam PascalCase — CLAUDE.md DB Naming Convention'ı.

### 1.3 `WaPhoneNormalizer` (tekleştirilmiş utility)

```csharp
// src/CalibraHub.Application/WhatsApp/WaPhoneNormalizer.cs
public static class WaPhoneNormalizer
{
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length == 0) return null;
        if (digits.StartsWith("00")) digits = digits[2..];
        if (digits.Length == 10 && !digits.StartsWith("9")) digits = "90" + digits; // TR default
        if (digits.StartsWith("0"))  digits = "90" + digits[1..];
        return digits;
    }

    public static bool IsLid(string jid) => jid?.EndsWith("@lid", StringComparison.OrdinalIgnoreCase) == true;
    public static bool IsGroup(string jid) => jid?.EndsWith("@g.us", StringComparison.OrdinalIgnoreCase) == true;

    public static string ExtractLocalPart(string jid)
        => jid?.Split('@')[0] ?? string.Empty;
}
```

Mevcut 4 normalize implementasyonu (controller'lar + service + checker) bu sınıfa delege edilir, body'leri silinir.

### 1.4 `WaContactResolver`

```csharp
public interface IWaContactResolver
{
    Task<WaContact> GetOrCreateAsync(string jid, string? pushName, CancellationToken ct);
    Task<WaContact?> ResolveLidAsync(string lidJid, CancellationToken ct); // bridge'e sorar
    Task RegisterLidMappingAsync(string lid, string phoneJid, CancellationToken ct);
}
```

Bridge'e ek endpoint: `GET /lid-resolve?jid=xxx@lid` → `{ phoneJid: "905...@s.whatsapp.net" | null }`. Baileys'in `signalRepository.lidMapping` API'sini sarar.

---

## 2. Faz 2 — Real-time core (SignalR + presence)

3 sn polling'i sıfırlamak. Mesaj geldiği an UI'a push edilecek.

### 2.1 SignalR hub

`src/CalibraHub.Web/Hubs/WhatsAppHub.cs` — kullanıcı kendi tenant grubuna bağlanır. Bridge → Web tarafında HTTP POST yerine, **Bridge bir WebSocket client** olarak hub'a bağlanır ve event'leri stream eder.

Alternatif (daha basit): `WhatsAppInboxPollingService` 3 sn yerine **uzun bekleme + Server-Sent Events**'e geçer — Bridge `/events` SSE endpoint'i açar, polling servisi tek bağlantı kurar, mesaj geldiğinde anında alır ve hub'a push eder.

Hub event'leri:
- `MessageReceived(message)`
- `MessageStatusUpdate(messageId, status)`  ← single tick → double tick → blue
- `PresenceUpdate(contactId, status, lastSeen)`
- `TypingUpdate(contactId, isTyping)`
- `MessageReaction(messageId, emoji, fromContactId)`
- `MessageDeleted(messageId)`
- `ProfilePictureUpdate(contactId, url)`

### 2.2 Bridge endpoint'leri (eklenecek)

| Endpoint | Yön | Açıklama |
|---|---|---|
| `POST /send-read-receipt` | out | Karşı tarafa "okundu" sinyali (`readMessages(keys)`) |
| `POST /send-typing` | out | `presenceSubscribe(jid) + sendPresenceUpdate('composing', jid)` |
| `POST /send-reaction` | out | `sendMessage({ react: { text: '👍', key } })` |
| `POST /send-reply` | out | `sendMessage(jid, { text }, { quoted })` |
| `POST /delete-message` | out | `sendMessage(jid, { delete: key })` |
| `GET /profile-pic?jid=` | out | `profilePictureUrl(jid, 'image')` |
| `GET /presence?jid=` | out | `presenceSubscribe(jid)` + cached store |
| `GET /events` (SSE) | in | Tüm event stream'i (mesaj, presence, receipt, typing, …) |

### 2.3 `wa_inbox`'a yeni kolonlar (snake_case — legacy stil)

```sql
ALTER TABLE dbo.wa_inbox ADD message_type      NVARCHAR(20)  NULL;   -- text|image|audio|video|document|sticker|location|contact|reaction|deleted
ALTER TABLE dbo.wa_inbox ADD quoted_msg_id     NVARCHAR(80)  NULL;   -- reply için
ALTER TABLE dbo.wa_inbox ADD reaction_emoji    NVARCHAR(20)  NULL;
ALTER TABLE dbo.wa_inbox ADD is_forwarded      BIT           NOT NULL CONSTRAINT DF_wa_inbox_is_forwarded DEFAULT 0;
ALTER TABLE dbo.wa_inbox ADD is_deleted        BIT           NOT NULL CONSTRAINT DF_wa_inbox_is_deleted   DEFAULT 0;
ALTER TABLE dbo.wa_inbox ADD delivery_status   NVARCHAR(20)  NULL;   -- sent|delivered|read|failed
ALTER TABLE dbo.wa_inbox ADD delivered_at      DATETIME2     NULL;
ALTER TABLE dbo.wa_inbox ADD read_by_peer_at   DATETIME2     NULL;   -- karşı tarafın okuma zamanı
```

---

## 3. Faz 3 — Mesajlaşma feature parity

| Özellik | Bridge | Backend | Frontend |
|---|---|---|---|
| Reply (quoted) | `sendMessage({...}, { quoted })` | `quoted_msg_id` kolonu | swipe-right ile reply paneli + mesaj üstünde quoted card |
| Reaction | `sendMessage({ react: ... })` | `reaction_emoji` + audit | hover/long-press picker (👍 ❤️ 😂 😮 😢 🙏) |
| Forward | `sendMessage(jid, { forward })` | yeni mesaj kaydı | mesaj menüsü → kişi seçici |
| Mesaj silme (her iki yön) | `sendMessage({ delete: key })` | `is_deleted` + tombstone gösterim | "Bu mesaj silindi" placeholder |
| Voice message (PTT) | `sendMessage({ audio, ptt: true })` | `message_type='audio'` | MediaRecorder API + dalga formu önizleme |
| Mesaj arama (chat içi) | — | LIKE sorgu + `IX_wa_inbox_search` | Ctrl+F üst panel |
| Star / favori | — | `wa_message_star` tablosu | yıldız ikonu + filtre |
| Mention (@) | otomatik (text içinde) | parse + highlight | composer'da @ → kişi listesi |

### 3.1 Voice mesaj akışı

Composer'a mikrofon butonu. Basılı tutulurken `MediaRecorder` (`audio/webm;codecs=opus`) kayıt → Bridge'e multipart upload → Baileys `audio` mesajı olarak gönder → `ptt: true` ile dalga formu görüntülenir.

### 3.2 Reaction UI

`WhatsAppMessenger.jsx`'te mesaj balonuna hover olunca yan tarafta `+` ikonu çıkar; tıklayınca picker (6 standart emoji + serbest). Mesaj balonunun alt-sağında reaction badge'i toplanır.

---

## 4. Faz 4 — Grup chat

WhatsApp Web'in olmazsa olmazı. Baileys tarafı tam destekli; CalibraHub tarafında veri modeli + UI yok.

### 4.1 DB

```sql
CREATE TABLE dbo.WaGroup (
    Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_WaGroup PRIMARY KEY,
    GroupJid        NVARCHAR(80)  NOT NULL CONSTRAINT UX_WaGroup_Jid UNIQUE,
    Subject         NVARCHAR(200) NOT NULL,
    Description     NVARCHAR(1000) NULL,
    ProfilePicUrl   NVARCHAR(500) NULL,
    OwnerContactId  INT NULL CONSTRAINT FK_WaGroup_Owner REFERENCES dbo.WaContact(Id),
    ParticipantsJson NVARCHAR(MAX) NULL,        -- contactId listesi cache
    IsActive        BIT NOT NULL CONSTRAINT DF_WaGroup_IsActive DEFAULT 1,
    Created         DATETIME2 NOT NULL CONSTRAINT DF_WaGroup_Created DEFAULT SYSUTCDATETIME(),
    Updated         DATETIME2 NULL
);

CREATE TABLE dbo.WaGroupMember (
    Id        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_WaGroupMember PRIMARY KEY,
    GroupId   INT NOT NULL CONSTRAINT FK_WaGroupMember_Group   REFERENCES dbo.WaGroup(Id),
    ContactId INT NOT NULL CONSTRAINT FK_WaGroupMember_Contact REFERENCES dbo.WaContact(Id),
    Role      NVARCHAR(20) NOT NULL CONSTRAINT DF_WaGroupMember_Role DEFAULT 'member',  -- admin|superadmin|member
    JoinedAt  DATETIME2 NOT NULL CONSTRAINT DF_WaGroupMember_JoinedAt DEFAULT SYSUTCDATETIME(),
    LeftAt    DATETIME2 NULL,
    CONSTRAINT UX_WaGroupMember_Unique UNIQUE (GroupId, ContactId)
);
```

`wa_inbox.contact_id` grup için `WaGroup`'a değil — grup mesajları için `wa_inbox.group_id INT NULL` kolonu eklenir (ya da `wa_inbox` `target_kind` + polymorphic). Pratik öneri: ayrı kolon.

### 4.2 Bridge

- `GET /groups` — `groupFetchAllParticipating()`
- `POST /group/create`
- `POST /group/{id}/participants` (add/remove/promote/demote)
- `POST /group/{id}/subject` (rename)
- `GET /group/{id}/invite-code`
- Inbound event: `groups.update`, `group-participants.update` → DB sync

### 4.3 UI

Sol listede grup ve birebir aynı listede karışık, ama avatar yerine grup ikonu + üye sayısı badge'i. Grup ekranında üstte üye listesi paneli, mesaj göndericinin adı her mesajın üstünde gösterilir.

---

## 5. Faz 5 — UI polish (WhatsApp Web hissi)

CLAUDE.md'deki "UI/UX Tasarım Sistemi" (memory: Apple/Linear/Vercel kalibresi) ile uyumlu olarak — ama WhatsApp Web jargonuna yaklaşan:

- **Sol panel:**
  - Üstte arama + filtre chip'leri (Tümü, Okunmamış, Gruplar, Arşiv)
  - Sohbet kartında: avatar, isim, son mesaj önizleme, saat, okunmamış badge, sessize alındı/sabitlendi ikonları
  - Sürekli polling değil — SignalR push ile **anlık** güncelleme
- **Sağ panel:**
  - Header: avatar + isim + "online" / "son görülme 14:32" / "yazıyor…"
  - Mesaj balonları: kendi mesajlar sağ + indigo gradient (CalibraHub dark mode), karşı mesajlar sol + slate
  - Çift tik (gönderildi/iletildi/okundu) — okundu = mavi
  - Mesaj balonu üzerine hover → reaction picker + menü (reply/forward/star/copy/delete)
  - Tarih ayracı ("Bugün", "Dün", "23 Mayıs Cumartesi") otomatik gruplama
- **Composer:**
  - Sol: emoji picker + 📎 attach (resim/video/belge/konum/kişi)
  - Sağ: metin boşken 🎤 mikrofon, doluyken ➤ gönder
  - Drag-drop dosya yükleme overlay
  - Ctrl+Enter ile gönder, Enter ile yeni satır (veya tersi — tercih ayarı)
- **Sağ üst menü:** sohbet bilgisi paneli (medya/dosya/link tab'ları, sessize al, arşivle, sil)
- **Bildirim:** browser notification + sound (Audio API), tab title'a `(3) WhatsApp`
- **Tema:** dark mode default (memory: UI tasarım sistemi)

### 5.1 Silme onayı — CLAUDE.md uyumu

Mevcut "3 saniye geri sayım" inline mekanizması var; CLAUDE.md zorunlu silme modal standardına **uymuyor**. Bu fazda `showConfirm({ title, message, okLabel })` Promise helper'ına geçirilir (referans: `Views/PriceList/Report.cshtml`):

```js
showConfirm({
    title: 'Sohbeti sil',
    message: `${contact.displayName} ile olan sohbet ve tüm mesajlar silinecek.`,
    okLabel: 'Sil',
    danger: true
}).then(ok => { if (!ok) return; deleteConversation(contact.id); });
```

---

## 6. Faz 6 — Operasyonel iyileştirmeler

Önceki denetim raporunda tespit edilen ortak eksikler:

- **Retry / outbox queue:** `WaOutboxQueue` tablosu (status: pending|sending|sent|failed|deadlettered), background worker exponential backoff (5s / 30s / 2m / 10m / 1h), Bridge timeout veya 5xx olunca otomatik retry.
- **Webhook HMAC** (Cloud API): `X-Hub-Signature-256` doğrulaması — Bridge tarafı n/a ama Cloud kullanılıyorsa kritik.
- **Token log maskeleme:** `ToPhone` log'larda son 4 hane görünür (`905******150`).
- **Test:** En az `WaPhoneNormalizer`, `WaContactResolver` ve `WhatsAppSafetyChecker` için xUnit test projesi.
- **Bridge kaynak kontrolü:** `WhatsAppBridge/index.js` repo'ya commit edilmeli (şu an sadece publish çıktısı var).

---

## 7. Faz sıralaması ve effort tahmini

| Faz | Effort | Açıklama |
|---|---|---|
| Faz 0 — LID + dedup | 1–2 gün | Akut, diğer fazların önkoşulu |
| Faz 1 — DB temel + WaContact | 1 gün | Şema + backfill + resolver |
| Faz 2 — SignalR / SSE + presence + typing + read receipt | 2 gün | "WhatsApp Web hissi"nin %60'ı |
| Faz 3 — Reply / reaction / voice / forward / silme | 2–3 gün | Feature parity |
| Faz 4 — Grup chat | 2–3 gün | Veri modeli + UI |
| Faz 5 — UI polish | 3–5 gün | Tasarım kalibresi |
| Faz 6 — Outbox retry + HMAC + test | 1–2 gün | Güvenilirlik |

**Toplam:** ~12–18 gün, sıralı geliştirme.

**Önerilen sıra:** 0 → 1 → 2 → 5 (UI polish'in temel kısmı) → 3 → 4 → 6.
UI polish'in temel kısmının (header'da presence, mesaj balonu çift tik, dark mode tüm panel) Faz 2'den hemen sonra gelmesi — "WhatsApp Web gibi çalışıyor mu?" sorusunu erken evet'e çevirir.

---

## 8. Kapsam dışı

- **Sesli/görüntülü arama:** Baileys desteklemiyor. Talep gelirse Twilio Voice gibi başka bir yol gerekir.
- **Çoklu hat (multi-account):** Tek bridge instance ile mümkün değil; her hesap için ayrı Node process + ayrı `WaContact` namespace gerekir. Şu an scope dışı.
- **WhatsApp Status (story):** Baileys destekliyor ama iş değeri belirsiz; talep üzerine eklenir.
- **End-to-end şifrelemenin kullanıcıya gösterimi:** Baileys altta E2E yapıyor; bunu UI'a "şifreli" rozetiyle yansıtmak kozmetik, ihtiyaca göre.

---

## 9. Referans dosyalar (mevcut)

- Service: `src/CalibraHub.Application/WhatsApp/WhatsAppService.cs`
- Safety: `src/CalibraHub.Application/WhatsApp/WhatsAppSafetyChecker.cs`
- Polling: `src/CalibraHub.Web/Services/WhatsAppInboxPollingService.cs`
- Controllers: `src/CalibraHub.Web/Controllers/WhatsApp*.cs`
- Repository: `src/CalibraHub.Persistence/Repositories/SqlWa*.cs`, `SqlWhatsApp*.cs`
- Domain: `src/CalibraHub.Domain/Entities/WhatsApp*.cs`, `WaInboxMessage.cs`
- DB init: `src/CalibraHub.Persistence/Database/CalibraDatabaseInitializer.cs` (`EnsureWhatsAppConfigTableAsync` 7325+)
- React: `ClientApp/src/components/WhatsAppMessenger/WhatsAppMessenger.{jsx,css}`
- Razor: `src/CalibraHub.Web/Views/WhatsApp/Index.cshtml`
- Bridge dist: `publish/WhatsAppBridge/` (kaynak henüz repo'da değil)
