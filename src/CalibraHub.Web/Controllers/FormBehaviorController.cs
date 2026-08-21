using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Auditing;
using CalibraHub.Application.Security;
using CalibraHub.Application.Services.Rules;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Web.Models.Sales;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Form Davranış Katmanı API (2026-08-05) — standart alan davranışları.
///   - GET  /api/form-behavior/{formCode} → katalog + davranış merge (tüm oturumlu
///     kullanıcılar; DocumentEdit runtime'ı ve Alan Yönetimi editörü okur)
///   - POST /api/form-behavior/save       → full-replace kaydet (yalnız admin, CSRF, audit)
///
/// Fail-open: davranış satırı olmayan alan varsayılan davranışta; katalogda olmayan
/// key yok sayılır; kilitli alanlar gizlenemez. Ekran markup'ı SABİTTİR — bu API
/// yalnızca davranış metadata'sı taşır (plan: ko-ula-ba-l-alan-g-r-n-rl-l).
/// </summary>
[Authorize]
public sealed partial class FormBehaviorController : Controller
{
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]{0,49}$")]
    private static partial Regex FormCodeRegex();

    private static readonly HashSet<string> AllowedLabelStyles =
        new(StringComparer.OrdinalIgnoreCase) { "standard", "modern", "inline" };

    /// <summary>
    /// Form başına genel hücre genişliği (defaultCardWidth) rezerve satırın FieldKey'i.
    /// Yeni tablo açılmadı — CardWidth kolonu bu satırda tekil form-seviye değeri taşır.
    /// Katalogda karşılığı olmadığından GET'teki fields[] merge döngüsüne hiç girmez.
    /// </summary>
    private const string DefaultCardWidthFieldKey = "__card";

    /// <summary>
    /// Şerit başına satır yüksekliği (px) rezerve satırın FieldKey öneki:
    /// "__strip1", "__strip2"… Değer RowHeight kolonunda taşınır. __card ile
    /// aynı desen (yeni tablo açılmaz); katalogda karşılığı olmadığından
    /// GET'teki fields[] merge döngüsüne girmez.
    /// </summary>
    private const string StripRowHeightKeyPrefix = "__strip";

    private const int RowHeightMin = 28;
    private const int RowHeightMax = 96;

    private static int? ClampRowHeight(int? v)
        => v is null ? null : Math.Min(RowHeightMax, Math.Max(RowHeightMin, v.Value));

    private readonly IFormFieldBehaviorRepository _repository;
    private readonly IAuditTrailService? _audit;
    private readonly ILogger<FormBehaviorController> _logger;
    private readonly CalibraHub.Application.Abstractions.Services.IWidgetService _widgetService;

    public FormBehaviorController(
        IFormFieldBehaviorRepository repository,
        ILogger<FormBehaviorController> logger,
        CalibraHub.Application.Abstractions.Services.IWidgetService widgetService,
        IAuditTrailService? audit = null)
    {
        _repository = repository;
        _logger = logger;
        _widgetService = widgetService;
        _audit = audit;
    }

    public sealed record FieldBehaviorDto(
        string Key, bool IsVisible = true, bool IsRequired = false,
        string? DefaultValue = null, string? LabelText = null, string? LabelStyle = null,
        string? VisibleIf = null, string? RequiredIf = null, int? CardSection = null, int? CardOrder = null,
        int? CardWidth = null);

    public sealed record TabBehaviorDto(string Key, bool IsVisible = true, int SortOrder = 0, string? LabelText = null);

    /// <summary>Şerit no (1..N) → satır yüksekliği (px). null/boş = varsayılan.</summary>
    public sealed record StripHeightDto(int Section, int? RowHeight);

    public sealed record SaveBehaviorRequest(
        string FormCode, List<FieldBehaviorDto>? Fields, List<TabBehaviorDto>? Tabs, int? DefaultCardWidth = null,
        List<StripHeightDto>? StripHeights = null);

    private sealed record CatalogItem(string Key, string Label, string Tab, string DataType, bool Locked);

    /// <summary>
    /// Form kodu → birleşik alan kataloğu. Header (*_EDIT) formları
    /// DocumentHeaderFieldCatalog'tan, kalem (*_LINES) formları
    /// DocumentLineFieldCatalog'tan gelir; kalem alanları tek "lines" sekmesindedir.
    /// Desteklenmeyen form → null. isHeader: sekme yönetimi yalnız header'da.
    /// </summary>
    /// <summary>
    /// Kalem kartinda inline gosterilen widget tipleri — CalibraLineItemsGrid'deki
    /// INLINE_TYPES ile AYNI kume olmali (yoksa duzenleyicide gorunen alan kartta
    /// yok, ya da tersi olur).
    /// </summary>
    private static readonly HashSet<string> InlineWidgetTypes =
        new(StringComparer.OrdinalIgnoreCase) { "text", "numeric", "date", "dropdown" };

    private async Task<(IReadOnlyList<CatalogItem> Items, bool IsHeader)?> ResolveCatalogAsync(
        string formCode, CancellationToken ct)
    {
        var header = DocumentHeaderFieldCatalog.Resolve(formCode);
        if (header is not null)
            return (header.Select(f => new CatalogItem(f.Key, f.Label, f.Tab, f.DataType, f.Locked)).ToList(), true);

        var lines = DocumentLineFieldCatalog.Resolve(formCode);
        if (lines is null) return null;

        var items = lines.Select(f => new CatalogItem(f.Key, f.Label, "lines", f.DataType, f.Locked)).ToList();

        // 2026-08-20 — "Kartta Goster" widget'lari da katalogda. Gerekce: bu alanlar
        // kalem kartinin izgarasinda GERCEKTEN yer kapliyor (CalibraLineItemsGrid
        // widgetCardColumns), ama Alan Duzeni'nde gorunmedikleri icin bolum/sira/
        // genislikleri ayarlanamiyordu — kullanici karttaki "Parti No"yu duzenleyicide
        // bulamiyordu. Anahtar "w_" + WidgetCode: kart tarafi zaten lineBehaviors[col.key]
        // ile bu anahtari okuyor, dolayisiyla ek bir cozumleme kodu GEREKMEDI.
        try
        {
            var schema = await _widgetService.GetFormSchemaByCodeAsync(formCode, ct);
            if (schema?.Widgets is not null)
            {
                foreach (var w in schema.Widgets)
                {
                    if (!w.ShowOnCard || !w.IsActive) continue;
                    if (!InlineWidgetTypes.Contains(w.DataType ?? string.Empty)) continue;
                    var key = "w_" + w.WidgetCode;
                    if (items.Any(i => string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase))) continue;
                    var ruleType = string.Equals(w.DataType, "numeric", StringComparison.OrdinalIgnoreCase)
                        ? "numeric" : "text";
                    items.Add(new CatalogItem(key, w.Label ?? w.WidgetCode, "lines", ruleType, false));
                }
            }
        }
        catch (Exception ex)
        {
            // Fail-open: widget semasi okunamazsa standart alanlarla devam — duzenleyici
            // acilmaya devam eder, yalniz widget satirlari listelenmez.
            _logger.LogWarning(ex, "Alan Düzeni: {FormCode} için widget şeması okunamadı.", formCode);
        }

        return (items, false);
    }

    [HttpGet("/api/form-behavior/{formCode}")]
    public async Task<IActionResult> Get(string formCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(formCode) || !FormCodeRegex().IsMatch(formCode.Trim()))
            return Json(new { ok = false, error = "Geçersiz form kodu." });
        formCode = formCode.Trim();

        var resolved = await ResolveCatalogAsync(formCode, ct);
        if (resolved is null)
            return Json(new { ok = false, error = "Bu form için davranış katmanı desteklenmiyor." });
        var (catalog, isHeader) = resolved.Value;

        var rows = await _repository.GetByFormCodeAsync(formCode, ct);
        var byKey = rows.ToDictionary(r => r.FieldKey, StringComparer.OrdinalIgnoreCase);

        var fields = catalog.Select(f =>
        {
            byKey.TryGetValue(f.Key, out var b);
            var rules = ParseRules(b?.RulesJson);
            return new
            {
                key = f.Key,
                label = f.Label,
                tab = f.Tab,
                dataType = f.DataType,
                locked = f.Locked,
                isVisible = f.Locked || b is null || b.IsVisible,
                isRequired = b?.IsRequired ?? false,
                defaultValue = b?.DefaultValue,
                labelText = b?.LabelText,
                labelStyle = b?.LabelStyle,
                visibleIf = rules.VisibleIf,
                requiredIf = rules.RequiredIf,
                cardSection = b?.CardSection,
                cardOrder = b?.CardOrder,
                cardWidth = b?.CardWidth,
            };
        }).ToArray();

        byKey.TryGetValue(DefaultCardWidthFieldKey, out var cardDefaultRow);
        var defaultCardWidth = cardDefaultRow?.CardWidth;

        // Şerit satır yükseklikleri — rezerve "__stripN" satırlarından toplanır.
        var stripHeights = rows
            .Where(r => r.FieldKey.StartsWith(StripRowHeightKeyPrefix, StringComparison.OrdinalIgnoreCase)
                        && r.RowHeight is not null)
            .Select(r => new
            {
                section = int.TryParse(r.FieldKey.AsSpan(StripRowHeightKeyPrefix.Length), out var n) ? n : 0,
                rowHeight = r.RowHeight,
            })
            .Where(x => x.section > 0)
            .ToArray();

        // Sekme yönetimi yalnız header formlarda — kalem formunda boş liste döner.
        object[] tabs = Array.Empty<object>();
        if (isHeader)
        {
            tabs = DocumentHeaderFieldCatalog.Tabs.Select((t, i) =>
            {
                byKey.TryGetValue("tab:" + t.Key, out var b);
                return new
                {
                    key = t.Key,
                    label = t.Label,
                    locked = t.Locked,
                    isVisible = t.Locked || b is null || b.IsVisible,
                    sortOrder = b?.SortOrder ?? i,
                    labelText = b?.LabelText,
                };
            }).OrderBy(t => t.sortOrder).Cast<object>().ToArray();
        }

        return Json(new { ok = true, formCode, canEdit = IsAdmin(), fields, tabs, defaultCardWidth, stripHeights });
    }

    [HttpPost("/api/form-behavior/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromBody] SaveBehaviorRequest request, CancellationToken ct)
    {
        if (!IsAdmin()) return Forbid();
        if (request is null || string.IsNullOrWhiteSpace(request.FormCode)
            || !FormCodeRegex().IsMatch(request.FormCode.Trim()))
            return Json(new { ok = false, error = "Geçersiz form kodu." });

        var formCode = request.FormCode.Trim();
        var resolvedCat = await ResolveCatalogAsync(formCode, ct);
        if (resolvedCat is null)
            return Json(new { ok = false, error = "Bu form için davranış katmanı desteklenmiyor." });
        var (catalogItems, isHeaderForm) = resolvedCat.Value;
        var catalogByKey = catalogItems.ToDictionary(f => f.Key, StringComparer.OrdinalIgnoreCase);
        var tabByKey = isHeaderForm
            ? DocumentHeaderFieldCatalog.Tabs.ToDictionary(t => t.Key, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, DocumentHeaderFieldCatalog.HeaderTab>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var rows = new List<FormFieldBehavior>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var f in request.Fields ?? [])
            {
                var key = f.Key?.Trim();
                if (string.IsNullOrEmpty(key) || !catalogByKey.TryGetValue(key, out var cat) || !seen.Add(key))
                    continue; // katalogda olmayan / yinelenen key yok sayılır

                var isVisible = cat.Locked || f.IsVisible;             // kilitli alan gizlenemez
                var visibleIf = cat.Locked ? null : RuleExpr.Sanitize("visibleIf(" + key + ")", f.VisibleIf);
                var requiredIf = RuleExpr.Sanitize("requiredIf(" + key + ")", f.RequiredIf);
                var labelText = string.IsNullOrWhiteSpace(f.LabelText) ? null : f.LabelText.Trim();
                if (labelText is { Length: > 60 }) labelText = labelText[..60];
                var labelStyle = !string.IsNullOrWhiteSpace(f.LabelStyle) && AllowedLabelStyles.Contains(f.LabelStyle.Trim())
                    ? f.LabelStyle.Trim().ToLowerInvariant() : null;
                var defaultValue = string.IsNullOrWhiteSpace(f.DefaultValue) ? null : f.DefaultValue.Trim();
                if (defaultValue is { Length: > 400 }) defaultValue = defaultValue[..400];
                var cardWidth = ClampCardWidth(f.CardWidth, $"field[{key}]");

                string? rulesJson = null;
                if (visibleIf is not null || requiredIf is not null)
                {
                    var dict = new Dictionary<string, string>();
                    if (visibleIf is not null) dict["visibleIf"] = visibleIf;
                    if (requiredIf is not null) dict["requiredIf"] = requiredIf;
                    rulesJson = JsonSerializer.Serialize(dict);
                }

                // Varsayılandan farksız davranış SAKLANMAZ — tablo yalın kalır,
                // fail-open semantiği net olur (satır yok = bugünkü davranış).
                var isDefault = isVisible && !f.IsRequired && defaultValue is null
                    && labelText is null && labelStyle is null && rulesJson is null
                    && f.CardSection is null && f.CardOrder is null && cardWidth is null;
                if (isDefault) continue;

                rows.Add(new FormFieldBehavior
                {
                    FormCode = formCode,
                    FieldKey = key,
                    IsVisible = isVisible,
                    IsRequired = f.IsRequired,
                    DefaultValue = defaultValue,
                    LabelText = labelText,
                    LabelStyle = labelStyle,
                    RulesJson = rulesJson,
                    SortOrder = 0,
                    CardSection = f.CardSection,
                    CardOrder = f.CardOrder,
                    CardWidth = cardWidth,
                    CreatedById = GetUserId(),
                    CreatedBy = User?.Identity?.Name,
                });
            }

            var defaultCardWidth = ClampCardWidth(request.DefaultCardWidth, "defaultCardWidth");
            if (defaultCardWidth is not null)
            {
                rows.Add(new FormFieldBehavior
                {
                    FormCode = formCode,
                    FieldKey = DefaultCardWidthFieldKey,
                    IsVisible = true,
                    CardWidth = defaultCardWidth,
                    CreatedById = GetUserId(),
                    CreatedBy = User?.Identity?.Name,
                });
            }

            // Şerit satır yükseklikleri — yalnız DEĞER VERİLMİŞ şeritler satır açar
            // (null = varsayılan, satır yazılmaz → fail-open, kart kendi ölçüsünü kullanır).
            foreach (var sh in request.StripHeights ?? [])
            {
                if (sh is null || sh.Section < 1 || sh.Section > 99) continue;
                var h = ClampRowHeight(sh.RowHeight);
                if (h is null) continue;
                var key = StripRowHeightKeyPrefix + sh.Section;
                if (!seen.Add(key)) continue;
                rows.Add(new FormFieldBehavior
                {
                    FormCode = formCode,
                    FieldKey = key,
                    IsVisible = true,
                    RowHeight = h,
                    CreatedById = GetUserId(),
                    CreatedBy = User?.Identity?.Name,
                });
            }

            foreach (var (t, i) in (request.Tabs ?? []).Select(static (t, i) => (t, i)))
            {
                var key = t.Key?.Trim();
                if (string.IsNullOrEmpty(key) || !tabByKey.TryGetValue(key, out var cat) || !seen.Add("tab:" + key))
                    continue;

                var isVisible = cat.Locked || t.IsVisible;
                var labelText = string.IsNullOrWhiteSpace(t.LabelText) ? null : t.LabelText.Trim();
                if (labelText is { Length: > 40 }) labelText = labelText[..40];

                // Varsayılan sıra = katalog sırası; farklı sıra da davranış sayılır.
                var catalogIndex = DocumentHeaderFieldCatalog.Tabs.ToList().FindIndex(x =>
                    string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
                if (isVisible && labelText is null && t.SortOrder == catalogIndex) continue;

                rows.Add(new FormFieldBehavior
                {
                    FormCode = formCode,
                    FieldKey = "tab:" + key,
                    IsVisible = isVisible,
                    LabelText = labelText,
                    SortOrder = t.SortOrder,
                    CreatedById = GetUserId(),
                    CreatedBy = User?.Identity?.Name,
                });
            }

            var old = await _repository.GetByFormCodeAsync(formCode, ct);
            await _repository.ReplaceForFormAsync(formCode, rows, ct);

            _audit?.LogUpdate("FormFieldBehavior", formCode, $"Form alan davranışı ({formCode})",
                new { Davranis = SerializeForAudit(old.Select(ToAuditShape)) },
                new { Davranis = SerializeForAudit(rows.Select(ToAuditShape)) });

            return Json(new { ok = true, count = rows.Count });
        }
        catch (ArgumentException ex)
        {
            // Kural sanitizasyon hatası — admin'e net mesaj (iç detay değil, doğrulama mesajı).
            return Json(new { ok = false, error = ex.Message });
        }
        catch (Exception ex)
        {
            HttpContext.RequestServices.GetService<ILogger<FormBehaviorController>>()
                ?.LogError(ex, "Form davranışı kaydedilemedi (formCode={FormCode})", formCode);
            return Json(new { ok = false, error = "Kaydedilemedi. Ayrıntılar sunucu loglarında." });
        }
    }

    private static object ToAuditShape(FormFieldBehavior r) => new
    {
        r.FieldKey, r.IsVisible, r.IsRequired, r.DefaultValue, r.LabelText, r.LabelStyle, r.RulesJson, r.SortOrder,
        r.CardSection, r.CardOrder, r.CardWidth,
    };

    /// <summary>
    /// 12-sütunlu ızgara sınırı — 1..12 dışı değer REDDEDİLMEZ, sessizce en yakın
    /// sınıra clamp'lenir ve sunucu loguna düşer (bkz. CLAUDE.md "sessiz kırık"
    /// kural #2 ruhu: bozuk girdi sessizce yutulmaz, iz bırakır).
    /// </summary>
    private int? ClampCardWidth(int? value, string context)
    {
        if (value is null) return null;
        var clamped = Math.Clamp(value.Value, 1, 12);
        if (clamped != value.Value)
            _logger.LogWarning(
                "CardWidth 1..12 dışında, clamp uygulandı: {Context} original={Original} clamped={Clamped}",
                context, value.Value, clamped);
        return clamped;
    }

    private static string SerializeForAudit(IEnumerable<object> rows) =>
        JsonSerializer.Serialize(rows, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static (string? VisibleIf, string? RequiredIf) ParseRules(string? rulesJson)
    {
        if (string.IsNullOrWhiteSpace(rulesJson)) return (null, null);
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(rulesJson);
            if (dict is null) return (null, null);
            dict.TryGetValue("visibleIf", out var vi);
            dict.TryGetValue("requiredIf", out var ri);
            return (vi, ri);
        }
        catch (JsonException) { return (null, null); }
    }

    private bool IsAdmin()
    {
        var roleStr = User?.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        return UserAuthorizationCatalog.TryParseRole(roleStr, out var role)
               && role is UserRole.DepartmentManager or UserRole.SystemAdmin;
    }

    private int? GetUserId() =>
        int.TryParse(User?.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : null;
}
