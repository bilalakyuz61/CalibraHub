using System.Globalization;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Common;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Services;

public sealed class ShiftService : IShiftService
{
    private readonly IShiftRepository _repo;

    public ShiftService(IShiftRepository repo) => _repo = repo;

    public Task<IReadOnlyList<ShiftDto>> ListAsync(bool includeInactive, CancellationToken ct)
        => _repo.ListAsync(includeInactive, ct);

    public Task<ShiftDto?> GetAsync(int id, CancellationToken ct) => _repo.GetAsync(id, ct);

    public async Task<int> SaveAsync(SaveShiftRequest request, int? userId, CancellationToken ct)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Vardiya adı zorunlu.", nameof(request.Name));

        var start = ParseTime(request.StartTime, "Başlangıç saati");
        var end   = ParseTime(request.EndTime,   "Bitiş saati");
        var overnight = Shift.ComputeOvernight(start, end);

        var name = request.Name.Trim();
        var id   = request.Id ?? 0;

        // K6 (CLAUDE.md): "Kullanıcı tarafından girilen kod alanı yok" — UI'dan Kod
        // alınmaz, aktif kayıtlar arasında isim üzerinden uniqueness + code auto-derive.
        var existing = await _repo.ListAsync(includeInactive: false, ct);
        var dupName = existing.FirstOrDefault(x =>
            string.Equals(x.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase)
            && x.Id != id);
        if (dupName is not null)
            throw new ArgumentException($"Aynı isimde başka bir vardiya zaten tanımlı: '{name}'");

        // Code DB'de var ama UI gostermez — auto-turetilir (mevcut record'sa onun kodunu koru)
        string code;
        if (id > 0)
        {
            var current = existing.FirstOrDefault(x => x.Id == id)
                          ?? await _repo.GetAsync(id, ct);
            code = !string.IsNullOrWhiteSpace(current?.Code) ? current!.Code : DeriveCode(name);
        }
        else
        {
            code = DeriveCode(name);
        }

        var entity = new Shift
        {
            Id          = id,
            Code        = code,
            Name        = name,
            StartTime   = start,
            EndTime     = end,
            IsOvernight = overnight,
            ColorHex    = string.IsNullOrWhiteSpace(request.ColorHex) ? null : request.ColorHex.Trim(),
            SortOrder   = request.SortOrder,
            IsActive    = request.IsActive,
            CreatedById = userId,
            UpdatedById = userId,
        };

        try { entity.EnsureValid(); }
        catch (DomainException dex) { throw new ArgumentException(dex.Message, dex); }

        // 2026-05-21: Aralar — null ise dokunma; aksi halde validate + DTO'dan entity'ye dönüştür.
        // Mola vardiya saat aralığı içinde olmalı (gündüz vardiya için basit kontrol; gece
        // vardiyası saat geçirdiği için validation soft — operatör admin bunu kendi disipliner).
        IReadOnlyList<ShiftBreak>? breakEntities = null;
        if (request.Breaks is not null)
        {
            var list = new List<ShiftBreak>(request.Breaks.Count);
            var idx = 0;
            foreach (var b in request.Breaks)
            {
                var bStart = ParseTime(b.StartTime, $"{b.Name} başlangıç saati");
                var bEnd   = ParseTime(b.EndTime,   $"{b.Name} bitiş saati");
                var brk = new ShiftBreak
                {
                    Name      = (b.Name ?? string.Empty).Trim(),
                    StartTime = bStart,
                    EndTime   = bEnd,
                    SortOrder = b.SortOrder > 0 ? b.SortOrder : (idx + 1) * 10,
                };
                try { brk.EnsureValid(); }
                catch (DomainException dex) { throw new ArgumentException(dex.Message, dex); }

                // Aralık çakışması kontrolü — basit O(N²) ama liste küçük (3-5 mola).
                foreach (var existingBreak in list)
                {
                    var overlap = brk.StartTime < existingBreak.EndTime
                               && existingBreak.StartTime < brk.EndTime;
                    if (overlap)
                        throw new ArgumentException(
                            $"'{brk.Name}' molası '{existingBreak.Name}' ile zaman çakışıyor. Aralar üst üste binmemelidir.");
                }

                // Gündüz vardiyası için molanın vardiya içinde olması beklenir. Gece
                // vardiyasında (overnight) basit aralık karşılaştırması yanıltıcı olur
                // (saat 02:00 mola, vardiya 22-06 → start > end durumu) — bu nedenle
                // overnight ise validation yapmıyoruz, admin kendi disipliner.
                if (!overnight)
                {
                    if (brk.StartTime < entity.StartTime || brk.EndTime > entity.EndTime)
                        throw new ArgumentException(
                            $"'{brk.Name}' molası vardiya saat aralığı dışında ({entity.StartTime:hh\\:mm}-{entity.EndTime:hh\\:mm}).");
                }

                list.Add(brk);
                idx++;
            }
            breakEntities = list;
        }

