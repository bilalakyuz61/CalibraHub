using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using Microsoft.Extensions.Caching.Memory;
using System.Text.RegularExpressions;
using ContractColumn = CalibraHub.Application.Contracts.IntegrationLookupFunctionColumn;

namespace CalibraHub.Application.Services.Integration;

public sealed class IntegrationLookupFunctionAdminService : IIntegrationLookupFunctionAdminService
{
    private static readonly Regex SafeIdentifierRegex =
        new(@"^[A-Za-z_][A-Za-z0-9_]{0,63}$", RegexOptions.Compiled);
    // SqlFunctionName: optional schema prefix + ad. Orn. "dbo.fn_X" veya "fn_X".
    // Sadece harf/rakam/underscore + en fazla 1 nokta (schema separator). Quotes/space yok.
    private static readonly Regex SqlFunctionNameRegex =
        new(@"^([A-Za-z_][A-Za-z0-9_]{0,127}\.)?[A-Za-z_][A-Za-z0-9_]{0,127}$", RegexOptions.Compiled);
    private static readonly Regex CodeRegex =
        new(@"^[A-Z][A-Z0-9_]{1,39}$", RegexOptions.Compiled);

    private const string RegistryCacheKey = "integration.lookup-functions.v1";

    private readonly IIntegrationLookupFunctionDefinitionRepository _repo;
    private readonly IMemoryCache _cache;

    public IntegrationLookupFunctionAdminService(
        IIntegrationLookupFunctionDefinitionRepository repo,
        IMemoryCache cache)
    {
        _repo = repo;
        _cache = cache;
    }

    private void InvalidateCache() => _cache.Remove(RegistryCacheKey);

    public async Task<IReadOnlyCollection<IntegrationLookupFunctionAdminDto>> GetAllAsync(
        bool includeInactive, CancellationToken ct)
    {
        var defs = await _repo.GetAllAsync(includeInactive, ct);
        return defs.Select(ToDto).ToList();
    }

    public async Task<IntegrationLookupFunctionAdminDto?> GetByIdAsync(int id, CancellationToken ct)
    {
        var d = await _repo.GetByIdAsync(id, ct);
        return d is null ? null : ToDto(d);
    }

    public async Task<(bool Success, string? Error, int? Id)> CreateAsync(
        SaveIntegrationLookupFunctionRequest req, string? user, CancellationToken ct)
    {
        var (ok, err) = Validate(req);
        if (!ok) return (false, err, null);

        if (await _repo.CodeExistsAsync(req.Code.Trim().ToUpperInvariant(), null, ct))
            return (false, $"'{req.Code}' kodu zaten kullaniliyor.", null);

        var entity = MapToEntity(req, isNew: true);
        var id = await _repo.InsertAsync(entity, user, ct);
        InvalidateCache();
        return (true, null, id);
    }

    public async Task<(bool Success, string? Error)> UpdateAsync(
        SaveIntegrationLookupFunctionRequest req, string? user, CancellationToken ct)
    {
        if (!req.Id.HasValue || req.Id.Value <= 0) return (false, "Id eksik.");
        var (ok, err) = Validate(req);
        if (!ok) return (false, err);

        var existing = await _repo.GetByIdAsync(req.Id.Value, ct);
        if (existing is null) return (false, "Kayit bulunamadi.");

        if (await _repo.CodeExistsAsync(req.Code.Trim().ToUpperInvariant(), req.Id.Value, ct))
            return (false, $"'{req.Code}' kodu zaten baska bir fonksiyon tarafindan kullaniliyor.");

        var entity = MapToEntity(req, isNew: false);
        entity.Id = req.Id.Value;
        await _repo.UpdateAsync(entity, user, ct);
        InvalidateCache();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> DeleteAsync(int id, string? user, CancellationToken ct)
    {
        var existing = await _repo.GetByIdAsync(id, ct);
        if (existing is null) return (false, "Kayit bulunamadi.");
        await _repo.SoftDeleteAsync(id, user, ct);
        InvalidateCache();
        return (true, null);
    }

    public async Task<IReadOnlyCollection<AvailableDbFunctionDto>> ListAvailableDbFunctionsAsync(CancellationToken ct)
        => await _repo.ListAvailableFunctionsAsync(ct);

    // ── Yardimcilar ─────────────────────────────────────────────────────────

    private static (bool Ok, string? Error) Validate(SaveIntegrationLookupFunctionRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Code))   return (false, "Kod zorunlu.");
        if (string.IsNullOrWhiteSpace(req.Label))  return (false, "Etiket zorunlu.");

        if (!CodeRegex.IsMatch(req.Code.Trim()))
            return (false, "Kod buyuk harf + rakam + alt cizgi olabilir (2-40 karakter, ilk karakter harf). Orn. PERSONNEL");

