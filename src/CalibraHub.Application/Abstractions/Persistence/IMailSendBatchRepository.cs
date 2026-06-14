using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Toplu mail gonderim log (batch + item) repository — tek arayuzde batch baslik
/// + item satirlari yonetimi. Per-company DB'de yasar.
/// </summary>
public interface IMailSendBatchRepository
{
    /// <summary>Yeni batch satiri olustur — Id geri donderir.</summary>
    Task<int> CreateBatchAsync(MailSendBatch batch, CancellationToken ct);

    /// <summary>
    /// Item satiri ekle — Id geri donderir. Render-once-per-recipient akisinda once
    /// LogItem olusturulur, Id alinir, view'lara <c>@DocumentId = LogItem.Id</c> olarak
    /// gecirilir; gonderim sonrasi <see cref="UpdateItemStatusAsync"/> ile status guncelenir.
    /// </summary>
    Task<int> AddItemAsync(MailSendLogItem item, CancellationToken ct);

    /// <summary>
    /// LogItem satirinin status / error / sentAt alanlarini guncelle. Render+gonderim
    /// sonrasi cagrilir.
    /// </summary>
    Task UpdateItemStatusAsync(int itemId, string status, string? errorMessage, DateTime? sentAt, CancellationToken ct);

    /// <summary>Batch bitiminde sayim guncelle (sent + fail).</summary>
    Task UpdateBatchCountsAsync(int batchId, int sentCount, int failCount, CancellationToken ct);

    /// <summary>Sirket'e ait son N batch — gecmis ekraninda en yeni gosterilir.</summary>
    Task<IReadOnlyList<MailSendBatch>> GetRecentBatchesAsync(int companyId, int take, CancellationToken ct);

    /// <summary>Tek batch detay + item listesi.</summary>
    Task<(MailSendBatch? Batch, IReadOnlyList<MailSendLogItem> Items)> GetBatchDetailAsync(int batchId, int companyId, CancellationToken ct);

    /// <summary>
    /// Batch ve butun item satirlarini kalici siler (hard delete). companyId 0 ise sirket
    /// kontrolu yapmaz, aksi halde sadece bu sirkete ait kayit silinir.
    /// </summary>
    Task DeleteBatchAsync(int batchId, int companyId, CancellationToken ct);
}
