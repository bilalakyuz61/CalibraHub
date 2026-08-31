using System.Text;
using System.Text.RegularExpressions;

namespace CalibraHub.Application.Diagnostics.SqlTrace;

/// <summary>
/// SQL izleme (canlı profiler) ekranında parametre DEĞERLERİ kullanıcı kararıyla
/// MASKELENMEZ (teşhis amacıyla görünür kalır) — TEK istisna kimlik/sır niteliğindeki
/// alanlardır (parola, token, PIN, kart kimliği vb.). Aynı gerekçe audit modülündeki
/// `snapshotIgnore` ile birebir aynı (bkz. CLAUDE.md "İşlem Log Modülü" — PIN/hash gibi
/// alanlar audit'e yazılmaz). Liste TEK noktada durur; yeni hassas alan çıkarsa buraya eklenir.
/// </summary>
public static class SqlTraceMasking
{
    public const string MaskedValue = "***";

    /// <summary>
    /// Parametre/kolon adında geçen ASCII (Türkçe karakterler normalize edilmiş) kelime
    /// parçaları. "Contains" ile eşleşir — örn. "UserPasswordHash", "CardCode", "NfcUid",
    /// "ResetCodeExpiry" hepsi yakalanır. Fazla-yakalama (over-masking) burada bilinçli
    /// tercih: teşhis için bir alanı gereksiz gizlemek, bir sırrı sızdırmaktan iyidir.
    /// </summary>
    private static readonly string[] SensitiveNameFragments =
    {
        "password", "passwordhash", "pwd", "pass", "parola", "sifre",
        "hash", "salt", "pin", "token", "apikey", "api_key", "secret", "clientsecret",
        "connectionstring", "cardcode", "nfc", "otp", "resetcode",
    };

    // Komut metni içinde NADİREN görülen gömülü literal ataması (ör. dinamik SQL'de
    // "SET Password = 'xxx'" gibi parametresiz kullanım). Regex "en iyi çaba" — genel SQL
    // ayrıştırıcısı değildir: string birleştirme (+), CASE ifadesi, çift-tırnak, ya da
    // literalin başka bir alias/alt sorgu içinde geçmesi gibi durumları YAKALAYAMAZ.
    private static readonly Regex InlineLiteralRegex = new(
        @"(?i)\[?(" + string.Join("|", SensitiveNameFragments) + @")\]?\s*=\s*N?'([^']*)'",
        RegexOptions.Compiled);

    public static bool IsSensitiveParamName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var normalized = Normalize(name);
        foreach (var fragment in SensitiveNameFragments)
        {
            if (normalized.Contains(fragment, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>Parametre adı hassassa değeri "***" ile değiştirir; adın kendisi HER ZAMAN görünür kalır.</summary>
    public static string MaskParamValue(string? name, string? rawValue)
        => IsSensitiveParamName(name) ? MaskedValue : rawValue ?? "NULL";

    /// <summary>
    /// Komut metni içindeki "Alan = 'literal'" biçimli hassas atamaları maskeler. Parametreli
    /// sorgularda (@Password gibi) bu fonksiyonun yapacak bir işi yoktur — değer zaten metinde
    /// değil, ayrı parametre koleksiyonundadır ve MaskParamValue ile ayrı maskelenir.
    /// </summary>
    public static string MaskInlineLiterals(string? commandText)
    {
        if (string.IsNullOrEmpty(commandText)) return commandText ?? string.Empty;
        return InlineLiteralRegex.Replace(commandText, m =>
        {
            // Grup 2 (literal içeriği) boş olabilir — string.Replace boş "oldValue" ile
            // çalışmaz, bu yüzden eşleşmeyi konum bazlı yeniden kurarız.
            var beforeLiteral = m.Value[..(m.Groups[2].Index - m.Index)];
            return beforeLiteral + MaskedValue + "'";
        });
    }

    private static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            sb.Append(char.ToLowerInvariant(c) switch
            {
                'ş' => 's',
                'ı' => 'i',
                'ğ' => 'g',
                'ü' => 'u',
                'ö' => 'o',
                'ç' => 'c',
                var lower => lower,
            });
        }
        return sb.ToString();
    }
}
