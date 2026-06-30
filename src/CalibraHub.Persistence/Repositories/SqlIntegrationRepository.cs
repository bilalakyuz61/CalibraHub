using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// Integration aggregate ve ilişkili tablolar için SQL repository.
/// Per-company DB üzerinde çalışır (SqlServerConnectionFactory.OpenConnectionAsync
/// HttpContext'ten company_id claim'ini cözer).
///
/// CalibraHub raw ADO.NET pattern'iyle yazıldı (Dapper paketi var ama codebase'de
/// kullanılmıyor — tutarlılık için ADO.NET tercih edildi).
/// </summary>
public sealed class SqlIntegrationRepository : IIntegrationRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _integrationTable;
    private readonly string _mappingTable;
    private readonly string _triggerTable;
    private readonly string _runTable;
    private readonly string _endpointTable;

    public SqlIntegrationRepository(
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _integrationTable = $"[{schema}].[Integration]";
        _mappingTable     = $"[{schema}].[IntegrationMapping]";
        _triggerTable     = $"[{schema}].[IntegrationTrigger]";
        _runTable         = $"[{schema}].[IntegrationRun]";
        _endpointTable    = $"[{schema}].[IntegrationEndpoint]";
    }

    // ── Integration (aggregate root) ─────────────────────────────────────

    public async Task<IReadOnlyCollection<Integration>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var list = new List<Integration>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[Name],[Description],[SourceFormCode],[TargetEndpointId],
                   [ErrorBehavior],[RetryCount],[IsActive],[VersionNo],
                   [CreatedById],[Created],[UpdatedById],[Updated],
                   [PreProcedureName],[PreProcedureParamsJson],
                   [PostProcedureName],[PostProcedureParamsJson],
                   [SourceFilterJson],[AllowAsCascadeTarget]
            FROM {_integrationTable}
            {(includeInactive ? "" : "WHERE [IsActive] = 1")}
            ORDER BY [Name];
            """;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(MapIntegration(reader));
        return list;
    }

    public async Task<Integration?> GetByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);

        Integration? integration = null;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT [Id],[Name],[Description],[SourceFormCode],[TargetEndpointId],
                       [ErrorBehavior],[RetryCount],[IsActive],[VersionNo],
                       [CreatedById],[Created],[UpdatedById],[Updated],
                       [PreProcedureName],[PreProcedureParamsJson],
                       [PostProcedureName],[PostProcedureParamsJson],
                       [SourceFilterJson]
                FROM {_integrationTable}
                WHERE [Id] = @Id;
                """;
            cmd.Parameters.Add(new SqlParameter("@Id", id));
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct)) integration = MapIntegration(reader);
        }

        if (integration is null) return null;

        // Aggregate children — ayrı sorgular
        integration.Mappings = (await GetMappingsAsync(id, ct)).ToList();
        integration.Triggers = (await GetTriggersAsync(id, ct)).ToList();
        // Faz O — Sadece-Prosedur modunda TargetEndpointId NULL olabilir; endpoint cekme yok.
        integration.Endpoint = integration.TargetEndpointId.HasValue
            ? await GetEndpointByIdAsync(integration.TargetEndpointId.Value, ct)
            : null;
        return integration;
    }

    public async Task<IReadOnlyCollection<Integration>> ListByFormCodeAsync(string formCode, IntegrationTriggerType triggerType, CancellationToken ct)
    {
        var list = new List<Integration>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT i.[Id],i.[Name],i.[Description],i.[SourceFormCode],i.[TargetEndpointId],
                   i.[ErrorBehavior],i.[RetryCount],i.[IsActive],i.[VersionNo],
                   i.[CreatedById],i.[Created],i.[UpdatedById],i.[Updated],
                   i.[PreProcedureName],i.[PreProcedureParamsJson],
                   i.[PostProcedureName],i.[PostProcedureParamsJson],
                   i.[SourceFilterJson],i.[AllowAsCascadeTarget]
            FROM {_integrationTable} i
            INNER JOIN {_triggerTable} t ON t.[IntegrationId] = i.[Id]
            WHERE i.[SourceFormCode] = @FormCode
              AND i.[IsActive] = 1
              AND t.[IsActive] = 1
              AND t.[TriggerType] = @TriggerType
            ORDER BY i.[Name];
            """;
        cmd.Parameters.Add(new SqlParameter("@FormCode", formCode));
        cmd.Parameters.Add(new SqlParameter("@TriggerType", triggerType.ToString()));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(MapIntegration(reader));
        return list;
    }

    public Task<IReadOnlyCollection<IntegrationManualButtonInfo>> ListManualButtonsAsync(
        string formCode, CancellationToken ct)
        => ListManualButtonsCoreAsync(formCode, ct);

    public Task<IReadOnlyCollection<IntegrationManualButtonInfo>> ListAllManualButtonsAsync(
        CancellationToken ct)
        => ListManualButtonsCoreAsync(null, ct);

    private async Task<IReadOnlyCollection<IntegrationManualButtonInfo>> ListManualButtonsCoreAsync(
        string? formCode, CancellationToken ct)
    {
        var list = new List<IntegrationManualButtonInfo>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        var whereFormCode = formCode is not null ? "AND i.[SourceFormCode] = @FormCode" : string.Empty;
        cmd.CommandText = $"""
            SELECT i.[Id], i.[Name], i.[Description], i.[TargetEndpointId], i.[SourceFormCode], t.[Config]
            FROM {_integrationTable} i
            INNER JOIN {_triggerTable} t ON t.[IntegrationId] = i.[Id]
            WHERE i.[IsActive] = 1
              AND t.[IsActive] = 1
              AND t.[TriggerType] = 'Manual'
              {whereFormCode}
            ORDER BY i.[Name];
            """;
        if (formCode is not null)
            cmd.Parameters.Add(new SqlParameter("@FormCode", formCode));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var config = r.IsDBNull(5) ? null : r.GetString(5);
            string? buttonLabel = null, buttonColor = null;
            if (config is not null)
                try {
                    var doc = System.Text.Json.JsonDocument.Parse(config);
                    if (doc.RootElement.TryGetProperty("buttonLabel", out var lbl)) buttonLabel = lbl.GetString();
                    if (doc.RootElement.TryGetProperty("color", out var col)) buttonColor = col.GetString();
                } catch { /* malformed JSON — skip */ }
            list.Add(new IntegrationManualButtonInfo(
                Id: r.GetInt32(0),
                Name: r.GetString(1),
                Description: r.IsDBNull(2) ? null : r.GetString(2),
                ButtonLabel: buttonLabel,
                ButtonColor: buttonColor,
                TargetEndpointId: r.IsDBNull(3) ? null : r.GetInt32(3),
                SourceFormCode: r.GetString(4)));
        }
        return list;
    }

    public async Task<IReadOnlyCollection<Integration>> ListByTriggerTypeAsync(
        IntegrationTriggerType triggerType, CancellationToken ct)
    {
        var list = new List<Integration>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT DISTINCT i.[Id],i.[Name],i.[Description],i.[SourceFormCode],i.[TargetEndpointId],
                   i.[ErrorBehavior],i.[RetryCount],i.[IsActive],i.[VersionNo],
                   i.[CreatedById],i.[Created],i.[UpdatedById],i.[Updated],
                   i.[PreProcedureName],i.[PreProcedureParamsJson],
                   i.[PostProcedureName],i.[PostProcedureParamsJson],
                   i.[SourceFilterJson],i.[AllowAsCascadeTarget]
            FROM {_integrationTable} i
            INNER JOIN {_triggerTable} t ON t.[IntegrationId] = i.[Id]
            WHERE i.[IsActive] = 1
              AND t.[IsActive] = 1
              AND t.[TriggerType] = @TriggerType
            ORDER BY i.[Name];
            """;
        cmd.Parameters.Add(new SqlParameter("@TriggerType", triggerType.ToString()));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(MapIntegration(reader));
        return list;
    }

    public async Task<int> AddAsync(Integration integration, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {_integrationTable}
              ([Name],[Description],[SourceFormCode],[TargetEndpointId],
               [ErrorBehavior],[RetryCount],[IsActive],[VersionNo],[CreatedById],
               [PreProcedureName],[PreProcedureParamsJson],
               [PostProcedureName],[PostProcedureParamsJson],
               [SourceFilterJson],[AllowAsCascadeTarget],[SourceCodeColumn])
            OUTPUT INSERTED.[Id]
            VALUES
              (@Name,@Description,@SourceFormCode,@TargetEndpointId,
               @ErrorBehavior,@RetryCount,@IsActive,@VersionNo,@CreatedById,
               @PreProcedureName,@PreProcedureParamsJson,
               @PostProcedureName,@PostProcedureParamsJson,
               @SourceFilterJson,@AllowAsCascadeTarget,@SourceCodeColumn);
            """;
        AddIntegrationParameters(cmd, integration);
        var newId = (int)(await cmd.ExecuteScalarAsync(ct) ?? 0);
        return newId;
    }

    public async Task UpdateAsync(Integration integration, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_integrationTable}
            SET [Name] = @Name,
                [Description] = @Description,
                [SourceFormCode] = @SourceFormCode,
                [TargetEndpointId] = @TargetEndpointId,
                [ErrorBehavior] = @ErrorBehavior,
                [RetryCount] = @RetryCount,
                [IsActive] = @IsActive,
                [VersionNo] = @VersionNo,
                [PreProcedureName] = @PreProcedureName,
                [PreProcedureParamsJson] = @PreProcedureParamsJson,
                [PostProcedureName] = @PostProcedureName,
                [PostProcedureParamsJson] = @PostProcedureParamsJson,
                [SourceFilterJson] = @SourceFilterJson,
                [AllowAsCascadeTarget] = @AllowAsCascadeTarget,
                [SourceCodeColumn] = @SourceCodeColumn,
                [UpdatedById] = @UpdatedById,
                [Updated] = SYSUTCDATETIME()
            WHERE [Id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", integration.Id));
        AddIntegrationParameters(cmd, integration);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        // CASCADE DELETE: IntegrationMapping + IntegrationTrigger silinir.
        // IntegrationRun korunur (audit log).
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_integrationTable} WHERE [Id] = @Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyCollection<Integration>> ListCascadeTargetsAsync(
        string? sourceFormCode, CancellationToken ct)
    {
        // 2026-05-22 Cascade: Wizard Step 2 "Bağımlılık" dropdown verisi.
        // Aktif + AllowAsCascadeTarget=true filtre. Opsiyonel formCode kısıtı —
        // parent integration kendi formuyla aynı form'a cascade etmeye genelde
        // ihtiyaç duymaz, ama API caller (frontend) seçici filtre uygulayabilir.
        // Aggregate children (mappings, triggers, endpoint) DOLDURULMAZ — performance.
        var list = new List<Integration>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        var where = "[IsActive] = 1 AND [AllowAsCascadeTarget] = 1";
        if (!string.IsNullOrWhiteSpace(sourceFormCode))
        {
            where += " AND [SourceFormCode] = @FormCode";
            cmd.Parameters.Add(new SqlParameter("@FormCode", sourceFormCode));
        }
        cmd.CommandText = $"""
            SELECT [Id],[Name],[Description],[SourceFormCode],[TargetEndpointId],
                   [ErrorBehavior],[RetryCount],[IsActive],[VersionNo],
                   [CreatedById],[Created],[UpdatedById],[Updated],
                   [PreProcedureName],[PreProcedureParamsJson],
                   [PostProcedureName],[PostProcedureParamsJson],
                   [SourceFilterJson],[AllowAsCascadeTarget]
            FROM {_integrationTable}
            WHERE {where}
            ORDER BY [Name];
            """;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(MapIntegration(reader));
        return list;
    }

    // ── Mapping ──────────────────────────────────────────────────────────

    public async Task<IReadOnlyCollection<IntegrationMapping>> GetMappingsAsync(int integrationId, CancellationToken ct)
    {
        var list = new List<IntegrationMapping>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[IntegrationId],[TargetPath],[TargetDataType],[SourceType],
                   [SourceValue],[LookupSourceField],[DefaultValue],[FormatPattern],
                   [IsRequired],[SortOrder],[GroupKey],[SourceSection],
                   [LookupFiltersJson],[LookupReturnColumn],[LookupParam],
                   [CascadeToIntegrationId]
            FROM {_mappingTable}
            WHERE [IntegrationId] = @IntegrationId
            ORDER BY [SortOrder], [Id];
            """;
        cmd.Parameters.Add(new SqlParameter("@IntegrationId", integrationId));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(MapMapping(reader));
        return list;
    }

    public async Task ReplaceMappingsAsync(int integrationId, IReadOnlyCollection<IntegrationMapping> mappings, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);

        try
        {
            await using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = $"DELETE FROM {_mappingTable} WHERE [IntegrationId] = @Id;";
                del.Parameters.Add(new SqlParameter("@Id", integrationId));
                await del.ExecuteNonQueryAsync(ct);
            }

            foreach (var m in mappings)
            {
                await using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = $"""
                    INSERT INTO {_mappingTable}
                      ([IntegrationId],[TargetPath],[TargetDataType],[SourceType],[SourceValue],
                       [LookupSourceField],[DefaultValue],[FormatPattern],[IsRequired],[SortOrder],[GroupKey],
                       [SourceSection],[LookupFiltersJson],[LookupReturnColumn],[LookupParam],
                       [CascadeToIntegrationId],[CascadeByValue])
                    VALUES
                      (@IntegrationId,@TargetPath,@TargetDataType,@SourceType,@SourceValue,
                       @LookupSourceField,@DefaultValue,@FormatPattern,@IsRequired,@SortOrder,@GroupKey,
                       @SourceSection,@LookupFiltersJson,@LookupReturnColumn,@LookupParam,
                       @CascadeToIntegrationId,@CascadeByValue);
                    """;
                ins.Parameters.Add(new SqlParameter("@IntegrationId", integrationId));
                ins.Parameters.Add(new SqlParameter("@TargetPath", m.TargetPath));
                ins.Parameters.Add(new SqlParameter("@TargetDataType", (object?)m.TargetDataType ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@SourceType", m.SourceType.ToString()));
                ins.Parameters.Add(new SqlParameter("@SourceValue", (object?)m.SourceValue ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@LookupSourceField", (object?)m.LookupSourceField ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@DefaultValue", (object?)m.DefaultValue ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@FormatPattern", (object?)m.FormatPattern ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@IsRequired", m.IsRequired));
                ins.Parameters.Add(new SqlParameter("@SortOrder", m.SortOrder));
                ins.Parameters.Add(new SqlParameter("@GroupKey", (object?)m.GroupKey ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@SourceSection",
                    string.IsNullOrWhiteSpace(m.SourceSection) ? "Header" : m.SourceSection));
                ins.Parameters.Add(new SqlParameter("@LookupFiltersJson",  (object?)m.LookupFiltersJson  ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@LookupReturnColumn", (object?)m.LookupReturnColumn ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@LookupParam",        (object?)m.LookupParam        ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@CascadeToIntegrationId",
                    (object?)m.CascadeToIntegrationId ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@CascadeByValue", m.CascadeByValue));
                await ins.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    // ── Trigger ─────────────────────────────────────────────────────────

    public async Task<IReadOnlyCollection<IntegrationTrigger>> GetTriggersAsync(int integrationId, CancellationToken ct)
    {
        var list = new List<IntegrationTrigger>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[IntegrationId],[TriggerType],[Config],[IsActive],[Created]
            FROM {_triggerTable}
            WHERE [IntegrationId] = @IntegrationId
            ORDER BY [Id];
            """;
        cmd.Parameters.Add(new SqlParameter("@IntegrationId", integrationId));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(MapTrigger(reader));
        return list;
    }

    public async Task ReplaceTriggersAsync(int integrationId, IReadOnlyCollection<IntegrationTrigger> triggers, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);

        try
        {
            await using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = $"DELETE FROM {_triggerTable} WHERE [IntegrationId] = @Id;";
                del.Parameters.Add(new SqlParameter("@Id", integrationId));
                await del.ExecuteNonQueryAsync(ct);
            }

            foreach (var t in triggers)
            {
                await using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = $"""
                    INSERT INTO {_triggerTable}
                      ([IntegrationId],[TriggerType],[Config],[IsActive])
                    VALUES
                      (@IntegrationId,@TriggerType,@Config,@IsActive);
                    """;
                ins.Parameters.Add(new SqlParameter("@IntegrationId", integrationId));
                ins.Parameters.Add(new SqlParameter("@TriggerType", t.TriggerType.ToString()));
                ins.Parameters.Add(new SqlParameter("@Config", (object?)t.Config ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@IsActive", t.IsActive));
                await ins.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    // ── Endpoint ────────────────────────────────────────────────────────

    public async Task<IReadOnlyCollection<IntegrationEndpoint>> ListEndpointsAsync(bool includeInactive, CancellationToken ct)
    {
        var list = new List<IntegrationEndpoint>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[ApiProfileId],[Name],[HttpMethod],[UrlTemplate],[BodySchema],
                   [Description],[IsActive],[CreatedById],[Created],[UpdatedById],[Updated]
            FROM {_endpointTable}
            {(includeInactive ? "" : "WHERE [IsActive] = 1")}
            ORDER BY [Name];
            """;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(MapEndpoint(reader));
        return list;
    }

    public async Task<IReadOnlyCollection<IntegrationEndpoint>> ListEndpointsByProfileAsync(Guid apiProfileId, CancellationToken ct)
    {
        var list = new List<IntegrationEndpoint>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[ApiProfileId],[Name],[HttpMethod],[UrlTemplate],[BodySchema],
                   [Description],[IsActive],[CreatedById],[Created],[UpdatedById],[Updated]
            FROM {_endpointTable}
            WHERE [ApiProfileId] = @ApiProfileId AND [IsActive] = 1
            ORDER BY [Name];
            """;
        cmd.Parameters.Add(new SqlParameter("@ApiProfileId", apiProfileId));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(MapEndpoint(reader));
        return list;
    }

    public async Task<IntegrationEndpoint?> GetEndpointByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[ApiProfileId],[Name],[HttpMethod],[UrlTemplate],[BodySchema],
                   [Description],[IsActive],[CreatedById],[Created],[UpdatedById],[Updated]
            FROM {_endpointTable}
            WHERE [Id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapEndpoint(reader) : null;
    }

    public async Task<int> AddEndpointAsync(IntegrationEndpoint endpoint, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {_endpointTable}
              ([ApiProfileId],[Name],[HttpMethod],[UrlTemplate],[BodySchema],
               [Description],[IsActive],[CreatedById])
            OUTPUT INSERTED.[Id]
            VALUES
              (@ApiProfileId,@Name,@HttpMethod,@UrlTemplate,@BodySchema,
               @Description,@IsActive,@CreatedById);
            """;
        AddEndpointParameters(cmd, endpoint);
        return (int)(await cmd.ExecuteScalarAsync(ct) ?? 0);
    }

    public async Task UpdateEndpointAsync(IntegrationEndpoint endpoint, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_endpointTable}
            SET [ApiProfileId] = @ApiProfileId,
                [Name] = @Name,
                [HttpMethod] = @HttpMethod,
                [UrlTemplate] = @UrlTemplate,
                [BodySchema] = @BodySchema,
                [Description] = @Description,
                [IsActive] = @IsActive,
                [UpdatedById] = @UpdatedById,
                [Updated] = SYSUTCDATETIME()
            WHERE [Id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", endpoint.Id));
        AddEndpointParameters(cmd, endpoint);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteEndpointAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_endpointTable} WHERE [Id] = @Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Run (audit log) ─────────────────────────────────────────────────

    public async Task<long> AddRunAsync(IntegrationRun run, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {_runTable}
              ([IntegrationId],[TriggerType],[SourceRecordId],[StartedAt],[FinishedAt],
               [DurationMs],[Status],[HttpStatusCode],[RequestBody],[ResponseBody],
               [ErrorMessage],[RetryAttempt],[TriggeredBy],[ParentRunId])
            OUTPUT INSERTED.[Id]
            VALUES
              (@IntegrationId,@TriggerType,@SourceRecordId,@StartedAt,@FinishedAt,
               @DurationMs,@Status,@HttpStatusCode,@RequestBody,@ResponseBody,
               @ErrorMessage,@RetryAttempt,@TriggeredBy,@ParentRunId);
            """;
        AddRunParameters(cmd, run);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct) ?? 0L);
    }

    public async Task UpdateRunAsync(IntegrationRun run, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_runTable}
            SET [FinishedAt] = @FinishedAt,
                [DurationMs] = @DurationMs,
                [Status] = @Status,
                [HttpStatusCode] = @HttpStatusCode,
                [RequestBody] = @RequestBody,
                [ResponseBody] = @ResponseBody,
                [ErrorMessage] = @ErrorMessage,
                [RetryAttempt] = @RetryAttempt
            WHERE [Id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", run.Id));
        AddRunParameters(cmd, run);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyCollection<IntegrationRun>> GetRunsAsync(int integrationId, int limit, CancellationToken ct)
    {
        var list = new List<IntegrationRun>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP (@Limit)
                   [Id],[IntegrationId],[TriggerType],[SourceRecordId],[StartedAt],[FinishedAt],
                   [DurationMs],[Status],[HttpStatusCode],[RequestBody],[ResponseBody],
                   [ErrorMessage],[RetryAttempt],[TriggeredBy],[ParentRunId]
            FROM {_runTable}
            WHERE [IntegrationId] = @IntegrationId
            ORDER BY [StartedAt] DESC, [Id] DESC;
            """;
        cmd.Parameters.Add(new SqlParameter("@IntegrationId", integrationId));
        cmd.Parameters.Add(new SqlParameter("@Limit", Math.Clamp(limit, 1, 1000)));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(MapRun(reader));
        return list;
    }

    public async Task<IntegrationRun?> GetLatestRunAsync(int integrationId, string sourceRecordId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP 1
                   [Id],[IntegrationId],[TriggerType],[SourceRecordId],[StartedAt],[FinishedAt],
                   [DurationMs],[Status],[HttpStatusCode],[RequestBody],[ResponseBody],
                   [ErrorMessage],[RetryAttempt],[TriggeredBy],[ParentRunId]
            FROM {_runTable}
            WHERE [IntegrationId] = @IntegrationId AND [SourceRecordId] = @SourceRecordId
            ORDER BY [StartedAt] DESC, [Id] DESC;
            """;
        cmd.Parameters.Add(new SqlParameter("@IntegrationId", integrationId));
        cmd.Parameters.Add(new SqlParameter("@SourceRecordId", sourceRecordId));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapRun(reader) : null;
    }

    public async Task<int> DeleteRunsForIntegrationAsync(int integrationId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_runTable} WHERE [IntegrationId] = @Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", integrationId));
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyCollection<IntegrationRun>> ListAllRunsAsync(
        int? integrationId, string? status, int sinceDays, int limit, CancellationToken ct)
    {
        var list = new List<IntegrationRun>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();

        var where = "[StartedAt] >= @Since";
        cmd.Parameters.Add(new SqlParameter("@Since", DateTime.UtcNow.AddDays(-Math.Max(1, sinceDays))));

        if (integrationId.HasValue && integrationId.Value > 0)
        {
            where += " AND [IntegrationId] = @IntegrationId";
            cmd.Parameters.Add(new SqlParameter("@IntegrationId", integrationId.Value));
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            where += " AND [Status] = @Status";
            cmd.Parameters.Add(new SqlParameter("@Status", status.Trim()));
        }

        cmd.CommandText = $"""
            SELECT TOP (@Limit)
                   [Id],[IntegrationId],[TriggerType],[SourceRecordId],[StartedAt],[FinishedAt],
                   [DurationMs],[Status],[HttpStatusCode],[RequestBody],[ResponseBody],
                   [ErrorMessage],[RetryAttempt],[TriggeredBy],[ParentRunId]
            FROM {_runTable}
            WHERE {where}
            ORDER BY [StartedAt] DESC, [Id] DESC;
            """;
        cmd.Parameters.Add(new SqlParameter("@Limit", Math.Clamp(limit, 1, 5000)));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(MapRun(reader));
        return list;
    }

    public async Task<IntegrationRun?> GetRunByIdAsync(long id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[IntegrationId],[TriggerType],[SourceRecordId],[StartedAt],[FinishedAt],
                   [DurationMs],[Status],[HttpStatusCode],[RequestBody],[ResponseBody],
                   [ErrorMessage],[RetryAttempt],[TriggeredBy],[ParentRunId]
            FROM {_runTable}
            WHERE [Id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapRun(reader) : null;
    }

    // ── Mapper helpers ──────────────────────────────────────────────────

    private static Integration MapIntegration(SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        Name = r.GetString(r.GetOrdinal("Name")),
        Description = r.IsDBNull(r.GetOrdinal("Description")) ? null : r.GetString(r.GetOrdinal("Description")),
        SourceFormCode = r.GetString(r.GetOrdinal("SourceFormCode")),
        TargetEndpointId = r.IsDBNull(r.GetOrdinal("TargetEndpointId")) ? null : r.GetInt32(r.GetOrdinal("TargetEndpointId")),
        ErrorBehavior = ParseEnum<IntegrationErrorBehavior>(r.GetString(r.GetOrdinal("ErrorBehavior"))),
        RetryCount = r.GetInt32(r.GetOrdinal("RetryCount")),
        IsActive = r.GetBoolean(r.GetOrdinal("IsActive")),
        VersionNo = r.GetInt32(r.GetOrdinal("VersionNo")),
        CreatedById = SafeGetInt(r, "CreatedById"),
        Created = r.GetDateTime(r.GetOrdinal("Created")),
        UpdatedById = SafeGetInt(r, "UpdatedById"),
        Updated = r.IsDBNull(r.GetOrdinal("Updated")) ? null : r.GetDateTime(r.GetOrdinal("Updated")),
        PreProcedureName        = SafeGetString(r, "PreProcedureName"),
        PreProcedureParamsJson  = SafeGetString(r, "PreProcedureParamsJson"),
        PostProcedureName       = SafeGetString(r, "PostProcedureName"),
        PostProcedureParamsJson = SafeGetString(r, "PostProcedureParamsJson"),
        SourceFilterJson        = SafeGetString(r, "SourceFilterJson"),
        AllowAsCascadeTarget    = SafeGetBool(r, "AllowAsCascadeTarget", defaultValue: true),
        SourceCodeColumn        = SafeGetString(r, "SourceCodeColumn"),
    };

    private static IntegrationMapping MapMapping(SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        IntegrationId = r.GetInt32(r.GetOrdinal("IntegrationId")),
        TargetPath = r.GetString(r.GetOrdinal("TargetPath")),
        TargetDataType = r.IsDBNull(r.GetOrdinal("TargetDataType")) ? null : r.GetString(r.GetOrdinal("TargetDataType")),
        SourceType = ParseEnum<IntegrationSourceType>(r.GetString(r.GetOrdinal("SourceType"))),
        SourceValue = r.IsDBNull(r.GetOrdinal("SourceValue")) ? null : r.GetString(r.GetOrdinal("SourceValue")),
        LookupSourceField = r.IsDBNull(r.GetOrdinal("LookupSourceField")) ? null : r.GetString(r.GetOrdinal("LookupSourceField")),
        DefaultValue = r.IsDBNull(r.GetOrdinal("DefaultValue")) ? null : r.GetString(r.GetOrdinal("DefaultValue")),
        FormatPattern = r.IsDBNull(r.GetOrdinal("FormatPattern")) ? null : r.GetString(r.GetOrdinal("FormatPattern")),
        IsRequired = r.GetBoolean(r.GetOrdinal("IsRequired")),
        SortOrder = r.GetInt32(r.GetOrdinal("SortOrder")),
        GroupKey = r.IsDBNull(r.GetOrdinal("GroupKey")) ? null : r.GetString(r.GetOrdinal("GroupKey")),
        SourceSection = SafeGetString(r, "SourceSection") ?? "Header",
        LookupFiltersJson  = SafeGetString(r, "LookupFiltersJson"),
        LookupReturnColumn = SafeGetString(r, "LookupReturnColumn"),
        LookupParam        = SafeGetString(r, "LookupParam"),
        CascadeToIntegrationId = SafeGetInt(r, "CascadeToIntegrationId"),
        CascadeByValue         = SafeGetBool(r, "CascadeByValue", defaultValue: false),
    };

    private static string? SafeGetString(SqlDataReader r, string columnName)
    {
        try
        {
            var ord = r.GetOrdinal(columnName);
            return r.IsDBNull(ord) ? null : r.GetString(ord);
        }
        catch (IndexOutOfRangeException) { return null; }   // kolon yok (eski schema)
    }

    private static bool SafeGetBool(SqlDataReader r, string columnName, bool defaultValue)
    {
        try
        {
            var ord = r.GetOrdinal(columnName);
            return r.IsDBNull(ord) ? defaultValue : r.GetBoolean(ord);
        }
        catch (IndexOutOfRangeException) { return defaultValue; }
    }

    private static int? SafeGetInt(SqlDataReader r, string columnName)
    {
        try
        {
            var ord = r.GetOrdinal(columnName);
            return r.IsDBNull(ord) ? null : r.GetInt32(ord);
        }
        catch (IndexOutOfRangeException) { return null; }
    }

    private static long? SafeGetInt64(SqlDataReader r, string columnName)
    {
        try
        {
            var ord = r.GetOrdinal(columnName);
            return r.IsDBNull(ord) ? null : r.GetInt64(ord);
        }
        catch (IndexOutOfRangeException) { return null; }
    }

    private static IntegrationTrigger MapTrigger(SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        IntegrationId = r.GetInt32(r.GetOrdinal("IntegrationId")),
        TriggerType = ParseEnum<IntegrationTriggerType>(r.GetString(r.GetOrdinal("TriggerType"))),
        Config = r.IsDBNull(r.GetOrdinal("Config")) ? null : r.GetString(r.GetOrdinal("Config")),
        IsActive = r.GetBoolean(r.GetOrdinal("IsActive")),
        Created = r.GetDateTime(r.GetOrdinal("Created")),
    };

    private static IntegrationEndpoint MapEndpoint(SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        ApiProfileId = r.GetGuid(r.GetOrdinal("ApiProfileId")),
        Name = r.GetString(r.GetOrdinal("Name")),
        HttpMethod = r.GetString(r.GetOrdinal("HttpMethod")),
        UrlTemplate = r.GetString(r.GetOrdinal("UrlTemplate")),
        BodySchema = r.IsDBNull(r.GetOrdinal("BodySchema")) ? null : r.GetString(r.GetOrdinal("BodySchema")),
        Description = r.IsDBNull(r.GetOrdinal("Description")) ? null : r.GetString(r.GetOrdinal("Description")),
        IsActive = r.GetBoolean(r.GetOrdinal("IsActive")),
        CreatedById = SafeGetInt(r, "CreatedById"),
        Created = r.GetDateTime(r.GetOrdinal("Created")),
        UpdatedById = SafeGetInt(r, "UpdatedById"),
        Updated = r.IsDBNull(r.GetOrdinal("Updated")) ? null : r.GetDateTime(r.GetOrdinal("Updated")),
    };

    private static IntegrationRun MapRun(SqlDataReader r) => new()
    {
        Id = r.GetInt64(r.GetOrdinal("Id")),
        IntegrationId = r.GetInt32(r.GetOrdinal("IntegrationId")),
        TriggerType = ParseEnum<IntegrationTriggerType>(r.GetString(r.GetOrdinal("TriggerType"))),
        SourceRecordId = r.IsDBNull(r.GetOrdinal("SourceRecordId")) ? null : r.GetString(r.GetOrdinal("SourceRecordId")),
        StartedAt = r.GetDateTime(r.GetOrdinal("StartedAt")),
        FinishedAt = r.IsDBNull(r.GetOrdinal("FinishedAt")) ? null : r.GetDateTime(r.GetOrdinal("FinishedAt")),
        DurationMs = r.IsDBNull(r.GetOrdinal("DurationMs")) ? null : r.GetInt32(r.GetOrdinal("DurationMs")),
        Status = ParseEnum<IntegrationRunStatus>(r.GetString(r.GetOrdinal("Status"))),
        HttpStatusCode = r.IsDBNull(r.GetOrdinal("HttpStatusCode")) ? null : r.GetInt32(r.GetOrdinal("HttpStatusCode")),
        RequestBody = r.IsDBNull(r.GetOrdinal("RequestBody")) ? null : r.GetString(r.GetOrdinal("RequestBody")),
        ResponseBody = r.IsDBNull(r.GetOrdinal("ResponseBody")) ? null : r.GetString(r.GetOrdinal("ResponseBody")),
        ErrorMessage = r.IsDBNull(r.GetOrdinal("ErrorMessage")) ? null : r.GetString(r.GetOrdinal("ErrorMessage")),
        RetryAttempt = r.GetInt32(r.GetOrdinal("RetryAttempt")),
        TriggeredBy = r.IsDBNull(r.GetOrdinal("TriggeredBy")) ? null : r.GetString(r.GetOrdinal("TriggeredBy")),
        ParentRunId = SafeGetInt64(r, "ParentRunId"),
    };

    private static void AddIntegrationParameters(SqlCommand cmd, Integration integration)
    {
        cmd.Parameters.Add(new SqlParameter("@Name", integration.Name));
        cmd.Parameters.Add(new SqlParameter("@Description", (object?)integration.Description ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@SourceFormCode", integration.SourceFormCode));
        cmd.Parameters.Add(new SqlParameter("@TargetEndpointId",
            (object?)integration.TargetEndpointId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@ErrorBehavior", integration.ErrorBehavior.ToString()));
        cmd.Parameters.Add(new SqlParameter("@RetryCount", integration.RetryCount));
        cmd.Parameters.Add(new SqlParameter("@IsActive", integration.IsActive));
        cmd.Parameters.Add(new SqlParameter("@VersionNo", integration.VersionNo));
        cmd.Parameters.Add(new SqlParameter("@CreatedById", (object?)integration.CreatedById ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@UpdatedById", (object?)integration.UpdatedById ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@PreProcedureName",
            (object?)integration.PreProcedureName ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@PreProcedureParamsJson",
            (object?)integration.PreProcedureParamsJson ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@PostProcedureName",
            (object?)integration.PostProcedureName ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@PostProcedureParamsJson",
            (object?)integration.PostProcedureParamsJson ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@SourceFilterJson",
            (object?)integration.SourceFilterJson ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@AllowAsCascadeTarget", integration.AllowAsCascadeTarget));
        cmd.Parameters.Add(new SqlParameter("@SourceCodeColumn",
            (object?)integration.SourceCodeColumn ?? DBNull.Value));
    }

    private static void AddEndpointParameters(SqlCommand cmd, IntegrationEndpoint endpoint)
    {
        cmd.Parameters.Add(new SqlParameter("@ApiProfileId", endpoint.ApiProfileId));
        cmd.Parameters.Add(new SqlParameter("@Name", endpoint.Name));
        cmd.Parameters.Add(new SqlParameter("@HttpMethod", endpoint.HttpMethod));
        cmd.Parameters.Add(new SqlParameter("@UrlTemplate", endpoint.UrlTemplate));
        cmd.Parameters.Add(new SqlParameter("@BodySchema", (object?)endpoint.BodySchema ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Description", (object?)endpoint.Description ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@IsActive", endpoint.IsActive));
        cmd.Parameters.Add(new SqlParameter("@CreatedById", (object?)endpoint.CreatedById ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@UpdatedById", (object?)endpoint.UpdatedById ?? DBNull.Value));
    }

    private static void AddRunParameters(SqlCommand cmd, IntegrationRun run)
    {
        cmd.Parameters.Add(new SqlParameter("@IntegrationId", run.IntegrationId));
        cmd.Parameters.Add(new SqlParameter("@TriggerType", run.TriggerType.ToString()));
        cmd.Parameters.Add(new SqlParameter("@SourceRecordId", (object?)run.SourceRecordId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@StartedAt", run.StartedAt));
        cmd.Parameters.Add(new SqlParameter("@FinishedAt", (object?)run.FinishedAt ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@DurationMs", (object?)run.DurationMs ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Status", run.Status.ToString()));
        cmd.Parameters.Add(new SqlParameter("@HttpStatusCode", (object?)run.HttpStatusCode ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@RequestBody", (object?)run.RequestBody ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@ResponseBody", (object?)run.ResponseBody ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@ErrorMessage", (object?)run.ErrorMessage ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@RetryAttempt", run.RetryAttempt));
        cmd.Parameters.Add(new SqlParameter("@TriggeredBy", (object?)run.TriggeredBy ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@ParentRunId", (object?)run.ParentRunId ?? DBNull.Value));
    }

    private static T ParseEnum<T>(string s) where T : struct
        => Enum.TryParse<T>(s, ignoreCase: true, out var v) ? v : default;
}
