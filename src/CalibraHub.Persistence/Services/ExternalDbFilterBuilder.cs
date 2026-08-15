using System.Globalization;
using System.Text.Json;
using CalibraHub.Application.Contracts;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Services;

/// <summary>
/// 2026-08-15 — Harici kaynak sorgusuna kısıt (WHERE) derleyicisi.
///
/// JSON şekli ve operatör sözlüğü dışa aktarımdaki "Kısıt Kuralları" ile AYNIDIR
/// (<c>IntegrationFilterEngine</c>) — kullanıcı tek dil öğrenir. O motor CalibraHub'ın
/// form/widget dünyasına bağlı olduğu için burada ayrı, kaynak-bağımsız bir derleyici var.
///
/// Güvenlik:
///   • Kolon adı serbest metin DEĞİL — kaynağın sys.columns'ından gelen listeye doğrulanır.
///     Bilinmeyen kolon SESSİZCE ATLANMAZ, hata olarak döner (yoksa kısıt kaybolur ve
///     beklenenden fazla satır aktarılır).
///   • Değerler daima SqlParameter olarak bağlanır.
/// </summary>
internal static class ExternalDbFilterBuilder
{
    public sealed record Result(string WhereClause, IReadOnlyList<SqlParameter> Parameters, string? Error)
    {
        public static readonly Result Empty = new(string.Empty, Array.Empty<SqlParameter>(), null);
        public bool HasWhere => WhereClause.Length > 0;
    }

    public static Result Build(string? filterJson, IReadOnlyCollection<ExternalDbColumnDto> availableColumns)
    {
        if (string.IsNullOrWhiteSpace(filterJson)) return Result.Empty;

        List<(string Field, string Op, string? Value)> rules;
        try
        {
            rules = Parse(filterJson);
        }
        catch (JsonException jx)
        {
            return new Result(string.Empty, Array.Empty<SqlParameter>(), $"Kısıt kuralları okunamadı: {jx.Message}");
        }
        if (rules.Count == 0) return Result.Empty;

        var byName = availableColumns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var parts = new List<string>(rules.Count);
        var parameters = new List<SqlParameter>();

        foreach (var (field, op, value) in rules)
        {
            if (!byName.TryGetValue(field, out var col))
                return new Result(string.Empty, Array.Empty<SqlParameter>(),
                    $"Kısıt kuralındaki kolon kaynakta yok: '{field}'");

            var colExpr = $"[{col.Name.Replace("]", "]]")}]";
            var fragment = BuildFragment(colExpr, op, value, parameters);
            if (fragment is null)
                return new Result(string.Empty, Array.Empty<SqlParameter>(),
                    $"Bilinmeyen kısıt operatörü: '{op}' ({field})");

            parts.Add(fragment);
        }

        return new Result(string.Join(" AND ", parts), parameters, null);
    }

    private static List<(string, string, string?)> Parse(string json)
    {
        var result = new List<(string, string, string?)>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;

        foreach (var el in doc.RootElement.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;
            var field = ReadString(el, "field");
            var op = ReadString(el, "op");
            if (string.IsNullOrWhiteSpace(field) || string.IsNullOrWhiteSpace(op)) continue;
            result.Add((field.Trim(), op.Trim().ToLowerInvariant(), ReadString(el, "value")));
        }
        return result;
    }

    /// <summary>Bilinmeyen operatörde null döner — çağıran hata olarak raporlar.</summary>
    private static string? BuildFragment(string colExpr, string op, string? value, List<SqlParameter> parameters)
    {
        string Param(object? v)
        {
            var name = $"@__flt{parameters.Count}";
            parameters.Add(new SqlParameter(name, v ?? DBNull.Value));
            return name;
        }

        switch (op)
        {
            case "isnull":  return $"{colExpr} IS NULL";
            case "notnull": return $"{colExpr} IS NOT NULL";

            case "eq":  return $"{colExpr} = {Param(value)}";
            case "neq": return $"{colExpr} <> {Param(value)}";
            case "gt":  return $"{colExpr} > {Param(value)}";
            case "gte": return $"{colExpr} >= {Param(value)}";
            case "lt":  return $"{colExpr} < {Param(value)}";
            case "lte": return $"{colExpr} <= {Param(value)}";

            case "contains":   return $"{colExpr} LIKE {Param("%" + Escape(value) + "%")} ESCAPE '\\'";
            case "startswith": return $"{colExpr} LIKE {Param(Escape(value) + "%")} ESCAPE '\\'";

            case "in":
            {
                var items = SplitList(value);
                if (items.Length == 0) return "1 = 0";   // boş liste → hiçbir satır
                return $"{colExpr} IN ({string.Join(", ", items.Select(i => Param(i)))})";
            }

            case "between":
            {
                var items = SplitList(value);
                if (items.Length != 2) return "1 = 0";
                return $"{colExpr} BETWEEN {Param(items[0])} AND {Param(items[1])}";
            }

            default: return null;
        }
    }

    /// <summary>LIKE joker karakterlerini kaçır — kullanıcı '%' yazarsa literal aransın.</summary>
    private static string Escape(string? value)
        => (value ?? string.Empty).Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");

    private static string[] SplitList(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                   .Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();

    private static string? ReadString(JsonElement el, string property)
    {
        if (!el.TryGetProperty(property, out var p)) return null;
        return p.ValueKind switch
        {
            JsonValueKind.String => p.GetString(),
            JsonValueKind.Number => p.ToString(),
            JsonValueKind.True   => "1",
            JsonValueKind.False  => "0",
            JsonValueKind.Null   => null,
            _                    => p.ToString(),
        };
    }
}