        // 3 mod (oncelik: SqlFunctionName > SqlSnippet[LEGACY] > View+Key):
        //   1) SqlFunctionName dolu => yeni "SQL Fonksiyonu" modu (3-param standart imza)
        //   2) SqlSnippet dolu      => [LEGACY] eski serbest SELECT modu (yeni kayitlarda kullanilmaz)
        //   3) Aksi halde           => "View+Key" rehber modu (klasik)
        var hasSqlFn      = !string.IsNullOrWhiteSpace(req.SqlFunctionName);
        var hasSqlSnippet = !string.IsNullOrWhiteSpace(req.SqlSnippet);

        if (hasSqlFn)
        {
            if (!SqlFunctionNameRegex.IsMatch(req.SqlFunctionName!.Trim()))
                return (false, "SQL Fonksiyon adi gecersiz. Sadece harf/rakam/_, opsiyonel 'schema.' prefix. Orn: dbo.fn_GetBalance");
        }
        else if (hasSqlSnippet)
        {
            var secError = IntegrationSqlSecurity.ValidateSelectOnly(req.SqlSnippet!.Trim());
            if (secError is not null)
                return (false, $"SQL guvenlik hatasi: {secError}");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(req.ViewName))  return (false, "View adi zorunlu (veya SQL Fonksiyonu secin).");
            if (string.IsNullOrWhiteSpace(req.KeyColumn)) return (false, "Anahtar kolon zorunlu (veya SQL Fonksiyonu secin).");
            if (!SafeIdentifierRegex.IsMatch(req.ViewName.Trim()))
                return (false, "View adi gecersiz (sadece harf/rakam/_, ilk karakter harf).");
            if (!SafeIdentifierRegex.IsMatch(req.KeyColumn.Trim()))
                return (false, "Anahtar kolon adi gecersiz.");
        }

        var cols = req.Columns?.ToList() ?? new();
        if (cols.Count == 0)
            return (false, "En az bir donulebilir kolon eklenmeli.");

        foreach (var c in cols)
        {
            if (string.IsNullOrWhiteSpace(c.Column)) return (false, "Kolon adi bos olamaz.");
            if (string.IsNullOrWhiteSpace(c.Label))  return (false, "Kolon etiketi bos olamaz.");
            if (!SafeIdentifierRegex.IsMatch(c.Column.Trim()))
                return (false, $"Gecersiz kolon adi: '{c.Column}'");
        }

        var dupCols = cols.GroupBy(c => c.Column.Trim(), StringComparer.OrdinalIgnoreCase)
                          .Where(g => g.Count() > 1).ToList();
        if (dupCols.Count > 0)
            return (false, "Ayni kolon birden fazla kez tanimlanmis: " + string.Join(", ", dupCols.Select(g => g.Key)));

        return (true, null);
    }

    private static IntegrationLookupFunctionDefinition MapToEntity(
        SaveIntegrationLookupFunctionRequest req, bool isNew)
    {
        var hasSqlFn      = !string.IsNullOrWhiteSpace(req.SqlFunctionName);
        var hasSqlSnippet = !string.IsNullOrWhiteSpace(req.SqlSnippet);
        var isAnySqlMode  = hasSqlFn || hasSqlSnippet;
        return new IntegrationLookupFunctionDefinition
        {
            Code = req.Code.Trim().ToUpperInvariant(),
            Label = req.Label.Trim(),
            Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
            ViewName    = isAnySqlMode ? null : req.ViewName?.Trim(),
            KeyColumn   = isAnySqlMode ? null : req.KeyColumn?.Trim(),
            SqlSnippet  = (!hasSqlFn && hasSqlSnippet) ? req.SqlSnippet!.Trim() : null,
            SqlFunctionName = hasSqlFn ? req.SqlFunctionName!.Trim() : null,
            SortOrder = req.SortOrder,
            IsActive = req.IsActive,
            Columns = (req.Columns ?? Array.Empty<ContractColumn>())
                .Select((c, i) => new Domain.Entities.IntegrationLookupFunctionColumn
                {
                    Column = c.Column.Trim(),
                    Label = c.Label.Trim(),
                    SortOrder = (i + 1) * 10,
                })
                .ToList(),
        };
    }

    private static IntegrationLookupFunctionAdminDto ToDto(IntegrationLookupFunctionDefinition d) =>
        new(
            Id: d.Id,
            Code: d.Code,
            Label: d.Label,
            Description: d.Description,
            ViewName: d.ViewName,
            KeyColumn: d.KeyColumn,
            SqlSnippet: d.SqlSnippet,
            SqlFunctionName: d.SqlFunctionName,
            SortOrder: d.SortOrder,
            IsActive: d.IsActive,
            Columns: d.Columns.OrderBy(c => c.SortOrder)
                .Select(c => new ContractColumn(c.Column, c.Label))
                .ToList());
}
