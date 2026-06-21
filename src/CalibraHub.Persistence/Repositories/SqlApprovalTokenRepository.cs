using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;
using System.Security.Cryptography;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlApprovalTokenRepository : IApprovalTokenRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _schema;

    public SqlApprovalTokenRepository(
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
    }

    public async Task<string> CreateAsync(int instanceId, int? stepRecordId, string approverId, CancellationToken ct)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = ((SqlConnection)con).CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO [{_schema}].[ApprovalActionToken]
                ([Token], [InstanceId], [StepRecordId], [ApproverId], [ExpiresAt])
            VALUES (@Token, @InstanceId, @StepRecordId, @ApproverId, @ExpiresAt);
            """;
        cmd.Parameters.AddWithValue("@Token", token);
        cmd.Parameters.AddWithValue("@InstanceId", instanceId);
        cmd.Parameters.AddWithValue("@StepRecordId", stepRecordId.HasValue ? (object)stepRecordId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@ApproverId", approverId);
        cmd.Parameters.AddWithValue("@ExpiresAt", DateTime.UtcNow.AddDays(7));
        await cmd.ExecuteNonQueryAsync(ct);
        return token;
    }

    public async Task<ApprovalTokenRecord?> FindAsync(int companyId, string token, CancellationToken ct)
    {
        var connStr = _connectionFactory.ResolveConnectionStringForCompany(companyId);
        await using var con = new SqlConnection(connStr);
        await con.OpenAsync(ct);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id], [Token], [InstanceId], [StepRecordId], [ApproverId],
                   [ExpiresAt], [UsedAt], [UsedAction]
            FROM [{_schema}].[ApprovalActionToken]
            WHERE [Token] = @Token;
            """;
        cmd.Parameters.AddWithValue("@Token", token);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new ApprovalTokenRecord(
            r.GetInt32(0),
            r.GetString(1),
            r.GetInt32(2),
            r.IsDBNull(3) ? null : (int?)r.GetInt32(3),
            r.GetString(4),
            r.GetDateTime(5),
            r.IsDBNull(6) ? null : r.GetDateTime(6),
            r.IsDBNull(7) ? null : r.GetString(7));
    }

    public async Task ConsumeAsync(int companyId, int tokenId, string action, CancellationToken ct)
    {
        var connStr = _connectionFactory.ResolveConnectionStringForCompany(companyId);
        await using var con = new SqlConnection(connStr);
        await con.OpenAsync(ct);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"""
            UPDATE [{_schema}].[ApprovalActionToken]
            SET [UsedAt] = @UsedAt, [UsedAction] = @Action
            WHERE [Id] = @Id AND [UsedAt] IS NULL;
            """;
        cmd.Parameters.AddWithValue("@UsedAt", DateTime.UtcNow);
        cmd.Parameters.AddWithValue("@Action", action);
        cmd.Parameters.AddWithValue("@Id", tokenId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
