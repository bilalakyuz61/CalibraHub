using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using CalibraHub.Persistence.Security;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// 2026-05-23 — AiProvider persistence. Per-company şirket DB'sinde izole.
///
/// **Şifreleme:**
///   - Write: plainApiKey → IntegratorSecretProtector.Protect → ApiKeyEncrypted ('enc:v1:' prefix)
///   - Read: entity ApiKeyEncrypted'ı HÂLÂ ŞİFRELİ döner; gerçek key için
///     <see cref="GetDecryptedApiKeyAsync"/> kullanılır.
///
/// **Single-default invariant:** SaveAsync IsDefault=true ise diğer tüm provider'larda
/// IsDefault=false yapılır (transaction içinde UPDATE).
/// </summary>
public sealed class SqlAiProviderRepository : IAiProviderRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _schema;
    private readonly string _table;

    public SqlAiProviderRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options)
    {
        _connectionFactory = factory;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        var s = _schema.Replace("]", "]]");
        _table = $"[{s}].[AiProvider]";
    }

    public async Task<IReadOnlyList<AiProvider>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[Code],[Label],[ApiKeyEncrypted],[EndpointUrl],[DefaultModel],
                   [ExtraJson],[IsActive],[IsDefault],[SortOrder],
                   [Created],[Updated],[CreatedById],[UpdatedById]
            FROM {_table}
            {(includeInactive ? "" : "WHERE [IsActive] = 1")}
            ORDER BY [SortOrder], [Label];
            """;
        var list = new List<AiProvider>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(Map(r));
        return list;
    }

    public async Task<AiProvider?> GetByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[Code],[Label],[ApiKeyEncrypted],[EndpointUrl],[DefaultModel],
                   [ExtraJson],[IsActive],[IsDefault],[SortOrder],
                   [Created],[Updated],[CreatedById],[UpdatedById]
            FROM {_table}
            WHERE [Id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Map(r) : null;
    }

    public async Task<AiProvider?> GetByCodeAsync(string code, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP 1 [Id],[Code],[Label],[ApiKeyEncrypted],[EndpointUrl],[DefaultModel],
                   [ExtraJson],[IsActive],[IsDefault],[SortOrder],
                   [Created],[Updated],[CreatedById],[UpdatedById]
            FROM {_table}
            WHERE [Code] = @Code AND [IsActive] = 1;
            """;
        cmd.Parameters.Add(new SqlParameter("@Code", code));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Map(r) : null;
    }

    public async Task<AiProvider?> GetDefaultAsync(CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP 1 [Id],[Code],[Label],[ApiKeyEncrypted],[EndpointUrl],[DefaultModel],
                   [ExtraJson],[IsActive],[IsDefault],[SortOrder],
                   [Created],[Updated],[CreatedById],[UpdatedById]
            FROM {_table}
            WHERE [IsActive] = 1 AND [IsDefault] = 1
            ORDER BY [SortOrder];
            """;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Map(r) : null;
    }

    public async Task<int> SaveAsync(AiProvider entity, string? plainApiKey, CancellationToken ct)
    {
        entity.EnsureValid();

        // plainApiKey verildiyse encrypt, NULL ise mevcut korunur (UPDATE branch'i ApiKeyEncrypted'ı atlar)
        string? encryptedKey = !string.IsNullOrWhiteSpace(plainApiKey)
            ? IntegratorSecretProtector.Protect(plainApiKey)
            : null;

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            // Single-default invariant: bu provider default ise diğerlerini false yap
            if (entity.IsDefault)
            {
                await using var clearCmd = conn.CreateCommand();
                clearCmd.Transaction = tx;
                clearCmd.CommandText = $"UPDATE {_table} SET [IsDefault] = 0 WHERE [Id] <> @KeepId AND [IsDefault] = 1;";
                clearCmd.Parameters.Add(new SqlParameter("@KeepId", entity.Id));
                await clearCmd.ExecuteNonQueryAsync(ct);
            }

            int newId;
            if (entity.Id <= 0)
            {
                // INSERT
                await using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = $"""
                    INSERT INTO {_table}
                      ([Code],[Label],[ApiKeyEncrypted],[EndpointUrl],[DefaultModel],[ExtraJson],
                       [IsActive],[IsDefault],[SortOrder],[CreatedById])
                    OUTPUT INSERTED.[Id]
                    VALUES
                      (@Code,@Label,@ApiKeyEncrypted,@EndpointUrl,@DefaultModel,@ExtraJson,
                       @IsActive,@IsDefault,@SortOrder,@CreatedById);
                    """;
                ins.Parameters.Add(new SqlParameter("@Code", entity.Code));
                ins.Parameters.Add(new SqlParameter("@Label", entity.Label));
                ins.Parameters.Add(new SqlParameter("@ApiKeyEncrypted", (object?)encryptedKey ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@EndpointUrl", (object?)entity.EndpointUrl ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@DefaultModel", (object?)entity.DefaultModel ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@ExtraJson", (object?)entity.ExtraJson ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@IsActive", entity.IsActive));
                ins.Parameters.Add(new SqlParameter("@IsDefault", entity.IsDefault));
                ins.Parameters.Add(new SqlParameter("@SortOrder", entity.SortOrder));
                ins.Parameters.Add(new SqlParameter("@CreatedById", (object?)(entity.CreatedById > 0 ? entity.CreatedById : null) ?? DBNull.Value));
                newId = (int)(await ins.ExecuteScalarAsync(ct) ?? 0);
            }
            else
            {
                // UPDATE — ApiKey'i sadece plainApiKey verildiyse güncelle
                await using var upd = conn.CreateCommand();
                upd.Transaction = tx;
                var setApiKey = encryptedKey != null
                    ? "[ApiKeyEncrypted] = @ApiKeyEncrypted,"
                    : "";
                upd.CommandText = $"""
                    UPDATE {_table}
                    SET [Code] = @Code,
                        [Label] = @Label,
                        {setApiKey}
                        [EndpointUrl] = @EndpointUrl,
                        [DefaultModel] = @DefaultModel,
                        [ExtraJson] = @ExtraJson,
                        [IsActive] = @IsActive,
                        [IsDefault] = @IsDefault,
                        [SortOrder] = @SortOrder,
                        [UpdatedById] = @UpdatedById,
                        [Updated] = SYSUTCDATETIME()
                    WHERE [Id] = @Id;
                    """;
                upd.Parameters.Add(new SqlParameter("@Id", entity.Id));
                upd.Parameters.Add(new SqlParameter("@Code", entity.Code));
                upd.Parameters.Add(new SqlParameter("@Label", entity.Label));
                if (encryptedKey != null)
                    upd.Parameters.Add(new SqlParameter("@ApiKeyEncrypted", encryptedKey));
                upd.Parameters.Add(new SqlParameter("@EndpointUrl", (object?)entity.EndpointUrl ?? DBNull.Value));
                upd.Parameters.Add(new SqlParameter("@DefaultModel", (object?)entity.DefaultModel ?? DBNull.Value));
                upd.Parameters.Add(new SqlParameter("@ExtraJson", (object?)entity.ExtraJson ?? DBNull.Value));
                upd.Parameters.Add(new SqlParameter("@IsActive", entity.IsActive));
                upd.Parameters.Add(new SqlParameter("@IsDefault", entity.IsDefault));
                upd.Parameters.Add(new SqlParameter("@SortOrder", entity.SortOrder));
                upd.Parameters.Add(new SqlParameter("@UpdatedById", (object?)(entity.UpdatedById > 0 ? entity.UpdatedById : null) ?? DBNull.Value));
                await upd.ExecuteNonQueryAsync(ct);
                newId = entity.Id;
            }

            await tx.CommitAsync(ct);
            return newId;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        // CASCADE: AiUserKey FK_AiUserKey_Provider ON DELETE CASCADE — kullanıcı override'lar
        // otomatik silinir.
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_table} WHERE [Id] = @Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<string?> GetDecryptedApiKeyAsync(int providerId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT [ApiKeyEncrypted] FROM {_table} WHERE [Id] = @Id AND [IsActive] = 1;";
        cmd.Parameters.Add(new SqlParameter("@Id", providerId));
        var raw = await cmd.ExecuteScalarAsync(ct);
        if (raw is null or DBNull) return null;
        var encrypted = raw.ToString();
        if (string.IsNullOrWhiteSpace(encrypted)) return null;
        var plain = IntegratorSecretProtector.Unprotect(encrypted);
        return string.IsNullOrWhiteSpace(plain) ? null : plain;
    }

    private static AiProvider Map(SqlDataReader r) => new()
    {
        Id              = r.GetInt32(0),
        Code            = r.GetString(1),
        Label           = r.GetString(2),
        ApiKeyEncrypted = r.IsDBNull(3) ? null : r.GetString(3),
        EndpointUrl     = r.IsDBNull(4) ? null : r.GetString(4),
        DefaultModel    = r.IsDBNull(5) ? null : r.GetString(5),
        ExtraJson       = r.IsDBNull(6) ? null : r.GetString(6),
        IsActive        = r.GetBoolean(7),
        IsDefault       = r.GetBoolean(8),
        SortOrder       = r.GetInt32(9),
        Created         = r.GetDateTime(10),
        Updated         = r.IsDBNull(11) ? null : r.GetDateTime(11),
        CreatedById     = r.IsDBNull(12) ? null : r.GetInt32(12),
        UpdatedById     = r.IsDBNull(13) ? null : r.GetInt32(13),
    };
}
