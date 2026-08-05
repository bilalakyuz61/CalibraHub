using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Auditing;
using CalibraHub.Application.Security;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Kalem kartı düzeni API — belge kalem kartlarının konum/boyut (24 kolon ızgara)
/// düzenini yönetir (2026-08-05).
///   - GET  /api/line-card-layout/{formCode}  → aktif düzen + canEdit (tüm oturumlu kullanıcılar;
///     grid mount'ta okur, admin olmayanlar yalnızca uygular)
///   - POST /api/line-card-layout/save        → upsert (yalnız admin: DepartmentManager/SystemAdmin)
///   - POST /api/line-card-layout/reset       → düzeni sil, varsayılana dön (yalnız admin)
///
/// Düzen form bazlıdır (herkese ortak, kullanıcı bazlı değil — 2026-08-05 kullanıcı kararı).
/// </summary>
[Authorize]
public sealed partial class LineCardLayoutController : Controller
{
    // Form kodu whitelist'i — SQL'e parametreli gidiyor ama çöp kayıt/anahtar
    // büyümesini de engelle (SALES_QUOTE_LINES, STOCK_IN_LINES ... deseni).
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]{0,49}$")]
    private static partial Regex FormCodeRegex();

    private static readonly JsonSerializerOptions StoreJson = new(JsonSerializerDefaults.Web);

    private readonly ILineCardLayoutRepository _repository;
    private readonly CalibraHub.Application.Abstractions.Services.IWidgetService _widgetService;
    private readonly IAuditTrailService? _audit;

    public LineCardLayoutController(
        ILineCardLayoutRepository repository,
        CalibraHub.Application.Abstractions.Services.IWidgetService widgetService,
        IAuditTrailService? audit = null)
    {
        _repository = repository;
        _widgetService = widgetService;
        _audit = audit;
    }

    /// <summary>
    /// Tek düzen öğesi. Span 1-24 (WidgetMas.ColSpan ızgarası ile aynı).
    /// Label* alanları başlık override'ıdır (2026-08-05 kullanıcı isteği):
    ///   Label       — başlık metni (boş = alanın varsayılan etiketi)
    ///   LabelSize   — px (9-15 arası), null = varsayılan (10)
    ///   LabelWeight — 400/500/600/700, null = varsayılan (bold)
    ///   LabelColor  — semantik token (slate/indigo/emerald/amber/rose/blue/violet),
    ///                 null = varsayılan. HEX asla saklanmaz — tema uyumu.
    /// </summary>
    public sealed record LayoutItemDto(
        string Key, int Span, int Order, bool Visible = true,
        string? Label = null, int? LabelSize = null, int? LabelWeight = null, string? LabelColor = null);

    private static readonly HashSet<string> AllowedLabelColors =
        new(StringComparer.OrdinalIgnoreCase) { "slate", "indigo", "emerald", "amber", "rose", "blue", "violet" };

    public sealed record SaveLayoutRequest(string FormCode, List<LayoutItemDto> Items);

    public sealed record ResetLayoutRequest(string FormCode);

    [HttpGet("/api/line-card-layout/{formCode}")]
    public async Task<IActionResult> Get(string formCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(formCode) || !FormCodeRegex().IsMatch(formCode.Trim()))
            return Json(new { ok = false, error = "Geçersiz form kodu." });

        var layout = await _repository.GetActiveAsync(formCode.Trim(), ct);
        List<LayoutItemDto>? items = null;
        if (layout is not null)
        {
            // Bozuk JSON düzeni ekranı asla kırmasın — null döner, grid varsayılanla çizer.
            try { items = JsonSerializer.Deserialize<List<LayoutItemDto>>(layout.LayoutJson, StoreJson); }
            catch (JsonException) { items = null; }
        }
        return Json(new { ok = true, formCode = formCode.Trim(), items, canEdit = IsAdmin() });
    }

    /// <summary>
    /// Alan katalogu + mevcut duzen (2026-08-05) — Alan Yönetimi ekranindaki
    /// "Kart Düzeni" editörünün self-load kaynagi. Belge grid config'i olmadan
    /// duzenlenebilir ogelerin tam listesini dondurur:
    ///   sistem kolonlari (SalesController.BuildDocumentLineGridConfig ile SENKRON
    ///   tutulmasi gereken iskelet — yeni layoutlanabilir kolon eklenirse buraya da
    ///   ekle) + ShowOnCard=1 inline-uyumlu widget'lar, kayitli duzenle merge edilmis.
    /// </summary>
    [HttpGet("/api/line-card-layout/{formCode}/fields")]
    public async Task<IActionResult> GetFields(string formCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(formCode) || !FormCodeRegex().IsMatch(formCode.Trim()))
            return Json(new { ok = false, error = "Geçersiz form kodu." });
        formCode = formCode.Trim();

        var docType = Models.Sales.DocumentTypeFormMap.FindDocTypeByLinesFormCode(formCode);
        if (docType is null)
            return Json(new { ok = false, error = "Bu form için kart düzeni desteklenmiyor (yalnızca ticari belge kalemleri)." });

        var hidePricing = string.Equals(docType, "alis_talebi", StringComparison.OrdinalIgnoreCase);

        // Layoutlanabilir sistem kolonlari — tlMirror/aksiyon/row-below kolonlari
        // kart alan izgarasina girmez, burada da yer almaz.
        var catalog = new List<FieldCatalogItem>
        {
            new("materialCode", "Malzeme Kodu", "Hash", true,  false),
            new("materialName", "Malzeme Adi",  "FileText", false, false),
            new("unitId",       "Birim",        "Ruler", false, false),
            new("quantity",     "Miktar",       "Sigma", true,  false),
        };
        if (!hidePricing)
        {
            catalog.Add(new("unitPrice",    "Birim Fiyat",   "DollarSign", false, false));
            catalog.Add(new("discountRate", "Iskonto %",     "Percent",    false, false));
            catalog.Add(new("taxRate",      "KDV %",         "Percent",    false, false));
            catalog.Add(new("lineTotal",    "Satir Toplami", "Calculator", false, false));
        }

        // ShowOnCard widget'lari (inline-uyumlu tipler — CalibraLineItemsGrid ile ayni set)
        var schema = await _widgetService.GetFormSchemaByCodeAsync(formCode, ct);
        if (schema?.Widgets is not null)
        {
            string[] inlineTypes = ["text", "numeric", "date", "dropdown"];
            foreach (var w in schema.Widgets)
            {
                if (!w.ShowOnCard || !w.IsActive) continue;
                if (!inlineTypes.Contains((w.DataType ?? "").Trim().ToLowerInvariant())) continue;
                catalog.Add(new("w_" + w.WidgetCode, w.Label, "Tag", w.IsRequired, true));
            }
        }

        // Kayitli duzenle merge — grid'in cardItems mantigiyla ayni: layout sirasi,
        // bilinmeyen key yok say, eksik kimlik kolonlari basa, digerleri sona.
        var layout = await _repository.GetActiveAsync(formCode, ct);
        List<LayoutItemDto>? saved = null;
        if (layout is not null)
        {
            try { saved = JsonSerializer.Deserialize<List<LayoutItemDto>>(layout.LayoutJson, StoreJson); }
            catch (JsonException) { saved = null; }
        }

        var ordered = new List<object>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        object ToItem(FieldCatalogItem c, LayoutItemDto? li, int defaultSpan) => new
        {
            key = c.Key,
            label = c.Label,
            icon = c.Icon,
            locked = c.Locked,
            isWidget = c.IsWidget,
            span = li is not null && li.Span is >= 1 and <= 24 ? li.Span : defaultSpan,
            visible = c.Locked || li is null || li.Visible,
            labelText = li?.Label ?? "",
            labelSize = li?.LabelSize,
            labelWeight = li?.LabelWeight,
            labelColor = li?.LabelColor,
        };
        if (saved is not null)
        {
            foreach (var li in saved)
            {
                var cat = catalog.FirstOrDefault(c => string.Equals(c.Key, li.Key, StringComparison.OrdinalIgnoreCase));
                if (cat is null || !seen.Add(cat.Key)) continue;
                ordered.Add(ToItem(cat, li, 6));
            }
        }
        // Eksik kimlik kolonlari basa (materialCode once), digerleri sona
        var missingIdentity = catalog
            .Where(c => (c.Key == "materialCode" || c.Key == "materialName") && !seen.Contains(c.Key))
            .ToList();
        for (var i = missingIdentity.Count - 1; i >= 0; i--)
        {
            seen.Add(missingIdentity[i].Key);
            ordered.Insert(0, ToItem(missingIdentity[i], null, 8));
        }
        foreach (var c in catalog)
            if (seen.Add(c.Key))
                ordered.Add(ToItem(c, null, 6));

        return Json(new { ok = true, formCode, canEdit = IsAdmin(), hasCustomLayout = saved is { Count: > 0 }, items = ordered });
    }

    private sealed record FieldCatalogItem(string Key, string Label, string Icon, bool Locked, bool IsWidget);

    [HttpPost("/api/line-card-layout/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromBody] SaveLayoutRequest request, CancellationToken ct)
    {
        if (!IsAdmin()) return Forbid();
        if (request is null || string.IsNullOrWhiteSpace(request.FormCode)
            || !FormCodeRegex().IsMatch(request.FormCode.Trim()))
            return Json(new { ok = false, error = "Geçersiz form kodu." });
        if (request.Items is null || request.Items.Count == 0)
            return Json(new { ok = false, error = "Düzen öğesi listesi boş." });

        var formCode = request.FormCode.Trim();

        // Normalize: key trim + boş/yinelenen key at, span 1-24 clamp, order'ı sıraya
        // bindir; başlık override'ları whitelist/clamp ile temizlenir.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<LayoutItemDto>(request.Items.Count);
        foreach (var it in request.Items.OrderBy(i => i.Order))
        {
            var key = it.Key?.Trim();
            if (string.IsNullOrEmpty(key) || key.Length > 120 || !seen.Add(key)) continue;

            var label = string.IsNullOrWhiteSpace(it.Label) ? null : it.Label.Trim();
            if (label is { Length: > 60 }) label = label[..60];
            int? size = it.LabelSize is >= 9 and <= 15 ? it.LabelSize : null;
            int? weight = it.LabelWeight is 400 or 500 or 600 or 700 ? it.LabelWeight : null;
            var color = !string.IsNullOrWhiteSpace(it.LabelColor) && AllowedLabelColors.Contains(it.LabelColor.Trim())
                ? it.LabelColor.Trim().ToLowerInvariant() : null;

            items.Add(new LayoutItemDto(key, Math.Clamp(it.Span, 1, 24), items.Count, it.Visible,
                label, size, weight, color));
        }
        if (items.Count == 0)
            return Json(new { ok = false, error = "Geçerli düzen öğesi yok." });

        try
        {
            var old = await _repository.GetActiveAsync(formCode, ct);
            var json = JsonSerializer.Serialize(items, StoreJson);
            await _repository.UpsertAsync(new LineCardLayout
            {
                FormCode = formCode,
                LayoutJson = json,
                CreatedById = GetUserId(),
                CreatedBy = User?.Identity?.Name,
                UpdatedById = GetUserId(),
                UpdatedBy = User?.Identity?.Name,
            }, ct);

            if (old is null)
                _audit?.LogInsert("LineCardLayout", formCode, $"Kalem kartı düzeni ({formCode})",
                    detail: $"{items.Count} öğe");
            else
                _audit?.LogUpdate("LineCardLayout", formCode, $"Kalem kartı düzeni ({formCode})",
                    new { Duzen = old.LayoutJson }, new { Duzen = json });

            return Json(new { ok = true, items });
        }
        catch (Exception ex)
        {
            // Kural 2: exception server'a loglanır, istemciye jenerik mesaj döner.
            HttpContext.RequestServices.GetService<ILogger<LineCardLayoutController>>()
                ?.LogError(ex, "Kalem kartı düzeni kaydedilemedi (formCode={FormCode})", formCode);
            return Json(new { ok = false, error = "Düzen kaydedilemedi. Ayrıntılar sunucu loglarında." });
        }
    }

    [HttpPost("/api/line-card-layout/reset")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reset([FromBody] ResetLayoutRequest request, CancellationToken ct)
    {
        if (!IsAdmin()) return Forbid();
        if (request is null || string.IsNullOrWhiteSpace(request.FormCode)
            || !FormCodeRegex().IsMatch(request.FormCode.Trim()))
            return Json(new { ok = false, error = "Geçersiz form kodu." });

        var formCode = request.FormCode.Trim();
        try
        {
            var old = await _repository.GetActiveAsync(formCode, ct);
            await _repository.DeleteAsync(formCode, ct);
            if (old is not null)
                _audit?.LogDelete("LineCardLayout", formCode, $"Kalem kartı düzeni ({formCode})",
                    detail: "Düzen sıfırlandı — varsayılana dönüldü.",
                    snapshot: [new AuditFieldChange("Duzen", "Düzen", old.LayoutJson, null)]);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            HttpContext.RequestServices.GetService<ILogger<LineCardLayoutController>>()
                ?.LogError(ex, "Kalem kartı düzeni sıfırlanamadı (formCode={FormCode})", formCode);
            return Json(new { ok = false, error = "Düzen sıfırlanamadı. Ayrıntılar sunucu loglarında." });
        }
    }

    /// <summary>Düzeni tasarlama yetkisi — Admin (DepartmentManager) veya SystemAdmin.</summary>
    private bool IsAdmin()
    {
        var roleStr = User?.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        return UserAuthorizationCatalog.TryParseRole(roleStr, out var role)
               && role is UserRole.DepartmentManager or UserRole.SystemAdmin;
    }

    private int? GetUserId() =>
        int.TryParse(User?.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : null;
}
