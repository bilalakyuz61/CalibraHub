using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Services;

/// <summary>
/// Cari grup service — kullanici Kod girmez (CLAUDE.md "kod alani yok kurali").
/// Code Name'den auto-turetilir (truncate + unique fallback).
/// Uniqueness ad uzerinden (per-company).
/// </summary>
public sealed class CariGroupService : ICariGroupService
{
    private readonly ICariGroupRepository _repo;
    public CariGroupService(ICariGroupRepository repo) => _repo = repo;

    public async Task<IReadOnlyCollection<CariGroupDto>> GetAllAsync(CancellationToken ct)
    {
        var all = await _repo.GetAllAsync(ct);
        return all.Select(x => new CariGroupDto(x.Id, x.Code, x.Name, x.SortOrder, x.IsActive)).ToArray();
    }

    public async Task<CariGroupDto?> GetByIdAsync(int id, CancellationToken ct)
    {
        var e = await _repo.GetByIdAsync(id, ct);
        return e is null ? null : new CariGroupDto(e.Id, e.Code, e.Name, e.SortOrder, e.IsActive);
    }

    public async Task<(bool Success, string? Error, int? Id)> CreateAsync(CreateCariGroupRequest request, CancellationToken ct)
    {
        var name = request.Name?.Trim() ?? "";
        if (string.IsNullOrEmpty(name))
            return (false, "Grup adi bos olamaz.", null);

        // Ad uniqueness — per-company (repo zaten CompanyId'ye gore tarar)
        var all = await _repo.GetAllAsync(ct);
        if (all.Any(x => x.Name.Trim().Equals(name, StringComparison.OrdinalIgnoreCase)))
            return (false, $"Ayni isimde baska bir cari grup zaten tanimli: '{name}'", null);

        // Code auto-derive: Name'i 50 karaktere kirp; cakisma durumunda fallback
        var code = DeriveCode(name, all.Select(x => x.Code));

        var newId = await _repo.AddAsync(new CariGroup
        {
            Code = code,
            Name = name,
            SortOrder = request.SortOrder,
            IsActive = request.IsActive,
        }, ct);

        return (true, null, newId);
    }

    public async Task<(bool Success, string? Error)> UpdateAsync(UpdateCariGroupRequest request, CancellationToken ct)
    {
        var name = request.Name?.Trim() ?? "";
        if (string.IsNullOrEmpty(name))
            return (false, "Grup adi bos olamaz.");

        var existing = await _repo.GetByIdAsync(request.Id, ct);
        if (existing is null) return (false, "Cari grup bulunamadi.");

        var all = await _repo.GetAllAsync(ct);
        if (all.Any(x => x.Id != request.Id &&
                         x.Name.Trim().Equals(name, StringComparison.OrdinalIgnoreCase)))
            return (false, $"Ayni isimde baska bir cari grup zaten tanimli: '{name}'");

        // Update: Code'u koru (eski referanslar bozulmasin)
        var updated = new CariGroup
        {
            Id        = existing.Id,
            CompanyId = existing.CompanyId,
            Code      = existing.Code,
            Name      = name,
            SortOrder = request.SortOrder,
            IsActive  = request.IsActive,
            CreatedAt = existing.CreatedAt,
        };
        await _repo.UpdateAsync(updated, ct);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> DeleteAsync(int id, CancellationToken ct)
    {
        var existing = await _repo.GetByIdAsync(id, ct);
        if (existing is null) return (false, "Cari grup bulunamadi.");
        await _repo.DeleteAsync(id, ct);
        return (true, null);
    }

    private static string DeriveCode(string name, IEnumerable<string> existingCodes)
    {
        var slug = new string(name
            .Trim()
            .ToUpperInvariant()
            .Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == ' ')
            .ToArray())
            .Replace(' ', '_');

        if (slug.Length > 40) slug = slug[..40];
        if (string.IsNullOrEmpty(slug)) slug = "GRP";

        var taken = new HashSet<string>(existingCodes, StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(slug)) return slug;

        for (int i = 2; i < 1000; i++)
        {
            var candidate = $"{slug}_{i}";
            if (!taken.Contains(candidate)) return candidate;
        }
        return $"GRP_{Guid.NewGuid():N}"[..16];
    }
}
