using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// 2026-06-20 — ImportTemplate persistence. Per-company şirket DB'sinde izole
/// (SqlServerConnectionFactory). Pattern: SqlAiProviderRepository.
/// </summary>
public sealed class SqlImportTemplateRepository : IImportTemplateRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _table;

    public SqlImportTemplateRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options)
    {
        _connectionFactory = factory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        var s = schema.Replace("]", "]]");
        _table = $"[{s}].[ImportTemplate]";
    }

    public async Task<IReadOnlyList<ImportTemplate>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[Name],[TargetEntity],[SheetName],[HeaderRowIndex],[MatchKeyField],
                   [MappingJson],[IsActive],[Created],[Updated],[CreatedById],[UpdatedById]
            FROM {_table}
            {(includeInactive ? "" : "WHERE [IsActive] = 1")}
            ORDER BY [Name];
            """;
        var list = new List<ImportTemplate>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(Map(r));
        return list;
    }

    public async Task<ImportTemplate?> GetByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[Name],[TargetEntity],[SheetName],[HeaderRowIndex],[MatchKeyField],
                   [MappingJson],[IsActive],[Created],[Updated],[CreatedById],[UpdatedById]
            FROM {_table}
            WHERE [Id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Map(r) : null;
    }

    public async Task<bool> NameExistsAsync(string name, int? excludeId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM {_table}
                WHERE [IsActive] = 1
                  AND LOWER([Name]) = LOWER(@Name)
                  AND (@ExcludeId IS NULL OR [Id] <> @ExcludeId)
            ) THEN 1 ELSE 0 END;
            """;
        cmd.Parameters.Add(new SqlParameter("@Name", name));
        cmd.Parameters.Add(new SqlParameter("@ExcludeId", (object?)excludeId ?? DBNull.Value));
        return ((int)(await cmd.ExecuteScalarAsync(ct) ?? 0)) == 1;
    }

    public async Task<int> SaveAsync(ImportTemplate entity, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        if (entity.Id <= 0)
        {
            cmd.CommandText = $"""
                INSERT INTO {_table}
                  ([Name],[TargetEntity],[SheetName],[HeaderRowIndex],[MatchKeyField],
                   [MappingJson],[IsActive],[CreatedById])
                OUTPUT INSERTED.[Id]
                VALUES
                  (@Name,@TargetEntity,@SheetName,@HeaderRowIndex,@MatchKeyField,
                   @MappingJson,@IsActive,@CreatedById);
                """;
            Bind(cmd, entity);
            cmd.Parameters.Add(new SqlParameter("@CreatedById", (object?)entity.CreatedById ?? DBNull.Value));
            return (int)(await cmd.ExecuteScalarAsync(ct) ?? 0);
        }

        cmd.CommandText = $"""
            UPDATE {_table}
            SET [Name] = @Name,
                [TargetEntity] = @TargetEntity,
                [SheetName] = @SheetName,
                [HeaderRowIndex] = @HeaderRowIndex,
                [MatchKeyField] = @MatchKeyField,
                [MappingJson] = @MappingJson,
                [IsActive] = @IsActive,
                [UpdatedById] = @UpdatedById,
                [Updated] = SYSUTCDATETIME()
            WHERE [Id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", entity.Id));
        Bind(cmd, entity);
        cmd.Parameters.Add(new SqlParameter("@UpdatedById", (object?)entity.UpdatedById ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
        return entity.Id;
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_table} WHERE [Id] = @Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> ToggleActiveAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_table} SET [IsActive] = CASE WHEN [IsActive] = 1 THEN 0 ELSE 1 END,
                                [Updated] = SYSUTCDATETIME()
            OUTPUT INSERTED.[IsActive]
            WHERE [Id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        var res = await cmd.ExecuteScalarAsync(ct);
        return res is bool b && b;
    }

    private static void Bind(SqlCommand cmd, ImportTemplate e)
    {
        cmd.Parameters.Add(new SqlParameter("@Name", e.Name));
        cmd.Parameters.Add(new SqlParameter("@TargetEntity", e.TargetEntity));
        cmd.Parameters.Add(new SqlParameter("@SheetName", (object?)e.SheetName ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@HeaderRowIndex", e.HeaderRowIndex));
        cmd.Parameters.Add(new SqlParameter("@MatchKeyField", (object?)e.MatchKeyField ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@MappingJson", e.MappingJson ?? "[]"));
        cmd.Parameters.Add(new SqlParameter("@IsActive", e.IsActive));
    }

    private static ImportTemplate Map(SqlDataReader r) => new()
    {
        Id             = r.GetInt32(0),
        Name           = r.GetString(1),
        TargetEntity   = r.GetString(2),
        SheetName      = r.IsDBNull(3) ? null : r.GetString(3),
        HeaderRowIndex = r.GetInt32(4),
        MatchKeyField  = r.IsDBNull(5) ? null : r.GetString(5),
        MappingJson    = r.IsDBNull(6) ? "[]" : r.GetString(6),
        IsActive       = r.GetBoolean(7),
        Created        = r.GetDateTime(8),
        Updated        = r.IsDBNull(9) ? null : r.GetDateTime(9),
        CreatedById    = r.IsDBNull(10) ? null : r.GetInt32(10),
        UpdatedById    = r.IsDBNull(11) ? null : r.GetInt32(11),
    };
}
