# CalibraHub – Metadata-Driven Engine (Dynamic DDL) Mimari Taslağı

> **Amaç:** Mevcut canlı dinamik form / widget (EAV) altyapısını **bozmadan**, paralel çalışan, fiziksel kolon (Dynamic DDL) tabanlı yeni nesil bir metaveri motoru kurmak. Binlerce firma + milyonlarca satır altında indekslenebilir, BI/AI uyumlu, SQL-injection'a karşı %100 korumalı.
>
> **Desen:** Strangler Fig (paralel çalışma) · Factory / Interface · Dapper micro-ORM · per-company DB izolasyonu.
>
> **Hedef:** .NET 8/9 · SQL Server · CalibraHub Clean Architecture katmanları (`Domain → Application → Persistence → Web`).

---

## 0. Yönetici Özeti & Tasarım Kararları

| Karar | Seçim | Gerekçe |
|-------|-------|---------|
| Veri modeli | **Fiziksel kolon (Dynamic DDL)**, EAV değil | Index, sayfalama, `WHERE`/`ORDER BY`, BI/AI raporlama native çalışır; milyonlarca satırda EAV pivot maliyeti yok |
| İzolasyon | Yeni tablolar **`engine` şemasında** (`engine.Inv_Master`) | `dbo`'daki canlı tablolar hiç etkilenmez; yetki/yedek/izleme şema bazında ayrılır |
| Metaveri konumu | `dbo.Sys_Entities`, `dbo.Sys_Fields` (per-company DB içinde) | Per-company DB mimarisi → `CompanyId` kolonu YOK (CLAUDE.md kuralı) |
| ORM | **Dapper** + parametreli komut | Mevcut repo deseni (`SqlWidgetRepository`); micro-ORM, tam SQL kontrolü |
| Bağlantı | Mevcut `SqlServerConnectionFactory.OpenConnectionAsync(ct)` | Per-company connection + MARS yeniden kullanılır, yeni altyapı gerekmez |
| Güvenlik | DDL ve DML'de **identifier whitelist + `QUOTENAME`/bracket-escape** | Kullanıcı girdisi asla ham SQL'e girmez; tip/isim katalogdan doğrulanır |
| Cache | `IMemoryCache` + sürüm damgası (schema version) | DDL sonrası invalidation; Save Engine her zaman güncel şemaya göre çalışır |

**Strangler Fig konumu:** Yeni motor bağımsız bir dikey (`CalibraHub.*.Engine` isim alanları) olarak eklenir. Mevcut `WidgetService` / `MaterialCardDynamicField*` akışları **dokunulmadan** çalışmaya devam eder. Yeni ekranlar/motorlar (stok_motoru, cari_motoru) doğrudan yeni motoru kullanır; eski ekranlar zamanla, modül modül, "boğan incir" gibi yeni motora taşınır. İki sistem aynı anda canlıda kalır.

---

## 1. İzole Metaveri Şeması (T-SQL)

### 1.1 Katalog mimarisi — genel bakış

```
┌──────────────────────── per-company DB ────────────────────────┐
│                                                                 │
│   dbo.Sys_Entities      (motor/modül tanımı: stok_motoru …)     │
│        │ 1                                                       │
│        │ ──────────────┐                                        │
│        ▼ N             ▼                                         │
│   dbo.Sys_Fields    engine.<PhysicalTable>  (fiziksel veri)     │
│   (alan kataloğu)   örn. engine.Inv_Master                      │
│                                                                 │
│   Metaveri yazılınca  ──ALTER TABLE──►  fiziksel kolon açılır   │
└─────────────────────────────────────────────────────────────────┘
```

`Sys_*` tabloları **kontrol düzlemi** (control plane): neyin var olduğunu, hangi tipte ve hangi kuralla olduğunu tanımlar. `engine.*` tabloları **veri düzlemi** (data plane): gerçek satırların tutulduğu, DDL ile büyüyen fiziksel tablolardır.

### 1.2 `dbo.Sys_Entities` — Motor / modül kataloğu

Her satır bir "motor" = bir fiziksel ana tabloya (ve şemaya) karşılık gelir.

```sql
-- ============================================================
--  dbo.Sys_Entities : Dynamic-DDL motor kataloğu
--  CalibraHub naming convention: PascalCase, INT IDENTITY PK,
--  audit dörtlüsü, per-company DB (CompanyId YOK).
-- ============================================================
IF OBJECT_ID(N'dbo.Sys_Entities', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Sys_Entities (
        Id              INT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_Sys_Entities PRIMARY KEY,

        -- Mantıksal motor kodu: 'stok_motoru', 'cari_motoru' (kullanıcıya gösterim + API key)
        EntityCode      NVARCHAR(50)  NOT NULL,
        -- Kullanıcıya görünen ad
        Name            NVARCHAR(200) NOT NULL,
        Description     NVARCHAR(1000) NULL,

        -- Fiziksel hedef: engine.Inv_Master  →  PhysicalSchema='engine', PhysicalTable='Inv_Master'
        PhysicalSchema  NVARCHAR(50)  NOT NULL
            CONSTRAINT DF_Sys_Entities_Schema DEFAULT N'engine',
        PhysicalTable   NVARCHAR(128) NOT NULL,

        -- Şema sürümü: her ALTER sonrası +1 → cache invalidation damgası
        SchemaVersion   INT NOT NULL
            CONSTRAINT DF_Sys_Entities_SchemaVersion DEFAULT 1,

        IsActive        BIT NOT NULL
            CONSTRAINT DF_Sys_Entities_IsActive DEFAULT 1,
        CreatedBy       NVARCHAR(120) NULL,
        Created         DATETIME      NOT NULL
            CONSTRAINT DF_Sys_Entities_Created DEFAULT SYSUTCDATETIME(),
        UpdatedBy       NVARCHAR(120) NULL,
        Updated         DATETIME      NULL
    );

    -- Doğal anahtar: motor kodu benzersiz (soft-delete güvenli filtered unique)
    CREATE UNIQUE INDEX UX_Sys_Entities_EntityCode
        ON dbo.Sys_Entities (EntityCode)
        WHERE IsActive = 1;

    -- Aynı fiziksel tabloya iki motor bağlanamaz
    CREATE UNIQUE INDEX UX_Sys_Entities_Physical
        ON dbo.Sys_Entities (PhysicalSchema, PhysicalTable);
END
GO
```

### 1.3 `dbo.Sys_Fields` — Alan kataloğu

Her satır, bir motorun fiziksel tablosundaki **bir kolona** karşılık gelir. Hem sistem (built-in) hem dinamik (kullanıcı eklediği) alanlar burada tanımlıdır.

