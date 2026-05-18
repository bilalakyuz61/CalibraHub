using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Domain.Enums;
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
public sealed class IntegrationController : Controller
{
    /// <summary>
    /// Bir entegrasyonu elle calistir. Sprint 3'teki form-uzeri "ERP'ye Aktar"
    /// butonu da bu endpoint'i cagiracak.
    /// </summary>
    [HttpPost("/Integration/Run/{integrationId:int}")]
    public async Task<IActionResult> Run(
        int integrationId,
        [FromQuery] string? recordId,
        [FromServices] IIntegrationRunner runner,
        CancellationToken ct)
    {
        if (integrationId <= 0)
            return Json(new { success = false, error = "integrationId zorunlu" });

        try
        {
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
            // Runner zaten try/catch ile sariliydi ama her ihtimale karsi controller-level
            // catch ile JSON error don. Aksi takdirde HTML error page doner (5xx).
            return Json(new
            {
                success = false,
                runId = 0L,
                statusCode = (int?)null,
                error = $"Beklenmedik hata: {ex.Message}",
                requestBody = (string?)null,
                responseBody = (string?)null,
            });
        }
    }
}
