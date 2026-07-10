using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Auditing;

/// <summary>
/// Channel'dan audit girdilerini tüketip günlük JSONL dosyalarına yazan background servis.
/// Dosya yolu: {root}/company-{id}/{yyyy-MM}/audit-{yyyy-MM-dd}.jsonl
///
/// - Batch yazım: kuyrukta biriken girdiler tek seferde drenlenir, dosya başına grup
///   halinde append edilir (her girdi için ayrı dosya açma maliyeti yok).
/// - Dosya FileShare.ReadWrite ile açılır — okuyucu (AuditQueryService) yazım
///   sırasında da dosyayı okuyabilir.
/// - Retention temizliği aynı serviste ayrı bir döngüde çalışır: şirket bazlı
///   AUDIT_RETENTION_DAYS parametresinden eski gün dosyaları silinir.
/// </summary>
public sealed class AuditFileWriter : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan CleanupInitialDelay = TimeSpan.FromMinutes(3);

    private readonly AuditTrailChannel _channel;
    private readonly AuditTrailOptions _options;
    private readonly IAuditRetentionResolver _retention;
    private readonly ILogger<AuditFileWriter> _logger;

    public AuditFileWriter(AuditTrailChannel channel, AuditTrailOptions options,
        IAuditRetentionResolver retention, ILogger<AuditFileWriter> logger)
    {
        _channel = channel;
        _options = options;
        _retention = retention;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[Audit] Dosya yazıcısı başladı. Kök: {Root}", _options.RootPath);
        await Task.WhenAll(WriteLoopAsync(stoppingToken), CleanupLoopAsync(stoppingToken));
    }

    // ── Yazım döngüsü ────────────────────────────────────────────────────────

    private async Task WriteLoopAsync(CancellationToken ct)
    {
        var batch = new List<AuditEntry>(256);
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

    private async Task FlushBatchAsync(List<AuditEntry> batch, CancellationToken ct)
    {
        foreach (var group in batch.GroupBy(e => (e.CompanyId, e.Ts.Date)))
        {
            var path = AuditFileNaming.DayFile(_options.RootPath, group.Key.CompanyId, group.Key.Date);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                var sb = new StringBuilder();
                foreach (var entry in group)
                    sb.Append(AuditJson.Serialize(entry)).Append('\n');

                await using var fs = new FileStream(path, FileMode.Append, FileAccess.Write,
                    FileShare.ReadWrite, bufferSize: 16 * 1024, useAsync: true);
                var bytes = Encoding.UTF8.GetBytes(sb.ToString());
                await fs.WriteAsync(bytes, ct);
                await fs.FlushAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Audit] Log dosyasına yazılamadı: {Path} ({Count} girdi)",
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
                await RunCleanupAsync(ct);
                await Task.Delay(CleanupInterval, ct);
            }
        }
        catch (OperationCanceledException) { /* kapanış */ }
    }

    private async Task RunCleanupAsync(CancellationToken ct)
    {
        try
        {
            if (!Directory.Exists(_options.RootPath)) return;

            foreach (var companyDir in Directory.EnumerateDirectories(_options.RootPath))
            {
                var companyId = AuditFileNaming.ParseCompanyId(Path.GetFileName(companyDir));
                if (companyId is null) continue;

                int retentionDays;
                try { retentionDays = await _retention.GetRetentionDaysAsync(companyId.Value, ct); }
                catch { retentionDays = Constants.AuditParameters.DefaultRetentionDays; }
                if (retentionDays <= 0) continue; // 0 = süresiz sakla

                var cutoff = DateTime.UtcNow.Date.AddDays(-retentionDays);
                var deleted = 0;

                foreach (var monthDir in Directory.EnumerateDirectories(companyDir))
                {
                    foreach (var file in Directory.EnumerateFiles(monthDir,
                                 AuditFileNaming.FilePrefix + "*" + AuditFileNaming.FileExtension))
                    {
                        var day = AuditFileNaming.ParseDay(Path.GetFileName(file));
                        if (day is null || day.Value >= cutoff) continue;
                        try { File.Delete(file); deleted++; }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "[Audit] Eski log dosyası silinemedi: {File}", file);
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
                        "[Audit] Retention temizliği: şirket {CompanyId}, {Deleted} gün dosyası silindi (> {Days} gün).",
                        companyId, deleted, retentionDays);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Audit] Retention temizliği başarısız.");
        }
    }
}
