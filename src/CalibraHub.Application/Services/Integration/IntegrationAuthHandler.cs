using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Domain.Entities;
using Microsoft.Extensions.Caching.Memory;

namespace CalibraHub.Application.Services.Integration;

/// <summary>
/// IIntegrationAuthHandler implementasyonu — UI'daki tüm AuthType degerlerini
/// (None / OAuth2Password / BearerStatic / BasicAuth / ApiKey) destekler ve
/// OAuth2Password icin token caching yapar.
///
/// Netsis NetOpenX REST entegrasyonu icin ozel: extraFields icindeki
/// branchcode/dbname/dbuser/dbpassword/dbtype degerleri token POST'unun
/// form body'sine eklenir — Netsis bu alanlarsiz token uretmez.
///
/// Token cache anahtari: "intauth:oauth2:{profileId}". TTL: token endpoint'in
/// expires_in alanindan -60 saniye (early refresh icin) veya 5dk default.
/// </summary>
public sealed class IntegrationAuthHandler : IIntegrationAuthHandler
{
    private const string CacheKeyPrefix = "intauth:oauth2:";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;

    public IntegrationAuthHandler(IHttpClientFactory httpClientFactory, IMemoryCache cache)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
    }

    public async Task ApplyAuthAsync(
        HttpRequestMessage request,
        IntegrationApiProfile profile,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(profile);

        var authType = (profile.AuthType ?? "None").Trim();
        if (string.IsNullOrEmpty(authType) || string.Equals(authType, "None", StringComparison.OrdinalIgnoreCase))
            return;

        switch (authType.ToLowerInvariant())
        {
            case "oauth2password":
                await ApplyOAuth2PasswordAsync(request, profile, ct).ConfigureAwait(false);
                break;
            case "bearer":
            case "bearerstatic":
                ApplyBearerStatic(request, profile);
                break;
            case "basic":
            case "basicauth":
                ApplyBasic(request, profile);
                break;
            case "apikey":
                ApplyApiKey(request, profile);
                break;
            default:
                // Bilinmeyen tip — sessizce skip (V2'de log/warn)
                break;
        }
    }

    public void InvalidateToken(Guid profileId)
        => _cache.Remove(CacheKeyPrefix + profileId);

    // ── OAuth2 Password ─────────────────────────────────────────────────

    private async Task ApplyOAuth2PasswordAsync(
        HttpRequestMessage request, IntegrationApiProfile profile, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(profile.AuthConfigJson))
            throw new InvalidOperationException("OAuth2Password: AuthConfigJson zorunlu.");

        // Cache hit — direkt Bearer ekle
        var cacheKey = CacheKeyPrefix + profile.Id;
        if (_cache.TryGetValue<string>(cacheKey, out var cached) && !string.IsNullOrEmpty(cached))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cached);
            return;
        }

        // Token al — cache'e koy — Bearer ekle
        var token = await FetchOAuth2TokenAsync(profile, ct).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>
    /// OAuth2 token endpoint'ine POST atip access_token doner. Cache'e de yazar.
    /// extraFields (Netsis: branchcode, dbname, dbuser, dbpassword, dbtype)
    /// form body'sine eklenir.
    /// </summary>
    private async Task<string> FetchOAuth2TokenAsync(
        IntegrationApiProfile profile, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(profile.AuthConfigJson!);
        var root = doc.RootElement;

        var tokenEndpoint = ReadString(root, "tokenEndpoint");
        if (string.IsNullOrWhiteSpace(tokenEndpoint))
            throw new InvalidOperationException("OAuth2Password: tokenEndpoint zorunlu.");

        var grantType = ReadString(root, "grantType") ?? "password";
        var username  = ReadString(root, "username") ?? string.Empty;
        var password  = ReadString(root, "password") ?? string.Empty;
        var tokenField = ReadString(root, "tokenField") ?? "access_token";

        var tokenUrl = (profile.BaseUrl ?? string.Empty).TrimEnd('/')
                     + "/" + tokenEndpoint!.TrimStart('/');

        // Form-urlencoded body
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = grantType!,
            ["username"]   = username,
            ["password"]   = password,
        };

        // extraFields (Netsis NetOpenX: branchcode, dbname, dbuser, dbpassword, dbtype)
        if (root.TryGetProperty("extraFields", out var extra) && extra.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in extra.EnumerateObject())
            {
                form[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString() ?? string.Empty,
                    JsonValueKind.Number => prop.Value.GetRawText(),
                    JsonValueKind.True   => "true",
                    JsonValueKind.False  => "false",
                    JsonValueKind.Null   => string.Empty,
                    _                    => prop.Value.GetRawText(),
                };
            }
        }

        using var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        using var resp = await client.PostAsync(
            tokenUrl, new FormUrlEncodedContent(form), ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"OAuth2Password token alinamadi (HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}). " +
                $"URL: {tokenUrl} — Cevap: {Truncate(body, 400)}");
        }

        // Token + expires_in parse
        string? token = null;
        int? expiresIn = null;
        try
        {
            using var tokenDoc = JsonDocument.Parse(body);
            if (tokenDoc.RootElement.TryGetProperty(tokenField, out var tEl))
                token = tEl.GetString();
            if (tokenDoc.RootElement.TryGetProperty("expires_in", out var ex))
            {
                expiresIn = ex.ValueKind switch
                {
                    JsonValueKind.Number => ex.GetInt32(),
                    JsonValueKind.String when int.TryParse(ex.GetString(), out var v) => v,
                    _ => null,
                };
            }
        }
        catch (JsonException jex)
        {
            throw new InvalidOperationException(
                $"OAuth2Password: token cevabi gecerli JSON degil. {jex.Message} — Cevap: {Truncate(body, 400)}");
        }

        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException(
                $"OAuth2Password: cevapta '{tokenField}' bulunamadi/bos. Cevap: {Truncate(body, 400)}");

        // Cache TTL: expires_in - 60s (erken refresh) veya 5dk default
        var ttl = expiresIn.HasValue
            ? TimeSpan.FromSeconds(Math.Max(60, expiresIn.Value - 60))
            : TimeSpan.FromMinutes(5);
        _cache.Set(CacheKeyPrefix + profile.Id, token!, ttl);

        return token!;
    }

    // ── Bearer (sabit) ──────────────────────────────────────────────────

    private static void ApplyBearerStatic(HttpRequestMessage req, IntegrationApiProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.AuthConfigJson)) return;
        try
        {
            using var doc = JsonDocument.Parse(profile.AuthConfigJson);
            var token = ReadString(doc.RootElement, "token");
            if (!string.IsNullOrWhiteSpace(token))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token!);
        }
        catch (JsonException) { /* gecersiz config — skip */ }
    }

    // ── Basic ───────────────────────────────────────────────────────────

    private static void ApplyBasic(HttpRequestMessage req, IntegrationApiProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.AuthConfigJson)) return;
        try
        {
            using var doc = JsonDocument.Parse(profile.AuthConfigJson);
            var user = ReadString(doc.RootElement, "username");
            var pwd  = ReadString(doc.RootElement, "password");
            if (!string.IsNullOrWhiteSpace(user))
            {
                var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pwd ?? string.Empty}"));
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
            }
        }
        catch (JsonException) { /* gecersiz config — skip */ }
    }

    // ── ApiKey ──────────────────────────────────────────────────────────

    private static void ApplyApiKey(HttpRequestMessage req, IntegrationApiProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.AuthConfigJson)) return;
        try
        {
            using var doc = JsonDocument.Parse(profile.AuthConfigJson);
            var root = doc.RootElement;
            // UI yeni: apiKeyHeader/apiKeyValue — Backend eski: headerName/key (geriye uyum)
            var headerName = ReadString(root, "apiKeyHeader") ?? ReadString(root, "headerName") ?? "X-API-Key";
            var key        = ReadString(root, "apiKeyValue")  ?? ReadString(root, "key");
            if (!string.IsNullOrWhiteSpace(headerName) && !string.IsNullOrWhiteSpace(key))
                req.Headers.TryAddWithoutValidation(headerName!, key!);
        }
        catch (JsonException) { /* gecersiz config — skip */ }
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static string? ReadString(JsonElement obj, string prop)
        => obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
           ? v.GetString() : null;

    private static string Truncate(string? s, int max)
        => string.IsNullOrEmpty(s) ? string.Empty
         : (s.Length > max ? s.Substring(0, max) + "..." : s);
}
