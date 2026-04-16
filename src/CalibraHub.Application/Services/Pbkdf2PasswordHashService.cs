using System.Security.Cryptography;
using CalibraHub.Application.Abstractions.Services;

namespace CalibraHub.Application.Services;

public sealed class Pbkdf2PasswordHashService : IPasswordHashService
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int IterationCount = 100_000;
    private const string FormatMarker = "PBKDF2";

    public string HashPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Password is required.", nameof(password));
        }

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, IterationCount, HashAlgorithmName.SHA256, KeySize);

        return string.Join(
            '$',
            FormatMarker,
            IterationCount.ToString(),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(key));
    }

    public bool VerifyPassword(string password, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        var segments = storedHash.Split('$');
        if (segments.Length != 4)
        {
            return false;
        }

        if (!string.Equals(segments[0], FormatMarker, StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(segments[1], out var iterations) || iterations <= 0)
        {
            return false;
        }

        byte[] salt;
        byte[] expectedKey;

        try
        {
            salt = Convert.FromBase64String(segments[2]);
            expectedKey = Convert.FromBase64String(segments[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actualKey = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expectedKey.Length);
        return CryptographicOperations.FixedTimeEquals(actualKey, expectedKey);
    }
}
