using System.Net;
using System.Text.RegularExpressions;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Services;

/// <summary>
/// Varlık Yönetimi iş kuralları. Machine deseni (LogisticsConfigurationService) ile aynı:
/// isim benzersizliği + kod auto-derive. Ek olarak Makine birleşimi (projection +
/// lazy-materialize) ve bakım/kalibrasyon takvim güncellemesi içerir.
/// </summary>
public sealed class AssetService : IAssetService
{
    private readonly IAssetRepository _repository;
    private readonly ILogisticsConfigurationService _logistics;
    private readonly IDepartmentRepository _departments;
    private readonly IPersonnelService _personnel;

    public AssetService(
        IAssetRepository repository,
        ILogisticsConfigurationService logistics,
        IDepartmentRepository departments,
        IPersonnelService personnel)
    {
        _repository = repository;
        _logistics = logistics;
        _departments = departments;
        _personnel = personnel;
    }

    // ── Asset CRUD ────────────────────────────────────────────────

    public async Task<IReadOnlyCollection<AssetDto>> GetAssetsAsync(CancellationToken ct)
    {
        var assets = await _repository.GetAssetsAsync(ct);
        var lookups = await LoadLookupsAsync(ct);
        return assets.Select(a => MapDto(a, lookups)).ToArray();
    }

    public async Task<AssetDto?> GetAssetByIdAsync(int id, CancellationToken ct)
    {
        var asset = await _repository.GetAssetByIdAsync(id, ct);
        if (asset is null) return null;
        var lookups = await LoadLookupsAsync(ct);
        return MapDto(asset, lookups);
    }

