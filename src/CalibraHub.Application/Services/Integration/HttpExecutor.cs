using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Services.Integration;

/// <summary>
/// IHttpExecutor implementasyonu. IHttpClientFactory ile yeni HttpClient acar,
/// IIntegrationAuthHandler'a auth header'larını yaptırır, hedef endpoint'e HTTP request gönderir.
///
/// 401 retry: Token expired olabileceginden, 401 alınca token cache invalidate edip
/// 1 retry yapilir. 2. cevap 401 ise gercekten yetkisizdir, hata olarak doner.
///
/// Hata yakalama: network/timeout/parse hatalari Success=false ile return — exception throw etmez
/// (audit log'a temiz yazilsin). Sadece IntegrationApiProfile null gibi caller-input
/// hatalarinda ArgumentException atilir.
/// </summary>
public sealed class HttpExecutor : IHttpExecutor
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IIntegrationAuthHandler _auth;

    public HttpExecutor(IHttpClientFactory httpClientFactory, IIntegrationAuthHandler auth)
    {
        _httpClientFactory = httpClientFactory;
        _auth = auth;
    }

    public async Task<HttpInvocationResult> SendAsync(
        IntegrationEndpoint endpoint,
        IntegrationApiProfile profile,
        JsonObject body,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(body);

        // BaseUrl + UrlTemplate birlestir
        var url = (profile.BaseUrl ?? string.Empty).TrimEnd('/')
                  + "/" + (endpoint.UrlTemplate ?? string.Empty).TrimStart('/');

        // Body'i JSON'a serialize et
        var bodyText = body.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        var method   = (endpoint.HttpMethod ?? "POST").ToUpperInvariant();

        var sw = Stopwatch.StartNew();
        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(60);

            // 1. Deneme
            var (resp, respBody) = await SendWithAuthAsync(client, method, url, bodyText, profile, ct)
                .ConfigureAwait(false);

            // 401 → token expired olabilir; cache invalidate + 1 retry
            if (resp.StatusCode == HttpStatusCode.Unauthorized)
            {
                resp.Dispose();
                _auth.InvalidateToken(profile.Id);
                (resp, respBody) = await SendWithAuthAsync(client, method, url, bodyText, profile, ct)
                    .ConfigureAwait(false);
            }

            using (resp)
            {
                sw.Stop();
                return new HttpInvocationResult
                {
                    Success      = resp.IsSuccessStatusCode,
                    StatusCode   = (int)resp.StatusCode,
                    RequestBody  = bodyText,
                    ResponseBody = respBody,
                    ErrorMessage = resp.IsSuccessStatusCode ? null
                                                            : $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}",
                    DurationMs   = (int)sw.ElapsedMilliseconds,
                };
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            return new HttpInvocationResult
            {
                Success      = false,
                StatusCode   = null,
                RequestBody  = bodyText,
                ResponseBody = null,
                ErrorMessage = "Request timeout (60s)",
                DurationMs   = (int)sw.ElapsedMilliseconds,
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new HttpInvocationResult
            {
                Success      = false,
                StatusCode   = null,
                RequestBody  = bodyText,
                ResponseBody = null,
                ErrorMessage = ex.Message,
                DurationMs   = (int)sw.ElapsedMilliseconds,
            };
        }
    }

    /// <summary>
    /// Tek bir HTTP cagri: request olustur → auth ekle → gonder → body oku.
    /// 401 retry icin ayri method (her seferinde yeni HttpRequestMessage gerek).
    /// </summary>
    private async Task<(HttpResponseMessage Response, string Body)> SendWithAuthAsync(
        HttpClient client, string method, string url, string bodyText,
        IntegrationApiProfile profile, CancellationToken ct)
    {
        var req = new HttpRequestMessage(new HttpMethod(method), url);

        // Body — GET/DELETE haricinde
        if (method is "POST" or "PUT" or "PATCH")
            req.Content = new StringContent(bodyText, Encoding.UTF8, "application/json");

        // Auth header (OAuth2Password ise token endpoint'e gidip alabilir — async)
        await _auth.ApplyAuthAsync(req, profile, ct).ConfigureAwait(false);

        var resp = await client.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return (resp, body);
    }
}
