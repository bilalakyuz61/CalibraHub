using System.Security.Claims;
using CalibraHub.Application.Abstractions.Persistence;
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
[CalibraHub.Web.Authorization.PermissionScope(CalibraHub.Application.Constants.FormCodes.DocTemplates)]
public sealed class DocDesignerApiController : ControllerBase
{
    private readonly IDocDesignerService _svc;
    private readonly IDocumentTypeRepository _docTypeRepo;

    public DocDesignerApiController(IDocDesignerService svc, IDocumentTypeRepository docTypeRepo)
    {
        _svc = svc;
        _docTypeRepo = docTypeRepo;
    }

    // ── CRUD ─────────────────────────────────────────────────────────────────

    [HttpGet("layouts")]
    public async Task<ActionResult<IReadOnlyCollection<DocLayoutSummaryDto>>> List(
        [FromQuery] string? docType, CancellationToken ct)
    {
        try
        {
            return Ok(await _svc.ListAsync(docType, ct));
        }
        catch (Exception ex) { return StatusCode(500, new { message = "İşlem sırasında bir hata oluştu." }); }
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
        catch (Exception ex) { return BadRequest(new { message = "İşlem sırasında bir hata oluştu." }); }
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
        catch (Exception ex) { return BadRequest(new { message = "İşlem sırasında bir hata oluştu." }); }
    }

    [HttpPost("render-pdf")]
    public async Task<IActionResult> RenderPdf([FromBody] DocLayoutRunRequest req, CancellationToken ct)
    {
        try
        {
            var bytes = await _svc.RenderPdfAsync(req, ct);
            return File(bytes, "application/pdf", $"belge_{req.LayoutId}.pdf");
        }
        catch (Exception ex) { return BadRequest(new { message = "İşlem sırasında bir hata oluştu." }); }
    }

    // ── Meta ─────────────────────────────────────────────────────────────────

    // 2026-05-26: Sabit liste yerine DocumentType DB tablosundan dinamik cek.
    // Numara Kurali (DocumentNumberRule) ekrani da ayni kaynagi kullanir — listeler tutarli.
    // Frontend artik [{id, code, name}] alir — ID-tabanli eslestirme icin (CLAUDE.md kurali).
    // "custom" sahte tipi icin id=null (DB'de karsiligi yok, ozgur tasarim).
    [HttpGet("doc-types")]
    public async Task<ActionResult<IReadOnlyCollection<object>>> DocTypes(CancellationToken ct)
    {
        var types = await _docTypeRepo.GetAllAsync(ct);
        var list = types
            .Where(t => t.IsActive)
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(t => (object)new { id = (int?)t.Id, code = t.Code, name = t.Name })
            .ToList();
        // "custom" (Özel Belge) DB'de yok — manuel ekle, id null.
        list.Add(new { id = (int?)null, code = "custom", name = "Özel Belge" });
        return Ok(list);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private ReportCallerContext BuildCaller()
    {
        var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : 0;
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
