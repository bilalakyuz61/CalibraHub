using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Security;
using CalibraHub.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Services.Gate;

/// <summary>
/// Gate sifre servisi. PBKDF2 hash ile DB'de saklar, <see cref="PasswordHasher"/> kullanir.
/// <para>
/// <b>Recovery / seed politikasi:</b>
/// <see cref="EnsureSeededAsync"/> her startup'ta cagrilir; <c>gate_credentials</c>
/// tablosunda satir yoksa asagidaki onceliklerle seed eder:
/// <list type="number">
///   <item>
///     <c>appsettings.json:GateSettings:InitialPassword</c> dolu ise (DPAPI'den de
///     cozulebilir) o deger kullanilir — per-musteri ozel recovery sifresi koymak isteyenler icin.
///   </item>
///   <item>
///     Bos ise <see cref="DefaultRecoveryPassword"/> sabiti kullanilir — derleme zamani
///     gomulu, deploy edilen config'lerde gorunmez. Vendor (sen) bu degeri bilirsin
///     ve unutmazsin; recovery icin DELETE FROM gate_credentials + restart yeterli.
///   </item>
/// </list>
/// </para>
/// <para>
/// <b>Onemli:</b> Bu sabit lisans Secret'i gibi vendor-shared bir gomulu sifredir.
/// Musteri ilk girisini bu sifreyle yapip <b>derhal</b> kendi sifresine cevirmeli.
/// Production'da bu konuyu deployment dokumanina koy.
/// </para>
/// </summary>
public sealed class GatePasswordService : IGatePasswordService
{
    /// <summary>
    /// Hardcoded varsayilan recovery sifresi. <c>gate_credentials</c> tablosu bos oldugunda
    /// ve appsettings'te override yoksa bu deger seed edilir. Vendor bunu unutmaz; musteriye
    /// ilk kurulumda bildirilir, musteri derhal kendi sifresini belirler.
    /// </summary>
    private const string DefaultRecoveryPassword = "Calibra-Recovery-2026!.,";

    private readonly IGateCredentialsRepository _repo;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GatePasswordService> _logger;

    public GatePasswordService(
        IGateCredentialsRepository repo,
        IConfiguration configuration,
        ILogger<GatePasswordService> logger)
    {
        _repo          = repo;
        _configuration = configuration;
        _logger        = logger;
    }

    public async Task<bool> VerifyAsync(string plaintextPassword, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(plaintextPassword)) return false;
        var record = await _repo.GetAsync(cancellationToken);
        if (record is null || string.IsNullOrEmpty(record.PasswordHash)) return false;
        return PasswordHasher.Verify(plaintextPassword, record.PasswordHash);
    }

    public async Task<GatePasswordChangeResult> ChangeAsync(
        string currentPassword,
        string newPassword,
        string? clientIp,
        CancellationToken cancellationToken)
    {
        var existing = await _repo.GetAsync(cancellationToken);
        if (existing is null || string.IsNullOrEmpty(existing.PasswordHash))
            return new GatePasswordChangeResult(false, "Mevcut sifre bulunamadi.");

        if (!PasswordHasher.Verify(currentPassword, existing.PasswordHash))
            return new GatePasswordChangeResult(false, "Mevcut sifre yanlis.");

        var (ok, error) = PasswordHasher.ValidateStrength(newPassword);
        if (!ok) return new GatePasswordChangeResult(false, error);

        if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
            return new GatePasswordChangeResult(false, "Yeni sifre eskisiyle ayni olamaz.");

        existing.PasswordHash      = PasswordHasher.Hash(newPassword);
        existing.LastChangedAt     = DateTime.UtcNow;
        existing.LastChangedFromIp = clientIp;
        await _repo.SaveAsync(existing, cancellationToken);
        _logger.LogInformation("Gate sifresi degistirildi (IP: {Ip})", clientIp ?? "-");
        return new GatePasswordChangeResult(true, "Sifre basariyla degistirildi.");
    }

    public async Task EnsureSeededAsync(CancellationToken cancellationToken)
    {
        var existing = await _repo.GetAsync(cancellationToken);
        if (existing is not null && !string.IsNullOrEmpty(existing.PasswordHash))
            return; // zaten seed'lenmis — DB canonical

        // 1) appsettings override (DPAPI sifreli olabilir)
        var initialRaw = _configuration["GateSettings:InitialPassword"];
        var initial    = DpapiSecretDecryptor.DecryptIfNeeded(initialRaw);

        // 2) Bos ise hardcoded varsayilan recovery sifresi
        if (string.IsNullOrWhiteSpace(initial))
        {
            initial = DefaultRecoveryPassword;
            _logger.LogWarning(
                "Gate sifresi HARDCODED varsayilan recovery sifresine seed edildi. " +
                "Musteri ilk girisini bu sifreyle yapip Sistem Ayarlari -> Sifre Degistir " +
                "bolumunden DERHAL kendi sifresine cevirmeli.");
        }
        else
        {
            _logger.LogInformation("Gate sifresi appsettings:GateSettings:InitialPassword degerinden seed edildi.");
        }

        var record = new GateCredentials
        {
            Id                = 1,
            PasswordHash      = PasswordHasher.Hash(initial),
            LastChangedAt     = DateTime.UtcNow,
            LastChangedFromIp = null,
            CreatedAt         = DateTime.UtcNow,
        };
        await _repo.SaveAsync(record, cancellationToken);
    }

    public async Task<DateTime?> GetLastChangedAtAsync(CancellationToken cancellationToken)
    {
        var existing = await _repo.GetAsync(cancellationToken);
        return existing?.LastChangedAt;
    }
}
