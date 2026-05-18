# CalibraHub — Entegrasyon Wizard Tasarım Dokümanı

> Wizard tabanlı, form ↔ REST endpoint eşleme entegrasyon modülü.
> Son kullanıcı **kod yazmadan**, 5 adımlı wizard ile bir CalibraHub formundan
> (örn. Müşteri Siparişi) çıkan verinin bir REST endpoint'ine
> (örn. Netsis Sipariş POST) nasıl gönderileceğini tanımlar.
>
> Bu doküman `D:\JetBrainsRider\Projeler\LogoRest\CalibaHub-EntegrasyonWizard-Tasarim.md`
> taslağının **CalibraHub mevcut altyapısına uyarlanmış** halidir. Tablo
> isimlendirme PascalCase, mevcut `Forms`/`WidgetMas`/`GuideMas`/`ScheduledTasks`/
> `integration_api_profiles` altyapıları **yeniden kullanılır**.

**Doküman versiyonu**: 2.0 (CalibraHub adaptation)
**Tarih**: 2026-05-12

---

## 1. Amaç

Sistemdeki bir formun (kaynak) bir REST endpoint'ine (hedef) **kullanıcı tarafından tanımlı** eşleme ile aktarılması:

- Form alanlarını dropdown ile eşleştir
- Sabit değer / formül / lookup ekle
- Trigger seç (manuel buton şimdilik; periyodik V1 sonrası; OnSave/Event V2)
- Test et, kaydet

Hedef: KOBİ/SMB kullanıcısının teknik destek olmadan Netsis/Logo/custom REST entegrasyonu kurabilmesi.

---

## 2. Onaylanan Tasarım Kararları

