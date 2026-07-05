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
}
