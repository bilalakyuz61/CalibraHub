using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Services.Integration;

/// <summary>
/// Entegrasyon paket servisi — JSON bundle ile tek-tıkla dışa/içe aktarma.
/// 2026-05-21 — Faz 1 MVP. Multi-integration ZIP sonra eklenir.
///
/// Endpoint dahil, ApiProfile referansı by Name+BaseUrl (auth secrets hariç).
/// Çakışma: name match olursa stratejiye göre (Overwrite/NewCopy/Skip).
/// </summary>
public sealed class IntegrationBundleService : IIntegrationBundleService
{
    private const int CurrentSchemaVersion = 1;

    private readonly IIntegrationRepository _integrationRepo;
    private readonly IIntegrationApiProfileRepository _apiProfileRepo;

    public IntegrationBundleService(
        IIntegrationRepository integrationRepo,
        IIntegrationApiProfileRepository apiProfileRepo)
    {
        _integrationRepo = integrationRepo;
        _apiProfileRepo  = apiProfileRepo;
    }

    public async Task<IntegrationBundleDto> ExportAsync(int integrationId, string? exportedBy, CancellationToken ct)
    {
        var integration = await _integrationRepo.GetByIdAsync(integrationId, ct)
            ?? throw new ArgumentException($"Entegrasyon bulunamadı (Id={integrationId}).");

        // Endpoint dolu ise tam export (URL + body schema + api profile hint)
        IntegrationBundleEndpointDto? endpointDto = null;
        IntegrationBundleApiProfileDto? profileDto = null;
        if (integration.Endpoint is not null)
        {
            // API profile referansı — by Name + BaseUrl + ProviderCode (id taşımıyoruz)
            IntegrationApiProfile? profile = null;
            try { profile = await _apiProfileRepo.GetByIdAsync(integration.Endpoint.ApiProfileId, ct); }
            catch { /* profile yok — orphan, geri uyum */ }

            endpointDto = new IntegrationBundleEndpointDto(
                Name:                   integration.Endpoint.Name,
                HttpMethod:             integration.Endpoint.HttpMethod,
                UrlTemplate:            integration.Endpoint.UrlTemplate,
                BodySchema:             integration.Endpoint.BodySchema,
                Description:            integration.Endpoint.Description,
                IsActive:               integration.Endpoint.IsActive,
                ApiProfileName:         profile?.Name,
                ApiProfileBaseUrl:      profile?.BaseUrl,
                ApiProfileProviderCode: profile?.ProviderCode);

            // Faz 1.5: profile entity'sini de bundle'a ekle (credentials HARİÇ).
            // Hedef ortamda profile yoksa Import otomatik yaratır.
            if (profile is not null)
            {
                profileDto = new IntegrationBundleApiProfileDto(
                    Name:         profile.Name,
                    BaseUrl:      profile.BaseUrl,
                    AuthType:     profile.AuthType,
                    ProviderCode: profile.ProviderCode,
                    IsActive:     profile.IsActive);
            }
        }

        var mappingDtos = integration.Mappings.Select(m => new IntegrationBundleMappingDto(
            TargetPath:         m.TargetPath,
            TargetDataType:     m.TargetDataType,
            SourceType:         m.SourceType.ToString(),
            SourceValue:        m.SourceValue,
            LookupSourceField:  m.LookupSourceField,
            DefaultValue:       m.DefaultValue,
            FormatPattern:      m.FormatPattern,
            IsRequired:         m.IsRequired,
            SortOrder:          m.SortOrder,
            GroupKey:           m.GroupKey,
            SourceSection:      m.SourceSection ?? "Header",
            LookupFiltersJson:  m.LookupFiltersJson,
            LookupParam:        m.LookupParam,
            LookupReturnColumn: m.LookupReturnColumn)).ToList();

        var triggerDtos = integration.Triggers.Select(t => new IntegrationBundleTriggerDto(
            TriggerType: t.TriggerType.ToString(),
            Config:      t.Config,
            IsActive:    t.IsActive)).ToList();

        var entry = new IntegrationBundleEntryDto(
            Name:                    integration.Name,
            Description:             integration.Description,
            SourceFormCode:          integration.SourceFormCode,
            ErrorBehavior:           integration.ErrorBehavior.ToString(),
            RetryCount:              integration.RetryCount,
            IsActive:                integration.IsActive,
            PreProcedureName:        integration.PreProcedureName,
            PreProcedureParamsJson:  integration.PreProcedureParamsJson,
            PostProcedureName:       integration.PostProcedureName,
            PostProcedureParamsJson: integration.PostProcedureParamsJson,
            Endpoint:                endpointDto,
            ApiProfile:              profileDto,    // 2026-05-21 Faz 1.5
            Mappings:                mappingDtos,
            Triggers:                triggerDtos);

        return new IntegrationBundleDto(
            SchemaVersion: CurrentSchemaVersion,
            ExportedAt:    DateTime.UtcNow,
            ExportedBy:    exportedBy,
            Kind:          "single-integration",
            Integration:   entry);
    }

