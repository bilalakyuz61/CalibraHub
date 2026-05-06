using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Sirket parametre okuma/yazma servisi. Tipli okuma helper'lari + List/Set/Delete.
/// Calling code'da hep bu servis kullanilir (repository dogrudan kullanilmaz).
/// </summary>
public interface ICompanyParameterService
{
    Task<IReadOnlyCollection<CompanyParameterDto>> ListAsync(string? formCode, CancellationToken ct);

    Task<string?> GetStringAsync(string formCode, string paramKey, CancellationToken ct);

    Task<int?> GetIntAsync(string formCode, string paramKey, CancellationToken ct);

    Task<bool?> GetBoolAsync(string formCode, string paramKey, CancellationToken ct);

    Task<DateTime?> GetDateAsync(string formCode, string paramKey, CancellationToken ct);

    Task SetAsync(SetCompanyParameterRequest request, CancellationToken ct);

    Task DeleteAsync(string formCode, string paramKey, CancellationToken ct);
}
