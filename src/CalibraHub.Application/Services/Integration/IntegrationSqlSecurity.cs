using System.Text.RegularExpressions;

namespace CalibraHub.Application.Services.Integration;

/// <summary>
/// Entegrasyon Lookup Function'larinda yazilan serbest SQL snippet'leri
/// guvenlik validasyonu. Kara liste yaklasimi: yaygin destruktif komutlar
/// reddedilir. Comment'leri stripleyip kontrol eder.
///
/// Sadece SELECT cumlelerine izin verilir.
/// </summary>
public static class IntegrationSqlSecurity
{
    private static readonly string[] ForbiddenPatterns = new[]
    {
        @"\bINSERT\b", @"\bUPDATE\b", @"\bDELETE\b", @"\bMERGE\b",
        @"\bDROP\b", @"\bCREATE\b", @"\bALTER\b", @"\bTRUNCATE\b",
        @"\bGRANT\b", @"\bREVOKE\b", @"\bDENY\b",
        @"\bEXEC(?:UTE)?\b", @"\bsp_\w+", @"\bxp_\w+",
        @"\bBULK\s+INSERT\b", @"\bOPENROWSET\b", @"\bOPENDATASOURCE\b",
        @"\bBACKUP\b", @"\bRESTORE\b", @"\bSHUTDOWN\b", @"\bKILL\b",
        @"\bSELECT\s+.*\s+INTO\s+\w",  // SELECT ... INTO yeni tablo
    };

    private static readonly Regex[] ForbiddenRegexes =
        ForbiddenPatterns.Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled)).ToArray();

    private static readonly Regex SelectRegex = new(@"\bSELECT\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LineCommentRegex = new(@"--.*$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex BlockCommentRegex = new(@"/\*[\s\S]*?\*/", RegexOptions.Compiled);

    /// <summary>
    /// SQL snippet guvenlik kontrolu — sadece SELECT'e izin verir. Hata mesaji
    /// doner null ise temiz.
    /// </summary>
    public static string? ValidateSelectOnly(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return "SQL bos.";

        // Comment'leri at
        var cleaned = LineCommentRegex.Replace(sql, "");
        cleaned = BlockCommentRegex.Replace(cleaned, "");

        foreach (var rx in ForbiddenRegexes)
        {
            var m = rx.Match(cleaned);
            if (m.Success) return $"Yasaklanmis komut: '{m.Value.Trim()}'. Sadece SELECT cumlesi izinli.";
        }

        if (!SelectRegex.IsMatch(cleaned))
            return "SQL en az bir SELECT icermeli.";

        return null;
    }
}
