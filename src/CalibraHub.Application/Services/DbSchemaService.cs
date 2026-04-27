using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Services;

public sealed class DbSchemaService : IDbSchemaService
{
    private readonly IDbSchemaRepository _repository;
    // Tablo ismi -> Type cache (hem 'Item', hem 'Items' gibi varyasyonlari destekler)
    private static readonly Lazy<IReadOnlyDictionary<string, Type>> EntityTypeIndex =
        new(BuildEntityTypeIndex, LazyThreadSafetyMode.ExecutionAndPublication);

    public DbSchemaService(IDbSchemaRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<DbTableSummaryDto>> GetTablesAsync(CancellationToken cancellationToken)
    {
        var raw = await _repository.GetTablesAsync(cancellationToken);
        return raw
            .Where(t => CalibraTableCatalog.IsOwned(t.Name))
            .Select(t =>
            {
                var entityType = FindEntityType(t.Name);
                var description = entityType?.GetCustomAttribute<DescriptionAttribute>()?.Description;
                return t with { Description = description };
            })
            .ToList();
    }

    public async Task<DbTableDetailDto?> GetTableDetailAsync(string schema, string name, CancellationToken cancellationToken)
    {
        var detail = await _repository.GetTableDetailAsync(schema, name, cancellationToken);
        if (detail is null) return null;

        var entityType = FindEntityType(name);
        if (entityType is null) return detail; // reflection eslesmesi yoksa raw sema

        var tableDescription = entityType.GetCustomAttribute<DescriptionAttribute>()?.Description;
        var properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var enrichedColumns = detail.Columns
            .Select(c => EnrichColumn(c, properties))
            .ToList();

        return detail with
        {
            Columns = enrichedColumns,
            Description = tableDescription,
            ClrTypeName = entityType.FullName,
        };
    }

    public async Task<string> BuildMermaidErAsync(CancellationToken cancellationToken)
    {
        var tables = (await _repository.GetTablesAsync(cancellationToken))
            .Where(t => CalibraTableCatalog.IsOwned(t.Name))
            .ToList();
        var fks = (await _repository.GetAllForeignKeysAsync(cancellationToken))
            .Where(fk => CalibraTableCatalog.IsOwned(fk.FromTable) && CalibraTableCatalog.IsOwned(fk.ToTable))
            .ToList();
        var fkByTable = fks
            .GroupBy(fk => fk.FromTable, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        sb.AppendLine("erDiagram");
        foreach (var t in tables)
        {
            var fullName = $"{t.Schema}.{t.Name}";
            var detail = await _repository.GetTableDetailAsync(t.Schema, t.Name, cancellationToken);
            if (detail is null) continue;

            sb.Append("    ").Append(SanitizeIdentifier(t.Name)).AppendLine(" {");
            foreach (var c in detail.Columns)
            {
                var keyMarker = c.IsPrimaryKey ? " PK" : (c.IsForeignKey ? " FK" : string.Empty);
                var nullable = c.IsNullable ? " \"NULL\"" : string.Empty;
                sb.Append("        ").Append(c.SqlType.ToLowerInvariant()).Append(' ').Append(c.Name).Append(keyMarker).AppendLine(nullable);
            }
            sb.AppendLine("    }");

            if (fkByTable.TryGetValue(fullName, out var outFks))
            {
                foreach (var fk in outFks)
                {
                    var toShortName = fk.ToTable.Split('.').Last();
                    sb.Append("    ")
                      .Append(SanitizeIdentifier(t.Name))
                      .Append(" }o--|| ")
                      .Append(SanitizeIdentifier(toShortName))
                      .Append(" : \"")
                      .Append(fk.FromColumn)
                      .AppendLine("\"");
                }
            }
        }
        return sb.ToString();
    }

    public async Task<string> BuildCsvAsync(CancellationToken cancellationToken)
    {
        var tables = await _repository.GetTablesAsync(cancellationToken);
        var sb = new StringBuilder();
        sb.AppendLine("Schema,Table,Column,OrdinalPosition,SqlType,Length,Nullable,Default,PK,Identity,FK,FKTarget,RowCount");

        foreach (var t in tables)
        {
            var detail = await _repository.GetTableDetailAsync(t.Schema, t.Name, cancellationToken);
            if (detail is null) continue;
            foreach (var c in detail.Columns)
            {
                sb.Append(CsvEscape(t.Schema)).Append(',');
                sb.Append(CsvEscape(t.Name)).Append(',');
                sb.Append(CsvEscape(c.Name)).Append(',');
                sb.Append(c.OrdinalPosition.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append(CsvEscape(c.SqlType)).Append(',');
                sb.Append(FormatLength(c.MaxLength, c.Precision, c.Scale)).Append(',');
                sb.Append(c.IsNullable ? "YES" : "NO").Append(',');
                sb.Append(CsvEscape(c.DefaultDefinition ?? string.Empty)).Append(',');
                sb.Append(c.IsPrimaryKey ? "YES" : string.Empty).Append(',');
                sb.Append(c.IsIdentity ? "YES" : string.Empty).Append(',');
                sb.Append(c.IsForeignKey ? "YES" : string.Empty).Append(',');
                sb.Append(CsvEscape(c.ForeignKeyTarget ?? string.Empty)).Append(',');
                sb.Append(t.RowCount.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine();
            }
        }
        return sb.ToString();
    }

    public async Task<string> BuildMarkdownAsync(CancellationToken cancellationToken)
    {
        var tables = await _repository.GetTablesAsync(cancellationToken);
        var sb = new StringBuilder();
        sb.AppendLine("# Veritabani Haritasi");
        sb.Append("Olusturma: ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
          .Append(" — Tablo sayisi: ").Append(tables.Count).AppendLine();
        sb.AppendLine();

        foreach (var t in tables)
        {
            var detail = await _repository.GetTableDetailAsync(t.Schema, t.Name, cancellationToken);
            if (detail is null) continue;

            sb.Append("## `").Append(t.Schema).Append('.').Append(t.Name).AppendLine("`");
            if (!string.IsNullOrWhiteSpace(detail.Description))
            {
                sb.AppendLine();
                sb.Append("> ").AppendLine(detail.Description);
            }
            sb.AppendLine();
            sb.Append("Satir sayisi: **").Append(t.RowCount.ToString("N0", CultureInfo.InvariantCulture)).AppendLine("**");
            sb.AppendLine();
            sb.AppendLine("| Kolon | Tip | Null | Default | PK | FK → |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (var c in detail.Columns)
            {
                sb.Append("| `").Append(c.Name).Append('`').Append(" | ")
                  .Append(FormatTypeWithLength(c)).Append(" | ")
                  .Append(c.IsNullable ? "✓" : "—").Append(" | ")
                  .Append(string.IsNullOrEmpty(c.DefaultDefinition) ? "—" : $"`{c.DefaultDefinition}`").Append(" | ")
                  .Append(c.IsPrimaryKey ? "✓" : "—").Append(" | ")
                  .Append(c.ForeignKeyTarget is null ? "—" : $"`{c.ForeignKeyTarget}`")
                  .AppendLine(" |");
            }
            sb.AppendLine();

            if (detail.Indexes.Count > 0)
            {
                sb.AppendLine("**Indeksler:**");
                foreach (var ix in detail.Indexes)
                {
                    var flags = ix.IsPrimaryKey ? "PK" : (ix.IsUnique ? "UNIQUE" : ix.Type);
                    sb.Append("- `").Append(ix.Name).Append("` (").Append(flags).Append(") — ")
                      .AppendLine(string.Join(", ", ix.Columns));
                }
                sb.AppendLine();
            }
        }
        return sb.ToString();
    }

    private static string CsvEscape(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string FormatLength(int? maxLength, int? precision, int? scale)
    {
        if (precision.HasValue && scale.HasValue) return $"{precision},{scale}";
        if (maxLength.HasValue) return maxLength.Value == -1 ? "MAX" : maxLength.Value.ToString(CultureInfo.InvariantCulture);
        return string.Empty;
    }

    private static string FormatTypeWithLength(DbColumnDto c)
    {
        var len = FormatLength(c.MaxLength, c.Precision, c.Scale);
        return string.IsNullOrEmpty(len) ? $"`{c.SqlType}`" : $"`{c.SqlType}({len})`";
    }

    private static string SanitizeIdentifier(string name)
    {
        // Mermaid entity isimleri harf/rakam/alt_cizgi; ayraclar sorun cikarir.
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
            sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
        return sb.ToString();
    }

    // ────────────────────────────────────────────────────────────
    // Reflection enrichment: [Description] ve Enum degerlerini
    // C# Domain entity'lerinden okuyarak UI sozlugune yansitir.
    // ────────────────────────────────────────────────────────────

    private static Type? FindEntityType(string tableName)
    {
        var index = EntityTypeIndex.Value;
        return index.TryGetValue(tableName, out var t) ? t : null;
    }

    private static IReadOnlyDictionary<string, Type> BuildEntityTypeIndex()
    {
        // Domain assembly'sinden CalibraHub.Domain.Entities namespace'indeki tum public class'lari index'le.
        // Tablo ismi cesitlemeleri: 'Contact', 'contact', 'Contacts', 'ContactAccounts' (legacy).
        var dict = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var domainAssembly = typeof(Domain.Entities.Contact).Assembly;
            foreach (var t in domainAssembly.GetTypes())
            {
                if (t.Namespace != "CalibraHub.Domain.Entities") continue;
                if (!t.IsClass || t.IsAbstract) continue;
                dict[t.Name] = t;
            }
        }
        catch
        {
            // Reflection yuklenirken sorun olursa bos index ile devam et.
        }
        return dict;
    }

    private static DbColumnDto EnrichColumn(DbColumnDto column, PropertyInfo[] properties)
    {
        var matched = MatchProperty(column.Name, properties);
        if (matched is null) return column;

        var description = matched.GetCustomAttribute<DescriptionAttribute>()?.Description;

        var propertyType = Nullable.GetUnderlyingType(matched.PropertyType) ?? matched.PropertyType;
        IReadOnlyList<DbEnumValueDto>? enumValues = null;
        if (propertyType.IsEnum)
        {
            enumValues = GetEnumValues(propertyType);
        }

        return column with
        {
            ClrPropertyName = matched.Name,
            Description = description,
            EnumValues = enumValues,
        };
    }

    private static PropertyInfo? MatchProperty(string columnName, PropertyInfo[] properties)
    {
        // 1) Direkt eslesme (case-insensitive): AccountType, Id gibi PascalCase kolonlari yakalar.
        var direct = properties.FirstOrDefault(p =>
            string.Equals(p.Name, columnName, StringComparison.OrdinalIgnoreCase));
        if (direct is not null) return direct;

        // 2) Snake_case -> PascalCase: document_type_id -> DocumentTypeId
        var pascal = SnakeToPascal(columnName);
        return properties.FirstOrDefault(p =>
            string.Equals(p.Name, pascal, StringComparison.OrdinalIgnoreCase));
    }

    private static string SnakeToPascal(string snake)
    {
        if (string.IsNullOrWhiteSpace(snake)) return snake;
        var parts = snake.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder(snake.Length);
        foreach (var part in parts)
        {
            if (part.Length == 0) continue;
            sb.Append(char.ToUpperInvariant(part[0]));
            if (part.Length > 1) sb.Append(part[1..]);
        }
        return sb.ToString();
    }

    private static IReadOnlyList<DbEnumValueDto> GetEnumValues(Type enumType)
    {
        var names = Enum.GetNames(enumType);
        var result = new List<DbEnumValueDto>(names.Length);
        foreach (var name in names)
        {
            var field = enumType.GetField(name, BindingFlags.Public | BindingFlags.Static);
            var description = field?.GetCustomAttribute<DescriptionAttribute>()?.Description;
            var rawValue = field?.GetRawConstantValue();
            var numericValue = rawValue is null ? 0L : Convert.ToInt64(rawValue, CultureInfo.InvariantCulture);
            result.Add(new DbEnumValueDto(name, numericValue, description));
        }
        return result;
    }
}
