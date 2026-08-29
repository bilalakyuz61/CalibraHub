using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Auditing;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Services;

public sealed class PersonnelService : IPersonnelService
{
    private readonly IPersonnelRepository _repo;
    private readonly ILogisticsConfigurationRepository _logistics;
    private readonly IAuditTrailService? _audit;

    public PersonnelService(
        IPersonnelRepository repo,
        ILogisticsConfigurationRepository logistics,
        IAuditTrailService? audit = null)
    {
        _repo = repo;
        _logistics = logistics;
        _audit = audit;
    }

    public async Task<IReadOnlyCollection<PersonnelDto>> ListAsync(bool includeInactive, bool onlyOperators, CancellationToken ct)
    {
        var rows = await _repo.ListAsync(includeInactive, onlyOperators, ct);
        if (rows.Count == 0) return rows;

        // Tek sorguda tüm atamalar — kişi başına sorgu (N+1) atılmaz.
        var stationMap = await _repo.GetStationIdsForAllAsync(ct);
        if (stationMap.Count == 0) return rows;

        var locationNames = (await _logistics.GetLocationsAsync(ct))
            .ToDictionary(l => l.Id, l => l.LocationName ?? l.LocationCode);

        return rows.Select(r =>
        {
            if (!stationMap.TryGetValue(r.Id, out var ids) || ids.Count == 0) return r;
            var names = ids
                .Select(id => locationNames.TryGetValue(id, out var n) ? n : null)
                .Where(n => n is not null);
            return r with { StationIds = ids, StationNames = string.Join(", ", names) };
        }).ToList();
    }

    public async Task<PersonnelDto?> GetAsync(int id, CancellationToken ct)
    {
        var dto = await _repo.GetAsync(id, ct);
        if (dto is null) return null;
        var stationIds = await _repo.GetStationIdsAsync(id, ct);
        if (stationIds.Count == 0) return dto with { StationIds = stationIds };

        // Ad çözümü yalnız gösterim için — karşılaştırma her zaman Id üzerinden yapılır.
        var locations = await _logistics.GetLocationsAsync(ct);
        var names = locations
            .Where(l => stationIds.Contains(l.Id))
            .OrderBy(l => l.SortOrder).ThenBy(l => l.LocationCode)
            .Select(l => l.LocationName ?? l.LocationCode);
        return dto with { StationIds = stationIds, StationNames = string.Join(", ", names) };
    }

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

