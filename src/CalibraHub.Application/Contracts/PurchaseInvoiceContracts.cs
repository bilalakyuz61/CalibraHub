namespace CalibraHub.Application.Contracts;

/// <summary>
/// Gelen e-belgenin bir kalemi — eşleştirme ekranının SOL tarafı.
/// <paramref name="SuggestedItemId"/> tedarikçi kodu (Cari×Stok) ya da stok kodu üzerinden
/// bulunan öneridir; kullanıcı değiştirebilir.
/// </summary>
public sealed record EDocumentInvoiceLineDto(
    int LineNumber,
    string? ItemCode,
    string? ItemName,
    decimal Quantity,
    string? UnitCode,
    decimal UnitPrice,
    decimal? LineAmount,
    decimal? VatRate,
    decimal? VatAmount,
    int? SuggestedItemId,
    string? SuggestedItemCode,
    string? SuggestedItemName);

/// <summary>
/// Sipariş / irsaliye tarafındaki aday satır — eşleştirme ekranının SAĞ tarafı.
/// <paramref name="RemainingQuantity"/> = miktar − daha önce faturalanan (DocumentLineLink).
/// </summary>
public sealed record PurchaseInvoiceSourceLineDto(
    int LineId,
    int DocumentId,
    string DocumentNumber,
    DateTime DocumentDate,
    int LineNo,
    int ItemId,
    string? ItemCode,
    string? ItemName,
    decimal Quantity,
    decimal InvoicedQuantity,
    decimal RemainingQuantity,
    int? UnitId,
    string? UnitCode,
    decimal UnitPrice,
    decimal BaseQuantity,
    int? LocationId);

/// <summary>Eşleştirme ekranını besleyen tek yük.</summary>
public sealed record PurchaseInvoiceCandidatesDto(
    int IncomingDocumentId,
    string DocumentNumber,
    DateTime IssueDate,
    string Mode,
    int? ContactId,
    string? ContactCode,
    string? ContactTitle,
    string? SenderTaxNumber,
    IReadOnlyList<EDocumentInvoiceLineDto> EDocumentLines,
    IReadOnlyList<PurchaseInvoiceSourceLineDto> SourceLines,
    int? ExistingInvoiceId,
    string? ExistingInvoiceNumber);

/// <summary>
/// Oluşturulacak fatura satırı. <paramref name="SourceLineId"/> doluysa satır bir sipariş/irsaliye
/// satırından türetilir — <b>kaynak satır başına BİR fatura satırı</b> yazılır, böylece bizdeki
/// 5 satır tek e-belge kalemine eşleşse bile satır kırılımı KAYBOLMAZ (kullanıcı kuralı).
/// </summary>
public sealed record PurchaseInvoiceLineInput(
    int EDocumentLineNumber,
    int? SourceLineId,
    int ItemId,
    int? UnitId,
    decimal Quantity,
    decimal UnitPrice,
    decimal VatRate,
    string? Notes);

/// <summary>
/// Fatura oluşturma isteği.
/// <para><b>Mod:</b> <c>direct</c> (stok eşleştirmeli doğrudan), <c>order</c> (sipariş bağlantılı),
/// <c>delivery</c> (irsaliye bağlantılı).</para>
/// <para><b>Stok etkisi:</b> yalnız <c>delivery</c> modunda hareket ÜRETİLMEZ — mal girişini
/// irsaliye zaten yaptı, fatura onu mükerrer saymaz (kullanıcı kararı 2026-08-31).</para>
/// </summary>
public sealed record CreatePurchaseInvoiceRequest(
    int IncomingDocumentId,
    string Mode,
    int ContactId,
    DateTime InvoiceDate,
    int? LocationId,
    string? ExternalNumber,
    string? Notes,
    IReadOnlyList<PurchaseInvoiceLineInput> Lines,
    bool AcceptDifferences);

public sealed record CreatePurchaseInvoiceResultDto(
    int DocumentId,
    string DocumentNumber,
    int LineCount,
    bool StockAffected);
