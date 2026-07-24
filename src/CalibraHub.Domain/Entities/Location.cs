using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

[Description("Depo ve raf lokasyonlari. Kendi uzerinde self-reference (ParentId) ile hiyerarsi: Depo > Kat > Kor > Raf > Goz. LocationTypeCode ile tip ayrimi.")]
public sealed class Location
{
    public int Id { get; init; }
    public int? ParentId { get; init; }
    public required string LocationTypeCode { get; init; }
    public required string LocationCode { get; init; }
    public string? LocationName { get; init; }
    public int SortOrder { get; init; }
    public decimal? MaxWeightCapacity { get; init; }
    public decimal? VolumeCapacity { get; init; }
    public bool IsActive { get; init; } = true;
    public bool IsMachinePark { get; init; }
    public bool IsStorageArea { get; init; }

    /// <summary>
    /// Sayım referansı: true ise sayım (envanter) sırasında bu lokasyonun altındaki alt
    /// kırılımlar ayrı ayrı değil, bu lokasyon üzerinden (toplama noktası olarak) sayılır.
    /// Genellikle "bölüm" (SECTION) gibi konteyner tipi lokasyonlarda anlamlıdır.
    /// NOT: İş kuralı uygulaması (sayım davranışı) hiç yapılmadı; bu alan yalnız taşınır.
    /// 2026-07-25: UI'dan kaldırıldı (LocationTree.jsx) — kolon geriye-uyumluluk için kalır,
    /// yeni kayıtlarda her zaman false, mevcut kayıtlarda güncellemede korunur (bkz.
    /// LogisticsConfigurationService.UpdateLocationAsync).
    /// </summary>
    public bool IsCountReference { get; init; }

    /// <summary>
    /// Alt kırılımlar tek türde olmalı: eskiden bu alan true ise bu lokasyonun doğrudan alt
    /// kırılımlarının aynı LocationTypeCode'a sahip olması opsiyonel olarak zorunlu kılınıyordu.
    /// 2026-07-25: kullanıcı kararıyla bu davranış artık BAYRAKSIZ, koşulsuz genel kural oldu —
    /// her parent'ın altındaki aktif kardeşler her zaman tek tip olmak zorunda (bkz.
    /// LogisticsConfigurationService.ValidateSingleChildTypeConstraint). Bu alan artık kural
    /// katmanında OKUNMUYOR; UI'dan da kaldırıldı — yalnızca geriye-uyumluluk için kolon/alan
    /// olarak taşınır, güncellemede mevcut değeri korunur (yeni kayıtta her zaman false).
    /// </summary>
    public bool IsSingleChildType { get; init; }

    /// <summary>
    /// Depo bazında eksi bakiye izni (üç durumlu): null = Stok parametresindeki varsayılanı
    /// devral, true = bu depoda eksi bakiyeye izin ver, false = engelle. Yalnızca şirket ana
    /// anahtarı (NEG_BALANCE_CONTROL) açıkken dikkate alınır.
    /// </summary>
    public bool? AllowNegativeBalance { get; init; }
}
