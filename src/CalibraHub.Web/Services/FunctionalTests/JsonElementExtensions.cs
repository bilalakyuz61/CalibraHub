using System.Text.Json;

namespace CalibraHub.Web.Services.FunctionalTests;

/// <summary>
/// Fonksiyon test altyapısı — JSON yanıtlarını (camelCase/PascalCase karışık gelebilir,
/// endpoint'ten endpoint'e tutarlı değil) case-insensitive okumak için küçük yardımcılar.
/// Gerçek HTTP yanıtları black-box olarak ayrıştırılır; hiçbir controller/DTO tipine
/// derleme zamanı bağımlılık kurulmaz (bkz. FunctionalTestHttpClient XML doc'u).
/// </summary>
public static class JsonElementExtensions
{
    public static bool TryGetPropertyCI(this JsonElement element, string name, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object) return false;
        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }
        return false;
    }

    public static int GetIntCI(this JsonElement element, string name, int fallback = 0)
        => element.TryGetPropertyCI(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var v) ? v : fallback;

    public static int? GetIntOrNullCI(this JsonElement element, string name)
        => element.TryGetPropertyCI(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var v) ? v : null;

    public static decimal GetDecimalCI(this JsonElement element, string name, decimal fallback = 0m)
        => element.TryGetPropertyCI(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDecimal(out var v) ? v : fallback;

    public static string? GetStringCI(this JsonElement element, string name)
        => element.TryGetPropertyCI(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    public static bool? GetBoolCI(this JsonElement element, string name)
        => element.TryGetPropertyCI(name, out var p) && p.ValueKind is JsonValueKind.True or JsonValueKind.False ? p.GetBoolean() : null;

    /// <summary>Kök elemanın kendisi bir dizi ise (çoğu GET listesi) onu döner.</summary>
    public static IReadOnlyList<JsonElement> AsArray(this JsonElement element)
        => element.ValueKind == JsonValueKind.Array ? element.EnumerateArray().ToList() : Array.Empty<JsonElement>();

    public static IReadOnlyList<JsonElement> GetArrayCI(this JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Array)
            return element.EnumerateArray().ToList();
        if (element.TryGetPropertyCI(name, out var p) && p.ValueKind == JsonValueKind.Array)
            return p.EnumerateArray().ToList();
        return Array.Empty<JsonElement>();
    }

    /// <summary>
    /// Endpoint'lerin çoğu <c>{success,message}</c> veya <c>{ok,error}</c> zarfını kullanır
    /// (proje içinde ikisi de yaygın — bkz. CLAUDE.md referans örnekleri). Zarf hiç yoksa
    /// (düz liste/nesne dönen GET uçları) "başarılı" varsayılır.
    /// </summary>
    public static bool IsBusinessOk(this JsonElement json, out string? message)
    {
        message = null;
        bool? ok = null;
        if (json.TryGetPropertyCI("success", out var s) && s.ValueKind is JsonValueKind.True or JsonValueKind.False)
            ok = s.GetBoolean();
        else if (json.TryGetPropertyCI("ok", out var o) && o.ValueKind is JsonValueKind.True or JsonValueKind.False)
            ok = o.GetBoolean();

        if (ok == false)
            message = json.GetStringCI("message") ?? json.GetStringCI("error");

        return ok ?? true;
    }
}
