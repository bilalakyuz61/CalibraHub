using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Services;

public interface ILicenseService
{
    /// <summary>Mevcut lisans kaydini okur (DB).</summary>
    Task<LicenseRecord?> GetCurrentAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Lisans key'i ve (opsiyonel) security key'i dogrular ve DB'ye kaydeder.
    /// security key bos gelirse mevcut (DB'deki encrypted secret veya config fallback) kullanilir.
    /// </summary>
    Task<LicenseSaveResult> SaveAsync(string licenseKey, string? securityKey, CancellationToken cancellationToken);

    /// <summary>Mevcut kayitli lisansi yeniden dogrular (son cache gecerli degilse kullanilir).</summary>
    Task<LicenseRecord> RevalidateAsync(CancellationToken cancellationToken);
}

public sealed record LicenseSaveResult(bool Success, string? Message, LicenseRecord Record);
