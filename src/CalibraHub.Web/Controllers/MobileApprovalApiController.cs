using System.Security.Claims;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Mobil "Onay Bekleyenler" — web'deki /PendingApproval ekraninin JSON karsiligi.
///
/// Uclar:
///   GET  /api/mobile/approvals                  — bana atanmis bekleyen onaylar
///   GET  /api/mobile/approvals/{instanceId}     — tek onayin detayi (kalemler dahil)
///   POST /api/mobile/approvals/{instanceId}/approve  — onayla
///   POST /api/mobile/approvals/{instanceId}/reject   — reddet (not ZORUNLU)
///
/// TASARIM: is mantigi TEKRARLANMAZ. Listeleme [IPendingApprovalService]'e, karar
/// [IApprovalFlowService]'e devredilir — web ekraniyla AYNI servisler, dolayisiyla
/// ayni kapsam (scope) kontrolu, ayni adim ilerletme/durum guncelleme davranisi.
///
/// KAPSAM: mobil YALNIZCA `scope = mine` kullanir. Web'de kullanicinin yetkisine gore
/// department/all secenekleri de var; telefonda "baskasinin onayini gor" ihtiyaci yok ve
/// kapsam genisletmek yetki yuzeyini bilerek buyutmek olurdu. Kapsamin izinli olup
/// olmadigi yine SERVIS icinde dogrulanir (EnsureScopeAllowedAsync) — burada tekrar edilmez.
///
/// YETKI: web ekraniyla ayni form kodu ([FormCodes.ApprovalPending]). Onay/red kararinda
/// approverId HER ZAMAN oturum claim'inden alinir; istemci govdesindeki bir deger
/// KULLANILMAZ (ApprovalFlowController.ApproveStep ile ayni karar — baskasi adina
/// onaylama girisimi mumkun olmamali).
/// </summary>
[ApiController]
[Route("api/mobile/approvals")]
[IgnoreAntiforgeryToken]
[EnableCors("MobileApi")]
[Authorize]
[CalibraHub.Web.Authorization.PermissionScope(FormCodes.ApprovalPending)]
public sealed class MobileApprovalApiController : ControllerBase
{
    private readonly IPendingApprovalService _pending;
    private readonly IApprovalFlowService _flow;
    private readonly ILogger<MobileApprovalApiController> _logger;

    public MobileApprovalApiController(
        IPendingApprovalService pending,
        IApprovalFlowService flow,
        ILogger<MobileApprovalApiController> logger)
    {
        _pending = pending;
        _flow = flow;
        _logger = logger;
    }

    /// <summary>Bana atanmis bekleyen onaylar — mobil kart listesi icin sadelestirilmis alanlar.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var items = await _pending.GetListAsync(PendingApprovalScope.Mine, null, ct);
        return Ok(items.Select(ToListRow));
    }

    /// <summary>Tek onayin detayi. Yetki/kapsam kontrolu servis icinde tekrar yapilir.</summary>
    [HttpGet("{instanceId:int}")]
    public async Task<IActionResult> Detail(int instanceId, CancellationToken ct)
    {
        var detail = await _pending.GetDetailAsync(instanceId, PendingApprovalScope.Mine, ct);
        if (detail is null)
            return NotFound(new { error = "Onay kaydı bulunamadı veya görme yetkiniz yok." });

        return Ok(detail);
    }

    [HttpPost("{instanceId:int}/approve")]
    public async Task<IActionResult> Approve(int instanceId, [FromBody] MobileApprovalDecision? body, CancellationToken ct)
        => await DecideAsync(instanceId, approve: true, note: body?.Note, ct);

    [HttpPost("{instanceId:int}/reject")]
    public async Task<IActionResult> Reject(int instanceId, [FromBody] MobileApprovalDecision? body, CancellationToken ct)
        => await DecideAsync(instanceId, approve: false, note: body?.Note, ct);

    /// <summary>
    /// Onay/red ortak yolu.
    ///
    /// GORME YETKISI ONCE DOGRULANIR: karar vermeden once [IPendingApprovalService.GetDetailAsync]
    /// `scope = mine` ile cagrilir. Bu, "bana atanmamis bir instanceId'yi tahmin edip onaylama"
    /// yolunu kapatir — ApproveStepAsync tek basina bunu garanti etmez.
    ///
    /// REDDETME NOTU ZORUNLU: [RejectStepRequest.Note] nullable degil ve red gerekcesi
    /// onay gecmisinin en kiymetli alani. Bos not sessizce "-" gibi bir doldurmaya
    /// cevrilmez; istek REDDEDILIR (bkz. CLAUDE.md "sessiz kirik" kurallari).
    /// </summary>
    private async Task<IActionResult> DecideAsync(int instanceId, bool approve, string? note, CancellationToken ct)
    {
        var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (!approve && trimmedNote is null)
            return BadRequest(new { ok = false, error = "Reddetme gerekçesi zorunludur." });

        var visible = await _pending.GetDetailAsync(instanceId, PendingApprovalScope.Mine, ct);
        if (visible is null)
            return NotFound(new { ok = false, error = "Onay kaydı bulunamadı veya bu onay size atanmamış." });

        var approverId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var approverName = User.FindFirstValue(ClaimTypes.Name) ?? "system";

        try
        {
            if (approve)
            {
                await _flow.ApproveStepAsync(
                    new ApproveStepRequest(instanceId, approverId, approverName, trimmedNote), ct);
            }
            else
            {
                await _flow.RejectAsync(
                    new RejectStepRequest(instanceId, approverId, approverName, trimmedNote!), ct);
            }
            return Ok(new { ok = true });
        }
        catch (InvalidOperationException ex)
        {
            // Is kurali reddi (adim zaten kapanmis, siradaki onaycı degilsiniz vb.) —
            // mesaji kullaniciya GOSTER, ic detay icermez.
            return BadRequest(new { ok = false, error = ex.Message });
        }
        catch (Exception ex)
        {
            // CLAUDE.md kural 2: mutasyon ucunda exception SUNUCUYA loglanir, istemciye jenerik doner.
            _logger.LogError(ex, "[MobileApproval] Karar basarisiz. InstanceId={InstanceId} Approve={Approve} User={User}",
                instanceId, approve, approverId);
            return BadRequest(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    /// <summary>
    /// Liste satiri — telefon karti icin gerekli ALANLAR ile sinirli. Web'in
    /// PendingApprovalItemDto'su ~20 alan tasiyor (adim sirasi, akis id, ek view kolonlari...);
    /// mobil listede hicbiri gosterilmiyor, hepsini gondermek bant genisligi israfi olurdu.
    /// Detay ucu tam DTO'yu doner.
    /// </summary>
    private static object ToListRow(PendingApprovalItemDto i) => new
    {
        instanceId = i.InstanceId,
        stepName = i.StepName,
        flowName = i.FlowName,
        stepPosition = i.StepPosition,
        totalSteps = i.TotalSteps,
        documentNumber = i.DocumentNumber,
        documentDate = i.DocumentDate,
        documentTypeName = i.DocumentTypeName,
        contactName = i.ContactName,
        grandTotal = i.GrandTotal,
        currencyCode = i.CurrencyCode,
        waitingSince = i.StepCreated,
    };

    /// <summary>Onay/red govdesi — not opsiyonel (onayda), reddetmede ZORUNLU (bkz. DecideAsync).</summary>
    public sealed record MobileApprovalDecision(string? Note);
}