    public async Task<int> CreateAssetAsync(CreateAssetRequest request, CancellationToken ct)
    {
        var name = (request.AssetName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Varlık adı zorunludur.");
        ValidateFormats(request.IpAddress, request.MacAddress, request.PlateNo);

        var existing = await _repository.GetAssetsAsync(ct);

        // İsim benzersizliği (makineye bağlı materialize hariç — makine adı zaten makineler arasında unique)
        if (!request.MachineId.HasValue &&
            existing.Any(a => string.Equals(a.AssetName?.Trim(), name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"Aynı isimde başka bir varlık zaten tanımlı: '{name}'");

        var code = GenerateUniqueCode(existing.Select(a => a.AssetCode));
        var kind = request.MachineId.HasValue ? AssetKind.Machine : request.Kind;

        var asset = new Asset
        {
            AssetCode = code,
            AssetName = name,
            Description = Trim(request.Description),
            Kind = kind,
            LocationId = NullIfZero(request.LocationId),
            DepartmentId = NullIfZero(request.DepartmentId),
            AssignedPersonnelId = NullIfZero(request.AssignedPersonnelId),
            MachineId = NullIfZero(request.MachineId),
            SerialNo = Trim(request.SerialNo),
            AcquisitionDate = request.AcquisitionDate,
            WarrantyExpiryDate = request.WarrantyExpiryDate,
            IpAddress = Trim(request.IpAddress),
            Hostname = Trim(request.Hostname),
            OperatingSystem = Trim(request.OperatingSystem),
            MacAddress = Trim(request.MacAddress),
            NetworkDomain = Trim(request.NetworkDomain),
            PlateNo = NormalizePlate(request.PlateNo),
            IsMaintained = request.IsMaintained,
            MaintenancePeriodDays = request.MaintenancePeriodDays,
            MaintenancePeriodUnit = request.MaintenancePeriodUnit,
            IsCalibrated = request.IsCalibrated,
            CalibrationPeriodDays = request.CalibrationPeriodDays,
            CalibrationPeriodUnit = request.CalibrationPeriodUnit,
            // Last/Next tarihleri hareketlerden (AssetEvent) türetilir — oluşturmada boş.
            Status = request.Status,
            SortOrder = request.SortOrder < 0 ? 0 : request.SortOrder,
            IsActive = request.IsActive,
            IsAssignable = request.IsAssignable,
            CreatedById = request.UserId,
        };
        return await _repository.AddAssetAsync(asset, ct);
    }

    public async Task UpdateAssetAsync(UpdateAssetRequest request, CancellationToken ct)
    {
        if (request.Id <= 0)
            throw new ArgumentException("Varlık seçimi zorunludur.");

        var name = (request.AssetName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Varlık adı zorunludur.");
        ValidateFormats(request.IpAddress, request.MacAddress, request.PlateNo);

        var existing = await _repository.GetAssetByIdAsync(request.Id, ct)
            ?? throw new ArgumentException("Güncellenecek varlık bulunamadı.");
        var oldStatus = existing.Status;

        var all = await _repository.GetAssetsAsync(ct);
        if (!existing.MachineId.HasValue &&
            all.Any(a => a.Id != request.Id &&
                         string.Equals(a.AssetName?.Trim(), name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"Aynı isimde başka bir varlık zaten tanımlı: '{name}'");

        var asset = new Asset
        {
            Id = existing.Id,
            CompanyId = existing.CompanyId,
            AssetCode = existing.AssetCode,                          // korunur
            AssetName = name,
            Description = Trim(request.Description),
            Kind = existing.MachineId.HasValue ? AssetKind.Machine : request.Kind,
            LocationId = existing.LocationId,                        // lokasyon zimmet hareketiyle yönetilir — edit formu dokunmaz
            DepartmentId = existing.DepartmentId,                    // departman zimmet hareketiyle yönetilir — edit formu dokunmaz
            AssignedPersonnelId = existing.AssignedPersonnelId,      // zimmet ayrı hareketle (Zimmetleme) yönetilir — edit formu dokunmaz
            MachineId = existing.MachineId,                          // korunur
            SerialNo = Trim(request.SerialNo),
            AcquisitionDate = request.AcquisitionDate,
            WarrantyExpiryDate = request.WarrantyExpiryDate,
            IpAddress = Trim(request.IpAddress),
            Hostname = Trim(request.Hostname),
            OperatingSystem = Trim(request.OperatingSystem),
            MacAddress = Trim(request.MacAddress),
            NetworkDomain = Trim(request.NetworkDomain),
            PlateNo = NormalizePlate(request.PlateNo),
            // Last* tarihleri hareketlerden türetilir — edit formu DEĞİŞTİREMEZ (korunur).
            // Next* = Last + (yeni) periyot ile tekrar hesaplanır.
            IsMaintained = request.IsMaintained,
            MaintenancePeriodDays = request.MaintenancePeriodDays,
            MaintenancePeriodUnit = request.MaintenancePeriodUnit,
            LastMaintenanceDate = existing.LastMaintenanceDate,
            NextMaintenanceDate = ComputeNext(existing.LastMaintenanceDate, request.MaintenancePeriodDays, request.MaintenancePeriodUnit),
            MaintenanceRemindedFor = existing.MaintenanceRemindedFor,
            IsCalibrated = request.IsCalibrated,
            CalibrationPeriodDays = request.CalibrationPeriodDays,
            CalibrationPeriodUnit = request.CalibrationPeriodUnit,
            LastCalibrationDate = existing.LastCalibrationDate,
            NextCalibrationDate = ComputeNext(existing.LastCalibrationDate, request.CalibrationPeriodDays, request.CalibrationPeriodUnit),
            CalibrationRemindedFor = existing.CalibrationRemindedFor,
            Status = request.Status,
            SortOrder = request.SortOrder < 0 ? 0 : request.SortOrder,
            IsActive = request.IsActive,
            IsAssignable = request.IsAssignable,
            Created = existing.Created,
            CreatedById = existing.CreatedById,
            UpdatedById = request.UserId,
        };
        await _repository.UpdateAssetAsync(asset, ct);

        // Durum değişikliği → geçmişe StatusChange olayı düş (yaşam döngüsü izi)
        if (oldStatus != request.Status)
        {
            await _repository.AddEventAsync(new AssetEvent
            {
                AssetId = request.Id,
                EventType = AssetEventType.StatusChange,
                EventDate = DateTime.UtcNow,
                Notes = $"Durum: {EnumText(oldStatus)} → {EnumText(request.Status)}",
                CreatedById = request.UserId,
            }, ct);
        }
    }

    public Task DeleteAssetAsync(int id, CancellationToken ct)
    {
        if (id <= 0) throw new ArgumentException("Varlık seçimi zorunludur.");
        return _repository.DeleteAssetAsync(id, ct);
    }

    public async Task<AssetEditLookupsDto> GetEditLookupsAsync(CancellationToken ct)
    {
        var locations = await _logistics.GetLocationsAsync(ct);
        var departments = await _departments.GetAllAsync(ct);
        var personnel = await _personnel.ListAsync(includeInactive: false, onlyOperators: false, ct);
        var machines = await _logistics.GetMachinesAsync(ct);

        return new AssetEditLookupsDto(
            locations.Where(l => l.IsActive)
                .Select(l => new AssetLocationItemDto(l.Id, l.ParentId, l.LocationCode, l.LocationName, l.SortOrder, l.IsActive))
                .ToList(),
            departments.Where(d => d.IsActive).OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .Select(d => new AssetLookupItemDto(d.Id, d.Name)).ToList(),
            personnel.Where(p => p.IsActive).OrderBy(p => p.FullName, StringComparer.OrdinalIgnoreCase)
                .Select(p => new AssetLookupItemDto(p.Id, p.FullName)).ToList(),
            machines.Where(m => m.IsActive).OrderBy(m => m.Name ?? m.Code, StringComparer.OrdinalIgnoreCase)
                .Select(m => new AssetLookupItemDto(m.Id, m.Name ?? m.Code)).ToList());
    }

    // ── Birleşik board ────────────────────────────────────────────

    public async Task<IReadOnlyCollection<AssetCardDto>> GetBoardCardsAsync(CancellationToken ct)
    {
        var assets = await _repository.GetAssetsAsync(ct);
        var lookups = await LoadLookupsAsync(ct);

        var cards = new List<AssetCardDto>();

        foreach (var a in assets)
        {
            var mach = a.MachineId.HasValue && lookups.Machines.TryGetValue(a.MachineId.Value, out var m) ? m : null;
            var locName = ResolveLocationText(a.LocationId, lookups) ?? mach?.LocationName;
            var name = mach != null ? (mach.Name ?? mach.Code) : a.AssetName;
            cards.Add(new AssetCardDto(
                CardId: $"a{a.Id}",
                AssetId: a.Id,
                MachineId: a.MachineId,
                Name: name,
                LocationName: locName,
                DepartmentName: a.DepartmentId.HasValue && lookups.Departments.TryGetValue(a.DepartmentId.Value, out var d) ? d.Name : null,
                AssignedPersonnelName: a.AssignedPersonnelId.HasValue && lookups.Personnel.TryGetValue(a.AssignedPersonnelId.Value, out var p) ? p.FullName : null,
                Kind: a.Kind,
                Status: a.Status,
                IsActive: a.IsActive,
                IsAssignable: a.IsAssignable,
                IsMaintained: a.IsMaintained,
                MaintenancePeriodDays: a.MaintenancePeriodDays,
                NextMaintenanceDate: a.NextMaintenanceDate,
                IsCalibrated: a.IsCalibrated,
                CalibrationPeriodDays: a.CalibrationPeriodDays,
                NextCalibrationDate: a.NextCalibrationDate,
                SortOrder: a.SortOrder,
                IsVirtualMachine: false));
        }

        // Materialize edilmemiş makineler → sanal kart
        var materializedMachineIds = assets.Where(a => a.MachineId.HasValue).Select(a => a.MachineId!.Value).ToHashSet();
        foreach (var m in lookups.Machines.Values)
        {
            if (materializedMachineIds.Contains(m.Id)) continue;
            var locText = m.LocationCode != null
                ? m.LocationCode + (m.LocationName != null ? " — " + m.LocationName : "")
                : m.LocationName;
            cards.Add(new AssetCardDto(
                CardId: $"m{m.Id}",
                AssetId: null,
                MachineId: m.Id,
                Name: m.Name ?? m.Code,
                LocationName: locText,
                DepartmentName: null,
                AssignedPersonnelName: null,
                Kind: AssetKind.Machine,
                Status: m.IsActive ? AssetStatus.Active : AssetStatus.Retired,
                IsActive: m.IsActive,
                IsAssignable: true,
                IsMaintained: false,
                MaintenancePeriodDays: null,
                NextMaintenanceDate: null,
                IsCalibrated: false,
                CalibrationPeriodDays: null,
                NextCalibrationDate: null,
                SortOrder: m.SortOrder,
                IsVirtualMachine: true));
        }

        return cards
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<AssetDto> GetOrMaterializeByMachineIdAsync(int machineId, int? userId, CancellationToken ct)
    {
        if (machineId <= 0) throw new ArgumentException("Makine seçimi zorunludur.");

        var existing = await _repository.GetAssetByMachineIdAsync(machineId, ct);
        if (existing is not null)
        {
            var lk = await LoadLookupsAsync(ct);
            return MapDto(existing, lk);
        }

        var machines = await _logistics.GetMachinesAsync(ct);
        var machine = machines.FirstOrDefault(m => m.Id == machineId)
            ?? throw new ArgumentException("Makine bulunamadı.");

        var allAssets = await _repository.GetAssetsAsync(ct);
        var code = GenerateUniqueCode(allAssets.Select(a => a.AssetCode));

        var asset = new Asset
        {
            AssetCode = code,
            AssetName = machine.Name ?? machine.Code,
            Kind = AssetKind.Machine,
            LocationId = machine.LocationId > 0 ? machine.LocationId : null,
            MachineId = machine.Id,
            Status = machine.IsActive ? AssetStatus.Active : AssetStatus.Retired,
            SortOrder = machine.SortOrder,
            IsActive = machine.IsActive,
            CreatedById = userId,
        };
        var newId = await _repository.AddAssetAsync(asset, ct);
        return (await GetAssetByIdAsync(newId, ct))!;
    }

    // ── Geçmiş (AssetEvent) ───────────────────────────────────────

    public async Task<IReadOnlyCollection<AssetEventDto>> GetEventsAsync(int assetId, CancellationToken ct)
    {
        var events = await _repository.GetEventsByAssetAsync(assetId, ct);
        var personnel = (await _personnel.ListAsync(includeInactive: true, onlyOperators: false, ct))
            .ToDictionary(p => p.Id);
        return events.Select(e => new AssetEventDto(
            Id: e.Id,
            AssetId: e.AssetId,
            EventType: e.EventType,
            EventDate: e.EventDate,
            PerformedByPersonnelId: e.PerformedByPersonnelId,
            PerformedByName: e.PerformedByPersonnelId.HasValue && personnel.TryGetValue(e.PerformedByPersonnelId.Value, out var p) ? p.FullName : null,
            PerformedByText: e.PerformedByText,
            Cost: e.Cost,
            Result: e.Result,
            Notes: e.Notes,
            NextDueDate: e.NextDueDate,
            DocumentUrl: e.DocumentUrl,
            Created: e.Created,
            CreatedById: e.CreatedById)).ToArray();
    }

    public async Task<int> AddEventAsync(CreateAssetEventRequest request, CancellationToken ct)
    {
        if (request.AssetId <= 0) throw new ArgumentException("Varlık seçimi zorunludur.");
        var asset = await _repository.GetAssetByIdAsync(request.AssetId, ct)
            ?? throw new ArgumentException("Varlık bulunamadı.");

        var ev = new AssetEvent
        {
            AssetId = request.AssetId,
            EventType = request.EventType,
            EventDate = request.EventDate == default ? DateTime.UtcNow : request.EventDate,
            PerformedByPersonnelId = NullIfZero(request.PerformedByPersonnelId),
            PerformedByText = Trim(request.PerformedByText),
            Cost = request.Cost,
            Result = request.Result,
            Notes = Trim(request.Notes),
            NextDueDate = request.NextDueDate,
            DocumentUrl = Trim(request.DocumentUrl),
            CreatedById = request.UserId,
        };
        var newId = await _repository.AddEventAsync(ev, ct);

        // Bakım/kalibrasyon olayı → varlığın "son" tarihlerini hareketlerden yeniden türet
        if (request.EventType is AssetEventType.Maintenance or AssetEventType.Calibration)
            await RecomputeScheduleAsync(request.AssetId, request.UserId, ct);

        return newId;
    }

    public async Task DeleteEventAsync(int id, CancellationToken ct)
    {
        if (id <= 0) throw new ArgumentException("Kayıt seçimi zorunludur.");
        var ev = await _repository.GetEventByIdAsync(id, ct);
        await _repository.DeleteEventAsync(id, ct);

        // Bakım/kalibrasyon hareketi silindiyse takvim yeniden türetilir
        if (ev is not null && ev.EventType is AssetEventType.Maintenance or AssetEventType.Calibration)
            await RecomputeScheduleAsync(ev.AssetId, null, ct);
    }

    public async Task<int> AssignAsync(int assetId, int? personnelId, int? departmentId, int? locationId, DateTime assignDate, string? note, string? documentNo, int? userId, CancellationToken ct)
    {
        var perId = NullIfZero(personnelId);
        var deptId = NullIfZero(departmentId);
        var locId = NullIfZero(locationId);
        if (perId is null && deptId is null)
            throw new ArgumentException("Zimmet için personel veya departman seçimi zorunludur.");

        var asset = await _repository.GetAssetByIdAsync(assetId, ct)
            ?? throw new ArgumentException("Varlık bulunamadı.");
        if (!asset.IsAssignable)
            throw new ArgumentException("Bu varlık zimmetlenebilir olarak işaretlenmemiş.");
        if (assignDate == default) assignDate = DateTime.Today;

        // Aktif zimmet varsa, yeni zimmet öncesi otomatik iade et
        var active = await _repository.GetActiveAssignmentAsync(assetId, ct);
        if (active is not null)
            await _repository.CloseAssignmentAsync(active.Id, assignDate, "Yeniden zimmet nedeniyle otomatik iade", userId, ct);

        var newId = await _repository.AddAssignmentAsync(new AssetAssignment
        {
            AssetId = assetId,
            PersonnelId = perId,
            DepartmentId = deptId,
            LocationId = locId,
            AssignDate = assignDate,
            AssignNote = Trim(note),
            DocumentNo = Trim(documentNo),
            CreatedById = userId,
        }, ct);

        // Güncel zimmet hedefi + lokasyon (denormalize — kart/rapor/filtre için)
        var updated = CloneWith(asset, m => { m.AssignedPersonnelId = perId; m.DepartmentId = deptId; m.LocationId = locId; m.UpdatedById = userId; });
        await _repository.UpdateAssetAsync(updated, ct);
        return newId;
    }

    public async Task ReturnAsync(int assetId, DateTime returnDate, string? note, int? userId, CancellationToken ct)
    {
        var asset = await _repository.GetAssetByIdAsync(assetId, ct)
            ?? throw new ArgumentException("Varlık bulunamadı.");
        var active = await _repository.GetActiveAssignmentAsync(assetId, ct)
            ?? throw new ArgumentException("Bu varlık şu anda zimmette değil.");
        if (returnDate == default) returnDate = DateTime.Today;

        await _repository.CloseAssignmentAsync(active.Id, returnDate, Trim(note), userId, ct);

        // İade → güncel zimmet sahibi + departman + lokasyon temizlenir (aktif zimmet kalmadı; geçmiş harekette korunur)
        var updated = CloneWith(asset, m => { m.AssignedPersonnelId = null; m.DepartmentId = null; m.LocationId = null; m.UpdatedById = userId; });
        await _repository.UpdateAssetAsync(updated, ct);
    }

    public async Task<IReadOnlyCollection<AssetAssignmentDto>> GetAssignmentsAsync(int assetId, CancellationToken ct)
    {
        var list = await _repository.GetAssignmentsByAssetAsync(assetId, ct);
        var personnel = (await _personnel.ListAsync(includeInactive: true, onlyOperators: false, ct)).ToDictionary(p => p.Id);
        var departments = (await _departments.GetAllAsync(ct)).GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First());
        var locations = (await _logistics.GetLocationsAsync(ct)).GroupBy(l => l.Id).ToDictionary(g => g.Key, g => g.First());
        return list.Select(a => MapAssignment(a, personnel, departments, locations)).ToArray();
    }

    public async Task<AssetAssignmentDto?> GetCurrentAssignmentAsync(int assetId, CancellationToken ct)
    {
        var active = await _repository.GetActiveAssignmentAsync(assetId, ct);
        if (active is null) return null;
        var personnel = (await _personnel.ListAsync(includeInactive: true, onlyOperators: false, ct)).ToDictionary(p => p.Id);
        var departments = (await _departments.GetAllAsync(ct)).GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First());
        var locations = (await _logistics.GetLocationsAsync(ct)).GroupBy(l => l.Id).ToDictionary(g => g.Key, g => g.First());
        return MapAssignment(active, personnel, departments, locations);
    }

    public async Task<AssetAssignmentDto?> GetAssignmentByIdAsync(int assignmentId, CancellationToken ct)
    {
        var a = await _repository.GetAssignmentByIdAsync(assignmentId, ct);
        if (a is null) return null;
        var personnel = (await _personnel.ListAsync(includeInactive: true, onlyOperators: false, ct)).ToDictionary(p => p.Id);
        var departments = (await _departments.GetAllAsync(ct)).GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First());
        var locations = (await _logistics.GetLocationsAsync(ct)).GroupBy(l => l.Id).ToDictionary(g => g.Key, g => g.First());
        return MapAssignment(a, personnel, departments, locations);
    }

    public async Task<IReadOnlyCollection<AssignmentReportRowDto>> GetAssignmentReportAsync(CancellationToken ct)
    {
        var assignments = await _repository.GetAllAssignmentsAsync(ct);
        if (assignments.Count == 0) return System.Array.Empty<AssignmentReportRowDto>();

        var assets = (await _repository.GetAssetsAsync(ct)).ToDictionary(a => a.Id);
        var personnel = (await _personnel.ListAsync(includeInactive: true, onlyOperators: false, ct)).ToDictionary(p => p.Id);
        var departments = (await _departments.GetAllAsync(ct)).GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First());
        var locations = (await _logistics.GetLocationsAsync(ct)).GroupBy(l => l.Id).ToDictionary(g => g.Key, g => g.First());

        return assignments.Select(a =>
        {
            assets.TryGetValue(a.AssetId, out var asset);
            return new AssignmentReportRowDto(
                AssignmentId: a.Id,
                AssetId: a.AssetId,
                AssetCode: asset?.AssetCode ?? "—",
                AssetName: asset?.AssetName ?? $"#{a.AssetId}",
                PersonnelId: a.PersonnelId,
                PersonnelName: a.PersonnelId.HasValue && personnel.TryGetValue(a.PersonnelId.Value, out var p) ? p.FullName : null,
                DepartmentName: a.DepartmentId.HasValue && departments.TryGetValue(a.DepartmentId.Value, out var dep) ? dep.Name : null,
                LocationName: a.LocationId.HasValue && locations.TryGetValue(a.LocationId.Value, out var lc) ? (lc.LocationName ?? lc.LocationCode) : null,
                AssignDate: a.AssignDate,
                ReturnDate: a.ReturnDate,
                DocumentNo: a.DocumentNo,
                AssignNote: a.AssignNote,
                ReturnNote: a.ReturnNote);
        }).ToArray();
    }

    private static AssetAssignmentDto MapAssignment(AssetAssignment a,
        IReadOnlyDictionary<int, PersonnelDto> personnel,
        IReadOnlyDictionary<int, Department> departments,
        IReadOnlyDictionary<int, LocationDto> locations)
        => new(a.Id, a.AssetId, a.PersonnelId,
            a.PersonnelId.HasValue && personnel.TryGetValue(a.PersonnelId.Value, out var p) ? p.FullName : null,
            a.DepartmentId,
            a.DepartmentId.HasValue && departments.TryGetValue(a.DepartmentId.Value, out var d) ? d.Name : null,
            a.LocationId,
            a.LocationId.HasValue && locations.TryGetValue(a.LocationId.Value, out var l) ? (l.LocationName ?? l.LocationCode) : null,
            a.AssignDate, a.ReturnDate, a.AssignNote, a.ReturnNote, a.DocumentNo, a.Created, a.CreatedById);

    // ── Helpers ───────────────────────────────────────────────────

    private sealed record Lookups(
        IReadOnlyDictionary<int, LocationDto> Locations,
        IReadOnlyDictionary<int, Department> Departments,
        IReadOnlyDictionary<int, PersonnelDto> Personnel,
        IReadOnlyDictionary<int, MachineDto> Machines);

    private async Task<Lookups> LoadLookupsAsync(CancellationToken ct)
    {
        var locations = await _logistics.GetLocationsAsync(ct);
        var departments = await _departments.GetAllAsync(ct);
        var personnel = await _personnel.ListAsync(includeInactive: true, onlyOperators: false, ct);
        var machines = await _logistics.GetMachinesAsync(ct);
        return new Lookups(
            locations.GroupBy(l => l.Id).ToDictionary(g => g.Key, g => g.First()),
            departments.GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First()),
            personnel.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First()),
            machines.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First()));
    }

