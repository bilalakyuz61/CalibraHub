using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Mail / WhatsApp mesajındaki onay bağlantılarını işler.
/// Kimlik doğrulama gerekmez; token + şirket ID kombinasyonu yetki olarak kullanılır.
///
/// Akış (2026-07-02 revizyonu — önizlemeli onay):
///   GET  /Approval/Quick?c={companyId}&t={token}&a=approve|reject
///        → İşlem YAPMAZ. Onaylayıcıya ne onayladığını gösteren önizleme sayfası:
///          belge özeti + firma-özel Belge Tasarımcısı şablonu (varsa) + Onayla/Reddet
///          butonları. `a` parametresi yalnızca hangi butonun vurgulanacağını belirler.
///   POST /Approval/Quick/Act  (c, t, a, note)
///        → Asıl onay/red işlemi + token consume + sonuç ekranı.
///
/// Önceki GET-ile-işlem davranışı kaldırıldı: mail istemcilerinin link ön-yüklemesi
/// (prefetch/scan) yanlışlıkla onay tetikleyebiliyordu; ayrıca onaylayıcı içeriği
/// görmeden işlem yapmış oluyordu.
/// </summary>
[AllowAnonymous]
[Route("Approval")]
[EnableRateLimiting("public-share")]
public sealed class QuickApprovalController : Controller
{
    private readonly IApprovalTokenRepository _tokenRepo;
    private readonly IApprovalFlowService _approvalService;
    private readonly IUserProfileRepository _userRepo;
    private readonly IApprovalInstanceRepository _instanceRepo;
    private readonly IDocumentRepository _documentRepo;
    private readonly IDocDesignerService? _designer;

    public QuickApprovalController(
        IApprovalTokenRepository tokenRepo,
        IApprovalFlowService approvalService,
        IUserProfileRepository userRepo,
        IApprovalInstanceRepository instanceRepo,
        IDocumentRepository documentRepo,
        IDocDesignerService? designer = null)
    {
        _tokenRepo       = tokenRepo;
        _approvalService = approvalService;
        _userRepo        = userRepo;
        _instanceRepo    = instanceRepo;
        _documentRepo    = documentRepo;
        _designer        = designer;
    }

    // ── GET: önizleme sayfası — onaylayıcı ne onayladığını görür ─────────────
    [HttpGet("Quick")]
    public async Task<IActionResult> Quick(int c, string? t, string? a, CancellationToken ct)
    {
        var validation = await ValidateTokenAsync(c, t, ct);
        if (validation.ErrorResult is not null) return validation.ErrorResult;
        var record = validation.Record!;

        HttpContext.Items["__override_company_id"] = c;

        // Instance + durum kontrolü — Pending değilse işlem sunulmaz.
        var instance = await _instanceRepo.GetByIdAsync(record.InstanceId, ct);
        if (instance is null)
            return QuickView(error: "Onay kaydı bulunamadı.");
        if (!string.Equals(instance.Status, "Pending", StringComparison.OrdinalIgnoreCase))
            return QuickView(error: "Bu onay talebi zaten sonuçlandırılmış.");

        var approverName = await ResolveApproverNameAsync(record.ApproverId, ct);

        // ── Belge bilgileri + firma-özel önizleme ────────────────────────────
        string? docNumber = null, contactName = null, amountText = null, dateText = null;
        string? previewHtml = null;
        if (instance.DocumentId.HasValue)
        {
            try
            {
                var document = await _documentRepo.GetByIdAsync(instance.DocumentId.Value, ct);
                if (document is not null)
                {
                    docNumber   = document.DocumentNumber;
                    contactName = document.ContactName;
                    dateText    = document.DocumentDate.ToString("dd.MM.yyyy");
                    if (document.GrandTotal > 0)
                        amountText = document.GrandTotal.ToString("N2");

                    // Firma-özel dizayn: belge tipine tanımlı Belge Tasarımcısı şablonu
                    // (IsDefault öncelikli). Firma tasarımı değiştirdiğinde onay önizlemesi
                    // otomatik yeni dizaynla gelir. Şablon yoksa yerleşik özet kartı kalır.
                    if (_designer is not null && document.DocumentTypeId.HasValue)
                    {
                        try
                        {
                            var layouts = await _designer.ListAsync(null, ct);
                            var layout = layouts
                                .Where(l => l.DocumentTypeId == document.DocumentTypeId.Value)
                                .OrderByDescending(l => l.IsDefault)
                                .ThenByDescending(l => l.UpdatedAt)
                                .FirstOrDefault();
                            if (layout is not null)
                            {
                                previewHtml = await _designer.RenderHtmlPreviewAsync(
                                    new DocLayoutRunRequest(layout.Id, document.Id, null), ct);
                            }
                        }
                        catch { /* şablon render edilemezse yerleşik özet gösterilir */ }
                    }
                }
            }
            catch { /* belge çekilemezse özet alanları boş kalır */ }
        }

        var currentStep = instance.StepRecords.FirstOrDefault(s => s.StepOrder == instance.CurrentStep);

        ViewBag.CompanyId    = c;
        ViewBag.Token        = t;
        ViewBag.Intent       = (a ?? "").ToLowerInvariant();  // approve | reject | ""
        ViewBag.ApproverName = approverName;
        ViewBag.FlowName     = instance.FlowName;
        ViewBag.StepName     = currentStep?.StepName;
        ViewBag.DocNumber    = docNumber;
        ViewBag.ContactName  = contactName;
        ViewBag.AmountText   = amountText;
        ViewBag.DateText     = dateText;
        ViewBag.PreviewHtml  = previewHtml;
        return View("~/Views/QuickApproval/Review.cshtml");
    }

