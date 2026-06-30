# CalibraHub – Agent Çalışma Kuralları

## Build & run sorumluluğu

Tüm geliştirme akışı Claude üzerinden yürür. Kod değişikliği yaptığında **build alıp gerekirse sunucuyu yeniden başlatmak senin işin** — kullanıcıdan bunu istemene gerek yok.

### Akış
- **C# / `.cshtml` değişikliği** → `dotnet build` veya doğrudan `dotnet run` ile yeniden başlat. Önce port 61001'i kontrol et (`netstat`); açıksa çalışan process'i sormadan durdurup yeniden başlat. 61001 yalnızca CalibraHub içindir, üzerinde çalışan herhangi bir process her zaman güvenle durdurulabilir (worktree veya ana proje farketmez).
- **`.jsx` / CSS değişikliği** → `wwwroot/react/` bundle'ı `npm run build` ile üretilir. JSX'te değişiklik varsa build et, ardından backend'i yeniden başlat (yeni bundle'ın yüklenmesi için).
- **Run sonrası** → "Now listening on" log'unu bekle, smoke test yap (`curl localhost:61001/`), 200/302 görünce hazırdır.
- **Verification gerekiyorsa** → `preview_start` + browser snapshot/console serbest. Backend zaten 61001'de çalışıyorsa preview tool'una sadece tarayıcı kontrolü için ihtiyaç var.

### Önemli
- Background process'leri `run_in_background: true` ile başlat, output dosyasını Read ile takip et.
- Build sonrası warning'ler (CS8602, CA1416 gibi) projedeki mevcut nullable warning'lerdir — fix etme. Yalnızca senin değişikliğinle gelen yeni hatayı düzelt.
- Port 61001'de çalışan process: kim başlattığı farketmez, sormadan `Stop-Process -Force` ile durdurabilirsin (port CalibraHub'a özel).

### Worktree / çoklu ajan kuralı
- **Tüm dosya değişiklikleri ana proje dizininde yapılır:** `D:\JetBrainsRider\Projeler\CalibraHub\src\...`  
  Worktree içinde çalışıyor olsan bile (`.claude\worktrees\...`) dosyaları worktree'ye değil ana projeye yaz.
- **`dotnet run` ve `npm run build` komutları ana proje dizininden çalıştırılır.**  
  Worktree kendi `src/` kopyasına sahip olsa da derleme/çalıştırma hep ana proje üzerinden olur.
- **Neden:** Birden fazla ajan aynı projeyi farklı worktree'lerden build/run yaptığında port çakışması ve dosya uyumsuzluğu oluşur. Ana proje tek kaynak olarak kalır.

## Mimari kararlar — KALIN UYULMASI GEREKEN

### DepartmentManager Rolü — Bilinçli Bypass Kararı (2026-06-25)

`PermissionService.CheckAsync` içinde `DepartmentManager` rolü, `SetupDefinitions` ve `Scheduler` dışında tüm formlara DB grant'larına bakmaksızın `true` döndürmektedir.

**Sebep:** DepartmentManager'ın kendi departmanındaki her operasyonu yönetmesi beklenmekte, granüler kısıtlama ihtiyacı henüz gündeme gelmemiştir.

**YAPILMAMASI GEREKEN:** Bu bypass'ı sessizce kaldırmak. Kaldırılması gerekirse önce tüm DeptManager kullanıcılarına gerekli grant'lar atanmalı, ardından bypass kaldırılmalıdır.

**Yeniden değerlendirme şartı:** Birden fazla müşteri "DepartmentManager farklı formlara farklı erişimde olsun" talebinde bulunursa bypass kaldırılıp grant matrix'e dahil edilebilir.

### ENGINE Architecture — KARARLAŞTIRILDI: YAPILMAYACAK (2026-06-10)

Metadata-driven engine motoru (`engine.Entity` + `engine.Field` + dynamic DDL) vizyonu **tamamen rafa kaldırıldı**. İlgili tüm kod (interfaces, services, controllers, DB schema) kaldırıldı. Şu kararlar **bilinçli** olarak verildi:

- **Sebep:** 1-3 kişilik uyarlama ekibi + canlı kopya yok + öncelik "hızlı ayağa kaldırma + stabilite". Engine 23-32 günlük inşa + sonsuza dek paralel kod (Strangler Fig) yükü getiriyordu. Mevcut **WidgetMas EAV** sistemi customer-spesifik form/alan ihtiyaçlarını zaten %85+ karşılıyor.
- **Yeni form/alan ihtiyacında yapılacak:** Önce `WidgetMas` ile çözmeyi dene. Admin alan rehberi (`/Admin/ViewSettings`) ile dinamik alan ekle. Bu yetmiyorsa standart C# kodu yaz (form-spesifik controller + entity + table).
- **YAPMA:** `engine.Entity` benzeri "dynamic DDL" sistemleri, runtime-defined motor yapıları, `DynamicDdlService` ALTER TABLE servisleri. Eğer ihtiyaç doğarsa, **önce CLAUDE.md güncelleyip ardından mimar tartışması yap** — direkt kodlama yasak.
- **Yeniden değerlendirme şartı:** 12+ ay canlıda çalıştıktan sonra eğer 5+ farklı müşteri "kendi form/motor tasarlamak" talebinde bulunursa, engine vizyonu yeniden gündeme alınabilir. O zamana kadar konu kapalı.