    public async Task<ImportIntegrationResultDto> ImportAsync(ImportIntegrationRequest request, string? actor, CancellationToken ct)
    {
        if (request?.Bundle is null)
            return Fail("Boş bundle.");
        if (request.Bundle.SchemaVersion > CurrentSchemaVersion)
            return Fail($"Bundle şema versiyonu desteklenmiyor ({request.Bundle.SchemaVersion}). En fazla {CurrentSchemaVersion} desteklenir.");
        if (!string.Equals(request.Bundle.Kind, "single-integration", StringComparison.OrdinalIgnoreCase))
            return Fail($"Bundle tipi desteklenmiyor: '{request.Bundle.Kind}'.");

        var entry = request.Bundle.Integration
            ?? throw new ArgumentException("Bundle.Integration null.");
        var warnings = new List<string>();

        // 1) Name çakışması kontrolü → stratejiye göre davran
        var existing = (await _integrationRepo.ListAsync(includeInactive: true, ct))
            .FirstOrDefault(x => string.Equals(x.Name, entry.Name, StringComparison.OrdinalIgnoreCase));

        var strategy = (request.ConflictStrategy ?? "NewCopy").Trim();
        if (existing is not null && string.Equals(strategy, "Skip", StringComparison.OrdinalIgnoreCase))
        {
            return new ImportIntegrationResultDto(
                Success:        true,
                IntegrationId:  existing.Id,
                Status:         "Skipped",
                Message:        $"'{entry.Name}' zaten var — atlandı.",
                Warnings:       warnings);
        }

        // 2) Endpoint resolve / create
        int? endpointId = null;
        if (entry.Endpoint is not null)
        {
            // 2a) ApiProfile lookup: önce mevcut profile var mı?
            Guid? apiProfileId = await ResolveApiProfileIdAsync(entry.Endpoint, ct);

            // 2a-bis) Faz 1.5 (2026-05-21): mevcut yoksa VE bundle profile dolu ise OTOMATIK YARAT.
            // Credentials boş (AuthConfigJson=null) — admin Profiller sayfasından girer.
            // Sonraki tüm aynı-profile import'larda Name+BaseUrl ile zaten eşleşir.
            if (apiProfileId is null && entry.ApiProfile is not null)
            {
                // CompanyId = 1 — IntegrationsController pattern'i (single-tenant veya hardcoded).
                // Per-company DB zaten doğru tenant'a bağlı; CompanyId kolonu legacy.
                var newProfile = new IntegrationApiProfile
                {
                    CompanyId      = 1,
                    Name           = entry.ApiProfile.Name,
                    BaseUrl        = entry.ApiProfile.BaseUrl,
                    AuthType       = entry.ApiProfile.AuthType,
                    ProviderCode   = entry.ApiProfile.ProviderCode,
                    AuthConfigJson = null,      // Credentials kasıtlı boş — admin elle girer
                    IsActive       = entry.ApiProfile.IsActive,
                };
                await _apiProfileRepo.UpsertAsync(newProfile, ct);
                apiProfileId = newProfile.Id;
                warnings.Add($"Yeni API Profile oluşturuldu: '{newProfile.Name}'. ⚠️ Auth credentials boş — Profiller sayfasından admin elle doldurmalı.");
            }
            else if (apiProfileId is null)
            {
                warnings.Add($"API Profile bulunamadı ('{entry.Endpoint.ApiProfileName ?? "?"}' @ '{entry.Endpoint.ApiProfileBaseUrl ?? "?"}') ve bundle'da da profile bilgisi yok. Endpoint orphan — admin elle bağlamalı.");
            }

            // 2b) Endpoint upsert (aynı Name + URL + HttpMethod varsa kullan, yoksa oluştur)
            var allEndpoints = await _integrationRepo.ListEndpointsAsync(includeInactive: true, ct);
            var existingEp = allEndpoints.FirstOrDefault(e =>
                string.Equals(e.Name, entry.Endpoint.Name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.UrlTemplate, entry.Endpoint.UrlTemplate, StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.HttpMethod, entry.Endpoint.HttpMethod, StringComparison.OrdinalIgnoreCase));
            if (existingEp is not null)
            {
                endpointId = existingEp.Id;
            }
            else if (apiProfileId.HasValue)
            {
                var newEp = new IntegrationEndpoint
                {
                    ApiProfileId = apiProfileId.Value,
                    Name         = entry.Endpoint.Name,
                    HttpMethod   = entry.Endpoint.HttpMethod,
                    UrlTemplate  = entry.Endpoint.UrlTemplate,
                    BodySchema   = entry.Endpoint.BodySchema,
                    Description  = entry.Endpoint.Description,
                    IsActive     = entry.Endpoint.IsActive,
                    // actor is string (bundle import context); CreatedById remains null.
                };
                endpointId = await _integrationRepo.AddEndpointAsync(newEp, ct);
                warnings.Add($"Yeni endpoint oluşturuldu: {entry.Endpoint.Name}");
            }
            else
            {
                // ApiProfile yoksa endpoint oluşturulamaz → integration "Sadece Procedure" moduna düşer veya endpoint orphan
                warnings.Add("ApiProfile olmadan endpoint oluşturulamadı — integration endpoint'siz import edildi.");
            }
        }

        // 3) Integration entity inşa
        var newIntegration = new CalibraHub.Domain.Entities.Integration
        {
            Name                    = entry.Name,
            Description             = entry.Description,
            SourceFormCode          = entry.SourceFormCode,
            TargetEndpointId        = endpointId,
            ErrorBehavior           = Enum.TryParse<IntegrationErrorBehavior>(entry.ErrorBehavior, true, out var eb) ? eb : IntegrationErrorBehavior.Skip,
            RetryCount              = entry.RetryCount,
            IsActive                = entry.IsActive,
            PreProcedureName        = entry.PreProcedureName,
            PreProcedureParamsJson  = entry.PreProcedureParamsJson,
            PostProcedureName       = entry.PostProcedureName,
            PostProcedureParamsJson = entry.PostProcedureParamsJson,
            // actor is string (bundle import context); CreatedById remains null.
        };

        // 4) Conflict — Overwrite veya NewCopy
        int integrationId;
        string status;
        if (existing is not null && string.Equals(strategy, "Overwrite", StringComparison.OrdinalIgnoreCase))
        {
            // Mevcut kaydı güncelle (Id'sini koru)
            newIntegration = new CalibraHub.Domain.Entities.Integration
            {
                Id                      = existing.Id,
                Name                    = newIntegration.Name,
                Description             = newIntegration.Description,
                SourceFormCode          = newIntegration.SourceFormCode,
                TargetEndpointId        = newIntegration.TargetEndpointId,
                ErrorBehavior           = newIntegration.ErrorBehavior,
                RetryCount              = newIntegration.RetryCount,
                IsActive                = newIntegration.IsActive,
                PreProcedureName        = newIntegration.PreProcedureName,
                PreProcedureParamsJson  = newIntegration.PreProcedureParamsJson,
                PostProcedureName       = newIntegration.PostProcedureName,
                PostProcedureParamsJson = newIntegration.PostProcedureParamsJson,
                // actor is string (bundle import context); UpdatedById remains null.
                VersionNo               = existing.VersionNo + 1,
            };
            await _integrationRepo.UpdateAsync(newIntegration, ct);
            integrationId = existing.Id;
            status = "Overwritten";
        }
        else
        {
            // NewCopy (varsayılan): aynı isim varsa " (Kopya)" suffix
            if (existing is not null)
            {
                var baseName = entry.Name + " (Kopya)";
                var unique = baseName;
                var n = 2;
                var allNames = (await _integrationRepo.ListAsync(includeInactive: true, ct))
                    .Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                while (allNames.Contains(unique)) unique = $"{baseName} {n++}";
                newIntegration.Name = unique;
                warnings.Add($"Aynı isim mevcut — yeni kayıt '{unique}' olarak oluşturuldu.");
            }
            integrationId = await _integrationRepo.AddAsync(newIntegration, ct);
            status = "Created";
        }

        // 5) Mappings replace
        var mappings = entry.Mappings.Select(m => new IntegrationMapping
        {
            IntegrationId      = integrationId,
            TargetPath         = m.TargetPath,
            TargetDataType     = m.TargetDataType,
            SourceType         = Enum.TryParse<IntegrationSourceType>(m.SourceType, true, out var st) ? st : IntegrationSourceType.FormField,
            SourceValue        = m.SourceValue,
            LookupSourceField  = m.LookupSourceField,
            DefaultValue       = m.DefaultValue,
            FormatPattern      = m.FormatPattern,
            IsRequired         = m.IsRequired,
            SortOrder          = m.SortOrder,
            GroupKey           = m.GroupKey,
            SourceSection      = m.SourceSection ?? "Header",
            LookupFiltersJson  = m.LookupFiltersJson,
            LookupParam        = m.LookupParam,
            LookupReturnColumn = m.LookupReturnColumn,
        }).ToList();
        await _integrationRepo.ReplaceMappingsAsync(integrationId, mappings, ct);

        // 6) Triggers replace
        var triggers = entry.Triggers.Select(t => new IntegrationTrigger
        {
            IntegrationId = integrationId,
            TriggerType   = Enum.TryParse<IntegrationTriggerType>(t.TriggerType, true, out var tt) ? tt : IntegrationTriggerType.Manual,
            Config        = t.Config,
            IsActive      = t.IsActive,
        }).ToList();
        await _integrationRepo.ReplaceTriggersAsync(integrationId, triggers, ct);

        return new ImportIntegrationResultDto(
            Success:       true,
            IntegrationId: integrationId,
            Status:        status,
            Message:       $"{mappings.Count} mapping, {triggers.Count} trigger içeri alındı.",
            Warnings:      warnings);
    }

