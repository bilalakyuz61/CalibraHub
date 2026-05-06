using System.Security.Cryptography;
using System.Text;

namespace CalibraHub.Application.Security;

/// <summary>
/// appsettings'te "dpapi:base64..." prefix'i ile saklanan secret'leri Windows DPAPI
/// (ProtectedData.Unprotect, LocalMachine scope) ile cozer.
/// Prefix yoksa veri aynen donulur — DEV/migration kolayligi icin.
/// Windows disindaki OS'larda da plaintext kabul edilir (ileri-uyumluluk).
/// </summary>
public static class DpapiSecretDecryptor
{
    private const string Prefix = "dpapi:";

    /// <summary>Verilen degeri (gerekirse) DPAPI ile cozer ve plaintext doner. NULL/bos guvenli.</summary>
    public static string DecryptIfNeeded(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        if (!value.StartsWith(Prefix, StringComparison.Ordinal)) return value;
        if (!OperatingSystem.IsWindows()) return value;

        try
        {
            var encrypted = Convert.FromBase64String(value[Prefix.Length..]);
            var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            // Decrypt basarisiz olursa raw donuyoruz — config admin'i log'larda hata gorur,
            // sistem tamamen disabled olur (hatali secret yanlis kod uretir/dogrular).
            return value;
        }
    }

    /// <summary>Plaintext'i DPAPI ile sifreler ve "dpapi:base64..." formatinda doner.</summary>
    public static string Encrypt(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) throw new ArgumentException("Plaintext bos olamaz.", nameof(plaintext));
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI sadece Windows'ta calisir.");

        var data      = Encoding.UTF8.GetBytes(plaintext);
        var encrypted = ProtectedData.Protect(data, null, DataProtectionScope.LocalMachine);
        return Prefix + Convert.ToBase64String(encrypted);
    }
}
