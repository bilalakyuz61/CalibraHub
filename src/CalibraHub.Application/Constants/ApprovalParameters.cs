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

    public static string EnabledKey(string kind) => $"APPROVAL_ENABLED_{kind}";
}
