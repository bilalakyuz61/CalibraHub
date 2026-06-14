using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// 2026-06-06 — PermissionGrant persistence. Tek tablo + iki sahip türü (UserId XOR DepartmentId).
/// </summary>
public sealed class SqlPermissionGrantRepository : IPermissionGrantRepository
{
    private readonly SqlServerConnectionFactory _factory;
    private readonly string _table;

    public SqlPermissionGrantRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options)
    {
        _factory = factory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        var s = schema.Replace("]", "]]");
        // 2026-06-06: DB tablo adı UserPermission, C# class adı PermissionGrant (enum çakışması).
        _table = $"[{s}].[UserPermission]";
    }

    private const string SelectColumns =
        "[Id],[UserId],[DepartmentId],[PermissionDefId],[IsGranted],[Created],[CreatedById]";

    public async Task<PermissionGrant?> GetByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = await _factory.OpenSystemConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM {_table} WHERE [Id]=@Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Map(r) : null;
    }

    public async Task<IReadOnlyList<PermissionGrant>> ListByUserAsync(int userId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenSystemConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM {_table} WHERE [UserId]=@U;";
        cmd.Parameters.AddWithValue("@U", userId);

        var list = new List<PermissionGrant>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(Map(r));
        return list;
    }

    public async Task<IReadOnlyList<PermissionGrant>> ListByDepartmentAsync(int departmentId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenSystemConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM {_table} WHERE [DepartmentId]=@D;";
        cmd.Parameters.AddWithValue("@D", departmentId);

        var list = new List<PermissionGrant>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(Map(r));
        return list;
    }

    public async Task<IReadOnlyList<PermissionGrant>> ListForUserAndDepartmentAsync(
        int userId, int? departmentId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenSystemConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        if (departmentId.HasValue)
        {
            cmd.CommandText = $"SELECT {SelectColumns} FROM {_table} WHERE [UserId]=@U OR [DepartmentId]=@D;";
            cmd.Parameters.AddWithValue("@U", userId);
            cmd.Parameters.AddWithValue("@D", departmentId.Value);
        }
        else
        {
            cmd.CommandText = $"SELECT {SelectColumns} FROM {_table} WHERE [UserId]=@U;";
            cmd.Parameters.AddWithValue("@U", userId);
        }

        var list = new List<PermissionGrant>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(Map(r));
        return list;
    }

    public async Task<int> SaveAsync(PermissionGrant entity, CancellationToken ct)
    {
        entity.EnsureValid();
        await using var conn = await _factory.OpenSystemConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        if (entity.Id > 0)
        {
            cmd.CommandText = $@"
                UPDATE {_table} SET
                    [UserId]=@U,[DepartmentId]=@D,[PermissionDefId]=@P,[IsGranted]=@G
                WHERE [Id]=@Id;
                SELECT @Id;";
            cmd.Parameters.AddWithValue("@Id", entity.Id);
        }
        else
        {
            cmd.CommandText = $@"
                INSERT INTO {_table}
                    ([UserId],[DepartmentId],[PermissionDefId],[IsGranted],[CreatedById])
                VALUES (@U,@D,@P,@G,@CreatedById);
                SELECT CAST(SCOPE_IDENTITY() AS INT);";
            cmd.Parameters.AddWithValue("@CreatedById", (object?)entity.CreatedById ?? DBNull.Value);
        }
        cmd.Parameters.AddWithValue("@U", (object?)entity.UserId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@D", (object?)entity.DepartmentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@P", entity.PermissionDefId);
        cmd.Parameters.AddWithValue("@G", entity.IsGranted);

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task BulkReplaceForOwnerAsync(
        int? userId, int? departmentId,
        IReadOnlyList<PermissionGrant> entities, CancellationToken ct)
    {
        if (!userId.HasValue && !departmentId.HasValue)
            throw new ArgumentException("UserId veya DepartmentId'den biri verilmelidir.");
        if (userId.HasValue && departmentId.HasValue)
            throw new ArgumentException("UserId ve DepartmentId aynı anda verilemez.");

        await using var conn = await _factory.OpenSystemConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            // 1) Mevcut sahibin tüm satırlarını sil
            await using (var delCmd = conn.CreateCommand())
            {
                delCmd.Transaction = tx;
                if (userId.HasValue)
                {
                    delCmd.CommandText = $"DELETE FROM {_table} WHERE [UserId]=@U;";
                    delCmd.Parameters.AddWithValue("@U", userId.Value);
                }
                else
                {
                    delCmd.CommandText = $"DELETE FROM {_table} WHERE [DepartmentId]=@D;";
                    delCmd.Parameters.AddWithValue("@D", departmentId!.Value);
                }
                await delCmd.ExecuteNonQueryAsync(ct);
            }

            // 2) Yeni satırları INSERT
            foreach (var e in entities)
            {
                // Sahibi zorla — entity'de yanlış doluysa override (BulkReplace anahtar bilgi)
                e.UserId = userId;
                e.DepartmentId = departmentId;
                e.EnsureValid();

                await using var insCmd = conn.CreateCommand();
                insCmd.Transaction = tx;
                insCmd.CommandText = $@"
                    INSERT INTO {_table}
                        ([UserId],[DepartmentId],[PermissionDefId],[IsGranted],[CreatedById])
                    VALUES (@U,@D,@P,@G,@CreatedById);";
                insCmd.Parameters.AddWithValue("@U", (object?)e.UserId ?? DBNull.Value);
                insCmd.Parameters.AddWithValue("@D", (object?)e.DepartmentId ?? DBNull.Value);
                insCmd.Parameters.AddWithValue("@P", e.PermissionDefId);
                insCmd.Parameters.AddWithValue("@G", e.IsGranted);
                insCmd.Parameters.AddWithValue("@CreatedById", (object?)e.CreatedById ?? DBNull.Value);
                await insCmd.ExecuteNonQueryAsync(ct);
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

    public async Task DeleteByUserAsync(int userId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenSystemConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_table} WHERE [UserId]=@U;";
        cmd.Parameters.AddWithValue("@U", userId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteByDepartmentAsync(int departmentId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenSystemConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_table} WHERE [DepartmentId]=@D;";
        cmd.Parameters.AddWithValue("@D", departmentId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static PermissionGrant Map(SqlDataReader r) => new()
    {
        Id              = r.GetInt32(0),
        UserId          = r.IsDBNull(1) ? null : r.GetInt32(1),
        DepartmentId    = r.IsDBNull(2) ? null : r.GetInt32(2),
        PermissionDefId = r.GetInt32(3),
        IsGranted       = r.GetBoolean(4),
        Created         = r.GetDateTime(5),
        CreatedById     = r.IsDBNull(6) ? null : r.GetInt32(6),
    };
}
