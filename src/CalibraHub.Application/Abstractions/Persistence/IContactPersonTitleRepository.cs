using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// ContactPersonTitle (cariye bagli iletisim kisileri icin unvan lookup) repository.
/// Per-company DB'de yasar; pattern SqlContactPersonRepository ile birebir.
/// </summary>
public interface IContactPersonTitleRepository
{
    /// <summary>SortOrder, Name siralamasinda aktif unvanlar.</summary>
    Task<IReadOnlyList<ContactPersonTitle>> GetAllActiveAsync(CancellationToken ct);

    Task<ContactPersonTitle?> GetByIdAsync(int id, CancellationToken ct);

    /// <summary>Case-insensitive trim ile isim eslesmesi — inline "+ Yeni" dedup icin.</summary>
    Task<ContactPersonTitle?> GetByNameAsync(string name, CancellationToken ct);

    /// <summary>INSERT — yeni Id donderir (SCOPE_IDENTITY).</summary>
    Task<int> AddAsync(ContactPersonTitle entity, CancellationToken ct);

    /// <summary>
    /// Soft delete — IsActive=false. IsSystem=true olanlar (seed) pasiflestirilebilir
    /// ama kullanicidan gizlemek icin frontend secimi: silinemez badge'i goster.
    /// Backend yine de bu metodu cagirmayi reddetmez.
    /// </summary>
    Task DeleteAsync(int id, int? updatedById, CancellationToken ct);

    /// <summary>Bu unvan kac aktif ContactPerson tarafindan kullanildi? Silmeden once kontrol.</summary>
    Task<int> GetUsageCountAsync(int titleId, CancellationToken ct);
}
