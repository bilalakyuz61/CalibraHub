using System.Diagnostics;
using System.Net;
using System.Text.Json;
using CalibraHub.Web.Models.Diagnostics;
using CalibraHub.Web.Models.Navigation;
using CalibraHub.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

    public HealthCheckController(
        IHttpClientFactory httpFactory,
        IHttpContextAccessor httpContextAccessor,
        ILogger<HealthCheckController> logger,
        SchemaProbeService schemaProbe)
    {
        _httpFactory = httpFactory;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        _schemaProbe = schemaProbe;
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
        var total = checks.Count;
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

    private (List<CheckTarget> Checks, HttpClient Client, string BaseUrl, string CookieHeader) PrepareRun()
    {
        var isAdmin = string.Equals(User.Identity?.Name, "admin@calibra.local", StringComparison.OrdinalIgnoreCase);
        var menu = MenuDefinition.GetMainMenu(isAdmin);
        var checks = new List<CheckTarget>();
        FlattenMenu(menu, null, checks);

        var req = _httpContextAccessor.HttpContext!.Request;
        var baseUrl = $"{req.Scheme}://{req.Host}";
        var cookieHeader = string.Join("; ", req.Cookies.Select(c => $"{c.Key}={c.Value}"));

        var client = _httpFactory.CreateClient("health-check");
        client.Timeout = TimeSpan.FromSeconds(15);

        return (checks, client, baseUrl, cookieHeader);
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
