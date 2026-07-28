using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Auditing;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Services;

/// <summary>
/// Hata kodu kataloğu servisi — ActivityReasonService deseni: kod UI'dan alınmaz, isimden
/// türetilir; isim üzerinden uniqueness. Audit kaydı ekler.
/// </summary>
public sealed class QualityDefectCodeService : IQualityDefectCodeService
{
    private const string AuditEntity = "QualityDefectCode";
    private readonly IQualityDefectCodeRepository _repo;
    private readonly ILogger<QualityDefectCodeService> _logger;
    private readonly IAuditTrailService? _audit;

    public QualityDefectCodeService(IQualityDefectCodeRepository repo, ILogger<QualityDefectCodeService> logger, IAuditTrailService? audit = null)
    {
        _repo = repo;
        _logger = logger;
        _audit = audit;
    }

    public Task<IReadOnlyList<QualityDefectCodeDto>> ListAsync(byte? category, bool includeInactive, CancellationToken ct)
        => _repo.ListAsync(category, includeInactive, ct);

    public Task<QualityDefectCodeDto?> GetAsync(int id, CancellationToken ct) => _repo.GetAsync(id, ct);

    public async Task<(bool Ok, string? Error, int Id)> SaveAsync(SaveQualityDefectCodeRequest request, int? userId, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return (false, "Hata adı zorunludur.", 0);
        if (name.Length > 200) return (false, "Hata adı en fazla 200 karakter olabilir.", 0);
        if (!Enum.IsDefined(typeof(DefectCategory), request.Category)) return (false, "Geçersiz kategori.", 0);

        var id = request.Id ?? 0;
        // İsim uniqueness (aktif kayıtlar; kendisi hariç) — K6 kuralı
        var all = await _repo.ListAsync(null, includeInactive: false, ct);
        if (all.Any(x => x.Id != id && string.Equals(x.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase)))
            return (false, $"Aynı isimde başka bir hata kodu zaten tanımlı: '{name}'", 0);

        var code = id > 0
            ? (all.FirstOrDefault(x => x.Id == id)?.Code is { Length: > 0 } c ? c : DeriveCode(name))
            : DeriveCode(name);

        var entity = new QualityDefectCode
        {
            Id = id,
            Category = (DefectCategory)request.Category,
            Code = code,
            Name = name,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            ColorHex = string.IsNullOrWhiteSpace(request.ColorHex) ? null : request.ColorHex.Trim(),
            SortOrder = request.SortOrder,
            IsActive = request.IsActive,
            CreatedById = userId,
            UpdatedById = userId,
        };
        try { entity.EnsureValid(); }
        catch (Domain.Common.DomainException dex) { return (false, dex.Message, 0); }

        var savedId = await _repo.SaveAsync(entity, ct);
        if (id > 0) _audit?.LogUpdate(AuditEntity, savedId, name, null, entity);
        else _audit?.LogInsert(AuditEntity, savedId, name, snapshot: entity);
        return (true, null, savedId);
    }

    public async Task<(bool Ok, string? Error)> DeleteAsync(int id, int? userId, CancellationToken ct)
    {
        if (id <= 0) return (false, "Silinecek hata kodu seçilmelidir.");
        var existing = await _repo.GetAsync(id, ct);
        if (existing is null) return (false, "Hata kodu bulunamadı.");
        await _repo.SoftDeleteAsync(id, userId, ct);
        _audit?.LogDelete(AuditEntity, id, existing.Name);
        return (true, null);
    }

    private static string DeriveCode(string name)
    {
        var t = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(t)) t = "AUTO_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        return t.Length > 50 ? t[..50] : t;
    }
}
