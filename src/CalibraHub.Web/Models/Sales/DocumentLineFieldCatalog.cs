namespace CalibraHub.Web.Models.Sales;

/// <summary>
/// Belge KALEM (satır) sabit kolon kataloğu — Form Davranış Katmanı + Kart Düzeni
/// ortak kaynağı (2026-08-05). SalesController.BuildDocumentLineGridConfig kolon
/// iskeletiyle ELLE SENKRON tutulmalıdır (yeni layoutlanabilir kolon eklenirse
/// buraya da ekle). tlMirror / aksiyon (kombinasyon, seri) / row-below (Not)
/// kolonları kart alan ızgarasına girmediği için burada yer almaz.
/// </summary>
public static class DocumentLineFieldCatalog
{
    /// <param name="DataType">Satır-scope kural tip ipucu: text/numeric</param>
    public sealed record LineField(string Key, string Label, string Icon, bool Locked, string DataType);

    private static readonly IReadOnlyList<LineField> Base =
    [
        new("materialCode", "Malzeme Kodu", "Hash",     true,  "text"),
        new("materialName", "Malzeme Adi",  "FileText", false, "text"),
        new("unitId",       "Birim",        "Ruler",    false, "text"),
        new("quantity",     "Miktar",       "Sigma",    true,  "numeric"),
    ];

    private static readonly IReadOnlyList<LineField> Pricing =
    [
        new("unitPrice",    "Birim Fiyat",   "DollarSign", false, "numeric"),
        // Ikon YOK ("") — baslikta zaten "%" var (2026-08-06 kullanici istegi).
        new("discountRate", "İskonto %",     "",           false, "numeric"),
        new("taxRate",      "KDV %",         "",           false, "numeric"),
        new("lineTotal",    "Satir Toplami", "Calculator", false, "numeric"),
    ];

    /// <summary>
    /// Kalem form kodundan kolon listesi; ticari belge kalemi değilse null.
    /// hidePricing: alis_talebi (İhtiyaç Kaydı) fiyat kolonlarını hiç taşımaz.
    /// </summary>
    public static IReadOnlyList<LineField>? Resolve(string? linesFormCode)
    {
        var docType = DocumentTypeFormMap.FindDocTypeByLinesFormCode(linesFormCode);
        if (docType is null) return null;
        var hidePricing = string.Equals(docType, "alis_talebi", StringComparison.OrdinalIgnoreCase);
        return hidePricing ? Base : [.. Base, .. Pricing];
    }
}
