using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>Bir malzemenin depo bazlı kullanılabilir bakiyesi (fiziksel − aktif rezervasyon).</summary>
public sealed record MrpAvailabilityRow(int ItemId, int LocationId, decimal Physical, decimal Reserved)
{
    public decimal Available => Physical - Reserved;
}

/// <summary>
/// Açık iş emri arzı + "mevcut emre bağlama" adaylığı için gereken her şey TEK sorgudan.
/// <para><c>RemainingQuantity</c> = PlannedQuantity − ProducedQuantity − ScrapQuantity (henüz
/// üretilmemiş kısım). <c>UnpeggedQuantity</c> = PlannedQuantity − (WorkOrderSource tahsisi +
/// WorkOrderPeg miktarı) — yani "bu emrin hangi kadarı henüz bir siparişe bağlanmamış".
/// Kullanıcının kuralı: (a) üretilmemiş miktar var, (b) bağlılar düşünce açık kalıyor,
/// (c) teslim tarihi uyuyor.</para>
/// </summary>
public sealed record MrpOpenWorkOrderRow(
    int WorkOrderId,
    string? DocumentNumber,
    int ItemId,
    int? ConfigId,
    decimal PlannedQuantity,
    decimal RemainingQuantity,
    decimal UnpeggedQuantity,
    DateTime? PlannedEndDate,
    byte Status);

/// <summary>Açık satın alma siparişinden beklenen giriş (malzeme bazında toplanmış).</summary>
public sealed record MrpOpenPurchaseRow(int ItemId, decimal OpenQuantity, DateTime? EarliestExpectedDate);

/// <summary>Kalıcılaştırılacak koşu satırı — <c>MrpRunLine</c> tablosunun birebir karşılığı.</summary>
public sealed record MrpRunLineRecord(
    int Id,
    int Level,
    int? ParentRunLineId,
    int ItemId,
    int? ConfigId,
    string ActionType,
    decimal GrossQuantity,
    decimal OnHandApplied,
    decimal OpenSupplyApplied,
    decimal NetQuantity,
    DateTime? PlannedStartDate,
    DateTime? PlannedEndDate,
    int? TargetWorkOrderId,
    string? PegJson,
    int? CreatedWorkOrderId,
    int? CreatedDocumentId,
    string? Message,
    int? LocationId);

/// <summary>
/// MRP veri erişimi (2026-08-29). <b>Tüm metotlar TOPLU çalışır</b> — malzeme/satır başına
/// sorgu açmak (N+1) bu modülde yasaktır: bir koşu yüzlerce sipariş satırı içerebilir.
/// </summary>
public interface IMrpRepository
{
    /// <summary>
    /// Açık satış siparişi satırları. <paramref name="lineIds"/> null/boşsa tüm açık satırlar
    /// döner (ekranın 1. adımı); dolu ise yalnız o satırlar (önizleme).
    /// <para>Açık miktar KANONİK formülle hesaplanır (<c>BaseQuantity − DeliveredQuantity</c>,
    /// <c>Status NOT IN (Rejected, Cancelled)</c>) — <c>vw_ItemOpenSalesQty</c> ile aynı tanım,
    /// ikinci bir "açık sipariş" rakamı doğmasın diye.</para>
    /// </summary>
    Task<IReadOnlyList<MrpOpenOrderLineDto>> ListOpenSalesOrderLinesAsync(
        IReadOnlyCollection<int>? lineIds, int? documentId, string? search, CancellationToken ct);

    /// <summary>(Malzeme, depo) çiftleri için fiziksel bakiye + aktif rezervasyon — tek sorgu.</summary>
    Task<IReadOnlyList<MrpAvailabilityRow>> GetAvailabilityAsync(
        IReadOnlyCollection<(int ItemId, int LocationId)> keys, CancellationToken ct);

    /// <summary>Açık (Planned/Released/InProgress) iş emirleri — arz + merge adaylığı.</summary>
    Task<IReadOnlyList<MrpOpenWorkOrderRow>> GetOpenWorkOrdersAsync(
        IReadOnlyCollection<int> itemIds, CancellationToken ct);

    /// <summary>Açık satın alma siparişlerinden beklenen giriş (vw_ItemOpenPurchaseQty).</summary>
    Task<IReadOnlyList<MrpOpenPurchaseRow>> GetOpenPurchaseSupplyAsync(
        IReadOnlyCollection<int> itemIds, CancellationToken ct);

    /// <summary>Draft koşu + satırlarını yazar; koşu Id'sini döner.</summary>
    Task<int> CreateRunAsync(
        string sourceScope, IReadOnlyCollection<int> selectedLineIds,
        IReadOnlyList<MrpRunLineRecord> lines, int? userId, CancellationToken ct);

    /// <summary>Koşu başlığı — durum kontrolü için (bulunamazsa null).</summary>
    Task<(int Id, MrpRunStatus Status, DateTime RunDate)?> GetRunAsync(int runId, CancellationToken ct);

    /// <summary>Koşunun satırları (yazılış sırasına göre).</summary>
    Task<IReadOnlyList<MrpRunLineRecord>> GetRunLinesAsync(int runId, CancellationToken ct);

    /// <summary>
    /// Koşuyu Applied yapar — YALNIZ Draft ise. Zaten uygulanmışsa false döner (çift-apply
    /// koruması; çift tıklama / iki sekme).
    /// </summary>
    Task<bool> TryMarkRunAppliedAsync(int runId, string? summaryJson, int? userId, CancellationToken ct);

    /// <summary>Koşuyu Discarded yapar (kullanıcı vazgeçti).</summary>
    Task DiscardRunAsync(int runId, int? userId, CancellationToken ct);

    /// <summary>Apply sonrası koşu satırına üretilen emir/belge Id'sini işler.</summary>
    Task SetRunLineResultAsync(int runLineId, int? workOrderId, int? documentId, string? message, CancellationToken ct);
}
