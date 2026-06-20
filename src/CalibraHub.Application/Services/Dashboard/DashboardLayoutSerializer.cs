using System.Text.Json;
using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Services.Dashboard;

/// <summary>
/// Pano layout JSON (serialize / parse) yardımcısı.
///
/// Saklanan JSON şeması (user_settings."dashboard_layout"):
/// <code>
/// Version 2 (güncel):
/// {
///   "version": 2,
///   "pages": [
///     { "id": "page-genel", "label": "Genel",
///       "widgets": [ { "type": "welcome-card", "size": "lg", "settings": {}, "height": 1 } ] }
///   ]
/// }
///
/// Version 1 (geriye dönük uyumluluk — tek sayfa olarak migrate edilir):
/// {
///   "version": 1,
///   "widgets": [ { "type": "welcome-card", "size": "lg", "settings": {} } ]
/// }
/// </code>
///
/// Bozuk / null / bilinmeyen versiyon → null döner (caller varsayılan sayfalara düşer).
/// </summary>
public static class DashboardLayoutSerializer
{
    /// <summary>Geçerli şema versiyonu — kırıcı değişiklikte artır + read'de migrate et.</summary>
    public const int CurrentVersion = 2;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Saklı JSON'u sayfa listesine çevirir. Geçersiz/boş → null.
    /// Version 1 (tek widget dizisi) otomatik olarak tek "Genel" sayfaya migrate edilir.
    /// </summary>
    public static IReadOnlyList<DashboardPageDto>? TryParsePages(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var version = root.TryGetProperty("version", out var vEl) && vEl.TryGetInt32(out var v) ? v : 0;
            if (version > CurrentVersion) return null; // gelecekten gelen şema — güvenle düş

            // ── Version 1 migration: eski widget dizisi → tek "Genel" sayfa ──
            if (version == 1)
            {
                if (!root.TryGetProperty("widgets", out var wEl) || wEl.ValueKind != JsonValueKind.Array)
                    return null;
                var migrated = ParseWidgets(wEl);
                if (migrated is null || migrated.Count == 0) return null;
                return new[] { new DashboardPageDto("page-genel", "Genel", migrated) };
            }

            // ── Version 2: pages format ──
            if (!root.TryGetProperty("pages", out var pagesEl) || pagesEl.ValueKind != JsonValueKind.Array)
                return null;

            var pages = new List<DashboardPageDto>();
            foreach (var pageEl in pagesEl.EnumerateArray())
            {
                if (pageEl.ValueKind != JsonValueKind.Object) continue;

                var id = pageEl.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                    ? idEl.GetString() : null;
                var label = pageEl.TryGetProperty("label", out var lEl) && lEl.ValueKind == JsonValueKind.String
                    ? lEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(label)) continue;

                var widgets = pageEl.TryGetProperty("widgets", out var wEl2) && wEl2.ValueKind == JsonValueKind.Array
                    ? (ParseWidgets(wEl2) ?? (IReadOnlyList<DashboardWidgetInstanceDto>)Array.Empty<DashboardWidgetInstanceDto>())
                    : Array.Empty<DashboardWidgetInstanceDto>();

                pages.Add(new DashboardPageDto(id!, label!, widgets));
            }

            return pages.Count > 0 ? pages : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Sayfa listesini saklanacak JSON metnine çevirir (version dahil).</summary>
    public static string SerializePages(IReadOnlyList<DashboardPageDto> pages)
    {
        var payload = new
        {
            version = CurrentVersion,
            pages = pages.Select(p => new
            {
                id = p.Id,
                label = p.Label,
                widgets = p.Widgets.Select(w => new
                {
                    type = w.Type,
                    size = NormalizeSize(w.Size),
                    height = w.Height,
                    settings = w.Settings,
                    layout = w.Layout,
                }),
            }),
        };
        return JsonSerializer.Serialize(payload, Options);
    }

    // ── JSON element'ten widget listesi okuma (paylaşılan yardımcı) ──
    private static IReadOnlyList<DashboardWidgetInstanceDto>? ParseWidgets(JsonElement widgetsEl)
    {
        var result = new List<DashboardWidgetInstanceDto>();
        foreach (var item in widgetsEl.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;

            var type = item.TryGetProperty("type", out var tEl) && tEl.ValueKind == JsonValueKind.String
                ? tEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(type)) continue;

            var size = item.TryGetProperty("size", out var sEl) && sEl.ValueKind == JsonValueKind.String
                ? NormalizeSize(sEl.GetString()) : "md";

            JsonElement? settings = null;
            if (item.TryGetProperty("settings", out var settingsEl) &&
                settingsEl.ValueKind != JsonValueKind.Null &&
                settingsEl.ValueKind != JsonValueKind.Undefined)
            {
                settings = settingsEl.Clone();
            }

            int? height = null;
            if (item.TryGetProperty("height", out var hEl) && hEl.TryGetInt32(out var h) && h >= 1 && h <= 3)
                height = h;

            JsonElement? layout = null;
            if (item.TryGetProperty("layout", out var layoutEl) &&
                layoutEl.ValueKind == JsonValueKind.Object)
            {
                layout = layoutEl.Clone();
            }

            result.Add(new DashboardWidgetInstanceDto(type!, size, settings, height, layout));
        }

        return result.Count > 0 ? result : null;
    }

    private static string NormalizeSize(string? size) => size?.Trim().ToLowerInvariant() switch
    {
        "sm" => "sm",
        "lg" => "lg",
        _ => "md",
    };
}
