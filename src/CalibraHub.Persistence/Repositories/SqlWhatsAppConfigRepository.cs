using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlWhatsAppConfigRepository : IWhatsAppConfigRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _table;

    public SqlWhatsAppConfigRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _table = $"[{schema}].[WhatsAppConfig]";
    }

    public async Task<WhatsAppConfig?> GetAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[Provider],[AccessTokenEncrypted],[PhoneNumberId],[BusinessAccountId],
                   [DisplayPhoneNumber],[WebhookVerifyToken],[WebQrBridgeUrl],[IsEnabled],
                   [LastSuccessfulSendAt],[LastError],[Created],[Updated],[app_secret_encrypted]
              FROM {_table}
             WHERE [Id] = 1;
            """;
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken)) return null;
        return new WhatsAppConfig
        {
            Id                    = r.GetInt32(0),
            Provider              = (WhatsAppProviderType)r.GetInt32(1),
            AccessTokenEncrypted  = r.IsDBNull(2) ? null : r.GetString(2),
            PhoneNumberId         = r.IsDBNull(3) ? null : r.GetString(3),
            BusinessAccountId     = r.IsDBNull(4) ? null : r.GetString(4),
            DisplayPhoneNumber    = r.IsDBNull(5) ? null : r.GetString(5),
            WebhookVerifyToken    = r.IsDBNull(6) ? null : r.GetString(6),
            WebQrBridgeUrl        = r.IsDBNull(7) ? null : r.GetString(7),
            IsEnabled             = r.GetBoolean(8),
            LastSuccessfulSendAt  = r.IsDBNull(9) ? null : r.GetDateTime(9),
            LastError             = r.IsDBNull(10) ? null : r.GetString(10),
            CreatedAt             = r.GetDateTime(11),
            UpdatedAt             = r.GetDateTime(12),
            AppSecretEncrypted    = r.IsDBNull(13) ? null : r.GetString(13),
        };
    }

    public async Task SaveAsync(WhatsAppConfig config, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            IF EXISTS (SELECT 1 FROM {_table} WHERE [Id] = 1)
            BEGIN
                UPDATE {_table}
                   SET [Provider]                 = @Provider,
                       [AccessTokenEncrypted]     = @Token,
                       [app_secret_encrypted]     = @AppSecret,
                       [PhoneNumberId]            = @PhoneNumberId,
                       [BusinessAccountId]        = @BusinessAccountId,
                       [DisplayPhoneNumber]       = @DisplayPhoneNumber,
                       [WebhookVerifyToken]       = @WebhookToken,
                       [WebQrBridgeUrl]           = @BridgeUrl,
                       [IsEnabled]               = @IsEnabled,
                       [LastSuccessfulSendAt]     = @LastSuccessfulSendAt,
                       [LastError]               = @LastError,
                       [Updated]               = GETUTCDATE()
                 WHERE [Id] = 1;
            END
            ELSE
            BEGIN
                INSERT INTO {_table}
                    ([Id],[Provider],[AccessTokenEncrypted],[app_secret_encrypted],[PhoneNumberId],[BusinessAccountId],
                     [DisplayPhoneNumber],[WebhookVerifyToken],[WebQrBridgeUrl],[IsEnabled],
                     [LastSuccessfulSendAt],[LastError],[Created],[Updated])
                VALUES
                    (1,@Provider,@Token,@AppSecret,@PhoneNumberId,@BusinessAccountId,@DisplayPhoneNumber,@WebhookToken,@BridgeUrl,@IsEnabled,
                     @LastSuccessfulSendAt,@LastError,GETUTCDATE(),GETUTCDATE());
            END;
            """;
        cmd.Parameters.Add(new SqlParameter("@Provider",              (int)config.Provider));
        cmd.Parameters.Add(new SqlParameter("@Token",                 (object?)config.AccessTokenEncrypted ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@AppSecret",             (object?)config.AppSecretEncrypted   ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@PhoneNumberId",         (object?)config.PhoneNumberId        ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@BusinessAccountId",     (object?)config.BusinessAccountId    ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@DisplayPhoneNumber",    (object?)config.DisplayPhoneNumber   ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@WebhookToken",          (object?)config.WebhookVerifyToken   ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@BridgeUrl",             (object?)config.WebQrBridgeUrl       ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@IsEnabled",             config.IsEnabled));
        cmd.Parameters.Add(new SqlParameter("@LastSuccessfulSendAt",  (object?)config.LastSuccessfulSendAt ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@LastError",             (object?)config.LastError            ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
