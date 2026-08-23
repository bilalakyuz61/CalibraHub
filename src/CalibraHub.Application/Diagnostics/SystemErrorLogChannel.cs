using System.Threading.Channels;

namespace CalibraHub.Application.Diagnostics;

/// <summary>
/// Hata log girdilerinin üretici (ILogger provider) → tüketici (SystemErrorLogWriter)
/// kuyruğu. <see cref="Application.Auditing.AuditTrailChannel"/> ile aynı desen.
/// Singleton kaydedilir. Unbounded — log kaybı yerine bellek tercih edilir; tüketici tek
/// olduğu için SingleReader optimizasyonu açık.
/// </summary>
public interface ISystemErrorLogChannel
{
    ChannelReader<SystemErrorEntry> Reader { get; }

    /// <summary>Enqueue — kanal kapalı/dolu olsa dahi exception fırlatmaz, sessizce false döner.</summary>
    bool TryWrite(SystemErrorEntry entry);
}

public sealed class SystemErrorLogChannel : ISystemErrorLogChannel
{
    private readonly Channel<SystemErrorEntry> _channel = Channel.CreateUnbounded<SystemErrorEntry>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });

    public ChannelReader<SystemErrorEntry> Reader => _channel.Reader;

    public bool TryWrite(SystemErrorEntry entry)
    {
        try { return _channel.Writer.TryWrite(entry); }
        catch { return false; } // kanal kapatılmışsa (shutdown) sessizce yut
    }
}
