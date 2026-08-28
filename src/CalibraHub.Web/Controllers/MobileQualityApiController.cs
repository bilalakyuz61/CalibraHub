using System.Security.Claims;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Mobil "Kalite Muayene" — sahada muayene açma, ölçüm girme, fotoğraf ekleme, tamamlama.
///
/// Uclar:
///   GET  /api/mobile/quality/plan?itemId=&amp;type=        — malzemenin muayene plani (yoksa null)
///   GET  /api/mobile/quality/defect-codes               — hata kodu katalogu
///   GET  /api/mobile/quality/source-lines?...           — kaynak belgenin kalemleri (on-doldurma)
///   GET  /api/mobile/quality/{documentId}               — muayene detayi
///   POST /api/mobile/quality                            — kaydet (yeni/guncelle)
///   POST /api/mobile/quality/{documentId}/complete      — tamamla (+ gerekiyorsa karar)
///   GET  /api/mobile/quality/{documentId}/photos        — foto listesi
///   POST /api/mobile/quality/{documentId}/photos        — foto yukle (multipart)
///
/// TASARIM — is mantigi TEKRARLANMAZ: dogrulama, otomatik sonuc hesaplama (Verdict), durum
/// gecis kurallari ve audit <see cref="IQualityService"/> icindedir. Bu controller yalnizca
/// mobil icin sadelestirilmis govde alir ve servise devreder; web ekraniyla AYNI kurallar isler.
///
/// DIKKAT — muayene kimligi: tum uclarda <c>documentId</c> = <c>Document.Id</c>'dir,
/// <c>QualityInspection.Id</c> DEGIL. Muayene Document'a 1-1 companion olarak baglidir.
///
/// MUAYENE PLANI ZORUNLU DEGIL: plan varsa karakteristikler istemciye hazir gelir; yoksa
/// kullanici tek bir "genel degerlendirme" satiriyla sade kayit acar. Plan ucu bu yuzden
/// plan bulunamadiginda 404 DEGIL, <c>null</c> doner — "plan yok" bir hata degil, normal durum.
/// </summary>
[ApiController]
[Route("api/mobile/quality")]
[IgnoreAntiforgeryToken]
[EnableCors("MobileApi")]
[Authorize]
[CalibraHub.Web.Authorization.PermissionScope(FormCodes.QualityInspectionEdit)]
public sealed class MobileQualityApiController : ControllerBase
{
    /// <summary>
    /// Tarayicida CALISAN icerik turleri — ek dosyalar ayni origin'den servis edildigi icin
    /// bir .html/.svg eki depolanmis XSS'e donusur. WidgetsController'daki AYNI liste
    /// (2026-08-24 guvenlik denetimi bulgusu); tek kaynak olmadigi icin bilincli kopya.
    /// </summary>
    private static readonly string[] BlockedExtensions =
    {
        ".exe", ".dll", ".bat", ".cmd", ".ps1", ".msi", ".scr", ".com", ".vbs", ".jar", ".sh",
        ".html", ".htm", ".xhtml", ".svg", ".svgz", ".mhtml", ".xml", ".xsl", ".xslt",
        ".js", ".mjs", ".hta", ".jsp", ".asp", ".aspx", ".php", ".cshtml",
    };

    private readonly IQualityService _quality;
    private readonly IPersonnelService _personnel;
    private readonly IAttachmentRepository _attachments;
    private readonly ILogger<MobileQualityApiController> _logger;

