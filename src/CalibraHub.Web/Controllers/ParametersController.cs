using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// ParametersController — Sirket Parametreleri (Genel + per-form key/value)
/// endpoint'leri (rapor §2.3 AdminController split).
///
/// Tasinmis endpoint'ler:
///   - GET  /Admin/Parameters                   → view (sol tab listesi)
///   - POST /Admin/SaveGeneralParametersJson    → Genel tab (Company entity)
///   - GET  /Admin/ParametersList?formCode=     → key/value liste
///   - POST /Admin/SaveParameter                → key/value upsert
///   - POST /Admin/DeleteParameter              → key/value sil
/// </summary>
[Authorize]
[CalibraHub.Web.Authorization.PermissionScope(CalibraHub.Application.Constants.FormCodes.CompanySettings)]
public sealed class ParametersController : Controller
{
    private readonly IAdminReadService _adminReadService;
    private readonly IAdminManagementService _adminManagementService;
    private readonly ICompanyParameterService _companyParameters;

    public ParametersController(
        IAdminReadService adminReadService,
        IAdminManagementService adminManagementService,
        ICompanyParameterService companyParameters)
    {
        _adminReadService = adminReadService;
        _adminManagementService = adminManagementService;
        _companyParameters = companyParameters;
    }

    private int GetCompanyId()
    {
        var raw = User.FindFirstValue("company_id");
        return int.TryParse(raw, out var id) ? id : 0;
    }

    // Belge türü bazında onay parametre anahtarları — ApprovalParameters constants
    // (formCode=APPROVAL, key=APPROVAL_ENABLED_{Kind}). Eski INVOICE_APPROVAL_ENABLED /
    // DISPATCH_APPROVAL_ENABLED anahtarları kaldırıldı (hiçbir runtime kodu tüketmiyordu).

    private const string ProductionFormCode = "PRODUCTION";
    public  const string ShopFloorMaxPinAttemptsKey = "SHOPFLOOR_MAX_PIN_ATTEMPTS";
    public  const int    ShopFloorMaxPinAttemptsDefault = 5;

    /// <summary>
    /// /Admin/Parameters → sabit sol-tab listesi (Genel, Onay Islemleri, Is Emri,
    /// Satis Teklifi). Her tab kodla gomulu hardcoded UI kontrollerini gosterir.
    /// </summary>
    [HttpGet("/Admin/Parameters")]
    public async Task<IActionResult> Parameters(CancellationToken cancellationToken)
    {
        ViewData["AdminMenu"] = "settings";

        // Genel tab'i icin: E-Belge Onay Sistemi switch'i (Sirket Ayarlari'ndan tasindi).
        var companyId = GetCompanyId();
        var snapshot = await _adminReadService.GetSnapshotAsync(cancellationToken);
        var company = snapshot.Companies.FirstOrDefault(x => x.Id == companyId);
        ViewData["IsEDocumentApprovalEnabled"] = company?.IsEDocumentApprovalEnabled ?? false;

        // Onay İşlemleri tab'i: belge türü bazında onay switch'leri.
        // Kaynak: DocumentEntityTypes.Definitions (Filter != null → Document tablosuna bağlı,
        // otomatik tetiklemede çözümlenebilen tipler). Parametre tanımsızsa AÇIK kabul edilir.
        var approvalParams = await _companyParameters.ListAsync(
            CalibraHub.Application.Constants.ApprovalParameters.FormCode, cancellationToken);
        ViewData["ApprovalKinds"] = CalibraHub.Application.Approval.EntityTypes.DocumentEntityTypes.Definitions
            .Where(d => d.Filter is not null)
            .Select(d => new ApprovalKindState(
                d.Code,
                d.Label,
                approvalParams.FirstOrDefault(p =>
                    p.ParamKey == CalibraHub.Application.Constants.ApprovalParameters.EnabledKey(d.Code))
                    ?.ParamValue != "false"))
            .ToList();

        // Üretim tab'i: shop-floor PIN lockout limiti
        var productionParams = await _companyParameters.ListAsync(ProductionFormCode, cancellationToken);
        var rawPin = productionParams
            .FirstOrDefault(p => p.ParamKey == ShopFloorMaxPinAttemptsKey)?.ParamValue;
        ViewData["ShopFloorMaxPinAttempts"] =
            int.TryParse(rawPin, out var pinLim) && pinLim >= 0 && pinLim <= 50
                ? pinLim
                : ShopFloorMaxPinAttemptsDefault;

        // Üretim tab'i: reçetede mükerrer bileşen izni (default: kapalı = birleştir)
        ViewData["BomAllowDuplicateComponents"] = productionParams
            .FirstOrDefault(p => p.ParamKey ==
                CalibraHub.Application.Constants.ProductionParameters.BomAllowDuplicateComponentsKey)
            ?.ParamValue == "true";

        // Stok tab'i: belge türü bazında "stok bakiyesini etkiler" switch'leri.
        // Parametre tanımsızsa AÇIK (etkiler) kabul edilir.
        var stockParams = await _companyParameters.ListAsync(
            CalibraHub.Application.Constants.StockParameters.FormCode, cancellationToken);
        ViewData["StockEffectStates"] = CalibraHub.Application.Constants.StockParameters.MovementCapableTypes
            .Select(t => new StockEffectState(
                t.Code,
                t.Label,
                t.Description,
                stockParams.FirstOrDefault(p =>
                    p.ParamKey == CalibraHub.Application.Constants.StockParameters.EffectKey(t.Code))
                    ?.ParamValue != "false"))
            .ToList();

        // Eksi bakiye kontrolü bayrakları (default: kapalı / engelli)
        ViewData["NegBalanceControl"] = stockParams.FirstOrDefault(p =>
            p.ParamKey == CalibraHub.Application.Constants.StockParameters.NegBalanceControlKey)?.ParamValue == "true";
        ViewData["NegBalanceAllowDefault"] = stockParams.FirstOrDefault(p =>
            p.ParamKey == CalibraHub.Application.Constants.StockParameters.NegBalanceAllowDefaultKey)?.ParamValue == "true";

        return View("~/Views/Admin/Parameters.cshtml");
    }

