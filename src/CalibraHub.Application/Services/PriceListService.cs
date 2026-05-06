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
        if (string.IsNullOrWhiteSpace(req.Code))  return (false, "Grup kodu zorunludur.", null);
        if (string.IsNullOrWhiteSpace(req.Name))  return (false, "Grup adi zorunludur.", null);
        if (!req.AllowsBuying && !req.AllowsSelling && !req.AllowsCost)
            return (false, "En az bir fiyat tipi (Alis/Satis/Maliyet) izinli olmali.", null);

        var entity = new PriceGroup
        {
            Code          = req.Code.Trim(),
            Name          = req.Name.Trim(),
            Description   = req.Description?.Trim(),
            IsActive      = req.IsActive,
            AllowsBuying  = req.AllowsBuying,
            AllowsSelling = req.AllowsSelling,
            AllowsCost    = req.AllowsCost
        };
        var id = await _repo.AddGroupAsync(entity, ct);
        return (true, null, id);
    }

    public async Task<(bool Success, string? Error)> UpdateGroupAsync(UpdatePriceGroupRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Code)) return (false, "Grup kodu zorunludur.");
        if (string.IsNullOrWhiteSpace(req.Name)) return (false, "Grup adi zorunludur.");
        if (!req.AllowsBuying && !req.AllowsSelling && !req.AllowsCost)
            return (false, "En az bir fiyat tipi (Alis/Satis/Maliyet) izinli olmali.");

        var entity = await _repo.GetGroupByIdAsync(req.Id, ct);
        if (entity is null) return (false, "Grup bulunamadi.");

        entity.Code          = req.Code.Trim();
        entity.Name          = req.Name.Trim();
        entity.Description   = req.Description?.Trim();
        entity.IsActive      = req.IsActive;
        entity.AllowsBuying  = req.AllowsBuying;
        entity.AllowsSelling = req.AllowsSelling;
        entity.AllowsCost    = req.AllowsCost;
        entity.UpdatedAt     = DateTime.Now;

        await _repo.UpdateGroupAsync(entity, ct);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> DeleteGroupAsync(int id, CancellationToken ct)
    {
        await _repo.DeleteGroupAsync(id, ct);
        return (true, null);
    }

    // ── Fiyat Kalemleri (her satir TEK fiyat: PriceType + Price) ──────────────

    public async Task<PagedPriceListResult> GetEntriesByGroupAsync(
        int groupId, PriceListFilter filter, CancellationToken ct)
        => await _repo.GetEntriesByGroupAsync(groupId, filter, ct);

    public async Task<(bool Success, string? Error, int? Id)> SaveEntryAsync(SavePriceListRequest req, CancellationToken ct)
    {
        if (req.GroupId <= 0)    return (false, "Fiyat grubu secilmedi.", null);
        if (req.ItemId <= 0)     return (false, "Malzeme secilmedi.", null);
        if (req.CurrencyId <= 0) return (false, "Doviz secilmedi.", null);
        if (!IsValidPriceType(req.PriceType)) return (false, "Fiyat tipi 'b' (alış), 's' (satış) veya 'm' (maliyet) olmali.", null);

        // Grup-tipi kisidi: bu grup bu tipe izin veriyor mu?
        var groupCheck = await _repo.GetGroupByIdAsync(req.GroupId, ct);
        if (groupCheck is null) return (false, "Fiyat grubu bulunamadi.", null);
        var typeAllowedErr = AssertTypeAllowed(groupCheck, NormalizePriceType(req.PriceType));
        if (typeAllowedErr is not null) return (false, typeAllowedErr, null);

        if (req.Id.HasValue && req.Id.Value > 0)
        {
            var existing = await _repo.GetEntryByIdAsync(req.Id.Value, ct);
            if (existing is null) return (false, "Kayit bulunamadi.", null);

            existing.ItemId     = req.ItemId;
            existing.ConfigId   = req.ConfigId;
            existing.CurrencyId = req.CurrencyId;
            existing.PriceType  = NormalizePriceType(req.PriceType);
            existing.Price      = req.Price;
            existing.ValidFrom  = req.ValidFrom;
            existing.ValidTo    = req.ValidTo;
            existing.IsActive   = req.IsActive;
            existing.UpdatedAt  = DateTime.Now;

            await _repo.UpdateEntryAsync(existing, ct);
            return (true, null, existing.Id);
        }
        else
        {
            var entity = new PriceList
            {
                GroupId    = req.GroupId,
                ItemId     = req.ItemId,
                ConfigId   = req.ConfigId,
                CurrencyId = req.CurrencyId,
                PriceType  = NormalizePriceType(req.PriceType),
                Price      = req.Price,
                ValidFrom  = req.ValidFrom,
                ValidTo    = req.ValidTo,
                IsActive   = req.IsActive
            };
            var id = await _repo.AddEntryAsync(entity, ct);
            return (true, null, id);
        }
    }

    public async Task<(bool Success, string? Error)> UpdateEntryPricesAsync(UpdatePriceEntryRequest req, CancellationToken ct)
    {
        var existing = await _repo.GetEntryByIdAsync(req.Id, ct);
        if (existing is null) return (false, "Kayit bulunamadi.");
        existing.Price     = req.Price;
        existing.UpdatedAt = DateTime.Now;
        await _repo.UpdateEntryAsync(existing, ct);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> DeleteEntryAsync(int id, CancellationToken ct)
    {
        await _repo.DeleteEntryAsync(id, ct);
        return (true, null);
    }

    // ── Toplu Fiyat Girisi (Upsert) — wizard'da secilen PriceType ile ────────

    public async Task<(bool Success, string? Error, int Inserted, int Updated)> SaveBulkEntriesAsync(SaveBulkPriceEntriesRequest req, CancellationToken ct)
    {
        if (req.GroupId <= 0) return (false, "Fiyat grubu secilmedi.", 0, 0);
        if (req.Lines == null || req.Lines.Count == 0) return (false, "En az bir malzeme secilmelidir.", 0, 0);
        if (req.CurrencyId <= 0) return (false, "Doviz tipi secilmedi.", 0, 0);
        if (!IsValidPriceType(req.PriceType)) return (false, "Fiyat tipi 'b' (alış), 's' (satış) veya 'm' (maliyet) olmali.", 0, 0);

        // Grup-tipi kisidi
        var grpCheck = await _repo.GetGroupByIdAsync(req.GroupId, ct);
        if (grpCheck is null) return (false, "Fiyat grubu bulunamadi.", 0, 0);
        var bulkTypeErr = AssertTypeAllowed(grpCheck, NormalizePriceType(req.PriceType));
        if (bulkTypeErr is not null) return (false, bulkTypeErr, 0, 0);

        var normalizedType = NormalizePriceType(req.PriceType);
        var entities = req.Lines.Select(line => new PriceList
        {
            GroupId    = req.GroupId,
            ItemId     = line.ItemId,
            ConfigId   = line.ConfigId,
            CurrencyId = req.CurrencyId,
            PriceType  = normalizedType,
            Price      = line.Price,
            ValidFrom  = req.ValidFrom,
            ValidTo    = req.ValidTo,
            IsActive   = true
        }).ToArray();

        var result = await _repo.UpsertBulkEntriesAsync(entities, ct);
        return (true, null, result.Inserted, result.Updated);
    }

    // ── Mevcut Fiyat Sorgusu ────────────────────────────────────────────────

    public async Task<IReadOnlyCollection<ExistingPriceRow>> GetExistingPricesAsync(GetExistingPricesRequest req, CancellationToken ct)
    {
        if (req.GroupId <= 0 || req.CurrencyId <= 0 || req.Keys == null || req.Keys.Count == 0)
            return Array.Empty<ExistingPriceRow>();
        if (!IsValidPriceType(req.PriceType))
            return Array.Empty<ExistingPriceRow>();

        return await _repo.GetExistingPricesAsync(
            req.GroupId, req.CurrencyId, req.PriceType, req.ValidFrom, req.Keys, ct);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    // Tek harf kod: "b" (buying / alis), "s" (selling / satis), "m" (maliyet / cost).
    // Case-insensitive kabul: kullanici/UI 'B' gondermis olsa bile 'b' olarak normalize ediyoruz
    // — DB'de tutarsiz buyuk/kucuk harf birikmesin diye PriceType'i hep lowercase saklamali.
    private static bool IsValidPriceType(string? pt) =>
        NormalizePriceType(pt) is "b" or "s" or "m";

    private static string NormalizePriceType(string? pt) =>
        string.IsNullOrWhiteSpace(pt) ? string.Empty : pt.Trim().ToLowerInvariant();

    private static PriceGroupDto MapGroup(PriceGroup g) => new(
        g.Id, g.Code, g.Name, g.Description, g.IsActive,
        g.AllowsBuying, g.AllowsSelling, g.AllowsCost,
        g.CreatedAt, g.UpdatedAt);

    /// <summary>
    /// Bu grup verilen fiyat tipini (lowercase 'b'/'s'/'m') kabul ediyor mu?
    /// Reddederse kullaniciya gosterilecek hata mesajini doner; kabul ediyorsa null doner.
    /// </summary>
    private static string? AssertTypeAllowed(PriceGroup g, string normalizedType) => normalizedType switch
    {
        "b" when !g.AllowsBuying  => $"'{g.Code}' grubu Alis fiyatini kabul etmiyor.",
        "s" when !g.AllowsSelling => $"'{g.Code}' grubu Satis fiyatini kabul etmiyor.",
        "m" when !g.AllowsCost    => $"'{g.Code}' grubu Maliyet fiyatini kabul etmiyor.",
        _ => null
    };
}
