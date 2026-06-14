using System.ComponentModel;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

[Description("Stok hareketleri — cikis/giris/transfer/duzeltme. RefType+RefId ile kaynak belgeye baglanir (WORK_ORDER, SALES_ORDER, PURCHASE, MANUAL). Faz 0 cekirdek; rezervasyon ve detayli akis Faz 4'te.")]
public sealed class StockMovement
{
    public int Id { get; init; }
    public int CompanyId { get; init; }
    public StockMovementType MovementType { get; init; }
    public int ItemId { get; init; }
    public int? ConfigId { get; init; }
    public decimal Quantity { get; init; }
    public int? UnitId { get; init; }
    public int? LocationId { get; init; }
    public required string RefType { get; init; }
    public int? RefId { get; init; }
    public int? RefLineId { get; init; }
    public DateTime MovementDate { get; init; }
    public string? BatchNo { get; init; }
    public string? LotNo { get; init; }
    public int? CreatedById { get; init; }
    public DateTime? Created { get; init; }
}
