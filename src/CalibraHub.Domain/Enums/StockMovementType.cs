using System.ComponentModel;

namespace CalibraHub.Domain.Enums;

/// <summary>StockMovement.MovementType — stok hareketi yonu/turu.</summary>
public enum StockMovementType : byte
{
    [Description("Cikis (uretim sarfi, satis irsaliye, vb.)")]
    Issue = 1,

    [Description("Giris (uretim cikti, satin alma irsaliye, vb.)")]
    Receipt = 2,

    [Description("Transfer (depo/lokasyon arasi)")]
    Transfer = 3,

    [Description("Duzeltme (sayim farki)")]
    Adjust = 4,
}
