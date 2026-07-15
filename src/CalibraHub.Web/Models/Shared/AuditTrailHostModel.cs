namespace CalibraHub.Web.Models.Shared;

/// <summary>
/// Paylaşılan "Değişiklik Geçmişi" (audit trail) host'u için model — `_AuditTrailHost.cshtml`.
///
/// 2026-07-16: Bu host artık kaydın geçmişini İNLINE göstermez. Bunun yerine merkezi
/// İşlem Logları ekranına (/AuditLog?entity=&amp;recordId=&amp;formCode=) giden, SADECE bu
/// kaydın loglarını gösterecek şekilde kilitlenmiş bir "Log Kayıtları" kartı/butonu render
/// eder (yeni workspace tab'ında açılır). Kayıt henüz kaydedilmemişse (RecordId boş) buton
/// disabled görünür.
///
/// Kullanım:
///   @await Html.PartialAsync("_AuditTrailHost", new AuditTrailHostModel {
///       Entity = "satis_siparisi",          // audit entity kodu (DocumentType.Code veya sabit entity adı)
///       RecordId = Model.DocumentId?.ToString(),
///       WidgetFormCode = "SALES_ORDER_EDIT" // opsiyonel — Ek Alanlar (widget) logları da /AuditLog'da merge edilsin
///   })
///
/// Yeni kayıt save sonrası (sayfa reload olmadan Id alan SPA akışlarında):
///   window.CalibraAuditTrail.reload('&lt;HostId&gt;', newId)
/// </summary>
public sealed class AuditTrailHostModel
{
    /// <summary>Audit entity kodu — belge tipleri için DocumentType.Code ("satis_siparisi"),
    /// sabit entity'ler için sınıf adı ("Item", "Contact", "WorkOrder").</summary>
    public required string Entity { get; init; }

    /// <summary>Kaydın Id'si (yeni kayıtta null — panel bilgi mesajı gösterir).</summary>
    public string? RecordId { get; init; }

    /// <summary>Opsiyonel: Ek Alanlar (WidgetTraLog) geçmişinin merge edileceği form kodu.</summary>
    public string? WidgetFormCode { get; init; }

    /// <summary>DOM element id — ekran başına benzersiz olmalı.</summary>
    public string HostId { get; init; } = "auditTrailHost";
}
