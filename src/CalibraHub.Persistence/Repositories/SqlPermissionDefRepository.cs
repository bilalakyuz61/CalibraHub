using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// 2026-06-06 — PermissionDef persistence. Yetkilendirme katalog tablosu.
/// </summary>
public sealed class SqlPermissionDefRepository : IPermissionDefRepository
{
    private readonly SqlServerConnectionFactory _factory;
    private readonly string _table;

    public SqlPermissionDefRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options)
    {
        _factory = factory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        var s = schema.Replace("]", "]]");
        _table = $"[{s}].[PermissionDef]";
    }

    private const string SelectColumns =
        "[Id],[FormCode],[ActionCode],[Label],[Category],[SortOrder],[IsActive],[Created],[Updated],[CreatedById],[UpdatedById]";

    public async Task<IReadOnlyList<PermissionDef>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        await using var conn = await _factory.OpenSystemConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = includeInactive
            ? $"SELECT {SelectColumns} FROM {_table} ORDER BY [FormCode],[SortOrder],[ActionCode];"
            : $"SELECT {SelectColumns} FROM {_table} WHERE [IsActive]=1 ORDER BY [FormCode],[SortOrder],[ActionCode];";

        var list = new List<PermissionDef>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(Map(r));
        return list;
    }

    public async Task<PermissionDef?> GetByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = await _factory.OpenSystemConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM {_table} WHERE [Id]=@Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Map(r) : null;
    }

    public async Task<PermissionDef?> GetByFormAndActionAsync(string formCode, string actionCode, CancellationToken ct)
    {
        await using var conn = await _factory.OpenSystemConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM {_table} WHERE [FormCode]=@F AND [ActionCode]=@A;";
        cmd.Parameters.AddWithValue("@F", formCode);
        cmd.Parameters.AddWithValue("@A", actionCode);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Map(r) : null;
    }

    public async Task<IReadOnlyList<PermissionDef>> ListByFormAsync(string formCode, CancellationToken ct)
    {
        await using var conn = await _factory.OpenSystemConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM {_table} WHERE [FormCode]=@F AND [IsActive]=1 ORDER BY [SortOrder],[ActionCode];";
        cmd.Parameters.AddWithValue("@F", formCode);

        var list = new List<PermissionDef>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(Map(r));
        return list;
    }

    public async Task<int> SaveAsync(PermissionDef entity, CancellationToken ct)
    {
        entity.EnsureValid();
        await using var conn = await _factory.OpenSystemConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        if (entity.Id > 0)
        {
            cmd.CommandText = $@"
                UPDATE {_table} SET
                    [FormCode]=@F,[ActionCode]=@A,[Label]=@L,[Category]=@C,
                    [SortOrder]=@S,[IsActive]=@IsActive,
                    [Updated]=SYSUTCDATETIME(),[UpdatedById]=@UpdatedById
                WHERE [Id]=@Id;
                SELECT @Id;";
            cmd.Parameters.AddWithValue("@Id", entity.Id);
        }
        else
        {
            cmd.CommandText = $@"
                INSERT INTO {_table}
                    ([FormCode],[ActionCode],[Label],[Category],[SortOrder],[IsActive],[CreatedById])
                VALUES (@F,@A,@L,@C,@S,@IsActive,@CreatedById);
                SELECT CAST(SCOPE_IDENTITY() AS INT);";
            cmd.Parameters.AddWithValue("@CreatedById", (object?)entity.CreatedById ?? DBNull.Value);
        }
        cmd.Parameters.AddWithValue("@F", entity.FormCode);
        cmd.Parameters.AddWithValue("@A", entity.ActionCode);
        cmd.Parameters.AddWithValue("@L", entity.Label);
        cmd.Parameters.AddWithValue("@C", (object?)entity.Category ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@S", entity.SortOrder);
        cmd.Parameters.AddWithValue("@IsActive", entity.IsActive);
        cmd.Parameters.AddWithValue("@UpdatedById", (object?)entity.UpdatedById ?? DBNull.Value);

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task BulkUpsertAsync(IReadOnlyList<PermissionDef> entities, CancellationToken ct)
    {
        if (entities.Count == 0) return;

        await using var conn = await _factory.OpenSystemConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            foreach (var e in entities)
            {
                e.EnsureValid();
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                // MERGE pattern — (FormCode, ActionCode) unique olduğu için UPSERT
                cmd.CommandText = $@"
                    MERGE {_table} AS target
                    USING (SELECT @F AS FormCode, @A AS ActionCode) AS src
                    ON target.[FormCode]=src.FormCode AND target.[ActionCode]=src.ActionCode
                    WHEN MATCHED THEN UPDATE SET
                        [Label]=@L,[Category]=@C,[SortOrder]=@S,[IsActive]=@IsActive,
                        [Updated]=SYSUTCDATETIME(),[UpdatedById]=@UpdatedById
                    WHEN NOT MATCHED THEN INSERT
                        ([FormCode],[ActionCode],[Label],[Category],[SortOrder],[IsActive],[CreatedById])
                        VALUES (@F,@A,@L,@C,@S,@IsActive,@CreatedById);";
                cmd.Parameters.AddWithValue("@F", e.FormCode);
                cmd.Parameters.AddWithValue("@A", e.ActionCode);
                cmd.Parameters.AddWithValue("@L", e.Label);
                cmd.Parameters.AddWithValue("@C", (object?)e.Category ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@S", e.SortOrder);
                cmd.Parameters.AddWithValue("@IsActive", e.IsActive);
                cmd.Parameters.AddWithValue("@CreatedById", (object?)e.CreatedById ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@UpdatedById", (object?)e.UpdatedById ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        catch
        {
            try { await tx.RollbackAsync(ct); } catch { /* ignore */ }
            throw;
        }
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        await using var conn = await _factory.OpenSystemConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_table} WHERE [Id]=@Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static PermissionDef Map(SqlDataReader r) => new()
    {
        Id          = r.GetInt32(0),
        FormCode    = r.GetString(1),
        ActionCode  = r.GetString(2),
        Label       = r.GetString(3),
        Category    = r.IsDBNull(4) ? null : r.GetString(4),
        SortOrder   = r.GetInt32(5),
        IsActive    = r.GetBoolean(6),
        Created     = r.GetDateTime(7),
        Updated     = r.IsDBNull(8) ? null : r.GetDateTime(8),
        CreatedById = r.IsDBNull(9) ? null : r.GetInt32(9),
        UpdatedById = r.IsDBNull(10) ? null : r.GetInt32(10),
    };
}
