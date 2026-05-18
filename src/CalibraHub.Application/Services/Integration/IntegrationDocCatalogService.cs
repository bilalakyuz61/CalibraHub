using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace CalibraHub.Application.Services.Integration;

public sealed class IntegrationDocCatalogService : IIntegrationDocCatalogService
{
    private const string CacheKeyPrefix = "intdoc:";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    // Singleton state — tum scope'lar ayni token'i paylasir
    private static CancellationTokenSource _cacheTokenSource = new();

    private readonly IIntegrationDocCatalogRepository _repo;
    private readonly IMemoryCache _cache;
    private readonly ILogger<IntegrationDocCatalogService> _log;

    public IntegrationDocCatalogService(
        IIntegrationDocCatalogRepository repo,
        IMemoryCache cache,
        ILogger<IntegrationDocCatalogService> log)
    {
        _repo = repo;
        _cache = cache;
        _log = log;
    }

    public void InvalidateCache()
    {
        // Mevcut token'i cancel et — tum cache entry'ler invalid olur
        var old = Interlocked.Exchange(ref _cacheTokenSource, new CancellationTokenSource());
        old.Cancel();
        old.Dispose();
    }

    // ── Wizard read (cache'li) ───────────────────────────────────────────

    public async Task<IReadOnlyDictionary<string, IntegrationFieldDocRuntimeDto>> GetFieldDocsAsync(
        string providerCode, string resource, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(providerCode) || string.IsNullOrWhiteSpace(resource))
            return new Dictionary<string, IntegrationFieldDocRuntimeDto>(0);

        var key = $"{CacheKeyPrefix}runtime:{providerCode}:{resource}";
        if (_cache.TryGetValue<IReadOnlyDictionary<string, IntegrationFieldDocRuntimeDto>>(key, out var cached) && cached is not null)
            return cached;

        var provider = await _repo.GetProviderByCodeAsync(providerCode, ct);
        if (provider is null)
            return new Dictionary<string, IntegrationFieldDocRuntimeDto>(0);

        var docs   = await _repo.ListFieldDocsAsync(provider.Id, resource, includeInactive: false, ct);
        var enums  = await _repo.ListEnumsAsync(provider.Id, includeInactive: false, ct);
        var enumMap = enums.ToDictionary(e => e.Id);

        var result = new Dictionary<string, IntegrationFieldDocRuntimeDto>(docs.Count, StringComparer.OrdinalIgnoreCase);

        // 1) LEGACY: FieldDoc kayitlari (eski model, geriye uyum — admin UI'dan eklenmez ama eski veri okunur)
        foreach (var d in docs)
        {
            IntegrationEnumRuntimeDto? enumDto = null;
            if (d.EnumDefinitionId.HasValue && enumMap.TryGetValue(d.EnumDefinitionId.Value, out var en))
            {
                enumDto = ToRuntimeEnum(en);
            }
            result[d.FieldPath] = new IntegrationFieldDocRuntimeDto(
                FieldPath: d.FieldPath,
                Label: d.Label,
                Description: d.Description,
                Example: d.Example,
                Notes: d.Notes,
                IsRequired: d.IsRequired,
                Enum: enumDto);
        }

        // 2) YENI MODEL: Enum'lardan sentez — her enum'un UsedInFieldPaths listesindeki her path icin
        //    tooltip ekle. FieldDoc'ta zaten varsa (legacy) skip — explicit record priority.
        foreach (var en in enums)
        {
            var paths = ParseFieldPaths(en.UsedInFieldPaths);
            if (paths.Count == 0) continue;
            var enumDto = ToRuntimeEnum(en);
            foreach (var path in paths)
            {
                if (result.ContainsKey(path)) continue;  // FieldDoc explicit priority
                result[path] = new IntegrationFieldDocRuntimeDto(
                    FieldPath: path,
                    Label: en.Label,           // enum label fall-back olarak field label
                    Description: en.Description,
                    Example: null,
                    Notes: null,
                    IsRequired: false,
                    Enum: enumDto);
            }
        }

