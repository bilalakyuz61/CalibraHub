namespace CalibraHub.Application.Constants;

/// <summary>
/// İşlem log modülü (audit trail) şirket parametreleri.
/// SECURITY form kodu altında saklanır → Admin → Parametreler → Güvenlik sekmesi.
/// </summary>
public static class AuditParameters
{
    /// <summary>Parametrelerin saklandığı form kodu (Güvenlik sekmesiyle ortak).</summary>
    public const string FormCode = SecurityParameters.FormCode; // "SECURITY"

    /// <summary>
    /// Log dosyası saklama süresi (gün). Süresi dolan günlük dosyalar background
    /// temizlikte silinir. 0 = süresiz sakla. Tanımsız → <see cref="DefaultRetentionDays"/>.
    /// </summary>
    public const string RetentionDaysKey = "AUDIT_RETENTION_DAYS";

    public const int DefaultRetentionDays = 365;
}
