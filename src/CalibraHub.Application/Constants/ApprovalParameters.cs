namespace CalibraHub.Application.Constants;

/// <summary>
/// Belge türü bazında onay akışı parametre anahtarları.
/// CompanyParameter tablosunda formCode=APPROVAL altında saklanır.
///
/// Anahtar formatı: APPROVAL_ENABLED_{Kind} — Kind, DocumentEntityTypes.Definitions'taki
/// spesifik entity kodudur (SalesQuote, PurchaseOrder, ...).
/// Parametre tanımsızsa AÇIK kabul edilir (geriye uyum: parametre yokken de
/// otomatik onay tetikleme çalışmaya devam eder).
/// </summary>
public static class ApprovalParameters
{
    public const string FormCode = "APPROVAL";

    /// <summary>
    /// Onay sistemi bu belge türünde KULLANILIYOR mu (ana anahtar). Kapalıysa o belge
    /// türünde onay akışı hiç devreye girmez — otomatik başlatma da yapılmaz.
    /// Tanımsızsa AÇIK kabul edilir (geriye uyum).
    /// </summary>
    public static string UseKey(string kind) => $"APPROVAL_USE_{kind}";

    /// <summary>
    /// Kayıt sırasında onay akışı OTOMATİK başlatılsın mı. Yalnızca <see cref="UseKey"/>
    /// açıkken anlamlıdır. Tanımsızsa AÇIK kabul edilir.
    /// </summary>
    public static string EnabledKey(string kind) => $"APPROVAL_ENABLED_{kind}";
}