```sql
-- ============================================================
--  dbo.Sys_Fields : Motor başına alan (kolon) kataloğu
-- ============================================================
IF OBJECT_ID(N'dbo.Sys_Fields', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Sys_Fields (
        Id              INT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_Sys_Fields PRIMARY KEY,

        EntityId        INT NOT NULL
            CONSTRAINT FK_Sys_Fields_Sys_Entities
            REFERENCES dbo.Sys_Entities (Id),

        -- Fiziksel kolon adı (örn. 'GlutenOrani'). Whitelist'ten geçer, ALTER'da bu kullanılır.
        FieldName       NVARCHAR(128) NOT NULL,
        -- Kullanıcıya görünen etiket (örn. 'Glüten Oranı')
        Label           NVARCHAR(200) NOT NULL,

        -- Mantıksal veri tipi: 1=String 2=Integer 3=Decimal 4=DateTime 5=Boolean 6=Lookup
        --  (CalibraHub MaterialCardDynamicFieldDataType enum'ı ile hizalı)
        DataType        TINYINT NOT NULL,

        -- Tip parametreleri (mantıksal → fiziksel SQL tipine çevirim için)
        MaxLength       INT NULL,            -- String için NVARCHAR(n); NULL → NVARCHAR(MAX)
        NumericScale    TINYINT NULL,        -- Decimal için DECIMAL(18, scale) — varsayılan 4

        IsRequired      BIT NOT NULL
            CONSTRAINT DF_Sys_Fields_IsRequired DEFAULT 0,
        IsIndexed       BIT NOT NULL
            CONSTRAINT DF_Sys_Fields_IsIndexed DEFAULT 0,

        -- Lookup (DataType=6) ise hangi motora bağlı: Sys_Entities.Id (FK ilişkisi ID üzerinden)
        LookupEntityId  INT NULL
            CONSTRAINT FK_Sys_Fields_LookupEntity
            REFERENCES dbo.Sys_Entities (Id),

        -- Sistem alanı mı (built-in, DDL ile silinemez) yoksa kullanıcı mı ekledi
        IsSystem        BIT NOT NULL
            CONSTRAINT DF_Sys_Fields_IsSystem DEFAULT 0,
        -- Fiziksel kolon gerçekten açıldı mı (ALTER başarılıysa 1) — idempotent senkron için
        IsMaterialized  BIT NOT NULL
            CONSTRAINT DF_Sys_Fields_IsMaterialized DEFAULT 0,

        DisplayOrder    INT NOT NULL
            CONSTRAINT DF_Sys_Fields_DisplayOrder DEFAULT 0,

        IsActive        BIT NOT NULL
            CONSTRAINT DF_Sys_Fields_IsActive DEFAULT 1,
        CreatedBy       NVARCHAR(120) NULL,
        Created         DATETIME      NOT NULL
            CONSTRAINT DF_Sys_Fields_Created DEFAULT SYSUTCDATETIME(),
        UpdatedBy       NVARCHAR(120) NULL,
        Updated         DATETIME      NULL
    );

    -- Bir motor içinde alan adı benzersiz (case-insensitive collation varsayımı)
    CREATE UNIQUE INDEX UX_Sys_Fields_Entity_FieldName
        ON dbo.Sys_Fields (EntityId, FieldName)
        WHERE IsActive = 1;

    CREATE INDEX IX_Sys_Fields_Entity
        ON dbo.Sys_Fields (EntityId, DisplayOrder);
END
GO
```

> **Lookup ilişkisi neden ID?** CLAUDE.md "ID tabanlı eşleştirme" kuralı: bir alan başka bir motora bağlıysa (`DataType=6`), bağ `LookupEntityId INT FK` üzerinden kurulur — asla `EntityCode` string'i üzerinden değil. Fiziksel kolon da `{FieldName}Id INT` olarak açılır (bkz. §2.4 tip eşleme).

### 1.4 `engine` şeması ve fiziksel ana tablo iskeleti

Her motor oluşturulduğunda, `engine` şeması altında minimal bir **çekirdek tablo** açılır; dinamik alanlar bunun üzerine `ALTER TABLE` ile eklenir.

```sql
-- engine şeması (idempotent)
IF SCHEMA_ID(N'engine') IS NULL
    EXEC(N'CREATE SCHEMA engine AUTHORIZATION dbo');
GO

-- Örnek: stok_motoru → engine.Inv_Master çekirdek iskeleti
--  (Bu iskelet C# tarafında EntityProvisioningService ile parametrik üretilir; aşağısı referans.)
IF OBJECT_ID(N'engine.Inv_Master', N'U') IS NULL
BEGIN
    CREATE TABLE engine.Inv_Master (
        Id          INT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_Inv_Master PRIMARY KEY,

        -- Çekirdek doğal anahtar (her motorda 'Code' standardı; CLAUDE.md guide kuralı)
        Code        NVARCHAR(50)  NOT NULL,
        Name        NVARCHAR(200) NULL,

        -- Audit dörtlüsü (her engine tablosunda zorunlu)
        IsActive    BIT NOT NULL
            CONSTRAINT DF_Inv_Master_IsActive DEFAULT 1,
        CreatedBy   NVARCHAR(120) NULL,
        Created     DATETIME      NOT NULL
            CONSTRAINT DF_Inv_Master_Created DEFAULT SYSUTCDATETIME(),
        UpdatedBy   NVARCHAR(120) NULL,
        Updated     DATETIME      NULL
    );

    CREATE UNIQUE INDEX UX_Inv_Master_Code
        ON engine.Inv_Master (Code)
        WHERE IsActive = 1;
END
GO
```

`Id`, `Code`, `Name` ve audit dörtlüsü her motorda **sistem alanı** (`Sys_Fields.IsSystem=1`) olarak da kataloglanır; böylece Save Engine bunları tanır ama DDL ile silinmelerine izin vermez.

---

## 2. Dynamic DDL Service (C#)

### 2.1 Katman yerleşimi (mevcut Clean Architecture'a uygun)

```
CalibraHub.Domain
  └─ Engine/                         (yeni dikey — Strangler Fig)
       ├─ EntityDefinition.cs        ← Sys_Entities POCO
       ├─ FieldDefinition.cs         ← Sys_Fields POCO
       └─ EngineFieldDataType.cs     ← enum (mevcut DataType enum'ı ile hizalı)

CalibraHub.Application
  └─ Abstractions/Engine/
       ├─ IEngineCatalogRepository.cs    ← Sys_* okuma/yazma
       ├─ IDynamicDdlService.cs          ← ALTER TABLE motoru
       ├─ IEngineSchemaCache.cs          ← MemoryCache cephe
       ├─ ISqlIdentifierGuard.cs         ← whitelist/escape guard
       └─ IEngineSaveService.cs          ← Generic Save Engine (§3)
  └─ Contracts/Engine/
       ├─ AddFieldRequest.cs
       └─ EngineSchemaSnapshot.cs        ← cache'lenen immutable şema
  └─ Services/Engine/
       ├─ DynamicDdlService.cs
       ├─ EngineSchemaCache.cs
       └─ EngineSaveService.cs

CalibraHub.Persistence
  └─ Repositories/Engine/
       ├─ SqlEngineCatalogRepository.cs   ← Dapper, SqlServerConnectionFactory
       └─ SqlEngineDataRepository.cs      ← Dapper, engine.* DML
```

