using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Makine Planlama Faz 3 (2026-08-05) — otomatik çizelgeleme veri katmanı. Motor hesaplaması
/// (yerleştirme algoritması) <c>MachineAutoScheduleService</c>'te; bu repo yalnız okuma/yazma.
/// Bkz. <c>MachineAutoScheduleContracts.cs</c> XML doc'ları.
/// </summary>
public interface IMachineAutoScheduleRepository
{
    /// <summary>Aday iş emirleri: en az 1 planlanmamış (aktif <c>MachineScheduleBlock</c>'u olmayan)
    /// operasyonu olan açık/yayımlanmış (Released/InProgress) iş emirleri. Priority DESC,
    /// PlannedEndDate ASC (NULL en sonda) sıralı; TotalMinutes = planlanmamış op'ların
    /// süre+setup toplamı (resolver zinciri ile).</summary>
    Task<IReadOnlyList<AutoScheduleCandidateWorkOrderDto>> GetCandidateWorkOrdersAsync(CancellationToken ct);

    /// <summary>Seçili iş emirlerinin planlanmamış operasyonları (motor girdisi) — WorkOrderId,
    /// Sequence sıralı (intra-WO precedence sırası).</summary>
    Task<IReadOnlyList<AutoScheduleOperationInputDto>> GetUnplannedOperationsAsync(
        IReadOnlyList<int> workOrderIds, CancellationToken ct);

    /// <summary>Blok Kilitle + Yeniden Çizelgele (2026-08-22) — yeniden çizelgeleme motor girdisi:
    /// TÜM açık iş emirlerinin (Released/InProgress, IsActive) Kilitli(2)/Onaylı(3) aktif bloğu
    /// OLMAYAN operasyonları (planlanmamış + yalnız-Planlı). <c>GetUnplannedOperationsAsync</c> ile
    /// AYNI SELECT/ORDER BY (wo.Id tiebreaker dahil), yalnız NOT EXISTS koşulunun Status filtresi
    /// farklıdır. İş emri filtresi/opt-out listesi YOK (kullanıcı kararı).</summary>
    Task<IReadOnlyList<AutoScheduleOperationInputDto>> GetReschedulableOperationsAsync(CancellationToken ct);

    /// <summary>Bir operasyon için <c>OperationMachineTime</c>'daki aday makineler (grup satırları
    /// hariç, distinct MachineId, aktif). WorkOrderOperation.MachineId NULL ise motor bu listeden seçer.</summary>
    Task<IReadOnlyList<int>> GetCandidateMachineIdsAsync(int operationId, CancellationToken ct);

    /// <summary><paramref name="fromUtc"/>'den sonra biten aktif bloklar (tüm makineler) — motorun
    /// kapasite kısıtı. <paramref name="lockedConfirmedOnly"/>=false (Faz 3 Otomatik Yerleştir): TÜM
    /// aktif bloklar (mevcut Kilitli/Onaylı/Planlı bloklar sabit kalır). <paramref name="lockedConfirmedOnly"/>=true
    /// (Yeniden Çizelgele, 2026-08-22): yalnız Status IN (Locked=2, Confirmed=3) — Planlı(1) bloklar
    /// serbest bırakılacağı için kapasite kısıtına dahil EDİLMEZ.</summary>
    Task<IReadOnlyList<OccupiedBlockDto>> GetOccupiedBlocksAsync(DateTime fromUtc, bool lockedConfirmedOnly, CancellationToken ct);

    /// <summary>Apply — hesaplanan blokları toplu kalıcı hale getirir (tek transaction, atomik).
    /// Her operasyon grubunda ÖNCE Production segmentleri eklenir (ilk eklenenin Id'si "ilk üretim
    /// segmenti" olarak anılır), SONRA Setup segmentleri o Id'yi ParentBlockId olarak alır (Faz 2
    /// pattern'iyle tutarlı — bkz. SqlMachineScheduleRepository.CreateSetupChildIfNeededAsync).
    /// Döner: toplam oluşturulan blok sayısı (setup+production).</summary>
    Task<int> InsertBlocksAsync(IReadOnlyList<PlannedOperationBlocksDto> operations, int? userId, CancellationToken ct);

    /// <summary>Blok Kilitle + Yeniden Çizelgele (2026-08-22) — Önizleme'nin persist ETMEYEN sayımı:
    /// Uygula çağrılırsa <see cref="ApplyRescheduleAsync"/>'in soft-delete edeceği "serbest set"
    /// (Kilitli/Onaylı bloğu OLMAYAN açık-WO operasyonlarına ait aktif Status=Planned bloklar)
    /// sayısı — <see cref="ApplyRescheduleAsync"/> ile AYNI predikat.</summary>
    Task<int> CountReleasableBlocksAsync(CancellationToken ct);

    /// <summary>Yeniden çizelgeleme Uygula — TEK transaction, atomik: (a) "serbest set"in (Kilitli/Onaylı
    /// bloğu OLMAYAN açık-WO operasyonlarına ait) aktif Status=Planned blokları soft-delete edilir —
    /// bu, aynı WorkOrderOperationId'yi paylaşan setup child'ları da kapsar (setup child'lar ayrıca
    /// ParentBlockId ile filtrelenmez, WorkOrderOperationId bazlı silinir); (b) <paramref name="operations"/>
    /// Status=Planned olarak <see cref="InsertBlocksAsync"/> ile AYNI mantıkla yazılır. Kilitli/Onaylı
    /// bloğu olan operasyonun HİÇBİR bloğu silinmez. Döner: (oluşturulan blok sayısı, serbest bırakılan
    /// blok sayısı).</summary>
    Task<(int CreatedCount, int ReleasedCount)> ApplyRescheduleAsync(
        IReadOnlyList<PlannedOperationBlocksDto> operations, int? userId, CancellationToken ct);
}
