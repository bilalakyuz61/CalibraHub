using System.Globalization;
using System.Text;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Services;

public sealed class DbSchemaService : IDbSchemaService
{
    private readonly IDbSchemaRepository _repository;

    public DbSchemaService(IDbSchemaRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<DbTableSummaryDto>> GetTablesAsync(CancellationToken cancellationToken)
        => _repository.GetTablesAsync(cancellationToken);

    public Task<DbTableDetailDto?> GetTableDetailAsync(string schema, string name, CancellationToken cancellationToken)
        => _repository.GetTableDetailAsync(schema, name, cancellationToken);

    public async Task<string> BuildMermaidErAsync(CancellationToken cancellationToken)
    {
        var tables = await _repository.GetTablesAsync(cancellationToken);
        var fks = await _repository.GetAllForeignKeysAsync(cancellationToken);
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
}
