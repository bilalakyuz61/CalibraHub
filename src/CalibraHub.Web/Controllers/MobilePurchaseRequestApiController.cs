using System.Security.Claims;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Mobil "İhtiyaç Kaydı" (alis_talebi) — sahadan talep açma + kendi taleplerini izleme.
///
/// Uclar:
///   GET  /api/mobile/purchase-requests        — talepler (varsayilan: BENIM taleplerim)
///   GET  /api/mobile/purchase-requests/{id}   — talep detayi (kalemler + karsilama durumu)
///   POST /api/mobile/purchase-requests        — yeni talep
///
/// TASARIM — is mantigi TEKRARLANMAZ: kayit web ile AYNI kapidan gecer
/// (<see cref="IDocumentService.SaveQuoteAsync"/>), dolayisiyla belge numarasi uretimi,
/// onay akisi tetikleme, audit ve dogrulama kurallari birebir aynidir. Bu controller yalnizca
/// mobil icin SADELESTIRILMIS bir govde alir ve tam istege cevirir.
///
/// SADELESTIRME (bilincli): telefonda para birimi / iskonto / KDV / cari alanlari YOKTUR.
///   - Cari: İhtiyaç Kaydı zaten IC bir belgedir; servis bu belge tipini cari zorunlulugundan
///     MUAF tutar (DocumentService "isInternalProcurementDoc"). Mobil de göndermez.
///   - Fiyat/iskonto/KDV: talep asamasinda fiyat bilinmez; satirlar 0 fiyatla acilir ve
///     fiyatlandirma satin alma tarafinda (web) yapilir. Bu, web'in kendi davranisiyla
///     tutarlidir — İhtiyaç Kaydı ekraninda da fiyat beklenmez.
///   - Para birimi: sirketin varsayilan para birimi kullanilir (belge tutari 0 oldugu icin
///     etkisi yok, ama CurrencyId zorunlu bir alan).
///
/// TALEP EDEN: servis bu belge tipinde RequesterPersonnelId'yi ZORUNLU kilar. Mobilde bu alan
/// kullaniciya SORULMAZ — giris yapmis kullanicinin bagli personel kaydindan (Personnel.UserId)
/// cozulur. Bagli personel yoksa istek net bir mesajla reddedilir (sessizce baskasi adina
/// talep acilmaz).
/// </summary>
[ApiController]
[Route("api/mobile/purchase-requests")]
[IgnoreAntiforgeryToken]
[EnableCors("MobileApi")]
[Authorize]
[CalibraHub.Web.Authorization.PermissionScope(FormCodes.PurchaseRequest)]
public sealed class MobilePurchaseRequestApiController : ControllerBase
{
    private const string PurchaseRequestTypeCode = "alis_talebi";

    private readonly IDocumentService _documents;
    private readonly IDocumentRepository _documentRepo;
    private readonly IDocumentTypeRepository _documentTypes;
    private readonly IPersonnelService _personnel;
    private readonly ICurrencyRepository _currencies;
    private readonly ILogger<MobilePurchaseRequestApiController> _logger;

    public MobilePurchaseRequestApiController(
        IDocumentService documents,
        IDocumentRepository documentRepo,
        IDocumentTypeRepository documentTypes,
        IPersonnelService personnel,
        ICurrencyRepository currencies,
        ILogger<MobilePurchaseRequestApiController> logger)
    {
        _documents = documents;
        _documentRepo = documentRepo;
        _documentTypes = documentTypes;
        _personnel = personnel;
        _currencies = currencies;
        _logger = logger;
    }