Hiçbiri `dbo.Widget*` / `MaterialCardDynamicField*` sınıflarına dokunmaz — tamamen ayrı isim alanı.

### 2.2 Domain — POCO'lar ve tip enum'ı

```csharp
namespace CalibraHub.Domain.Engine;

/// <summary>Sys_Entities satırı — bir Dynamic-DDL motoru.</summary>
public sealed class EntityDefinition
{
    public int    Id { get; init; }
    public string EntityCode { get; init; } = string.Empty;   // 'stok_motoru'
    public string Name { get; init; } = string.Empty;
    public string PhysicalSchema { get; init; } = "engine";
    public string PhysicalTable { get; init; } = string.Empty; // 'Inv_Master'
    public int    SchemaVersion { get; init; } = 1;
    public bool   IsActive { get; init; } = true;
}

/// <summary>Sys_Fields satırı — bir fiziksel kolon tanımı.</summary>
public sealed class FieldDefinition
{
    public int    Id { get; init; }
    public int    EntityId { get; init; }
    public string FieldName { get; init; } = string.Empty;     // 'GlutenOrani'
    public string Label { get; init; } = string.Empty;
    public EngineFieldDataType DataType { get; init; }
    public int?   MaxLength { get; init; }
    public byte?  NumericScale { get; init; }
    public bool   IsRequired { get; init; }
    public bool   IsIndexed { get; init; }
    public int?   LookupEntityId { get; init; }
    public bool   IsSystem { get; init; }
    public bool   IsMaterialized { get; init; }
}

/// <summary>MaterialCardDynamicFieldDataType ile bilinçli olarak hizalı tutulur.</summary>
public enum EngineFieldDataType : byte
{
    String   = 1,
    Integer  = 2,
    Decimal  = 3,
    DateTime = 4,
    Boolean  = 5,
    Lookup   = 6
}
```

### 2.3 Güvenlik temeli — `ISqlIdentifierGuard`

DDL ve DML'in **tek savunma hattı**. Parametreler değer (`@p0`) güvenliğini sağlar; ama tablo/kolon **isimleri** parametre olamaz, doğrudan SQL metnine girer. Bu yüzden her identifier iki kapıdan geçer: (1) katı regex whitelist, (2) `[...]` bracket-escape.

```csharp
namespace CalibraHub.Application.Abstractions.Engine;

public interface ISqlIdentifierGuard
{
    /// <summary>Geçerli SQL identifier mı? (harf/altçizgi ile başlar, [A-Za-z0-9_], 1..128).</summary>
    bool IsValid(string identifier);

    /// <summary>Doğrular + [bracket] içine alır. Geçersizse InvalidOperationException atar.</summary>
    string Quote(string identifier);

    /// <summary>schema.table → [schema].[table] (ikisi de doğrulanır).</summary>
    string QuoteQualified(string schema, string table);
}
```

```csharp
using System.Text.RegularExpressions;

namespace CalibraHub.Application.Services.Engine;

public sealed partial class SqlIdentifierGuard : ISqlIdentifierGuard
{
    // T-SQL regular identifier kuralı + uzunluk sınırı (128 = sysname).
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();

    // Rezerve kelime kara listesi (ALTER hedefi olamayacak tehlikeli isimler).
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT","INSERT","UPDATE","DELETE","DROP","ALTER","EXEC","EXECUTE",
        "TABLE","CREATE","TRUNCATE","GRANT","xp_cmdshell"
    };

    public bool IsValid(string identifier)
        => !string.IsNullOrWhiteSpace(identifier)
           && IdentifierPattern().IsMatch(identifier)
           && !Reserved.Contains(identifier);

    public string Quote(string identifier)
    {
        if (!IsValid(identifier))
            throw new InvalidOperationException($"Geçersiz SQL tanımlayıcı: '{identifier}'.");
        // Whitelist'ten geçse de defensive bracket-escape (']' → ']]').
        return "[" + identifier.Replace("]", "]]") + "]";
    }

    public string QuoteQualified(string schema, string table)
        => $"{Quote(schema)}.{Quote(table)}";
}
```

> **Neden hem regex hem escape?** Regex zaten `]` karakterini reddeder; bracket-escape ikinci savunma katmanı (defense-in-depth). Hiçbir koşulda kullanıcı girdisi string-concat ile çıplak SQL'e karışmaz.

### 2.4 Mantıksal tip → fiziksel SQL tipi eşleme

```csharp
namespace CalibraHub.Application.Services.Engine;

internal static class SqlTypeMapper
{
    /// <summary>FieldDefinition → fiziksel SQL tip ifadesi (ALTER ADD için).</summary>
    public static string ToSqlType(FieldDefinition f) => f.DataType switch
    {
        EngineFieldDataType.String =>
            f.MaxLength is > 0 and <= 4000 ? $"NVARCHAR({f.MaxLength})" : "NVARCHAR(MAX)",
        EngineFieldDataType.Integer  => "INT",
        EngineFieldDataType.Decimal  => $"DECIMAL(18,{f.NumericScale ?? 4})",
        EngineFieldDataType.DateTime => "DATETIME",          // CLAUDE.md: yeni kolonlar DATETIME (UTC)
        EngineFieldDataType.Boolean  => "BIT",
        // Lookup → başka motora FK; fiziksel kolon INT (ID tabanlı eşleştirme kuralı)
        EngineFieldDataType.Lookup   => "INT",
        _ => throw new InvalidOperationException($"Desteklenmeyen tip: {f.DataType}")
    };

    /// <summary>Dapper parametre değeri için .NET tip dönüşümü (Save Engine kullanır).</summary>
    public static object? Coerce(EngineFieldDataType type, object? raw)
    {
        if (raw is null) return DBNull.Value;
        var s = raw.ToString();
        if (string.IsNullOrWhiteSpace(s)) return DBNull.Value;

        return type switch
        {
            EngineFieldDataType.String   => s,
            EngineFieldDataType.Integer  => int.Parse(s, CultureInfo.InvariantCulture),
            EngineFieldDataType.Lookup   => int.Parse(s, CultureInfo.InvariantCulture),
            EngineFieldDataType.Decimal  => decimal.Parse(s, NumberStyles.Number, CultureInfo.InvariantCulture),
            EngineFieldDataType.DateTime => DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal),
            EngineFieldDataType.Boolean  => s is "1" or "true" or "True" or "on",
            _ => throw new InvalidOperationException($"Coerce desteklenmeyen tip: {type}")
        };
    }
}
```

