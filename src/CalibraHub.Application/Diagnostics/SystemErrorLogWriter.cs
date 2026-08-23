using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Diagnostics;

/// <summary>
/// Channel'dan hata log girdilerini tüketip günlük JSONL dosyalarına yazan background servis.
/// <see cref="Application.Auditing.AuditFileWriter"/> ile aynı desen (batch yazım + retention).
/// Dosya yolu: {root}/{yyyy-MM}/error-{yyyy-MM-dd}.jsonl
///
/// KRİTİK: Bu servisin kendi kategorisi (ILogger&lt;SystemErrorLogWriter&gt;) SystemErrorLoggerProvider
/// tarafından bilinçli olarak dışlanır (Web/Logging/SystemErrorLogger.cs) — yazım hatası tekrar
/// kendi kanalına düşüp geri-besleme (feedback loop) yaratmaz.
/// </summary>
public sealed class SystemErrorLogWriter : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan CleanupInitialDelay = TimeSpan.FromMinutes(3);

    private readonly ISystemErrorLogChannel _channel;
    private readonly SystemErrorLogOptions _options;
    private readonly ILogger<SystemErrorLogWriter> _logger;

    public SystemErrorLogWriter(ISystemErrorLogChannel channel, SystemErrorLogOptions options,
        ILogger<SystemErrorLogWriter> logger)
    {
        _channel = channel;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[ErrorLog] Dosya yazıcısı başladı. Kök: {Root}", _options.RootPath);
        await Task.WhenAll(WriteLoopAsync(stoppingToken), CleanupLoopAsync(stoppingToken));
    }

    // ── Yazım döngüsü ────────────────────────────────────────────────────────

    private async Task WriteLoopAsync(CancellationToken ct)
    {
        var batch = new List<SystemErrorEntry>(256);
        try
        {
            while (await _channel.Reader.WaitToReadAsync(ct))
            {
                batch.Clear();
                while (batch.Count < 1000 && _channel.Reader.TryRead(out var entry))
                    batch.Add(entry);

                if (batch.Count > 0)
                    await FlushBatchAsync(batch, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Kapanış: kuyruğu son kez boşalt — bekleyen loglar kaybolmasın
            batch.Clear();
            while (_channel.Reader.TryRead(out var entry))
                batch.Add(entry);
            if (batch.Count > 0)
                await FlushBatchAsync(batch, CancellationToken.None);
        }
    }

    private async Task FlushBatchAsync(List<SystemErrorEntry> batch, CancellationToken ct)
    {
        foreach (var group in batch.GroupBy(e => e.TimestampUtc.Date))
        {
            var path = SystemErrorLogFileNaming.DayFile(_options.RootPath, group.Key);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                var sb = new StringBuilder();
                foreach (var entry in group)
                    sb.Append(SystemErrorLogJson.Serialize(entry)).Append('\n');

                await using var fs = new FileStream(path, FileMode.Append, FileAccess.Write,
                    FileShare.ReadWrite, bufferSize: 16 * 1024, useAsync: true);
                var bytes = Encoding.UTF8.GetBytes(sb.ToString());
                await fs.WriteAsync(bytes, ct);
                await fs.FlushAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ErrorLog] Log dosyasına yazılamadı: {Path} ({Count} girdi)",
                    path, group.Count());
            }
        }
    }

    // ── Retention temizliği ─────────────────────────────────────────────────

    private async Task CleanupLoopAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(CleanupInitialDelay, ct);
            while (!ct.IsCancellationRequested)
            {
                RunCleanup();
                await Task.Delay(CleanupInterval, ct);
            }
        }
        catch (OperationCanceledException) { /* kapanış */ }
    }

    private void RunCleanup()
    {
        try
        {
            if (_options.RetentionDays <= 0) return; // 0 = süresiz sakla
            if (!Directory.Exists(_options.RootPath)) return;

            var cutoff = DateTime.UtcNow.Date.AddDays(-_options.RetentionDays);
            var deleted = 0;

            foreach (var monthDir in Directory.EnumerateDirectories(_options.RootPath))
            {
                foreach (var file in Directory.EnumerateFiles(monthDir,
                             SystemErrorLogFileNaming.FilePrefix + "*" + SystemErrorLogFileNaming.FileExtension))
                {
                    var day = SystemErrorLogFileNaming.ParseDay(Path.GetFileName(file));
                    if (day is null || day.Value >= cutoff) continue;
                    try { File.Delete(file); deleted++; }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[ErrorLog] Eski log dosyası silinemedi: {File}", file);
                    }
                }
                // Boşalan ay klasörünü kaldır
                if (!Directory.EnumerateFileSystemEntries(monthDir).Any())
                {
                    try { Directory.Delete(monthDir); } catch { /* önemsiz */ }
                }
            }

            if (deleted > 0)
                _logger.LogInformation(
                    "[ErrorLog] Retention temizliği: {Deleted} gün dosyası silindi (> {Days} gün).",
                    deleted, _options.RetentionDays);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ErrorLog] Retention temizliği başarısız.");
        }
    }
}
