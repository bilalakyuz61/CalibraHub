using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// MRP koşusu — Faz 2 (2026-08-29): tek seviye (mamul) net ihtiyaç + önizleme/onay.
/// Çok seviyeli patlatma (yarı mamul alt iş emirleri) Faz 4'te eklenecek; sözleşme
/// o zaman değişmesin diye ağaç alanları şimdiden vardır.
/// </summary>
public interface IMrpService
{
    /// <summary>Ekranın 1. adımı — seçilebilir açık satış siparişi satırları.</summary>
    Task<IReadOnlyList<MrpOpenOrderLineDto>> ListOpenOrderLinesAsync(
        int? documentId, string? search, CancellationToken ct);

    /// <summary>
    /// 2. adım — seçilen satırlar için planı HESAPLAR ve Draft koşu olarak SAKLAR.
    /// Hiçbir iş emri açılmaz, hiçbir rezervasyon yazılmaz.
    /// </summary>
    Task<MrpPreviewResult> PreviewAsync(MrpPreviewRequest request, int? userId, CancellationToken ct);

    /// <summary>
    /// 3. adım — önizlenen koşunun TAMAMINI uygular. Draft olmayan koşu reddedilir
    /// (çift-apply koruması).
    /// </summary>
    Task<MrpApplyResult> ApplyAsync(MrpApplyRequest request, int? userId, CancellationToken ct);

    /// <summary>Kullanıcı vazgeçti — Draft koşuyu Discarded yapar.</summary>
    Task DiscardAsync(int runId, int? userId, CancellationToken ct);

    /// <summary>Saklanmış bir koşuyu önizleme ağacı olarak geri okur.</summary>
    Task<MrpPreviewResult> GetRunAsync(int runId, CancellationToken ct);
}
