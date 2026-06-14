# DB Naming Convention

SQL Server tabloları, kolonları, kısıtları ve index isimlendirmesi için standart kurallar. **Yeni eklenen her tablo ve kolon bu kurallara uyar.** Mevcut/legacy snake_case tablolar dokunulmaz (riske girilmez); ama o tablolara yeni kolon eklenirse mevcut stiline uyulur.

---

## Tablo isimleri

- **PascalCase, singular**: `Document`, `DocumentLine`, `Integration` (çoğul kullanılmaz: `Documents` ❌).
  - İstisna: doğal çoğul olanlar (`Items`, `Currencies`, `MaterialGroups`).
- **Prefix yok**: `TBL_`, `tbl_`, `CL_`, `APP_` gibi prefix kullanılmaz.
- **İlişki tabloları**: ana entity adı + ek (`DocumentLine`, `ItemFeatureMappings`, `IntegrationMapping`).
- **Snake_case legacy tablolardan yeni türetilen tablolar PascalCase olur**: `document_types` legacy ise yeni eklenecek tablo `DocumentTypeAttachment` (snake_case türevi `document_type_attachment` değil).

---

## Kolon isimleri

- **PascalCase**: `DocumentNumber`, `CompanyId`, `IsActive`, `TaxRate`.
- **PK her zaman `Id`**: `[Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY` — tablo prefix'i yok. `DocumentId` PK olamaz, sadece FK olur.
- **FK her zaman `{Entity}Id`**: `CompanyId`, `ItemId`, `ParentDocumentId`. **Asla string FK yok** (`MaterialCode` FK olamaz; bunun yerine `ItemId`).
- **Boolean kolon**: `Is{Quality}` veya `Has{Quality}` — `IsActive`, `IsMachinePark`, `HasChildren`.
- **Display alanları (klasik üçlü)**: `Code` (kısa, unique), `Name` (kullanıcıya gösterim), `Description` (opsiyonel uzun).
- **Eski snake_case tablolarda yeni kolon eklerken o tablonun stiline uy** — `users` tablosuna eklenen yeni alan `phone_number` olur, `PhoneNumber` değil. Sebep: SELECT/INSERT'leri tutarlı tut, audit ve raporlamada karışıklık yaratma.

### Audit dörtlüsü (her yeni tabloda olmalı)

```sql
Created    DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
Updated    DATETIME2 NULL                                -- UPDATE'te set edilir
CreatedBy  NVARCHAR(120) NULL
UpdatedBy  NVARCHAR(120) NULL
```

---

## Index ve constraint isimlendirme

| Tür | Pattern | Örnek |
|-----|---------|-------|
| Primary key | `PK_{Table}` | `PK_Document` |
| Foreign key | `FK_{Table}_{ReferencedTable}` | `FK_IntegrationMapping_Integration` |
| Normal index | `IX_{Table}_{Columns}` | `IX_IntegrationRun_Integration_Started` |
| Unique index | `UX_{Table}_{Columns}` | `UX_PriceList_Unique_Active` |
| Default constraint | `DF_{Table}_{Column}` | `DF_Document_IsActive` |

**Filtered unique (soft-delete safe)**: `UNIQUE … WHERE [IsActive] = 1` — soft-delete edilmiş kayıtlar yeniden aynı kodu kullanabilsin.

---

## Standart kolon seti (her yeni tablo iskeleti)

```sql
CREATE TABLE dbo.<TableName> (
    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_<TableName> PRIMARY KEY,
    -- ... domain-specific kolonlar (PascalCase)
    IsActive    BIT          NOT NULL CONSTRAINT DF_<TableName>_IsActive DEFAULT 1,
    CreatedBy   NVARCHAR(120) NULL,
    Created     DATETIME2     NOT NULL CONSTRAINT DF_<TableName>_Created DEFAULT SYSUTCDATETIME(),
    UpdatedBy   NVARCHAR(120) NULL,
    Updated     DATETIME2     NULL
);
```

---

## Tip standartları

