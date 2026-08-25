using System.Globalization;
using System.Text;
using System.Text.Json;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// Entegratör belge aktarım logunun DOSYA tabanlı deposu.
///
/// NEDEN (2026-08-25, kullanıcı kararı): bu log daha önce <c>PLT_SISTEM_LOG</c> tablosuna
/// yazılıyordu. O tablo CalibraHub'a ait değil — dış bir sistemin şema konvansiyonu
/// (VERITABANI / UYGULAMA_ID / MODUL_NO / S_SAHA_01…). Bağ tamamen kesildi: artık o tabloya
/// ne yazılıyor ne de okunuyor.
///
/// Yeni depo, projenin kendi log deseniyle aynı: günlük JSONL dosyaları (audit trail ve
/// hata logu da böyle — "log DB'ye yazılmaz" bilinçli kararı). Yapı:
///   {root}/{yyyy-MM}/integrator-{yyyy-MM-dd}.jsonl
///
/// MEVCUT TABLO SİLİNMEZ. İçindeki geçmiş kayıtlar müşteri verisidir; okumayı bırakmak
/// yeterlidir, DROP etmek geri dönüşü olmayan bir kayıp olurdu.
/// </summary>
public sealed class FileIntegratorImportLogRepository : IIntegratorImportLogRepository
{
    private const string FilePrefix = "integrator-";
    private const string FileExtension = ".jsonl";

    /// <summary>Aynı anda iki yazıcı aynı satırı bölmesin — süreç içi tek kilit yeter.</summary>
    private static readonly SemaphoreSlim WriteLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly string _rootPath;

    public FileIntegratorImportLogRepository(IntegratorImportLogOptions options)
        => _rootPath = options.RootPath;

