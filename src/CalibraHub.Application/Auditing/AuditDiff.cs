using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;

namespace CalibraHub.Application.Auditing;

/// <summary>
/// Reflection tabanlı alan diff motoru — iki snapshot nesnesini karşılaştırıp
/// yalnızca DEĞİŞEN skaler alanları döner ("sadece hangi alanın düzeltildiği" kuralı).
///
/// Kurallar:
///   - Yalnızca skaler property'ler karşılaştırılır (string, sayı, bool, tarih, enum, Guid).
///     Koleksiyon ve nested obje property'leri atlanır — kalem satırları gibi child
///     listeler çağıran tarafça ayrıca diff'lenir (bkz. IAuditTrailService.LogChanges).
///   - Audit kolonları (Created/Updated/CreatedBy/...) her zaman yok sayılır.
///   - Farklı tiplerdeki nesneler property ADI eşleşmesiyle karşılaştırılabilir
///     (ör. eski entity ↔ yeni request DTO'su).
///   - null ↔ boş string farkı değişiklik sayılmaz; string'ler Trim'lenmiş karşılaştırılır.
///   - decimal 5.00 ↔ 5 gibi gösterim farkları değişiklik sayılmaz.
/// </summary>
public static class AuditDiff
{
    /// <summary>Her diff'te otomatik yok sayılan property adları.</summary>
    private static readonly HashSet<string> DefaultIgnored = new(StringComparer.OrdinalIgnoreCase)
    {
        "Id", "Created", "Updated", "CreatedAt", "UpdatedAt",
        "CreatedBy", "UpdatedBy", "CreatedById", "UpdatedById", "RowVersion",
    };

    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertyCache = new();

    /// <summary>
    /// İki snapshot arasındaki değişen alanları hesaplar.
    /// </summary>
    /// <param name="oldSnapshot">Eski durum (insert senaryosunda null olabilir).</param>
    /// <param name="newSnapshot">Yeni durum.</param>
    /// <param name="entity">Etiket çözümlemesinde kullanılacak entity kodu (opsiyonel).</param>
    /// <param name="ignore">Ek yok sayılacak property adları (opsiyonel).</param>
    public static List<AuditFieldChange> Compute(object? oldSnapshot, object? newSnapshot,
        string? entity = null, IEnumerable<string>? ignore = null)
    {
        var changes = new List<AuditFieldChange>();
        if (newSnapshot is null && oldSnapshot is null) return changes;

        var extraIgnore = ignore is null
            ? null
            : new HashSet<string>(ignore, StringComparer.OrdinalIgnoreCase);

        // Yeni nesnenin property'leri üzerinden yürü; eski nesnede aynı ada sahip
        // property varsa değerini al (tip farklı olabilir), yoksa alanı atla.
        var reference = newSnapshot ?? oldSnapshot!;
        var newProps = GetScalarProperties(reference.GetType());
        var oldProps = oldSnapshot is null
            ? null
            : GetScalarProperties(oldSnapshot.GetType()).ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var prop in newProps)
        {
            if (DefaultIgnored.Contains(prop.Name)) continue;
            if (extraIgnore is not null && extraIgnore.Contains(prop.Name)) continue;

            object? newVal = newSnapshot is null ? null : SafeGet(prop, newSnapshot);
            object? oldVal = null;
            if (oldSnapshot is not null)
            {
                if (oldProps!.TryGetValue(prop.Name, out var oldProp))
                    oldVal = SafeGet(oldProp, oldSnapshot);
                else
                    continue; // eski snapshot'ta bu alan yok → karşılaştırılamaz
            }

            var oldStr = Normalize(oldVal);
            var newStr = Normalize(newVal);
            if (string.Equals(oldStr ?? "", newStr ?? "", StringComparison.Ordinal)) continue;

            changes.Add(new AuditFieldChange(
                prop.Name,
                AuditFieldLabels.Resolve(entity, prop.Name),
                oldStr,
                newStr));
        }

        return changes;
    }

    /// <summary>
    /// Değeri log dosyasına yazılacak normalize string'e çevirir.
    /// null/boş → null; decimal → gereksiz sıfırlar atılır; tarih → "yyyy-MM-dd HH:mm:ss".
    /// </summary>
    public static string? Normalize(object? value)
    {
        switch (value)
        {
            case null: return null;
            case string s:
                var t = s.Trim();
                return t.Length == 0 ? null : t;
            case bool b: return b ? "true" : "false";
            case decimal d: return d.ToString("0.############################", CultureInfo.InvariantCulture);
            case double dbl: return dbl.ToString("0.############################", CultureInfo.InvariantCulture);
            case float f: return f.ToString("0.############################", CultureInfo.InvariantCulture);
            case DateTime dt:
                // Saat bileşeni yoksa yalnızca tarih yaz — "01.01.2026 00:00" gürültüsü olmasın
                return dt.TimeOfDay == TimeSpan.Zero
                    ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            case DateTimeOffset dto: return dto.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            case DateOnly dOnly: return dOnly.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            case TimeOnly tOnly: return tOnly.ToString("HH:mm", CultureInfo.InvariantCulture);
            case Enum e: return e.ToString();
            default:
                return Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }

    private static object? SafeGet(PropertyInfo prop, object target)
    {
        try { return prop.GetValue(target); }
        catch { return null; }
    }

    private static PropertyInfo[] GetScalarProperties(Type type) =>
        PropertyCache.GetOrAdd(type, static t => t
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && IsScalar(p.PropertyType))
            .ToArray());

    private static bool IsScalar(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        if (t == typeof(string) || t == typeof(decimal) || t == typeof(Guid) ||
            t == typeof(DateTime) || t == typeof(DateTimeOffset) ||
            t == typeof(DateOnly) || t == typeof(TimeOnly))
            return true;
        if (t.IsEnum || t.IsPrimitive) return true;
        // string dışındaki IEnumerable'lar (koleksiyonlar) ve kompleks tipler skaler değildir
        if (typeof(IEnumerable).IsAssignableFrom(t)) return false;
        return false;
    }
}
