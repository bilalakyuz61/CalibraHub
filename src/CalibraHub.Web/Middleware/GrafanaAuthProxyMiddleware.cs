using System.Security.Claims;
using System.Text;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Configuration;
using CalibraHub.Application.Abstractions.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CalibraHub.Web.Middleware;

// Grafana auth.proxy mode: claim'lerden uretilen X-WEBAUTH-* header'larini
// reverse-proxy'ye giden istek baslarina basar. Boylece Grafana login ekrani
// gostermez, kullanici CalibraHub cookie'sinden tek-oturum acilir.
//
// Org binding: companyId -> orgId mapping runtime'da resolve edilir
// (IGrafanaProvisioningService.EnsureOrganizationAsync). Sonuc 10 dakika
// IMemoryCache'te tutulur — her request icin Grafana'ya HTTP cagirmaktan kacinilir.
// Bu sayede kullanici **direkt Calibra_{companyId} org'unda** baslar; Grafana'nin
// default davranisi olan "her user icin kendi org'u" tetiklenmez.
public sealed class GrafanaAuthProxyMiddleware
{
    private const string UserHeader = "X-WEBAUTH-USER";
    private const string EmailHeader = "X-WEBAUTH-EMAIL";
    private const string NameHeader = "X-WEBAUTH-NAME";
    private const string OrgIdHeader = "X-Grafana-Org-Id";

    private readonly RequestDelegate _next;
    private readonly GrafanaOptions _options;

    public GrafanaAuthProxyMiddleware(RequestDelegate next, IOptions<GrafanaOptions> options)
    {
        _next = next;
        _options = options.Value;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IGrafanaProvisioningService provisioning,
        ICompanyRepository companyRepo,
        IMemoryCache cache)
    {
        if (!_options.Enabled)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsync("Grafana entegrasyonu devre disi.");
            return;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            // YARP'a vermeden once: cookie auth challenge -> /Account/Login redirect.
            await context.ChallengeAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }

        var companyIdStr = context.User.FindFirstValue("company_id");
        if (string.IsNullOrWhiteSpace(companyIdStr) || !int.TryParse(companyIdStr, out var companyId))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var headers = context.Request.Headers;

        // ── Username — okunabilir tercih sirasi ─────────────────────────────
        // 1) email (admin@calibra.local) — Grafana'da auto_sign_up ile dogal user adi
        // 2) employee_code (ADM-001) — kurumsal kimlik
        // 3) UUID fallback (company_X_user_<guid>) — sadece email/code yoksa
        var email = context.User.FindFirstValue(ClaimTypes.Email);
        var employeeCode = context.User.FindFirstValue("employee_code");
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        string username;
        if (!string.IsNullOrWhiteSpace(email))
            username = email;
        else if (!string.IsNullOrWhiteSpace(employeeCode))
            username = employeeCode;
        else
            username = $"company_{companyId}_user_{userId ?? "unknown"}";
        // .NET HttpConnection HTTP/1.1 header'larini ASCII (0-127) olarak yazar; non-ASCII
        // (Turkce ÇĞİÖŞÜ, akut/diaresis, vb.) HttpRequestException firlatir → upstream'a
        // hic ulasamadan YARP 502 doner. Bu nedenle tum auth header'lari fold ediliyor.
        headers[UserHeader] = ToAsciiHeader(username);

        if (!string.IsNullOrWhiteSpace(email))
            headers[EmailHeader] = ToAsciiHeader(email);

        var fullName = context.User.FindFirstValue(ClaimTypes.Name);
        if (!string.IsNullOrWhiteSpace(fullName))
            headers[NameHeader] = ToAsciiHeader(fullName);

        // ── Org id — companyId -> orgId mapping (10 dk cache) ───────────────
        // Cache miss'te company adini repo'dan al, EnsureOrganizationAsync ile
        // Grafana'da org yarat veya bul, orgId'yi cache'le. Boylece kullanici
        // **her zaman dogru org'da** baslar; switch dropdown sade kalir.
        var cacheKey = $"grafana_org_id_company_{companyId}";
        if (!cache.TryGetValue<int>(cacheKey, out var orgId) || orgId <= 0)
        {
            try
            {
                var company = await companyRepo.GetByIdAsync(companyId, context.RequestAborted);
                var companyName = company?.Name?.Trim();
                if (!string.IsNullOrWhiteSpace(companyName))
                {
                    orgId = await provisioning.EnsureOrganizationAsync(companyId, companyName, context.RequestAborted);
                    if (orgId > 0)
                        cache.Set(cacheKey, orgId, TimeSpan.FromMinutes(10));
                }
            }
            catch
            {
                // Provisioning fail olursa header gonderme — Grafana default davraniri yapar.
                orgId = 0;
            }
        }

        if (orgId > 0)
            headers[OrgIdHeader] = orgId.ToString();

        await _next(context);
    }

    /// <summary>
    /// Non-ASCII karakterleri ASCII karsiliklarina cevirir. Turkce + yaygin Latin
    /// aksanli karakterler eslenir; diger non-ASCII '?' olur. HTTP/1.1 header'larinda
    /// .NET sadece 0-127 kabul ettigi icin auth header'lari bu fold'dan gecmek zorundadir.
    /// </summary>
    private static string ToAsciiHeader(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? string.Empty;

        // Hizli yol: zaten ASCII ise kopyalama yapma.
        var hasNonAscii = false;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] > 127) { hasNonAscii = true; break; }
        }
        if (!hasNonAscii) return value;

        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch < 128) { sb.Append(ch); continue; }
            sb.Append(FoldChar(ch));
        }
        return sb.ToString();
    }

    private static string FoldChar(char ch) => ch switch
    {
        // Turkce
        'Ç' => "C", 'Ğ' => "G", 'İ' => "I", 'Ö' => "O", 'Ş' => "S", 'Ü' => "U",
        'ç' => "c", 'ğ' => "g", 'ı' => "i", 'ö' => "o", 'ş' => "s", 'ü' => "u",
        // Latin Extended (yaygin Avrupa)
        'Â' => "A", 'Á' => "A", 'À' => "A", 'Ä' => "A", 'Å' => "A", 'Ã' => "A", 'Æ' => "AE",
        'â' => "a", 'á' => "a", 'à' => "a", 'ä' => "a", 'å' => "a", 'ã' => "a", 'æ' => "ae",
        'É' => "E", 'È' => "E", 'Ê' => "E", 'Ë' => "E",
        'é' => "e", 'è' => "e", 'ê' => "e", 'ë' => "e",
        'Î' => "I", 'Í' => "I", 'Ì' => "I", 'Ï' => "I",
        'î' => "i", 'í' => "i", 'ì' => "i", 'ï' => "i",
        'Ó' => "O", 'Ò' => "O", 'Ô' => "O", 'Õ' => "O", 'Ø' => "O", 'Œ' => "OE",
        'ó' => "o", 'ò' => "o", 'ô' => "o", 'õ' => "o", 'ø' => "o", 'œ' => "oe",
        'Û' => "U", 'Ú' => "U", 'Ù' => "U",
        'û' => "u", 'ú' => "u", 'ù' => "u",
        'Ñ' => "N", 'ñ' => "n",
        'Ý' => "Y", 'ý' => "y", 'ÿ' => "y",
        'ß' => "ss",
        _ => "?",
    };
}
