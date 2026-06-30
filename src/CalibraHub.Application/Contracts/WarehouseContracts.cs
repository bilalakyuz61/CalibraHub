namespace CalibraHub.Application.Contracts;

// ── Belge başlığı DTO ─────────────────────────────────────────────────────────
public sealed record StockDocDto(
    int Id,
    int CompanyId,
    string DocType,
    string DocNo,
    DateTime DocDate,
    int? FromLocationId,
    string? FromLocationName,
    int? ToLocationId,
    string? ToLocationName,
    string? RefNo,
    string? Notes,
    int? CreatedById,
    DateTime Created,
    bool IsActive,
    int LineCount,
    int? ArgeProjectId,
    string? ArgeProjectName);

// ── Kalem DTO ─────────────────────────────────────────────────────────────────
public sealed record StockDocLineDto(
    int Id,
    int DocId,
    int LineNo,
    int ItemId,
    string? MaterialCode,
    string? MaterialName,
    int? UnitId,
    string? UnitCode,
    decimal Qty,
    int? CombinationId,
    string? CombinationCode,
    string? Notes,
    int? FromLocationId,
    string? FromLocationName,
    int? ToLocationId,
    string? ToLocationName,
    decimal? UnitCost,
    string? LotNo = null);

// ── Kayıt istekleri ───────────────────────────────────────────────────────────
public sealed record SaveStockDocRequest(
    int? Id,
    string DocType,
    string? DocNo,
    DateTime DocDate,
    int? FromLocationId,
    int? ToLocationId,
    string? RefNo,
    string? Notes,
    IReadOnlyList<SaveStockDocLineRequest> Lines,
    int? ArgeProjectId);

public sealed record SaveStockDocLineRequest(
    int? Id,
    int? ItemId,
    string? MaterialCode,
    string? MaterialName,
    int? UnitId,
    decimal Qty,
    int? CombinationId,
    string? Notes,
    int? FromLocationId,
    int? ToLocationId,
    decimal? UnitCost,
    string? LotNo = null);
