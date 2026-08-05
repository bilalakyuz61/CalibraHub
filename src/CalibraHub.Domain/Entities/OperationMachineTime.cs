using System.ComponentModel;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

[Description("Operasyon × Makine (veya makine grubu) bazlı üretim süresi. Bir operasyon farklı makinelerde/gruplarda farklı sürede tamamlanabilir. RoutingId opsiyonel: boş ise tüm rotalarda ortak, dolu ise yalnız o rotaya özel. MachineId XOR MachineGroupId (biri zorunlu, ikisi birden olmaz). ItemId/ItemGroupId opsiyonel ve karşılıklı dışlayıcı: dolu ise sadece o ürün/ürün grubu için özel süre; boş ise genel süre. UnitId yalnız ItemId doluyken geçerli (ürünün birim setinden bir birim).")]
public sealed class OperationMachineTime
{
    public int Id { get; init; }
    public int CompanyId { get; init; }
    public int OperationId { get; init; }
    /// <summary>Boş = tüm rotalarda ortak; dolu = yalnız bu rotaya özel süre.</summary>
    public int? RoutingId { get; init; }
    /// <summary>MachineGroupId ile karşılıklı dışlayıcı (XOR). Makine grubu seçildiyse null.</summary>
    public int? MachineId { get; init; }
    /// <summary>CardGroup (CardType=3) referansı. MachineId ile karşılıklı dışlayıcı (XOR).</summary>
    public int? MachineGroupId { get; init; }
    /// <summary>ItemGroupId ile karşılıklı dışlayıcı. Dolu ise ürüne özel süre.</summary>
    public int? ItemId { get; init; }
    /// <summary>CardGroup (CardType=1) referansı. ItemId ile karşılıklı dışlayıcı.</summary>
    public int? ItemGroupId { get; init; }
    /// <summary>Ürünün birim setinden (Items.UnitId veya ItemUnits) bir birim. Yalnız ItemId doluyken anlamlı.</summary>
    public int? UnitId { get; init; }
    /// <summary>Batch miktar — "Şu kadar birim için" (DurationPerUnit bu miktar için süre).</summary>
    public decimal Quantity { get; init; } = 1m;
    /// <summary>Quantity birimi için toplam süre. Birim/dakika hesabı: DurationPerUnit / Quantity.</summary>
    public decimal DurationPerUnit { get; init; }
    public DurationUnit DurationUnit { get; init; } = DurationUnit.Minute;
    /// <summary>Makine Planlama Faz 2 (2026-08-05) — operasyon setup (hazırlık) süresi, satırın
    /// kendi <see cref="DurationUnit"/>'i ile yorumlanır (ayrı birim yok). NULL/0 = setup yok.</summary>
    public decimal? SetupDuration { get; init; }
    /// <summary>Vardiya Senaryoları Faz 2 (2026-08-05) — bu operasyon×makine kombinasyonunun aynı
    /// anda kaç operatör tükettiği. Otomatik çizelgeleme personel sonlu-kapasite kısıtında kullanılır
    /// (bkz. <c>MachineAutoScheduleService.PlaceOnMachine</c>). Varsayılan 1.</summary>
    public byte RequiredOperators { get; init; } = 1;
    public bool IsActive { get; init; } = true;
    public DateTime Created { get; init; }
    public DateTime? Updated { get; init; }
}