## Güvenlik Kararları — KALIN UYULMASI GEREKEN

### Genel yetkilendirme katmanı

CalibraHub cookie tabanlı kimlik doğrulama kullanır. Tüm controller'lar varsayılan olarak `[Authorize]` gerektirir; `[AllowAnonymous]` yalnızca aşağıdaki özel gerekçelerle kullanılır.

### DB Ayarları Endpoint'leri — Kurulum Koruması (2026-06-26)

`AccountController`'daki üç endpoint (`GetDbSettings`, `TestDbSettings`, `SaveDbSettings`) kurulum sihirbazı ve Sistem Yönetimi sayfası için kullanılır.

**Erişim kuralı:**
- **Kurulum tamamlanmamışsa** (DB'de hiç şirket kaydı yok) → Anonim erişime açık. Gerekçe: İlk kurulumda henüz giriş yapabilecek kullanıcı yoktur.
- **Kurulum tamamsa** (en az bir şirket kaydı var) → Yalnızca giriş yapmış kullanıcı erişebilir.
- **Ek katman:** Sistem Yönetimi sayfasına zaten ayrıca şifreli giriş gerektirilmektedir.

**Uygulama:** Her üç endpoint'in başında `IsSetupCompleteAsync()` private helper kontrolü yapılır. Bu helper DB'ye erişemiyorsa (kurulum başlangıcı) `false` döner → anonim geçer.

**YAPILMAMASI GEREKEN:** Bu endpoint'leri tekrar `[AllowAnonymous]` bırakmak. Kurulum koruması kaldırılırsa uygulama bağlantı dizesi dışarıdan değiştirilebilir hale gelir.

**Sistem Yönetimi şifre katmanı:** Mevcut ayrı şifre koruması korunmalı; bu guard onun yerine geçmez, ek bir savunma katmanıdır.

### ShopFloor (Fabrika Katı) Endpoint'leri — İki Katmanlı Auth (2026-06-26)

ShopFloor, üretim operatörlerinin iş emri operasyonlarını başlatıp tamamladığı tablet/kiosk ekranıdır. Erişim **iki katmanlıdır**:

1. **CalibraHub oturumu (cookie)** — `/Production/ShopFloor` sayfasına erişmek için standart kullanıcı girişi gerekir. Tüm ShopFloor endpoint'leri `[Authorize]` kapsamındadır (eski `[AllowAnonymous]` kaldırıldı).
2. **Operatör PIN / NFC kart** — Her operasyon başlatma/tamamlama işleminde `AuthOperator` endpoint'i üzerinden personel kimliği doğrulanır. Hatalı deneme sayısı aşılınca personel deaktifleştirilir (`ShopFloorLockoutTracker`).

**Oturum süresi:** Henüz tanımlanmamış. Kiosk tablette oturum süresi dolduğunda ekran login sayfasına düşer; tekrar giriş gerekir. İleride uzun süreli oturum veya otomatik yenileme kararı alınırsa burayı güncelle.

**YAPILMAMASI GEREKEN:** ShopFloor endpoint'lerine tekrar `[AllowAnonymous]` eklemek.

## Diğer kurallar

- DB tasarımında kısa tablo/kolon isimleri, INT PK/FK kullan; SQL entegrasyonu önceliklidir.
- Veri giriş ekranlarında sol tab menüsü + sağ seçili sekme içeriği standardı (`st-modal-body--tabbed`).
- Sebep net tespit edildiyse plan dökümanı yazma — direkt Edit + kısa açıklama.
- **Boolean alanlar için checkbox değil switchkey (toggle switch) kullanılır.** Aktif/Pasif, Makine/Depo gibi açma-kapama girişleri her zaman switch kontrolüyle gösterilir. Native `<input type="checkbox">` ham haliyle UI'a düşmez — ya Bootstrap `form-check form-switch` ya da custom switch CSS pattern'i kullanılır (track + sliding thumb). Form içinde "evet/hayır" değeri toplayan tüm yerlerde geçerlidir.
- **Başlık/etiket metinleri Title Case (İlk Harfler Büyük), ALL-CAPS değil.** Bölüm başlıkları, panel başlıkları, form etiketleri "Rapor Özellikleri", "Veri Kaynağı", "Görünüm" gibi yazılır — `text-transform: uppercase` ile büyütülmez. Metin zaten JSX/Razor'da doğru yazılır; CSS yalnızca gösterir. Yeni CSS yazarken başlık/label sınıflarına `text-transform: uppercase` ekleme. (İstisna: kısa kod/tip rozetleri — 2-3 harfli durum etiketleri kalabilir.)

## CSS ve Tema Kuralları

CalibraHub iki temayı destekler: **light** ve **dark**. Tema, `<body>` üzerindeki `app-theme-light` / `app-theme-dark` class'ı ile kontrol edilir. Yeni CSS/JSX yazarken aşağıdaki kurallara uy.

### Tek dark selector: `body.app-theme-dark`

Koyu tema override'ları için **yalnızca** `body.app-theme-dark` kullanılır.

| ❌ YANLIŞ | ✅ DOĞRU |
|-----------|---------|
| `.dark .my-el` | `body.app-theme-dark .my-el` |
| `html.dark .my-el` | `body.app-theme-dark .my-el` |
| `[data-theme="dark"] .my-el` | `body.app-theme-dark .my-el` |

**Sebep:** Tailwind'in `.dark`, Daisy UI'ın `[data-theme]` vb. ile karışmaz. Tek nokta olur, grep ile bulunur.

### Light = default pattern

CSS'in tüm renk değişkenleri **light değerler** olarak tanımlanır, `body.app-theme-dark` onları ezer.

```css
/* ✅ Doğru: Light default → Dark override */
html body {
  --my-bg: #ffffff;
  --my-text: #0f172a;
}
body.app-theme-dark {
  --my-bg: #1e293b;
  --my-text: #e2e8f0;
}

/* ❌ Yanlış: Dark default → Light override (anti-pattern) */
html body {
  --my-bg: #1e293b;   /* dark default = tema sınıfı olmayan sayfalarda koyu çıkar */
}
body.app-theme-light {
  --my-bg: #ffffff;
}
```

### React bileşenlerinde tema: `className` + CSS değişkenleri

JSX inline style içinde hardcoded hex/rgba **kullanılmaz**. Bunun iki zararı var:
1. **Light modda hep koyu görünür** (tema sınıfından etkilenmez).
2. **Native form kontrollerinde whiteness** — `color-scheme` sinyali eksik olduğunda tarayıcı checkbox/input'u light olarak çizer.

**Doğru pattern:**
```css
/* ComponentName.css */
.cn-root {
  color-scheme: light;                /* native kontroller light mod */
  --cn-bg: #f1f5f9;
  --cn-text: #0f172a;
  background: var(--cn-bg);
  color: var(--cn-text);
}
body.app-theme-dark .cn-root {
  color-scheme: dark;                 /* native kontroller dark mod */
  --cn-bg: #0b1220;
  --cn-text: #e2e8f0;
}
```
```jsx
/* ComponentName.jsx */
<div className="cn-root" style={{ height: '100%' }}>   {/* layout-only inline */}
```

Referans implementasyon: `IntegrationQueue.jsx` + `IntegrationWizard.css` → `.iq-root` (2026-06-08).

### `color-scheme` zorunluluğu

Native form kontrolleri (`<input>`, `<select>`, `<checkbox>`, scrollbar) `color-scheme: dark` olmadan her zaman beyaz/light render eder — `body.app-theme-dark` class'ı tek başına yetmez.

**Shell.jsx** her tema değişiminde `html.style.colorScheme = isDark ? 'dark' : 'light'` set eder — bu iframe'lere de yansıtılır.  
Standalone React bileşeni yazarken kendi root elementine `color-scheme` ekle (yukarıdaki pattern).

### font-weight geçerli değerleri

CSS spec yalnızca **100 basamaklı** değerleri tanır: 100, 200, 300, 400, 500, 600, 700, 800, 900.  
`font-weight: 560`, `620`, `640`, `650` gibi ara değerler **geçersizdir** — tarayıcı yuvarlar ama bu tutarsızlığa yol açar.

| Sık kullanılan | Eşleşme |
|----------------|---------|
| Normal metin | 400 |
| Orta vurgu | 500 |
| Yarı-kalın | 600 |
| Kalın başlık | 700 |

### Monospace font stack

Kod, ID, timestamp alanları için:
```css
font-family: ui-monospace, Menlo, Consolas, monospace;
```
`Courier New` veya tek başına `monospace` kullanılmaz.

### Inline `<style>` ve `.cshtml` içi CSS

`.cshtml` içinde `<style>` bloğu yazarken Razor `@` karakteri atlatma gerekir:
- `@keyframes` → `@@keyframes` (Razor direktif çakışmasını önler)
- `@media` → `@@media`

Renk değerleri için CSS değişken fallback kullan:
```css
/* ✅ */
background: var(--app-surface, #fff);
color: var(--bs-body-color, #0f172a);
border: 1px solid var(--app-border, #e2e8f0);

/* ❌ */
background: #fff;
color: #1e293b;
```

### Denetim metodolojisi (tema audit)

Yeni ekran geliştirirken veya mevcut ekranı denetlerken kontrol listesi:

1. **Hardcoded hex grep** — `grep -r "#[0-9a-fA-F]\{3,6\}"` JSX/CSS dosyalarında
2. **rgba near-white grep** — `rgba(255,255,255,0\.[0-9])` ve `rgba(248,249` gibi pattern'lar (grep'e takılmayan near-white arka planlar)
3. **Selector grep** — `.dark .` ve `html.dark` → hepsi `body.app-theme-dark` olmalı
4. **`color-scheme` kontrolü** — standalone React bileşeni ise root'ta var mı?
5. **Bundle rebuild** — CSS değişikliği yaptıktan sonra `npm run build` zorunlu; eski bundle bellekteki eski CSS'i döndürür

