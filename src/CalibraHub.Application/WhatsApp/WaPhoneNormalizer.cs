namespace CalibraHub.Application.WhatsApp;

/// <summary>
/// WhatsApp JID ve telefon numarası normalizasyon yardımcısı.
/// Projenin tek normalize implementasyonu — tüm servis/controller'lar buraya delege eder.
/// </summary>
public static class WaPhoneNormalizer
{
    /// <summary>
    /// JID veya ham telefon numarasından sadece rakamları çıkarır.
    /// LID JID'leri (xxx@lid) için de digit kısmını döner — çağıran IsLid() ile kontrol etmeli.
    /// Null veya boş girişte null döner.
    /// </summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? null : digits;
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
