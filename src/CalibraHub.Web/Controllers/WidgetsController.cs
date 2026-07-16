using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Services.Security;   // CanViewFieldAsync extension
using CalibraHub.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Antiforgery;
using System.Security.Claims;                      // FindFirstValue extension

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
[Authorize]
[ApiController]
[Route("api/widgets")]
[IgnoreAntiforgeryToken]
[CalibraHub.Web.Authorization.PermissionScope(CalibraHub.Application.Constants.FormCodes.ViewSettings)]
public sealed class WidgetsController : ControllerBase
{
    private readonly IWidgetService _widgetService;
    private readonly IIntegrationRepository _integrationRepo;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CalibraHub.Application.Abstractions.Services.IPermissionService _permService;
    private readonly IAttachmentRepository _attachments;

    // Not: Faz B-D'de kullanilan AdminFormWhitelist HashSet'i kaldirildi.
    // Tek dogruluk kaynagi artik dbo.Forms tablosu. Yeni form eklemek icin sadece
    // SQL seed/INSERT yeterli — C# deploy gereksiz. IsActive=1 filtresi hala
    // repository katmaninda geçerli (pasif form'lar admin panelinde gorunmez).

    public WidgetsController(
        IWidgetService widgetService,
        IIntegrationRepository integrationRepo,
        IServiceScopeFactory scopeFactory,
        CalibraHub.Application.Abstractions.Services.IPermissionService permService,
        IAttachmentRepository attachments)
    {
        _widgetService = widgetService;
        _integrationRepo = integrationRepo;
        _scopeFactory = scopeFactory;
        _permService = permService;
        _attachments = attachments;
    }

    /// <summary>
    /// 2026-06-08 — Yetkilendirilebilir alanları kullanıcının izinlerine göre filtreler.
    /// IsPermissionControlled=true olan widget'lar için kullanıcı FIELD:<WidgetCode> iznine
    /// sahip değilse schema'dan çıkarılır. SystemAdmin'a tüm alanlar görünür.
    /// </summary>
    private async Task<CalibraHub.Application.Contracts.WidgetFormSchemaDto> FilterByFieldPermissionsAsync(
        CalibraHub.Application.Contracts.WidgetFormSchemaDto schema, CancellationToken ct)
    {
        if (schema?.Widgets is null || schema.Widgets.Count == 0) return schema!;

        // SystemAdmin shortcut — tüm alanlar görünür
        var roleStr = User.FindFirstValue(System.Security.Claims.ClaimTypes.Role) ?? string.Empty;
        if (string.Equals(roleStr, "SystemAdmin", StringComparison.OrdinalIgnoreCase)) return schema;

        // Anonymous için filtre yapma (controller zaten Authorize ile korunmuyorsa public schema dönsün)
        var uidStr = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (!int.TryParse(uidStr, out var uid) || uid <= 0) return schema;

        var deptStr = User.FindFirstValue("department_id");
        int? deptId = int.TryParse(deptStr, out var d) && d > 0 ? d : null;
        if (!CalibraHub.Application.Security.UserAuthorizationCatalog.TryParseRole(roleStr, out var role))
            role = CalibraHub.Domain.Enums.UserRole.Operator;

        // IsPermissionControlled=true olan widget'ları test et — diğerleri kalır
        var visible = new List<CalibraHub.Application.Contracts.WidgetDefinitionDto>(schema.Widgets.Count);
        foreach (var w in schema.Widgets)
        {
            if (!w.IsPermissionControlled)
            {
                visible.Add(w);
                continue;
            }
            var canView = await _permService.CanViewFieldAsync(uid, role, deptId, schema.FormCode, w.WidgetCode, ct);
            if (canView) visible.Add(w);
        }

        // Aynı schema ile — yalnızca Widgets değişti
        return schema with { Widgets = visible };
    }

