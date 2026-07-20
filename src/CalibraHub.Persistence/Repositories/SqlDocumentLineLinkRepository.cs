using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

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
}
