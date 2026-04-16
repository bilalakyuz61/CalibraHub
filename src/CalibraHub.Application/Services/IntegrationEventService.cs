using System.Data;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Services;

public sealed class IntegrationEventService : IIntegrationEventService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IntegrationEventService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _memoryCache;

    public IntegrationEventService(
        IServiceScopeFactory scopeFactory,
        ILogger<IntegrationEventService> logger,
        IHttpClientFactory httpClientFactory,
        IMemoryCache memoryCache)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _memoryCache = memoryCache;
    }

    public async Task ExecuteBeforeEventAsync(int companyId, string eventSource, string eventType,
        Dictionary<string, string>? placeholders, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IIntegrationEventRepository>();
        var definitions = await repo.GetActiveAsync(companyId, eventSource, eventType, ct);

        if (definitions.Count == 0) return;

        foreach (var def in definitions)
        {
            await ExecuteDefinitionAsync(repo, def, companyId, placeholders, ct);
        }
    }

    public void FireAfterEvent(int companyId, string eventSource, string eventType,
        Dictionary<string, string>? placeholders)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IIntegrationEventRepository>();
                var definitions = await repo.GetActiveAsync(companyId, eventSource, eventType, default);

                if (definitions.Count == 0) return;

                foreach (var def in definitions)
                {
                    try
                    {
                        await ExecuteDefinitionAsync(repo, def, companyId, placeholders, default);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "After event hatasi: {DefName}", def.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FireAfterEvent hata: {EventSource}.{EventType}", eventSource, eventType);
            }
        });
    }

    private async Task ExecuteDefinitionAsync(IIntegrationEventRepository repo,
        IntegrationEventDefinition def, int companyId,
        Dictionary<string, string>? placeholders, CancellationToken ct)
    {
        switch (def.ActionType)
        {
            case "SqlProcedure":
                await ExecuteProcedureAsync(repo, def, companyId, placeholders, ct);
                break;
            case "RestApi":
                await ExecuteRestApiAsync(repo, def, companyId, placeholders, ct);
                break;
            default: // SqlCommand
                await ExecuteSqlCommandAsync(repo, def, companyId, placeholders, ct);
                break;
        }
    }

    private async Task ExecuteSqlCommandAsync(IIntegrationEventRepository repo,
        IntegrationEventDefinition def, int companyId,
        Dictionary<string, string>? placeholders, CancellationToken ct)
    {
        var sql = ApplyPlaceholders(def.SqlCommand ?? "", placeholders, companyId, def.EventSource, def.EventType);

        var secErr = ValidateSqlSecurity(sql);
        if (secErr != null)
        {
            await LogSafe(repo, def, companyId, "SqlCommand", sql, null, false, secErr, 0, ct);
            if (def.StopOnError)
                throw new IntegrationEventException($"[{def.Name}]: {secErr}", new InvalidOperationException(secErr));
            return;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            await repo.ExecuteSqlOnCompanyDbAsync(companyId, sql, 30, ct);
            sw.Stop();
            await LogSafe(repo, def, companyId, "SqlCommand", sql, null, true, null, sw.ElapsedMilliseconds, ct);
        }
        catch (Exception ex)
        {
            sw.Stop();
            await LogSafe(repo, def, companyId, "SqlCommand", sql, null, false, ex.Message, sw.ElapsedMilliseconds, ct);
            if (def.StopOnError)
                throw new IntegrationEventException($"Entegrasyon hatasi [{def.Name}]: {ex.Message}", ex);
        }
    }

    private async Task ExecuteProcedureAsync(IIntegrationEventRepository repo,
        IntegrationEventDefinition def, int companyId,
        Dictionary<string, string>? placeholders, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(def.ProcedureName))
        {
            var err = "Prosedur adi tanimlanmamis.";
            await LogSafe(repo, def, companyId, "SqlProcedure", null, null, false, err, 0, ct);
            if (def.StopOnError) throw new IntegrationEventException($"[{def.Name}]: {err}", new InvalidOperationException(err));
            return;
        }

        List<ProcParamDef> paramDefs = [];
        if (!string.IsNullOrWhiteSpace(def.ParametersJson))
        {
            try { paramDefs = JsonSerializer.Deserialize<List<ProcParamDef>>(def.ParametersJson, _jsonOpts) ?? []; }
            catch (Exception ex) { _logger.LogWarning(ex, "ParametersJson parse hatasi: {DefName}", def.Name); }
        }

        var parameters = new List<ProcedureParameter>();
        foreach (var pd in paramDefs)
        {
            var dir = pd.Direction?.ToLowerInvariant() == "output" ? ParameterDirection.Output : ParameterDirection.Input;
            var dbType = ParseSqlDbType(pd.Type);
            object? value = null;
            if (dir == ParameterDirection.Input)
            {
                value = pd.Source?.ToLowerInvariant() == "manual"
                    ? pd.Value
                    : (placeholders?.GetValueOrDefault(pd.Field ?? "") ?? "");
            }
            parameters.Add(new ProcedureParameter(pd.Name ?? "@Param", dir, dbType, value));
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var (returnCode, returnMsg) = await repo.ExecuteProcedureOnCompanyDbAsync(
                companyId, def.ProcedureName, parameters, 30, ct);
            sw.Stop();

            var executedDesc = $"EXEC {def.ProcedureName} (ReturnCode={returnCode})";
            if (returnCode != 0)
            {
                var errMsg = returnMsg ?? $"Prosedur ReturnCode={returnCode}";
                await LogSafe(repo, def, companyId, "SqlProcedure", executedDesc, null, false, errMsg, sw.ElapsedMilliseconds, ct);
                if (def.StopOnError)
                    throw new IntegrationEventException($"[{def.Name}]: {errMsg}", new InvalidOperationException(errMsg));
            }
            else
            {
                await LogSafe(repo, def, companyId, "SqlProcedure", executedDesc, null, true, null, sw.ElapsedMilliseconds, ct);
            }
        }
        catch (IntegrationEventException) { throw; }
        catch (Exception ex)
        {
            sw.Stop();
            await LogSafe(repo, def, companyId, "SqlProcedure", $"EXEC {def.ProcedureName}", null, false, ex.Message, sw.ElapsedMilliseconds, ct);
            if (def.StopOnError)
                throw new IntegrationEventException($"Entegrasyon hatasi [{def.Name}]: {ex.Message}", ex);
        }
    }

    private async Task ExecuteRestApiAsync(IIntegrationEventRepository repo,
        IntegrationEventDefinition def, int companyId,
        Dictionary<string, string>? placeholders, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(def.ApiConfigJson))
        {
            var err = "API konfigurasyonu tanimlanmamis.";
            await LogSafe(repo, def, companyId, "RestApi", null, null, false, err, 0, ct);
            if (def.StopOnError) throw new IntegrationEventException($"[{def.Name}]: {err}", new InvalidOperationException(err));
            return;
        }

        ApiDefinitionConfig? defConfig;
        try { defConfig = JsonSerializer.Deserialize<ApiDefinitionConfig>(def.ApiConfigJson, _jsonOpts); }
        catch (Exception ex)
        {
            var err = $"API config parse hatasi: {ex.Message}";
            await LogSafe(repo, def, companyId, "RestApi", null, null, false, err, 0, ct);
            if (def.StopOnError) throw new IntegrationEventException($"[{def.Name}]: {err}", ex);
            return;
        }

        if (defConfig?.ProfileId == null)
        {
            var err = "API profili secilmemis.";
            await LogSafe(repo, def, companyId, "RestApi", null, null, false, err, 0, ct);
            if (def.StopOnError) throw new IntegrationEventException($"[{def.Name}]: {err}", new InvalidOperationException(err));
            return;
        }

        // Load profile from repo
        using var scope2 = _scopeFactory.CreateScope();
        var profileRepo = scope2.ServiceProvider.GetRequiredService<IIntegrationApiProfileRepository>();
        var profile = await profileRepo.GetByIdAsync(defConfig.ProfileId.Value, ct);
        if (profile == null)
        {
            var err = $"API profili bulunamadi: {defConfig.ProfileId}";
            await LogSafe(repo, def, companyId, "RestApi", null, null, false, err, 0, ct);
            if (def.StopOnError) throw new IntegrationEventException($"[{def.Name}]: {err}", new InvalidOperationException(err));
            return;
        }

        RestApiAuthConfig? authConfig = null;
        if (!string.IsNullOrWhiteSpace(profile.AuthConfigJson))
        {
            try { authConfig = JsonSerializer.Deserialize<RestApiAuthConfig>(profile.AuthConfigJson, _jsonOpts); }
            catch { }
        }

        var sw = Stopwatch.StartNew();
        string? responseBody = null;
        var endpoint = $"{profile.BaseUrl.TrimEnd('/')}{defConfig.Endpoint}";

        try
        {
            var client = _httpClientFactory.CreateClient("IntegrationEvents");

            // Build a temporary RestApiConfig from profile + defConfig
            var fullConfig = new RestApiConfig
            {
                BaseUrl = profile.BaseUrl,
                Endpoint = defConfig.Endpoint,
                Method = defConfig.Method,
                AuthType = profile.AuthType,
                AuthConfig = authConfig,
                BodyTemplate = defConfig.BodyTemplate,
                SuccessPath = defConfig.SuccessPath,
                SuccessValue = defConfig.SuccessValue
            };

            var token = await GetAuthTokenAsync(client, fullConfig, ct);

            var body = ApplyPlaceholdersRaw(defConfig.BodyTemplate ?? "{}", placeholders);
            var request = new HttpRequestMessage(
                defConfig.Method?.ToUpperInvariant() switch
                {
                    "GET" => HttpMethod.Get,
                    "PUT" => HttpMethod.Put,
                    "DELETE" => HttpMethod.Delete,
                    _ => HttpMethod.Post
                },
                endpoint);

            if (token != null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            else if (profile.AuthType == "BasicAuth" && authConfig != null)
            {
                var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{authConfig.Username}:{authConfig.Password}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            }
            else if (profile.AuthType == "ApiKey" && authConfig != null)
            {
                request.Headers.TryAddWithoutValidation(authConfig.ApiKeyHeader ?? "X-Api-Key", authConfig.ApiKeyValue ?? "");
            }

            if (request.Method != HttpMethod.Get)
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request, ct);
            responseBody = await response.Content.ReadAsStringAsync(ct);
            sw.Stop();

            bool success = CheckSuccess(responseBody, defConfig.SuccessPath, defConfig.SuccessValue);
            if (!success && response.IsSuccessStatusCode && string.IsNullOrWhiteSpace(defConfig.SuccessPath))
                success = true;

            await LogSafe(repo, def, companyId, "RestApi", endpoint, responseBody, success,
                success ? null : $"HTTP {(int)response.StatusCode}: {responseBody?.Substring(0, Math.Min(200, responseBody?.Length ?? 0))}",
                sw.ElapsedMilliseconds, ct);

            if (!success && def.StopOnError)
            {
                var errMsg = $"REST API basarisiz: HTTP {(int)response.StatusCode}";
                throw new IntegrationEventException($"[{def.Name}]: {errMsg}", new InvalidOperationException(errMsg));
            }
        }
        catch (IntegrationEventException) { throw; }
        catch (Exception ex)
        {
            sw.Stop();
            await LogSafe(repo, def, companyId, "RestApi", endpoint, responseBody, false, ex.Message, sw.ElapsedMilliseconds, ct);
            if (def.StopOnError)
                throw new IntegrationEventException($"Entegrasyon hatasi [{def.Name}]: {ex.Message}", ex);
        }
    }

    private async Task<string?> GetAuthTokenAsync(HttpClient client, RestApiConfig config, CancellationToken ct)
    {
        if (config.AuthType == "OAuth2Password" && config.AuthConfig != null)
        {
            var cacheKey = $"ie_token_{config.BaseUrl}_{config.AuthConfig.Username}";
            if (_memoryCache.TryGetValue<string>(cacheKey, out var cached))
                return cached;

            var tokenUrl = $"{config.BaseUrl?.TrimEnd('/')}{config.AuthConfig.TokenEndpoint}";
            var formFields = new Dictionary<string, string>
            {
                ["grant_type"] = config.AuthConfig.GrantType ?? "password",
                ["username"] = config.AuthConfig.Username ?? "",
                ["password"] = config.AuthConfig.Password ?? ""
            };
            if (config.AuthConfig.ExtraFields != null)
                foreach (var kv in config.AuthConfig.ExtraFields)
                    formFields[kv.Key] = kv.Value;

            var resp = await client.PostAsync(tokenUrl, new FormUrlEncodedContent(formFields), ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var tokenField = config.AuthConfig.TokenField ?? "access_token";
            var token = doc.RootElement.TryGetProperty(tokenField, out var t) ? t.GetString() : null;

            if (token != null)
            {
                var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600;
                _memoryCache.Set(cacheKey, token, TimeSpan.FromSeconds(expiresIn - 60));
            }

            return token;
        }
        if (config.AuthType == "BearerStatic" && config.AuthConfig != null)
            return config.AuthConfig.Token;

        return null;
    }

    private static bool CheckSuccess(string? responseBody, string? successPath, string? successValue)
    {
        if (string.IsNullOrWhiteSpace(responseBody) || string.IsNullOrWhiteSpace(successPath)) return true;
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var parts = successPath.Split('.');
            JsonElement current = doc.RootElement;
            foreach (var part in parts)
            {
                if (!current.TryGetProperty(part, out var next)) return false;
                current = next;
            }
            var actual = current.ValueKind == JsonValueKind.True ? "true"
                       : current.ValueKind == JsonValueKind.False ? "false"
                       : current.ToString();
            return string.Equals(actual, successValue ?? "true", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public async Task<IReadOnlyCollection<IntegrationEventDefinitionDto>> GetDefinitionsAsync(
        int companyId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IIntegrationEventRepository>();
        var defs = await repo.GetByCompanyAsync(companyId, ct);
        return defs.Select(d => new IntegrationEventDefinitionDto(
            d.Id, d.CompanyId, d.Name, d.EventSource, d.EventType, d.EventDetail,
            d.SqlCommand, d.StopOnError, d.IsActive, d.ExecutionOrder, d.CreatedAt, d.UpdatedAt,
            d.ActionType, d.ProcedureName, d.ParametersJson, d.ApiConfigJson
        )).ToArray();
    }

    public async Task SaveDefinitionAsync(int companyId, SaveIntegrationEventRequest request, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IIntegrationEventRepository>();

        // SQL guvenlik kontrolu sadece SqlCommand tipinde
        if (request.ActionType == "SqlCommand" || string.IsNullOrWhiteSpace(request.ActionType))
        {
            var secError = ValidateSqlSecurity(request.SqlCommand ?? "");
            if (secError != null)
                throw new InvalidOperationException(secError);
        }

        // Tip bazli zorunlu alan kontrolleri
        if (request.ActionType == "SqlProcedure" && string.IsNullOrWhiteSpace(request.ProcedureName))
            throw new InvalidOperationException("SQL Prosedur tipi icin prosedur adi zorunludur.");
        if (request.ActionType == "RestApi" && string.IsNullOrWhiteSpace(request.ApiConfigJson))
            throw new InvalidOperationException("REST API tipi icin API konfigurasyonu zorunludur.");

        IntegrationEventDefinition def;
        if (request.Id.HasValue)
        {
            def = await repo.GetByIdAsync(request.Id.Value, ct)
                  ?? throw new InvalidOperationException("Tanim bulunamadi.");
            def.Name = request.Name;
            def.EventSource = request.EventSource;
            def.EventType = request.EventType;
            def.EventDetail = request.EventDetail;
            def.SqlCommand = request.SqlCommand;
            def.StopOnError = request.StopOnError;
            def.IsActive = request.IsActive;
            def.ExecutionOrder = request.ExecutionOrder;
            def.ActionType = request.ActionType ?? "SqlCommand";
            def.ProcedureName = request.ProcedureName;
            def.ParametersJson = request.ParametersJson;
            def.ApiConfigJson = request.ApiConfigJson;
            def.UpdatedAt = DateTime.Now;
        }
        else
        {
            def = new IntegrationEventDefinition
            {
                CompanyId = companyId,
                Name = request.Name,
                EventSource = request.EventSource,
                EventType = request.EventType,
                EventDetail = request.EventDetail,
                SqlCommand = request.SqlCommand,
                StopOnError = request.StopOnError,
                IsActive = request.IsActive,
                ExecutionOrder = request.ExecutionOrder,
                ActionType = request.ActionType ?? "SqlCommand",
                ProcedureName = request.ProcedureName,
                ParametersJson = request.ParametersJson,
                ApiConfigJson = request.ApiConfigJson
            };
        }

        await repo.UpsertDefinitionAsync(def, ct);
    }

    public async Task DeleteDefinitionAsync(Guid id, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IIntegrationEventRepository>();
        await repo.DeleteDefinitionAsync(id, ct);
    }

    public async Task<IReadOnlyCollection<IntegrationEventLogDto>> GetRecentLogsAsync(
        int companyId, int take, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IIntegrationEventRepository>();
        var logs = await repo.GetRecentLogsAsync(companyId, take, ct);
        return logs.Select(l => new IntegrationEventLogDto(
            l.Id, l.DefinitionId, l.EventSource, l.EventType,
            l.ExecutedSql, l.ActionType, l.ResponseBody,
            l.Success, l.ErrorMessage, l.ExecutedAt, l.DurationMs
        )).ToArray();
    }

    // ── SQL Guvenlik Kontrolu ──────────────────────────────────────────────
    private static readonly string[] ForbiddenPatterns = new[]
    {
        @"\bCREATE\s+(LOGIN|USER|ROLE|DATABASE|SCHEMA)\b",
        @"\bALTER\s+(LOGIN|USER|ROLE|DATABASE|SERVER)\b",
        @"\bDROP\s+(LOGIN|USER|ROLE|DATABASE|SCHEMA|TABLE|VIEW|PROCEDURE|FUNCTION|INDEX)\b",
        @"\bTRUNCATE\s+TABLE\b",
        @"\bGRANT\b", @"\bREVOKE\b", @"\bDENY\b",
        @"\bADD\s+MEMBER\b", @"\bsp_addsrvrolemember\b", @"\bsp_addrolemember\b",
        @"\bxp_cmdshell\b", @"\bxp_regread\b", @"\bxp_regwrite\b", @"\bxp_fileexist\b",
        @"\bsp_configure\b", @"\bsp_addlinkedserver\b", @"\bsp_oacreate\b",
        @"\bOPENROWSET\b", @"\bOPENDATASOURCE\b", @"\bOPENQUERY\b",
        @"\bBULK\s+INSERT\b", @"\bBCP\b",
        @"\bRESTORE\b", @"\bBACKUP\b",
        @"\bEXEC\s*\(\s*[@']",
        @"\bsp_executesql\b",
        @"\bSHUTDOWN\b", @"\bKILL\b",
    };

    private static readonly System.Text.RegularExpressions.Regex[] ForbiddenRegexes =
        ForbiddenPatterns.Select(p => new System.Text.RegularExpressions.Regex(
            p, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled
        )).ToArray();

    public static string? ValidateSqlSecurity(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return null;
        var cleaned = System.Text.RegularExpressions.Regex.Replace(sql, @"--.*$", "", System.Text.RegularExpressions.RegexOptions.Multiline);
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"/\*[\s\S]*?\*/", "");
        foreach (var regex in ForbiddenRegexes)
        {
            var match = regex.Match(cleaned);
            if (match.Success)
                return $"Guvenlik ihlali: '{match.Value.Trim()}' komutu kullanilamaz.";
        }
        return null;
    }

    private static string SqlEscape(string value) => "N'" + value.Replace("'", "''") + "'";

    private static string ApplyPlaceholders(string sql, Dictionary<string, string>? placeholders,
        int companyId, string eventSource, string eventType)
    {
        sql = sql.Replace("{CompanyId}", SqlEscape(companyId.ToString()))
                 .Replace("{EventSource}", SqlEscape(eventSource))
                 .Replace("{EventType}", SqlEscape(eventType))
                 .Replace("{Timestamp}", SqlEscape(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
        if (placeholders != null)
            foreach (var (key, value) in placeholders)
                sql = sql.Replace($"{{{key}}}", SqlEscape(value));
        sql = System.Text.RegularExpressions.Regex.Replace(sql, @"\{[A-Za-z]\w*\}", "N''");
        return sql;
    }

    private static string ApplyPlaceholdersRaw(string template, Dictionary<string, string>? placeholders)
    {
        if (placeholders != null)
            foreach (var (key, value) in placeholders)
                template = template.Replace($"{{{key}}}", value);
        return template;
    }

    private static async Task LogSafe(IIntegrationEventRepository repo, IntegrationEventDefinition def,
        int companyId, string actionType, string? executedSql, string? responseBody,
        bool success, string? error, long durationMs, CancellationToken ct)
    {
        try
        {
            await repo.AddLogAsync(new IntegrationEventLog
            {
                DefinitionId = def.Id,
                CompanyId = companyId,
                EventSource = def.EventSource,
                EventType = def.EventType,
                ExecutedSql = executedSql,
                ActionType = actionType,
                ResponseBody = responseBody,
                Success = success,
                ErrorMessage = error,
                DurationMs = durationMs
            }, ct);
        }
        catch { }
    }

    private static SqlDbType ParseSqlDbType(string? type) => type?.ToLowerInvariant() switch
    {
        "int" or "integer" => SqlDbType.Int,
        "bigint" => SqlDbType.BigInt,
        "bit" => SqlDbType.Bit,
        "decimal" or "numeric" => SqlDbType.Decimal,
        "float" => SqlDbType.Float,
        "datetime" or "datetime2" => SqlDbType.DateTime2,
        "date" => SqlDbType.Date,
        "uniqueidentifier" or "guid" => SqlDbType.UniqueIdentifier,
        _ => SqlDbType.NVarChar
    };

    private static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    // ── API Profile CRUD ─────────────────────────────────────────────────────
    public async Task<IReadOnlyCollection<IntegrationApiProfileDto>> GetApiProfilesAsync(int companyId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IIntegrationApiProfileRepository>();
        var profiles = await repo.GetByCompanyAsync(companyId, ct);
        return profiles.Select(p => new IntegrationApiProfileDto(
            p.Id, p.CompanyId, p.Name, p.AuthType, p.BaseUrl, p.AuthConfigJson, p.IsActive, p.CreatedAt, p.UpdatedAt
        )).ToArray();
    }

    public async Task SaveApiProfileAsync(int companyId, SaveIntegrationApiProfileRequest request, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IIntegrationApiProfileRepository>();

        IntegrationApiProfile profile;
        if (request.Id.HasValue)
        {
            profile = await repo.GetByIdAsync(request.Id.Value, ct)
                      ?? throw new InvalidOperationException("Profil bulunamadi.");
            profile.Name = request.Name;
            profile.AuthType = request.AuthType;
            profile.BaseUrl = request.BaseUrl;
            profile.AuthConfigJson = request.AuthConfigJson;
            profile.IsActive = request.IsActive;
            profile.UpdatedAt = DateTime.Now;
        }
        else
        {
            profile = new IntegrationApiProfile
            {
                CompanyId = companyId,
                Name = request.Name,
                AuthType = request.AuthType,
                BaseUrl = request.BaseUrl,
                AuthConfigJson = request.AuthConfigJson,
                IsActive = request.IsActive
            };
        }
        await repo.UpsertAsync(profile, ct);
    }

    public async Task DeleteApiProfileAsync(Guid id, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IIntegrationApiProfileRepository>();
        await repo.DeleteAsync(id, ct);
    }

    // ── Inner DTOs for JSON deserialization ──────────────────────────────────
    private sealed class ProcParamDef
    {
        public string? Name { get; set; }
        public string? Direction { get; set; }
        public string? Type { get; set; }
        public string? Source { get; set; }
        public string? Field { get; set; }
        public string? Value { get; set; }
    }

    private sealed class RestApiConfig
    {
        public string? BaseUrl { get; set; }
        public string? Endpoint { get; set; }
        public string? Method { get; set; }
        public string? AuthType { get; set; }
        public RestApiAuthConfig? AuthConfig { get; set; }
        public Dictionary<string, string>? Headers { get; set; }
        public string? BodyTemplate { get; set; }
        public string? SuccessPath { get; set; }
        public string? SuccessValue { get; set; }
    }

    private sealed class RestApiAuthConfig
    {
        public string? TokenEndpoint { get; set; }
        public string? TokenField { get; set; }
        public string? GrantType { get; set; }
        public Dictionary<string, string>? ExtraFields { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? Token { get; set; }
        public string? ApiKeyHeader { get; set; }
        public string? ApiKeyValue { get; set; }
    }

    private sealed class ApiDefinitionConfig
    {
        public Guid? ProfileId { get; set; }
        public string? Endpoint { get; set; }
        public string? Method { get; set; }
        public string? BodyTemplate { get; set; }
        public string? SuccessPath { get; set; }
        public string? SuccessValue { get; set; }
    }
}

public sealed class IntegrationEventException : Exception
{
    public IntegrationEventException(string message, Exception inner) : base(message, inner) { }
}