    /// <summary>
    /// OnSave trigger dispatcher — verilen formCode + recordId icin aktif OnSave
    /// trigger'i olan tum entegrasyonlari fire-and-forget calistirir. Save response'u
    /// bloke etmez (kullanici hizli geri donus alsin); hatalar IntegrationRun audit'e duser.
    ///
    /// **Duplicate guard:** Trigger config'inde `{"onlyIfNotSent": true}` (default true) ise
    /// belge tablosunda IntegrationSentAt dolu kayitlar SKIP edilir. Boylece bir siparis
    /// guncelendiginde tekrar tekrar ERP'ye gitmez. Yeniden gondermek icin liste/edit
    /// ekranindaki "Yeniden Gonder" manuel butonu kullanilir.
    /// </summary>
    private void FireOnSaveTriggersFireAndForget(string formCode, string recordId, string? userName)
    {
        // Background task'a kopya context olustur — request scope kapanmadan disari cikmasin
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var repo    = scope.ServiceProvider.GetRequiredService<IIntegrationRepository>();
                var runner  = scope.ServiceProvider.GetRequiredService<IIntegrationRunner>();
                var formMeta = scope.ServiceProvider.GetService<CalibraHub.Application.Abstractions.Services.IFormMetadataService>();
                var tracker  = scope.ServiceProvider.GetService<CalibraHub.Application.Abstractions.Services.IIntegrationStatusTracker>();

                var integrations = await repo.ListByFormCodeAsync(
                    formCode, IntegrationTriggerType.OnSave, default);

                if (integrations.Count == 0) return;

                // Form'un BaseTable + BaseRecordKey'i — duplicate guard sorgusu icin
                CalibraHub.Application.Contracts.IntegrationFormDto? formDto = null;
                if (formMeta is not null)
                    formDto = await formMeta.GetFormAsync(formCode, default);

                // Tracker kolonlari (IntegrationSentAt vb.) IntegrationRunner'in yazdigi "kanonik"
                // RecordId ile anahtarlanir: Forms.BaseRecordKey "Id" degilse (orn. Document
                // formlarinda "DocumentNumber") IntegrationRunner.RunInternalAsync sample.RecordId'yi
                // (yani "TKL202600000013" gibi is anahtarini) MarkSentAsync/MarkFailedAsync'e verir —
                // bu metoda gelen ham recordId ("13") degil. Guard'in dogru satiri bulmasi icin ayni
                // cozumleme burada bir kez (formCode basina) tekrarlanir; formMeta yoksa/hata olursa
                // ham recordId'ye duser. IntegrationOnSaveDispatcher ile ayni desen — bkz. o dosyadaki
                // "statusRecordId" cozumlemesi.
                var statusRecordId = recordId;
                if (tracker is not null && formMeta is not null)
                {
                    try
                    {
                        var sample = await formMeta.GetSampleRecordAsync(formCode, recordId, default);
                        if (sample?.RecordId is { Length: > 0 } resolvedId)
                            statusRecordId = resolvedId;
                    }
                    catch { /* guard ham recordId ile devam eder */ }
                }

                // Belgenin onceden gonderilip gonderilmedigini bir kere sorgula (cache)
                DateTime? alreadySentAt = null;
                if (tracker is not null
                    && formDto is { BaseTable: not null, BaseRecordKey: not null }
                    && !string.IsNullOrWhiteSpace(formDto.BaseTable)
                    && !string.IsNullOrWhiteSpace(formDto.BaseRecordKey))
                {
                    alreadySentAt = await tracker.GetSentAtAsync(
                        formDto.BaseTable!, formDto.BaseRecordKey!, statusRecordId, default);
                }

                foreach (var integ in integrations)
                {
                    // Trigger config'inden onlyIfNotSent oku — default TRUE (guvenli)
                    var triggers = await repo.GetTriggersAsync(integ.Id, default);
                    var onSaveTrigger = triggers.FirstOrDefault(t =>
                        t.IsActive && t.TriggerType == IntegrationTriggerType.OnSave);
                    var onlyIfNotSent = ParseOnlyIfNotSent(onSaveTrigger?.Config);

                    if (onlyIfNotSent && alreadySentAt.HasValue)
                        continue;   // zaten gonderildi → skip

                    try
                    {
                        await runner.RunAsync(
                            integrationId: integ.Id,
                            sourceRecordId: recordId,
                            triggerType: IntegrationTriggerType.OnSave,
                            triggeredBy: userName ?? "OnSave",
                            ct: default);
                    }
                    catch { /* Run log'a yazildi; sessizce devam */ }
                }
            }
            catch { /* Background task — exception'i yutuyoruz */ }
        });
    }

    private static bool ParseOnlyIfNotSent(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson)) return true; // default TRUE
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(configJson);
            if (doc.RootElement.TryGetProperty("onlyIfNotSent", out var v))
                return v.ValueKind switch {
                    System.Text.Json.JsonValueKind.True  => true,
                    System.Text.Json.JsonValueKind.False => false,
                    _ => true,
                };
        }
        catch { /* malformed JSON → default true */ }
        return true;
    }

    // GET /api/widgets/forms
    // Alan Rehberi dropdown'u için: IsWidgetForm=true olan formları döner.
    // IsWidgetForm=false: container/liste formları (SALES_QUOTE vb.) + _NEW formları + ayarlar sayfaları.
    // Tüm formlar için GetFormsAsync (no filter) servis üzerinden kullanılır.
    [HttpGet("forms")]
    public async Task<IActionResult> GetForms(CancellationToken ct)
    {
        var all = await _widgetService.GetFormsAsync(ct);
        return Ok(all.Where(f => f.IsWidgetForm).OrderBy(f => f.SortOrder).ToArray());
    }

    // GET /api/widgets/forms/{formCode}/schema
    [HttpGet("forms/{formCode}/schema")]
    public async Task<IActionResult> GetSchemaByCode(string formCode, CancellationToken ct)
    {
        var schema = await _widgetService.GetFormSchemaByCodeAsync(formCode, ct);
        if (schema == null) return NotFound(new { success = false, message = "Form bulunamadi." });
        // 2026-06-08 — Yetkilendirilebilir alanları kullanıcı iznine göre filtrele
        var filtered = await FilterByFieldPermissionsAsync(schema, ct);
        return Ok(filtered);
    }

    // GET /api/widgets/forms/id/{formId}/schema
    [HttpGet("forms/id/{formId:int}/schema")]
    public async Task<IActionResult> GetSchemaById(int formId, CancellationToken ct)
    {
        var schema = await _widgetService.GetFormSchemaAsync(formId, ct);
        if (schema == null) return NotFound(new { success = false, message = "Form bulunamadi." });
        var filtered = await FilterByFieldPermissionsAsync(schema, ct);
        return Ok(filtered);
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

            // OnSave tetikleyici — formCode'u sema uzerinden cek + fire-and-forget
            try
            {
                var schema = await _widgetService.GetFormSchemaAsync(formId, ct);
                if (schema is not null && !string.IsNullOrWhiteSpace(schema.FormCode))
                    FireOnSaveTriggersFireAndForget(schema.FormCode, recordId, User?.Identity?.Name);
            }
            catch { /* OnSave dispatch hatasi save'i bozmasin */ }

            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "İşlem sırasında bir hata oluştu." });
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

    // GET /api/widgets/forms/{formCode}/records/{recordId}/history
    // Alan bazli degisiklik gecmisi (audit) — eski deger → yeni deger + kim + ne zaman.
    // Grid child satirlarinin degisiklikleri de dahildir (childRecordId dolu gelir).
    [HttpGet("forms/{formCode}/records/{recordId}/history")]
    public async Task<IActionResult> GetRecordHistory(
        string formCode,
        string recordId,
        CancellationToken ct)
    {
        var history = await _widgetService.GetValueHistoryAsync(formCode, recordId, ct);
        if (history == null)
            return NotFound(new { success = false, message = "Form bulunamadi." });
        return Ok(history);
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

            // OnSave tetikleyici — fire-and-forget
            try
            {
                FireOnSaveTriggersFireAndForget(formCode, recordId, User?.Identity?.Name);
            }
            catch { /* dispatch hatasi save'i bozmasin */ }

            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "İşlem sırasında bir hata oluştu." });
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
            return StatusCode(500, new { success = false, message = "İşlem sırasında bir hata oluştu." });
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
            return StatusCode(500, new { success = false, message = "İşlem sırasında bir hata oluştu." });
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
            return StatusCode(500, new { success = false, message = "İşlem sırasında bir hata oluştu." });
        }
    }

    // PATCH /api/widgets/widgets/sort-orders
    // Body: [{ "id": 12, "sortOrder": 20 }, { "id": 15, "sortOrder": 10 }]
    // Yalnizca SortOrder gunceller — reorder icin tam upsert gondermek OptionsJSON'un
    // client tarafinda kayipli yeniden insasini gerektiriyordu (lookup/grid metadata'si).
    [HttpPatch("widgets/sort-orders")]
    public async Task<IActionResult> PatchSortOrders(
        [FromBody] IReadOnlyCollection<WidgetSortOrderItem>? items,
        CancellationToken ct)
    {
        if (items == null || items.Count == 0)
            return BadRequest(new { success = false, message = "En az bir siralama kaydi gerekli." });
        try
        {
            await _widgetService.ReorderWidgetsAsync(items, ct);
            return Ok(new { success = true });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "İşlem sırasında bir hata oluştu." });
        }
    }

    // PATCH /api/widgets/widgets/{widgetId}/active
    // Body: { "isActive": true/false }
    // Yalnizca IsActive gunceller — toggle icin tam upsert gondermek lookup/grid/rehber
    // bagli widget'larda metadata kaybina yol aciyordu.
    [HttpPatch("widgets/{widgetId:int}/active")]
    public async Task<IActionResult> PatchActive(int widgetId, [FromBody] PatchWidgetActiveRequest? req, CancellationToken ct)
    {
        if (req == null)
            return BadRequest(new { success = false, message = "Request govdesi bos." });
        try
        {
            await _widgetService.SetWidgetActiveAsync(widgetId, req.IsActive, ct);
            return Ok(new { success = true });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "İşlem sırasında bir hata oluştu." });
        }
    }

    // ══════════════════════════════════════════════════════════
    // Widget tanim transport (export/import) — 2026-07-06
    // ══════════════════════════════════════════════════════════

    // GET /api/widgets/forms/{formCode}/export
    // Formun custom widget tanimlarini JSON dosyasi olarak indirir
    // (sirketler arasi kopyalama + test→canli tasima).
    [HttpGet("forms/{formCode}/export")]
    public async Task<IActionResult> ExportWidgetPackage(string formCode, CancellationToken ct)
    {
        var package = await _widgetService.ExportWidgetPackageAsync(formCode, ct);
        if (package == null)
            return NotFound(new { success = false, message = "Form bulunamadi." });

        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(package,
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                WriteIndented = true,
            });
        var fileName = $"calibra-widgets-{package.FormCode.ToLowerInvariant()}-{DateTime.UtcNow:yyyyMMdd}.json";
        return File(json, "application/json", fileName);
    }

    // POST /api/widgets/forms/{formCode}/import
    // Body: WidgetPackageDto (export ciktisi). WidgetCode uzerinden upsert.
    [HttpPost("forms/{formCode}/import")]
    public async Task<IActionResult> ImportWidgetPackage(
        string formCode,
        [FromBody] WidgetPackageDto? package,
        CancellationToken ct)
    {
        if (package == null)
            return BadRequest(new { success = false, message = "Paket govdesi bos veya JSON okunamadi." });
        try
        {
            var result = await _widgetService.ImportWidgetPackageAsync(formCode, package, ct);
            return Ok(new { success = true, created = result.Created, updated = result.Updated, skipped = result.Skipped });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception)
        {
            return StatusCode(500, new { success = false, message = "İşlem sırasında bir hata oluştu." });
        }
    }

    // ══════════════════════════════════════════════════════════
    // Attachment widget tipi — dosya yukle/indir (2026-07-06)
    // Depo: merkezi dbo.Attachment (system DB), FormId=WidgetAttachment,
    // RefId=WidgetMas.Id. Kayit bagi WidgetTra.Value = Attachment.Id.
    // ══════════════════════════════════════════════════════════

    private static readonly string[] BlockedAttachmentExtensions =
    {
        ".exe", ".dll", ".bat", ".cmd", ".ps1", ".msi", ".scr", ".com", ".vbs", ".jar", ".sh",
    };

    private int? CurrentUserIdOrNull()
    {
        var raw = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) && id > 0 ? id : null;
    }

    // POST /api/widgets/attachments  (multipart/form-data: widgetId, file)
    [HttpPost("attachments")]
    [RequestSizeLimit(25 * 1024 * 1024)]
    public async Task<IActionResult> UploadWidgetAttachment(
        [FromForm] int widgetId,
        [FromForm] IFormFile? file,
        CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { success = false, message = "Dosya secilmedi." });

        var fileName = Path.GetFileName(file.FileName ?? "dosya");
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (BlockedAttachmentExtensions.Contains(ext))
            return BadRequest(new { success = false, message = $"'{ext}' uzantili dosyalar guvenlik nedeniyle yuklenemez." });

        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);

        var id = await _attachments.AddAsync(new CalibraHub.Domain.Entities.Attachment
        {
            FormId        = CalibraHub.Application.Constants.AttachmentFormIds.WidgetAttachment,
            RefId         = widgetId,
            FileName      = fileName,
            ContentType   = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            FileSize      = ms.Length,
            CreatedById   = CurrentUserIdOrNull(),
            BinaryContent = ms.ToArray(),
        }, ct);

        return Ok(new { success = true, id, fileName, fileSize = ms.Length, contentType = file.ContentType });
    }

    // GET /api/widgets/attachments/{id} — meta (dosya adi/boyut; binary yok)
    [HttpGet("attachments/{id:int}")]
    public async Task<IActionResult> GetWidgetAttachmentMeta(int id, CancellationToken ct)
    {
        var att = await _attachments.GetByIdAsync(id, ct);
        if (att == null || !att.IsActive || att.FormId != CalibraHub.Application.Constants.AttachmentFormIds.WidgetAttachment)
            return NotFound(new { success = false, message = "Dosya bulunamadi." });
        return Ok(new { success = true, id = att.Id, fileName = att.FileName, fileSize = att.FileSize, contentType = att.ContentType });
    }

    // GET /api/widgets/attachments/{id}/download?inline=1
    // inline: gorsel/pdf onizleme icin Content-Disposition inline.
    [HttpGet("attachments/{id:int}/download")]
    public async Task<IActionResult> DownloadWidgetAttachment(int id, [FromQuery] bool inline, CancellationToken ct)
    {
        var att = await _attachments.GetByIdAsync(id, ct);
        if (att == null || !att.IsActive || att.FormId != CalibraHub.Application.Constants.AttachmentFormIds.WidgetAttachment)
            return NotFound(new { success = false, message = "Dosya bulunamadi." });

        var bytes = await _attachments.GetBinaryAsync(id, ct);
        if (bytes == null || bytes.Length == 0)
            return NotFound(new { success = false, message = "Dosya icerigi bulunamadi." });

        var contentType = string.IsNullOrWhiteSpace(att.ContentType) ? "application/octet-stream" : att.ContentType;
        if (inline)
        {
            Response.Headers.Append("Content-Disposition",
                $"inline; filename*=UTF-8''{Uri.EscapeDataString(att.FileName)}");
            return File(bytes, contentType);
        }
        return File(bytes, contentType, att.FileName);
    }

    // DELETE /api/widgets/attachments/{id} — soft-delete (deger degistirilince eski dosya)
    [HttpDelete("attachments/{id:int}")]
    public async Task<IActionResult> DeleteWidgetAttachment(int id, CancellationToken ct)
    {
        var att = await _attachments.GetByIdAsync(id, ct);
        if (att == null || att.FormId != CalibraHub.Application.Constants.AttachmentFormIds.WidgetAttachment)
            return NotFound(new { success = false, message = "Dosya bulunamadi." });
        await _attachments.DeleteAsync(id, ct);
        return Ok(new { success = true });
    }
}

public sealed record PatchIsPlainFieldRequest(bool IsPlainField);

public sealed record PatchWidgetActiveRequest(bool IsActive);
