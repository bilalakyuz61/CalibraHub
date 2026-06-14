using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Contracts;

/// <summary>
/// Tek bir entegrasyonun taşınabilir paketi (2026-05-21 — Faz 1 İçe/Dışa Aktar).
/// Hedef ortama JSON dosyası olarak yüklenir; ID'ler taşınmaz, referanslar Code/Name ile
/// resolve edilir.
///
/// DAHİL: Integration meta + Mappings + Triggers + Endpoint (URL+schema)
/// HARİÇ: ApiProfile credentials (auth token/password) — güvenlik, hedefte admin girer.
/// </summary>
public sealed record IntegrationBundleDto(
    int    SchemaVersion,          // 1 (gelecek değişikliklerde artar)
    DateTime ExportedAt,
    string? ExportedBy,            // user name (display only)
    string  Kind,                  // "single-integration"
    IntegrationBundleEntryDto Integration);

public sealed record IntegrationBundleEntryDto(
    // ── Integration aggregate ──
    string  Name,
    string? Description,
    string  SourceFormCode,
    string  ErrorBehavior,         // enum string ("Skip"/"Retry"/"Manual")
    int     RetryCount,
    bool    IsActive,
    string? PreProcedureName,
    string? PreProcedureParamsJson,
    string? PostProcedureName,
    string? PostProcedureParamsJson,

    // ── Endpoint (opsiyonel — TargetEndpointId NULL ise null) ──
    IntegrationBundleEndpointDto? Endpoint,

    // ── API Profile (Faz 1.5 — 2026-05-21): self-contained import için profile da
    //    bundle'a katıldı. Credentials YOK (kullanıcı tercihi 1) — admin hedefte
    //    bir kez elle girer, sonraki tüm aynı-profile import'larda otomatik eşleşir.
    IntegrationBundleApiProfileDto? ApiProfile,

    // ── Mappings (target path → source rule) ──
    IReadOnlyList<IntegrationBundleMappingDto> Mappings,

    // ── Triggers (manual/cron/event) ──
    IReadOnlyList<IntegrationBundleTriggerDto> Triggers);

public sealed record IntegrationBundleEndpointDto(
    string  Name,
    string  HttpMethod,
    string  UrlTemplate,
    string? BodySchema,
    string? Description,
    bool    IsActive,
    // ApiProfile lookup hint — hedefte aynı isimli/baseUrl'li profile aranır.
    // Yoksa Faz 1.5'te otomatik yaratılır (ApiProfile bundle alanı doluysa).
    string? ApiProfileName,
    string? ApiProfileBaseUrl,
    string? ApiProfileProviderCode);

/// <summary>
/// API Profile self-contained bundle parçası (2026-05-21 — Faz 1.5).
/// AuthType + BaseUrl + ProviderCode taşınır; AuthConfigJson (credentials)
/// **kasıtlı olarak null** — güvenlik gereği hiçbir ortamda export edilmez.
/// Hedef ortamda Profiller sayfasından admin bir kez doldurur, sonraki tüm
/// import'larda Name+BaseUrl eşleşmesi ile otomatik bağlanır.
/// </summary>
public sealed record IntegrationBundleApiProfileDto(
    string  Name,
    string  BaseUrl,
    string  AuthType,              // "None" / "BearerToken" / "BasicAuth" / "ApiKey" / ...
    string? ProviderCode,          // "Netsis" / "Logo" / "Custom" — provider catalog lookup için
    bool    IsActive);

public sealed record IntegrationBundleMappingDto(
    string  TargetPath,
    string? TargetDataType,
    string  SourceType,            // enum string
    string? SourceValue,
    string? LookupSourceField,
    string? DefaultValue,
    string? FormatPattern,
    bool    IsRequired,
    int     SortOrder,
    string? GroupKey,
    string  SourceSection,         // "Header" / "Lines" / "Combination"
    string? LookupFiltersJson,
    string? LookupParam,
    string? LookupReturnColumn);

public sealed record IntegrationBundleTriggerDto(
    string  TriggerType,           // enum string
    string? Config,
    bool    IsActive);

/// <summary>
/// Import isteği — bundle + çakışma stratejisi.
/// </summary>
public sealed record ImportIntegrationRequest(
    IntegrationBundleDto Bundle,
    /// <summary>"Overwrite" | "NewCopy" | "Skip"</summary>
    string ConflictStrategy = "NewCopy");

/// <summary>
/// Import sonuç raporu.
/// </summary>
public sealed record ImportIntegrationResultDto(
    bool   Success,
    int?   IntegrationId,          // başarılıysa yeni veya update edilen Integration.Id
    string Status,                 // "Created" | "Overwritten" | "Skipped" | "Failed"
    string? Message,
    IReadOnlyList<string> Warnings); // örn. "ApiProfile bulunamadı — endpoint orphan"
