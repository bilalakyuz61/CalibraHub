using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// ContactPerson (cariye bagli iletisim kisileri) repository.
/// SqlContactItemRepository pattern'i ile birebir: schema-aware, ADO.NET, soft delete.
/// </summary>
public interface IContactPersonRepository
{
    /// <summary>Verilen cari icin aktif (IsActive=1) kisileri doner; IsPrimary desc + Title asc sirasi.</summary>
    Task<IReadOnlyList<ContactPerson>> GetByContactIdAsync(int contactId, CancellationToken ct);

    Task<ContactPerson?> GetByIdAsync(int id, CancellationToken ct);

    /// <summary>INSERT — yeni Id donderir (SCOPE_IDENTITY).</summary>
    Task<int> AddAsync(ContactPerson entity, CancellationToken ct);

    Task UpdateAsync(ContactPerson entity, CancellationToken ct);

    /// <summary>Soft delete: IsActive=0, Updated/UpdatedBy guncellenir.</summary>
    Task DeleteAsync(int id, int? deletedById, CancellationToken ct);

    /// <summary>
    /// Bu cari + unvan kombinasyonuyla zaten aktif bir kayit var mi?
    /// Save duplicate check icin. excludeId verilirse o kaydi dislar (kendi updatesi sayilmaz).
    /// </summary>
    Task<bool> ExistsByContactAndTitleAsync(int contactId, int titleId, int? excludeId, CancellationToken ct);
}
