namespace CalibraHub.Application.Constants;

/// <summary>
/// Üretim modülü şirket parametresi anahtarları (formCode = PRODUCTION).
/// Admin → Parametreler → Üretim sekmesinden yönetilir; runtime tüketiciler
/// ICompanyParameterService.ListAsync(FormCode) üzerinden okur.
/// </summary>
public static class ProductionParameters
{
    public const string FormCode = "PRODUCTION";

    /// <summary>
    /// "Reçetede aynı bileşen birden fazla satırda kullanılabilsin" (Bool, default false).
    /// false (varsayılan): UI aynı (kod+kombinasyon) eklemede miktarları birleştirir,
    /// domain (BOM.AddLine/EnsureValid) mükerrer satırı reddeder — mevcut davranış.
    /// true: aynı bileşen ayrı satırlarda tutulabilir (farklı fire/açıklama için);
    /// ExplodeBOM/maliyet toplama GroupBy ile zaten mükerrer-güvenli, iş emri
    /// patlatması satır başına ayrı WorkOrderComponent üretir (unique kısıt yok).
    /// </summary>
    public const string BomAllowDuplicateComponentsKey = "BOM_ALLOW_DUPLICATE_COMPONENTS";

    /// <summary>
    /// "Üretimi başlamış iş emrinin reçetesi (bileşen listesi) düzeltilebilsin" (Bool,
    /// default false — 2026-08-06 kullanıcı isteği). false: iş emri InProgress olduktan
    /// sonra bileşen ekleme/çıkarma/miktar değişikliği ve yeniden patlatma REDDEDİLİR.
    /// true: düzenleme serbest — ANCAK sarf yapılmış (IssuedQuantity&gt;0) satırın
    /// silinmesi/sarf altına düşürülmesi ve sarflı emirde yeniden patlatma parametreden
    /// BAĞIMSIZ olarak her zaman engellidir (mutabakat korunur).
    /// </summary>
    public const string AllowStartedWoRecipeEditKey = "ALLOW_STARTED_WO_RECIPE_EDIT";
}
