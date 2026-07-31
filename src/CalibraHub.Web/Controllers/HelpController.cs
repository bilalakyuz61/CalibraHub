using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Sayfa-bazlı yardım (F1) dokümanları. İçerik disk üzerinde tutulur:
/// {ContentRoot}/App_Data/Help/{lang}/{key}.html — rebuild GEREKMEZ, dosya
/// düzenlenince anında yayında. Yeni bir sayfaya yardım eklemek için:
///   1) İlgili controller action'ında ViewData["HelpKey"] = "&lt;key&gt;" ver.
///   2) App_Data/Help/tr/&lt;key&gt;.html dosyasını oluştur.
/// </summary>
[Authorize]
[Route("[controller]/[action]")]
public sealed class HelpController : Controller
{
    private readonly IWebHostEnvironment _env;

    public HelpController(IWebHostEnvironment env)
    {
        _env = env;
    }

    /// <summary>GET /Help/Content?key=calendar — yardım dokümanının HTML gövdesi.</summary>
    [HttpGet]
    public IActionResult Content([FromQuery] string? key, [FromQuery] string? lang)
    {
        var safeKey = Sanitize(key);
        if (safeKey is null)
            return NotFound();

        // Şimdilik yalnız TR içeriği var; ileride 'en' eklenince lang parametresi kullanılır.
        var safeLang = Sanitize(lang) ?? "tr";

        var path = Path.Combine(_env.ContentRootPath, "App_Data", "Help", safeLang, safeKey + ".html");
        if (!System.IO.File.Exists(path))
        {
            // TR dışı bir dil istenip bulunamazsa TR'ye düş.
            if (safeLang != "tr")
            {
                path = Path.Combine(_env.ContentRootPath, "App_Data", "Help", "tr", safeKey + ".html");
                if (!System.IO.File.Exists(path))
                    return NotFound();
            }
            else
            {
                return NotFound();
            }
        }

        var html = System.IO.File.ReadAllText(path);
        return content(html);
    }

    private ContentResult content(string html) =>
        new ContentResult { Content = html, ContentType = "text/html; charset=utf-8", StatusCode = 200 };

    /// <summary>
    /// Path-traversal koruması: yalnız harf, rakam, tire ve alt çizgi; küçük harfe indir.
    /// Boş/geçersiz → null.
    /// </summary>
    private static string? Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim().ToLowerInvariant();
        foreach (var ch in trimmed)
        {
            var ok = (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '-' || ch == '_';
            if (!ok)
                return null;
        }

        return trimmed.Length is > 0 and <= 64 ? trimmed : null;
    }
}
