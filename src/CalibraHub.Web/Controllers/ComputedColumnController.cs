using System.Security.Claims;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Contracts;
using CalibraHub.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Hesaplanan Kolon yönetimi — liste ekranlarına SQL VIEW'dan beslenen salt-okunur kolon.
///
/// SystemAdmin-only (SetupDefinitions): tanım, veritabanı şemasına doğrudan bakan bir
/// araçtır ve /ViewBuilder ile birlikte kullanılır — aynı kapı, aynı bağlam.
///
/// Uçlar SQL KABUL ETMEZ. Gelen her şey tanımlayıcıdır ve depo katmanında sys kataloğuna
/// karşı doğrulanır; doğrulanmayan ad sorguya hiç girmez.
/// </summary>
[Authorize]
[PermissionScope(FormCodes.SetupDefinitions)]
public sealed class ComputedColumnController : Controller
{
    private readonly IComputedColumnRepository _repo;
    private readonly ILogger<ComputedColumnController> _logger;

    public ComputedColumnController(IComputedColumnRepository repo, ILogger<ComputedColumnController> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    [HttpGet("/ComputedColumn")]
    public async Task<IActionResult> Index(CancellationToken ct)
        => View("~/Views/ComputedColumn/Index.cshtml", await BuildBoardConfigAsync(ct));

    /// <summary>
    /// In-place refresh ucu — SmartBoard karttaki silme/değişiklik sonrası tüm sayfayı
    /// yeniden yüklemeden listeyi tazeler (CLAUDE.md C-Grid kuralı).
    /// </summary>
    [HttpGet("/ComputedColumn/BoardConfig")]
    public async Task<IActionResult> BoardConfig(CancellationToken ct)
        => Json(await BuildBoardConfigAsync(ct));

    [HttpGet("/ComputedColumn/Edit")]
    public async Task<IActionResult> Edit(int? id, CancellationToken ct)
    {
        ComputedColumnDto? item = null;
        if (id is > 0)
        {
            var all = await _repo.GetAllAsync(ct);
            item = all.FirstOrDefault(x => x.Id == id.Value);
            if (item is null) return NotFound();
        }
        return View("~/Views/ComputedColumn/Edit.cshtml", item);
    }

    private async Task<object> BuildBoardConfigAsync(CancellationToken ct)
    {
        var items = await _repo.GetAllAsync(ct);

        var entities = items.Select(i => new
        {
            id = i.Id,
            title = i.Label,
            // Kaynağın tamamı alt başlıkta: hangi view'ın hangi kolonu olduğu, kartı
            // açmadan görülebilmeli — tanımların çoğu ancak bununla ayırt edilir.
            subtitle = $"{i.ViewName}.{i.ValueColumn}",
            description = (string?)null,
            imageUrl = (string?)null,
            statusBadge = i.IsActive
                ? (object)new { label = "Aktif", color = "emerald" }
                : new { label = "Kapalı", color = "slate" },
            widgets = new object[]
            {
                new { id = "w_entity", type = "data", dataType = "text",
                      label = "Varlık", value = EntityLabel(i.EntityKind), detail = (string?)null, color = "indigo" },
                new { id = "w_type", type = "data", dataType = "text",
                      label = "Veri Tipi", value = TypeLabel(i.DataType), detail = (string?)null, color = "blue" },
                new { id = "w_key", type = "data", dataType = "text",
                      label = "Anahtar", value = i.KeyColumn, detail = (string?)null, color = "slate" },
                new { id = "w_scope", type = "data", dataType = "text",
                      label = "Kapsam", value = string.IsNullOrWhiteSpace(i.BoardKeys) ? "Tüm listeler" : i.BoardKeys,
                      detail = (string?)null, color = "violet" },
            },
            primaryAction = new
            {
                label = "Düzenle",
                icon = "Edit",
                color = "amber",
                url = $"/ComputedColumn/Edit?id={i.Id}",
                hideButton = true,
            },
            secondaryAction = new
            {
                label = "Sil",
                icon = "Trash2",
                apiUrl = $"/ComputedColumn/api/delete?id={i.Id}",
                apiMethod = "POST",
                confirm = $"Bu tanım silinsin mi? ({i.Label})",
            },
        }).ToList();

        return new
        {
            boardKey = "settings-computed-columns",
            title = "Hesaplanan Kolonlar",
            subtitle = $"{entities.Count} tanım",
            itemLabel = "tanım",
            icon = "Calculator",
            iconColor = "indigo",
            refreshUrl = "/ComputedColumn/BoardConfig",
            searchPlaceholder = "Başlık, view ara…",
            emptyText = "Henüz hesaplanan kolon tanımlanmamış",
            actions = new object[]
            {
                new { id = "new", label = "Yeni Tanım", icon = "Plus", variant = "primary", url = "/ComputedColumn/Edit" },
            },
            masterWidgets = new object[]
            {
                new { id = "w_entity", label = "Varlık",    dataType = "text" },
                new { id = "w_type",   label = "Veri Tipi", dataType = "text" },
                new { id = "w_key",    label = "Anahtar",   dataType = "text" },
                new { id = "w_scope",  label = "Kapsam",    dataType = "text" },
            },
            entities,
        };
    }

    private static string EntityLabel(string? kind) => (kind ?? "").ToLowerInvariant() switch
    {
        "contact"  => "Cari",
        "document" => "Belge",
        _          => "Malzeme",
    };

    private static string TypeLabel(string? type) => (type ?? "").ToLowerInvariant() switch
    {
        "decimal"  => "Ondalık",
        "money"    => "Para",
        "date"     => "Tarih",
        "duration" => "Süre",
        "text"     => "Metin",
        "bool"     => "Evet / Hayır",
        _          => "Sayı",
    };

    [HttpGet("/ComputedColumn/api/list")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        try
        {
            return Json(new { ok = true, items = await _repo.GetAllAsync(ct) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[HesaplananKolon] Liste okunamadı.");
            return Json(new { ok = false, error = "Liste okunamadı." });
        }
    }

    /// <summary>Seçilebilecek view'lar + kolonları. Tanım ekranındaki açılır listeleri besler.</summary>
    [HttpGet("/ComputedColumn/api/sources")]
    public async Task<IActionResult> Sources(CancellationToken ct)
    {
        try
        {
            return Json(new { ok = true, sources = await _repo.GetSourcesAsync(ct) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[HesaplananKolon] Kaynak view listesi okunamadı.");
            return Json(new { ok = false, error = "Kaynak listesi okunamadı." });
        }
    }

    /// <summary>
    /// Kaydetmeden ÖNCE deneme okuması. Süreyi de döndürür: yavaş bir view'ın liste
    /// ekranını kilitleyeceği ancak burada fark edilebilir.
    /// </summary>
    [HttpPost("/ComputedColumn/api/preview")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Preview([FromBody] SaveComputedColumnRequest request, CancellationToken ct)
    {
        if (request is null) return Json(new { ok = false, error = "Geçersiz istek." });
        var result = await _repo.PreviewAsync(request, 3, ct);
        return Json(new
        {
            ok = result.Ok,
            error = result.Error,
            elapsedMs = result.ElapsedMs,
            rows = result.Rows,
        });
    }

    [HttpPost("/ComputedColumn/api/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromBody] SaveComputedColumnRequest request, CancellationToken ct)
    {
        if (request is null) return Json(new { ok = false, error = "Geçersiz istek." });
        try
        {
            var id = await _repo.SaveAsync(request, CurrentUserId(), ct);
            return Json(new { ok = true, id });
        }
        catch (ArgumentException ex)
        {
            // Doğrulama hatası kullanıcıya AYNEN döner — "view bulunamadı", "kolon yok" gibi
            // mesajlar teşhis ettirir; jenerik bir metin kullanıcıyı çıkmaza sokardı.
            return Json(new { ok = false, error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[HesaplananKolon] Kaydetme başarısız. Label={Label}", request.Label);
            return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    [HttpPost("/ComputedColumn/api/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete([FromQuery] int id, CancellationToken ct)
    {
        try
        {
            await _repo.DeleteAsync(id, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[HesaplananKolon] Silme başarısız. Id={Id}", id);
            return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    private int? CurrentUserId()
        => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id > 0 ? id : null;
}
