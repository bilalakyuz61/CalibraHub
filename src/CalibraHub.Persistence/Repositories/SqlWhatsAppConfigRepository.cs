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
        _table = $"[{schema}].[whatsapp_config]";
    }

    public async Task<WhatsAppConfig?> GetAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT [id],[provider],[access_token_encrypted],[phone_number_id],[business_account_id],
                   [display_phone_number],[webhook_verify_token],[web_qr_bridge_url],[is_enabled],
                   [last_successful_send_at],[last_error],[Created],[Updated]
              FROM {_table}
             WHERE [id] = 1;
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
        };
    }

    public async Task SaveAsync(WhatsAppConfig config, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            IF EXISTS (SELECT 1 FROM {_table} WHERE [id] = 1)
            BEGIN
                UPDATE {_table}
                   SET [provider]                 = @Provider,
                       [access_token_encrypted]   = @Token,
                       [phone_number_id]          = @PhoneNumberId,
                       [business_account_id]      = @BusinessAccountId,
                       [display_phone_number]     = @DisplayPhoneNumber,
                       [webhook_verify_token]     = @WebhookToken,
                       [web_qr_bridge_url]        = @BridgeUrl,
                       [is_enabled]               = @IsEnabled,
                       [last_successful_send_at]  = @LastSuccessfulSendAt,
                       [last_error]               = @LastError,
                       [Updated]               = GETUTCDATE()
                 WHERE [id] = 1;
            END
            ELSE
            BEGIN
                INSERT INTO {_table}
                    ([id],[provider],[access_token_encrypted],[phone_number_id],[business_account_id],
                     [display_phone_number],[webhook_verify_token],[web_qr_bridge_url],[is_enabled],
                     [last_successful_send_at],[last_error],[Created],[Updated])
                VALUES
                    (1,@Provider,@Token,@PhoneNumberId,@BusinessAccountId,@DisplayPhoneNumber,@WebhookToken,@BridgeUrl,@IsEnabled,
                     @LastSuccessfulSendAt,@LastError,GETUTCDATE(),GETUTCDATE());
            END;
            """;
        cmd.Parameters.Add(new SqlParameter("@Provider",              (int)config.Provider));
        cmd.Parameters.Add(new SqlParameter("@Token",                 (object?)config.AccessTokenEncrypted ?? DBNull.Value));
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
