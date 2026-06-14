using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Services;

public sealed class OperationService : IOperationService
{
    private readonly IOperationRepository _repo;

    public OperationService(IOperationRepository repo) => _repo = repo;

    public async Task<IReadOnlyCollection<OperationDto>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var list = await _repo.ListAsync(includeInactive, ct);
        return list.Select(Map).ToArray();
    }

    public async Task<OperationDto?> GetAsync(int id, CancellationToken ct)
    {
        var e = await _repo.GetAsync(id, ct);
        return e is null ? null : Map(e);
    }

    public async Task<int> SaveAsync(SaveOperationRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Operasyon adı zorunlu.", nameof(request.Name));
        if (request.StandardDuration.HasValue && request.StandardDuration < 0)
            throw new ArgumentException("Standart süre negatif olamaz.", nameof(request.StandardDuration));
        if (request.HourlyRate.HasValue && request.HourlyRate < 0)
            throw new ArgumentException("Saatlik ücret negatif olamaz.", nameof(request.HourlyRate));

        var name = request.Name.Trim();

        // K6 (CLAUDE.md): "Kullanıcı tarafından girilen kod alanı yok" — UI'dan Kod
        // alınmaz, isim üzerinden uniqueness + code auto-derive.
        var all = await _repo.ListAsync(includeInactive: true, ct);
        if (all.Any(o => o.Id != request.Id &&
                         string.Equals(o.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"Aynı isimde başka bir operasyon zaten tanımlı: '{name}'");
        }

        // Code DB'de var ama UI gostermez — auto-turetilir (mevcut record'sa onun kodunu koru)
        string code;
        if (request.Id > 0)
        {
            var existing = all.FirstOrDefault(o => o.Id == request.Id);
            code = !string.IsNullOrWhiteSpace(existing?.Code) ? existing!.Code : DeriveCode(name);
        }
        else
        {
            code = DeriveCode(name);
        }

        var entity = new Operation
        {
            Id = request.Id,
            Code = code,
            Name = name,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            StandardDuration = request.StandardDuration,
            DurationUnit = request.DurationUnit,
            HourlyRate = request.HourlyRate,
            SortOrder = request.SortOrder,
            IsActive = request.IsActive,
        };
        return await _repo.UpsertAsync(entity, ct);
    }

    // Backward-compat: Code DB'de var ama UI'dan kaldirildi (K6).
    // Yeni kayit icin name'den turet (50 char ile sinirla — NVARCHAR(50)).
    private static string DeriveCode(string name)
    {
        var t = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(t)) t = "AUTO_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        return t.Length > 50 ? t[..50] : t;
    }

    public Task DeleteAsync(int id, CancellationToken ct) => _repo.DeleteAsync(id, ct);

    private static OperationDto Map(Operation e) =>
        new(e.Id, e.Code, e.Name, e.Description, e.StandardDuration, e.DurationUnit,
            e.HourlyRate, e.SortOrder, e.IsActive, e.Created, e.Updated);
}
