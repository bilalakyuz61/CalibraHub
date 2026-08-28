using CalibraHub.Application.Services.EDocument;
using Xunit;

namespace CalibraHub.Tests.Services;

/// <summary>
/// Netsis Turkce karakter duzeltmesi. Bu testler VERITABANI ISTEMEZ.
///
/// <para>Beklenen degerler CANLI VERIDEN alindi: kaynak baytlar 4B 45 53 4B DD 4E
/// ("KESK" + 0xDD + "N") ve 0xDD, CP1252 okumasinda 'Ý' olarak gelir.</para>
/// </summary>
public sealed class NetsisTextDecoderTests
{
    [Theory]
    [InlineData("KESKÝN", "KESKİN")]        // 0xDD -> İ
    [InlineData("ÝZMÝR", "İZMİR")]
    [InlineData("ÇELÝK SAN.", "ÇELİK SAN.")]
    [InlineData("DAÐITIM", "DAĞITIM")]      // 0xD0 -> Ğ
    [InlineData("ÞÝRKET", "ŞİRKET")]        // 0xDE -> Ş
    [InlineData("yaðmur", "yağmur")]        // 0xF0 -> ğ
    [InlineData("þeker", "şeker")]          // 0xFE -> ş
    [InlineData("kýrmýzý", "kırmızı")]      // 0xFD -> ı
    public void Bozuk_karakterler_duzeltilir(string bozuk, string beklenen)
        => Assert.Equal(beklenen, NetsisTextDecoder.Fix(bozuk));

    [Theory]
    [InlineData("ÜRÜN")]        // CP1252 ile CP1254 AYNI -> dokunulmamali
    [InlineData("ÖZEL")]
    [InlineData("ÇANTA")]
    [InlineData("üzüm")]
    [InlineData("gözlük")]
    [InlineData("çiçek")]
    [InlineData("NORMAL TEXT 123")]
    public void Zaten_dogru_olan_metin_DEGISMEZ(string metin)
        => Assert.Equal(metin, NetsisTextDecoder.Fix(metin));

    [Fact]
    public void Bozulma_yoksa_AYNI_ornek_donulur()
    {
        // Kalemlerin cogunda Turkce harf yoktur; gereksiz kopya uretilmemeli.
        const string s = "SOL ARKA KAPI ONARIM";
        Assert.Same(s, NetsisTextDecoder.Fix(s));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    public void Bos_deger_guvenli(string? girdi, string? beklenen)
        => Assert.Equal(beklenen, NetsisTextDecoder.Fix(girdi));
}
