using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// DocumentNumberRule CRUD — Tasarım Kuralı (DocLayoutRule) pattern'inin tıpkısı.
/// Ağırlık (Weight) save/update sırasında otomatik hesaplanır (Cari=16+Grup=8+...).
/// </summary>
public interface IDocumentNumberRuleRepository
{
    Task<IReadOnlyCollection<DocumentNumberRule>> ListAsync(CancellationToken ct);
    Task<DocumentNumberRule?> GetAsync(int id, CancellationToken ct);

    /// <summary>Insert (Id=0) veya Update — Weight otomatik recompute edilir.</summary>
    Task<int> SaveAsync(DocumentNumberRule rule, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);

    /// <summary>Bir kuralın anlık sayaç state'lerini listele (admin debug için).</summary>
    Task<IReadOnlyCollection<DocumentNumberCounter>> GetCountersAsync(int ruleId, CancellationToken ct);

    /// <summary>Bir kuralın belirli dönem sayacını sıfırla (admin restart).</summary>
    Task ResetCounterAsync(int ruleId, string resetKey, CancellationToken ct);
}
