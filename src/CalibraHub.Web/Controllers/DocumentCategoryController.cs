using System.Security.Claims;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.SmartBoard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Doküman kategori/tip tanımlama ekranı (PageComment Seq 47) — iki seviyeli ağaç
/// (Kategori L1 → Tip L2). SmartBoard C-Grid standardında düz liste (Kategori + Tip birlikte,
/// statusBadge ile seviye ayrımı) + cascading dropdown için ayrı Categories/Types uçları.
/// DocumentManagementController bu servisin Categories/Types uçlarını kategori/tip
/// dropdown'ları için tüketir; Attachment.DocumentCategoryId (en spesifik düğüm) ile bağlanır.
/// </summary>
[Authorize]
[CalibraHub.Web.Authorization.PermissionScope(FormCodes.DocumentCategory)]
public sealed class DocumentCategoryController : Controller
{
    private readonly IDocumentCategoryService _service;

    public DocumentCategoryController(IDocumentCategoryService service)
    {
        _service = service;
    }

    // GET /DocumentCategory — yönetim ekranı.
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewData["Title"] = "Doküman Kategorileri";
        ViewData["FormCode"] = FormCodes.DocumentCategory;
        return View();
    }

    // GET /DocumentCategory/BoardConfig — SmartBoard C-Grid (Kategori + Tip birlikte, düz liste;
    // statusBadge ile L1/L2 ayrımı). Edit modal prefill için ayrıca GET /DocumentCategory/Get?id= kullanılır.
    [HttpGet]
    public async Task<IActionResult> BoardConfig(CancellationToken ct)
    {
        var config = await BuildBoardConfigAsync(ct);
        return Json(config);
    }

    // GET /DocumentCategory/Categories — L1 (Kategori) listesi, üst dropdown.
    [HttpGet]
    public async Task<IActionResult> Categories(CancellationToken ct)
    {
        var list = await _service.GetLevel1Async(ct);
        return Json(list.Select(x => new { id = x.Id, name = x.Name }).ToArray());
    }

    // GET /DocumentCategory/Types?parentId=123 — seçilen Kategori'nin Tip (L2) listesi, alt dropdown.
    [HttpGet]
    public async Task<IActionResult> Types(int parentId, CancellationToken ct)
    {
        if (parentId <= 0) return Json(Array.Empty<object>());
        var list = await _service.GetByParentAsync(parentId, ct);
        return Json(list.Select(x => new { id = x.Id, name = x.Name }).ToArray());
    }

    // GET /DocumentCategory/Get?id= — tekil kayıt (edit modal prefill).
    [HttpGet]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var item = await _service.GetByIdAsync(id, ct);
        return item is null ? NotFound() : Json(item);
    }

    // POST /DocumentCategory/Save — insert (id=0/null) veya update.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromBody] DocumentCategorySaveInput input, CancellationToken ct)
    {
        if (input is null)
            return Json(new { success = false, message = "Geçersiz istek." });

        var actorId = GetUserId();

        if (input.Id.HasValue && input.Id.Value > 0)
        {
            var (ok, err) = await _service.UpdateAsync(
                new UpdateDocumentCategoryRequest(input.Id.Value, input.ParentId, input.Name ?? "", input.SortOrder, input.IsActive),
                actorId, ct);
            return Json(new { success = ok, message = err });
        }
        else
        {
            var (ok, err, newId) = await _service.CreateAsync(
                new CreateDocumentCategoryRequest(input.ParentId, input.Name ?? "", input.SortOrder, input.IsActive),
                actorId, ct);
            return Json(new { success = ok, message = err, id = newId });
        }
    }

    // POST /DocumentCategory/Delete — soft delete (onay frontend'de custom modal ile alınır).
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var (ok, err) = await _service.DeleteAsync(id, GetUserId(), ct);
        return Json(new { success = ok, message = err });
    }

    // ── Board config builder ────────────────────────────────────────────────
    private async Task<object> BuildBoardConfigAsync(CancellationToken ct)
    {
        var all = await _service.GetAllAsync(ct);
        var byId = all.ToDictionary(x => x.Id);
        var ordered = all
            .OrderBy(x => x.ParentId.HasValue ? (byId.TryGetValue(x.ParentId.Value, out var p) ? p.SortOrder : 0) : x.SortOrder)
            .ThenBy(x => x.ParentId ?? x.Id)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToArray();

        return SmartBoard.For(ordered)
            .WithBoardKey("document-category")
            .WithTitle("Doküman Kategorileri", subtitle: $"{ordered.Length} kayıt")
            .WithIcon("FolderTree", "indigo")
            .WithRefreshUrl("/DocumentCategory/BoardConfig")
            .WithSearchPlaceholder("Kategori veya tip ara…")
            .WithEmptyText("Henüz kategori tanımlanmamış.")
            .AddHeaderAction("new", "Yeni Kategori", "Plus", "#")
            .MapEntities(x =>
            {
                var isL1 = !x.ParentId.HasValue;
                var parentName = x.ParentId.HasValue && byId.TryGetValue(x.ParentId.Value, out var parent) ? parent.Name : null;
                return SmartBoardEntity.For(x.Id, x.Name, isL1 ? null : parentName)
                    .WithStatusBadge(isL1 ? "Kategori" : "Tip", isL1 ? "indigo" : "violet")
                    .AddNumericWidget("w_sort", "Sıra", x.SortOrder.ToString())
                    .AddStatusWidget("w_active", "Durum", x.IsActive)
                    .WithSecondaryAction("Sil", "Trash2",
                        apiUrl: $"/DocumentCategory/Delete?id={x.Id}",
                        confirm: $"\"{x.Name}\" kaydını silmek istediğinize emin misiniz?" +
                                 (isL1 ? " Altındaki tüm tipler de pasifleşecek." : ""));
            })
            .Build();
    }

    private int? GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : null;
    }
}

/// <summary>Kategori/Tip kaydetme girdisi — Id null/0 ise insert.</summary>
public sealed record DocumentCategorySaveInput(int? Id, int? ParentId, string? Name, int SortOrder, bool IsActive);
