namespace CalibraHub.Application.Contracts;

/// <summary>
/// MRP (Malzeme İhtiyaç Planlaması) sözleşmeleri — Faz 2 (2026-08-29).
///
/// <para><b>Temel kural:</b> MRP'nin stok tahsisi SANALDIR. Koşu sırasında serbest bakiye
/// ihtiyaçlara dağıtılır ama <c>StockReservation</c>'a hiçbir şey yazılmaz; her koşu sıfırdan
/// hesaplar. Kalıcı olan tek şey açık iş emirleri ve sipariş bağlarıdır. Kullanıcının FİİLEN
/// yaptığı rezervasyonlar ise talebi DÜŞÜRÜR (rezerve miktar için iş emri açılmaz).</para>
///
/// <para><b>Koşu neden saklanır:</b> onay, kullanıcının GÖRDÜĞÜ planı uygular. Apply anında
/// yeniden hesaplansaydı "3 emir gördüm, 5 açıldı" olurdu. Ayrıca Draft→Applied tek yönlü
/// geçişi çift-apply'ı engeller ve "bu emir neden açıldı" izini verir.</para>
/// </summary>
public static class MrpActionTypes
{
    /// <summary>Yeni iş emri açılacak.</summary>
    public const string NewWorkOrder = "NewWorkOrder";
    /// <summary>Mevcut açık iş emrine miktar eklenecek (sipariş bağı kurulur).</summary>
    public const string MergeWorkOrder = "MergeWorkOrder";
    /// <summary>Üretilemeyen malzeme eksik — Satın Alma Talebi önerilecek.</summary>
    public const string PurchaseRequest = "PurchaseRequest";
    /// <summary>İhtiyaç eldeki stok/açık arz ile tamamen karşılanıyor — aksiyon yok.</summary>
    public const string CoveredByStock = "CoveredByStock";
    /// <summary>Aksiyon üretilemedi (reçete yok, rota yok, depo yok vb.) — gerekçe Message'da.</summary>
    public const string Shortage = "Shortage";
}

/// <summary>MRP koşusunun durumu. Draft→Applied TEK YÖNLÜDÜR (idempotentlik).</summary>
public enum MrpRunStatus
{
    Draft = 0,
    Applied = 1,
    Discarded = 2,
}

/// <summary>
/// MRP ekranının 1. adımı — seçilebilir açık satış siparişi satırı.
/// <para><c>OpenQuantity</c> HAM açık miktardır (BaseQuantity − DeliveredQuantity, gösterim
/// biriminde). Rezerve ve iş emrine tahsis edilmiş miktarlar AYRI alanlardır; tek bir
/// "kalan" rakamına indirgenmez — kullanıcı hangi miktarın neden düştüğünü görebilmeli.</para>
/// </summary>
public sealed record MrpOpenOrderLineDto(
    int LineId,
    int DocumentId,
    string DocumentNumber,
    string? ContactName,
    DateTime? DeliveryDate,
    int ItemId,
    string ItemCode,
    string ItemName,
    int? ConfigId,
    int? UnitId,
    string? UnitCode,
    int? LocationId,
    decimal OrderQuantity,
    decimal DeliveredQuantity,
    decimal ReservedQuantity,
    decimal AllocatedQuantity,
    decimal OpenQuantity,
    /// <summary>Malzemenin kırılım politikası (malzeme kartından) — önizlemede rozet olarak gösterilir.</summary>
    string SplitPolicy,
    /// <summary>Malzeme üretilebilir mi (Mamul/Yarı Mamul)? Değilse iş emri açılamaz.</summary>
    bool IsProducible);

/// <summary>Bir planlı emrin hangi sipariş satırından ne kadar talep taşıdığı (pegging).</summary>
public sealed record MrpPegDto(
    int RootDocumentId,
    string RootDocumentNumber,
    int RootLineId,
    decimal Quantity);

/// <summary>
/// Önizleme ağacının bir düğümü. Faz 2'de yalnız <c>Level = 0</c> üretilir; ağaç alanları
/// (Level / ParentRunLineId) Faz 4'te sözleşme değişmesin diye şimdiden vardır.
/// </summary>
public sealed record MrpPreviewNodeDto(
    int RunLineId,
    int Level,
    int? ParentRunLineId,
    int ItemId,
    string ItemCode,
    string ItemName,
    int? ConfigId,
    string? UnitCode,
    int? LocationId,
    string? LocationName,
    string ActionType,
    string SplitPolicy,
    decimal GrossQuantity,
    decimal OnHandApplied,
    decimal OpenSupplyApplied,
    decimal NetQuantity,
    DateTime? PlannedStartDate,
    DateTime? PlannedEndDate,
    /// <summary>MergeWorkOrder ise hedef emrin Id'si + belge numarası.</summary>
    int? TargetWorkOrderId,
    string? TargetWorkOrderNumber,
    /// <summary>Atlama/uyarı gerekçesi. Sessiz atlama YASAK — her aksiyonsuz düğümde doludur.</summary>
    string? Message,
    IReadOnlyList<MrpPegDto> Pegs);

/// <summary>Önizleme sonucu — Draft koşu + ağaç + özet.</summary>
public sealed record MrpPreviewResult(
    bool Ok,
    string? Error,
    int RunId,
    IReadOnlyList<MrpPreviewNodeDto> Nodes,
    MrpPreviewSummaryDto Summary);

public sealed record MrpPreviewSummaryDto(
    int NewWorkOrderCount,
    int MergeWorkOrderCount,
    int CoveredByStockCount,
    int ShortageCount,
    int PurchaseRequestCount,
    int SelectedLineCount);

/// <summary>Önizleme isteği. <paramref name="LineIds"/> boşsa hiçbir şey hesaplanmaz (hata döner).</summary>
public sealed record MrpPreviewRequest(
    IReadOnlyCollection<int> LineIds,
    /// <summary>'Selected' (toplu ekran) | 'SingleOrder' (sipariş kartı kısayolu) — yalnız iz amaçlı.</summary>
    string SourceScope = "Selected");

/// <summary>Onay isteği — önizlenen koşunun TAMAMI uygulanır (düğüm bazlı seçim yoktur).</summary>
public sealed record MrpApplyRequest(
    int RunId,
    /// <summary>Eksik malzemeler için Satın Alma Talebi belgesi de oluşturulsun mu (Faz 4).</summary>
    bool CreatePurchaseRequest = false);

/// <summary>Apply sonucu — oluşan/güncellenen emirler ve varsa uyarılar.</summary>
public sealed record MrpApplyResult(
    bool Ok,
    string? Error,
    int RunId,
    IReadOnlyList<MrpCreatedWorkOrderDto> Created,
    IReadOnlyList<MrpCreatedWorkOrderDto> Merged,
    /// <summary>Uygulanamayan düğümler — gerekçeleriyle. Boş atlama yok.</summary>
    IReadOnlyList<string> Warnings,
    int? PurchaseRequestDocumentId);

public sealed record MrpCreatedWorkOrderDto(
    int WorkOrderId,
    string? DocumentNumber,
    int ItemId,
    string ItemCode,
    string ItemName,
    decimal Quantity);
