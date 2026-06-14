using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Form sabit alanlarinin rehber eslestirmesi ve ayarlari.
///
///   GET    /api/field-settings/form/{formId}         → Formun alan ayarlari
///   GET    /api/field-settings/guide/{guideCode}     → Rehberin eslestirmeleri
///   POST   /api/field-settings                       → Tekil upsert
///   POST   /api/field-settings/bulk-map              → Toplu eslestirme kaydet
///   DELETE /api/field-settings/{id}                  → Alan sil
///   GET    /api/field-settings/runtime/{formCode}    → Runtime baglantilari
///   GET    /api/field-settings/discover/{formId}     → Alan kesfi
/// </summary>
[Authorize]
[ApiController]
[Route("api/field-settings")]
public sealed class FieldSettingsController : ControllerBase
{
    private readonly IFieldSettingRepository _repo;

    public FieldSettingsController(IFieldSettingRepository repo)
    {
        _repo = repo;
    }

    [HttpGet("form/{formId:int}")]
    public async Task<IActionResult> GetByForm(int formId, CancellationToken ct)
    {
        var items = await _repo.GetByFormIdAsync(formId, ct);
        return Ok(items.Select(f => new FieldSettingDto(
            f.Id, f.FormId, f.FieldKey, f.FieldLabel,
            f.GuideCode, f.ViewName, f.FilterJson, f.IsRequired,
            f.FormatJson, f.IsActive, f.SortOrder)));
    }

    [HttpGet("guide/{guideCode}")]
    public async Task<IActionResult> GetByGuide(string guideCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(guideCode))
            return BadRequest(new { success = false, message = "guideCode gerekli." });

        var items = await _repo.GetByGuideCodeAsync(guideCode, ct);
        return Ok(items.Select(f => new FieldSettingDto(
            f.Id, f.FormId, f.FieldKey, f.FieldLabel,
            f.GuideCode, f.ViewName, f.FilterJson, f.IsRequired,
            f.FormatJson, f.IsActive, f.SortOrder)));
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] UpsertFieldSettingRequest request, CancellationToken ct)
    {
        if (request == null)
            return BadRequest(new { success = false, message = "Gecersiz istek." });
        if (request.FormId <= 0)
            return BadRequest(new { success = false, message = "FormId gerekli." });
        if (string.IsNullOrWhiteSpace(request.FieldKey))
            return BadRequest(new { success = false, message = "FieldKey gerekli." });
        if (string.IsNullOrWhiteSpace(request.FieldLabel))
            return BadRequest(new { success = false, message = "FieldLabel gerekli." });

        try
        {
            var id = await _repo.UpsertAsync(request, ct);
            return Ok(new { success = true, id });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    [HttpPost("by-form-code")]
    public async Task<IActionResult> UpsertByFormCode(
        [FromBody] UpsertFieldSettingByFormCodeRequest request, CancellationToken ct)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.FormCode))
            return BadRequest(new { success = false, message = "FormCode gerekli." });
        if (string.IsNullOrWhiteSpace(request.FieldKey))
            return BadRequest(new { success = false, message = "FieldKey gerekli." });
        if (string.IsNullOrWhiteSpace(request.FieldLabel))
            return BadRequest(new { success = false, message = "FieldLabel gerekli." });
        try
        {
            var id = await _repo.UpsertByFormCodeAsync(request, ct);
            return Ok(new { success = true, id });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    [HttpPost("bulk-map")]
    public async Task<IActionResult> BulkMap([FromBody] BulkMapGuideRequest request, CancellationToken ct)
    {
        if (request == null)
            return BadRequest(new { success = false, message = "Gecersiz istek." });
        if (string.IsNullOrWhiteSpace(request.GuideCode))
            return BadRequest(new { success = false, message = "GuideCode gerekli." });
        if (request.FormId <= 0)
            return BadRequest(new { success = false, message = "FormId gerekli." });

        try
        {
            await _repo.BulkMapGuideAsync(request, ct);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        if (id <= 0) return BadRequest(new { success = false, message = "Gecersiz id." });

        try
        {
            await _repo.DeleteAsync(id, ct);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    [HttpGet("runtime/{formCode}")]
    public async Task<IActionResult> GetRuntimeBindings(string formCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(formCode))
            return BadRequest(new { success = false, message = "formCode gerekli." });

        var bindings = await _repo.GetGuideBindingsForFormAsync(formCode, ct);
        return Ok(bindings);
    }

    [HttpGet("discover/{formId:int}")]
    public async Task<IActionResult> DiscoverFields(int formId, [FromQuery] bool includeMapped, CancellationToken ct)
    {
        if (formId <= 0) return BadRequest(new { success = false, message = "Gecersiz formId." });

        try
        {
            // includeMapped=true: FldSet'te eslesmis kolonlar da dahil (widget tanimlamada).
            // includeMapped=false (default): sadece henuz eslesmemis kolonlar (FldSet sayfasi).
            var columns = await _repo.DiscoverFieldsAsync(formId, includeMapped, ct);
            return Ok(columns);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }
}
