using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Calibra master DB'deki merkezi Attachment tablosu icin repository arayuzu.
/// Tum entity turlerinden (Note, Document, BOM...) erisilebilir.
/// </summary>
public interface IAttachmentRepository
{
    Task<IReadOnlyCollection<Attachment>> GetByEntityAsync(string entityType, string entityId, CancellationToken ct);
    /// <summary>Belirtilen tipte aktif eki olan EntityId'lerin kümesi (kart "görsel var mı" kontrolü için toplu sorgu).</summary>
    Task<IReadOnlyCollection<string>> GetEntityIdsWithAttachmentAsync(string entityType, CancellationToken ct);
    Task<Attachment?> GetByIdAsync(int id, CancellationToken ct);
    Task<byte[]?> GetBinaryAsync(int id, CancellationToken ct);
    Task<int> AddAsync(Attachment attachment, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
    Task DeleteByEntityAsync(string entityType, string entityId, CancellationToken ct);
}