    private static AssetDto MapDto(Asset a, Lookups lk)
    {
        LocationDto? loc = a.LocationId.HasValue && lk.Locations.TryGetValue(a.LocationId.Value, out var l) ? l : null;
        Department? dept = a.DepartmentId.HasValue && lk.Departments.TryGetValue(a.DepartmentId.Value, out var d) ? d : null;
        PersonnelDto? pers = a.AssignedPersonnelId.HasValue && lk.Personnel.TryGetValue(a.AssignedPersonnelId.Value, out var p) ? p : null;
        MachineDto? mach = a.MachineId.HasValue && lk.Machines.TryGetValue(a.MachineId.Value, out var m) ? m : null;

        return new AssetDto(
            Id: a.Id,
            AssetCode: a.AssetCode,
            AssetName: a.AssetName,
            Description: a.Description,
            Kind: a.Kind,
            LocationId: a.LocationId,
            LocationCode: loc?.LocationCode,
            LocationName: loc?.LocationName,
            DepartmentId: a.DepartmentId,
            DepartmentName: dept?.Name,
            AssignedPersonnelId: a.AssignedPersonnelId,
            AssignedPersonnelName: pers?.FullName,
            MachineId: a.MachineId,
            MachineCode: mach?.Code,
            MachineName: mach?.Name,
            SerialNo: a.SerialNo,
            AcquisitionDate: a.AcquisitionDate,
            WarrantyExpiryDate: a.WarrantyExpiryDate,
            IpAddress: a.IpAddress,
            Hostname: a.Hostname,
            OperatingSystem: a.OperatingSystem,
            MacAddress: a.MacAddress,
            NetworkDomain: a.NetworkDomain,
            PlateNo: a.PlateNo,
            IsMaintained: a.IsMaintained,
            MaintenancePeriodDays: a.MaintenancePeriodDays,
            MaintenancePeriodUnit: a.MaintenancePeriodUnit,
            LastMaintenanceDate: a.LastMaintenanceDate,
            NextMaintenanceDate: a.NextMaintenanceDate,
            IsCalibrated: a.IsCalibrated,
            CalibrationPeriodDays: a.CalibrationPeriodDays,
            CalibrationPeriodUnit: a.CalibrationPeriodUnit,
            LastCalibrationDate: a.LastCalibrationDate,
            NextCalibrationDate: a.NextCalibrationDate,
            Status: a.Status,
            SortOrder: a.SortOrder,
            IsActive: a.IsActive,
            IsAssignable: a.IsAssignable);
    }

