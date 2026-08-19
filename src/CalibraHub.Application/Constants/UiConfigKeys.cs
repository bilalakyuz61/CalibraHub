namespace CalibraHub.Application.Constants;

/// <summary>
/// Kullanıcı bazlı UI tercih anahtarları (IUserSettingRepository / UserSettings tablosu
/// üzerinden saklanır — ayrı şema/tablo GEREKMEZ). 2026-08-19: belge kalem listesi
/// görünüm modu (KART/DATAGRID) eklendi.
/// </summary>
public static class UiConfigKeys
{
    /// <summary>Belge kalem listesi görünüm modu — değer "card" | "grid".</summary>
    public const string LineGridViewMode = "line_grid_view_mode";

    public const string ViewModeCard = "card";
    public const string ViewModeGrid = "grid";

    /// <summary>Geçersiz/eksik değeri her zaman "card"a düşürür (fail-open, mevcut davranış).</summary>
    public static string NormalizeViewMode(string? raw) =>
        string.Equals(raw, ViewModeGrid, StringComparison.OrdinalIgnoreCase) ? ViewModeGrid : ViewModeCard;

    public static bool IsValidViewMode(string? raw) =>
        string.Equals(raw, ViewModeCard, StringComparison.OrdinalIgnoreCase)
        || string.Equals(raw, ViewModeGrid, StringComparison.OrdinalIgnoreCase);
}
