using CalibraHub.Application.Services.Security;
using Xunit;

namespace CalibraHub.Tests.Security;

/// <summary>
/// Not temizleyicisinin gömülü görsel davranışı (2026-08-24, kayıt-anı temizleme eklenirken).
/// Kayıt anında temizleme açıldığı için bu davranış artık VERİ KAYBI riski taşır:
/// data:image düşerse kullanıcının yapıştırdığı görsel kalıcı olarak silinir.
/// </summary>
public class NoteHtmlSanitizerImageTests
{
    [Fact]
    public void Gomulu_gorsel_korunur()
    {
        const string png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUg==";
        var temiz = NoteHtmlSanitizer.Sanitize($"<p>Ek</p><img src=\"{png}\">");
        Assert.Contains("data:image/png;base64", temiz);
    }

    [Fact]
    public void Data_html_gomulmesi_dusurulur()
    {
        var temiz = NoteHtmlSanitizer.Sanitize("<a href=\"data:text/html;base64,PHNjcmlwdD4=\">tikla</a>");
        Assert.DoesNotContain("data:text/html", temiz, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Data_svg_gomulmesi_dusurulur()
    {
        // SVG script tasiyabilir — gorsel gibi gorunse de gecmemeli.
        var temiz = NoteHtmlSanitizer.Sanitize("<img src=\"data:image/svg+xml;base64,PHN2Zz4=\">");
        Assert.DoesNotContain("svg+xml", temiz, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Http_gorsel_korunur()
    {
        var temiz = NoteHtmlSanitizer.Sanitize("<img src=\"https://ornek.com/a.png\">");
        Assert.Contains("https://ornek.com/a.png", temiz);
    }
}
