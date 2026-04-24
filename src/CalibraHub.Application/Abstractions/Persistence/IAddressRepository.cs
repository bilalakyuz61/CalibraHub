using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// PTT posta kodu verileri + cariye bagli teslim adresleri icin repository.
/// PostalLocality: read-heavy (dropdown cascade) — denormalize flat tablo.
/// ContactAddress: per-cari CRUD.
/// </summary>
public interface IAddressRepository
{
    // ── PostalLocality (read-only katalog) ──────────────────────────
    Task<IReadOnlyCollection<string>> GetCitiesAsync(string? countryCode, CancellationToken ct);
    Task<IReadOnlyCollection<string>> GetDistrictsAsync(string? countryCode, string cityName, CancellationToken ct);
    Task<IReadOnlyCollection<PostalLocality>> GetNeighborhoodsAsync(string? countryCode, string cityName, string districtName, CancellationToken ct);
    Task<PostalLocality?> FindByPostalCodeAsync(string postalCode, CancellationToken ct);
    Task<int> GetPostalLocalityCountAsync(CancellationToken ct);
    Task BulkInsertPostalLocalitiesAsync(IReadOnlyCollection<PostalLocality> rows, bool clearExisting, CancellationToken ct);

    // ── ContactAddress (CRUD per cari) ─────────────────────────────
    Task<IReadOnlyCollection<ContactAddress>> GetAddressesByContactAsync(int contactId, CancellationToken ct);
    Task<ContactAddress?> GetAddressByIdAsync(int id, CancellationToken ct);
    Task<int> AddAddressAsync(ContactAddress entity, CancellationToken ct);
    Task UpdateAddressAsync(ContactAddress entity, CancellationToken ct);
    Task DeleteAddressAsync(int id, CancellationToken ct);
    Task SetDefaultAddressAsync(int contactId, int addressId, CancellationToken ct);
}
