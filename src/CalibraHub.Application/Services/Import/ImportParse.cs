using System.Globalization;

namespace CalibraHub.Application.Services.Import;

/// <summary>İçe aktarım handler'ları için ortak değer ayrıştırma yardımcıları (kültür-duyarlı).</summary>
public static class ImportParse
{
    public static string? Get(IReadOnlyDictionary<string, string?> d, string key) => d.TryGetValue(key, out var v) ? v : null;

    public static string DigitsOnly(string s) => new(s.Where(char.IsDigit).ToArray());

    public static bool ParseBool(string? raw)
    {
        var v = (raw ?? "").Trim().ToLowerInvariant();
        return v is "1" or "true" or "evet" or "e" or "x" or "var" or "yes" or "✓";
    }

    public static decimal? ParseDecimal(string? raw)
    {
        var v = (raw ?? "").Trim();
        if (v.Length == 0) return null;
        v = v.Replace(" ", "");
        if (v.Contains(',') && !v.Contains('.')) v = v.Replace(',', '.');
        else if (v.Contains(',') && v.Contains('.')) v = v.Replace(".", "").Replace(',', '.');
        return decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    public static DateTime? ParseDate(string? raw)
    {
        var v = (raw ?? "").Trim();
        if (v.Length == 0) return null;
        var tr = new CultureInfo("tr-TR");
        if (DateTime.TryParse(v, tr, DateTimeStyles.None, out var d)) return d;
        if (DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return d;
        foreach (var fmt in new[] { "dd.MM.yyyy", "yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy", "d.M.yyyy" })
            if (DateTime.TryParseExact(v, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return d;
        return null;
    }
}
