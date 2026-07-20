using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// DocumentLineLink persistence — FAZ 0 İSKELET (2026-07-20, bkz. tasarım dökümanı
/// "Birleşik Kalem-Eşleşme Tablosu (DocumentLineLink)"). Bu sınıf DI'a kayıtlıdır ama
/// HİÇBİR servis/controller tarafından çağrılmıyor; tablo şu an için "ölü" (davranış
/// değişmez). Faz 1'de dual-write adaptörü bağlandığında InsertAsync mevcut üç mekanizmanın
/// (SourceLineId / WorkOrderSource / DocumentLineFulfillment) yazdığı her noktaya paralel
/// eklenecek — o zamana kadar metod gövdeleri kasıtlı olarak basit tutuldu (tek SQL ifadesi,
/// açık transaction gerektirmez).
///
/// SqlWorkOrderRepository ile aynı bağlantı deseni (per-company connection factory, SELECT/
/// INSERT/UPDATE parametreli) + FulfillmentLedger ile aynı ters-çevirme deseni (tür filtresi
/// yok, yalnız hedef Id ile pasifleştirme) izlenir. Bu tabloda CompanyId kolonu YOKTUR —
/// per-company DB mimarisi gereği bağlantının kendisi zaten ilgili şirkete çözülür.
///
/// FAZ 1a EKLEMESİ (2026-07-20): <see cref="GetFloorComponentsAsync"/> ve
/// <see cref="GetFloorComponentsForDocumentAsync"/> okuma-only metotları eklendi — bunlar da
/// hiçbir servis/controller tarafından henüz çağrılmıyor, yalnız Faz 1b'de çapraz-doğrulama
/// (link'ten hesaplanan floor == eski ComputeLineFloor) ve Faz 2'de gerçek okuma geçişi için
/// hazırlandı. InsertAsync/GetBySourceLineAsync/ReverseByTargetAsync Faz 0'dan değişmedi.
/// </summary>
public sealed class SqlDocumentLineLinkRepository : IDocumentLineLinkRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _table;

    public SqlDocumentLineLinkRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options)
    {
        _connectionFactory = factory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        var s = schema.Replace("]", "]]");
        _table = $"[{s}].[DocumentLineLink]";
    }

    public async Task InsertAsync(DocumentLineLinkEntry entry, int? userId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {_table}
                ([LinkType],[SourceLineId],[SourceDocId],[TargetLineId],[TargetDocId],[TargetWorkOrderId],
                 [Quantity],[Notes],[IsActive],[CreatedById],[Created])
            VALUES
                (@LinkType,@SourceLineId,@SourceDocId,@TargetLineId,@TargetDocId,@TargetWorkOrderId,
                 @Quantity,@Notes,1,@CreatedById,SYSUTCDATETIME());
            """;
        cmd.Parameters.AddWithValue("@LinkType", (byte)entry.LinkType);
        cmd.Parameters.AddWithValue("@SourceLineId", entry.SourceLineId);
        cmd.Parameters.AddWithValue("@SourceDocId", entry.SourceDocId);
        cmd.Parameters.AddWithValue("@TargetLineId", (object?)entry.TargetLineId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@TargetDocId", (object?)entry.TargetDocId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@TargetWorkOrderId", (object?)entry.TargetWorkOrderId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Quantity", entry.Quantity);
        cmd.Parameters.AddWithValue("@Notes", (object?)entry.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CreatedById", (object?)userId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<DocumentLineLink>> GetBySourceLineAsync(int sourceLineId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[LinkType],[SourceLineId],[SourceDocId],[TargetLineId],[TargetDocId],[TargetWorkOrderId],
                   [Quantity],[Notes],[IsActive],[CreatedById],[Created],[UpdatedById],[Updated]
              FROM {_table}
             WHERE [SourceLineId] = @SourceLineId
               AND [IsActive] = 1
             ORDER BY [Id];
            """;
        cmd.Parameters.AddWithValue("@SourceLineId", sourceLineId);

        var list = new List<DocumentLineLink>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new DocumentLineLink
            {
                Id = r.GetInt32(0),
                LinkType = r.GetByte(1),
                SourceLineId = r.GetInt32(2),
                SourceDocId = r.GetInt32(3),
                TargetLineId = r.IsDBNull(4) ? null : r.GetInt32(4),
                TargetDocId = r.IsDBNull(5) ? null : r.GetInt32(5),
                TargetWorkOrderId = r.IsDBNull(6) ? null : r.GetInt32(6),
                Quantity = r.GetDecimal(7),
                Notes = r.IsDBNull(8) ? null : r.GetString(8),
                IsActive = r.GetBoolean(9),
                CreatedById = r.IsDBNull(10) ? null : r.GetInt32(10),
                Created = r.GetDateTime(11),
                UpdatedById = r.IsDBNull(12) ? null : r.GetInt32(12),
                Updated = r.IsDBNull(13) ? null : r.GetDateTime(13),
            });
        }
        return list;
    }

    public async Task<int> ReverseByTargetAsync(int targetDocId, int? userId, CancellationToken ct)
    {
        if (targetDocId <= 0) return 0;

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_table}
               SET [IsActive] = 0,
                   [UpdatedById] = @UpdatedById,
                   [Updated] = SYSUTCDATETIME()
             WHERE [IsActive] = 1
               AND [TargetDocId] = @TargetDocId;
            """;
        cmd.Parameters.AddWithValue("@TargetDocId", targetDocId);
        cmd.Parameters.AddWithValue("@UpdatedById", (object?)userId ?? DBNull.Value);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── FAZ 1a (2026-07-20): floor-okuma — tasarım §5, çapraz-doğrulama için ────────────
    // Bu iki metot yalnız OKUR; hiçbir yazma yolu yok, hiçbir servis/controller henüz çağırmıyor.

    public async Task<(decimal Consumed, decimal Derived, decimal WorkOrder)> GetFloorComponentsAsync(int sourceLineId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        // Üç bileşen TEK sorguda: karşılama kovası (1-7), dönüşüm zinciri (10), iş emri
        // tahsisi (20). LinkType 21/22 (üretim sarf/mamul) bilinçli olarak hariç — sipariş
        // floor'una girmez (tasarım §4). GROUP BY yok -> agregat SUM'lar hiç satır
        // eşleşmese bile NULL döner, aşağıda 0'a çevrilir.
        cmd.CommandText = $"""
            SELECT
                SUM(CASE WHEN [LinkType] IN (1,2,3,4,5,6,7) THEN [Quantity] ELSE 0 END) AS Consumed,
                SUM(CASE WHEN [LinkType] = 10 THEN [Quantity] ELSE 0 END) AS Derived,
                SUM(CASE WHEN [LinkType] = 20 THEN [Quantity] ELSE 0 END) AS WorkOrder
              FROM {_table}
             WHERE [SourceLineId] = @SourceLineId
               AND [IsActive] = 1;
            """;
        cmd.Parameters.AddWithValue("@SourceLineId", sourceLineId);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (await r.ReadAsync(ct))
        {
            var consumed = r.IsDBNull(0) ? 0m : r.GetDecimal(0);
            var derived = r.IsDBNull(1) ? 0m : r.GetDecimal(1);
            var workOrder = r.IsDBNull(2) ? 0m : r.GetDecimal(2);
            return (consumed, derived, workOrder);
        }
        return (0m, 0m, 0m);
    }

    public async Task<IReadOnlyDictionary<int, (decimal Consumed, decimal Derived, decimal WorkOrder)>> GetFloorComponentsForDocumentAsync(int documentId, CancellationToken ct)
    {
        var map = new Dictionary<int, (decimal Consumed, decimal Derived, decimal WorkOrder)>();

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        // GetFloorComponentsAsync ile aynı üç bileşen, belge bazında TOPLU: N satırlık
        // doküman için N ayrı sorgu yerine tek sorgu (SqlWorkOrderRepository.
        // GetAllocatedQuantitiesForDocumentAsync ile aynı N+1-önleme deseni). SourceDocId
        // denormalize kolonu üzerinden filtrelenip SourceLineId'ye göre gruplanır.
        cmd.CommandText = $"""
            SELECT [SourceLineId],
                   SUM(CASE WHEN [LinkType] IN (1,2,3,4,5,6,7) THEN [Quantity] ELSE 0 END) AS Consumed,
                   SUM(CASE WHEN [LinkType] = 10 THEN [Quantity] ELSE 0 END) AS Derived,
                   SUM(CASE WHEN [LinkType] = 20 THEN [Quantity] ELSE 0 END) AS WorkOrder
              FROM {_table}
             WHERE [SourceDocId] = @SourceDocId
               AND [IsActive] = 1
             GROUP BY [SourceLineId];
            """;
        cmd.Parameters.AddWithValue("@SourceDocId", documentId);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var sourceLineId = r.GetInt32(0);
            var consumed = r.IsDBNull(1) ? 0m : r.GetDecimal(1);
            var derived = r.IsDBNull(2) ? 0m : r.GetDecimal(2);
            var workOrder = r.IsDBNull(3) ? 0m : r.GetDecimal(3);
            map[sourceLineId] = (consumed, derived, workOrder);
        }
        return map;
    }
}
