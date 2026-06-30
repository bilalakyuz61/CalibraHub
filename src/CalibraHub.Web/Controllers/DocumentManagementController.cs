using System.Security.Claims;
using System.Text.Json;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Constants;
using CalibraHub.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Merkezi Döküman Yönetimi — master DB Attachment tablosu (FormId=DocMgr ve diğer modüller).
/// SmartBoard C-Grid standardında kart listesi; extraActions ile 4 aksiyon (İndir/Düzenle/Revize/Geçmiş/Sil).
/// </summary>
[Authorize]
[CalibraHub.Web.Authorization.PermissionScope(FormCodes.DocumentManagement)]
public sealed class DocumentManagementController : Controller
{
    private readonly IAttachmentRepository _attachments;
    private const long MaxFileBytes = 50L * 1024 * 1024; // 50 MB

    private static readonly string[] AllowedCategories =
        ["Kalite", "Teknik", "İdari", "Mali", "Hukuki", "Diğer"];

    private static readonly Dictionary<int, string> FormLabels = new()
    {
        [AttachmentFormIds.DocMgr]          = "Serbest Belge",
        [AttachmentFormIds.Asset]           = "Varlık",
        [AttachmentFormIds.AssetImage]      = "Varlık Görseli",
        [AttachmentFormIds.AssetAssignment] = "Zimmet",
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public DocumentManagementController(IAttachmentRepository attachments)
    {
        _attachments = attachments;
    }

    // ── Index ────────────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var config = await BuildBoardConfigAsync(ct);
        ViewBag.BoardConfigJson = JsonSerializer.Serialize(config, JsonOpts);
        ViewBag.Categories = AllowedCategories;
        return View();
    }

    // ── BoardConfig (in-place refresh) ──────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> BoardConfig(CancellationToken ct)
    {
        var config = await BuildBoardConfigAsync(ct);
        return Json(config);
    }

    // ── Download ─────────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> Download(int id, CancellationToken ct)
    {
        var att   = await _attachments.GetByIdAsync(id, ct);
        if (att is null || !att.IsActive) return NotFound();
        var bytes = await _attachments.GetBinaryAsync(id, ct);
        if (bytes is null || bytes.Length == 0) return NotFound();
        return File(bytes, att.ContentType ?? "application/octet-stream", att.FileName);
    }

    // ── EditPartial (fetch-modal içeriği) ────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> EditPartial(int id, CancellationToken ct)
    {
        var att = await _attachments.GetByIdAsync(id, ct);
        if (att is null || !att.IsActive) return NotFound();
        ViewBag.Attachment = att;
        ViewBag.Categories = AllowedCategories;
        return PartialView("_EditPartial", att);
    }

    // ── RevisePartial (fetch-modal içeriği) ──────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> RevisePartial(int id, CancellationToken ct)
    {
        var att = await _attachments.GetByIdAsync(id, ct);
        if (att is null || !att.IsActive) return NotFound();
        return PartialView("_RevisePartial", att);
    }

    // ── VersionHistoryPartial (fetch-modal içeriği) ──────────────────────────────
    [HttpGet]
    public async Task<IActionResult> VersionHistoryPartial(int id, CancellationToken ct)
    {
        var history = await _attachments.GetVersionHistoryAsync(id, ct);
        return PartialView("_VersionHistoryPartial", history);
    }

