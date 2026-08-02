using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// DÖF (CAPA) kaydı veri erişimi. Document shell service tarafında IDocumentRepository ile
/// yazılır; bu repo companion (Capa) + aksiyon satırlarını yönetir. Stok repo'suna referans yok.
/// </summary>
public interface ICapaRepository
{
    Task<IReadOnlyCollection<CapaListItemDto>> ListAsync(string? search, byte? status, CancellationToken ct);
    Task<Capa?> GetByDocumentIdAsync(int documentId, CancellationToken ct);
    Task<CapaDetailDto?> GetDetailAsync(int documentId, CancellationToken ct);
    Task UpsertAsync(Capa capa, IReadOnlyList<CapaAction> actions, CancellationToken ct);

    /// <summary>Yalnız companion Status/ClosedAt alanlarını günceller (ChangeStatus).</summary>
    Task UpdateStatusAsync(int documentId, byte status, DateTime? closedAt, int? userId, CancellationToken ct);

    Task<IReadOnlyCollection<CapaPersonnelOption>> GetPersonnelOptionsAsync(CancellationToken ct);

    /// <summary>KPI panosu agregasyonu — salt-okunur, tüm zaman (trend son 12 ay). Enum bucket'ları
    /// (ByStatus/ByType/BySeverity) TÜM enum değerlerini içerir (0 sayım dahil, dağılım grafiği tam olsun).</summary>
    Task<CapaKpiDto> GetKpiAsync(CancellationToken ct);
}
