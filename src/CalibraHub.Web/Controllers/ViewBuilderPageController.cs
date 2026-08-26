using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.SmartBoard;
using CalibraHub.Web.Authorization;
using CalibraHub.Web.Helpers;
using CalibraHub.Web.Models.ViewBuilder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// SQL View Yönetimi — Faz 2 React ekranının host controller'ı.
///
/// API zaten <see cref="ViewBuilderController"/>'da (api/view-builder/*, Faz 1); bu controller
/// yalnızca React mount noktasını barındıran sayfaları döndürür. Aynı sınıfta iki controller
/// olamayacağı için (ViewBuilderController zaten [ApiController] + api/view-builder route'unu
/// kullanıyor) ayrı bir dosya/sınıf olarak eklendi — CLAUDE.md "SQL View Yönetimi — Kontrollü
/// İstisna" (2026-07-17): SystemAdmin-only, DatabaseMetadataController/LegacyMigrationController
/// ile BİREBİR aynı erişim deseni ([Authorize] + [PermissionScope(FormCodes.SetupDefinitions)]).
///
/// PageComment Seq 1121 (2026-08-26) retrofit: liste ekranı artık standart C-Grid/SmartBoard
/// (bkz. CLAUDE.md "CalibraSmartBoard (C-Grid) kuralları"). Kurucu (görsel/gelişmiş SQL) React
/// bileşeni artık ayrı bir sayfada (/ViewBuilder/Edit) — sidebar kaldırıldı, seçim navigasyon
/// (query string) ile yapılır. Route sözleşmesi (frontend ajanı ViewBuilder.jsx'te tüketir):
///   GET /ViewBuilder/Edit?viewName={bareViewName}  → mevcut view'ı düzenle (viewName = api/view-builder
///                                                     uçlarının beklediği ÇIPLAK isim, şema öneki YOK —
///                                                     zaten tek şema (per-company DB) kullanılıyor).
///   GET /ViewBuilder/Edit?new=1                     → yeni Özel View kurucusu.
/// Mount config'ine <c>initialViewName</c> / <c>initialNew</c> alanları eklenir; ViewBuilder.jsx
/// bunları okuyup ilk state'i buna göre kurar (bu değişiklik frontend ajanının sahası).
/// </summary>
[Authorize]
[PermissionScope(FormCodes.SetupDefinitions)]
public sealed class ViewBuilderPageController : Controller
{
    private const string BoardKey = "admin-sql-views";

    private readonly IViewBuilderService _service;
    private readonly ILogger<ViewBuilderPageController> _logger;

    public ViewBuilderPageController(IViewBuilderService service, ILogger<ViewBuilderPageController> logger)
    {
        _service = service;
        _logger = logger;
    }

    // GET /ViewBuilder — C-Grid liste ekranı.
    [HttpGet("/ViewBuilder")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewData["Title"] = "SQL View Yönetimi";
        object? boardConfig;
        try
        {
            boardConfig = await BuildBoardConfigAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SQL View listesi board config oluşturulurken hata");
            boardConfig = null;
        }
        return View("~/Views/ViewBuilder/Index.cshtml", new ViewBuilderBoardViewModel { BoardConfig = boardConfig });
    }

    // GET /ViewBuilder/BoardConfig — in-place refresh ucu (GET, tüm board config JSON'ını döner).
    [HttpGet("/ViewBuilder/BoardConfig")]
    public async Task<IActionResult> BoardConfig(CancellationToken ct)
    {
        try
        {
            var config = await BuildBoardConfigAsync(ct);
            return Json(config);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SQL View listesi board config (refresh) oluşturulurken hata");
            return StatusCode(500, new { success = false, message = "View listesi alınamadı." });
        }
    }

