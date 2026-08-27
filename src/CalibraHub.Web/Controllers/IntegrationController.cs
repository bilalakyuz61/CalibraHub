using System.Security.Claims;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Security;
using CalibraHub.Domain.Enums;
using CalibraHub.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Entegrasyon Wizard runtime endpoint'leri (MVP).
///
/// V1 minimal:
///   POST /Integration/Run/{integrationId}?recordId=...   manuel tetikleme
///
/// Sprint 2'de eklenecek:
///   - List / Edit / Save (wizard kaydet)
///   - GET /Integration/Runs (audit log SmartBoard)
///   - GET /Integration/Forms (Step 1 dropdown — IFormMetadataService)
///   - GET /Integration/Endpoints (Step 2 dropdown)
///
/// V2:
///   - Otomatik tetikleme dispatcher (OnSave hook)
///   - Bulk islem
/// </summary>
[Authorize]
[CalibraHub.Web.Authorization.PermissionScope(CalibraHub.Application.Constants.FormCodes.Integrations)]
public sealed class IntegrationController : Controller
{
    /// <summary>
    /// Bir entegrasyonu elle calistir. Sprint 3'teki form-uzeri "ERP'ye Aktar"
    /// butonu da bu endpoint'i cagiracak.
    /// </summary>
    [HttpPost("/Integration/Run/{integrationId:int}")]
    // Sınıf-seviyesi [PermissionScope(Integrations)] bu action'da BİLİNÇLİ OLARAK muaf tutuldu:
    // gerçek yetki, butonun kendi SourceFormCode'u + BUTTON:INT_{id} tanımı üzerinden aşağıda
    // (IntegrationButtonActionHelper.CanRunManualButtonAsync) uygulanır. [Authorize] (cookie) korunur.
    [CalibraHub.Web.Authorization.PermissionAction]
    public async Task<IActionResult> Run(
        int integrationId,
        [FromQuery] string? recordId,
        [FromServices] IIntegrationRunner runner,
        [FromServices] IPermissionService permService,
        [FromServices] IPermissionDefRepository permDefRepo,
        [FromServices] IIntegrationRepository integrationRepo,
        CancellationToken ct,
        [FromServices] ILogger<IntegrationController> logger)
    {
        if (integrationId <= 0)
            return Json(new { success = false, error = "integrationId zorunlu" });

        try
        {
            // Per-buton yetki kontrolü — ortak kural (IntegrationButtonActionHelper). Sınıf kapısı
            // bu action'da muaf; gerçek yetki butonun SourceFormCode'u + BUTTON:INT_{id} üzerinden.
            var roleStr = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
            if (!UserAuthorizationCatalog.TryParseRole(roleStr, out var role))
                role = UserRole.Operator;
            if (role != UserRole.SystemAdmin)
            {
                // Butonun SourceFormCode'u için Integration kaydını çöz (varlık kontrolü de burada).
                var integration = await integrationRepo.GetByIdAsync(integrationId, ct);
                if (integration is null)
                    return Json(new { success = false, error = "Entegrasyon bulunamadı." });

                int? userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) && uid > 0 ? uid : null;
                int? deptId = int.TryParse(User.FindFirstValue("department_id"), out var d) && d > 0 ? d : null;

                var canRun = await IntegrationButtonActionHelper.CanRunManualButtonAsync(
                    role, userId, deptId, integration.SourceFormCode, integrationId,
                    permDefRepo, permService, ct);
                if (!canRun)
                    return Json(new { success = false, error = "Bu entegrasyonu çalıştırma yetkiniz bulunmuyor." });
            }

            var userName = User?.Identity?.Name ?? "system";
            var result = await runner.RunAsync(
                integrationId,
                string.IsNullOrWhiteSpace(recordId) ? null : recordId,
                IntegrationTriggerType.Manual,
                userName,
                ct);

            return Json(new
            {
                success = result.Success,
                runId = result.RunId,
                statusCode = result.HttpStatusCode,
                error = result.ErrorMessage,
                // request/response body — admin debugging icin; production'da
                // sadece error tracking icin (V2'de [Authorize(Role=Admin)] eklenir).
                requestBody = result.RequestBody,
                responseBody = result.ResponseBody,
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Integration] Run başarısız.");
            // Runner zaten try/catch ile sariliydi ama her ihtimale karsi controller-level
            // catch ile JSON error don. Aksi takdirde HTML error page doner (5xx).
            return Json(new
            {
                success = false,
                runId = 0L,
                statusCode = (int?)null,
                error = $"Beklenmedik hata: {"İşlem sırasında bir hata oluştu."}",
                requestBody = (string?)null,
                responseBody = (string?)null,
            });
        }
    }
}
