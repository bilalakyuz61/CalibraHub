using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Auditing;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Services;

public sealed class PersonnelService : IPersonnelService
{
    private readonly IPersonnelRepository _repo;
    private readonly IAuditTrailService? _audit;

    public PersonnelService(IPersonnelRepository repo, IAuditTrailService? audit = null)
    {
        _repo = repo;
        _audit = audit;
    }

    public Task<IReadOnlyCollection<PersonnelDto>> ListAsync(bool includeInactive, bool onlyOperators, CancellationToken ct)
        => _repo.ListAsync(includeInactive, onlyOperators, ct);

    public Task<PersonnelDto?> GetAsync(int id, CancellationToken ct) => _repo.GetAsync(id, ct);

    public async Task<int> SaveAsync(SavePersonnelRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.FullName))
            throw new ArgumentException("Tam ad zorunlu.", nameof(req.FullName));
        if (!string.IsNullOrWhiteSpace(req.PinCode) && req.PinCode.Trim().Length is < 4 or > 10)
            throw new ArgumentException("PIN 4-10 hane olmalı.", nameof(req.PinCode));

        var fullName = req.FullName.Trim();

        // Ayni isimli personel kontrolu (kendisi haric)
        var all = await _repo.ListAsync(includeInactive: true, onlyOperators: false, ct);
        if (all.Any(p => p.Id != req.Id &&
                         string.Equals(p.FullName?.Trim(), fullName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"Aynı isimde başka bir personel zaten tanımlı: '{fullName}'");
        }

        // Code DB'de var ama UI gostermez — auto-turetilir (mevcut record'sa onun kodunu koru)
        var existing = req.Id > 0 ? all.FirstOrDefault(p => p.Id == req.Id) : null;
        var code = req.Id > 0
            ? (!string.IsNullOrWhiteSpace(existing?.Code) ? existing!.Code : DeriveCode(fullName))
            : DeriveCode(fullName);

        var entity = new Personnel
        {
            Id = req.Id,
            Code = code,
            FullName = fullName,
            Title = string.IsNullOrWhiteSpace(req.Title) ? null : req.Title.Trim(),
            Department = string.IsNullOrWhiteSpace(req.Department) ? null : req.Department.Trim(),
            PinCode = string.IsNullOrWhiteSpace(req.PinCode) ? null : req.PinCode.Trim(),
            CardNo = string.IsNullOrWhiteSpace(req.CardNo) ? null : req.CardNo.Trim(),
            IsProductionOperator = req.IsProductionOperator,
            IsActive = req.IsActive,
            UserId = req.UserId,
            LocationId = req.LocationId,
            Phone = string.IsNullOrWhiteSpace(req.Phone) ? null : req.Phone.Trim(),
            Email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim(),
            Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim(),
            BirthDate = req.BirthDate,
        };
        var savedId = await _repo.SaveAsync(entity, ct);

        // İşlem logu — PIN değeri log dosyasına yazılmaz (ignore); değiştiyse maskeli işaretlenir
        if (_audit is not null)
        {
            try
            {
                if (existing is null)
                {
                    // İlk değer dökümü — PIN asla loglanmaz
                    _audit.LogInsert("Personnel", savedId, fullName,
                        snapshot: entity, snapshotIgnore: ["PinCode", "CompanyId", "Code"]);
                }
                else
                {
                    var changes = AuditDiff.Compute(existing, entity, "Personnel",
                        ignore: new[] { "PinCode", "CompanyId", "Code" });
                    if (!string.Equals(existing.PinCode ?? "", entity.PinCode ?? "", StringComparison.Ordinal))
                        changes.Add(new AuditFieldChange("PinCode", "PIN", null, "(değiştirildi)"));
                    _audit.LogChanges("Personnel", savedId, fullName, changes);
                }
            }
            catch { /* audit yazımı kaydı asla bozmaz */ }
        }
        return savedId;
    }

    // Backward-compat: Code DB'de var ama UI'dan kaldirildi.
    // Yeni kayit icin name'den turet (50 char ile sinirla).
    private static string DeriveCode(string name)
    {
        var t = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(t)) t = "AUTO_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        return t.Length > 50 ? t[..50] : t;
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        // İşlem logu için silinen personelin adını silmeden ÖNCE al (okunamazsa Id ile loglanır)
        string? fullName = null;
        if (_audit is not null)
        {
            try { fullName = (await _repo.GetAsync(id, ct))?.FullName; } catch { }
        }

        await _repo.DeleteAsync(id, ct);

        _audit?.LogDelete("Personnel", id, fullName ?? ("#" + id));
    }

    public Task<PersonnelDto?> GetByPinOrCardAsync(string? pinCode, string? cardNo, CancellationToken ct)
        => _repo.GetByPinOrCardAsync(pinCode, cardNo, ct);

    public Task<PersonnelDto?> GetByPinOrCardAsync(string? personnelCode, string? pinCode, string? cardNo, CancellationToken ct)
        => _repo.GetByPinOrCardAsync(personnelCode, pinCode, cardNo, ct);

    public Task<PersonnelDto?> GetByUserIdAsync(int userId, CancellationToken ct)
        => _repo.GetByUserIdAsync(userId, ct);
}