    // GET /ViewBuilder/Edit?viewName=&new=1 — kurucu ekranı (tek view detay modu, sidebar yok).
    [HttpGet("/ViewBuilder/Edit")]
    public IActionResult Edit(string? viewName, int? @new)
    {
        var isNew = @new == 1;
        ViewData["Title"] = isNew
            ? "SQL View Yönetimi — Yeni Özel View"
            : (string.IsNullOrWhiteSpace(viewName) ? "SQL View Yönetimi" : $"SQL View Yönetimi — {viewName}");
        return View("~/Views/ViewBuilder/Edit.cshtml", new ViewBuilderEditPageModel
        {
            InitialViewName = string.IsNullOrWhiteSpace(viewName) ? null : viewName.Trim(),
            InitialNew = isNew,
        });
    }

    // ── Board config ────────────────────────────────────────────────────

    private async Task<object> BuildBoardConfigAsync(CancellationToken ct)
    {
        var views = await _service.ListViewsAsync(ct);
        var overrideCount = views.Count(v => v.HasOverride);

        return CalibraHub.Application.SmartBoard.SmartBoard.For(views)
            .WithBoardKey(BoardKey)
            .WithTitle("SQL View Yönetimi", subtitle: $"{views.Count} view ({overrideCount} özelleştirilmiş)")
            .WithIcon("Database", "indigo")
            .WithRefreshUrl("/ViewBuilder/BoardConfig")
            .WithSearchPlaceholder("View adı ara…")
            .WithEmptyText("Henüz view bulunamadı")
            .AddHeaderAction("new-custom", "Yeni Özel View", "Plus", "/ViewBuilder/Edit?new=1")
            .WithMasterWidgets(new List<object>
            {
                SmartBoardFilterHelpers.MakeStdWidget("w_durum", "Durum", "text"),
                SmartBoardFilterHelpers.MakeStdWidget("w_kolon", "Kolon Sayısı", "numeric"),
                SmartBoardFilterHelpers.MakeStdWidget("w_revizyon", "Revizyon Sayısı", "numeric"),
                SmartBoardFilterHelpers.MakeStdWidget("w_guncelleme", "Son Güncelleme", "text"),
            })
            .MapEntities(BuildEntity)
            .Build();
    }

    private static SmartBoardEntityBuilder BuildEntity(ViewListItemDto v)
    {
        var title = $"{v.Schema}.{v.Name}";
        var (statusLabel, statusColor, subtitle) = DescribeStatus(v);

        var b = SmartBoardEntity.For(v.Name, title, subtitle)
            .WithStatusBadge(statusLabel, statusColor)
            .AddTextWidget("w_durum", "Durum", statusLabel, color: statusColor);

        if (v.ColumnCount is int cc)
            b.AddNumericWidget("w_kolon", "Kolon Sayısı", cc.ToString(), color: "indigo");

        if (v.HasOverride)
        {
            if (v.RevisionCount is int rc)
                b.AddNumericWidget("w_revizyon", "Revizyon Sayısı", rc.ToString(), color: "amber");
            if (v.OverrideUpdated is DateTime upd)
                b.AddTextWidget("w_guncelleme", "Son Güncelleme", upd.ToLocalTime().ToString("dd.MM.yyyy HH:mm"), color: "slate");
            if (!string.IsNullOrWhiteSpace(v.OverrideUpdatedBy))
                b.AddTextWidget("w_guncelleyen", "Güncelleyen", v.OverrideUpdatedBy, color: "slate");
        }

        return b.WithPrimaryAction("Düzenle", "Edit", $"/ViewBuilder/Edit?viewName={Uri.EscapeDataString(v.Name)}", color: "amber", hideButton: true);
    }

    private static (string Label, string Color, string? Subtitle) DescribeStatus(ViewListItemDto v)
    {
        if (!v.HasOverride)
            return ("Sistem", "slate", "Sistem view'ı — hiç özelleştirilmemiş");

        if (string.Equals(v.OverrideKind, "SystemExtend", StringComparison.OrdinalIgnoreCase))
            return ("Genişletildi", "amber", "Sistem view'ı — join/hesaplanan alan ile genişletildi");

        return ("Özel", "indigo", "Sıfırdan tanımlanmış özel view");
    }
}

/// <summary>Kurucu (Edit) sayfası için başlangıç state — React mount config'ine gömülür.</summary>
public sealed class ViewBuilderEditPageModel
{
    public string? InitialViewName { get; init; }
    public bool InitialNew { get; init; }
}
