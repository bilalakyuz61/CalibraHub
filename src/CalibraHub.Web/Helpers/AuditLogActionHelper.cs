namespace CalibraHub.Web.Helpers;

/// <summary>
/// SmartBoard extraActions "İşlem Logu" (audit trail) aksiyonu — Purchase/Sales/Finance/
/// Logistics/Production/Warehouse controller'larında ayrı ayrı kopyalanmış özdeş
/// BuildAuditLogAction private metodları yerine TEK kaynak (2026-07-21 merkezileştirme).
/// entity/formCode değerleri Views/Shared/_AuditTrailHost.cshtml ile birebir aynı olmalı —
/// ikisi de aynı kayda ait audit zaman çizelgesini gösterir.
///
/// Koşulsuz eklenir: hedef /AuditLog?entity=&amp;recordId= kayda-kilitli modda yalnızca
/// [Authorize] ister (AuditLogController.Index, 2026-07-16 kararı) — kaldırılan "Değişiklik
/// Geçmişi" sekmesi de aynı şekilde kapısızdı, bu aksiyon o erişimi aynen korur.
///
/// openInTab + matchPath (2026-07-21): aynı iframe içinde window.location.href ile navigasyon
/// Shell'in workspace-tab başlığını GÜNCELLEMEZ (tab eski sayfanın başlığında kalır) — bu
/// yüzden İşlem Logu her modülde AYRI sekmede açılır (önceden yalnız Purchase/Sales'te vardı,
/// Finance/Logistics/Production/Warehouse'da eksikti — "aynı sekmede açılıyor" şikayetinin
/// kaynağı). matchPath = "/AuditLog" sayesinde /AuditLog sekmesi zaten açıksa yeni sekme
/// yerine mevcut sekme yeni entity/recordId ile güncellenir (Shell.jsx openWorkspaceTab
/// prefix-match, bkz. Shell.jsx satır ~654-673).
/// ÖNEMLİ (doğrulandı — SmartCard.jsx + SmartTableRow.jsx dispatchActionUrl): matchPath
/// action.openInTab.matchPath olarak okunur — openInTab objesinin İÇİNDE, action üzerinde
/// top-level bir alan DEĞİL. Bu yüzden matchPath burada da openInTab'ın içine konur.
/// </summary>
public static class AuditLogActionHelper
{
    public static object BuildAuditLogAction(string entity, int recordId, string? formCode)
    {
        var url = $"/AuditLog?entity={Uri.EscapeDataString(entity)}&recordId={recordId}"
            + (string.IsNullOrWhiteSpace(formCode) ? "" : $"&formCode={Uri.EscapeDataString(formCode)}");
        var tabTitle = CalibraHub.Application.Auditing.AuditFieldLabels.EntityLabel(entity) + " — Log Kayıtları";
        return new
        {
            label = "İşlem Logu",
            icon = "ScrollText",
            color = "slate",
            url,
            openInTab = new { title = tabTitle, matchPath = "/AuditLog" },
        };
    }
}
