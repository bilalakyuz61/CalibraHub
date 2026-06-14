using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// DocLayout (OutputFormat='email') sablonunu HTML mail govdesine cevirir.
/// PDF renderer'dan ayri bir pipeline — tablo-tabanli inline-style sablon,
/// mail istemcilerinin uyumlulugu icin minimal CSS.
/// </summary>
public interface IMailTemplateRenderer
{
    /// <summary>
    /// Layout JSON'unu HTML'e render eder.
    /// </summary>
    /// <param name="layout">Tasarim detayi (LayoutJson + meta).</param>
    /// <param name="tokenValues">{contactName}, {contactEmail}, {currentDate} gibi sabit tokenlar icin.</param>
    /// <param name="mailBodyContent">"mail_body" band placeholder'inin icerigi (kullanici gonderim sirasinda doldurur). Plain text — escape edilip &lt;p&gt; ile sarilir.</param>
    string RenderHtml(DocLayoutDetailDto layout, IDictionary<string, string> tokenValues, string? mailBodyContent);
}
