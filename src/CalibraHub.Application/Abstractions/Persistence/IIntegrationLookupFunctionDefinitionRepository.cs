using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Admin panelinin DB-tabanli "Lookup Fonksiyonu" yonetimi icin repository.
/// Registry servisi de bu repository uzerinden cache'lenmis okuma yapar.
/// </summary>
public interface IIntegrationLookupFunctionDefinitionRepository
{
    /// <summary>Aktif/pasif tum kayitlari getir (kolonlari ile birlikte).</summary>
    Task<IReadOnlyList<IntegrationLookupFunctionDefinition>> GetAllAsync(bool includeInactive, CancellationToken ct);

    Task<IntegrationLookupFunctionDefinition?> GetByIdAsync(int id, CancellationToken ct);

    /// <summary>Code uniqueness check (Save oncesi). Mevcut Id verirsen onu hesaba katar.</summary>
    Task<bool> CodeExistsAsync(string code, int? excludeId, CancellationToken ct);

    Task<int> InsertAsync(IntegrationLookupFunctionDefinition entity, int? userId, CancellationToken ct);
    Task UpdateAsync(IntegrationLookupFunctionDefinition entity, int? userId, CancellationToken ct);

    /// <summary>Soft delete — IsActive=0. Hard delete admin override icin DeleteHard.</summary>
    Task SoftDeleteAsync(int id, int? userId, CancellationToken ct);
    Task DeleteHardAsync(int id, CancellationToken ct);

    /// <summary>
    /// Per-company DB'deki tum scalar/TVF fonksiyonlari listeler (sys.objects + sys.parameters).
    /// Admin "SQL Fonksiyonu" dropdown'i icin. Schema-qualified ad + tip + parametre sayisi doner.
    /// </summary>
    Task<IReadOnlyList<AvailableDbFunctionDto>> ListAvailableFunctionsAsync(CancellationToken ct);
}
