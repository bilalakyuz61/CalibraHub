using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Auditing;

/// <summary>
/// Günlük JSONL dosyalarını akış halinde okuyup filtreleyen sorgu servisi.
/// Şirket, aktif isteğin claim'inden (IAuditContextProvider) çözümlenir.
///
/// Performans yaklaşımı: gün dosyaları yeniden-eskiye açılır; her satır önce
/// ucuz IndexOf ön-filtresinden geçer (action/entity/entityId/serbest metin),
/// yalnızca geçen satırlar JSON parse edilir. Dosyalar FileShare.ReadWrite ile
/// açılır — yazıcı ile çakışmaz.
/// </summary>
public sealed class AuditQueryService : IAuditQueryService
{
    private readonly AuditTrailOptions _options;
    private readonly IAuditContextProvider _context;
    private readonly ILogger<AuditQueryService> _logger;

    public AuditQueryService(AuditTrailOptions options, IAuditContextProvider context,
        ILogger<AuditQueryService> logger)
    {
        _options = options;
        _context = context;
        _logger = logger;
    }

    public async Task<AuditSearchResult> SearchAsync(AuditSearchRequest request, CancellationToken ct)
    {
        var companyId = _context.Resolve().CompanyId;
        if (companyId <= 0)
            return new AuditSearchResult([], 0, [], []);

        var matches = new List<AuditEntry>();
        var entities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var users = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (_, path) in AuditFileNaming.EnumerateDayFilesDescending(
                     _options.RootPath, companyId, request.FromUtc, request.ToUtc))
        {
            ct.ThrowIfCancellationRequested();
            var dayMatches = new List<AuditEntry>();

            await foreach (var line in ReadLinesAsync(path, ct))
            {
                if (!QuickFilter(line, request)) continue;

                var entry = AuditJson.Deserialize(line);
                if (entry is null) continue;
                if (!FullFilter(entry, request)) continue;

                dayMatches.Add(entry);
                if (entry.Entity is not null) entities.Add(entry.Entity);
                if (entry.User is not null) users.Add(entry.User);
            }

            // Dosya içi sıra kronolojik (eski→yeni); global sıralama yeniden-eskiye
            dayMatches.Reverse();
            matches.AddRange(dayMatches);
        }

        var page = Math.Max(1, request.Page);
        var size = Math.Clamp(request.PageSize, 1, 500);
        var items = matches.Skip((page - 1) * size).Take(size).ToList();

