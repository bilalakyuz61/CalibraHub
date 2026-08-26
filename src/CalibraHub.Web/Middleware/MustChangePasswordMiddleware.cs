namespace CalibraHub.Web.Middleware;

/// <summary>
/// Geçici parolayla açılmış hesabı, parolasını değiştirene kadar uygulamada
/// gezinmekten alıkoyar.
///
/// <para><b>Neden (2026-08-24 güvenlik denetimi, K4 tamamlayıcısı):</b> Yönetici
/// yeni kullanıcı oluşturduğunda veya parola sıfırladığında rastgele bir geçici
/// parola üretiliyor ve kullanıcıya iletiliyor. Bu parola çoğu zaman e-posta /
/// WhatsApp / sözlü olarak paylaşılıyor, yani <b>üçüncü bir kanalda</b> duruyor.
/// Değiştirilmesi zorunlu olmadığı için hesaplar aylarca bu parolayla kalabiliyordu.</para>
///
/// <para><b>Davranış:</b> <c>must_change_password</c> claim'i taşıyan oturum, yalnızca
/// parola değiştirme ekranına, çıkışa ve statik içeriğe erişebilir. API/JSON istekleri
/// yönlendirilmez — 403 + açıklayıcı gövde döner (redirect alan bir fetch, HTML'i JSON
/// sanıp anlamsız hata gösterirdi).</para>
///
/// <para><b>Bilinçli sınır:</b> claim yalnızca girişte kurulur. Yönetici, ÇALIŞAN bir
/// oturumu olan kullanıcının parolasını sıfırlarsa zorunluluk o kullanıcının bir sonraki
/// girişinde devreye girer. Anında etki için oturumun düşürülmesi gerekir; bu, mevcut
/// oturum modelinin (cookie, sunucu tarafı oturum deposu yok) doğal sonucudur.</para>
/// </summary>
public sealed class MustChangePasswordMiddleware
{
    private readonly RequestDelegate _next;

    public MustChangePasswordMiddleware(RequestDelegate next) => _next = next;

    // Zorunluluk sırasında bile erişilebilir yollar.
    private static readonly string[] AllowedPrefixes =
    {
        "/Account/ChangePassword",
        "/Account/Logout",
        "/Account/Login",
        "/css/", "/js/", "/lib/", "/react/", "/images/", "/img/", "/fonts/", "/favicon",
    };

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User?.Identity?.IsAuthenticated == true
            && context.User.HasClaim("must_change_password", "1"))
        {
            var path = context.Request.Path.HasValue ? context.Request.Path.Value! : "/";
            var allowed = AllowedPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
            if (!allowed)
            {
                var wantsJson = path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(context.Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
                    || (context.Request.Headers.Accept.ToString()?.Contains("application/json", StringComparison.OrdinalIgnoreCase) ?? false);

                if (wantsJson)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/json; charset=utf-8";
                    await context.Response.WriteAsync(
                        "{\"ok\":false,\"error\":\"Devam etmek icin sifrenizi degistirmelisiniz.\",\"redirect\":\"/Account/ChangePassword\"}");
                    return;
                }

                context.Response.Redirect("/Account/ChangePassword");
                return;
            }
        }

        await _next(context);
    }
}
