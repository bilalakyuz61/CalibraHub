using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Abstractions.Services;

public interface IActivityReasonService
{
    Task<IReadOnlyList<ActivityReasonDto>> ListAsync(
        WorkOrderActivityType? activityType, bool includeInactive, CancellationToken ct);

    Task<ActivityReasonDto?> GetAsync(int id, CancellationToken ct);

    /// <summary>UPSERT. Aynı (ActivityType, Code) aktif kayıt varsa hata.</summary>
    Task<int> SaveAsync(SaveActivityReasonRequest request, int? userId, CancellationToken ct);

    Task DeleteAsync(int id, int? userId, CancellationToken ct);
}
