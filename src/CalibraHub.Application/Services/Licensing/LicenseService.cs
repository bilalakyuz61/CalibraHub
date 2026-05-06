using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Services.Licensing;

/// <summary>
/// Lisans servisi. Security Key (paylasilan sir) <b>vendor tarafinda hardcoded</b> bir sabittir
/// — musteri UI'sinde gorunmez ve degistirilemez. Lisans ureticisi (vendor uygulamasi) ile
/// CalibraHub ayni sabiti kullanmak zorundadir; aksi halde validation basarisiz olur.
/// Lisans key icindeki payload: son kullanma tarihi + concurrent limit + toplam kullanici sayisi.
/// </summary>
public sealed class LicenseService : ILicenseService
{
    /// <summary>
    /// Vendor + product paylasilan siri. Kesinlikle UI'dan/configten okunmaz —
    /// derleme zamani sabit. Lisans ureticisinde de AYNEN ayni deger kullanilmali.
    /// </summary>
    private const string LicenseSecret = "Calibra-LicenceKey-2026!.,";

    private readonly ILicenseRepository _repo;
    private readonly IMachineIdProvider _machineIdProvider;

    public LicenseService(
        ILicenseRepository repo,
        IMachineIdProvider machineIdProvider)
    {
        _repo              = repo;
        _machineIdProvider = machineIdProvider;
    }

    public Task<LicenseRecord?> GetCurrentAsync(CancellationToken cancellationToken)
        => _repo.GetAsync(cancellationToken);

    /// <summary>
    /// Lisans anahtarini hardcoded paylasilan sir ile cozer ve dogrular.
    /// <paramref name="securityKey"/> parametresi geriye donuk uyumluluk icin korunuyor
    /// fakat <b>kullanilmaz</b> — secret her zaman <see cref="LicenseSecret"/>'tir.
    /// </summary>
    public async Task<LicenseSaveResult> SaveAsync(string licenseKey, string? securityKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            var empty = new LicenseRecord
            {
                LicenseKey      = null,
                IsValid         = false,
                LastError       = "Bos lisans key",
                LastValidatedAt = DateTime.UtcNow,
            };
            await _repo.SaveAsync(empty, cancellationToken);
            return new LicenseSaveResult(false, "Bos lisans key", empty);
        }

        var machineId = _machineIdProvider.GetMachineId();
        var ok = LicenseValidator.IsMachineLicensed(
            licenseKey.Trim(), machineId, LicenseSecret,
            out var expiry, out var concurrent, out var total, out var error);

        var record = new LicenseRecord
        {
            LicenseKey      = licenseKey.Trim(),
            SecretEncrypted = null, // hardcoded secret kullanildigi icin DB'ye yazmiyoruz
            IsValid         = ok,
            ExpiryDate      = expiry,
            ConcurrentLimit = concurrent,
            TotalUserLimit  = total,
            LastError       = error,
            LastValidatedAt = DateTime.UtcNow,
        };
        await _repo.SaveAsync(record, cancellationToken);
        return new LicenseSaveResult(ok, ok ? "Lisans aktive edildi." : error, record);
    }

    public async Task<LicenseRecord> RevalidateAsync(CancellationToken cancellationToken)
    {
        var existing = await _repo.GetAsync(cancellationToken);
        if (existing?.LicenseKey is null)
            return existing ?? new LicenseRecord { LastError = "Lisans bulunamadi" };

        var machineId = _machineIdProvider.GetMachineId();
        var ok = LicenseValidator.IsMachineLicensed(
            existing.LicenseKey, machineId, LicenseSecret,
            out var expiry, out var concurrent, out var total, out var error);

        existing.IsValid         = ok;
        existing.ExpiryDate      = expiry;
        existing.ConcurrentLimit = concurrent;
        existing.TotalUserLimit  = total;
        existing.LastError       = error;
        existing.LastValidatedAt = DateTime.UtcNow;
        await _repo.SaveAsync(existing, cancellationToken);
        return existing;
    }
}
