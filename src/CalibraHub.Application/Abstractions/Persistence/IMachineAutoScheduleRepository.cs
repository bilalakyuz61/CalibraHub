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

    /// <summary>Bir operasyon için <c>OperationMachineTime</c>'daki aday makineler (grup satırları
    /// hariç, distinct MachineId, aktif). WorkOrderOperation.MachineId NULL ise motor bu listeden seçer.</summary>
    Task<IReadOnlyList<int>> GetCandidateMachineIdsAsync(int operationId, CancellationToken ct);

    /// <summary><paramref name="fromUtc"/>'den sonra biten tüm aktif bloklar (tüm makineler) —
    /// motorun kapasite kısıtı (mevcut Kilitli/Onaylı/manuel Planlı bloklar sabit kalır).</summary>
    Task<IReadOnlyList<OccupiedBlockDto>> GetOccupiedBlocksAsync(DateTime fromUtc, CancellationToken ct);

    /// <summary>Apply — hesaplanan blokları toplu kalıcı hale getirir (tek transaction, atomik).
    /// Her operasyon grubunda ÖNCE Production segmentleri eklenir (ilk eklenenin Id'si "ilk üretim
    /// segmenti" olarak anılır), SONRA Setup segmentleri o Id'yi ParentBlockId olarak alır (Faz 2
    /// pattern'iyle tutarlı — bkz. SqlMachineScheduleRepository.CreateSetupChildIfNeededAsync).
    /// Döner: toplam oluşturulan blok sayısı (setup+production).</summary>
    Task<int> InsertBlocksAsync(IReadOnlyList<PlannedOperationBlocksDto> operations, int? userId, CancellationToken ct);
}
