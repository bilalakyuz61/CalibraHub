using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using CalibraHub.Persistence.Security;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlIntegratorSettingsRepository : IIntegratorSettingsRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _tableName;

    public SqlIntegratorSettingsRepository(
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _tableName = $"[{schema}].[integrator_settings]";
    }

    public async Task<IReadOnlyCollection<IntegratorSettings>> GetAllAsync(CancellationToken cancellationToken)
    {
        var result = new List<IntegratorSettings>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [id], [company_id], [provider], [name], [base_url], [company_tax_number], [username], [secret], [polling_interval_seconds], [max_records_per_pull], [log_retention_days], [include_received_documents_in_pull], [mark_downloaded_documents_as_received], [include_issued_einvoice_in_pull], [include_issued_earchive_in_pull], [include_issued_edispatch_in_pull], [is_active], [created_at], [app_str], [source], [app_version], [schedule_enabled], [timeout_seconds], [lookback_days]
            FROM {_tableName}
            ORDER BY [name];
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(MapSettings(reader));
        }

        return result;
    }

    public async Task<IReadOnlyCollection<IntegratorSettings>> GetActiveAsync(CancellationToken cancellationToken)
    {
        var result = new List<IntegratorSettings>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [id], [company_id], [provider], [name], [base_url], [company_tax_number], [username], [secret], [polling_interval_seconds], [max_records_per_pull], [log_retention_days], [include_received_documents_in_pull], [mark_downloaded_documents_as_received], [include_issued_einvoice_in_pull], [include_issued_earchive_in_pull], [include_issued_edispatch_in_pull], [is_active], [created_at], [app_str], [source], [app_version], [schedule_enabled], [timeout_seconds], [lookback_days]
            FROM {_tableName}
            WHERE [is_active] = 1
            ORDER BY [name];
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(MapSettings(reader));
        }

        return result;
    }

    public async Task<IntegratorSettings?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [id], [company_id], [provider], [name], [base_url], [company_tax_number], [username], [secret], [polling_interval_seconds], [max_records_per_pull], [log_retention_days], [include_received_documents_in_pull], [mark_downloaded_documents_as_received], [include_issued_einvoice_in_pull], [include_issued_earchive_in_pull], [include_issued_edispatch_in_pull], [is_active], [created_at], [app_str], [source], [app_version], [schedule_enabled], [timeout_seconds], [lookback_days]
            FROM {_tableName}
            WHERE [id] = @Id;
            """;
        command.Parameters.Add(new SqlParameter("@Id", id));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return MapSettings(reader);
    }

    public async Task<IntegratorSettings?> GetByCompanyIdAsync(int companyId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [id], [company_id], [provider], [name], [base_url], [company_tax_number], [username], [secret], [polling_interval_seconds], [max_records_per_pull], [log_retention_days], [include_received_documents_in_pull], [mark_downloaded_documents_as_received], [include_issued_einvoice_in_pull], [include_issued_earchive_in_pull], [include_issued_edispatch_in_pull], [is_active], [created_at], [app_str], [source], [app_version], [schedule_enabled], [timeout_seconds], [lookback_days]
            FROM {_tableName}
            WHERE [company_id] = @CompanyId;
            """;
        command.Parameters.Add(new SqlParameter("@CompanyId", companyId));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return MapSettings(reader);
    }

    public async Task<int> AddAsync(IntegratorSettings settings, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {_tableName}
                ([company_id], [provider], [name], [base_url], [company_tax_number], [username], [secret], [polling_interval_seconds], [max_records_per_pull], [log_retention_days], [include_received_documents_in_pull], [mark_downloaded_documents_as_received], [include_issued_einvoice_in_pull], [include_issued_earchive_in_pull], [include_issued_edispatch_in_pull], [is_active], [schedule_enabled], [created_at], [updated_at], [app_str], [source], [app_version], [timeout_seconds], [lookback_days])
            VALUES
                (@EntityCompanyId, @Provider, @Name, @BaseUrl, @CompanyTaxNumber, @Username, @Secret, @PollingIntervalSeconds, @MaxRecordsPerPull, @LogRetentionDays, @IncludeReceivedDocumentsInPull, @MarkDownloadedDocumentsAsReceived, @IncludeIssuedEInvoiceInPull, @IncludeIssuedEArchiveInPull, @IncludeIssuedEDispatchInPull, @IsActive, @ScheduleEnabled, @CreatedAt, @UpdatedAt, @AppStr, @Source, @AppVersion, @TimeoutSeconds, @LookbackDays);
            SELECT SCOPE_IDENTITY();
            """;

        AddCommonParameters(command, settings);
        command.Parameters.Add(new SqlParameter("@CreatedAt", settings.CreatedAt));
        command.Parameters.Add(new SqlParameter("@UpdatedAt", DateTime.Now));

        var scalar = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(scalar);
    }

    public async Task UpdateAsync(IntegratorSettings settings, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_tableName}
            SET [company_id] = @EntityCompanyId,
                [provider] = @Provider,
                [name] = @Name,
                [base_url] = @BaseUrl,
                [company_tax_number] = @CompanyTaxNumber,
                [username] = @Username,
                [secret] = CASE WHEN LEN(@Secret) > 0 THEN @Secret ELSE [secret] END,
                [polling_interval_seconds] = @PollingIntervalSeconds,
                [max_records_per_pull] = @MaxRecordsPerPull,
                [log_retention_days] = @LogRetentionDays,
                [include_received_documents_in_pull] = @IncludeReceivedDocumentsInPull,
                [mark_downloaded_documents_as_received] = @MarkDownloadedDocumentsAsReceived,
                [include_issued_einvoice_in_pull] = @IncludeIssuedEInvoiceInPull,
                [include_issued_earchive_in_pull] = @IncludeIssuedEArchiveInPull,
                [include_issued_edispatch_in_pull] = @IncludeIssuedEDispatchInPull,
                [is_active] = @IsActive,
                [schedule_enabled] = @ScheduleEnabled,
                [updated_at] = @UpdatedAt,
                [app_str] = @AppStr,
                [source] = @Source,
                [app_version] = @AppVersion,
                [timeout_seconds] = @TimeoutSeconds,
                [lookback_days] = @LookbackDays
            WHERE [id] = @Id;
            """;

        AddCommonParameters(command, settings);
        command.Parameters.Add(new SqlParameter("@Id", settings.Id));
        command.Parameters.Add(new SqlParameter("@UpdatedAt", DateTime.Now));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            DELETE FROM {_tableName}
            WHERE [id] = @Id;
            """;

        command.Parameters.Add(new SqlParameter("@Id", id));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddCommonParameters(SqlCommand command, IntegratorSettings settings)
    {
        command.Parameters.Add(new SqlParameter("@EntityCompanyId", settings.CompanyId));
        command.Parameters.Add(new SqlParameter("@Provider", settings.Provider.ToString()));
        command.Parameters.Add(new SqlParameter("@Name", settings.Name));
        command.Parameters.Add(new SqlParameter("@BaseUrl", settings.BaseUrl));
        command.Parameters.Add(new SqlParameter("@CompanyTaxNumber", settings.CompanyTaxNumber));
        command.Parameters.Add(new SqlParameter("@Username", settings.Username));
        command.Parameters.Add(new SqlParameter("@Secret", IntegratorSecretProtector.Protect(settings.Secret)));
        command.Parameters.Add(new SqlParameter("@PollingIntervalSeconds", settings.PollingIntervalSeconds));
        command.Parameters.Add(new SqlParameter("@MaxRecordsPerPull", settings.MaxRecordsPerPull));
        command.Parameters.Add(new SqlParameter("@LogRetentionDays", settings.LogRetentionDays));
        command.Parameters.Add(new SqlParameter("@IncludeReceivedDocumentsInPull", settings.IncludeReceivedDocumentsInPull));
        command.Parameters.Add(new SqlParameter("@MarkDownloadedDocumentsAsReceived", settings.MarkDownloadedDocumentsAsReceived));
        command.Parameters.Add(new SqlParameter("@IncludeIssuedEInvoiceInPull", settings.IncludeIssuedEInvoicesInPull));
        command.Parameters.Add(new SqlParameter("@IncludeIssuedEArchiveInPull", settings.IncludeIssuedEArchivesInPull));
        command.Parameters.Add(new SqlParameter("@IncludeIssuedEDispatchInPull", settings.IncludeIssuedEDispatchesInPull));
        command.Parameters.Add(new SqlParameter("@IsActive", settings.IsActive));
        command.Parameters.Add(new SqlParameter("@ScheduleEnabled", settings.ScheduleEnabled));
        command.Parameters.Add(new SqlParameter("@AppStr", (object?)settings.AppStr ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@Source", (object?)settings.Source ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@AppVersion", (object?)settings.AppVersion ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@TimeoutSeconds", settings.TimeoutSeconds));
        command.Parameters.Add(new SqlParameter("@LookbackDays", settings.LookbackDays));
    }

    private static IntegratorSettings MapSettings(SqlDataReader reader)
    {
        var providerValue = reader.GetString(2);
        if (string.Equals(providerValue, "Foriba", StringComparison.OrdinalIgnoreCase))
        {
            providerValue = IntegratorProvider.Logo.ToString();
        }

        if (!Enum.TryParse(providerValue, true, out IntegratorProvider provider) ||
            !Enum.IsDefined(provider))
        {
            provider = IntegratorProvider.Unknown;
        }

        var settings = new IntegratorSettings
        {
            Id = reader.GetInt32(0),
            CompanyId = reader.GetInt32(1),
            Provider = provider,
            Name = reader.GetString(3),
            BaseUrl = reader.GetString(4),
            CompanyTaxNumber = reader.GetString(5),
            Username = reader.GetString(6),
            Secret = IntegratorSecretProtector.Unprotect(reader.GetString(7)),
            CreatedAt = reader.GetFieldValue<DateTime>(17),
            AppStr = reader.IsDBNull(18) ? null : reader.GetString(18),
            Source = reader.IsDBNull(19) ? null : reader.GetString(19),
            AppVersion = reader.IsDBNull(20) ? null : reader.GetString(20)
        };

        settings.UpdatePollingInterval(reader.GetInt32(8));
        settings.UpdateMaxRecordsPerPull(reader.GetInt32(9));
        settings.UpdateLogRetentionDays(reader.GetInt32(10));
        settings.ConfigureIncludeReceivedDocumentsInPull(reader.GetBoolean(11));
        settings.ConfigureDownloadedDocumentReceipt(reader.GetBoolean(12));
        settings.ConfigureIssuedDocumentPull(
            reader.GetBoolean(13),
            reader.GetBoolean(14),
            reader.GetBoolean(15));
        if (!reader.GetBoolean(16))
        {
            settings.Deactivate();
        }
        settings.ConfigureScheduleEnabled(reader.GetBoolean(reader.GetOrdinal("schedule_enabled")));
        settings.UpdateTimeoutSeconds(reader.GetInt32(reader.GetOrdinal("timeout_seconds")));
        settings.UpdateLookbackDays(reader.GetInt32(reader.GetOrdinal("lookback_days")));

        return settings;
    }
}
