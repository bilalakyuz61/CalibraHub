using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Lokasyon Tanımlamaları grubu erişimi (per-company DB): Bölüm → Alt Bölüm.
/// Ad benzersizliği ve silme guard'ları repository'de — ihlallerde Türkçe
/// mesajlı InvalidOperationException.
/// </summary>
public interface ILocationSectionRepository
{
    Task<IReadOnlyList<LocationSectionDto>> ListSectionsAsync(CancellationToken ct);
    Task<LocationSectionDto?> GetSectionAsync(int id, CancellationToken ct);
    Task<int> SaveSectionAsync(int? id, string name, int? userId, CancellationToken ct);
    Task DeleteSectionAsync(int id, CancellationToken ct);

    Task<IReadOnlyList<LocationSubSectionDto>> ListSubSectionsAsync(int sectionId, CancellationToken ct);
    Task<IReadOnlyList<LocationSubSectionListDto>> ListAllSubSectionsAsync(CancellationToken ct);
    Task<LocationSubSectionDto?> GetSubSectionAsync(int id, CancellationToken ct);
    Task<int> SaveSubSectionAsync(int? id, int sectionId, string name, int? userId, CancellationToken ct);
    Task DeleteSubSectionAsync(int id, CancellationToken ct);
}
