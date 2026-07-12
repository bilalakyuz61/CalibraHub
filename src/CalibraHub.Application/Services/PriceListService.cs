using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Services;

public sealed class PriceListService : IPriceListService
{
    private readonly IPriceListRepository _repo;
    private readonly IFinanceService _finance;

    public PriceListService(IPriceListRepository repo, IFinanceService finance)
    {
        _repo = repo;
        _finance = finance;
    }

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
        // "Genel Liste" (default) grubu fallback icin zorunlu — silinemez.
        var grp = await _repo.GetGroupByIdAsync(id, ct);
        if (grp is null) return (false, "Grup bulunamadi.");
        if (grp.IsDefault) return (false, "Genel Liste silinemez. Once baska bir grubu Genel Liste yapin.");

        await _repo.DeleteGroupAsync(id, ct);
        return (true, null);
    }

    // Verilen grubu "Genel Liste" (default) yap — ayni company'de tek default garanti edilir.
    public async Task<(bool Success, string? Error)> SetDefaultGroupAsync(int groupId, CancellationToken ct)
    {
        var grp = await _repo.GetGroupByIdAsync(groupId, ct);
        if (grp is null) return (false, "Grup bulunamadi.");
        if (!grp.IsActive) return (false, "Pasif grup Genel Liste yapilamaz.");
        await _repo.SetDefaultGroupAsync(groupId, ct);
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

        // Kismi update — null gelen alan mevcut degerden korunur.
        if (req.Price.HasValue) existing.Price = req.Price.Value;
        if (req.CurrencyId.HasValue && req.CurrencyId.Value > 0) existing.CurrencyId = req.CurrencyId.Value;
        if (!string.IsNullOrWhiteSpace(req.PriceType))
        {
            var t = NormalizePriceType(req.PriceType);
            if (!IsValidPriceType(t))
                return (false, "Fiyat tipi 'b' (alış), 's' (satış) veya 'm' (maliyet) olmali.");
            // Grup-tipi kisidi
            var grp = await _repo.GetGroupByIdAsync(existing.GroupId, ct);
            if (grp is not null)
            {
                var typeErr = AssertTypeAllowed(grp, t);
                if (typeErr is not null) return (false, typeErr);
            }
            existing.PriceType = t;
        }
        if (req.ValidFrom.HasValue) existing.ValidFrom = req.ValidFrom.Value;
        if (req.ClearValidTo) existing.ValidTo = null;
        else if (req.ValidTo.HasValue) existing.ValidTo = req.ValidTo.Value;

        // ── Uniqueness ön-kontrol: aynı (Group + Item + Config + Currency + PriceType + ValidFrom)
        //    kombinasyonu ile başka aktif kayıt var mı? Varsa kullanıcıya açık hata göster.
        //    DB'deki UNIQUE INDEX zaten enforce ediyor; bu pre-check sadece daha okunabilir mesaj için.
        if (existing.IsActive)
        {
            var dup = await _repo.FindActiveDuplicateAsync(
                existing.GroupId, existing.ItemId, existing.ConfigId,
                existing.CurrencyId, existing.PriceType, existing.ValidFrom,
                excludeId: existing.Id, ct);
            if (dup != null)
            {
                return (false,
                    $"Aynı kombinasyonda başka bir aktif kayıt zaten var (ID: {dup.Id}). " +
                    "Fiyat grubu + stok + kombinasyon + döviz + tip + başlangıç tarihi aynı olan ikinci kayıt oluşturulamaz.");
            }
        }

        existing.UpdatedAt = DateTime.Now;
        try
        {
            await _repo.UpdateEntryAsync(existing, ct);
        }
        catch (Exception ex) when (
            ex.GetType().Name == "SqlException" &&
            (ex.Message.Contains("ux_pricelist_unique_active") ||
             ex.Message.Contains("UNIQUE KEY") ||
             ex.Message.Contains("duplicate key")))
        {
            // SQL UNIQUE INDEX ihlali — pre-check kacirdi (race condition?), DB son savunma hatti.
            // Application katmani Microsoft.Data.SqlClient'a referans vermiyor, type name kontrolu kullaniyoruz.
            return (false,
                "Aynı stok + kombinasyon + döviz + tip + başlangıç tarihi için zaten bir kayıt mevcut. " +
                "Yeni bir başlangıç tarihi girin veya mevcut kaydı düzeltin.");
        }
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

    // ── Fallback'li Fiyat Cozumu (belge/kit satiri) ─────────────────────────
    // Cari (varsa) → Contact.PriceGroupId listesi; yoksa/urun yoksa Genel Liste.
    // Kit rollup ileride ayni metodu bilesen key'leriyle cagirir (flat batch).
    public async Task<IReadOnlyCollection<ResolvedPriceRow>> ResolveLinePricesAsync(
        ResolveLinePricesRequest req, CancellationToken ct)
    {
        if (req.CurrencyId <= 0 || req.Keys is null || req.Keys.Count == 0)
            return Array.Empty<ResolvedPriceRow>();

        // Genel Liste (fallback) grubu — kullanici Fiyat Listesi ekranindan isaretler;
        // isaretlenmemisse null olabilir. O durumda yalnizca cari grubundan cozulur.
        var defaultGroupId = await _repo.GetDefaultGroupIdAsync(ct);

        // Cari → fiyat grubu.
        int? contactGroupId = null;
        if (req.ContactId is > 0)
        {
            var contact = await _finance.GetContactByIdAsync(req.ContactId.Value, ct);
            contactGroupId = contact?.PriceGroupId is > 0 ? contact.PriceGroupId : null;
        }

        // Ne cari grubu ne Genel Liste varsa cozecek kaynak yok.
        if (contactGroupId is null && (defaultGroupId is null || defaultGroupId.Value <= 0))
            return Array.Empty<ResolvedPriceRow>();

        var priceType = req.Direction switch
        {
            PriceDirection.Sales    => "s",
            PriceDirection.Purchase => "b",
            PriceDirection.Cost     => "m",
            _                       => "s"
        };

        // defaultGroupId null → 0 gecilir; repo'da 0 hicbir gruba eslesmez (yalniz cari grubu).
        return await _repo.ResolveExistingPricesAsync(
            contactGroupId, defaultGroupId ?? 0, req.CurrencyId, priceType, req.Date, req.Keys, ct);
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
        g.IsDefault,
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
