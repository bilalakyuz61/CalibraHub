using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace CalibraHub.Application.Services.Licensing;

/// <summary>
/// CalibraHub lisans dogrulama modulu — v4 kisa formati.
///
/// Payload (17 byte, decrypt sonrasi):
///   [0]       version  = 4
///   [1..4]    expDays  (int32 big-endian, 2000-01-01 epoch'tan gun sayisi)
///   [5..12]   machineHash (8 byte, SHA256(normalizedMachineId).Take(8))
///   [13..14]  concurrentLimit (uint16 big-endian, 0 = limitsiz)
///   [15..16]  totalUserLimit  (uint16 big-endian, 0 = limitsiz)
///
/// Sifreli veri duzeni (license string base64url decode sonrasi):
///   nonce(12) + ciphertext + tag(16)
///
/// Key turetme:
///   PBKDF2(HMAC-SHA256, password=secret, salt="Calibra-Licence-Manager-LicenseKey-v2", iter=120000, keyLen=32)
///
/// Hicbir loglama/side-effect yapmaz. Sadece true/false ve out parametreleri.
/// </summary>
public static class LicenseValidator
{
    private const byte   Version   = 4;
    private const int    NonceSize = 12;
    private const int    TagSize   = 16;
    private const int    KeySize   = 32;
    private const int    Iter      = 120_000;
    private static readonly byte[] Salt = Encoding.UTF8.GetBytes("Calibra-Licence-Manager-LicenseKey-v2");
    private static readonly DateTime Epoch = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Lisansi dogrula. Herhangi bir hatada <c>false</c> doner, <c>error</c> kisa sebebi icerir.
    /// concurrentLimit / totalUserLimit bilgi amaclidir; enforcement caller'in sorumlulugundadir.
    /// </summary>
    public static bool IsMachineLicensed(
        string license,
        string machineId,
        string secret,
        out DateTime? expiryDate,
        out int concurrentLimit,
        out int totalUserLimit,
        out string? error)
    {
        expiryDate      = null;
        concurrentLimit = 0;
        totalUserLimit  = 0;
        error           = null;

        if (string.IsNullOrWhiteSpace(license))  { error = "invalid format: empty license";  return false; }
        if (string.IsNullOrWhiteSpace(machineId)) { error = "invalid format: empty machineId"; return false; }
        if (string.IsNullOrWhiteSpace(secret))    { error = "invalid format: empty secret";    return false; }

        // 1) Base64url decode
        byte[] blob;
        try { blob = Base64UrlDecode(license.Trim()); }
        catch { error = "invalid format: base64url decode failed"; return false; }

        if (blob.Length < NonceSize + TagSize + 17)
        {
            error = "invalid format: blob too short";
            return false;
        }

        // 2) AES-GCM decrypt
        byte[] plaintext;
        try
        {
            var nonce      = blob.AsSpan(0, NonceSize);
            var tag        = blob.AsSpan(blob.Length - TagSize, TagSize);
            var ciphertext = blob.AsSpan(NonceSize, blob.Length - NonceSize - TagSize);
            plaintext      = new byte[ciphertext.Length];

            var key        = DeriveKey(secret);
            using var aes  = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            CryptographicOperations.ZeroMemory(key);
        }
        catch
        {
            error = "decrypt failed";
            return false;
        }

        // 3) Payload parse
        if (plaintext.Length < 17)        { error = "invalid format: payload too short"; return false; }
        if (plaintext[0] != Version)       { error = "invalid format: unsupported version"; return false; }

        var expDays         = BinaryPrimitives.ReadInt32BigEndian(plaintext.AsSpan(1, 4));
        var machineHash     = plaintext.AsSpan(5, 8).ToArray();
        var concurrentValue = BinaryPrimitives.ReadUInt16BigEndian(plaintext.AsSpan(13, 2));
        var totalValue      = BinaryPrimitives.ReadUInt16BigEndian(plaintext.AsSpan(15, 2));

        // 4) Tarih
        DateTime computedExpiry;
        try { computedExpiry = Epoch.AddDays(expDays); }
        catch { error = "invalid format: expiry overflow"; return false; }

        // 5) Machine eslesmesi
        var expectedHash = ComputeMachineHash(machineId);
        if (!CryptographicOperations.FixedTimeEquals(expectedHash, machineHash))
        {
            error = "machine mismatch";
            return false;
        }

        // 6) Suresi gecmis mi?
        if (computedExpiry.Date < DateTime.Today)
        {
            expiryDate = computedExpiry;
            error      = "expired";
            return false;
        }

        expiryDate      = computedExpiry;
        concurrentLimit = concurrentValue;
        totalUserLimit  = totalValue;
        return true;
    }

    /// <summary>machineId'yi normalize eder: Trim + ToUpperInvariant + sadece harf/rakam.</summary>
    public static string NormalizeMachineId(string machineId)
    {
        if (string.IsNullOrEmpty(machineId)) return string.Empty;
        var trimmed = machineId.Trim().ToUpperInvariant();
        var sb = new StringBuilder(trimmed.Length);
        foreach (var c in trimmed)
            if (char.IsLetterOrDigit(c)) sb.Append(c);
        return sb.ToString();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static byte[] ComputeMachineHash(string machineId)
    {
        var normalized = NormalizeMachineId(machineId);
        var bytes      = Encoding.UTF8.GetBytes(normalized);
        var full       = SHA256.HashData(bytes);
        var result     = new byte[8];
        Buffer.BlockCopy(full, 0, result, 0, 8);
        return result;
    }

    private static byte[] DeriveKey(string secret)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(secret, Salt, Iter, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(KeySize);
    }

    private static byte[] Base64UrlDecode(string input)
    {
        // Base64url → Base64
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "=";  break;
            case 1: throw new FormatException("base64url length invalid");
        }
        return Convert.FromBase64String(s);
    }
}
