using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IActivityReasonRepository
{
    /// <summary>
    /// Tüm sebep tanımları. activityType verildiyse o tipe filtreli (ShopFloor için).
    /// includeInactive=false ise yalnız aktifler.
    /// </summary>
    Task<IReadOnlyList<ActivityReasonDto>> ListAsync(
        WorkOrderActivityType? activityType, bool includeInactive, CancellationToken ct);

    Task<ActivityReasonDto?> GetAsync(int id, CancellationToken ct);

    /// <summary>UPSERT — Id=0/null → INSERT, dolu → UPDATE. Code uniqueness service'te.</summary>
    Task<int> SaveAsync(ActivityReason entity, CancellationToken ct);

    /// <summary>Soft delete (IsActive=0). Activity tablosundan referans varsa fiziksel silme yapılmaz.</summary>
    Task DeleteAsync(int id, int? userId, CancellationToken ct);
}
