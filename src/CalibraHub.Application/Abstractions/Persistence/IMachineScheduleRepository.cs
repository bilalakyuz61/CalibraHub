using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Makine Planlama (Üretim Çizelgeleme) — Faz 1 Manuel (2026-08-04). Bkz.
/// <c>MachineScheduleBlock</c> entity ve <c>MachineScheduleContracts.cs</c> XML doc'ları.
/// </summary>
public interface IMachineScheduleRepository
{
    /// <summary>Verilen UTC pencere için: aktif makineler, pencereyle kesişen aktif bloklar,
    /// henüz bloğu olmayan açık iş emri operasyonları (unplanned kuyruğu — pencereden bağımsız).</summary>
    Task<MachineScheduleDataDto> GetScheduleDataAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct);

    /// <summary>Blok oluştur/güncelle (Id&lt;=0 → yeni). Çakışma = aynı makinede zaman örtüşen
    /// DİĞER aktif bloklar; manuel planlamada çakışmaya rağmen kaydedilir, sonuçta uyarı olarak döner.</summary>
    Task<SaveScheduleBlockResult> SaveBlockAsync(SaveScheduleBlockRequest request, int? userId, CancellationToken ct);

    /// <summary>Soft-delete (IsActive=0).</summary>
    Task DeleteBlockAsync(int id, int? userId, CancellationToken ct);
}
