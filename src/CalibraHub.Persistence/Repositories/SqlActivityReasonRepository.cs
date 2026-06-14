using System.ComponentModel;
using System.Reflection;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlActivityReasonRepository : IActivityReasonRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _schema;
    private readonly string _table;

    public SqlActivityReasonRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options)
    {
        _connectionFactory = factory;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        var s = _schema.Replace("]", "]]");
        _table = $"[{s}].[ActivityReason]";
    }

    public async Task<IReadOnlyList<ActivityReasonDto>> ListAsync(
        WorkOrderActivityType? activityType, bool includeInactive, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        var typeFilter = activityType.HasValue ? "AND [ActivityType] = @Type" : "";
        var activeFilter = includeInactive ? "" : "AND [IsActive] = 1";
        cmd.CommandText = $@"
            SELECT [Id],[ActivityType],[Code],[Name],[Description],[ColorHex],
                   [SortOrder],[IsActive],[Created],[Updated]
            FROM {_table}
            WHERE 1=1 {typeFilter} {activeFilter}
            ORDER BY [ActivityType], [SortOrder], [Name];";
        if (activityType.HasValue)
            cmd.Parameters.AddWithValue("@Type", (byte)activityType.Value);
        var list = new List<ActivityReasonDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(Read(r));
        return list;
    }

    public async Task<ActivityReasonDto?> GetAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT [Id],[ActivityType],[Code],[Name],[Description],[ColorHex],
                   [SortOrder],[IsActive],[Created],[Updated]
            FROM {_table}
            WHERE [Id] = @Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Read(r) : null;
    }

    public async Task<int> SaveAsync(ActivityReason entity, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        if (entity.Id <= 0)
        {
            cmd.CommandText = $@"
                INSERT INTO {_table}
                    ([ActivityType],[Code],[Name],[Description],[ColorHex],
                     [SortOrder],[IsActive],[CreatedById],[Created])
                VALUES
                    (@Type,@Code,@Name,@Description,@ColorHex,
                     @SortOrder,@IsActive,@CreatedById,SYSUTCDATETIME());
                SELECT CAST(SCOPE_IDENTITY() AS INT);";
        }
        else
        {
            cmd.CommandText = $@"
                UPDATE {_table}
                SET [ActivityType] = @Type,
                    [Code]         = @Code,
                    [Name]         = @Name,
                    [Description]  = @Description,
                    [ColorHex]     = @ColorHex,
                    [SortOrder]    = @SortOrder,
                    [IsActive]     = @IsActive,
                    [UpdatedById]  = @UpdatedById,
                    [Updated]      = SYSUTCDATETIME()
                WHERE [Id] = @Id;
                SELECT @Id;";
            cmd.Parameters.AddWithValue("@Id", entity.Id);
            cmd.Parameters.AddWithValue("@UpdatedById", (object?)entity.UpdatedById ?? DBNull.Value);
        }
        cmd.Parameters.AddWithValue("@Type",        (byte)entity.ActivityType);
        cmd.Parameters.AddWithValue("@Code",        entity.Code.Trim());
        cmd.Parameters.AddWithValue("@Name",        entity.Name.Trim());
        cmd.Parameters.AddWithValue("@Description", (object?)entity.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ColorHex",    (object?)entity.ColorHex    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@SortOrder",   entity.SortOrder);
        cmd.Parameters.AddWithValue("@IsActive",    entity.IsActive);
        if (entity.Id <= 0)
            cmd.Parameters.AddWithValue("@CreatedById", (object?)entity.CreatedById ?? DBNull.Value);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
    }

    public async Task DeleteAsync(int id, int? userId, CancellationToken ct)
    {
        // Soft delete — Activity log'da referans olabilir, fiziksel silmek FK ihlali.
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            UPDATE {_table}
            SET [IsActive] = 0,
                [UpdatedById] = @UpdatedById,
                [Updated]     = SYSUTCDATETIME()
            WHERE [Id] = @Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@UpdatedById", (object?)userId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static ActivityReasonDto Read(SqlDataReader r)
    {
        var type = (WorkOrderActivityType)r.GetByte(1);
        return new ActivityReasonDto(
            Id:                r.GetInt32(0),
            ActivityType:      type,
            ActivityTypeLabel: DescribeActivityType(type),
            Code:              r.GetString(2),
            Name:              r.GetString(3),
            Description:       r.IsDBNull(4) ? null : r.GetString(4),
            ColorHex:          r.IsDBNull(5) ? null : r.GetString(5),
            SortOrder:         r.GetInt32(6),
            IsActive:          r.GetBoolean(7),
            Created:           r.GetDateTime(8),
            Updated:           r.IsDBNull(9) ? null : r.GetDateTime(9));
    }

    private static string DescribeActivityType(WorkOrderActivityType type)
    {
        var member = typeof(WorkOrderActivityType).GetMember(type.ToString()).FirstOrDefault();
        var attr = member?.GetCustomAttribute<DescriptionAttribute>();
        return attr?.Description ?? type.ToString();
    }
}
