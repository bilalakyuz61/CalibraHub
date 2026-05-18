namespace CalibraHub.Domain.Enums;

/// <summary>
/// Bir Integration'in hata aldiginda nasil davranacagi. Wizard Step 5'te seclir.
/// DB'de NVARCHAR(20).
/// </summary>
public enum IntegrationErrorBehavior
{
    /// <summary>Hata loglanir, sonraki kayda gecilir. Tek seferlik hata icin uygun.</summary>
    Skip = 0,

    /// <summary>Retry N kez (1dk, 5dk, 15dk gibi exponential backoff). RetryCount kolonu ile.</summary>
    Retry = 1,

    /// <summary>Manuel inceleme kuyruguna at. Admin elle gozden gecirip yeniden tetikler.</summary>
    Manual = 2,
}
