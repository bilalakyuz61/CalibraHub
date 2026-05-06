using System.Security.Claims;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Security;
using CalibraHub.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

[ApiController]
[Authorize]
[Route("api/doc-designer")]
public sealed class DocDesignerApiController : ControllerBase
{
    private readonly IDocDesignerService _svc;

    public DocDesignerApiController(IDocDesignerService svc) => _svc = svc;

    // ── CRUD ─────────────────────────────────────────────────────────────────

    [HttpGet("layouts")]
    public async Task<ActionResult<IReadOnlyCollection<DocLayoutSummaryDto>>> List(
        [FromQuery] string? docType, CancellationToken ct)
    {
        try
        {
            return Ok(await _svc.ListAsync(docType, ct));
        }
        catch (Exception ex) { return StatusCode(500, new { message = ex.Message }); }
    }

    [HttpGet("layouts/{id:int}")]
    public async Task<ActionResult<DocLayoutDetailDto>> Get(int id, CancellationToken ct)
    {
        var dto = await _svc.GetAsync(id, ct);
        return dto == null ? NotFound(new { message = $"Şablon {id} bulunamadı." }) : Ok(dto);
    }

    [HttpPut("layouts")]
    public async Task<ActionResult<int>> Save([FromBody] SaveDocLayoutRequest req, CancellationToken ct)
    {
        try
        {
            var id = await _svc.SaveAsync(req, BuildCaller(), ct);
            return Ok(id);
        }
        catch (Exception ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpDelete("layouts/{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _svc.DeleteAsync(id, ct);
        return NoContent();
    }

    // ── Render ───────────────────────────────────────────────────────────────

    [HttpPost("preview")]
    public async Task<ActionResult<object>> Preview([FromBody] DocLayoutRunRequest req, CancellationToken ct)
    {
        try
        {
            var html = await _svc.RenderHtmlPreviewAsync(req, ct);
            return Ok(new { html });
        }
        catch (Exception ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("render-pdf")]
    public async Task<IActionResult> RenderPdf([FromBody] DocLayoutRunRequest req, CancellationToken ct)
    {
        try
        {
            var bytes = await _svc.RenderPdfAsync(req, ct);
            return File(bytes, "application/pdf", $"belge_{req.LayoutId}.pdf");
        }
        catch (Exception ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ── Meta ─────────────────────────────────────────────────────────────────

    [HttpGet("doc-types")]
    public ActionResult<IReadOnlyCollection<string>> DocTypes()
        => Ok(new[]
        {
            "sales_quote", "sales_order", "purchase_order",
            "delivery_note", "invoice", "expense_note", "custom"
        });

    // ── Helpers ───────────────────────────────────────────────────────────────

    private ReportCallerContext BuildCaller()
    {
        var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : Guid.Empty;
        var companyId = int.TryParse(User.FindFirstValue("company_id"), out var cid) ? cid : 0;

        var roles = new List<UserRole>();
        foreach (var c in User.FindAll(ClaimTypes.Role))
        {
            if (Enum.TryParse<UserRole>(c.Value, true, out var r) && Enum.IsDefined(r))
            {
                roles.Add(r);
                continue;
            }
            foreach (var candidate in UserAuthorizationCatalog.Roles)
            {
                if (string.Equals(UserAuthorizationCatalog.GetRoleLabel(candidate), c.Value, StringComparison.OrdinalIgnoreCase))
                {
                    roles.Add(candidate);
                    break;
                }
            }
        }

        var perms = new List<UserPermission>();
        foreach (var c in User.FindAll("permission"))
        {
            if (Enum.TryParse<UserPermission>(c.Value, true, out var p) && Enum.IsDefined(p))
                perms.Add(p);
        }

        return new ReportCallerContext(userId, companyId, roles, perms);
    }
}
