namespace CalibraHub.Web.Models.Shared;

/// <summary>
/// Paylaşılan "Değişiklik Geçmişi" (audit trail) host'u için model — `_AuditTrailHost.cshtml`.
///
/// Belge/tanım düzenleme ekranı bu partial'ı bir sekme içine include eder; panel
/// /AuditLog/Record endpoint'inden kaydın işlem geçmişini çeker (dosya tabanlı audit log +
/// opsiyonel WidgetTraLog "Ek Alanlar" geçmişi) ve zaman çizelgesi olarak gösterir.
///
/// Kullanım:
///   @await Html.PartialAsync("_AuditTrailHost", new AuditTrailHostModel {
///       Entity = "satis_siparisi",          // audit entity kodu (DocumentType.Code veya sabit entity adı)
///       RecordId = Model.DocumentId?.ToString(),
///       WidgetFormCode = "SALES_ORDER_EDIT" // opsiyonel — Ek Alanlar geçmişi de gelsin
///   })
///
/// Yeni kayıt save sonrası: window.CalibraAuditTrail.reload('&lt;HostId&gt;', newId)
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
