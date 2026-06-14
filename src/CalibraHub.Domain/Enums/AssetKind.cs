using System.ComponentModel;

namespace CalibraHub.Domain.Enums;

/// <summary>
/// Varlık türü. <see cref="Machine"/> türündeki varlıklar Makine modülünden
/// projeksiyonla gelen / lazy-materialize edilen kayıtlardır (MachineId dolu).
/// Diğer türler bağımsız (standalone) varlıklardır.
/// </summary>
public enum AssetKind : byte
{
    [Description("Ekipman")]
    Equipment = 0,

    [Description("Makine")]
    Machine = 1,

    [Description("Ölçüm Cihazı")]
    Instrument = 2,

    [Description("Araç")]
    Vehicle = 3,

    [Description("El Aleti")]
    Tool = 4,

    [Description("Aparat / Kalıp")]
    Fixture = 5,

    // ── BT / Donanım varlıkları (IP / işletim sistemi / hostname / MAC girilebilir) ──
    [Description("Bilgisayar")]
    Computer = 10,

    [Description("Sunucu")]
    Server = 11,

    [Description("Cep Telefonu")]
    MobilePhone = 12,

    [Description("Tablet")]
    Tablet = 13,

    [Description("Ağ Cihazı")]
    NetworkDevice = 14,

    [Description("Yazıcı")]
    Printer = 15,

    [Description("Diğer")]
    Other = 9,
}

/// <summary>BT/donanım varlık türleri için yardımcı — IP/OS/hostname alanları yalnız bunlarda anlamlı.</summary>
public static class AssetKindExtensions
{
    public static bool IsItAsset(this AssetKind kind) => kind is
        AssetKind.Computer or AssetKind.Server or AssetKind.MobilePhone or
        AssetKind.Tablet or AssetKind.NetworkDevice or AssetKind.Printer;
}