        return new AuditSearchResult(
            items,
            matches.Count,
            entities.OrderBy(e => AuditFieldLabels.EntityLabel(e), StringComparer.CurrentCultureIgnoreCase).ToList(),
            users.OrderBy(u => u, StringComparer.CurrentCultureIgnoreCase).ToList());
    }

    public async Task<IReadOnlyList<AuditEntry>> GetRecordTrailAsync(string entity, string entityId,
        int maxItems, CancellationToken ct)
    {
        var companyId = _context.Resolve().CompanyId;
        if (companyId <= 0 || string.IsNullOrWhiteSpace(entity) || string.IsNullOrWhiteSpace(entityId))
            return [];

        // Ucuz ön-filtre desenleri — camelCase serialize edilen alan adlarıyla eşleşir
        var entityToken = "\"entity\":\"" + entity + "\"";
        var idToken = "\"entityId\":\"" + entityId + "\"";
        var max = Math.Clamp(maxItems, 1, 1000);

        var results = new List<AuditEntry>();
        foreach (var (_, path) in AuditFileNaming.EnumerateDayFilesDescending(
                     _options.RootPath, companyId, DateTime.MinValue, DateTime.MaxValue))
        {
            ct.ThrowIfCancellationRequested();
            var dayMatches = new List<AuditEntry>();

            await foreach (var line in ReadLinesAsync(path, ct))
            {
                if (!line.Contains(entityToken, StringComparison.Ordinal) ||
                    !line.Contains(idToken, StringComparison.Ordinal))
                    continue;

                var entry = AuditJson.Deserialize(line);
                if (entry is null) continue;
                if (!string.Equals(entry.Entity, entity, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(entry.EntityId, entityId, StringComparison.Ordinal)) continue;
                dayMatches.Add(entry);
            }

            dayMatches.Reverse();
            results.AddRange(dayMatches);
            if (results.Count >= max) break;
        }

        return results.Count > max ? results.Take(max).ToList() : results;
    }

    public async Task<AuditStats> GetStatsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var companyId = _context.Resolve().CompanyId;
        int total = 0, inserts = 0, updates = 0, deletes = 0, security = 0;
        var byDay = new Dictionary<string, int>(StringComparer.Ordinal);
        var byEntity = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var byUser = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        if (companyId > 0)
        {
            foreach (var (_, path) in AuditFileNaming.EnumerateDayFilesDescending(
                         _options.RootPath, companyId, fromUtc, toUtc))
            {
                ct.ThrowIfCancellationRequested();

                await foreach (var line in ReadLinesAsync(path, ct))
                {
                    var entry = AuditJson.Deserialize(line);
                    if (entry is null) continue;
                    if (entry.Ts < fromUtc || entry.Ts >= toUtc) continue;

                    total++;
                    // Gün dağılımı yerel güne göre (kullanıcının gördüğü takvim)
                    var dayKey = entry.Ts.ToLocalTime().ToString("yyyy-MM-dd");
                    byDay[dayKey] = byDay.GetValueOrDefault(dayKey) + 1;
                    switch (entry.Action)
                    {
                        case AuditActions.Insert: inserts++; break;
                        case AuditActions.Update: updates++; break;
                        case AuditActions.Delete: deletes++; break;
                        case AuditActions.Login:
                        case AuditActions.LoginFailed:
                        case AuditActions.Logout: security++; break;
                    }
                    if (entry.Entity is not null)
                        byEntity[entry.Entity] = byEntity.GetValueOrDefault(entry.Entity) + 1;
                    if (entry.User is not null)
                        byUser[entry.User] = byUser.GetValueOrDefault(entry.User) + 1;
                }
            }
        }

        return new AuditStats(
            total, inserts, updates, deletes, security,
            byUser.Count,
            byDay.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => new AuditDayCount(kv.Key, kv.Value)).ToList(),
            byEntity.OrderByDescending(kv => kv.Value).Take(8)
                .Select(kv => new AuditKeyCount(kv.Key, AuditFieldLabels.EntityLabel(kv.Key), kv.Value)).ToList(),
            byUser.OrderByDescending(kv => kv.Value).Take(8)
                .Select(kv => new AuditKeyCount(kv.Key, kv.Key, kv.Value)).ToList());
    }

    // ── Yardımcılar ─────────────────────────────────────────────────────────

    /// <summary>JSON parse öncesi ucuz satır ön-filtresi.</summary>
    private static bool QuickFilter(string line, AuditSearchRequest req)
    {
        if (!string.IsNullOrEmpty(req.Action) &&
            !line.Contains("\"action\":\"" + req.Action + "\"", StringComparison.Ordinal))
            return false;
        if (!string.IsNullOrEmpty(req.Entity) &&
            !line.Contains("\"entity\":\"" + req.Entity + "\"", StringComparison.Ordinal))
            return false;
        if (!string.IsNullOrEmpty(req.Text) &&
            !line.Contains(req.Text, StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private static bool FullFilter(AuditEntry entry, AuditSearchRequest req)
    {
        if (!string.IsNullOrEmpty(req.Action) &&
            !string.Equals(entry.Action, req.Action, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrEmpty(req.Entity) &&
            !string.Equals(entry.Entity, req.Entity, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrEmpty(req.User) &&
            !(entry.User?.Contains(req.User, StringComparison.OrdinalIgnoreCase) ?? false))
            return false;
        if (entry.Ts < req.FromUtc || entry.Ts >= req.ToUtc) return false;
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
            _logger.LogWarning(ex, "[Audit] Log dosyası okunamadı: {Path}", path);
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
                    _logger.LogWarning(ex, "[Audit] Log dosyası okuma kesildi: {Path}", path);
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
