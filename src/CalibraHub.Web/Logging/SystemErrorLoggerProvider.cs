using System.Collections.Concurrent;
using CalibraHub.Application.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Web.Logging;

/// <summary>
/// Otomatik hata log yakalama — tüm ILogger kategorilerinden LogError/LogCritical
/// çağrılarını SystemErrorLogChannel'a yönlendirir. Program.cs'de
/// <c>services.AddSingleton&lt;ILoggerProvider, SystemErrorLoggerProvider&gt;()</c> ile
/// kaydedilir (logging factory DI'daki tüm ILoggerProvider'ları tüketir — builder.Logging
/// sıralaması ile ilgili sorunları bu yaklaşım çözer).
/// </summary>
public sealed class SystemErrorLoggerProvider : ILoggerProvider
{
    private readonly ISystemErrorLogChannel _channel;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ConcurrentDictionary<string, SystemErrorLogger> _loggers = new(StringComparer.Ordinal);

    public SystemErrorLoggerProvider(ISystemErrorLogChannel channel, IHttpContextAccessor httpContextAccessor)
    {
        _channel = channel;
        _httpContextAccessor = httpContextAccessor;
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new SystemErrorLogger(name, _channel, _httpContextAccessor));

    public void Dispose() => _loggers.Clear();
}