    /// <summary>
    /// Talep listesi. <paramref name="mine"/> varsayilan TRUE — telefonda birincil ihtiyac
    /// "benim taleplerim ne durumda". false verilirse (yetki zaten kontrol edildi) tum
    /// talepler doner; veri perdeleme kurallari repository katmaninda uygulanmaya devam eder.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool mine = true, [FromQuery] int? take = null, CancellationToken ct = default)
    {
        // Repository belge tipini KODLA suzer (entity doner, DTO degil).
        var all = await _documentRepo.GetByTypeAsync(PurchaseRequestTypeCode, null, null, ct);

        IEnumerable<CalibraHub.Domain.Entities.Document> filtered = all;
        if (mine)
        {
            var me = await ResolveMyPersonnelIdAsync(ct);
            // Bagli personel yoksa BOS liste doner — "hepsini goster"e DUSMEZ. Aksi halde
            // "benim taleplerim" filtresi sessizce baskalarinin taleplerini gosterirdi.
            filtered = me is null
                ? Array.Empty<CalibraHub.Domain.Entities.Document>()
                : all.Where(d => d.RequesterPersonnelId == me.Value);
        }

        var limit = take.GetValueOrDefault(50);
        if (limit <= 0) limit = 50;
        if (limit > 200) limit = 200;

        var rows = filtered
            .OrderByDescending(d => d.DocumentDate)
            .ThenByDescending(d => d.Id)
            .Take(limit)
            .Select(d => new
            {
                id = d.Id,
                documentNumber = d.DocumentNumber,
                documentDate = d.DocumentDate,
                // Enum -> string: JsonStringEnumConverter zaten string uretir, acikca yaziyoruz
                // ki sozlesme istemci tarafinda net olsun.
                status = d.Status.ToString(),
                notes = d.Notes,
            });

        return Ok(rows);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id, CancellationToken ct)
    {
        var doc = await _documentRepo.GetByIdAsync(id, ct);
        if (doc is null || doc.DocumentTypeId is null)
            return NotFound(new { error = "Talep bulunamadı." });

        // Tip DOGRULANIR: baska bir belge tipinin id'si verilerek bu uctan okunamasin.
        var type = await _documentTypes.GetByCodeAsync(PurchaseRequestTypeCode, ct);
        if (type is null || doc.DocumentTypeId != type.Id)
            return NotFound(new { error = "Bu belge bir İhtiyaç Kaydı değil." });

        var lines = await _documentRepo.GetLinesAsync(id, ct);

        return Ok(new
        {
            id = doc.Id,
            documentNumber = doc.DocumentNumber,
            documentDate = doc.DocumentDate,
            status = doc.Status.ToString(),
            notes = doc.Notes,
            lines = lines.Select(l => new
            {
                id = l.Id,
                itemId = l.ItemId,
                materialCode = l.MaterialCode,
                materialName = l.MaterialName,
                quantity = l.Quantity,
                deliveredQuantity = l.DeliveredQuantity,
                notes = l.Notes,
            }),
        });
    }

    /// <summary>Mobil talep govdesi — sadelestirilmis (bkz. sinif KDoc'u).</summary>
    public sealed record MobilePurchaseRequestBody(
        IReadOnlyList<MobilePurchaseRequestLine>? Lines,
        string? Note,
        int? LocationId,
        DateTime? NeededDate);

