using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Contracts;

/// <summary>
/// SmartBoard liste sayfasinin tek bir kart icin gosterdigi minimal bilgi.
/// Aggregate yuklenmez (mappings/triggers ayri sorgu) — performance.
/// </summary>
public sealed record IntegrationListItemDto(
    int Id,
    string Name,
    string? Description,
    string SourceFormCode,
    string? SourceFormLabel,           // Forms tablosundan join — kullaniciya "Satis Teklifi" gibi
    int? TargetEndpointId,             // Faz O — NULL = sadece prosedur modu
    string EndpointName,
    string ApiProfileName,
    string ErrorBehavior,
    bool IsActive,
    int VersionNo,
    int TriggerCount,                  // kac aktif trigger var (Manual/Cron/...)
    DateTime Created,
    DateTime? Updated,
    int RunCount,                      // toplam IntegrationRun sayisi (audit ozeti)
    DateTime? LastRunAt,               // son run zamani
    string? LastRunStatus);            // son run durumu (Success/Failed/...)

/// <summary>
/// Wizard'in edit modunda yuklenen tam aggregate. Yeni kayitta null doner.
/// Mappings + Triggers + Endpoint sirasiyla nested.
/// </summary>
public sealed record IntegrationDetailDto(
    int Id,
    string Name,
    string? Description,
    string SourceFormCode,
    int? TargetEndpointId,                // Faz O — NULL = "Sadece Prosedur" modu
    IntegrationErrorBehavior ErrorBehavior,
    int RetryCount,
    bool IsActive,
    int VersionNo,
    IReadOnlyList<IntegrationMappingDto> Mappings,
    IReadOnlyList<IntegrationTriggerDto> Triggers,
    IntegrationEndpointDto? Endpoint,
    string? PreProcedureName = null,
    string? PreProcedureParamsJson = null,
    string? PostProcedureName = null,
    string? PostProcedureParamsJson = null,
    // 2026-05-22 Pre-flight Filter — JSON kural listesi (bkz. Integration.SourceFilterJson)
    string? SourceFilterJson = null,
    // 2026-05-22 Cascade target flag — Wizard Step 2 dropdown'ında görünür mü? Default true.
    bool AllowAsCascadeTarget = true,
    // Kod bazlı cascade: bu integration cascade hedefi olarak CODE ile çağrıldığında
    // hangi kolona göre entity bulunacak (orn. "CariKod", "StokKodu"). NULL = ID bazlı (default).
    string? SourceCodeColumn = null);

public sealed record IntegrationMappingDto(
    int Id,
    string TargetPath,
    string? TargetDataType,
    IntegrationSourceType SourceType,
    string? SourceValue,
    string? LookupSourceField,
    string? DefaultValue,
    string? FormatPattern,
    bool IsRequired,
    int SortOrder,
    string? GroupKey,
    string SourceSection = "Header",       // "Header" | "Lines" | "Combination"
    string? LookupFiltersJson = null,      // Lookup için çoklu WHERE filtre (GuideConstraint[] JSON)
    string? LookupReturnColumn = null,     // Lookup için hangi guide kolonu döner (null = DisplayColumn)
    string? LookupParam = null,            // SourceType=Function + SqlFn modu icin manuel @P3
    int? CascadeToIntegrationId = null,    // 2026-05-22: FK alanı için cascade hedef integration (null = cascade yok)
    bool CascadeByValue = false);          // Değer bazlı cascade: alan değerini (kod) ID'ye çevirip cascade et

public sealed record IntegrationTriggerDto(
    int Id,
    IntegrationTriggerType TriggerType,
    string? Config,                    // JSON string
    bool IsActive);

public sealed record IntegrationEndpointDto(
    int Id,
    Guid ApiProfileId,
    string ApiProfileName,
    string Name,
    string HttpMethod,
    string UrlTemplate,
    string? BodySchema,
    string? Description,
    bool IsActive);

/// <summary>
/// Wizard kaydet (Create veya Update) — tum aggregate tek POST'ta gelir.
/// Id=0 ise yeni; >0 ise update.
/// </summary>
public sealed record SaveIntegrationRequest(
    int Id,
    string Name,
    string? Description,
    string SourceFormCode,
    int? TargetEndpointId,                // Faz O — NULL = "Sadece Prosedur" modu
    IntegrationErrorBehavior ErrorBehavior,
    int RetryCount,
    bool IsActive,
    IReadOnlyList<SaveIntegrationMappingDto> Mappings,
    IReadOnlyList<SaveIntegrationTriggerDto> Triggers,
    string? PreProcedureName = null,
    string? PreProcedureParamsJson = null,
    string? PostProcedureName = null,
    string? PostProcedureParamsJson = null,
    // 2026-05-22 Pre-flight Filter — JSON kural listesi
    string? SourceFilterJson = null,
    // 2026-05-22 Cascade target flag — bu integration başka biri tarafından cascade edilebilir mi
    bool AllowAsCascadeTarget = true,
    // Kod bazlı cascade: NULL = ID bazlı (default). "CariKod" gibi set edilirse kod→ID çevirimi yapılır.
    string? SourceCodeColumn = null);

