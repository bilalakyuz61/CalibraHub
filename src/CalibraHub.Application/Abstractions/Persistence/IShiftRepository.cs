using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IShiftRepository
{
    Task<IReadOnlyList<ShiftDto>> ListAsync(bool includeInactive, CancellationToken ct);
    Task<ShiftDto?> GetAsync(int id, CancellationToken ct);
    /// <summary>
    /// UPSERT shift + opsiyonel breaks replace. breaks=null → aralara dokunma; aksi halde
    /// shift'in tüm aktif araları silinip yeni liste yazılır (idempotent değiştirme).
    /// </summary>
    Task<int> SaveAsync(Shift entity, IReadOnlyList<ShiftBreak>? breaks, CancellationToken ct);
    Task DeleteAsync(int id, int? userId, CancellationToken ct);
}

public interface IShiftAssignmentRepository
{
    /// <summary>Bir personelin tüm aktif atamaları (7 güne kadar).</summary>
    Task<IReadOnlyList<ShiftAssignmentDto>> GetByPersonnelAsync(int personnelId, CancellationToken ct);

    /// <summary>Bir vardiyaya bağlı tüm aktif atamalar.</summary>
    Task<IReadOnlyList<ShiftAssignmentDto>> GetByShiftAsync(int shiftId, CancellationToken ct);

    Task<ShiftAssignmentDto?> GetAsync(int id, CancellationToken ct);
    Task<int> SaveAsync(ShiftAssignment entity, CancellationToken ct);
    Task DeleteAsync(int id, int? userId, CancellationToken ct);

    /// <summary>
    /// Belirli bir personelin verilen gün için aktif atamasını döner (yoksa null).
    /// ShopFloor terminal başlığında o anki vardiyayı göstermek için kullanılır.
    /// </summary>
    Task<ShiftAssignmentDto?> GetCurrentAsync(int personnelId, DateOnly date, CancellationToken ct);
}
