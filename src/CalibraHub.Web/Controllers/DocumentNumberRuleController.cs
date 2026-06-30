using System.Security.Claims;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Domain.Entities;
using CalibraHub.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Belge Numarası Kuralları — Tasarım Kuralları (DocLayoutRule) pattern'inin tıpkısı.
/// İki sayfa aynı menü altında tab'lı görünür: Belge Tasarımı + Belge Numarası.
///
/// Routes:
///   GET    /DocumentNumberRule                    → liste (SmartBoard)
///   GET    /DocumentNumberRule/BoardConfig        → board JSON (in-place refresh için)
///   GET    /DocumentNumberRule/New                → boş form
///   GET    /DocumentNumberRule/Edit/{id}          → mevcut form
///   POST   /DocumentNumberRule/SaveJson           → upsert (JSON)
///   POST   /DocumentNumberRule/DeleteJson         → silme (JSON)
///   GET    /DocumentNumberRule/Counters/{id}      → sayaç state'leri (admin debug)
/// </summary>
[Authorize]
[Route("[controller]")]
[CalibraHub.Web.Authorization.PermissionScope(CalibraHub.Application.Constants.FormCodes.DocNumberRules)]
public sealed class DocumentNumberRuleController : Controller
{
    private readonly IDocumentNumberRuleRepository _repo;
    private readonly IDocumentTypeRepository _docTypeRepo;
    private readonly IDocumentNumberService _generatorSvc;

    public DocumentNumberRuleController(
        IDocumentNumberRuleRepository repo,
        IDocumentTypeRepository docTypeRepo,
        IDocumentNumberService generatorSvc)
    {
        _repo = repo;
        _docTypeRepo = docTypeRepo;
        _generatorSvc = generatorSvc;
    }

