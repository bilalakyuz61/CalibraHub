namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Belge numarası türetme servisi — Tasarım Kuralı (DocLayoutRule) pattern'inin tıpkısı.
///
/// Akış:
///   1. Verilen context (documentTypeId + cariId? + userId? + tarih) için aktif kuralları bul
///   2. En yüksek ağırlıklı kural seç (Cari=16 + Grup=8 + Kullanıcı=4 + Şube=2 + Tarih=1)
///   3. Sayaç state'ini lock + increment et (concurrent-safe)
///   4. Format uygula: PREFIX + YEAR + MONTH + zero-padded COUNTER
///
/// Hiçbir kural eşleşmezse caller eski default davranışa fallback yapar (şu an
/// "TKL{yyMM}{0000xx}" gibi DocumentRepository.GetNextDocumentNumberAsync).
/// </summary>
public interface IDocumentNumberService
{
    /// <summary>
    /// Verilen context için bir sonraki belge numarasını üretir.
    /// Kural bulunmazsa NULL döner — caller fallback uygular.
    /// </summary>
    Task<string?> GenerateNextAsync(DocumentNumberContext context, CancellationToken ct);
}

/// <summary>
/// Belge numarası türetme bağlamı — kural matching için tüm filtre değerleri.
/// </summary>
public sealed record DocumentNumberContext(
    int DocumentTypeId,
    int? ContactId,
    int? ContactGroupId,
    int? UserId,
    int? BranchId,
    DateTime IssueDate);
