using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IFinanceRepository
{
    Task<IReadOnlyCollection<Contact>> GetContactsAsync(byte? accountType, string? search, CancellationToken cancellationToken);
    Task<(IReadOnlyCollection<Contact> Items, int TotalCount)> GetContactsPagedAsync(byte? accountType, string? search, int offset, int pageSize, CancellationToken cancellationToken);
    Task<Contact?> GetContactByIdAsync(int id, CancellationToken cancellationToken);
    /// <summary>AccountCode ile cari bul (case-insensitive). Bulunamazsa null doner.</summary>
    Task<Contact?> GetContactByCodeAsync(string code, CancellationToken cancellationToken);
    Task<bool> CodeExistsAsync(string code, int? excludeId, CancellationToken cancellationToken);
    Task<int> AddContactAsync(Contact account, CancellationToken cancellationToken);
    Task UpdateContactAsync(Contact account, CancellationToken cancellationToken);
    Task DeleteContactAsync(int id, CancellationToken cancellationToken);

    // Cari ↔ Fiyat Grubu eslestirme (1 cari → 0..1 fiyat grubu).
    // priceGroupId NULL = eslestirme kaldirilir.
    Task UpdateContactPriceGroupAsync(int contactId, int? priceGroupId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<Contact>> GetContactsByPriceGroupAsync(int priceGroupId, CancellationToken cancellationToken);
}
