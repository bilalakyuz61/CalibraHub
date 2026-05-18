namespace CalibraHub.Domain.Enums;

/// <summary>
/// IntegrationRun audit kayitinin sonuc durumu. DB'de NVARCHAR(20).
/// </summary>
public enum IntegrationRunStatus
{
    /// <summary>HTTP 2xx, basarili.</summary>
    Success = 0,

    /// <summary>HTTP 4xx/5xx veya network/parsing hatasi.</summary>
    Failed = 1,

    /// <summary>SafetyChecker / quiet hours / validation failure ile gondermeden atlandi.</summary>
    Skipped = 2,

    /// <summary>Hata aldi, retry kuyrugunda. RetryAttempt > 0 ile birlikte gorulur.</summary>
    Retrying = 3,
}
