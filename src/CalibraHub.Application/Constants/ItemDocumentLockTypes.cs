namespace CalibraHub.Application.Constants;

/// <summary>
/// Malzeme Belge Kilitleri (ItemDocumentLock.DocType) için tek kaynak: kod + TR etiket.
/// Kilitli bir malzeme, ilgili belge tipinin malzeme rehberinde/gridinde seçilemez
/// (bkz. SalesController.GetMaterials, WarehouseController.GetMaterialsJson — enforcement,
/// bu görev kapsamında değiştirilmedi).
///
/// Kod listesi <c>dbo.DocumentType</c> seed'i (CalibraDatabaseInitializer.DefaultDocumentTypes)
/// ve <c>DocumentTypeFormMap</c> ile doğrulanmıştır (2026-07-25). Etiketler güncel menü/Forms
/// adlandırmasıyla eşleşir (SeedFormsAsync'teki FormName/SubModule) — bazı DocumentType.Name
/// seed metinleri tarihseldir (ör. "alis_talebi" DB'de "Ihtiyac Kaydi" iken bu kataloqda
/// "İhtiyaç Kaydı" kullanılır, çünkü PURCHASE_REQUEST formu ve menüde geçerli isim budur).
///
/// UYARI — senkron borcu: <c>Views/Logistics/MaterialCardEdit.cshtml</c> (satır ~4808,
/// <c>_mceDocLockTypes</c>) yalnızca 9 kodluk AYRI/eski bir JS kopyası taşır ve bazı
/// etiketleri farklıdır (ör. "alis_talebi" → "Alış Talebi"). Bu dosya o ekranı otomatik
/// güncellemez (CLAUDE.md kısıtı: MaterialCardEdit.cshtml bu görev kapsamında değiştirilmedi) —
/// frontend/özet ajanı MaterialCardEdit'i bu kataloğa göre senkronize etmeli.
/// </summary>
public static class ItemDocumentLockTypes
{
    public sealed record LockType(string Code, string Label, int SortOrder);

    public static readonly IReadOnlyList<LockType> All = new List<LockType>
    {
        new("satis_teklifi",     "Satış Teklifi",           10),
        new("satis_siparisi",    "Satış Siparişi",          20),
        new("satis_irsaliyesi",  "Satış İrsaliyesi",        30),
        new("alis_talebi",       "İhtiyaç Kaydı",           40),
        new("alis_teklifi",      "Alış Teklifi",            50),
        new("alis_siparisi",     "Alış Siparişi",           60),
        new("satin_alma_talebi", "Satın Alma Talebi",       70),
        new("alis_irsaliyesi",   "Alış İrsaliyesi",         80),
        new("depo_giris",        "Ambar Giriş",             90),
        new("depo_cikis",        "Ambar Çıkış",            100),
        new("depo_transfer",     "Depolar Arası Transfer", 110),
        new("sayim",             "Sayım",                  120),
    }.AsReadOnly();

    private static readonly IReadOnlyDictionary<string, string> _labelByCode =
        All.ToDictionary(x => x.Code, x => x.Label, StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlySet<string> _validCodes =
        All.Select(x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Bilinmeyen kod için kodun kendisini döner (fallback — hiç etiketsiz kalmasın).</summary>
    public static string LabelFor(string? code) =>
        !string.IsNullOrWhiteSpace(code) && _labelByCode.TryGetValue(code, out var label) ? label : (code ?? "");

    public static bool IsValidCode(string? code) =>
        !string.IsNullOrWhiteSpace(code) && _validCodes.Contains(code);

    /// <summary>Girdi listesini bilinen kodlara indirger (geçersiz/boş/tekrar kodlar elenir).</summary>
    public static IReadOnlyCollection<string> FilterValid(IEnumerable<string?>? codes)
    {
        if (codes is null) return Array.Empty<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var c in codes)
        {
            var trimmed = c?.Trim();
            if (string.IsNullOrEmpty(trimmed) || !_validCodes.Contains(trimmed)) continue;
            if (seen.Add(trimmed)) result.Add(trimmed);
        }
        return result;
    }
}
