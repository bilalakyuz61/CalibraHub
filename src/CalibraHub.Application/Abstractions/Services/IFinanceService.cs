using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface IFinanceService
{
    Task<IReadOnlyCollection<ContactDto>> GetContactsAsync(byte? accountType, string? search, CancellationToken cancellationToken);
    Task<(IReadOnlyCollection<ContactDto> Items, int TotalCount)> GetContactsPagedAsync(byte? accountType, string? search, int offset, int pageSize, CancellationToken cancellationToken);
    Task<ContactDto?> GetContactByIdAsync(int id, CancellationToken cancellationToken);
    Task<(bool Success, string? Error, ContactDto? Account)> UpsertContactAsync(SaveContactRequest request, CancellationToken cancellationToken);
    Task<(bool Success, string? Error)> DeleteContactAsync(int id, CancellationToken cancellationToken);
}
