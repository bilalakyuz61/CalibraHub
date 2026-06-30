using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Mail / WhatsApp mesajındaki onay bağlantılarını işler.
/// Kimlik doğrulama gerekmez; token + şirket ID kombinasyonu yetki olarak kullanılır.
/// URL: /Approval/Quick?c={companyId}&t={token}&a=approve|reject
/// </summary>
[AllowAnonymous]
[Route("Approval")]
public sealed class QuickApprovalController : Controller
{
    private readonly IApprovalTokenRepository _tokenRepo;
    private readonly IApprovalFlowService _approvalService;
    private readonly IUserProfileRepository _userRepo;

    public QuickApprovalController(
        IApprovalTokenRepository tokenRepo,
        IApprovalFlowService approvalService,
        IUserProfileRepository userRepo)
    {
        _tokenRepo       = tokenRepo;
        _approvalService = approvalService;
        _userRepo        = userRepo;
    }

    [HttpGet("Quick")]
    public async Task<IActionResult> Quick(int c, string? t, string? a, CancellationToken ct)
    {
        // --- parametre doğrulama ---
        if (c <= 0 || string.IsNullOrWhiteSpace(t) || string.IsNullOrWhiteSpace(a))
            return QuickView(error: "Geçersiz bağlantı parametreleri.");

        if (!string.Equals(a, "approve", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(a, "reject",  StringComparison.OrdinalIgnoreCase))
            return QuickView(error: "Geçersiz işlem türü.");

        // --- token arama ---
        ApprovalTokenRecord? record;
        try
        {
            record = await _tokenRepo.FindAsync(c, t, ct);
        }
        catch
        {
            return QuickView(error: "�?irket bulunamadı veya bağlantı erişilemiyor.");
        }

        if (record is null)
            return QuickView(error: "Bu bağlantı geçerli değil veya daha önce kullanıldı.");

        if (record.UsedAt.HasValue)
            return QuickView(alreadyUsed: true, usedAction: record.UsedAction);

        if (record.ExpiresAt < DateTime.UtcNow)
            return QuickView(error: "Bu bağlantının süresi dolmuş (7 gün geçerlidir).");

        // --- onaylayıcı bilgisini çek ---
        string approverName = "Bağlantı Üzerinden";
        if (int.TryParse(record.ApproverId, out var uid))
        {
            // HttpContext.Items override — per-company servisler doğru DB'yi açsın
            HttpContext.Items["__override_company_id"] = c;

            try
            {
                var user = await _userRepo.GetByIdAsync(uid, ct);
                if (user is not null) approverName = user.FullName;
            }
            catch { /* kullanıcı adı alınamazsa devam et */ }
        }

        // --- onay / red işlemi ---
        HttpContext.Items["__override_company_id"] = c;
        try
        {
            bool isApprove = string.Equals(a, "approve", StringComparison.OrdinalIgnoreCase);
            if (isApprove)
            {
                await _approvalService.ApproveStepAsync(
                    new ApproveStepRequest(
                        record.InstanceId,
                        record.ApproverId,
                        approverName,
                        Note: "Hızlı onay (bağlantı üzerinden)"),
                    ct);
            }
            else
            {
                await _approvalService.RejectAsync(
                    new RejectStepRequest(
                        record.InstanceId,
                        record.ApproverId,
                        approverName,
                        Note: "Red (bağlantı üzerinden)"),
                    ct);
            }

            await _tokenRepo.ConsumeAsync(c, record.Id, a.ToLowerInvariant(), ct);
            return QuickView(success: true, isApprove: isApprove, approverName: approverName);
        }
        catch (Exception ex)
        {
            var msg = "Islem sirasinda bir hata olustu.";
            if (msg.Contains("durumda değil") || msg.Contains("Approved") || msg.Contains("Rejected") || msg.Contains("Completed"))
                return QuickView(error: "Bu onay talebi zaten sonuçlandırılmış veya başka biri tarafından işleme alınmış.");
            return QuickView(error: msg);
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
