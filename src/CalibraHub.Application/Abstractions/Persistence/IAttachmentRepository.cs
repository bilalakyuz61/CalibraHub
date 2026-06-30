using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Calibra master DB'deki merkezi Attachment tablosu için repository arayüzü.
/// FormId + RefId (her ikisi de INT) polimorfik tasarım.
/// Not ekleri bu repository'yi kullanmaz; doğrudan note_attachments (company DB) üzerinden çalışır.
/// </summary>
public interface IAttachmentRepository
{
    Task<IReadOnlyCollection<Attachment>> GetByFormRefAsync(int formId, int refId, CancellationToken ct);

    /// <summary>Belirtilen formda aktif eki olan RefId'lerin kümesi (kart "görsel var mı" kontrolü için toplu sorgu).</summary>
    Task<IReadOnlyCollection<int>> GetRefIdsWithAttachmentAsync(int formId, CancellationToken ct);

    Task<Attachment?> GetByIdAsync(int id, CancellationToken ct);
    Task<byte[]?> GetBinaryAsync(int id, CancellationToken ct);
    Task<int> AddAsync(Attachment attachment, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
    Task DeleteByFormRefAsync(int formId, int refId, CancellationToken ct);

    // ── Döküman Yönetimi ────────────────────────────────────────────────────────
    /// <summary>Aktif (IsActive=1) tüm ekleri döner. formIdFilter=null → tüm modüller.</summary>
    Task<IReadOnlyCollection<Attachment>> GetAllActiveAsync(int? formIdFilter, CancellationToken ct);

    /// <summary>Başlık, açıklama, kategori ve etiketleri günceller.</summary>
    Task UpdateMetaAsync(int id, string? title, string? description, string? category, string? tags, int? updatedById, CancellationToken ct);

    /// <summary>Bir belgenin tüm revizyon geçmişini döner (aktif + pasif, yeniden eskiye sıralı).</summary>
    Task<IReadOnlyCollection<Attachment>> GetVersionHistoryAsync(int id, CancellationToken ct);
}
