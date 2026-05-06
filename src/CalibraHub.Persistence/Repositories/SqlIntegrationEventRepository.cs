using System.Data;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlIntegrationEventRepository : IIntegrationEventRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _defTable;
    private readonly string _logTable;

    public SqlIntegrationEventRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _defTable = $"[{schema}].[integration_event_definitions]";
        _logTable = $"[{schema}].[integration_event_logs]";
    }

    public async Task<IReadOnlyCollection<IntegrationEventDefinition>> GetByCompanyAsync(int companyId, CancellationToken ct)
    {
        var list = new List<IntegrationEventDefinition>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [id],[company_id],[name],[event_source],[event_type],[event_detail],
                   [sql_command],[stop_on_error],[is_active],[execution_order],[Created],[Updated],
                   [action_type],[procedure_name],[parameters_json],[api_config_json]
            FROM {_defTable}
            WHERE [company_id] = @CompanyId
            ORDER BY [event_source], [event_type], [execution_order];
            """;
        cmd.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(MapDefinition(r));
        return list;
    }

    public async Task<IReadOnlyCollection<IntegrationEventDefinition>> GetActiveAsync(
        int companyId, string eventSource, string eventType, CancellationToken ct)
    {
        var list = new List<IntegrationEventDefinition>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [id],[company_id],[name],[event_source],[event_type],[event_detail],
                   [sql_command],[stop_on_error],[is_active],[execution_order],[Created],[Updated],
                   [action_type],[procedure_name],[parameters_json],[api_config_json]
            FROM {_defTable}
            WHERE [company_id] = @CompanyId AND [event_source] = @EventSource
                  AND [event_type] = @EventType AND [is_active] = 1
            ORDER BY [execution_order];
            """;
        cmd.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        cmd.Parameters.Add(new SqlParameter("@EventSource", eventSource));
        cmd.Parameters.Add(new SqlParameter("@EventType", eventType));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(MapDefinition(r));
        return list;
    }

    public async Task<IntegrationEventDefinition?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [id],[company_id],[name],[event_source],[event_type],[event_detail],
                   [sql_command],[stop_on_error],[is_active],[execution_order],[Created],[Updated],
                   [action_type],[procedure_name],[parameters_json],[api_config_json]
            FROM {_defTable}
            WHERE [id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? MapDefinition(r) : null;
    }

    public async Task UpsertDefinitionAsync(IntegrationEventDefinition def, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            IF EXISTS (SELECT 1 FROM {_defTable} WHERE [id] = @Id)
                UPDATE {_defTable} SET
                    [name] = @Name, [event_source] = @EventSource, [event_type] = @EventType,
                    [event_detail] = @EventDetail, [sql_command] = @SqlCommand,
                    [stop_on_error] = @StopOnError, [is_active] = @IsActive,
                    [execution_order] = @ExecutionOrder, [Updated] = @UpdatedAt,
                    [action_type] = @ActionType, [procedure_name] = @ProcedureName,
                    [parameters_json] = @ParametersJson, [api_config_json] = @ApiConfigJson
                WHERE [id] = @Id
            ELSE
                INSERT INTO {_defTable}
                    ([id],[company_id],[name],[event_source],[event_type],[event_detail],
                     [sql_command],[stop_on_error],[is_active],[execution_order],[Created],[Updated],
                     [action_type],[procedure_name],[parameters_json],[api_config_json])
                VALUES
                    (@Id, @CompanyId, @Name, @EventSource, @EventType, @EventDetail,
                     @SqlCommand, @StopOnError, @IsActive, @ExecutionOrder, @CreatedAt, @UpdatedAt,
                     @ActionType, @ProcedureName, @ParametersJson, @ApiConfigJson);
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", def.Id));
        cmd.Parameters.Add(new SqlParameter("@CompanyId", def.CompanyId));
        cmd.Parameters.Add(new SqlParameter("@Name", def.Name));
        cmd.Parameters.Add(new SqlParameter("@EventSource", def.EventSource));
        cmd.Parameters.Add(new SqlParameter("@EventType", def.EventType));
        cmd.Parameters.Add(new SqlParameter("@EventDetail", (object?)def.EventDetail ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@SqlCommand", (object?)def.SqlCommand ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@StopOnError", def.StopOnError));
        cmd.Parameters.Add(new SqlParameter("@IsActive", def.IsActive));
        cmd.Parameters.Add(new SqlParameter("@ExecutionOrder", def.ExecutionOrder));
        cmd.Parameters.Add(new SqlParameter("@CreatedAt", def.CreatedAt));
        cmd.Parameters.Add(new SqlParameter("@UpdatedAt", def.UpdatedAt));
        cmd.Parameters.Add(new SqlParameter("@ActionType", def.ActionType));
        cmd.Parameters.Add(new SqlParameter("@ProcedureName", (object?)def.ProcedureName ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@ParametersJson", (object?)def.ParametersJson ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@ApiConfigJson", (object?)def.ApiConfigJson ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteDefinitionAsync(Guid id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_defTable} WHERE [id] = @Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task AddLogAsync(IntegrationEventLog log, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {_logTable}
                ([id],[definition_id],[company_id],[event_source],[event_type],
                 [executed_sql],[success],[error_message],[executed_at],[duration_ms],
                 [action_type],[response_body])
            VALUES
                (@Id, @DefinitionId, @CompanyId, @EventSource, @EventType,
                 @ExecutedSql, @Success, @ErrorMessage, @ExecutedAt, @DurationMs,
                 @ActionType, @ResponseBody);
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", log.Id));
        cmd.Parameters.Add(new SqlParameter("@DefinitionId", log.DefinitionId));
        cmd.Parameters.Add(new SqlParameter("@CompanyId", log.CompanyId));
        cmd.Parameters.Add(new SqlParameter("@EventSource", log.EventSource));
        cmd.Parameters.Add(new SqlParameter("@EventType", log.EventType));
        cmd.Parameters.Add(new SqlParameter("@ExecutedSql", (object?)log.ExecutedSql ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Success", log.Success));
        cmd.Parameters.Add(new SqlParameter("@ErrorMessage", (object?)log.ErrorMessage ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@ExecutedAt", log.ExecutedAt));
        cmd.Parameters.Add(new SqlParameter("@DurationMs", log.DurationMs));
        cmd.Parameters.Add(new SqlParameter("@ActionType", log.ActionType));
        cmd.Parameters.Add(new SqlParameter("@ResponseBody", (object?)log.ResponseBody ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyCollection<IntegrationEventLog>> GetRecentLogsAsync(int companyId, int take, CancellationToken ct)
    {
        var list = new List<IntegrationEventLog>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP(@Take) [id],[definition_id],[company_id],[event_source],[event_type],
                   [executed_sql],[success],[error_message],[executed_at],[duration_ms],
                   [action_type],[response_body]
            FROM {_logTable}
            WHERE [company_id] = @CompanyId
            ORDER BY [executed_at] DESC;
            """;
        cmd.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        cmd.Parameters.Add(new SqlParameter("@Take", take));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(MapLog(r));
        return list;
    }

    public async Task ExecuteSqlOnCompanyDbAsync(int companyId, string sql, int timeoutSeconds, CancellationToken ct)
    {
        var connStr = _connectionFactory.ResolveConnectionStringForCompany(companyId);
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = timeoutSeconds;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<(int returnCode, string? returnMessage)> ExecuteProcedureOnCompanyDbAsync(
        int companyId, string procedureName,
        IReadOnlyList<ProcedureParameter> parameters,
        int timeoutSeconds, CancellationToken ct)
    {
        var connStr = _connectionFactory.ResolveConnectionStringForCompany(companyId);
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = procedureName;
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.CommandTimeout = timeoutSeconds;

        var outputParams = new List<SqlParameter>();
        foreach (var p in parameters)
        {
            var sqlParam = new SqlParameter(p.Name, p.DbType);
            if (p.Direction == ParameterDirection.Output || p.Direction == ParameterDirection.InputOutput)
            {
                sqlParam.Direction = p.Direction;
                sqlParam.Size = p.DbType == SqlDbType.NVarChar || p.DbType == SqlDbType.VarChar ? 500 : 0;
                outputParams.Add(sqlParam);
            }
            else
            {
                sqlParam.Direction = ParameterDirection.Input;
                sqlParam.Value = p.Value ?? DBNull.Value;
            }
            cmd.Parameters.Add(sqlParam);
        }

        await cmd.ExecuteNonQueryAsync(ct);

        int returnCode = 0;
        string? returnMessage = null;
        foreach (var op in outputParams)
        {
            var val = op.Value;
            if (op.ParameterName.Equals("@ReturnCode", StringComparison.OrdinalIgnoreCase) && val != DBNull.Value)
                returnCode = Convert.ToInt32(val);
            if (op.ParameterName.Equals("@ReturnMsg", StringComparison.OrdinalIgnoreCase) && val != DBNull.Value)
                returnMessage = val?.ToString();
        }

        return (returnCode, returnMessage);
    }

    private static IntegrationEventDefinition MapDefinition(SqlDataReader r)
    {
        var ordActionType = GetOrdinalSafe(r, "action_type");
        var ordProcName = GetOrdinalSafe(r, "procedure_name");
        var ordParamsJson = GetOrdinalSafe(r, "parameters_json");
        var ordApiConfigJson = GetOrdinalSafe(r, "api_config_json");

        return new IntegrationEventDefinition
        {
            Id = r.GetGuid(r.GetOrdinal("id")),
            CompanyId = r.GetInt32(r.GetOrdinal("company_id")),
            Name = r.GetString(r.GetOrdinal("name")),
            EventSource = r.GetString(r.GetOrdinal("event_source")),
            EventType = r.GetString(r.GetOrdinal("event_type")),
            EventDetail = r.IsDBNull(r.GetOrdinal("event_detail")) ? null : r.GetString(r.GetOrdinal("event_detail")),
            SqlCommand = r.IsDBNull(r.GetOrdinal("sql_command")) ? null : r.GetString(r.GetOrdinal("sql_command")),
            StopOnError = r.GetBoolean(r.GetOrdinal("stop_on_error")),
            IsActive = r.GetBoolean(r.GetOrdinal("is_active")),
            ExecutionOrder = r.GetInt32(r.GetOrdinal("execution_order")),
            CreatedAt = r.GetDateTime(r.GetOrdinal("Created")),
            UpdatedAt = r.GetDateTime(r.GetOrdinal("Updated")),
            ActionType = ordActionType >= 0 && !r.IsDBNull(ordActionType) ? r.GetString(ordActionType) : "SqlCommand",
            ProcedureName = ordProcName >= 0 && !r.IsDBNull(ordProcName) ? r.GetString(ordProcName) : null,
            ParametersJson = ordParamsJson >= 0 && !r.IsDBNull(ordParamsJson) ? r.GetString(ordParamsJson) : null,
            ApiConfigJson = ordApiConfigJson >= 0 && !r.IsDBNull(ordApiConfigJson) ? r.GetString(ordApiConfigJson) : null,
        };
    }

    private static IntegrationEventLog MapLog(SqlDataReader r)
    {
        var ordActionType = GetOrdinalSafe(r, "action_type");
        var ordResponseBody = GetOrdinalSafe(r, "response_body");

        return new IntegrationEventLog
        {
            Id = r.GetGuid(r.GetOrdinal("id")),
            DefinitionId = r.GetGuid(r.GetOrdinal("definition_id")),
            CompanyId = r.GetInt32(r.GetOrdinal("company_id")),
            EventSource = r.GetString(r.GetOrdinal("event_source")),
            EventType = r.GetString(r.GetOrdinal("event_type")),
            ExecutedSql = r.IsDBNull(r.GetOrdinal("executed_sql")) ? null : r.GetString(r.GetOrdinal("executed_sql")),
            Success = r.GetBoolean(r.GetOrdinal("success")),
            ErrorMessage = r.IsDBNull(r.GetOrdinal("error_message")) ? null : r.GetString(r.GetOrdinal("error_message")),
            ExecutedAt = r.GetDateTime(r.GetOrdinal("executed_at")),
            DurationMs = r.GetInt64(r.GetOrdinal("duration_ms")),
            ActionType = ordActionType >= 0 && !r.IsDBNull(ordActionType) ? r.GetString(ordActionType) : "SqlCommand",
            ResponseBody = ordResponseBody >= 0 && !r.IsDBNull(ordResponseBody) ? r.GetString(ordResponseBody) : null,
        };
    }

    private static int GetOrdinalSafe(SqlDataReader r, string columnName)
    {
        try { return r.GetOrdinal(columnName); }
        catch { return -1; }
    }
}
