using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Document → Document koprusu (N-1): bir hedef belge (siparis/fatura) hangi
/// kaynak belgelerden (teklif/siparis) turetildi. Her bir baglanti ayri kayit.
/// </summary>
public interface IDocumentSourceRepository
{
    /// <summary>document_source tablosu yoksa olusturur (per-company schema).</summary>
    Task EnsureSchemaAsync(CancellationToken ct);

    /// <summary>Yeni baglanti ekle. Ayni cift varsa tekrar etmez (UNIQUE INDEX).</summary>
    Task AddAsync(int documentId, int sourceDocumentId, CancellationToken ct);

    /// <summary>Bir hedef belgenin kaynak belge id'lerini doner.</summary>
    Task<IReadOnlyCollection<int>> GetSourceIdsAsync(int documentId, CancellationToken ct);

    /// <summary>Bir kaynak belgenin (teklif) zaten siparise donusturulmus olup olmadigi.</summary>
    Task<bool> IsSourceConsumedAsync(int sourceDocumentId, CancellationToken ct);

    /// <summary>
    /// Bir kaynak belgeden (alis_talebi / İhtiyaç Kaydı) türetilmiş tüm belge ID'lerini döner.
    /// Ters yön: GetSourceIdsAsync(docId) → "bu belgenin kaynakları", bu metot → "bu kaynaktan türetilenler".
    /// </summary>
    Task<IReadOnlyCollection<int>> GetDerivedDocumentIdsAsync(int sourceDocumentId, CancellationToken ct);
}