### 2.5 `IDynamicDdlService` — güvenli ALTER motoru

```csharp
namespace CalibraHub.Application.Abstractions.Engine;

public interface IDynamicDdlService
{
    /// <summary>
    /// Metaveriye alan ekler + engine.&lt;Table&gt;'a fiziksel kolon açar (idempotent, transactional).
    /// Mevcut dbo.* tablolarına ASLA dokunmaz — sadece kendi entity'sinin engine tablosu.
    /// </summary>
    Task<FieldDefinition> AddFieldAsync(AddFieldRequest request, CancellationToken ct);
}
```

```csharp
namespace CalibraHub.Application.Services.Engine;

public sealed class DynamicDdlService : IDynamicDdlService
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly IEngineCatalogRepository   _catalog;
    private readonly ISqlIdentifierGuard        _guard;
    private readonly IEngineSchemaCache         _cache;
    private readonly ILogger<DynamicDdlService> _logger;

    public DynamicDdlService(
        SqlServerConnectionFactory connectionFactory,
        IEngineCatalogRepository catalog,
        ISqlIdentifierGuard guard,
        IEngineSchemaCache cache,
        ILogger<DynamicDdlService> logger)
    {
        _connectionFactory = connectionFactory;
        _catalog = catalog;
        _guard   = guard;
        _cache   = cache;
        _logger  = logger;
    }

    public async Task<FieldDefinition> AddFieldAsync(AddFieldRequest request, CancellationToken ct)
    {
        // 1) Motoru doğrula (var mı, aktif mi).
        var entity = await _catalog.GetEntityByCodeAsync(request.EntityCode, ct)
            ?? throw new InvalidOperationException($"Motor bulunamadı: '{request.EntityCode}'.");

        // 2) Güvenlik kapısı — isim ve şema/tablo whitelist'ten geçmeli.
        var quotedColumn = _guard.Quote(request.FieldName);
        var quotedTable  = _guard.QuoteQualified(entity.PhysicalSchema, entity.PhysicalTable);

        // 3) Alan adı çakışması (katalog seviyesinde, case-insensitive).
        if (await _catalog.FieldExistsAsync(entity.Id, request.FieldName, ct))
            throw new InvalidOperationException(
                $"Bu motorda aynı isimde alan zaten var: '{request.FieldName}'.");

        var field = request.ToDefinition(entity.Id);
        var sqlType = SqlTypeMapper.ToSqlType(field);

        // 4) Transactional: metaveri + fiziksel kolon birlikte ya hep ya hiç.
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx   = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            // 4a) Sys_Fields'a yaz (Dapper, parametreli) — IsMaterialized=0.
            var fieldId = await _catalog.InsertFieldAsync(conn, tx, field, request.ActorUserName, ct);

            // 4b) Fiziksel kolon: IF COL_LENGTH ... IS NULL guard → idempotent.
            //     İsim/şema parametre OLAMAZ; whitelist'ten geçmiş quoted identifier kullanılır.
            //     Değer/sabit yok → injection yüzeyi sıfır.
            var nullClause = field.IsRequired ? "NOT NULL" : "NULL";
            var addColumnSql =
                $"IF COL_LENGTH('{entity.PhysicalSchema}.{entity.PhysicalTable}', " +
                $"               @ColName) IS NULL " +
                $"ALTER TABLE {quotedTable} ADD {quotedColumn} {sqlType} " +
                // Zorunlu alan + mevcut satır varsa: DEFAULT ile ekle (NOT NULL ihlali olmasın).
                (field.IsRequired ? DefaultForType(field) : string.Empty) + " " + nullClause + ";";

            await using (var cmd = new SqlCommand(addColumnSql, conn, tx))
            {
                cmd.Parameters.Add(new SqlParameter("@ColName", request.FieldName));
                await cmd.ExecuteNonQueryAsync(ct);
            }

            // 4c) Opsiyonel index (IsIndexed) — yine quoted identifier'larla.
            if (field.IsIndexed)
            {
                var ixName = _guard.Quote($"IX_{entity.PhysicalTable}_{field.FieldName}");
                var ixSql =
                    $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = @IxName) " +
                    $"CREATE INDEX {ixName} ON {quotedTable} ({quotedColumn});";
                await using var ixCmd = new SqlCommand(ixSql, conn, tx);
                ixCmd.Parameters.Add(new SqlParameter("@IxName", $"IX_{entity.PhysicalTable}_{field.FieldName}"));
                await ixCmd.ExecuteNonQueryAsync(ct);
            }

            // 4d) Materialize bayrağı + SchemaVersion++ (cache damgası).
            await _catalog.MarkFieldMaterializedAsync(conn, tx, fieldId, ct);
            await _catalog.BumpSchemaVersionAsync(conn, tx, entity.Id, ct);

            await tx.CommitAsync(ct);

            // 5) Cache invalidation — bir sonraki Save/okuma güncel şemayı görür.
            _cache.Invalidate(entity.EntityCode);

            _logger.LogInformation(
                "Engine alan eklendi: {Entity}.{Field} ({Type})",
                entity.EntityCode, field.FieldName, sqlType);

            return field with { Id = fieldId, IsMaterialized = true };
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private static string DefaultForType(FieldDefinition f) => f.DataType switch
    {
        EngineFieldDataType.Integer or EngineFieldDataType.Decimal
            or EngineFieldDataType.Lookup => "CONSTRAINT " +
                $"DF_{Sanitize(f.FieldName)} DEFAULT 0",
        EngineFieldDataType.Boolean  => $"CONSTRAINT DF_{Sanitize(f.FieldName)} DEFAULT 0",
        EngineFieldDataType.DateTime => $"CONSTRAINT DF_{Sanitize(f.FieldName)} DEFAULT SYSUTCDATETIME()",
        _ => $"CONSTRAINT DF_{Sanitize(f.FieldName)} DEFAULT N''"
    };

    private static string Sanitize(string s) => new(s.Where(char.IsLetterOrDigit).ToArray());
}
```

> **`ALTER TABLE` güvenliği — özet:**
> 1. `EntityCode` katalogdan çözülür; hedef şema/tablo **sadece** o motorun kayıtlı `engine.*` tablosudur — kullanıcı hedef tablo seçemez.
> 2. Kolon adı `ISqlIdentifierGuard` whitelist + bracket-escape'ten geçer.
> 3. Tip ifadesi (`NVARCHAR(n)`, `DECIMAL(18,s)`) **kod tarafında** üretilir, kullanıcı serbest metin gönderemez.
> 4. `IF COL_LENGTH(...) IS NULL` → idempotent; çift çağrı hata vermez.
> 5. Metaveri + DDL aynı transaction → kısmi durum kalmaz.

