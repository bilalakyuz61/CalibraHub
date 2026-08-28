using CalibraHub.Application.Services.EDocument;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IIncomingDocumentRepository
{
    Task<bool> ExistsByEnvelopeIdAsync(string envelopeId, CancellationToken cancellationToken);
    Task<bool> ExistsByDocumentNumberAndRecipientAsync(
        string documentNumber,
        string recipientTaxNumber,
        DocumentKind kind,
        CancellationToken cancellationToken);
    /// <param name="details">
    /// Kalem / vergi / tasima satirlari HAZIR verildiginde kullanilir (OFFLINE ERP yolu).
    /// null ise <see cref="IncomingDocument.PayloadRaw"/> icindeki UBL XML ayristirilir
    /// (ONLINE entegrator yolu). Offline kaynakta ayristirilacak XML YOKTUR: Netsis
    /// veritabaninda zarf XML'i (TBLEFATZARF.XMLVERI) pratikte BOSTUR (olculdu: 14.382
    /// satirin tamaminda 1 bayt), veri iliskisel tablolarda durur.
    /// </param>
    Task AddAsync(IncomingDocument document, CancellationToken cancellationToken,
                  EDocumentDetails? details = null);
    Task<IReadOnlyCollection<IncomingDocument>> GetPendingApprovalsAsync(bool? isProcessed, CancellationToken cancellationToken);
    Task<IncomingDocument?> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task UpdateIsProcessedAsync(int id, bool isProcessed, CancellationToken cancellationToken);
}
