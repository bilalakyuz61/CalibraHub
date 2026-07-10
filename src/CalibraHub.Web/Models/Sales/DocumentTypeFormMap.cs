namespace CalibraHub.Web.Models.Sales;

/// <summary>
/// document_types.code → Forms.FormCode esleme (Header/New/Lines).
///
/// dbo.Document tablosu butun ticari belgeleri (teklif/siparis/talep) DocumentTypeId
/// ile ayrir. Edit/New ekranlari Forms tablosundan UI metadata cektikleri icin
/// DocumentTypeCode → FormCode haritasinin tek noktada tanimlanmasi gerekir.
///
/// Yeni bir belge tipi eklenince:
///   1) document_types tablosuna seed (Code, Name)
///   2) Forms tablosuna 4 form (List/New/Edit/Lines) + BaseTable/BaseTableFilter
///   3) Buraya yeni entry — controller ve view bunu tek kaynaktan okur.
/// </summary>
public static class DocumentTypeFormMap
{
    // Parent: izin kontrolünde kullanılan liste/parent form kodu (PURCHASE_REQUEST, SALES_QUOTE vb.)
    public sealed record FormCodes(string Header, string HeaderNew, string Lines, string ListUrl, string Parent);

    private static readonly Dictionary<string, FormCodes> _map =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // ── Satis tarafi ──
            ["satis_teklifi"]  = new("SALES_QUOTE_EDIT",      "SALES_QUOTE_NEW",      "SALES_QUOTE_LINES",      "/Sales/Quotes",              "SALES_QUOTE"),
            ["satis_siparisi"] = new("SALES_ORDER_EDIT",      "SALES_ORDER_NEW",      "SALES_ORDER_LINES",      "/Sales/Orders",              "SALES_ORDER"),
            ["satis_irsaliyesi"] = new("SALES_DELIVERY_EDIT", "SALES_DELIVERY_NEW",   "SALES_DELIVERY_LINES",   "/Sales/Deliveries",          "SALES_DELIVERY"),
            // ── Satin Alma tarafi (2026-05-23) ──
            ["alis_talebi"]        = new("PURCHASE_REQUEST_EDIT", "PURCHASE_REQUEST_NEW", "PURCHASE_REQUEST_LINES", "/Purchase/Requests",         "PURCHASE_REQUEST"),
            ["alis_teklifi"]       = new("PURCHASE_QUOTE_EDIT",   "PURCHASE_QUOTE_NEW",   "PURCHASE_QUOTE_LINES",   "/Purchase/Quotes",           "PURCHASE_QUOTE"),
            ["alis_siparisi"]      = new("PURCHASE_ORDER_EDIT",   "PURCHASE_ORDER_NEW",   "PURCHASE_ORDER_LINES",   "/Purchase/Orders",           "PURCHASE_ORDER"),
            ["satin_alma_talebi"]  = new("PURCHASE_DEMAND_EDIT",  "PURCHASE_DEMAND_NEW",  "PURCHASE_DEMAND_LINES",  "/Purchase/PurchaseDemands",  "PURCHASE_DEMAND"),
            ["alis_irsaliyesi"]    = new("PURCHASE_DELIVERY_EDIT","PURCHASE_DELIVERY_NEW","PURCHASE_DELIVERY_LINES","/Purchase/Deliveries",       "PURCHASE_DELIVERY"),
        };

    /// <summary>
    /// Belge tipi kodundan FormCodes seti doner. Bilinmeyen tip icin satis_teklifi
    /// defaults (geriye uyum) — yeni belge tipi henuz haritalanmadiysa hata yerine
    /// makul bir fallback verir.
    /// </summary>
    public static FormCodes Resolve(string? documentTypeCode)
    {
        var code = (documentTypeCode ?? "").Trim();
        if (string.IsNullOrWhiteSpace(code) || !_map.TryGetValue(code, out var fc))
            return _map["satis_teklifi"];
        return fc;
    }

    /// <summary>true ise belge satin alma tarafindan (alis_*).</summary>
    public static bool IsPurchase(string? documentTypeCode)
        => (documentTypeCode ?? "").StartsWith("alis_", StringComparison.OrdinalIgnoreCase);
}