    /// <summary>
    /// Genel tab'indaki sirket-seviyesi parametreleri kaydeder.
    /// Su an sadece IsEDocumentApprovalEnabled var; ileride yeni Company entity
    /// property'leri eklenebilir.
    /// </summary>
    [HttpPost("/Admin/SaveGeneralParametersJson")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveGeneralParametersJson(
        [FromBody] GeneralParametersInput input,
        CancellationToken cancellationToken)
    {
        try
        {
            var companyId = GetCompanyId();
            var snapshot = await _adminReadService.GetSnapshotAsync(cancellationToken);
            var company = snapshot.Companies.FirstOrDefault(x => x.Id == companyId);
            if (company is null) return Json(new { ok = false, error = "Sirket bulunamadi." });

            await _adminManagementService.SaveCompanyAsync(
                new SaveCompanyRequest(
                    company.Id,
                    company.Name,
                    company.Title,
                    company.Address,
                    company.City,
                    company.District,
                    company.PostalCode,
                    company.TaxOffice,
                    company.TaxNumber,
                    input.IsEDocumentApprovalEnabled,
                    company.IsActive,
                    company.DatabaseConnectionString),
                cancellationToken);

            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    [HttpGet("/Admin/ParametersList")]
    public async Task<IActionResult> ParametersList(string? formCode, CancellationToken ct)
    {
        var list = await _companyParameters.ListAsync(formCode, ct);
        return Json(list);
    }

    [HttpPost("/Admin/SaveParameter")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveParameter([FromBody] SetCompanyParameterRequest req, CancellationToken ct)
    {
        try
        {
            await _companyParameters.SetAsync(req, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    [HttpPost("/Admin/DeleteParameter")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteParameter([FromBody] DeleteCompanyParameterRequest req, CancellationToken ct)
    {
        try
        {
            await _companyParameters.DeleteAsync(req.FormCode, req.ParamKey, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    [HttpPost("/Admin/SaveApprovalParametersJson")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveApprovalParametersJson(
        [FromBody] ApprovalParametersInput input,
        CancellationToken ct)
    {
        try
        {
            if (input.Kinds is null || input.Kinds.Count == 0)
                return Json(new { ok = false, error = "Kaydedilecek parametre yok." });

            // Whitelist: yalnızca DocumentEntityTypes'ta tanımlı spesifik kind'lar kabul edilir —
            // keyspace'e rastgele anahtar yazılmasını önler.
            var validKinds = CalibraHub.Application.Approval.EntityTypes.DocumentEntityTypes.Definitions
                .Where(d => d.Filter is not null)
                .Select(d => d.Code)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var item in input.Kinds)
            {
                if (string.IsNullOrWhiteSpace(item.Kind) || !validKinds.Contains(item.Kind))
                    continue;

                await _companyParameters.SetAsync(new SetCompanyParameterRequest(
                    CalibraHub.Application.Constants.ApprovalParameters.FormCode,
                    CalibraHub.Application.Constants.ApprovalParameters.EnabledKey(item.Kind),
                    item.Enabled ? "true" : "false",
                    CalibraHub.Domain.Enums.CompanyParameterDataType.Bool), ct);
            }

            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    [HttpPost("/Admin/SaveProductionParametersJson")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveProductionParametersJson(
        [FromBody] ProductionParametersInput input,
        CancellationToken ct)
    {
        try
        {
            if (input.ShopFloorMaxPinAttempts < 0 || input.ShopFloorMaxPinAttempts > 50)
                return Json(new { ok = false, error = "Hatalı PIN limiti 0 ile 50 arasında olmalı." });

            await _companyParameters.SetAsync(new SetCompanyParameterRequest(
                ProductionFormCode,
                ShopFloorMaxPinAttemptsKey,
                input.ShopFloorMaxPinAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
                CalibraHub.Domain.Enums.CompanyParameterDataType.Int), ct);

            // Reçetede mükerrer bileşen izni (Ürün Ağacı satır davranışı)
            await _companyParameters.SetAsync(new SetCompanyParameterRequest(
                ProductionFormCode,
                CalibraHub.Application.Constants.ProductionParameters.BomAllowDuplicateComponentsKey,
                input.BomAllowDuplicateComponents ? "true" : "false",
                CalibraHub.Domain.Enums.CompanyParameterDataType.Bool), ct);

            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    [HttpPost("/Admin/SaveStockParametersJson")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveStockParametersJson(
        [FromBody] StockParametersInput input,
        CancellationToken ct)
    {
        try
        {
            var form = CalibraHub.Application.Constants.StockParameters.FormCode;

            // Eksi bakiye kontrolü ana anahtarı + kendi ayarı olmayan depolar için varsayılan izin
            await _companyParameters.SetAsync(new SetCompanyParameterRequest(
                form, CalibraHub.Application.Constants.StockParameters.NegBalanceControlKey,
                input.NegControl ? "true" : "false",
                CalibraHub.Domain.Enums.CompanyParameterDataType.Bool), ct);
            await _companyParameters.SetAsync(new SetCompanyParameterRequest(
                form, CalibraHub.Application.Constants.StockParameters.NegBalanceAllowDefaultKey,
                input.NegDefault ? "true" : "false",
                CalibraHub.Domain.Enums.CompanyParameterDataType.Bool), ct);

            // Çekirdek belge türleri artık HER ZAMAN stok bakiyesini etkiler — eski
            // STOCK_EFFECT_{code} switch'leri kaldırıldı; kalıntı "false" değerlerini temizle
            // (aksi halde StockEffectHelper o türü bakiye dışı bırakmaya devam eder).
            foreach (var t in CalibraHub.Application.Constants.StockParameters.MovementCapableTypes)
                await _companyParameters.DeleteAsync(
                    form, CalibraHub.Application.Constants.StockParameters.EffectKey(t.Code), ct);

            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    public sealed record GeneralParametersInput(bool IsEDocumentApprovalEnabled);
    public sealed record ApprovalParametersInput(List<ApprovalKindInput> Kinds);
    public sealed record ApprovalKindInput(string Kind, bool Enabled);
    public sealed record ApprovalKindState(string Code, string Label, bool Enabled);
    public sealed record ProductionParametersInput(int ShopFloorMaxPinAttempts, bool BomAllowDuplicateComponents = false);
    public sealed record StockParametersInput(bool NegControl, bool NegDefault);
    public sealed record StockEffectInput(string Code, bool Enabled);
    public sealed record StockEffectState(string Code, string Label, string Description, bool Enabled);
    public sealed record DeleteCompanyParameterRequest(string FormCode, string ParamKey);
}
