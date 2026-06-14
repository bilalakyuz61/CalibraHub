using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlRoutingRepository : IRoutingRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly IDataVisibilityFilter _dvFilter;
    private readonly string _schema;
    private readonly string _routingTable;
    private readonly string _opTable;
    private readonly string _mapTable;

    public SqlRoutingRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options, IDataVisibilityFilter dvFilter)
    {
        _connectionFactory = factory;
        _dvFilter = dvFilter;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        var s = _schema.Replace("]", "]]");
        _routingTable = $"[{s}].[Routing]";
        _opTable = $"[{s}].[RoutingOperation]";
        _mapTable = $"[{s}].[RoutingItemMap]";
    }

    public async Task<IReadOnlyCollection<RoutingDto>> ListAsync(int? itemId, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        var dv = await _dvFilter.BuildAsync(FormCodes.Routings, "r", "Id", ct);
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        var itemFilter = itemId.HasValue ? "AND r.[ItemId] = @ItemId" : "";
        cmd.CommandText = $@"
            SELECT r.[Id], r.[Code], r.[Name], r.[ItemId],
                   i.[Code] AS ItemCode, i.[Name] AS ItemName,
                   r.[ConfigId], r.[Description], r.[IsActive],
                   (SELECT COUNT(*) FROM {_opTable} o WHERE o.[RoutingId] = r.[Id]) AS OperationCount,
                   r.[Created], r.[Updated]
            FROM {_routingTable} r
            LEFT JOIN [{_schema}].[Items] i ON i.[Id] = r.[ItemId]
            WHERE r.[CompanyId] = @CompanyId
            {itemFilter}
            {dv.Sql}
            ORDER BY r.[Code];";
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        if (itemId.HasValue) cmd.Parameters.AddWithValue("@ItemId", itemId.Value);
        foreach (var prm in dv.Parameters) cmd.Parameters.AddWithValue(prm.Name, prm.Value);
        return await ReadListAsync(cmd, ct);
    }

    public async Task<RoutingDto?> GetAsync(int id, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT TOP 1 r.[Id], r.[Code], r.[Name], r.[ItemId],
                   i.[Code], i.[Name],
                   r.[ConfigId], r.[Description], r.[IsActive],
                   (SELECT COUNT(*) FROM {_opTable} o WHERE o.[RoutingId] = r.[Id]),
                   r.[Created], r.[Updated]
            FROM {_routingTable} r
            LEFT JOIN [{_schema}].[Items] i ON i.[Id] = r.[ItemId]
            WHERE r.[Id] = @Id AND r.[CompanyId] = @CompanyId;";
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        var list = await ReadListAsync(cmd, ct);
        return list.FirstOrDefault();
    }

    public async Task<IReadOnlyCollection<RoutingOperationDto>> GetOperationsAsync(int routingId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT ro.[Id], ro.[RoutingId], ro.[Sequence],
                   ro.[OperationId], op.[Code], op.[Name],
                   ro.[MachineId], NULL AS MachineCode, NULL AS MachineName,
                   ro.[OverrideDuration], ro.[DurationUnit], ro.[Notes]
            FROM {_opTable} ro
            INNER JOIN [{_schema}].[Operation] op ON op.[Id] = ro.[OperationId]
            WHERE ro.[RoutingId] = @RoutingId
            ORDER BY ro.[Sequence];";
        cmd.Parameters.AddWithValue("@RoutingId", routingId);
        var list = new List<RoutingOperationDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new RoutingOperationDto(
                Id: r.GetInt32(0),
                RoutingId: r.GetInt32(1),
                Sequence: r.GetInt32(2),
                OperationId: r.GetInt32(3),
                OperationCode: r.IsDBNull(4) ? null : r.GetString(4),
                OperationName: r.IsDBNull(5) ? null : r.GetString(5),
                MachineId: r.IsDBNull(6) ? null : r.GetInt32(6),
                Code: r.IsDBNull(7) ? null : r.GetString(7),
                Name: r.IsDBNull(8) ? null : r.GetString(8),
                OverrideDuration: r.IsDBNull(9) ? null : r.GetDecimal(9),
                DurationUnit: (DurationUnit)r.GetByte(10),
                Notes: r.IsDBNull(11) ? null : r.GetString(11)));
        }
        return list;
    }

    public async Task<int> SaveAsync(Routing header, IReadOnlyList<RoutingOperation>? operations, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            int routingId;
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                if (header.Id <= 0)
                {
                    cmd.CommandText = $@"
                        INSERT INTO {_routingTable}
                            ([CompanyId],[Code],[Name],[ItemId],[ConfigId],[Description],[IsActive],[Created])
                        VALUES (@CompanyId,@Code,@Name,@ItemId,@ConfigId,@Description,@IsActive,GETUTCDATE());
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";
                }
                else
                {
                    cmd.CommandText = $@"
                        UPDATE {_routingTable}
                        SET [Code]=@Code,[Name]=@Name,[ItemId]=@ItemId,[ConfigId]=@ConfigId,
                            [Description]=@Description,[IsActive]=@IsActive,[Updated]=GETUTCDATE()
                        WHERE [Id]=@Id AND [CompanyId]=@CompanyId;
                        SELECT @Id;";
                    cmd.Parameters.AddWithValue("@Id", header.Id);
                }
                cmd.Parameters.AddWithValue("@CompanyId", companyId);
                cmd.Parameters.AddWithValue("@Code", header.Code);
                cmd.Parameters.AddWithValue("@Name", header.Name);
                cmd.Parameters.AddWithValue("@ItemId", (object?)header.ItemId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ConfigId", (object?)header.ConfigId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Description", (object?)header.Description ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@IsActive", header.IsActive);
                var res = await cmd.ExecuteScalarAsync(ct);
                routingId = res != null && res != DBNull.Value ? Convert.ToInt32(res) : 0;
                if (routingId <= 0) throw new InvalidOperationException("Rota kaydedilemedi.");
            }

            // Operasyonlar — DELETE + INSERT (basit ve güvenilir; rota kaydında satır sayısı genelde küçük)
            if (operations != null)
            {
                await using (var del = conn.CreateCommand())
                {
                    del.Transaction = tx;
                    del.CommandText = $"DELETE FROM {_opTable} WHERE [RoutingId] = @RoutingId;";
                    del.Parameters.AddWithValue("@RoutingId", routingId);
                    await del.ExecuteNonQueryAsync(ct);
                }

                foreach (var op in operations)
                {
                    await using var ins = conn.CreateCommand();
                    ins.Transaction = tx;
                    ins.CommandText = $@"
                        INSERT INTO {_opTable}
                            ([RoutingId],[Sequence],[OperationId],[MachineId],
                             [OverrideDuration],[DurationUnit],[Notes])
                        VALUES (@RoutingId,@Sequence,@OperationId,@MachineId,
                                @OverrideDuration,@DurationUnit,@Notes);";
                    ins.Parameters.AddWithValue("@RoutingId", routingId);
                    ins.Parameters.AddWithValue("@Sequence", op.Sequence);
                    ins.Parameters.AddWithValue("@OperationId", op.OperationId);
                    ins.Parameters.AddWithValue("@MachineId", (object?)op.MachineId ?? DBNull.Value);
                    ins.Parameters.AddWithValue("@OverrideDuration", (object?)op.OverrideDuration ?? DBNull.Value);
                    ins.Parameters.AddWithValue("@DurationUnit", (byte)op.DurationUnit);
                    ins.Parameters.AddWithValue("@Notes", (object?)op.Notes ?? DBNull.Value);
                    await ins.ExecuteNonQueryAsync(ct);
                }
            }

            await tx.CommitAsync(ct);
            return routingId;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        // RoutingOperation FK ON DELETE CASCADE — sadece header silinir.
        cmd.CommandText = $"DELETE FROM {_routingTable} WHERE [Id]=@Id AND [CompanyId]=@CompanyId;";
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyCollection<RoutingDto>> ListByOperationAsync(int operationId, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT DISTINCT r.[Id], r.[Code], r.[Name], r.[ItemId],
                   i.[Code], i.[Name],
                   r.[ConfigId], r.[Description], r.[IsActive],
                   (SELECT COUNT(*) FROM {_opTable} o2 WHERE o2.[RoutingId] = r.[Id]),
                   r.[Created], r.[Updated]
            FROM {_routingTable} r
            INNER JOIN {_opTable} ro ON ro.[RoutingId] = r.[Id]
            LEFT JOIN [{_schema}].[Items] i ON i.[Id] = r.[ItemId]
            WHERE r.[CompanyId] = @CompanyId AND ro.[OperationId] = @OperationId
            ORDER BY r.[Code];";
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        cmd.Parameters.AddWithValue("@OperationId", operationId);
        return await ReadListAsync(cmd, ct);
    }

    public async Task<IReadOnlyCollection<RoutingOperationDto>> GetAllOperationsAsync(CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT ro.[Id], ro.[RoutingId], ro.[Sequence],
                   ro.[OperationId], op.[Code], op.[Name],
                   ro.[MachineId], NULL AS MachineCode, NULL AS MachineName,
                   ro.[OverrideDuration], ro.[DurationUnit], ro.[Notes]
            FROM {_opTable} ro
            INNER JOIN [{_schema}].[Operation] op ON op.[Id] = ro.[OperationId]
            WHERE EXISTS (
                SELECT 1 FROM {_routingTable} r
                WHERE r.[Id] = ro.[RoutingId] AND r.[CompanyId] = @CompanyId
            )
            ORDER BY ro.[RoutingId], ro.[Sequence];";
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        var list = new List<RoutingOperationDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new RoutingOperationDto(
                Id: r.GetInt32(0),
                RoutingId: r.GetInt32(1),
                Sequence: r.GetInt32(2),
                OperationId: r.GetInt32(3),
                OperationCode: r.IsDBNull(4) ? null : r.GetString(4),
                OperationName: r.IsDBNull(5) ? null : r.GetString(5),
                MachineId: r.IsDBNull(6) ? null : r.GetInt32(6),
                Code: r.IsDBNull(7) ? null : r.GetString(7),
                Name: r.IsDBNull(8) ? null : r.GetString(8),
                OverrideDuration: r.IsDBNull(9) ? null : r.GetDecimal(9),
                DurationUnit: (DurationUnit)r.GetByte(10),
                Notes: r.IsDBNull(11) ? null : r.GetString(11)));
        }
        return list;
    }

    public async Task<IReadOnlyCollection<RoutingItemMapDto>> GetItemMapsAsync(int routingId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT m.[Id], m.[RoutingId], m.[ItemId],
                   i.[Code] AS ItemCode, i.[Name] AS ItemName,
                   m.[ConfigId], ic.[Code] AS CombinationCode, ic.[Name] AS CombinationName
            FROM {_mapTable} m
            INNER JOIN [{_schema}].[Items] i ON i.[Id] = m.[ItemId]
            LEFT JOIN [{_schema}].[ItemConfiguration] ic ON ic.[Id] = m.[ConfigId]
            WHERE m.[RoutingId] = @RoutingId
            ORDER BY i.[Code], ic.[Code];";
        cmd.Parameters.AddWithValue("@RoutingId", routingId);
        var list = new List<RoutingItemMapDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new RoutingItemMapDto(
                Id: r.GetInt32(0),
                RoutingId: r.GetInt32(1),
                ItemId: r.GetInt32(2),
                ItemCode: r.IsDBNull(3) ? null : r.GetString(3),
                ItemName: r.IsDBNull(4) ? null : r.GetString(4),
                ConfigId: r.IsDBNull(5) ? null : r.GetInt32(5),
                CombinationCode: r.IsDBNull(6) ? null : r.GetString(6),
                CombinationName: r.IsDBNull(7) ? null : r.GetString(7)));
        }
        return list;
    }

    public async Task<int> AddItemMapAsync(int routingId, int itemId, int? configId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            IF NOT EXISTS (
                SELECT 1 FROM {_mapTable}
                WHERE [RoutingId]=@RoutingId AND [ItemId]=@ItemId
                  AND ((@ConfigId IS NULL AND [ConfigId] IS NULL) OR [ConfigId]=@ConfigId)
            )
            BEGIN
                INSERT INTO {_mapTable} ([RoutingId],[ItemId],[ConfigId])
                VALUES (@RoutingId,@ItemId,@ConfigId);
            END
            SELECT ISNULL(
                (SELECT [Id] FROM {_mapTable}
                 WHERE [RoutingId]=@RoutingId AND [ItemId]=@ItemId
                   AND ((@ConfigId IS NULL AND [ConfigId] IS NULL) OR [ConfigId]=@ConfigId)),
                0);";
        cmd.Parameters.AddWithValue("@RoutingId", routingId);
        cmd.Parameters.AddWithValue("@ItemId", itemId);
        cmd.Parameters.AddWithValue("@ConfigId", (object?)configId ?? DBNull.Value);
        var res = await cmd.ExecuteScalarAsync(ct);
        return res != null && res != DBNull.Value ? Convert.ToInt32(res) : 0;
    }

    public async Task DeleteItemMapAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_mapTable} WHERE [Id]=@Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<IReadOnlyCollection<RoutingDto>> ReadListAsync(SqlCommand cmd, CancellationToken ct)
    {
        var list = new List<RoutingDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new RoutingDto(
                Id: r.GetInt32(0),
                Code: r.GetString(1),
                Name: r.GetString(2),
                ItemId: r.IsDBNull(3) ? null : r.GetInt32(3),
                ItemCode: r.IsDBNull(4) ? null : r.GetString(4),
                ItemName: r.IsDBNull(5) ? null : r.GetString(5),
                ConfigId: r.IsDBNull(6) ? null : r.GetInt32(6),
                Description: r.IsDBNull(7) ? null : r.GetString(7),
                IsActive: r.GetBoolean(8),
                OperationCount: r.GetInt32(9),
                Created: r.GetDateTime(10),
                Updated: r.IsDBNull(11) ? null : r.GetDateTime(11)));
        }
        return list;
    }
}
