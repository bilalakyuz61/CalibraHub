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
        _tableName = $"[{schema}].[IntegratorSetting]";
    }

    public async Task<IReadOnlyCollection<IntegratorSettings>> GetAllAsync(CancellationToken cancellationToken)
    {
        var result = new List<IntegratorSettings>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [Id], [CompanyId], [Provider], [Name], [BaseUrl], [CompanyTaxNumber], [Username], [Secret], [PollingIntervalSeconds], [MaxRecordsPerPull], [LogRetentionDays], [IncludeReceivedDocumentsInPull], [MarkDownloadedDocumentsAsReceived], [IncludeIssuedEInvoiceInPull], [IncludeIssuedEArchiveInPull], [IncludeIssuedEDispatchInPull], [IsActive], [Created], [AppStr], [Source], [AppVersion], [ScheduleEnabled], [TimeoutSeconds], [LookbackDays]
            FROM {_tableName}
            ORDER BY [Name];
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
            SELECT [Id], [CompanyId], [Provider], [Name], [BaseUrl], [CompanyTaxNumber], [Username], [Secret], [PollingIntervalSeconds], [MaxRecordsPerPull], [LogRetentionDays], [IncludeReceivedDocumentsInPull], [MarkDownloadedDocumentsAsReceived], [IncludeIssuedEInvoiceInPull], [IncludeIssuedEArchiveInPull], [IncludeIssuedEDispatchInPull], [IsActive], [Created], [AppStr], [Source], [AppVersion], [ScheduleEnabled], [TimeoutSeconds], [LookbackDays]
            FROM {_tableName}
            WHERE [IsActive] = 1
            ORDER BY [Name];
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
            SELECT [Id], [CompanyId], [Provider], [Name], [BaseUrl], [CompanyTaxNumber], [Username], [Secret], [PollingIntervalSeconds], [MaxRecordsPerPull], [LogRetentionDays], [IncludeReceivedDocumentsInPull], [MarkDownloadedDocumentsAsReceived], [IncludeIssuedEInvoiceInPull], [IncludeIssuedEArchiveInPull], [IncludeIssuedEDispatchInPull], [IsActive], [Created], [AppStr], [Source], [AppVersion], [ScheduleEnabled], [TimeoutSeconds], [LookbackDays]
            FROM {_tableName}
            WHERE [Id] = @Id;
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
            SELECT [Id], [CompanyId], [Provider], [Name], [BaseUrl], [CompanyTaxNumber], [Username], [Secret], [PollingIntervalSeconds], [MaxRecordsPerPull], [LogRetentionDays], [IncludeReceivedDocumentsInPull], [MarkDownloadedDocumentsAsReceived], [IncludeIssuedEInvoiceInPull], [IncludeIssuedEArchiveInPull], [IncludeIssuedEDispatchInPull], [IsActive], [Created], [AppStr], [Source], [AppVersion], [ScheduleEnabled], [TimeoutSeconds], [LookbackDays]
            FROM {_tableName}
            WHERE [CompanyId] = @CompanyId;
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
                ([CompanyId], [Provider], [Name], [BaseUrl], [CompanyTaxNumber], [Username], [Secret], [PollingIntervalSeconds], [MaxRecordsPerPull], [LogRetentionDays], [IncludeReceivedDocumentsInPull], [MarkDownloadedDocumentsAsReceived], [IncludeIssuedEInvoiceInPull], [IncludeIssuedEArchiveInPull], [IncludeIssuedEDispatchInPull], [IsActive], [ScheduleEnabled], [Created], [Updated], [AppStr], [Source], [AppVersion], [TimeoutSeconds], [LookbackDays])
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
            SET [CompanyId] = @EntityCompanyId,
                [Provider] = @Provider,
                [Name] = @Name,
                [BaseUrl] = @BaseUrl,
                [CompanyTaxNumber] = @CompanyTaxNumber,
                [Username] = @Username,
                [Secret] = CASE WHEN LEN(@Secret) > 0 THEN @Secret ELSE [Secret] END,
                [PollingIntervalSeconds] = @PollingIntervalSeconds,
                [MaxRecordsPerPull] = @MaxRecordsPerPull,
                [LogRetentionDays] = @LogRetentionDays,
                [IncludeReceivedDocumentsInPull] = @IncludeReceivedDocumentsInPull,
                [MarkDownloadedDocumentsAsReceived] = @MarkDownloadedDocumentsAsReceived,
                [IncludeIssuedEInvoiceInPull] = @IncludeIssuedEInvoiceInPull,
                [IncludeIssuedEArchiveInPull] = @IncludeIssuedEArchiveInPull,
                [IncludeIssuedEDispatchInPull] = @IncludeIssuedEDispatchInPull,
                [IsActive] = @IsActive,
                [ScheduleEnabled] = @ScheduleEnabled,
                [Updated] = @UpdatedAt,
                [AppStr] = @AppStr,
                [Source] = @Source,
                [AppVersion] = @AppVersion,
                [TimeoutSeconds] = @TimeoutSeconds,
                [LookbackDays] = @LookbackDays
            WHERE [Id] = @Id;
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
            WHERE [Id] = @Id;
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
        settings.ConfigureScheduleEnabled(reader.GetBoolean(reader.GetOrdinal("ScheduleEnabled")));
        settings.UpdateTimeoutSeconds(reader.GetInt32(reader.GetOrdinal("TimeoutSeconds")));
        settings.UpdateLookbackDays(reader.GetInt32(reader.GetOrdinal("LookbackDays")));

        return settings;
    }
}
