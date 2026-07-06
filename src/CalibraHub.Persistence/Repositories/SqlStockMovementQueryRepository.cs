using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// Malzeme kartı stok hareketleri sorgusu. Kaynak: Document + DocumentLine
/// (MovementType dolu satırlar). Yön/işaret hangi lokasyon alanının dolu olduğuna göre
/// belirlenir — SqlInventoryCountRepository bakiye formülüyle birebir tutarlı:
///   itemin toplam bakiyesine katkı = (ToLocationId dolu ? +Qty : 0) − (FromLocationId dolu ? +Qty : 0)
///   → Giriş(Receipt) +Qty, Çıkış(Issue) −Qty, Transfer 0, Düzeltme(Adjust) ±Qty.
/// Koşan bakiye tüm geçmiş üzerinden kronolojik hesaplanır; filtreler yalnızca gösterimi
/// daraltır (her satır gerçek bakiyesini korur).
/// </summary>
public sealed class SqlStockMovementQueryRepository : IStockMovementQueryRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _schema;

    public SqlStockMovementQueryRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
    }

    private string T(string table) => $"[{_schema}].[{table}]";

    private static string LabelFor(byte mt) => mt switch
    {
        1 => "Çıkış",
        2 => "Giriş",
        3 => "Transfer",
        4 => "Düzeltme",
        _ => "—",
    };

    private static string? LocLabel(string? code, string? name)
    {
        var c = string.IsNullOrWhiteSpace(code) ? null : code!.Trim();
        var n = string.IsNullOrWhiteSpace(name) ? null : name!.Trim();
        if (c is not null && n is not null) return $"{c} - {n}";
        return n ?? c;
    }

    public async Task<ItemStockMovementResultDto> ListForItemAsync(ItemStockMovementFilter filter, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();

        var raw = new List<Raw>();
        await using (var conn = await _connectionFactory.OpenConnectionAsync(ct))
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT
                    l.[Id], d.[Id] AS DocumentId, d.[DocumentNumber], d.[DocumentDate],
                    dt.[Code] AS DocTypeCode, dt.[Name] AS DocTypeName,
                    l.[MovementType], l.[Quantity], u.[Code] AS UnitCode,
                    l.[FromLocationId], fl.[LocationCode] AS FromLocCode, fl.[LocationName] AS FromLocName,
                    l.[LocationId],     tl.[LocationCode] AS ToLocCode,   tl.[LocationName] AS ToLocName,
                    l.[UnitCost], l.[LotNo], l.[Notes],
                    cfg.[RecordCode] AS CombinationCode,
                    usr.[FullName] AS CreatedByName
                FROM {T("DocumentLine")} l
                INNER JOIN {T("Document")} d        ON d.[Id]  = l.[DocumentId]
                LEFT  JOIN {T("DocumentType")} dt   ON dt.[Id] = d.[DocumentTypeId]
                LEFT  JOIN {T("Unit")} u            ON u.[Id]  = l.[UnitId]
                LEFT  JOIN {T("Location")} fl       ON fl.[Id] = l.[FromLocationId]
                LEFT  JOIN {T("Location")} tl       ON tl.[Id] = l.[LocationId]
                LEFT  JOIN {T("ItemConfiguration")} cfg ON cfg.[Id] = l.[CombinationId]
                LEFT  JOIN {T("Users")} usr         ON usr.[Id] = d.[CreatedById]
                WHERE l.[ItemId] = @ItemId
                  AND l.[MovementType] IS NOT NULL
                  AND d.[CompanyId] = @CompanyId
                  AND d.[IsActive] = 1
                ORDER BY d.[DocumentDate] ASC, l.[Id] ASC;
                """;
            cmd.Parameters.AddWithValue("@ItemId", filter.ItemId);
            cmd.Parameters.AddWithValue("@CompanyId", companyId);

            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var mt = r.IsDBNull(6) ? (byte)0 : r.GetByte(6);
                var qty = r.GetDecimal(7);
                int? fromLoc = r.IsDBNull(9) ? null : r.GetInt32(9);
                int? toLoc = r.IsDBNull(12) ? null : r.GetInt32(12);
                // İşaret: hangi lokasyon dolu → giriş/çıkış (bakiye formülüyle tutarlı)
                var signed = (toLoc.HasValue ? qty : 0m) - (fromLoc.HasValue ? qty : 0m);

                raw.Add(new Raw
                {
                    LineId = r.GetInt32(0),
                    DocumentId = r.GetInt32(1),
                    DocumentNumber = r.IsDBNull(2) ? "" : r.GetString(2),
                    MovementDate = r.GetDateTime(3),
                    DocTypeCode = r.IsDBNull(4) ? null : r.GetString(4),
                    DocTypeName = r.IsDBNull(5) ? null : r.GetString(5),
                    MovementType = mt,
                    Quantity = qty,
                    UnitCode = r.IsDBNull(8) ? null : r.GetString(8),
                    FromLocationId = fromLoc,
                    FromLocationCode = r.IsDBNull(10) ? null : r.GetString(10),
                    FromLocationName = r.IsDBNull(11) ? null : r.GetString(11),
                    ToLocationId = toLoc,
                    ToLocationCode = r.IsDBNull(13) ? null : r.GetString(13),
                    ToLocationName = r.IsDBNull(14) ? null : r.GetString(14),
                    UnitCost = r.IsDBNull(15) ? null : r.GetDecimal(15),
                    LotNo = r.IsDBNull(16) ? null : r.GetString(16),
                    Notes = r.IsDBNull(17) ? null : r.GetString(17),
                    CombinationCode = r.IsDBNull(18) ? null : r.GetString(18),
                    CreatedByName = r.IsDBNull(19) ? null : r.GetString(19),
                    SignedDelta = signed,
                });
            }
        }

        // 1) Koşan bakiye — tüm geçmiş, kronolojik (artan)
        var running = 0m;
        foreach (var row in raw)
        {
            running += row.SignedDelta;
            row.RunningBalance = running;
        }
        var currentBalance = running; // son (en yeni) hareketin bakiyesi = gerçek güncel stok

        // 2) Filtre dropdown'u — tüm hareketlerde geçen benzersiz lokasyonlar
        var locMap = new Dictionary<int, string>();
        foreach (var row in raw)
        {
            if (row.FromLocationId is int fid && !locMap.ContainsKey(fid))
                locMap[fid] = LocLabel(row.FromLocationCode, row.FromLocationName) ?? fid.ToString();
            if (row.ToLocationId is int tid && !locMap.ContainsKey(tid))
                locMap[tid] = LocLabel(row.ToLocationCode, row.ToLocationName) ?? tid.ToString();
        }
        var locations = locMap
            .Select(kv => new StockMovementLocationDto(kv.Key, kv.Value))
            .OrderBy(l => l.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        // 3) Gösterim filtreleri (bakiye zaten hesaplandı — sadece hangi satırlar görünecek)
        IEnumerable<Raw> q = raw;
        if (filter.FromDate is DateTime fromD)
            q = q.Where(x => x.MovementDate.Date >= fromD.Date);
        if (filter.ToDate is DateTime toD)
            q = q.Where(x => x.MovementDate.Date <= toD.Date);
        if (filter.MovementType is byte mtf and >= 1 and <= 4)
            q = q.Where(x => x.MovementType == mtf);
        if (filter.LocationId is int locF and > 0)
            q = q.Where(x => x.FromLocationId == locF || x.ToLocationId == locF);

        var filtered = q.ToList();

        // 4) Özet — filtrelenmiş (gösterilen) satırlar üzerinden
        var totalIn = filtered.Where(x => x.SignedDelta > 0).Sum(x => x.Quantity);
        var totalOut = filtered.Where(x => x.SignedDelta < 0).Sum(x => x.Quantity);

        // 5) DTO — yeni→eski göster (her satır gerçek koşan bakiyesini korur)
        var rows = filtered
            .OrderByDescending(x => x.MovementDate)
            .ThenByDescending(x => x.LineId)
            .Select(x => new ItemStockMovementRowDto(
                x.LineId, x.DocumentId, x.DocumentNumber, x.MovementDate,
                x.DocTypeCode, x.DocTypeName, x.MovementType, LabelFor(x.MovementType),
                x.Quantity, x.SignedDelta, x.RunningBalance, x.UnitCode,
                x.FromLocationId, x.FromLocationCode, x.FromLocationName,
                x.ToLocationId, x.ToLocationCode, x.ToLocationName,
                x.UnitCost, x.LotNo, x.CombinationCode, x.Notes, x.CreatedByName))
            .ToList();

        return new ItemStockMovementResultDto(rows, locations, totalIn, totalOut, currentBalance, rows.Count);
    }

    private sealed class Raw
    {
        public int LineId;
        public int DocumentId;
        public string DocumentNumber = "";
        public DateTime MovementDate;
        public string? DocTypeCode;
        public string? DocTypeName;
        public byte MovementType;
        public decimal Quantity;
        public string? UnitCode;
        public int? FromLocationId;
        public string? FromLocationCode;
        public string? FromLocationName;
        public int? ToLocationId;
        public string? ToLocationCode;
        public string? ToLocationName;
        public decimal? UnitCost;
        public string? LotNo;
        public string? Notes;
        public string? CombinationCode;
        public string? CreatedByName;
        public decimal SignedDelta;
        public decimal RunningBalance;
    }
}
