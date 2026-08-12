namespace CalibraHub.Application.Constants;

/// <summary>
/// İzlenebilirlik (lot/seri) şirket parametreleri (formCode = TRACEABILITY).
/// Admin → Parametreler → İzlenebilirlik sekmesinden yönetilir. Seri/lot davranış
/// kontrolleri buradan; item bazlı TrackingType ('Serial'/'Lot') ayrı (Malzeme Kartı).
/// </summary>
public static class TraceabilityParameters
{
    public const string FormCode = "TRACEABILITY";

    /// <summary>
    /// Seri numarası benzersizlik kapsamı:
    ///   "Item"   (varsayılan) → seri no yalnızca aynı malzeme içinde benzersiz (ItemId+SerialNo).
    ///   "Global" → seri no barkod gibi TÜM malzemeler arasında benzersiz; bir seri bir kez
    ///              kullanıldıysa başka bir stok kartında kullanılamaz.
    /// Boş/tanımsız → "Item" (geriye uyum).
    /// </summary>
    public const string SerialUniqueScopeKey = "SERIAL_UNIQUE_SCOPE";

    public const string SerialUniqueScopeItem   = "Item";
    public const string SerialUniqueScopeGlobal = "Global";

    /// <summary>
    /// İzlenebilirlik özelliği şirket genelinde açık mı? (PageComment Seq 1099, 2026-08-12)
    /// "true"/"false" string; tanımsız/boş → <c>true</c> kabul edilir (geriye dönük uyum —
    /// mevcut lot/seri takipli malzemeler etkilenmesin). Kapalıyken Malzeme Kartı'nda
    /// izlenebilirlik seçimi (Lot/Seri) değiştirilemez; sunucu tarafında da aynı kapı
    /// uygulanır (bkz. MaterialController.SaveMaterialCardJson). Admin UI switch'i
    /// Parametreler → Stok sekmesine eklenecek (Seq 1100 kapsamında, ayrı iş).
    /// </summary>
    public const string EnabledKey = "ENABLED";
}
