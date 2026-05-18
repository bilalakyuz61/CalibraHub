using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlIntegrationDocCatalogRepository : IIntegrationDocCatalogRepository
{
    private readonly SqlServerConnectionFactory _conn;
    private readonly string _schema;

    public SqlIntegrationDocCatalogRepository(SqlServerConnectionFactory conn, CalibraDatabaseOptions opts)
    {
        _conn = conn;
        _schema = string.IsNullOrWhiteSpace(opts.Schema) ? "dbo" : opts.Schema.Trim();
    }

    private string S => _schema.Replace("]", "]]");

    // ── Provider ──────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<IntegrationProvider>> ListProvidersAsync(bool includeInactive, CancellationToken ct)
    {
        var list = new List<IntegrationProvider>();
        await using var c = await _conn.OpenConnectionAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[Code],[Label],[Description],[SourceInfo],[IconColor],[SortOrder],[IsActive],
                   [CreatedBy],[Created],[UpdatedBy],[Updated]
            FROM [{S}].[IntegrationProvider]
            {(includeInactive ? "" : "WHERE [IsActive] = 1")}
            ORDER BY [SortOrder], [Label];
            """;
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct)) list.Add(MapProvider(rd));
        return list;
    }

    public async Task<IntegrationProvider?> GetProviderByIdAsync(int id, CancellationToken ct)
    {
        await using var c = await _conn.OpenConnectionAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT [Id],[Code],[Label],[Description],[SourceInfo],[IconColor],[SortOrder],[IsActive],[CreatedBy],[Created],[UpdatedBy],[Updated] FROM [{S}].[IntegrationProvider] WHERE [Id] = @Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        return await rd.ReadAsync(ct) ? MapProvider(rd) : null;
    }

    public async Task<IntegrationProvider?> GetProviderByCodeAsync(string code, CancellationToken ct)
    {
        await using var c = await _conn.OpenConnectionAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT TOP 1 [Id],[Code],[Label],[Description],[SourceInfo],[IconColor],[SortOrder],[IsActive],[CreatedBy],[Created],[UpdatedBy],[Updated] FROM [{S}].[IntegrationProvider] WHERE [Code] = @Code AND [IsActive] = 1;";
        cmd.Parameters.Add(new SqlParameter("@Code", code));
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        return await rd.ReadAsync(ct) ? MapProvider(rd) : null;
    }

    public async Task<int> UpsertProviderAsync(IntegrationProvider entity, string? actor, CancellationToken ct)
    {
        await using var c = await _conn.OpenConnectionAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = entity.Id == 0
            ? $"""
                INSERT INTO [{S}].[IntegrationProvider]([Code],[Label],[Description],[SourceInfo],[IconColor],[SortOrder],[IsActive],[CreatedBy])
                OUTPUT INSERTED.[Id]
                VALUES (@Code,@Label,@Description,@SourceInfo,@IconColor,@SortOrder,@IsActive,@Actor);
                """
            : $"""
                UPDATE [{S}].[IntegrationProvider]
                SET [Code]=@Code,[Label]=@Label,[Description]=@Description,[SourceInfo]=@SourceInfo,
                    [IconColor]=@IconColor,[SortOrder]=@SortOrder,[IsActive]=@IsActive,
                    [UpdatedBy]=@Actor,[Updated]=SYSUTCDATETIME()
                OUTPUT INSERTED.[Id]
                WHERE [Id]=@Id;
                """;
        if (entity.Id != 0) cmd.Parameters.Add(new SqlParameter("@Id", entity.Id));
        cmd.Parameters.Add(new SqlParameter("@Code", entity.Code));
        cmd.Parameters.Add(new SqlParameter("@Label", entity.Label));
        cmd.Parameters.Add(new SqlParameter("@Description", (object?)entity.Description ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@SourceInfo", (object?)entity.SourceInfo ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@IconColor", (object?)entity.IconColor ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@SortOrder", entity.SortOrder));
        cmd.Parameters.Add(new SqlParameter("@IsActive", entity.IsActive));
        cmd.Parameters.Add(new SqlParameter("@Actor", (object?)actor ?? DBNull.Value));
        var newId = (int)(await cmd.ExecuteScalarAsync(ct) ?? 0);
        return newId == 0 ? entity.Id : newId;
    }

    public async Task DeleteProviderAsync(int id, string? actor, CancellationToken ct)
    {
        await using var c = await _conn.OpenConnectionAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"UPDATE [{S}].[IntegrationProvider] SET [IsActive]=0,[UpdatedBy]=@Actor,[Updated]=SYSUTCDATETIME() WHERE [Id]=@Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        cmd.Parameters.Add(new SqlParameter("@Actor", (object?)actor ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Enum Definition ───────────────────────────────────────────────────

    public async Task<IReadOnlyList<IntegrationEnumDefinition>> ListEnumsAsync(int? providerId, bool includeInactive, CancellationToken ct)
    {
        var enums = new Dictionary<int, IntegrationEnumDefinition>();
        await using var c = await _conn.OpenConnectionAsync(ct);

        var where = (providerId.HasValue ? "WHERE [ProviderId]=@P" : "") +
                    (!includeInactive
                        ? (providerId.HasValue ? " AND [IsActive]=1" : "WHERE [IsActive]=1")
                        : "");
        await using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT [Id],[ProviderId],[Code],[Label],[Description],[SourceInfo],[IsActive],[CreatedBy],[Created],[UpdatedBy],[Updated],[UsedInFieldPaths]
                FROM [{S}].[IntegrationEnumDefinition] {where} ORDER BY [Code];
                """;
            if (providerId.HasValue) cmd.Parameters.Add(new SqlParameter("@P", providerId.Value));
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct)) { var e = MapEnum(rd); enums[e.Id] = e; }
        }

        if (enums.Count == 0) return Array.Empty<IntegrationEnumDefinition>();

        // Values
        await using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT [Id],[EnumDefinitionId],[Value],[Label],[TechnicalCode],[Description],[SortOrder],[IsActive]
                FROM [{S}].[IntegrationEnumValue]
                WHERE [EnumDefinitionId] IN (SELECT value FROM STRING_SPLIT(@Ids, ',')) AND [IsActive]=1
                ORDER BY [EnumDefinitionId],[SortOrder];
                """;
            cmd.Parameters.Add(new SqlParameter("@Ids", string.Join(',', enums.Keys)));
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                var v = MapEnumValue(rd);
                if (enums.TryGetValue(v.EnumDefinitionId, out var def)) def.Values.Add(v);
            }
        }

        return enums.Values.OrderBy(e => e.Code, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<IntegrationEnumDefinition?> GetEnumByIdAsync(int id, CancellationToken ct)
    {
        await using var c = await _conn.OpenConnectionAsync(ct);
        IntegrationEnumDefinition? def = null;
        await using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = $"SELECT [Id],[ProviderId],[Code],[Label],[Description],[SourceInfo],[IsActive],[CreatedBy],[Created],[UpdatedBy],[Updated],[UsedInFieldPaths] FROM [{S}].[IntegrationEnumDefinition] WHERE [Id]=@Id;";
            cmd.Parameters.Add(new SqlParameter("@Id", id));
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            if (await rd.ReadAsync(ct)) def = MapEnum(rd);
        }
        if (def == null) return null;

        await using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = $"SELECT [Id],[EnumDefinitionId],[Value],[Label],[TechnicalCode],[Description],[SortOrder],[IsActive] FROM [{S}].[IntegrationEnumValue] WHERE [EnumDefinitionId]=@Id ORDER BY [SortOrder];";
            cmd.Parameters.Add(new SqlParameter("@Id", id));
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct)) def.Values.Add(MapEnumValue(rd));
        }
        return def;
    }

    public async Task<IntegrationEnumDefinition?> GetEnumByCodeAsync(int providerId, string code, CancellationToken ct)
    {
        await using var c = await _conn.OpenConnectionAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT TOP 1 [Id] FROM [{S}].[IntegrationEnumDefinition] WHERE [ProviderId]=@P AND [Code]=@C AND [IsActive]=1;";
        cmd.Parameters.Add(new SqlParameter("@P", providerId));
        cmd.Parameters.Add(new SqlParameter("@C", code));
        var id = (int)(await cmd.ExecuteScalarAsync(ct) ?? 0);
        return id == 0 ? null : await GetEnumByIdAsync(id, ct);
    }

    public async Task<int> UpsertEnumAsync(IntegrationEnumDefinition entity, string? actor, CancellationToken ct)
    {
        await using var c = await _conn.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct);

        int id = entity.Id;
        await using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = entity.Id == 0
                ? $"INSERT INTO [{S}].[IntegrationEnumDefinition]([ProviderId],[Code],[Label],[Description],[SourceInfo],[IsActive],[UsedInFieldPaths],[CreatedBy]) OUTPUT INSERTED.[Id] VALUES (@P,@C,@L,@D,@S,@A,@FP,@U);"
                : $"UPDATE [{S}].[IntegrationEnumDefinition] SET [ProviderId]=@P,[Code]=@C,[Label]=@L,[Description]=@D,[SourceInfo]=@S,[IsActive]=@A,[UsedInFieldPaths]=@FP,[UpdatedBy]=@U,[Updated]=SYSUTCDATETIME() OUTPUT INSERTED.[Id] WHERE [Id]=@Id;";
            if (entity.Id != 0) cmd.Parameters.Add(new SqlParameter("@Id", entity.Id));
            cmd.Parameters.Add(new SqlParameter("@P", entity.ProviderId));
            cmd.Parameters.Add(new SqlParameter("@C", entity.Code));
            cmd.Parameters.Add(new SqlParameter("@L", entity.Label));
            cmd.Parameters.Add(new SqlParameter("@D", (object?)entity.Description ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@S", (object?)entity.SourceInfo ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@A", entity.IsActive));
            cmd.Parameters.Add(new SqlParameter("@FP", (object?)entity.UsedInFieldPaths ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@U", (object?)actor ?? DBNull.Value));
            id = (int)(await cmd.ExecuteScalarAsync(ct) ?? 0);
        }

        // Replace all values
        await using (var del = c.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = $"DELETE FROM [{S}].[IntegrationEnumValue] WHERE [EnumDefinitionId]=@Id;";
            del.Parameters.Add(new SqlParameter("@Id", id));
            await del.ExecuteNonQueryAsync(ct);
        }

        foreach (var v in entity.Values)
        {
            await using var ins = c.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = $"INSERT INTO [{S}].[IntegrationEnumValue]([EnumDefinitionId],[Value],[Label],[TechnicalCode],[Description],[SortOrder],[IsActive],[CreatedBy]) VALUES (@E,@V,@L,@T,@D,@S,@A,@U);";
            ins.Parameters.Add(new SqlParameter("@E", id));
            ins.Parameters.Add(new SqlParameter("@V", v.Value));
            ins.Parameters.Add(new SqlParameter("@L", v.Label));
            ins.Parameters.Add(new SqlParameter("@T", (object?)v.TechnicalCode ?? DBNull.Value));
            ins.Parameters.Add(new SqlParameter("@D", (object?)v.Description ?? DBNull.Value));
            ins.Parameters.Add(new SqlParameter("@S", v.SortOrder));
            ins.Parameters.Add(new SqlParameter("@A", v.IsActive));
            ins.Parameters.Add(new SqlParameter("@U", (object?)actor ?? DBNull.Value));
            await ins.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return id;
    }

    public async Task DeleteEnumAsync(int id, string? actor, CancellationToken ct)
    {
        await using var c = await _conn.OpenConnectionAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"UPDATE [{S}].[IntegrationEnumDefinition] SET [IsActive]=0,[UpdatedBy]=@A,[Updated]=SYSUTCDATETIME() WHERE [Id]=@Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        cmd.Parameters.Add(new SqlParameter("@A", (object?)actor ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Field Doc ─────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<IntegrationFieldDoc>> ListFieldDocsAsync(int? providerId, string? resource, bool includeInactive, CancellationToken ct)
    {
        var list = new List<IntegrationFieldDoc>();
        await using var c = await _conn.OpenConnectionAsync(ct);
        await using var cmd = c.CreateCommand();
        var wheres = new List<string>();
        if (providerId.HasValue)            wheres.Add("[ProviderId]=@P");
        if (!string.IsNullOrWhiteSpace(resource)) wheres.Add("[Resource]=@R");
        if (!includeInactive)               wheres.Add("[IsActive]=1");
        var where = wheres.Count == 0 ? "" : "WHERE " + string.Join(" AND ", wheres);
        cmd.CommandText = $"""
            SELECT [Id],[ProviderId],[Resource],[FieldPath],[Label],[Description],[Example],[Notes],
                   [EnumDefinitionId],[IsRequired],[SortOrder],[IsActive],[CreatedBy],[Created],[UpdatedBy],[Updated]
            FROM [{S}].[IntegrationFieldDoc] {where} ORDER BY [Resource],[SortOrder],[FieldPath];
            """;
        if (providerId.HasValue) cmd.Parameters.Add(new SqlParameter("@P", providerId.Value));
        if (!string.IsNullOrWhiteSpace(resource)) cmd.Parameters.Add(new SqlParameter("@R", resource));
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct)) list.Add(MapFieldDoc(rd));
        return list;
    }

    public async Task<IntegrationFieldDoc?> GetFieldDocByIdAsync(int id, CancellationToken ct)
    {
        await using var c = await _conn.OpenConnectionAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT [Id],[ProviderId],[Resource],[FieldPath],[Label],[Description],[Example],[Notes],[EnumDefinitionId],[IsRequired],[SortOrder],[IsActive],[CreatedBy],[Created],[UpdatedBy],[Updated] FROM [{S}].[IntegrationFieldDoc] WHERE [Id]=@Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        return await rd.ReadAsync(ct) ? MapFieldDoc(rd) : null;
    }

    public async Task<IntegrationFieldDoc?> GetFieldDocByPathAsync(int providerId, string resource, string fieldPath, CancellationToken ct)
    {
        await using var c = await _conn.OpenConnectionAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT TOP 1 [Id],[ProviderId],[Resource],[FieldPath],[Label],[Description],[Example],[Notes],[EnumDefinitionId],[IsRequired],[SortOrder],[IsActive],[CreatedBy],[Created],[UpdatedBy],[Updated] FROM [{S}].[IntegrationFieldDoc] WHERE [ProviderId]=@P AND [Resource]=@R AND [FieldPath]=@F AND [IsActive]=1;";
        cmd.Parameters.Add(new SqlParameter("@P", providerId));
        cmd.Parameters.Add(new SqlParameter("@R", resource));
        cmd.Parameters.Add(new SqlParameter("@F", fieldPath));
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        return await rd.ReadAsync(ct) ? MapFieldDoc(rd) : null;
    }

    public async Task<int> UpsertFieldDocAsync(IntegrationFieldDoc entity, string? actor, CancellationToken ct)
    {
        await using var c = await _conn.OpenConnectionAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = entity.Id == 0
            ? $"INSERT INTO [{S}].[IntegrationFieldDoc]([ProviderId],[Resource],[FieldPath],[Label],[Description],[Example],[Notes],[EnumDefinitionId],[IsRequired],[SortOrder],[IsActive],[CreatedBy]) OUTPUT INSERTED.[Id] VALUES (@P,@R,@F,@L,@D,@E,@N,@En,@Req,@S,@A,@U);"
            : $"UPDATE [{S}].[IntegrationFieldDoc] SET [ProviderId]=@P,[Resource]=@R,[FieldPath]=@F,[Label]=@L,[Description]=@D,[Example]=@E,[Notes]=@N,[EnumDefinitionId]=@En,[IsRequired]=@Req,[SortOrder]=@S,[IsActive]=@A,[UpdatedBy]=@U,[Updated]=SYSUTCDATETIME() OUTPUT INSERTED.[Id] WHERE [Id]=@Id;";
        if (entity.Id != 0) cmd.Parameters.Add(new SqlParameter("@Id", entity.Id));
        cmd.Parameters.Add(new SqlParameter("@P", entity.ProviderId));
        cmd.Parameters.Add(new SqlParameter("@R", entity.Resource));
        cmd.Parameters.Add(new SqlParameter("@F", entity.FieldPath));
        cmd.Parameters.Add(new SqlParameter("@L", (object?)entity.Label ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@D", (object?)entity.Description ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@E", (object?)entity.Example ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@N", (object?)entity.Notes ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@En", (object?)entity.EnumDefinitionId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Req", entity.IsRequired));
        cmd.Parameters.Add(new SqlParameter("@S", entity.SortOrder));
        cmd.Parameters.Add(new SqlParameter("@A", entity.IsActive));
        cmd.Parameters.Add(new SqlParameter("@U", (object?)actor ?? DBNull.Value));
        var newId = (int)(await cmd.ExecuteScalarAsync(ct) ?? 0);
        return newId == 0 ? entity.Id : newId;
    }

    public async Task DeleteFieldDocAsync(int id, string? actor, CancellationToken ct)
    {
        await using var c = await _conn.OpenConnectionAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"UPDATE [{S}].[IntegrationFieldDoc] SET [IsActive]=0,[UpdatedBy]=@A,[Updated]=SYSUTCDATETIME() WHERE [Id]=@Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        cmd.Parameters.Add(new SqlParameter("@A", (object?)actor ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Mapping ───────────────────────────────────────────────────────────

    private static IntegrationProvider MapProvider(SqlDataReader r) => new()
    {
        Id          = r.GetInt32(0),
        Code        = r.GetString(1),
        Label       = r.GetString(2),
        Description = r.IsDBNull(3) ? null : r.GetString(3),
        SourceInfo  = r.IsDBNull(4) ? null : r.GetString(4),
        IconColor   = r.IsDBNull(5) ? null : r.GetString(5),
        SortOrder   = r.GetInt32(6),
        IsActive    = r.GetBoolean(7),
        CreatedBy   = r.IsDBNull(8) ? null : r.GetString(8),
        Created     = r.GetDateTime(9),
        UpdatedBy   = r.IsDBNull(10) ? null : r.GetString(10),
        Updated     = r.IsDBNull(11) ? null : r.GetDateTime(11),
    };

    private static IntegrationEnumDefinition MapEnum(SqlDataReader r) => new()
    {
        Id          = r.GetInt32(0),
        ProviderId  = r.GetInt32(1),
        Code        = r.GetString(2),
        Label       = r.GetString(3),
        Description = r.IsDBNull(4) ? null : r.GetString(4),
        SourceInfo  = r.IsDBNull(5) ? null : r.GetString(5),
        IsActive    = r.GetBoolean(6),
        CreatedBy   = r.IsDBNull(7) ? null : r.GetString(7),
        Created     = r.GetDateTime(8),
        UpdatedBy   = r.IsDBNull(9) ? null : r.GetString(9),
        Updated     = r.IsDBNull(10) ? null : r.GetDateTime(10),
        UsedInFieldPaths = r.IsDBNull(11) ? null : r.GetString(11),
    };

    private static IntegrationEnumValue MapEnumValue(SqlDataReader r) => new()
    {
        Id               = r.GetInt32(0),
        EnumDefinitionId = r.GetInt32(1),
        Value            = r.GetString(2),
        Label            = r.GetString(3),
        TechnicalCode    = r.IsDBNull(4) ? null : r.GetString(4),
        Description      = r.IsDBNull(5) ? null : r.GetString(5),
        SortOrder        = r.GetInt32(6),
        IsActive         = r.GetBoolean(7),
    };

    private static IntegrationFieldDoc MapFieldDoc(SqlDataReader r) => new()
    {
        Id               = r.GetInt32(0),
        ProviderId       = r.GetInt32(1),
        Resource         = r.GetString(2),
        FieldPath        = r.GetString(3),
        Label            = r.IsDBNull(4)  ? null : r.GetString(4),
        Description      = r.IsDBNull(5)  ? null : r.GetString(5),
        Example          = r.IsDBNull(6)  ? null : r.GetString(6),
        Notes            = r.IsDBNull(7)  ? null : r.GetString(7),
        EnumDefinitionId = r.IsDBNull(8)  ? null : r.GetInt32(8),
        IsRequired       = r.GetBoolean(9),
        SortOrder        = r.GetInt32(10),
        IsActive         = r.GetBoolean(11),
        CreatedBy        = r.IsDBNull(12) ? null : r.GetString(12),
        Created          = r.GetDateTime(13),
        UpdatedBy        = r.IsDBNull(14) ? null : r.GetString(14),
        Updated          = r.IsDBNull(15) ? null : r.GetDateTime(15),
    };
}