## Silme onay standardı

Tüm silme/destruktif işlemler **ekranın ortasında** custom modal ile onay alır — browser native `confirm()` / `alert()` **kullanma** (sayfanın üst kenarında çıkar, tema uyumsuz).

**Modal yapısı (zorunlu):**
- Tam ekran backdrop (yarı şeffaf koyu, blur)
- Ortalanmış card: danger renkli ikon (kırmızı çöp/uyarı) + başlık + açıklama mesajı
- İki buton: **Vazgeç** (ghost/secondary) + **Sil** (`--danger` / kırmızı)
- Esc ile veya backdrop tıklamasıyla iptal, Enter ile onay
- Ok butonu varsayılan focus'ta

**Referans uygulama:** `Views/PriceList/Report.cshtml` → `showConfirm({ title, message, okLabel })` Promise tabanlı helper + `.plr-modal-backdrop` CSS. Yeni ekranlarda aynı pattern'i tekrarla; promise zincirinde `.then(ok => { if (!ok) return; ... })`.

Silme/destruktif fiil olmayan sıradan bilgilendirme/uyarılar için inline mesaj veya toast yeterlidir; modal sadece **kullanıcı onayı gerektiren** akışlarda zorunludur.

## DB Naming Convention

CalibraHub yıllar içinde snake_case'ten PascalCase'e geçiş yaptı; rename migration'ları (`MigrateTableRenamesAsync`, `MigrateColumnRenamesAsync`) yön bunu gösterir. Kodda hâlâ hibrit görünüm var (eski tablolar dokunulmuyor) ama **yeni eklenen her tablo ve kolon aşağıdaki kurallara uyar**.

