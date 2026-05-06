namespace CalibraHub.Domain.Entities;

/// <summary>
/// Sistem lisans kaydi — tek row (id=1) olarak saklanir. Yeni lisans girilirse
/// row update edilir; versiyon geri izlenebilirligi icin previous_license_key
/// alani history benzeri (sadece son bir onceki) saklanir.
/// </summary>
public sealed class LicenseRecord
{
    public int Id { get; init; } = 1;

    /// <summary>Aktif lisans key (base64url encoded).</summary>
    public string? LicenseKey { get; set; }

    /// <summary>
    /// Lisans uretim/dogrulama icin paylasilan secret. Vendor'in kullandigi "Security Key"
    /// ile ayni olmali (Cafebra/Calibra/Cafe gibi urun bazli secret'lerden biri).
    /// DB'de DPAPI ile sifreli saklanir; runtime'da decrypt edilir. NULL ise appsettings
    /// fallback'e duser (LicenseSettings:Secret).
    /// </summary>
    public string? SecretEncrypted { get; set; }

    /// <summary>Son dogrulama sonucu — cache icin. IsValid=false ise error mesaji LastError'de.</summary>
    public bool IsValid { get; set; }

    public DateTime? ExpiryDate { get; set; }
    public int? ConcurrentLimit { get; set; }
    public int? TotalUserLimit { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastValidatedAt { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
