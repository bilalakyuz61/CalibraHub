using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>İçe aktarım şablonu CRUD. Impl: SqlImportTemplateRepository (per-company DB).</summary>
public interface IImportTemplateRepository
{
    Task<IReadOnlyList<ImportTemplate>> ListAsync(bool includeInactive, CancellationToken ct);
    Task<ImportTemplate?> GetByIdAsync(int id, CancellationToken ct);
    /// <summary>Aynı isimde aktif başka şablon var mı? (kendisi hariç).</summary>
    Task<bool> NameExistsAsync(string name, int? excludeId, CancellationToken ct);
    Task<int> SaveAsync(ImportTemplate entity, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
    /// <summary>Aktif/Pasif çevir; yeni durumu döner.</summary>
    Task<bool> ToggleActiveAsync(int id, CancellationToken ct);
}