### Tablo isimleri
- **PascalCase, singular**: `Document`, `DocumentLine`, `Integration` (çoğul kullanılmaz: `Documents` ❌). İstisna: doğal çoğul olanlar (`Items`, `Currencies`, `MaterialGroups`).
- **Prefix yok**: `CALIBA_`, `CL_`, `TBL_`, `tbl_` gibi prefix kullanılmaz.
- **İlişki tabloları**: ana entity adı + ek (`DocumentLine`, `ItemFeatureMappings`, `IntegrationMapping`).
- **Snake_case legacy tablolarda yeni tablo türetme**: yeni bağımlı tablo PascalCase olur (`document_types` legacy ama yeni eklenmesi gereken `DocumentTypeAttachment` olur, `document_type_attachment` değil).

### Kolon isimleri
- **PascalCase**: `DocumentNumber`, `CompanyId`, `IsActive`, `TaxRate`.
- **PK her zaman `Id`**: `[Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY` — tablo prefix'i yok (`DocumentId` PK olamaz, sadece FK olur).
- **FK her zaman `{Entity}Id`**: `CompanyId`, `ItemId`, `ParentDocumentId`. Asla string FK yok (`MaterialCode` FK olamaz).
- **Boolean kolon**: `Is{Quality}` veya `Has{Quality}` — `IsActive`, `IsMachinePark`, `HasChildren`.
- **Display alanları (klasik üçlü)**: `Code` (kısa, unique), `Name` (kullanıcıya gösterim), `Description` (opsiyonel uzun).
- **Audit dörtlüsü (her tabloda olmalı)**:
  - `Created    DATETIME NOT NULL DEFAULT SYSUTCDATETIME()`
  - `Updated    DATETIME NULL` (UPDATE'te set edilir)
  - `CreatedBy  NVARCHAR(120) NULL`
  - `UpdatedBy  NVARCHAR(120) NULL`
- **Eski snake_case tablolarda yeni kolon eklerken o tablonun stiline uy** — `User` tablosuna eklenen yeni alan `phone_number`, `PhoneNumber` değil. Sebep: SELECT/INSERT'leri tutarlı tut, audit ve raporlamada karışıklık yaratma.

### Index ve constraint isimlendirme
- **Normal index**: `IX_{Table}_{Columns}` — `IX_IntegrationRun_Integration_Started`
- **Unique index**: `UX_{Table}_{Columns}` — `UX_PriceList_Unique_Active`
- **Foreign key**: `FK_{Table}_{ReferencedTable}` — `FK_IntegrationMapping_Integration`
- **Default constraint**: `DF_{Table}_{Column}` — `DF_Document_IsActive`
- **Primary key**: `PK_{Table}` — `PK_Document`
- **Filtered unique** (soft-delete safe): UNIQUE + `WHERE [IsActive] = 1`

### Standart kolon seti (her yeni tablo iskeleti)
```sql
CREATE TABLE dbo.<TableName> (
    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_<TableName> PRIMARY KEY,
    -- ... domain-specific kolonlar (PascalCase)
    IsActive    BIT          NOT NULL CONSTRAINT DF_<TableName>_IsActive DEFAULT 1,
    CreatedBy   NVARCHAR(120) NULL,
    Created     DATETIME      NOT NULL CONSTRAINT DF_<TableName>_Created DEFAULT SYSUTCDATETIME(),
    UpdatedBy   NVARCHAR(120) NULL,
    Updated     DATETIME      NULL
);
```

### Tip standartları
- **PK / FK**: `INT IDENTITY` (PK), `INT` (FK). `BIGINT` sadece audit/log tabloları için (`IntegrationRun.Id`, `whatsapp_send_log.id`).
- **String boyutları**: `NVARCHAR(50)` kod alanları, `NVARCHAR(200)` ad alanları, `NVARCHAR(1000)` kısa açıklama, `NVARCHAR(MAX)` serbest metin/JSON.
- **Tarih**: `DATETIME` (UTC). Tüm tarih kolonlarında standart. `DATETIME2` kullanılmaz — 2026-06-11'de tüm mevcut DATETIME2 kolonları DATETIME'a migrate edildi.
- **Decimal**: `DECIMAL(18,4)` para/miktar, `DECIMAL(5,2)` oran/yüzde.
- **JSON**: `NVARCHAR(MAX)` — SQL Server'da native JSON tipi yok.

### Multi-tenant kolon
- Per-company DB mimarisi var → **`CompanyId` kolonu YOK** (tablo zaten ait olduğu DB'de). Master DB tabloları (`dbo.Company`) hariç.

### Legacy istisnalar (refactor edilmiyor)
Mevcut snake_case tablolar **kasıtlı olarak dokunulmuyor**: çalışıyor, kapsamlı view/proc/repository bağımlılıkları var, riske girilmez. Liste (özet): `user_settings`, `notes*`, `card_groups*`, `integration_api_profiles`, `integration_event_*`, `sales_quote_line_details`, `sales_representatives`, `document_types`, `currencies`, `whatsapp_*`, `wa_inbox`, `item_locations`, `design_templates`. Bu tablolara yeni kolon eklendiğinde o tablonun stiline uyulur.

Refactor şart olduğunda **full-rename + view backward-compat + migration** yaklaşımı: PriceGroup/PriceList örneğine bak (`MigrateColumnRenamesAsync` içinde `group_code → Code`, `price_group_id → GroupId` gibi).

## ID tabanlı eşleştirme kuralı

**Tüm karşılaştırma / dedup / match / FK ilişkileri ID üzerinden yapılır.** String alanları (kod, ad, açıklama) sadece kullanıcıya gösterim içindir; runtime karşılaştırmada **kullanılmaz**.

### Uygulama
- **FK kolonları**: Tüm tablolarda referans verilen kolon **`int` PK** olur. String code/name FK olarak kullanılmaz (örn. `MaterialCode` yerine `ItemId`).
- **DTO'lar**: Karşılaştırma gerektiren her DTO ilgili `*Id` alanını taşır (örn. `CombinationFeatureValueDto.FeatureValueId`). Display için `Code`/`Name` ek olarak yer alabilir, ama karar logiclerinde kullanılmaz.
- **Service match/dedup**: `OrderBy(id) → array eşitliği` desenle. String NormKey/lowercase trick'ini kullanma — Türkçe karakter, whitespace, case farkları her zaman bug üretir.
- **API response**: `value`/`name`/`code`'a ek olarak `valueId`/`id` döndür. Frontend client-side kontrol veya cache key'leri için ID kullanır.
- **Yeni tablo tasarımı**: PK her zaman `INT IDENTITY`. Doğal anahtar (kod) UNIQUE INDEX ile tutulur, ama referanslar PK'ya verilir.

### Geriye dönük temizlik
Eski string-based match (NormKey, lowercase concatenation, Trim) gördüğünde bu kurala göre ID-tabanlı yeniden yazmaya çalış — DTO/Repository'ye gerekiyorsa ID alanı ekle.

### İstisna
Standart kod alanları (`GuideMas.ValueColumn = Code`) kullanıcıya gösterim için kalır. Ama o kod alanının başka tabloya FK olarak gitmesi yanlış — yeni tabloda referans `int FK_Id` olur.

### Bilinen ihlal — `CreatedBy` / `UpdatedBy` (string NVARCHAR(120))
Audit dörtlüsündeki `CreatedBy` ve `UpdatedBy` alanları şu an **string** (email/username) tutuluyor. **Kural ihlali**: `Users.Id` (INT) referans olması doğrudur. **Erteleme sebebi (2026-05-31 kararı)**: 42 tablo + 35 entity + 30+ repo + 20+ controller'ı kapsayan büyük bir refactor; canlı veri kaybetmeden Big Bang yapmak risk. **Plan**: Faz 1'de yeni tablolar `CreatedById INT NULL FK -> Users(Id)` ile tasarlanır (mevcut tablolar string kalır). Faz 2'de mevcut tablolar parça parça migrate edilir (önce kritik 10 tablo: Document, Item, Contact, Personnel, DocLayout, ApprovalFlow, Note, BOM, WorkOrder, Integration). Mevcut tablolarda yeni kayıt eklerken **hem `CreatedBy` (legacy)** hem ileride eklenecek **`CreatedById`** doldurulmalı.

**FK tasarım kararı (2026-06-11):** `CreatedById` FK'sinde `ON DELETE SET NULL` **kullanılmaz**. Kullanıcılar gerçek anlamda silinmez — sadece `IsActive = 0` yapılır (soft-delete). Dolayısıyla FK constraint ihlali riski yoktur; standart `FOREIGN KEY REFERENCES dbo.[User](Id)` yeterlidir. Kullanıcı silme işlemi yalnızca deaktifleştirme olarak uygulanır, fiziksel DELETE yasaktır.

## Kullanıcı tarafından girilen kod alanı yok kuralı

**Kullanıcı kod girmez. Tanımlama ekranlarında "Kod" inputu olmaz.** Personel, Departman, Makine, Operasyon vb. ana tanım kayıtlarında uniqueness **isim üzerinden** sağlanır (per-company veya global, entity bağlamına göre).

### Sebep
- ID tabanlı eşleştirme kuralı zaten kodu runtime'da gereksiz kılıyor (FK'ler INT, code sadece display)
- Kod alanı kullanıcıya yük bindiriyor — "ne yazayım, kuralı ne" sorusu
- Ad benzersizliği daha doğal: aynı isimli iki personel zaten karışıklık yaratır

### Uygulama
- **DB kolonu kalır** — backward-compat için silinmez (mevcut veri bozulmaz, eski DTO/sql sorgular çalışmaya devam eder)
- **UI'dan tamamen kaldırılır** — Edit form'da Kod inputu yok, list view'da Kod sütunu yok
- **Service tarafı**: `Code` alanı backend'de auto-türetilir
  - Yeni kayıt: `code = name` (truncated to DB max length, örn. 20-50 char) veya `MAC-{6-hex}` (Machine), `AUTO_{guid}` (fallback)
  - Update: mevcut `code`'u koru (değişmesin, eski referanslar bozulmasın)
- **Uniqueness check** ad üzerinden: `string.Equals(x.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase)` (kendisi hariç)
- **Hata mesajı**: `"Aynı isimde başka bir X zaten tanımlı: '{name}'"`

### Entegrasyonda kod eşleme ihtiyacı varsa
Üçüncü parti sistem (ERP, makine PLC, e-ticaret) ile entegre olurken o sistemin kendi kodunu CalibraHub kaydına bağlamak için **widget alanı** (Admin → Widget Tanımları → ilgili form code) tanımlanır. Örnek: makine için "ERP_KODU" widget'ı tanımla, integration servisi bu widget değerini okur.

### Standart isim uniqueness scope'ları
| Entity | Scope |
|--------|-------|
| Personnel | Global (aynı şirket içinde) |
| Department | Per-company (CompanyId + Name) |
| Machine | Global (aynı şirket içinde) |
| Operation | Global |
| Routing | Global |

### Referans implementasyonlar
`PersonnelService.SaveAsync`, `LogisticsConfigurationService.CreateMachineAsync/UpdateMachineAsync`, `AdminManagementService.CreateDepartmentAsync/UpdateDepartmentAsync` — hepsi aynı pattern'ı kullanır (name validation + name uniqueness + code auto-derive).

## CalibraSmartBoard (C-Grid) kuralları

- **Tüm liste ekranları** SmartBoard / C-Grid standardında yapılır.
- **In-place refresh zorunludur:** Kart üzerinde yapılan her değişiklik (toggle, silme vb.) `refreshUrl` + `refreshBoard()` mekanizmasıyla sadece etkilenen entity'yi günceller — `window.location.reload()` kullanılmaz.
  - Board config'e `refreshUrl = "/Controller/Action/BoardEntities"` eklenir.
  - İlgili endpoint `GET` olup tüm board config JSON'ını döndürür (entity listesiyle birlikte).
  - SmartCard `onRefresh(id)` → SmartBoard `refreshBoard(highlightId)` → `setEntities()` + indigo highlight animasyonu (1.8 sn).
- Yeni kayıt oluşturma için `actions: [{ label, icon, variant:"primary", url }]` header action kullanılır.

### C-Grid ekran yapısı (Backend → Frontend tam akış)

**1. Controller (örn. `LogisticsController.cs`)**
```csharp
// Liste action
[HttpGet]
public async Task<IActionResult> Machines(CancellationToken ct)
{
    var config = await BuildMachinesBoardConfigAsync(ct);
    return View(new MachinesSmartBoardViewModel { BoardConfig = config });
}

// In-place refresh endpoint (GET, aynı config'i döner)
[HttpGet("/Logistics/MachinesBoardConfig")]
public async Task<IActionResult> MachinesBoardConfig(CancellationToken ct)
{
    var config = await BuildMachinesBoardConfigAsync(ct);
    return Json(config);
}

// Silme endpoint (POST, CSRF korumalı, JSON döner)
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> DeleteMachineJson(int id, CancellationToken ct)
{
    try { await _service.DeleteAsync(id, ct); return Json(new { ok = true }); }
    catch (Exception ex) { return Json(new { ok = false, error = ex.Message }); }
}
```

**2. Board config nesnesi**
```csharp
private async Task<object> BuildMachinesBoardConfigAsync(CancellationToken ct)
{
    var items = await _service.GetAllAsync(ct);
    // ... masterWidgets + batchWidgets ...
    return new {
        boardKey          = "logistics-machines",        // benzersiz string
        title             = "Makine Tanımlamaları",
        subtitle          = $"{entities.Count} makine",
        icon              = "Cog",                       // lucide-react ikon adı
        iconColor         = "indigo",                    // renk: indigo/emerald/amber/rose/slate/blue/violet
        refreshUrl        = "/Logistics/MachinesBoardConfig",
        searchPlaceholder = "Hızlı ara…",
        emptyText         = "Henüz makine tanımlanmamış",
        actions = new object[] {
            new { id = "new", label = "Yeni Makine", icon = "Plus", variant = "primary", url = "/Logistics/MachineEdit" }
        },
        masterWidgets,   // admin widget panel için şablon
        entities,        // kart listesi (aşağıda)
    };
}
```

**3. Entity nesnesi**
```csharp
new {
    id          = m.Id,
    title       = m.MachineName,          // kartın büyük başlığı
    subtitle    = m.MachineCode,          // başlığın altında küçük metin
    description = "Lokasyon açıklaması", // opsiyonel — üçüncü satır
    imageUrl    = (string?)null,
    statusBadge = (object?)null,          // veya new { label="Aktif", color="emerald" }
    widgets     = BuildWidgets(m),        // veri chip'leri
    primaryAction = new {
        label      = "Düzenle",
        icon       = "Edit",
        color      = "amber",
        url        = $"/Logistics/MachineEdit?id={m.Id}",
        hideButton = true,                // true → kart tıklanınca navigate, buton gizli
    },
    secondaryAction = new {
        label     = "Sil",
        icon      = "Trash2",
        apiUrl    = $"/Logistics/DeleteMachineJson?id={m.Id}",
        apiMethod = "POST",
        confirm   = $"Silmek istediğinize emin misiniz? ({m.MachineName})",
    },
}
```

**4. Widget nesnesi** (kart üzerindeki veri chip'leri)
```csharp
// Sistem widget'ı (sabit)
new { id = "w_status", type = "data", dataType = "text",
      label = "Durum", value = "Aktif", detail = (string?)null,
      color = "emerald" }   // renk: emerald/indigo/amber/rose/slate/blue/violet

// Sayısal widget
new { id = "w_qty", type = "data", dataType = "numeric",
      label = "Planlanan", value = "100,00", detail = "adet", color = "indigo" }
```
Widget `color` değerleri: `emerald` (aktif/pozitif), `slate` (pasif/nötr), `indigo` (sayısal/referans), `amber` (uyarı), `rose` (hata/iptal), `blue` (bilgi), `violet` (özel durum).

**5. Razor view (`.cshtml`)**
```html
@section Scripts {
<script>
(function() {
    var BOARD_CONFIG = @Html.Raw(boardConfigJson);
    // ... loadCss(), loadScript() ...
    function doMount() {
        window.CalibraHub.mountSmartBoard(el, BOARD_CONFIG);
    }
    if (window.CalibraHub?.mountSmartBoard) doMount(); else loadScript(doMount);
})();
</script>
}
```
`mountSmartBoard` → `SmartBoard.jsx` → her entity için `SmartCard.jsx`.

**Referans ekranlar:** `Views/Logistics/Machines.cshtml`, `Views/Production/Operations.cshtml`, `Views/Production/Personnel.cshtml`

### C-Grid sayfa standardı (header + araçlar)

Tüm liste/kart ekranlarında **header her zaman aynı düzeni** taşır — kullanıcı bir C-Grid sayfasına geçince beklediği aynı sırayı bulur:

```
[İkon] Başlık                [🔍 Arama...]   [⚲ Filtre]  [⬇ Excel]  [⚙ Widget]   [+ Ana Eylem]
       alt başlık (X kayıt)
```

**Standart bileşenler (sırayla):**
1. **Sol kimlik:** 36×36 renk gradientli ikon kutusu + başlık + alt başlık (kayıt sayısı)
2. **Arama:** Kod/ad gibi temel alanlarda anlık (client-side) ya da debounced (server-side) filtre
3. **Filtre butonu:** `Filter` ikonu — `SmartBoardFilterPanel` modalını açar. Aktif filtre varsa indigo dot + sayı badge gösterilir
4. **Excel export:** `Download` ikonu — POST `/api/export/smartboard-excel`, kolonlar: Kod + Ad + sistem widget'ları + master widget'ları
5. **Widget ayarları:** `Settings2` ikonu — `SmartBoardConfigPanel` modalını açar (visibleIds + order, localStorage scope = boardKey)
6. **Ana eylem:** Primary button (`Yeni X`, `+` ikonu)

**Genişletilmiş düzen (tree/master-detail ekranlar):** Detay paneli (operasyon listesi, alt kalemler vb.) açıldığında detay başlığında ayrı bir küçük (xs) `Settings2` butonu — alt seviye widget'lar için ayrı `boardKey` ve ayrı master widget seti.

**Kullanılan ortak komponentler:**
- `SmartBoardConfigPanel` — visibleIds + order yönetimi (drag/X/+, localStorage)
- `SmartBoardFilterPanel` + `entityMatchesFilters` — master widget üzerinden filtreleme (`{ id, label, dataType }` minimum kontratı)
- `widgetConfigService.loadWidgetConfig(boardKey)` — kullanıcı tercihleri okuma
- `/api/export/smartboard-excel` — payload `{ fileName, sheetName, headers:[{id,label}], rows:[{...}] }`

**Tek `boardKey` kuralı:** Her board için tek bir benzersiz string (`production-routings-tree`, `logistics-machines` vb.). Filter ve widget config localStorage'da bu key altında izole edilir. Master-detail ekranda alt seviye için ayrı bir key (`*-ops`, `*-lines`).

**Backend kontratı (controller config):**
```csharp
return new {
    boardKey, title, subtitle, icon, iconColor,
    refreshUrl,                                  // in-place refresh endpoint (GET, JSON)
    searchPlaceholder, emptyText,
    actions = new[] { new { id, label, icon, variant, url } },
    masterWidgets,                               // master widget şablonu (admin tanımlı)
    entities,                                    // kart listesi (her birinde widgets[] dahil)
};
```
Tree/master-detail için ek alanlar: `routingMasterWidgets`, `opMasterWidgets`, `routingFormCode`, `opFormCode`.

**Referans (custom kart, full standart):** `ClientApp/src/components/RoutingTree/RoutingTree.jsx` — SmartBoard değil ama header standardı + filter/export/widget paneli + iki seviyeli widget yönetimi içerir. Custom liste ekranı yazarken bu örneği kopyala.

## React / Frontend — API'den Enum Yükleme Kuralı

Backend, `Program.cs`'de `JsonStringEnumConverter` kullanır. Bu nedenle API'den gelen tüm C# enum değerleri **sayı değil string** olarak gelir:

```
SourceType: "Lookup"     // integer 3 değil
TriggerType: "OnSave"   // integer 2 değil
ErrorBehavior: "Stop"   // integer 0 değil
```

React bileşenlerinde enum değerleri integer olarak karşılaştırılır (`m.sourceType === 3`, `m.triggerType === 2`). API'den yüklenen state bu haliyle kullanılırsa karşılaştırmalar **her zaman false** döner — UI yanlış seçili gösterir veya hiç seçili olmaz.

### Zorunlu pattern: normalize fonksiyonu

API'den gelen enum içeren her alan için **yükleme sırasında** integer'a çevrilmelidir:

```js
// 1) Mapping objesi tanımla (string → integer)
const SOURCE_TYPE_NUM = { FormField: 0, Constant: 1, Formula: 2, Lookup: 3, Function: 4 }

// 2) Normalize fonksiyonu — hem string hem integer input'u güvenle işler
function normalizeSourceType(t) {
  if (typeof t === 'number') return t
  if (typeof t === 'string' && t in SOURCE_TYPE_NUM) return SOURCE_TYPE_NUM[t]
  return 0  // bilinmeyen → varsayılan
}

// 3) Load akışında kullan
mappings: (it.mappings || []).map(m => ({
  sourceType: normalizeSourceType(m.sourceType),  // ← normalize
  ...
}))
```

**Kaydetme tarafında sorun yok** — backend `allowIntegerValues: true` ile hem integer hem string kabul eder. Sadece load/render tarafı normalize edilmeli.

### Mevcut örnekler (referans)
- `IntegrationWizard.jsx` → `normalizeTriggerType` + `normalizeSourceType`
- Yeni wizard/form bileşeni yazarken aynı pattern'i uygula.

---

## Standart rehber kuralı

Tüm standart rehberler (cbv_Guide_* view'larından beslenen) için **her ekranda aynı davranış**:

- **ValueColumn = `Code`** — rehberden satır seçilince input'a yazılan değer.
- **DisplayColumn = `Name`** — input yanında görüntülenen etiket (read-only label, fillTargets target'ı, vs.).

Standart rehber view'ları zorunlu olarak `Id`, `Code`, `Name` kolonlarını içerir. Bu nedenle yeni bir rehber için ayrıca konfigürasyon yapılmaz — discovery (`SqlGuideRepository.DiscoverAndRegisterGuidesAsync`) ve normalize pass (`NormalizeStandardColumnsAsync`) her başlatmada bu kuralı GuideMas'a uygular.

### İhlaller
- Bir view'da `Code`/`Name` kolonları olmayabilir → fallback olarak ORDINAL_POSITION 1./2. kolon kullanılır. Yeni view tasarlanırken **bu duruma izin verilmemeli** — standart kolon adlarını koruyun.
- Frontend'de admin "Alan Ayarları" → `formatJson.valueColumn` / `displayColumn` ile lokal override yapılabilir; ancak GuideMas seviyesindeki standardı bozmayın. Override sadece özel durumlar içindir.
- "Özel alan rehberi" (admin UI'dan serbest tanımlanan) bu kural dışındadır; kendi value/display ayarlarına sahiptir.