    private async Task<Guid?> ResolveApiProfileIdAsync(IntegrationBundleEndpointDto endpoint, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(endpoint.ApiProfileName) && string.IsNullOrWhiteSpace(endpoint.ApiProfileBaseUrl))
            return null;

        // CompanyId 1 — IntegrationsController.SaveEndpoint pattern'i (legacy single-tenant).
        // Per-company DB zaten doğru tenant'a bağlı; CompanyId kolonu artık decorative.
        var all = await _apiProfileRepo.GetByCompanyAsync(1, ct);
        if (all.Count == 0) return null;

        // 1) Name + BaseUrl tam eşleşme
        var match = all.FirstOrDefault(p =>
            string.Equals(p.Name, endpoint.ApiProfileName ?? "", StringComparison.OrdinalIgnoreCase)
            && string.Equals(p.BaseUrl, endpoint.ApiProfileBaseUrl ?? "", StringComparison.OrdinalIgnoreCase));
        if (match is not null) return match.Id;

        // 2) Yalnız Name
        match = all.FirstOrDefault(p =>
            !string.IsNullOrWhiteSpace(endpoint.ApiProfileName)
            && string.Equals(p.Name, endpoint.ApiProfileName, StringComparison.OrdinalIgnoreCase));
        if (match is not null) return match.Id;

        // 3) Yalnız BaseUrl + ProviderCode
        match = all.FirstOrDefault(p =>
            !string.IsNullOrWhiteSpace(endpoint.ApiProfileBaseUrl)
            && string.Equals(p.BaseUrl, endpoint.ApiProfileBaseUrl, StringComparison.OrdinalIgnoreCase));
        return match?.Id;
    }

    private static ImportIntegrationResultDto Fail(string msg) => new(
        Success:       false,
        IntegrationId: null,
        Status:        "Failed",
        Message:       msg,
        Warnings:      Array.Empty<string>());
}
