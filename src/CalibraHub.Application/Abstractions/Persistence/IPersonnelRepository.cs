using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IPersonnelRepository
{
    /// <summary>Şirket personnel listesi (filtre: pasifler, sadece operatörler).</summary>
    Task<IReadOnlyCollection<PersonnelDto>> ListAsync(bool includeInactive, bool onlyOperators, CancellationToken ct);

    Task<PersonnelDto?> GetAsync(int id, CancellationToken ct);

    Task<int> SaveAsync(Personnel entity, CancellationToken ct);

    Task DeleteAsync(int id, CancellationToken ct);

    /// <summary>
    /// Shop-floor giriş — PIN veya NFC kart numarası ile aktif üretim operatörünü bulur.
    /// İkisinden biri verilmelidir; ikisi de boşsa NULL döner. Sadece IsActive + IsProductionOperator true olanlar.
    /// </summary>
    Task<PersonnelDto?> GetByPinOrCardAsync(string? pinCode, string? cardNo, CancellationToken ct);
}
