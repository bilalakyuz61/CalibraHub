using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Calibra master DB'deki merkezi Attachment tablosu icin repository arayuzu.
/// Tum entity turlerinden (Note, Document, BOM...) erisilebilir.
/// </summary>
public interface IAttachmentRepository
{
    Task<IReadOnlyCollection<Attachment>> GetByEntityAsync(string entityType, string entityId, CancellationToken ct);
    Task<Attachment?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<byte[]?> GetBinaryAsync(Guid id, CancellationToken ct);
    Task<Guid> AddAsync(Attachment attachment, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
    Task DeleteByEntityAsync(string entityType, string entityId, CancellationToken ct);
}