        // Sicil no (Code) — kullanicidan ISTENMEZ, sunucuda uretilir (CLAUDE.md kurali).
        // Mevcut kaydin sicili KORUNUR: sicil terminale/mobile elle yazilan bir degerdir,
        // kayit guncellendi diye degismesi operatorun ezberini bozardi.
        var existing = req.Id > 0 ? all.FirstOrDefault(p => p.Id == req.Id) : null;
        var code = !string.IsNullOrWhiteSpace(existing?.Code)
            ? existing!.Code
            : NextNumericCode(all);

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
            IsMobilePinRequired = req.IsMobilePinRequired,
            IsActive = req.IsActive,
            UserId = req.UserId,
            LocationId = req.LocationId,
            Phone = string.IsNullOrWhiteSpace(req.Phone) ? null : req.Phone.Trim(),
            Email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim(),
            Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim(),
            BirthDate = req.BirthDate,
        };
        var savedId = await _repo.SaveAsync(entity, ct);

        // ── İstasyon atamaları ────────────────────────────────────────────────
        // NULL = "dokunma" (eski istemci alanı hiç göndermez; kaydetmesi mevcut atamayı
        // silmemeli). Boş liste = "tümünü kaldır" — kullanıcının bilinçli seçimi.
        var oldStationIds = req.Id > 0
            ? await _repo.GetStationIdsAsync(req.Id, ct)
            : (IReadOnlyList<int>)Array.Empty<int>();
        if (req.StationIds is not null)
        {
            var requested = req.StationIds.Where(id => id > 0).Distinct().ToList();
            if (requested.Count > 0)
            {
                // İstasyon = makine parkı lokasyonu. Depo/raf/göz seçilirse SESSİZCE
                // atlanmaz, istek reddedilir — atama yapıldı sanılıp yapılmamış olması,
                // sonradan kurulacak süzgeci sessizce yanlışlar.
                var valid = (await _logistics.GetLocationsAsync(ct))
                    .Where(l => l.IsActive && l.IsMachinePark)
                    .Select(l => l.Id)
                    .ToHashSet();
                var bad = requested.Where(id => !valid.Contains(id)).ToList();
                if (bad.Count > 0)
                {
                    throw new ArgumentException(
                        "Seçilen istasyonlardan biri geçerli bir makine parkı değil " +
                        $"(Id: {string.Join(", ", bad)}). İstasyon olarak yalnızca " +
                        "\"Makine Parkuru\" işaretli aktif lokasyonlar seçilebilir.");
                }
            }
            await _repo.SetStationsAsync(savedId, requested, ct);
        }

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
                    // İstasyon ataması ayrı tabloda — reflection diff'i göremez, elle eklenir.
                    if (req.StationIds is not null)
                    {
                        var newIds = req.StationIds.Where(i => i > 0).Distinct().OrderBy(i => i).ToList();
                        var oldIds = oldStationIds.OrderBy(i => i).ToList();
                        if (!oldIds.SequenceEqual(newIds))
                        {
                            var locNames = (await _logistics.GetLocationsAsync(ct))
                                .ToDictionary(l => l.Id, l => l.LocationName ?? l.LocationCode);
                            string Label(IEnumerable<int> ids) => string.Join(", ",
                                ids.Select(i => locNames.TryGetValue(i, out var n) ? n : $"#{i}"));
                            changes.Add(new AuditFieldChange(
                                "StationIds", "İstasyonlar", Label(oldIds), Label(newIds)));
                        }
                    }
                    _audit.LogChanges("Personnel", savedId, fullName, changes);
                }
            }
            catch { /* audit yazımı kaydı asla bozmaz */ }
        }
        return savedId;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StationOptionDto>> ListStationsAsync(CancellationToken ct)
    {
        var locations = await _logistics.GetLocationsAsync(ct);
        var byId = locations.ToDictionary(l => l.Id);

        // Makine sayısı gösterilir çünkü boş bir istasyona personel atamak neredeyse her
        // zaman bir tanım hatasıdır — kullanıcı bunu listede görsün.
        var machines = await _logistics.GetMachinesAsync(ct);
        var machineCount = machines
            .Where(m => m.IsActive)
            .GroupBy(m => m.LocationId)
            .ToDictionary(g => g.Key, g => g.Count());

        return locations
            .Where(l => l.IsActive && l.IsMachinePark)
            .OrderBy(l => l.SortOrder).ThenBy(l => l.LocationCode)
            .Select(l => new StationOptionDto(
                l.Id,
                l.LocationCode,
                l.LocationName ?? l.LocationCode,
                l.ParentId.HasValue && byId.TryGetValue(l.ParentId.Value, out var p)
                    ? (p.LocationName ?? p.LocationCode)
                    : null,
                machineCount.TryGetValue(l.Id, out var c) ? c : 0))
            .ToList();
    }

    /// <summary>Sicil no'nun basamak sayisi (0001, 0002...). 9999'u asinca dogal olarak buyur.</summary>
    private const int CodeDigits = 4;

    /// <summary>
    /// Siradaki sicil no: mevcut NUMERIK kodlarin en buyugu + 1, sifir dolgulu.
    ///
    /// <para><b>Neden numerik (2026-08-28 kullanici karari):</b> sicil no daha once addan
    /// turetiliyordu ("Bilal Akyuz"). Bu deger terminal/mobil giris ekraninda ELLE yazilan
    /// bir alan; uzun ve Turkce karakterli olmasi kiosk klavyesinde pratik degildi.</para>
    ///
    /// <para>Numerik olmayan eski kodlar hesaba katilmaz (yalnizca numerikler taranir);
    /// boylece gecis sirasinda karisik durumda da artan bir sira uretilir.</para>
    /// </summary>
    private static string NextNumericCode(IEnumerable<PersonnelDto> all)
    {
        var max = 0;
        foreach (var p in all)
        {
            var c = (p.Code ?? string.Empty).Trim();
            if (c.Length > 0 && c.All(char.IsAsciiDigit) && int.TryParse(c, out var n) && n > max)
                max = n;
        }
        return (max + 1).ToString(new string('0', CodeDigits));
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
