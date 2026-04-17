using System.Security.Cryptography;
using CalibraHub.Application.Abstractions.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Infrastructure.Security;

/// <summary>
/// Not içeriğini ASP.NET Core Data Protection API ile şifreler (AES-256-CBC +
/// HMAC-SHA256). Key material, uygulama dizinindeki <c>.app-data-protection/</c>
/// klasöründen okunur — aynı mekanizma bağlantı dizesi şifrelemede de kullanılıyor.
///
/// Purpose string: <c>Notes.Content.v1</c>
///   - <c>v1</c> → ileride key rotation için (yeni bir <c>v2</c> ile eski kayıtlar yine çözülebilir
///     çünkü DataProtection otomatik key versioning yapar).
///
/// Şifrelenmiş çıktı formatı: Data Protection'ın kendi base64 payload'u
/// (header + encrypted data + auth tag). DB'de <c>nvarchar(max)</c> kolonuna yazılır.
///
/// Geriye dönük uyum: Unprotect bir <see cref="CryptographicException"/> veya
/// <see cref="FormatException"/> fırlatırsa (mevcut düz metin kayıtlar, bozuk veri vb.)
/// girdi olduğu gibi döndürülür; bu sayede migration öncesi kayıtlar çalışmaya devam eder.
///
/// Web ve Worker projeleri aynı <c>ApplicationName</c> ve <c>PersistKeysToFileSystem</c>
/// yolu kullanarak aynı key ring'i paylaşmalıdır; aksi takdirde Worker tarafında
/// Web tarafında şifrelenen veri çözülemez.
/// </summary>
public sealed class DataProtectionNoteEncryptionService : INoteEncryptionService
{
    private readonly IDataProtector _protector;
    private readonly ILogger<DataProtectionNoteEncryptionService> _logger;

    public DataProtectionNoteEncryptionService(
        IDataProtectionProvider provider,
        ILogger<DataProtectionNoteEncryptionService> logger)
    {
        _protector = provider.CreateProtector("Notes.Content.v1");
        _logger = logger;
    }

    public string? Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return plaintext;
        try
        {
            return _protector.Protect(plaintext);
        }
        catch (Exception ex)
        {
            // Şifreleme başarısız olursa kayıt yine yapılsın (düz metin olarak)
            // — veri kaybı olmasın. Production'da alarm çalacak bir warning düşer.
            _logger.LogError(ex, "[NoteEncryption] Protect basarisiz, icerik duz metin olarak yazilacak.");
            return plaintext;
        }
    }

    public string? Unprotect(string? ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext)) return ciphertext;
        try
        {
            return _protector.Unprotect(ciphertext);
        }
        catch (CryptographicException)
        {
            // Mevcut düz metin kayıt veya eski key'le şifrelenmiş (artık yok olmuş) içerik.
            // Veri kaybetmeden aynen döndür — geriye dönük uyum için kritik.
            return ciphertext;
        }
        catch (FormatException)
        {
            // Base64 olmayan düz metin
            return ciphertext;
        }
    }
}
