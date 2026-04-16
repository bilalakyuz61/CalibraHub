using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Antiforgery;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// WidgetsController — EAV widget sisteminin JSON API'si.
///
/// Endpoint'ler:
///   GET  /api/widgets/forms                             → tum aktif form kataloglari
///   GET  /api/widgets/forms/{formCode}/schema           → bir formun widget tanimlari
///   GET  /api/widgets/forms/{formId}/records/{recordId} → bir kaydin render model'i
///   POST /api/widgets/forms/{formId}/records/{recordId} → kaydin widget degerlerini upsert
///
/// React "Aptal Bilesen" tarafi GET record endpoint'inden aldigi JSON'u dogrudan
/// cizer: { widgetId, label, dataType, options, value } dizisi.
/// </summary>
[ApiController]
[Route("api/widgets")]
[IgnoreAntiforgeryToken]
public sealed class WidgetsController : ControllerBase
{
    private readonly IWidgetService _widgetService;

    // Not: Faz B-D'de kullanilan AdminFormWhitelist HashSet'i kaldirildi.
    // Tek dogruluk kaynagi artik dbo.Forms tablosu. Yeni form eklemek icin sadece
    // SQL seed/INSERT yeterli — C# deploy gereksiz. IsActive=1 filtresi hala
    // repository katmaninda geçerli (pasif form'lar admin panelinde gorunmez).

    public WidgetsController(IWidgetService widgetService)
    {
        _widgetService = widgetService;
    }

    // GET /api/widgets/forms
    // dbo.Forms'taki tum aktif formlari listeler (IsActive=1 filtresi repository'de).
    // Admin module selector besler — yeni form eklemek icin sadece DB seed yeterli.
    [HttpGet("forms")]
    public async Task<IActionResult> GetForms(CancellationToken ct)
    {
        var all = await _widgetService.GetFormsAsync(ct);
        return Ok(all.OrderBy(f => f.SortOrder).ToArray());
    }

    // GET /api/widgets/forms/{formCode}/schema
    [HttpGet("forms/{formCode}/schema")]
    public async Task<IActionResult> GetSchemaByCode(string formCode, CancellationToken ct)
    {
        var schema = await _widgetService.GetFormSchemaByCodeAsync(formCode, ct);
        if (schema == null) return NotFound(new { success = false, message = "Form bulunamadi." });
        return Ok(schema);
    }

    // GET /api/widgets/forms/id/{formId}/schema
    [HttpGet("forms/id/{formId:int}/schema")]
    public async Task<IActionResult> GetSchemaById(int formId, CancellationToken ct)
    {
        var schema = await _widgetService.GetFormSchemaAsync(formId, ct);
        if (schema == null) return NotFound(new { success = false, message = "Form bulunamadi." });
        return Ok(schema);
    }

    // GET /api/widgets/forms/{formId}/records/{recordId}
    [HttpGet("forms/{formId:int}/records/{recordId}")]
    public async Task<IActionResult> GetRecord(int formId, string recordId, CancellationToken ct)
    {
        var dtos = await _widgetService.GetRenderModelAsync(formId, recordId, ct);
        return Ok(dtos);
    }

    // POST /api/widgets/forms/{formId}/records/{recordId}
    // Body: SaveRecordRequest { values: { ... }, grids?: { widgetCode: { childFormCode, rows: [...] } } }
    [HttpPost("forms/{formId:int}/records/{recordId}")]
    public async Task<IActionResult> SaveRecord(
        int formId,
        string recordId,
        [FromBody] SaveRecordRequest? request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(recordId))
            return BadRequest(new { success = false, message = "RecordId bos olamaz." });

        try
        {
            var result = await _widgetService.SaveRecordAsync(
                formId,
                recordId,
                request ?? new SaveRecordRequest(null, null),
                ct);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    // ══════════════════════════════════════════════════════════
    // Faz C — Edit sayfalari icin form-code bazli endpoint'ler
    // ══════════════════════════════════════════════════════════

    // GET /api/widgets/forms/{formCode}/records/{recordId}
    // DynamicWidgetRenderer tek round trip ile schema+value alir.
    [HttpGet("forms/{formCode}/records/{recordId}")]
    public async Task<IActionResult> GetRecordByCode(
        string formCode,
        string recordId,
        CancellationToken ct)
    {
        var record = await _widgetService.GetRecordByCodeAsync(formCode, recordId, ct);
        if (record == null)
            return NotFound(new { success = false, message = "Form bulunamadi." });
        return Ok(record);
    }

    // POST /api/widgets/forms/{formCode}/records/{recordId}
    // Body: SaveRecordRequest { values, grids? }
    [HttpPost("forms/{formCode}/records/{recordId}")]
    public async Task<IActionResult> SaveRecordByCode(
        string formCode,
        string recordId,
        [FromBody] SaveRecordRequest? request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(recordId))
            return BadRequest(new { success = false, message = "RecordId bos olamaz." });

        var schema = await _widgetService.GetFormSchemaByCodeAsync(formCode, ct);
        if (schema == null)
            return NotFound(new { success = false, message = "Form bulunamadi." });

        try
        {
            var result = await _widgetService.SaveRecordAsync(
                schema.FormId,
                recordId,
                request ?? new SaveRecordRequest(null, null),
                ct);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    // ══════════════════════════════════════════════════════════
    // Widget tanim CRUD (Faz B — admin UI icin)
    // ══════════════════════════════════════════════════════════

    // POST /api/widgets/widgets
    // Body: UpsertWidgetRequest (Id=null → create; Id>0 → update)
    [HttpPost("widgets")]
    public async Task<IActionResult> UpsertWidget(
        [FromBody] UpsertWidgetRequest? request,
        CancellationToken ct)
    {
        if (request == null)
            return BadRequest(new { success = false, message = "Request govdesi bos." });

        // Form dogrulamasi — gercekten dbo.Forms'ta var mi?
        // (Whitelist kaldirildi; varlik kontrolu yeterli, servis tarafi da ayrica kontrol eder.)
        var form = (await _widgetService.GetFormsAsync(ct))
            .FirstOrDefault(f => f.Id == request.FormId);
        if (form == null)
            return BadRequest(new { success = false, message = $"FormId={request.FormId} bulunamadi veya pasif." });

        try
        {
            var id = await _widgetService.UpsertWidgetAsync(request, ct);
            return Ok(new UpsertWidgetResponse(id));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    // DELETE /api/widgets/widgets/{widgetId}
    [HttpDelete("widgets/{widgetId:int}")]
    public async Task<IActionResult> DeleteWidget(int widgetId, CancellationToken ct)
    {
        try
        {
            await _widgetService.DeleteWidgetAsync(widgetId, ct);
            return Ok(new { success = true });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    // PATCH /api/widgets/widgets/{widgetId}/is-plain-field
    // Body: { "isPlainField": true/false }
    [HttpPatch("widgets/{widgetId:int}/is-plain-field")]
    public async Task<IActionResult> PatchIsPlainField(int widgetId, [FromBody] PatchIsPlainFieldRequest? req, CancellationToken ct)
    {
        if (req == null)
            return BadRequest(new { success = false, message = "Request govdesi bos." });
        try
        {
            await _widgetService.ToggleIsPlainFieldAsync(widgetId, req.IsPlainField, ct);
            return Ok(new { success = true });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }
}

public sealed record PatchIsPlainFieldRequest(bool IsPlainField);
