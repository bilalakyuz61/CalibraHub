using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using CalibraHub.Persistence.Security;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// 2026-08-15 — ExternalDbConnection persistence (veritabanı üzerinden içe aktarım kaynağı).
/// Per-company şirket DB'sinde izole. Pattern: SqlImportTemplateRepository.
/// Parola DPAPI ile şifrelenerek yazılır; okunan entity şifreli değeri taşır, çözme
/// yalnız bağlantı kurulurken (SqlServerExternalDbReader) yapılır.
/// </summary>
public sealed class SqlExternalDbConnectionRepository : IExternalDbConnectionRepository
{
    private const string Columns =
        "[Id],[Name],[ServerName],[DatabaseName],[AuthMode],[Username],[PasswordEncrypted]," +
        "[Encrypt],[TrustServerCertificate],[ConnectTimeoutSeconds],[CommandTimeoutSeconds]," +
        "[IsActive],[Created],[Updated],[CreatedById],[UpdatedById],[UseHostServer]";

    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _table;

    public SqlExternalDbConnectionRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options)
    {
        _connectionFactory = factory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        var s = schema.Replace("]", "]]");
        _table = $"[{s}].[ExternalDbConnection]";
    }

    public async Task<IReadOnlyList<ExternalDbConnection>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT {Columns}
            FROM {_table}
            {(includeInactive ? "" : "WHERE [IsActive] = 1")}
            ORDER BY [Name];
            """;
        var list = new List<ExternalDbConnection>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(Map(r));
        return list;
    }

    public async Task<ExternalDbConnection?> GetByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM {_table} WHERE [Id] = @Id;";
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

    public async Task<int> SaveAsync(ExternalDbConnection entity, string? plainPassword, CancellationToken ct)
    {
        var hasNewPassword = !string.IsNullOrWhiteSpace(plainPassword);
        var encrypted = hasNewPassword ? ExternalDbSecretProtector.Protect(plainPassword!.Trim()) : null;

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();

        if (entity.Id <= 0)
        {
            cmd.CommandText = $"""
                INSERT INTO {_table}
                  ([Name],[ServerName],[DatabaseName],[AuthMode],[Username],[PasswordEncrypted],
                   [Encrypt],[TrustServerCertificate],[ConnectTimeoutSeconds],[CommandTimeoutSeconds],
                   [IsActive],[CreatedById],[UseHostServer])
                OUTPUT INSERTED.[Id]
                VALUES
                  (@Name,@ServerName,@DatabaseName,@AuthMode,@Username,@PasswordEncrypted,
                   @Encrypt,@TrustServerCertificate,@ConnectTimeoutSeconds,@CommandTimeoutSeconds,
                   @IsActive,@CreatedById,@UseHostServer);
                """;
            Bind(cmd, entity);
            cmd.Parameters.Add(new SqlParameter("@PasswordEncrypted", (object?)encrypted ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@CreatedById", (object?)entity.CreatedById ?? DBNull.Value));
            return (int)(await cmd.ExecuteScalarAsync(ct) ?? 0);
        }

        // Parola boş geldiyse kolona DOKUNULMAZ — istemci parolayı geri okumadığı için
        // "boş = değiştirme" semantiği zorunludur.
        cmd.CommandText = $"""
            UPDATE {_table}
            SET [Name] = @Name,
                [ServerName] = @ServerName,
                [DatabaseName] = @DatabaseName,
                [AuthMode] = @AuthMode,
                [Username] = @Username,
                {(hasNewPassword ? "[PasswordEncrypted] = @PasswordEncrypted," : "")}
                [Encrypt] = @Encrypt,
                [TrustServerCertificate] = @TrustServerCertificate,
                [ConnectTimeoutSeconds] = @ConnectTimeoutSeconds,
                [CommandTimeoutSeconds] = @CommandTimeoutSeconds,
                [UseHostServer] = @UseHostServer,
                [IsActive] = @IsActive,
                [UpdatedById] = @UpdatedById,
                [Updated] = SYSUTCDATETIME()
            WHERE [Id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", entity.Id));
        Bind(cmd, entity);
        if (hasNewPassword) cmd.Parameters.Add(new SqlParameter("@PasswordEncrypted", encrypted));
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

    private static void Bind(SqlCommand cmd, ExternalDbConnection e)
    {
        cmd.Parameters.Add(new SqlParameter("@Name", e.Name));
        cmd.Parameters.Add(new SqlParameter("@ServerName", e.ServerName));
        cmd.Parameters.Add(new SqlParameter("@DatabaseName", e.DatabaseName));
        cmd.Parameters.Add(new SqlParameter("@AuthMode", e.AuthMode));
        cmd.Parameters.Add(new SqlParameter("@Username", (object?)e.Username ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Encrypt", e.Encrypt));
        cmd.Parameters.Add(new SqlParameter("@TrustServerCertificate", e.TrustServerCertificate));
        cmd.Parameters.Add(new SqlParameter("@ConnectTimeoutSeconds", e.ConnectTimeoutSeconds));
        cmd.Parameters.Add(new SqlParameter("@CommandTimeoutSeconds", e.CommandTimeoutSeconds));
        cmd.Parameters.Add(new SqlParameter("@IsActive", e.IsActive));
        cmd.Parameters.Add(new SqlParameter("@UseHostServer", e.UseHostServer));
    }

    private static ExternalDbConnection Map(SqlDataReader r) => new()
    {
        Id                     = r.GetInt32(0),
        Name                   = r.GetString(1),
        ServerName             = r.GetString(2),
        DatabaseName           = r.GetString(3),
        AuthMode               = r.GetString(4),
        Username               = r.IsDBNull(5) ? null : r.GetString(5),
        PasswordEncrypted      = r.IsDBNull(6) ? null : r.GetString(6),
        Encrypt                = r.GetBoolean(7),
        TrustServerCertificate = r.GetBoolean(8),
        ConnectTimeoutSeconds  = r.GetInt32(9),
        CommandTimeoutSeconds  = r.GetInt32(10),
        IsActive               = r.GetBoolean(11),
        Created                = r.GetDateTime(12),
        Updated                = r.IsDBNull(13) ? null : r.GetDateTime(13),
        CreatedById            = r.IsDBNull(14) ? null : r.GetInt32(14),
        UpdatedById            = r.IsDBNull(15) ? null : r.GetInt32(15),
        UseHostServer          = r.GetBoolean(16),
    };
}