| # | Konu | Karar |
|---|---|---|
| 1 | Tetikleme yolları | Manuel buton + Periyodik (V1); OnSave + Event (V2, schema hazır) |
| 2 | Manuel buton görünürlüğü | `recordId > 0` olan form sayfalarında otomatik render |
| 3 | Re-send davranışı | Endpoint sorumluluğu; her tıklama yeni HTTP, idempotency tracking yok |
| 4 | Durum görünümü | Log ekranı üstünden; buton dumb (V2'de durum badge eklenebilir) |
| 5 | Toplu işlem | MVP'de yok; V2 |
| 6 | Form metadata kaynağı | Mevcut `dbo.Forms` + `dbo.WidgetMas` + `v_Flat_{FormCode}` |
| 7 | Lookup kaynağı | Mevcut `cbv_Guide_*` + `dbo.GuideMas` (yeni lookup tablosu yok) |
| 8 | Scheduler | Mevcut `ScheduledTasks` (Hangfire eklenmiyor) |
| 9 | Auth profile | Mevcut `integration_api_profiles` tablosu (yeniden yazılmıyor) |
| 10 | Frontend | MVC + React (mevcut hibrit pattern; SmartBoard standardı liste, Compose-style wizard) |
| 11 | Expression engine | NCalc (V1); Roslyn Scripting opsiyonu V2+ |
| 12 | Permission | `UserAuthorizationCatalog` + yeni 3 permission |
| 13 | Multi-tenant | Per-company DB mimarisi zaten çözüyor; ek iş yok |
| 14 | Versiyonlama | `Integration` tablosuna `VersionNo` + `IntegrationHistory` snapshot tablosu (V1.1) |

---

## 3. Wizard Akışı (5 Adım)

```
[Step 1]          [Step 2]            [Step 3]            [Step 4]      [Step 5]
Kaynak Form  →   Hedef Endpoint  →   Alan Eşleme    →   Test         →  Trigger
seç              seç                  yap                yap              + Kaydet
   │                │                    │                  │               │
   ▼                ▼                    ▼                  ▼               ▼
Forms tablosu    IntegrationEndpoint   Mapping satırı    JSON preview   IntegrationTrigger
+ WidgetMas      + ApiProfile         (form/sabit/      + dry-run      satırları
+ v_Flat_*'tan   tablosundan          formül/lookup)    HTTP test      + Active toggle
örnek veri
```

### Step 1 — Kaynak Form Seçimi

**UI**:
- Module → SubModule → Form dropdown'u (`dbo.Forms` hiyerarşik gösterim)
- Form seçilince sağda alanlar tree view (WidgetMas'tan: label + dataType + isRequired)

**Backend**:
```csharp
public interface IFormMetadataService {
    Task<IReadOnlyList<FormFieldDto>> GetFormFieldsAsync(string formCode, CancellationToken ct);
    Task<SampleRecordDto?> GetSampleRecordAsync(string formCode, int? recordId, CancellationToken ct);
}

// Implementation reads from dbo.WidgetMas + v_Flat_{FormCode}
```

**Çıktı**: `state.SourceFormCode`, `state.SourceFields[]`

### Step 2 — Hedef REST Endpoint Seçimi

**UI**:
- ApiProfile dropdown (`integration_api_profiles` tablosundan; örn. "Netsis Üretim", "Logo Demo")
- Endpoint dropdown (seçilen profile'a ait `IntegrationEndpoint` satırları)
- Seçilince sağda body schema tree view

**Backend**:
```csharp
public interface IIntegrationEndpointService {
    Task<IReadOnlyList<EndpointDto>> GetEndpointsByProfileAsync(int profileId, CancellationToken ct);
    Task<JsonSchema> GetBodySchemaAsync(int endpointId, CancellationToken ct);
}
```

**Schema kaynağı**:
- Endpoint kayıtlarında elle yapıştırılmış JSON örnek (MVP)
- Swagger import (V1.5)

**Çıktı**: `state.TargetEndpointId`, `state.TargetFields[]` (JSON path + dataType + required)

### Step 3 — Alan Eşleme (Wizard'ın Kalbi)

Her hedef alan için 4 kaynak tipi:

| Kaynak Tipi | Açıklama | Backend handling |
|---|---|---|
| **FormField** | Kaynak formdan bir alan | `record.GetField(name)` |
| **Sabit** | Literal değer | `rule.SourceValue` |
| **Formül** | NCalc expression | `_ncalcEval.Evaluate(expr, record)` |
| **Lookup** | Standart rehber (cbv_Guide_*) | `_guideService.ResolveAsync(guideCode, lookupField)` |

**Ek özellikler**:
- **Conditional**: `if Sipariş.Tip == 'A' then "ftAFat" else "ftSFat"` → NCalc expression
- **Format dönüşümü**: tarih → ISO 8601, sayı → 2 ondalık, string → upper
- **Default değer**: kaynak null/boş ise X kullan
- **Nested mapping**: `Kalemler[]` array → her satır için iç mapping bloğu

**UI**:
- 2-sütun layout: solda Source Tree, sağda Target Tree
- Her hedef alanın yanında `[▼ Source]` popup'ı
- "Otomatik Eşle" butonu (isim benzerlik bazlı, V1)

```
┌──────────────────────────────────────────────────────────────┐
│  Müşteri Sipariş → Netsis Sipariş POST  (3/5)                │
├──────────────────────────────────────────────────────────────┤
│  KAYNAK FORM                  HEDEF JSON BODY                │
│  ▼ Müşteri Sipariş            ▼ Netsis ItemSlips POST        │
│                                                              │
│  ├─ MusteriKodu  (str)        ├─ FatUst                      │
│  ├─ Tarih        (date)       │  ├─ FATIRS_NO  [⚙ Otomatik]  │
│  ├─ ToplamTutar  (decimal)    │  ├─ CariKod    [⚙ MusteriKod]│
│  └─ Kalemler[]                │  ├─ Tarih      [⚙ Tarih ISO] │
│     ├─ StokKodu               │  └─ TIPI       [⚙ "ftYIc"]   │
│     ├─ Adet                   └─ Kalemler[]                  │
│     └─ BirimFiyat                ├─ StokKod   [⚙ StokKodu]   │
│                                  ├─ Miktar    [⚙ Adet]       │
│                                  └─ Tutar     [⚙ Adet*BF]    │
│                                                              │
│  [Otomatik Eşle]  [Önizleme]  [< Geri]  [İleri >]          │
└──────────────────────────────────────────────────────────────┘
```

`[⚙]` popup'ı 4 sekme: FormField / Sabit / Formül / Lookup.

### Step 4 — Test & Validation

**UI**:
- "Örnek kayıt seç" dropdown (mevcut form kayıtlarından)
- Önizleme paneli: mapping uygulanmış JSON output
- "Test İste" butonu (dry-run veya gerçek test endpoint)
- Eksik zorunlu alanlar kırmızı; HTTP yanıt gösterilir

**Backend**:
```csharp
var sample   = await _formMeta.GetSampleRecordAsync(formCode, recordId, ct);
var output   = _mappingEngine.Build(integration, sample);
var validate = _validator.CheckRequiredFields(output, targetSchema);
if (validate.HasErrors) return ValidationErrors(validate);
var resp     = await _httpExecutor.SendTestAsync(endpoint, output, ct);
return new TestResultDto(output, resp);
```

### Step 5 — Trigger Tanımı + Kaydet

**UI** (multi-checkbox):

```
TETİKLEME YÖNTEMLERİ (birden çok seçilebilir):

☑ Manuel buton — form ekranında göster (yalnız recordId>0 iken)
   Buton etiketi: [ ERP'ye Aktar     ]
   Buton rengi:   [ Mavi ▼ ]

☐ Periyodik — cron ile çalıştır
   [ 0 */15 * * * * ]      Açıklama: her 15 dakikada bir
   Hedef: yeni veya değişmiş kayıtları işle (filter)

☒ Otomatik — kayıt edilince arka planda (V2'de gelecek)
   ☐ Sadece yeni kayıt
   ☐ Yeni + güncelleme

☒ Event — özel olay (V2'de gelecek)
   [ Event kodu seç ▼ ]
```

V2 seçenekleri **gri renkte, tooltip "Bu özellik V2'de aktif olacak"**. Wizard schema-aware, DB'ye yazılır ama runner gün 0'da OnSave/Event'i fire etmez (dispatcher henüz yok).

**Hata davranışı**: Skip / Retry N / Manuel inceleme listesine ekle.

**Backend**: tüm state `Integration` + `IntegrationMapping` + `IntegrationTrigger` tablolarına INSERT.

---

## 4. Veri Modeli

> **Naming**: PascalCase, CALIBA_ prefix yok. Mevcut CalibraHub konvansiyonu.

```sql
-- ─── Integration definition ────────────────────────────────────────
CREATE TABLE dbo.Integration (
    Id                   INT IDENTITY PRIMARY KEY,
    Name                 NVARCHAR(200)  NOT NULL,
    Description          NVARCHAR(1000) NULL,
    SourceFormCode       NVARCHAR(50)   NOT NULL,    -- Forms.FormCode ile eşleşir
    TargetEndpointId     INT            NOT NULL,    -- IntegrationEndpoint.Id
    ErrorBehavior        NVARCHAR(20)   NOT NULL DEFAULT 'Skip',  -- Skip | Retry | Manual
    RetryCount           INT            NOT NULL DEFAULT 0,
    IsActive             BIT            NOT NULL DEFAULT 1,
    VersionNo            INT            NOT NULL DEFAULT 1,       -- V1.1 history desteği
    CreatedBy            NVARCHAR(120)  NULL,
    Created              DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedBy            NVARCHAR(120)  NULL,
    Updated              DATETIME2      NULL,
    CONSTRAINT FK_Integration_Endpoint FOREIGN KEY (TargetEndpointId)
        REFERENCES dbo.IntegrationEndpoint(Id)
);
CREATE INDEX IX_Integration_SourceForm ON dbo.Integration(SourceFormCode, IsActive);

-- ─── Field mapping rules ───────────────────────────────────────────
CREATE TABLE dbo.IntegrationMapping (
    Id                   INT IDENTITY PRIMARY KEY,
    IntegrationId        INT            NOT NULL,
    TargetPath           NVARCHAR(500)  NOT NULL,    -- "FatUst.CariKod" | "Kalemler[].StokKod"
    TargetDataType       NVARCHAR(50)   NULL,        -- string | decimal | datetime | bool | int
    SourceType           NVARCHAR(20)   NOT NULL,    -- FormField | Constant | Formula | Lookup
    SourceValue          NVARCHAR(MAX)  NULL,        -- field name | literal | NCalc expr | guide code
    LookupSourceField    NVARCHAR(200)  NULL,        -- Lookup için: hangi form alanı lookup'a verilecek
    DefaultValue         NVARCHAR(500)  NULL,
    FormatPattern        NVARCHAR(100)  NULL,        -- "yyyy-MM-dd" | "N2" | "upper"
    IsRequired           BIT            NOT NULL DEFAULT 0,
    SortOrder            INT            NOT NULL DEFAULT 0,
    GroupKey             NVARCHAR(100)  NULL,        -- "FatUst" | "Kalemler" — nested için
    CONSTRAINT FK_IntegrationMapping_Integration FOREIGN KEY (IntegrationId)
        REFERENCES dbo.Integration(Id) ON DELETE CASCADE
);
CREATE INDEX IX_IntegrationMapping_Integration ON dbo.IntegrationMapping(IntegrationId, SortOrder);

-- ─── Trigger config (multi-trigger per integration) ────────────────
CREATE TABLE dbo.IntegrationTrigger (
    Id                   INT IDENTITY PRIMARY KEY,
    IntegrationId        INT            NOT NULL,
    TriggerType          NVARCHAR(20)   NOT NULL,    -- Manual | Cron | OnSave | Event
    Config               NVARCHAR(MAX)  NULL,        -- JSON: cron expr, button label, event code
    IsActive             BIT            NOT NULL DEFAULT 1,
    Created              DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_IntegrationTrigger_Integration FOREIGN KEY (IntegrationId)
        REFERENCES dbo.Integration(Id) ON DELETE CASCADE
);
CREATE INDEX IX_IntegrationTrigger_Type ON dbo.IntegrationTrigger(TriggerType, IsActive);

-- ─── Execution audit log ───────────────────────────────────────────
CREATE TABLE dbo.IntegrationRun (
    Id                   BIGINT IDENTITY PRIMARY KEY,
    IntegrationId        INT            NOT NULL,
    TriggerType          NVARCHAR(20)   NOT NULL,    -- hangi tetikleyici çalıştırdı
    SourceRecordId       NVARCHAR(100)  NULL,        -- form kaydı PK (NVARCHAR çünkü farklı tip olabilir)
    StartedAt            DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    FinishedAt           DATETIME2      NULL,
    DurationMs           INT            NULL,
    Status               NVARCHAR(20)   NOT NULL,    -- Success | Failed | Skipped | Retrying
    HttpStatusCode       INT            NULL,
    RequestBody          NVARCHAR(MAX)  NULL,
    ResponseBody         NVARCHAR(MAX)  NULL,
    ErrorMessage         NVARCHAR(MAX)  NULL,
    RetryAttempt         INT            NOT NULL DEFAULT 0,
    TriggeredBy          NVARCHAR(120)  NULL,        -- manuel için kullanıcı; cron/event için 'system'
    CONSTRAINT FK_IntegrationRun_Integration FOREIGN KEY (IntegrationId)
        REFERENCES dbo.Integration(Id)
);
CREATE INDEX IX_IntegrationRun_Integration_Started ON dbo.IntegrationRun(IntegrationId, StartedAt DESC);
CREATE INDEX IX_IntegrationRun_SourceRecord ON dbo.IntegrationRun(SourceRecordId, StartedAt DESC);

-- ─── REST endpoint catalog ─────────────────────────────────────────
CREATE TABLE dbo.IntegrationEndpoint (
    Id                   INT IDENTITY PRIMARY KEY,
    ApiProfileId         INT            NOT NULL,    -- integration_api_profiles.id
    Name                 NVARCHAR(200)  NOT NULL,    -- "Netsis Sipariş POST"
    HttpMethod           NVARCHAR(10)   NOT NULL,    -- GET | POST | PUT | DELETE | PATCH
    UrlTemplate          NVARCHAR(500)  NOT NULL,    -- "/api/v2/ItemSlips" — profile.BaseUrl'e relative
    BodySchema           NVARCHAR(MAX)  NULL,        -- örnek JSON veya JSON Schema
    Description          NVARCHAR(1000) NULL,
    IsActive             BIT            NOT NULL DEFAULT 1,
    Created              DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_IntegrationEndpoint_Profile FOREIGN KEY (ApiProfileId)
        REFERENCES dbo.integration_api_profiles(id)
);
CREATE INDEX IX_IntegrationEndpoint_Profile ON dbo.IntegrationEndpoint(ApiProfileId, IsActive);

-- ─── (V1.1) Integration history snapshot — versiyonlama ────────────
CREATE TABLE dbo.IntegrationHistory (
    Id                   BIGINT IDENTITY PRIMARY KEY,
    IntegrationId        INT            NOT NULL,
    VersionNo            INT            NOT NULL,
    SnapshotJson         NVARCHAR(MAX)  NOT NULL,    -- {integration, mappings, triggers} JSON
    SnapshotAt           DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    SnapshotBy           NVARCHAR(120)  NULL
);
```

### Yeni tablolar değil — yeniden kullanılan mevcutlar

| Tablo | Kullanım |
|---|---|
| `dbo.Forms` | Step 1 form dropdown'u; FormCode → BaseTable / WidgetMas hookup |
| `dbo.WidgetMas` | Form alanları listesi (label, dataType, isRequired) |
| `v_Flat_{FormCode}` | Step 4 örnek kayıt çekme (mevcut flat view'lar) |
| `dbo.GuideMas` + `cbv_Guide_*` | Step 3 Lookup tipi (yeni CALIBA_LOOKUP yok) |
| `dbo.integration_api_profiles` | Step 2 auth profile dropdown'u |
| `dbo.ScheduledTasks` | Periyodik trigger cron entry (Hangfire değil) |
| `dbo.IntegrationEvents` | V2'de OnSave event publish/subscribe |

---

## 5. Mapping Engine

```csharp
public interface IMappingEngine {
    Task<JObject> BuildAsync(Integration integration, FormRecord source, CancellationToken ct);
}

public sealed class MappingEngine : IMappingEngine {
    private readonly IExpressionEvaluator _expr;     // NCalc wrapper
    private readonly IGuideService _guideService;     // mevcut CalibraHub

    public async Task<JObject> BuildAsync(Integration integration, FormRecord source, CancellationToken ct) {
        var output = new JObject();

        foreach (var rule in integration.Mappings.OrderBy(m => m.SortOrder)) {
            object? value = rule.SourceType switch {
                "FormField" => source.GetField(rule.SourceValue),
                "Constant"  => rule.SourceValue,
                "Formula"   => _expr.Evaluate(rule.SourceValue!, source),
                "Lookup"    => await _guideService.ResolveAsync(
                                  rule.SourceValue!,  // guide code (cbv_Guide_xxx)
                                  source.GetField(rule.LookupSourceField!),
                                  ct),
                _           => null
            };

            value ??= rule.DefaultValue;
            value = ApplyFormat(value, rule.TargetDataType, rule.FormatPattern);

            SetJsonPath(output, rule.TargetPath, value);
        }

        return output;
    }

    // SetJsonPath: "FatUst.CariKod" → output["FatUst"]["CariKod"] = value
    // "Kalemler[].StokKod" → output["Kalemler"] her satır için iç mapping
    // (Detay: nested array handling — alt sınıf MappingRowGroupResolver)
}
```

### Expression Engine (NCalc)

```csharp
var e = new NCalc.Expression("Adet * BirimFiyat * (1 - Iskonto / 100)");
e.Parameters["Adet"]       = record.GetField("Adet");
e.Parameters["BirimFiyat"] = record.GetField("BirimFiyat");
e.Parameters["Iskonto"]    = record.GetField("Iskonto");
var result = e.Evaluate();
```

**Güvenlik (Risk 12.2)**: NCalc EvaluateFunction event'i ile whitelist; `System.IO`, `Process`, vb. tipler tamamen unreachable çünkü NCalc sadece deyimleri parse eder, kod execute etmez. Yine de input sanitization yapılır.

---

## 6. HTTP Executor

```csharp
public interface IHttpExecutor {
    Task<HttpInvocationResult> SendAsync(
        IntegrationEndpoint endpoint, JObject body, CancellationToken ct);
}

public sealed class HttpExecutor : IHttpExecutor {
    private readonly IHttpClientFactory _http;
    private readonly IApiProfileService _profileService;   // mevcut

    public async Task<HttpInvocationResult> SendAsync(
        IntegrationEndpoint endpoint, JObject body, CancellationToken ct) {
        var profile = await _profileService.GetAsync(endpoint.ApiProfileId, ct);
        var url = profile.BaseUrl.TrimEnd('/') + endpoint.UrlTemplate;

        using var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(60);

        using var req = new HttpRequestMessage(new HttpMethod(endpoint.HttpMethod), url);
        if (endpoint.HttpMethod is "POST" or "PUT" or "PATCH")
            req.Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");

        // Profile'dan auth ekle (OAuth2 token refresh, ApiKey header, Basic, vb.)
        await _profileService.AttachAuthAsync(req, profile, ct);

        var sw = Stopwatch.StartNew();
        try {
            using var resp = await client.SendAsync(req, ct);
            var respBody = await resp.Content.ReadAsStringAsync(ct);
            sw.Stop();
            return new HttpInvocationResult {
                StatusCode = (int)resp.StatusCode,
                ResponseBody = respBody,
                Success = resp.IsSuccessStatusCode,
                DurationMs = (int)sw.ElapsedMilliseconds,
                RequestBody = body.ToString()
            };
        } catch (Exception ex) {
            sw.Stop();
            return new HttpInvocationResult {
                Success = false,
                ErrorMessage = ex.Message,
                DurationMs = (int)sw.ElapsedMilliseconds,
                RequestBody = body.ToString()
            };
        }
    }
}
```

---

## 7. Trigger Sistemi

### 7.1 Manual button (MVP — gün 1)

**Form sayfasına partial enjeksiyonu**:
```cshtml
@* SalesOrderEdit.cshtml *@
@if (Model?.Id > 0) {
    @await Html.PartialAsync("_IntegrationButtons", new IntegrationButtonsModel {
        FormCode = "SALES_ORDERS",
        RecordId = Model.Id
    })
}
```

**Partial**:
```cshtml
@model IntegrationButtonsModel
@inject IIntegrationService IntegrationService

@{
    var integrations = await IntegrationService.GetManualIntegrationsAsync(Model.FormCode);
}

@foreach (var integ in integrations) {
    <button type="button" class="btn btn-primary"
            data-integration-id="@integ.Id"
            data-record-id="@Model.RecordId"
            onclick="runIntegration(@integ.Id, @Model.RecordId)">
        @(integ.ButtonLabel ?? integ.Name)
    </button>
}
```

**JavaScript (mevcut CalibraHub.toast pattern)**:
```js
async function runIntegration(integrationId, recordId) {
    const r = await fetch(`/Integration/Run/${integrationId}?recordId=${recordId}`, {
        method: 'POST',
        headers: { 'X-Requested-With': 'CalibraHubManual' }
    });
    const d = await r.json();
    if (d.success) {
        window.CalibraHub.toast(`✓ Aktarıldı: ${d.message ?? ''}`, 'ok');
    } else {
        window.CalibraHub.toast(`✗ Hata: ${d.error ?? 'bilinmeyen'}`, 'err');
    }
}
```

### 7.2 Periodic (Cron — V1)

**ScheduledTasks ile integration kaydı bir-bire**:

```csharp
// Trigger kaydedilirken:
if (trigger.TriggerType == "Cron") {
    await _scheduledTasksService.UpsertAsync(new ScheduledTask {
        Code = $"INTEGRATION_{integration.Id}",
        Title = integration.Name,
        CronExpression = trigger.Config["cron"],
        HandlerType = "IntegrationCronHandler",
        Payload = JsonSerializer.Serialize(new { integrationId = integration.Id }),
        IsActive = true
    }, ct);
}

// IntegrationCronHandler:
public class IntegrationCronHandler : IScheduledTaskHandler {
    public async Task RunAsync(string payloadJson, CancellationToken ct) {
        var payload = JsonSerializer.Deserialize<Payload>(payloadJson);
        // Hangi kayıtların aktarılması gerek? Filter:
        //   - Son cron'dan beri değişmiş kayıtlar (Updated > lastRunAt)
        //   - Veya hiç IntegrationRun'ı olmayanlar
        var records = await _formData.GetUnsyncedRecordsAsync(integration.SourceFormCode, lastRunAt, ct);
        foreach (var rec in records) {
            await _runner.RunAsync(integration.Id, rec.Id, triggerType: "Cron", ct);
        }
    }
}
```

### 7.3 OnSave (V2 — schema hazır, dispatcher yok)

Wizard'da seçilebilir ama tooltip "V2'de aktif olacak". DB'ye `IntegrationTrigger` satırı yazılır, runner ignore eder.

V2'de eklenecek:
```csharp
public interface IIntegrationDispatcher {
    Task PublishAsync(string formCode, string eventType, int recordId, CancellationToken ct);
}

// Her Save controller'ında:
await _integrationDispatcher.PublishAsync("SALES_ORDERS", "OnSave", savedId, ct);

// Dispatcher implementation:
public async Task PublishAsync(string formCode, string eventType, int recordId, CancellationToken ct) {
    var triggers = await _db.IntegrationTrigger
        .Where(t => t.TriggerType == eventType
                 && t.IsActive
                 && t.Integration.SourceFormCode == formCode
                 && t.Integration.IsActive)
        .ToListAsync(ct);

    foreach (var t in triggers) {
        await _queue.EnqueueAsync(new IntegrationJob(t.IntegrationId, recordId, eventType), ct);
    }
}
```

### 7.4 Event (V2 — IntegrationEvents tablosu üstünden)

Mevcut `IntegrationEvents` mekanizması var; V2'de wizard ile bağlanır.

---

## 8. Form Metadata — Mevcut Altyapı Reuse

**Step 1'de form alanları çekmek**:
```csharp
public async Task<IReadOnlyList<FormFieldDto>> GetFormFieldsAsync(string formCode, CancellationToken ct) {
    using var conn = await _connFactory.OpenConnectionAsync(ct);
    var sql = """
        SELECT w.WidgetCode, w.Label, w.DataType, w.IsRequired
        FROM dbo.WidgetMas w
        INNER JOIN dbo.Forms f ON f.Id = w.FormId
        WHERE f.FormCode = @code
          AND w.IsActive = 1
          AND w.DataType NOT IN ('group', 'grid')
        ORDER BY w.SortOrder
    """;
    return (await conn.QueryAsync<FormFieldDto>(sql, new { code = formCode })).ToList();
}
```

**Step 4'te örnek kayıt çekmek** — `v_Flat_{FormCode}` view'ından:
```csharp
var viewName = $"v_Flat_{formCode}";
var sql = $"SELECT TOP 1 * FROM dbo.[{viewName}] WHERE id = @id";  // (identifier validated)
```

---

## 9. Lookup — Standart Rehber (cbv_Guide_*) Reuse

Wizard Step 3'te "Lookup" tipi seçildiğinde:
- Açılır rehber popup'ı: `dbo.GuideMas` tablosundan aktif rehberler listelenir
- Kullanıcı bir guide code seçer (örn. `COUNTRIES`)
- Ayrıca seçer: "kaynak alandan lookup'a hangi değer gönderilecek" (örn. `MusteriUlke`)

Mapping satırı:
```
SourceType        = 'Lookup'
SourceValue       = 'COUNTRIES'           -- guide code
LookupSourceField = 'MusteriUlke'         -- bu form alanı lookup'a verilir
```

Runtime'da:
```csharp
case "Lookup":
    var sourceVal = source.GetField(rule.LookupSourceField);
    value = await _guideService.ResolveAsync(rule.SourceValue, sourceVal, ct);
    // GuideService internal: SELECT DisplayColumn FROM cbv_Guide_COUNTRIES WHERE ValueColumn = @v
    break;
```

`GuideService.ResolveAsync` mevcut CalibraHub interface'inde yoksa eklenir (~10 satır).

---

## 10. Log / Run History UI

**SmartBoard standardında liste sayfası**: `/Integration/Runs`

```
ENTEGRASYON ÇALIŞTIRMA GEÇMİŞİ                    [🔍] [⚲] [⬇ Excel] [⚙ Widget]

[Filtre: Entegrasyon ▼] [Durum ▼] [Tarih: Son 7 gün ▼]

┌──────────────────────────────────────────────────────────────────────┐
│ ✓ Sipariş→Netsis     SIP-2026-0042   12.05.2026 14:23  201   125ms │
│ ✗ Sipariş→Netsis     SIP-2026-0042   12.05.2026 14:20  400   312ms │
│ ✓ Fatura→Logo        INV-2026-1289   12.05.2026 14:15  200    87ms │
│ ⏳ Cari→Netsis        CAR-2026-0008   12.05.2026 14:10  -     retry│
└──────────────────────────────────────────────────────────────────────┘
                                                            ← Sayfa: 1/12 →

[Tıklanınca detay modalı]:
  Request Body  (JSON, pretty-printed, copy butonu)
  Response Body (JSON, pretty-printed)
  Error Message + stack trace (varsa)
  Re-run butonu (aynı record, aynı integration, manuel tetikle)
```

Backend: `IntegrationRun` tablosundan SmartBoard pattern'ine uygun JSON config.

---

## 11. Permission Modeli

`UserPermission` enum'a yeni değerler:

```csharp
public enum UserPermission {
    // ... mevcut permissions
    ManageIntegrations,        // Wizard'a erişim, oluşturma, silme, edit
    ExecuteIntegrations,       // Manuel buton ile tetikleme
    ViewIntegrationLog,        // /Integration/Runs sayfası
    ConfigureApiProfiles       // integration_api_profiles tablosuna erişim (zaten varsa)
}
```

Wizard menüsü `Yöneticiler` rolüne; manuel butonlar `ExecuteIntegrations` izni olanlara.

---

## 12. Sprint Planı (3 sprint, ~5 hafta)

### Sprint 1 — Backend altyapı (2 hafta)

- [ ] DB tablolarını ekle (`Integration`, `IntegrationMapping`, `IntegrationTrigger`, `IntegrationRun`, `IntegrationEndpoint`) — `CalibraDatabaseInitializer.cs`
- [ ] Entity sınıfları (`Application/Domain/Entities/Integration/*.cs`)
- [ ] Repository'ler (Dapper-based, CalibraHub mevcut pattern)
- [ ] `IFormMetadataService` — Forms + WidgetMas + v_Flat_* okuyucu
- [ ] `IIntegrationEndpointService` — endpoint CRUD + body schema okuma
- [ ] `MappingEngine` + NCalc expression evaluator + JSON path setter
- [ ] `HttpExecutor` + ApiProfile auth bağlama
- [ ] `IntegrationRunner` — runMapping → executeHttp → writeRun log
- [ ] Unit testler (MappingEngine için en az 20 case)
- [ ] Hardcoded bir test endpoint ile E2E manual çalıştırma (henüz wizard yok)

### Sprint 2 — Wizard UI (2 hafta)

- [ ] `IntegrationsController` + 5 step action method
- [ ] `IntegrationWizard.jsx` React component (mevcut Compose pattern'inde)
- [ ] Step 1: Form picker (Module/SubModule/Form tree)
- [ ] Step 2: ApiProfile + Endpoint picker
- [ ] Step 3: 2-sütun mapping editor (drag/dropdown + 4-tab popup)
- [ ] Step 4: Sample record + JSON preview + Test button
- [ ] Step 5: Trigger checkbox (Manual + Cron aktif; OnSave + Event disabled tooltip)
- [ ] Validation (her step'te ileri gitmeden önce zorunlu alanlar)
- [ ] Liste sayfası (SmartBoard standardı): `/Integrations` — aktif/pasif toggle, sil, kopyala

### Sprint 3 — Trigger + Log + Polish (1 hafta)

- [ ] Manual button partial injection (`_IntegrationButtons.cshtml`)
- [ ] `/Integration/Run/{id}` endpoint
- [ ] ScheduledTasks integration (cron trigger)
- [ ] `IntegrationCronHandler` (unsynced records filter)
- [ ] `/Integration/Runs` log sayfası (SmartBoard standardı)
- [ ] Run detail modal (request/response body, re-run butonu)
- [ ] "Otomatik Eşle" (isim benzerlik)
- [ ] Permission entegrasyonu
- [ ] CLAUDE.md'ye Integration Wizard kullanım dokümanı

---

## 13. Verification

### Sprint 1 kabul
- [ ] `dotnet build` 0 hata
- [ ] `IntegrationDatabaseTests` — tüm tablolar create + FK + index test
- [ ] `MappingEngineTests` — 20+ case: FormField, Constant, Formula, Lookup, nested, format, default
- [ ] Manuel curl ile test endpoint'e mesaj atılıyor: `POST /Integration/Run/1?recordId=42` → 200 + IntegrationRun satırı

### Sprint 2 kabul
- [ ] Yeni entegrasyon wizard akışı uçtan uca çalışıyor (Sipariş → echo endpoint)
- [ ] Liste sayfası SmartBoard standardına uygun (filtre, arama, widget config)
- [ ] Permission kontrolü çalışıyor (ManageIntegrations olmayan kullanıcı wizard'a giremez)

### Sprint 3 kabul
- [ ] Sipariş edit ekranında "ERP'ye Aktar" butonu görünüyor (`recordId>0` iken)
- [ ] Buton tıklanınca toast: success/fail
- [ ] Log sayfasında o çalıştırma görünüyor
- [ ] Cron trigger 15dk aralıkla otomatik fire ediyor
- [ ] Detay modal request/response body gösteriyor

---

## 14. V2+ Kapsam Dışı

- **OnSave automatic dispatcher** (form Save'inden tetikleme — schema hazır, sadece servis + Save action satırları lazım)
- **Event-based trigger** (`IntegrationEvents` tablosu üstünden)
- **Bulk işlem**: Liste ekranlarında çoklu seçim → "Seçilileri Entegre Et"
- **Swagger import** (endpoint katalogu otomatik dolsun)
- **Conditional mapping görsel editor** ("When → Then → Else" form editörü)
- **Buton durum badge** (son çalıştırma yeşil/sarı/kırmızı, tooltip ile timestamp)
- **Roslyn Scripting** (NCalc'tan daha güçlü expression)
- **Integration versioning + rollback** (`IntegrationHistory` snapshot — V1.1)
- **Webhook receiver** (dışarıdan gelen istekleri form kaydı olarak işle — ters yön)
- **Export/Import** (entegrasyon konfigürasyonu JSON'a)

---

## 15. Risk Listesi

| Risk | Etki | Önlem |
|---|---|---|
| Hedef endpoint schema değişirse mapping bozulur | Yüksek | Endpoint kayıtlarında `SchemaHash`; haftalık validation job hash karşılaştırıp admin uyarısı |
| NCalc expression injection (kullanıcı kötü niyetli formül) | Düşük (NCalc kod execute etmez) | Whitelist fonksiyonlar; input sanitization |
| Büyük volume (10K kayıt/saat) HTTP rate limit | Orta | Cron handler concurrency limit; exponential backoff retry |
| Tarih/sayı locale sorunu | Orta | `MappingEngine.ApplyFormat` ISO 8601 default |
| OnSave trigger'ı sync çağrılırsa user save'i bekler | Yüksek (V2) | Kuyruğa atıp fire-and-forget; save endpoint'i bloklamasın |
| Mapping migration kırılması (versiyonlama) | Orta | `IntegrationHistory` snapshot + "Yeni versiyon" akışı |
| Manuel buton form sayfalarında render'a izin gerek | Düşük | Partial pattern — Razor view dokunulmaz, partial inject |

---

## 16. CalibraHub'a Spesifik Mimari Notlar

- **Tablo isimlendirme**: PascalCase, prefix yok — bkz. [CLAUDE.md → DB Naming Convention](../CLAUDE.md#db-naming-convention). `Integration`, `IntegrationMapping`, vb.
- **Schema**: per-company DB. `Integration` tablosu her şirketin kendi DB'sinde — `CompanyId` kolonu YOK (zaten ait olduğu DB belirler).
- **Migration**: `CalibraDatabaseInitializer.EnsureIntegrationTablesAsync` (yeni method) — idempotent CREATE IF NOT EXISTS.
- **Connection pattern**: `SqlServerConnectionFactory.OpenConnectionAsync(ct)` — HttpContext'ten company resolve.
- **Logging**: Serilog + log file (mevcut); `IntegrationRun` tablosu ayrıca audit log.
- **Frontend bundle**: Wizard React komponenti `calibrahub-widgets.js` bundle'ına dahil edilir; `mount.jsx` içinde `mountIntegrationWizard(element, config)` expose edilir.
- **UI standardı**:
  - Liste sayfası: SmartBoard
  - Wizard sayfası: full-screen Compose-style (Login/RoutingTree benzeri)
  - Mapping editor: 2-sütun custom (referans alınacak: CardGroupTree'nin iki-panel layout'u)
  - Modal'lar: CLAUDE.md custom modal standardı (silme/onay için)
  - Boolean alanlar: switchkey (CLAUDE.md kuralı)

---

## 17. Önceki Doküman (LogoRest taslağı) Ile Farklar

| LogoRest taslağı | CalibraHub adaptation |
|---|---|
| `CALIBA_INTEGRATION` | `Integration` (PascalCase, prefix yok) |
| `CALIBA_LOOKUP` + `_ITEM` | **Silindi** — mevcut `cbv_Guide_*` + `GuideMas` |
| Hangfire | **Silindi** — mevcut `ScheduledTasks` |
| Yeni `IFormMetadataProvider` | Mevcut `Forms` + `WidgetMas` + `v_Flat_*` |
| Yeni `CALIBA_REST_ENDPOINT.AuthTip/AuthConfig` | Mevcut `integration_api_profiles` |
| Tek `TriggerTip` kolonu | Ayrı `IntegrationTrigger` tablosu (multi-trigger) |
| Blazor/MVC frontend? | MVC + React (mevcut hibrit pattern netleştirildi) |
| Multi-tenant açık soru | Per-company DB ile zaten çözülmüş |
| 5 sprint (9 hafta) | 3 sprint (5 hafta) — altyapı reuse %30-40 kazanım |

---

**Doküman versiyonu**: 2.0
**Önceki versiyon**: `LogoRest/CalibaHub-EntegrasyonWizard-Tasarim.md` v1.0 (2026-05-12, taslak)
**Tarih**: 2026-05-12
**Onay bekleyen**: kullanıcı
