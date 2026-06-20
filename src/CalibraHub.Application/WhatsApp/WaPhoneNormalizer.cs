namespace CalibraHub.Application.WhatsApp;

/// <summary>
/// WhatsApp JID ve telefon numarası normalizasyon yardımcısı.
/// Projenin tek normalize implementasyonu — tüm servis/controller'lar buraya delege eder.
/// </summary>
public static class WaPhoneNormalizer
{
    /// <summary>
    /// JID veya ham telefon numarasından uluslararası formatta rakam dizisi üretir.
    /// Türkiye yerel formatlarını (05XX, 5XX) otomatik olarak 90XX'e çevirir.
    /// LID JID'leri (xxx@lid) için de digit kısmını döner — çağıran IsLid() ile kontrol etmeli.
    /// Null veya boş girişte null döner.
    /// </summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length == 0) return null;

        // Uluslararası arama öneki "00" → kaldır
        if (digits.StartsWith("00", StringComparison.Ordinal))
            digits = digits[2..];

        // Türkiye yerel: 11 rakam, "0" ile başlar (05XX XXX XX XX) → "90" + kalan 10 rakam
        if (digits.Length == 11 && digits.StartsWith('0'))
            digits = "90" + digits[1..];

        // Türkiye yerel: 10 rakam, "5" ile başlar (5XX XXX XX XX) → "90" ekle
        else if (digits.Length == 10 && digits.StartsWith('5'))
            digits = "90" + digits;

        return digits;
    }

    /// <summary>JID'in LID (Linked ID) formatında olup olmadığını kontrol eder: xxx@lid</summary>
    public static bool IsLid(string? jid)
        => jid?.EndsWith("@lid", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>JID'in grup formatında olup olmadığını kontrol eder: xxx@g.us</summary>
    public static bool IsGroup(string? jid)
        => jid?.EndsWith("@g.us", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>JID'den @ öncesi yerel bölümü alır. Ör: "905...@s.whatsapp.net" → "905..."</summary>
    public static string ExtractLocalPart(string? jid)
        => jid?.Split('@')[0] ?? string.Empty;
}
