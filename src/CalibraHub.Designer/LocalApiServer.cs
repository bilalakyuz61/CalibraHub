using System.Net;
using System.Text;

namespace CalibraHub.Designer;

/// <summary>
/// localhost:61002 üzerinde dinler.
/// CalibraHub web sayfasından "Designer'ı Aç" isteği geldiğinde OnOpenTemplate event'ini tetikler.
/// </summary>
public sealed class LocalApiServer
{
    public const int Port = 61002;
    private readonly HttpListener _listener = new();

    /// <summary>(templateId, calibraBaseUrl) çifti ile tetiklenir.</summary>
    public event Action<string, string>? OnOpenTemplate;

    public async Task StartAsync(CancellationToken ct = default)
    {
        _listener.Prefixes.Add($"http://localhost:{Port}/");
        try { _listener.Start(); }
        catch (HttpListenerException ex)
        {
            MessageBox.Show(
                $"Port {Port} kullanımda veya izin hatası: {ex.Message}\n\nUygulama kapatılıyor.",
                "CalibraHub Designer", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Application.Exit();
            return;
        }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => Handle(context), ct);
            }
            catch (HttpListenerException) { break; }
        }
    }

    private void Handle(HttpListenerContext ctx)
    {
        // Localhost CORS + Chrome Private Network Access
        ctx.Response.Headers["Access-Control-Allow-Origin"]          = "*";
        ctx.Response.Headers["Access-Control-Allow-Methods"]         = "GET, POST, OPTIONS";
        ctx.Response.Headers["Access-Control-Allow-Headers"]         = "*";
        ctx.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
        ctx.Response.Headers["Access-Control-Max-Age"]               = "86400";

        if (ctx.Request.HttpMethod == "OPTIONS")
        {
            ctx.Response.StatusCode = 204;
            ctx.Response.Close();
            return;
        }

        var path = ctx.Request.Url?.AbsolutePath ?? "";

        if (path == "/ping")
        {
            Json(ctx, 200, """{"ok":true,"app":"CalibraHub.Designer"}""");
        }
        else if (path == "/open")
        {
            var id  = ctx.Request.QueryString["id"]  ?? "";
            var url = ctx.Request.QueryString["url"] ?? "http://localhost:61001";

            // Once response dondur, sonra event tetikle (bloklama onlemi)
            Json(ctx, 200, """{"ok":true}""");

            if (!string.IsNullOrWhiteSpace(id))
            {
                try { OnOpenTemplate?.Invoke(id, url); }
                catch { /* UI thread'e iletilecek */ }
            }
        }
        else
        {
            Json(ctx, 404, """{"error":"not found"}""");
        }
    }

    private static void Json(HttpListenerContext ctx, int status, string body)
    {
        ctx.Response.StatusCode  = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(body);
        ctx.Response.OutputStream.Write(bytes);
        ctx.Response.Close();
    }

    public void Stop() => _listener.Stop();
}
