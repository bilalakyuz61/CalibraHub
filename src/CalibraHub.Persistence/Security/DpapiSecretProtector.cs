using System.Security.Cryptography;
using System.Text;

namespace CalibraHub.Persistence.Security;

/// <summary>
/// DPAPI (LocalMachine kapsamı) ile "enc:v1:" önekli gizli değer koruması.
/// Entropy çağıran modüle özgüdür — bir modülün şifrelediğini diğeri çözemez.
/// Windows dışında ve zaten şifreli değerlerde no-op'tur.
/// </summary>
internal static class DpapiSecretProtector
{
    private const string Prefix = "enc:v1:";

    public static string Protect(string secret, byte[] entropy)
    {
        if (string.IsNullOrWhiteSpace(secret)) return secret;
        if (secret.StartsWith(Prefix, StringComparison.Ordinal)) return secret;
        if (!OperatingSystem.IsWindows()) return secret;

        var plainBytes = Encoding.UTF8.GetBytes(secret);
        var protectedBytes = ProtectedData.Protect(plainBytes, entropy, DataProtectionScope.LocalMachine);
        return Prefix + Convert.ToBase64String(protectedBytes);
    }

    public static string Unprotect(string value, byte[] entropy)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        if (!value.StartsWith(Prefix, StringComparison.Ordinal)) return value;
        if (!OperatingSystem.IsWindows()) return value;

        try
        {
            var protectedBytes = Convert.FromBase64String(value[Prefix.Length..]);
            var plainBytes = ProtectedData.Unprotect(protectedBytes, entropy, DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (FormatException)
        {
            return string.Empty;
        }
        catch (CryptographicException)
        {
            return string.Empty;
        }
    }
}

/// <summary>Harici SQL bağlantı parolaları için DPAPI koruması.</summary>
internal static class ExternalDbSecretProtector
{
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("CalibraHub.ExternalDbConnection.Password.v1");

    public static string Protect(string secret) => DpapiSecretProtector.Protect(secret, Entropy);

    public static string Unprotect(string value) => DpapiSecretProtector.Unprotect(value, Entropy);
}
