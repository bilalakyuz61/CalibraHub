using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Common;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Services;

public sealed class ActivityReasonService : IActivityReasonService
{
    private readonly IActivityReasonRepository _repo;

    public ActivityReasonService(IActivityReasonRepository repo) => _repo = repo;

    public Task<IReadOnlyList<ActivityReasonDto>> ListAsync(
        WorkOrderActivityType? activityType, bool includeInactive, CancellationToken ct)
        => _repo.ListAsync(activityType, includeInactive, ct);

    public Task<ActivityReasonDto?> GetAsync(int id, CancellationToken ct) => _repo.GetAsync(id, ct);

    public async Task<int> SaveAsync(SaveActivityReasonRequest request, int? userId, CancellationToken ct)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Sebep adı zorunlu.", nameof(request.Name));

        var name = request.Name.Trim();
        var id   = request.Id ?? 0;

        // K6 (CLAUDE.md): "Kullanıcı tarafından girilen kod alanı yok" — UI'dan Kod
        // alınmaz, aynı ActivityType altında isim üzerinden uniqueness + code auto-derive.
        var existing = await _repo.ListAsync(request.ActivityType, includeInactive: false, ct);
        var dupName = existing.FirstOrDefault(x =>
            string.Equals(x.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase)
            && x.Id != id);
        if (dupName is not null)
            throw new ArgumentException($"Aynı isimde başka bir aktivite sebebi zaten tanımlı: '{name}'");

        // Code DB'de var ama UI gostermez — auto-turetilir (mevcut record'sa onun kodunu koru)
        string code;
        if (id > 0)
        {
            var current = existing.FirstOrDefault(x => x.Id == id);
            code = !string.IsNullOrWhiteSpace(current?.Code) ? current!.Code : DeriveCode(name);
        }
        else
        {
            code = DeriveCode(name);
        }

        var entity = new ActivityReason
        {
            Id           = id,
            ActivityType = request.ActivityType,
            Code         = code,
            Name         = name,
            Description  = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            ColorHex     = string.IsNullOrWhiteSpace(request.ColorHex)    ? null : request.ColorHex.Trim(),
            SortOrder    = request.SortOrder,
            IsActive     = request.IsActive,
            CreatedById  = userId,
            UpdatedById  = userId,
        };

        try { entity.EnsureValid(); }
        catch (DomainException dex) { throw new ArgumentException(dex.Message, dex); }

        return await _repo.SaveAsync(entity, ct);
    }

    // Backward-compat: Code DB'de var ama UI'dan kaldirildi (K6).
    // Yeni kayit icin name'den turet (50 char ile sinirla — NVARCHAR(50)).
    private static string DeriveCode(string name)
    {
        var t = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(t)) t = "AUTO_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        return t.Length > 50 ? t[..50] : t;
    }

    public Task DeleteAsync(int id, int? userId, CancellationToken ct)
    {
        if (id <= 0) throw new ArgumentException("Silinecek sebep seçilmelidir.");
        return _repo.DeleteAsync(id, userId, ct);
    }
}
