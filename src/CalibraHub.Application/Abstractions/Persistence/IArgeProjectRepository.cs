using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// AR-GE companion (ArgeProject) veri erisimi. AR-GE projesinin Document shell'i
/// IDocumentRepository ile yazilir; bu repo yalnizca companion'i (Name/Status/Owner/
/// Target/Progress) ve liste projeksiyonunu yonetir. Soft-delete Document.IsActive'dedir
/// (companion silinmez), bu yuzden burada Delete yoktur.
/// </summary>
public interface IArgeProjectRepository
{
    /// <summary>ArgeProject ⨝ Document (IsActive=1). Opsiyonel statu + ad/numara araması.</summary>
    Task<IReadOnlyCollection<ArgeProjectListItem>> ListAsync(string? search, byte? status, CancellationToken ct);

    /// <summary>DocumentId ile companion kaydi (yoksa null).</summary>
    Task<ArgeProject?> GetByDocumentIdAsync(int documentId, CancellationToken ct);

    /// <summary>Companion'i DocumentId'ye gore insert (yoksa) veya update eder (Name/Owner/Target/Progress/Status).</summary>
    Task UpsertCompanionAsync(ArgeProject project, CancellationToken ct);

    /// <summary>Yalnizca statu kolonunu gunceller (yasam dongusu gecisi). Etkilenen satir varsa true.</summary>
    Task<bool> UpdateStatusAsync(int documentId, byte status, int? updatedById, CancellationToken ct);

    /// <summary>Aktif personel listesi — sorumlu (owner) dropdown'u icin.</summary>
    Task<IReadOnlyCollection<ArgePersonnelOption>> GetPersonnelAsync(CancellationToken ct);

    /// <summary>Proje daha once uretime aktarildi mi (ArgeProductionLink var mi).</summary>
    Task<bool> IsTransferredAsync(int documentId, CancellationToken ct);

    /// <summary>Uretime aktarim baglantisi ekler (proje → uretilen seri Item).</summary>
    Task AddProductionLinkAsync(int documentId, int itemId, int version, int? createdById, CancellationToken ct);

    // ── Prototip CRUD (ArgePrototype) ───────────────────────────────────────

    /// <summary>Projenin aktif prototipleri (Items join + BOM/Routing varlik bayraklari).</summary>
    Task<IReadOnlyCollection<ArgePrototypeDto>> ListPrototypesAsync(int projectId, CancellationToken ct);

    /// <summary>Tek prototip kaydi (klon kaynagi icin). Yoksa null.</summary>
    Task<ArgePrototype?> GetPrototypeAsync(int prototypeId, CancellationToken ct);

    /// <summary>Prototip insert (Id=0) veya update. Yeni Id'yi doner.</summary>
    Task<int> UpsertPrototypeAsync(ArgePrototype prototype, CancellationToken ct);

    /// <summary>Prototip soft-delete (IsActive=0).</summary>
    Task SoftDeletePrototypeAsync(int prototypeId, int? updatedById, CancellationToken ct);

    /// <summary>Prototipe stok karti (Item) baglar.</summary>
    Task LinkPrototypeItemAsync(int prototypeId, int itemId, int? updatedById, CancellationToken ct);

    /// <summary>Prototipin onay (klon kaynagi) bayragini gunceller — proje basina tek onayli.</summary>
    Task SetPrototypeApprovedAsync(int prototypeId, int projectId, bool approved, int? updatedById, CancellationToken ct);

    // ── Faz 3: is emri ↔ proje + isçilik maliyeti ───────────────────────────

    /// <summary>
    /// Verilen Item bir AR-GE seri (ArgeProductionLink) veya prototip (ArgePrototype) mamuluyse
    /// projesinin DocumentId'sini doner. Yoksa null. (WorkOrder otomatik proje baglamasi icin.)
    /// </summary>
    Task<int?> FindProjectIdByItemAsync(int itemId, CancellationToken ct);

    /// <summary>Projeye bagli is emirlerinin (WorkOrder.ArgeProjectId) isçilik maliyeti rollup'i.</summary>
    Task<ArgeProjectLaborDto> GetProjectLaborAsync(int projectId, CancellationToken ct);
}
