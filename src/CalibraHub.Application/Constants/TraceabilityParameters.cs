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
}
