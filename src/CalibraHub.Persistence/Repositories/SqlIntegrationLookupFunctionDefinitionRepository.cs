using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
// Ambiguity fix: hem Domain hem Contracts namespace'inde IntegrationLookupFunctionColumn var.
// Entity (DB) tarafini varsayilan tut — DTO tarafi sadece Repository.ListAvailableFunctionsAsync icin.
using EntityColumn = CalibraHub.Domain.Entities.IntegrationLookupFunctionColumn;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlIntegrationLookupFunctionDefinitionRepository
    : IIntegrationLookupFunctionDefinitionRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _table;
    private readonly string _colTable;

    public SqlIntegrationLookupFunctionDefinitionRepository(
        SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _table    = $"[{schema}].[IntegrationLookupFunction]";
        _colTable = $"[{schema}].[IntegrationLookupFunctionColumn]";
    }

    public async Task<IReadOnlyList<IntegrationLookupFunctionDefinition>> GetAllAsync(
        bool includeInactive, CancellationToken ct)
    {
        var list = new List<IntegrationLookupFunctionDefinition>();
        var byId = new Dictionary<int, IntegrationLookupFunctionDefinition>();

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = includeInactive
                ? $"SELECT [Id],[Code],[Label],[Description],[ViewName],[KeyColumn],[SortOrder],[IsActive],[SqlSnippet],[SqlFunctionName] FROM {_table} ORDER BY [SortOrder],[Label];"
                : $"SELECT [Id],[Code],[Label],[Description],[ViewName],[KeyColumn],[SortOrder],[IsActive],[SqlSnippet],[SqlFunctionName] FROM {_table} WHERE [IsActive] = 1 ORDER BY [SortOrder],[Label];";
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                var def = MapHeader(rd);
                list.Add(def);
                byId[def.Id] = def;
            }
        }

        if (byId.Count == 0) return list;

        await using (var ccmd = conn.CreateCommand())
        {
            ccmd.CommandText = $@"
                SELECT [Id],[FunctionId],[Column],[Label],[SortOrder] FROM {_colTable}
                WHERE [FunctionId] IN (SELECT value FROM STRING_SPLIT(@Ids, ','))
                ORDER BY [FunctionId],[SortOrder];";
            ccmd.Parameters.Add(new SqlParameter("@Ids", string.Join(',', byId.Keys)));
            await using var crd = await ccmd.ExecuteReaderAsync(ct);
            while (await crd.ReadAsync(ct))
            {
                var fid = crd.GetInt32(1);
                if (!byId.TryGetValue(fid, out var def)) continue;
                def.Columns.Add(new EntityColumn
                {
                    Id = crd.GetInt32(0),
                    FunctionId = fid,
                    Column = crd.GetString(2),
                    Label  = crd.GetString(3),
                    SortOrder = crd.GetInt32(4),
                });
            }
        }
        return list;
    }

    public async Task<IntegrationLookupFunctionDefinition?> GetByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        IntegrationLookupFunctionDefinition? def = null;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT [Id],[Code],[Label],[Description],[ViewName],[KeyColumn],[SortOrder],[IsActive],[SqlSnippet],[SqlFunctionName] FROM {_table} WHERE [Id]=@Id;";
            cmd.Parameters.Add(new SqlParameter("@Id", id));
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            if (await rd.ReadAsync(ct)) def = MapHeader(rd);
        }
        if (def is null) return null;

        await using var ccmd = conn.CreateCommand();
        ccmd.CommandText = $"SELECT [Id],[FunctionId],[Column],[Label],[SortOrder] FROM {_colTable} WHERE [FunctionId]=@Id ORDER BY [SortOrder];";
        ccmd.Parameters.Add(new SqlParameter("@Id", id));
        await using var crd = await ccmd.ExecuteReaderAsync(ct);
        while (await crd.ReadAsync(ct))
        {
            def.Columns.Add(new EntityColumn
            {
                Id = crd.GetInt32(0),
                FunctionId = crd.GetInt32(1),
                Column = crd.GetString(2),
                Label  = crd.GetString(3),
                SortOrder = crd.GetInt32(4),
            });
        }
        return def;
    }

    public async Task<bool> CodeExistsAsync(string code, int? excludeId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = excludeId.HasValue
            ? $"SELECT COUNT(1) FROM {_table} WHERE [Code]=@Code AND [Id]<>@Id AND [IsActive]=1;"
            : $"SELECT COUNT(1) FROM {_table} WHERE [Code]=@Code AND [IsActive]=1;";
        cmd.Parameters.Add(new SqlParameter("@Code", code));
        if (excludeId.HasValue) cmd.Parameters.Add(new SqlParameter("@Id", excludeId.Value));
        var result = (int)(await cmd.ExecuteScalarAsync(ct) ?? 0);
        return result > 0;
    }

    public async Task<int> InsertAsync(IntegrationLookupFunctionDefinition entity, string? userName, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        int newId;

        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = $@"
                INSERT INTO {_table} ([Code],[Label],[Description],[ViewName],[KeyColumn],[SqlSnippet],[SqlFunctionName],[SortOrder],[IsActive],[CreatedBy],[Created])
                OUTPUT INSERTED.[Id]
                VALUES (@Code,@Label,@Description,@ViewName,@KeyColumn,@SqlSnippet,@SqlFunctionName,@SortOrder,@IsActive,@User,SYSUTCDATETIME());";
            cmd.Parameters.Add(new SqlParameter("@Code", entity.Code));
            cmd.Parameters.Add(new SqlParameter("@Label", entity.Label));
            cmd.Parameters.Add(new SqlParameter("@Description", (object?)entity.Description ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@ViewName", (object?)entity.ViewName ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@KeyColumn", (object?)entity.KeyColumn ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@SqlSnippet", (object?)entity.SqlSnippet ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@SqlFunctionName", (object?)entity.SqlFunctionName ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@SortOrder", entity.SortOrder));
            cmd.Parameters.Add(new SqlParameter("@IsActive", entity.IsActive));
            cmd.Parameters.Add(new SqlParameter("@User", (object?)userName ?? DBNull.Value));
            newId = (int)(await cmd.ExecuteScalarAsync(ct))!;
        }

        await InsertColumnsAsync(conn, tx, newId, entity.Columns, ct);
        await tx.CommitAsync(ct);
        return newId;
    }

    public async Task UpdateAsync(IntegrationLookupFunctionDefinition entity, string? userName, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = $@"
                UPDATE {_table}
                SET [Code]=@Code, [Label]=@Label, [Description]=@Description,
                    [ViewName]=@ViewName, [KeyColumn]=@KeyColumn, [SqlSnippet]=@SqlSnippet,
                    [SqlFunctionName]=@SqlFunctionName,
                    [SortOrder]=@SortOrder, [IsActive]=@IsActive,
                    [UpdatedBy]=@User, [Updated]=SYSUTCDATETIME()
                WHERE [Id]=@Id;";
            cmd.Parameters.Add(new SqlParameter("@Id", entity.Id));
            cmd.Parameters.Add(new SqlParameter("@Code", entity.Code));
            cmd.Parameters.Add(new SqlParameter("@Label", entity.Label));
            cmd.Parameters.Add(new SqlParameter("@Description", (object?)entity.Description ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@ViewName", (object?)entity.ViewName ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@KeyColumn", (object?)entity.KeyColumn ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@SqlSnippet", (object?)entity.SqlSnippet ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@SqlFunctionName", (object?)entity.SqlFunctionName ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@SortOrder", entity.SortOrder));
            cmd.Parameters.Add(new SqlParameter("@IsActive", entity.IsActive));
            cmd.Parameters.Add(new SqlParameter("@User", (object?)userName ?? DBNull.Value));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Kolonlar: tum eski kolonlari sil + yeniden insert (basit ve tutarli).
        await using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = $"DELETE FROM {_colTable} WHERE [FunctionId]=@Id;";
            del.Parameters.Add(new SqlParameter("@Id", entity.Id));
            await del.ExecuteNonQueryAsync(ct);
        }
        await InsertColumnsAsync(conn, tx, entity.Id, entity.Columns, ct);

        await tx.CommitAsync(ct);
    }

    public async Task SoftDeleteAsync(int id, string? userName, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE {_table} SET [IsActive]=0, [UpdatedBy]=@User, [Updated]=SYSUTCDATETIME() WHERE [Id]=@Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        cmd.Parameters.Add(new SqlParameter("@User", (object?)userName ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteHardAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_table} WHERE [Id]=@Id;";  // CASCADE kolonlari da siler
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task InsertColumnsAsync(
        SqlConnection conn, SqlTransaction tx, int functionId,
        IEnumerable<EntityColumn> cols, CancellationToken ct)
    {
        var idx = 0;
        foreach (var c in cols)
        {
            idx++;
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $@"
                INSERT INTO {_colTable} ([FunctionId],[Column],[Label],[SortOrder])
                VALUES (@F,@C,@L,@S);";
            cmd.Parameters.Add(new SqlParameter("@F", functionId));
            cmd.Parameters.Add(new SqlParameter("@C", c.Column));
            cmd.Parameters.Add(new SqlParameter("@L", c.Label));
            cmd.Parameters.Add(new SqlParameter("@S", c.SortOrder > 0 ? c.SortOrder : idx * 10));
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task<IReadOnlyList<AvailableDbFunctionDto>> ListAvailableFunctionsAsync(CancellationToken ct)
    {
        var list = new List<AvailableDbFunctionDto>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                SCHEMA_NAME(o.schema_id) + N'.' + o.name AS FullName,
                o.type AS Type,
                (SELECT COUNT(*) FROM sys.parameters p
                  WHERE p.object_id = o.object_id AND p.is_output = 0) AS ParamCount
            FROM sys.objects o
            WHERE o.type IN ('FN', 'IF', 'TF')
              AND o.is_ms_shipped = 0
            ORDER BY o.name;
            """;
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            list.Add(new AvailableDbFunctionDto(
                FullName: rd.GetString(0),
                Type: rd.GetString(1).Trim(),
                ParameterCount: rd.GetInt32(2)));
        }
        return list;
    }

    private static IntegrationLookupFunctionDefinition MapHeader(SqlDataReader rd) => new()
    {
        Id          = rd.GetInt32(0),
        Code        = rd.GetString(1),
        Label       = rd.GetString(2),
        Description = rd.IsDBNull(3) ? null : rd.GetString(3),
        ViewName    = rd.IsDBNull(4) ? null : rd.GetString(4),
        KeyColumn   = rd.IsDBNull(5) ? null : rd.GetString(5),
        SortOrder   = rd.GetInt32(6),
        IsActive    = rd.GetBoolean(7),
        SqlSnippet  = rd.IsDBNull(8) ? null : rd.GetString(8),
        SqlFunctionName = rd.IsDBNull(9) ? null : rd.GetString(9),
    };
}
