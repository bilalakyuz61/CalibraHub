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
    private readonly IAuditTrailService? _audit;

    public LineCardLayoutController(ILineCardLayoutRepository repository, IAuditTrailService? audit = null)
    {
        _repository = repository;
        _audit = audit;
    }

    /// <summary>Tek düzen öğesi. Span 1-24 (WidgetMas.ColSpan ızgarası ile aynı).</summary>
    public sealed record LayoutItemDto(string Key, int Span, int Order, bool Visible = true);

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

        // Normalize: key trim + boş/yinelenen key at, span 1-24 clamp, order'ı sıraya bindir.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<LayoutItemDto>(request.Items.Count);
        foreach (var it in request.Items.OrderBy(i => i.Order))
        {
            var key = it.Key?.Trim();
            if (string.IsNullOrEmpty(key) || key.Length > 120 || !seen.Add(key)) continue;
            items.Add(new LayoutItemDto(key, Math.Clamp(it.Span, 1, 24), items.Count, it.Visible));
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
