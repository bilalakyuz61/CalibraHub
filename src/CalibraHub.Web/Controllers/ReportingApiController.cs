using System.Security.Claims;
using System.Text;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Security;
using CalibraHub.Application.Services.Reporting;
using CalibraHub.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

[ApiController]
[Authorize]
[Route("api/reporting")]
[CalibraHub.Web.Authorization.PermissionScope(CalibraHub.Application.Constants.FormCodes.ReportDesigner)]
public sealed class ReportingApiController : ControllerBase
{
    private readonly IReportEngineService _engine;

    public ReportingApiController(IReportEngineService engine) => _engine = engine;

    // ── Metadata ──────────────────────────────────────────────────────────

    [HttpGet("views")]
    public async Task<ActionResult<IReadOnlyCollection<ReportViewDto>>> ListViews(CancellationToken ct)
        => await Execute(() => _engine.ListViewsAsync(BuildCaller(), ct));

    [HttpGet("views/{id:int}")]
    public async Task<ActionResult<ReportViewDetailDto>> GetView(int id, CancellationToken ct)
    {
        return await Execute(async () =>
        {
            var detail = await _engine.GetViewAsync(id, BuildCaller(), ct);
            return detail ?? throw new ReportNotFoundException($"VIEW bulunamadi: {id}");
        });
    }

    [HttpGet("views/{id:int}/discover-columns")]
    public async Task<ActionResult<IReadOnlyCollection<DiscoveredColumnDto>>> DiscoverColumns(int id, CancellationToken ct)
        => await Execute(() => _engine.DiscoverColumnsAsync(id, BuildCaller(), ct));

    // ── Definitions ───────────────────────────────────────────────────────

    [HttpGet("definitions")]
    public async Task<ActionResult<IReadOnlyCollection<ReportDefinitionSummaryDto>>> ListDefinitions(CancellationToken ct)
        => await Execute(() => _engine.ListDefinitionsAsync(BuildCaller(), ct));

    [HttpGet("definitions/{id:int}")]
    public async Task<ActionResult<ReportDefinitionDto>> GetDefinition(int id, CancellationToken ct)
    {
        return await Execute(async () =>
        {
            var dto = await _engine.GetDefinitionAsync(id, BuildCaller(), ct);
            return dto ?? throw new ReportNotFoundException($"Rapor tanimi bulunamadi: {id}");
        });
    }

    [HttpPut("definitions")]
    public async Task<ActionResult<int>> SaveDefinition([FromBody] SaveReportDefinitionRequest req, CancellationToken ct)
        => await Execute(() => _engine.SaveDefinitionAsync(req, BuildCaller(), ct));

    [HttpDelete("definitions/{id:int}")]
    public async Task<IActionResult> DeleteDefinition(int id, CancellationToken ct)
    {
        try
        {
            await _engine.DeleteDefinitionAsync(id, BuildCaller(), ct);
            return NoContent();
        }
        catch (ReportNotFoundException) { return NotFound(); }
        catch (ReportAuthorizationException ex) { return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message }); }
        catch (ReportValidationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ── Execution ─────────────────────────────────────────────────────────

    [HttpPost("execute")]
    public async Task<ActionResult<ReportExecutionResult>> Execute([FromBody] ExecuteReportRequest req, CancellationToken ct)
        => await Execute(() => _engine.ExecuteAsync(req, BuildCaller(), ct));

    [HttpPost("execute/csv")]
    public async Task<IActionResult> ExecuteCsv([FromBody] ExecuteReportRequest req, CancellationToken ct)
    {
        try
        {
            var fileName = $"rapor-{DateTime.Now:yyyyMMdd-HHmmss}.csv";
            Response.ContentType = "text/csv; charset=utf-8";
            Response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";

            await using var stream = Response.Body;
            await using var writer = new StreamWriter(stream, new UTF8Encoding(true));
            var (rowCount, _) = await _engine.ExecuteCsvAsync(req, BuildCaller(), writer, ct);
            await writer.FlushAsync();
            Response.Headers["X-Report-Row-Count"] = rowCount.ToString();
            return new EmptyResult();
        }
        catch (ReportNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (ReportAuthorizationException ex) { return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message }); }
        catch (ReportValidationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ── Admin ─────────────────────────────────────────────────────────────

    [HttpPut("admin/views")]
    public async Task<ActionResult<int>> UpsertView([FromBody] UpsertRptViewRequest req, CancellationToken ct)
        => await Execute(() => _engine.UpsertViewAsync(req, BuildCaller(), ct));

    [HttpPut("admin/views/{viewId:int}/columns")]
    public async Task<IActionResult> ReplaceColumns(
        int viewId,
        [FromBody] IReadOnlyCollection<UpsertRptViewColumnRequest> cols,
        CancellationToken ct)
    {
        try
        {
            await _engine.ReplaceColumnsAsync(viewId, cols, BuildCaller(), ct);
            return NoContent();
        }
        catch (ReportAuthorizationException ex) { return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message }); }
        catch (ReportValidationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPut("admin/views/{viewId:int}/roles")]
    public async Task<IActionResult> ReplaceViewRoles(
        int viewId,
        [FromBody] IReadOnlyCollection<UpsertRptViewRoleRequest> roles,
        CancellationToken ct)
    {
        try
        {
            await _engine.ReplaceViewRolesAsync(viewId, roles, BuildCaller(), ct);
            return NoContent();
        }
        catch (ReportAuthorizationException ex) { return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message }); }
        catch (ReportValidationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private async Task<ActionResult<T>> Execute<T>(Func<Task<T>> op)
    {
        try
        {
            return await op();
        }
        catch (ReportNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (ReportAuthorizationException ex) { return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message }); }
        catch (ReportValidationException ex) { return BadRequest(new { message = ex.Message }); }
    }

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
            // Login akisi claim'e UserAuthorizationCatalog.GetRoleLabel cikartiyor
            // ("Sistem Yoneticisi" gibi). Etikete gore tersine map.
            foreach (var candidate in UserAuthorizationCatalog.Roles)
            {
                if (string.Equals(UserAuthorizationCatalog.GetRoleLabel(candidate), c.Value, StringComparison.OrdinalIgnoreCase))
                {
                    roles.Add(candidate);
                    break;
                }
            }
        }

        var permissions = new List<UserPermission>();
        foreach (var c in User.FindAll("permission"))
            if (Enum.TryParse<UserPermission>(c.Value, true, out var p) && Enum.IsDefined(p)) permissions.Add(p);

        // Fallback: expand role-based permissions via catalog if claim list is empty.
        if (permissions.Count == 0)
        {
            foreach (var r in roles)
                permissions.AddRange(UserAuthorizationCatalog.GetAllowedPermissions(r));
        }

        return new ReportCallerContext(
            UserId: userId,
            CompanyId: companyId,
            Roles: roles.Distinct().ToArray(),
            Permissions: permissions.Distinct().ToArray());
    }
}
