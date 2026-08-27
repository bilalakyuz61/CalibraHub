using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlDocumentRepository : IDocumentRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IDataVisibilityFilter _dvFilter;
    private readonly ILogger<SqlDocumentRepository>? _logger;
    // FAZ 1b-ii (2026-07-20) — DocumentLineLink dual-write, best-effort. Bkz. FulfillmentLedger
    // sınıf başlığındaki "C: karşılama" notu; nullable — _docSourceRepo/_logger ile aynı
    // "opsiyonel best-effort bağımlılık" üslubu (WorkOrderService deseniyle birebir).
    private readonly IDocumentLineLinkRepository? _lineLinks;
    private readonly string _quoteTable;
    private readonly string _lineTable;
    private readonly string _fulfillmentTable;
    private readonly string _detailTable;
    private readonly string _schema;

    public SqlDocumentRepository(
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions options,
        IHttpContextAccessor httpContextAccessor,
        IDataVisibilityFilter dvFilter,
        ILogger<SqlDocumentRepository>? logger = null,
        IDocumentLineLinkRepository? lineLinks = null)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
        _dvFilter = dvFilter;
        _lineLinks = lineLinks;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _schema = schema;
        _quoteTable = $"[{schema}].[Document]";
        _lineTable = $"[{schema}].[DocumentLine]";
        _fulfillmentTable = $"[{schema}].[DocumentLineFulfillment]";
        _detailTable = $"[{schema}].[SalesQuoteLineDetail]";
    }

    /* ── Kit snapshot (Faz 2) ─────────────────────────────────────── */

    public async Task<IReadOnlyCollection<KitSnapshotSourceDto>> GetActiveKitContentsAsync(
        IEnumerable<int> kitItemIds, CancellationToken ct)
    {
        var ids = (kitItemIds ?? Array.Empty<int>()).Where(i => i > 0).Distinct().ToArray();
        if (ids.Length == 0) return Array.Empty<KitSnapshotSourceDto>();

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        var paramNames = ids.Select((_, i) => "@k" + i).ToArray();
        // Aktif kit = ayni ItemId icin MAX(Id) WHERE IsActive=1; satirlariyla JOIN.
        // PriceMode (Seq 1073 Part B, 4 moda genisletildi Seq 1078) — server-truth fiyatlama
        // icin DocumentService bu alani kullanir. l.UnitPrice — FixedComponent modda bilesenin
        // ELLE girilen birim fiyati (fiyat listesi degil); diger modlarda NULL.
        cmd.CommandText = $"""
            SELECT k.[ItemId], k.[VersionNo], k.[PriceMode], l.[ItemId] AS ComponentItemId, l.[ConfigId], l.[Quantity], l.[UnitPrice]
            FROM [{_schema}].[ItemKit] k
            INNER JOIN [{_schema}].[ItemKitLine] l ON l.[ItemKitId] = k.[Id]
            WHERE k.[IsActive] = 1
              AND k.[ItemId] IN ({string.Join(",", paramNames)})
              AND k.[Id] = (SELECT MAX([Id]) FROM [{_schema}].[ItemKit] WHERE [ItemId] = k.[ItemId] AND [IsActive] = 1)
            ORDER BY k.[ItemId], l.[Id];
            """;
        for (var i = 0; i < ids.Length; i++)
            cmd.Parameters.Add(new SqlParameter(paramNames[i], ids[i]));

        var byKit = new Dictionary<int, (int Version, string PriceMode, List<KitSnapshotComponentDto> Comps)>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var kitItemId = reader.GetInt32(0);
            var version = reader.GetInt32(1);
            var priceMode = reader.IsDBNull(2) ? KitPriceMode.FixedPackage : reader.GetString(2);
            if (!byKit.TryGetValue(kitItemId, out var entry))
            {
                entry = (version, priceMode, new List<KitSnapshotComponentDto>());
                byKit[kitItemId] = entry;
            }
            entry.Comps.Add(new KitSnapshotComponentDto(
                reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.GetDecimal(5),
                reader.IsDBNull(6) ? null : reader.GetDecimal(6)));
        }
        return byKit.Select(kv => new KitSnapshotSourceDto(kv.Key, kv.Value.Version, kv.Value.PriceMode, kv.Value.Comps)).ToList();
    }

    public async Task<IReadOnlyCollection<(int LineId, int KitItemId)>> GetExistingKitSnapshotsAsync(
        IEnumerable<int> documentLineIds, CancellationToken ct)
    {
        var ids = (documentLineIds ?? Array.Empty<int>()).Where(i => i > 0).Distinct().ToArray();
        if (ids.Length == 0) return Array.Empty<(int, int)>();

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        var paramNames = ids.Select((_, i) => "@l" + i).ToArray();
        cmd.CommandText = $"""
            SELECT DISTINCT [DocumentLineId], [KitItemId] FROM [{_schema}].[DocumentLineKitComponent]
            WHERE [DocumentLineId] IN ({string.Join(",", paramNames)});
            """;
        for (var i = 0; i < ids.Length; i++)
            cmd.Parameters.Add(new SqlParameter(paramNames[i], ids[i]));

        var result = new List<(int, int)>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add((reader.GetInt32(0), reader.GetInt32(1)));
        return result;
    }

    public async Task ReplaceKitSnapshotAsync(int documentLineId, int kitItemId, int versionNo,
        IReadOnlyCollection<KitSnapshotComponentDto> components, CancellationToken ct)
    {
        if (documentLineId <= 0) return;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = conn.BeginTransaction();

        await using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = $"DELETE FROM [{_schema}].[DocumentLineKitComponent] WHERE [DocumentLineId] = @L;";
            del.Parameters.Add(new SqlParameter("@L", documentLineId));
            await del.ExecuteNonQueryAsync(ct);
        }

        foreach (var c in components ?? Array.Empty<KitSnapshotComponentDto>())
        {
            await using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = $"""
                INSERT INTO [{_schema}].[DocumentLineKitComponent]
                    ([DocumentLineId],[KitItemId],[KitVersionNo],[ComponentItemId],[ConfigId],[Quantity],[UnitPrice],[Created])
                VALUES (@L,@K,@V,@C,@Cfg,@Q,@UP,SYSUTCDATETIME());
                """;
            ins.Parameters.Add(new SqlParameter("@L", documentLineId));
            ins.Parameters.Add(new SqlParameter("@K", kitItemId));
            ins.Parameters.Add(new SqlParameter("@V", versionNo));
            ins.Parameters.Add(new SqlParameter("@C", c.ComponentItemId));
            ins.Parameters.Add(new SqlParameter("@Cfg", (object?)c.ConfigId ?? DBNull.Value));
            ins.Parameters.Add(new SqlParameter("@Q", c.Quantity));
            // FixedComponent modda donduruldugu anki elle bilesen fiyati (bkz. KitSnapshotComponentDto);
            // diger modlarda NULL (fiyat her save'de canli fiyat listesinden yeniden cozulur).
            ins.Parameters.Add(new SqlParameter("@UP", (object?)c.UnitPrice ?? DBNull.Value));
            await ins.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyCollection<DocumentLineKitComponent>> GetKitSnapshotAsync(int documentLineId, CancellationToken ct)
    {
        if (documentLineId <= 0) return Array.Empty<DocumentLineKitComponent>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT s.[Id], s.[DocumentLineId], s.[KitItemId], s.[KitVersionNo], s.[ComponentItemId],
                   s.[ConfigId], s.[Quantity], s.[UnitPrice], s.[Created],
                   ci.[Code] AS CompCode, ci.[Name] AS CompName, cfg.[RecordCode] AS ConfigCode,
                   u.[Code] AS CompUnit
            FROM [{_schema}].[DocumentLineKitComponent] s
            LEFT JOIN [{_schema}].[Items] ci ON ci.[Id] = s.[ComponentItemId]
            LEFT JOIN [{_schema}].[ItemConfiguration] cfg ON cfg.[Id] = s.[ConfigId]
            -- 2026-08-22: birim once malzemenin VARSAYILAN birimi (Items.UnitId),
            -- o bos ise malzemenin birim listesindeki ILK satir (ItemUnits, LineNo
            -- sirasi = taban birim). Kit recetesi birim tasimaz (ItemKitLine'da
            -- kolon yok), tek kaynak malzemenin kendi birimidir.
            LEFT JOIN [{_schema}].[Unit] u ON u.[Id] = COALESCE(
                ci.[UnitId],
                (SELECT TOP 1 iu.[UnitId] FROM [{_schema}].[ItemUnits] iu
                  WHERE iu.[ItemId] = ci.[Id] ORDER BY iu.[LineNo]))
            WHERE s.[DocumentLineId] = @L
            ORDER BY s.[Id];
            """;
        cmd.Parameters.Add(new SqlParameter("@L", documentLineId));

        var list = new List<DocumentLineKitComponent>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new DocumentLineKitComponent
            {
                Id              = reader.GetInt32(0),
                DocumentLineId  = reader.GetInt32(1),
                KitItemId       = reader.GetInt32(2),
                KitVersionNo    = reader.GetInt32(3),
                ComponentItemId = reader.GetInt32(4),
                ConfigId        = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                Quantity        = reader.GetDecimal(6),
                UnitPrice       = reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                Created         = reader.GetDateTime(8),
                ComponentCode   = reader.IsDBNull(9) ? null : reader.GetString(9),
                ComponentName   = reader.IsDBNull(10) ? null : reader.GetString(10),
                ConfigCode      = reader.IsDBNull(11) ? null : reader.GetString(11),
                UnitCode        = reader.IsDBNull(12) ? null : reader.GetString(12),
            });
        }
        return list;
    }

    /// <summary>
    /// HTTP context'ten mevcut kullanicinin sirket kimligini ceker.
    /// Claim yoksa 0 doner; caller fallback olarak DB default'una (1) guvenir.
    /// </summary>
    private int GetCurrentCompanyId()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext?.User.Identity?.IsAuthenticated == true)
        {
            var raw = httpContext.User.FindFirst("company_id")?.Value;
            if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out var id))
                return id;
        }
        return 0;
    }

    public async Task<IReadOnlyCollection<Document>> GetAllAsync(string? search, string? status, CancellationToken ct)
    {
        var list = new List<Document>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();

        // Kiraci suzgeci: where dizesi asagida Replace("[", "q.[") ile alias aliyor,
        // bu yuzden kosul KOSESIZ yazilir.
        var where = "WHERE [IsActive] = 1 AND [CompanyId] = @CompanyId";
        cmd.Parameters.Add(new SqlParameter("@CompanyId", _connectionFactory.ResolveEffectiveCompanyId()));
        if (!string.IsNullOrWhiteSpace(status))
        {
            where += " AND [Status] = @Status";
            cmd.Parameters.Add(new SqlParameter("@Status", status.Trim()));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            where += " AND ([DocumentNumber] LIKE @Search)";
            cmd.Parameters.Add(new SqlParameter("@Search", $"%{search.Trim()}%"));
        }

        // Satır görünürlük kuralları (row-level security). dv.Sql alias 'q' ile üretilir ve
        // where.Replace("[", "q.[") DÖNÜŞÜMÜNDEN SONRA eklenir (aksi halde q.q.[ bozulur).
        var dv = await _dvFilter.BuildAsync(FormCodes.SalesQuote, "q", "id", ct);

        // Cari ismi Contact.AccountTitle'dan cekilir — contact_name kolonu Faz 2'de drop edildi.
        cmd.CommandText = $"""
            SELECT q.[Id],q.[CompanyId],q.[DocumentNumber],q.[DocumentDate],q.[ValidUntil],q.[DeliveryDate],q.[DeliveryDays],q.[ContactId],
                   ca.[AccountTitle] AS [contact_name],
                   q.[ContactAddress],
                   q.[SalesRepId],q.[RequesterPersonnelId],p.[FullName] AS RequesterPersonnelName,
                   q.[LocationId],hloc.[LocationName] AS HeaderLocationName,
                   q.[CurrencyId],cur.[Code] AS CurrencyCode,cur.[Symbol] AS CurrencySymbol,q.[SubTotal],q.[DiscountRate],q.[DiscountAmount],q.[TaxRate],q.[TaxAmount],q.[GrandTotal],
                   q.[ExchangeRate],q.[IsVatIncluded],q.[RateDate],q.[SourceDocumentNo],
                   q.[PaymentTerms],q.[DeliveryTerms],q.[DeliveryAddress],q.[Status],q.[RevisionNo],q.[ParentDocumentId],
                   q.[Notes],q.[CreatedById],q.[Created],q.[Updated],q.[IsActive],q.[DocumentTypeId],
                   (SELECT COUNT(*) FROM {_lineTable} l WHERE l.[DocumentId] = q.[Id]) AS [line_count]
            FROM {_quoteTable} q
            LEFT JOIN [{_schema}].[Contact] ca ON ca.[Id] = q.[ContactId]
            LEFT JOIN [{_schema}].[Currency] cur ON cur.[Id] = q.[CurrencyId]
            LEFT JOIN [{_schema}].[Personnel] p ON p.[Id] = q.[RequesterPersonnelId]
            LEFT JOIN [{_schema}].[Location] hloc ON hloc.[Id] = q.[LocationId]
            {where.Replace("[", "q.[")}
            {dv.Sql}
            ORDER BY q.[Created] DESC;
            """;
        foreach (var prm in dv.Parameters) cmd.Parameters.Add(new SqlParameter(prm.Name, prm.Value));

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(MapQuote(r));
        return list;
    }

    public async Task<IReadOnlyCollection<Document>> GetByTypeAsync(string typeCode, string? search, string? status, CancellationToken ct)
    {
        var list = new List<Document>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();

        var where = "WHERE q.[IsActive] = 1 AND q.[CompanyId] = @CompanyId AND dt.[Code] = @TypeCode";
        cmd.Parameters.Add(new SqlParameter("@CompanyId", _connectionFactory.ResolveEffectiveCompanyId()));
        cmd.Parameters.Add(new SqlParameter("@TypeCode", typeCode));
        if (!string.IsNullOrWhiteSpace(status))
        {
            where += " AND q.[Status] = @Status";
            cmd.Parameters.Add(new SqlParameter("@Status", status.Trim()));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            where += " AND q.[DocumentNumber] LIKE @Search";
            cmd.Parameters.Add(new SqlParameter("@Search", $"%{search.Trim()}%"));
        }
        // Satır görünürlük kuralları (row-level security) — alias 'q', where zaten q. prefix'li.
        var dv = await _dvFilter.BuildAsync(FormCodes.SalesQuote, "q", "id", ct);

        cmd.CommandText = $"""
            SELECT q.[Id],q.[CompanyId],q.[DocumentNumber],q.[DocumentDate],q.[ValidUntil],q.[DeliveryDate],q.[DeliveryDays],q.[ContactId],
                   ca.[AccountTitle] AS [contact_name],
                   q.[ContactAddress],
                   q.[SalesRepId],q.[RequesterPersonnelId],p.[FullName] AS RequesterPersonnelName,
                   q.[LocationId],hloc.[LocationName] AS HeaderLocationName,
                   q.[CurrencyId],cur.[Code] AS CurrencyCode,cur.[Symbol] AS CurrencySymbol,q.[SubTotal],q.[DiscountRate],q.[DiscountAmount],q.[TaxRate],q.[TaxAmount],q.[GrandTotal],
                   q.[ExchangeRate],q.[IsVatIncluded],q.[RateDate],q.[SourceDocumentNo],
                   q.[PaymentTerms],q.[DeliveryTerms],q.[DeliveryAddress],q.[Status],q.[RevisionNo],q.[ParentDocumentId],
                   q.[Notes],q.[CreatedById],q.[Created],q.[Updated],q.[IsActive],q.[DocumentTypeId],
                   (SELECT COUNT(*) FROM {_lineTable} l WHERE l.[DocumentId] = q.[Id]) AS [line_count],
                   (SELECT COUNT(*) FROM {_lineTable} l WHERE l.[DocumentId] = q.[Id] AND ISNULL(l.[FulfillmentStatus],0) = 0) AS [fulfill_pending],
                   (SELECT COUNT(*) FROM {_lineTable} l WHERE l.[DocumentId] = q.[Id] AND ISNULL(l.[FulfillmentStatus],0) = 1) AS [fulfill_partial],
                   (SELECT COUNT(*) FROM {_lineTable} l WHERE l.[DocumentId] = q.[Id] AND ISNULL(l.[FulfillmentStatus],0) = 2) AS [fulfill_full]
            FROM {_quoteTable} q
            LEFT JOIN [{_schema}].[Contact] ca ON ca.[Id] = q.[ContactId]
            LEFT JOIN [{_schema}].[Currency] cur ON cur.[Id] = q.[CurrencyId]
            LEFT JOIN [{_schema}].[Personnel] p ON p.[Id] = q.[RequesterPersonnelId]
            LEFT JOIN [{_schema}].[Location] hloc ON hloc.[Id] = q.[LocationId]
            INNER JOIN [{_schema}].[DocumentType] dt ON dt.[Id] = q.[DocumentTypeId]
            {where}
            {dv.Sql}
            ORDER BY q.[Created] DESC;
            """;
        foreach (var prm in dv.Parameters) cmd.Parameters.Add(new SqlParameter(prm.Name, prm.Value));

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(MapQuote(r));
        return list;
    }

    public async Task<IReadOnlyCollection<Document>> GetConvertibleQuotesAsync(
        DateTime? fromDate, DateTime? toDate, int? contactId, string? search, CancellationToken ct)
    {
        var list = new List<Document>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();

        var where = "WHERE q.[IsActive] = 1 AND q.[CompanyId] = @CompanyId AND q.[Status] = N'Approved' AND dt.[Code] = N'satis_teklifi'";
        cmd.Parameters.Add(new SqlParameter("@CompanyId", _connectionFactory.ResolveEffectiveCompanyId()));
        // NOT EXISTS koprusu — daha onceden siparise donusturulen teklifler hariç.
        // document_source tablosu IDocumentSourceRepository.EnsureSchemaAsync ile garantilenir;
        // burada OBJECT_ID guard ile tabloya bagimliligi gevsetmek zorunda kalmadan,
        // sadece NOT EXISTS sub-query'i tablo olusturulduktan sonra cagiriyoruz (DocumentService garanti eder).
        where += $" AND NOT EXISTS (SELECT 1 FROM [{_schema}].[DocumentSource] ds WHERE ds.[SourceDocumentId] = q.[Id])";

        if (fromDate.HasValue)
        {
            where += " AND q.[DocumentDate] >= @FromDate";
            cmd.Parameters.Add(new SqlParameter("@FromDate", fromDate.Value.Date));
        }
        if (toDate.HasValue)
        {
            where += " AND q.[DocumentDate] < @ToDateExclusive";
            cmd.Parameters.Add(new SqlParameter("@ToDateExclusive", toDate.Value.Date.AddDays(1)));
        }
        if (contactId.HasValue && contactId.Value > 0)
        {
            where += " AND q.[ContactId] = @ContactId";
            cmd.Parameters.Add(new SqlParameter("@ContactId", contactId.Value));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            where += " AND q.[DocumentNumber] LIKE @Search";
            cmd.Parameters.Add(new SqlParameter("@Search", $"%{search.Trim()}%"));
        }

        cmd.CommandText = $"""
            SELECT q.[Id],q.[CompanyId],q.[DocumentNumber],q.[DocumentDate],q.[ValidUntil],q.[DeliveryDate],q.[DeliveryDays],q.[ContactId],
                   ca.[AccountTitle] AS [contact_name],
                   q.[ContactAddress],
                   q.[SalesRepId],q.[RequesterPersonnelId],p.[FullName] AS RequesterPersonnelName,
                   q.[LocationId],hloc.[LocationName] AS HeaderLocationName,
                   q.[CurrencyId],cur.[Code] AS CurrencyCode,cur.[Symbol] AS CurrencySymbol,q.[SubTotal],q.[DiscountRate],q.[DiscountAmount],q.[TaxRate],q.[TaxAmount],q.[GrandTotal],
                   q.[ExchangeRate],q.[IsVatIncluded],q.[RateDate],q.[SourceDocumentNo],
                   q.[PaymentTerms],q.[DeliveryTerms],q.[DeliveryAddress],q.[Status],q.[RevisionNo],q.[ParentDocumentId],
                   q.[Notes],q.[CreatedById],q.[Created],q.[Updated],q.[IsActive],q.[DocumentTypeId],
                   (SELECT COUNT(*) FROM {_lineTable} l WHERE l.[DocumentId] = q.[Id]) AS [line_count]
            FROM {_quoteTable} q
            LEFT JOIN [{_schema}].[Contact] ca ON ca.[Id] = q.[ContactId]
            LEFT JOIN [{_schema}].[Currency] cur ON cur.[Id] = q.[CurrencyId]
            LEFT JOIN [{_schema}].[Personnel] p ON p.[Id] = q.[RequesterPersonnelId]
            LEFT JOIN [{_schema}].[Location] hloc ON hloc.[Id] = q.[LocationId]
            INNER JOIN [{_schema}].[DocumentType] dt ON dt.[Id] = q.[DocumentTypeId]
            {where}
            ORDER BY q.[ContactId], q.[DocumentDate] DESC;
            """;

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(MapQuote(r));
        return list;
    }

    public async Task<Document?> GetByIdAsync(int id, CancellationToken ct)
    {
        // GUVENLIK (2026-08-24, Y5): satir gorunurluk (veri perdeleme) predikati burada da
        // uygulanir. Eskiden YALNIZ liste sorgularinda vardi; tekil okuma sadece
        // "WHERE q.[Id] = @Id" idi. Sonuc: listede gizlenen bir belge, kullanici adres
        // cubuguna /Sales/DocumentEdit?id=N yazinca TUM baslik/kalem/fiyat bilgisiyle
        // aciliyordu — perdeleme salt-gorsel kaliyordu.
        var dv = await _dvFilter.BuildAsync(FormCodes.SalesQuote, "q", "id", ct);

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        // Cari ismi Contact.AccountTitle'dan gelir — contact_name kolonu Faz 2'de drop edildi.
        cmd.CommandText = $"""
            SELECT q.[Id],q.[CompanyId],q.[DocumentNumber],q.[DocumentDate],q.[ValidUntil],q.[DeliveryDate],q.[DeliveryDays],q.[ContactId],
                   ca.[AccountTitle] AS [contact_name],
                   q.[ContactAddress],
                   q.[SalesRepId],q.[RequesterPersonnelId],p.[FullName] AS RequesterPersonnelName,
                   q.[LocationId],hloc.[LocationName] AS HeaderLocationName,
                   q.[CurrencyId],cur.[Code] AS CurrencyCode,cur.[Symbol] AS CurrencySymbol,q.[SubTotal],q.[DiscountRate],q.[DiscountAmount],q.[TaxRate],q.[TaxAmount],q.[GrandTotal],
                   q.[ExchangeRate],q.[IsVatIncluded],q.[RateDate],q.[SourceDocumentNo],
                   q.[PaymentTerms],q.[DeliveryTerms],q.[DeliveryAddress],q.[Status],q.[RevisionNo],q.[ParentDocumentId],
                   q.[Notes],q.[CreatedById],q.[Created],q.[Updated],q.[IsActive],q.[DocumentTypeId],
                   ca.[AccountCode] AS customer_code
            FROM {_quoteTable} q
            LEFT JOIN [{_schema}].[Contact] ca ON ca.[Id] = q.[ContactId]
            LEFT JOIN [{_schema}].[Currency] cur ON cur.[Id] = q.[CurrencyId]
            LEFT JOIN [{_schema}].[Personnel] p ON p.[Id] = q.[RequesterPersonnelId]
            LEFT JOIN [{_schema}].[Location] hloc ON hloc.[Id] = q.[LocationId]
            WHERE q.[Id] = @Id AND q.[CompanyId] = @CompanyId{dv.Sql};
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        cmd.Parameters.Add(new SqlParameter("@CompanyId", _connectionFactory.ResolveEffectiveCompanyId()));
        foreach (var prm in dv.Parameters) cmd.Parameters.Add(new SqlParameter(prm.Name, prm.Value));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? MapQuote(r) : null;
    }

    public async Task<IReadOnlyCollection<DocumentLine>> GetLinesAsync(int documentId, CancellationToken ct)
    {
        var list = new List<DocumentLine>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        // Material/Unit/Combination/Location display alanlari JOIN ile okunur — tabloda tutulmuyor.
        cmd.CommandText = $"""
            SELECT l.[Id],l.[DocumentId],l.[LineNo],l.[ItemId],l.[UnitId],
                   l.[Quantity],l.[UnitPrice],l.[DiscountRate],l.[LineTotal],
                   l.[CombinationId],l.[LocationId],l.[Notes],ISNULL(l.[NotesPinned], 0) AS [NotesPinned],
                   l.[RevisedFromId], l.[SourceLineId], l.[KitParentLineId], l.[DeliveryDate], l.[DeliveryDays],
                   ISNULL(l.[FulfilledFromStock], 0) AS [FulfilledFromStock],
                   ISNULL(l.[FulfilledByPurchase], 0) AS [FulfilledByPurchase],
                   CAST(ISNULL(l.[FulfillmentStatus], 0) AS INT) AS [FulfillmentStatus],
                   i.[Code] AS [material_code], i.[Name] AS [material_name], i.[TypeId] AS [item_type_id],
                   u.[Code] AS [unit_code], u.[Name] AS [unit_name],
                   pc.[RecordCode] AS [combination_code],
                   loc.[LocationCode] AS [location_code], loc.[LocationName] AS [location_name]
            FROM {_lineTable} l
            LEFT JOIN [{_schema}].[Items] i ON i.[Id] = l.[ItemId]
            LEFT JOIN [{_schema}].[Unit] u ON u.[Id] = l.[UnitId]
            LEFT JOIN [{_schema}].[ItemConfiguration] pc ON pc.[Id] = l.[CombinationId]
            LEFT JOIN [{_schema}].[Location] loc ON loc.[Id] = l.[LocationId]
            WHERE l.[DocumentId] = @DocumentId
            ORDER BY l.[LineNo];
            """;
        cmd.Parameters.Add(new SqlParameter("@DocumentId", documentId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(MapLine(r));
        return list;
    }

    /// <summary>
    /// INSERT (Id=0) veya UPDATE (Id&gt;0). Yeni kayit icin IDENTITY tarafindan atanan
    /// Id'yi Document.Id yazmak mumkun degil (init-only) — bunun yerine yeni Id
    /// caller'a return edilir.
    /// </summary>
    public async Task<int> UpsertAsync(Document q, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();

        if (q.Id > 0)
        {
            cmd.CommandText = $"""
                UPDATE {_quoteTable} SET
                    [DocumentTypeId]=@DocumentTypeId,
                    [DocumentDate]=@DocumentDate, [ValidUntil]=@ValidUntil, [DeliveryDate]=@DeliveryDate, [DeliveryDays]=@DeliveryDays,
                    [ContactId]=@ContactId, [ContactAddress]=@ContactAddress,
                    [SalesRepId]=@SalesRepId, [RequesterPersonnelId]=@RequesterPersonnelId, [LocationId]=@LocationId,
                    [CurrencyId]=@CurrencyId, [SubTotal]=@SubTotal, [DiscountRate]=@DiscountRate,
                    [DiscountAmount]=@DiscountAmount, [TaxRate]=@TaxRate, [TaxAmount]=@TaxAmount,
                    [GrandTotal]=@GrandTotal, [ExchangeRate]=@ExchangeRate, [IsVatIncluded]=@IsVatIncluded, [RateDate]=@RateDate,
                    [SourceDocumentNo]=@SourceDocumentNo,
                    [PaymentTerms]=@PaymentTerms, [DeliveryTerms]=@DeliveryTerms,
                    [DeliveryAddress]=@DeliveryAddress, [Status]=@Status, [RevisionNo]=@RevisionNo,
                    [ParentDocumentId]=@ParentDocumentId,
                    [Notes]=@Notes, [Updated]=@UpdatedAt
                WHERE [Id] = @Id AND [CompanyId] = @CompanyId;
                SELECT @Id;
                """;
            // Id ISTEMCIDEN gelir: sart olmadan baska sirketin belgesi ezilebilirdi.
            cmd.Parameters.Add(new SqlParameter("@Id", q.Id));
            cmd.Parameters.Add(new SqlParameter("@CompanyId", _connectionFactory.ResolveEffectiveCompanyId()));
        }
        else
        {
            cmd.CommandText = $"""
                INSERT INTO {_quoteTable}
                    ([CompanyId],[DocumentNumber],[DocumentTypeId],[DocumentDate],[ValidUntil],[DeliveryDate],[DeliveryDays],[ContactId],[ContactAddress],
                     [SalesRepId],[RequesterPersonnelId],[LocationId],[CurrencyId],[SubTotal],[DiscountRate],[DiscountAmount],[TaxRate],[TaxAmount],[GrandTotal],
                     [ExchangeRate],[IsVatIncluded],[RateDate],[SourceDocumentNo],
                     [PaymentTerms],[DeliveryTerms],[DeliveryAddress],[Status],[RevisionNo],[ParentDocumentId],
                     [Notes],[CreatedById],[Created],[Updated],[IsActive])
                VALUES
                    (@CompanyId,@DocumentNumber,@DocumentTypeId,@DocumentDate,@ValidUntil,@DeliveryDate,@DeliveryDays,@ContactId,@ContactAddress,
                     @SalesRepId,@RequesterPersonnelId,@LocationId,@CurrencyId,@SubTotal,@DiscountRate,@DiscountAmount,@TaxRate,@TaxAmount,@GrandTotal,
                     @ExchangeRate,@IsVatIncluded,@RateDate,@SourceDocumentNo,
                     @PaymentTerms,@DeliveryTerms,@DeliveryAddress,@Status,@RevisionNo,@ParentDocumentId,
                     @Notes,@CreatedById,@CreatedAt,@UpdatedAt,@IsActive);
                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """;
            // Kayit anindaki oturum kullanicisinin sirketinden cek; claim yoksa 1 (default).
            var effectiveCompanyId = q.CompanyId > 0 ? q.CompanyId
                                    : (GetCurrentCompanyId() is int cid && cid > 0 ? cid : 1);
            cmd.Parameters.Add(new SqlParameter("@CompanyId", effectiveCompanyId));
        }

        cmd.Parameters.Add(new SqlParameter("@DocumentNumber", q.DocumentNumber));
        cmd.Parameters.Add(new SqlParameter("@DocumentTypeId", (object?)q.DocumentTypeId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@DocumentDate", q.DocumentDate));
        cmd.Parameters.Add(new SqlParameter("@ValidUntil", (object?)q.ValidUntil ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@DeliveryDate", (object?)q.DeliveryDate ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@DeliveryDays", (object?)q.DeliveryDays ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@ContactId", (object?)q.ContactId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@ContactAddress", (object?)q.ContactAddress ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@SalesRepId", (object?)q.SalesRepId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@RequesterPersonnelId", (object?)q.RequesterPersonnelId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@LocationId", (object?)q.LocationId ?? DBNull.Value));
        // CurrencyId — entity'de int, default 1 (TRY). Display alanlari (CurrencyCode/Symbol)
        // SELECT'te currencies JOIN ile doldurulur, INSERT/UPDATE'te kullanilmaz.
        cmd.Parameters.Add(new SqlParameter("@CurrencyId", q.CurrencyId > 0 ? q.CurrencyId : 1));
        cmd.Parameters.Add(new SqlParameter("@SubTotal", q.SubTotal));
        cmd.Parameters.Add(new SqlParameter("@DiscountRate", q.DiscountRate));
        cmd.Parameters.Add(new SqlParameter("@DiscountAmount", q.DiscountAmount));
        cmd.Parameters.Add(new SqlParameter("@TaxRate", q.TaxRate));
        cmd.Parameters.Add(new SqlParameter("@TaxAmount", q.TaxAmount));
        cmd.Parameters.Add(new SqlParameter("@GrandTotal", q.GrandTotal));
        cmd.Parameters.Add(new SqlParameter("@ExchangeRate", q.ExchangeRate > 0 ? q.ExchangeRate : 1m));
        cmd.Parameters.Add(new SqlParameter("@IsVatIncluded", q.IsVatIncluded));
        cmd.Parameters.Add(new SqlParameter("@RateDate", (object?)q.RateDate ?? DBNull.Value));
        // Dis sistem belge no (ice aktarim mukerrer korumasi) — bos/whitespace ise NULL yazilir
        // ki filtreli index (WHERE [SourceDocumentNo] IS NOT NULL) yalniz gercek degerleri tasisin.
        cmd.Parameters.Add(new SqlParameter("@SourceDocumentNo",
            string.IsNullOrWhiteSpace(q.SourceDocumentNo) ? (object)DBNull.Value : q.SourceDocumentNo.Trim()));
        cmd.Parameters.Add(new SqlParameter("@PaymentTerms", (object?)q.PaymentTerms ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@DeliveryTerms", (object?)q.DeliveryTerms ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@DeliveryAddress", (object?)q.DeliveryAddress ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Status", q.Status.ToString()));
        cmd.Parameters.Add(new SqlParameter("@RevisionNo", q.RevisionNo));
        cmd.Parameters.Add(new SqlParameter("@ParentDocumentId", (object?)q.ParentDocumentId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Notes", (object?)q.Notes ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@CreatedById", (object?)q.CreatedById ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@CreatedAt", q.CreatedAt));
        cmd.Parameters.Add(new SqlParameter("@UpdatedAt", q.UpdatedAt));
        cmd.Parameters.Add(new SqlParameter("@IsActive", q.IsActive));

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    /// <summary>
    /// UPSERT (DELETE degil). Mevcut satirlarin Id'leri korunur ki:
    ///   1) Widget verileri (WidgetTra, RecordId = DocumentLine.Id) orphanlanmaz,
    ///   2) Kombinasyon detaylari (sales_quote_line_details) yeniden yazilmaz.
    /// Akis:
    ///   - DB'deki mevcut satir Id'lerini cek
    ///   - Request'teki her satir: Id>0 ve mevcut ise UPDATE, aksi halde INSERT
    ///   - Request'te olmayan mevcut Id'leri DELETE et (CASCADE detaylari temizler)
    /// </summary>
    public async Task SaveLinesAsync(int documentId, IReadOnlyCollection<DocumentLine> lines, CancellationToken ct,
        int? createdById = null, IReadOnlyCollection<PendingFulfillmentEntry>? fulfillmentEntries = null)
    {
        // TANI LOG: hangi satir id'lerine hangi qty/price geliyor — revize sorunlarini
        // takip etmek icin (kullanici "qty parent'a yaziliyor" raporu).
        if (_logger != null)
        {
            foreach (var ln in lines)
            {
                _logger.LogInformation(
                    "[SaveLines] docId={DocId} lineId={LineId} lineNo={LineNo} itemId={ItemId} qty={Qty} unitPrice={Price} revisedFromId={Rev}",
                    documentId, ln.Id, ln.LineNo, ln.ItemId, ln.Quantity, ln.UnitPrice, ln.RevisedFromId);
            }
        }

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        // 2026-07-20 (Madde 2): transaction eklendi — kalem yazımı (sil+upsert) İLE karşılama
        // defteri kaydının (fulfillmentEntries doluysa) atomik olabilmesi için gerekliydi; bu
        // metod öncesinde transaction TAŞIMIYORDU (her komut kendi auto-commit'i ile çalışıyordu).
        // Yan etki (kasıtlı iyileştirme): kalem sil+upsert döngüsünün kendisi de artık atomik.
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            // 1) Mevcut satir Id'lerini topla
            var existingIds = new HashSet<int>();
            await using (var getCmd = conn.CreateCommand())
            {
                getCmd.Transaction = tx;
                getCmd.CommandText = $"SELECT [Id] FROM {_lineTable} WHERE [DocumentId] = @DocumentId;";
                getCmd.Parameters.Add(new SqlParameter("@DocumentId", documentId));
                await using var r = await getCmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct)) existingIds.Add(r.GetInt32(0));
            }

            var keptIds = new HashSet<int>();

            // 2) Request satirlari — UPSERT
            foreach (var ln in lines)
            {
                var isUpdate = ln.Id > 0 && existingIds.Contains(ln.Id);
                if (isUpdate) keptIds.Add(ln.Id);

                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                // BaseQuantity: ana birime normalize miktar (ticari satırlarda da tutarlı doldurulur —
                // stok etkilemez ama teklif→sipariş→irsaliye dönüşümü/raporlamada baz miktar hazır olur)
                var baseQtyExpr = StockUnitSql.BaseQtyExpr($"[{_schema}].[Items]", $"[{_schema}].[ItemUnits]", "@Quantity", "@ItemId", "@UnitId");
                if (isUpdate)
                {
                    cmd.CommandText = $"""
                        UPDATE {_lineTable} SET
                            [LineNo]         = @LineNo,
                            [ItemId]         = @ItemId,
                            [UnitId]         = @UnitId,
                            [Quantity]        = @Quantity,
                            [BaseQuantity]   = {baseQtyExpr},
                            [UnitPrice]      = @UnitPrice,
                            [DiscountRate]   = @DiscountRate,
                            [LineTotal]      = @LineTotal,
                            [CombinationId]  = @CombinationId,
                            [LocationId]     = @LocationId,
                            [Notes]           = @Notes,
                            [NotesPinned]    = @NotesPinned,
                            [RevisedFromId] = @RevisedFromId,
                            [SourceLineId]  = @SourceLineId,
                            [DeliveryDate]  = @DeliveryDate,
                            [DeliveryDays]  = @DeliveryDays
                        WHERE [Id] = @Id AND [DocumentId] = @DocumentId
                          AND [CompanyId] = (SELECT d.[CompanyId] FROM {_quoteTable} d WHERE d.[Id] = @DocumentId);
                        """;
                    cmd.Parameters.Add(new SqlParameter("@Id", ln.Id));
                }
                else
                {
                    cmd.CommandText = $"""
                        INSERT INTO {_lineTable}
                            ([DocumentId],[LineNo],[ItemId],[UnitId],
                             [Quantity],[BaseQuantity],[UnitPrice],[DiscountRate],[LineTotal],
                             [CombinationId],[LocationId],[Notes],[NotesPinned],[RevisedFromId],[SourceLineId],[DeliveryDate],[DeliveryDays],[CompanyId])
                        VALUES
                            (@DocumentId,@LineNo,@ItemId,@UnitId,
                             @Quantity,{baseQtyExpr},@UnitPrice,@DiscountRate,@LineTotal,
                             @CombinationId,@LocationId,@Notes,@NotesPinned,@RevisedFromId,@SourceLineId,@DeliveryDate,@DeliveryDays,
                             (SELECT d.[CompanyId] FROM {_quoteTable} d WHERE d.[Id] = @DocumentId));
                        """;
                }
                cmd.Parameters.Add(new SqlParameter("@DocumentId", documentId));
                cmd.Parameters.Add(new SqlParameter("@LineNo", ln.LineNo));
                cmd.Parameters.Add(new SqlParameter("@ItemId", ln.ItemId));
                cmd.Parameters.Add(new SqlParameter("@UnitId", (object?)ln.UnitId ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Quantity", ln.Quantity));
                cmd.Parameters.Add(new SqlParameter("@UnitPrice", ln.UnitPrice));
                cmd.Parameters.Add(new SqlParameter("@DiscountRate", ln.DiscountRate));
                cmd.Parameters.Add(new SqlParameter("@LineTotal", ln.LineTotal));
                cmd.Parameters.Add(new SqlParameter("@CombinationId", (object?)ln.CombinationId ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@LocationId", (object?)ln.LocationId ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Notes", (object?)ln.Notes ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@NotesPinned", ln.NotesPinned));
                cmd.Parameters.Add(new SqlParameter("@RevisedFromId", (object?)ln.RevisedFromId ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@SourceLineId", (object?)ln.SourceLineId ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@DeliveryDate", (object?)ln.DeliveryDate ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@DeliveryDays", (object?)ln.DeliveryDays ?? DBNull.Value));
                await cmd.ExecuteNonQueryAsync(ct);
            }

            // 3) Request'te olmayan mevcut Id'leri sil (CASCADE detaylari temizler)
            var toDelete = existingIds.Except(keptIds).ToArray();
            if (toDelete.Length > 0)
            {
                await using var delCmd = conn.CreateCommand();
                delCmd.Transaction = tx;
                delCmd.CommandText = $"""
                    DELETE FROM {_lineTable} WHERE [DocumentId] = @DocumentId AND [Id] IN ({string.Join(",", toDelete)})
                      AND [CompanyId] = (SELECT d.[CompanyId] FROM {_quoteTable} d WHERE d.[Id] = @DocumentId);
                    """;
                delCmd.Parameters.Add(new SqlParameter("@DocumentId", documentId));
                await delCmd.ExecuteNonQueryAsync(ct);
            }

            // 4) Karşılama defteri (2026-07-20, Madde 2) — kalem yazımıyla AYNI transaction'da.
            // _lineLinks/_logger: FAZ 1b-ii DocumentLineLink dual-write (best-effort, bkz.
            // FulfillmentLedger.TryLinkFulfillmentEntriesAsync) — ana kaydı asla etkilemez.
            if (fulfillmentEntries is { Count: > 0 })
                await FulfillmentLedger.InsertEntriesAsync(conn, tx, _schema, documentId, fulfillmentEntries, createdById, ct, _lineLinks, _logger);

            // 5) DocumentLineLink dual-write (FAZ 1b-iii, 2026-07-20, "A: dönüşüm zinciri") —
            // yukarıdaki upsert/silme TAMAMLANDIKTAN SONRA, best-effort. Bkz. DerivationLinkHelper
            // sınıf başlığı (WorkOrderService/FulfillmentLedger ile birebir aynı defensive desen).
            await DerivationLinkHelper.TryLinkDerivedLinesAsync(conn, tx, _schema, documentId, createdById, _lineLinks, _logger, ct);

            await tx.CommitAsync(ct);
        }
        catch
        {
            try { await tx.RollbackAsync(ct); } catch { /* bağlantı düşmüşse yut */ }
            throw;
        }
    }

    public async Task<int> AppendStockLineAsync(int documentId, DocumentLine line, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            var newId = await AppendStockLineCoreAsync(conn, tx, documentId, line, ct);
            await tx.CommitAsync(ct);
            return newId;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// Append-only insert — SaveLinesAsync'in upsert-all/replace davranışının aksine mevcut
    /// satırlara dokunmaz. LineNo ataması UPDLOCK+HOLDLOCK ile concurrent-safe (SqlDocumentNumberService
    /// deseninin aynısı — bkz. SqlDocumentNumberService.IncrementCounterAsync).
    /// </summary>
    private async Task<int> AppendStockLineCoreAsync(SqlConnection conn, SqlTransaction tx, int documentId, DocumentLine line, CancellationToken ct)
    {
        int nextLineNo;
        await using (var selCmd = conn.CreateCommand())
        {
            selCmd.Transaction = tx;
            selCmd.CommandText = $"""
                SELECT ISNULL(MAX([LineNo]), 0) + 1 FROM {_lineTable} WITH (UPDLOCK, HOLDLOCK)
                WHERE [DocumentId] = @DocumentId;
                """;
            selCmd.Parameters.Add(new SqlParameter("@DocumentId", documentId));
            nextLineNo = Convert.ToInt32(await selCmd.ExecuteScalarAsync(ct));
        }

        await using var insCmd = conn.CreateCommand();
        insCmd.Transaction = tx;
        insCmd.CommandText = $"""
            INSERT INTO {_lineTable}
                ([DocumentId],[LineNo],[ItemId],[UnitId],[Quantity],[BaseQuantity],[UnitPrice],[DiscountRate],[LineTotal],
                 [CombinationId],[LocationId],[FromLocationId],[MovementType],[UnitCost],[LotNo],[Notes],[CompanyId])
            VALUES
                (@DocumentId,@LineNo,@ItemId,@UnitId,@Quantity,{StockUnitSql.BaseQtyExpr($"[{_schema}].[Items]", $"[{_schema}].[ItemUnits]", "@Quantity", "@ItemId", "@UnitId")},0,0,0,
                 @CombinationId,@LocationId,@FromLocationId,@MovementType,@UnitCost,@LotNo,@Notes,
                 (SELECT d.[CompanyId] FROM {_quoteTable} d WHERE d.[Id] = @DocumentId));
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;
        insCmd.Parameters.Add(new SqlParameter("@DocumentId", documentId));
        insCmd.Parameters.Add(new SqlParameter("@LineNo", nextLineNo));
        insCmd.Parameters.Add(new SqlParameter("@ItemId", line.ItemId));
        insCmd.Parameters.Add(new SqlParameter("@UnitId", (object?)line.UnitId ?? DBNull.Value));
        insCmd.Parameters.Add(new SqlParameter("@Quantity", line.Quantity));
        insCmd.Parameters.Add(new SqlParameter("@CombinationId", (object?)line.CombinationId ?? DBNull.Value));
        insCmd.Parameters.Add(new SqlParameter("@LocationId", (object?)line.LocationId ?? DBNull.Value));
        insCmd.Parameters.Add(new SqlParameter("@FromLocationId", (object?)line.FromLocationId ?? DBNull.Value));
        insCmd.Parameters.Add(new SqlParameter("@MovementType", (object?)line.MovementType ?? DBNull.Value));
        insCmd.Parameters.Add(new SqlParameter("@UnitCost", (object?)line.UnitCost ?? DBNull.Value));
        insCmd.Parameters.Add(new SqlParameter("@LotNo", (object?)line.LotNo ?? DBNull.Value));
        insCmd.Parameters.Add(new SqlParameter("@Notes", (object?)line.Notes ?? DBNull.Value));
        return Convert.ToInt32(await insCmd.ExecuteScalarAsync(ct));
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx   = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = $"UPDATE {_quoteTable} SET [IsActive] = 0, [Updated] = @Now WHERE [Id] = @Id AND [CompanyId] = @CompanyId;";
                cmd.Parameters.Add(new SqlParameter("@CompanyId", _connectionFactory.ResolveEffectiveCompanyId()));
                cmd.Parameters.Add(new SqlParameter("@Id", id));
                cmd.Parameters.Add(new SqlParameter("@Now", DateTime.Now));
                await cmd.ExecuteNonQueryAsync(ct);
            }

            // Bu belge bir İhtiyaç Kaydı'nı karşılıyorduysa, defterdeki katkısını AYNI
            // transaction'da geri al. Ayrı çağrı olarak yapılsaydı "belge silindi, karşılama
            // geri alınmadı" yarım durumu oluşabilirdi — ki bu tam olarak düzeltmeye
            // çalıştığımız hatanın kendisi (ihtiyaç satırı sonsuza dek karşılanmış görünür ve
            // kaynak belge bir daha silinemez). _lineLinks/_logger: FAZ 1b-ii DocumentLineLink
            // dual-write reverse (best-effort).
            await FulfillmentLedger.ReverseByDocumentAsync(conn, tx, _schema, id, null, ct, _lineLinks, _logger);

            // Bu belge bir TÜREV belgeyse (başka bir belgenin SourceLineId ile referans verdiği
            // satırlara sahipse — örn. teklif→sipariş), dönüşüm link'lerini (LinkType.Derivation)
            // pasifleştir — FAZ 1b-iii (2026-07-20, "A" mekanizması) KN-3 senkronu. Bkz.
            // DerivationLinkHelper sınıf başlığı; tip-10 filtreli, karşılama link'lerine dokunmaz.
            await DerivationLinkHelper.TryReverseDerivedLinksAsync(id, null, _lineLinks, _logger, ct);

            await tx.CommitAsync(ct);
        }
        catch
        {
            try { await tx.RollbackAsync(ct); } catch { /* bağlantı düşmüşse yut */ }
            throw;
        }
    }

    public async Task UpdateStatusAsync(int id, string status, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE {_quoteTable} SET [Status] = @Status, [Updated] = @Now WHERE [Id] = @Id AND [CompanyId] = @CompanyId;";
        cmd.Parameters.Add(new SqlParameter("@CompanyId", _connectionFactory.ResolveEffectiveCompanyId()));
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        cmd.Parameters.Add(new SqlParameter("@Status", status));
        cmd.Parameters.Add(new SqlParameter("@Now", DateTime.Now));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// 2026-07-18 — Satirin bagli oldugu belge Id'si (satir yoksa null).
    /// Yetkilendirme icin: satir-bazli endpoint'te izin, satirin ait oldugu BELGENIN
    /// tipinden cozulur (istemci beyanindan degil). Bkz. SalesController.ReviseLine.
    /// </summary>
    public async Task<int?> GetDocumentIdByLineAsync(int lineId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT [DocumentId] FROM {_lineTable} WHERE [Id] = @Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", lineId));
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null || result is DBNull ? null : Convert.ToInt32(result);
    }

    /// <summary>
    /// Satir revizyonu — atomic SQL batch:
    ///   1) Eski satirin notlari @Description ile UPDATE edilir (revize gerekcesi).
    ///   2) Yeni satir INSERT edilir: eski satirin kolonlari aynen kopyalanir,
    ///      revised_from_id = parentLineId, notes = eski satirin ORIJINAL notu.
    ///   3) Kombinasyon detaylari (sales_quote_line_details) yeni satira kopyalanir.
    /// Not: Siralama icin line_no = MAX(line_no)+1 atanir (belge genelinde).
    /// Widget degerleri (WidgetTra vb.) bu method'a dahil DEGIL — WidgetService
    /// tarafinda copy-forward yapilir (schema dinamik).
    /// </summary>
    public async Task<int?> ReviseLineAsync(int parentLineId, string? description, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (Microsoft.Data.SqlClient.SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            // 1) Eski satirin dokumentId + orijinal notes'unu al (copy icin, sonra UPDATE)
            int documentId;
            string? originalNotes;
            await using (var selCmd = conn.CreateCommand())
            {
                selCmd.Transaction = tx;
                selCmd.CommandText = $"SELECT [DocumentId], [Notes] FROM {_lineTable} WHERE [Id] = @Id;";
                selCmd.Parameters.Add(new SqlParameter("@Id", parentLineId));
                await using var r = await selCmd.ExecuteReaderAsync(ct);
                if (!await r.ReadAsync(ct))
                {
                    await tx.RollbackAsync(ct);
                    return null;
                }
                documentId = r.GetInt32(0);
                originalNotes = r.IsDBNull(1) ? null : r.GetString(1);
            }

            // 2) Yeni line_no hesapla (belgenin max + 1)
            int newLineNo;
            await using (var maxCmd = conn.CreateCommand())
            {
                maxCmd.Transaction = tx;
                maxCmd.CommandText = $"SELECT ISNULL(MAX([LineNo]), 0) + 1 FROM {_lineTable} WHERE [DocumentId] = @DocId;";
                maxCmd.Parameters.Add(new SqlParameter("@DocId", documentId));
                var obj = await maxCmd.ExecuteScalarAsync(ct);
                newLineNo = obj == null || obj == DBNull.Value ? 1 : Convert.ToInt32(obj);
            }

            // 3) Eski satirin notes'unu aciklama ile UPDATE et
            await using (var upd = conn.CreateCommand())
            {
                upd.Transaction = tx;
                upd.CommandText = $"UPDATE {_lineTable} SET [Notes] = @Desc WHERE [Id] = @Id AND [CompanyId] = @CompanyId;";
                upd.Parameters.Add(new SqlParameter("@CompanyId", _connectionFactory.ResolveEffectiveCompanyId()));
                upd.Parameters.Add(new SqlParameter("@Id", parentLineId));
                upd.Parameters.Add(new SqlParameter("@Desc",
                    string.IsNullOrWhiteSpace(description) ? (object)DBNull.Value : description));
                await upd.ExecuteNonQueryAsync(ct);
            }

            // 4) Yeni satir INSERT — SELECT ile eski satirin kolonlarini kopyala
            //    notes = orijinal notes, revised_from_id = NULL (yeni satir her zaman aktif/son haldir)
            //    source_line_id = eski satirinki aynen tasinir (orijinal kaynak iz korunur)
            int newLineId;
            await using (var insCmd = conn.CreateCommand())
            {
                insCmd.Transaction = tx;
                insCmd.CommandText = $"""
                    INSERT INTO {_lineTable}
                        ([DocumentId],[LineNo],[ItemId],[UnitId],[Quantity],[BaseQuantity],[UnitPrice],
                         [DiscountRate],[LineTotal],[CombinationId],[LocationId],[Notes],
                         [NotesPinned],[RevisedFromId],[SourceLineId],[DeliveryDate],[DeliveryDays],[CompanyId])
                    SELECT
                        [DocumentId], @NewLineNo, [ItemId], [UnitId], [Quantity], [BaseQuantity], [UnitPrice],
                        [DiscountRate], [LineTotal], [CombinationId], [LocationId], @OrigNotes,
                        [NotesPinned], NULL, [SourceLineId], [DeliveryDate], [DeliveryDays], [CompanyId]
                    FROM {_lineTable}
                    WHERE [Id] = @ParentId;
                    SELECT CAST(SCOPE_IDENTITY() AS INT);
                    """;
                insCmd.Parameters.Add(new SqlParameter("@NewLineNo", newLineNo));
                insCmd.Parameters.Add(new SqlParameter("@ParentId", parentLineId));
                insCmd.Parameters.Add(new SqlParameter("@OrigNotes", (object?)originalNotes ?? DBNull.Value));
                var idObj = await insCmd.ExecuteScalarAsync(ct);
                newLineId = Convert.ToInt32(idObj);
            }

            // 4b) Eski satirda revised_from_id = yeni satir id'si — eski satir artik superseded
            await using (var markOld = conn.CreateCommand())
            {
                markOld.Transaction = tx;
                markOld.CommandText = $"UPDATE {_lineTable} SET [RevisedFromId] = @NewLineId WHERE [Id] = @ParentId AND [CompanyId] = @CompanyId;";
                markOld.Parameters.Add(new SqlParameter("@CompanyId", _connectionFactory.ResolveEffectiveCompanyId()));
                markOld.Parameters.Add(new SqlParameter("@NewLineId", newLineId));
                markOld.Parameters.Add(new SqlParameter("@ParentId", parentLineId));
                await markOld.ExecuteNonQueryAsync(ct);
            }

            // 5) Kombinasyon detaylarini yeni satira kopyala
            await using (var copyDetails = conn.CreateCommand())
            {
                copyDetails.Transaction = tx;
                copyDetails.CommandText = $"""
                    INSERT INTO {_detailTable}
                        ([QuoteLineId],[FeatureName],[ValueCode],[ValueName],[Description],[LineOrder],[CompanyId])
                    SELECT @NewId, [FeatureName], [ValueCode], [ValueName], [Description], [LineOrder],
                           (SELECT dl.[CompanyId] FROM {_lineTable} dl WHERE dl.[Id] = @NewId)
                    FROM {_detailTable}
                    WHERE [QuoteLineId] = @ParentId;
                    """;
                copyDetails.Parameters.Add(new SqlParameter("@NewId", newLineId));
                copyDetails.Parameters.Add(new SqlParameter("@ParentId", parentLineId));
                await copyDetails.ExecuteNonQueryAsync(ct);
            }

            // 6) DocumentLineLink dual-write (FAZ 1b-iii, 2026-07-20) — revize yeni satir da
            // SourceLineId'yi kopyaladigindan (yukarida INSERT, [SourceLineId] aynen tasinir),
            // o belgenin turev link'lerini reset+rebuild ile guncelle (SaveLinesAsync ile ayni
            // helper). Boylece revize sonrasi link SUM(tip 10) eski GetDerivedLineAggregatesAsync
            // ile birebir kalir (superseded eski + yeni satir birlikte sayilir; helper de
            // SourceLineId IS NOT NULL olan her satiri linkler, RevisedFromId filtrelemez ->
            // eski okuma ile ayni). Best-effort; ana revize kaydini asla bozmaz.
            await DerivationLinkHelper.TryLinkDerivedLinesAsync(conn, tx, _schema, documentId, null, _lineLinks, _logger, ct);

            await tx.CommitAsync(ct);
            return newLineId;
        }
        catch
        {
            try { await tx.RollbackAsync(ct); } catch { }
            throw;
        }
    }

    public async Task<string> GetNextDocumentNumberAsync(CancellationToken ct)
    {
        // Format: TKL202600000001 (15 karakter) — TKL + yyyy + 8 haneli sira
        var prefix = "TKL" + DateTime.Now.ToString("yyyy");
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP 1 [DocumentNumber] FROM {_quoteTable}
            WHERE [CompanyId] = @CompanyId AND [DocumentNumber] LIKE @Prefix + '%'
            ORDER BY [DocumentNumber] DESC;
            """;
        cmd.Parameters.Add(new SqlParameter("@CompanyId", _connectionFactory.ResolveEffectiveCompanyId()));
        cmd.Parameters.Add(new SqlParameter("@Prefix", prefix));
        var last = await cmd.ExecuteScalarAsync(ct) as string;
        int nextSeq = 1;
        if (last != null && last.Length == 15 && last.StartsWith(prefix))
        {
            if (int.TryParse(last[prefix.Length..], out var seq))
                nextSeq = seq + 1;
        }
        return prefix + nextSeq.ToString("D8");
    }

    /// <summary>
    /// Ice aktarim mukerrer kontrolu: (belge turu + dis sistem belge no) ikilisiyle AKTIF belgeyi
    /// bulur. Bulamazsa null. Ayni kaynak numara farkli belge turlerinde gelebildigi icin
    /// DocumentTypeId filtresi ZORUNLUDUR (DB'de UNIQUE kisit yok — bkz. CalibraDatabaseInitializer).
    /// Birden fazla eslesme varsa (elle acilmis mukerrer) en eski/kucuk Id doner — deterministik.
    /// </summary>
    public async Task<int?> FindIdBySourceDocumentNoAsync(string sourceDocumentNo, int documentTypeId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sourceDocumentNo) || documentTypeId <= 0) return null;

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP 1 [Id] FROM {_quoteTable}
            WHERE [CompanyId] = @CompanyId AND [SourceDocumentNo] = @SourceDocumentNo
              AND [DocumentTypeId]   = @DocumentTypeId
              AND [IsActive] = 1
            ORDER BY [Id];
            """;
        cmd.Parameters.Add(new SqlParameter("@CompanyId", _connectionFactory.ResolveEffectiveCompanyId()));
        cmd.Parameters.Add(new SqlParameter("@SourceDocumentNo", sourceDocumentNo.Trim()));
        cmd.Parameters.Add(new SqlParameter("@DocumentTypeId", documentTypeId));

        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null || result == DBNull.Value ? null : Convert.ToInt32(result);
    }

    private static Document MapQuote(SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        CompanyId = TryGetOrdinal(r, "CompanyId") is int cmpOrd && cmpOrd >= 0 && !r.IsDBNull(cmpOrd)
            ? r.GetInt32(cmpOrd) : 0,
        DocumentNumber = r.GetString(r.GetOrdinal("DocumentNumber")),
        // Kolon eski semada olmayabilir → TryGetOrdinal ile guvenli oku.
        SourceDocumentNo = TryGetOrdinal(r, "SourceDocumentNo") is int sdnOrd && sdnOrd >= 0 && !r.IsDBNull(sdnOrd)
            ? r.GetString(sdnOrd) : null,
        DocumentTypeId = TryGetOrdinal(r, "DocumentTypeId") is int dtOrd && dtOrd >= 0 && !r.IsDBNull(dtOrd)
            ? r.GetInt32(dtOrd) : null,
        DocumentDate = r.GetDateTime(r.GetOrdinal("DocumentDate")),
        ValidUntil = r.IsDBNull(r.GetOrdinal("ValidUntil")) ? null : r.GetDateTime(r.GetOrdinal("ValidUntil")),
        DeliveryDate = SafeOrdinalDate(r, "DeliveryDate"),
        DeliveryDays = SafeOrdinalInt(r, "DeliveryDays"),
        ContactId = r.IsDBNull(r.GetOrdinal("ContactId")) ? null : r.GetInt32(r.GetOrdinal("ContactId")),
        ContactName = r.IsDBNull(r.GetOrdinal("contact_name")) ? null : r.GetString(r.GetOrdinal("contact_name")),
        ContactAddress = r.IsDBNull(r.GetOrdinal("ContactAddress")) ? null : r.GetString(r.GetOrdinal("ContactAddress")),
        ContactCode = TryGetOrdinal(r, "customer_code") is int ccOrd && ccOrd >= 0 && !r.IsDBNull(ccOrd)
            ? r.GetString(ccOrd) : null,
        SalesRepId = r.IsDBNull(r.GetOrdinal("SalesRepId")) ? null : r.GetInt32(r.GetOrdinal("SalesRepId")),
        RequesterPersonnelId = TryGetOrdinal(r, "RequesterPersonnelId") is int reqOrd && reqOrd >= 0 && !r.IsDBNull(reqOrd)
            ? r.GetInt32(reqOrd) : null,
        RequesterPersonnelName = TryGetOrdinal(r, "RequesterPersonnelName") is int rnOrd && rnOrd >= 0 && !r.IsDBNull(rnOrd)
            ? r.GetString(rnOrd) : null,
        LocationId = TryGetOrdinal(r, "LocationId") is int hlocOrd && hlocOrd >= 0 && !r.IsDBNull(hlocOrd)
            ? r.GetInt32(hlocOrd) : null,
        LocationName = TryGetOrdinal(r, "HeaderLocationName") is int hlocNOrd && hlocNOrd >= 0 && !r.IsDBNull(hlocNOrd)
            ? r.GetString(hlocNOrd) : null,
        CurrencyId = TryGetOrdinal(r, "CurrencyId") is int curOrd && curOrd >= 0 && !r.IsDBNull(curOrd)
            ? r.GetInt32(curOrd) : 1,
        CurrencyCode = TryGetOrdinal(r, "CurrencyCode") is int curCodeOrd && curCodeOrd >= 0 && !r.IsDBNull(curCodeOrd)
            ? r.GetString(curCodeOrd) : null,
        CurrencySymbol = TryGetOrdinal(r, "CurrencySymbol") is int curSymOrd && curSymOrd >= 0 && !r.IsDBNull(curSymOrd)
            ? r.GetString(curSymOrd) : null,
        SubTotal = r.GetDecimal(r.GetOrdinal("SubTotal")),
        DiscountRate = r.GetDecimal(r.GetOrdinal("DiscountRate")),
        DiscountAmount = r.GetDecimal(r.GetOrdinal("DiscountAmount")),
        TaxRate = r.GetDecimal(r.GetOrdinal("TaxRate")),
        TaxAmount = r.GetDecimal(r.GetOrdinal("TaxAmount")),
        GrandTotal = r.GetDecimal(r.GetOrdinal("GrandTotal")),
        ExchangeRate = TryGetOrdinal(r, "ExchangeRate") is int exrOrd && exrOrd >= 0 && !r.IsDBNull(exrOrd)
            ? r.GetDecimal(exrOrd) : 1m,
        RateDate = TryGetOrdinal(r, "RateDate") is int rdOrd && rdOrd >= 0 && !r.IsDBNull(rdOrd)
            ? r.GetDateTime(rdOrd) : (DateTime?)null,
        IsVatIncluded = TryGetOrdinal(r, "IsVatIncluded") is int vatOrd && vatOrd >= 0 && !r.IsDBNull(vatOrd)
            && r.GetBoolean(vatOrd),
        PaymentTerms = r.IsDBNull(r.GetOrdinal("PaymentTerms")) ? null : r.GetString(r.GetOrdinal("PaymentTerms")),
        DeliveryTerms = r.IsDBNull(r.GetOrdinal("DeliveryTerms")) ? null : r.GetString(r.GetOrdinal("DeliveryTerms")),
        DeliveryAddress = r.IsDBNull(r.GetOrdinal("DeliveryAddress")) ? null : r.GetString(r.GetOrdinal("DeliveryAddress")),
        Status = Enum.TryParse<DocumentStatus>(r.GetString(r.GetOrdinal("Status")), out var s) ? s : DocumentStatus.Draft,
        RevisionNo = r.GetInt32(r.GetOrdinal("RevisionNo")),
        ParentDocumentId = r.IsDBNull(r.GetOrdinal("ParentDocumentId")) ? null : r.GetInt32(r.GetOrdinal("ParentDocumentId")),
        Notes = r.IsDBNull(r.GetOrdinal("Notes")) ? null : r.GetString(r.GetOrdinal("Notes")),
        CreatedById = TryGetOrdinal(r, "CreatedById") is int cbOrd && cbOrd >= 0 && !r.IsDBNull(cbOrd) ? r.GetInt32(cbOrd) : null,
        CreatedAt = r.GetDateTime(r.GetOrdinal("Created")),
        UpdatedAt = r.IsDBNull(r.GetOrdinal("Updated")) ? DateTime.MinValue : r.GetDateTime(r.GetOrdinal("Updated")),
        IsActive = r.GetBoolean(r.GetOrdinal("IsActive")),
        LineCount      = TryGetOrdinal(r, "line_count")      is int lcOrd  && lcOrd  >= 0 ? r.GetInt32(lcOrd)  : 0,
        FulfillPending = TryGetOrdinal(r, "fulfill_pending") is int fp0rd  && fp0rd  >= 0 ? r.GetInt32(fp0rd)  : 0,
        FulfillPartial = TryGetOrdinal(r, "fulfill_partial") is int fp1rd  && fp1rd  >= 0 ? r.GetInt32(fp1rd)  : 0,
        FulfillFull    = TryGetOrdinal(r, "fulfill_full")    is int fp2rd  && fp2rd  >= 0 ? r.GetInt32(fp2rd)  : 0,
    };

    private static DocumentLine MapLine(SqlDataReader r)
    {
        var unitIdOrd  = TryGetOrdinal(r, "UnitId");
        var unitCodeOrd = TryGetOrdinal(r, "unit_code");
        var unitNameOrd = TryGetOrdinal(r, "unit_name");
        var matCodeOrd = TryGetOrdinal(r, "material_code");
        var matNameOrd = TryGetOrdinal(r, "material_name");
        var itemTypeIdOrd = TryGetOrdinal(r, "item_type_id");
        var combIdOrd  = TryGetOrdinal(r, "CombinationId");
        var combCodeOrd = TryGetOrdinal(r, "combination_code");
        var locIdOrd   = TryGetOrdinal(r, "LocationId");
        var locCodeOrd = TryGetOrdinal(r, "location_code");
        var locNameOrd = TryGetOrdinal(r, "location_name");
        var notesPinnedOrd = TryGetOrdinal(r, "NotesPinned");
        var revisedFromIdOrd = TryGetOrdinal(r, "RevisedFromId");
        var sourceLineIdOrd = TryGetOrdinal(r, "SourceLineId");
        var kitParentIdOrd = TryGetOrdinal(r, "KitParentLineId");
        return new()
        {
            Id = r.GetInt32(r.GetOrdinal("Id")),
            DocumentId = r.GetInt32(r.GetOrdinal("DocumentId")),
            LineNo = r.GetInt32(r.GetOrdinal("LineNo")),
            ItemId = r.GetInt32(r.GetOrdinal("ItemId")),
            UnitId = unitIdOrd >= 0 && !r.IsDBNull(unitIdOrd) ? r.GetInt32(unitIdOrd) : null,
            Quantity = r.GetDecimal(r.GetOrdinal("Quantity")),
            UnitPrice = r.GetDecimal(r.GetOrdinal("UnitPrice")),
            DiscountRate = r.GetDecimal(r.GetOrdinal("DiscountRate")),
            LineTotal = r.GetDecimal(r.GetOrdinal("LineTotal")),
            CombinationId = combIdOrd >= 0 && !r.IsDBNull(combIdOrd) ? r.GetInt32(combIdOrd) : null,
            LocationId = locIdOrd >= 0 && !r.IsDBNull(locIdOrd) ? r.GetInt32(locIdOrd) : null,
            Notes = r.IsDBNull(r.GetOrdinal("Notes")) ? null : r.GetString(r.GetOrdinal("Notes")),
            NotesPinned = notesPinnedOrd >= 0 && !r.IsDBNull(notesPinnedOrd) && r.GetBoolean(notesPinnedOrd),
            RevisedFromId = revisedFromIdOrd >= 0 && !r.IsDBNull(revisedFromIdOrd) ? r.GetInt32(revisedFromIdOrd) : null,
            SourceLineId = sourceLineIdOrd >= 0 && !r.IsDBNull(sourceLineIdOrd) ? r.GetInt32(sourceLineIdOrd) : null,
            KitParentLineId = kitParentIdOrd >= 0 && !r.IsDBNull(kitParentIdOrd) ? r.GetInt32(kitParentIdOrd) : null,
            DeliveryDate = SafeOrdinalDate(r, "DeliveryDate"),
            DeliveryDays = SafeOrdinalInt(r, "DeliveryDays"),
            MaterialCode = matCodeOrd >= 0 && !r.IsDBNull(matCodeOrd) ? r.GetString(matCodeOrd) : null,
            MaterialName = matNameOrd >= 0 && !r.IsDBNull(matNameOrd) ? r.GetString(matNameOrd) : null,
            ItemTypeId = itemTypeIdOrd >= 0 && !r.IsDBNull(itemTypeIdOrd) ? r.GetInt32(itemTypeIdOrd) : null,
            UnitCode = unitCodeOrd >= 0 && !r.IsDBNull(unitCodeOrd) ? r.GetString(unitCodeOrd) : null,
            UnitName = unitNameOrd >= 0 && !r.IsDBNull(unitNameOrd) ? r.GetString(unitNameOrd) : null,
            CombinationCode = combCodeOrd >= 0 && !r.IsDBNull(combCodeOrd) ? r.GetString(combCodeOrd) : null,
            LocationCode       = locCodeOrd >= 0 && !r.IsDBNull(locCodeOrd) ? r.GetString(locCodeOrd) : null,
            LocationName       = locNameOrd >= 0 && !r.IsDBNull(locNameOrd) ? r.GetString(locNameOrd) : null,
            FulfilledFromStock = SafeOrdinalDecimal(r, "FulfilledFromStock"),
            FulfilledByPurchase = SafeOrdinalDecimal(r, "FulfilledByPurchase"),
            FulfillmentStatus  = TryGetOrdinal(r, "FulfillmentStatus") is int fsOrd && fsOrd >= 0 && !r.IsDBNull(fsOrd) ? r.GetInt32(fsOrd) : 0,
        };
    }

    private static int TryGetOrdinal(SqlDataReader r, string name)
    {
        try { return r.GetOrdinal(name); } catch { return -1; }
    }

    /// <summary>Kolon yoksa (eski schema) veya null ise null doner — SELECT'lerde guvenli.</summary>
    private static DateTime? SafeOrdinalDate(SqlDataReader r, string name)
    {
        var ord = TryGetOrdinal(r, name);
        if (ord < 0) return null;
        return r.IsDBNull(ord) ? null : r.GetDateTime(ord);
    }

    /// <summary>Int kolonunu guvenli oku — kolon yoksa veya null ise null doner.</summary>
    private static int? SafeOrdinalInt(SqlDataReader r, string name)
    {
        var ord = TryGetOrdinal(r, name);
        if (ord < 0) return null;
        return r.IsDBNull(ord) ? null : r.GetInt32(ord);
    }

    /// <summary>Decimal kolonunu guvenli oku — kolon yoksa veya null ise 0 doner.</summary>
    private static decimal SafeOrdinalDecimal(SqlDataReader r, string name)
    {
        var ord = TryGetOrdinal(r, name);
        if (ord < 0) return 0m;
        return r.IsDBNull(ord) ? 0m : r.GetDecimal(ord);
    }

    // ── Fulfillment update ───────────────────────────────────────────────

    public async Task UpdateLineFulfillmentAsync(int lineId, decimal fulfilledFromStock, decimal fulfilledByPurchase, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            DECLARE @qty DECIMAL(18,4), @newStatus INT;
            SELECT @qty = [Quantity] FROM {_lineTable} WHERE [Id] = @LineId AND [CompanyId] = @CompanyId;
            SET @newStatus = CASE
                WHEN @qty IS NULL THEN 0
                WHEN (@FulfilledFromStock + @FulfilledByPurchase) >= @qty THEN 2
                WHEN (@FulfilledFromStock + @FulfilledByPurchase) > 0     THEN 1
                ELSE 0
            END;
            UPDATE {_lineTable}
               SET [FulfilledFromStock]  = @FulfilledFromStock,
                   [FulfilledByPurchase] = @FulfilledByPurchase,
                   [FulfillmentStatus]   = @newStatus
             WHERE [Id] = @LineId AND [CompanyId] = @CompanyId;
            """;
        cmd.Parameters.Add(new SqlParameter("@CompanyId", _connectionFactory.ResolveEffectiveCompanyId()));
        cmd.Parameters.Add(new SqlParameter("@LineId", lineId));
        cmd.Parameters.Add(new SqlParameter("@FulfilledFromStock",  fulfilledFromStock));
        cmd.Parameters.Add(new SqlParameter("@FulfilledByPurchase", fulfilledByPurchase));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Karşılama defteri (DocumentLineFulfillment) ──────────────────────
    //
    // Toplamlar (FulfilledFromStock/ByPurchase) artık TÜRETİLMİŞ değerdir: defterdeki aktif
    // satırların toplamı. Doğruluk kaynağı defter, DocumentLine kolonları ise okuma kolaylığı
    // için tutulan önbellektir. Bu yüzden her yazma/ters çevirme sonrası yeniden hesaplanır.

    public async Task<int> AddFulfillmentEntriesAsync(IReadOnlyCollection<FulfillmentEntry> entries, int? userId, CancellationToken ct)
    {
        if (entries is null || entries.Count == 0) return 0;

        var lineIds = entries.Select(e => e.RequestLineId).Distinct().ToList();

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx   = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            // 1) Defter satırlarını yaz
            await using (var insert = conn.CreateCommand())
            {
                insert.Transaction = tx;
                var values = new List<string>(entries.Count);
                var i = 0;
                foreach (var e in entries)
                {
                    values.Add($"(@Q{i}, @K{i}, @D{i}, @V{i}, @L{i}, @N{i}, 1, @User, SYSUTCDATETIME(), (SELECT dl.[CompanyId] FROM {_lineTable} dl WHERE dl.[Id] = @Q{i}))");
                    insert.Parameters.Add(new SqlParameter($"@Q{i}", e.RequestLineId));
                    insert.Parameters.Add(new SqlParameter($"@K{i}", (byte)e.Kind));
                    insert.Parameters.Add(new SqlParameter($"@D{i}", e.RefDocId));
                    insert.Parameters.Add(new SqlParameter($"@V{i}", e.Quantity));
                    insert.Parameters.Add(new SqlParameter($"@L{i}", (object?)e.RefDocLineId ?? DBNull.Value));
                    insert.Parameters.Add(new SqlParameter($"@N{i}", (object?)e.Notes ?? DBNull.Value));
                    i++;
                }
                insert.Parameters.Add(new SqlParameter("@User", (object?)userId ?? DBNull.Value));
                insert.CommandText = $"""
                    INSERT INTO {_fulfillmentTable}
                        ([RequestLineId], [FulfillmentType], [RefDocId], [Quantity], [RefDocLineId], [Notes], [IsActive], [CreatedById], [Created], [CompanyId])
                    VALUES {string.Join(",\n           ", values)};
                    """;
                await insert.ExecuteNonQueryAsync(ct);
            }

            // 2) Toplamları defterden yeniden hesapla (kapatmayı temizler — satır yeniden açılır)
            int affected;
            await using (var recalc = conn.CreateCommand())
            {
                recalc.Transaction = tx;
                recalc.CommandText = FulfillmentLedger.BuildRecalcSql(
                    _fulfillmentTable, _lineTable, lineIds, preserveClosed: false, out var ps);
                foreach (var p in ps) recalc.Parameters.Add(p);
                affected = await recalc.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            return affected;
        }
        catch
        {
            try { await tx.RollbackAsync(ct); } catch { /* bağlantı düşmüşse yut */ }
            throw;
        }
    }

    /// <summary>Bkz. IDocumentRepository.GetFulfillmentContributionByItemAsync KDoc'u (Madde 1).</summary>
    public async Task<IReadOnlyDictionary<int, (string? MaterialCode, string? MaterialName, decimal FloorQty)>>
        GetFulfillmentContributionByItemAsync(int refDocId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        return await FulfillmentLedger.GetContributionByItemAsync(conn, null, _schema, refDocId, ct);
    }

    /// <summary>Bkz. IDocumentRepository.GetFulfillmentAffectedLineIdsAsync KDoc'u (Madde 3).</summary>
    public async Task<IReadOnlyList<int>> GetFulfillmentAffectedLineIdsAsync(int refDocId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        return await FulfillmentLedger.GetAffectedLineIdsAsync(conn, null, _schema, refDocId, ct);
    }


    /// <summary>
    /// Satirlarin KALAN miktarini kapatir (FulfillmentStatus = 3). Karsilanan miktarlar korunur.
    /// Bkz. IDocumentRepository.CloseLineFulfillmentAsync KDoc'u.
    ///
    /// GUVENLIK/DOGRULUK: UPDATE yalnizca ihtiyac kaydi ("alis_talebi") belgelerinin satirlarini
    /// hedefler — bu metodun yanlislikla baska belge tipinin (siparis/irsaliye) satirini iptal
    /// etmesi mumkun degil. Id listesi PARAMETRELI IN ile gecirilir, SQL'e gomulmez.
    /// </summary>
    public async Task<int> CloseLineFulfillmentAsync(IReadOnlyCollection<int> lineIds, CancellationToken ct)
    {
        if (lineIds is null || lineIds.Count == 0) return 0;

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();

        var paramNames = new List<string>(lineIds.Count);
        var i = 0;
        foreach (var id in lineIds)
        {
            var p = "@L" + i++;
            paramNames.Add(p);
            cmd.Parameters.Add(new SqlParameter(p, id));
        }

        cmd.CommandText = $"""
            UPDATE l
               SET l.[FulfillmentStatus] = 3
              FROM {_lineTable} l
             INNER JOIN [{_schema}].[Document]     d  ON d.[Id]  = l.[DocumentId]
             INNER JOIN [{_schema}].[DocumentType] dt ON dt.[Id] = d.[DocumentTypeId]
             WHERE l.[Id] IN ({string.Join(",", paramNames)})
               AND dt.[Code] = 'alis_talebi';
            """;
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyDictionary<int, (int Count, decimal QtySum)>> GetDerivedLineAggregatesAsync(
        int documentId, CancellationToken ct)
    {
        // Bu belgenin satırlarına SourceLineId ile referans veren türetilmiş satırlar —
        // yalnızca AKTİF (IsActive=1) belgelerdekiler sayılır (soft-delete edilen türev
        // bağı serbest bırakır). IX_DocumentLine_SourceLineId index'i kullanılır.
        var map = new Dictionary<int, (int Count, decimal QtySum)>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT src.[Id], COUNT(dl.[Id]) AS Cnt, SUM(dl.[Quantity]) AS QtySum
            FROM {_lineTable} src
            INNER JOIN {_lineTable} dl ON dl.[SourceLineId] = src.[Id]
            INNER JOIN {_quoteTable} d ON d.[Id] = dl.[DocumentId] AND d.[IsActive] = 1
            WHERE src.[DocumentId] = @DocId
            GROUP BY src.[Id];
            """;
        cmd.Parameters.Add(new SqlParameter("@DocId", documentId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            map[r.GetInt32(0)] = (r.GetInt32(1), r.IsDBNull(2) ? 0m : r.GetDecimal(2));
        return map;
    }

    // ── Line Details (ozellik-deger aciklamalari) ────────────────────────

    public async Task<IReadOnlyCollection<DocumentLineDetail>> GetLineDetailsAsync(int documentLineId, CancellationToken ct)
    {
        var list = new List<DocumentLineDetail>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[QuoteLineId],[FeatureName],[ValueCode],[ValueName],[Description],[LineOrder]
            FROM {_detailTable} WHERE [QuoteLineId] = @LineId ORDER BY [LineOrder];
            """;
        cmd.Parameters.Add(new SqlParameter("@LineId", documentLineId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new DocumentLineDetail
            {
                Id = r.GetInt32(0),
                QuoteLineId = r.GetInt32(1),
                FeatureName = r.GetString(2),
                ValueCode = r.GetString(3),
                ValueName = r.GetString(4),
                Description = r.IsDBNull(5) ? null : r.GetString(5),
                LineOrder = r.GetInt32(6),
            });
        return list;
    }

    public async Task SaveLineDetailsAsync(int documentLineId, IReadOnlyCollection<DocumentLineDetail> details, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        var companyId = _connectionFactory.ResolveEffectiveCompanyId();
        await using (var del = conn.CreateCommand())
        {
            del.CommandText = $"DELETE FROM {_detailTable} WHERE [QuoteLineId] = @LineId AND [CompanyId] = @CompanyId;";
            del.Parameters.Add(new SqlParameter("@LineId", documentLineId));
            del.Parameters.Add(new SqlParameter("@CompanyId", companyId));
            await del.ExecuteNonQueryAsync(ct);
        }
        var order = 0;
        foreach (var d in details)
        {
            order++;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                INSERT INTO {_detailTable} ([QuoteLineId],[FeatureName],[ValueCode],[ValueName],[Description],[LineOrder],[CompanyId])
                VALUES (@LineId, @Feature, @ValCode, @ValName, @Desc, @Order,
                    (SELECT dl.[CompanyId] FROM {_lineTable} dl WHERE dl.[Id] = @LineId));
                """;
            cmd.Parameters.Add(new SqlParameter("@LineId", documentLineId));
            cmd.Parameters.Add(new SqlParameter("@Feature", d.FeatureName));
            cmd.Parameters.Add(new SqlParameter("@ValCode", d.ValueCode));
            cmd.Parameters.Add(new SqlParameter("@ValName", d.ValueName));
            cmd.Parameters.Add(new SqlParameter("@Desc", (object?)d.Description ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@Order", order));
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}
