using System.Globalization;

namespace CalibraHub.Application.Auditing;

/// <summary>
/// Audit dosya deposu ayarları. RootPath Program.cs'de set edilir:
/// appsettings "AuditTrail:RootPath" ?? {ContentRoot}/App_Data/AuditLogs.
/// </summary>
public sealed class AuditTrailOptions
{
    public required string RootPath { get; init; }
}

/// <summary>
/// Günlük log dosyası yol/adlandırma kuralları — yazıcı ve okuyucu ortak kullanır.
/// Yapı: {root}/company-{id}/{yyyy-MM}/audit-{yyyy-MM-dd}.jsonl
/// </summary>
public static class AuditFileNaming
{
    public const string FilePrefix = "audit-";
    public const string FileExtension = ".jsonl";

    public static string CompanyDir(string root, int companyId) =>
        Path.Combine(root, "company-" + companyId.ToString(CultureInfo.InvariantCulture));

    public static string DayFile(string root, int companyId, DateTime dayUtc) =>
        Path.Combine(
            CompanyDir(root, companyId),
            dayUtc.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            FilePrefix + dayUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + FileExtension);

    /// <summary>Dosya adından gün çıkarır ("audit-2026-07-10.jsonl" → 2026-07-10). Uymayan ad → null.</summary>
    public static DateTime? ParseDay(string fileName)
    {
        if (!fileName.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase) ||
            !fileName.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase))
            return null;
        var core = fileName.Substring(FilePrefix.Length,
            fileName.Length - FilePrefix.Length - FileExtension.Length);
        return DateTime.TryParseExact(core, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var day)
            ? day.Date
            : null;
    }

    /// <summary>Klasör adından şirket id çıkarır ("company-3" → 3). Uymayan ad → null.</summary>
    public static int? ParseCompanyId(string dirName) =>
        dirName.StartsWith("company-", StringComparison.OrdinalIgnoreCase) &&
        int.TryParse(dirName.AsSpan("company-".Length), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;

    /// <summary>
    /// Şirketin [from..to] aralığındaki MEVCUT gün dosyalarını YENİDEN ESKİYE sıralı döner.
    /// Ay klasörleri üzerinden gezer; olmayan günler atlanır.
    /// </summary>
    public static IEnumerable<(DateTime Day, string Path)> EnumerateDayFilesDescending(
        string root, int companyId, DateTime fromUtc, DateTime toUtc)
    {
        var companyDir = CompanyDir(root, companyId);
        if (!Directory.Exists(companyDir)) yield break;

        var from = fromUtc.Date;
        var to = toUtc.Date;

        foreach (var monthDir in Directory.EnumerateDirectories(companyDir)
                     .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal))
        {
            foreach (var file in Directory.EnumerateFiles(monthDir, FilePrefix + "*" + FileExtension)
                         .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal))
            {
                var day = ParseDay(Path.GetFileName(file));
                if (day is null) continue;
                if (day.Value < from || day.Value > to) continue;
                yield return (day.Value, file);
            }
        }
    }
}