        // CacheTtl + cancellation token (InvalidateCache ile reset edilebilir)
        var options = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(CacheTtl)
            .AddExpirationToken(new CancellationChangeToken(_cacheTokenSource.Token));
        _cache.Set(key, (IReadOnlyDictionary<string, IntegrationFieldDocRuntimeDto>)result, options);
        return result;
    }

    // ── Provider admin ───────────────────────────────────────────────────

    public async Task<IReadOnlyList<IntegrationProviderAdminDto>> ListProvidersAsync(bool includeInactive, CancellationToken ct)
    {
        var providers = await _repo.ListProvidersAsync(includeInactive, ct);
        var enums  = await _repo.ListEnumsAsync(null, includeInactive: false, ct);
        var fdocs  = await _repo.ListFieldDocsAsync(null, null, includeInactive: false, ct);
        var enumCnt = enums.GroupBy(e => e.ProviderId).ToDictionary(g => g.Key, g => g.Count());
        var fdocCnt = fdocs.GroupBy(d => d.ProviderId).ToDictionary(g => g.Key, g => g.Count());
        return providers.Select(p => new IntegrationProviderAdminDto(
            p.Id, p.Code, p.Label, p.Description, p.SourceInfo, p.IconColor, p.SortOrder, p.IsActive,
            enumCnt.GetValueOrDefault(p.Id, 0),
            fdocCnt.GetValueOrDefault(p.Id, 0))).ToList();
    }

    public async Task<int> SaveProviderAsync(SaveIntegrationProviderRequest req, string? actor, CancellationToken ct)
    {
        var entity = new IntegrationProvider
        {
            Id          = req.Id ?? 0,
            Code        = req.Code.Trim(),
            Label       = req.Label.Trim(),
            Description = req.Description,
            SourceInfo  = req.SourceInfo,
            IconColor   = req.IconColor,
            SortOrder   = req.SortOrder,
            IsActive    = req.IsActive,
        };
        var id = await _repo.UpsertProviderAsync(entity, actor, ct);
        InvalidateCache();
        return id;
    }

    public async Task DeleteProviderAsync(int id, string? actor, CancellationToken ct)
    {
        await _repo.DeleteProviderAsync(id, actor, ct);
        InvalidateCache();
    }

    // ── Enum admin ───────────────────────────────────────────────────────

    public async Task<IReadOnlyList<IntegrationEnumDefinitionAdminDto>> ListEnumsAsync(int? providerId, bool includeInactive, CancellationToken ct)
    {
        var enums = await _repo.ListEnumsAsync(providerId, includeInactive, ct);
        var providers = (await _repo.ListProvidersAsync(includeInactive: true, ct)).ToDictionary(p => p.Id);
        return enums.Select(e => new IntegrationEnumDefinitionAdminDto(
            e.Id, e.ProviderId,
            providers.TryGetValue(e.ProviderId, out var p) ? p.Code : "?",
            e.Code, e.Label, e.Description, e.SourceInfo, e.IsActive,
            e.Values.OrderBy(v => v.SortOrder)
                .Select(v => new IntegrationEnumValueDto(v.Id, v.Value, v.Label, v.TechnicalCode, v.Description, v.SortOrder))
                .ToList(),
            ParseFieldPaths(e.UsedInFieldPaths))).ToList();
    }

    public async Task<IntegrationEnumDefinitionAdminDto?> GetEnumAsync(int id, CancellationToken ct)
    {
        var e = await _repo.GetEnumByIdAsync(id, ct);
        if (e == null) return null;
        var p = await _repo.GetProviderByIdAsync(e.ProviderId, ct);
        return new IntegrationEnumDefinitionAdminDto(
            e.Id, e.ProviderId, p?.Code ?? "?", e.Code, e.Label, e.Description, e.SourceInfo, e.IsActive,
            e.Values.OrderBy(v => v.SortOrder)
                .Select(v => new IntegrationEnumValueDto(v.Id, v.Value, v.Label, v.TechnicalCode, v.Description, v.SortOrder))
                .ToList(),
            ParseFieldPaths(e.UsedInFieldPaths));
    }

    private static IntegrationEnumRuntimeDto ToRuntimeEnum(IntegrationEnumDefinition en) =>
        new(
            Code: en.Code,
            Label: en.Label,
            Description: en.Description,
            Values: en.Values
                .Where(v => v.IsActive)
                .OrderBy(v => v.SortOrder)
                .Select(v => new IntegrationEnumValueRuntimeDto(v.Value, v.Label, v.TechnicalCode, v.Description))
                .ToList());

    /// <summary>UsedInFieldPaths kolonu JSON array string olarak saklanir; parse hatasinda bos liste.</summary>
    private static IReadOnlyList<string> ParseFieldPaths(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            var arr = System.Text.Json.JsonSerializer.Deserialize<string[]>(json);
            return arr?.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray() ?? Array.Empty<string>();
        }
        catch { return Array.Empty<string>(); }
    }

    public async Task<int> SaveEnumAsync(SaveIntegrationEnumDefinitionRequest req, string? actor, CancellationToken ct)
    {
        var entity = new IntegrationEnumDefinition
        {
            Id          = req.Id ?? 0,
            ProviderId  = req.ProviderId,
            Code        = req.Code.Trim(),
            Label       = req.Label.Trim(),
            Description = req.Description,
            SourceInfo  = req.SourceInfo,
            IsActive    = req.IsActive,
            UsedInFieldPaths = (req.UsedInFieldPaths is { Count: > 0 })
                ? System.Text.Json.JsonSerializer.Serialize(
                    req.UsedInFieldPaths.Where(s => !string.IsNullOrWhiteSpace(s))
                                        .Select(s => s.Trim()).Distinct().ToArray())
                : null,
            Values      = (req.Values ?? Array.Empty<SaveIntegrationEnumValueRequest>())
                            .Select((v, i) => new IntegrationEnumValue
                            {
                                Value         = v.Value,
                                Label         = v.Label,
                                TechnicalCode = v.TechnicalCode,
                                Description   = v.Description,
                                SortOrder     = v.SortOrder > 0 ? v.SortOrder : (i + 1) * 10,
                            }).ToList(),
        };
        var id = await _repo.UpsertEnumAsync(entity, actor, ct);
        InvalidateCache();
        return id;
    }

    public async Task DeleteEnumAsync(int id, string? actor, CancellationToken ct)
    {
        await _repo.DeleteEnumAsync(id, actor, ct);
        InvalidateCache();
    }

    // ── Field Doc admin ──────────────────────────────────────────────────

    public async Task<IReadOnlyList<IntegrationFieldDocAdminDto>> ListFieldDocsAsync(int? providerId, string? resource, bool includeInactive, CancellationToken ct)
    {
        var docs = await _repo.ListFieldDocsAsync(providerId, resource, includeInactive, ct);
        var providers = (await _repo.ListProvidersAsync(includeInactive: true, ct)).ToDictionary(p => p.Id);
        var enums = (await _repo.ListEnumsAsync(providerId, includeInactive: true, ct)).ToDictionary(e => e.Id);
        return docs.Select(d => new IntegrationFieldDocAdminDto(
            d.Id, d.ProviderId,
            providers.TryGetValue(d.ProviderId, out var p) ? p.Code : "?",
            d.Resource, d.FieldPath, d.Label, d.Description, d.Example, d.Notes,
            d.EnumDefinitionId,
            d.EnumDefinitionId.HasValue && enums.TryGetValue(d.EnumDefinitionId.Value, out var en) ? en.Code : null,
            d.IsRequired, d.SortOrder, d.IsActive)).ToList();
    }

    public async Task<IntegrationFieldDocAdminDto?> GetFieldDocAsync(int id, CancellationToken ct)
    {
        var d = await _repo.GetFieldDocByIdAsync(id, ct);
        if (d == null) return null;
        var p = await _repo.GetProviderByIdAsync(d.ProviderId, ct);
        string? enumCode = null;
        if (d.EnumDefinitionId.HasValue)
        {
            var e = await _repo.GetEnumByIdAsync(d.EnumDefinitionId.Value, ct);
            enumCode = e?.Code;
        }
        return new IntegrationFieldDocAdminDto(d.Id, d.ProviderId, p?.Code ?? "?", d.Resource, d.FieldPath,
            d.Label, d.Description, d.Example, d.Notes, d.EnumDefinitionId, enumCode,
            d.IsRequired, d.SortOrder, d.IsActive);
    }

    public async Task<int> SaveFieldDocAsync(SaveIntegrationFieldDocRequest req, string? actor, CancellationToken ct)
    {
        var entity = new IntegrationFieldDoc
        {
            Id               = req.Id ?? 0,
            ProviderId       = req.ProviderId,
            Resource         = req.Resource.Trim(),
            FieldPath        = req.FieldPath.Trim(),
            Label            = req.Label,
            Description      = req.Description,
            Example          = req.Example,
            Notes            = req.Notes,
            EnumDefinitionId = req.EnumDefinitionId,
            IsRequired       = req.IsRequired,
            SortOrder        = req.SortOrder,
            IsActive         = req.IsActive,
        };
        var id = await _repo.UpsertFieldDocAsync(entity, actor, ct);
        InvalidateCache();
        return id;
    }

    public async Task DeleteFieldDocAsync(int id, string? actor, CancellationToken ct)
    {
        await _repo.DeleteFieldDocAsync(id, actor, ct);
        InvalidateCache();
    }
}
