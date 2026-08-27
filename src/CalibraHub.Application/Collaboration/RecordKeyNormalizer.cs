using System.Text;

namespace CalibraHub.Application.Collaboration;

/// <summary>
/// Eşzamanlı düzenleme kilidi anahtarlarının normalizasyonu — istemcideki
/// <c>wwwroot/js/collaboration.js → normalizeToken</c> fonksiyonunun birebir karşılığı.
///
/// Kilit anahtarı istemcinin ürettiği biçimle saklanır ("SALES_QUOTE_EDIT" →
/// "sales-quote-edit"). Sunucu farklı normalize ederse anahtar TUTMAZ ve kilit kontrolü
/// sessizce "kilit yok" der — hata vermez, sadece hiç çalışmaz. Bu yüzden tek kaynak
/// burasıdır ve <c>CollaborationLockGuardTests</c> ile pariteye sabitlenmiştir.
///
/// Web katmanında değil Application'da durur: birim testleri (net10.0) Web projesini
/// (net10.0-windows) referans alamıyor.
/// </summary>
public static class RecordKeyNormalizer
{
    /// <summary>
    /// camelCase/rakam sınırına '-' koyar, alfanümerik olmayanları '-' yapar,
    /// tekrarlı ve baştaki/sondaki tireleri atar, küçük harfe indirir.
    /// </summary>
    public static string NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var src = value.Trim();
        var sb = new StringBuilder(src.Length + 4);

        for (var i = 0; i < src.Length; i++)
        {
            var c = src[i];
            if (char.IsUpper(c) && i > 0 && (char.IsLower(src[i - 1]) || char.IsDigit(src[i - 1])))
                sb.Append('-');                       // camelCase sınırı
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
        }

        var parts = sb.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries);
        return string.Join('-', parts);
    }
}
