using System.Security.Cryptography;

namespace CalibraHub.Application.Security;

/// <summary>
/// PBKDF2-HMAC-SHA256 ile sifre hashleme. Format:
/// <c>pbkdf2$&lt;iter&gt;$&lt;saltBase64&gt;$&lt;hashBase64&gt;</c>
/// <para>
/// Default iterations 600,000 — 2024 OWASP onerisi. Iterations format icine yazildigi
/// icin gelecekte arttirilabilir; eski hash'ler hala dogrulanir, yenisi otomatik degisir.
/// </para>
/// </summary>
public static class PasswordHasher
{
    private const string Algo            = "pbkdf2";
    private const int    DefaultIter     = 600_000;
    private const int    SaltSize        = 16;
    private const int    HashSize        = 32;
    private static readonly HashAlgorithmName HashAlgo = HashAlgorithmName.SHA256;

    /// <summary>Plaintext sifreyi yeni rastgele salt ile hashler.</summary>
    public static string Hash(string plaintext, int iterations = DefaultIter)
    {
        if (string.IsNullOrEmpty(plaintext)) throw new ArgumentException("Empty password", nameof(plaintext));
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var derived = Rfc2898DeriveBytes.Pbkdf2(plaintext, salt, iterations, HashAlgo, HashSize);
        return $"{Algo}${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(derived)}";
    }

    /// <summary>Plaintext sifreyi saklanmis hash ile karsilastirir (timing-safe).</summary>
    public static bool Verify(string plaintext, string storedHash)
    {
        if (string.IsNullOrEmpty(plaintext) || string.IsNullOrEmpty(storedHash)) return false;
        var parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != Algo) return false;
        if (!int.TryParse(parts[1], out var iter) || iter < 1) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var derived = Rfc2898DeriveBytes.Pbkdf2(plaintext, salt, iter, HashAlgo, expected.Length);
            return CryptographicOperations.FixedTimeEquals(derived, expected);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Sifre gucu kontrolu — minimum kurumsal kurallar.</summary>
    public static (bool Ok, string? Error) ValidateStrength(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))           return (false, "Sifre bos olamaz.");
        if (plaintext.Length < 10)                      return (false, "Sifre en az 10 karakter olmali.");
        if (!plaintext.Any(char.IsUpper))               return (false, "Sifre en az bir buyuk harf icermeli.");
        if (!plaintext.Any(char.IsLower))               return (false, "Sifre en az bir kucuk harf icermeli.");
        if (!plaintext.Any(char.IsDigit))               return (false, "Sifre en az bir rakam icermeli.");
        if (plaintext.All(char.IsLetterOrDigit))        return (false, "Sifre en az bir ozel karakter (.!@#$ vb.) icermeli.");
        return (true, null);
    }
}
