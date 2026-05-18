using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// BodyTemplate CRUD — endpoint body schema icin hazır JSON sablonlari.
/// </summary>
public interface IBodyTemplateRepository
{
    /// <summary>Tum aktif sablonlari listele. category/provider/search opsiyonel filtre.</summary>
    Task<IReadOnlyCollection<BodyTemplate>> ListAsync(
        string? category, string? provider, string? search, CancellationToken ct);

    Task<BodyTemplate?> GetByIdAsync(int id, CancellationToken ct);

    /// <summary>Sablon kullanildiginda popularite sayacini artir.</summary>
    Task IncrementUsageAsync(int id, CancellationToken ct);

    /// <summary>Yeni kullanici sablonu olusturur (IsBuiltIn=0). Id doner.</summary>
    Task<int> AddAsync(BodyTemplate template, CancellationToken ct);

    /// <summary>
    /// Kullanici sablonunu siler. IsBuiltIn=1 (sistem) sablonlari silinmez —
    /// repo InvalidOperationException atar. Sistem sablonu pasif yapilmak istenirse
    /// admin DB'den IsActive=0 ile manuel.
    /// </summary>
    Task DeleteAsync(int id, CancellationToken ct);
}