    private static string? ResolveLocationText(int? locationId, Lookups lk)
    {
        if (!locationId.HasValue || !lk.Locations.TryGetValue(locationId.Value, out var l)) return null;
        return l.LocationCode + (l.LocationName != null ? " — " + l.LocationName : "");
    }

    /// <summary>
    /// Son bakım/kalibrasyon tarihlerini <b>hareketlerden</b> (AssetEvent) yeniden türetir.
    /// Edit formu bu alanlara dokunmaz; tek doğru kaynak hareket geçmişidir.
    /// Last = ilgili tipteki en güncel EventDate; Next = Last + periyot (gün).
    /// </summary>
    private async Task RecomputeScheduleAsync(int assetId, int? userId, CancellationToken ct)
    {
        var asset = await _repository.GetAssetByIdAsync(assetId, ct);
        if (asset is null) return;

        var events = await _repository.GetEventsByAssetAsync(assetId, ct);
        var lastMaint = events.Where(e => e.EventType == AssetEventType.Maintenance)
            .Select(e => (DateTime?)e.EventDate).DefaultIfEmpty(null).Max();
        var lastCalib = events.Where(e => e.EventType == AssetEventType.Calibration)
            .Select(e => (DateTime?)e.EventDate).DefaultIfEmpty(null).Max();

        var updated = CloneWith(asset, m =>
        {
            m.LastMaintenanceDate = lastMaint;
            m.NextMaintenanceDate = ComputeNext(lastMaint, asset.MaintenancePeriodDays, asset.MaintenancePeriodUnit);
            m.LastCalibrationDate = lastCalib;
            m.NextCalibrationDate = ComputeNext(lastCalib, asset.CalibrationPeriodDays, asset.CalibrationPeriodUnit);
            if (userId.HasValue) m.UpdatedById = userId;
        });
        await _repository.UpdateAssetAsync(updated, ct);
    }

