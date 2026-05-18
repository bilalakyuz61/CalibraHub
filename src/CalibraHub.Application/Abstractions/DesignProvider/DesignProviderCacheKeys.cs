namespace CalibraHub.Application.Abstractions.DesignProvider;

/// <summary>
/// IDesignProvider için cache anahtarları — tek noktadan format kontrolü.
/// DocType case-insensitive (her zaman lowercase'e indirgenir) ve trim'lenir.
/// </summary>
public static class DesignProviderCacheKeys
{
    /// <summary>DocLayoutRule listesi anahtarı: <c>layout_rules_{docType}</c>.</summary>
    public static string Rules(string docType)
        => $"layout_rules_{Normalize(docType)}";

    /// <summary>DocLayout fallback anahtarı: <c>layout_default_{docType}</c>.</summary>
    public static string Default(string docType)
        => $"layout_default_{Normalize(docType)}";

    private static string Normalize(string? docType)
        => (docType ?? string.Empty).Trim().ToLowerInvariant();
}
