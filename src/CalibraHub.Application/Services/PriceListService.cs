using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Services;

public sealed class PriceListService : IPriceListService
{
    private readonly IPriceListRepository _repo;

    public PriceListService(IPriceListRepository repo) => _repo = repo;

    // ── Fiyat Gruplari ────────────────────────────────────────────────────────

    public async Task<IReadOnlyCollection<PriceGroupDto>> GetAllGroupsAsync(CancellationToken ct)
    {
        var groups = await _repo.GetAllGroupsAsync(ct);
        return groups.Select(MapGroup).ToArray();
    }

    public async Task<PriceGroupDto?> GetGroupByIdAsync(int id, CancellationToken ct)
    {
        var g = await _repo.GetGroupByIdAsync(id, ct);
        return g is null ? null : MapGroup(g);
    }

    public async Task<(bool Success, string? Error, int? Id)> CreateGroupAsync(CreatePriceGroupRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.GroupCode))  return (false, "Grup kodu zorunludur.", null);
        if (string.IsNullOrWhiteSpace(req.GroupName))  return (false, "Grup adi zorunludur.", null);

        var entity = new PriceGroup
        {
            GroupCode   = req.GroupCode.Trim(),
            GroupName   = req.GroupName.Trim(),
            Description = req.Description?.Trim(),
            IsActive    = req.IsActive
        };
        var id = await _repo.AddGroupAsync(entity, ct);
        return (true, null, id);
    }

    public async Task<(bool Success, string? Error)> UpdateGroupAsync(UpdatePriceGroupRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.GroupCode)) return (false, "Grup kodu zorunludur.");
        if (string.IsNullOrWhiteSpace(req.GroupName)) return (false, "Grup adi zorunludur.");

        var entity = await _repo.GetGroupByIdAsync(req.Id, ct);
        if (entity is null) return (false, "Grup bulunamadi.");

        entity.GroupCode   = req.GroupCode.Trim();
        entity.GroupName   = req.GroupName.Trim();
        entity.Description = req.Description?.Trim();
        entity.IsActive    = req.IsActive;
        entity.UpdatedAt   = DateTime.Now;

        await _repo.UpdateGroupAsync(entity, ct);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> DeleteGroupAsync(int id, CancellationToken ct)
    {
        await _repo.DeleteGroupAsync(id, ct);
        return (true, null);
    }

    // ── Fiyat Kalemleri ──────────────────────────────────────────────────────

    public async Task<IReadOnlyCollection<PriceListEntryDto>> GetEntriesByGroupAsync(int groupId, CancellationToken ct)
    {
        var entries = await _repo.GetEntriesByGroupAsync(groupId, ct);
        return entries.Select(MapEntry).ToArray();
    }

    public async Task<(bool Success, string? Error, int? Id)> SaveEntryAsync(SavePriceListEntryRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.MaterialCode)) return (false, "Malzeme kodu zorunludur.", null);

        if (req.Id.HasValue && req.Id.Value > 0)
        {
            var existing = await _repo.GetEntryByIdAsync(req.Id.Value, ct);
            if (existing is null) return (false, "Kayit bulunamadi.", null);

            existing.MaterialCode  = req.MaterialCode.Trim();
            existing.MaterialName  = req.MaterialName?.Trim();
            existing.Currency      = req.Currency;
            existing.BuyingPrice   = req.BuyingPrice;
            existing.SellingPrice  = req.SellingPrice;
            existing.ValidFrom     = req.ValidFrom;
            existing.ValidTo       = req.ValidTo;
            existing.IsActive      = req.IsActive;
            existing.UpdatedAt     = DateTime.Now;

            await _repo.UpdateEntryAsync(existing, ct);
            return (true, null, existing.Id);
        }
        else
        {
            var entity = new PriceListEntry
            {
                PriceGroupId  = req.PriceGroupId,
                StockCardId   = req.StockCardId,
                MaterialCode  = req.MaterialCode.Trim(),
                MaterialName  = req.MaterialName?.Trim(),
                Currency      = req.Currency,
                BuyingPrice   = req.BuyingPrice,
                SellingPrice  = req.SellingPrice,
                ValidFrom     = req.ValidFrom,
                ValidTo       = req.ValidTo,
                IsActive      = req.IsActive
            };
            var id = await _repo.AddEntryAsync(entity, ct);
            return (true, null, id);
        }
    }

    public async Task<(bool Success, string? Error)> UpdateEntryPricesAsync(UpdatePriceEntryRequest req, CancellationToken ct)
    {
        var existing = await _repo.GetEntryByIdAsync(req.Id, ct);
        if (existing is null) return (false, "Kayit bulunamadi.");
        existing.BuyingPrice  = req.BuyingPrice;
        existing.SellingPrice = req.SellingPrice;
        existing.UpdatedAt    = DateTime.Now;
        await _repo.UpdateEntryAsync(existing, ct);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> DeleteEntryAsync(int id, CancellationToken ct)
    {
        await _repo.DeleteEntryAsync(id, ct);
        return (true, null);
    }

    // ── Toplu Fiyat Girisi ──────────────────────────────────────────────────

    public async Task<(bool Success, string? Error, int Count)> SaveBulkEntriesAsync(SaveBulkPriceEntriesRequest req, CancellationToken ct)
    {
        if (req.PriceGroupId <= 0) return (false, "Fiyat grubu secilmedi.", 0);
        if (req.Lines == null || req.Lines.Count == 0) return (false, "En az bir malzeme secilmelidir.", 0);
        if (string.IsNullOrWhiteSpace(req.Currency)) return (false, "Doviz tipi secilmedi.", 0);

        var entities = req.Lines.Select(line => new PriceListEntry
        {
            PriceGroupId = req.PriceGroupId,
            StockCardId  = line.StockCardId,
            MaterialCode = line.MaterialCode.Trim(),
            MaterialName = line.MaterialName?.Trim(),
            Currency     = req.Currency,
            BuyingPrice  = line.BuyingPrice,
            SellingPrice = line.SellingPrice,
            ValidFrom    = req.ValidFrom,
            ValidTo      = req.ValidTo,
            IsActive     = true
        }).ToArray();

        await _repo.AddBulkEntriesAsync(entities, ct);
        return (true, null, entities.Length);
    }

    // ── Mapper'lar ────────────────────────────────────────────────────────────

    private static PriceGroupDto MapGroup(PriceGroup g) => new(
        g.Id, g.GroupCode, g.GroupName, g.Description, g.IsActive, g.CreatedAt, g.UpdatedAt);

    private static PriceListEntryDto MapEntry(PriceListEntry e) => new(
        e.Id, e.PriceGroupId, e.StockCardId,
        e.MaterialCode, e.MaterialName,
        e.Currency, e.BuyingPrice, e.SellingPrice,
        e.ValidFrom, e.ValidTo, e.IsActive,
        e.CreatedAt, e.UpdatedAt);
}
