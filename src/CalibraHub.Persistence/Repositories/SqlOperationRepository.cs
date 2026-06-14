using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// Operation tablosu persistence — üretim operasyon sözlüğü.
/// Per-company izolasyon (CompanyId), UNIQUE (CompanyId, Code) constraint.
/// </summary>
public sealed class SqlOperationRepository : IOperationRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly IDataVisibilityFilter _dvFilter;
    private readonly string _table;

    public SqlOperationRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options, IDataVisibilityFilter dvFilter)
    {
        _connectionFactory = factory;
        _dvFilter = dvFilter;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _table = $"[{schema.Replace("]", "]]")}].[Operation]";
    }

    public async Task<IReadOnlyCollection<Operation>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        var dv = await _dvFilter.BuildAsync(FormCodes.Operations, "op", "Id", ct);
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        var activeFilter = includeInactive ? "" : "AND op.[IsActive] = 1";
        cmd.CommandText = $@"
            SELECT op.[Id],op.[CompanyId],op.[Code],op.[Name],op.[Description],op.[StandardDuration],
                   op.[DurationUnit],op.[HourlyRate],op.[SortOrder],op.[IsActive],op.[Created],op.[Updated]
            FROM {_table} op
            WHERE op.[CompanyId] = @CompanyId
            {activeFilter}
            {dv.Sql}
            ORDER BY op.[SortOrder], op.[Code];";
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        foreach (var prm in dv.Parameters) cmd.Parameters.AddWithValue(prm.Name, prm.Value);
        return await ReadListAsync(cmd, ct);
    }

    public async Task<Operation?> GetAsync(int id, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT TOP 1 [Id],[CompanyId],[Code],[Name],[Description],[StandardDuration],
                         [DurationUnit],[HourlyRate],[SortOrder],[IsActive],[Created],[Updated]
            FROM {_table}
            WHERE [Id] = @Id AND [CompanyId] = @CompanyId;";
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Map(r) : null;
    }

    public async Task<int> UpsertAsync(Operation e, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        if (e.Id <= 0)
        {
            cmd.CommandText = $@"
                INSERT INTO {_table}
                    ([CompanyId],[Code],[Name],[Description],[StandardDuration],
                     [DurationUnit],[HourlyRate],[SortOrder],[IsActive],[Created])
                VALUES
                    (@CompanyId,@Code,@Name,@Description,@StandardDuration,
                     @DurationUnit,@HourlyRate,@SortOrder,@IsActive,GETUTCDATE());
                SELECT CAST(SCOPE_IDENTITY() AS INT);";
        }
        else
        {
            cmd.CommandText = $@"
                UPDATE {_table}
                SET [Code]=@Code, [Name]=@Name, [Description]=@Description,
                    [StandardDuration]=@StandardDuration, [DurationUnit]=@DurationUnit,
                    [HourlyRate]=@HourlyRate, [SortOrder]=@SortOrder, [IsActive]=@IsActive,
                    [Updated]=GETUTCDATE()
                WHERE [Id]=@Id AND [CompanyId]=@CompanyId;
                SELECT @Id;";
            cmd.Parameters.AddWithValue("@Id", e.Id);
        }
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        cmd.Parameters.AddWithValue("@Code", e.Code);
        cmd.Parameters.AddWithValue("@Name", e.Name);
        cmd.Parameters.AddWithValue("@Description", (object?)e.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@StandardDuration", (object?)e.StandardDuration ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@DurationUnit", (byte)e.DurationUnit);
        cmd.Parameters.AddWithValue("@HourlyRate", (object?)e.HourlyRate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@SortOrder", e.SortOrder);
        cmd.Parameters.AddWithValue("@IsActive", e.IsActive);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_table} WHERE [Id]=@Id AND [CompanyId]=@CompanyId;";
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<IReadOnlyCollection<Operation>> ReadListAsync(SqlCommand cmd, CancellationToken ct)
    {
        var list = new List<Operation>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(Map(r));
        return list;
    }

    private static Operation Map(SqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        CompanyId = r.GetInt32(1),
        Code = r.GetString(2),
        Name = r.GetString(3),
        Description = r.IsDBNull(4) ? null : r.GetString(4),
        StandardDuration = r.IsDBNull(5) ? null : r.GetDecimal(5),
        DurationUnit = (DurationUnit)r.GetByte(6),
        HourlyRate = r.IsDBNull(7) ? null : r.GetDecimal(7),
        SortOrder = r.GetInt32(8),
        IsActive = r.GetBoolean(9),
        Created = r.GetDateTime(10),
        Updated = r.IsDBNull(11) ? null : r.GetDateTime(11),
    };
}
