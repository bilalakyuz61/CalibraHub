using System.Security.Claims;
using CalibraHub.Application.Abstractions.DesignProvider;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.SmartBoard;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Web.Models.Assets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Varlık Yönetimi — birleşik (Asset + Machine projection) SmartBoard liste + master-detail
/// edit ekranı + geçmiş (AssetEvent) API'leri. Machine deseni (MachineController) ile aynı şekil.
/// </summary>
[Authorize]
[CalibraHub.Web.Authorization.PermissionScope(FormCodes.Assets)]
public sealed class AssetsController : Controller
{
    private readonly IAssetService _assetService;
    private readonly IWidgetService _widgetService;
    private readonly IPrintDispatcher _printDispatcher;
    private readonly IAttachmentRepository _attachments;

    private const string AttEntityDoc = "Asset";        // evraklar (çoklu)
    private const string AttEntityImage = "AssetImage";  // kapak görseli (tek)
    private const string AttEntitySignature = "AssetAssignment"; // zimmet/iade imzası (hareket bazlı)

    public AssetsController(IAssetService assetService, IWidgetService widgetService, IPrintDispatcher printDispatcher, IAttachmentRepository attachments)
    {
        _assetService = assetService;
        _widgetService = widgetService;
        _printDispatcher = printDispatcher;
        _attachments = attachments;
    }