    // ── Sayfalar ────────────────────────────────────────────────────────────

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var config = await BuildBoardConfigAsync(ct);
        return View(config);
    }

    [HttpGet("BoardConfig")]
    public async Task<IActionResult> BoardConfig(CancellationToken ct)
    {
        var config = await BuildBoardConfigAsync(ct);
        return Json(config);
    }

    [HttpGet("New")]
    public async Task<IActionResult> New(CancellationToken ct)
    {
        await PopulateLookupsAsync(ct);
        return View("Edit", new DocumentNumberRule
        {
            Name = string.Empty,
            DocumentTypeId = 0,
            CounterLength = 6,
            CounterStart = 1,
            ResetPeriod = DocumentNumberResetPeriod.Yearly,
            IsActive = true,
        });
    }

    [HttpGet("Edit/{id:int}")]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var rule = await _repo.GetAsync(id, ct);
        if (rule is null) return NotFound();
        await PopulateLookupsAsync(ct);
        return View("Edit", rule);
    }

    [HttpPost("SaveJson")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveJson([FromForm] DocumentNumberRule input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return BadRequest(new { ok = false, error = "Kural adı zorunludur." });
        if (input.DocumentTypeId <= 0)
            return BadRequest(new { ok = false, error = "Belge tipi seçimi zorunludur." });
        if (input.CounterLength is < 1 or > 20)
            return BadRequest(new { ok = false, error = "Sayaç uzunluğu 1-20 arasında olmalı." });

        try
        {
            var uid = int.TryParse(User?.FindFirstValue(ClaimTypes.NameIdentifier), out var _uid) ? _uid : (int?)null;
            input.UpdatedById = uid;
            if (input.Id == 0) input.CreatedById = uid;
            var savedId = await _repo.SaveAsync(input, ct);
            return Ok(new { ok = true, id = savedId });
        }
        catch (Exception ex)
        {
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
            return StatusCode(500, new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    [HttpGet("Counters/{id:int}")]
    public async Task<IActionResult> Counters(int id, CancellationToken ct)
    {
        var counters = await _repo.GetCountersAsync(id, ct);
        return Json(counters);
    }

    /// <summary>
    /// Live preview — admin form'da ayar girerken canlı "abc26000001" gibi bir örnek
    /// numara üretir (gerçek sayaç ARTMAZ). RuleId verilirse mevcut kuralın counter'ından
    /// +1, verilmezse CounterStart kullanılır.
    /// </summary>
    [HttpGet("Preview")]
    public IActionResult Preview(string? prefix, string? yearFormat, string? monthFormat,
        int counterLength = 6, int counterStart = 1, int? totalLength = null)
    {
        var date = DateTime.Now;
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(prefix))      sb.Append(prefix);
        if (!string.IsNullOrEmpty(yearFormat))  sb.Append(date.ToString(yearFormat, System.Globalization.CultureInfo.InvariantCulture));
        if (!string.IsNullOrEmpty(monthFormat)) sb.Append(date.ToString(monthFormat, System.Globalization.CultureInfo.InvariantCulture));
        var counterStr = counterStart.ToString().PadLeft(Math.Max(1, counterLength), '0');
        sb.Append(counterStr);
        var sample = sb.ToString();
        if (totalLength is > 0 && sample.Length < totalLength)
        {
            // Counter haneleri arttırarak total'e ulaş
            var needed = totalLength.Value - sample.Length;
            sb.Clear();
            if (!string.IsNullOrEmpty(prefix))      sb.Append(prefix);
            if (!string.IsNullOrEmpty(yearFormat))  sb.Append(date.ToString(yearFormat, System.Globalization.CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(monthFormat)) sb.Append(date.ToString(monthFormat, System.Globalization.CultureInfo.InvariantCulture));
            sb.Append(counterStart.ToString().PadLeft(counterLength + needed, '0'));
            sample = sb.ToString();
        }
        return Json(new
        {
            sample,
            length = sample.Length,
            parts = new
            {
                prefix      = prefix      ?? string.Empty,
                year        = string.IsNullOrEmpty(yearFormat)  ? string.Empty : date.ToString(yearFormat,  System.Globalization.CultureInfo.InvariantCulture),
                month       = string.IsNullOrEmpty(monthFormat) ? string.Empty : date.ToString(monthFormat, System.Globalization.CultureInfo.InvariantCulture),
                counter     = counterStr,
            },
        });
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private async Task PopulateLookupsAsync(CancellationToken ct)
    {
        var types = await _docTypeRepo.GetAllAsync(ct);
        ViewBag.DocumentTypes = types
            .Where(t => t.IsActive)
            .Select(t => new { id = t.Id, code = t.Code, name = t.Name })
            .ToList();
        ViewBag.ResetPeriods = new[]
        {
            new { value = (int)DocumentNumberResetPeriod.None,    label = "Sıfırlanmaz" },
            new { value = (int)DocumentNumberResetPeriod.Yearly,  label = "Yıllık (her 1 Ocak)" },
            new { value = (int)DocumentNumberResetPeriod.Monthly, label = "Aylık (her ayın 1'i)" },
            new { value = (int)DocumentNumberResetPeriod.Daily,   label = "Günlük" },
        };
    }

    private async Task<object> BuildBoardConfigAsync(CancellationToken ct)
    {
        var rules = await _repo.ListAsync(ct);
        var types = (await _docTypeRepo.GetAllAsync(ct)).ToDictionary(t => t.Id, t => t.Name);

        var entities = rules.Select(r =>
        {
            var typeName = types.TryGetValue(r.DocumentTypeId, out var n) ? n : $"Tip #{r.DocumentTypeId}";
            var sample = BuildSamplePreview(r);
            return new
            {
                id = r.Id,
                title = r.Name,
                subtitle = $"{typeName} · ağırlık {r.Weight}",
                description = $"Format: {sample}",
                statusBadge = r.IsActive
                    ? new { label = "Aktif",  color = "emerald" }
                    : new { label = "Pasif", color = "slate" },
                widgets = new object[]
                {
                    new { id="w_prefix",  type="data", dataType="text", label="Prefix",
                          value = string.IsNullOrEmpty(r.Prefix) ? "—" : r.Prefix, color="indigo" },
                    new { id="w_format",  type="data", dataType="text", label="Yıl/Ay/Sayaç",
                          value = $"{r.YearFormat ?? "-"} / {r.MonthFormat ?? "-"} / {r.CounterLength}", color="slate" },
                    new { id="w_reset",   type="data", dataType="options", label="Reset",
                          value = r.ResetPeriod.ToString(), color="amber" },
                    new { id="w_filters", type="data", dataType="text", label="Filtre",
                          value = BuildFilterChip(r), color="violet" },
                },
                primaryAction = new
                {
                    label = "Düzenle", icon = "Edit", color = "amber",
                    url = $"/DocumentNumberRule/Edit/{r.Id}",
                    hideButton = true,
                },
                secondaryAction = new
                {
                    label = "Sil", icon = "Trash2",
                    apiUrl = $"/DocumentNumberRule/DeleteJson?id={r.Id}",
                    apiMethod = "POST",
                    confirm = $"Silmek istediğinize emin misiniz? ({r.Name})",
                },
            };
        }).ToArray();

        return new
        {
            boardKey = "document-number-rules",
            title = "Belge Numarası Kuralları",
            subtitle = $"{entities.Length} kural",
            icon = "Hash",
            iconColor = "indigo",
            refreshUrl = "/DocumentNumberRule/BoardConfig",
            searchPlaceholder = "Hızlı ara…",
            emptyText = "Henüz numara kuralı tanımlanmamış",
            actions = new object[]
            {
                new { id = "new", label = "Yeni Numara Kuralı", icon = "Plus", variant = "primary",
                      url = "/DocumentNumberRule/New" },
            },
            masterWidgets = new List<object>
            {
                SmartBoardFilterHelpers.MakeStdWidget   ("w_prefix",  "Prefix",       "text"),
                SmartBoardFilterHelpers.MakeStdWidget   ("w_format",  "Yıl/Ay/Sayaç", "text"),
                SmartBoardFilterHelpers.MakeOptionsWidget("w_reset",  "Reset",
                    SmartBoardFilterHelpers.ToOptionsList(new[] { "Never", "Yearly", "Monthly", "Daily" })),
                SmartBoardFilterHelpers.MakeStdWidget   ("w_filters", "Filtre",       "text"),
            },
            entities,
        };
    }

    private static string BuildSamplePreview(DocumentNumberRule r)
    {
        var date = DateTime.Now;
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(r.Prefix))      sb.Append(r.Prefix);
        if (!string.IsNullOrEmpty(r.YearFormat))  sb.Append(date.ToString(r.YearFormat, System.Globalization.CultureInfo.InvariantCulture));
        if (!string.IsNullOrEmpty(r.MonthFormat)) sb.Append(date.ToString(r.MonthFormat, System.Globalization.CultureInfo.InvariantCulture));
        sb.Append(r.CounterStart.ToString().PadLeft(Math.Max(1, r.CounterLength), '0'));
        return sb.ToString();
    }

    private static string BuildFilterChip(DocumentNumberRule r)
    {
        var parts = new List<string>();
        if (r.ContactId      is > 0) parts.Add($"Cari #{r.ContactId}");
        if (r.ContactGroupId is > 0) parts.Add($"Grup #{r.ContactGroupId}");
        if (r.UserId         is > 0) parts.Add($"User #{r.UserId}");
        if (r.BranchId       is > 0) parts.Add($"Şube #{r.BranchId}");
        if (r.FromDate.HasValue || r.ToDate.HasValue) parts.Add("Tarih aralığı");
        return parts.Count == 0 ? "(Wildcard — herkes)" : string.Join(" · ", parts);
    }
}
