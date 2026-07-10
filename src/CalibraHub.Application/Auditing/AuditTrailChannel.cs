using System.Threading.Channels;

namespace CalibraHub.Application.Auditing;

/// <summary>
/// Audit girdilerinin üretici (request thread) → tüketici (AuditFileWriter) kuyruğu.
/// Singleton kaydedilir. Unbounded — log kaybı yerine bellek tercih edilir;
/// tüketici tek olduğu için SingleReader optimizasyonu açık.
/// </summary>
public sealed class AuditTrailChannel
{
    private readonly Channel<AuditEntry> _channel = Channel.CreateUnbounded<AuditEntry>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });

    public ChannelReader<AuditEntry> Reader => _channel.Reader;

    public bool TryWrite(AuditEntry entry) => _channel.Writer.TryWrite(entry);
}
