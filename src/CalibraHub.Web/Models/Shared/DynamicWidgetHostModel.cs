namespace CalibraHub.Web.Models.Shared;

/// <summary>
/// Paylaşılan dinamik widget (Alan Yönetimi / EAV) host'u için model — `_DynamicWidgetHost.cshtml`.
///
/// Amaç: Her belge/tanım düzenleme ekranının kendi başına ~40 satır widget mount/reload/save
/// JS'i kopyalaması yerine tek partial include etmesi. Ambar Giriş/Çıkış/Transfer/Sayım
/// ekranlarında bu host UNUTULDUĞU için Alan Yönetimi'nde tanımlı alanlar görünmüyordu
/// (2026-07-08). Partial bunu tekrarlanamaz kılar.
///
/// Host ekranın YAPMASI GEREKEN 3 entegrasyon noktası (partial'ın header yorumunda da var):
///   1) Bu partial'ı bir sekme/panel içine include et: FormCode = üst-bilgi form kodu.
///   2) Kayıt yüklenince:  window.CalibraWidgetHost.reload('&lt;HostId&gt;', recordId)
///   3) Belge kaydedilip Id alınınca:  await window.CalibraWidgetHost.save('&lt;HostId&gt;', recordId)
/// </summary>
public sealed class DynamicWidgetHostModel
{
    /// <summary>Üst-bilgi form kodu (Forms.FormCode) — örn. "STOCK_OUT", "INVENTORY_COUNT". Alan Yönetimi bu kod altına tanımlanır.</summary>
    public required string FormCode { get; init; }

    /// <summary>DOM element id'si — reload/save çağrılarında bu id kullanılır. Ekran başına benzersiz olmalı.</summary>
    public string HostId { get; init; } = "dynWidgetHost";

    /// <summary>Düzenlemede mevcut kaydın Id'si (yeni kayıtta boş/null — save sonrası reload edilir).</summary>
    public string? RecordId { get; init; }

    /// <summary>Boş durum (hiç widget tanımlı değil) mesajı gizli kalsın mı — varsayılan false (renderer zaten boşsa host'u gizler).</summary>
    public bool HideWhenEmpty { get; init; } = true;
}
