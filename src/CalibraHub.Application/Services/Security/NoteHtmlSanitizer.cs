using Ganss.Xss;

namespace CalibraHub.Application.Services.Security;

/// <summary>
/// Not içeriğinin (Tiptap zengin metin HTML'i) güvenli gösterimi.
///
/// <para><b>Neden (2026-08-24 güvenlik denetimi, Y1):</b> <c>Views/Notes/Public.cshtml</c>
/// not içeriğini <c>@Html.Raw</c> ile basıyordu ve o sayfa <c>[AllowAnonymous]</c>. İçerik
/// kullanıcı yazımı (Tiptap HTML) ve kaydedilirken temizlenmiyor. Sonuç: bir kullanıcı
/// <c>&lt;img src=x onerror=...&gt;</c> içeren bir not oluşturup herkese açık paylaşınca,
/// linke tıklayan ANONİM ziyaretçinin (veya başka bir çalışanın) tarayıcısında script
/// çalışıyordu — oturum/CSRF token okuma dahil.</para>
///
/// <para><b>Yaklaşım:</b> elle regex/blacklist YAZILMAZ (XSS'te klasik hata kaynağı).
/// AngleSharp tabanlı <see cref="HtmlSanitizer"/> ile gerçek DOM ayrıştırması + ALLOWLIST:
/// yalnızca biçimlendirme etiketleri/öznitelikleri geçer; <c>script</c>, <c>iframe</c>,
/// <c>on*</c> olay öznitelikleri, <c>javascript:</c> URL'leri ve <c>style</c> ifadeleri düşer.</para>
///
/// <para>Örnek (thread-safe): <see cref="HtmlSanitizer"/> örneği yeniden kullanılabilir,
/// bu yüzden statik olarak bir kez kurulur.</para>
/// </summary>
public static class NoteHtmlSanitizer
{
    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();

    private static HtmlSanitizer CreateSanitizer()
    {
        var s = new HtmlSanitizer();

        // Varsayılan allowlist zaten makul; Tiptap'in ürettiği biçimlendirme
        // etiketlerini açıkça garanti altına alıyoruz.
        foreach (var tag in new[]
                 {
                     "p", "br", "strong", "b", "em", "i", "u", "s", "strike", "del", "mark",
                     "h1", "h2", "h3", "h4", "h5", "h6",
                     "ul", "ol", "li", "blockquote", "pre", "code", "hr",
                     "table", "thead", "tbody", "tfoot", "tr", "th", "td",
                     "a", "img", "span", "div",
                 })
            s.AllowedTags.Add(tag);

        // Tiptap görev listesi / hizalama gibi özellikler data-* ve class ile gelir.
        foreach (var attr in new[] { "class", "colspan", "rowspan", "start", "type",
                                     "data-type", "data-checked", "data-align" })
            s.AllowedAttributes.Add(attr);

        // Bağlantılar: yalnız güvenli şemalar (javascript: DIŞARIDA).
        s.AllowedSchemes.Clear();
        s.AllowedSchemes.Add("http");
        s.AllowedSchemes.Add("https");
        s.AllowedSchemes.Add("mailto");
        s.AllowedSchemes.Add("tel");
        // data: — YALNIZ gömülü GÖRSEL için (2026-08-24). Notlara pano üzerinden
        // yapıştırılan görseller data:image/... base64 olarak içerikte durur; şemayı
        // tamamen yasaklamak temizleme sırasında bu görselleri SİLER (veri kaybı).
        // Tehlikeli olan data:text/html'dir; aşağıdaki FilterUrl yalnız image/* geçirir.
        s.AllowedSchemes.Add("data");
        s.FilterUrl += (_, e) =>
        {
            var url = e.OriginalUrl ?? string.Empty;
            if (!url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return;
            var isSafeImage =
                url.StartsWith("data:image/png;base64,",  StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("data:image/jpeg;base64,", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("data:image/jpg;base64,",  StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("data:image/gif;base64,",  StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("data:image/webp;base64,", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("data:image/bmp;base64,",  StringComparison.OrdinalIgnoreCase);
            // image/svg+xml BİLİNÇLİ olarak dışarıda: SVG script taşıyabilir.
            if (!isSafeImage) e.SanitizedUrl = null;
        };

        // Gömülü içerik ve stil tamamen kapalı — CSS ifadeleri de bir XSS taşıyıcısıdır.
        s.AllowedTags.Remove("style");
        s.AllowedAttributes.Remove("style");
        s.AllowedCssProperties.Clear();

        // Herkese açık sayfada dış referans/izleme sızıntısını sınırla.
        s.RemovingAttribute += (_, e) => { /* sessiz: allowlist dışı öznitelik düşürüldü */ };
        return s;
    }

    /// <summary>Not HTML'ini güvenli hale getirir. Girdi boşsa boş döner.</summary>
    public static string Sanitize(string? html)
        => string.IsNullOrWhiteSpace(html) ? string.Empty : Sanitizer.Sanitize(html);
}
