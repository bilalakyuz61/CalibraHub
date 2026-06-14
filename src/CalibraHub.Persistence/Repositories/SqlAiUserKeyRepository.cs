using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using CalibraHub.Persistence.Security;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// 2026-05-23 — AiUserKey persistence. Kullanıcı override key'leri.
/// SaveAsync UPSERT pattern (MERGE değil, basit IF EXISTS UPDATE ELSE INSERT).
/// </summary>
public sealed class SqlAiUserKeyRepository : IAiUserKeyRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _schema;
    private readonly string _table;

    public SqlAiUserKeyRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options)
    {
        _connectionFactory = factory;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        var s = _schema.Replace("]", "]]");
        _table = $"[{s}].[AiUserKey]";
    }

    public async Task<IReadOnlyList<AiUserKey>> ListByUserAsync(int userId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[UserId],[AiProviderId],[ApiKeyEncrypted],[Created],[Updated]
            FROM {_table}
            WHERE [UserId] = @UserId
            ORDER BY [Id];
            """;
        cmd.Parameters.Add(new SqlParameter("@UserId", userId));
        var list = new List<AiUserKey>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(Map(r));
        return list;
    }

    public async Task<AiUserKey?> GetAsync(int userId, int providerId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP 1 [Id],[UserId],[AiProviderId],[ApiKeyEncrypted],[Created],[Updated]
            FROM {_table}
            WHERE [UserId] = @UserId AND [AiProviderId] = @ProviderId;
            """;
        cmd.Parameters.Add(new SqlParameter("@UserId", userId));
        cmd.Parameters.Add(new SqlParameter("@ProviderId", providerId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Map(r) : null;
    }

    public async Task<int> SaveAsync(int userId, int providerId, string plainApiKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(plainApiKey))
            throw new ArgumentException("plainApiKey boş olamaz.", nameof(plainApiKey));

        var encrypted = IntegratorSecretProtector.Protect(plainApiKey);

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);

        // Mevcut mu? UPDATE veya INSERT
        await using var existsCmd = conn.CreateCommand();
        existsCmd.CommandText = $"SELECT [Id] FROM {_table} WHERE [UserId] = @UserId AND [AiProviderId] = @ProviderId;";
        existsCmd.Parameters.Add(new SqlParameter("@UserId", userId));
        existsCmd.Parameters.Add(new SqlParameter("@ProviderId", providerId));
        var existing = await existsCmd.ExecuteScalarAsync(ct);

        if (existing is int existingId)
        {
            await using var upd = conn.CreateCommand();
            upd.CommandText = $"""
                UPDATE {_table}
                SET [ApiKeyEncrypted] = @ApiKeyEncrypted,
                    [Updated] = SYSUTCDATETIME()
                WHERE [Id] = @Id;
                """;
            upd.Parameters.Add(new SqlParameter("@Id", existingId));
            upd.Parameters.Add(new SqlParameter("@ApiKeyEncrypted", encrypted));
            await upd.ExecuteNonQueryAsync(ct);
            return existingId;
        }

        await using var ins = conn.CreateCommand();
        ins.CommandText = $"""
            INSERT INTO {_table} ([UserId],[AiProviderId],[ApiKeyEncrypted])
            OUTPUT INSERTED.[Id]
            VALUES (@UserId,@ProviderId,@ApiKeyEncrypted);
            """;
        ins.Parameters.Add(new SqlParameter("@UserId", userId));
        ins.Parameters.Add(new SqlParameter("@ProviderId", providerId));
        ins.Parameters.Add(new SqlParameter("@ApiKeyEncrypted", encrypted));
        var newId = (int)(await ins.ExecuteScalarAsync(ct) ?? 0);
        return newId;
    }

    public async Task DeleteAsync(int userId, int providerId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_table} WHERE [UserId] = @UserId AND [AiProviderId] = @ProviderId;";
        cmd.Parameters.Add(new SqlParameter("@UserId", userId));
        cmd.Parameters.Add(new SqlParameter("@ProviderId", providerId));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<string?> GetDecryptedApiKeyAsync(int userId, int providerId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT [ApiKeyEncrypted] FROM {_table} WHERE [UserId] = @UserId AND [AiProviderId] = @ProviderId;";
        cmd.Parameters.Add(new SqlParameter("@UserId", userId));
        cmd.Parameters.Add(new SqlParameter("@ProviderId", providerId));
        var raw = await cmd.ExecuteScalarAsync(ct);
        if (raw is null or DBNull) return null;
        var encrypted = raw.ToString();
        if (string.IsNullOrWhiteSpace(encrypted)) return null;
        var plain = IntegratorSecretProtector.Unprotect(encrypted);
        return string.IsNullOrWhiteSpace(plain) ? null : plain;
    }

    private static AiUserKey Map(SqlDataReader r) => new()
    {
        Id              = r.GetInt32(0),
        UserId          = r.GetInt32(1),
        AiProviderId    = r.GetInt32(2),
        ApiKeyEncrypted = r.GetString(3),
        Created         = r.GetDateTime(4),
        Updated         = r.IsDBNull(5) ? null : r.GetDateTime(5),
    };
}
