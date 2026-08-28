namespace CalibraHub.Application.Services.EDocument;

/// <summary>
/// Netsis metin alanlarindaki TURKCE KARAKTER bozulmasini duzeltir.
///
/// <para><b>Sorun:</b> Netsis Turkce harfleri <c>CP1254</c> (Turkce) bayt degerleriyle yazar,
/// ama kolonun harmanlamasi <c>SQL_Latin1_General_CP1_CI_AS</c> yani <c>CP1252</c>'dir. SQL Server
/// baytlari kolonun kod sayfasina gore cozdugu icin, ornegin <c>0xDD</c> baytini
/// <c>İ</c> (U+0130) yerine <c>Ý</c> (U+00DD) olarak dondurur. Sonuc: "KESKİN" -> "KESKÝN".</para>
///
/// <para><b>Olcum (bu veritabani):</b> aktarilmis 36.970 kalem adinda ikili harmanlamayla arandiginda
/// GERCEK Turkce harf sayisi 0'di; kaynak baytlar <c>4B 45 53 4B DD 4E</c> ("KESK" + 0xDD + "N").
/// Duzeltme sonrasi ayni konum U+0130 dondurur.</para>
///
/// <para><b>Neden C# tarafinda:</b> ayni isi SQL'de 14 katli <c>REPLACE</c> zinciriyle yapmak her
/// sorguyu sisirir ve her yeni sorguda tekrar edilmesi gerekirdi (kopyalanmayi unutmak =
/// sessizce bozuk metin). Burada TEK noktadan gecer ve birim testi yazilabilir.</para>
///
/// <para><b>Yalniz 6 esleme gerekir:</b> CP1252 ile CP1254 <c>Ü Ö Ç ü ö ç</c> kodlarinda AYNIDIR
/// (220, 214, 199, 252, 246, 231) — o karakterler zaten dogru gelir, eslenmeleri gereksizdir.
/// Bozulan yalniz alti harftir.</para>
/// </summary>
public static class NetsisTextDecoder
{
    // CP1252 okumasiyla ortaya cikan karakter -> gercek Turkce karakter.
    private static readonly (char From, char To)[] Map =
    {
        ('Ð', 'Ğ'),   // 0xD0 Ð
        ('Þ', 'Ş'),   // 0xDE Þ
        ('Ý', 'İ'),   // 0xDD Ý
        ('ð', 'ğ'),   // 0xF0 ð
        ('þ', 'ş'),   // 0xFE þ
        ('ý', 'ı'),   // 0xFD ý
    };

    /// <summary>
    /// Bozuk karakterleri duzeltir. Bozulma yoksa GIRDIYI AYNEN dondurur (yeni string ayirmaz) —
    /// kalemlerin cogunda Turkce harf bulunmaz, gereksiz kopya uretmemek icin.
    /// </summary>
    public static string? Fix(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        var needsFix = false;
        foreach (var ch in value)
        {
            foreach (var (from, _) in Map)
            {
                if (ch == from) { needsFix = true; break; }
            }
            if (needsFix) break;
        }
        if (!needsFix) return value;

        var buffer = value.ToCharArray();
        for (var i = 0; i < buffer.Length; i++)
        {
            foreach (var (from, to) in Map)
            {
                if (buffer[i] == from) { buffer[i] = to; break; }
            }
        }
        return new string(buffer);
    }
}
