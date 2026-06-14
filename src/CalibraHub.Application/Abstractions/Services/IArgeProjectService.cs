using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// AR-GE proje uygulama servisi. Document motorunu (numara/Document shell) yeniden kullanir,
/// uzerine AR-GE invariant'larini (companion yazimi, ValidateTransition'li statu gecisi) koyar.
/// DocumentService.SaveQuoteAsync KULLANILMAZ (cari/satir zorunlulugu + satir/fiyat mantigi AR-GE'ye uymaz).
/// </summary>
public interface IArgeProjectService
{
    Task<IReadOnlyCollection<ArgeProjectListItem>> ListAsync(string? search, byte? status, CancellationToken ct);

    Task<ArgeProjectDetail?> GetAsync(int documentId, CancellationToken ct);

    /// <summary>Yeni proje (Document shell + companion) veya mevcut companion guncelle. (ok, hata, documentId).</summary>
    Task<(bool Ok, string? Error, int DocumentId)> SaveAsync(SaveArgeProjectRequest request, int? userId, CancellationToken ct);

    /// <summary>Yasam dongusu gecisi — ValidateTransition ile dogrulanir. (ok, hata).</summary>
    Task<(bool Ok, string? Error)> ChangeStatusAsync(int documentId, byte newStatus, int? userId, CancellationToken ct);

    /// <summary>Projeyi soft-delete eder (Document.IsActive=0).</summary>
    Task DeleteAsync(int documentId, CancellationToken ct);

    /// <summary>Aktif personel listesi — sorumlu dropdown'u icin.</summary>
    Task<IReadOnlyCollection<ArgePersonnelOption>> GetPersonnelAsync(CancellationToken ct);

    /// <summary>
    /// Onaylanan projeyi uretime aktarir: seri Item (urun karti) uretir, ArgeProductionLink
    /// kaydeder, durumu UretimeAktarildi yapar. Onayli prototip varsa onun BOM+Rota'sini seri
    /// Item'a KLONLAR (best-effort). Idempotent (UNIQUE link). (ok, hata, itemId, itemCode, not).
    /// </summary>
    Task<(bool Ok, string? Error, int ItemId, string? ItemCode, string? Note)> ConvertToProductionAsync(int documentId, int? userId, CancellationToken ct);

    // ── Prototip yonetimi ────────────────────────────────────────────────────

    /// <summary>Projenin aktif prototipleri (Item + BOM/Rota durumu).</summary>
    Task<IReadOnlyCollection<ArgePrototypeDto>> ListPrototypesAsync(int projectId, CancellationToken ct);

    /// <summary>Prototip ekle/guncelle. (ok, hata, id).</summary>
    Task<(bool Ok, string? Error, int Id)> SavePrototypeAsync(SavePrototypeRequest request, int? userId, CancellationToken ct);

    /// <summary>Prototip soft-delete.</summary>
    Task DeletePrototypeAsync(int prototypeId, int? userId, CancellationToken ct);

    /// <summary>
    /// Prototipe stok karti (Item) baglar — yoksa turetir. Recete/rota tanimlamak icin gerekli.
    /// Zaten bagliysa mevcut Item'i doner. (ok, hata, itemId, itemCode).
    /// </summary>
    Task<(bool Ok, string? Error, int ItemId, string? ItemCode)> EnsurePrototypeItemAsync(int prototypeId, int? userId, CancellationToken ct);

    /// <summary>Prototipin onay (klon kaynagi) bayragini ayarlar — proje basina tek onayli.</summary>
    Task<(bool Ok, string? Error)> SetPrototypeApprovedAsync(int prototypeId, bool approved, int? userId, CancellationToken ct);

    /// <summary>Projeye bagli is emirlerinin isçilik maliyeti rollup'i (Faz 3).</summary>
    Task<ArgeProjectLaborDto> GetProjectLaborAsync(int projectId, CancellationToken ct);
}
