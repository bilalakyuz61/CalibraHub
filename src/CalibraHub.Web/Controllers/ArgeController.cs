using System.Globalization;
using System.Security.Claims;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.SmartBoard;
using CalibraHub.Domain.Enums;
using CalibraHub.Web.Authorization;
using CalibraHub.Web.Models.Arge;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// AR-GE Projeleri modulu. AR-GE projesi = 'arge_proje' tipinde Document + ArgeProject companion.
/// Liste/kart ekrani SmartBoard standardini, yetki PermissionScope desenini izler.
/// </summary>
[Authorize]
[PermissionScope("ARGE_PROJECT_EDIT")]
public sealed class ArgeController : Controller
{
    private readonly IArgeProjectService _arge;

    public ArgeController(IArgeProjectService arge) => _arge = arge;

    // GET /Arge/Projects → SmartBoard liste ekrani (view: Views/Arge/Projects.cshtml)
    [HttpGet]
    public async Task<IActionResult> Projects(CancellationToken ct)
        => View(new ArgeProjectsViewModel { BoardConfig = await BuildBoardConfigAsync(ct) });

    // GET /Arge/ProjectEdit?id= → proje yeni/duzenle formu (Views/Arge/ProjectEdit.cshtml).
    // Edit'te detay server-side yuklenir (model); yeni'de model null.
    [HttpGet]
    public async Task<IActionResult> ProjectEdit(int? id, CancellationToken ct)
    {
        ArgeProjectDetail? model = id is > 0 ? await _arge.GetAsync(id.Value, ct) : null;
        return View(model);
    }

    // GET /Arge/ProjectsBoardConfig → in-place refresh (JSON, ayni config)
    [HttpGet("/Arge/ProjectsBoardConfig")]
    public async Task<IActionResult> ProjectsBoardConfig(CancellationToken ct)
        => Json(await BuildBoardConfigAsync(ct));

    // POST /Arge/SaveProject → JSON
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveProject([FromBody] SaveArgeProjectRequest? request, CancellationToken ct)
    {
        if (request is null) return Json(new { ok = false, error = "Geçersiz istek." });
        var (ok, error, id) = await _arge.SaveAsync(request, CurrentUserId(), ct);
        return Json(new { ok, error, id });
    }