        return await _repo.SaveAsync(entity, breakEntities, ct);
    }

    public Task DeleteAsync(int id, int? userId, CancellationToken ct)
    {
        if (id <= 0) throw new ArgumentException("Silinecek vardiya seçilmelidir.");
        return _repo.DeleteAsync(id, userId, ct);
    }

    // Backward-compat: Code DB'de var ama UI'dan kaldirildi (K6).
    // Yeni kayit icin name'den turet (50 char ile sinirla — NVARCHAR(50)).
    private static string DeriveCode(string name)
    {
        var t = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(t)) t = "AUTO_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        return t.Length > 50 ? t[..50] : t;
    }

    private static TimeSpan ParseTime(string raw, string fieldLabel)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException($"{fieldLabel} zorunludur (HH:mm formatında).");
        // "HH:mm" veya "HH:mm:ss" kabul et
        if (TimeSpan.TryParseExact(raw.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out var t1)) return t1;
        if (TimeSpan.TryParseExact(raw.Trim(), @"hh\:mm\:ss", CultureInfo.InvariantCulture, out var t2)) return t2;
        if (TimeSpan.TryParse(raw.Trim(), CultureInfo.InvariantCulture, out var t3)) return t3;
        throw new ArgumentException($"{fieldLabel} 'HH:mm' formatında olmalı (örn. '07:30').");
    }
}

public sealed class ShiftAssignmentService : IShiftAssignmentService
{
    private readonly IShiftAssignmentRepository _repo;

    public ShiftAssignmentService(IShiftAssignmentRepository repo) => _repo = repo;

    public Task<IReadOnlyList<ShiftAssignmentDto>> GetByPersonnelAsync(int personnelId, CancellationToken ct)
        => _repo.GetByPersonnelAsync(personnelId, ct);

    public Task<IReadOnlyList<ShiftAssignmentDto>> GetByShiftAsync(int shiftId, CancellationToken ct)
        => _repo.GetByShiftAsync(shiftId, ct);

    public Task<ShiftAssignmentDto?> GetAsync(int id, CancellationToken ct) => _repo.GetAsync(id, ct);

    public async Task<int> SaveAsync(SaveShiftAssignmentRequest request, int? userId, CancellationToken ct)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var entity = new ShiftAssignment
        {
            Id             = request.Id ?? 0,
            PersonnelId    = request.PersonnelId,
            ShiftId        = request.ShiftId,
            DayOfWeek      = request.DayOfWeek,
            EffectiveFrom  = request.EffectiveFrom,
            EffectiveTo    = request.EffectiveTo,
            IsActive       = request.IsActive,
            CreatedById    = userId,
            UpdatedById    = userId,
        };

        try { entity.EnsureValid(); }
        catch (DomainException dex) { throw new ArgumentException(dex.Message, dex); }

        return await _repo.SaveAsync(entity, ct);
    }

    public Task DeleteAsync(int id, int? userId, CancellationToken ct)
    {
        if (id <= 0) throw new ArgumentException("Silinecek atama seçilmelidir.");
        return _repo.DeleteAsync(id, userId, ct);
    }

    public Task<ShiftAssignmentDto?> GetCurrentAsync(int personnelId, DateOnly date, CancellationToken ct)
        => _repo.GetCurrentAsync(personnelId, date, ct);
}
