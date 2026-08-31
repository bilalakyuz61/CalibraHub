using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Gelen e-belgeden ALIŞ FATURASI üretimi (3 yol: doğrudan / sipariş bağlantılı / irsaliye bağlantılı).
///
/// <para><b>Neden ayrı depo:</b> ticari belge kaydetme akışı (DocumentService) kullanıcının elle
/// doldurduğu formu kaydeder; burada kaynak BAŞKA bir belgedir (e-belge + sipariş/irsaliye satırları)
/// ve satır türetme kuralı farklıdır. Sipariş→irsaliye dönüşümünün (SqlStockDocRepository
/// .ConvertOrderToDeliveryAsync) kardeşi sayılır.</para>
/// </summary>
public interface IPurchaseInvoiceRepository
{
    /// <summary>Eşleştirme ekranını besler: e-belge kalemleri + seçili moda göre aday kaynak satırlar.</summary>
    Task<PurchaseInvoiceCandidatesDto?> GetCandidatesAsync(
        int incomingDocumentId, string mode, CancellationToken cancellationToken);

    /// <summary>
    /// Faturayı oluşturur. Kaynak satır başına bir fatura satırı yazılır; satırlar
    /// <c>SourceLineId</c> ile kaynağa bağlanır (DocumentLineLink dönüşüm kaydı bundan türetilir).
    /// </summary>
    Task<CreatePurchaseInvoiceResultDto> CreateAsync(
        CreatePurchaseInvoiceRequest request, int? userId, CancellationToken cancellationToken);
}