    // POST /Arge/DeleteProjectJson?id= → JSON (SmartCard secondaryAction)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteProjectJson(int id, CancellationToken ct)
    {
        try
        {
            await _arge.DeleteAsync(id, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }

    // POST /Arge/ChangeProjectStatus → JSON (yasam dongusu gecisi)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeProjectStatus([FromBody] ChangeArgeStatusRequest? body, CancellationToken ct)
    {
        if (body is null) return Json(new { ok = false, error = "Geçersiz istek." });
        var (ok, error) = await _arge.ChangeStatusAsync(body.DocumentId, body.Status, CurrentUserId(), ct);
        return Json(new { ok, error });
    }

    // GET /Arge/GetProject?id= → JSON (edit hydration)
    [HttpGet]
    public async Task<IActionResult> GetProject(int id, CancellationToken ct)
    {
        var detail = await _arge.GetAsync(id, ct);
        return detail is null ? NotFound() : Json(detail);
    }

    // GET /Arge/PersonnelLookup → aktif personel listesi (sorumlu dropdown)
    [HttpGet]
    public async Task<IActionResult> PersonnelLookup(CancellationToken ct)
        => Json(await _arge.GetPersonnelAsync(ct));

    // GET /Arge/ProjectsLookup → aktif AR-GE/ÜR-GE projeleri (is emri proje dropdown'u icin)
    [HttpGet]
    public async Task<IActionResult> ProjectsLookup(CancellationToken ct)
    {
        var projects = await _arge.ListAsync(null, null, ct);
        return Json(projects.Select(p => new
        {
            id    = p.DocumentId,
            label = ((ArgeProjectType)p.ProjectType == ArgeProjectType.UrGe ? "ÜR-GE" : "AR-GE")
                    + " · " + p.DocumentNumber + " · " + p.Name
        }));
    }

    // POST /Arge/ConvertToProduction?id= → onayli projeyi uretime aktar (seri urun karti uretir)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConvertToProduction(int id, CancellationToken ct)
    {
        var (ok, error, itemId, itemCode, note) = await _arge.ConvertToProductionAsync(id, CurrentUserId(), ct);
        return Json(new { ok, error, itemId, itemCode, note });
    }

    // ── Prototip yonetimi (ProjectEdit "Prototipler" sekmesi) ─────────────────

    // GET /Arge/Prototypes?projectId= → JSON prototip listesi
    [HttpGet]
    public async Task<IActionResult> Prototypes(int projectId, CancellationToken ct)
        => Json(await _arge.ListPrototypesAsync(projectId, ct));

    // POST /Arge/SavePrototype → JSON (ekle/guncelle)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePrototype([FromBody] SavePrototypeRequest? request, CancellationToken ct)
    {
        if (request is null) return Json(new { ok = false, error = "Geçersiz istek." });
        var (ok, error, id) = await _arge.SavePrototypeAsync(request, CurrentUserId(), ct);
        return Json(new { ok, error, id });
    }

    // POST /Arge/DeletePrototypeJson?id= → JSON (soft-delete)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePrototypeJson(int id, CancellationToken ct)
    {
        try
        {
            await _arge.DeletePrototypeAsync(id, CurrentUserId(), ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }

    // POST /Arge/EnsurePrototypeItem?id= → stok karti turet/getir (recete/rota deep-link icin)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EnsurePrototypeItem(int id, CancellationToken ct)
    {
        var (ok, error, itemId, itemCode) = await _arge.EnsurePrototypeItemAsync(id, CurrentUserId(), ct);
        return Json(new { ok, error, itemId, itemCode });
    }

    // POST /Arge/SetPrototypeApproved → JSON (klon kaynagi bayragi — proje basina tek)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetPrototypeApproved([FromBody] SetPrototypeApprovedRequest? body, CancellationToken ct)
    {
        if (body is null) return Json(new { ok = false, error = "Geçersiz istek." });
        var (ok, error) = await _arge.SetPrototypeApprovedAsync(body.PrototypeId, body.Approved, CurrentUserId(), ct);
        return Json(new { ok, error });
    }

    // GET /Arge/ProjectCost?id= → proje maliyet rollup (Faz 3: isçilik; Faz 4: + malzeme)
    [HttpGet]
    public async Task<IActionResult> ProjectCost(int id, CancellationToken ct)
    {
        var labor = await _arge.GetProjectLaborAsync(id, ct);
        return Json(new
        {
            projectId      = id,
            laborCost      = labor.LaborCost,
            laborHours     = labor.LaborHours,
            workOrderCount = labor.WorkOrderCount,
            operationCount = labor.OperationCount
        });
    }

    private int? CurrentUserId()
        => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private async Task<object> BuildBoardConfigAsync(CancellationToken ct)
    {
        var items = await _arge.ListAsync(null, null, ct);
        var tr = CultureInfo.GetCultureInfo("tr-TR");

        return SmartBoard.For(items)
            .WithBoardKey("arge-projects")
            .WithTitle("AR-GE / ÜR-GE Projeleri", subtitle: $"{items.Count} proje")
            .WithIcon("FlaskConical", "indigo")
            .WithRefreshUrl("/Arge/ProjectsBoardConfig")
            .WithSearchPlaceholder("Hızlı ara… (proje no, ad)")
            .WithEmptyText("Henüz AR-GE projesi yok")
            .AddHeaderAction("new", "Yeni Proje", "Plus", "/Arge/ProjectEdit")
            .MapEntities(p =>
            {
                var status = (ArgeProjectStatus)p.Status;
                var ptype = (ArgeProjectType)p.ProjectType;
                var eb = SmartBoardEntity.For(p.DocumentId, p.Name, subtitle: p.DocumentNumber)
                    .WithDescription(p.Description ?? string.Empty)
                    .AddTextWidget("w_type", "Tür", ptype == ArgeProjectType.UrGe ? "ÜR-GE" : "AR-GE",
                        color: ptype == ArgeProjectType.UrGe ? "teal" : "indigo")
                    .AddTextWidget("w_status", "Durum", StatusLabel(status), color: StatusColor(status))
                    .AddNumericWidget("w_progress", "İlerleme", p.ProgressPercent.ToString("0.#", tr), detail: "%",
                        color: p.ProgressPercent >= 80 ? "emerald" : p.ProgressPercent >= 40 ? "blue" : "amber");
                if (!string.IsNullOrWhiteSpace(p.OwnerName))
                    eb.AddTextWidget("w_owner", "Sorumlu", p.OwnerName, color: "slate");
                eb.AddTextWidget("w_target", "Hedef Tarih",
                    p.TargetDate?.ToString("dd.MM.yyyy", tr) ?? "-",
                    color: p.TargetDate is { } t && t.Date < DateTime.Today ? "rose" : "emerald");
                if (p.PrototypeCount > 0)
                    eb.AddNumericWidget("w_proto", "Prototip", p.PrototypeCount.ToString(), detail: "adet", color: "violet");
                return eb.WithEditAndDelete(
                    editUrl: $"/Arge/ProjectEdit?id={p.DocumentId}",
                    deleteApiUrl: $"/Arge/DeleteProjectJson?id={p.DocumentId}",
                    deleteConfirm: $"Bu AR-GE projesini silmek istediğinize emin misiniz? ({p.DocumentNumber})");
            })
            .Build();
    }

    private static string StatusLabel(ArgeProjectStatus s) => s switch
    {
        ArgeProjectStatus.Planning => "Planlama",
        ArgeProjectStatus.Development => "Geliştirme",
        ArgeProjectStatus.Prototyping => "Prototip",
        ArgeProjectStatus.Testing => "Test",
        ArgeProjectStatus.DesignReview => "Tasarım Onayında",
        ArgeProjectStatus.Approved => "Onaylandı",
        ArgeProjectStatus.TransferredToProduction => "Üretime Aktarıldı",
        ArgeProjectStatus.Rejected => "Reddedildi",
        ArgeProjectStatus.Cancelled => "İptal",
        _ => s.ToString(),
    };

    private static string StatusColor(ArgeProjectStatus s) => s switch
    {
        ArgeProjectStatus.Planning => "slate",
        ArgeProjectStatus.Development => "blue",
        ArgeProjectStatus.Prototyping => "indigo",
        ArgeProjectStatus.Testing => "amber",
        ArgeProjectStatus.DesignReview => "violet",
        ArgeProjectStatus.Approved => "emerald",
        ArgeProjectStatus.TransferredToProduction => "emerald",
        ArgeProjectStatus.Rejected => "rose",
        ArgeProjectStatus.Cancelled => "rose",
        _ => "slate",
    };
}

/// <summary>Statu degistirme istek govdesi (POST /Arge/ChangeProjectStatus).</summary>
public sealed record ChangeArgeStatusRequest(int DocumentId, byte Status);

/// <summary>Prototip onay (klon kaynagi) bayragi istegi (POST /Arge/SetPrototypeApproved).</summary>
public sealed record SetPrototypeApprovedRequest(int PrototypeId, bool Approved);
