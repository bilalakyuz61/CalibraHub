using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Harici SQL bağlantı tanımlarının persistence'ı. Per-company şirket DB'sinde izole.
/// Parola şifreli saklanır; <see cref="SaveAsync"/>'e boş parola gelirse mevcut değer korunur.
/// </summary>
public interface IExternalDbConnectionRepository
{
    Task<IReadOnlyList<ExternalDbConnection>> ListAsync(bool includeInactive, CancellationToken ct);

    Task<ExternalDbConnection?> GetByIdAsync(int id, CancellationToken ct);

    Task<bool> NameExistsAsync(string name, int? excludeId, CancellationToken ct);

    /// <summary>
    /// Ekler/günceller ve Id döner. <paramref name="plainPassword"/> null/boş ise
    /// güncellemede mevcut parola korunur (istemci parolayı geri okumaz).
    /// </summary>
    Task<int> SaveAsync(ExternalDbConnection entity, string? plainPassword, CancellationToken ct);

    Task DeleteAsync(int id, CancellationToken ct);

    Task<bool> ToggleActiveAsync(int id, CancellationToken ct);
}
