using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>Hata kodu kataloğu servisi (ActivityReasonService deseni — kod isimden türetilir).</summary>
public interface IQualityDefectCodeService
{
    Task<IReadOnlyList<QualityDefectCodeDto>> ListAsync(byte? category, bool includeInactive, CancellationToken ct);
    Task<QualityDefectCodeDto?> GetAsync(int id, CancellationToken ct);
    Task<(bool Ok, string? Error, int Id)> SaveAsync(SaveQualityDefectCodeRequest request, int? userId, CancellationToken ct);
    Task<(bool Ok, string? Error)> DeleteAsync(int id, int? userId, CancellationToken ct);
}
