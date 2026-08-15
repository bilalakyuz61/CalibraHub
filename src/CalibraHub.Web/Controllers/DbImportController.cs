using System.Security.Claims;
using System.Text.Json;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// 2026-08-15 — Veritabanı üzerinden içe aktarım (harici SQL kaynağı).
/// Excel içe aktarımdan (<c>ImportController</c>) AYRI yetkiyle korunur
/// (<see cref="FormCodes.DbDataImport"/>): harici DB kimlik bilgisi saklar ve
/// kaynak DB'de prosedür çalıştırabilir.
///
/// Razor:
///   GET  /DbImport              → aktarım işleri (SmartBoard)
///   GET  /DbImport/Connections  → kaynak bağlantıları (SmartBoard)
///   GET  /DbImport/Wizard       → iş sihirbazı (query: id, mode)
///
/// JSON API (React tüketir): /DbImport/api/...
/// </summary>
[Authorize]
[CalibraHub.Web.Authorization.PermissionScope(FormCodes.DbDataImport)]
public sealed class DbImportController : Controller
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string GenericError = "İşlem sırasında bir hata oluştu.";

    private readonly IDataImportService _service;
    private readonly ILogger<DbImportController> _logger;

    public DbImportController(IDataImportService service, ILogger<DbImportController> logger)
    {
        _service = service;
        _logger = logger;
    }

    // ── Razor ──────────────────────────────────────────────────────────

    [HttpGet("/DbImport")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewData["BoardConfigJson"] = JsonSerializer.Serialize(await BuildJobsBoardAsync(ct), Json);
        return View();
    }

    [HttpGet("/DbImport/BoardConfig")]
    public async Task<IActionResult> BoardConfig(CancellationToken ct)
        => Json(await BuildJobsBoardAsync(ct));

    /// <summary>Kaynak bağlantıları — özel C-Grid ekranı (liste + düzenleme modalı, React).</summary>
    [HttpGet("/DbImport/Connections")]
    public IActionResult Connections() => View();

    [HttpGet("/DbImport/Wizard")]
    public IActionResult Wizard() => View();

    // ── Board config ───────────────────────────────────────────────────

    private async Task<object> BuildJobsBoardAsync(CancellationToken ct)
    {
        var jobs = await _service.ListJobsAsync(true, ct);
        var labels = (await _service.ListTargetEntitiesAsync(ct))
            .ToDictionary(e => e.Entity, e => e.Label, StringComparer.OrdinalIgnoreCase);
        string Label(string? e) => labels.TryGetValue(e ?? "", out var l) ? l : (e ?? "");

        var entities = jobs.Select(j => new
        {
            id = j.Id,
            title = j.Name,
            subtitle = $"{j.ConnectionName ?? "?"} · {j.SourceSchema}.{j.SourceObject}",
            statusBadge = new { label = j.IsActive ? "Aktif" : "Pasif", color = j.IsActive ? "emerald" : "slate" },
            widgets = new object[]
            {
                new { id = "w_entity", type = "data", dataType = "text",    label = "Hedef",   value = (object)Label(j.TargetEntity), color = "indigo", alwaysVisible = true },
                new { id = "w_cols",   type = "data", dataType = "numeric", label = "Alan",    value = (object)j.Columns.Count,       color = "slate",  alwaysVisible = true },
                new { id = "w_match",  type = "data", dataType = "text",    label = "Anahtar", value = (object)j.MatchKeyField,       color = "violet", alwaysVisible = true },
                new { id = "w_proc",   type = "data", dataType = "text",    label = "Prosedür",
                      value = (object)ProcedureSummary(j), color = "amber" },
            },
            primaryAction = new { label = "Aktar", icon = "Play", url = $"/DbImport/Wizard?id={j.Id}&mode=run" },
            secondaryAction = new
            {
                label = "Sil",
                icon = "Trash2",
                apiUrl = $"/DbImport/api/jobs/delete/{j.Id}",
                apiMethod = "POST",
                confirm = $"'{j.Name}' aktarım işi silinsin mi? Bu işlem geri alınamaz.",
            },
            extraActions = new object[]
            {
                new { id = "edit",   label = "Düzenle", icon = "Pencil", url = $"/DbImport/Wizard?id={j.Id}&mode=edit", color = "blue" },
                new { id = "toggle", label = j.IsActive ? "Pasifleştir" : "Aktifleştir", icon = "Power",
                      type = "api-post", url = $"/DbImport/api/jobs/toggle/{j.Id}", color = "amber" },
            },
        }).ToList();

        return new
        {
            boardKey = "db-import-jobs",
            title = "Veritabanı Aktarımı",
            subtitle = $"{jobs.Count} aktarım işi",
            icon = "Database",
            iconColor = "indigo",
            refreshUrl = "/DbImport/BoardConfig",
            searchPlaceholder = "İş ara…",
            emptyText = "Henüz aktarım işi yok — önce bir kaynak bağlantısı tanımlayın, sonra 'Yeni İş' ile oluşturun.",
            actions = new object[]
            {
                new { id = "connections", label = "Kaynak Bağlantıları", icon = "Server",  variant = "secondary", url = "/DbImport/Connections" },
                new { id = "new",         label = "Yeni İş",             icon = "Plus",    variant = "primary",   url = "/DbImport/Wizard?mode=new" },
            },
            masterWidgets = new object[]
            {
                new { id = "w_entity", label = "Hedef",    dataType = "text" },
                new { id = "w_cols",   label = "Alan",     dataType = "numeric" },
                new { id = "w_match",  label = "Anahtar",  dataType = "text" },
                new { id = "w_proc",   label = "Prosedür", dataType = "text" },
            },
            entities,
        };
    }

    private static string ProcedureSummary(DataImportJobDto j)
    {
        var parts = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(j.PreProcedureName)) parts.Add("Ön");
        if (!string.IsNullOrWhiteSpace(j.PostProcedureName)) parts.Add("Son");
        return parts.Count == 0 ? "Yok" : string.Join(" + ", parts);
    }

    // ── Kaynak bağlantıları API ────────────────────────────────────────

    [HttpGet("/DbImport/api/connections")]
    public async Task<IActionResult> ListConnections([FromQuery] bool includeInactive = true, CancellationToken ct = default)
        => await SafeAsync(async () => Json(new { success = true, items = await _service.ListConnectionsAsync(includeInactive, ct) }),
                           "Kaynak bağlantıları listelenemedi.");

    [HttpPost("/DbImport/api/connections/save")]
    public async Task<IActionResult> SaveConnection([FromBody] SaveExternalDbConnectionRequest? request, CancellationToken ct)
    {
        if (request is null) return Json(new { success = false, error = "İstek boş." });
        return await SafeAsync(async () =>
        {
            var (ok, error, id) = await _service.SaveConnectionAsync(request, CurrentUserId(), ct);
            return ok ? Json(new { success = true, id }) : Json(new { success = false, error });
        }, "Kaynak bağlantısı kaydedilemedi.");
    }

    [HttpPost("/DbImport/api/connections/delete/{id:int}")]
    public async Task<IActionResult> DeleteConnection(int id, CancellationToken ct)
        => await SafeAsync(async () => { await _service.DeleteConnectionAsync(id, ct); return Json(new { success = true }); },
                           $"Kaynak bağlantısı silinemedi (Id={id}).");

    [HttpPost("/DbImport/api/connections/toggle/{id:int}")]
    public async Task<IActionResult> ToggleConnection(int id, CancellationToken ct)
        => await SafeAsync(async () => Json(new { success = true, isActive = await _service.ToggleConnectionActiveAsync(id, ct) }),
                           $"Kaynak bağlantısı durumu değiştirilemedi (Id={id}).");

    [HttpPost("/DbImport/api/connections/test/{id:int}")]
    public async Task<IActionResult> TestConnection(int id, CancellationToken ct)
        => await SafeAsync(async () =>
        {
            var result = await _service.TestConnectionAsync(id, ct);
            return Json(new { success = result.Ok, error = result.Error, serverVersion = result.ServerVersion, elapsedMs = result.ElapsedMs });
        }, $"Bağlantı testi yapılamadı (Id={id}).");

    [HttpPost("/DbImport/api/connections/test-draft")]
    public async Task<IActionResult> TestConnectionDraft([FromBody] SaveExternalDbConnectionRequest? request, CancellationToken ct)
    {
        if (request is null) return Json(new { success = false, error = "İstek boş." });
        return await SafeAsync(async () =>
        {
            var result = await _service.TestConnectionDraftAsync(request, ct);
            return Json(new { success = result.Ok, error = result.Error, serverVersion = result.ServerVersion, elapsedMs = result.ElapsedMs });
        }, "Bağlantı testi yapılamadı (taslak).");
    }

    // ── Şema keşfi API ─────────────────────────────────────────────────

    [HttpGet("/DbImport/api/source-objects")]
    public async Task<IActionResult> SourceObjects([FromQuery] int connectionId, CancellationToken ct)
        => await SafeAsync(async () => Json(new { success = true, items = await _service.ListSourceObjectsAsync(connectionId, ct) }),
                           $"Kaynak nesneleri okunamadı (ConnectionId={connectionId}).");

    [HttpGet("/DbImport/api/source-columns")]
    public async Task<IActionResult> SourceColumns(
        [FromQuery] int connectionId,
        [FromQuery] string schema = "dbo",
        [FromQuery] string obj = "",
        CancellationToken ct = default)
        => await SafeAsync(async () => Json(new { success = true, items = await _service.ListSourceColumnsAsync(connectionId, schema, obj, ct) }),
                           $"Kaynak kolonları okunamadı ({schema}.{obj}).");

    // ── Hedef katalog API ──────────────────────────────────────────────

    [HttpGet("/DbImport/api/entities")]
    public async Task<IActionResult> Entities(CancellationToken ct)
        => await SafeAsync(async () => Json(new { success = true, entities = await _service.ListTargetEntitiesAsync(ct) }),
                           "Hedef entity listesi okunamadı.");

    [HttpGet("/DbImport/api/target-fields")]
    public async Task<IActionResult> TargetFields([FromQuery] string entity = "", CancellationToken ct = default)
        => await SafeAsync(async () => Json(new { success = true, fields = await _service.ListTargetFieldsAsync(entity, ct) }),
                           $"Hedef alan kataloğu okunamadı (entity={entity}).");

    // ── İş tanımı API ──────────────────────────────────────────────────

    [HttpGet("/DbImport/api/jobs")]
    public async Task<IActionResult> ListJobs([FromQuery] bool includeInactive = true, CancellationToken ct = default)
        => await SafeAsync(async () => Json(new { success = true, items = await _service.ListJobsAsync(includeInactive, ct) }),
                           "Aktarım işleri listelenemedi.");

    [HttpGet("/DbImport/api/jobs/{id:int}")]
    public async Task<IActionResult> GetJob(int id, CancellationToken ct)
        => await SafeAsync(async () =>
        {
            var dto = await _service.GetJobAsync(id, ct);
            return dto is null
                ? Json(new { success = false, error = "Aktarım işi bulunamadı." })
                : Json(new { success = true, job = dto });
        }, $"Aktarım işi okunamadı (Id={id}).");

    [HttpPost("/DbImport/api/jobs/save")]
    public async Task<IActionResult> SaveJob([FromBody] SaveDataImportJobRequest? request, CancellationToken ct)
    {
        if (request is null) return Json(new { success = false, error = "İstek boş." });
        return await SafeAsync(async () =>
        {
            var (ok, error, id) = await _service.SaveJobAsync(request, CurrentUserId(), ct);
            return ok ? Json(new { success = true, id }) : Json(new { success = false, error });
        }, "Aktarım işi kaydedilemedi.");
    }

    [HttpPost("/DbImport/api/jobs/delete/{id:int}")]
    public async Task<IActionResult> DeleteJob(int id, CancellationToken ct)
        => await SafeAsync(async () => { await _service.DeleteJobAsync(id, ct); return Json(new { success = true }); },
                           $"Aktarım işi silinemedi (Id={id}).");

    [HttpPost("/DbImport/api/jobs/toggle/{id:int}")]
    public async Task<IActionResult> ToggleJob(int id, CancellationToken ct)
        => await SafeAsync(async () => Json(new { success = true, isActive = await _service.ToggleJobActiveAsync(id, ct) }),
                           $"Aktarım işi durumu değiştirilemedi (Id={id}).");

    // ── Önizleme / çalıştırma API ──────────────────────────────────────

    [HttpPost("/DbImport/api/jobs/{id:int}/preview")]
    public async Task<IActionResult> Preview(int id, [FromQuery] int sampleRows = 50, CancellationToken ct = default)
        => await SafeAsync(async () =>
        {
            var result = await _service.PreviewAsync(id, sampleRows, ct);
            return Json(new { success = result.Ok, error = result.Error, rowsRead = result.RowsRead, preview = result.Preview });
        }, $"Önizleme yapılamadı (JobId={id}).");

    [HttpPost("/DbImport/api/jobs/{id:int}/run")]
    public async Task<IActionResult> Run(int id, CancellationToken ct)
        => await SafeAsync(async () =>
        {
            var result = await _service.RunAsync(
                id, DataImportTriggerType.Manual, User.Identity?.Name, CurrentUserId(), ct);
            return Json(new { success = result.Ok, error = result.Error, run = result.Run, commit = result.Commit });
        }, $"Aktarım çalıştırılamadı (JobId={id}).");

    [HttpGet("/DbImport/api/runs")]
    public async Task<IActionResult> Runs([FromQuery] int? jobId = null, [FromQuery] int take = 100, CancellationToken ct = default)
        => await SafeAsync(async () => Json(new { success = true, items = await _service.ListRunsAsync(jobId, take, ct) }),
                           "Çalıştırma logu okunamadı.");

    // ── Yardımcılar ────────────────────────────────────────────────────

    /// <summary>
    /// Hatayı SUNUCUYA loglar, istemciye jenerik mesaj döner (CLAUDE.md "sessiz catch" kuralı:
    /// exception yutulmaz, iç detay da sızdırılmaz).
    /// </summary>
    private async Task<IActionResult> SafeAsync(Func<Task<IActionResult>> action, string logContext)
    {
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DbImport: {Context}", logContext);
            return Json(new { success = false, error = GenericError });
        }
    }

    private int? CurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : null;
    }
}