### 2.6 MemoryCache invalidation — `IEngineSchemaCache`

Save Engine her kayıtta DB'ye "şema nedir" diye sormaz; şemayı `IMemoryCache`'ten okur. DDL sonrası bu girdi geçersiz kılınır.

```csharp
namespace CalibraHub.Application.Abstractions.Engine;

public interface IEngineSchemaCache
{
    /// <summary>Motorun güncel şema anlık görüntüsünü getirir (yoksa DB'den yükler + cache'ler).</summary>
    Task<EngineSchemaSnapshot> GetAsync(string entityCode, CancellationToken ct);

    /// <summary>DDL sonrası tek motoru geçersiz kıl.</summary>
    void Invalidate(string entityCode);
}
```

```csharp
namespace CalibraHub.Application.Services.Engine;

public sealed class EngineSchemaCache : IEngineSchemaCache
{
    private readonly IMemoryCache             _cache;
    private readonly IEngineCatalogRepository _catalog;
    private readonly SqlServerConnectionFactory _connectionFactory;
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);

    public EngineSchemaCache(
        IMemoryCache cache,
        IEngineCatalogRepository catalog,
        SqlServerConnectionFactory connectionFactory)
    {
        _cache = cache;
        _catalog = catalog;
        _connectionFactory = connectionFactory;
    }

    // Per-company izolasyon: cache key'e company_id katılır (aynı süreçte çok firma).
    private string Key(string entityCode)
        => $"engine-schema::{_connectionFactory.ResolveCurrentCompanyId()}::{entityCode}";

    public async Task<EngineSchemaSnapshot> GetAsync(string entityCode, CancellationToken ct)
    {
        return (await _cache.GetOrCreateAsync(Key(entityCode), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Ttl;
            await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
            var entity = await _catalog.GetEntityByCodeAsync(entityCode, ct)
                ?? throw new InvalidOperationException($"Motor bulunamadı: '{entityCode}'.");
            var fields = await _catalog.GetFieldsAsync(entity.Id, ct);
            return EngineSchemaSnapshot.From(entity, fields);
        }))!;
    }

    public void Invalidate(string entityCode) => _cache.Remove(Key(entityCode));
}
```

```csharp
namespace CalibraHub.Application.Contracts.Engine;

/// <summary>Cache'lenen immutable şema; Save Engine'in tek doğruluk kaynağı.</summary>
public sealed class EngineSchemaSnapshot
{
    public required EntityDefinition Entity { get; init; }
    public required int SchemaVersion { get; init; }
    // FieldName (case-insensitive) → tanım. Save Engine bilinmeyen alanı reddeder.
    public required IReadOnlyDictionary<string, FieldDefinition> Fields { get; init; }

    public static EngineSchemaSnapshot From(EntityDefinition e, IReadOnlyList<FieldDefinition> fs)
        => new()
        {
            Entity = e,
            SchemaVersion = e.SchemaVersion,
            Fields = fs.Where(f => f.IsMaterialized)
                       .ToDictionary(f => f.FieldName, f => f, StringComparer.OrdinalIgnoreCase)
        };
}
```

> **Neden `SchemaVersion` damgası?** Çok süreçli/çok sunuculu kurulumda `Invalidate` sadece kendi process cache'ini temizler. İleride dağıtık cache (Redis) veya "her okumada `Sys_Entities.SchemaVersion` ucuz kontrolü" eklenerek diğer node'lar da güncellenir. Tek-process kurulumda `Invalidate` yeterlidir; tasarım her ikisine de açık.

---

## 3. Generic Save Engine (Dapper & C#)

Arayüzden gelen dinamik JSON paketini (`{"stok_kodu":"STK001","miktar":150,"gluten_orani":2.5}`) alır, cache'teki güncel şemaya göre **doğrular**, sonra **tamamen parametreli** bir `INSERT`/`UPDATE` üretip `engine.*` tablosuna yazar. Kullanıcı verisi hiçbir noktada ham SQL'e string olarak girmez.

### 3.1 Akış

```
JSON paket
   │
   ▼
[1] Şema çöz  ──►  EngineSchemaCache.GetAsync(entityCode)   (cache, DDL sonrası tazelenmiş)
   │
   ▼
[2] Doğrula   ──►  her key şemada var mı? · tip coerce edilebiliyor mu? · zorunlular dolu mu?
   │                bilinmeyen key → reddet (whitelist). Hiçbir alan adı kod üretmez, sadece doğrulanır.
   ▼
[3] SQL üret  ──►  kolon adları = guard.Quote(fieldName) · DEĞERLER = @p0,@p1… (Dapper DynamicParameters)
   │
   ▼
[4] Çalıştır  ──►  Dapper ExecuteScalarAsync → yeni Id  (engine.* tablosu)
```

### 3.2 Sözleşmeler

```csharp
namespace CalibraHub.Application.Abstractions.Engine;

public interface IEngineSaveService
{
    /// <summary>Dinamik paketi ilgili motorun engine tablosuna ekler; yeni Id döner.</summary>
    Task<int> InsertAsync(string entityCode, IDictionary<string, object?> payload,
                          string actorUserName, CancellationToken ct);

    /// <summary>Var olan kaydı günceller (Id zorunlu). Etkilenen satır sayısı döner.</summary>
    Task<int> UpdateAsync(string entityCode, int id, IDictionary<string, object?> payload,
                          string actorUserName, CancellationToken ct);
}
```

Web tarafında JSON `IDictionary<string, object?>`'e deserialize edilir (`System.Text.JsonElement` değerleri `SqlTypeMapper.Coerce` ile ehlileştirilir). Controller örneği §4.3'te.

### 3.3 Doğrulayıcı — şema dışı her şeyi reddeder