    // ── Upload (yeni serbest belge) ──────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(50 * 1024 * 1024 + 65536)]
    public async Task<IActionResult> Upload(
        IFormFile file, string? title, string? description,
        string? category, string? tags, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return Json(new { ok = false, error = "Dosya boş olamaz." });
        if (file.Length > MaxFileBytes)
            return Json(new { ok = false, error = "Dosya boyutu 50 MB sınırını aşıyor." });

        byte[] bytes;
        await using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms, ct);
            bytes = ms.ToArray();
        }

        var att = new Attachment
        {
            FormId        = AttachmentFormIds.DocMgr,
            RefId         = 0,
            Title         = Trim(title),
            Category      = Trim(category),
            Tags          = Trim(tags),
            FileName      = Path.GetFileName(file.FileName),
            ContentType   = file.ContentType,
            FileSize      = file.Length,
            Description   = Trim(description),
            BinaryContent = bytes,
            CreatedById   = GetUserId(),
        };
        await _attachments.AddAsync(att, ct);
        return Json(new { ok = true });
    }

    // ── UpdateMeta ────────────────────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateMeta(
        int id, string? title, string? description,
        string? category, string? tags, CancellationToken ct)
    {
        var att = await _attachments.GetByIdAsync(id, ct);
        if (att is null || !att.IsActive)
            return Json(new { ok = false, error = "Dosya bulunamadı." });

        await _attachments.UpdateMetaAsync(id, Trim(title), Trim(description), Trim(category), Trim(tags), GetUserId(), ct);
        return Json(new { ok = true });
    }

    // ── Revise ────────────────────────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(50 * 1024 * 1024 + 65536)]
    public async Task<IActionResult> Revise(
        int originalId, IFormFile file, string? description, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return Json(new { ok = false, error = "Dosya boş olamaz." });
        if (file.Length > MaxFileBytes)
            return Json(new { ok = false, error = "Dosya boyutu 50 MB sınırını aşıyor." });

        var original = await _attachments.GetByIdAsync(originalId, ct);
        if (original is null || !original.IsActive)
            return Json(new { ok = false, error = "Revize edilecek belge bulunamadı." });

        var rootId = original.OriginalId ?? originalId;
        await _attachments.DeleteAsync(originalId, ct);

        byte[] bytes;
        await using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms, ct);
            bytes = ms.ToArray();
        }

        var newAtt = new Attachment
        {
            FormId         = original.FormId,
            RefId          = original.RefId,
            Title          = original.Title,
            Category       = original.Category,
            Tags           = original.Tags,
            FileName       = Path.GetFileName(file.FileName),
            ContentType    = file.ContentType,
            FileSize       = file.Length,
            Description    = Trim(description),
            RevisionNumber = (short)(original.RevisionNumber + 1),
            OriginalId     = rootId,
            BinaryContent  = bytes,
            CreatedById    = GetUserId(),
        };
        await _attachments.AddAsync(newAtt, ct);
        return Json(new { ok = true });
    }

    // ── Delete ────────────────────────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var att = await _attachments.GetByIdAsync(id, ct);
        if (att is null || !att.IsActive)
            return Json(new { ok = false, error = "Dosya bulunamadı." });
        await _attachments.DeleteAsync(id, ct);
        return Json(new { ok = true });
    }

    // ── Board Config Builder ──────────────────────────────────────────────────────
    private async Task<object> BuildBoardConfigAsync(CancellationToken ct)
    {
        var docs = await _attachments.GetAllActiveAsync(null, ct);

        var entities = docs.Select(a => new
        {
            id          = a.Id,
            title       = string.IsNullOrEmpty(a.Title) ? a.FileName : a.Title,
            subtitle    = string.IsNullOrEmpty(a.Title) ? ExtBadgeText(a.FileName) : a.FileName,
            description = a.Description,
            statusBadge = new { label = ExtBadgeText(a.FileName), color = ExtBadgeColor(a.FileName) },
            widgets     = BuildWidgets(a),
            primaryAction = (object?)null,
            secondaryAction = new
            {
                label     = "Sil",
                icon      = "Trash2",
                apiUrl    = $"/DocumentManagement/Delete?id={a.Id}",
                apiMethod = "POST",
                confirm   = $"“{(string.IsNullOrEmpty(a.Title) ? a.FileName : a.Title)}” belgesini silmek istediğinize emin misiniz?",
            },
            extraActions = new object[]
            {
                new { label = "İndir",     icon = "Download",   type = "download",    url      = $"/DocumentManagement/Download/{a.Id}",              color = "blue"   },
                new { label = "Düzenle",   icon = "Edit2",      type = "fetch-modal", fetchUrl = $"/DocumentManagement/EditPartial/{a.Id}",            modalTitle = "Belge Düzenle",       color = "amber"  },
                new { label = "Revize Et", icon = "RefreshCw",  type = "fetch-modal", fetchUrl = $"/DocumentManagement/RevisePartial/{a.Id}",          modalTitle = "Yeni Revizyon Yükle", color = "violet" },
                new { label = "Geçmiş",    icon = "History",    type = "fetch-modal", fetchUrl = $"/DocumentManagement/VersionHistoryPartial/{a.Id}",  modalTitle = "Versiyon Geçmişi",    color = "slate"  },
            },
        }).ToList();

        return new
        {
            boardKey          = "doc-management",
            title             = "Döküman Yönetimi",
            subtitle          = $"{entities.Count} belge",
            icon              = "FileStack",
            iconColor         = "indigo",
            refreshUrl        = "/DocumentManagement/BoardConfig",
            searchPlaceholder = "Dosya adı veya başlık ara…",
            emptyText         = "Henüz belge eklenmemiş. 'Belge Yükle' ile başlayın.",
            actions = new object[]
            {
                new { id = "upload", label = "Belge Yükle", icon = "Upload", variant = "primary", trigger = "dmOpenUpload" },
            },
            masterWidgets = BuildMasterWidgets(),
            entities,
        };
    }

    private static object[] BuildWidgets(Attachment a)
    {
        var list = new List<object>
        {
            new { id = "w_version", type = "data", dataType = "text",
                  label = "Revizyon", value = $"v{a.RevisionNumber}",
                  color = a.RevisionNumber > 1 ? "amber" : "slate",
                  alwaysVisible = true },
            new { id = "w_size", type = "data", dataType = "text",
                  label = "Boyut", value = FormatSize(a.FileSize), color = "slate" },
            new { id = "w_date", type = "data", dataType = "text",
                  label = "Tarih", value = a.Created.ToLocalTime().ToString("dd.MM.yyyy"), color = "slate" },
            new { id = "w_module", type = "data", dataType = "text",
                  label = "Modül", value = FormLabels.GetValueOrDefault(a.FormId, $"Form {a.FormId}"), color = "blue" },
        };

        if (!string.IsNullOrEmpty(a.Category))
            list.Insert(1, new { id = "w_category", type = "data", dataType = "text",
                label = "Kategori", value = a.Category, color = "indigo" });

        if (!string.IsNullOrEmpty(a.Tags))
        {
            var tagPreview = a.Tags.Length > 30 ? a.Tags[..30] + "…" : a.Tags;
            list.Add(new { id = "w_tags", type = "data", dataType = "text",
                label = "Etiketler", value = tagPreview, color = "violet" });
        }

        return list.ToArray();
    }

    private static object[] BuildMasterWidgets() =>
    [
        new { id = "w_version",  label = "Revizyon", dataType = "text" },
        new { id = "w_size",     label = "Boyut",    dataType = "text" },
        new { id = "w_date",     label = "Tarih",    dataType = "text" },
        new { id = "w_module",   label = "Modül",    dataType = "text" },
        new { id = "w_category", label = "Kategori", dataType = "text" },
        new { id = "w_tags",     label = "Etiketler",dataType = "text" },
    ];

    // ── Helpers ──────────────────────────────────────────────────────────────────
    private static string ExtBadgeText(string fileName)
    {
        var ext = Path.GetExtension(fileName).TrimStart('.').ToUpperInvariant();
        return string.IsNullOrEmpty(ext) ? "FILE" : ext;
    }

    private static string ExtBadgeColor(string fileName)
    {
        var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "pdf"                                          => "rose",
            "doc" or "docx"                               => "blue",
            "xls" or "xlsx"                               => "emerald",
            "ppt" or "pptx"                               => "amber",
            "jpg" or "jpeg" or "png" or "gif" or "webp"   => "violet",
            "zip" or "rar" or "7z"                         => "amber",
            _                                              => "slate",
        };
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024):F1} MB";
    }

    private static string? Trim(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private int? GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : null;
    }
}