public sealed record SaveIntegrationMappingDto(
    string TargetPath,
    string? TargetDataType,
    IntegrationSourceType SourceType,
    string? SourceValue,
    string? LookupSourceField,
    string? DefaultValue,
    string? FormatPattern,
    bool IsRequired,
    int SortOrder,
    string? GroupKey,
    string SourceSection = "Header",       // "Header" | "Lines" | "Combination"
    string? LookupFiltersJson = null,      // Lookup çoklu WHERE filtre (GuideConstraint[] JSON)
    string? LookupReturnColumn = null,     // Lookup hangi guide kolonu döner
    string? LookupParam = null,            // SourceType=Function + SqlFn modu icin manuel @P3
    int? CascadeToIntegrationId = null,    // 2026-05-22: FK alanı için cascade hedef integration ID'si
    bool CascadeByValue = false);          // Değer bazlı cascade: alan değerini (kod) ID'ye çevirip cascade et

public sealed record SaveIntegrationTriggerDto(
    IntegrationTriggerType TriggerType,
    string? Config,
    bool IsActive);

/// <summary>
/// Wizard Step 4: dry-run test isteği. Mapping uygulanmis JSON output'u doner,
/// opsiyonel olarak gercekten endpoint'e gonderir (sendForReal=true).
/// 2026-05-25: OverrideRequestBody — kullanici Step 4'te body'yi duzenleyip
/// gercek istegi o body ile gonderebilir. Bos ise mapping ciktisi kullanilir.
/// </summary>
public sealed record TestIntegrationRequest(
    SaveIntegrationRequest Integration,
    string? SampleRecordId,
    bool SendForReal,
    string? OverrideRequestBody = null);

public sealed record TestIntegrationResponse(
    bool Success,
    string? RequestBody,           // mapping ciktisi (JSON)
    int? HttpStatusCode,            // sendForReal=true ise
    string? ResponseBody,           // sendForReal=true ise
    string? ErrorMessage,
    IReadOnlyList<string> ValidationWarnings);   // eksik zorunlu alan vb.

/// <summary>
/// Endpoint admin create/update isteği (inline modal veya admin sayfası).
/// Id=0 ise yeni, >0 ise update.
/// </summary>
public sealed record SaveIntegrationEndpointRequest(
    int Id,
    Guid ApiProfileId,
    string Name,
    string HttpMethod,
    string UrlTemplate,
    string? BodySchema,
    string? Description,
    bool IsActive);

/// <summary>
/// Endpoint katalogu toplu import (CSV seed) request'i.
/// Profile ya mevcut bir id ile ya da NewProfileName + NewProfileBaseUrl ile
/// yeni yaratilarak verilir. CSV format: NetsisRestEndpoints.csv standardi
/// (Resource,Method,HttpMethod,UrlTemplate,InputType,ReturnType — header opsiyonel).
/// Idempotent: ayni (HttpMethod + UrlTemplate) varsa skip.
/// </summary>
public sealed record BulkImportEndpointsRequest(
    Guid? ApiProfileId,
    string? NewProfileName,
    string? NewProfileBaseUrl,
    string CsvText);

/// <summary>
/// Wizard Step 1 list endpoint'i için endpoint kataloğu (gruplanmis).
/// </summary>
public sealed record IntegrationEndpointListItemDto(
    int Id,
    Guid ApiProfileId,
    string ApiProfileName,
    string Name,
    string HttpMethod,
    string UrlTemplate,
    bool IsActive);

/// <summary>
/// IntegrationRun audit log SmartBoard listesi için tek satir.
/// </summary>
public sealed record IntegrationRunListItemDto(
    long Id,
    int IntegrationId,
    string IntegrationName,
    IntegrationTriggerType TriggerType,
    string? SourceRecordId,
    DateTime StartedAt,
    int? DurationMs,
    IntegrationRunStatus Status,
    int? HttpStatusCode,
    string? ErrorMessage,
    string? TriggeredBy,
    long? ParentRunId = null);   // 2026-05-22: cascade parent — null = top-level
