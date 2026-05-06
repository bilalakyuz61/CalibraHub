using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// Personnel tablosu persistence. Per-company izolasyon (CompanyId).
/// PIN/CardNo lookup shop-floor tablet auth için kritik path.
/// </summary>
public sealed class SqlPersonnelRepository : IPersonnelRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _schema;
    private readonly string _table;

    public SqlPersonnelRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options)
    {
        _connectionFactory = factory;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        var s = _schema.Replace("]", "]]");
        _table = $"[{s}].[Personnel]";
    }

    public async Task<IReadOnlyCollection<PersonnelDto>> ListAsync(bool includeInactive, bool onlyOperators, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        var activeFilter = includeInactive ? "" : "AND p.[IsActive] = 1";
        var operatorFilter = onlyOperators ? "AND p.[IsProductionOperator] = 1" : "";
        cmd.CommandText = $@"
            SELECT p.[Id], p.[CompanyId], p.[Code], p.[FullName], p.[Title], p.[Department],
                   p.[PinCode], p.[CardNo], p.[IsProductionOperator], p.[IsActive],
                   p.[UserId], u.[full_name] AS UserFullName,
                   p.[Phone], p.[Email], p.[Notes], p.[Created], p.[Updated]
            FROM {_table} p
            LEFT JOIN [{_schema}].[User] u ON u.[id] = p.[UserId]
            WHERE p.[CompanyId] = @CompanyId
            {activeFilter}
            {operatorFilter}
            ORDER BY p.[FullName];";
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        return await ReadListAsync(cmd, ct);
    }

    public async Task<PersonnelDto?> GetAsync(int id, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT TOP 1
                   p.[Id], p.[CompanyId], p.[Code], p.[FullName], p.[Title], p.[Department],
                   p.[PinCode], p.[CardNo], p.[IsProductionOperator], p.[IsActive],
                   p.[UserId], u.[full_name] AS UserFullName,
                   p.[Phone], p.[Email], p.[Notes], p.[Created], p.[Updated]
            FROM {_table} p
            LEFT JOIN [{_schema}].[User] u ON u.[id] = p.[UserId]
            WHERE p.[Id] = @Id AND p.[CompanyId] = @CompanyId;";
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        var list = await ReadListAsync(cmd, ct);
        return list.FirstOrDefault();
    }

    public async Task<int> SaveAsync(Personnel e, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        if (e.Id <= 0)
        {
            cmd.CommandText = $@"
                INSERT INTO {_table}
                    ([CompanyId],[Code],[FullName],[Title],[Department],
                     [PinCode],[CardNo],[IsProductionOperator],[IsActive],
                     [UserId],[Phone],[Email],[Notes],[Created])
                VALUES
                    (@CompanyId,@Code,@FullName,@Title,@Department,
                     @PinCode,@CardNo,@IsProductionOperator,@IsActive,
                     @UserId,@Phone,@Email,@Notes,SYSUTCDATETIME());
                SELECT CAST(SCOPE_IDENTITY() AS INT);";
        }
        else
        {
            cmd.CommandText = $@"
                UPDATE {_table}
                SET [Code]=@Code, [FullName]=@FullName,
                    [Title]=@Title, [Department]=@Department,
                    [PinCode]=@PinCode, [CardNo]=@CardNo,
                    [IsProductionOperator]=@IsProductionOperator, [IsActive]=@IsActive,
                    [UserId]=@UserId, [Phone]=@Phone, [Email]=@Email, [Notes]=@Notes,
                    [Updated]=SYSUTCDATETIME()
                WHERE [Id]=@Id AND [CompanyId]=@CompanyId;
                SELECT @Id;";
            cmd.Parameters.AddWithValue("@Id", e.Id);
        }
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        cmd.Parameters.AddWithValue("@Code", e.Code);
        cmd.Parameters.AddWithValue("@FullName", e.FullName);
        cmd.Parameters.AddWithValue("@Title", (object?)e.Title ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Department", (object?)e.Department ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@PinCode", (object?)e.PinCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CardNo", (object?)e.CardNo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@IsProductionOperator", e.IsProductionOperator);
        cmd.Parameters.AddWithValue("@IsActive", e.IsActive);
        cmd.Parameters.AddWithValue("@UserId", (object?)e.UserId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Phone", (object?)e.Phone ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Email", (object?)e.Email ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Notes", (object?)e.Notes ?? DBNull.Value);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_table} WHERE [Id] = @Id AND [CompanyId] = @CompanyId;";
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<PersonnelDto?> GetByPinOrCardAsync(string? pinCode, string? cardNo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pinCode) && string.IsNullOrWhiteSpace(cardNo))
            return null;

        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();

        // Hangi alan dolu ise ona gore filtrele. Ikisi de doluysa once Card sonra PIN.
        string filter = !string.IsNullOrWhiteSpace(cardNo)
            ? "AND p.[CardNo] = @CardNo"
            : "AND p.[PinCode] = @PinCode";

        cmd.CommandText = $@"
            SELECT TOP 1
                   p.[Id], p.[CompanyId], p.[Code], p.[FullName], p.[Title], p.[Department],
                   p.[PinCode], p.[CardNo], p.[IsProductionOperator], p.[IsActive],
                   p.[UserId], u.[full_name] AS UserFullName,
                   p.[Phone], p.[Email], p.[Notes], p.[Created], p.[Updated]
            FROM {_table} p
            LEFT JOIN [{_schema}].[User] u ON u.[id] = p.[UserId]
            WHERE p.[CompanyId] = @CompanyId
              AND p.[IsActive] = 1
              AND p.[IsProductionOperator] = 1
              {filter};";

        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        if (!string.IsNullOrWhiteSpace(cardNo))
            cmd.Parameters.AddWithValue("@CardNo", cardNo.Trim());
        else
            cmd.Parameters.AddWithValue("@PinCode", pinCode!.Trim());

        var list = await ReadListAsync(cmd, ct);
        return list.FirstOrDefault();
    }

    private static async Task<IReadOnlyCollection<PersonnelDto>> ReadListAsync(SqlCommand cmd, CancellationToken ct)
    {
        var list = new List<PersonnelDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new PersonnelDto(
                Id: r.GetInt32(0),
                CompanyId: r.GetInt32(1),
                Code: r.GetString(2),
                FullName: r.GetString(3),
                Title: r.IsDBNull(4) ? null : r.GetString(4),
                Department: r.IsDBNull(5) ? null : r.GetString(5),
                PinCode: r.IsDBNull(6) ? null : r.GetString(6),
                CardNo: r.IsDBNull(7) ? null : r.GetString(7),
                IsProductionOperator: r.GetBoolean(8),
                IsActive: r.GetBoolean(9),
                UserId: r.IsDBNull(10) ? null : r.GetGuid(10),
                UserFullName: r.IsDBNull(11) ? null : r.GetString(11),
                Phone: r.IsDBNull(12) ? null : r.GetString(12),
                Email: r.IsDBNull(13) ? null : r.GetString(13),
                Notes: r.IsDBNull(14) ? null : r.GetString(14),
                Created: r.GetDateTime(15),
                Updated: r.IsDBNull(16) ? null : r.GetDateTime(16)));
        }
        return list;
    }
}
