using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface IFinanceService
{
    Task<IReadOnlyCollection<ContactDto>> GetContactsAsync(byte? accountType, string? search, CancellationToken cancellationToken);
    Task<(IReadOnlyCollection<ContactDto> Items, int TotalCount)> GetContactsPagedAsync(byte? accountType, string? search, int offset, int pageSize, CancellationToken cancellationToken);
    Task<ContactDto?> GetContactByIdAsync(int id, CancellationToken cancellationToken);
    /// <summary>AccountCode ile cari bul (case-insensitive). Bulunamazsa null doner. DocumentEdit cari kod input'unda direkt yazinca lookup icin.</summary>
    Task<ContactDto?> GetContactByCodeAsync(string code, CancellationToken cancellationToken);
    Task<(bool Success, string? Error, ContactDto? Account)> UpsertContactAsync(SaveContactRequest request, CancellationToken cancellationToken);
    Task<(bool Success, string? Error)> DeleteContactAsync(int id, CancellationToken cancellationToken);

    // Cari ↔ Fiyat Grubu eslestirme (1 cari → 0..1 fiyat grubu).
    Task<(bool Success, string? Error)> SetContactPriceGroupAsync(int contactId, int? priceGroupId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ContactDto>> GetContactsByPriceGroupAsync(int priceGroupId, CancellationToken cancellationToken);
}
