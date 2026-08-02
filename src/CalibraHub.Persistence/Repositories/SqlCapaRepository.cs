using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// DÖF (CAPA) veri erişimi. Document shell service tarafında IDocumentRepository ile yazılır;
/// bu repo companion (Capa) + aksiyon satırlarını yönetir. Stok repo'suna hiç referans yok.
/// FK kasing: Document PK [id] (küçük harf), diğerleri [Id] — SqlQualityRepository deseni.
/// </summary>
public sealed class SqlCapaRepository : ICapaRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _capaTable;
    private readonly string _actionTable;
    private readonly string _docTable;
    private readonly string _defectTable;
    private readonly string _personnelTable;

    public SqlCapaRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options)
    {
        _connectionFactory = factory;
        var s = (string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim()).Replace("]", "]]");
        _capaTable = $"[{s}].[Capa]";
        _actionTable = $"[{s}].[CapaAction]";
        _docTable = $"[{s}].[Document]";
        _defectTable = $"[{s}].[QualityDefectCode]";
        _personnelTable = $"[{s}].[Personnel]";
    }

    private static string Describe(Enum v) => SqlQualityDefectCodeRepository.Describe(v);

    public async Task<IReadOnlyCollection<CapaListItemDto>> ListAsync(string? search, byte? status, CancellationToken ct)
    {
        var where = "d.[IsActive] = 1";
        if (status.HasValue) where += " AND c.[Status] = @Status";
        if (!string.IsNullOrWhiteSpace(search)) where += " AND (d.[DocumentNumber] LIKE @S OR c.[Title] LIKE @S)";
        var sql = $"""
            SELECT c.[DocumentId], d.[DocumentNumber], c.[Title], c.[CapaType], c.[Status], c.[Severity],
                   c.[ResponsiblePersonnelId], p.[FullName] AS ResponsibleName,
                   c.[DueDate], c.[EffectivenessVerified], c.[Created]
            FROM {_capaTable} c
            INNER JOIN {_docTable} d ON d.[id] = c.[DocumentId]
            LEFT JOIN {_personnelTable} p ON p.[Id] = c.[ResponsiblePersonnelId]
            WHERE {where}
            ORDER BY c.[Created] DESC;
            """;
        var list = new List<CapaListItemDto>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        if (status.HasValue) cmd.Parameters.Add(new SqlParameter("@Status", status.Value));
        if (!string.IsNullOrWhiteSpace(search)) cmd.Parameters.Add(new SqlParameter("@S", $"%{search.Trim()}%"));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var type = r.GetByte(3); var st = r.GetByte(4); var sev = r.GetByte(5);
            list.Add(new CapaListItemDto(
                DocumentId: r.GetInt32(0),
                DocumentNumber: r.GetString(1),
                Title: r.GetString(2),
                CapaType: type,
                CapaTypeLabel: Describe((CapaType)type),
                Status: st,
                StatusLabel: Describe((CapaStatus)st),
                Severity: sev,
                SeverityLabel: Describe((CapaSeverity)sev),
                ResponsiblePersonnelId: r.IsDBNull(6) ? null : r.GetInt32(6),
                ResponsibleName: r.IsDBNull(7) ? null : r.GetString(7),
                DueDate: r.IsDBNull(8) ? null : r.GetDateTime(8),
                EffectivenessVerified: r.GetBoolean(9),
                Created: r.GetDateTime(10)));
        }
        return list;
    }

    public async Task<Capa?> GetByDocumentIdAsync(int documentId, CancellationToken ct)
    {
        // d.[IsActive]=1 filtresi ListAsync ile tutarlı — soft-delete edilmiş (silinmiş) DÖF'ün
        // Document.IsActive=0 olmasına rağmen companion satırı silinmeden URL ile açılıp
        // düzenlenebilmesini engeller (2026-07-31 code review fix).
        var sql = $"""
            SELECT c.[Id],c.[DocumentId],c.[CapaType],c.[SourceKind],c.[SourceId],c.[Title],c.[ProblemDescription],
                   c.[DefectCodeId],c.[Severity],c.[RootCauseMethod],c.[RootCause],c.[ResponsiblePersonnelId],c.[DueDate],
                   c.[Status],c.[EffectivenessVerified],c.[VerifiedByPersonnelId],c.[VerifiedAt],c.[EffectivenessNote],c.[ClosedAt],
                   c.[CreatedById],c.[Created],c.[UpdatedById],c.[Updated]
            FROM {_capaTable} c
            INNER JOIN {_docTable} d ON d.[id] = c.[DocumentId]
            WHERE c.[DocumentId]=@Doc AND d.[IsActive]=1;
            """;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@Doc", documentId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new Capa
        {
            Id = r.GetInt32(0),
            DocumentId = r.GetInt32(1),
            CapaType = (CapaType)r.GetByte(2),
            SourceKind = r.IsDBNull(3) ? null : r.GetString(3),
            SourceId = r.IsDBNull(4) ? null : r.GetInt32(4),
            Title = r.GetString(5),
            ProblemDescription = r.IsDBNull(6) ? null : r.GetString(6),
            DefectCodeId = r.IsDBNull(7) ? null : r.GetInt32(7),
            Severity = (CapaSeverity)r.GetByte(8),
            RootCauseMethod = r.IsDBNull(9) ? null : (RootCauseMethod)r.GetByte(9),
            RootCause = r.IsDBNull(10) ? null : r.GetString(10),
            ResponsiblePersonnelId = r.IsDBNull(11) ? null : r.GetInt32(11),
            DueDate = r.IsDBNull(12) ? null : r.GetDateTime(12),
            Status = (CapaStatus)r.GetByte(13),
            EffectivenessVerified = r.GetBoolean(14),
            VerifiedByPersonnelId = r.IsDBNull(15) ? null : r.GetInt32(15),
            VerifiedAt = r.IsDBNull(16) ? null : r.GetDateTime(16),
            EffectivenessNote = r.IsDBNull(17) ? null : r.GetString(17),
            ClosedAt = r.IsDBNull(18) ? null : r.GetDateTime(18),
            CreatedById = r.IsDBNull(19) ? null : r.GetInt32(19),
            Created = r.GetDateTime(20),
            UpdatedById = r.IsDBNull(21) ? null : r.GetInt32(21),
            Updated = r.IsDBNull(22) ? null : r.GetDateTime(22),
        };
    }

    public async Task<CapaDetailDto?> GetDetailAsync(int documentId, CancellationToken ct)
    {
        var sql = $"""
            SELECT c.[DocumentId], d.[DocumentNumber], c.[CapaType], c.[SourceKind], c.[SourceId],
                   c.[Title], c.[ProblemDescription], c.[DefectCodeId], dc.[Name] AS DefectCodeName,
                   c.[Severity], c.[RootCauseMethod], c.[RootCause],
                   c.[ResponsiblePersonnelId], rp.[FullName] AS ResponsibleName, c.[DueDate],
                   c.[Status], c.[EffectivenessVerified], c.[VerifiedByPersonnelId], vp.[FullName] AS VerifiedByName,
                   c.[VerifiedAt], c.[EffectivenessNote], c.[ClosedAt]
            FROM {_capaTable} c
            INNER JOIN {_docTable} d ON d.[id] = c.[DocumentId]
            LEFT JOIN {_defectTable} dc ON dc.[Id] = c.[DefectCodeId]
            LEFT JOIN {_personnelTable} rp ON rp.[Id] = c.[ResponsiblePersonnelId]
            LEFT JOIN {_personnelTable} vp ON vp.[Id] = c.[VerifiedByPersonnelId]
            WHERE c.[DocumentId] = @Doc AND d.[IsActive] = 1;
            SELECT a.[Id], a.[ActionType], a.[Description], a.[ResponsiblePersonnelId], ap.[FullName] AS ResponsibleName,
                   a.[DueDate], a.[Status], a.[CompletedAt], a.[OrderNo]
            FROM {_actionTable} a
            LEFT JOIN {_personnelTable} ap ON ap.[Id] = a.[ResponsiblePersonnelId]
            WHERE a.[CapaId] = (SELECT [Id] FROM {_capaTable} WHERE [DocumentId] = @Doc)
            ORDER BY a.[OrderNo], a.[Id];
            """;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@Doc", documentId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        var capaType = r.GetByte(2);
        var severity = r.GetByte(9);
        byte? rootCauseMethod = r.IsDBNull(10) ? null : r.GetByte(10);
        var status = r.GetByte(15);
        var head = new CapaDetailDto(
            DocumentId: r.GetInt32(0), DocumentNumber: r.GetString(1),
            CapaType: capaType,
            SourceKind: r.IsDBNull(3) ? null : r.GetString(3),
            SourceId: r.IsDBNull(4) ? null : r.GetInt32(4),
            Title: r.GetString(5),
            ProblemDescription: r.IsDBNull(6) ? null : r.GetString(6),
            DefectCodeId: r.IsDBNull(7) ? null : r.GetInt32(7),
            DefectCodeName: r.IsDBNull(8) ? null : r.GetString(8),
            Severity: severity,
            RootCauseMethod: rootCauseMethod,
            RootCause: r.IsDBNull(11) ? null : r.GetString(11),
            ResponsiblePersonnelId: r.IsDBNull(12) ? null : r.GetInt32(12),
            ResponsibleName: r.IsDBNull(13) ? null : r.GetString(13),
            DueDate: r.IsDBNull(14) ? null : r.GetDateTime(14),
            Status: status,
            EffectivenessVerified: r.GetBoolean(16),
            VerifiedByPersonnelId: r.IsDBNull(17) ? null : r.GetInt32(17),
            VerifiedByName: r.IsDBNull(18) ? null : r.GetString(18),
            VerifiedAt: r.IsDBNull(19) ? null : r.GetDateTime(19),
            EffectivenessNote: r.IsDBNull(20) ? null : r.GetString(20),
            ClosedAt: r.IsDBNull(21) ? null : r.GetDateTime(21),
            Actions: Array.Empty<CapaActionDto>());
        var actions = new List<CapaActionDto>();
        await r.NextResultAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var actType = r.GetByte(1); var actStatus = r.GetByte(6);
            actions.Add(new CapaActionDto(
                Id: r.GetInt32(0),
                ActionType: actType,
                ActionTypeLabel: Describe((CapaActionType)actType),
                Description: r.GetString(2),
                ResponsiblePersonnelId: r.IsDBNull(3) ? null : r.GetInt32(3),
                ResponsibleName: r.IsDBNull(4) ? null : r.GetString(4),
                DueDate: r.IsDBNull(5) ? null : r.GetDateTime(5),
                Status: actStatus,
                StatusLabel: Describe((CapaActionStatus)actStatus),
                CompletedAt: r.IsDBNull(7) ? null : r.GetDateTime(7),
                OrderNo: r.GetInt32(8)));
        }
        return head with { Actions = actions };
    }

    public async Task UpsertAsync(Capa c, IReadOnlyList<CapaAction> actions, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            int capaId;
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = $"""
                    IF EXISTS (SELECT 1 FROM {_capaTable} WHERE [DocumentId]=@Doc)
                        UPDATE {_capaTable} SET [CapaType]=@Type,[SourceKind]=@SrcK,[SourceId]=@SrcId,
                            [Title]=@Title,[ProblemDescription]=@Problem,[DefectCodeId]=@Defect,[Severity]=@Sev,
                            [RootCauseMethod]=@RcMethod,[RootCause]=@RootCause,[ResponsiblePersonnelId]=@Resp,
                            [DueDate]=@Due,[Status]=@Status,[EffectivenessVerified]=@EffVer,
                            [VerifiedByPersonnelId]=@VerBy,[VerifiedAt]=@VerAt,[EffectivenessNote]=@EffNote,
                            [ClosedAt]=@Closed,[UpdatedById]=@Upd,[Updated]=SYSUTCDATETIME() WHERE [DocumentId]=@Doc;
                    ELSE
                        INSERT INTO {_capaTable} ([DocumentId],[CapaType],[SourceKind],[SourceId],[Title],[ProblemDescription],
                            [DefectCodeId],[Severity],[RootCauseMethod],[RootCause],[ResponsiblePersonnelId],[DueDate],[Status],
                            [EffectivenessVerified],[VerifiedByPersonnelId],[VerifiedAt],[EffectivenessNote],[ClosedAt],[CreatedById],[Created])
                        VALUES (@Doc,@Type,@SrcK,@SrcId,@Title,@Problem,@Defect,@Sev,@RcMethod,@RootCause,@Resp,@Due,@Status,
                            @EffVer,@VerBy,@VerAt,@EffNote,@Closed,@Cre,SYSUTCDATETIME());
                    SELECT [Id] FROM {_capaTable} WHERE [DocumentId]=@Doc;
                    """;
                cmd.Parameters.Add(new SqlParameter("@Doc", c.DocumentId));
                cmd.Parameters.Add(new SqlParameter("@Type", (byte)c.CapaType));
                cmd.Parameters.Add(new SqlParameter("@SrcK", (object?)c.SourceKind ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@SrcId", (object?)c.SourceId ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Title", c.Title));
                cmd.Parameters.Add(new SqlParameter("@Problem", (object?)c.ProblemDescription ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Defect", (object?)c.DefectCodeId ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Sev", (byte)c.Severity));
                cmd.Parameters.Add(new SqlParameter("@RcMethod", (object?)(c.RootCauseMethod.HasValue ? (byte)c.RootCauseMethod.Value : (byte?)null) ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@RootCause", (object?)c.RootCause ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Resp", (object?)c.ResponsiblePersonnelId ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Due", (object?)c.DueDate ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Status", (byte)c.Status));
                cmd.Parameters.Add(new SqlParameter("@EffVer", c.EffectivenessVerified));
                cmd.Parameters.Add(new SqlParameter("@VerBy", (object?)c.VerifiedByPersonnelId ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@VerAt", (object?)c.VerifiedAt ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@EffNote", (object?)c.EffectivenessNote ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Closed", (object?)c.ClosedAt ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Cre", (object?)c.CreatedById ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Upd", (object?)c.UpdatedById ?? DBNull.Value));
                capaId = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
            }
            await using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = $"DELETE FROM {_actionTable} WHERE [CapaId]=@C;";
                del.Parameters.Add(new SqlParameter("@C", capaId));
                await del.ExecuteNonQueryAsync(ct);
            }
            foreach (var a in actions)
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = $"""
                    INSERT INTO {_actionTable} ([CapaId],[ActionType],[Description],[ResponsiblePersonnelId],[DueDate],
                        [Status],[CompletedAt],[OrderNo],[CreatedById],[Created])
                    VALUES (@C,@Type,@Desc,@Resp,@Due,@Status,@Completed,@Order,@Cre,SYSUTCDATETIME());
                    """;
                cmd.Parameters.Add(new SqlParameter("@C", capaId));
                cmd.Parameters.Add(new SqlParameter("@Type", (byte)a.ActionType));
                cmd.Parameters.Add(new SqlParameter("@Desc", a.Description));
                cmd.Parameters.Add(new SqlParameter("@Resp", (object?)a.ResponsiblePersonnelId ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Due", (object?)a.DueDate ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Status", (byte)a.Status));
                cmd.Parameters.Add(new SqlParameter("@Completed", (object?)a.CompletedAt ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Order", a.OrderNo));
                cmd.Parameters.Add(new SqlParameter("@Cre", (object?)a.CreatedById ?? DBNull.Value));
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        catch { await tx.RollbackAsync(ct); throw; }
    }

    public async Task UpdateStatusAsync(int documentId, byte status, DateTime? closedAt, int? userId, CancellationToken ct)
    {
        var sql = $"""
            UPDATE {_capaTable} SET [Status]=@Status,[ClosedAt]=@Closed,
                [UpdatedById]=@Upd,[Updated]=SYSUTCDATETIME() WHERE [DocumentId]=@Doc;
            """;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@Doc", documentId));
        cmd.Parameters.Add(new SqlParameter("@Status", status));
        cmd.Parameters.Add(new SqlParameter("@Closed", (object?)closedAt ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Upd", (object?)userId ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyCollection<CapaPersonnelOption>> GetPersonnelOptionsAsync(CancellationToken ct)
    {
        var list = new List<CapaPersonnelOption>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT [Id],[FullName] FROM {_personnelTable} WHERE [IsActive] = 1 ORDER BY [FullName];";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new CapaPersonnelOption(r.GetInt32(0), r.GetString(1)));
        return list;
    }

    /// <summary>
    /// KPI panosu agregasyonu — tek bağlantı, çok result-set'li tek SQL batch (7 sorgu). Soft-delete
    /// filtresi d.[IsActive]=1 diğer metodlarla (ListAsync vb.) tutarlı. "Bu ay"/"bugün" tanımı sunucu
    /// yerel saatiyle (DateTime.Now) hesaplanır — QualityController.BuildCapasBoardAsync'teki overdue
    /// hesabıyla aynı taban (UTC değil).
    /// </summary>
    public async Task<CapaKpiDto> GetKpiAsync(CancellationToken ct)
    {
        var now = DateTime.Now;
        var today = now.Date;
        var monthStart = new DateTime(now.Year, now.Month, 1);
        var trendStart = monthStart.AddMonths(-11); // son 12 takvim ayı (bu ay dahil)

        var sql = $"""
            SELECT
                COUNT(*) AS TotalCount,
                SUM(CASE WHEN c.[Status] NOT IN (4,5) THEN 1 ELSE 0 END) AS OpenCount,
                SUM(CASE WHEN c.[DueDate] IS NOT NULL AND c.[DueDate] < @Today AND c.[Status] NOT IN (4,5) THEN 1 ELSE 0 END) AS OverdueCount,
                SUM(CASE WHEN c.[Status] = 4 THEN 1 ELSE 0 END) AS ClosedCount,
                SUM(CASE WHEN c.[Created] >= @MonthStart THEN 1 ELSE 0 END) AS OpenedThisMonth,
                SUM(CASE WHEN c.[Status] = 4 AND c.[ClosedAt] >= @MonthStart THEN 1 ELSE 0 END) AS ClosedThisMonth
            FROM {_capaTable} c
            INNER JOIN {_docTable} d ON d.[id] = c.[DocumentId]
            WHERE d.[IsActive] = 1;

            SELECT AVG(CAST(DATEDIFF(day, c.[Created], c.[ClosedAt]) AS FLOAT)) AS AvgClosureDays
            FROM {_capaTable} c
            INNER JOIN {_docTable} d ON d.[id] = c.[DocumentId]
            WHERE d.[IsActive] = 1 AND c.[Status] = 4 AND c.[ClosedAt] IS NOT NULL;

            SELECT c.[Status] AS Bucket, COUNT(*) AS Cnt
            FROM {_capaTable} c INNER JOIN {_docTable} d ON d.[id] = c.[DocumentId]
            WHERE d.[IsActive] = 1 GROUP BY c.[Status];

            SELECT c.[CapaType] AS Bucket, COUNT(*) AS Cnt
            FROM {_capaTable} c INNER JOIN {_docTable} d ON d.[id] = c.[DocumentId]
            WHERE d.[IsActive] = 1 GROUP BY c.[CapaType];

            SELECT c.[Severity] AS Bucket, COUNT(*) AS Cnt
            FROM {_capaTable} c INNER JOIN {_docTable} d ON d.[id] = c.[DocumentId]
            WHERE d.[IsActive] = 1 GROUP BY c.[Severity];

            SELECT YEAR(c.[Created]) AS Yr, MONTH(c.[Created]) AS Mo, COUNT(*) AS Cnt
            FROM {_capaTable} c INNER JOIN {_docTable} d ON d.[id] = c.[DocumentId]
            WHERE d.[IsActive] = 1 AND c.[Created] >= @TrendStart
            GROUP BY YEAR(c.[Created]), MONTH(c.[Created]);

            SELECT YEAR(c.[ClosedAt]) AS Yr, MONTH(c.[ClosedAt]) AS Mo, COUNT(*) AS Cnt
            FROM {_capaTable} c INNER JOIN {_docTable} d ON d.[id] = c.[DocumentId]
            WHERE d.[IsActive] = 1 AND c.[Status] = 4 AND c.[ClosedAt] >= @TrendStart
            GROUP BY YEAR(c.[ClosedAt]), MONTH(c.[ClosedAt]);

            SELECT TOP 8 c.[ResponsiblePersonnelId] AS PersonnelId, p.[FullName] AS Name, COUNT(*) AS Cnt
            FROM {_capaTable} c
            INNER JOIN {_docTable} d ON d.[id] = c.[DocumentId]
            INNER JOIN {_personnelTable} p ON p.[Id] = c.[ResponsiblePersonnelId]
            WHERE d.[IsActive] = 1 AND c.[ResponsiblePersonnelId] IS NOT NULL AND c.[Status] NOT IN (4,5)
            GROUP BY c.[ResponsiblePersonnelId], p.[FullName]
            ORDER BY COUNT(*) DESC;
            """;

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@Today", today));
        cmd.Parameters.Add(new SqlParameter("@MonthStart", monthStart));
        cmd.Parameters.Add(new SqlParameter("@TrendStart", trendStart));

        await using var r = await cmd.ExecuteReaderAsync(ct);

        int totalCount = 0, openCount = 0, overdueCount = 0, closedCount = 0, openedThisMonth = 0, closedThisMonth = 0;
        if (await r.ReadAsync(ct))
        {
            totalCount = r.GetInt32(0);
            openCount = r.IsDBNull(1) ? 0 : r.GetInt32(1);
            overdueCount = r.IsDBNull(2) ? 0 : r.GetInt32(2);
            closedCount = r.IsDBNull(3) ? 0 : r.GetInt32(3);
            openedThisMonth = r.IsDBNull(4) ? 0 : r.GetInt32(4);
            closedThisMonth = r.IsDBNull(5) ? 0 : r.GetInt32(5);
        }

        await r.NextResultAsync(ct);
        double? avgClosureDays = null;
        if (await r.ReadAsync(ct) && !r.IsDBNull(0)) avgClosureDays = r.GetDouble(0);

        await r.NextResultAsync(ct);
        var statusCounts = new Dictionary<byte, int>();
        while (await r.ReadAsync(ct)) statusCounts[r.GetByte(0)] = r.GetInt32(1);

        await r.NextResultAsync(ct);
        var typeCounts = new Dictionary<byte, int>();
        while (await r.ReadAsync(ct)) typeCounts[r.GetByte(0)] = r.GetInt32(1);

        await r.NextResultAsync(ct);
        var severityCounts = new Dictionary<byte, int>();
        while (await r.ReadAsync(ct)) severityCounts[r.GetByte(0)] = r.GetInt32(1);

        await r.NextResultAsync(ct);
        var openedByMonth = new Dictionary<(int Year, int Month), int>();
        while (await r.ReadAsync(ct)) openedByMonth[(r.GetInt32(0), r.GetInt32(1))] = r.GetInt32(2);

        await r.NextResultAsync(ct);
        var closedByMonth = new Dictionary<(int Year, int Month), int>();
        while (await r.ReadAsync(ct)) closedByMonth[(r.GetInt32(0), r.GetInt32(1))] = r.GetInt32(2);

        await r.NextResultAsync(ct);
        var topResponsible = new List<CapaKpiResponsible>();
        while (await r.ReadAsync(ct))
            topResponsible.Add(new CapaKpiResponsible(r.GetInt32(0), r.GetString(1), r.GetInt32(2)));

        // Enum bucket'ları TÜM tanımlı değerleri içerir — SQL sadece dolu grupları döndürür,
        // eksik değerler burada 0 olarak eklenir (dağılım grafiği tam olsun, CLAUDE.md ID/enum kuralı).
        var byStatus = Enum.GetValues<CapaStatus>()
            .Select(v => new CapaKpiBucket((byte)v, Describe(v), statusCounts.TryGetValue((byte)v, out var c) ? c : 0))
            .ToList();
        var byType = Enum.GetValues<CapaType>()
            .Select(v => new CapaKpiBucket((byte)v, Describe(v), typeCounts.TryGetValue((byte)v, out var c) ? c : 0))
            .ToList();
        var bySeverity = Enum.GetValues<CapaSeverity>()
            .Select(v => new CapaKpiBucket((byte)v, Describe(v), severityCounts.TryGetValue((byte)v, out var c) ? c : 0))
            .ToList();

        var monthlyTrend = new List<CapaKpiTrendPoint>(12);
        for (var i = 0; i < 12; i++)
        {
            var m = trendStart.AddMonths(i);
            var key = (m.Year, m.Month);
            var opened = openedByMonth.TryGetValue(key, out var o) ? o : 0;
            var closed = closedByMonth.TryGetValue(key, out var cl) ? cl : 0;
            monthlyTrend.Add(new CapaKpiTrendPoint(m.ToString("yyyy-MM"), opened, closed));
        }

        return new CapaKpiDto(
            TotalCount: totalCount, OpenCount: openCount, OverdueCount: overdueCount, ClosedCount: closedCount,
            OpenedThisMonth: openedThisMonth, ClosedThisMonth: closedThisMonth,
            AvgClosureDays: avgClosureDays,
            ByStatus: byStatus, ByType: byType, BySeverity: bySeverity,
            MonthlyTrend: monthlyTrend, TopResponsible: topResponsible);
    }
}
