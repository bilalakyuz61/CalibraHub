using CalibraHub.Application.Abstractions.DesignProvider;
using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// DocLayoutRule tablosuna erişim. Hiyerarşik kural eşleşmesi + IsDefault
/// fallback'i için tek noktadan veri erişimi sağlar.
/// </summary>
public interface IDocLayoutRuleRepository
{
    /// <summary>
    /// Verilen bağlama en spesifik kuralı bulur (kriter ağırlık toplamı en yüksek).
    /// Bulunamazsa <c>null</c> döner.
    /// </summary>
    Task<int?> FindBestMatchAsync(DesignSelectionContext ctx, CancellationToken ct);

    /// <summary>
    /// Fallback: DocLayout.IsDefault flag'ine göre belge tipi için varsayılan
    /// tasarımı bulur. Hiç IsDefault yoksa en güncel aktif layout'a düşer.
    /// </summary>
    Task<int?> FindDefaultAsync(string docType, CancellationToken ct);

    /// <summary>
    /// Belirtilen DocType için aktif kuralları (kriter alanlarıyla) listeler.
    /// IDesignProvider'ın memory-cache stratejisi tarafından kullanılır:
    /// bütün kural listesi RAM'e alınır, eşleşme/ağırlık hesabı C# tarafında yapılır.
    /// </summary>
    Task<IReadOnlyList<DocLayoutRuleMatchRow>> ListActiveByDocTypeAsync(string docType, CancellationToken ct);

    // ── Yönetim CRUD ─────────────────────────────────────────────────────────

    /// <summary>Aktif kural listesi (admin grid için).</summary>
    Task<IReadOnlyCollection<DocLayoutRuleDto>> ListAllAsync(CancellationToken ct);

    /// <summary>Tek kuralı getirir.</summary>
    Task<DocLayoutRuleDto?> GetByIdAsync(int id, CancellationToken ct);

    /// <summary>
    /// Insert/Update. UNIQUE INDEX ihlali yakalanırsa <c>InvalidOperationException</c>
    /// fırlatılır ("Aynı kombinasyonda kural zaten var").
    /// </summary>
    Task<int> UpsertAsync(SaveDocLayoutRuleRequest req, CancellationToken ct);

    /// <summary>IsActive = 0 yapar.</summary>
    Task SoftDeleteAsync(int id, CancellationToken ct);
}