```csharp
namespace CalibraHub.Application.Services.Engine;

internal sealed class EnginePayloadValidator
{
    /// <summary>
    /// Payload'ı şemaya göre doğrular ve (FieldDefinition, coerced-value) listesine indirger.
    /// - Bilinmeyen alan → hata (whitelist; injection ve "yanlış kolon" koruması).
    /// - Sistem/audit alanları payload'dan KABUL EDİLMEZ (Id, Created, CreatedBy… kod set eder).
    /// - Tip dönüşmüyorsa → anlaşılır hata.
    /// - Zorunlu alan eksikse (yalnız INSERT) → hata.
    /// </summary>
    public static IReadOnlyList<(FieldDefinition Field, object? Value)> Validate(
        EngineSchemaSnapshot schema,
        IDictionary<string, object?> payload,
        bool isInsert)
    {
        var result = new List<(FieldDefinition, object?)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, raw) in payload)
        {
            if (ReservedColumns.Contains(key))
                continue; // audit/PK alanları sessizce yok sayılır — kod yönetir

            if (!schema.Fields.TryGetValue(key, out var field))
                throw new EngineValidationException($"Tanımsız alan: '{key}'.");

            if (field.IsSystem)
                continue; // sistem alanı kullanıcıdan set edilmez

            object? value;
            try { value = SqlTypeMapper.Coerce(field.DataType, raw); }
            catch { throw new EngineValidationException(
                $"'{field.Label}' alanı için geçersiz değer: '{raw}'."); }

            if (field.IsRequired && value is DBNull or null)
                throw new EngineValidationException($"'{field.Label}' zorunludur.");

            result.Add((field, value));
            seen.Add(field.FieldName);
        }

        // INSERT'te eksik zorunlu alan kontrolü.
        if (isInsert)
        {
            foreach (var f in schema.Fields.Values)
                if (f is { IsRequired: true, IsSystem: false } && !seen.Contains(f.FieldName))
                    throw new EngineValidationException($"'{f.Label}' zorunludur.");
        }

        if (result.Count == 0)
            throw new EngineValidationException("Kaydedilecek geçerli alan yok.");

        return result;
    }

    private static readonly HashSet<string> ReservedColumns = new(StringComparer.OrdinalIgnoreCase)
    { "Id", "Created", "CreatedBy", "Updated", "UpdatedBy" };
}

public sealed class EngineValidationException(string message) : Exception(message);
```

### 3.4 Dinamik INSERT/UPDATE üretici — Dapper `DynamicParameters`

Kritik kural: **kolon adları** guard'lı identifier'dır; **değerler** her zaman `@p{i}` parametresidir. İkisi asla karışmaz.

```csharp
using Dapper;

namespace CalibraHub.Application.Services.Engine;

public sealed class EngineSaveService : IEngineSaveService
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly IEngineSchemaCache         _cache;
    private readonly ISqlIdentifierGuard        _guard;

    public EngineSaveService(
        SqlServerConnectionFactory connectionFactory,
        IEngineSchemaCache cache,
        ISqlIdentifierGuard guard)
    {
        _connectionFactory = connectionFactory;
        _cache = cache;
        _guard = guard;
    }

    public async Task<int> InsertAsync(
        string entityCode, IDictionary<string, object?> payload,
        string actorUserName, CancellationToken ct)
    {
        var schema = await _cache.GetAsync(entityCode, ct);
        var pairs  = EnginePayloadValidator.Validate(schema, payload, isInsert: true);

        var table = _guard.QuoteQualified(
            schema.Entity.PhysicalSchema, schema.Entity.PhysicalTable);

        var columns = new List<string>();
        var values  = new List<string>();
        var dp = new DynamicParameters();

        for (var i = 0; i < pairs.Count; i++)
        {
            columns.Add(_guard.Quote(pairs[i].Field.FieldName)); // identifier
            values.Add($"@p{i}");                                // parametre
            dp.Add($"p{i}", pairs[i].Value);                     // değer (parametreli)
        }

        // Audit alanları kod tarafında — kullanıcı override edemez.
        columns.Add("[CreatedBy]"); values.Add("@CreatedBy"); dp.Add("CreatedBy", actorUserName);
        columns.Add("[Created]");   values.Add("SYSUTCDATETIME()");

        // OUTPUT INSERTED.Id → yeni anahtar, ek sorgu yok.
        var sql =
            $"INSERT INTO {table} ({string.Join(", ", columns)}) " +
            $"OUTPUT INSERTED.[Id] " +
            $"VALUES ({string.Join(", ", values)});";

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, dp, cancellationToken: ct));
    }

    public async Task<int> UpdateAsync(
        string entityCode, int id, IDictionary<string, object?> payload,
        string actorUserName, CancellationToken ct)
    {
        var schema = await _cache.GetAsync(entityCode, ct);
        var pairs  = EnginePayloadValidator.Validate(schema, payload, isInsert: false);

        var table = _guard.QuoteQualified(
            schema.Entity.PhysicalSchema, schema.Entity.PhysicalTable);

        var sets = new List<string>();
        var dp = new DynamicParameters();

        for (var i = 0; i < pairs.Count; i++)
        {
            sets.Add($"{_guard.Quote(pairs[i].Field.FieldName)} = @p{i}");
            dp.Add($"p{i}", pairs[i].Value);
        }

        sets.Add("[UpdatedBy] = @UpdatedBy"); dp.Add("UpdatedBy", actorUserName);
        sets.Add("[Updated] = SYSUTCDATETIME()");
        dp.Add("Id", id);

        var sql =
            $"UPDATE {table} SET {string.Join(", ", sets)} " +
            $"WHERE [Id] = @Id AND [IsActive] = 1;";

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        return await conn.ExecuteAsync(
            new CommandDefinition(sql, dp, cancellationToken: ct));
    }
}
```

### 3.5 Üretilen SQL — örnek

`InsertAsync("stok_motoru", {"Code":"STK001","Miktar":150,"GlutenOrani":2.5})` çağrısı şunu üretir (değerler parametre olarak gider, metne gömülmez):

```sql
INSERT INTO [engine].[Inv_Master] ([Code], [Miktar], [GlutenOrani], [CreatedBy], [Created])
OUTPUT INSERTED.[Id]
VALUES (@p0, @p1, @p2, @CreatedBy, SYSUTCDATETIME());
-- @p0='STK001' (NVARCHAR), @p1=150 (INT), @p2=2.5 (DECIMAL(18,4)), @CreatedBy='bilal@...'
```

`@p2`'ye `2.5; DROP TABLE …` gibi bir değer gelse bile bu **veri**dir, parametre olarak bağlanır — SQL olarak yorumlanmaz. Kolon adı `gluten_orani` yerine `GlutenOrani'; DROP…` gönderilse, `EnginePayloadValidator` onu şemada bulamaz ve **reddeder**.

> **SQL injection neden imkânsız:** (a) kolon adları yalnızca `Sys_Fields` katalogunda **materialize edilmiş** alanlardan gelir ve `ISqlIdentifierGuard`'tan geçer; (b) tüm kullanıcı **değerleri** `DynamicParameters` ile bağlanır; (c) şema dışı her anahtar reddedilir. Üç katman bağımsız olarak injection yüzeyini kapatır.

### 3.6 Persistence — `SqlEngineCatalogRepository` (Dapper, mevcut desen)

`SqlWidgetRepository` deseniyle birebir: `SqlServerConnectionFactory`, configurable schema, parametreli Dapper.

```csharp
namespace CalibraHub.Persistence.Repositories.Engine;

