using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// WorkOrder + WorkOrderSource persistence. ListAsync/GetAsync JOIN'li DTO doner;
/// CRUD entity tabanli. Tum sorgular request'in CompanyId'sine gore izole calisir.
/// </summary>
public sealed class SqlWorkOrderRepository : IWorkOrderRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly IDataVisibilityFilter _dvFilter;
    private readonly string _schema;
    private readonly string _woTable;
    private readonly string _srcTable;

    public SqlWorkOrderRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options, IDataVisibilityFilter dvFilter)
    {
        _connectionFactory = factory;
        _dvFilter = dvFilter;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        var s = _schema.Replace("]", "]]");
        _woTable = $"[{s}].[WorkOrder]";
        _srcTable = $"[{s}].[WorkOrderSource]";
    }

    public async Task<IReadOnlyCollection<WorkOrderListItemDto>> ListAsync(WorkOrderStatus? status, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        var statusFilter = status.HasValue ? "AND w.[Status] = @Status" : "";
        // Satır görünürlük kuralları (row-level security) — alan-değer-operatör kısıtları.
        var dv = await _dvFilter.BuildAsync(FormCodes.WorkOrders, "w", "Id", ct);
        cmd.CommandText = $@"
            SELECT w.[Id], w.[OrderNumber], w.[OrderDate], w.[ItemId],
                   i.[Code] AS ItemCode, i.[Name] AS ItemName,
                   w.[ConfigId], w.[PlannedQuantity], w.[ProducedQuantity],
                   w.[UnitId], u.[Code] AS UnitCode,
                   w.[Status], w.[Priority],
                   w.[PlannedStartDate], w.[PlannedEndDate],
                   w.[AssignedUserId], usr.[FullName] AS AssignedUserName,
                   w.[RevisionNo],
                   w.[AssignedPersonnelId], ap.[FullName] AS AssignedPersonnelName
            FROM {_woTable} w
            LEFT JOIN [{_schema}].[Items] i ON i.[Id] = w.[ItemId]
            LEFT JOIN [{_schema}].[Unit] u ON u.[Id] = w.[UnitId]
            LEFT JOIN [{_schema}].[Users] usr ON usr.[Id] = w.[AssignedUserId]
            LEFT JOIN [{_schema}].[Personnel] ap ON ap.[Id] = w.[AssignedPersonnelId]
            WHERE w.[CompanyId] = @CompanyId AND w.[IsActive] = 1
            {statusFilter}
            {dv.Sql}
            ORDER BY w.[OrderDate] DESC, w.[Id] DESC;";
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        if (status.HasValue) cmd.Parameters.AddWithValue("@Status", (byte)status.Value);
        foreach (var prm in dv.Parameters) cmd.Parameters.AddWithValue(prm.Name, prm.Value);

        var list = new List<WorkOrderListItemDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new WorkOrderListItemDto(
                Id: r.GetInt32(0),
                OrderNumber: r.GetString(1),
                OrderDate: r.GetDateTime(2),
                ItemId: r.GetInt32(3),
                ItemCode: r.IsDBNull(4) ? null : r.GetString(4),
                ItemName: r.IsDBNull(5) ? null : r.GetString(5),
                ConfigId: r.IsDBNull(6) ? null : r.GetInt32(6),
                PlannedQuantity: r.GetDecimal(7),
                ProducedQuantity: r.GetDecimal(8),
                UnitId: r.IsDBNull(9) ? null : r.GetInt32(9),
                UnitCode: r.IsDBNull(10) ? null : r.GetString(10),
                Status: (WorkOrderStatus)r.GetByte(11),
                Priority: (WorkOrderPriority)r.GetByte(12),
                PlannedStartDate: r.IsDBNull(13) ? null : r.GetDateTime(13),
                PlannedEndDate: r.IsDBNull(14) ? null : r.GetDateTime(14),
                AssignedUserId: r.IsDBNull(15) ? null : r.GetInt32(15),
                AssignedUserName: r.IsDBNull(16) ? null : r.GetString(16),
                RevisionNo: r.GetInt32(17),
                AssignedPersonnelId: r.FieldCount > 18 && !r.IsDBNull(18) ? r.GetInt32(18) : null,
                AssignedPersonnelName: r.FieldCount > 19 && !r.IsDBNull(19) ? r.GetString(19) : null));
        }
        return list;
    }

    public async Task<WorkOrderDto?> GetAsync(int id, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);

        WorkOrderDto? dto;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                SELECT w.[Id], w.[CompanyId], w.[OrderNumber], w.[OrderDate],
                       w.[ItemId], i.[Code] AS ItemCode, i.[Name] AS ItemName,
                       w.[ConfigId],
                       w.[PlannedQuantity], w.[ProducedQuantity], w.[ScrapQuantity],
                       w.[UnitId], u.[Code] AS UnitCode,
                       w.[PlannedStartDate], w.[PlannedEndDate],
                       w.[ActualStartDate], w.[ActualEndDate],
                       w.[Status], w.[Priority],
                       w.[AssignedUserId], usr.[FullName],
                       w.[WarehouseLocationId], loc.[LocationCode],
                       w.[RevisionNo], w.[ParentWorkOrderId], w.[RevisedFromId],
                       w.[RoutingId], rt.[Code] AS RoutingCode, rt.[Name] AS RoutingName,
                       w.[DefaultMachineId], dm.[Code] AS DefaultMachineCode, dm.[Name] AS DefaultMachineName,
                       w.[AssignedPersonnelId], ap.[FullName] AS AssignedPersonnelName,
                       w.[Notes], w.[Created], w.[Updated],
                       -- 2026-05-22: Standart rehber pattern A icin ek display kolonlari
                       ap.[Code] AS AssignedPersonnelCode,
                       loc.[LocationName] AS WarehouseLocationName,
                       w.[ArgeProjectId], apr.[Name] AS ArgeProjectName
                FROM {_woTable} w
                LEFT JOIN [{_schema}].[Items] i ON i.[Id] = w.[ItemId]
                LEFT JOIN [{_schema}].[Unit] u ON u.[Id] = w.[UnitId]
                LEFT JOIN [{_schema}].[Users] usr ON usr.[Id] = w.[AssignedUserId]
                LEFT JOIN [{_schema}].[Location] loc ON loc.[Id] = w.[WarehouseLocationId]
                LEFT JOIN [{_schema}].[Routing] rt ON rt.[Id] = w.[RoutingId]
                LEFT JOIN [{_schema}].[Machine] dm ON dm.[Id] = w.[DefaultMachineId]
                LEFT JOIN [{_schema}].[Personnel] ap ON ap.[Id] = w.[AssignedPersonnelId]
                LEFT JOIN [{_schema}].[ArgeProject] apr ON apr.[DocumentId] = w.[ArgeProjectId]
                WHERE w.[Id] = @Id AND w.[CompanyId] = @CompanyId;";
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@CompanyId", companyId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return null;

            dto = new WorkOrderDto(
                Id: r.GetInt32(0),
                CompanyId: r.GetInt32(1),
                OrderNumber: r.GetString(2),
                OrderDate: r.GetDateTime(3),
                ItemId: r.GetInt32(4),
                ItemCode: r.IsDBNull(5) ? null : r.GetString(5),
                ItemName: r.IsDBNull(6) ? null : r.GetString(6),
                ConfigId: r.IsDBNull(7) ? null : r.GetInt32(7),
                PlannedQuantity: r.GetDecimal(8),
                ProducedQuantity: r.GetDecimal(9),
                ScrapQuantity: r.GetDecimal(10),
                UnitId: r.IsDBNull(11) ? null : r.GetInt32(11),
                UnitCode: r.IsDBNull(12) ? null : r.GetString(12),
                PlannedStartDate: r.IsDBNull(13) ? null : r.GetDateTime(13),
                PlannedEndDate: r.IsDBNull(14) ? null : r.GetDateTime(14),
                ActualStartDate: r.IsDBNull(15) ? null : r.GetDateTime(15),
                ActualEndDate: r.IsDBNull(16) ? null : r.GetDateTime(16),
                Status: (WorkOrderStatus)r.GetByte(17),
                Priority: (WorkOrderPriority)r.GetByte(18),
                AssignedUserId: r.IsDBNull(19) ? null : r.GetInt32(19),
                AssignedUserName: r.IsDBNull(20) ? null : r.GetString(20),
                WarehouseLocationId: r.IsDBNull(21) ? null : r.GetInt32(21),
                WarehouseLocationCode: r.IsDBNull(22) ? null : r.GetString(22),
                RevisionNo: r.GetInt32(23),
                ParentWorkOrderId: r.IsDBNull(24) ? null : r.GetInt32(24),
                RevisedFromId: r.IsDBNull(25) ? null : r.GetInt32(25),
                RoutingId: r.IsDBNull(26) ? null : r.GetInt32(26),
                RoutingCode: r.IsDBNull(27) ? null : r.GetString(27),
                RoutingName: r.IsDBNull(28) ? null : r.GetString(28),
                DefaultMachineId: r.IsDBNull(29) ? null : r.GetInt32(29),
                DefaultMachineCode: r.IsDBNull(30) ? null : r.GetString(30),
                DefaultMachineName: r.IsDBNull(31) ? null : r.GetString(31),
                AssignedPersonnelId: r.IsDBNull(32) ? null : r.GetInt32(32),
                AssignedPersonnelName: r.IsDBNull(33) ? null : r.GetString(33),
                Notes: r.IsDBNull(34) ? null : r.GetString(34),
                Created: r.GetDateTime(35),
                Updated: r.IsDBNull(36) ? null : r.GetDateTime(36),
                Sources: Array.Empty<WorkOrderSourceDto>(),
                AssignedPersonnelCode: r.IsDBNull(37) ? null : r.GetString(37),
                WarehouseLocationName: r.IsDBNull(38) ? null : r.GetString(38),
                ArgeProjectId: r.IsDBNull(39) ? null : r.GetInt32(39),
                ArgeProjectName: r.IsDBNull(40) ? null : r.GetString(40));
        }

        var sources = await GetSourcesInternalAsync(conn, id, ct);
        return dto with { Sources = sources };
    }

    public async Task<int> CreateAsync(WorkOrder e, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            INSERT INTO {_woTable}
                ([CompanyId],[OrderNumber],[OrderDate],
                 [ItemId],[ConfigId],[PlannedQuantity],[UnitId],
                 [PlannedStartDate],[PlannedEndDate],
                 [Status],[Priority],[AssignedUserId],[WarehouseLocationId],
                 [RevisionNo],[ParentWorkOrderId],[RevisedFromId],[RoutingId],[DefaultMachineId],
                 [AssignedPersonnelId],[Notes],[ArgeProjectId],[CreatedById],[Created],[IsActive])
            VALUES
                (@CompanyId,@OrderNumber,@OrderDate,
                 @ItemId,@ConfigId,@PlannedQuantity,@UnitId,
                 @PlannedStartDate,@PlannedEndDate,
                 @Status,@Priority,@AssignedUserId,@WarehouseLocationId,
                 @RevisionNo,@ParentWorkOrderId,@RevisedFromId,@RoutingId,@DefaultMachineId,
                 @AssignedPersonnelId,@Notes,@ArgeProjectId,@CreatedById,SYSUTCDATETIME(),1);
            SELECT CAST(SCOPE_IDENTITY() AS INT);";
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        cmd.Parameters.AddWithValue("@OrderNumber", e.OrderNumber);
        cmd.Parameters.AddWithValue("@OrderDate", e.OrderDate);
        cmd.Parameters.AddWithValue("@ItemId", e.ItemId);
        cmd.Parameters.AddWithValue("@ConfigId", (object?)e.ConfigId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@PlannedQuantity", e.PlannedQuantity);
        cmd.Parameters.AddWithValue("@UnitId", (object?)e.UnitId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@PlannedStartDate", (object?)e.PlannedStartDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@PlannedEndDate", (object?)e.PlannedEndDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Status", (byte)e.Status);
        cmd.Parameters.AddWithValue("@Priority", (byte)e.Priority);
        cmd.Parameters.AddWithValue("@AssignedUserId", (object?)e.AssignedUserId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@WarehouseLocationId", (object?)e.WarehouseLocationId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@RevisionNo", e.RevisionNo);
        cmd.Parameters.AddWithValue("@ParentWorkOrderId", (object?)e.ParentWorkOrderId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@RevisedFromId", (object?)e.RevisedFromId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@RoutingId", (object?)e.RoutingId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@DefaultMachineId", (object?)e.DefaultMachineId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@AssignedPersonnelId", (object?)e.AssignedPersonnelId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Notes", (object?)e.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ArgeProjectId", (object?)e.ArgeProjectId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CreatedById", (object?)e.CreatedById ?? DBNull.Value);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
    }

    public async Task UpdateAsync(int id, UpdateWorkOrderRequest req, int? updatedBy, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            UPDATE {_woTable}
            SET [PlannedQuantity]=@PlannedQuantity,
                [UnitId]=@UnitId,
                [PlannedStartDate]=@PlannedStartDate,
                [PlannedEndDate]=@PlannedEndDate,
                [Priority]=@Priority,
                [AssignedUserId]=@AssignedUserId,
                [WarehouseLocationId]=@WarehouseLocationId,
                [RoutingId]=@RoutingId,
                [DefaultMachineId]=@DefaultMachineId,
                [AssignedPersonnelId]=@AssignedPersonnelId,
                [Notes]=@Notes,
                [ArgeProjectId]=@ArgeProjectId,
                [UpdatedById]=@UpdatedById,
                [Updated]=SYSUTCDATETIME()
            WHERE [Id]=@Id AND [CompanyId]=@CompanyId;";
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        cmd.Parameters.AddWithValue("@PlannedQuantity", req.PlannedQuantity);
        cmd.Parameters.AddWithValue("@UnitId", (object?)req.UnitId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@PlannedStartDate", (object?)req.PlannedStartDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@PlannedEndDate", (object?)req.PlannedEndDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Priority", (byte)req.Priority);
        cmd.Parameters.AddWithValue("@AssignedUserId", (object?)req.AssignedUserId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@WarehouseLocationId", (object?)req.WarehouseLocationId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@RoutingId", (object?)req.RoutingId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@DefaultMachineId", (object?)req.DefaultMachineId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@AssignedPersonnelId", (object?)req.AssignedPersonnelId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Notes", (object?)req.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ArgeProjectId", (object?)req.ArgeProjectId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@UpdatedById", (object?)updatedBy ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task ChangeStatusAsync(int id, WorkOrderStatus newStatus, int? userId, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        // ActualStart auto-set @ InProgress (eger NULL ise), ActualEnd @ Completed (eger NULL ise).
        cmd.CommandText = $@"
            UPDATE {_woTable}
            SET [Status]=@Status,
                [ActualStartDate] = CASE WHEN @Status = 2 AND [ActualStartDate] IS NULL THEN SYSUTCDATETIME() ELSE [ActualStartDate] END,
                [ActualEndDate]   = CASE WHEN @Status = 3 AND [ActualEndDate] IS NULL THEN SYSUTCDATETIME() ELSE [ActualEndDate] END,
                [UpdatedById]=@UpdatedById,
                [Updated]=SYSUTCDATETIME()
            WHERE [Id]=@Id AND [CompanyId]=@CompanyId;";
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        cmd.Parameters.AddWithValue("@Status", (byte)newStatus);
        cmd.Parameters.AddWithValue("@UpdatedById", (object?)userId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> CreateRevisionAsync(int existingId, int? userId, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            int newId;
            // 1) Yeni emir kopyala (RevisionNo+1, RevisedFromId, Status=Planned, OrderNumber'a "-Rn" suffix)
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = $@"
                    INSERT INTO {_woTable}
                        ([CompanyId],[OrderNumber],[OrderDate],
                         [ItemId],[ConfigId],[PlannedQuantity],[UnitId],
                         [PlannedStartDate],[PlannedEndDate],
                         [Status],[Priority],[AssignedUserId],[WarehouseLocationId],
                         [RevisionNo],[ParentWorkOrderId],[RevisedFromId],[RoutingId],[DefaultMachineId],
                         [AssignedPersonnelId],[Notes],[CreatedById],[Created],[IsActive])
                    SELECT [CompanyId],
                           [OrderNumber] + N'-R' + CAST([RevisionNo]+1 AS NVARCHAR(4)),
                           SYSUTCDATETIME(),
                           [ItemId],[ConfigId],[PlannedQuantity],[UnitId],
                           [PlannedStartDate],[PlannedEndDate],
                           0 /* Planned */, [Priority], [AssignedUserId], [WarehouseLocationId],
                           [RevisionNo]+1, [ParentWorkOrderId], [Id] /* RevisedFromId */, [RoutingId], [DefaultMachineId],
                           [AssignedPersonnelId], [Notes], @UserId, SYSUTCDATETIME(), 1
                    FROM {_woTable}
                    WHERE [Id] = @ExistingId AND [CompanyId] = @CompanyId;
                    SELECT CAST(SCOPE_IDENTITY() AS INT);";
                cmd.Parameters.AddWithValue("@ExistingId", existingId);
                cmd.Parameters.AddWithValue("@CompanyId", companyId);
                cmd.Parameters.AddWithValue("@UserId", (object?)userId ?? DBNull.Value);
                var result = await cmd.ExecuteScalarAsync(ct);
                newId = result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
                if (newId <= 0) throw new InvalidOperationException("Revize emri olusturulamadi.");
            }

            // 2) Eski emir Cancelled
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = $@"
                    UPDATE {_woTable}
                    SET [Status] = 5 /* Cancelled */,
                        [UpdatedById] = @UserId,
                        [Updated] = SYSUTCDATETIME()
                    WHERE [Id] = @Id AND [CompanyId] = @CompanyId;";
                cmd.Parameters.AddWithValue("@Id", existingId);
                cmd.Parameters.AddWithValue("@CompanyId", companyId);
                cmd.Parameters.AddWithValue("@UserId", (object?)userId ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            // 3) Source kayitlarini kopyala (yeni emire baglansin)
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = $@"
                    INSERT INTO {_srcTable} ([WorkOrderId],[SourceDocumentId],[SourceLineId],[AllocatedQuantity],[Created])
                    SELECT @NewId, [SourceDocumentId], [SourceLineId], [AllocatedQuantity], SYSUTCDATETIME()
                    FROM {_srcTable} WHERE [WorkOrderId] = @OldId;";
                cmd.Parameters.AddWithValue("@NewId", newId);
                cmd.Parameters.AddWithValue("@OldId", existingId);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            return newId;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<IReadOnlyCollection<WorkOrderSourceDto>> GetSourcesAsync(int workOrderId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        return await GetSourcesInternalAsync(conn, workOrderId, ct);
    }

    private async Task<IReadOnlyCollection<WorkOrderSourceDto>> GetSourcesInternalAsync(SqlConnection conn, int workOrderId, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT s.[Id], s.[WorkOrderId], s.[SourceDocumentId], d.[DocumentNumber], s.[SourceLineId], s.[AllocatedQuantity]
            FROM {_srcTable} s
            LEFT JOIN [{_schema}].[Document] d ON d.[Id] = s.[SourceDocumentId]
            WHERE s.[WorkOrderId] = @WorkOrderId
            ORDER BY s.[Id];";
        cmd.Parameters.AddWithValue("@WorkOrderId", workOrderId);
        var list = new List<WorkOrderSourceDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new WorkOrderSourceDto(
                Id: r.GetInt32(0),
                WorkOrderId: r.GetInt32(1),
                SourceDocumentId: r.GetInt32(2),
                SourceDocumentNumber: r.IsDBNull(3) ? null : r.GetString(3),
                SourceLineId: r.GetInt32(4),
                AllocatedQuantity: r.GetDecimal(5)));
        }
        return list;
    }

    public async Task AddSourceAsync(int workOrderId, int sourceDocumentId, int sourceLineId, decimal allocatedQuantity, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            INSERT INTO {_srcTable} ([WorkOrderId],[SourceDocumentId],[SourceLineId],[AllocatedQuantity],[Created])
            VALUES (@WorkOrderId, @SourceDocumentId, @SourceLineId, @AllocatedQuantity, SYSUTCDATETIME());";
        cmd.Parameters.AddWithValue("@WorkOrderId", workOrderId);
        cmd.Parameters.AddWithValue("@SourceDocumentId", sourceDocumentId);
        cmd.Parameters.AddWithValue("@SourceLineId", sourceLineId);
        cmd.Parameters.AddWithValue("@AllocatedQuantity", allocatedQuantity);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<decimal> GetAllocatedQuantityForLineAsync(int sourceLineId, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        // Cancelled emirleri sayma — onlarin allocation'i serbest.
        cmd.CommandText = $@"
            SELECT ISNULL(SUM(s.[AllocatedQuantity]), 0)
            FROM {_srcTable} s
            INNER JOIN {_woTable} w ON w.[Id] = s.[WorkOrderId]
            WHERE s.[SourceLineId] = @SourceLineId
              AND w.[CompanyId] = @CompanyId
              AND w.[Status] <> 5 /* Cancelled */
              AND w.[IsActive] = 1;";
        cmd.Parameters.AddWithValue("@SourceLineId", sourceLineId);
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result != null && result != DBNull.Value ? Convert.ToDecimal(result) : 0m;
    }

    public async Task SetRoutingIdAsync(int workOrderId, int routingId, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            UPDATE {_woTable}
            SET [RoutingId] = @RoutingId,
                [Updated] = SYSUTCDATETIME()
            WHERE [Id] = @Id AND [CompanyId] = @CompanyId;";
        cmd.Parameters.AddWithValue("@Id", workOrderId);
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        cmd.Parameters.AddWithValue("@RoutingId", routingId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int?> FindRoutingForItemAsync(int itemId, int? configId, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        // Öncelik: ConfigId tam eşleşme, sonra ConfigId NULL (default rota).
        cmd.CommandText = $@"
            SELECT TOP 1 [Id]
            FROM [{_schema}].[Routing]
            WHERE [CompanyId] = @CompanyId
              AND [ItemId] = @ItemId
              AND [IsActive] = 1
              AND ([ConfigId] = @ConfigId OR [ConfigId] IS NULL)
            ORDER BY CASE WHEN [ConfigId] = @ConfigId THEN 0 ELSE 1 END,
                     [Id] DESC;";
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        cmd.Parameters.AddWithValue("@ItemId", itemId);
        cmd.Parameters.AddWithValue("@ConfigId", (object?)configId ?? DBNull.Value);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result != null && result != DBNull.Value ? Convert.ToInt32(result) : null;
    }

    public async Task<IReadOnlyCollection<WorkOrderListItemDto>> ListEligibleForMergeAsync(int itemId, int? configId, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        var configFilter = configId.HasValue
            ? "AND w.[ConfigId] = @ConfigId"
            : "AND w.[ConfigId] IS NULL";
        cmd.CommandText = $@"
            SELECT w.[Id], w.[OrderNumber], w.[OrderDate], w.[ItemId],
                   i.[Code], i.[Name],
                   w.[ConfigId], w.[PlannedQuantity], w.[ProducedQuantity],
                   w.[UnitId], u.[Code] AS UnitCode,
                   w.[Status], w.[Priority],
                   w.[PlannedStartDate], w.[PlannedEndDate],
                   w.[AssignedUserId], usr.[FullName], w.[RevisionNo]
            FROM {_woTable} w
            LEFT JOIN [{_schema}].[Items] i ON i.[Id] = w.[ItemId]
            LEFT JOIN [{_schema}].[Unit] u ON u.[Id] = w.[UnitId]
            LEFT JOIN [{_schema}].[Users] usr ON usr.[Id] = w.[AssignedUserId]
            WHERE w.[CompanyId] = @CompanyId
              AND w.[ItemId] = @ItemId
              {configFilter}
              AND w.[Status] IN (0, 1) /* Planned, Released */
              AND w.[IsActive] = 1
            ORDER BY w.[OrderDate] DESC;";
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        cmd.Parameters.AddWithValue("@ItemId", itemId);
        if (configId.HasValue) cmd.Parameters.AddWithValue("@ConfigId", configId.Value);

        var list = new List<WorkOrderListItemDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new WorkOrderListItemDto(
                Id: r.GetInt32(0),
                OrderNumber: r.GetString(1),
                OrderDate: r.GetDateTime(2),
                ItemId: r.GetInt32(3),
                ItemCode: r.IsDBNull(4) ? null : r.GetString(4),
                ItemName: r.IsDBNull(5) ? null : r.GetString(5),
                ConfigId: r.IsDBNull(6) ? null : r.GetInt32(6),
                PlannedQuantity: r.GetDecimal(7),
                ProducedQuantity: r.GetDecimal(8),
                UnitId: r.IsDBNull(9) ? null : r.GetInt32(9),
                UnitCode: r.IsDBNull(10) ? null : r.GetString(10),
                Status: (WorkOrderStatus)r.GetByte(11),
                Priority: (WorkOrderPriority)r.GetByte(12),
                PlannedStartDate: r.IsDBNull(13) ? null : r.GetDateTime(13),
                PlannedEndDate: r.IsDBNull(14) ? null : r.GetDateTime(14),
                AssignedUserId: r.IsDBNull(15) ? null : r.GetInt32(15),
                AssignedUserName: r.IsDBNull(16) ? null : r.GetString(16),
                RevisionNo: r.GetInt32(17),
                AssignedPersonnelId: r.FieldCount > 18 && !r.IsDBNull(18) ? r.GetInt32(18) : null,
                AssignedPersonnelName: r.FieldCount > 19 && !r.IsDBNull(19) ? r.GetString(19) : null));
        }
        return list;
    }
}
