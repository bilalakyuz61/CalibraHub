using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Configuration;
using CalibraHub.Domain.Enums;
using CalibraHub.Infrastructure.Grafana.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CalibraHub.Infrastructure.Grafana;

// HTTP client tabanli Grafana provisioning. Tum metodlar idempotent ve
// hata-toleransli: HTTP basarisizliginda exception firlatmaz, log uyarir.
// Caller (login/setup flow'lari) bu sebeple bloke olmaz.
public sealed class GrafanaProvisioningService : IGrafanaProvisioningService
{
    private const string OrgIdHeader = "X-Grafana-Org-Id";

    private readonly HttpClient _http;
    private readonly GrafanaOptions _options;
    private readonly ILogger<GrafanaProvisioningService> _logger;

    public bool IsEnabled => _options.Enabled;

    public GrafanaProvisioningService(
        HttpClient http,
        IOptions<GrafanaOptions> options,
        ILogger<GrafanaProvisioningService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;

        if (_options.Enabled && !string.IsNullOrWhiteSpace(_options.Url))
        {
            _http.BaseAddress = new Uri(_options.Url.TrimEnd('/') + "/");
            var creds = $"{_options.AdminUser}:{_options.AdminPassword}";
            var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(creds));
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", b64);
        }
    }

    public async Task<int> EnsureOrganizationAsync(int companyId, string companyName, CancellationToken ct)
    {
        if (!_options.Enabled) return 0;

        var orgName = $"{_options.OrgNamePrefix}{companyId}";

        try
        {
            using var get = await _http.GetAsync($"api/orgs/name/{Uri.EscapeDataString(orgName)}", ct);
            if (get.IsSuccessStatusCode)
            {
                var existing = await get.Content.ReadFromJsonAsync<GrafanaOrgResponse>(cancellationToken: ct);
                if (existing is not null && existing.Id > 0)
                {
                    return existing.Id;
                }
            }

            var displayName = string.IsNullOrWhiteSpace(companyName) ? orgName : companyName;
            using var post = await _http.PostAsJsonAsync("api/orgs", new { name = orgName }, ct);
            if (post.IsSuccessStatusCode)
            {
                var created = await post.Content.ReadFromJsonAsync<GrafanaOrgCreatedResponse>(cancellationToken: ct);
                if (created is not null && created.OrgId > 0)
                {
                    _logger.LogInformation(
                        "[Grafana] Org olusturuldu: company={CompanyId} ({DisplayName}) orgId={OrgId}",
                        companyId, displayName, created.OrgId);
                    return created.OrgId;
                }
            }
            else
            {
                var body = await post.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "[Grafana] Org create basarisiz: company={CompanyId} status={Status} body={Body}",
                    companyId, post.StatusCode, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Grafana] Org provisioning hatasi: company={CompanyId}", companyId);
        }

        return 0;
    }

    public async Task EnsureDataSourceAsync(int orgId, string companyName, string connectionString, CancellationToken ct)
    {
        if (!_options.Enabled || orgId <= 0 || string.IsNullOrWhiteSpace(connectionString)) return;

        try
        {
            var (server, database, user, password, encrypt) = ParseConnectionString(connectionString);
            if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database))
            {
                _logger.LogWarning("[Grafana] Datasource icin gecerli connection string parse edilemedi: orgId={OrgId}", orgId);
                return;
            }

            var dsName = string.IsNullOrWhiteSpace(companyName) ? $"calibra-{orgId}" : companyName;

            // Mevcut datasource var mi?
            using (var existsReq = new HttpRequestMessage(HttpMethod.Get, $"api/datasources/name/{Uri.EscapeDataString(dsName)}"))
            {
                existsReq.Headers.Add(OrgIdHeader, orgId.ToString());
                using var existsResp = await _http.SendAsync(existsReq, ct);
                if (existsResp.IsSuccessStatusCode)
                {
                    return; // zaten var, idempotent
                }
            }

            // NOT: Grafana mssql datasource encrypt = "false" set ediliyor.
            //
            // Sebep: Microsoft.Data.SqlClient (4.0+) varsayilani Mandatory'e cevirdi —
            // connection string'de Encrypt belirtilmezse TLS zorunlu olur. Local SQL Express /
            // Developer Edition gecerli bir TLS sertifikasina sahip degildir, bu yuzden
            // Grafana mssql driver'i datasource'a baglanamaz ve "Page not found / connection
            // failed" gibi hatalar uretir.
            //
            // disable - hic sifrelenmez (tum trafik clear)
            // false   - sadece login paketi sifrelenir (SQL'in eski default davranisi) ←
            // true    - tum trafik sifrelenir (gecerli sertifika gerekir)
            //
            // Eski SQL Server (2008 R2 vs.) ve sertifikasiz kurulumlar icin "false" en pratik.
            // Eger production'da gercek sertifika varsa connection string'inde Encrypt=Mandatory
            // belirtilebilir; ileride app config'den override etme noktasi eklenebilir.
            var payload = new
            {
                name = dsName,
                type = "mssql",
                access = "proxy",
                url = server,
                database = database,
                user = user,
                isDefault = true,
                jsonData = new
                {
                    encrypt = "false",
                    tlsSkipVerify = true
                },
                secureJsonData = new
                {
                    password = password ?? string.Empty
                }
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, "api/datasources")
            {
                Content = JsonContent.Create(payload)
            };
            req.Headers.Add(OrgIdHeader, orgId.ToString());

            using var resp = await _http.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
            {
                _logger.LogInformation("[Grafana] Datasource olusturuldu: orgId={OrgId} name={Name}", orgId, dsName);
            }
            else
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "[Grafana] Datasource create basarisiz: orgId={OrgId} status={Status} body={Body}",
                    orgId, resp.StatusCode, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Grafana] Datasource provisioning hatasi: orgId={OrgId}", orgId);
        }
    }

    public async Task ProvisionDefaultDashboardsAsync(int orgId, CancellationToken ct)
    {
        if (!_options.Enabled || orgId <= 0) return;

        try
        {
            var assembly = typeof(GrafanaProvisioningService).Assembly;
            var resourceNames = assembly.GetManifestResourceNames()
                .Where(n => n.Contains(".Grafana.DefaultDashboards.", StringComparison.Ordinal)
                            && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            foreach (var resourceName in resourceNames)
            {
                await ImportSingleDashboardAsync(orgId, assembly, resourceName, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Grafana] Default dashboard provisioning hatasi: orgId={OrgId}", orgId);
        }
    }

    public async Task EnsureUserOrganizationMembershipAsync(
        int orgId,
        string username,
        string email,
        string fullName,
        GrafanaRole role,
        CancellationToken ct)
    {
        if (!_options.Enabled || orgId <= 0 || string.IsNullOrWhiteSpace(username)) return;

        try
        {
            // 1) Kullanici var mi? Yoksa admin API ile yarat (auto_sign_up zaten varsa,
            //    gercekte ilk login'de Grafana yaratir; pre-create idempotency icin ek katman).
            var userId = await EnsureGrafanaUserAsync(username, email, fullName, ct);
            if (userId <= 0) return;

            var roleStr = role switch
            {
                GrafanaRole.Admin => "Admin",
                GrafanaRole.Designer => "Editor",
                _ => "Viewer"
            };

            // 2) Org'a ekle (POST). Conflict durumunda PATCH ile rol guncelle.
            using var addReq = new HttpRequestMessage(HttpMethod.Post, $"api/orgs/{orgId}/users")
            {
                Content = JsonContent.Create(new { loginOrEmail = username, role = roleStr })
            };
            using var addResp = await _http.SendAsync(addReq, ct);

            var addedOrUpdated = false;

            if (addResp.IsSuccessStatusCode)
            {
                _logger.LogInformation("[Grafana] User org'a eklendi: orgId={OrgId} user={User} role={Role}",
                    orgId, username, roleStr);
                addedOrUpdated = true;
            }
            else if (addResp.StatusCode == HttpStatusCode.Conflict)
            {
                using var patchReq = new HttpRequestMessage(HttpMethod.Patch, $"api/orgs/{orgId}/users/{userId}")
                {
                    Content = JsonContent.Create(new { role = roleStr })
                };
                using var patchResp = await _http.SendAsync(patchReq, ct);
                if (patchResp.IsSuccessStatusCode)
                {
                    addedOrUpdated = true;
                }
                else
                {
                    var body = await patchResp.Content.ReadAsStringAsync(ct);
                    _logger.LogWarning(
                        "[Grafana] Org user role update basarisiz: orgId={OrgId} user={User} status={Status} body={Body}",
                        orgId, username, patchResp.StatusCode, body);
                }
            }
            else
            {
                var addBody = await addResp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "[Grafana] Org user add basarisiz: orgId={OrgId} user={User} status={Status} body={Body}",
                    orgId, username, addResp.StatusCode, addBody);
            }

            // 3) Main Org (id=1) sizintisini temizle — Grafana otomatik olarak
            //    yeni kullaniciyi auto_assign_org_id (1) icin Main Org'a ekler.
            //    Calibra_X'e ekledikten sonra Main Org'tan dusurmek lazim ki
            //    kullanici yalnizca kendi sirket org'unu gorebilsin. Hedef org
            //    zaten Main Org degilse — orgId == 1 ise no-op.
            if (addedOrUpdated && orgId != MainOrgId)
            {
                await RemoveUserFromMainOrgIfPresentAsync(userId, username, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Grafana] Org user provisioning hatasi: orgId={OrgId} user={User}", orgId, username);
        }
    }

    private const int MainOrgId = 1;

    /// <summary>
    /// Kullaniciyi Grafana'nin built-in "Main Org." org'undan (id=1) cikarir.
    /// 404 → kullanici Main Org'da yok, no-op. Diger hatalar log'a yazilir
    /// ama exception firlatilmaz (caller akisi bloklanmasin).
    /// </summary>
    private async Task RemoveUserFromMainOrgIfPresentAsync(int userId, string username, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Delete, $"api/orgs/{MainOrgId}/users/{userId}");
            using var resp = await _http.SendAsync(req, ct);

            if (resp.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "[Grafana] User Main Org'dan cikarildi: user={User} userId={UserId}",
                    username, userId);
                return;
            }

            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                // Main Org'da degil — beklenen durum eger auto_assign_org=false ise
                return;
            }

            var body = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "[Grafana] Main Org cleanup basarisiz: user={User} status={Status} body={Body}",
                username, resp.StatusCode, body);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Grafana] Main Org cleanup hatasi: user={User}", username);
        }
    }

    public async Task RemoveUserFromOrganizationAsync(int orgId, string username, CancellationToken ct)
    {
        if (!_options.Enabled || orgId <= 0 || string.IsNullOrWhiteSpace(username))
            return;

        try
        {
            // 1) Username -> Grafana userId resolve (lookup by login)
            using var lookupReq = new HttpRequestMessage(HttpMethod.Get, $"api/users/lookup?loginOrEmail={Uri.EscapeDataString(username)}");
            lookupReq.Headers.Add(OrgIdHeader, orgId.ToString());

            using var lookupResp = await _http.SendAsync(lookupReq, ct);
            if (lookupResp.StatusCode == System.Net.HttpStatusCode.NotFound)
                return; // Kullanici zaten yok — no-op

            if (!lookupResp.IsSuccessStatusCode)
            {
                _logger.LogWarning("[Grafana] User lookup basarisiz: user={User} status={Status}", username, lookupResp.StatusCode);
                return;
            }

            var lookupBody = await lookupResp.Content.ReadAsStringAsync(ct);
            var userJson = System.Text.Json.JsonDocument.Parse(lookupBody).RootElement;
            if (!userJson.TryGetProperty("id", out var idEl) || idEl.ValueKind != System.Text.Json.JsonValueKind.Number)
                return;
            var grafanaUserId = idEl.GetInt32();

            // 2) DELETE /api/orgs/{orgId}/users/{userId}
            using var delReq = new HttpRequestMessage(HttpMethod.Delete, $"api/orgs/{orgId}/users/{grafanaUserId}");
            using var delResp = await _http.SendAsync(delReq, ct);
            if (delResp.IsSuccessStatusCode)
            {
                _logger.LogInformation("[Grafana] User org'tan cikartildi: orgId={OrgId} user={User}", orgId, username);
            }
            else
            {
                _logger.LogWarning("[Grafana] User org'tan cikartma basarisiz: orgId={OrgId} user={User} status={Status}",
                    orgId, username, delResp.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Grafana] User remove hatasi: orgId={OrgId} user={User}", orgId, username);
        }
    }

    public async Task<IReadOnlyList<GrafanaDashboardSummary>> ListDashboardsAsync(int orgId, CancellationToken ct)
    {
        if (!_options.Enabled || orgId <= 0)
        {
            return Array.Empty<GrafanaDashboardSummary>();
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "api/search?type=dash-db");
            req.Headers.Add(OrgIdHeader, orgId.ToString());

            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "[Grafana] Dashboard list basarisiz: orgId={OrgId} status={Status} body={Body}",
                    orgId, resp.StatusCode, body);
                return Array.Empty<GrafanaDashboardSummary>();
            }

            var items = await resp.Content.ReadFromJsonAsync<GrafanaSearchItem[]>(cancellationToken: ct);
            if (items is null || items.Length == 0)
            {
                return Array.Empty<GrafanaDashboardSummary>();
            }

            var result = new List<GrafanaDashboardSummary>(items.Length);
            foreach (var it in items)
            {
                if (string.IsNullOrWhiteSpace(it.Uid) || string.IsNullOrWhiteSpace(it.Title)) continue;
                if (!string.Equals(it.Type, "dash-db", StringComparison.OrdinalIgnoreCase)) continue;

                result.Add(new GrafanaDashboardSummary(
                    Uid: it.Uid,
                    Title: it.Title,
                    FolderTitle: string.IsNullOrWhiteSpace(it.FolderTitle) ? null : it.FolderTitle,
                    Tags: it.Tags is { Length: > 0 } ? it.Tags : Array.Empty<string>(),
                    Url: it.Url ?? string.Empty));
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Grafana] Dashboard list hatasi: orgId={OrgId}", orgId);
            return Array.Empty<GrafanaDashboardSummary>();
        }
    }

    private async Task<int> EnsureGrafanaUserAsync(string username, string email, string fullName, CancellationToken ct)
    {
        try
        {
            using var lookup = await _http.GetAsync(
                $"api/users/lookup?loginOrEmail={Uri.EscapeDataString(username)}", ct);
            if (lookup.IsSuccessStatusCode)
            {
                var existing = await lookup.Content.ReadFromJsonAsync<GrafanaUserLookupResponse>(cancellationToken: ct);
                if (existing is not null && existing.Id > 0)
                {
                    return existing.Id;
                }
            }

            // POST /api/admin/users — pre-create. auto_sign_up zaten varsa bu cagri
            // genelde ilk login'den once tetiklenir, login'de Grafana mevcut user'i bulur.
            var randomPwd = Guid.NewGuid().ToString("N");
            using var createResp = await _http.PostAsJsonAsync(
                "api/admin/users",
                new
                {
                    name = string.IsNullOrWhiteSpace(fullName) ? username : fullName,
                    email = email ?? string.Empty,
                    login = username,
                    password = randomPwd
                },
                ct);
            if (createResp.IsSuccessStatusCode)
            {
                var created = await createResp.Content.ReadFromJsonAsync<GrafanaUserCreatedResponse>(cancellationToken: ct);
                return created?.Id ?? 0;
            }

            var body = await createResp.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("[Grafana] Admin user create basarisiz: user={User} status={Status} body={Body}",
                username, createResp.StatusCode, body);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Grafana] User ensure hatasi: user={User}", username);
        }

        return 0;
    }

    private async Task ImportSingleDashboardAsync(int orgId, Assembly assembly, string resourceName, CancellationToken ct)
    {
        try
        {
            await using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                _logger.LogWarning("[Grafana] Dashboard resource bulunamadi: {Resource}", resourceName);
                return;
            }

            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var dashboardJson = JsonNode.Parse(doc.RootElement.GetRawText())!;

            var payload = new JsonObject
            {
                ["dashboard"] = dashboardJson,
                ["folderUid"] = "",
                ["overwrite"] = true,
                ["message"] = "CalibraHub default dashboard provisioning"
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, "api/dashboards/db")
            {
                Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
            };
            req.Headers.Add(OrgIdHeader, orgId.ToString());

            using var resp = await _http.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
            {
                _logger.LogInformation("[Grafana] Dashboard import: orgId={OrgId} resource={Resource}", orgId, resourceName);
            }
            else
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "[Grafana] Dashboard import basarisiz: orgId={OrgId} resource={Resource} status={Status} body={Body}",
                    orgId, resourceName, resp.StatusCode, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Grafana] Dashboard import hatasi: orgId={OrgId} resource={Resource}",
                orgId, resourceName);
        }
    }

    private static (string Server, string Database, string User, string? Password, bool Encrypt) ParseConnectionString(string connectionString)
    {
        var b = new SqlConnectionStringBuilder(connectionString);
        var server = b.DataSource ?? string.Empty;
        var db = b.InitialCatalog ?? string.Empty;
        var user = b.IntegratedSecurity ? string.Empty : (b.UserID ?? string.Empty);
        var pwd = b.IntegratedSecurity ? null : b.Password;
        var encrypt = b.Encrypt != SqlConnectionEncryptOption.Optional;
        return (server, db, user, pwd, encrypt);
    }
}
