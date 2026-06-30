using System.Security.Claims;
using CalibraHub.Application.Abstractions.DesignProvider;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// DocumentController — belge PDF basimlari icin ince bridge.
/// Tum render isi Belge Tasarimcisi (DocDesigner) tarafindan yapilir; bu controller
/// yalnizca HTTP endpoint'lerini PrintDispatcher'a yonlendirir.
///
/// Eski FastReport sablonlari ve onlarla iliskili tum CRUD/upload/preview action'lari
/// kaldirildi. Yeni akista belge tasarimlari "Belge Tasarimcisi" (/DocDesigner)
/// ekranindan yonetilir, DocLayoutRule + DocLayout sistemine kaydedilir ve
/// PrintDispatcher uzerinden render edilir.
/// </summary>
[Authorize]
[CalibraHub.Web.Authorization.PermissionScope(CalibraHub.Application.Constants.FormCodes.DocTemplates)]
public sealed class DocumentController : Controller
{
    private readonly IDocumentTypeRepository _docTypeRepo;
    private readonly IDocumentRepository _documentRepo;
    private readonly IFinanceRepository _financeRepo;
    private readonly IPrintDispatcher _printDispatcher;
    private readonly ILogger<DocumentController> _logger;

    public DocumentController(
        IDocumentTypeRepository docTypeRepo,
        IDocumentRepository documentRepo,
        IFinanceRepository financeRepo,
        IPrintDispatcher printDispatcher,
        ILogger<DocumentController> logger)
    {
        _docTypeRepo     = docTypeRepo;
        _documentRepo    = documentRepo;
        _financeRepo     = financeRepo;
        _printDispatcher = printDispatcher;
        _logger          = logger;
    }

    // ── Print Endpoint'leri ──────────────────────────────────────────────────
    // PrintInvoice/PrintDeliveryNote/PrintPurchaseOrder — PrintDispatcher'a delege.
    // Dispatcher DocLayoutRule + DocLayout zincirini cozer ve DocDesigner'in
    // render motoruyla PDF uretir.

    [HttpGet("/Document/PrintInvoice/{id:int}")]
    public Task<IActionResult> PrintInvoice(int id, CancellationToken ct)
        => DispatchDocumentPrint(id, "invoice", "fatura.pdf", ct);

    [HttpGet("/Document/PrintDeliveryNote/{id:int}")]
    public Task<IActionResult> PrintDeliveryNote(int id, CancellationToken ct)
        => DispatchDocumentPrint(id, "delivery_note", "irsaliye.pdf", ct);

    [HttpGet("/Document/PrintPurchaseOrder/{id:int}")]
    public Task<IActionResult> PrintPurchaseOrder(int id, CancellationToken ct)
        => DispatchDocumentPrint(id, "purchase_order", "satinalma.pdf", ct);

    private async Task<IActionResult> DispatchDocumentPrint(int id, string docType, string fileName, CancellationToken ct)
    {
        try
        {
            var document = await _documentRepo.GetByIdAsync(id, ct);
            if (document is null) return NotFound();

            // DesignSelectionContext — kullanici/cari/grup bilgileriyle kural eslemesi
            var userIdRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var userId = int.TryParse(userIdRaw, out var u) ? (int?)u : null;

            int? contactGroupId = null;
            byte? contactAccountType = null;
            if (document.ContactId is int cid && cid > 0)
            {
                var contact = await _financeRepo.GetContactByIdAsync(cid, ct);
                contactGroupId = contact?.ContactGroupId;
                contactAccountType = (byte?)contact?.AccountType;
            }

            var ctx = new DesignSelectionContext
            {
                DocType        = docType,
                CustomerId     = document.ContactId,
                ContactGroupId = contactGroupId,
                UserId         = userId,
                BranchId       = null,
                WarehouseId    = null,
                AccountType    = contactAccountType,
            };

            _logger.LogInformation(
                "[Print{DocType}] id={Id} → dispatcher (cust={Cust}, user={User})",
                docType, id, ctx.CustomerId, ctx.UserId);

            var pdf = await _printDispatcher.DispatchPrintAsync(ctx, id, ct);

            Response.Headers["Content-Disposition"] = $"inline; filename=\"{fileName}\"";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            // Tasarimci degisiklikleri hemen yansisin diye browser cache devre disi
            Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
            Response.Headers["Pragma"]        = "no-cache";
            Response.Headers["Expires"]       = "0";
            return File(pdf, "application/pdf");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "[Print{DocType}] id={Id} konfig hatasi.", docType, id);
            return DocPrintErrorPage(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Print{DocType}] id={Id} beklenmeyen hata.", docType, id);
            return DocPrintErrorPage("Yazdirma hatasi: " + "İşlem sırasında bir hata oluştu.");
        }
    }

    private IActionResult DocPrintErrorPage(string message)
    {
        var safe = System.Net.WebUtility.HtmlEncode(message);
        var html = "<!DOCTYPE html><html lang=\"tr\"><head><meta charset=\"utf-8\"/>"
            + "<title>Yazdirma Hatasi</title>"
            + "<style>html,body{margin:0;height:100%;font-family:Segoe UI,system-ui,sans-serif}"
            + "body{display:flex;align-items:center;justify-content:center;background:#0b1020;color:rgba(255,255,255,.85);padding:32px}"
            + "@media (prefers-color-scheme: light){body{background:#f8fafc;color:#1e293b}}"
            + ".card{max-width:520px;background:rgba(30,41,59,.7);border:1px solid #475569;border-radius:14px;padding:24px}"
            + "@media (prefers-color-scheme: light){.card{background:#fff;border-color:#e5e7eb}}"
            + "h1{margin:0 0 10px;font-size:1.2rem;color:#fca5a5}p{margin:8px 0;font-size:.92rem;line-height:1.55}</style>"
            + "</head><body><div class=\"card\"><h1>&#9888; Yazdirilamadi</h1><p>" + safe + "</p></div></body></html>";
        return Content(html, "text/html; charset=utf-8");
    }
}
