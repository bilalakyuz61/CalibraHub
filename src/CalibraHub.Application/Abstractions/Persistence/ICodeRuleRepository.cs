using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Cari/Stok kod türetme kuralı CRUD + sayaç state operasyonları.
/// EntityType: "Contact" | "Item".
/// </summary>
public interface ICodeRuleRepository
{
    /// <summary>EntityType'a göre tüm kurallar (admin liste için, IsActive dahil).</summary>
    Task<IReadOnlyList<CodeRule>> ListAsync(string entityType, CancellationToken ct);

    /// <summary>ID ile tek kural + şartları yükle.</summary>
    Task<CodeRule?> GetAsync(int id, CancellationToken ct);

    /// <summary>Insert (Id=0) veya Update. Conditions tam değiştirilir (delete+reinsert).</summary>
    Task<int> SaveAsync(CodeRule rule, CancellationToken ct);

    Task DeleteAsync(int id, CancellationToken ct);

    /// <summary>Aktif kurallar Priority DESC sırasında — runtime için.</summary>
    Task<IReadOnlyList<CodeRule>> GetActiveByEntityAsync(string entityType, CancellationToken ct);

    /// <summary>
    /// Sayacı atomik olarak +1 yap. Counter satırı yoksa CounterStart-1 ile oluşturur.
    /// Return: yeni current value (kullanılacak sayı).
    /// </summary>
    Task<long> IncrementCounterAsync(int ruleId, string resetKey, long startValue, CancellationToken ct);

    /// <summary>Kuralın anlık sayaç state'leri (admin debug).</summary>
    Task<IReadOnlyList<CodeRuleCounter>> GetCountersAsync(int ruleId, CancellationToken ct);

    /// <summary>Belirli dönem sayacını 0'a sıfırla.</summary>
    Task ResetCounterAsync(int ruleId, string resetKey, CancellationToken ct);
}
