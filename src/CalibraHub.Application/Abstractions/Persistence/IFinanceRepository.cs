using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IFinanceRepository
{
    Task<IReadOnlyCollection<Contact>> GetContactsAsync(byte? accountType, string? search, CancellationToken cancellationToken);
    Task<(IReadOnlyCollection<Contact> Items, int TotalCount)> GetContactsPagedAsync(byte? accountType, string? search, int offset, int pageSize, CancellationToken cancellationToken);
    Task<Contact?> GetContactByIdAsync(int id, CancellationToken cancellationToken);
    Task<bool> CodeExistsAsync(string code, int? excludeId, CancellationToken cancellationToken);
    Task<int> AddContactAsync(Contact account, CancellationToken cancellationToken);
    Task UpdateContactAsync(Contact account, CancellationToken cancellationToken);
    Task DeleteContactAsync(int id, CancellationToken cancellationToken);
}
