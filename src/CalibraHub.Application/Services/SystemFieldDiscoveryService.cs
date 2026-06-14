using System.ComponentModel;
using System.Reflection;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Services;

/// <summary>
/// Universal Form Engine — Sprint 1 POC implementasyonu.
///
/// Domain entity public property'lerini reflection ile okur, hedef form'a
/// WidgetDefinition (IsSystemField=true) olarak seed eder. Var olan
/// IsSystemField satirlari dokunulmaz (idempotent).
///
/// Property → DataType eslestirme:
///   string                  → "text"
///   bool / bool?            → "boolean"
///   DateTime / DateTime?    → "date"
///   numeric (int, decimal,
///   long, short, double,
///   float ve nullable hali) → "numeric"
///   diger                   → atlanir (POC kapsami)
///
/// Skip listesi: Id, CompanyId, IsActive, Combinations gibi sistem/audit kolonlari
/// son kullaniciya widget olarak gosterilmez — kendi pipeline'lariyla yonetilir.
/// </summary>
public sealed class SystemFieldDiscoveryService : ISystemFieldDiscoveryService
{
    private readonly IWidgetRepository _repository;

    public SystemFieldDiscoveryService(IWidgetRepository repository)
    {
        _repository = repository;
    }

    /// <summary>Discovery hedef ciftleri — entity tipi ile baglandigi form kodu.</summary>
    private static readonly (Type EntityType, string FormCode)[] EntityFormPairs =
    [
        (typeof(Item), "ITEMS"),
    ];

    /// <summary>Sistem alanlari widget olarak yazilmaz — entity audit/PK/multi-tenant.</summary>
    private static readonly HashSet<string> SkipColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "Id",
        "CompanyId",
        "IsActive",       // soft-delete pipeline'i ayri
        "Created",        // audit alanlari (CLAUDE.md standardi)
        "Updated",
        "CreateDate",     // legacy isimler (geriye uyum)
        "ModifyDate",
        "CreatedAt",
        "UpdatedAt",
        "CreatedBy",
        "UpdatedBy",
    };

    public async Task<int> DiscoverAndSeedAsync(CancellationToken ct)
    {
        int totalSeeded = 0;
        foreach (var (entityType, formCode) in EntityFormPairs)
        {
            totalSeeded += await SeedForEntityAsync(entityType, formCode, ct);
        }
        return totalSeeded;
    }

    private async Task<int> SeedForEntityAsync(Type entityType, string formCode, CancellationToken ct)
    {
        var form = await _repository.GetFormByCodeAsync(formCode, ct);
        if (form == null) return 0;  // form yok — sessiz no-op

        // Mevcut widget'larin EntityColumn setini cikar (idempotency)
        var existing = await _repository.GetWidgetsByFormAsync(form.Id, ct, includeInactive: true);
        var existingByEntityColumn = new HashSet<string>(
            existing
                .Where(w => w.IsSystemField && !string.IsNullOrWhiteSpace(w.EntityColumn))
                .Select(w => w.EntityColumn!),
            StringComparer.OrdinalIgnoreCase);

        // En yuksek SortOrder + 1'den baslat ki yeni alanlar listenin sonuna gitsin
        int nextSortOrder = existing.Count == 0 ? 10 : existing.Max(w => w.SortOrder) + 10;

        int seeded = 0;
        var props = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var prop in props)
        {
            if (SkipColumns.Contains(prop.Name)) continue;
            if (existingByEntityColumn.Contains(prop.Name)) continue;

            var dataType = MapDataType(prop.PropertyType);
            if (dataType == null) continue;  // POC kapsaminda olmayan tip (Guid, byte[], navigasyon vb.)

            // Label: [Description] attribute varsa kullan, yoksa Pascal-case bol
            var label = ResolveLabel(prop);
            // WidgetCode: lowercase + alt cizgi (orn. "TaxRate" → "tax_rate"). System field icin
            // "sf_" prefix'i ile mark edilir — admin custom widget'larla cakismaz.
            var widgetCode = "sf_" + ToSnakeCase(prop.Name);

            var widget = new WidgetDefinition
            {
                Id = 0,
                FormId = form.Id,
                ParentId = null,
                WidgetCode = widgetCode,
                Label = label,
                DataType = dataType,
                SortOrder = nextSortOrder,
                IsActive = true,
                IsRequired = IsRequiredProperty(prop),
                IsSystemField = true,
                EntityColumn = prop.Name,
                ColSpan = 12,
                LabelStyle = "standard",
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
            };

            await _repository.UpsertWidgetAsync(widget, ct);
            seeded++;
            nextSortOrder += 10;
        }

        return seeded;
    }

    private static string? MapDataType(Type t)
    {
        var underlying = Nullable.GetUnderlyingType(t) ?? t;
        if (underlying == typeof(string)) return "text";
        if (underlying == typeof(bool)) return "boolean";
        if (underlying == typeof(DateTime)) return "date";
        if (underlying == typeof(int) ||
            underlying == typeof(long) ||
            underlying == typeof(short) ||
            underlying == typeof(decimal) ||
            underlying == typeof(double) ||
            underlying == typeof(float))
        {
            return "numeric";
        }
        return null;
    }

    /// <summary>
    /// "required" modifier'i C# 11 nullable annotation'larla discovery anti-pattern;
    /// burada conservative: tip non-nullable reference (string ama nullable degil) veya
    /// non-nullable value type ise required varsayilir. Admin UI override edebilir.
    /// </summary>
    private static bool IsRequiredProperty(PropertyInfo prop)
    {
        // C# 'required' keyword'unu doğrudan reflection ile okumak zor; basit heuristic:
        // nullable degerleri (string?, int?) → IsRequired=false
        var t = prop.PropertyType;
        if (Nullable.GetUnderlyingType(t) != null) return false;
        if (!t.IsValueType)
        {
            // Reference tip — RequiredMember attribute ile mark edilmis mi?
            return prop.CustomAttributes.Any(a =>
                a.AttributeType.FullName == "System.Runtime.CompilerServices.RequiredMemberAttribute");
        }
        return true;  // value type, non-nullable → required
    }

    private static string ResolveLabel(PropertyInfo prop)
    {
        var desc = prop.GetCustomAttribute<DescriptionAttribute>();
        if (desc != null && !string.IsNullOrWhiteSpace(desc.Description))
            return desc.Description;
        return SplitPascalCase(prop.Name);
    }

    private static string SplitPascalCase(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var sb = new System.Text.StringBuilder(s.Length + 4);
        sb.Append(s[0]);
        for (int i = 1; i < s.Length; i++)
        {
            if (char.IsUpper(s[i]) && !char.IsUpper(s[i - 1]))
                sb.Append(' ');
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    private static string ToSnakeCase(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var sb = new System.Text.StringBuilder(s.Length + 4);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && !char.IsUpper(s[i - 1])) sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
