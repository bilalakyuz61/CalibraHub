using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Lookup Fonksiyonu yonetimi (admin paneli CRUD).
/// Mapping engine'in kullandigi `IIntegrationLookupFunctionRegistry` ile ayni DB tablosunu paylasir;
/// admin save sonrasi registry cache'i invalidate edilir.
/// </summary>
public interface IIntegrationLookupFunctionAdminService
{
    Task<IReadOnlyCollection<IntegrationLookupFunctionAdminDto>> GetAllAsync(bool includeInactive, CancellationToken ct);
    Task<IntegrationLookupFunctionAdminDto?> GetByIdAsync(int id, CancellationToken ct);
    Task<(bool Success, string? Error, int? Id)> CreateAsync(SaveIntegrationLookupFunctionRequest req, int? userId, CancellationToken ct);
    Task<(bool Success, string? Error)> UpdateAsync(SaveIntegrationLookupFunctionRequest req, int? userId, CancellationToken ct);
    Task<(bool Success, string? Error)> DeleteAsync(int id, int? userId, CancellationToken ct);

    /// <summary>
    /// Per-company DB'de tanimli SQL fonksiyonlarini listeler (sys.objects type IN ('FN','IF','TF')).
    /// Admin Edit ekranindaki "SQL Fonksiyonu" dropdown'i bunu kullanir.
    /// 3 parametre kabul edenler oncelikli; diger fonksiyonlar da listeye girer ama UI'da uyari verir.
    /// </summary>
    Task<IReadOnlyCollection<AvailableDbFunctionDto>> ListAvailableDbFunctionsAsync(CancellationToken ct);
}