    public async Task WriteAsync(IntegratorImportLogWriteRequest request, CancellationToken cancellationToken)
    {
        if (request is null) return;

        var occurredAt = request.OccurredAt ?? DateTime.Now;
        var line = JsonSerializer.Serialize(new StoredEntry(
            occurredAt,
            request.IntegratorSettingsId,
            request.CompanyId,
            request.IntegratorName ?? string.Empty,
            request.Level ?? "Info",
            request.Message ?? string.Empty,
            request.ImportedCount,
            request.SkippedCount), JsonOptions);

        var path = DayFile(occurredAt);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await WriteLock.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(path, line + Environment.NewLine, Encoding.UTF8, cancellationToken);
        }
        finally
        {
            WriteLock.Release();
        }
    }

    public async Task<IReadOnlyCollection<IntegratorImportLogEntryDto>> GetRecentAsync(
        int take, CancellationToken cancellationToken)
    {
        if (take <= 0) return Array.Empty<IntegratorImportLogEntryDto>();
        var effectiveTake = Math.Clamp(take, 1, 1000);

        var result = new List<IntegratorImportLogEntryDto>(effectiveTake);
        foreach (var file in EnumerateDayFilesDescending())
        {
            cancellationToken.ThrowIfCancellationRequested();

            List<string> lines;
            try
            {
                lines = (await File.ReadAllLinesAsync(file, cancellationToken)).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Okunamayan tek dosya listeyi tamamen boş bırakmasın — diğerlerine devam.
                continue;
            }

            // Dosya içi sıra kronolojik (eski→yeni); listeleme yeniden-eskiye.
            for (var i = lines.Count - 1; i >= 0 && result.Count < effectiveTake; i--)
            {
                var entry = Deserialize(lines[i]);
                if (entry is not null) result.Add(entry);
            }
            if (result.Count >= effectiveTake) break;
        }
        return result;
    }

    /// <summary>
    /// Saklama süresi dolmuş GÜN DOSYALARINI siler. Tablo sürümünden farkı: silme dosya
    /// düzeyindedir, satır düzeyinde değil — bu yüzden entegratör bazında ayrıştırma yapılmaz.
    /// <paramref name="integratorSettingsId"/> imza uyumu için durur; dosya deposunda
    /// entegratör başına ayrı saklama süresi UYGULANMAZ (en uzun süre kazanır demektir).
    /// </summary>
    public Task CleanupExpiredAsync(int integratorSettingsId, int retentionDays, CancellationToken cancellationToken)
    {
        if (retentionDays <= 0) return Task.CompletedTask;   // 0 = süresiz sakla
        if (!Directory.Exists(_rootPath)) return Task.CompletedTask;

        var cutoff = DateTime.Now.Date.AddDays(-retentionDays);
        foreach (var monthDir in SafeDirs(_rootPath))
        {
            foreach (var file in SafeFiles(monthDir))
            {
                var day = ParseDay(Path.GetFileName(file));
                if (day is null || day.Value >= cutoff) continue;
                try { File.Delete(file); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Kilitli dosya bir sonraki temizlikte silinir — temizlik akışı durmamalı.
                }
            }
        }
        return Task.CompletedTask;
    }

    // ── Yardımcılar ─────────────────────────────────────────────────────────

    private string DayFile(DateTime day) => Path.Combine(
        _rootPath,
        day.ToString("yyyy-MM", CultureInfo.InvariantCulture),
        FilePrefix + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + FileExtension);

    private static DateTime? ParseDay(string fileName)
    {
        if (!fileName.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase) ||
            !fileName.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase))
            return null;
        var core = fileName.Substring(FilePrefix.Length, fileName.Length - FilePrefix.Length - FileExtension.Length);
        return DateTime.TryParseExact(core, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var day) ? day.Date : null;
    }

    private IEnumerable<string> EnumerateDayFilesDescending()
    {
        if (!Directory.Exists(_rootPath)) yield break;
        var files = new List<(DateTime Day, string Path)>();
        foreach (var monthDir in SafeDirs(_rootPath))
            foreach (var file in SafeFiles(monthDir))
            {
                var day = ParseDay(Path.GetFileName(file));
                if (day is not null) files.Add((day.Value, file));
            }
        foreach (var f in files.OrderByDescending(f => f.Day)) yield return f.Path;
    }

    private static IEnumerable<string> SafeDirs(string root)
    {
        try { return Directory.GetDirectories(root); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    private static IEnumerable<string> SafeFiles(string dir)
    {
        try { return Directory.GetFiles(dir, FilePrefix + "*" + FileExtension); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    private static IntegratorImportLogEntryDto? Deserialize(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try
        {
            var e = JsonSerializer.Deserialize<StoredEntry>(line, JsonOptions);
            if (e is null) return null;
            return new IntegratorImportLogEntryDto(
                OccurredAt: e.OccurredAt,
                IntegratorSettingsId: e.IntegratorSettingsId,
                CompanyId: e.CompanyId,
                // Şirket ADI dosyada tutulmaz: şirket sonradan yeniden adlandırılırsa
                // dosyadaki kopya eskir ve yanlış ad gösterirdi. Ekran gerekirse Id'den çözer.
                CompanyName: string.Empty,
                IntegratorName: e.IntegratorName,
                Level: e.Level,
                Message: e.Message,
                ImportedCount: e.ImportedCount,
                SkippedCount: e.SkippedCount,
                SourceFileName: string.Empty);
        }
        catch (JsonException)
        {
            // Bozuk tek satır tüm listeyi düşürmesin — atlanır.
            return null;
        }
    }

    /// <summary>Diske yazılan satırın biçimi (DTO'dan AYRI: dosya biçimi ekran sözleşmesine bağlı kalmamalı).</summary>
    private sealed record StoredEntry(
        DateTime OccurredAt,
        int IntegratorSettingsId,
        int? CompanyId,
        string IntegratorName,
        string Level,
        string Message,
        int ImportedCount,
        int SkippedCount);
}

/// <summary>
/// Entegratör aktarım logu dosya deposu ayarları. Program.cs'de set edilir:
/// appsettings "Diagnostics:IntegratorLogRootPath" ?? {ContentRoot}/App_Data/IntegratorLogs.
/// </summary>
public sealed class IntegratorImportLogOptions
{
    public required string RootPath { get; init; }
}