    /// <summary>Sonraki tarih = son tarih + periyot (birime göre Gün/Ay/Yıl). Periyot yoksa null.</summary>
    private static DateTime? ComputeNext(DateTime? last, int? value, AssetPeriodUnit unit)
    {
        if (!last.HasValue || !value.HasValue || value.Value <= 0) return null;
        return unit switch
        {
            AssetPeriodUnit.Months => last.Value.AddMonths(value.Value),
            AssetPeriodUnit.Years => last.Value.AddYears(value.Value),
            _ => last.Value.AddDays(value.Value),
        };
    }

    /// <summary>Asset init-only olduğu için kopya üzerinde mutasyon yapan klon yardımcısı.</summary>
    private static Asset CloneWith(Asset source, Action<AssetMutable> mutate)
    {
        var m = AssetMutable.From(source);
        mutate(m);
        return m.ToAsset();
    }

    private static string GenerateUniqueCode(IEnumerable<string> existingCodes)
    {
        var set = new HashSet<string>(existingCodes, StringComparer.OrdinalIgnoreCase);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var code = "VRL-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
            if (!set.Contains(code)) return code;
        }
        return "VRL-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
    }

    private static int? NullIfZero(int? value) => value.HasValue && value.Value > 0 ? value : null;
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // ── Format doğrulama (IP / MAC / plaka) ───────────────────────
    // MAC: 6 oktet hex, ':' veya '-' ayraçlı (00:1A:2B:3C:4D:5E)
    private static readonly Regex MacRegex = new(@"^([0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}$", RegexOptions.Compiled);
    // TR plaka: il (01-81) + 1-3 harf + 2-5 rakam (boşluklar opsiyonel)
    private static readonly Regex PlateRegex = new(@"^(0[1-9]|[1-7][0-9]|8[01]) ?[A-Z]{1,3} ?[0-9]{2,5}$", RegexOptions.Compiled);

    /// <summary>Plaka normalize: büyük harf, fazla boşlukları tek boşluğa indir, baş/son trim.</summary>
    private static string? NormalizePlate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var compact = Regex.Replace(value.Trim().ToUpperInvariant(), @"\s+", " ");
        return compact;
    }

    /// <summary>IP/MAC/plaka alanları doluysa formatı doğrular; hatalıysa ArgumentException atar.</summary>
    private static void ValidateFormats(string? ip, string? mac, string? plate)
    {
        var ipv = Trim(ip);
        if (ipv is not null && !IPAddress.TryParse(ipv, out _))
            throw new ArgumentException($"Geçersiz IP adresi: '{ipv}'. Örnek: 192.168.1.50");

        var macv = Trim(mac);
        if (macv is not null && !MacRegex.IsMatch(macv))
            throw new ArgumentException($"Geçersiz MAC adresi: '{macv}'. Örnek: 00:1A:2B:3C:4D:5E");

        var platev = NormalizePlate(plate);
        if (platev is not null && !PlateRegex.IsMatch(platev))
            throw new ArgumentException($"Geçersiz plaka: '{plate}'. Örnek: 34 ABC 123");
    }

    private static string EnumText(AssetStatus status) => status switch
    {
        AssetStatus.Active => "Aktif",
        AssetStatus.InMaintenance => "Bakımda",
        AssetStatus.Retired => "Hurda",
        AssetStatus.Disposed => "Elden Çıkarıldı",
        _ => status.ToString(),
    };

    /// <summary>Asset (init-only) için mutasyon ara modeli.</summary>
    private sealed class AssetMutable
    {
        public int Id; public int CompanyId; public string AssetCode = ""; public string AssetName = "";
        public string? Description; public AssetKind Kind; public int? LocationId; public int? DepartmentId;
        public int? AssignedPersonnelId; public int? MachineId; public string? SerialNo;
        public DateTime? AcquisitionDate; public DateTime? WarrantyExpiryDate;
        public string? IpAddress; public string? Hostname; public string? OperatingSystem; public string? MacAddress; public string? NetworkDomain; public string? PlateNo;
        public bool IsMaintained; public int? MaintenancePeriodDays; public AssetPeriodUnit MaintenancePeriodUnit; public DateTime? LastMaintenanceDate; public DateTime? NextMaintenanceDate; public DateTime? MaintenanceRemindedFor;
        public bool IsCalibrated; public int? CalibrationPeriodDays; public AssetPeriodUnit CalibrationPeriodUnit; public DateTime? LastCalibrationDate; public DateTime? NextCalibrationDate; public DateTime? CalibrationRemindedFor;
        public bool IsAssignable; public AssetStatus Status; public int SortOrder; public bool IsActive;
        public DateTime Created; public DateTime? Updated; public int? CreatedById; public int? UpdatedById;

        public static AssetMutable From(Asset a) => new()
        {
            Id = a.Id, CompanyId = a.CompanyId, AssetCode = a.AssetCode, AssetName = a.AssetName,
            Description = a.Description, Kind = a.Kind, LocationId = a.LocationId, DepartmentId = a.DepartmentId,
            AssignedPersonnelId = a.AssignedPersonnelId, MachineId = a.MachineId, SerialNo = a.SerialNo,
            AcquisitionDate = a.AcquisitionDate, WarrantyExpiryDate = a.WarrantyExpiryDate,
            IpAddress = a.IpAddress, Hostname = a.Hostname, OperatingSystem = a.OperatingSystem, MacAddress = a.MacAddress, NetworkDomain = a.NetworkDomain, PlateNo = a.PlateNo,
            IsMaintained = a.IsMaintained, MaintenancePeriodDays = a.MaintenancePeriodDays, MaintenancePeriodUnit = a.MaintenancePeriodUnit, LastMaintenanceDate = a.LastMaintenanceDate,
            NextMaintenanceDate = a.NextMaintenanceDate, MaintenanceRemindedFor = a.MaintenanceRemindedFor,
            IsCalibrated = a.IsCalibrated, CalibrationPeriodDays = a.CalibrationPeriodDays, CalibrationPeriodUnit = a.CalibrationPeriodUnit, LastCalibrationDate = a.LastCalibrationDate,
            NextCalibrationDate = a.NextCalibrationDate, CalibrationRemindedFor = a.CalibrationRemindedFor,
            IsAssignable = a.IsAssignable, Status = a.Status, SortOrder = a.SortOrder, IsActive = a.IsActive,
            Created = a.Created, Updated = a.Updated, CreatedById = a.CreatedById, UpdatedById = a.UpdatedById,
        };

        public Asset ToAsset() => new()
        {
            Id = Id, CompanyId = CompanyId, AssetCode = AssetCode, AssetName = AssetName,
            Description = Description, Kind = Kind, LocationId = LocationId, DepartmentId = DepartmentId,
            AssignedPersonnelId = AssignedPersonnelId, MachineId = MachineId, SerialNo = SerialNo,
            AcquisitionDate = AcquisitionDate, WarrantyExpiryDate = WarrantyExpiryDate,
            IpAddress = IpAddress, Hostname = Hostname, OperatingSystem = OperatingSystem, MacAddress = MacAddress, NetworkDomain = NetworkDomain, PlateNo = PlateNo,
            IsMaintained = IsMaintained, MaintenancePeriodDays = MaintenancePeriodDays, MaintenancePeriodUnit = MaintenancePeriodUnit, LastMaintenanceDate = LastMaintenanceDate,
            NextMaintenanceDate = NextMaintenanceDate, MaintenanceRemindedFor = MaintenanceRemindedFor,
            IsCalibrated = IsCalibrated, CalibrationPeriodDays = CalibrationPeriodDays, CalibrationPeriodUnit = CalibrationPeriodUnit, LastCalibrationDate = LastCalibrationDate,
            NextCalibrationDate = NextCalibrationDate, CalibrationRemindedFor = CalibrationRemindedFor,
            IsAssignable = IsAssignable, Status = Status, SortOrder = SortOrder, IsActive = IsActive,
            Created = Created, Updated = Updated, CreatedById = CreatedById, UpdatedById = UpdatedById,
        };
    }
}