    public MobileQualityApiController(
        IQualityService quality,
        IPersonnelService personnel,
        IAttachmentRepository attachments,
        ILogger<MobileQualityApiController> logger)
    {
        _quality = quality;
        _personnel = personnel;
        _attachments = attachments;
        _logger = logger;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Lookup
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>Malzemenin muayene plani. Plan YOKSA <c>null</c> doner (hata degil).</summary>
    [HttpGet("plan")]
    public async Task<IActionResult> Plan([FromQuery] int itemId, [FromQuery] byte type, CancellationToken ct)
    {
        if (itemId <= 0) return BadRequest(new { error = "Malzeme belirtilmedi." });
        var plan = await _quality.FindPlanForItemAsync(itemId, type, ct);
        return Ok(plan);
    }

    [HttpGet("defect-codes")]
    public async Task<IActionResult> DefectCodes(CancellationToken ct)
        => Ok(await _quality.GetDefectCodeOptionsAsync(ct));

    // ──────────────────────────────────────────────────────────────────────
    // Muayene
    // ──────────────────────────────────────────────────────────────────────

    [HttpGet("{documentId:int}")]
    public async Task<IActionResult> Detail(int documentId, CancellationToken ct)
    {
        var detail = await _quality.GetInspectionAsync(documentId, ct);
        if (detail is null) return NotFound(new { error = "Muayene bulunamadı." });
        return Ok(detail);
    }

    /// <summary>Mobil kaydetme govdesi — sadelestirilmis (bkz. sinif KDoc'u).</summary>
    public sealed record MobileInspectionBody(
        int Id,
        int? PlanId,
        int? ItemId,
        byte InspectionType,
        string? SourceKind,
        int? SourceId,
        decimal? Quantity,
        string? Notes,
        IReadOnlyList<MobileInspectionLine>? Lines);

    public sealed record MobileInspectionLine(
        int? PlanLineId,
        string CharacteristicName,
        decimal? Nominal,
        decimal? LowerTol,
        decimal? UpperTol,
        decimal? Measured,
        bool IsNumeric,
        byte Result,
        int? DefectCodeId,
        int OrderNo,
        string? Notes);

    [HttpPost]
    public async Task<IActionResult> Save([FromBody] MobileInspectionBody? body, CancellationToken ct)
    {
        var lines = (body?.Lines ?? Array.Empty<MobileInspectionLine>())
            .Where(l => l is not null && !string.IsNullOrWhiteSpace(l.CharacteristicName))
            .ToList();

        // Sessiz atlama YOK — bos govde/satirsiz istek acikca reddedilir.
        if (body is null || lines.Count == 0)
            return BadRequest(new { ok = false, error = "Muayenede en az bir ölçüm satırı olmalı." });

        // Muayeneyi KIM yapti: kullaniciya SORULMAZ, oturumun bagli personel kaydindan cozulur.
        // (Web ekrani bu alani bugun null gonderiyor — mobilde dolduruyoruz, izlenebilirlik kazanci.)
        var inspectorId = await ResolveMyPersonnelIdAsync(ct);

        var request = new SaveQualityInspectionRequest(
            Id: body.Id,
            PlanId: body.PlanId,
            ItemId: body.ItemId,
            InspectionType: body.InspectionType,
            SourceKind: string.IsNullOrWhiteSpace(body.SourceKind) ? null : body.SourceKind.Trim(),
            SourceId: body.SourceId,
            Quantity: body.Quantity,
            InspectedByPersonnelId: inspectorId,
            InspectedAt: DateTime.Now,
            Notes: string.IsNullOrWhiteSpace(body.Notes) ? null : body.Notes.Trim(),
            Lines: lines.Select(l => new SaveQualityInspectionLine(
                Id: 0,                       // satir upsert'i delete-all + re-insert; Id anlamsiz
                PlanLineId: l.PlanLineId,    // KORUNUR (web null gonderiyor) — plan izlenebilirligi
                CharacteristicName: l.CharacteristicName.Trim(),
                Nominal: l.Nominal,
                LowerTol: l.LowerTol,
                UpperTol: l.UpperTol,
                Measured: l.Measured,
                IsNumeric: l.IsNumeric,
                Result: l.Result,            // sayisal satirda sunucu yok sayar, tolerans hesaplar
                DefectCodeId: l.DefectCodeId,
                OrderNo: l.OrderNo,
                Notes: string.IsNullOrWhiteSpace(l.Notes) ? null : l.Notes.Trim())).ToList());

        try
        {
            var (ok, error, documentId) = await _quality.SaveInspectionAsync(request, CurrentUserIdOrNull(), ct);
            if (!ok) return BadRequest(new { ok = false, error = error ?? "Muayene kaydedilemedi." });

            // Kaydettikten sonra detayi geri okuyup Verdict'i dondururuz: sonuc SUNUCUDA
            // hesaplandigi icin istemci bunu baska turlu bilemez ve "tamamlanabilir mi,
            // karar gerekli mi" kararini veremez.
            var saved = await _quality.GetInspectionAsync(documentId, ct);
            return Ok(new
            {
                ok = true,
                documentId,
                documentNumber = saved?.DocumentNumber,
                verdict = saved?.Verdict,
                status = saved?.Status,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MobileQuality] Muayene kaydedilemedi. User={User}", User?.Identity?.Name);
            return BadRequest(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    public sealed record MobileCompleteBody(byte? Disposition);

    /// <summary>
    /// Muayeneyi tamamlar. Sunucu kurali: <c>Verdict</c> uygunsuz ise <c>Disposition</c>
    /// (Kabul/Ret/Yeniden Islem/Sapma Izni) ZORUNLU — bu yuzden karar once yazilir,
    /// sonra durum degistirilir. Sira ters olursa tamamlama reddedilir.
    /// </summary>
    [HttpPost("{documentId:int}/complete")]
    public async Task<IActionResult> Complete(int documentId, [FromBody] MobileCompleteBody? body, CancellationToken ct)
    {
        try
        {
            if (body?.Disposition is byte disp && disp > 0)
            {
                var (dOk, dErr) = await _quality.SetInspectionDispositionAsync(
                    new SetInspectionDispositionRequest(documentId, disp), CurrentUserIdOrNull(), ct);
                if (!dOk) return BadRequest(new { ok = false, error = dErr ?? "Uygunsuzluk kararı kaydedilemedi." });
            }

            // 2 = InspectionStatus.Completed
            var (ok, error) = await _quality.ChangeInspectionStatusAsync(
                new ChangeInspectionStatusRequest(documentId, 2), CurrentUserIdOrNull(), ct);
            if (!ok) return BadRequest(new { ok = false, error = error ?? "Muayene tamamlanamadı." });

            return Ok(new { ok = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MobileQuality] Tamamlama basarisiz. DocumentId={Id}", documentId);
            return BadRequest(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Fotograf / ek
    // ──────────────────────────────────────────────────────────────────────

    [HttpGet("{documentId:int}/photos")]
    public async Task<IActionResult> Photos(int documentId, CancellationToken ct)
    {
        if (await EnsureIsInspectionAsync(documentId, ct) is { } denied) return denied;

        var list = await _attachments.GetByFormRefAsync(AttachmentFormIds.QualityInspection, documentId, ct);
        return Ok(list.Where(a => a.IsActive).Select(a => new
        {
            id = a.Id,
            fileName = a.FileName,
            contentType = a.ContentType,
            fileSize = a.FileSize,
            created = a.Created,
        }));
    }

    [HttpPost("{documentId:int}/photos")]
    [RequestSizeLimit(25 * 1024 * 1024)]
    public async Task<IActionResult> UploadPhoto(int documentId, [FromForm] IFormFile? file, CancellationToken ct)
    {
        if (await EnsureIsInspectionAsync(documentId, ct) is { } denied) return denied;

        if (file is null || file.Length == 0)
            return BadRequest(new { ok = false, error = "Dosya seçilmedi." });

        var fileName = Path.GetFileName(file.FileName ?? "foto.jpg");
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (BlockedExtensions.Contains(ext))
            return BadRequest(new { ok = false, error = $"'{ext}' uzantılı dosyalar güvenlik nedeniyle yüklenemez." });

        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);

        var id = await _attachments.AddAsync(new CalibraHub.Domain.Entities.Attachment
        {
            FormId        = AttachmentFormIds.QualityInspection,
            RefId         = documentId,
            FileName      = fileName,
            ContentType   = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            FileSize      = ms.Length,
            CreatedById   = CurrentUserIdOrNull(),
            BinaryContent = ms.ToArray(),
        }, ct);

        return Ok(new { ok = true, id, fileName, fileSize = ms.Length });
    }

    // ──────────────────────────────────────────────────────────────────────
    // Yardimcilar
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Verilen documentId GERCEKTEN bir muayene mi? Ek dosya uclari yalnizca muayeneye
    /// baglanmali — aksi halde bu uctan baska belge turlerine (teklif, irsaliye…) ek
    /// iliştirilebilirdi. Muayene degilse/yoksa 404.
    /// </summary>
    private async Task<IActionResult?> EnsureIsInspectionAsync(int documentId, CancellationToken ct)
    {
        var detail = await _quality.GetInspectionAsync(documentId, ct);
        return detail is null ? NotFound(new { ok = false, error = "Muayene bulunamadı." }) : null;
    }

    private async Task<int?> ResolveMyPersonnelIdAsync(CancellationToken ct)
    {
        var userId = CurrentUserIdOrNull();
        if (userId is null) return null;
        var me = await _personnel.GetByUserIdAsync(userId.Value, ct);
        return me?.Id;
    }

    private int? CurrentUserIdOrNull()
        => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id > 0 ? id : null;
}