public sealed class SqlEngineCatalogRepository : IEngineCatalogRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;

    public SqlEngineCatalogRepository(SqlServerConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public async Task<EntityDefinition?> GetEntityByCodeAsync(string code, CancellationToken ct)
    {
        const string sql = """
            SELECT Id, EntityCode, Name, PhysicalSchema, PhysicalTable, SchemaVersion, IsActive
            FROM   dbo.Sys_Entities
            WHERE  EntityCode = @code AND IsActive = 1;
            """;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<EntityDefinition>(
            new CommandDefinition(sql, new { code }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<FieldDefinition>> GetFieldsAsync(int entityId, CancellationToken ct)
    {
        const string sql = """
            SELECT Id, EntityId, FieldName, Label, DataType, MaxLength, NumericScale,
                   IsRequired, IsIndexed, LookupEntityId, IsSystem, IsMaterialized
            FROM   dbo.Sys_Fields
            WHERE  EntityId = @entityId AND IsActive = 1
            ORDER BY DisplayOrder, Id;
            """;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        return (await conn.QueryAsync<FieldDefinition>(
            new CommandDefinition(sql, new { entityId }, cancellationToken: ct))).ToList();
    }

    public async Task<bool> FieldExistsAsync(int entityId, string fieldName, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(1) FROM dbo.Sys_Fields
            WHERE EntityId = @entityId AND FieldName = @fieldName AND IsActive = 1;
            """;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, new { entityId, fieldName }, cancellationToken: ct)) > 0;
    }

    // InsertFieldAsync / MarkFieldMaterializedAsync / BumpSchemaVersionAsync
    // aynı conn+tx üzerinde Dapper ile (DynamicDdlService transaction'ı içinde) çalışır.
    public async Task<int> InsertFieldAsync(
        SqlConnection conn, SqlTransaction tx, FieldDefinition f, string actor, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO dbo.Sys_Fields
              (EntityId, FieldName, Label, DataType, MaxLength, NumericScale,
               IsRequired, IsIndexed, LookupEntityId, IsSystem, IsMaterialized,
               DisplayOrder, CreatedBy, Created)
            OUTPUT INSERTED.Id
            VALUES
              (@EntityId, @FieldName, @Label, @DataType, @MaxLength, @NumericScale,
               @IsRequired, @IsIndexed, @LookupEntityId, 0, 0,
               @DisplayOrder, @actor, SYSUTCDATETIME());
            """;
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            sql, new {
                f.EntityId, f.FieldName, f.Label, DataType = (byte)f.DataType,
                f.MaxLength, f.NumericScale, f.IsRequired, f.IsIndexed,
                f.LookupEntityId, DisplayOrder = 0, actor
            }, tx, cancellationToken: ct));
    }

    public Task MarkFieldMaterializedAsync(SqlConnection conn, SqlTransaction tx, int fieldId, CancellationToken ct)
        => conn.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.Sys_Fields SET IsMaterialized = 1, Updated = SYSUTCDATETIME() WHERE Id = @fieldId;",
            new { fieldId }, tx, cancellationToken: ct));

    public Task BumpSchemaVersionAsync(SqlConnection conn, SqlTransaction tx, int entityId, CancellationToken ct)
        => conn.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.Sys_Entities SET SchemaVersion = SchemaVersion + 1, Updated = SYSUTCDATETIME() WHERE Id = @entityId;",
            new { entityId }, tx, cancellationToken: ct));
}
```

---

## 4. Strangler Fig Entegrasyonu, Factory Pattern & DI

### 4.1 Factory — motor başına özelleştirme

Çoğu motor jenerik servisi paylaşır; ama bazıları özel doğrulama/iş kuralı isteyebilir (stok_motoru'nda negatif miktar yasak gibi). `IEngineFactory` doğru servis setini motor koduna göre çözer.

```csharp
namespace CalibraHub.Application.Abstractions.Engine;

/// <summary>Motor koduna göre uygun Save/DDL davranışını çözer (Strangler Fig genişleme noktası).</summary>
public interface IEngineFactory
{
    IEngineSaveService GetSaveService(string entityCode);
    bool IsEngineEntity(string entityCode); // yeni motor mu, yoksa eski sisteme mi düşülecek?
}

public sealed class EngineFactory : IEngineFactory
{
    private readonly IEngineSaveService _default;
    private readonly IReadOnlyDictionary<string, IEngineSaveService> _overrides;
    private readonly IEngineCatalogRepository _catalog;

    public EngineFactory(
        IEngineSaveService defaultSave,
        IEnumerable<IKeyedEngineSaveService> keyed,  // motor-özel implementasyonlar
        IEngineCatalogRepository catalog)
    {
        _default = defaultSave;
        _overrides = keyed.ToDictionary(k => k.EntityCode, k => k.Service, StringComparer.OrdinalIgnoreCase);
        _catalog = catalog;
    }

    public IEngineSaveService GetSaveService(string entityCode)
        => _overrides.TryGetValue(entityCode, out var s) ? s : _default;

    public bool IsEngineEntity(string entityCode)
        => _catalog.GetEntityByCodeAsync(entityCode, CancellationToken.None)
                   .GetAwaiter().GetResult() is not null;
}
```

> Yeni bir motor eklemek = `Sys_Entities`'e satır + `engine.*` çekirdek tablo. Hiçbir mevcut sınıf değişmez. Özel kural gerekirse `IKeyedEngineSaveService` implement eden tek bir sınıf eklenir — **açık/kapalı prensibi** (OCP).

### 4.2 Strangler Fig — eski ve yeni yan yana

```
                       ┌─────────────────────────────────────────┐
   Kullanıcı isteği ──►│  EngineRouter (ince yönlendirme katmanı) │
                       └───────────────┬─────────────────────────┘
                          IsEngineEntity?│
                    ┌──────── evet ──────┴──── hayır ────────┐
                    ▼                                         ▼
        YENİ: EngineSaveService               ESKİ: WidgetService / MaterialCardDynamicField*
        (engine.* fiziksel kolon)             (dbo.* EAV — DOKUNULMADI, canlı)
```

`EngineRouter` tek karar verir: motor kodu `Sys_Entities`'te kayıtlı mı? Kayıtlıysa yeni motor; değilse mevcut akış. Böylece migrasyon **modül modül** yapılır: bir modülü yeni motora taşımak = onu `Sys_Entities`'e tanımlamak. Eskiler hiç değişmeden çalışmaya devam eder. İki sistem aynı anda canlıda — "boğan incir" eskisini zamanla sarar.

```csharp
namespace CalibraHub.Application.Services.Engine;

/// <summary>Strangler Fig yönlendiricisi — yeni motor mu eski sistem mi? Tek karar noktası.</summary>
public sealed class EngineRouter
{
    private readonly IEngineFactory _factory;
    private readonly ILegacyDynamicFieldFacade _legacy; // mevcut WidgetService'i saran ince adaptör

