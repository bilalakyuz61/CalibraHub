using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// ContactItem (cari × stok eslestirmesi) icin repository.
/// JOIN ile Items.Code/Name dolduran "list row" projection'i ekran tarafi icindir.
/// </summary>
public interface IContactItemRepository
{
    Task<IReadOnlyCollection<ContactItemListRow>> GetByContactAsync(int contactId, CancellationToken ct);

    Task<int> AddAsync(ContactItem entity, CancellationToken ct);

    Task UpdateAsync(ContactItem entity, CancellationToken ct);

    Task DeleteAsync(int id, CancellationToken ct);
}

/// <summary>
/// Liste ekranina dondurulen satir — ContactItem alanlari + JOIN'le gelen Item kod/adi.
/// </summary>
public sealed record ContactItemListRow(
    int Id,
    int ContactId,
    int ItemId,
    string ItemCode,
    string ItemName,
    string? VendorCode,
    string? VendorName,
    string? Notes,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
