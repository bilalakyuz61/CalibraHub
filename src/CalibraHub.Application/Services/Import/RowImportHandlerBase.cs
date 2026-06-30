using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Services.Import;

/// <summary>
/// Satır-bazlı (her Excel satırı = tek kayıt) içe-aktarım handler'ları için ortak taban.
/// Preview/Commit döngüsünü yönetir; alt sınıflar yalnız ValidateRow / ResolveActionAsync /
/// CommitRowAsync sağlar. (Reçete gibi başlık+kalem entity'leri bu tabanı KULLANMAZ;
/// IImportTargetHandler'ı doğrudan implemente eder.)
/// </summary>
public abstract class RowImportHandlerBase : IImportTargetHandler
{
    private const int PreviewDetailLimit = 500;

    public abstract string Entity { get; }
    public abstract string Label { get; }
    public abstract IReadOnlyList<ImportTargetFieldDto> GetFields();

    /// <summary>Tek satırın doğrulama hataları (boş = geçerli).</summary>
    protected abstract IReadOnlyList<string> ValidateRow(IReadOnlyDictionary<string, string?> row);

    /// <summary>Upsert anahtarına göre insert/update kararı + mevcut kayıt Id'si.</summary>
    protected abstract Task<(string Action, int? ExistingId)> ResolveActionAsync(
        IReadOnlyDictionary<string, string?> row, string? matchKeyField, CancellationToken ct);

    /// <summary>Tek satırı kaydet. usedCodes aynı dosya içinde kod çakışmasını önlemek içindir.</summary>
    protected abstract Task<(bool Ok, string? Error, int? RecordId)> CommitRowAsync(
        IReadOnlyDictionary<string, string?> row, string action, int? existingId,
        int? userId, HashSet<string> usedCodes, CancellationToken ct);

    public virtual async Task<ImportPreviewResultDto> PreviewAsync(ImportRowSet set, CancellationToken ct)
    {
        await PreloadAsync(ct);
        var (keys, labels) = DisplayCols(set.MappedKeys);
        int total = 0, valid = 0, error = 0, ins = 0, upd = 0;
        var detail = new List<ImportPreviewRowDto>();

        foreach (var row in set.Rows)
        {
            ct.ThrowIfCancellationRequested();
            total++;
            var errs = ValidateRow(row);
            string action;
            if (errs.Count > 0) { action = "error"; error++; }
            else
            {
                var (a, _) = await ResolveActionAsync(row, set.MatchKeyField, ct);
                action = a; valid++;
                if (a == "update") upd++; else ins++;
            }
            if (detail.Count < PreviewDetailLimit)
            {
                var cells = keys.Select(k => new ImportPreviewCellDto(k, row.TryGetValue(k, out var v) ? v : null)).ToList();
                detail.Add(new ImportPreviewRowDto(total, action, cells, errs));
            }
        }
        return new ImportPreviewResultDto(true, null, total, valid, error, ins, upd, keys, labels, detail);
    }

    public async Task<ImportCommitResultDto> CommitAsync(ImportRowSet set, int? userId, CancellationToken ct)
    {
        await PreloadAsync(ct);
        int inserted = 0, updated = 0, failed = 0, rowNo = 0;
        var results = new List<ImportCommitRowDto>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in set.Rows)
        {
            ct.ThrowIfCancellationRequested();
            rowNo++;
            var errs = ValidateRow(row);
            if (errs.Count > 0) { results.Add(new ImportCommitRowDto(rowNo, false, "error", string.Join("; ", errs), null)); failed++; continue; }
            try
            {
                var (action, existingId) = await ResolveActionAsync(row, set.MatchKeyField, ct);
                var (ok, err, id) = await CommitRowAsync(row, action, existingId, userId, used, ct);
                if (ok) { if (action == "update") updated++; else inserted++; results.Add(new ImportCommitRowDto(rowNo, true, action, null, id)); }
                else { failed++; results.Add(new ImportCommitRowDto(rowNo, false, action, err ?? "Bilinmeyen hata", null)); }
            }
            catch (Exception ex) { failed++; results.Add(new ImportCommitRowDto(rowNo, false, "error", ex.Message, null)); }
        }
        return new ImportCommitResultDto(true, null, inserted, updated, failed, results);
    }

    /// <summary>Varsayılan: dinamik izinli değer yok. Lookup'lu handler (ContactPerson) override eder.</summary>
    public virtual Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetDynamicAllowedValuesAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<string>>>(
            new Dictionary<string, IReadOnlyList<string>>());

    /// <summary>Dinamik durum yükleme (varsayılan no-op). Widget'lı handler override eder.</summary>
    public virtual Task PreloadAsync(CancellationToken ct) => Task.CompletedTask;

    // ── Ortak yardımcılar ────────────────────────────────────────────────
    protected (IReadOnlyList<string> Keys, IReadOnlyList<string> Labels) DisplayCols(IReadOnlyList<string> mappedKeys)
    {
        var keys = new List<string>();
        var labels = new List<string>();
        foreach (var f in GetFields())
            if (mappedKeys.Any(k => string.Equals(k, f.Key, StringComparison.OrdinalIgnoreCase)))
            { keys.Add(f.Key); labels.Add(f.Label); }
        return (keys, labels);
    }

    protected static string? Get(IReadOnlyDictionary<string, string?> d, string key) => d.TryGetValue(key, out var v) ? v : null;
    protected static string DigitsOnly(string s) => new(s.Where(char.IsDigit).ToArray());

    protected static bool ParseBool(string? raw)
    {
        var v = (raw ?? "").Trim().ToLowerInvariant();
        return v is "1" or "true" or "evet" or "e" or "x" or "var" or "yes" or "✓";
    }

    protected static decimal? ParseDecimal(string? raw)
    {
        var v = (raw ?? "").Trim();
        if (string.IsNullOrEmpty(v)) return null;
        v = v.Replace(" ", "");
        // Türkçe ondalık: virgül → nokta (binlik nokta yoksa). Hem "18,5" hem "18.5" çalışır.
        if (v.Contains(',') && !v.Contains('.')) v = v.Replace(',', '.');
        else if (v.Contains(',') && v.Contains('.')) v = v.Replace(".", "").Replace(',', '.');
        return decimal.TryParse(v, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
    }
}
