using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlOperationMachineTimeRepository : IOperationMachineTimeRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _schema;
    private readonly string _table;

    public SqlOperationMachineTimeRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options)
    {
        _connectionFactory = factory;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _table = $"[{_schema.Replace("]", "]]")}].[OperationMachineTime]";
    }

    public async Task<IReadOnlyCollection<OperationMachineTimeDto>> ListByOperationAsync(int operationId, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        // Machine bilgisi tablosu varsa JOIN — yoksa NULL kalır (Logistics.Machine entity zaten var).
        // ItemId üzerinden Items JOIN — opsiyonel ürün-özel kayıt için.
        cmd.CommandText = $@"
            SELECT t.[Id], t.[OperationId], op.[Code] AS OpCode, op.[Name] AS OpName,
                   t.[MachineId], m.[MachineCode], m.[MachineName],
                   t.[ItemId], i.[Code] AS ItemCode, i.[Name] AS ItemName,
                   t.[Quantity], t.[DurationPerUnit], t.[DurationUnit],
                   t.[IsActive], t.[Created], t.[Updated]
            FROM {_table} t
            LEFT JOIN [{_schema}].[Operation] op ON op.[Id] = t.[OperationId]
            LEFT JOIN [{_schema}].[Machine] m ON m.[Id] = t.[MachineId]
            LEFT JOIN [{_schema}].[Items] i ON i.[Id] = t.[ItemId]
            WHERE t.[CompanyId] = @CompanyId AND t.[OperationId] = @OperationId
            ORDER BY i.[Code], m.[MachineCode];";
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        cmd.Parameters.AddWithValue("@OperationId", operationId);

        var list = new List<OperationMachineTimeDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new OperationMachineTimeDto(
                Id: r.GetInt32(0),
                OperationId: r.GetInt32(1),
                OperationCode: r.IsDBNull(2) ? null : r.GetString(2),
                OperationName: r.IsDBNull(3) ? null : r.GetString(3),
                MachineId: r.GetInt32(4),
                MachineCode: r.IsDBNull(5) ? null : r.GetString(5),
                MachineName: r.IsDBNull(6) ? null : r.GetString(6),
                ItemId: r.IsDBNull(7) ? null : r.GetInt32(7),
                ItemCode: r.IsDBNull(8) ? null : r.GetString(8),
                ItemName: r.IsDBNull(9) ? null : r.GetString(9),
                Quantity: r.GetDecimal(10),
                DurationPerUnit: r.GetDecimal(11),
                DurationUnit: (DurationUnit)r.GetByte(12),
                IsActive: r.GetBoolean(13),
                Created: r.GetDateTime(14),
                Updated: r.IsDBNull(15) ? null : r.GetDateTime(15)));
        }
        return list;
    }

    public async Task<int> SaveAsync(OperationMachineTime e, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        if (e.Id <= 0)
        {
            cmd.CommandText = $@"
                INSERT INTO {_table}
                    ([CompanyId],[OperationId],[MachineId],[ItemId],
                     [Quantity],[DurationPerUnit],[DurationUnit],[IsActive],[Created])
                VALUES (@CompanyId,@OperationId,@MachineId,@ItemId,
                        @Quantity,@DurationPerUnit,@DurationUnit,@IsActive,GETUTCDATE());
                SELECT CAST(SCOPE_IDENTITY() AS INT);";
        }
        else
        {
            cmd.CommandText = $@"
                UPDATE {_table}
                SET [OperationId]=@OperationId,[MachineId]=@MachineId,[ItemId]=@ItemId,
                    [Quantity]=@Quantity,[DurationPerUnit]=@DurationPerUnit,[DurationUnit]=@DurationUnit,
                    [IsActive]=@IsActive,[Updated]=GETUTCDATE()
                WHERE [Id]=@Id AND [CompanyId]=@CompanyId;
                SELECT @Id;";
            cmd.Parameters.AddWithValue("@Id", e.Id);
        }
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        cmd.Parameters.AddWithValue("@OperationId", e.OperationId);
        cmd.Parameters.AddWithValue("@MachineId", e.MachineId);
        cmd.Parameters.AddWithValue("@ItemId", (object?)e.ItemId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Quantity", e.Quantity);
        cmd.Parameters.AddWithValue("@DurationPerUnit", e.DurationPerUnit);
        cmd.Parameters.AddWithValue("@DurationUnit", (byte)e.DurationUnit);
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
}
