using System.Security.Cryptography;
using System.Text;

namespace CalibraHub.Persistence.Security;

internal static class IntegratorSecretProtector
{
    private const string Prefix = "enc:v1:";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CalibraHub.IntegratorSettings.Secret.v1");

    public static string Protect(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return secret;
        }

        if (secret.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return secret;
        }

        if (!OperatingSystem.IsWindows())
        {
            return secret;
        }

        var plainBytes = Encoding.UTF8.GetBytes(secret);
        var protectedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.LocalMachine);
        return Prefix + Convert.ToBase64String(protectedBytes);
    }

    public static string Unprotect(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (!value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return value;
        }

        if (!OperatingSystem.IsWindows())
        {
            return value;
        }

        try
        {
            var protectedBytes = Convert.FromBase64String(value[Prefix.Length..]);
            var plainBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.LocalMachine);
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
