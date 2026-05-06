namespace CalibraHub.Application.Abstractions.Services;

/// <summary>Gate sifre dogrulama + degistirme servisi.</summary>
public interface IGatePasswordService
{
    /// <summary>Plaintext sifreyi DB'deki hash ile karsilastir. Tek-kullanici, tek-sifre modeli.</summary>
    Task<bool> VerifyAsync(string plaintextPassword, CancellationToken cancellationToken);

    /// <summary>
    /// Sifre degistir. Mevcut sifre dogru olmali; yeni sifre gucluluk kurallarina uymali.
    /// </summary>
    Task<GatePasswordChangeResult> ChangeAsync(
        string currentPassword,
        string newPassword,
        string? clientIp,
        CancellationToken cancellationToken);

    /// <summary>
    /// İlk-kurulum kontrolu — DB'de hic kayit yoksa appsettings'ten/random-uretilmis
    /// varsayilan sifreyi seed'ler. Idempotent: zaten kayit varsa hicbir sey yapmaz.
    /// </summary>
    Task EnsureSeededAsync(CancellationToken cancellationToken);

    /// <summary>Mevcut sifrenin son degistirilme tarihi (UI'da gostermek icin).</summary>
    Task<DateTime?> GetLastChangedAtAsync(CancellationToken cancellationToken);
}

public sealed record GatePasswordChangeResult(bool Success, string? Message);
