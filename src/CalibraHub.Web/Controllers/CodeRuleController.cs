using System.Security.Claims;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Cari/Stok/Seri Kodu Türetme Kuralları — Tasarım Kuralları (DocLayoutRule/DocumentNumberRule)
/// pattern'inin tıpkısı. Sol "Tasarım > Tasarım Kuralları" altında tab'lar:
///   - /CodeRule?entity=contact → Cari Kodu Kuralları
///   - /CodeRule?entity=item    → Stok Kodu Kuralları
///   - /CodeRule?entity=serial  → Seri Numarası Kuralları (2026-08-29) — otomatik seri üretimi
///     (Items.AutoSerial açık kartlarda depo girişi/mobil teslimat) bu kural motorunu kullanır;
///     bkz. SqlStockDocRepository.GenerateAutoSerialsAsync (fail-open: aktif/eşleşen kural yoksa
///     legacy sabit format'a düşer). Benzersizlik CodeRuleCounter'a dayandığı için template'te
///     {Counter:N} ZORUNLU tutulur (bkz. SaveJson).
///
/// Routes:
///   GET    /CodeRule[?entity=contact|item]   → Index (SmartBoard liste)
///   GET    /CodeRule/BoardConfig?entity=...  → board JSON (refresh)
///   GET    /CodeRule/New?entity=...          → boş form
///   GET    /CodeRule/Edit/{id}               → mevcut form
///   POST   /CodeRule/SaveJson                → upsert + conditions
///   POST   /CodeRule/DeleteJson              → silme
///   POST   /CodeRule/Preview                 → canlı önizleme (sayaç ARTMAZ)
/// </summary>
[Authorize]
[Route("[controller]")]
[CalibraHub.Web.Authorization.PermissionScope(CalibraHub.Application.Constants.FormCodes.DocNumberRules)]
public sealed class CodeRuleController : Controller
{
    private readonly ICodeRuleRepository _repo;
    private readonly ICodeGeneratorService _generator;
    private readonly ILogger<CodeRuleController> _logger;

    public CodeRuleController(ICodeRuleRepository repo, ICodeGeneratorService generator, ILogger<CodeRuleController> logger)
    {
        _repo = repo;
        _generator = generator;
        _logger = logger;
    }

