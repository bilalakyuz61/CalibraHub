using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface IFinanceService
{
    Task<IReadOnlyCollection<ContactAccountDto>> GetContactAccountsAsync(byte? accountType, string? search, CancellationToken cancellationToken);
    Task<(IReadOnlyCollection<ContactAccountDto> Items, int TotalCount)> GetContactAccountsPagedAsync(byte? accountType, string? search, int offset, int pageSize, CancellationToken cancellationToken);
    Task<ContactAccountDto?> GetContactAccountByIdAsync(int id, CancellationToken cancellationToken);
    Task<(bool Success, string? Error, ContactAccountDto? Account)> UpsertContactAccountAsync(SaveContactAccountRequest request, CancellationToken cancellationToken);
    Task<(bool Success, string? Error)> DeleteContactAccountAsync(int id, CancellationToken cancellationToken);
}
