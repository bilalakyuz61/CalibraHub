using System.Text.Json;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlApprovalInstanceRepository : IApprovalInstanceRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _s;

    public SqlApprovalInstanceRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options)
    {
        _connectionFactory = factory;
        _s = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
    }

    public async Task<int> CreateAsync(
        StartApprovalRequest request,
        IReadOnlyList<ApprovalFlowStepDto> steps,
        CancellationToken ct)
    {
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = con.BeginTransaction();
        try
        {
            await using var ins = con.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = $"""
                INSERT INTO [{_s}].[ApprovalInstance]
                    ([DocumentId],[EntityKind],[FlowId],[Status],[CurrentStep],[StartedBy],[StartedAt],[IsActive],[CreatedById],[Created])
                VALUES
                    (@DocId,@EntityKind,@FlowId,N'Pending',1,@By,SYSUTCDATETIME(),1,@CreatedById,SYSUTCDATETIME());
                SELECT SCOPE_IDENTITY();
                """;
            ins.Parameters.Add(new SqlParameter { ParameterName = "@DocId", Value = (object?)request.DocumentId ?? DBNull.Value, SqlDbType = System.Data.SqlDbType.Int });
            ins.Parameters.Add(new SqlParameter("@EntityKind", request.EntityKind ?? "Document"));
            ins.Parameters.Add(new SqlParameter("@FlowId", request.FlowId));
            ins.Parameters.Add(new SqlParameter("@By", (object?)request.StartedBy ?? DBNull.Value));
            ins.Parameters.Add(new SqlParameter { ParameterName = "@CreatedById", Value = DBNull.Value, SqlDbType = System.Data.SqlDbType.Int });
            var instanceId = Convert.ToInt32(await ins.ExecuteScalarAsync(ct));

            var now = DateTime.UtcNow;
            foreach (var step in steps.OrderBy(s => s.StepOrder))
            {
                var dueDate = ComputeDueDateFromNodeData(step.NodeData, now);
                await using var sCmd = con.CreateCommand();
                sCmd.Transaction = tx;
                // SpecificUser ve ManagerOfRequester için çözümlenen onaylayıcıyı step record'a yaz.
                // ManagerOfRequester: StartAsync'te supervisor çözümlendi, ApproverId/ApproverLabel set edildi.
                var storeApprover = step.ApproverType == CalibraHub.Domain.Enums.ApproverType.SpecificUser
                                 || step.ApproverType == CalibraHub.Domain.Enums.ApproverType.ManagerOfRequester;
                var intendedApproverId   = storeApprover ? step.ApproverId   : null;
                var intendedApproverName = storeApprover ? step.ApproverLabel : null;

                sCmd.CommandText = $"""
                    INSERT INTO [{_s}].[ApprovalStepRecord]
                        ([InstanceId],[StepOrder],[StepName],[Status],[ApproverId],[ApproverName],[DueDate],[CreatedById],[Created])
                    VALUES
                        (@Iid,@Ord,@Name,N'Pending',@AppId,@AppName,@Due,@CreatedById,SYSUTCDATETIME());
                    """;
                sCmd.Parameters.Add(new SqlParameter("@Iid", instanceId));
                sCmd.Parameters.Add(new SqlParameter("@Ord", step.StepOrder));
                sCmd.Parameters.Add(new SqlParameter("@Name", step.StepName));
                sCmd.Parameters.Add(new SqlParameter("@AppId",   (object?)intendedApproverId   ?? DBNull.Value));
                sCmd.Parameters.Add(new SqlParameter("@AppName", (object?)intendedApproverName ?? DBNull.Value));
                sCmd.Parameters.Add(new SqlParameter("@Due", (object?)dueDate ?? DBNull.Value));
                sCmd.Parameters.Add(new SqlParameter { ParameterName = "@CreatedById", Value = DBNull.Value, SqlDbType = System.Data.SqlDbType.Int });
                await sCmd.ExecuteNonQueryAsync(ct);
            }

            // CurrentStep'i ilk step record'un StepOrder'ına hizala.
            // Graph akışlarda StepOrder=1 genellikle "Başla" (start node) olur ve step record üretmez;
            // hard-coded default=1 bırakırsak "Onayda Bekleyenler" sorgusu (sr.StepOrder = inst.CurrentStep)
            // hiçbir kayıtla eşleşmez → onay sırasındaki kullanıcı listede görünmez.
            // Linear akışlarda da en küçük StepOrder=1 dönerek doğru davranış korunur.
            await using (var alignCmd = con.CreateCommand())
            {
                alignCmd.Transaction = tx;
                alignCmd.CommandText = $"""
                    UPDATE [{_s}].[ApprovalInstance]
                    SET [CurrentStep] = ISNULL((
                        SELECT MIN([StepOrder]) FROM [{_s}].[ApprovalStepRecord]
                        WHERE [InstanceId] = @Iid AND [Status] = N'Pending'
                    ), 1)
                    WHERE [Id] = @Iid;
                    """;
                alignCmd.Parameters.Add(new SqlParameter("@Iid", instanceId));
                await alignCmd.ExecuteNonQueryAsync(ct);
            }

            tx.Commit();
            return instanceId;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task<ApprovalInstanceDto?> GetByDocumentIdAsync(int documentId, CancellationToken ct)
    {
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        var id = await GetInstanceIdByDocumentAsync(con, documentId, ct);
        return id.HasValue ? await ReadInstanceAsync(con, id.Value, ct) : null;
    }

    public async Task<ApprovalInstanceDto?> GetByIdAsync(int instanceId, CancellationToken ct)
    {
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        return await ReadInstanceAsync(con, instanceId, ct);
    }

    public async Task<IReadOnlyList<ApprovalInstanceDto>> GetPendingAsync(CancellationToken ct)
    {
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id] FROM [{_s}].[ApprovalInstance]
            WHERE [Status] = N'Pending' AND [IsActive] = 1
            ORDER BY [StartedAt] DESC;
            """;
        var ids = new List<int>();
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                ids.Add(r.GetInt32(0));

        var result = new List<ApprovalInstanceDto>();
        foreach (var id in ids)
        {
            var inst = await ReadInstanceAsync(con, id, ct);
            if (inst is not null) result.Add(inst);
        }
        return result;
    }

    /// <summary>
    /// Onayda bekleyenler ekrani: ApprovalStepRecord JOIN Document + document_types + Contact + currencies.
    /// Sadece su andaki adim (StepOrder = Instance.CurrentStep) Pending olanlar listelenir.
    /// scope: mine → ApproverId = @UserId, department → ApproverId IN (@DepUsers), all → filtre yok.
    /// </summary>
    public async Task<IReadOnlyList<PendingApprovalItemDto>> GetPendingForUserAsync(
        string userId,
        string scope,
        IReadOnlyCollection<string>? departmentUserIds,
        CancellationToken ct)
    {
        var s = _s;
        var sb = new System.Text.StringBuilder();
        sb.Append($@"
SELECT
    sr.[Id]                AS StepRecordId,
    sr.[InstanceId]        AS InstanceId,
    sr.[StepOrder]         AS StepOrder,
    sr.[StepName]          AS StepName,
    sr.[ApproverId]        AS ApproverId,
    sr.[ApproverName]      AS ApproverName,
    sr.[Created]           AS StepCreated,
    sr.[DueDate]           AS DueDate,
    inst.[Id]              AS InstanceIdAlias,
    inst.[DocumentId]      AS DocumentId,
    inst.[EntityKind]      AS EntityKind,
    inst.[StartedAt]       AS InstanceStarted,
    inst.[FlowId]          AS FlowId,
    flow.[Name]            AS FlowName,
    (SELECT COUNT(*) FROM [{s}].[ApprovalStepRecord] WHERE [InstanceId] = inst.[Id]) AS TotalSteps,
    (SELECT COUNT(*) FROM [{s}].[ApprovalStepRecord] WHERE [InstanceId] = inst.[Id] AND [StepOrder] <= sr.[StepOrder]) AS StepPosition,
    doc.[id]               AS DocumentInternalId,
    doc.[DocumentNumber]   AS DocumentNumber,
    doc.[DocumentDate]     AS DocumentDate,
    doc.[DocumentTypeId]   AS DocumentTypeId,
    dt.[name]              AS DocumentTypeName,
    doc.[ContactId]        AS ContactId,
    c.[AccountTitle]       AS ContactName,
    doc.[GrandTotal]       AS GrandTotal,
    cur.[code]             AS CurrencyCode
FROM [{s}].[ApprovalStepRecord] sr
INNER JOIN [{s}].[ApprovalInstance] inst ON inst.[Id] = sr.[InstanceId]
INNER JOIN [{s}].[ApprovalFlow] flow              ON flow.[Id] = inst.[FlowId]
LEFT  JOIN [{s}].[Document] doc                   ON inst.[EntityKind] = N'Document' AND doc.[id] = inst.[DocumentId]
LEFT  JOIN [{s}].[document_types] dt              ON dt.[id] = doc.[DocumentTypeId]
LEFT  JOIN [{s}].[Contact] c                      ON c.[Id]  = doc.[ContactId]
LEFT  JOIN [{s}].[currencies] cur                 ON cur.[id] = doc.[CurrencyId]
WHERE sr.[Status] = N'Pending'
  AND inst.[Status] = N'Pending'
  AND inst.[IsActive] = 1
  AND sr.[StepOrder] = inst.[CurrentStep]
  AND inst.[DocumentId] IS NOT NULL
  AND (inst.[EntityKind] <> N'Document' OR doc.[IsActive] = 1)
");

        var sql = sb.ToString();

        // Scope filtresi
        if (string.Equals(scope, PendingApprovalScope.Mine, StringComparison.OrdinalIgnoreCase))
        {
            // sr.ApproverId NULL olabilir (backfill öncesi SpecificUser adımı); bu durumda
            // ApprovalFlowStep tablosuna fallback yaparak SpecificUser ApproverId kontrolü yap.
            // AnyUser / ManagerOfRequester adımları atanmamış görev sayılır — "mine" kapsamına
            // dahil edilmez; bu adımlar yalnızca "all" scope'ta görünür.
            sql += $@" AND (
    sr.[ApproverId] = @UserId
    OR (sr.[ApproverId] IS NULL AND EXISTS (
        SELECT 1 FROM [{s}].[ApprovalFlowStep] fs
        WHERE fs.[FlowId] = inst.[FlowId]
          AND fs.[StepOrder] = sr.[StepOrder]
          AND fs.[ApproverType] = N'SpecificUser'
          AND fs.[ApproverId] = @UserId
    ))
)
";
        }
        else if (string.Equals(scope, PendingApprovalScope.Department, StringComparison.OrdinalIgnoreCase)
                 && departmentUserIds != null && departmentUserIds.Count > 0)
        {
            var paramNames = departmentUserIds.Select((_, i) => "@DU" + i).ToArray();
            sql += " AND sr.[ApproverId] IN (" + string.Join(",", paramNames) + ")\n";
        }
        sql += "ORDER BY sr.[Created] DESC;";

        var items = new List<PendingApprovalItemDto>();
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@UserId", userId ?? string.Empty));
        if (string.Equals(scope, PendingApprovalScope.Department, StringComparison.OrdinalIgnoreCase)
            && departmentUserIds != null && departmentUserIds.Count > 0)
        {
            var idx = 0;
            foreach (var u in departmentUserIds)
                cmd.Parameters.Add(new SqlParameter("@DU" + idx++, u ?? string.Empty));
        }

        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            items.Add(new PendingApprovalItemDto(
                InstanceId:          rdr.GetInt32(rdr.GetOrdinal("InstanceId")),
                StepRecordId:        rdr.GetInt32(rdr.GetOrdinal("StepRecordId")),
                StepOrder:           rdr.GetInt32(rdr.GetOrdinal("StepOrder")),
                StepPosition:        rdr.GetInt32(rdr.GetOrdinal("StepPosition")),
                TotalSteps:          rdr.GetInt32(rdr.GetOrdinal("TotalSteps")),
                StepName:            rdr.GetString(rdr.GetOrdinal("StepName")),
                FlowName:            rdr.GetString(rdr.GetOrdinal("FlowName")),
                FlowId:              rdr.GetInt32(rdr.GetOrdinal("FlowId")),
                EntityKind:          rdr.IsDBNull(rdr.GetOrdinal("EntityKind")) ? "Document" : rdr.GetString(rdr.GetOrdinal("EntityKind")),
                DocumentId:          rdr.IsDBNull(rdr.GetOrdinal("DocumentId")) ? (int?)null : rdr.GetInt32(rdr.GetOrdinal("DocumentId")),
                DocumentInternalId:  rdr.IsDBNull(rdr.GetOrdinal("DocumentInternalId")) ? null : rdr.GetInt32(rdr.GetOrdinal("DocumentInternalId")),
                DocumentNumber:      rdr.IsDBNull(rdr.GetOrdinal("DocumentNumber")) ? "(belge yok)" : rdr.GetString(rdr.GetOrdinal("DocumentNumber")),
                DocumentDate:        rdr.IsDBNull(rdr.GetOrdinal("DocumentDate")) ? DateTime.MinValue : rdr.GetDateTime(rdr.GetOrdinal("DocumentDate")),
                DocumentTypeId:      rdr.IsDBNull(rdr.GetOrdinal("DocumentTypeId")) ? null : rdr.GetInt32(rdr.GetOrdinal("DocumentTypeId")),
                DocumentTypeName:    rdr.IsDBNull(rdr.GetOrdinal("DocumentTypeName")) ? null : rdr.GetString(rdr.GetOrdinal("DocumentTypeName")),
                ContactId:           rdr.IsDBNull(rdr.GetOrdinal("ContactId")) ? null : rdr.GetInt32(rdr.GetOrdinal("ContactId")),
                ContactName:         rdr.IsDBNull(rdr.GetOrdinal("ContactName")) ? null : rdr.GetString(rdr.GetOrdinal("ContactName")),
                GrandTotal:          rdr.IsDBNull(rdr.GetOrdinal("GrandTotal")) ? 0m : rdr.GetDecimal(rdr.GetOrdinal("GrandTotal")),
                CurrencyCode:        rdr.IsDBNull(rdr.GetOrdinal("CurrencyCode")) ? null : rdr.GetString(rdr.GetOrdinal("CurrencyCode")),
                ApproverId:          rdr.IsDBNull(rdr.GetOrdinal("ApproverId")) ? null : rdr.GetString(rdr.GetOrdinal("ApproverId")),
                ApproverName:        rdr.IsDBNull(rdr.GetOrdinal("ApproverName")) ? null : rdr.GetString(rdr.GetOrdinal("ApproverName")),
                StepCreated:         rdr.GetDateTime(rdr.GetOrdinal("StepCreated")),
                DueDate:             rdr.IsDBNull(rdr.GetOrdinal("DueDate")) ? null : rdr.GetDateTime(rdr.GetOrdinal("DueDate")),
                InstanceStarted:     rdr.GetDateTime(rdr.GetOrdinal("InstanceStarted"))
            ));
        }
        return items;
    }

    public async Task<IReadOnlyList<PendingApprovalItemDto>> GetCompletedForUserAsync(
        string userId, string scope, IReadOnlyCollection<string>? departmentUserIds, CancellationToken ct)
    {
        var s = _s;
        var sb = new System.Text.StringBuilder();
        sb.Append($@"
SELECT
    sr.[Id]                AS StepRecordId,
    inst.[Id]              AS InstanceId,
    sr.[StepOrder]         AS StepOrder,
    sr.[StepName]          AS StepName,
    sr.[ApproverId]        AS ApproverId,
    sr.[ApproverName]      AS ApproverName,
    ISNULL(inst.[CompletedAt], inst.[StartedAt]) AS StepCreated,
    inst.[Id]              AS InstanceIdAlias,
    inst.[DocumentId]      AS DocumentId,
    inst.[EntityKind]      AS EntityKind,
    inst.[StartedAt]       AS InstanceStarted,
    inst.[FlowId]          AS FlowId,
    inst.[Status]          AS InstanceStatus,
    flow.[Name]            AS FlowName,
    (SELECT COUNT(*) FROM [{s}].[ApprovalStepRecord] WHERE [InstanceId] = inst.[Id]) AS TotalSteps,
    (SELECT COUNT(*) FROM [{s}].[ApprovalStepRecord] WHERE [InstanceId] = inst.[Id] AND [StepOrder] <= sr.[StepOrder]) AS StepPosition,
    doc.[id]               AS DocumentInternalId,
    doc.[DocumentNumber]   AS DocumentNumber,
    doc.[DocumentDate]     AS DocumentDate,
    doc.[DocumentTypeId]   AS DocumentTypeId,
    dt.[name]              AS DocumentTypeName,
    doc.[ContactId]        AS ContactId,
    c.[AccountTitle]       AS ContactName,
    doc.[GrandTotal]       AS GrandTotal,
    cur.[code]             AS CurrencyCode
FROM [{s}].[ApprovalInstance] inst
INNER JOIN [{s}].[ApprovalFlow] flow              ON flow.[Id] = inst.[FlowId]
INNER JOIN [{s}].[ApprovalStepRecord] sr          ON sr.[InstanceId] = inst.[Id]
    AND sr.[StepOrder] = (
        SELECT MAX([StepOrder]) FROM [{s}].[ApprovalStepRecord]
        WHERE [InstanceId] = inst.[Id] AND [Status] NOT IN (N'Pending',N'Waiting',N'Skipped')
    )
LEFT  JOIN [{s}].[Document] doc                   ON inst.[EntityKind] = N'Document' AND doc.[id] = inst.[DocumentId]
LEFT  JOIN [{s}].[document_types] dt              ON dt.[id] = doc.[DocumentTypeId]
LEFT  JOIN [{s}].[Contact] c                      ON c.[Id]  = doc.[ContactId]
LEFT  JOIN [{s}].[currencies] cur                 ON cur.[id] = doc.[CurrencyId]
WHERE inst.[Status] IN (N'Approved',N'Rejected')
  AND inst.[IsActive] = 1
  AND inst.[DocumentId] IS NOT NULL
  AND (inst.[EntityKind] <> N'Document' OR doc.[IsActive] = 1)
");

        var sql = sb.ToString();

        if (string.Equals(scope, PendingApprovalScope.Mine, StringComparison.OrdinalIgnoreCase))
        {
            sql += $@" AND (
    inst.[StartedBy] = @UserId
    OR EXISTS (
        SELECT 1 FROM [{s}].[ApprovalStepRecord] asr
        WHERE asr.[InstanceId] = inst.[Id] AND asr.[ApproverId] = @UserId
    )
)";
        }
        else if (string.Equals(scope, PendingApprovalScope.Department, StringComparison.OrdinalIgnoreCase)
                 && departmentUserIds != null && departmentUserIds.Count > 0)
        {
            var paramNames = departmentUserIds.Select((_, i) => "@DU" + i).ToArray();
            sql += $@" AND (
    inst.[StartedBy] IN ({string.Join(",", paramNames)})
    OR EXISTS (
        SELECT 1 FROM [{s}].[ApprovalStepRecord] asr
        WHERE asr.[InstanceId] = inst.[Id] AND asr.[ApproverId] IN ({string.Join(",", paramNames)})
    )
)";
        }

        sql += " ORDER BY ISNULL(inst.[CompletedAt], inst.[StartedAt]) DESC";

        var items = new List<PendingApprovalItemDto>();
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@UserId", userId ?? string.Empty));
        if (string.Equals(scope, PendingApprovalScope.Department, StringComparison.OrdinalIgnoreCase)
            && departmentUserIds != null && departmentUserIds.Count > 0)
        {
            var idx = 0;
            foreach (var u in departmentUserIds)
                cmd.Parameters.Add(new SqlParameter("@DU" + idx++, u ?? string.Empty));
        }

        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            items.Add(new PendingApprovalItemDto(
                InstanceId:          rdr.GetInt32(rdr.GetOrdinal("InstanceId")),
                StepRecordId:        rdr.GetInt32(rdr.GetOrdinal("StepRecordId")),
                StepOrder:           rdr.GetInt32(rdr.GetOrdinal("StepOrder")),
                StepPosition:        rdr.GetInt32(rdr.GetOrdinal("StepPosition")),
                TotalSteps:          rdr.GetInt32(rdr.GetOrdinal("TotalSteps")),
                StepName:            rdr.GetString(rdr.GetOrdinal("StepName")),
                FlowName:            rdr.GetString(rdr.GetOrdinal("FlowName")),
                FlowId:              rdr.GetInt32(rdr.GetOrdinal("FlowId")),
                EntityKind:          rdr.IsDBNull(rdr.GetOrdinal("EntityKind")) ? "Document" : rdr.GetString(rdr.GetOrdinal("EntityKind")),
                DocumentId:          rdr.IsDBNull(rdr.GetOrdinal("DocumentId")) ? (int?)null : rdr.GetInt32(rdr.GetOrdinal("DocumentId")),
                DocumentInternalId:  rdr.IsDBNull(rdr.GetOrdinal("DocumentInternalId")) ? null : rdr.GetInt32(rdr.GetOrdinal("DocumentInternalId")),
                DocumentNumber:      rdr.IsDBNull(rdr.GetOrdinal("DocumentNumber")) ? "(belge yok)" : rdr.GetString(rdr.GetOrdinal("DocumentNumber")),
                DocumentDate:        rdr.IsDBNull(rdr.GetOrdinal("DocumentDate")) ? DateTime.MinValue : rdr.GetDateTime(rdr.GetOrdinal("DocumentDate")),
                DocumentTypeId:      rdr.IsDBNull(rdr.GetOrdinal("DocumentTypeId")) ? null : rdr.GetInt32(rdr.GetOrdinal("DocumentTypeId")),
                DocumentTypeName:    rdr.IsDBNull(rdr.GetOrdinal("DocumentTypeName")) ? null : rdr.GetString(rdr.GetOrdinal("DocumentTypeName")),
                ContactId:           rdr.IsDBNull(rdr.GetOrdinal("ContactId")) ? null : rdr.GetInt32(rdr.GetOrdinal("ContactId")),
                ContactName:         rdr.IsDBNull(rdr.GetOrdinal("ContactName")) ? null : rdr.GetString(rdr.GetOrdinal("ContactName")),
                GrandTotal:          rdr.IsDBNull(rdr.GetOrdinal("GrandTotal")) ? 0m : rdr.GetDecimal(rdr.GetOrdinal("GrandTotal")),
                CurrencyCode:        rdr.IsDBNull(rdr.GetOrdinal("CurrencyCode")) ? null : rdr.GetString(rdr.GetOrdinal("CurrencyCode")),
                ApproverId:          rdr.IsDBNull(rdr.GetOrdinal("ApproverId")) ? null : rdr.GetString(rdr.GetOrdinal("ApproverId")),
                ApproverName:        rdr.IsDBNull(rdr.GetOrdinal("ApproverName")) ? null : rdr.GetString(rdr.GetOrdinal("ApproverName")),
                StepCreated:         rdr.GetDateTime(rdr.GetOrdinal("StepCreated")),
                DueDate:             null,
                InstanceStarted:     rdr.GetDateTime(rdr.GetOrdinal("InstanceStarted")),
                InstanceStatus:      rdr.IsDBNull(rdr.GetOrdinal("InstanceStatus")) ? null : rdr.GetString(rdr.GetOrdinal("InstanceStatus"))
            ));
        }
        return items;
    }

    public async Task<PendingApprovalDetailDto?> GetPendingDetailAsync(int instanceId, CancellationToken ct)
    {
        // Tek satir basligi icin GetPendingForUserAsync ile ayni pencereyi paylasalim — basit yol:
        // tum bekleyenleri "all" scope ile cek, instance'i bul. Veya direct sorgu.
        // Performans icin: dogrudan instanceId ile sorgula.
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        var instance = await ReadInstanceAsync(con, instanceId, ct);
        if (instance is null) return null;

        // Header DTO'sunu olusturmak icin instance.CurrentStep'teki step kaydini bul
        var currentStep = instance.StepRecords.FirstOrDefault(s => s.StepOrder == instance.CurrentStep);
        if (currentStep is null && instance.StepRecords.Count > 0)
            currentStep = instance.StepRecords[0];

        var allStepOrders = instance.StepRecords.Select(s => s.StepOrder).OrderBy(o => o).ToList();
        var stepPosition  = allStepOrders.IndexOf(instance.CurrentStep) + 1;
        if (stepPosition < 1) stepPosition = 1;

        var header = new PendingApprovalItemDto(
            InstanceId:          instance.Id,
            StepRecordId:        currentStep?.Id ?? 0,
            StepOrder:           instance.CurrentStep,
            StepPosition:        stepPosition,
            TotalSteps:          instance.TotalSteps,
            StepName:            currentStep?.StepName ?? string.Empty,
            FlowName:            instance.FlowName,
            FlowId:              instance.FlowId,
            EntityKind:          instance.EntityKind ?? "Document",
            DocumentId:         instance.DocumentId,
            DocumentInternalId: null,
            DocumentNumber:     "—",
            DocumentDate:       DateTime.MinValue,
            DocumentTypeId:     null,
            DocumentTypeName:   null,
            ContactId:          null,
            ContactName:        null,
            GrandTotal:         0m,
            CurrencyCode:       null,
            ApproverId:         currentStep?.ApproverId,
            ApproverName:       currentStep?.ApproverName,
            StepCreated:        currentStep != null ? (currentStep.ActionDate ?? instance.StartedAt) : instance.StartedAt,
            DueDate:            currentStep?.DueDate,
            InstanceStarted:    instance.StartedAt
        );

        return new PendingApprovalDetailDto(header, instance.StepRecords);
    }

    public async Task ApproveStepAsync(
        int instanceId, int stepOrder,
        string approverId, string approverName, string? note,
        CancellationToken ct)
    {
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = con.BeginTransaction();
        try
        {
            // Mevcut adımı onayla
            await using var updStep = con.CreateCommand();
            updStep.Transaction = tx;
            updStep.CommandText = $"""
                UPDATE [{_s}].[ApprovalStepRecord]
                SET [Status]=N'Approved',[ApproverId]=@Aid,[ApproverName]=@AName,[Note]=@Note,[ActionDate]=SYSUTCDATETIME(),
                    [UpdatedById]=@UpdatedById,[Updated]=SYSUTCDATETIME()
                WHERE [InstanceId]=@Iid AND [StepOrder]=@Ord;
                """;
            updStep.Parameters.Add(new SqlParameter("@Aid", approverId));
            updStep.Parameters.Add(new SqlParameter("@AName", approverName));
            updStep.Parameters.Add(new SqlParameter("@Note", (object?)note ?? DBNull.Value));
            updStep.Parameters.Add(new SqlParameter("@Iid", instanceId));
            updStep.Parameters.Add(new SqlParameter("@Ord", stepOrder));
            updStep.Parameters.Add(new SqlParameter { ParameterName = "@UpdatedById", Value = DBNull.Value, SqlDbType = System.Data.SqlDbType.Int });
            await updStep.ExecuteNonQueryAsync(ct);

            // Sonraki adım var mı?
            await using var nextCmd = con.CreateCommand();
            nextCmd.Transaction = tx;
            nextCmd.CommandText = $"""
                SELECT MIN([StepOrder]) FROM [{_s}].[ApprovalStepRecord]
                WHERE [InstanceId]=@Iid AND [StepOrder] > @Ord AND [Status]=N'Pending';
                """;
            nextCmd.Parameters.Add(new SqlParameter("@Iid", instanceId));
            nextCmd.Parameters.Add(new SqlParameter("@Ord", stepOrder));
            var nextStepRaw = await nextCmd.ExecuteScalarAsync(ct);
            var nextStep = nextStepRaw == DBNull.Value ? (int?)null : Convert.ToInt32(nextStepRaw);

            await using var updInst = con.CreateCommand();
            updInst.Transaction = tx;
            if (nextStep.HasValue)
            {
                updInst.CommandText = $"""
                    UPDATE [{_s}].[ApprovalInstance]
                    SET [CurrentStep]=@Next,[UpdatedById]=@UpdatedById,[Updated]=SYSUTCDATETIME()
                    WHERE [Id]=@Iid;
                    """;
                updInst.Parameters.Add(new SqlParameter("@Next", nextStep.Value));
            }
            else
            {
                updInst.CommandText = $"""
                    UPDATE [{_s}].[ApprovalInstance]
                    SET [Status]=N'Approved',[CompletedAt]=SYSUTCDATETIME(),[UpdatedById]=@UpdatedById,[Updated]=SYSUTCDATETIME()
                    WHERE [Id]=@Iid;
                    """;
            }
            updInst.Parameters.Add(new SqlParameter("@Aid", approverId));
            updInst.Parameters.Add(new SqlParameter { ParameterName = "@UpdatedById", Value = DBNull.Value, SqlDbType = System.Data.SqlDbType.Int });
            updInst.Parameters.Add(new SqlParameter("@Iid", instanceId));
            await updInst.ExecuteNonQueryAsync(ct);

            // Sonraki step pending oldugunda DueDate'i (eger NULL ise) flow tanimina
            // gore set et. CreateAsync zaten her step icin Due hesapliyor; bu bir
            // safety net — ornegin eski instance'lar yeni SLA ile baslarsa.
            if (nextStep.HasValue)
            {
                await EnsureDueDateForStepAsync(con, tx, instanceId, nextStep.Value, ct);
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task RejectAsync(
        int instanceId, int stepOrder,
        string approverId, string approverName, string note,
        CancellationToken ct)
    {
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = con.BeginTransaction();
        try
        {
            await using var updStep = con.CreateCommand();
            updStep.Transaction = tx;
            updStep.CommandText = $"""
                UPDATE [{_s}].[ApprovalStepRecord]
                SET [Status]=N'Rejected',[ApproverId]=@Aid,[ApproverName]=@AName,[Note]=@Note,[ActionDate]=SYSUTCDATETIME(),
                    [UpdatedById]=@UpdatedById,[Updated]=SYSUTCDATETIME()
                WHERE [InstanceId]=@Iid AND [StepOrder]=@Ord;
                """;
            updStep.Parameters.Add(new SqlParameter("@Aid", approverId));
            updStep.Parameters.Add(new SqlParameter("@AName", approverName));
            updStep.Parameters.Add(new SqlParameter("@Note", note));
            updStep.Parameters.Add(new SqlParameter("@Iid", instanceId));
            updStep.Parameters.Add(new SqlParameter("@Ord", stepOrder));
            updStep.Parameters.Add(new SqlParameter { ParameterName = "@UpdatedById", Value = DBNull.Value, SqlDbType = System.Data.SqlDbType.Int });
            await updStep.ExecuteNonQueryAsync(ct);

            await using var updInst = con.CreateCommand();
            updInst.Transaction = tx;
            updInst.CommandText = $"""
                UPDATE [{_s}].[ApprovalInstance]
                SET [Status]=N'Rejected',[CompletedAt]=SYSUTCDATETIME(),[RejectNote]=@Note,
                    [UpdatedById]=@UpdatedById,[Updated]=SYSUTCDATETIME()
                WHERE [Id]=@Iid;
                """;
            updInst.Parameters.Add(new SqlParameter("@Aid", approverId));
            updInst.Parameters.Add(new SqlParameter { ParameterName = "@UpdatedById", Value = DBNull.Value, SqlDbType = System.Data.SqlDbType.Int });
            updInst.Parameters.Add(new SqlParameter("@Note", note));
            updInst.Parameters.Add(new SqlParameter("@Iid", instanceId));
            await updInst.ExecuteNonQueryAsync(ct);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task CancelAsync(int instanceId, string byUser, CancellationToken ct)
    {
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"""
            UPDATE [{_s}].[ApprovalInstance]
            SET [Status]=N'Cancelled',[CompletedAt]=SYSUTCDATETIME(),[IsActive]=0,
                [UpdatedById]=@UpdatedById,[Updated]=SYSUTCDATETIME()
            WHERE [Id]=@Iid;
            """;
        cmd.Parameters.Add(new SqlParameter("@By", byUser));
        cmd.Parameters.Add(new SqlParameter { ParameterName = "@UpdatedById", Value = DBNull.Value, SqlDbType = System.Data.SqlDbType.Int });
        cmd.Parameters.Add(new SqlParameter("@Iid", instanceId));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task ForceCompleteAsync(int instanceId, CancellationToken ct)
    {
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = con.CreateCommand();
        // WHERE Status='Pending' → normal approve/reject sonrası no-op (idempotent).
        cmd.CommandText = $"""
            UPDATE [{_s}].[ApprovalInstance]
            SET [Status]=N'Approved',[CompletedAt]=SYSUTCDATETIME(),[IsActive]=0,
                [Updated]=SYSUTCDATETIME()
            WHERE [Id]=@Iid AND [Status]=N'Pending';
            """;
        cmd.Parameters.Add(new SqlParameter("@Iid", instanceId));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateRevisionIdAsync(int instanceId, int revisionId, CancellationToken ct)
    {
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"""
            UPDATE [{_s}].[ApprovalInstance]
            SET [RevisionId] = @RevId, [Updated] = SYSUTCDATETIME()
            WHERE [Id] = @Iid;
            """;
        cmd.Parameters.Add(new SqlParameter("@RevId", revisionId));
        cmd.Parameters.Add(new SqlParameter("@Iid",   instanceId));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── SLA: tarama + isaretleme + eskale ─────────────────────────────────────

    public async Task<IReadOnlyList<OverdueStepRecord>> GetOverdueStepsAsync(DateTime nowUtc, CancellationToken ct)
    {
        // DueDate gecmis + Pending + henuz SLA aksiyonu yok.
        var sql = $"""
            SELECT sr.[Id], sr.[InstanceId], sr.[StepOrder], sr.[StepName], sr.[DueDate],
                   sr.[ApproverId], sr.[ApproverName],
                   i.[DocumentId], f.[Name] AS FlowName,
                   s.[NodeData]
            FROM [{_s}].[ApprovalStepRecord] sr
            JOIN [{_s}].[ApprovalInstance]  i  ON i.[Id] = sr.[InstanceId]
            JOIN [{_s}].[ApprovalFlow]              f  ON f.[Id] = i.[FlowId]
            LEFT JOIN [{_s}].[ApprovalFlowStep]     s  ON s.[FlowId] = i.[FlowId] AND s.[StepOrder] = sr.[StepOrder]
            WHERE sr.[Status] = N'Pending'
              AND sr.[DueDate] IS NOT NULL
              AND sr.[DueDate] < @Now
              AND sr.[SlaActionAt] IS NULL
              AND i.[IsActive] = 1 AND i.[Status] = N'Pending';
            """;
        return await QueryOverdueAsync(sql, nowUtc, ct);
    }

    public async Task<IReadOnlyList<OverdueStepRecord>> GetPendingWarningsAsync(DateTime nowUtc, CancellationToken ct)
    {
        // Pre-warning: DueDate yaklasiyor (NodeData icindeki slaReminderHoursBefore'a
        // gore filtre uygulamayi backend (SqlGetOverdueResultAsync) yerine burada SQL
        // ile yapamiyoruz (NodeData JSON parse C#-side). Bu yuzden DueDate'i bugunden
        // 48 saat icinde olan tum kayitlari cek, parse sonrasi pre-warning suresine
        // gore filtrele.
        var sql = $"""
            SELECT sr.[Id], sr.[InstanceId], sr.[StepOrder], sr.[StepName], sr.[DueDate],
                   sr.[ApproverId], sr.[ApproverName],
                   i.[DocumentId], f.[Name] AS FlowName,
                   s.[NodeData]
            FROM [{_s}].[ApprovalStepRecord] sr
            JOIN [{_s}].[ApprovalInstance]  i  ON i.[Id] = sr.[InstanceId]
            JOIN [{_s}].[ApprovalFlow]              f  ON f.[Id] = i.[FlowId]
            LEFT JOIN [{_s}].[ApprovalFlowStep]     s  ON s.[FlowId] = i.[FlowId] AND s.[StepOrder] = sr.[StepOrder]
            WHERE sr.[Status] = N'Pending'
              AND sr.[DueDate] IS NOT NULL
              AND sr.[DueDate] > @Now
              AND DATEDIFF(HOUR, @Now, sr.[DueDate]) <= 48
              AND sr.[SlaWarnedAt] IS NULL
              AND sr.[SlaActionAt] IS NULL
              AND i.[IsActive] = 1 AND i.[Status] = N'Pending';
            """;
        var candidates = await QueryOverdueAsync(sql, nowUtc, ct);

        // Pre-warning ayarina gore filtrele: warningAt = DueDate - slaReminderHoursBefore
        var due = new List<OverdueStepRecord>();
        foreach (var c in candidates)
        {
            if (!c.SlaEnabled) continue;
            if (c.SlaReminderHoursBefore <= 0) continue;
            if (!c.DueDate.HasValue) continue;
            var warnAt = c.DueDate.Value.AddHours(-c.SlaReminderHoursBefore);
            if (warnAt <= nowUtc) due.Add(c);
        }
        return due;
    }

    public async Task MarkSlaActionAsync(int recordId, string actionType, CancellationToken ct)
    {
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"""
            UPDATE [{_s}].[ApprovalStepRecord]
            SET [SlaActionAt]=SYSUTCDATETIME(),[SlaActionType]=@Type,
                [Updated]=SYSUTCDATETIME()
            WHERE [Id]=@Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Type", actionType));
        cmd.Parameters.Add(new SqlParameter("@Id", recordId));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task ResetSlaForLoopAsync(int instanceId, int stepOrder, string? nodeData, CancellationToken ct)
    {
        var newDue = ComputeDueDateFromNodeData(nodeData, DateTime.UtcNow);
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = con.CreateCommand();
        // DueDate: SLA tanımlıysa yeni süreye set, değilse NULL yap (artık geçerli deadline yok).
        cmd.CommandText = $"""
            UPDATE [{_s}].[ApprovalStepRecord]
            SET [SlaActionAt]   = NULL,
                [SlaActionType] = NULL,
                [SlaWarnedAt]   = NULL,
                [DueDate]       = @NewDue,
                [Updated]       = SYSUTCDATETIME()
            WHERE [InstanceId] = @Iid
              AND [StepOrder]  = @Ord
              AND [Status]     = N'Pending';
            """;
        cmd.Parameters.Add(new SqlParameter("@Iid",    instanceId));
        cmd.Parameters.Add(new SqlParameter("@Ord",    stepOrder));
        cmd.Parameters.Add(new SqlParameter("@NewDue", (object?)newDue ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task MarkSlaWarnedAsync(int recordId, CancellationToken ct)
    {
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"""
            UPDATE [{_s}].[ApprovalStepRecord]
            SET [SlaWarnedAt]=SYSUTCDATETIME(),[Updated]=SYSUTCDATETIME()
            WHERE [Id]=@Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", recordId));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> CreateEscalatedStepAsync(int sourceRecordId, string newApproverId, string newApproverName, CancellationToken ct)
    {
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = con.BeginTransaction();
        try
        {
            // Kaynak kaydi oku
            int instanceId; int stepOrder; string stepName; DateTime? srcDue;
            await using (var read = con.CreateCommand())
            {
                read.Transaction = tx;
                read.CommandText = $"""
                    SELECT [InstanceId],[StepOrder],[StepName],[DueDate]
                    FROM [{_s}].[ApprovalStepRecord]
                    WHERE [Id]=@Id;
                    """;
                read.Parameters.Add(new SqlParameter("@Id", sourceRecordId));
                await using var rdr = await read.ExecuteReaderAsync(ct);
                if (!await rdr.ReadAsync(ct))
                    throw new InvalidOperationException($"Kaynak kayit bulunamadi: {sourceRecordId}");
                instanceId = rdr.GetInt32(0);
                stepOrder  = rdr.GetInt32(1);
                stepName   = rdr.GetString(2);
                srcDue     = rdr.IsDBNull(3) ? null : rdr.GetDateTime(3);
            }

            // Kaynak kaydi Escalated'a cek (mevcut Status enum'unda yok — string serbest)
            await using (var upd = con.CreateCommand())
            {
                upd.Transaction = tx;
                upd.CommandText = $"""
                    UPDATE [{_s}].[ApprovalStepRecord]
                    SET [Status]=N'Escalated',[ActionDate]=SYSUTCDATETIME(),
                        [Updated]=SYSUTCDATETIME()
                    WHERE [Id]=@Id;
                    """;
                upd.Parameters.Add(new SqlParameter("@Id", sourceRecordId));
                await upd.ExecuteNonQueryAsync(ct);
            }

            // Yeni eskale kaydi — ayni step order, yeni approver, yeni DueDate (orijinal + 24sa)
            var newDue = srcDue?.AddHours(24);
            int newId;
            await using (var ins = con.CreateCommand())
            {
                ins.Transaction = tx;
                ins.CommandText = $"""
                    INSERT INTO [{_s}].[ApprovalStepRecord]
                        ([InstanceId],[StepOrder],[StepName],[Status],[ApproverId],[ApproverName],
                         [DueDate],[SlaEscalatedFromRecordId],[CreatedById],[Created])
                    VALUES
                        (@Iid,@Ord,@Name,N'Pending',@Aid,@AName,@Due,@Src,@CreatedById,SYSUTCDATETIME());
                    SELECT SCOPE_IDENTITY();
                    """;
                ins.Parameters.Add(new SqlParameter("@Iid", instanceId));
                ins.Parameters.Add(new SqlParameter("@Ord", stepOrder));
                ins.Parameters.Add(new SqlParameter("@Name", stepName + " (Eskale)"));
                ins.Parameters.Add(new SqlParameter("@Aid", newApproverId));
                ins.Parameters.Add(new SqlParameter("@AName", newApproverName));
                ins.Parameters.Add(new SqlParameter("@Due", (object?)newDue ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@Src", sourceRecordId));
                ins.Parameters.Add(new SqlParameter { ParameterName = "@CreatedById", Value = DBNull.Value, SqlDbType = System.Data.SqlDbType.Int });
                newId = Convert.ToInt32(await ins.ExecuteScalarAsync(ct));
            }

            tx.Commit();
            return newId;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<int?> GetInstanceIdByDocumentAsync(SqlConnection con, int documentId, CancellationToken ct)
    {
        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP 1 [Id] FROM [{_s}].[ApprovalInstance]
            WHERE [DocumentId]=@DocId AND [IsActive]=1
            ORDER BY [StartedAt] DESC;
            """;
        cmd.Parameters.Add(new SqlParameter("@DocId", documentId));
        var raw = await cmd.ExecuteScalarAsync(ct);
        return raw == null || raw == DBNull.Value ? null : Convert.ToInt32(raw);
    }

    private async Task<ApprovalInstanceDto?> ReadInstanceAsync(SqlConnection con, int instanceId, CancellationToken ct)
    {
        string? flowName = null;
        ApprovalInstanceDto? inst = null;

        await using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT i.[Id],i.[DocumentId],i.[EntityKind],i.[FlowId],f.[Name],i.[Status],i.[CurrentStep],
                       i.[StartedBy],i.[StartedAt],i.[CompletedAt],i.[RejectNote],
                       (SELECT COUNT(1) FROM [{_s}].[ApprovalStepRecord] sr WHERE sr.[InstanceId]=i.[Id]) AS TotalSteps
                FROM [{_s}].[ApprovalInstance] i
                JOIN [{_s}].[ApprovalFlow] f ON f.[Id]=i.[FlowId]
                WHERE i.[Id]=@Iid;
                """;
            cmd.Parameters.Add(new SqlParameter("@Iid", instanceId));
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return null;
            flowName = r.GetString(4);
            inst = new ApprovalInstanceDto(
                Id:          r.GetInt32(0),
                DocumentId:  r.IsDBNull(1) ? (int?)null : r.GetInt32(1),
                EntityKind:  r.IsDBNull(2) ? "Document" : r.GetString(2),
                FlowId:      r.GetInt32(3),
                FlowName:    flowName,
                Status:      r.GetString(5),
                CurrentStep: r.GetInt32(6),
                TotalSteps:  r.GetInt32(11),
                StartedBy:   r.IsDBNull(7) ? null : r.GetString(7),
                StartedAt:   r.GetDateTime(8),
                CompletedAt: r.IsDBNull(9) ? null : r.GetDateTime(9),
                RejectNote:  r.IsDBNull(10) ? null : r.GetString(10),
                StepRecords: Array.Empty<ApprovalStepRecordDto>());
        }

        if (inst is null) return null;

        var steps = new List<ApprovalStepRecordDto>();
        await using (var sCmd = con.CreateCommand())
        {
            sCmd.CommandText = $"""
                SELECT [Id],[InstanceId],[StepOrder],[StepName],[Status],[ApproverId],[ApproverName],[Note],[ActionDate],
                       [DueDate],[SlaWarnedAt],[SlaActionAt],[SlaActionType],[Created]
                FROM [{_s}].[ApprovalStepRecord]
                WHERE [InstanceId]=@Iid
                ORDER BY [StepOrder];
                """;
            sCmd.Parameters.Add(new SqlParameter("@Iid", instanceId));
            await using var sr = await sCmd.ExecuteReaderAsync(ct);
            while (await sr.ReadAsync(ct))
            {
                steps.Add(new ApprovalStepRecordDto(
                    sr.GetInt32(0), sr.GetInt32(1), sr.GetInt32(2), sr.GetString(3),
                    sr.GetString(4),
                    sr.IsDBNull(5) ? null : sr.GetString(5),
                    sr.IsDBNull(6) ? null : sr.GetString(6),
                    sr.IsDBNull(7) ? null : sr.GetString(7),
                    sr.IsDBNull(8) ? null : sr.GetDateTime(8),
                    sr.IsDBNull(9)  ? null : sr.GetDateTime(9),
                    sr.IsDBNull(10) ? null : sr.GetDateTime(10),
                    sr.IsDBNull(11) ? null : sr.GetDateTime(11),
                    sr.IsDBNull(12) ? null : sr.GetString(12),
                    EnteredAt: sr.IsDBNull(13) ? null : sr.GetDateTime(13)));
            }
        }

        return inst with { StepRecords = steps };
    }

    private async Task<IReadOnlyList<OverdueStepRecord>> QueryOverdueAsync(string sql, DateTime nowUtc, CancellationToken ct)
    {
        var list = new List<OverdueStepRecord>();
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@Now", nowUtc));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var stepRecordId = r.GetInt32(0);
            var instanceId   = r.GetInt32(1);
            var stepOrder    = r.GetInt32(2);
            var stepName     = r.GetString(3);
            var dueDate      = r.IsDBNull(4) ? (DateTime?)null : r.GetDateTime(4);
            var approverId   = r.IsDBNull(5) ? null : r.GetString(5);
            var approverName = r.IsDBNull(6) ? null : r.GetString(6);
            var documentId   = r.IsDBNull(7) ? (int?)null : (int?)r.GetInt32(7);
            var flowName     = r.GetString(8);
            var nodeData     = r.IsDBNull(9) ? null : r.GetString(9);

            var sla = ParseSlaSettings(nodeData);
            // DocumentNumber'i instance icin ayri cekmek pahali; bos birakiyoruz,
            // dispatcher template'inde fallback DocumentId'yi gosterebilir.
            list.Add(new OverdueStepRecord(
                instanceId, stepRecordId, stepOrder, stepName, dueDate,
                approverId, approverName,
                documentId, /*documentNumber:*/ null,
                flowName,
                sla.SlaEnabled, sla.SlaHours, sla.SlaTimeUnit, sla.SlaAction,
                sla.SlaEscalateToType, sla.SlaEscalateToId, sla.SlaEscalateToLabel,
                sla.SlaReminderHoursBefore, sla.SlaMessageTemplate, sla.SlaRejectReason));
        }
        return list;
    }

    private async Task EnsureDueDateForStepAsync(SqlConnection con, SqlTransaction tx, int instanceId, int stepOrder, CancellationToken ct)
    {
        // Bu adim icin DueDate zaten doluysa dokunma.
        await using var check = con.CreateCommand();
        check.Transaction = tx;
        check.CommandText = $"""
            SELECT sr.[DueDate], s.[NodeData]
            FROM [{_s}].[ApprovalStepRecord] sr
            JOIN [{_s}].[ApprovalInstance] i ON i.[Id] = sr.[InstanceId]
            LEFT JOIN [{_s}].[ApprovalFlowStep] s ON s.[FlowId] = i.[FlowId] AND s.[StepOrder] = sr.[StepOrder]
            WHERE sr.[InstanceId]=@Iid AND sr.[StepOrder]=@Ord;
            """;
        check.Parameters.Add(new SqlParameter("@Iid", instanceId));
        check.Parameters.Add(new SqlParameter("@Ord", stepOrder));
        DateTime? existing = null; string? nodeData = null;
        await using (var rdr = await check.ExecuteReaderAsync(ct))
        {
            if (!await rdr.ReadAsync(ct)) return;
            existing = rdr.IsDBNull(0) ? null : rdr.GetDateTime(0);
            nodeData = rdr.IsDBNull(1) ? null : rdr.GetString(1);
        }
        if (existing.HasValue) return;

        var due = ComputeDueDateFromNodeData(nodeData, DateTime.UtcNow);
        if (!due.HasValue) return;

        await using var upd = con.CreateCommand();
        upd.Transaction = tx;
        upd.CommandText = $"""
            UPDATE [{_s}].[ApprovalStepRecord]
            SET [DueDate]=@Due, [Updated]=SYSUTCDATETIME()
            WHERE [InstanceId]=@Iid AND [StepOrder]=@Ord;
            """;
        upd.Parameters.Add(new SqlParameter("@Due", (object)due.Value));
        upd.Parameters.Add(new SqlParameter("@Iid", instanceId));
        upd.Parameters.Add(new SqlParameter("@Ord", stepOrder));
        await upd.ExecuteNonQueryAsync(ct);
    }

    // ── NodeData JSON parse + DueDate hesaplama ──────────────────────────────

    internal static DateTime? ComputeDueDateFromNodeData(string? nodeData, DateTime fromUtc)
    {
        var sla = ParseSlaSettings(nodeData);
        if (!sla.SlaEnabled || sla.SlaHours <= 0) return null;
        return AddSlaDuration(fromUtc, sla.SlaHours, sla.SlaTimeUnit);
    }

    internal static DateTime AddSlaDuration(DateTime fromUtc, int amount, string timeUnit)
    {
        // timeUnit: "hours" | "days" | "businessDays"
        if (string.Equals(timeUnit, "days", StringComparison.OrdinalIgnoreCase))
            return fromUtc.AddHours(amount * 24);
        if (string.Equals(timeUnit, "businessDays", StringComparison.OrdinalIgnoreCase))
            return AddBusinessDays(fromUtc, amount);
        // default hours
        return fromUtc.AddHours(amount);
    }

    private static DateTime AddBusinessDays(DateTime fromUtc, int days)
    {
        // SkipWeekends: Cumartesi/Pazar atla. Tatil takvimi yok (MVP).
        var result = fromUtc;
        var added = 0;
        while (added < days)
        {
            result = result.AddDays(1);
            if (result.DayOfWeek != DayOfWeek.Saturday && result.DayOfWeek != DayOfWeek.Sunday)
                added++;
        }
        return result;
    }

    internal sealed record SlaSettings(
        bool SlaEnabled,
        int SlaHours,
        string SlaTimeUnit,
        string SlaAction,
        string? SlaEscalateToType,
        string? SlaEscalateToId,
        string? SlaEscalateToLabel,
        int SlaReminderHoursBefore,
        string? SlaMessageTemplate,
        string? SlaRejectReason);

    internal static SlaSettings ParseSlaSettings(string? nodeData)
    {
        var empty = new SlaSettings(false, 0, "hours", "reminder", null, null, null, 0, null, null);
        if (string.IsNullOrWhiteSpace(nodeData)) return empty;
        try
        {
            using var doc = JsonDocument.Parse(nodeData);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return empty;

            bool enabled = TryGetBool(root, "slaEnabled");
            int hours = TryGetInt(root, "slaHours");
            string timeUnit = TryGetString(root, "slaTimeUnit") ?? "hours";
            string action = TryGetString(root, "slaAction") ?? "reminder";
            string? escTo = TryGetString(root, "slaEscalateToType");
            string? escId = TryGetString(root, "slaEscalateToId");
            string? escLabel = TryGetString(root, "slaEscalateToLabel");
            int reminderH = TryGetInt(root, "slaReminderHoursBefore");
            string? template = TryGetString(root, "slaMessageTemplate");
            string? rejectReason = TryGetString(root, "slaRejectReason");
            return new SlaSettings(enabled, hours, timeUnit, action, escTo, escId, escLabel, reminderH, template, rejectReason);
        }
        catch
        {
            return empty;
        }
    }

    private static bool TryGetBool(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v)) return false;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(v.GetString(), out var b) && b,
            _ => false
        };
    }
    private static int TryGetInt(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v)) return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetInt32(out var i) ? i : 0,
            JsonValueKind.String => int.TryParse(v.GetString(), out var i) ? i : 0,
            _ => 0
        };
    }
    private static string? TryGetString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null
        };
    }

    public async Task<IReadOnlyList<ExtraColumnMetaDto>> GetViewColumnMetaAsync(string viewName, CancellationToken ct)
    {
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = con.CreateCommand();
        // INFORMATION_SCHEMA.COLUMNS sorgusu — schema + view adina gore kolon listesi
        // InstanceId kolonu join anahtaridir, kullaniciya gosterilmez.
        cmd.CommandText = $"""
            SELECT c.[COLUMN_NAME], c.[DATA_TYPE]
            FROM INFORMATION_SCHEMA.COLUMNS c
            WHERE c.[TABLE_SCHEMA] = @Schema
              AND c.[TABLE_NAME]   = @ViewName
              AND c.[COLUMN_NAME]  <> N'InstanceId'
            ORDER BY c.[ORDINAL_POSITION];
            """;
        cmd.Parameters.Add(new SqlParameter("@Schema", _s));
        cmd.Parameters.Add(new SqlParameter("@ViewName", viewName));

        var result = new List<ExtraColumnMetaDto>();
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            var colName  = rdr.GetString(0);
            var dataType = rdr.GetString(1);
            // Kolon adindan label uret: alt-cizgi/buyuk-harf → bosluklu
            var label = System.Text.RegularExpressions.Regex.Replace(colName, "([A-Z])", " $1").Trim()
                             .Replace("_", " ").Trim();
            var metaType = dataType switch
            {
                "int" or "bigint" or "smallint" or "tinyint"
                    or "decimal" or "numeric" or "float" or "real" or "money" or "smallmoney" => "numeric",
                "date" or "datetime" or "datetime2" or "smalldatetime" or "datetimeoffset" => "date",
                _ => "text"
            };
            result.Add(new ExtraColumnMetaDto(colName, label, metaType));
        }
        return result;
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyDictionary<string, string?>>> GetViewRowDataAsync(
        string viewName, IReadOnlyCollection<int> instanceIds, CancellationToken ct)
    {
        var result = new Dictionary<int, IReadOnlyDictionary<string, string?>>();
        if (instanceIds.Count == 0) return result;

        // instanceIds int listesi — SQL injection riski yok; viewName controller'da regex + whitelist ile dogrulandi.
        var inClause = string.Join(",", instanceIds);
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"SELECT * FROM [{_s}].[{viewName}] WHERE [InstanceId] IN ({inClause})";

        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        var fieldCount = rdr.FieldCount;
        var colNames = Enumerable.Range(0, fieldCount).Select(i => rdr.GetName(i)).ToArray();
        int instanceIdOrdinal = Array.FindIndex(colNames, c => string.Equals(c, "InstanceId", StringComparison.OrdinalIgnoreCase));

        while (await rdr.ReadAsync(ct))
        {
            if (instanceIdOrdinal < 0) break;
            var instanceId = rdr.IsDBNull(instanceIdOrdinal) ? 0 : rdr.GetInt32(instanceIdOrdinal);
            if (instanceId <= 0) continue;

            var row = new Dictionary<string, string?>();
            for (var i = 0; i < fieldCount; i++)
            {
                if (i == instanceIdOrdinal) continue;
                if (rdr.IsDBNull(i)) { row[colNames[i]] = null; continue; }
                var val = rdr.GetValue(i);
                row[colNames[i]] = val switch
                {
                    DateTime dt  => dt.ToString("yyyy-MM-dd HH:mm"),
                    decimal  d   => d.ToString("N2"),
                    double   d   => d.ToString("G"),
                    float    f   => f.ToString("G"),
                    _            => val.ToString()
                };
            }
            result[instanceId] = row;
        }
        return result;
    }

    // ── Timer node işlemleri ────────────────────────────────────────────────

    public async Task CreateTimerNodeRecordAsync(
        int instanceId, int timerNodeId, int stepOrder, string stepName, DateTime fireAt, CancellationToken ct)
    {
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO [{_s}].[ApprovalStepRecord]
                ([InstanceId],[StepOrder],[StepName],[Status],[ApproverId],[ApproverName],[DueDate],[Created])
            VALUES
                (@InstanceId, @StepOrder, @StepName, N'WaitingTimer', @NodeId, NULL, @FireAt, SYSUTCDATETIME())
            """;
        cmd.Parameters.Add(new SqlParameter("@InstanceId", instanceId));
        cmd.Parameters.Add(new SqlParameter("@StepOrder", stepOrder));
        cmd.Parameters.Add(new SqlParameter("@StepName", stepName));
        // NodeId'yi ApproverId kolonuna geçici olarak saklıyoruz (timer node'unu tanımlamak için).
        cmd.Parameters.Add(new SqlParameter("@NodeId", timerNodeId.ToString()));
        cmd.Parameters.Add(new SqlParameter("@FireAt", fireAt));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<PendingTimerRecord>> GetFiredTimersAsync(DateTime nowUtc, CancellationToken ct)
    {
        var result = new List<PendingTimerRecord>();
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id], [InstanceId], [ApproverId], [DueDate]
            FROM [{_s}].[ApprovalStepRecord]
            WHERE [Status] = N'WaitingTimer'
              AND [DueDate] <= @Now
            """;
        cmd.Parameters.Add(new SqlParameter("@Now", nowUtc));
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            var recordId    = rdr.GetInt32(0);
            var instanceId  = rdr.GetInt32(1);
            var nodeIdStr   = rdr.IsDBNull(2) ? "0" : rdr.GetString(2);
            var fireAt      = rdr.IsDBNull(3) ? nowUtc : rdr.GetDateTime(3);
            if (!int.TryParse(nodeIdStr, out var timerNodeId)) timerNodeId = 0;
            result.Add(new PendingTimerRecord(recordId, instanceId, timerNodeId, fireAt));
        }
        return result;
    }

    public async Task MarkTimerFiredAsync(int recordId, CancellationToken ct)
    {
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"""
            UPDATE [{_s}].[ApprovalStepRecord]
            SET [Status] = N'TimerFired', [Updated] = SYSUTCDATETIME()
            WHERE [Id] = @Id AND [Status] = N'WaitingTimer'
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", recordId));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Vote node ─────────────────────────────────────────────────────────────

    public async Task CreateVoteStepRecordsAsync(
        int instanceId, int stepOrder, string stepName,
        IReadOnlyList<(string VoterId, string VoterName)> voters,
        string? nodeData, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var dueDate = ComputeDueDateFromNodeData(nodeData, now);

        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = con.BeginTransaction();
        try
        {
            foreach (var (voterId, voterName) in voters)
            {
                await using var ins = con.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = $"""
                    INSERT INTO [{_s}].[ApprovalStepRecord]
                        ([InstanceId],[StepOrder],[StepName],[Status],[ApproverId],[ApproverName],[DueDate],[CreatedById],[Created])
                    VALUES
                        (@Iid,@Ord,@Name,N'Pending',@VId,@VName,@Due,NULL,SYSUTCDATETIME());
                    """;
                ins.Parameters.Add(new SqlParameter("@Iid",   instanceId));
                ins.Parameters.Add(new SqlParameter("@Ord",   stepOrder));
                ins.Parameters.Add(new SqlParameter("@Name",  stepName));
                ins.Parameters.Add(new SqlParameter("@VId",   voterId));
                ins.Parameters.Add(new SqlParameter("@VName", voterName));
                ins.Parameters.Add(new SqlParameter("@Due",   (object?)dueDate ?? DBNull.Value));
                await ins.ExecuteNonQueryAsync(ct);
            }

            // Ensure CurrentStep is set to the vote node's StepOrder
            await using var upd = con.CreateCommand();
            upd.Transaction = tx;
            upd.CommandText = $"""
                UPDATE [{_s}].[ApprovalInstance]
                SET [CurrentStep]=@Ord,[Updated]=SYSUTCDATETIME()
                WHERE [Id]=@Iid;
                """;
            upd.Parameters.Add(new SqlParameter("@Iid", instanceId));
            upd.Parameters.Add(new SqlParameter("@Ord", stepOrder));
            await upd.ExecuteNonQueryAsync(ct);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task<VoteConsensusResult> VoteOnStepAsync(
        int instanceId, int stepOrder,
        string approverId, string approverName, string? note, bool isApprove,
        string consensusType, CancellationToken ct)
    {
        var status = isApprove ? "Approved" : "Rejected";

        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = con.BeginTransaction();
        try
        {
            // Update only this voter's Pending record
            await using var updVote = con.CreateCommand();
            updVote.Transaction = tx;
            updVote.CommandText = $"""
                UPDATE [{_s}].[ApprovalStepRecord]
                SET [Status]=@Status,[ApproverName]=@AName,[Note]=@Note,
                    [ActionDate]=SYSUTCDATETIME(),[Updated]=SYSUTCDATETIME()
                WHERE [InstanceId]=@Iid AND [StepOrder]=@Ord
                  AND [ApproverId]=@Aid AND [Status]=N'Pending';
                """;
            updVote.Parameters.Add(new SqlParameter("@Status", status));
            updVote.Parameters.Add(new SqlParameter("@AName",  approverName));
            updVote.Parameters.Add(new SqlParameter("@Note",   (object?)note ?? DBNull.Value));
            updVote.Parameters.Add(new SqlParameter("@Iid",    instanceId));
            updVote.Parameters.Add(new SqlParameter("@Ord",    stepOrder));
            updVote.Parameters.Add(new SqlParameter("@Aid",    approverId));
            var affected = await updVote.ExecuteNonQueryAsync(ct);
            if (affected == 0)
            {
                tx.Commit();
                return new VoteConsensusResult(Voted: false, ConsensusReached: false, ConsensusApproved: false,
                    TotalVoters: 0, ApprovedCount: 0, RejectedCount: 0, PendingCount: 0, ConsensusType: consensusType);
            }

            // Read current vote tally
            await using var tally = con.CreateCommand();
            tally.Transaction = tx;
            tally.CommandText = $"""
                SELECT
                    COUNT(*) AS Total,
                    SUM(CASE WHEN [Status]=N'Approved' THEN 1 ELSE 0 END) AS Approved,
                    SUM(CASE WHEN [Status]=N'Rejected' THEN 1 ELSE 0 END) AS Rejected,
                    SUM(CASE WHEN [Status]=N'Pending'  THEN 1 ELSE 0 END) AS Pending
                FROM [{_s}].[ApprovalStepRecord]
                WHERE [InstanceId]=@Iid AND [StepOrder]=@Ord;
                """;
            tally.Parameters.Add(new SqlParameter("@Iid", instanceId));
            tally.Parameters.Add(new SqlParameter("@Ord", stepOrder));
            await using var rdr = await tally.ExecuteReaderAsync(ct);
            int total = 0, approved = 0, rejected = 0, pending = 0;
            if (await rdr.ReadAsync(ct))
            {
                total    = rdr.GetInt32(0);
                approved = rdr.GetInt32(1);
                rejected = rdr.GetInt32(2);
                pending  = rdr.GetInt32(3);
            }
            await rdr.CloseAsync();

            // Determine consensus
            bool consensusReached;
            bool consensusApproved;
            var ct2 = consensusType.Trim().ToLowerInvariant();
            switch (ct2)
            {
                case "any":
                    consensusReached  = true;
                    consensusApproved = isApprove;
                    break;
                case "unanimous":
                    consensusReached  = (pending == 0);
                    consensusApproved = (pending == 0 && rejected == 0);
                    break;
                default: // "majority"
                    var half = total / 2.0;
                    consensusReached  = approved > half || rejected > half;
                    consensusApproved = approved > half;
                    break;
            }

            if (consensusReached)
            {
                if (consensusApproved)
                {
                    // Mark remaining Pending records as Skipped (vote concluded)
                    await using var skipCmd = con.CreateCommand();
                    skipCmd.Transaction = tx;
                    skipCmd.CommandText = $"""
                        UPDATE [{_s}].[ApprovalStepRecord]
                        SET [Status]=N'Skipped',[Updated]=SYSUTCDATETIME()
                        WHERE [InstanceId]=@Iid AND [StepOrder]=@Ord AND [Status]=N'Pending';
                        """;
                    skipCmd.Parameters.Add(new SqlParameter("@Iid", instanceId));
                    skipCmd.Parameters.Add(new SqlParameter("@Ord", stepOrder));
                    await skipCmd.ExecuteNonQueryAsync(ct);

                    // Advance CurrentStep
                    await using var nextCmd = con.CreateCommand();
                    nextCmd.Transaction = tx;
                    nextCmd.CommandText = $"""
                        SELECT MIN([StepOrder]) FROM [{_s}].[ApprovalStepRecord]
                        WHERE [InstanceId]=@Iid AND [StepOrder]>@Ord AND [Status]=N'Pending';
                        """;
                    nextCmd.Parameters.Add(new SqlParameter("@Iid", instanceId));
                    nextCmd.Parameters.Add(new SqlParameter("@Ord", stepOrder));
                    var nextRaw = await nextCmd.ExecuteScalarAsync(ct);
                    var nextStep = nextRaw == DBNull.Value ? (int?)null : Convert.ToInt32(nextRaw);

                    await using var updInst = con.CreateCommand();
                    updInst.Transaction = tx;
                    if (nextStep.HasValue)
                    {
                        updInst.CommandText = $"""
                            UPDATE [{_s}].[ApprovalInstance]
                            SET [CurrentStep]=@Next,[Updated]=SYSUTCDATETIME()
                            WHERE [Id]=@Iid;
                            """;
                        updInst.Parameters.Add(new SqlParameter("@Next", nextStep.Value));
                    }
                    else
                    {
                        updInst.CommandText = $"""
                            UPDATE [{_s}].[ApprovalInstance]
                            SET [Status]=N'Approved',[CompletedAt]=SYSUTCDATETIME(),[Updated]=SYSUTCDATETIME()
                            WHERE [Id]=@Iid;
                            """;
                    }
                    updInst.Parameters.Add(new SqlParameter("@Iid", instanceId));
                    await updInst.ExecuteNonQueryAsync(ct);
                }
                else
                {
                    // Red oylaması consensus'a ulaştı — instance'ı Rejected yap
                    await using var rejInst = con.CreateCommand();
                    rejInst.Transaction = tx;
                    rejInst.CommandText = $"""
                        UPDATE [{_s}].[ApprovalInstance]
                        SET [Status]=N'Rejected',[CompletedAt]=SYSUTCDATETIME(),
                            [RejectNote]=@Note,[Updated]=SYSUTCDATETIME()
                        WHERE [Id]=@Iid;
                        """;
                    rejInst.Parameters.Add(new SqlParameter("@Iid",  instanceId));
                    rejInst.Parameters.Add(new SqlParameter("@Note", (object?)note ?? DBNull.Value));
                    await rejInst.ExecuteNonQueryAsync(ct);
                }
            }

            tx.Commit();
            return new VoteConsensusResult(
                Voted: true,
                ConsensusReached:  consensusReached,
                ConsensusApproved: consensusApproved,
                TotalVoters:    total,
                ApprovedCount:  approved,
                RejectedCount:  rejected,
                PendingCount:   pending,
                ConsensusType:  consensusType);
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}