    // ── Sayfalar ────────────────────────────────────────────────────────────

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] string entity = "contact", CancellationToken ct = default)
    {
        var et = NormalizeEntity(entity);
        ViewBag.EntityType = et;
        ViewBag.EntityLabel = EntityLabel(et);
        var config = await BuildBoardConfigAsync(et, ct);
        return View(config);
    }

    [HttpGet("BoardConfig")]
    public async Task<IActionResult> BoardConfig([FromQuery] string entity = "contact", CancellationToken ct = default)
    {
        var config = await BuildBoardConfigAsync(NormalizeEntity(entity), ct);
        return Json(config);
    }

    [HttpGet("New")]
    public IActionResult New([FromQuery] string entity = "contact")
    {
        var et = NormalizeEntity(entity);
        PopulateLookups(et);
        return View("Edit", new CodeRule
        {
            EntityType = et,
            Name = string.Empty,
            Template = string.Empty,
            Priority = 0,
            ResetPeriod = DocumentNumberResetPeriod.None,
            IsActive = true,
        });
    }

    [HttpGet("Edit/{id:int}")]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var rule = await _repo.GetAsync(id, ct);
        if (rule is null) return NotFound();
        PopulateLookups(rule.EntityType);
        return View("Edit", rule);
    }

    [HttpPost("SaveJson")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveJson([FromBody] SaveCodeRuleRequest input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return BadRequest(new { ok = false, error = "Kural adı zorunludur." });
        if (string.IsNullOrWhiteSpace(input.Template))
            return BadRequest(new { ok = false, error = "Template zorunludur (örn. 'MS-{Field:City}-{Counter:4}')." });

        var et = NormalizeEntity(input.EntityType);
        if (et != "Contact" && et != "Item" && et != "Serial")
            return BadRequest(new { ok = false, error = "EntityType 'Contact', 'Item' veya 'Serial' olmalı." });

        // Seri kuralı benzersizliği CodeRuleCounter'ın atomik artışına dayanır (bkz.
        // SqlStockDocRepository.GenerateAutoSerialsAsync) — sayaçsız sabit template aynı
        // belge/satır içindeki çoklu seri üretiminde (henüz DB'ye yazılmamış aday kodlar
        // arasında) mükerrer kod üretebilir. Bu yüzden kaydetmeden reddedilir.
        if (et == "Serial" && !input.Template.Contains("{Counter:", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { ok = false, error = "Seri Numarası kuralında template mutlaka {Counter:N} içermelidir (aksi halde çoklu seri üretiminde çakışma riski oluşur)." });

        try
        {
            var uid = int.TryParse(User?.FindFirstValue(ClaimTypes.NameIdentifier), out var _uid) ? _uid : (int?)null;
            var rule = new CodeRule
            {
                Id          = input.Id,
                EntityType  = et,
                Name        = input.Name.Trim(),
                Template    = input.Template.Trim(),
                Priority    = input.Priority,
                ResetPeriod = (DocumentNumberResetPeriod)input.ResetPeriod,
                IsActive    = input.IsActive,
                CreatedById = input.Id == 0 ? uid : null,
                UpdatedById = uid,
                Conditions  = (input.Conditions ?? new()).Select(c => new CodeRuleCondition
                {
                    FieldType = string.IsNullOrWhiteSpace(c.FieldType) ? "Field" : c.FieldType.Trim(),
                    FieldName = (c.FieldName ?? string.Empty).Trim(),
                    Operator  = string.IsNullOrWhiteSpace(c.Operator) ? "=" : c.Operator.Trim(),
                    Value     = c.Value,
                }).Where(c => !string.IsNullOrWhiteSpace(c.FieldName)).ToList(),
            };
            var savedId = await _repo.SaveAsync(rule, ct);
            return Ok(new { ok = true, id = savedId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CodeRule] SaveJson başarısız.");
            return StatusCode(500, new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    [HttpPost("DeleteJson")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteJson(int id, CancellationToken ct)
    {
        try
        {
            await _repo.DeleteAsync(id, ct);
            return Ok(new { ok = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CodeRule] DeleteJson başarısız.");
            return StatusCode(500, new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    /// <summary>
    /// Canlı önizleme — kural test edilirken örnek FieldValues + WidgetValues ile
    /// kod üretir. NOT: Counter ARTAR (test sırasında DB'de iz bırakır). Production'da
    /// gerçek "kuru" preview için ileride snapshot/rollback eklenebilir.
    /// </summary>
    [HttpPost("Preview")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Preview([FromBody] GenerateCodeRequest request, CancellationToken ct)
    {
        request.EntityType = NormalizeEntity(request.EntityType);
        var result = await _generator.GenerateAsync(request, ct);
        return Json(result);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static string NormalizeEntity(string? raw)
    {
        var v = (raw ?? "contact").Trim().ToLowerInvariant();
        return v switch
        {
            "item" => "Item",
            "serial" => "Serial",
            _ => "Contact",
        };
    }

    private static string EntityLabel(string entityType) => entityType switch
    {
        "Item" => "Stok Kodu",
        "Serial" => "Seri Numarası",
        _ => "Cari Kodu",
    };

    private void PopulateLookups(string entityType)
    {
        ViewBag.EntityType = entityType;
        ViewBag.EntityLabel = EntityLabel(entityType);
        ViewBag.ResetPeriods = new[]
        {
            new { value = (int)DocumentNumberResetPeriod.None,    label = "Sıfırlanmaz" },
            new { value = (int)DocumentNumberResetPeriod.Yearly,  label = "Yıllık" },
            new { value = (int)DocumentNumberResetPeriod.Monthly, label = "Aylık" },
            new { value = (int)DocumentNumberResetPeriod.Daily,   label = "Günlük" },
        };
        ViewBag.SampleFields = entityType switch
        {
            "Contact" => new[] { "AccountCode", "AccountTitle", "TaxNumber", "City", "District", "CountryCode", "ContactGroupId", "AccountType" },
            "Serial"  => new[] { "ItemCode" },
            _         => new[] { "Code", "Name", "TypeId", "UnitId", "TaxRate" },
        };
        ViewBag.Operators = new[]
        {
            new { value = "=",          label = "Eşittir (=)" },
            new { value = "!=",         label = "Eşit değildir (!=)" },
            new { value = "in",         label = "Listede (in)" },
            new { value = "notin",      label = "Listede değil (notin)" },
            new { value = "startsWith", label = "İle başlar" },
            new { value = "isNull",     label = "Boş" },
            new { value = "isNotNull",  label = "Dolu" },
        };
    }

    private async Task<object> BuildBoardConfigAsync(string entityType, CancellationToken ct)
    {
        var rules = await _repo.ListAsync(entityType, ct);
        var label = EntityLabel(entityType);

        var entities = rules.Select(r => new
        {
            id = r.Id,
            title = r.Name,
            subtitle = $"Öncelik {r.Priority} · {r.Conditions.Count} şart",
            description = $"Format: {r.Template}",
            statusBadge = r.IsActive
                ? new { label = "Aktif", color = "emerald" }
                : new { label = "Pasif", color = "slate" },
            widgets = new object[]
            {
                new { id = "w_template", type = "data", dataType = "text",
                      label = "Template", value = r.Template, color = "indigo" },
                new { id = "w_reset",    type = "data", dataType = "options",
                      label = "Reset",    value = r.ResetPeriod.ToString(), color = "amber" },
                new { id = "w_conds",    type = "data", dataType = "numeric",
                      label = "Şart",     value = r.Conditions.Count.ToString(), color = "violet" },
            },
            primaryAction = new
            {
                label = "Düzenle", icon = "Edit", color = "amber",
                // embed=1 zorunlu: bu kart _DesignRulesTabs'in İÇ iframe'inde render edilir;
                // embed'siz url iç iframe'i host moduna düşürüp şerit çiftlenmesine yol açar.
                url = $"/CodeRule/Edit/{r.Id}?embed=1",
                hideButton = true,
            },
            secondaryAction = new
            {
                label = "Sil", icon = "Trash2",
                apiUrl = $"/CodeRule/DeleteJson?id={r.Id}",
                apiMethod = "POST",
                confirm = $"Silmek istediğinize emin misiniz? ({r.Name})",
            },
        }).ToArray();

        return new
        {
            boardKey = $"code-rules-{entityType.ToLowerInvariant()}",
            title = $"{label} Kuralları",
            subtitle = $"{entities.Length} kural",
            icon = entityType switch { "Contact" => "Users", "Serial" => "Hash", _ => "Package" },
            iconColor = entityType switch { "Contact" => "indigo", "Serial" => "violet", _ => "emerald" },
            refreshUrl = $"/CodeRule/BoardConfig?entity={entityType.ToLowerInvariant()}",
            searchPlaceholder = "Hızlı ara…",
            emptyText = "Henüz kural tanımlanmamış",
            actions = new object[]
            {
                // embed=1 — bkz. primaryAction üstündeki not (iç iframe host moduna düşmesin).
                new { id = "new", label = $"Yeni {label} Kuralı", icon = "Plus", variant = "primary",
                      url = $"/CodeRule/New?entity={entityType.ToLowerInvariant()}&embed=1" },
            },
            masterWidgets = Array.Empty<object>(),
            entities,
        };
    }
}
