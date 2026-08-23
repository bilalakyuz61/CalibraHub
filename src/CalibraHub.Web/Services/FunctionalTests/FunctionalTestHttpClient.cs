using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CalibraHub.Web.Services.FunctionalTests;

/// <summary>
/// Fonksiyon testleri gerçek HTTP uçlarına, test kullanıcısının oturum çerezi ile istek atar
/// (bkz. HealthCheckController.StreamTestCompany → LoginAsTestUserAsync ile aynı yaklaşım —
/// servis katmanı doğrudan çağrılmaz, controller/yetki/doğrulama katmanı da sınanır).
///
/// CSRF: Program.cs global <c>AutoValidateAntiforgeryTokenAttribute</c> filtresi TÜM POST
/// uçlarını korur. Token, <c>?workspace=1</c> query'li herhangi bir authenticated sayfadan
/// (bkz. Views/Shared/_Layout.cshtml "isWorkspaceFrame" dalı → gizli
/// <c>#workspaceAntiforgeryForm</c> + <c>@Html.AntiForgeryToken()</c>) okunur, sonraki tüm
/// POST'larda <c>RequestVerificationToken</c> header'ı olarak gönderilir (Program.cs
/// <c>AddAntiforgery(o => o.HeaderName = "RequestVerificationToken")</c>).
///
/// Bilinçli tasarım kararı: bu istemci yanıtları <see cref="JsonElement"/> olarak "black box"
/// ayrıştırır — hiçbir Web katmanı DTO/record tipine (SaveDocumentRequest, CreateTransferRequest
/// vb.) derleme zamanı bağımlılık kurmaz. Amaç: senaryolar gerçek JSON kontratını (frontend'in
/// gördüğü kontratla birebir) sınasın; controller içi tip değişikliği testin de otomatik uyum
/// sağlamasını beklemesin — bir sözleşme kırılırsa test de kırılmalı.
/// </summary>
public sealed class FunctionalTestHttpClient : IDisposable
{
    private static readonly JsonSerializerOptions WriteOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly Regex CsrfRegex = new(
        "name=\"__RequestVerificationToken\"[^>]+value=\"([^\"]+)\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HttpClient _client;
    private readonly string _baseUrl;
    private string _csrfToken = "";

    private FunctionalTestHttpClient(HttpClient client, string baseUrl)
    {
        _client = client;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    /// <summary>
    /// Test kullanıcısının cookie header'ından (HealthCheckController.LoginAsTestUserAsync
    /// çıktısı) bir CookieContainer kurar, ardından <c>?workspace=1</c> sayfasından CSRF
    /// token'ı çeker. Token alınamazsa (oturum geçersiz/sayfa değişmiş) null + hata döner —
    /// çağıran (controller) bunu fn_error olarak raporlar.
    /// </summary>
    public static async Task<(FunctionalTestHttpClient? Client, string? Error)> CreateAsync(
        string baseUrl, string sourceCookieHeader, CancellationToken ct)
    {
        var uri = new Uri(baseUrl);
        var cookieContainer = new CookieContainer();
        foreach (var part in (sourceCookieHeader ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = part.IndexOf('=');
            if (idx <= 0) continue;
            var name = part[..idx].Trim();
            var value = part[(idx + 1)..].Trim();
            try { cookieContainer.Add(uri, new Cookie(name, value)); }
            catch { /* Gecersiz cookie adi/karakteri — bu tekil cerez atlanir, digerleri etkilenmez. */ }
        }

        var handler = new HttpClientHandler
        {
            CookieContainer = cookieContainer,
            UseCookies = true,
            AllowAutoRedirect = false,
        };
        var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        var instance = new FunctionalTestHttpClient(httpClient, baseUrl);

        try
        {
            using var resp = await httpClient.GetAsync($"{instance._baseUrl}/Admin/HealthCheck?workspace=1", ct);
            var html = await resp.Content.ReadAsStringAsync(ct);
            var match = CsrfRegex.Match(html);
            if (!match.Success)
            {
                httpClient.Dispose();
                return (null, "CSRF token bulunamadı (workspace sayfası okunamadı — oturum geçersiz olabilir).");
            }
            instance._csrfToken = match.Groups[1].Value;
        }
        catch (Exception ex)
        {
            httpClient.Dispose();
            return (null, "CSRF token alınamadı: " + ex.Message);
        }
        return (instance, null);
    }

    /// <summary>POST + JSON gövde + business-ok zarf kontrolü tek adımda. Hata durumunda Ok=false + okunabilir mesaj.</summary>
    public async Task<FunctionalTestApiResult> PostAsync(string path, object? body, CancellationToken ct)
    {
        var (httpOk, json, err) = await SendJsonAsync(HttpMethod.Post, path, body, ct);
        return ToApiResult(httpOk, json, err);
    }

    /// <summary>GET + business-ok zarf kontrolü. Zarfsız (düz liste/nesne) yanıtlarda Ok=true varsayılır.</summary>
    public async Task<FunctionalTestApiResult> GetAsync(string path, CancellationToken ct)
    {
        var (httpOk, json, err) = await SendJsonAsync(HttpMethod.Get, path, null, ct);
        return ToApiResult(httpOk, json, err);
    }

    private static FunctionalTestApiResult ToApiResult(bool httpOk, JsonElement json, string? err)
    {
        if (!httpOk || err != null)
            return new FunctionalTestApiResult(false, json, err ?? "HTTP isteği başarısız.");
        var ok = json.IsBusinessOk(out var msg);
        return new FunctionalTestApiResult(ok, json, ok ? null : (msg ?? "İşlem başarısız (sunucu neden bildirmedi)."));
    }

    private async Task<(bool HttpOk, JsonElement Json, string? Error)> SendJsonAsync(
        HttpMethod method, string path, object? body, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(method, _baseUrl + path);
            if (method != HttpMethod.Get)
            {
                req.Headers.TryAddWithoutValidation("RequestVerificationToken", _csrfToken);
                var payloadJson = JsonSerializer.Serialize(body ?? new { }, WriteOpts);
                req.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
            }
            using var resp = await _client.SendAsync(req, ct);
            var text = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return (false, default, $"HTTP {(int)resp.StatusCode}: {Truncate(text)}");
            try
            {
                return (true, JsonDocument.Parse(text).RootElement.Clone(), null);
            }
            catch
            {
                return (true, default, "Yanıt JSON olarak ayrıştırılamadı: " + Truncate(text));
            }
        }
        catch (Exception ex)
        {
            return (false, default, ex.Message);
        }
    }

    private static string Truncate(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length > 300 ? s[..300] + "…" : s;
    }

    public void Dispose() => _client.Dispose();
}

/// <summary>Tek bir HTTP çağrısının sonucu — Ok=false ise Error kullanıcıya/rapora aynen yansıtılır.</summary>
public sealed record FunctionalTestApiResult(bool Ok, JsonElement Json, string? Error);
