namespace CalibraHub.Domain.Entities;

/// <summary>İletişim yöntemi seçimi.</summary>
public enum WhatsAppProviderType
{
    /// <summary>Meta Cloud API — resmi, Facebook Business Manager hesabı gerekli.</summary>
    CloudApi = 0,
    /// <summary>WhatsApp Web QR — Node sidecar üzerinden, Facebook gerekmez ama ToS ihlali riski.</summary>
    WebQr    = 1,
}

/// <summary>
/// WhatsApp yapilandirmasi — tek-satir tablo (id=1).
/// Provider'a göre Cloud API alanları veya Web QR (sidecar URL) kullanılır.
/// Token hassas oldugundan DPAPI ile sifrelenir.
/// </summary>
public sealed class WhatsAppConfig
{
    public int                  Id                       { get; set; } = 1;
    public WhatsAppProviderType Provider                 { get; set; } = WhatsAppProviderType.CloudApi;

    // Cloud API alanları
    public string?              AccessTokenEncrypted     { get; set; }       // Meta token (DPAPI)
    public string?              AppSecretEncrypted       { get; set; }       // Meta App Secret — webhook imza doğrulaması (DPAPI)
    public string?              PhoneNumberId            { get; set; }
    public string?              BusinessAccountId        { get; set; }
    public string?              WebhookVerifyToken       { get; set; }

    // Web QR alanları (Node sidecar)
    public string?              WebQrBridgeUrl           { get; set; }       // örn: http://localhost:61100

    // Ortak
    public string?              DisplayPhoneNumber       { get; set; }
    public bool                 IsEnabled                { get; set; }
    public DateTime?            LastSuccessfulSendAt     { get; set; }
    public string?              LastError                { get; set; }
    public DateTime             CreatedAt                { get; set; }
    public DateTime             UpdatedAt                { get; set; }
}
