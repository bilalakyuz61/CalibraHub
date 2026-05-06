using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Sirket bazli parametre repository — CompanyParameter tablosu.
/// Tum metotlar mevcut request'in CompanyId'sine gore filtre uygular (per-company isolation).
/// </summary>
public interface ICompanyParameterRepository
{
    Task<IReadOnlyCollection<CompanyParameter>> GetAllAsync(CancellationToken ct);

    Task<IReadOnlyCollection<CompanyParameter>> GetByFormAsync(string formCode, CancellationToken ct);

    Task<CompanyParameter?> GetAsync(string formCode, string paramKey, CancellationToken ct);

    /// <summary>UPSERT — varsa guncelle, yoksa ekle. Donen Id satirin PK'si.</summary>
    Task<int> UpsertAsync(string formCode, string paramKey, string? paramValue, CompanyParameterDataType dataType, int? updatedBy, CancellationToken ct);

    Task DeleteAsync(string formCode, string paramKey, CancellationToken ct);
}
