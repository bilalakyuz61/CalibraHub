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

    /// <summary>
    /// Açık workspace sekmeleri (PageComment Seq 1123, 2026-08-27) — JSON dizi,
    /// Shell.jsx'teki `tabs` state'inin sunucu yansıması. UserId bazlı saklanır;
    /// per-company DB + per-company kullanıcı kaydı olduğundan (aynı e-posta farklı
    /// şirkette farklı userId üretir) bu zaten "kullanıcı ve şirket bazında" ayrımı
    /// sağlar — ayrı bir CompanyId kolonuna gerek YOK.
    /// </summary>
    public const string WorkspaceTabs = "workspace_tabs";

    public const string ViewModeCard = "card";
    public const string ViewModeGrid = "grid";

    /// <summary>Geçersiz/eksik değeri her zaman "card"a düşürür (fail-open, mevcut davranış).</summary>
    public static string NormalizeViewMode(string? raw) =>
        string.Equals(raw, ViewModeGrid, StringComparison.OrdinalIgnoreCase) ? ViewModeGrid : ViewModeCard;

    public static bool IsValidViewMode(string? raw) =>
        string.Equals(raw, ViewModeCard, StringComparison.OrdinalIgnoreCase)
        || string.Equals(raw, ViewModeGrid, StringComparison.OrdinalIgnoreCase);
}