    private int? CurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : null;
    }

    // ── Liste + SmartBoard ────────────────────────────────────────

    [HttpGet]
    public IActionResult Index()
        => View("~/Views/Assets/Assets.cshtml", new AssetsSmartBoardViewModel());

    /// <summary>In-place refresh endpoint (GET, JSON) — *BoardConfig deseni; izin filtresinden muaf.</summary>
    [HttpGet]
    public async Task<IActionResult> AssetsBoardConfig(CancellationToken ct)
        => Json(await BuildBoardConfigAsync(ct));

    private async Task<object> BuildBoardConfigAsync(CancellationToken ct)
    {
        var cards = await _assetService.GetBoardCardsAsync(ct);

        var masterWidgets = new List<object>();
        var schema = await _widgetService.GetFormSchemaByCodeAsync(FormCodes.Assets, ct);
        if (schema != null)
        {
            foreach (var w in schema.Widgets.Where(w => w.IsActive
                && !string.Equals(w.DataType, "group", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(w.DataType, "grid", StringComparison.OrdinalIgnoreCase)))
            {
                masterWidgets.Add(new
                {
                    id = w.WidgetCode,
                    dbId = w.Id,
                    isPlainField = w.IsPlainField,
                    type = "data",
                    dataType = w.DataType.ToLowerInvariant(),
                    label = w.Label,
                });
            }
        }

        var recordIds = cards.Where(c => c.AssetId.HasValue).Select(c => c.AssetId!.Value.ToString()).ToArray();
        var batchWidgets = masterWidgets.Count > 0 && recordIds.Length > 0
            ? await _widgetService.GetBatchRenderModelsAsync(FormCodes.Assets, recordIds, ct)
            : new Dictionary<string, IReadOnlyCollection<WidgetRenderDto>>();

        // Kapak görseli olan varlıklar (kartta görsel göstermek için)
        var imageSet = (await _attachments.GetEntityIdsWithAttachmentAsync(AttEntityImage, ct)).ToHashSet(StringComparer.Ordinal);

        return SmartBoard.For(cards)
            .WithBoardKey("assets-list")
            .WithTitle("Varlık Yönetimi", subtitle: $"{cards.Count} varlık")
            .WithIcon("Boxes", "indigo")
            .WithRefreshUrl("/Assets/AssetsBoardConfig")
            .WithSearchPlaceholder("Hızlı ara… (ad, lokasyon)")
            .WithEmptyText("Henüz varlık tanımlanmamış")
            .AddHeaderAction("new", "Yeni Varlık", "Plus", "#asset-new")
            .WithMasterWidgets(masterWidgets)
            .MapEntities(c =>
            {
                var eb = SmartBoardEntity.For(c.CardId, c.Name)
                    .WithSubtitle(KindText(c.Kind))
                    .WithDescription(string.Empty)
                    .WithStatusBadge(StatusText(c.Status), StatusColor(c.Status));

                // Kapak görseli (varsa) — varlığı görsel olarak tanımlamak için kartta gösterilir
                if (c.AssetId.HasValue && imageSet.Contains(c.AssetId.Value.ToString()))
                    eb.WithImageUrl($"/Assets/AssetImage?id={c.AssetId.Value}");

                // Zimmet + departman + lokasyon karttan kaldırıldı — zimmetleme ekranından hareket olarak yönetilir.
                if (c.NextMaintenanceDate.HasValue)
                    eb.AddTextWidget("w_maint", "Sonraki Bakım", c.NextMaintenanceDate.Value.ToString("dd.MM.yyyy"),
                        color: DueColor(c.NextMaintenanceDate.Value));
                if (c.NextCalibrationDate.HasValue)
                    eb.AddTextWidget("w_calib", "Sonraki Kalibrasyon", c.NextCalibrationDate.Value.ToString("dd.MM.yyyy"),
                        color: DueColor(c.NextCalibrationDate.Value));
                if (c.IsVirtualMachine)
                    eb.AddTextWidget("w_src", "Kaynak", "Makine (henüz bağlanmadı)", color: "violet");

                if (c.AssetId.HasValue && batchWidgets.TryGetValue(c.AssetId.Value.ToString(), out var dtos))
                {
                    eb.AppendWidgets(dtos.Select(w => (object)new
                    {
                        id = w.WidgetId,
                        type = "data",
                        dataType = w.DataType.ToLowerInvariant(),
                        label = w.Label,
                        value = w.Value,
                        isPlainField = w.IsPlainField,
                        detail = (string?)null,
                        color = (string?)null,
                    }));
                }

                // Hash URL → konteyner içinde (sekme şeridi kalır) edit iframe'i açar.
                var editUrl = c.AssetId.HasValue
                    ? $"#asset-edit-a{c.AssetId.Value}"
                    : $"#asset-edit-m{c.MachineId}";

                if (c.AssetId.HasValue)
                {
                    eb.WithEditAndDelete(
                        editUrl: editUrl,
                        deleteApiUrl: $"/Assets/DeleteAssetJson?id={c.AssetId.Value}",
                        deleteConfirm: $"Bu varlığı silmek istediğinize emin misiniz? ({c.Name})");
                }
                else
                {
                    // Sanal makine kartı — silme yok (Makine modülünde yönetilir); açınca materialize olur.
                    eb.WithPrimaryAction("Düzenle", "Edit", editUrl, color: "amber", hideButton: true);
                }
                return eb;
            })
            .Build();
    }

    // ── Zimmetleme / Kalibrasyon / Bakım board'ları (alt sekmeler) ──

    [HttpGet]
    public async Task<IActionResult> AssignmentBoardConfig(CancellationToken ct)
    {
        var cards = (await _assetService.GetBoardCardsAsync(ct)).Where(c => c.AssetId.HasValue && c.IsAssignable).ToList();
        return Json(SmartBoard.For(cards)
            .WithBoardKey("assets-assignment")
            .WithTitle("Zimmetleme", subtitle: $"{cards.Count} varlık")
            .WithIcon("UserCheck", "blue")
            .WithRefreshUrl("/Assets/AssignmentBoardConfig")
            .WithSearchPlaceholder("Hızlı ara… (ad, lokasyon)")
            .WithEmptyText("Zimmetlenebilir varlık yok (varlık kartında \"Zimmetlenebilir\" seçeneğini açın)")
            .AddHeaderAction("bulk", "Toplu Zimmet", "Users", "#asset-bulk-assign")
            .MapEntities(c => SmartBoardEntity.For(c.CardId, c.Name)
                .WithSubtitle(KindText(c.Kind))
                .WithDescription(c.LocationName ?? string.Empty)
                .WithStatusBadge(StatusText(c.Status), StatusColor(c.Status))
                .AddTextWidget("w_zimmet", "Zimmet",
                    !string.IsNullOrWhiteSpace(c.AssignedPersonnelName) ? c.AssignedPersonnelName
                        : !string.IsNullOrWhiteSpace(c.DepartmentName) ? c.DepartmentName + " (departman)"
                        : "Boşta",
                    color: (!string.IsNullOrWhiteSpace(c.AssignedPersonnelName) || !string.IsNullOrWhiteSpace(c.DepartmentName)) ? "blue" : "slate")
                .WithNavigateAction("Zimmet İşlemi", "UserCheck", $"#asset-assign-{c.AssetId}", color: "indigo"))
            .Build());
    }

    [HttpGet]
    public async Task<IActionResult> CalibrationBoardConfig(CancellationToken ct)
    {
        var cards = (await _assetService.GetBoardCardsAsync(ct))
            .Where(c => c.AssetId.HasValue && c.IsCalibrated)
            .OrderBy(c => c.NextCalibrationDate ?? DateTime.MaxValue).ToList();
        return Json(SmartBoard.For(cards)
            .WithBoardKey("assets-calibration")
            .WithTitle("Kalibrasyon Takibi", subtitle: $"{cards.Count} cihaz")
            .WithIcon("Crosshair", "violet")
            .WithRefreshUrl("/Assets/CalibrationBoardConfig")
            .WithSearchPlaceholder("Hızlı ara…")
            .WithEmptyText("Kalibrasyon takipli cihaz yok (varlık kartında \"Kalibre edilir\" seçeneğini açın)")
            .MapEntities(c => SmartBoardEntity.For(c.CardId, c.Name)
                .WithSubtitle(KindText(c.Kind))
                .WithDescription(c.LocationName ?? string.Empty)
                .WithStatusBadge(DueBadgeText(c.NextCalibrationDate), DueBadgeColor(c.NextCalibrationDate))
                .AddTextWidget("w_next", "Sonraki Kalibrasyon",
                    c.NextCalibrationDate?.ToString("dd.MM.yyyy") ?? "Planlanmadı",
                    detail: DueText(c.NextCalibrationDate),
                    color: c.NextCalibrationDate.HasValue ? DueColor(c.NextCalibrationDate.Value) : "slate")
                .WithNavigateAction("Kalibrasyon Yap", "Crosshair", $"#asset-calib-{c.AssetId}", color: "violet"))
            .Build());
    }

    [HttpGet]
    public async Task<IActionResult> MaintenanceBoardConfig(CancellationToken ct)
    {
        var cards = (await _assetService.GetBoardCardsAsync(ct))
            .Where(c => c.AssetId.HasValue && c.IsMaintained)
            .OrderBy(c => c.NextMaintenanceDate ?? DateTime.MaxValue).ToList();
        return Json(SmartBoard.For(cards)
            .WithBoardKey("assets-maintenance")
            .WithTitle("Bakım Takibi", subtitle: $"{cards.Count} varlık")
            .WithIcon("Wrench", "amber")
            .WithRefreshUrl("/Assets/MaintenanceBoardConfig")
            .WithSearchPlaceholder("Hızlı ara…")
            .WithEmptyText("Bakım takipli varlık yok (varlık kartında \"Bakım yapılır\" seçeneğini açın)")
            .MapEntities(c => SmartBoardEntity.For(c.CardId, c.Name)
                .WithSubtitle(KindText(c.Kind))
                .WithDescription(c.LocationName ?? string.Empty)
                .WithStatusBadge(DueBadgeText(c.NextMaintenanceDate), DueBadgeColor(c.NextMaintenanceDate))
                .AddTextWidget("w_next", "Sonraki Bakım",
                    c.NextMaintenanceDate?.ToString("dd.MM.yyyy") ?? "Planlanmadı",
                    detail: DueText(c.NextMaintenanceDate),
                    color: c.NextMaintenanceDate.HasValue ? DueColor(c.NextMaintenanceDate.Value) : "slate")
                .WithNavigateAction("Bakım Yap", "Wrench", $"#asset-maint-{c.AssetId}", color: "amber"))
            .Build());
    }

    // ── Zimmet ekranı ─────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> AssetAssign(int id, CancellationToken ct)
    {
        var a = await _assetService.GetAssetByIdAsync(id, ct);
        if (a is null) return NotFound();
        return View("~/Views/Assets/AssetAssign.cshtml", new AssetAssignViewModel
        {
            Id = a.Id,
            AssetCode = a.AssetCode,
            AssetName = a.AssetName,
        });
    }

    /// <summary>Toplu zimmetleme ekranı (çoklu varlık → tek kişi/departman).</summary>
    [HttpGet]
    public IActionResult BulkAssign()
        => View("~/Views/Assets/AssetBulkAssign.cshtml");

    /// <summary>Toplu zimmet için aday varlıklar — zimmetlenebilir + materialize edilmiş.</summary>
    [HttpGet]
    public async Task<IActionResult> BulkAssignCandidatesJson(CancellationToken ct)
    {
        var cards = (await _assetService.GetBoardCardsAsync(ct))
            .Where(c => c.AssetId.HasValue && c.IsAssignable)
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase);
        return Json(cards.Select(c => new
        {
            id = c.AssetId!.Value,
            name = c.Name,
            kind = KindText(c.Kind),
            currentAssignee = !string.IsNullOrWhiteSpace(c.AssignedPersonnelName) ? c.AssignedPersonnelName
                : !string.IsNullOrWhiteSpace(c.DepartmentName) ? c.DepartmentName + " (departman)" : null,
        }));
    }

    [HttpPost]
    public async Task<IActionResult> BulkAssignJson([FromBody] BulkAssignInput input, CancellationToken ct)
    {
        if (input is null || input.AssetIds is null || input.AssetIds.Count == 0)
            return Json(new { success = false, message = "En az bir varlık seçiniz." });
        if ((input.PersonnelId ?? 0) <= 0 && (input.DepartmentId ?? 0) <= 0)
            return Json(new { success = false, message = "Personel veya departman seçimi zorunludur." });

        var date = input.AssignDate ?? DateTime.Today;
        var userId = CurrentUserId();
        int ok = 0; var errors = new List<string>();
        foreach (var assetId in input.AssetIds.Distinct())
        {
            try
            {
                await _assetService.AssignAsync(assetId, input.PersonnelId, input.DepartmentId, input.LocationId, date, input.Note, null, userId, ct);
                ok++;
            }
            catch (ArgumentException ex) { errors.Add($"#{assetId}: {ex.Message}"); }
        }
        return Json(new { success = ok > 0, assigned = ok, failed = errors.Count, errors });
    }

    /// <summary>Bakım & Kalibrasyon işlem ekranı (karttan açılır).</summary>
    [HttpGet]
    public async Task<IActionResult> AssetMaintenance(int id, string? mode, CancellationToken ct)
    {
        var a = await _assetService.GetAssetByIdAsync(id, ct);
        if (a is null) return NotFound();
        // Tip açılış kaynağından sabit: Bakım Takibi → maint, Kalibrasyon Takibi → calib (formda seçtirilmez)
        ViewData["MaintMode"] = string.Equals(mode, "calib", StringComparison.OrdinalIgnoreCase) ? "calib" : "maint";
        return View("~/Views/Assets/AssetMaintenance.cshtml", new AssetAssignViewModel
        {
            Id = a.Id,
            AssetCode = a.AssetCode,
            AssetName = a.AssetName,
        });
    }

    /// <summary>Varlığın zimmet hareketleri (geçmiş + aktif). *Json — izin filtresinden muaf değil ama GET=VIEW.</summary>
    [HttpGet]
    public async Task<IActionResult> AssignmentsJson(int assetId, CancellationToken ct)
    {
        var list = await _assetService.GetAssignmentsAsync(assetId, ct);
        // İmza eki olan hareketleri işaretle (Zimmet Hareketleri'nde "imza" linki için)
        var signed = (await _attachments.GetEntityIdsWithAttachmentAsync(AttEntitySignature, ct)).ToHashSet(StringComparer.Ordinal);
        return Json(list.Select(a => new
        {
            a.Id, a.AssetId, a.PersonnelId, a.PersonnelName, a.DepartmentId, a.DepartmentName,
            a.LocationId, a.LocationName, a.AssignDate, a.ReturnDate, a.AssignNote, a.ReturnNote,
            a.DocumentNo, a.Created, a.CreatedById, a.IsActive,
            hasSignature = signed.Contains(a.Id.ToString()),
        }));
    }

    /// <summary>Sade zimmet takip raporu (tüm varlıklar). Kompleks analiz Grafana'da.</summary>
    [HttpGet]
    public async Task<IActionResult> AssignmentReportJson(CancellationToken ct)
        => Json(await _assetService.GetAssignmentReportAsync(ct));

    [HttpPost]
    public async Task<IActionResult> AssignAssetJson([FromBody] AssignInput input, CancellationToken ct)
    {
        if (input is null || input.AssetId <= 0)
            return Json(new { success = false, message = "Geçersiz istek." });
        if ((input.PersonnelId ?? 0) <= 0 && (input.DepartmentId ?? 0) <= 0)
            return Json(new { success = false, message = "Personel veya departman seçimi zorunludur." });
        try
        {
            var assignmentId = await _assetService.AssignAsync(
                input.AssetId, input.PersonnelId, input.DepartmentId, input.LocationId, input.AssignDate ?? DateTime.Today,
                input.Note, input.DocumentNo, CurrentUserId(), ct);
            return Json(new { success = true, assignmentId });
        }
        catch (ArgumentException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> ReturnAssetJson([FromBody] ReturnInput input, CancellationToken ct)
    {
        if (input is null || input.AssetId <= 0)
            return Json(new { success = false, message = "Geçersiz istek." });
        try
        {
            await _assetService.ReturnAsync(input.AssetId, input.ReturnDate ?? DateTime.Today, input.Note, CurrentUserId(), ct);
            return Json(new { success = true });
        }
        catch (ArgumentException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    /// <summary>Dokunmatik imza kaydeder — base64 PNG'yi zimmet hareketine (AssetAssignment) Attachment olarak ekler.</summary>
    [HttpPost]
    public async Task<IActionResult> SaveAssignmentSignatureJson([FromBody] SignatureInput input, CancellationToken ct)
    {
        if (input is null || input.AssignmentId <= 0) return Json(new { success = false, message = "Geçersiz hareket." });
        var bytes = DecodeDataUrl(input.DataUrl);
        if (bytes is null || bytes.Length == 0) return Json(new { success = false, message = "İmza boş." });
        var isReturn = string.Equals(input.Kind, "return", StringComparison.OrdinalIgnoreCase);
        await _attachments.AddAsync(new Attachment
        {
            EntityType = AttEntitySignature,
            EntityId = input.AssignmentId.ToString(),
            FileName = isReturn ? "iade-imza.png" : "zimmet-imza.png",
            ContentType = "image/png",
            FileSize = bytes.LongLength,
            Description = isReturn ? "İade imzası" : "Zimmet imzası",
            CreatedById = CurrentUserId(),
            BinaryContent = bytes,
        }, ct);
        return Json(new { success = true });
    }

    /// <summary>Zimmet hareketinin en güncel imzasını inline döner.</summary>
    [HttpGet]
    public async Task<IActionResult> AssignmentSignatureImage(int assignmentId, CancellationToken ct)
    {
        if (assignmentId <= 0) return NotFound();
        var sig = (await _attachments.GetByEntityAsync(AttEntitySignature, assignmentId.ToString(), ct)).LastOrDefault();
        if (sig is null) return NotFound();
        var bytes = await _attachments.GetBinaryAsync(sig.Id, ct);
        if (bytes is null) return NotFound();
        Response.Headers.CacheControl = "no-cache";
        return File(bytes, sig.ContentType ?? "image/png");
    }

    /// <summary>data:image/png;base64,xxx → byte[]</summary>
    private static byte[]? DecodeDataUrl(string? dataUrl)
    {
        if (string.IsNullOrWhiteSpace(dataUrl)) return null;
        var idx = dataUrl.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
        var b64 = idx >= 0 ? dataUrl[(idx + 7)..] : dataUrl;
        try { return Convert.FromBase64String(b64); } catch { return null; }
    }

    /// <summary>
    /// Zimmet belgesini Belge Tasarımcısı (DocDesigner) ile basar — "zimmet_teslim" belge tipi
    /// için tasarlanmış düzen varsa PDF döner; yoksa mevcut sabit forma (AssignmentDocument) düşer.
    /// Tasarımda master veri kaynağı: SELECT * FROM vw_AssetAssignment WHERE AssignmentId = @DocumentId
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> PrintAssignment(int id, CancellationToken ct)
    {
        try
        {
            var ctx = new DesignSelectionContext { DocType = "zimmet_teslim", UserId = CurrentUserId() };
            var pdf = await _printDispatcher.DispatchPrintAsync(ctx, id, ct);
            return File(pdf, "application/pdf", $"zimmet-teslim-{id}.pdf");
        }
        catch (Exception)
        {
            // Henüz tasarlanmış düzen yok → mevcut sabit zimmet teslim formuna düş.
            return RedirectToAction(nameof(AssignmentDocument), new { id });
        }
    }

    /// <summary>Zimmet teslim formu — yazdırılabilir (sabit) belge; tasarım yoksa fallback.</summary>
    [HttpGet]
    public async Task<IActionResult> AssignmentDocument(int id, CancellationToken ct)
    {
        var a = await _assetService.GetAssignmentByIdAsync(id, ct);
        if (a is null) return NotFound();
        var asset = await _assetService.GetAssetByIdAsync(a.AssetId, ct);
        return View("~/Views/Assets/AssignmentDocument.cshtml", new AssignmentDocumentViewModel
        {
            AssignmentId = a.Id,
            AssetCode = asset?.AssetCode,
            AssetName = asset?.AssetName,
            SerialNo = asset?.SerialNo,
            LocationName = a.LocationName,
            PersonnelName = a.PersonnelName ?? (string.IsNullOrWhiteSpace(a.DepartmentName) ? null : a.DepartmentName + " (departman)"),
            AssignDate = a.AssignDate,
            ReturnDate = a.ReturnDate,
            DocumentNo = a.DocumentNo,
            Note = a.AssignNote,
        });
    }

    // ── Görsel & Evraklar (merkezi Attachment tablosu) ────────────

    /// <summary>Varlığın evrak listesi (EntityType="Asset"). Kapak görseli (AssetImage) hariç.</summary>
    [HttpGet]
    public async Task<IActionResult> AssetAttachments(int id, CancellationToken ct)
    {
        if (id <= 0) return Json(System.Array.Empty<object>());
        var list = await _attachments.GetByEntityAsync(AttEntityDoc, id.ToString(), ct);
        return Json(list.Select(a => new
        {
            id = a.Id,
            fileName = a.FileName,
            contentType = a.ContentType,
            fileSize = a.FileSize,
            description = a.Description,
            created = a.Created,
            isImage = (a.ContentType ?? "").StartsWith("image/", StringComparison.OrdinalIgnoreCase),
        }));
    }

    [HttpPost]
    [RequestSizeLimit(25 * 1024 * 1024)] // 25 MB / dosya
    public async Task<IActionResult> UploadAssetAttachment([FromForm] int assetId, [FromForm] IFormFile? file, [FromForm] string? description, CancellationToken ct)
    {
        if (assetId <= 0) return Json(new { success = false, message = "Varlık seçimi zorunludur." });
        if (file is null || file.Length <= 0) return Json(new { success = false, message = "Dosya seçilmedi." });
        var bytes = await ReadFileAsync(file, ct);
        var newId = await _attachments.AddAsync(new Attachment
        {
            EntityType = AttEntityDoc,
            EntityId = assetId.ToString(),
            FileName = file.FileName ?? "dosya",
            ContentType = file.ContentType,
            FileSize = bytes.LongLength,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            CreatedById = CurrentUserId(),
            BinaryContent = bytes,
        }, ct);
        return Json(new { success = true, id = newId });
    }

    [HttpGet]
    public async Task<IActionResult> AssetAttachmentDownload(int id, CancellationToken ct)
    {
        var meta = await _attachments.GetByIdAsync(id, ct);
        if (meta is null || !meta.IsActive) return NotFound();
        var bytes = await _attachments.GetBinaryAsync(id, ct);
        if (bytes is null) return NotFound();
        return File(bytes, meta.ContentType ?? "application/octet-stream", meta.FileName);
    }

    [HttpPost]
    public async Task<IActionResult> DeleteAssetAttachmentJson(int id, CancellationToken ct)
    {
        if (id <= 0) return Json(new { success = false, message = "Geçersiz istek." });
        await _attachments.DeleteAsync(id, ct);
        return Json(new { success = true });
    }

    /// <summary>Kapak görseli yükler (tek). Önceki AssetImage'ları pasifler, yenisini ekler.</summary>
    [HttpPost]
    [RequestSizeLimit(25 * 1024 * 1024)]
    public async Task<IActionResult> UploadAssetImage([FromForm] int assetId, [FromForm] IFormFile? file, CancellationToken ct)
    {
        if (assetId <= 0) return Json(new { success = false, message = "Varlık seçimi zorunludur." });
        if (file is null || file.Length <= 0) return Json(new { success = false, message = "Görsel seçilmedi." });
        if (!(file.ContentType ?? "").StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return Json(new { success = false, message = "Yalnızca görsel dosyası yüklenebilir (JPG/PNG vb.)." });

        await _attachments.DeleteByEntityAsync(AttEntityImage, assetId.ToString(), ct);
        var bytes = await ReadFileAsync(file, ct);
        await _attachments.AddAsync(new Attachment
        {
            EntityType = AttEntityImage,
            EntityId = assetId.ToString(),
            FileName = file.FileName ?? "gorsel",
            ContentType = file.ContentType,
            FileSize = bytes.LongLength,
            CreatedById = CurrentUserId(),
            BinaryContent = bytes,
        }, ct);
        return Json(new { success = true });
    }

    /// <summary>Varlığın kapak görselini inline döner (kart + edit önizleme). Yoksa 404.</summary>
    [HttpGet]
    public async Task<IActionResult> AssetImage(int id, CancellationToken ct)
    {
        if (id <= 0) return NotFound();
        var img = (await _attachments.GetByEntityAsync(AttEntityImage, id.ToString(), ct)).FirstOrDefault();
        if (img is null) return NotFound();
        var bytes = await _attachments.GetBinaryAsync(img.Id, ct);
        if (bytes is null) return NotFound();
        Response.Headers.CacheControl = "no-cache";
        return File(bytes, img.ContentType ?? "image/jpeg");
    }

    [HttpPost]
    public async Task<IActionResult> DeleteAssetImageJson(int assetId, CancellationToken ct)
    {
        if (assetId <= 0) return Json(new { success = false, message = "Geçersiz istek." });
        await _attachments.DeleteByEntityAsync(AttEntityImage, assetId.ToString(), ct);
        return Json(new { success = true });
    }

    private static async Task<byte[]> ReadFileAsync(IFormFile file, CancellationToken ct)
    {
        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    // ── Edit ──────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> AssetEdit(int? id, int? machineId, CancellationToken ct)
    {
        if (id.HasValue && id.Value > 0)
        {
            var a = await _assetService.GetAssetByIdAsync(id.Value, ct);
            if (a is null) return NotFound();
            return View("~/Views/Assets/AssetEdit.cshtml", MapEdit(a));
        }
        if (machineId.HasValue && machineId.Value > 0)
        {
            // Makine kartından açıldı → lazy-materialize (yoksa oluştur, varsa getir)
            var a = await _assetService.GetOrMaterializeByMachineIdAsync(machineId.Value, CurrentUserId(), ct);
            return View("~/Views/Assets/AssetEdit.cshtml", MapEdit(a));
        }
        return View("~/Views/Assets/AssetEdit.cshtml", new AssetEditViewModel
        {
            IsActive = true,
            Status = AssetStatus.Active,
            Kind = AssetKind.Equipment,
        });
    }

    [HttpGet]
    public async Task<IActionResult> AssetLookups(CancellationToken ct)
        => Json(await _assetService.GetEditLookupsAsync(ct));

    [HttpPost]
    public async Task<IActionResult> SaveAssetJson([FromBody] AssetInput input, CancellationToken ct)
    {
        if (input is null) return Json(new { success = false, message = "Geçersiz istek." });
        if (string.IsNullOrWhiteSpace(input.AssetName))
            return Json(new { success = false, message = "Varlık adı zorunludur." });

        try
        {
            if (input.Id.HasValue && input.Id.Value > 0)
            {
                await _assetService.UpdateAssetAsync(new UpdateAssetRequest(
                    input.Id.Value, input.AssetName!, input.Description, (AssetKind)input.Kind,
                    input.LocationId, input.DepartmentId, input.AssignedPersonnelId,
                    input.SerialNo, input.AcquisitionDate, input.WarrantyExpiryDate,
                    input.IpAddress, input.Hostname, input.OperatingSystem, input.MacAddress, input.NetworkDomain, input.PlateNo,
                    input.IsMaintained, input.MaintenancePeriodDays, (AssetPeriodUnit)input.MaintenancePeriodUnit,
                    input.IsCalibrated, input.CalibrationPeriodDays, (AssetPeriodUnit)input.CalibrationPeriodUnit,
                    (AssetStatus)input.Status, input.SortOrder, input.IsActive, input.IsAssignable, CurrentUserId()), ct);
                return Json(new { success = true, id = input.Id.Value });
            }
            var newId = await _assetService.CreateAssetAsync(new CreateAssetRequest(
                input.AssetName!, input.Description, (AssetKind)input.Kind,
                input.LocationId, input.DepartmentId, input.AssignedPersonnelId, input.MachineId,
                input.SerialNo, input.AcquisitionDate, input.WarrantyExpiryDate,
                input.IpAddress, input.Hostname, input.OperatingSystem, input.MacAddress, input.NetworkDomain, input.PlateNo,
                input.IsMaintained, input.MaintenancePeriodDays, (AssetPeriodUnit)input.MaintenancePeriodUnit,
                input.IsCalibrated, input.CalibrationPeriodDays, (AssetPeriodUnit)input.CalibrationPeriodUnit,
                (AssetStatus)input.Status, input.SortOrder, input.IsActive, input.IsAssignable, CurrentUserId()), ct);
            return Json(new { success = true, id = newId });
        }
        catch (ArgumentException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteAssetJson(int id, CancellationToken ct)
    {
        try
        {
            await _assetService.DeleteAssetAsync(id, ct);
            // Bağlı evrak + kapak görselini de pasifle (merkezi Attachment tablosu)
            await _attachments.DeleteByEntityAsync(AttEntityDoc, id.ToString(), ct);
            await _attachments.DeleteByEntityAsync(AttEntityImage, id.ToString(), ct);
            return Json(new { success = true });
        }
        catch (ArgumentException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ── Geçmiş (AssetEvent) ───────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> AssetEvents(int assetId, CancellationToken ct)
        => Json(await _assetService.GetEventsAsync(assetId, ct));

    [HttpPost]
    public async Task<IActionResult> SaveAssetEventJson([FromBody] AssetEventInput input, CancellationToken ct)
    {
        if (input is null || input.AssetId <= 0)
            return Json(new { success = false, message = "Geçersiz istek." });
        try
        {
            var newId = await _assetService.AddEventAsync(new CreateAssetEventRequest(
                input.AssetId, (AssetEventType)input.EventType,
                input.EventDate ?? DateTime.UtcNow,
                input.PerformedByPersonnelId, input.PerformedByText,
                input.Cost, (AssetEventResult)input.Result, input.Notes,
                input.NextDueDate, input.DocumentUrl, CurrentUserId()), ct);
            return Json(new { success = true, id = newId });
        }
        catch (ArgumentException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteAssetEventJson(int id, CancellationToken ct)
    {
        try
        {
            await _assetService.DeleteEventAsync(id, ct);
            return Json(new { success = true });
        }
        catch (ArgumentException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ── Helpers ───────────────────────────────────────────────────

    private static AssetEditViewModel MapEdit(AssetDto a) => new()
    {
        Id = a.Id,
        AssetCode = a.AssetCode,
        AssetName = a.AssetName,
        Description = a.Description,
        Kind = a.Kind,
        LocationId = a.LocationId,
        DepartmentId = a.DepartmentId,
        AssignedPersonnelId = a.AssignedPersonnelId,
        MachineId = a.MachineId,
        MachineName = a.MachineName,
        SerialNo = a.SerialNo,
        AcquisitionDate = a.AcquisitionDate,
        WarrantyExpiryDate = a.WarrantyExpiryDate,
        IpAddress = a.IpAddress,
        Hostname = a.Hostname,
        OperatingSystem = a.OperatingSystem,
        MacAddress = a.MacAddress,
        NetworkDomain = a.NetworkDomain,
        PlateNo = a.PlateNo,
        IsMaintained = a.IsMaintained,
        MaintenancePeriodDays = a.MaintenancePeriodDays,
        MaintenancePeriodUnit = a.MaintenancePeriodUnit,
        LastMaintenanceDate = a.LastMaintenanceDate,
        NextMaintenanceDate = a.NextMaintenanceDate,
        IsCalibrated = a.IsCalibrated,
        CalibrationPeriodDays = a.CalibrationPeriodDays,
        CalibrationPeriodUnit = a.CalibrationPeriodUnit,
        LastCalibrationDate = a.LastCalibrationDate,
        NextCalibrationDate = a.NextCalibrationDate,
        Status = a.Status,
        SortOrder = a.SortOrder,
        IsActive = a.IsActive,
        IsAssignable = a.IsAssignable,
    };

    private static string StatusText(AssetStatus s) => s switch
    {
        AssetStatus.Active => "Aktif",
        AssetStatus.InMaintenance => "Bakımda",
        AssetStatus.Retired => "Hurda",
        AssetStatus.Disposed => "Elden Çıkarıldı",
        _ => s.ToString(),
    };

    private static string StatusColor(AssetStatus s) => s switch
    {
        AssetStatus.Active => "emerald",
        AssetStatus.InMaintenance => "amber",
        AssetStatus.Retired => "slate",
        AssetStatus.Disposed => "rose",
        _ => "slate",
    };

    private static string KindText(AssetKind k) => k switch
    {
        AssetKind.Equipment => "Ekipman",
        AssetKind.Machine => "Makine",
        AssetKind.Instrument => "Ölçüm Cihazı",
        AssetKind.Vehicle => "Araç",
        AssetKind.Tool => "El Aleti",
        AssetKind.Fixture => "Aparat / Kalıp",
        AssetKind.Computer => "Bilgisayar",
        AssetKind.Server => "Sunucu",
        AssetKind.MobilePhone => "Cep Telefonu",
        AssetKind.Tablet => "Tablet",
        AssetKind.NetworkDevice => "Ağ Cihazı",
        AssetKind.Printer => "Yazıcı",
        _ => "Diğer",
    };

    private static string DueColor(DateTime due)
    {
        var days = (due.Date - DateTime.Today).TotalDays;
        if (days < 0) return "rose";        // gecikmiş
        if (days <= 14) return "amber";     // yaklaşıyor
        return "emerald";
    }

    private static string? DueText(DateTime? due)
    {
        if (!due.HasValue) return null;
        var days = (due.Value.Date - DateTime.Today).TotalDays;
        if (days < 0) return "gecikmiş";
        if (days <= 14) return "yaklaşıyor";
        return null;
    }

    private static string DueBadgeText(DateTime? due)
    {
        if (!due.HasValue) return "Planlanmadı";
        var days = (due.Value.Date - DateTime.Today).TotalDays;
        if (days < 0) return "Gecikmiş";
        if (days <= 14) return "Yaklaşıyor";
        return "Planlı";
    }

    private static string DueBadgeColor(DateTime? due)
    {
        if (!due.HasValue) return "slate";
        var days = (due.Value.Date - DateTime.Today).TotalDays;
        if (days < 0) return "rose";
        if (days <= 14) return "amber";
        return "emerald";
    }

    // ── Request body modelleri ────────────────────────────────────

    public sealed class AssetInput
    {
        public int? Id { get; set; }
        public string? AssetName { get; set; }
        public string? Description { get; set; }
        public int Kind { get; set; }
        public int? LocationId { get; set; }
        public int? DepartmentId { get; set; }
        public int? AssignedPersonnelId { get; set; }
        public int? MachineId { get; set; }
        public string? SerialNo { get; set; }
        public DateTime? AcquisitionDate { get; set; }
        public DateTime? WarrantyExpiryDate { get; set; }
        public string? IpAddress { get; set; }
        public string? Hostname { get; set; }
        public string? OperatingSystem { get; set; }
        public string? MacAddress { get; set; }
        public string? NetworkDomain { get; set; }
        public string? PlateNo { get; set; }
        public bool IsMaintained { get; set; }
        public int? MaintenancePeriodDays { get; set; }
        public int MaintenancePeriodUnit { get; set; }  // 0=Days, 1=Months, 2=Years
        public bool IsCalibrated { get; set; }
        public int? CalibrationPeriodDays { get; set; }
        public int CalibrationPeriodUnit { get; set; }  // 0=Days, 1=Months, 2=Years
        public int Status { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
        public bool IsAssignable { get; set; } = true;
    }

    public sealed class AssetEventInput
    {
        public int AssetId { get; set; }
        public int EventType { get; set; }
        public DateTime? EventDate { get; set; }
        public int? PerformedByPersonnelId { get; set; }
        public string? PerformedByText { get; set; }
        public decimal? Cost { get; set; }
        public int Result { get; set; }
        public string? Notes { get; set; }
        public DateTime? NextDueDate { get; set; }
        public string? DocumentUrl { get; set; }
    }

    public sealed class AssignInput
    {
        public int AssetId { get; set; }
        public int? PersonnelId { get; set; }
        public int? DepartmentId { get; set; }
        public int? LocationId { get; set; }
        public DateTime? AssignDate { get; set; }
        public string? DocumentNo { get; set; }
        public string? Note { get; set; }
    }

    public sealed class ReturnInput
    {
        public int AssetId { get; set; }
        public DateTime? ReturnDate { get; set; }
        public string? Note { get; set; }
    }

    public sealed class BulkAssignInput
    {
        public List<int>? AssetIds { get; set; }
        public int? PersonnelId { get; set; }
        public int? DepartmentId { get; set; }
        public int? LocationId { get; set; }
        public DateTime? AssignDate { get; set; }
        public string? Note { get; set; }
    }

    public sealed class SignatureInput
    {
        public int AssignmentId { get; set; }
        public string? Kind { get; set; }     // "assign" | "return"
        public string? DataUrl { get; set; }  // data:image/png;base64,...
    }
}
