namespace CalibraHub.Application.Contracts;

/// <summary>
/// Makine Planlama — Kapasite / Yük Raporu (backend, 2026-08-22). API sözleşmesi KİLİTLİ
/// (bkz. görev notu) — frontend (Views/Production/CapacityLoad.cshtml + ısı haritası bileşenleri)
/// bu DTO'lara göre yazılıyor, alan adı/tipini değiştirme.
///
/// <para><b>Hesaplama özeti:</b> Tüm kova/gün sınırları YEREL takvim (<c>TimeZoneInfo.Local</c>,
/// Faz 3 motoruyla aynı desen); bloklar UTC tutulur, yerelde kırpılır. Kapasite = makinenin o
/// yerel gün için MachineWorkWindow toplamı (hiç penceresi yoksa 7/24=1440), resmi tatilde 0,
/// üstüne binen Bakım(3)/Duruş(4) bloklarının o güne düşen dakikaları düşülür (0'ın altına
/// inmez). Yük = Üretim(1)+Hazırlık(2) bloklarının o güne düşen dakikaları. Kova = kovadaki
/// günlerin toplamı. <c>UtilizationPct</c> yalnız kapasite&gt;0 iken hesaplanır, aksi halde null
/// (frontend "kapasite yok" olarak yorumlar) — kapasite 0 iken yük varsa da null döner.</para>
/// </summary>
public sealed record CapacityLoadBucketDto(
    string Key,
    string Label,
    DateTime StartUtc,
    DateTime EndUtc);

public sealed record CapacityLoadCellDto(
    string BucketKey,
    int CapacityMinutes,
    int LoadMinutes,
    int? UtilizationPct,
    int OverloadMinutes);

public sealed record CapacityLoadMachineDto(
    int MachineId,
    string MachineName,
    IReadOnlyList<CapacityLoadCellDto> Cells);

public sealed record CapacityLoadResultDto(
    string Bucket,
    IReadOnlyList<CapacityLoadBucketDto> Buckets,
    IReadOnlyList<CapacityLoadMachineDto> Machines);

/// <summary>Kapasite/Yük Raporu için hafif blok okuma sözleşmesi — <c>ScheduleBlockDto</c>'nun
/// aksine WorkOrder/Item/Operation join'leri taşımaz (rapor yalnız MachineId/zaman/tip kullanır).</summary>
public sealed record CapacityBlockDto(
    int MachineId,
    DateTime StartUtc,
    DateTime EndUtc,
    byte BlockType);
