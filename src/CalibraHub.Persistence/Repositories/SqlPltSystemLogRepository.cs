using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlPltSystemLogRepository : IIntegratorImportLogRepository
{
    private const int DefaultApplicationId = 0;
    private const int DefaultModuleNo = 100;
    private const string SystemUserName = "SYSTEM";
    private const string SqlSourceName = "PLT_SISTEM_LOG";

    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _tableName;
    private readonly string _companyTableName;
    private readonly string _databaseName;

    public SqlPltSystemLogRepository(
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;

        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _tableName = $"[{schema}].[PLT_SISTEM_LOG]";
        _companyTableName = $"[{schema}].[company]";

        try
        {
            var builder = new SqlConnectionStringBuilder(options.ConnectionString);
            _databaseName = string.IsNullOrWhiteSpace(builder.InitialCatalog)
                ? string.Empty
                : builder.InitialCatalog.Trim();
        }
        catch (ArgumentException)
        {
            _databaseName = string.Empty;
        }
    }

    public async Task WriteAsync(IntegratorImportLogWriteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var occurredAt = request.OccurredAt ?? DateTime.Now;
        var level = NormalizeText(request.Level, 50, "Info");
        var integratorName = NormalizeText(request.IntegratorName, 100, "Calibra");
        var sourceId = request.IntegratorSettingsId.ToString();
        var message = NormalizeText(request.Message, 4000, "Log kaydi");
        var description = NormalizeText(message, 200, "Log kaydi");
        var programNo = ResolveProgramNo(integratorName);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {_tableName}
                ([VERITABANI], [UYGULAMA_ID], [ACIKLAMA], [MODUL_NO], [PROGRAM_NO], [KAYIT_NO], [BELGE_TURU], [TARIH], [S_SAHA_01], [S_SAHA_02], [S_SAHA_03], [I_SAHA_01], [I_SAHA_02], [N_SAHA_01], [COMPANY_ID], [KAYITYAPANKUL], [KAYITTARIHI])
            VALUES
                (@Veritabani, @UygulamaId, @Aciklama, @ModulNo, @ProgramNo, @KayitNo, @BelgeTuru, @Tarih, @SSaha01, @SSaha02, @SSaha03, @ISaha01, @ISaha02, @NSaha01, @CompanyId, @KayitYapanKul, @KayitTarihi);
            """;

        command.Parameters.Add(CreateParameter("@Veritabani", ToDbValue(_databaseName)));
        command.Parameters.Add(CreateParameter("@UygulamaId", DefaultApplicationId));
        command.Parameters.Add(CreateParameter("@Aciklama", ToDbValue(description)));
        command.Parameters.Add(CreateParameter("@ModulNo", DefaultModuleNo));
        command.Parameters.Add(CreateParameter("@ProgramNo", programNo));
        command.Parameters.Add(CreateParameter("@KayitNo", request.ImportedCount));
        command.Parameters.Add(CreateParameter("@BelgeTuru", ToDbValue(level)));
        command.Parameters.Add(CreateParameter("@Tarih", occurredAt));
        command.Parameters.Add(CreateParameter("@SSaha01", ToDbValue(sourceId)));
        command.Parameters.Add(CreateParameter("@SSaha02", ToDbValue(integratorName)));
        command.Parameters.Add(CreateParameter("@SSaha03", ToDbValue(level)));
        command.Parameters.Add(CreateParameter("@ISaha01", request.ImportedCount));
        command.Parameters.Add(CreateParameter("@ISaha02", request.SkippedCount));
        command.Parameters.Add(CreateParameter("@NSaha01", ToDbValue(message)));
        command.Parameters.Add(CreateParameter("@CompanyId", request.CompanyId.HasValue ? request.CompanyId.Value : DBNull.Value));
        command.Parameters.Add(CreateParameter("@KayitYapanKul", SystemUserName));
        command.Parameters.Add(CreateParameter("@KayitTarihi", occurredAt));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<IntegratorImportLogEntryDto>> GetRecentAsync(
        int take,
        CancellationToken cancellationToken)
    {
        if (take <= 0)
        {
            return Array.Empty<IntegratorImportLogEntryDto>();
        }

        var effectiveTake = Math.Clamp(take, 1, 1000);
        var entries = new List<IntegratorImportLogEntryDto>(effectiveTake);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT TOP (@Take)
                log.[ID],
                log.[TARIH],
                log.[KAYITTARIHI],
                log.[S_SAHA_01],
                log.[S_SAHA_02],
                log.[S_SAHA_03],
                log.[BELGE_TURU],
                log.[N_SAHA_01],
                log.[ACIKLAMA],
                log.[I_SAHA_01],
                log.[I_SAHA_02],
                log.[COMPANY_ID],
                company.[name]
            FROM {_tableName} AS log
            LEFT JOIN {_companyTableName} AS company
                ON company.[id] = log.[COMPANY_ID]
            ORDER BY ISNULL(log.[KAYITTARIHI], log.[TARIH]) DESC, log.[ID] DESC;
            """;
        command.Parameters.Add(CreateParameter("@Take", effectiveTake));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var occurredAt = ReadOccurredAt(reader);
            var sourceIdRaw = ReadNullableString(reader, 3);
            var integratorName = ReadNullableString(reader, 4);
            var level = ReadNullableString(reader, 5);
            var belgeTuru = ReadNullableString(reader, 6);
            var longMessage = ReadNullableString(reader, 7);
            var shortMessage = ReadNullableString(reader, 8);
            var importedCount = ReadInt32(reader, 9);
            var skippedCount = ReadInt32(reader, 10);
            var companyId = reader.IsDBNull(11) ? (int?)null : reader.GetInt32(11);
            var companyName = ReadNullableString(reader, 12);

            entries.Add(
                new IntegratorImportLogEntryDto(
                    occurredAt,
                    int.TryParse(sourceIdRaw, out var srcId) ? srcId : 0,
                    companyId,
                    string.IsNullOrWhiteSpace(companyName) ? "-" : companyName!,
                    string.IsNullOrWhiteSpace(integratorName) ? "Calibra" : integratorName!,
                    FirstNonEmpty(level, belgeTuru, "Info"),
                    FirstNonEmpty(longMessage, shortMessage, "Log kaydi"),
                    importedCount,
                    skippedCount,
                    SqlSourceName));
        }

        return entries;
    }

    public async Task CleanupExpiredAsync(
        int integratorSettingsId,
        int retentionDays,
        CancellationToken cancellationToken)
    {
        var effectiveRetentionDays = Math.Clamp(retentionDays, 1, 3650);
        var cutoff = DateTime.Now.AddDays(-effectiveRetentionDays);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            DELETE FROM {_tableName}
            WHERE [S_SAHA_01] = @SourceId
              AND ISNULL([KAYITTARIHI], [TARIH]) < @Cutoff;
            """;
        command.Parameters.Add(CreateParameter("@SourceId", integratorSettingsId.ToString()));
        command.Parameters.Add(CreateParameter("@Cutoff", cutoff));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static object ToDbValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static SqlParameter CreateParameter(string name, object? value) =>
        new()
        {
            ParameterName = name,
            Value = value ?? DBNull.Value
        };

    private static string NormalizeText(string? value, int maxLength, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return normalized.Length > maxLength ? normalized[..maxLength] : normalized;
    }

    private static DateTime ReadOccurredAt(SqlDataReader reader)
    {
        var baseDate = reader.IsDBNull(2)
            ? (reader.IsDBNull(1) ? DateTime.Now : reader.GetDateTime(1))
            : reader.GetDateTime(2);

        return baseDate;
    }

    private static string? ReadNullableString(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static int ReadInt32(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0 : reader.GetInt32(ordinal);

    private static Guid ParseGuidOrDefault(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Guid.Empty;
        }

        if (Guid.TryParse(raw, out var parsed))
        {
            return parsed;
        }

        if (raw.Length == 32 && Guid.TryParseExact(raw, "N", out parsed))
        {
            return parsed;
        }

        return Guid.Empty;
    }

    private static string FirstNonEmpty(string? first, string? second, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(first))
        {
            return first.Trim();
        }

        if (!string.IsNullOrWhiteSpace(second))
        {
            return second.Trim();
        }

        return fallback;
    }

    private static int ResolveProgramNo(string integratorName)
    {
        if (integratorName.Contains("Entegrator Baglanti", StringComparison.OrdinalIgnoreCase))
        {
            return 10;
        }

        if (integratorName.Contains("SMTP Baglanti", StringComparison.OrdinalIgnoreCase))
        {
            return 20;
        }

        if (integratorName.Contains("SQL Baglanti", StringComparison.OrdinalIgnoreCase))
        {
            return 30;
        }

        return 1;
    }
}
