using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;
using System.Linq;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// Kalite muayene planı + muayene kaydı veri erişimi. Muayene kaydının Document shell'i
/// service tarafında IDocumentRepository ile yazılır; bu repo companion + native satırları yönetir.
/// Stok repo'suna hiç referans yok. FK kasing: Document PK [id] (küçük harf), diğerleri [Id].
/// </summary>
public sealed class SqlQualityRepository : IQualityRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _planTable;
    private readonly string _planLineTable;
    private readonly string _inspTable;
    private readonly string _inspLineTable;
    private readonly string _defectTable;
    private readonly string _docTable;
    private readonly string _itemsTable;
    private readonly string _cardGroupTable;
    private readonly string _cardGroupMappingTable;

    public SqlQualityRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options)
    {
        _connectionFactory = factory;
        var s = (string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim()).Replace("]", "]]");
        _planTable = $"[{s}].[QualityInspectionPlan]";
        _planLineTable = $"[{s}].[QualityInspectionPlanLine]";
        _inspTable = $"[{s}].[QualityInspection]";
        _inspLineTable = $"[{s}].[QualityInspectionLine]";
        _defectTable = $"[{s}].[QualityDefectCode]";
        _docTable = $"[{s}].[Document]";
        _itemsTable = $"[{s}].[Items]";
        // Malzeme grubu artık legacy MaterialGroups DEĞİL — CardGroup (CardType=1) + CardGroupMapping (EntityType=1).
        // Sebep: Item'ların gerçek grup ilişkisi CardGroupMapping ile kurulur; MaterialGroups hiç kullanılmıyordu (bkz. CLAUDE.md/Seq 54).
        _cardGroupTable = $"[{s}].[CardGroup]";
        _cardGroupMappingTable = $"[{s}].[CardGroupMapping]";
    }

    private static string Describe(Enum v) => SqlQualityDefectCodeRepository.Describe(v);

    // ── Muayene Planı ─────────────────────────────────────────────

    public async Task<IReadOnlyCollection<QualityInspectionPlanListItem>> ListPlansAsync(string? search, byte? inspectionType, CancellationToken ct)
    {
        var where = "p.[IsActive] = 1";
        if (inspectionType.HasValue) where += " AND p.[InspectionType] = @Type";
        if (!string.IsNullOrWhiteSpace(search)) where += " AND (p.[Name] LIKE @S OR it.[Name] LIKE @S)";
        var sql = $"""
            SELECT p.[Id], p.[ItemId], it.[Name] AS ItemName, p.[MaterialGroupId],
                   COALESCE(NULLIF(g.[Description], ''), g.[Code]) AS GroupName,
                   p.[InspectionType], p.[Name], p.[IsActive], p.[Created],
                   (SELECT COUNT(*) FROM {_planLineTable} l WHERE l.[PlanId] = p.[Id]) AS LineCount
            FROM {_planTable} p
            LEFT JOIN {_itemsTable} it ON it.[Id] = p.[ItemId]
            LEFT JOIN {_cardGroupTable} g ON g.[Id] = p.[MaterialGroupId] AND g.[CardType] = 1
            WHERE {where}
            ORDER BY p.[Created] DESC;
            """;
        var list = new List<QualityInspectionPlanListItem>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        if (inspectionType.HasValue) cmd.Parameters.Add(new SqlParameter("@Type", inspectionType.Value));
        if (!string.IsNullOrWhiteSpace(search)) cmd.Parameters.Add(new SqlParameter("@S", $"%{search.Trim()}%"));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var type = r.GetByte(5);
            list.Add(new QualityInspectionPlanListItem(
                Id: r.GetInt32(0),
                ItemId: r.IsDBNull(1) ? null : r.GetInt32(1),
                ItemName: r.IsDBNull(2) ? null : r.GetString(2),
                MaterialGroupId: r.IsDBNull(3) ? null : r.GetInt32(3),
                MaterialGroupName: r.IsDBNull(4) ? null : r.GetString(4),
                InspectionType: type,
                InspectionTypeLabel: Describe((InspectionType)type),
                Name: r.GetString(6),
                IsActive: r.GetBoolean(7),
                Created: r.GetDateTime(8),
                LineCount: r.GetInt32(9)));
        }
        return list;
    }

    public async Task<QualityInspectionPlanDetail?> GetPlanAsync(int planId, CancellationToken ct)
    {
        var sql = $"""
            SELECT [Id],[ItemId],[MaterialGroupId],[InspectionType],[Name],[IsActive] FROM {_planTable} WHERE [Id]=@P;
            SELECT [Id],[CharacteristicName],[Nominal],[LowerTol],[UpperTol],[UnitId],[Method],[GaugeName],[IsNumeric],[OrderNo]
            FROM {_planLineTable} WHERE [PlanId]=@P ORDER BY [OrderNo],[Id];
            """;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@P", planId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        var head = (Id: r.GetInt32(0), ItemId: r.IsDBNull(1) ? (int?)null : r.GetInt32(1),
            MatGroup: r.IsDBNull(2) ? (int?)null : r.GetInt32(2), Type: r.GetByte(3), Name: r.GetString(4), Active: r.GetBoolean(5));
        var lines = new List<QualityInspectionPlanLineDto>();
        await r.NextResultAsync(ct);
        while (await r.ReadAsync(ct)) lines.Add(ReadPlanLine(r));
        return new QualityInspectionPlanDetail(head.Id, head.ItemId, head.MatGroup, head.Type, head.Name, head.Active, lines);
    }

    private static QualityInspectionPlanLineDto ReadPlanLine(SqlDataReader r) => new(
        Id: r.GetInt32(0),
        CharacteristicName: r.GetString(1),
        Nominal: r.IsDBNull(2) ? null : r.GetDecimal(2),
        LowerTol: r.IsDBNull(3) ? null : r.GetDecimal(3),
        UpperTol: r.IsDBNull(4) ? null : r.GetDecimal(4),
        UnitId: r.IsDBNull(5) ? null : r.GetInt32(5),
        Method: r.IsDBNull(6) ? null : r.GetString(6),
        GaugeName: r.IsDBNull(7) ? null : r.GetString(7),
        IsNumeric: r.GetBoolean(8),
        OrderNo: r.GetInt32(9));

    public async Task<int> UpsertPlanAsync(QualityInspectionPlan plan, IReadOnlyList<QualityInspectionPlanLine> lines, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            int planId;
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                var companyId = _connectionFactory.ResolveEffectiveCompanyId();
                cmd.CommandText = plan.Id > 0
                    ? $"""
                        UPDATE {_planTable} SET [ItemId]=@Item,[MaterialGroupId]=@Grp,[InspectionType]=@Type,
                            [Name]=@Name,[IsActive]=@Active,[UpdatedById]=@Upd,[Updated]=SYSUTCDATETIME() WHERE [Id]=@Id AND [CompanyId]=@Company;
                        SELECT [Id] FROM {_planTable} WHERE [Id]=@Id AND [CompanyId]=@Company;
                        """
                    : $"""
                        INSERT INTO {_planTable} ([ItemId],[MaterialGroupId],[InspectionType],[Name],[IsActive],[CreatedById],[Created],[CompanyId])
                        VALUES (@Item,@Grp,@Type,@Name,@Active,@Cre,SYSUTCDATETIME(),@Company);
                        SELECT CAST(SCOPE_IDENTITY() AS INT);
                        """;
                cmd.Parameters.Add(new SqlParameter("@Id", plan.Id));
                cmd.Parameters.Add(new SqlParameter("@Item", (object?)plan.ItemId ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Grp", (object?)plan.MaterialGroupId ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Type", (byte)plan.InspectionType));
                cmd.Parameters.Add(new SqlParameter("@Name", plan.Name));
                cmd.Parameters.Add(new SqlParameter("@Active", plan.IsActive));
                cmd.Parameters.Add(new SqlParameter("@Cre", (object?)plan.CreatedById ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Upd", (object?)plan.UpdatedById ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Company", companyId));
                planId = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
            }
            // tenant-ok: planId yukarida SELECT ... WHERE [Id]=@Id AND [CompanyId]=@Company ile
            // dogrulandi (yabanci sirket plan.Id ise ExecuteScalar null doner ve Convert.ToInt32 patlar) —
            // bu satirlar zaten sadece kendi sirketimizin plani icin calisir.
            await using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = $"DELETE FROM {_planLineTable} WHERE [PlanId]=@P;";
                del.Parameters.Add(new SqlParameter("@P", planId));
                await del.ExecuteNonQueryAsync(ct);
            }
            foreach (var l in lines)
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                // tenant-ok: PlanId (@P) yukarida dogrulanan planId — bkz. yukaridaki not.
                cmd.CommandText = $"""
                    INSERT INTO {_planLineTable} ([PlanId],[CharacteristicName],[Nominal],[LowerTol],[UpperTol],[UnitId],[Method],[GaugeName],[IsNumeric],[OrderNo],[CreatedById],[Created])
                    VALUES (@P,@Char,@Nom,@Low,@Up,@Unit,@Method,@Gauge,@Num,@Order,@Cre,SYSUTCDATETIME());
                    """;
                cmd.Parameters.Add(new SqlParameter("@P", planId));
                cmd.Parameters.Add(new SqlParameter("@Char", l.CharacteristicName));
                cmd.Parameters.Add(new SqlParameter("@Nom", (object?)l.Nominal ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Low", (object?)l.LowerTol ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Up", (object?)l.UpperTol ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Unit", (object?)l.UnitId ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Method", (object?)l.Method ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Gauge", (object?)l.GaugeName ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Num", l.IsNumeric));
                cmd.Parameters.Add(new SqlParameter("@Order", l.OrderNo));
                cmd.Parameters.Add(new SqlParameter("@Cre", (object?)l.CreatedById ?? DBNull.Value));
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
            return planId;
        }
        catch { await tx.RollbackAsync(ct); throw; }
    }

    public async Task SoftDeletePlanAsync(int planId, int? userId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE {_planTable} SET [IsActive]=0,[UpdatedById]=@Upd,[Updated]=SYSUTCDATETIME() WHERE [Id]=@Id AND [CompanyId]=@Company;";
        cmd.Parameters.Add(new SqlParameter("@Id", planId));
        cmd.Parameters.Add(new SqlParameter("@Upd", (object?)userId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Company", _connectionFactory.ResolveEffectiveCompanyId()));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<QualityPlanLookup?> FindPlanForItemAsync(int itemId, byte inspectionType, CancellationToken ct)
    {
        // 1) Önce item-özel plan — bulunursa grup planını EZER (erken dön).
        // 2) Yoksa item'ın CardGroup (CardType=1) üyeliklerine tanımlı grup planına düş.
        var sql = $"""
            SELECT TOP 1 [Id],[Name] FROM {_planTable}
            WHERE [IsActive]=1 AND [InspectionType]=@Type AND [ItemId]=@Item ORDER BY [Created] DESC;
            SELECT TOP 1 [Id],[Name] FROM {_planTable}
            WHERE [IsActive]=1 AND [InspectionType]=@Type AND [ItemId] IS NULL
              AND [MaterialGroupId] IN (
                  SELECT [CardGroupId] FROM {_cardGroupMappingTable}
                  WHERE [EntityType]=1 AND [EntityId]=@ItemStr)
            ORDER BY [Created] DESC;
            """;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        int planId; string planName;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = sql;
            cmd.Parameters.Add(new SqlParameter("@Type", inspectionType));
            cmd.Parameters.Add(new SqlParameter("@Item", itemId));
            cmd.Parameters.Add(new SqlParameter("@ItemStr", itemId.ToString()));
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                planId = r.GetInt32(0); planName = r.GetString(1);
            }
            else
            {
                if (!await r.NextResultAsync(ct) || !await r.ReadAsync(ct)) return null;
                planId = r.GetInt32(0); planName = r.GetString(1);
            }
        }
        var lines = new List<QualityInspectionPlanLineDto>();
        await using (var lcmd = conn.CreateCommand())
        {
            lcmd.CommandText = $"""
                SELECT [Id],[CharacteristicName],[Nominal],[LowerTol],[UpperTol],[UnitId],[Method],[GaugeName],[IsNumeric],[OrderNo]
                FROM {_planLineTable} WHERE [PlanId]=@P ORDER BY [OrderNo],[Id];
                """;
            lcmd.Parameters.Add(new SqlParameter("@P", planId));
            await using var lr = await lcmd.ExecuteReaderAsync(ct);
            while (await lr.ReadAsync(ct)) lines.Add(ReadPlanLine(lr));
        }
        return new QualityPlanLookup(planId, planName, lines);
    }

    // ── Muayene Kaydı ─────────────────────────────────────────────

    public async Task<IReadOnlyCollection<QualityInspectionListItem>> ListInspectionsAsync(string? search, byte? status, byte? verdict, CancellationToken ct)
    {
        var where = "d.[IsActive] = 1";
        if (status.HasValue) where += " AND q.[Status] = @Status";
        if (verdict.HasValue) where += " AND q.[Verdict] = @Verdict";
        if (!string.IsNullOrWhiteSpace(search)) where += " AND (d.[DocumentNumber] LIKE @S OR it.[Name] LIKE @S)";
        var sql = $"""
            SELECT q.[DocumentId], d.[DocumentNumber], q.[ItemId], it.[Name] AS ItemName,
                   q.[InspectionType], q.[Status], q.[Verdict], q.[Disposition], q.[InspectedAt], q.[Created]
            FROM {_inspTable} q
            INNER JOIN {_docTable} d ON d.[id] = q.[DocumentId]
            LEFT JOIN {_itemsTable} it ON it.[Id] = q.[ItemId]
            WHERE {where}
            ORDER BY q.[Created] DESC;
            """;
        var list = new List<QualityInspectionListItem>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        if (status.HasValue) cmd.Parameters.Add(new SqlParameter("@Status", status.Value));
        if (verdict.HasValue) cmd.Parameters.Add(new SqlParameter("@Verdict", verdict.Value));
        if (!string.IsNullOrWhiteSpace(search)) cmd.Parameters.Add(new SqlParameter("@S", $"%{search.Trim()}%"));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var type = r.GetByte(4); var st = r.GetByte(5);
            byte? vd = r.IsDBNull(6) ? null : r.GetByte(6);
            byte? disp = r.IsDBNull(7) ? null : r.GetByte(7);
            list.Add(new QualityInspectionListItem(
                DocumentId: r.GetInt32(0),
                DocumentNumber: r.GetString(1),
                ItemId: r.IsDBNull(2) ? null : r.GetInt32(2),
                ItemName: r.IsDBNull(3) ? null : r.GetString(3),
                InspectionType: type,
                InspectionTypeLabel: Describe((InspectionType)type),
                Status: st,
                StatusLabel: Describe((InspectionStatus)st),
                Verdict: vd,
                VerdictLabel: vd.HasValue ? Describe((InspectionVerdict)vd.Value) : null,
                Disposition: disp,
                DispositionLabel: disp.HasValue ? Describe((InspectionDisposition)disp.Value) : null,
                InspectedAt: r.IsDBNull(8) ? null : r.GetDateTime(8),
                Created: r.GetDateTime(9)));
        }
        return list;
    }

    public async Task<QualityInspection?> GetInspectionByDocumentIdAsync(int documentId, CancellationToken ct)
    {
        var sql = $"""
            SELECT [Id],[DocumentId],[PlanId],[ItemId],[InspectionType],[Status],[Verdict],[Disposition],
                   [SourceKind],[SourceId],[Quantity],[InspectedByPersonnelId],[InspectedAt],[Notes],
                   [CreatedById],[Created],[UpdatedById],[Updated]
            FROM {_inspTable} WHERE [DocumentId]=@Doc;
            """;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@Doc", documentId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new QualityInspection
        {
            Id = r.GetInt32(0),
            DocumentId = r.GetInt32(1),
            PlanId = r.IsDBNull(2) ? null : r.GetInt32(2),
            ItemId = r.IsDBNull(3) ? null : r.GetInt32(3),
            InspectionType = (InspectionType)r.GetByte(4),
            Status = (InspectionStatus)r.GetByte(5),
            Verdict = r.IsDBNull(6) ? null : (InspectionVerdict)r.GetByte(6),
            Disposition = r.IsDBNull(7) ? null : (InspectionDisposition)r.GetByte(7),
            SourceKind = r.IsDBNull(8) ? null : r.GetString(8),
            SourceId = r.IsDBNull(9) ? null : r.GetInt32(9),
            Quantity = r.IsDBNull(10) ? null : r.GetDecimal(10),
            InspectedByPersonnelId = r.IsDBNull(11) ? null : r.GetInt32(11),
            InspectedAt = r.IsDBNull(12) ? null : r.GetDateTime(12),
            Notes = r.IsDBNull(13) ? null : r.GetString(13),
            CreatedById = r.IsDBNull(14) ? null : r.GetInt32(14),
            Created = r.GetDateTime(15),
            UpdatedById = r.IsDBNull(16) ? null : r.GetInt32(16),
            Updated = r.IsDBNull(17) ? null : r.GetDateTime(17),
        };
    }

    public async Task<QualityInspectionDetail?> GetInspectionDetailAsync(int documentId, CancellationToken ct)
    {
        var sql = $"""
            SELECT q.[DocumentId], d.[DocumentNumber], q.[PlanId], q.[ItemId], q.[InspectionType], q.[Status],
                   q.[Verdict], q.[Disposition], q.[SourceKind], q.[SourceId], q.[Quantity],
                   q.[InspectedByPersonnelId], q.[InspectedAt], q.[Notes]
            FROM {_inspTable} q INNER JOIN {_docTable} d ON d.[id]=q.[DocumentId]
            WHERE q.[DocumentId]=@Doc;
            SELECT l.[Id],l.[PlanLineId],l.[CharacteristicName],l.[Nominal],l.[LowerTol],l.[UpperTol],
                   l.[Measured],l.[IsNumeric],l.[Result],l.[DefectCodeId],dc.[Name],l.[OrderNo],l.[Notes]
            FROM {_inspLineTable} l
            LEFT JOIN {_defectTable} dc ON dc.[Id]=l.[DefectCodeId]
            WHERE l.[InspectionId]=(SELECT [Id] FROM {_inspTable} WHERE [DocumentId]=@Doc)
            ORDER BY l.[OrderNo],l.[Id];
            """;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@Doc", documentId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        var head = new QualityInspectionDetail(
            DocumentId: r.GetInt32(0), DocumentNumber: r.GetString(1),
            PlanId: r.IsDBNull(2) ? null : r.GetInt32(2), ItemId: r.IsDBNull(3) ? null : r.GetInt32(3),
            InspectionType: r.GetByte(4), Status: r.GetByte(5),
            Verdict: r.IsDBNull(6) ? null : r.GetByte(6), Disposition: r.IsDBNull(7) ? null : r.GetByte(7),
            SourceKind: r.IsDBNull(8) ? null : r.GetString(8), SourceId: r.IsDBNull(9) ? null : r.GetInt32(9),
            Quantity: r.IsDBNull(10) ? null : r.GetDecimal(10),
            InspectedByPersonnelId: r.IsDBNull(11) ? null : r.GetInt32(11),
            InspectedAt: r.IsDBNull(12) ? null : r.GetDateTime(12),
            Notes: r.IsDBNull(13) ? null : r.GetString(13),
            Lines: Array.Empty<QualityInspectionLineDto>());
        var lines = new List<QualityInspectionLineDto>();
        await r.NextResultAsync(ct);
        while (await r.ReadAsync(ct))
        {
            lines.Add(new QualityInspectionLineDto(
                Id: r.GetInt32(0),
                PlanLineId: r.IsDBNull(1) ? null : r.GetInt32(1),
                CharacteristicName: r.GetString(2),
                Nominal: r.IsDBNull(3) ? null : r.GetDecimal(3),
                LowerTol: r.IsDBNull(4) ? null : r.GetDecimal(4),
                UpperTol: r.IsDBNull(5) ? null : r.GetDecimal(5),
                Measured: r.IsDBNull(6) ? null : r.GetDecimal(6),
                IsNumeric: r.GetBoolean(7),
                Result: r.GetByte(8),
                DefectCodeId: r.IsDBNull(9) ? null : r.GetInt32(9),
                DefectCodeName: r.IsDBNull(10) ? null : r.GetString(10),
                OrderNo: r.GetInt32(11),
                Notes: r.IsDBNull(12) ? null : r.GetString(12)));
        }
        return head with { Lines = lines };
    }

    public async Task UpsertInspectionAsync(QualityInspection q, IReadOnlyList<QualityInspectionLine> lines, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            int inspectionId;
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                var companyId = _connectionFactory.ResolveEffectiveCompanyId();
                cmd.CommandText = $"""
                    IF EXISTS (SELECT 1 FROM {_inspTable} WHERE [DocumentId]=@Doc AND [CompanyId]=@Company)
                        UPDATE {_inspTable} SET [PlanId]=@Plan,[ItemId]=@Item,[InspectionType]=@Type,[Status]=@Status,
                            [Verdict]=@Verdict,[Disposition]=@Disp,[SourceKind]=@SrcK,[SourceId]=@SrcId,[Quantity]=@Qty,
                            [InspectedByPersonnelId]=@Insp,[InspectedAt]=@InspAt,[Notes]=@Notes,
                            [UpdatedById]=@Upd,[Updated]=SYSUTCDATETIME() WHERE [DocumentId]=@Doc AND [CompanyId]=@Company;
                    ELSE IF NOT EXISTS (SELECT 1 FROM {_inspTable} WHERE [DocumentId]=@Doc)
                        INSERT INTO {_inspTable} ([DocumentId],[PlanId],[ItemId],[InspectionType],[Status],[Verdict],[Disposition],
                            [SourceKind],[SourceId],[Quantity],[InspectedByPersonnelId],[InspectedAt],[Notes],[CreatedById],[Created],[CompanyId])
                        VALUES (@Doc,@Plan,@Item,@Type,@Status,@Verdict,@Disp,@SrcK,@SrcId,@Qty,@Insp,@InspAt,@Notes,@Cre,SYSUTCDATETIME(),@Company);
                    SELECT [Id] FROM {_inspTable} WHERE [DocumentId]=@Doc AND [CompanyId]=@Company;
                    """;
                cmd.Parameters.Add(new SqlParameter("@Doc", q.DocumentId));
                cmd.Parameters.Add(new SqlParameter("@Plan", (object?)q.PlanId ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Item", (object?)q.ItemId ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Type", (byte)q.InspectionType));
                cmd.Parameters.Add(new SqlParameter("@Status", (byte)q.Status));
                cmd.Parameters.Add(new SqlParameter("@Verdict", (object?)(q.Verdict.HasValue ? (byte)q.Verdict.Value : (byte?)null) ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Disp", (object?)(q.Disposition.HasValue ? (byte)q.Disposition.Value : (byte?)null) ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@SrcK", (object?)q.SourceKind ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@SrcId", (object?)q.SourceId ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Qty", (object?)q.Quantity ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Insp", (object?)q.InspectedByPersonnelId ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@InspAt", (object?)q.InspectedAt ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Notes", (object?)q.Notes ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Cre", (object?)q.CreatedById ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Upd", (object?)q.UpdatedById ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Company", companyId));
                inspectionId = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
            }
            // tenant-ok: inspectionId yukarida [CompanyId]=@Company ile dogrulanmis satirdan geldi.
            await using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = $"DELETE FROM {_inspLineTable} WHERE [InspectionId]=@I;";
                del.Parameters.Add(new SqlParameter("@I", inspectionId));
                await del.ExecuteNonQueryAsync(ct);
            }
            foreach (var l in lines)
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                // tenant-ok: InspectionId (@I) yukarida dogrulanan inspectionId — bkz. yukaridaki not.
                cmd.CommandText = $"""
                    INSERT INTO {_inspLineTable} ([InspectionId],[PlanLineId],[CharacteristicName],[Nominal],[LowerTol],[UpperTol],
                        [Measured],[IsNumeric],[Result],[DefectCodeId],[OrderNo],[Notes],[CreatedById],[Created])
                    VALUES (@I,@PL,@Char,@Nom,@Low,@Up,@Meas,@Num,@Res,@Defect,@Order,@Notes,@Cre,SYSUTCDATETIME());
                    """;
                cmd.Parameters.Add(new SqlParameter("@I", inspectionId));
                cmd.Parameters.Add(new SqlParameter("@PL", (object?)l.PlanLineId ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Char", l.CharacteristicName));
                cmd.Parameters.Add(new SqlParameter("@Nom", (object?)l.Nominal ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Low", (object?)l.LowerTol ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Up", (object?)l.UpperTol ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Meas", (object?)l.Measured ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Num", l.IsNumeric));
                cmd.Parameters.Add(new SqlParameter("@Res", (byte)l.Result));
                cmd.Parameters.Add(new SqlParameter("@Defect", (object?)l.DefectCodeId ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Order", l.OrderNo));
                cmd.Parameters.Add(new SqlParameter("@Notes", (object?)l.Notes ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Cre", (object?)l.CreatedById ?? DBNull.Value));
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        catch { await tx.RollbackAsync(ct); throw; }
    }

    public async Task UpdateInspectionStateAsync(int documentId, byte status, byte? verdict, byte? disposition, int? userId, CancellationToken ct)
    {
        var sql = $"""
            UPDATE {_inspTable} SET [Status]=@Status,[Verdict]=@Verdict,[Disposition]=@Disp,
                [UpdatedById]=@Upd,[Updated]=SYSUTCDATETIME() WHERE [DocumentId]=@Doc AND [CompanyId]=@Company;
            """;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@Doc", documentId));
        cmd.Parameters.Add(new SqlParameter("@Status", status));
        cmd.Parameters.Add(new SqlParameter("@Verdict", (object?)verdict ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Disp", (object?)disposition ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Upd", (object?)userId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Company", _connectionFactory.ResolveEffectiveCompanyId()));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyCollection<QualityDefectCodeOption>> GetDefectCodeOptionsAsync(CancellationToken ct)
    {
        var list = new List<QualityDefectCodeOption>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT [Id],[Name],[ColorHex] FROM {_defectTable} WHERE [IsActive]=1 ORDER BY [Category],[SortOrder],[Name];";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new QualityDefectCodeOption(r.GetInt32(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2)));
        return list;
    }

    public async Task<IReadOnlyCollection<MaterialGroupLookupItem>> ListMaterialGroupsAsync(CancellationToken ct)
    {
        // CardGroup (CardType=1) — düz liste hiyerarşi bilgisiyle (Id, ParentId, Level, Code, Description) çekilir,
        // "Üst > Alt" yol etiketi C# tarafında ParentId zinciri yürünerek kurulur (recursive CTE'den kaçınmak için;
        // grup ağacı küçük — tipik onlarca satır).
        var rows = new List<(int Id, int? ParentId, byte Level, string Code, string? Description)>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[ParentId],[Level],[Code],[Description]
            FROM {_cardGroupTable}
            WHERE [CardType]=1
            ORDER BY [Level],[Code];
            """;
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
                rows.Add((r.GetInt32(0), r.IsDBNull(1) ? null : r.GetInt32(1), r.GetByte(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4)));
        }
        var byId = rows.ToDictionary(x => x.Id, x => x);
        string Label((int Id, int? ParentId, byte Level, string Code, string? Description) g)
        {
            var self = string.IsNullOrWhiteSpace(g.Description) ? g.Code : $"{g.Code} — {g.Description}";
            var path = new List<string> { self };
            var parentId = g.ParentId;
            var guard = 0;
            while (parentId.HasValue && byId.TryGetValue(parentId.Value, out var parent) && guard++ < 20)
            {
                path.Insert(0, string.IsNullOrWhiteSpace(parent.Description) ? parent.Code : $"{parent.Code} — {parent.Description}");
                parentId = parent.ParentId;
            }
            return string.Join(" > ", path);
        }
        return rows.OrderBy(g => g.Level).ThenBy(g => g.Code)
            .Select(g => new MaterialGroupLookupItem(g.Id, Label(g)))
            .ToList();
    }
}