| Tip | Kullanım |
|-----|----------|
| `INT IDENTITY` | PK |
| `INT` | FK |
| `BIGINT` | Sadece audit/log tabloları için (yüksek satır sayısı) |
| `NVARCHAR(50)` | Kod alanları (`Code`, `MaterialCode`) |
| `NVARCHAR(200)` | Ad alanları (`Name`, `FullName`) |
| `NVARCHAR(1000)` | Kısa açıklama |
| `NVARCHAR(MAX)` | Serbest metin / JSON (SQL Server'da native JSON tipi yok) |
| `DATETIME2` | Tüm tarih kolonları (UTC) |
| `DECIMAL(18,4)` | Para / miktar |
| `DECIMAL(5,2)` | Oran / yüzde |

---

## Multi-tenant kolon (per-company DB mimarisi varsa)

- Per-company DB kullanılıyorsa → **`CompanyId` kolonu YOK** (tablo zaten ait olduğu DB'de). Master DB tabloları (`dbo.Company` vb.) hariç.
- Tek DB + multi-tenant kullanılıyorsa → her tabloya `CompanyId INT NOT NULL` + her sorguda `WHERE CompanyId = @CurrentCompanyId` zorunlu.

---

## ID tabanlı eşleştirme kuralı

**Tüm karşılaştırma / dedup / match / FK ilişkileri ID üzerinden yapılır.** String alanları (kod, ad, açıklama) **sadece kullanıcıya gösterim** içindir; runtime karşılaştırmada kullanılmaz.

### Uygulama

- **FK kolonları**: Tüm tablolarda referans verilen kolon **`INT` PK** olur. String code/name FK olarak kullanılmaz (örn. `MaterialCode` yerine `ItemId`).
- **DTO'lar**: Karşılaştırma gerektiren her DTO ilgili `*Id` alanını taşır. Display için `Code`/`Name` ek olarak yer alabilir, ama karar logiclerinde kullanılmaz.
- **Service match/dedup**: `OrderBy(id) → array eşitliği` desenle. **String NormKey/lowercase trick'ini kullanma** — Türkçe karakter, whitespace, case farkları her zaman bug üretir.
- **API response**: `value`/`name`/`code`'a ek olarak `valueId`/`id` döndür. Frontend client-side kontrol veya cache key'leri için ID kullanır.
- **Yeni tablo tasarımı**: PK her zaman `INT IDENTITY`. Doğal anahtar (kod) UNIQUE INDEX ile tutulur, ama referanslar PK'ya verilir.

### İstisna

Standart kod alanları (rehber/lookup view'ları için `ValueColumn = Code`) kullanıcıya gösterim için kalır. Ama o kod alanının **başka tabloya FK olarak gitmesi yanlış** — yeni tabloda referans `int FK_Id` olur.

---

## Kullanıcı kod alanı yok kuralı

**Kullanıcı kod girmez. Tanımlama ekranlarında "Kod" inputu olmaz.** Personel, Departman, Makine, Operasyon vb. ana tanım kayıtlarında uniqueness **isim üzerinden** sağlanır.

### Sebep

- ID tabanlı eşleştirme kuralı zaten kodu runtime'da gereksiz kılıyor (FK'ler INT, code sadece display)
- Kod alanı kullanıcıya yük bindiriyor — "ne yazayım, kuralı ne" sorusu
- Ad benzersizliği daha doğal: aynı isimli iki personel zaten karışıklık yaratır

### Uygulama

- **DB kolonu kalır** (backward-compat: mevcut veri bozulmaz, eski DTO/SQL sorgular çalışır)
- **UI'dan tamamen kaldırılır** (Edit form'da Kod inputu yok, list view'da Kod sütunu yok)
- **Service tarafı**: `Code` alanı backend'de auto-türetilir
  - Yeni kayıt: `code = name` (truncated to DB max length, örn. 20-50 char) veya `MAC-{6-hex}` (Machine için), `AUTO_{guid}` (fallback)
  - Update: mevcut `code`'u koru (değişmesin, eski referanslar bozulmasın)
- **Uniqueness check** ad üzerinden: `string.Equals(x.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase)` (kendisi hariç)
- **Hata mesajı**: `"Aynı isimde başka bir X zaten tanımlı: '{name}'"`

### Entegrasyonda kod eşleme ihtiyacı varsa

Üçüncü parti sistem (ERP, makine PLC, e-ticaret) ile entegre olurken o sistemin kendi kodunu kayda bağlamak için **widget alanı** (admin UI'dan tanımlanabilir dinamik alan) kullanılır.

---

## Legacy istisnalar — refactor edilmiyor

Eski snake_case tablolar (`user_settings`, `notes*`, `card_groups*`, `whatsapp_*`, `currencies` vb.) **kasıtlı olarak dokunulmuyor**: çalışıyor, kapsamlı view/proc/repository bağımlılıkları var, riske girilmez. Bu tablolara yeni kolon eklendiğinde **o tablonun stiline uyulur** (snake_case → snake_case).

Refactor şart olduğunda **full-rename + view backward-compat + migration** yaklaşımı izlenir:
1. Yeni isimle tablo yarat
2. Eski isim için synonym/view oluştur (geçiş süreci)
3. Tüm referansları (repo, view, proc, SQL string) yeni isme çevir
4. Synonym/view'u kaldır
