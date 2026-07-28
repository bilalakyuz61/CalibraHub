using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>Hata kodu kataloğu veri erişimi (ActivityReason deseni).</summary>
public interface IQualityDefectCodeRepository
{
    Task<IReadOnlyList<QualityDefectCodeDto>> ListAsync(byte? category, bool includeInactive, CancellationToken ct);
    Task<QualityDefectCodeDto?> GetAsync(int id, CancellationToken ct);
    Task<int> SaveAsync(QualityDefectCode entity, CancellationToken ct);
    Task SoftDeleteAsync(int id, int? userId, CancellationToken ct);
}
