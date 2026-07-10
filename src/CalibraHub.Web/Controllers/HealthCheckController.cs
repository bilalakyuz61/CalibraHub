using System.Diagnostics;
using System.Net;
using System.Text.Json;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Security;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using CalibraHub.Web.Models.Diagnostics;
using CalibraHub.Web.Models.Navigation;
using CalibraHub.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// 2026-05-26 — Sistem Saglik Kontrolu sayfasi.
/// Tum menu URL'lerini server-side HttpClient ile tek tek dener,
/// HTTP status + sure + hata mesajini doner. Frontend tablo halinde gosterir.
/// </summary>
[Authorize]
[CalibraHub.Web.Authorization.PermissionScope(CalibraHub.Application.Constants.FormCodes.SetupDefinitions)]
public sealed class HealthCheckController : Controller
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<HealthCheckController> _logger;
    private readonly SchemaProbeService _schemaProbe;
    private readonly IAdminManagementService _adminManagement;
    private readonly ICompanyRepository _companyRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly CalibraDatabaseInitializer _dbInitializer;
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _schema;

    public HealthCheckController(
        IHttpClientFactory httpFactory,
        IHttpContextAccessor httpContextAccessor,
        ILogger<HealthCheckController> logger,
        SchemaProbeService schemaProbe,
        IAdminManagementService adminManagement,
        ICompanyRepository companyRepository,
        IDepartmentRepository departmentRepository,
        CalibraDatabaseInitializer dbInitializer,
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions dbOptions)
    {
        _httpFactory = httpFactory;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        _schemaProbe = schemaProbe;
        _adminManagement = adminManagement;
        _companyRepository = companyRepository;
        _departmentRepository = departmentRepository;
        _dbInitializer = dbInitializer;
        _connectionFactory = connectionFactory;
        _schema = string.IsNullOrWhiteSpace(dbOptions.Schema) ? "dbo" : dbOptions.Schema.Trim();
    }

    [HttpGet("/Admin/HealthCheck")]
    public IActionResult Index() => View();

    /// <summary>
    /// Tum menu URL'lerini iterate eder, her birine HTTP GET atar.
    /// Auth cookie'yi forward eder ki authenticated endpoint'ler 200 donsun.
    /// </summary>
    [HttpPost("/Admin/HealthCheck/Run")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Run(CancellationToken ct)
    {
        var (checks, client, baseUrl, cookieHeader) = PrepareRun();

        var results = new List<CheckResult>();
        foreach (var target in checks)
            results.Add(await RunSingleAsync(client, baseUrl, cookieHeader, target, ct));

        // Altyapı / Şema derinlik kontrolleri (menü URL smoke'unun kapsamadığı)
        await using (var infraConn = await TryOpenAsync(ct))
            foreach (var spec in BuildInfraSpecs())
                results.Add(await RunInfraAsync(spec, infraConn, ct));

        var summary = new
        {
            total      = results.Count,
            ok         = results.Count(r => r.Status == "ok"),
            redirect   = results.Count(r => r.Status == "redirect"),
            warn       = results.Count(r => r.Status == "warn"),
            error      = results.Count(r => r.Status == "error"),
            exception  = results.Count(r => r.Status == "exception"),
            durationMs = results.Sum(r => r.DurationMs),
        };

        return Json(new { ok = true, summary, results });
    }

    /// <summary>
    /// NDJSON streaming: her satir bir frame.
    /// Frame tipleri:
    ///   - { type:"start", total }
    ///   - { type:"checking", index, total, label, parentLabel, path }
    ///   - { type:"result",   index, total, result }
    ///   - { type:"done",     summary }
    /// Frontend her satiri parse edip "su an X kontrol ediliyor" gosterimini canli gunceller.
    /// </summary>
    [HttpPost("/Admin/HealthCheck/Stream")]
    [ValidateAntiForgeryToken]
    public async Task Stream(CancellationToken ct)
    {
        Response.ContentType = "application/x-ndjson; charset=utf-8";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no"; // nginx vb. proxy buffer'i devre disi
        Response.StatusCode = 200;

        var (checks, client, baseUrl, cookieHeader) = PrepareRun();
        var infraSpecs = BuildInfraSpecs();
        var total = checks.Count + infraSpecs.Count;
        var results = new List<CheckResult>(total);

        await WriteFrameAsync(new { type = "start", total }, ct);

        for (var i = 0; i < checks.Count; i++)
        {
            var target = checks[i];
            await WriteFrameAsync(new
            {
                type        = "checking",
                index       = i + 1,
                total,
                label       = target.Label,
                parentLabel = target.ParentLabel,
                path        = target.Path,
            }, ct);

            var result = await RunSingleAsync(client, baseUrl, cookieHeader, target, ct);

            // Schema probe: registry'de tanım varsa INSERT...ROLLBACK testi yap
            SchemaProbeResult? schemaProbe = null;
            var probeDef = SchemaProbeRegistry.Resolve(target.Path);
            if (probeDef != null)
            {
                schemaProbe = await _schemaProbe.ProbeAsync(probeDef, ct);
                result.SchemaProbe = schemaProbe;
            }

            results.Add(result);

            await WriteFrameAsync(new
            {
                type   = "result",
                index  = i + 1,
                total,
                result,
            }, ct);
        }

        // Altyapı / Şema derinlik kontrolleri — aynı canlı akışa devam
        await using (var infraConn = await TryOpenAsync(ct))
        {
            for (var j = 0; j < infraSpecs.Count; j++)
            {
                var spec = infraSpecs[j];
                await WriteFrameAsync(new
                {
                    type        = "checking",
                    index       = checks.Count + j + 1,
                    total,
                    label       = spec.Label,
                    parentLabel = spec.Group,
                    path        = "",
                }, ct);
                var result = await RunInfraAsync(spec, infraConn, ct);
                results.Add(result);
                await WriteFrameAsync(new
                {
                    type   = "result",
                    index  = checks.Count + j + 1,
                    total,
                    result,
                }, ct);
            }
        }

        await WriteFrameAsync(new
        {
            type    = "done",
            summary = new
            {
                total,
                ok         = results.Count(r => r.Status == "ok"),
                redirect   = results.Count(r => r.Status == "redirect"),
                warn       = results.Count(r => r.Status == "warn"),
                error      = results.Count(r => r.Status == "error"),
                exception  = results.Count(r => r.Status == "exception"),
                durationMs = results.Sum(r => r.DurationMs),
            },
        }, ct);
    }

    private async Task WriteFrameAsync(object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        await Response.WriteAsync(json + "\n", ct);
        await Response.Body.FlushAsync(ct);
    }

    // ── Altyapı / Şema derinlik kontrolleri ──────────────────────────
    // Menü-URL smoke'unun kapsamadığı: bağlantı, yazma yeteneği, çekirdek tablo/kolon
    // bütünlüğü ve seed sayımları. CheckResult olarak üretilir (ParentLabel = grup),
    // aynı JSON/NDJSON akışına eklenir; frontend değişikliği gerekmez.

    private static readonly string[] CoreTables =
    {
        "Users", "Forms", "Location", "Items", "ItemLocation", "ItemDocumentLock",
        "Document", "DocumentLine", "Contact", "DecimalSetting", "PermissionDef", "ApprovalFlow"
    };

    private static readonly (string Table, string Column)[] CoreColumns =
    {
        ("Items", "MinStock"), ("ItemLocation", "MinStock"), ("DocumentLine", "BaseQuantity"),
        ("Document", "Status"), ("ItemDocumentLock", "DocType"),
    };

    private sealed record InfraSpec(
        string Key, string Label, string Group,
        Func<SqlConnection?, CancellationToken, Task<(string Status, string Detail)>> Run);

    private List<InfraSpec> BuildInfraSpecs()
    {
        const string gdb = "Altyapı / Veritabanı";
        const string gseed = "Altyapı / Seed";
        return new List<InfraSpec>
        {
            new("infra.conn", "Veritabanı bağlantısı", gdb, async (conn, ct) =>
            {
                if (conn is null) return ("error", "Bağlantı açılamadı");
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT DB_NAME(), CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(64));";
                await using var r = await cmd.ExecuteReaderAsync(ct);
                if (await r.ReadAsync(ct))
                    return ("ok", $"DB: {(r.IsDBNull(0) ? "?" : r.GetString(0))} · SQL {(r.IsDBNull(1) ? "?" : r.GetString(1))}");
                return ("ok", "bağlı");
            }),
            new("infra.write", "Yazma yeteneği (geçici tablo)", gdb, async (conn, ct) =>
            {
                if (conn is null) return ("error", "Bağlantı yok");
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "CREATE TABLE #hc_probe(x INT); INSERT INTO #hc_probe VALUES(1); SELECT COUNT(*) FROM #hc_probe; DROP TABLE #hc_probe;";
                var n = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct) ?? 0);
                return n == 1 ? ("ok", "yaz/oku başarılı — gerçek veri etkilenmez") : ("error", $"beklenen 1, gelen {n}");
            }),
            new("infra.tables", "Çekirdek tablolar", gdb, async (conn, ct) =>
            {
                if (conn is null) return ("error", "Bağlantı yok");
                var missing = new List<string>();
                foreach (var t in CoreTables)
                    if (!await ObjExistsAsync(conn, $"[{_schema}].[{t}]", ct)) missing.Add(t);
                return missing.Count == 0
                    ? ("ok", $"{CoreTables.Length}/{CoreTables.Length} mevcut")
                    : ("error", $"EKSİK ({missing.Count}): {string.Join(", ", missing)}");
            }),
            new("infra.columns", "Kritik kolonlar", gdb, async (conn, ct) =>
            {
                if (conn is null) return ("error", "Bağlantı yok");
                var missing = new List<string>();
                foreach (var (t, c) in CoreColumns)
                    if (!await ColExistsAsync(conn, $"[{_schema}].[{t}]", c, ct)) missing.Add($"{t}.{c}");
                return missing.Count == 0
                    ? ("ok", $"{CoreColumns.Length}/{CoreColumns.Length} mevcut")
                    : ("error", $"EKSİK: {string.Join(", ", missing)} — self-healing ensure devreye girmeli");
            }),
            new("seed.users", "Kullanıcı kaydı", gseed, async (conn, ct) =>
            {
                var n = await CountAsync(conn, "Users", ct);
                return n < 0 ? ("error", "okunamadı") : n > 0 ? ("ok", $"{n} kayıt") : ("error", "en az bir admin bekleniyor");
            }),
            new("seed.forms", "Form tanımı (seed)", gseed, async (conn, ct) =>
            {
                var n = await CountAsync(conn, "Forms", ct);
                return n < 0 ? ("error", "okunamadı") : n > 0 ? ("ok", $"{n} kayıt") : ("warn", "form seed eksik");
            }),
            new("seed.perms", "İzin tanımı (seed)", gseed, async (conn, ct) =>
            {
                var n = await CountAsync(conn, "PermissionDef", ct);
                return n < 0 ? ("error", "okunamadı") : n > 0 ? ("ok", $"{n} kayıt") : ("warn", "izin seed eksik");
            }),
            new("seed.decimal", "Ondalık ayarı", gseed, async (conn, ct) =>
            {
                var n = await CountAsync(conn, "DecimalSetting", ct);
                return n < 0 ? ("error", "okunamadı") : ("ok", n > 0 ? $"{n} kayıt" : "kayıt yok (varsayılanlar devrede)");
            }),
        };
    }

    private async Task<CheckResult> RunInfraAsync(InfraSpec spec, SqlConnection? conn, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        string status = "ok", detail = "";
        try { (status, detail) = await spec.Run(conn, ct); }
        catch (Exception ex) { status = "exception"; detail = ex.Message.Length > 200 ? ex.Message[..200] : ex.Message; }
        sw.Stop();
        // View, errorSnippet'i kırmızı stille gösterir → sadece gerçek hata detayı oraya düşsün.
        // ok/uyarı detayı etiket satırına eklenir (yeşil/amber satır temiz kalır).
        var problem = status is "error" or "exception";
        return new CheckResult
        {
            Key = spec.Key,
            Label = (!problem && !string.IsNullOrWhiteSpace(detail)) ? $"{spec.Label} — {detail}" : spec.Label,
            Path = "",
            ParentLabel = spec.Group,
            Status = status,
            DurationMs = (int)sw.ElapsedMilliseconds,
            ErrorSnippet = problem && !string.IsNullOrWhiteSpace(detail) ? detail : null,
        };
    }

    private async Task<SqlConnection?> TryOpenAsync(CancellationToken ct)
    {
        try { return await _connectionFactory.OpenConnectionAsync(ct); }
        catch { return null; }
    }

    private static async Task<bool> ObjExistsAsync(SqlConnection conn, string objName, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT CASE WHEN OBJECT_ID(@n, N'U') IS NOT NULL THEN 1 ELSE 0 END;";
        cmd.Parameters.Add(new SqlParameter("@n", objName));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct) ?? 0) == 1;
    }

    private static async Task<bool> ColExistsAsync(SqlConnection conn, string objName, string col, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT CASE WHEN COL_LENGTH(@n, @c) IS NOT NULL THEN 1 ELSE 0 END;";
        cmd.Parameters.Add(new SqlParameter("@n", objName));
        cmd.Parameters.Add(new SqlParameter("@c", col));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct) ?? 0) == 1;
    }

    private async Task<int> CountAsync(SqlConnection? conn, string table, CancellationToken ct)
    {
        if (conn is null) return -1;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(1) FROM [{_schema}].[{table}];";
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct) ?? 0);
        }
        catch { return -1; }
    }

    private (List<CheckTarget> Checks, HttpClient Client, string BaseUrl, string CookieHeader) PrepareRun()
    {
        var checks = BuildCheckList();
        var req = _httpContextAccessor.HttpContext!.Request;
        var baseUrl = $"{req.Scheme}://{req.Host}";
        var cookieHeader = string.Join("; ", req.Cookies.Select(c => $"{c.Key}={c.Value}"));
        var client = _httpFactory.CreateClient("health-check");
        client.Timeout = TimeSpan.FromSeconds(15);
        return (checks, client, baseUrl, cookieHeader);
    }

    private List<CheckTarget> BuildCheckList()
    {
        var isAdmin = string.Equals(User.Identity?.Name, "admin@calibra.local", StringComparison.OrdinalIgnoreCase);
        var menu = MenuDefinition.GetMainMenu(isAdmin);
        var checks = new List<CheckTarget>();
        FlattenMenu(menu, null, checks);
        return checks;
    }

    private async Task<CheckResult> RunSingleAsync(
        HttpClient client, string baseUrl, string cookieHeader, CheckTarget target, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var result = new CheckResult { Key = target.Key, Label = target.Label, Path = target.Path, ParentLabel = target.ParentLabel };
        try
        {
            using var msg = new HttpRequestMessage(HttpMethod.Get, baseUrl + target.Path);
            if (!string.IsNullOrEmpty(cookieHeader))
                msg.Headers.Add("Cookie", cookieHeader);
            msg.Headers.Add("Sec-Fetch-Dest", "iframe");

            using var resp = await client.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ct);
            sw.Stop();
            result.StatusCode = (int)resp.StatusCode;
            result.DurationMs = (int)sw.ElapsedMilliseconds;

            if ((int)resp.StatusCode == 200)
                result.Status = "ok";
            else if ((int)resp.StatusCode is 301 or 302 or 303 or 307 or 308)
                result.Status = "redirect";
            else if ((int)resp.StatusCode >= 500)
            {
                result.Status = "error";
                try
                {
                    var body = await resp.Content.ReadAsStringAsync(ct);
                    result.ErrorSnippet = ExtractErrorSnippet(body);
                }
                catch { /* ignore */ }
            }
            else
            {
                result.Status = "warn";
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.DurationMs = (int)sw.ElapsedMilliseconds;
            result.Status = "exception";
            result.ErrorSnippet = "İşlem sırasında bir hata oluştu.";
        }
        return result;
    }

    /// <summary>
    /// Test şirketi oluştur → test kullanıcısı oluştur → login → tüm formları test et.
    /// Her adım NDJSON stream olarak frontend'e iletilir.
    /// Frame tipleri: setup_start | setup_step | setup_done | setup_error | start | checking | result | done
    /// </summary>
    [HttpPost("/Admin/HealthCheck/StreamTestCompany")]
    [ValidateAntiForgeryToken]
    public async Task StreamTestCompany([FromQuery] bool createNewDb = false, CancellationToken ct = default)
    {
        Response.ContentType = "application/x-ndjson; charset=utf-8";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
        Response.StatusCode = 200;

        // Test şirketi adı: TEST_DDMMYYHHII (UTC)
        var now = DateTime.UtcNow;
        var testCompanyName = $"TEST_{now:ddMMyy}{now:HHmm}";
        var testEmail       = $"test.hc.{now:ddMMyyHHmm}@calibra.test";
        var testPassword    = $"Hc!{Guid.NewGuid().ToString("N")[..8]}";

        // Adım listesi: createNewDb true ise DB oluşturma 1. adım olarak eklenir
        var stepTotal = createNewDb ? 4 : 3;
        var stepLabels = createNewDb
            ? new[] { "Veritabanı oluşturuluyor", "Test şirketi oluşturuluyor", "Test kullanıcısı oluşturuluyor", "Test oturumu başlatılıyor" }
            : new[] { "Test şirketi oluşturuluyor", "Test kullanıcısı oluşturuluyor", "Test oturumu başlatılıyor" };

        await WriteFrameAsync(new { type = "setup_start", total = stepTotal, labels = stepLabels }, ct);

        // Mevcut şirketin DB bağlantısını al (template olarak kullanılır)
        int.TryParse(User.FindFirst("company_id")?.Value, out var currentCompanyId);
        var currentCompany  = currentCompanyId > 0
            ? await _companyRepository.GetByIdAsync(currentCompanyId, ct)
            : null;
        var connectionString = currentCompany?.DatabaseConnectionString;

        // Admin kullanıcısının company_id claim'i olmayabilir; createNewDb modunda
        // SQL Server adresi gerekiyor → şifre çözülmüş sistem DB connection string'ini kullan
        if (createNewDb && string.IsNullOrWhiteSpace(connectionString))
            connectionString = _connectionFactory.ResolveConnectionStringForCompany(0);

        // Adım offset: createNewDb'de ilk adım DB oluşturma
        var stepOffset = createNewDb ? 1 : 0;

        int testCompanyId;
        try
        {
            // [Opsiyonel] Adım 1: Yeni test veritabanı oluştur ve şemayı init et
            if (createNewDb)
            {
                await WriteFrameAsync(new { type = "setup_step", step = 1, total = stepTotal, message = stepLabels[0] }, ct);
                var newDbName = $"CalibraTest_{now:ddMMyyHHmm}";
                var (newConnStr, dbError) = await CreateTestDatabaseAsync(connectionString, newDbName, ct);
                if (dbError != null)
                {
                    await WriteFrameAsync(new { type = "setup_error", message = $"Veritabanı oluşturulamadı: {dbError}" }, ct);
                    return;
                }
                // Tam şema init (tüm Ensure* + Seed* metodları)
                await _dbInitializer.InitializeForConnectionAsync(newConnStr, ct);
                connectionString = newConnStr;
            }

            // Adım stepOffset+1: Test şirketi oluştur
            await WriteFrameAsync(new { type = "setup_step", step = 1 + stepOffset, total = stepTotal, message = stepLabels[stepOffset] }, ct);
            var taxNumber = $"TST-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
            testCompanyId = await _adminManagement.SaveCompanyAsync(
                new SaveCompanyRequest(null, testCompanyName, testCompanyName, "-", null, null, null, "-", taxNumber, false, true, connectionString),
                ct);

            // Adım stepOffset+2: Test departmanı + kullanıcısı oluştur
            await WriteFrameAsync(new { type = "setup_step", step = 2 + stepOffset, total = stepTotal, message = stepLabels[1 + stepOffset] }, ct);
            await _adminManagement.CreateDepartmentAsync(new CreateDepartmentRequest(testCompanyId, "Yönetim"), ct);
            var allDepts = await _departmentRepository.GetAllAsync(ct);
            var dept = allDepts.First(x => x.CompanyId == testCompanyId);
            await _adminManagement.CreateUserAsync(
                new CreateUserRequest(
                    testCompanyId, "Test Admin", testEmail, "TST-001", dept.Id, null,
                    UserRole.SystemAdmin, UserAuthorizationCatalog.GetAllowedPermissions(UserRole.SystemAdmin),
                    testPassword),
                ct);
        }
        catch (Exception ex)
        {
            await WriteFrameAsync(new { type = "setup_error", message = $"Ortam oluşturulamadı: {ex.Message}" }, ct);
            return;
        }

        // Adım son: Test oturumu başlat (programmatic login)
        try
        {
            await WriteFrameAsync(new { type = "setup_step", step = stepTotal, total = stepTotal, message = stepLabels[^1] }, ct);
            var req     = _httpContextAccessor.HttpContext!.Request;
            var baseUrl = $"{req.Scheme}://{req.Host}";

            var (testCookieHeader, loginError) = await LoginAsTestUserAsync(baseUrl, testEmail, testPassword, testCompanyId, ct);
            if (testCookieHeader == null)
            {
                await WriteFrameAsync(new { type = "setup_error", message = $"Test oturumu başlatılamadı: {loginError}" }, ct);
                return;
            }

            await WriteFrameAsync(new { type = "setup_done", companyName = testCompanyName, userEmail = testEmail, testCompanyId }, ct);

            // Form testleri — mevcut Stream() ile aynı mantık, test kullanıcısının cookie'si ile
            var checks = BuildCheckList();
            var total   = checks.Count;
            var results = new List<CheckResult>(total);
            var client  = _httpFactory.CreateClient("health-check");
            client.Timeout = TimeSpan.FromSeconds(15);

            await WriteFrameAsync(new { type = "start", total }, ct);

            for (var i = 0; i < checks.Count; i++)
            {
                var target = checks[i];
                await WriteFrameAsync(new
                {
                    type        = "checking",
                    index       = i + 1,
                    total,
                    label       = target.Label,
                    parentLabel = target.ParentLabel,
                    path        = target.Path,
                }, ct);

                var result = await RunSingleAsync(client, baseUrl, testCookieHeader, target, ct);
                results.Add(result);

                await WriteFrameAsync(new { type = "result", index = i + 1, total, result }, ct);
            }

            await WriteFrameAsync(new
            {
                type    = "done",
                summary = new
                {
                    total,
                    ok         = results.Count(r => r.Status == "ok"),
                    redirect   = results.Count(r => r.Status == "redirect"),
                    warn       = results.Count(r => r.Status == "warn"),
                    error      = results.Count(r => r.Status == "error"),
                    exception  = results.Count(r => r.Status == "exception"),
                    durationMs = results.Sum(r => r.DurationMs),
                    testCompanyId,
                    testCompanyName,
                },
            }, ct);
        }
        catch (Exception ex)
        {
            await WriteFrameAsync(new { type = "setup_error", message = $"Test sırasında hata: {ex.Message}" }, ct);
        }
    }

    /// <summary>
    /// Programmatic login: GET login sayfasından CSRF token al, POST ile giriş yap, auth cookie'yi döndür.
    /// </summary>
    private async Task<(string? CookieHeader, string? Error)> LoginAsTestUserAsync(
        string baseUrl, string email, string password, int companyId, CancellationToken ct)
    {
        var cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies        = true,
            CookieContainer   = cookieContainer,
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        var loginUri = new Uri($"{baseUrl}/Account/Login");

        try
        {
            // 1. GET login sayfası → antiforgery cookie + form token
            using var getResp = await client.GetAsync(loginUri, ct);
            var html = await getResp.Content.ReadAsStringAsync(ct);
            var csrfToken = ExtractCsrfToken(html);
            if (string.IsNullOrWhiteSpace(csrfToken))
                return (null, "CSRF token bulunamadı");

            // 2. POST login
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["CompanyId"]                    = companyId.ToString(),
                ["Email"]                        = email,
                ["Password"]                     = password,
                ["RememberMe"]                   = "false",
                ["__RequestVerificationToken"]   = csrfToken,
            });
            using var postResp = await client.PostAsync(loginUri, form, ct);

            // Başarı = 302 redirect to home
            if (postResp.StatusCode is not (HttpStatusCode.Redirect or HttpStatusCode.OK or HttpStatusCode.Found))
                return (null, $"Giriş başarısız (HTTP {(int)postResp.StatusCode})");

            var cookies = cookieContainer.GetCookies(loginUri);
            if (cookies.Count == 0)
                return (null, "Oturum cookie'si alınamadı");

            var cookieHeader = string.Join("; ", cookies.Cast<Cookie>().Select(c => $"{c.Name}={c.Value}"));
            return (cookieHeader, null);
        }
        catch (Exception ex)
        {
            return (null, $"Login hatası: {ex.Message}");
        }
    }

    /// <summary>
    /// Mevcut connection string'in gösterdiği sunucuda yeni bir test DB'si oluşturur.
    /// DB adı alphanumerik+alt_çizgi olduğundan doğrudan identifier olarak kullanılabilir.
    /// </summary>
    private static async Task<(string NewConnectionString, string? Error)> CreateTestDatabaseAsync(
        string? templateConnectionString, string dbName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(templateConnectionString))
            return (string.Empty, "Kaynak şirketin bağlantı bilgisi bulunamadı");
        try
        {
            var builder = new SqlConnectionStringBuilder(templateConnectionString);
            builder.InitialCatalog = "master";

            await using var masterConn = new SqlConnection(builder.ConnectionString);
            await masterConn.OpenAsync(ct);
            await using var cmd = masterConn.CreateCommand();
            // dbName: CalibraTest_DDMMYYHHII — yalnızca alfanümerik ve alt çizgi, injection riski yok
            cmd.CommandText = $"""
                IF NOT EXISTS (SELECT name FROM master.sys.databases WHERE name = N'{dbName}')
                    CREATE DATABASE [{dbName}];
                """;
            await cmd.ExecuteNonQueryAsync(ct);

            var newBuilder = new SqlConnectionStringBuilder(templateConnectionString)
            {
                InitialCatalog = dbName
            };
            return (newBuilder.ConnectionString, null);
        }
        catch (Exception ex)
        {
            return (string.Empty, ex.Message);
        }
    }

    private static string? ExtractCsrfToken(string html)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            html,
            @"<input[^>]+name=""__RequestVerificationToken""[^>]+value=""([^""]+)""",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static void FlattenMenu(IReadOnlyList<MenuDefinition.MenuNode> menu, string? parentLabel, List<CheckTarget> output)
    {
        foreach (var node in menu)
        {
            // Sadece URL'i olan node'lar test edilir (grup baslari atlanir)
            if (!string.IsNullOrEmpty(node.Url))
            {
                output.Add(new CheckTarget
                {
                    Key = node.Key,
                    Label = node.Label,
                    Path = node.Url,
                    ParentLabel = parentLabel,
                });
            }
            if (node.Children != null && node.Children.Count > 0)
            {
                var childParent = string.IsNullOrEmpty(parentLabel) ? node.Label : $"{parentLabel} › {node.Label}";
                FlattenMenu(node.Children, childParent, output);
            }
        }
    }

    /// <summary>HTML hata sayfasindan anlamli kismi cek (exception message'i icerir).</summary>
    private static string ExtractErrorSnippet(string body)
    {
        if (string.IsNullOrEmpty(body)) return string.Empty;

        // ASP.NET dev exception page'inin baslik kismindan exception type ve mesajini al
        var titleIdx = body.IndexOf("<title>", StringComparison.OrdinalIgnoreCase);
        if (titleIdx >= 0)
        {
            var endIdx = body.IndexOf("</title>", titleIdx, StringComparison.OrdinalIgnoreCase);
            if (endIdx > titleIdx)
            {
                var title = body.Substring(titleIdx + 7, endIdx - titleIdx - 7);
                if (!string.IsNullOrWhiteSpace(title) && title.Length < 200)
                    return title.Trim();
            }
        }

        // SqlException tipini ara
        var sqlIdx = body.IndexOf("SqlException", StringComparison.OrdinalIgnoreCase);
        if (sqlIdx >= 0)
        {
            var end = Math.Min(sqlIdx + 300, body.Length);
            return body.Substring(sqlIdx, end - sqlIdx).Replace("\n", " ").Replace("  ", " ");
        }

        // Genel: ilk 300 character (HTML tag'lerini at)
        var stripped = System.Text.RegularExpressions.Regex.Replace(body, "<[^>]+>", " ");
        stripped = System.Text.RegularExpressions.Regex.Replace(stripped, @"\s+", " ").Trim();
        return stripped.Length > 300 ? stripped.Substring(0, 300) + "..." : stripped;
    }

    private sealed class CheckTarget
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public string Path { get; set; } = "";
        public string? ParentLabel { get; set; }
    }

    private sealed class CheckResult
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public string Path { get; set; } = "";
        public string? ParentLabel { get; set; }
        public int StatusCode { get; set; }
        public int DurationMs { get; set; }
        public string Status { get; set; } = "pending";   // ok/redirect/warn/error/exception
        public string? ErrorSnippet { get; set; }
        /// <summary>Schema probe sonucu (registry'de tanımlıysa); yoksa null = "—" gösterilir.</summary>
        public SchemaProbeResult? SchemaProbe { get; set; }
    }
}
