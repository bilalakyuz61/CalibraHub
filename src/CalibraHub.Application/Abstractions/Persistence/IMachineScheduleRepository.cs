using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Makine Planlama (Üretim Çizelgeleme) — Faz 1 Manuel (2026-08-04). Bkz.
/// <c>MachineScheduleBlock</c> entity ve <c>MachineScheduleContracts.cs</c> XML doc'ları.
/// </summary>
public interface IMachineScheduleRepository
{
    /// <summary>Verilen UTC pencere için: aktif makineler, pencereyle kesişen aktif bloklar,
    /// henüz bloğu olmayan açık iş emri operasyonları (unplanned kuyruğu — pencereden bağımsız).
    /// <paramref name="scenarioId"/> (Vardiya Senaryoları, 2026-08-05) Gantt gölgeleme verisi
    /// (WorkWindows) için hangi senaryonun kullanılacağını belirler; null ise varsayılan senaryo.</summary>
    Task<MachineScheduleDataDto> GetScheduleDataAsync(DateTime fromUtc, DateTime toUtc, int? scenarioId, CancellationToken ct);

    /// <summary>Blok oluştur/güncelle (Id&lt;=0 → yeni). Çakışma = aynı makinede zaman örtüşen
    /// DİĞER aktif bloklar; manuel planlamada çakışmaya rağmen kaydedilir, sonuçta uyarı olarak döner.</summary>
    Task<SaveScheduleBlockResult> SaveBlockAsync(SaveScheduleBlockRequest request, int? userId, CancellationToken ct);

    /// <summary>Soft-delete (IsActive=0).</summary>
    Task DeleteBlockAsync(int id, int? userId, CancellationToken ct);

    /// <summary>Blok Kilitle + Yeniden Çizelgele (2026-08-22) — YALNIZ Status alanını değiştirir
    /// (start/end/tip dokunulmaz). Setup child (ParentBlockId dolu) bloklar hariç tutulur — elle
    /// kilitlenemez/durum değiştirilemez, parent bloğuyla birlikte hareket eder. Bulunamayan/aktif
    /// olmayan/setup-child id için false döner (no-op, exception fırlatmaz).</summary>
    Task<bool> SetBlockStatusAsync(int id, byte status, int? userId, CancellationToken ct);

    /// <summary>Toplu durum değişimi — <see cref="SetBlockStatusAsync"/> ile AYNI kural (setup
    /// child'lar hariç). Döner: fiilen güncellenen blok sayısı.</summary>
    Task<int> BulkSetBlockStatusAsync(IReadOnlyList<int> ids, byte status, int? userId, CancellationToken ct);

    /// <summary>Kapasite/Yük Raporu (backend, 2026-08-22) — verilen UTC aralığıyla kesişen aktif
    /// bloklar, yalnız MachineId/StartUtc/EndUtc/BlockType (join'siz, hafif — <see cref="GetScheduleDataAsync"/>'in
    /// aksine WorkOrder/Item/Operation etiketleri taşımaz). Per-company güvenliği Machine.CompanyId
    /// join'i ile sağlanır.</summary>
    Task<IReadOnlyList<CapacityBlockDto>> ListBlocksForCapacityReportAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct);
}
