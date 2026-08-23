using System.Security.Claims;
using System.Threading;
using CalibraHub.Application.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Web.Logging;

/// <summary>
/// Tek bir kategori için <see cref="ILogger"/> — LogLevel Error/Critical çağrılarını
/// <see cref="SystemErrorEntry"/>'ye çevirip channel'a enqueue eder. Enqueue/çevirme hatası
/// KENDİ İÇİNDE yutulur; hiçbir koşulda ILogger'a geri yazmaz (sonsuz döngü riski).
/// </summary>
internal sealed class SystemErrorLogger : ILogger
{
    // Kendi yazıcı/provider kategorilerimiz + gürültülü host log'ları — sonsuz geri-besleme
    // ve anlamsız kayıt guard'ı. Tam eşleşme (StartsWith) ile kontrol edilir.
    private static readonly string[] IgnoredCategoryPrefixes =
    [
        "CalibraHub.Application.Diagnostics.",
        "CalibraHub.Web.Logging.",
        "Microsoft.Hosting.",
        "Microsoft.Extensions.Hosting.",
    ];

    private readonly string _category;
    private readonly ISystemErrorLogChannel _channel;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private static long _seq;

    public SystemErrorLogger(string category, ISystemErrorLogChannel channel, IHttpContextAccessor httpContextAccessor)
    {
        _category = category;
        _channel = channel;
        _httpContextAccessor = httpContextAccessor;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel)
    {
        if (logLevel < LogLevel.Error) return false;
        foreach (var prefix in IgnoredCategoryPrefixes)
        {
            if (_category.StartsWith(prefix, StringComparison.Ordinal)) return false;
        }
        return true;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        try
        {
            string message;
            try { message = formatter(state, exception); }
            catch { message = state?.ToString() ?? string.Empty; }

            string? requestPath = null;
            string? userName = null;
            int? companyId = null;
            try
            {
                var http = _httpContextAccessor.HttpContext;
                if (http is not null)
                {
                    requestPath = http.Request?.Path.Value;
                    var user = http.User;
                    if (user?.Identity?.IsAuthenticated == true)
                    {
                        userName = user.Identity.Name;
                        if (string.IsNullOrWhiteSpace(userName))
                            userName = user.FindFirstValue(ClaimTypes.Email);
                        if (int.TryParse(user.FindFirstValue("company_id"), out var cid))
                            companyId = cid;
                    }
                }
            }
            catch { /* HttpContext dışı akış (background) veya erişim hatası — ortam bilgisi olmadan devam */ }

            var entry = new SystemErrorEntry
            {
                Id = NextId(),
                TimestampUtc = DateTime.UtcNow,
                Level = logLevel == LogLevel.Critical ? "Critical" : "Error",
                Category = _category,
                Message = message,
                ExceptionType = exception?.GetType().FullName,
                ExceptionMessage = exception?.Message,
                StackTrace = exception?.StackTrace,
                Source = exception?.Source,
                RequestPath = requestPath,
                UserName = userName,
                CompanyId = companyId,
            };
            _channel.TryWrite(entry);
        }
        catch
        {
            // Kayıt yakalama hatası ASLA ILogger'a geri yazılmaz (sonsuz döngü riski).
        }
    }

    private static string NextId()
    {
        var seq = Interlocked.Increment(ref _seq) % 10000;
        return DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + seq.ToString("D4");
    }
}
