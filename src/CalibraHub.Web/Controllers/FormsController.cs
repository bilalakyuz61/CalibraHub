using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// FormsController — dbo.Forms (Form Yöneticisi) JSON API.
///
/// Endpoint'ler:
///   GET    /api/forms                → Tüm formları listele
///   GET    /api/forms/{id}           → Tek form
///   POST   /api/forms                → Yeni form ekle
///   PUT    /api/forms/{id}           → Güncelle
///   DELETE /api/forms/{id}           → Fiziksel sil
///
/// POST/PUT sonrası BaseTable doluysa IWidgetRepository üzerinden
/// v_Flat_{FormCode} dinamik view'ı yeniden oluşturulur (try/catch ile sarılı —
/// view hatası form kayıt akışını engellemez).
/// </summary>
[Authorize]
[ApiController]
[Route("api/forms")]
[IgnoreAntiforgeryToken]
public sealed class FormsController : ControllerBase
{
    private readonly IFormRepository _formRepository;
    private readonly IWidgetRepository _widgetRepository;
    private readonly ILogger<FormsController> _logger;

    public FormsController(
        IFormRepository formRepository,
        IWidgetRepository widgetRepository,
        ILogger<FormsController> logger)
    {
        _formRepository = formRepository;
        _widgetRepository = widgetRepository;
        _logger = logger;
    }

    // GET /api/forms
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var forms = await _formRepository.GetAllAsync(ct);
        return Ok(forms);
    }

    // GET /api/forms/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var form = await _formRepository.GetByIdAsync(id, ct);
        if (form == null)
            return NotFound(new { success = false, message = "Form bulunamadı." });
        return Ok(form);
    }

    // POST /api/forms
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateFormRequest request, CancellationToken ct)
    {
        if (request == null)
            return BadRequest(new { success = false, message = "İstek gövdesi boş." });

        if (string.IsNullOrWhiteSpace(request.FormCode))
            return BadRequest(new { success = false, message = "FormCode zorunludur." });

        if (string.IsNullOrWhiteSpace(request.FormName))
            return BadRequest(new { success = false, message = "FormName zorunludur." });

        int newId;
        try
        {
            newId = await _formRepository.CreateAsync(request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Form oluşturulurken hata: {FormCode}", request.FormCode);
            return StatusCode(500, new { success = false, message = "Form oluşturulamadı: " + ex.Message });
        }

        // Flat View Regeneration — hata akışı engellemez
        if (!string.IsNullOrWhiteSpace(request.BaseTable))
        {
            await TryRegenerateFlatViewAsync(newId, request.FormCode, request.BaseTable, request.BaseRecordKey, ct);
        }

        var created = await _formRepository.GetByIdAsync(newId, ct);
        return CreatedAtAction(nameof(GetById), new { id = newId }, created);
    }

    // PUT /api/forms/{id}
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateFormRequest request, CancellationToken ct)
    {
        if (request == null)
            return BadRequest(new { success = false, message = "İstek gövdesi boş." });

        if (id != request.Id)
            return BadRequest(new { success = false, message = "URL id ile body id uyuşmuyor." });

        if (string.IsNullOrWhiteSpace(request.FormCode))
            return BadRequest(new { success = false, message = "FormCode zorunludur." });

        if (string.IsNullOrWhiteSpace(request.FormName))
            return BadRequest(new { success = false, message = "FormName zorunludur." });

        var existing = await _formRepository.GetByIdAsync(id, ct);
        if (existing == null)
            return NotFound(new { success = false, message = "Form bulunamadı." });

        try
        {
            await _formRepository.UpdateAsync(request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Form güncellenirken hata: {Id}", id);
            return StatusCode(500, new { success = false, message = "Form güncellenemedi: " + ex.Message });
        }

        // Flat View Regeneration — hata akışı engellemez
        if (!string.IsNullOrWhiteSpace(request.BaseTable))
        {
            await TryRegenerateFlatViewAsync(id, request.FormCode, request.BaseTable, request.BaseRecordKey, ct);
        }

        var updated = await _formRepository.GetByIdAsync(id, ct);
        return Ok(updated);
    }

    // DELETE /api/forms/{id}
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var existing = await _formRepository.GetByIdAsync(id, ct);
        if (existing == null)
            return NotFound(new { success = false, message = "Form bulunamadı." });

        try
        {
            await _formRepository.DeleteAsync(id, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Form silinirken hata: {Id}", id);
            return StatusCode(500, new { success = false, message = "Form silinemedi: " + ex.Message });
        }

        return Ok(new { success = true });
    }

    // ─── Yardımcı: Flat View Regeneration ───────────────────────────────────
    private async Task TryRegenerateFlatViewAsync(
        int formId,
        string formCode,
        string baseTable,
        string? baseRecordKey,
        CancellationToken ct)
    {
        try
        {
            var formEntity = new FormDefinition
            {
                Id = formId,
                FormCode = formCode,
                FormName = formCode,
                Module = string.Empty,
                BaseTable = baseTable,
                BaseRecordKey = baseRecordKey,
                IsActive = true,
            };
            var widgets = await _widgetRepository.GetWidgetsByFormAsync(formId, ct);
            await _widgetRepository.RegenerateFlattenedViewAsync(formEntity, widgets, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Flat view oluşturulurken hata (kritik değil): FormCode={FormCode}, BaseTable={BaseTable}",
                formCode, baseTable);
        }
    }
}
