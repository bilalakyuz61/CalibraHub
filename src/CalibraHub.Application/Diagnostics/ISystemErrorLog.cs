namespace CalibraHub.Application.Diagnostics;

/// <summary>
/// Elle enstrümante etmek için doğrudan erişim noktası. Ana yol
/// (LogError/LogCritical → dosyaya otomatik düşme) SystemErrorLoggerProvider'dır
/// (CalibraHub.Web/Logging); bu servis yalnızca özel durumlarda (ör. arka plan
/// job'ının kendi kimliğiyle kayıt eklemesi) kullanılır.
/// </summary>
public interface ISystemErrorLog
{
    /// <summary>Enqueue — never throws; iş akışını asla bozmaz.</summary>
    void Record(SystemErrorEntry entry);
}

public sealed class SystemErrorLog : ISystemErrorLog
{
    private readonly ISystemErrorLogChannel _channel;
    private static long _seq;

    public SystemErrorLog(ISystemErrorLogChannel channel)
    {
        _channel = channel;
    }

    public void Record(SystemErrorEntry entry)
    {
        try
        {
            if (string.IsNullOrEmpty(entry.Id))
                entry.Id = NextId();
            if (entry.TimestampUtc == default)
                entry.TimestampUtc = DateTime.UtcNow;
            _channel.TryWrite(entry);
        }
        catch { /* enstrümantasyon iş akışını asla bozmaz */ }
    }

    internal static string NextId()
    {
        var seq = Interlocked.Increment(ref _seq) % 10000;
        return DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + seq.ToString("D4");
    }
}
