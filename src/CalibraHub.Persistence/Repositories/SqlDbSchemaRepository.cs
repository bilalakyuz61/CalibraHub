using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Persistence.Database;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// Aktif sirket DB'sinden sys.* introspection ile fiziksel sema metadata'si okur.
/// Sadece metadata: tablolar, kolonlar, indeksler, FK'ler. Ornek veri/PII OKUNMAZ.
/// </summary>
public sealed class SqlDbSchemaRepository : IDbSchemaRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;

    public SqlDbSchemaRepository(SqlServerConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<DbTableSummaryDto>> GetTablesAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT
                s.name AS schema_name,
                t.name AS table_name,
                (SELECT COUNT(*) FROM sys.columns c WHERE c.object_id = t.object_id) AS column_count,
                (SELECT COUNT(*) FROM sys.foreign_keys fk WHERE fk.parent_object_id = t.object_id) AS fk_count,
                ISNULL((
                    SELECT SUM(p.rows)
                      FROM sys.partitions p
                     WHERE p.object_id = t.object_id AND p.index_id IN (0,1)
                ), 0) AS row_count
            FROM sys.tables t
            INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE t.is_ms_shipped = 0
            ORDER BY s.name, t.name;
            """;

        var list = new List<DbTableSummaryDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new DbTableSummaryDto(
                Schema: reader.GetString(0),
                Name: reader.GetString(1),
                RowCount: reader.GetInt64(4),
                ColumnCount: reader.GetInt32(2),
                ForeignKeyCount: reader.GetInt32(3)));
        }
        return list;
    }

    public async Task<DbTableDetailDto?> GetTableDetailAsync(string schema, string name, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);

        // 1) Object id + row count
        var fullName = $"[{schema.Replace("]", "]]")}].[{name.Replace("]", "]]")}]";
        var rowCount = await GetRowCountAsync(connection, fullName, cancellationToken);
        if (rowCount is null) return null;

        var columns = await GetColumnsAsync(connection, fullName, cancellationToken);
        var indexes = await GetIndexesAsync(connection, fullName, cancellationToken);
        var outgoing = await GetForeignKeysAsync(connection, fullName, outgoing: true, cancellationToken);
        var incoming = await GetForeignKeysAsync(connection, fullName, outgoing: false, cancellationToken);

        return new DbTableDetailDto(
            Schema: schema,
            Name: name,
            RowCount: rowCount.Value,
            Columns: columns,
            Indexes: indexes,
            OutgoingForeignKeys: outgoing,
            IncomingForeignKeys: incoming);
    }

    public async Task<IReadOnlyList<DbForeignKeyDto>> GetAllForeignKeysAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT
                fk.name,
                OBJECT_SCHEMA_NAME(fk.parent_object_id)     + '.' + OBJECT_NAME(fk.parent_object_id)     AS from_table,
                pc.name                                                                                 AS from_col,
                OBJECT_SCHEMA_NAME(fk.referenced_object_id) + '.' + OBJECT_NAME(fk.referenced_object_id) AS to_table,
                rc.name                                                                                 AS to_col,
                fk.delete_referential_action_desc,
                fk.update_referential_action_desc
            FROM sys.foreign_keys fk
            INNER JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
            INNER JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id     AND pc.column_id = fkc.parent_column_id
            INNER JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
            ORDER BY from_table, fk.name;
            """;

        var list = new List<DbForeignKeyDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new DbForeignKeyDto(
                ConstraintName: reader.GetString(0),
                FromTable: reader.GetString(1),
                FromColumn: reader.GetString(2),
                ToTable: reader.GetString(3),
                ToColumn: reader.GetString(4),
                DeleteAction: reader.GetString(5),
                UpdateAction: reader.GetString(6)));
        }
        return list;
    }

    public async Task<IReadOnlyList<string>> GetViewNamesAsync(CancellationToken cancellationToken)
    {
        var list = new List<string>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT s.name + '.' + v.name AS full_name
              FROM sys.views v
              INNER JOIN sys.schemas s ON s.schema_id = v.schema_id
             WHERE v.is_ms_shipped = 0
             ORDER BY s.name, v.name;
            """;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) list.Add(reader.GetString(0));
        return list;
    }

    private static async Task<long?> GetRowCountAsync(SqlConnection connection, string fullName, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT ISNULL(SUM(p.rows), -1)
              FROM sys.partitions p
             WHERE p.object_id = OBJECT_ID(@full) AND p.index_id IN (0, 1);
            """;
        cmd.Parameters.Add(new SqlParameter("@full", fullName));
        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is null or DBNull) return null;
        var v = Convert.ToInt64(result);
        return v < 0 ? null : v;
    }

    private static async Task<IReadOnlyList<DbColumnDto>> GetColumnsAsync(SqlConnection connection, string fullName, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT
                c.column_id,
                c.name,
                TYPE_NAME(c.user_type_id) AS sql_type,
                c.max_length,
                c.precision,
                c.scale,
                c.is_nullable,
                c.is_identity,
                dc.definition AS default_def,
                CASE WHEN EXISTS (
                    SELECT 1
                      FROM sys.index_columns ic
                      INNER JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                     WHERE i.is_primary_key = 1
                       AND ic.object_id = c.object_id
                       AND ic.column_id = c.column_id
                ) THEN 1 ELSE 0 END AS is_pk,
                fk_target.target AS fk_target
            FROM sys.columns c
            LEFT JOIN sys.default_constraints dc
              ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
            OUTER APPLY (
                SELECT TOP 1
                    OBJECT_SCHEMA_NAME(fkc.referenced_object_id) + '.' +
                    OBJECT_NAME(fkc.referenced_object_id)        + '.' + rc.name AS target
                FROM sys.foreign_key_columns fkc
                INNER JOIN sys.columns rc
                  ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
                WHERE fkc.parent_object_id = c.object_id AND fkc.parent_column_id = c.column_id
            ) AS fk_target
            WHERE c.object_id = OBJECT_ID(@full)
            ORDER BY c.column_id;
            """;
        cmd.Parameters.Add(new SqlParameter("@full", fullName));

        var list = new List<DbColumnDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var sqlType = reader.GetString(2);
            var rawMaxLen = reader.GetInt16(3);
            int? maxLen = IsStringType(sqlType)
                ? (rawMaxLen == -1 ? -1 : (sqlType.StartsWith("n", StringComparison.OrdinalIgnoreCase) ? rawMaxLen / 2 : rawMaxLen))
                : null;
            int? precision = IsNumericType(sqlType) ? reader.GetByte(4) : null;
            int? scale = IsNumericType(sqlType) ? reader.GetByte(5) : null;
            var fkTarget = reader.IsDBNull(10) ? null : reader.GetString(10);

            list.Add(new DbColumnDto(
                OrdinalPosition: reader.GetInt32(0),
                Name: reader.GetString(1),
                SqlType: sqlType,
                MaxLength: maxLen,
                Precision: precision,
                Scale: scale,
                IsNullable: reader.GetBoolean(6),
                IsIdentity: reader.GetBoolean(7),
                DefaultDefinition: reader.IsDBNull(8) ? null : reader.GetString(8),
                IsPrimaryKey: reader.GetInt32(9) == 1,
                IsForeignKey: fkTarget is not null,
                ForeignKeyTarget: fkTarget));
        }
        return list;
    }

    private static async Task<IReadOnlyList<DbIndexDto>> GetIndexesAsync(SqlConnection connection, string fullName, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT
                i.name,
                i.type_desc,
                i.is_unique,
                i.is_primary_key,
                STUFF((
                    SELECT ', ' + c2.name
                      FROM sys.index_columns ic2
                      INNER JOIN sys.columns c2 ON c2.object_id = ic2.object_id AND c2.column_id = ic2.column_id
                     WHERE ic2.object_id = i.object_id AND ic2.index_id = i.index_id
                     ORDER BY ic2.key_ordinal
                    FOR XML PATH(''), TYPE
                ).value('.', 'NVARCHAR(MAX)'), 1, 2, '') AS columns
            FROM sys.indexes i
            WHERE i.object_id = OBJECT_ID(@full)
              AND i.type > 0
              AND i.is_hypothetical = 0
              AND i.name IS NOT NULL
            ORDER BY i.is_primary_key DESC, i.index_id;
            """;
        cmd.Parameters.Add(new SqlParameter("@full", fullName));

        var list = new List<DbIndexDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var cols = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
            list.Add(new DbIndexDto(
                Name: reader.GetString(0),
                Type: reader.GetString(1),
                IsUnique: reader.GetBoolean(2),
                IsPrimaryKey: reader.GetBoolean(3),
                Columns: cols.Length == 0
                    ? Array.Empty<string>()
                    : cols.Split(", ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)));
        }
        return list;
    }

    private static async Task<IReadOnlyList<DbForeignKeyDto>> GetForeignKeysAsync(
        SqlConnection connection, string fullName, bool outgoing, CancellationToken ct)
    {
        var filterColumn = outgoing ? "fk.parent_object_id" : "fk.referenced_object_id";
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT
                fk.name,
                OBJECT_SCHEMA_NAME(fk.parent_object_id)     + '.' + OBJECT_NAME(fk.parent_object_id)     AS from_table,
                pc.name                                                                                 AS from_col,
                OBJECT_SCHEMA_NAME(fk.referenced_object_id) + '.' + OBJECT_NAME(fk.referenced_object_id) AS to_table,
                rc.name                                                                                 AS to_col,
                fk.delete_referential_action_desc,
                fk.update_referential_action_desc
            FROM sys.foreign_keys fk
            INNER JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
            INNER JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id     AND pc.column_id = fkc.parent_column_id
            INNER JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
            WHERE {filterColumn} = OBJECT_ID(@full)
            ORDER BY fk.name, fkc.constraint_column_id;
            """;
        cmd.Parameters.Add(new SqlParameter("@full", fullName));

        var list = new List<DbForeignKeyDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new DbForeignKeyDto(
                ConstraintName: reader.GetString(0),
                FromTable: reader.GetString(1),
                FromColumn: reader.GetString(2),
                ToTable: reader.GetString(3),
                ToColumn: reader.GetString(4),
                DeleteAction: reader.GetString(5),
                UpdateAction: reader.GetString(6)));
        }
        return list;
    }

    public async Task<IReadOnlyList<RdViewInfo>> GetDesignerViewsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT
                v.name           AS ViewName,
                c.name           AS ColumnName,
                TYPE_NAME(c.user_type_id) AS SqlType
            FROM sys.views v
            INNER JOIN sys.schemas s ON s.schema_id = v.schema_id
            INNER JOIN sys.columns c ON c.object_id = v.object_id
            WHERE v.is_ms_shipped = 0 AND s.name = 'dbo'
            ORDER BY v.name, c.column_id;
            """;

        var byView = new Dictionary<string, List<RdColumnInfo>>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var viewName   = reader.GetString(0);
            var colName    = reader.GetString(1);
            var sqlType    = reader.GetString(2);
            var isNumeric  = IsRdNumericType(sqlType);
            var isTime     = IsRdTimeType(sqlType);
            if (!byView.TryGetValue(viewName, out var cols))
            {
                cols = [];
                byView[viewName] = cols;
            }
            cols.Add(new RdColumnInfo(colName, sqlType, isNumeric, isTime));
        }

        return byView
            .Select(kv => new RdViewInfo(kv.Key, kv.Value))
            .OrderBy(v => v.Name)
            .ToList();
    }

    private static bool IsStringType(string sqlType) =>
        sqlType.Equals("char", StringComparison.OrdinalIgnoreCase) ||
        sqlType.Equals("varchar", StringComparison.OrdinalIgnoreCase) ||
        sqlType.Equals("nchar", StringComparison.OrdinalIgnoreCase) ||
        sqlType.Equals("nvarchar", StringComparison.OrdinalIgnoreCase) ||
        sqlType.Equals("binary", StringComparison.OrdinalIgnoreCase) ||
        sqlType.Equals("varbinary", StringComparison.OrdinalIgnoreCase);

    private static bool IsNumericType(string sqlType) =>
        sqlType.Equals("decimal", StringComparison.OrdinalIgnoreCase) ||
        sqlType.Equals("numeric", StringComparison.OrdinalIgnoreCase);

    private static bool IsRdNumericType(string t) =>
        t is "int" or "bigint" or "smallint" or "tinyint"
          or "decimal" or "numeric" or "float" or "real"
          or "money" or "smallmoney";

    private static bool IsRdTimeType(string t) =>
        t is "date" or "datetime" or "datetime2" or "datetimeoffset" or "smalldatetime";
}
