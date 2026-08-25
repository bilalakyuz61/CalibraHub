using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Diagnostics;

/// <summary>
/// <see cref="Application.Auditing.AuditQueryService"/> ile aynı okuma deseni — gün dosyaları
/// yeniden-eskiye açılır; her satır önce ucuz IndexOf ön-filtresinden geçer, yalnızca geçen
/// satırlar JSON parse edilir. Dosyalar FileShare.ReadWrite ile açılır — yazıcı ile çakışmaz.
/// </summary>
public sealed class SystemErrorLogQueryService : ISystemErrorLogQueryService
{
    private readonly SystemErrorLogOptions _options;
    private readonly ILogger<SystemErrorLogQueryService> _logger;

    public SystemErrorLogQueryService(SystemErrorLogOptions options, ILogger<SystemErrorLogQueryService> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<SystemErrorSearchResult> SearchAsync(SystemErrorSearchRequest request, CancellationToken ct)
    {
        var matches = new List<SystemErrorEntry>();

        // Açılır liste seçenekleri, kullanıcı/kaynak seçimi UYGULANMADAN toplanır: seçim
        // yapıldığında listenin tek seçeneğe düşüp seçimin geri alınamaz hale gelmemesi için.
        var users = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var categories = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var exceptionTypes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (_, path) in SystemErrorLogFileNaming.EnumerateDayFilesDescending(
                     _options.RootPath, request.FromUtc, request.ToUtc))
        {
            ct.ThrowIfCancellationRequested();
            var dayMatches = new List<SystemErrorEntry>();

            await foreach (var line in ReadLinesAsync(path, ct))
            {
                if (!QuickFilter(line, request)) continue;

                var entry = SystemErrorLogJson.Deserialize(line);
                if (entry is null) continue;
                if (!FullFilter(entry, request)) continue;

                if (!string.IsNullOrWhiteSpace(entry.UserName)) users.Add(entry.UserName!);
                if (!string.IsNullOrWhiteSpace(entry.Category)) categories.Add(entry.Category!);
                if (!string.IsNullOrWhiteSpace(entry.ExceptionType)) exceptionTypes.Add(entry.ExceptionType!);

                if (!SelectionFilter(entry, request)) continue;

                dayMatches.Add(entry);
            }

            // Dosya içi sıra kronolojik (eski→yeni); global sıralama yeniden-eskiye
            dayMatches.Reverse();
            matches.AddRange(dayMatches);
        }

        List<SystemErrorEntry> items;
        if (request.Unpaged)
        {
            items = matches;
        }
        else
        {
            var page = Math.Max(1, request.Page);
            var size = Math.Clamp(request.PageSize, 1, 500);
            items = matches.Skip((page - 1) * size).Take(size).ToList();
        }

        return new SystemErrorSearchResult(
            items, matches.Count, users.ToList(), categories.ToList(), exceptionTypes.ToList());
    }

    // ── Yardımcılar ─────────────────────────────────────────────────────────

    /// <summary>JSON parse öncesi ucuz satır ön-filtresi.</summary>
    private static bool QuickFilter(string line, SystemErrorSearchRequest req)
    {
        if (!string.IsNullOrEmpty(req.Level) &&
            !string.Equals(req.Level, "all", StringComparison.OrdinalIgnoreCase) &&
            !line.Contains("\"level\":\"" + req.Level + "\"", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrEmpty(req.Text) &&
            !line.Contains(req.Text, StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private static bool FullFilter(SystemErrorEntry entry, SystemErrorSearchRequest req)
    {
        if (!string.IsNullOrEmpty(req.Level) &&
            !string.Equals(req.Level, "all", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(entry.Level, req.Level, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrEmpty(req.Text))
        {
            var text = req.Text;
            var hit =
                (entry.Category?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (entry.Source?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (entry.Message?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (entry.ExceptionMessage?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (entry.ExceptionType?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false);
            if (!hit) return false;
        }
        if (entry.TimestampUtc < req.FromUtc || entry.TimestampUtc >= req.ToUtc) return false;
        // Şirket kapsamı: bu şirket VEYA şirketsiz (açılış/worker/kimliksiz istek hataları).
        if (req.CompanyScope is int scope && entry.CompanyId is int owner && owner != scope) return false;
        return true;
    }

    /// <summary>
    /// Açılır listelerden yapılan seçimler (kullanıcı / kaynak). <see cref="FullFilter"/>'dan
    /// AYRI tutulur: seçenek listesi bu süzgeçten geçmeyen kayıtlardan da beslenir.
    /// Eşleşme TAM — seçenekler zaten log'un kendi değerlerinden üretiliyor.
    /// </summary>
    private static bool SelectionFilter(SystemErrorEntry entry, SystemErrorSearchRequest req)
    {
        if (!string.IsNullOrWhiteSpace(req.UserName) &&
            !string.Equals(entry.UserName, req.UserName, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(req.Category) &&
            !string.Equals(entry.Category, req.Category, StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private async IAsyncEnumerable<string> ReadLinesAsync(string path,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        FileStream? fs = null;
        StreamReader? reader = null;
        try
        {
            fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                bufferSize: 64 * 1024, useAsync: true);
            reader = new StreamReader(fs);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "[ErrorLog] Log dosyası okunamadı: {Path}", path);
            fs?.Dispose();
            yield break;
        }

        try
        {
            while (!ct.IsCancellationRequested)
            {
                string? line;
                try { line = await reader.ReadLineAsync(ct); }
                catch (Exception ex) when (ex is IOException)
                {
                    _logger.LogWarning(ex, "[ErrorLog] Log dosyası okuma kesildi: {Path}", path);
                    yield break;
                }
                if (line is null) yield break;
                if (line.Length == 0) continue;
                yield return line;
            }
        }
        finally
        {
            reader.Dispose();
        }
    }
}
