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
/// Kalem kartı düzeni API — belge kalem kartlarının konum/boyut (GridUnits=48 birim
/// ızgara; v1 24'tü) düzenini yönetir (2026-08-05).
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

    /// <summary>
    /// Izgara cozunurlugu. v1 duzenler 24 birimdi; daha hassas genislik icin 48'e
    /// cikarildi (2026-08-05 kullanici istegi). Eski kayitlar (ciplak dizi JSON)
    /// okunurken span x2 olceklenir; yeni kayitlar {"v":2,"items":[...]} zarfiyla
    /// saklanir — bir sonraki cozunurluk artisinda ayni yol izlenir.
    /// </summary>
    private const int GridUnits = 48;

    private sealed record LayoutEnvelope(int V, List<LayoutItemDto>? Items);

    /// <summary>
    /// LayoutJson → item listesi (surum farkindaligiyla). Bozuk JSON'da null —
    /// ekran asla kirilmaz, varsayilan duzene duser.
    /// </summary>
    private static List<LayoutItemDto>? ParseLayoutItems(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            if (json.TrimStart().StartsWith('['))
            {
                // v1 (24 birim, ciplak dizi) → 48'e olcekle
                var legacy = JsonSerializer.Deserialize<List<LayoutItemDto>>(json, StoreJson);
                return legacy?.Select(it => it with { Span = Math.Clamp(it.Span * 2, 1, GridUnits) }).ToList();
            }
            var env = JsonSerializer.Deserialize<LayoutEnvelope>(json, StoreJson);
            return env?.Items;
        }
        catch (JsonException) { return null; }
    }

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
    /// Tek düzen öğesi. Span 1-48 (GridUnits — v1'de 24'tü, okuma yolunda ölçeklenir).
    /// Label* alanları başlık override'ıdır (2026-08-05 kullanıcı isteği):
    ///   Label       — başlık metni (boş = alanın varsayılan etiketi)
    ///   LabelSize   — px (9-15 arası), null = varsayılan (10)
    ///   LabelWeight — 400/500/600/700, null = varsayılan (bold)
    ///   LabelColor  — semantik token (slate/indigo/emerald/amber/rose/blue/violet),
    ///                 null = varsayılan. HEX asla saklanmaz — tema uyumu.
    ///   LabelStyle  — Alan Yönetimi'ndeki etiket stili sözlüğüyle aynı:
    ///                 "standard" (etiket üstte — varsayılan), "modern" (kutunun
    ///                 üst kenarında yüzer), "inline" (Sade — etiket solda, giriş sağda).
    /// </summary>
    /// <remarks>
    /// Row/Col (v3, 2026-08-06): SERBEST yerleşim. Alanlar artık yalnız sıraya göre
    /// sola/üste yığılmaz; admin bir alanı istediği satıra ve sütuna koyabilir
    /// (aradaki boşluk korunur). Row 1-tabanlı satır, Col 1-tabanlı sütun (1..48).
    /// null = eski davranış (akış/flow) — v1/v2 kayıtları okunurken istemci sıradan
    /// türetir, böylece mevcut düzenlerin görünümü birebir korunur.
    /// </remarks>
    public sealed record LayoutItemDto(
        string Key, int Span, int Order, bool Visible = true,
        string? Label = null, int? LabelSize = null, int? LabelWeight = null, string? LabelColor = null,
        string? LabelStyle = null, int? Row = null, int? Col = null);

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
        var items = layout is not null ? ParseLayoutItems(layout.LayoutJson) : null;
        return Json(new { ok = true, formCode = formCode.Trim(), items, gridUnits = GridUnits, canEdit = IsAdmin() });
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

        // Layoutlanabilir sistem kolonlari — DocumentLineFieldCatalog tek kaynak
        // (Form Davranış Katmanı ile paylaşılır; BuildDocumentLineGridConfig
        // iskeletiyle elle senkron tutulur).
        var lineFields = Models.Sales.DocumentLineFieldCatalog.Resolve(formCode);
        if (lineFields is null)
            return Json(new { ok = false, error = "Bu form için kart düzeni desteklenmiyor (yalnızca ticari belge kalemleri)." });
        var catalog = lineFields.Select(f => new FieldCatalogItem(f.Key, f.Label, f.Icon, f.Locked, false)).ToList();

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
        var saved = layout is not null ? ParseLayoutItems(layout.LayoutJson) : null;

        var ordered = new List<object>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        object ToItem(FieldCatalogItem c, LayoutItemDto? li, int defaultSpan) => new
        {
            key = c.Key,
            label = c.Label,
            icon = c.Icon,
            locked = c.Locked,
            isWidget = c.IsWidget,
            span = li is not null && li.Span >= 1 && li.Span <= GridUnits ? li.Span : defaultSpan,
            visible = c.Locked || li is null || li.Visible,
            labelText = li?.Label ?? "",
            labelSize = li?.LabelSize,
            labelWeight = li?.LabelWeight,
            labelColor = li?.LabelColor,
            labelStyle = li?.LabelStyle,
        };
        if (saved is not null)
        {
            foreach (var li in saved)
            {
                var cat = catalog.FirstOrDefault(c => string.Equals(c.Key, li.Key, StringComparison.OrdinalIgnoreCase));
                if (cat is null || !seen.Add(cat.Key)) continue;
                ordered.Add(ToItem(cat, li, 12));
            }
        }
        // Eksik kimlik kolonlari basa (materialCode once), digerleri sona
        var missingIdentity = catalog
            .Where(c => (c.Key == "materialCode" || c.Key == "materialName") && !seen.Contains(c.Key))
            .ToList();
        for (var i = missingIdentity.Count - 1; i >= 0; i--)
        {
            seen.Add(missingIdentity[i].Key);
            ordered.Insert(0, ToItem(missingIdentity[i], null, 16));
        }
        foreach (var c in catalog)
            if (seen.Add(c.Key))
                ordered.Add(ToItem(c, null, 12));

        return Json(new { ok = true, formCode, gridUnits = GridUnits, canEdit = IsAdmin(), hasCustomLayout = saved is { Count: > 0 }, items = ordered });
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

        // Normalize: key trim + boş/yinelenen key at, span 1-GridUnits clamp, order'ı
        // sıraya bindir; başlık override'ları whitelist/clamp ile temizlenir.
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
            // WidgetMas.LabelStyle whitelist'i ile ayni: modern/inline disi her sey
            // varsayilan (null=standard) kabul edilir.
            var style = it.LabelStyle?.Trim().ToLowerInvariant();
            if (style is not ("modern" or "inline")) style = null;

            // Serbest yerlesim (v3): Row 1..500, Col 1..GridUnits ve col+span izgarayi
            // tasmamali. Gecersiz/eksik degerler null → istemci akis (flow) moduna duser
            // (fail-open; eski kayitlarin gorunumu birebir korunur).
            var span = Math.Clamp(it.Span, 1, GridUnits);
            int? row = it.Row is >= 1 and <= 500 ? it.Row : null;
            int? col = it.Col is >= 1 && it.Col <= GridUnits && (it.Col + span - 1) <= GridUnits ? it.Col : null;
            if (row is null || col is null) { row = null; col = null; }

            items.Add(new LayoutItemDto(key, span, items.Count, it.Visible,
                label, size, weight, color, style, row, col));
        }
        if (items.Count == 0)
            return Json(new { ok = false, error = "Geçerli düzen öğesi yok." });

        try
        {
            var old = await _repository.GetActiveAsync(formCode, ct);
            // v3 zarf — Row/Col (serbest yerlesim) tasiyan surum. Okuma yolu v1 (ciplak
            // dizi, 24 birim) ve v2 (48 birim, akis) ile ayrim yapabilsin.
            var json = JsonSerializer.Serialize(new LayoutEnvelope(3, items), StoreJson);
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