    public EngineRouter(IEngineFactory factory, ILegacyDynamicFieldFacade legacy)
    {
        _factory = factory;
        _legacy  = legacy;
    }

    public Task<int> SaveAsync(string entityCode, IDictionary<string, object?> payload,
                               string actor, CancellationToken ct)
        => _factory.IsEngineEntity(entityCode)
            ? _factory.GetSaveService(entityCode).InsertAsync(entityCode, payload, actor, ct)
            : _legacy.SaveAsync(entityCode, payload, actor, ct); // mevcut yol — değişmedi
}
```

### 4.3 Web — Controller (mevcut `WidgetsController` deseni)

```csharp
namespace CalibraHub.Web.Controllers;

[ApiController]
[Route("api/engine")]
public sealed class EngineController : ControllerBase
{
    private readonly IDynamicDdlService _ddl;
    private readonly EngineRouter       _router;

    public EngineController(IDynamicDdlService ddl, EngineRouter router)
    {
        _ddl = ddl;
        _router = router;
    }

    /// <summary>Arayüzden yeni dinamik alan: metaveri + fiziksel kolon.</summary>
    [HttpPost("{entityCode}/fields")]
    public async Task<IActionResult> AddField(string entityCode, [FromBody] AddFieldRequestDto dto, CancellationToken ct)
    {
        try
        {
            var field = await _ddl.AddFieldAsync(dto.ToRequest(entityCode, User.Identity?.Name ?? "system"), ct);
            return Ok(new { ok = true, fieldId = field.Id });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { ok = false, error = ex.Message }); }
    }

    /// <summary>Dinamik kayıt: JSON paket → engine.* (yeni motor) veya legacy (eski). Router karar verir.</summary>
    [HttpPost("{entityCode}/records")]
    public async Task<IActionResult> Save(string entityCode, [FromBody] Dictionary<string, object?> payload, CancellationToken ct)
    {
        try
        {
            var id = await _router.SaveAsync(entityCode, payload, User.Identity?.Name ?? "system", ct);
            return Ok(new { ok = true, id });
        }
        catch (EngineValidationException ex) { return BadRequest(new { ok = false, error = ex.Message }); }
    }
}
```

### 4.4 DI kaydı (CalibraHub.Web `Program.cs` / composition root)

```csharp
// ── Metadata-Driven Engine (Dynamic DDL) — Strangler Fig dikeyi ──
services.AddSingleton<ISqlIdentifierGuard, SqlIdentifierGuard>();      // stateless, thread-safe
services.AddScoped<IEngineCatalogRepository, SqlEngineCatalogRepository>();
services.AddScoped<IEngineSchemaCache, EngineSchemaCache>();           // IMemoryCache kullanır
services.AddScoped<IDynamicDdlService, DynamicDdlService>();
services.AddScoped<IEngineSaveService, EngineSaveService>();           // default save
services.AddScoped<IEngineFactory, EngineFactory>();
services.AddScoped<EngineRouter>();
// Motor-özel override örneği (opsiyonel):
// services.AddScoped<IKeyedEngineSaveService, StokMotoruSaveService>();

// IMemoryCache zaten kayıtlı değilse:
services.AddMemoryCache();
```

`SqlServerConnectionFactory` ve `IMemoryCache` zaten kayıtlı; yeni motor mevcut altyapıyı **yeniden kullanır**, paralel bir altyapı kurmaz.

### 4.5 Güvenlik & dayanıklılık kontrol listesi

| Risk | Önlem | Bölüm |
|------|-------|-------|
| SQL injection (kolon adı) | `ISqlIdentifierGuard` whitelist regex + bracket-escape + rezerve kelime reddi | §2.3 |
| SQL injection (değer) | Tüm değerler Dapper `DynamicParameters` / `SqlParameter` | §3.4 |
| Şema dışı/yanlış kolon | `EnginePayloadValidator` whitelist; bilinmeyen key reddedilir | §3.3 |
| Audit/PK override | `ReservedColumns` + `IsSystem` payload'dan dışlanır; `Created*` kod set eder | §3.3-3.4 |
| Yanlış tabloyu ALTER etme | Hedef yalnızca `Sys_Entities`'teki kayıtlı `engine.*`; kullanıcı hedef seçemez | §2.5 |
| Kısmi durum (metaveri var, kolon yok) | Metaveri + DDL tek transaction; hata → rollback | §2.5 |
| Çift alan ekleme | `IF COL_LENGTH(...) IS NULL` + katalog unique index → idempotent | §1.3, §2.5 |
| Bayat şema (DDL sonrası) | `IEngineSchemaCache.Invalidate` + `SchemaVersion` damgası | §2.6 |
| Mevcut sistemi bozma | Ayrı `engine` şeması + ayrı isim alanı + Router; `dbo.*`'a sıfır dokunuş | §0, §4.2 |
| Çok firma karışması | Cache key + bağlantı per-company (`SqlServerConnectionFactory`) | §2.6 |
| `NOT NULL` + dolu tablo | Zorunlu alan `DEFAULT` ile eklenir (mevcut satırlar ihlal etmez) | §2.5 |

### 4.6 Performans & ölçek notları

Fiziksel kolon yaklaşımı EAV'a kıyasla milyon-satır sorgularında pivot/self-join maliyetini ortadan kaldırır; `IsIndexed` alanlar gerçek B-tree index alır, `WHERE GlutenOrani > 2` sargable olur. Ölçek büyüdükçe değerlendirilecekler: (a) çok sık ALTER edilen motorlarda yoğun saatlerde DDL'i kuyruğa alma; (b) SQL Server'ın tablo başına 1024 kolon sınırına yaklaşan motorlar için "wide table" veya sparse column stratejisi; (c) okuma-yoğun BI senaryolarında `engine.*` üzerinde indexed view veya columnstore index. Bunlar v1 kapsamı dışıdır ama tasarım bunları engellemez.

### 4.7 Sonraki adımlar (öneri)

Bu taslak onaylanırsa sıralı uygulama: (1) `sql/engine/` altına `Sys_Entities`, `Sys_Fields`, `engine` şema + ilk motor (`stok_motoru` → `engine.Inv_Master`) migration'ları (`CalibraDatabaseInitializer` deseni); (2) Domain + Abstractions iskeleti; (3) `SqlIdentifierGuard` + birim testleri (injection saldırı vektörleri ile); (4) `DynamicDdlService` + transaction/idempotent testleri; (5) `EngineSaveService` + validator testleri; (6) `EngineRouter` ile bir pilot ekranı yeni motora taşıma. Her adım derlenip mevcut sistem bozulmadan canlıya alınabilir.

---

*Bu doküman bir mimari taslaktır; çalışan projeye hiçbir kod yazılmamıştır. Onay sonrası kod, yukarıdaki katman yerleşimine göre `src/` altına eklenir.*
