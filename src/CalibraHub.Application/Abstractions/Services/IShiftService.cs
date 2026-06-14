using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface IShiftService
{
    Task<IReadOnlyList<ShiftDto>> ListAsync(bool includeInactive, CancellationToken ct);
    Task<ShiftDto?> GetAsync(int id, CancellationToken ct);
    /// <summary>UPSERT. Aktif Code uniqueness service'te kontrol edilir.</summary>
    Task<int> SaveAsync(SaveShiftRequest request, int? userId, CancellationToken ct);
    Task DeleteAsync(int id, int? userId, CancellationToken ct);
}

public interface IShiftAssignmentService
{
    Task<IReadOnlyList<ShiftAssignmentDto>> GetByPersonnelAsync(int personnelId, CancellationToken ct);
    Task<IReadOnlyList<ShiftAssignmentDto>> GetByShiftAsync(int shiftId, CancellationToken ct);
    Task<ShiftAssignmentDto?> GetAsync(int id, CancellationToken ct);
    Task<int> SaveAsync(SaveShiftAssignmentRequest request, int? userId, CancellationToken ct);
    Task DeleteAsync(int id, int? userId, CancellationToken ct);
    Task<ShiftAssignmentDto?> GetCurrentAsync(int personnelId, DateOnly date, CancellationToken ct);
}
