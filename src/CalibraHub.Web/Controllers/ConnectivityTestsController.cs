using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// ConnectivityTestsController — Genel baglanti test endpoint'leri
/// (SMTP + Company DB) (rapor §2.3 AdminController split).
///
/// Tasinmis endpoint'ler:
///   - POST /Admin/TestSmtpConnectionJson           → SMTP MX/Helo testi (AJAX)
///   - POST /Admin/TestCompanyDatabaseConnection    → SQL Server baglanti testi
/// </summary>
[Authorize]
[CalibraHub.Web.Authorization.PermissionScope(CalibraHub.Application.Constants.FormCodes.SetupDefinitions)]
public sealed class ConnectivityTestsController : Controller
{
    private readonly IAdminManagementService _adminManagementService;
    private readonly ILogger<ConnectivityTestsController> _logger;

    public ConnectivityTestsController(
        IAdminManagementService adminManagementService,
        ILogger<ConnectivityTestsController> logger)
    {
        _adminManagementService = adminManagementService;
        _logger = logger;
    }

    /// <summary>AJAX (JSON) SMTP test — CompanySettings sayfasindaki "Test Et" butonu cagirir.</summary>
    [HttpPost("/Admin/TestSmtpConnectionJson")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestSmtpConnectionJson(
        [FromBody] TestSmtpConnectionRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null)
            return Json(new { isSuccess = false, message = "Gecersiz istek." });
        try
        {
            var result = await _adminManagementService.TestSmtpConnectionAsync(request, cancellationToken);
            return Json(new { isSuccess = result.IsSuccess, message = result.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ConnectivityTests] SMTP bağlantı testi başarısız.");
            return Json(new { isSuccess = false, message = "İşlem sırasında bir hata oluştu." });
        }
    }

    [HttpPost("/Admin/TestCompanyDatabaseConnection")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestCompanyDatabaseConnection(
        [FromBody] TestCompanyDatabaseConnectionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.ConnectionString))
            return Json(new { success = false, message = "Baglanti dizesi bos birakilamaz." });

        try
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(request.ConnectionString)
            {
                ConnectTimeout = 5,
                Pooling = false
            };
            await using var connection = new Microsoft.Data.SqlClient.SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            return Json(new { success = true, message = "Baglanti basarili." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ConnectivityTests] DB bağlantı testi başarısız.");
            return Json(new { success = false, message = $"Baglanti kurulamadi: {"İşlem sırasında bir hata oluştu."}" });
        }
    }
}
