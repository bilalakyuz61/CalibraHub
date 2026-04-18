using System.ComponentModel;

namespace CalibraHub.Domain.Enums;

/// <summary>
/// Cari hesap tipi. Contact tablosundaki AccountType kolonunun tip-guvenli karsiligi.
/// DB'de byte (TINYINT) olarak saklanir.
/// </summary>
public enum ContactType : byte
{
    [Description("Musteri")]
    Customer = 1,

    [Description("Satici")]
    Supplier = 2,

    [Description("Hem musteri hem satici")]
    Both = 3,
}
