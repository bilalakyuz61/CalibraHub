using System.Text.Json;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Web.Services;

/// <summary>
/// Startup'ta Resources/Integration/Seed/*.json dosyalarini DB'ye idempotent yukler.
/// - Provider yoksa ekler, varsa atlar (Code'a gore)
/// - Provider'in enum'lari yoksa ekler (Provider+Code unique)
/// - Provider'in field doc'lari yoksa ekler (Provider+Resource+FieldPath unique)
///
/// Mevcut data (admin tarafindan editlenen) ASLA dokunulmaz — sadece eksikleri ekler.
/// </summary>
public sealed class IntegrationDocCatalogSeedService : IHostedService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<IntegrationDocCatalogSeedService> _log;

    public IntegrationDocCatalogSeedService(
        IServiceScopeFactory scopes,
        IWebHostEnvironment env,
        ILogger<IntegrationDocCatalogSeedService> log)
    {
        _scopes = scopes;
        _env = env;
        _log = log;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        var dir = Path.Combine(_env.ContentRootPath, "Resources", "Integration", "Seed");
        if (!Directory.Exists(dir))
        {
            _log.LogInformation("[DocCatalogSeed] Seed dizini bulunamadi: {Dir}", dir);
            return;
        }

        var files = Directory.GetFiles(dir, "*.json");
        if (files.Length == 0)
        {
            _log.LogInformation("[DocCatalogSeed] Seed JSON dosyasi yok.");
            return;
        }

        using var scope = _scopes.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IIntegrationDocCatalogRepository>();

        int providersAdded = 0, enumsAdded = 0, valuesAdded = 0, fieldDocsAdded = 0;

        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file, ct);
                var doc  = JsonSerializer.Deserialize<SeedRoot>(json, _jsonOpts);
                if (doc?.Provider is null) continue;

                // Provider upsert (sadece yoksa)
                var existingProvider = await repo.GetProviderByCodeAsync(doc.Provider.Code, ct);
                IntegrationProvider provider;
                if (existingProvider is null)
                {
                    provider = new IntegrationProvider
                    {
                        Code = doc.Provider.Code,
                        Label = doc.Provider.Label,
                        Description = doc.Provider.Description,
                        SourceInfo = doc.Provider.SourceInfo,
                        IconColor = doc.Provider.IconColor,
                        SortOrder = doc.Provider.SortOrder,
                        IsActive = true,
                    };
                    provider.Id = await repo.UpsertProviderAsync(provider, "system-seed", ct);
                    providersAdded++;
                }
                else provider = existingProvider;

                // Enums (sadece yoksa, code bazli)
                var existingEnums = (await repo.ListEnumsAsync(provider.Id, includeInactive: true, ct))
                    .ToDictionary(e => e.Code, StringComparer.OrdinalIgnoreCase);
                var enumIdMap = existingEnums.ToDictionary(kv => kv.Key, kv => kv.Value.Id, StringComparer.OrdinalIgnoreCase);

                foreach (var e in doc.Enums ?? Array.Empty<SeedEnum>())
                {
                    if (existingEnums.ContainsKey(e.Code)) continue;
                    var entity = new IntegrationEnumDefinition
                    {
                        ProviderId = provider.Id,
                        Code = e.Code,
                        Label = e.Label ?? e.Code,
                        Description = e.Description,
                        SourceInfo = e.SourceInfo,
                        IsActive = true,
                        Values = (e.Values ?? Array.Empty<SeedEnumValue>())
                            .Select((v, i) => new IntegrationEnumValue
                            {
                                Value = v.Value,
                                Label = v.Label ?? v.Value,
                                TechnicalCode = v.TechnicalCode,
                                Description = v.Description,
                                SortOrder = v.SortOrder > 0 ? v.SortOrder : (i + 1) * 10,
                            }).ToList(),
                    };
                    var newId = await repo.UpsertEnumAsync(entity, "system-seed", ct);
                    enumIdMap[e.Code] = newId;
                    enumsAdded++;
                    valuesAdded += entity.Values.Count;
                }

                // FieldDocs (sadece yoksa, Provider+Resource+FieldPath bazli)
                var existingFds = (await repo.ListFieldDocsAsync(provider.Id, null, includeInactive: true, ct))
                    .Select(d => $"{d.Resource}::{d.FieldPath}")
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var f in doc.FieldDocs ?? Array.Empty<SeedFieldDoc>())
                {
                    var key = $"{f.Resource}::{f.FieldPath}";
                    if (existingFds.Contains(key)) continue;

                    int? enumId = null;
                    if (!string.IsNullOrWhiteSpace(f.EnumCode) && enumIdMap.TryGetValue(f.EnumCode, out var eid))
                        enumId = eid;

                    var entity = new IntegrationFieldDoc
                    {
                        ProviderId = provider.Id,
                        Resource = f.Resource,
                        FieldPath = f.FieldPath,
                        Label = f.Label,
                        Description = f.Description,
                        Example = f.Example,
                        Notes = f.Notes,
                        EnumDefinitionId = enumId,
                        IsRequired = f.IsRequired,
                        SortOrder = f.SortOrder,
                        IsActive = true,
                    };
                    await repo.UpsertFieldDocAsync(entity, "system-seed", ct);
                    fieldDocsAdded++;
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "[DocCatalogSeed] {File} yuklenirken hata", Path.GetFileName(file));
            }
        }

        Console.WriteLine($"[DB INIT] IntegrationDocCatalogSeed tamamlandi. " +
            $"+{providersAdded} provider, +{enumsAdded} enum (+{valuesAdded} deger), +{fieldDocsAdded} field-doc.");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ── JSON shape ────────────────────────────────────────────────────────
    private sealed class SeedRoot
    {
        public SeedProvider? Provider { get; set; }
        public SeedEnum[]? Enums { get; set; }
        public SeedFieldDoc[]? FieldDocs { get; set; }
    }
    private sealed class SeedProvider
    {
        public string Code { get; set; } = "";
        public string Label { get; set; } = "";
        public string? Description { get; set; }
        public string? SourceInfo { get; set; }
        public string? IconColor { get; set; }
        public int SortOrder { get; set; }
    }
    private sealed class SeedEnum
    {
        public string Code { get; set; } = "";
        public string? Label { get; set; }
        public string? Description { get; set; }
        public string? SourceInfo { get; set; }
        public SeedEnumValue[]? Values { get; set; }
    }
    private sealed class SeedEnumValue
    {
        public string Value { get; set; } = "";
        public string? Label { get; set; }
        public string? TechnicalCode { get; set; }
        public string? Description { get; set; }
        public int SortOrder { get; set; }
    }
    private sealed class SeedFieldDoc
    {
        public string Resource { get; set; } = "";
        public string FieldPath { get; set; } = "";
        public string? Label { get; set; }
        public string? Description { get; set; }
        public string? Example { get; set; }
        public string? Notes { get; set; }
        public string? EnumCode { get; set; }
        public bool IsRequired { get; set; }
        public int SortOrder { get; set; }
    }
}