    // ── POST: asıl onay/red işlemi ────────────────────────────────────────────
    [HttpPost("Quick/Act")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Act(int c, string? t, string? a, string? note, CancellationToken ct)
    {
        if (!string.Equals(a, "approve", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(a, "reject",  StringComparison.OrdinalIgnoreCase))
            return QuickView(error: "Geçersiz işlem türü.");

        var validation = await ValidateTokenAsync(c, t, ct);
        if (validation.ErrorResult is not null) return validation.ErrorResult;
        var record = validation.Record!;

        HttpContext.Items["__override_company_id"] = c;
        var approverName = await ResolveApproverNameAsync(record.ApproverId, ct);

        try
        {
            bool isApprove = string.Equals(a, "approve", StringComparison.OrdinalIgnoreCase);
            var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
            if (isApprove)
            {
                await _approvalService.ApproveStepAsync(
                    new ApproveStepRequest(
                        record.InstanceId,
                        record.ApproverId,
                        approverName,
                        Note: trimmedNote ?? "Hızlı onay (bağlantı üzerinden)"),
                    ct);
            }
            else
            {
                await _approvalService.RejectAsync(
                    new RejectStepRequest(
                        record.InstanceId,
                        record.ApproverId,
                        approverName,
                        Note: trimmedNote ?? "Red (bağlantı üzerinden)"),
                    ct);
            }

            await _tokenRepo.ConsumeAsync(c, record.Id, a!.ToLowerInvariant(), ct);
            return QuickView(success: true, isApprove: isApprove, approverName: approverName);
        }
        catch (InvalidOperationException ex)
        {
            return QuickView(error: ex.Message);
        }
        catch
        {
            return QuickView(error: "İşlem sırasında bir hata oluştu.");
        }
    }

    // ── Ortak token doğrulama ─────────────────────────────────────────────────
    private async Task<(ApprovalTokenRecord? Record, IActionResult? ErrorResult)> ValidateTokenAsync(
        int c, string? t, CancellationToken ct)
    {
        if (c <= 0 || string.IsNullOrWhiteSpace(t))
            return (null, QuickView(error: "Geçersiz bağlantı parametreleri."));

        ApprovalTokenRecord? record;
        try
        {
            record = await _tokenRepo.FindAsync(c, t, ct);
        }
        catch
        {
            return (null, QuickView(error: "Şirket bulunamadı veya bağlantıya erişilemiyor."));
        }

        if (record is null)
            return (null, QuickView(error: "Bu bağlantı geçerli değil veya daha önce kullanıldı."));

        if (record.UsedAt.HasValue)
            return (null, QuickView(alreadyUsed: true, usedAction: record.UsedAction));

        if (record.ExpiresAt < DateTime.UtcNow)
            return (null, QuickView(error: "Bu bağlantının süresi dolmuş (7 gün geçerlidir)."));

        return (record, null);
    }

    private async Task<string> ResolveApproverNameAsync(string approverId, CancellationToken ct)
    {
        if (!int.TryParse(approverId, out var uid)) return "Bağlantı Üzerinden";
        try
        {
            var user = await _userRepo.GetByIdAsync(uid, ct);
            return user?.FullName ?? "Bağlantı Üzerinden";
        }
        catch
        {
            return "Bağlantı Üzerinden";
        }
    }

    private IActionResult QuickView(
        string? error = null,
        bool success = false,
        bool isApprove = false,
        bool alreadyUsed = false,
        string? usedAction = null,
        string? approverName = null)
    {
        ViewBag.Error       = error;
        ViewBag.Success     = success;
        ViewBag.IsApprove   = isApprove;
        ViewBag.AlreadyUsed = alreadyUsed;
        ViewBag.UsedAction  = usedAction;
        ViewBag.ApproverName = approverName;
        return View("~/Views/QuickApproval/Action.cshtml");
    }
}
