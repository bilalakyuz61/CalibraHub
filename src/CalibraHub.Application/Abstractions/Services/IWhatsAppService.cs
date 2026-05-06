using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Meta WhatsApp Cloud API uzerinden mesajlasma. Token + PhoneNumberId DB'de DPAPI sifreli saklanir.
/// </summary>
public interface IWhatsAppService
{
    Task<WhatsAppConfig?> GetConfigAsync(CancellationToken cancellationToken);

    Task<WhatsAppConfigSaveResult> SaveConfigAsync(
        string accessToken,
        string phoneNumberId,
        string? businessAccountId,
        string? webhookVerifyToken,
        bool isEnabled,
        CancellationToken cancellationToken);

    /// <summary>Yapilandirmayi dogrular — Meta'dan numara bilgisini cekip cevap alir.</summary>
    Task<WhatsAppTestResult> TestConfigAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Belirli bir telefona duz metin mesaj gonderir.
    /// </summary>
    /// <param name="interactive">
    /// true: kullanici elinde yazip basti — anti-spam icin "insan benzeri rastgele gecikme" atlanir.
    /// false (default): otomasyon/toplu gonderim — gecikme uygulanir.
    /// </param>
    Task<WhatsAppSendResult> SendTextMessageAsync(string toPhone, string message, CancellationToken cancellationToken, bool interactive = false);

    /// <summary>Web QR Bridge'in /status + /qr ucunu proxyler — mixed-content ve CORS'u sunucudan geciriyoruz.</summary>
    Task<WhatsAppQrStatusResult> GetWebQrStatusAsync(CancellationToken cancellationToken);
}

public sealed record WhatsAppConfigSaveResult(bool Success, string? Message);

/// <summary>
/// Kind: ok (yesil) | info (sari, gecici durum — connecting/awaiting_qr) | error (kirmizi)
/// Success: sadece "kullanima hazir" durumu icin true; UI badge guncellemesi icin kullanilir.
/// </summary>
public sealed record WhatsAppTestResult(bool Success, string? Message, string? DisplayPhoneNumber, string Kind = "ok");

public sealed record WhatsAppSendResult(bool Success, string? Message, string? MessageId);
public sealed record WhatsAppQrStatusResult(bool Reachable, string State, string? Qr, string? Phone, string? DisplayName, string? Error);