    public sealed record MobilePurchaseRequestLine(int ItemId, decimal Quantity, string? Note);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] MobilePurchaseRequestBody? body, CancellationToken ct)
    {
        var lines = (body?.Lines ?? Array.Empty<MobilePurchaseRequestLine>())
            .Where(l => l is not null)
            .ToList();

        // Sessiz atlama YOK: gecersiz satir bulunursa istek acikca reddedilir
        // (CLAUDE.md kural 3 — kullanicinin girdigi kalem sessizce kaybolmamali).
        if (lines.Count == 0)
            return BadRequest(new { ok = false, error = "En az bir malzeme satırı eklenmeli." });
        if (lines.Any(l => l.ItemId <= 0))
            return BadRequest(new { ok = false, error = "Geçersiz malzeme satırı var." });
        if (lines.Any(l => l.Quantity <= 0m))
            return BadRequest(new { ok = false, error = "Tüm satırlarda miktar sıfırdan büyük olmalı." });

        var requesterId = await ResolveMyPersonnelIdAsync(ct);
        if (requesterId is null)
            return BadRequest(new
            {
                ok = false,
                error = "Kullanıcınıza bağlı bir personel kaydı bulunamadı. " +
                        "İhtiyaç Kaydı 'Talep Eden' personel gerektirir — yöneticinizden personel kartınıza kullanıcınızı bağlamasını isteyin.",
            });

        var type = await _documentTypes.GetByCodeAsync(PurchaseRequestTypeCode, ct);
        if (type is null)
            return BadRequest(new { ok = false, error = "İhtiyaç Kaydı belge tipi tanımlı değil." });

        var currencyId = await ResolveDefaultCurrencyIdAsync(ct);
        if (currencyId is null)
            return BadRequest(new { ok = false, error = "Varsayılan para birimi bulunamadı." });

        var request = new SaveDocumentRequest(
            Id: null,
            DocumentDate: DateTime.Now,
            ValidUntil: body?.NeededDate,
            ContactId: null,                 // IC belge — servis bu tipi cari zorunlulugundan muaf tutar
            ContactName: null,
            ContactAddress: null,
            SalesRepId: null,
            CurrencyId: currencyId.Value,
            DiscountRate: 0m,
            TaxRate: 0m,
            PaymentTerms: null,
            DeliveryTerms: null,
            DeliveryAddress: null,
            Notes: string.IsNullOrWhiteSpace(body?.Note) ? null : body!.Note!.Trim(),
            Lines: lines.Select(l => new SaveDocumentLineRequest(
                Id: null,
                ItemId: l.ItemId,
                UnitId: null,                // null → servis malzemenin ana birimine duser
                Quantity: l.Quantity,
                UnitPrice: 0m,               // talep asamasinda fiyat bilinmez (web ile ayni)
                DiscountRate: 0m,
                CombinationId: null,
                LocationId: null,
                Notes: string.IsNullOrWhiteSpace(l.Note) ? null : l.Note.Trim())).ToList(),
            DocumentTypeId: type.Id,
            RequesterPersonnelId: requesterId.Value,
            LocationId: body?.LocationId is > 0 ? body.LocationId : null);

        try
        {
            var userId = CurrentUserIdOrNull();
            var (success, error, doc, approvalStarted) =
                await _documents.SaveQuoteAsync(request, userId, User?.Identity?.Name, ct);

            if (!success || doc is null)
                return BadRequest(new { ok = false, error = error ?? "Talep kaydedilemedi." });

            return Ok(new
            {
                ok = true,
                id = doc.Id,
                documentNumber = doc.DocumentNumber,
                status = doc.Status,
                // Onay akisi tanimliysa belge kaydedilir kaydedilmez onaya duser; kullaniciya
                // "kaydedildi" demek yetmez, "onaya gitti" bilgisi de gerekir.
                approvalStarted,
            });
        }
        catch (Exception ex)
        {
            // CLAUDE.md kural 2 — mutasyon ucunda exception SUNUCUYA loglanir, istemciye jenerik doner.
            _logger.LogError(ex, "[MobilePurchaseRequest] Talep kaydedilemedi. User={User}", User?.Identity?.Name);
            return BadRequest(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    /// <summary>Giris yapmis kullaniciya bagli personel Id'si (Personnel.UserId) — yoksa null.</summary>
    private async Task<int?> ResolveMyPersonnelIdAsync(CancellationToken ct)
    {
        var userId = CurrentUserIdOrNull();
        if (userId is null) return null;
        var me = await _personnel.GetByUserIdAsync(userId.Value, ct);
        return me?.Id;
    }

    private int? CurrentUserIdOrNull()
        => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id > 0 ? id : null;

    private async Task<int?> ResolveDefaultCurrencyIdAsync(CancellationToken ct)
    {
        var all = await _currencies.GetAllAsync(ct);
        var active = all.Where(c => c.IsActive).ToList();
        if (active.Count == 0) return null;
        // Currency'de "varsayilan" bayragi YOK — TRY tercih edilir, yoksa ilk aktif kayit.
        // Talep satirlari 0 fiyatla acildigi icin secim tutari ETKILEMEZ; alan yalnizca
        // NOT NULL oldugu icin doldurulur.
        return active.FirstOrDefault(c => string.Equals(c.Code, "TRY", StringComparison.OrdinalIgnoreCase))?.Id
               ?? active[0].Id;
    }
}
