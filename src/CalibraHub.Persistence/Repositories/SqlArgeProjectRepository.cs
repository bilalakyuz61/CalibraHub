using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// AR-GE/ÜR-GE companion (ArgeProject) veri erisimi. Tablolar CalibraDatabaseInitializer.EnsureArgeTablesAsync
/// tarafindan kurulur. Per-company DB: SqlServerConnectionFactory.
/// FK kasing: Document PK [id] (kucuk harf), Personnel/ArgePrototype PK [Id].
/// </summary>
public sealed class SqlArgeProjectRepository : IArgeProjectRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _argeTable;
    private readonly string _docTable;
    private readonly string _protoTable;
    private readonly string _personnelTable;
    private readonly string _linkTable;
    private readonly string _itemsTable;
    private readonly string _bomTable;
    private readonly string _routingTable;
    private readonly string _woTable;
    private readonly string _woOpTable;
    private readonly string _operationTable;
    private readonly string _stockDocTable;
    private readonly string _stockDocLineTable;

    public SqlArgeProjectRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options)
    {
        _connectionFactory = factory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        var s = schema.Replace("]", "]]");
        _argeTable = $"[{s}].[ArgeProject]";
        _docTable = $"[{s}].[Document]";
        _protoTable = $"[{s}].[ArgePrototype]";
        _personnelTable = $"[{s}].[Personnel]";
        _linkTable = $"[{s}].[ArgeProductionLink]";
        _itemsTable = $"[{s}].[Items]";
        _bomTable = $"[{s}].[BOM]";
        _routingTable = $"[{s}].[Routing]";
        _woTable = $"[{s}].[WorkOrder]";
        _woOpTable = $"[{s}].[WorkOrderOperation]";
        _operationTable = $"[{s}].[Operation]";
        _stockDocTable = $"[{s}].[stock_doc]";
        _stockDocLineTable = $"[{s}].[stock_doc_line]";
    }

    public async Task<IReadOnlyCollection<ArgeProjectListItem>> ListAsync(string? search, byte? status, CancellationToken ct)
    {
        var where = "d.[IsActive] = 1";
        if (status.HasValue) where += " AND a.[Status] = @Status";
        if (!string.IsNullOrWhiteSpace(search)) where += " AND (d.[DocumentNumber] LIKE @S OR a.[Name] LIKE @S OR p.[FullName] LIKE @S)";

        var sql = $"""
            SELECT a.[DocumentId], d.[DocumentNumber], a.[Name], a.[ProjectType], a.[Status],
                   a.[OwnerPersonnelId], p.[FullName] AS [OwnerName],
                   a.[TargetDate], a.[ProgressPercent], a.[Description], a.[Created],
                   (SELECT COUNT(*) FROM {_protoTable} pr WHERE pr.[ProjectId] = a.[DocumentId] AND pr.[IsActive] = 1) AS [PrototypeCount]
            FROM {_argeTable} a
            INNER JOIN {_docTable} d ON d.[id] = a.[DocumentId]
            LEFT JOIN {_personnelTable} p ON p.[Id] = a.[OwnerPersonnelId]
            WHERE {where}
            ORDER BY a.[Created] DESC;
            """;

        var list = new List<ArgeProjectListItem>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        if (status.HasValue) cmd.Parameters.Add(new SqlParameter("@Status", status.Value));
        if (!string.IsNullOrWhiteSpace(search)) cmd.Parameters.Add(new SqlParameter("@S", $"%{search.Trim()}%"));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new ArgeProjectListItem(
                DocumentId: r.GetInt32(0),
                DocumentNumber: r.GetString(1),
                Name: r.GetString(2),
                ProjectType: r.GetByte(3),
                Status: r.GetByte(4),
                OwnerPersonnelId: r.IsDBNull(5) ? null : r.GetInt32(5),
                OwnerName: r.IsDBNull(6) ? null : r.GetString(6),
                TargetDate: r.IsDBNull(7) ? null : r.GetDateTime(7),
                ProgressPercent: r.GetDecimal(8),
                Description: r.IsDBNull(9) ? null : r.GetString(9),
                Created: r.GetDateTime(10),
                PrototypeCount: r.GetInt32(11)));
        }
        return list;
    }

    public async Task<ArgeProject?> GetByDocumentIdAsync(int documentId, CancellationToken ct)
    {
        var sql = $"""
            SELECT [Id],[DocumentId],[Name],[Status],[ProjectType],[OwnerPersonnelId],[TargetDate],
                   [ProgressPercent],[Description],[CreatedById],[Created],[UpdatedById],[Updated]
            FROM {_argeTable} WHERE [DocumentId] = @Doc;
            """;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@Doc", documentId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new ArgeProject
        {
            Id = r.GetInt32(0),
            DocumentId = r.GetInt32(1),
            Name = r.GetString(2),
            Status = (ArgeProjectStatus)r.GetByte(3),
            ProjectType = (ArgeProjectType)r.GetByte(4),
            OwnerPersonnelId = r.IsDBNull(5) ? null : r.GetInt32(5),
            TargetDate = r.IsDBNull(6) ? null : r.GetDateTime(6),
            ProgressPercent = r.GetDecimal(7),
            Description = r.IsDBNull(8) ? null : r.GetString(8),
            CreatedById = r.IsDBNull(9) ? null : r.GetInt32(9),
            Created = r.GetDateTime(10),
            UpdatedById = r.IsDBNull(11) ? null : r.GetInt32(11),
            Updated = r.IsDBNull(12) ? null : r.GetDateTime(12),
        };
    }

    public async Task UpsertCompanionAsync(ArgeProject p, CancellationToken ct)
    {
        var sql = $"""
            IF EXISTS (SELECT 1 FROM {_argeTable} WHERE [DocumentId] = @Doc)
                UPDATE {_argeTable} SET
                    [Name]=@Name, [Status]=@Status, [ProjectType]=@Type, [OwnerPersonnelId]=@Owner,
                    [TargetDate]=@Target, [ProgressPercent]=@Progress, [Description]=@Desc,
                    [UpdatedById]=@Upd, [Updated]=SYSUTCDATETIME()
                WHERE [DocumentId]=@Doc;
            ELSE
                INSERT INTO {_argeTable}
                    ([DocumentId],[Name],[Status],[ProjectType],[OwnerPersonnelId],[TargetDate],[ProgressPercent],[Description],[CreatedById],[Created])
                VALUES
                    (@Doc,@Name,@Status,@Type,@Owner,@Target,@Progress,@Desc,@Cre,SYSUTCDATETIME());
            """;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@Doc", p.DocumentId));
        cmd.Parameters.Add(new SqlParameter("@Name", p.Name));
        cmd.Parameters.Add(new SqlParameter("@Status", (byte)p.Status));
        cmd.Parameters.Add(new SqlParameter("@Type", (byte)p.ProjectType));
        cmd.Parameters.Add(new SqlParameter("@Owner", (object?)p.OwnerPersonnelId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Target", (object?)p.TargetDate ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Progress", p.ProgressPercent));
        cmd.Parameters.Add(new SqlParameter("@Desc", (object?)p.Description ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Upd", (object?)p.UpdatedById ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Cre", (object?)p.CreatedById ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> UpdateStatusAsync(int documentId, byte status, int? updatedById, CancellationToken ct)
    {
        var sql = $"""
            UPDATE {_argeTable} SET [Status]=@Status, [UpdatedById]=@Upd, [Updated]=SYSUTCDATETIME()
            WHERE [DocumentId]=@Doc;
            """;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@Status", status));
        cmd.Parameters.Add(new SqlParameter("@Upd", (object?)updatedById ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Doc", documentId));
        var rows = await cmd.ExecuteNonQueryAsync(ct);
        return rows > 0;
    }

    public async Task<IReadOnlyCollection<ArgePersonnelOption>> GetPersonnelAsync(CancellationToken ct)
    {
        var list = new List<ArgePersonnelOption>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT [Id],[FullName] FROM {_personnelTable} WHERE [IsActive] = 1 ORDER BY [FullName];";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new ArgePersonnelOption(r.GetInt32(0), r.GetString(1)));
        return list;
    }

    public async Task<bool> IsTransferredAsync(int documentId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT TOP 1 1 FROM {_linkTable} WHERE [ArgeProjectId] = @Doc;";
        cmd.Parameters.Add(new SqlParameter("@Doc", documentId));
        var r = await cmd.ExecuteScalarAsync(ct);
        return r != null && r != DBNull.Value;
    }

    public async Task AddProductionLinkAsync(int documentId, int itemId, int version, int? createdById, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {_linkTable} ([ArgeProjectId],[ItemId],[Version],[CreatedById],[Created])
            VALUES (@Doc,@Item,@Ver,@Cre,SYSUTCDATETIME());
            """;
        cmd.Parameters.Add(new SqlParameter("@Doc", documentId));
        cmd.Parameters.Add(new SqlParameter("@Item", itemId));
        cmd.Parameters.Add(new SqlParameter("@Ver", version));
        cmd.Parameters.Add(new SqlParameter("@Cre", (object?)createdById ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Prototip CRUD ────────────────────────────────────────────────────────

    public async Task<IReadOnlyCollection<ArgePrototypeDto>> ListPrototypesAsync(int projectId, CancellationToken ct)
    {
        var sql = $"""
            SELECT pr.[Id], pr.[ProjectId], pr.[Name], pr.[Description], pr.[VersionLabel], pr.[ItemId],
                   it.[Code] AS [ItemCode], it.[Name] AS [ItemName], pr.[IsApproved],
                   CAST(CASE WHEN pr.[ItemId] IS NOT NULL
                             AND EXISTS (SELECT 1 FROM {_bomTable} b WHERE b.[ItemId] = pr.[ItemId])
                        THEN 1 ELSE 0 END AS BIT) AS [HasBom],
                   CAST(CASE WHEN pr.[ItemId] IS NOT NULL
                             AND EXISTS (SELECT 1 FROM {_routingTable} rt WHERE rt.[ItemId] = pr.[ItemId] AND rt.[IsActive] = 1)
                        THEN 1 ELSE 0 END AS BIT) AS [HasRouting],
                   pr.[Created]
            FROM {_protoTable} pr
            LEFT JOIN {_itemsTable} it ON it.[Id] = pr.[ItemId]
            WHERE pr.[ProjectId] = @Proj AND pr.[IsActive] = 1
            ORDER BY pr.[IsApproved] DESC, pr.[Created] DESC;
            """;
        var list = new List<ArgePrototypeDto>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@Proj", projectId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new ArgePrototypeDto(
                Id: r.GetInt32(0),
                ProjectId: r.GetInt32(1),
                Name: r.GetString(2),
                Description: r.IsDBNull(3) ? null : r.GetString(3),
                VersionLabel: r.IsDBNull(4) ? null : r.GetString(4),
                ItemId: r.IsDBNull(5) ? null : r.GetInt32(5),
                ItemCode: r.IsDBNull(6) ? null : r.GetString(6),
                ItemName: r.IsDBNull(7) ? null : r.GetString(7),
                IsApproved: r.GetBoolean(8),
                HasBom: r.GetBoolean(9),
                HasRouting: r.GetBoolean(10),
                Created: r.GetDateTime(11)));
        }
        return list;
    }

    public async Task<ArgePrototype?> GetPrototypeAsync(int prototypeId, CancellationToken ct)
    {
        var sql = $"""
            SELECT [Id],[ProjectId],[Name],[Description],[VersionLabel],[ItemId],[IsApproved],[IsActive],
                   [CreatedById],[Created],[UpdatedById],[Updated]
            FROM {_protoTable} WHERE [Id] = @Id;
            """;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@Id", prototypeId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new ArgePrototype
        {
            Id = r.GetInt32(0),
            ProjectId = r.GetInt32(1),
            Name = r.GetString(2),
            Description = r.IsDBNull(3) ? null : r.GetString(3),
            VersionLabel = r.IsDBNull(4) ? null : r.GetString(4),
            ItemId = r.IsDBNull(5) ? null : r.GetInt32(5),
            IsApproved = r.GetBoolean(6),
            IsActive = r.GetBoolean(7),
            CreatedById = r.IsDBNull(8) ? null : r.GetInt32(8),
            Created = r.GetDateTime(9),
            UpdatedById = r.IsDBNull(10) ? null : r.GetInt32(10),
            Updated = r.IsDBNull(11) ? null : r.GetDateTime(11),
        };
    }

    public async Task<int> UpsertPrototypeAsync(ArgePrototype p, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        if (p.Id > 0)
        {
            cmd.CommandText = $"""
                UPDATE {_protoTable} SET
                    [Name]=@Name, [Description]=@Desc, [VersionLabel]=@Ver,
                    [UpdatedById]=@Upd, [Updated]=SYSUTCDATETIME()
                WHERE [Id]=@Id;
                SELECT @Id;
                """;
            cmd.Parameters.Add(new SqlParameter("@Id", p.Id));
            cmd.Parameters.Add(new SqlParameter("@Upd", (object?)p.UpdatedById ?? DBNull.Value));
        }
        else
        {
            cmd.CommandText = $"""
                INSERT INTO {_protoTable} ([ProjectId],[Name],[Description],[VersionLabel],[CreatedById],[Created])
                VALUES (@Proj,@Name,@Desc,@Ver,@Cre,SYSUTCDATETIME());
                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """;
            cmd.Parameters.Add(new SqlParameter("@Proj", p.ProjectId));
            cmd.Parameters.Add(new SqlParameter("@Cre", (object?)p.CreatedById ?? DBNull.Value));
        }
        cmd.Parameters.Add(new SqlParameter("@Name", p.Name));
        cmd.Parameters.Add(new SqlParameter("@Desc", (object?)p.Description ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Ver", (object?)p.VersionLabel ?? DBNull.Value));
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is int id ? id : p.Id;
    }

    public async Task SoftDeletePrototypeAsync(int prototypeId, int? updatedById, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_protoTable} SET [IsActive]=0, [UpdatedById]=@Upd, [Updated]=SYSUTCDATETIME()
            WHERE [Id]=@Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", prototypeId));
        cmd.Parameters.Add(new SqlParameter("@Upd", (object?)updatedById ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task LinkPrototypeItemAsync(int prototypeId, int itemId, int? updatedById, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_protoTable} SET [ItemId]=@Item, [UpdatedById]=@Upd, [Updated]=SYSUTCDATETIME()
            WHERE [Id]=@Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Item", itemId));
        cmd.Parameters.Add(new SqlParameter("@Id", prototypeId));
        cmd.Parameters.Add(new SqlParameter("@Upd", (object?)updatedById ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SetPrototypeApprovedAsync(int prototypeId, int projectId, bool approved, int? updatedById, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        // Onaylanan prototip klon kaynagidir → proje basina tek onayli (digerlerini sifirla).
        cmd.CommandText = approved
            ? $"""
                UPDATE {_protoTable} SET [IsApproved]=0, [UpdatedById]=@Upd, [Updated]=SYSUTCDATETIME()
                WHERE [ProjectId]=@Proj AND [Id]<>@Id;
                UPDATE {_protoTable} SET [IsApproved]=1, [UpdatedById]=@Upd, [Updated]=SYSUTCDATETIME()
                WHERE [Id]=@Id;
                """
            : $"""
                UPDATE {_protoTable} SET [IsApproved]=0, [UpdatedById]=@Upd, [Updated]=SYSUTCDATETIME()
                WHERE [Id]=@Id;
                """;
        cmd.Parameters.Add(new SqlParameter("@Id", prototypeId));
        cmd.Parameters.Add(new SqlParameter("@Proj", projectId));
        cmd.Parameters.Add(new SqlParameter("@Upd", (object?)updatedById ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Faz 3: is emri ↔ proje + isçilik maliyeti ───────────────────────────

    public async Task<int?> FindProjectIdByItemAsync(int itemId, CancellationToken ct)
    {
        // Once seri Item (ArgeProductionLink), yoksa prototip Item (ArgePrototype, en guncel aktif).
        var sql = $"""
            SELECT COALESCE(
                (SELECT TOP 1 [ArgeProjectId] FROM {_linkTable} WHERE [ItemId] = @Item),
                (SELECT TOP 1 [ProjectId] FROM {_protoTable} WHERE [ItemId] = @Item AND [IsActive] = 1 ORDER BY [Id] DESC)
            );
            """;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@Item", itemId));
        var r = await cmd.ExecuteScalarAsync(ct);
        return r != null && r != DBNull.Value ? Convert.ToInt32(r) : null;
    }

    public async Task<ArgeProjectLaborDto> GetProjectLaborAsync(int projectId, CancellationToken ct)
    {
        // Sure -> saat: DurationUnit 2=Saat (oldugu gibi), aksi (1=Dakika) /60. Gerçekleşen yoksa planlanan.
        // LaborCost yalnizca HourlyRate tanimli operasyonlari toplar (NULL rate -> 0 katki, SUM yok sayar).
        var sql = $"""
            SELECT
                COUNT(DISTINCT wo.[Id]) AS WorkOrderCount,
                COUNT(woo.[Id])        AS OperationCount,
                ISNULL(SUM(
                    CASE WHEN woo.[DurationUnit] = 2 THEN COALESCE(woo.[ActualDuration], woo.[PlannedDuration])
                         ELSE COALESCE(woo.[ActualDuration], woo.[PlannedDuration]) / 60.0 END
                ), 0) AS LaborHours,
                ISNULL(SUM(
                    (CASE WHEN woo.[DurationUnit] = 2 THEN COALESCE(woo.[ActualDuration], woo.[PlannedDuration])
                          ELSE COALESCE(woo.[ActualDuration], woo.[PlannedDuration]) / 60.0 END) * op.[HourlyRate]
                ), 0) AS LaborCost
            FROM {_woTable} wo
            INNER JOIN {_woOpTable} woo ON woo.[WorkOrderId] = wo.[Id]
            INNER JOIN {_operationTable} op ON op.[Id] = woo.[OperationId]
            WHERE wo.[ArgeProjectId] = @Proj AND wo.[IsActive] = 1;
            """;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@Proj", projectId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct))
            return new ArgeProjectLaborDto(0m, 0m, 0, 0);
        return new ArgeProjectLaborDto(
            LaborCost: r.IsDBNull(3) ? 0m : Convert.ToDecimal(r.GetValue(3)),
            LaborHours: r.IsDBNull(2) ? 0m : Convert.ToDecimal(r.GetValue(2)),
            WorkOrderCount: r.GetInt32(0),
            OperationCount: r.GetInt32(1));
    }

    public async Task<ArgeProjectMaterialDto> GetProjectMaterialAsync(int projectId, CancellationToken ct)
    {
        var sql = $"""
            SELECT
                COUNT(DISTINCT sd.[id]) AS DocCount,
                COUNT(sdl.[id])         AS LineCount,
                ISNULL(SUM(sdl.[qty] * ISNULL(sdl.[unit_cost], 0)), 0) AS MaterialCost
            FROM {_stockDocTable} sd
            INNER JOIN {_stockDocLineTable} sdl ON sdl.[stock_doc_id] = sd.[id]
            WHERE sd.[arge_project_id] = @Proj
              AND sd.[doc_type] = 'STOCK_OUT'
              AND sd.[is_active] = 1
              AND sdl.[is_active] = 1;
            """;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@Proj", projectId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct))
            return new ArgeProjectMaterialDto(0m, 0, 0);
        return new ArgeProjectMaterialDto(
            MaterialCost: r.IsDBNull(2) ? 0m : Convert.ToDecimal(r.GetValue(2)),
            DocCount: r.GetInt32(0),
            LineCount: r.GetInt32(1));
    }
}
