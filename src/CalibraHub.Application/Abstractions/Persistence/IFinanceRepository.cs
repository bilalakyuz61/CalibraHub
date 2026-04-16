using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IFinanceRepository
{
    Task<IReadOnlyCollection<ContactAccount>> GetContactAccountsAsync(byte? accountType, string? search, CancellationToken cancellationToken);
    Task<(IReadOnlyCollection<ContactAccount> Items, int TotalCount)> GetContactAccountsPagedAsync(byte? accountType, string? search, int offset, int pageSize, CancellationToken cancellationToken);
    Task<ContactAccount?> GetContactAccountByIdAsync(int id, CancellationToken cancellationToken);
    Task<bool> CodeExistsAsync(string code, int? excludeId, CancellationToken cancellationToken);
    Task<int> AddContactAccountAsync(ContactAccount account, CancellationToken cancellationToken);
    Task UpdateContactAccountAsync(ContactAccount account, CancellationToken cancellationToken);
    Task DeleteContactAccountAsync(int id, CancellationToken cancellationToken);
}
