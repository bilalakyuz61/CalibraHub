using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Mobil "Cari Kartı" — SALT OKUNUR görüntüleme.
///
/// Uclar:
///   GET /api/mobile/contacts?q=&amp;take=   — arama (kod/unvan/vergi no)
///   GET /api/mobile/contacts/{id}          — kart detayi
///
/// KAPSAM NOTU — BAKIYE YOK: CalibraHub'da cari bakiye/borc HESAPLANMAZ. Sistemde
/// tahsilat/odeme modulu yok (ne tablo ne servis; `fn_GetContactBalance` yalnizca
/// entegrasyon dokumantasyonunda ORNEK bir isim). Fatura toplamlarindan "bakiye" turetmek
/// odenen tutari goremeyecegi icin YANLIS bir rakam uretirdi — kullaniciya yanlis bakiye
/// gostermektense hic gostermemek dogru. Bakiye gerekiyorsa dogru kaynak ERP entegrasyonudur
/// (ayri bir is: entegrasyondan bakiye cekip cariye baglamak).
///
/// Yazma yok: mobil cari OLUSTURMAZ/DEGISTIRMEZ (VKN/TCKN dogrulamasi, fiyat grubu,
/// vergi dairesi gibi alanlar telefonda dogru girilmesi zor ve hatali kayit temizlemesi
/// pahali). Duzenleme web'de kalir.
/// </summary>
[ApiController]
[Route("api/mobile/contacts")]
[IgnoreAntiforgeryToken]
[EnableCors("MobileApi")]
[Authorize]
[CalibraHub.Web.Authorization.PermissionScope(FormCodes.Contacts)]
public sealed class MobileContactApiController : ControllerBase
{
    private readonly IFinanceService _finance;

    public MobileContactApiController(IFinanceService finance)
    {
        _finance = finance;
    }

    /// <summary>
    /// Arama — bos sorgu BOS LISTE doner (tum carileri dokmez: telefonda binlerce satir
    /// hem yavas hem anlamsiz; kullanici once yazmali).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string? q, [FromQuery] int? take, CancellationToken ct)
    {
        var query = (q ?? string.Empty).Trim();
        if (query.Length == 0) return Ok(Array.Empty<object>());

        var pageSize = take.GetValueOrDefault(20);
        if (pageSize <= 0) pageSize = 20;
        if (pageSize > 50) pageSize = 50;

        var (items, _) = await _finance.GetContactsPagedAsync(null, query, 0, pageSize, ct);
        return Ok(items.Where(c => c.IsActive).Select(ToListRow));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id, CancellationToken ct)
    {
        var c = await _finance.GetContactByIdAsync(id, ct);
        if (c is null) return NotFound(new { error = "Cari bulunamadı." });

        return Ok(new
        {
            id = c.Id,
            code = c.AccountCode,
            title = c.AccountTitle,
            accountType = c.AccountType,
            taxNumber = c.TaxNumber,
            identityNumber = c.IdentityNumber,
            taxOffice = c.TaxOffice,
            phone = c.Phone,
            mobile = c.Mobile,
            email = c.Email,
            contactPerson = c.ContactPerson,
            address = c.Address,
            neighborhood = c.Neighborhood,
            district = c.District,
            city = c.City,
            postalCode = c.PostalCode,
            isActive = c.IsActive,
        });
    }

    /// <summary>Liste satiri — kartta gosterilen dort alan (bant genisligi icin daraltildi).</summary>
    private static object ToListRow(ContactDto c) => new
    {
        id = c.Id,
        code = c.AccountCode,
        title = c.AccountTitle,
        city = c.City,
        phone = string.IsNullOrWhiteSpace(c.Phone) ? c.Mobile : c.Phone,
    };
}
