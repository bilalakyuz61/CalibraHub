namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Form ekranindaki Save eyleminden sonra OnSave (otomatik) trigger'li
/// entegrasyonlari ARKA PLANDA fire eder. Kullanici Save bekleyisini geciktirmez —
/// metot derhal doner, gercek HTTP cagrilari Task.Run icinde scoped DI ile yapilir.
///
/// Kullanim (controller Save endpoint'inde, basarili kayit sonrasi):
///   _onSaveDispatcher.FireOnSave(
///       formCodes: new[] { "SALES_ORDER_NEW", "SALES_ORDER_EDIT" },
///       recordId: savedDocument.Id.ToString(),
///       triggeredBy: userName);
/// </summary>
public interface IIntegrationOnSaveDispatcher
{
    /// <summary>Tek form code icin fire-and-forget. Bekleme yok.</summary>
    void FireOnSave(string formCode, string recordId, string? triggeredBy = null,
        IntegrationSaveOrigin origin = IntegrationSaveOrigin.Web);

    /// <summary>
    /// Coklu form code (ornek: SALES_ORDER_NEW + SALES_ORDER_EDIT) — kayit hem yeni
    /// hem mevcut formdan tetiklenebileceginden ikisini de tarayip OnSave trigger'lari calistirir.
    /// </summary>
    void FireOnSave(IEnumerable<string> formCodes, string recordId, string? triggeredBy = null,
        IntegrationSaveOrigin origin = IntegrationSaveOrigin.Web);
}

/// <summary>
/// Kaydin HANGI istemciden geldigi. OnSave trigger'inin "Mobil dahil" anahtari bu bilgiye
/// gore karar verir: kapaliyken mobil kaynakli kayitlar ANINDA gonderilmez, gonderilmemis
/// olarak kalir ve Aktarim Kuyrugu'nda "Bekleyen" olarak gorunur (kuyruk zaten
/// IntegrationRecordStatus ile LEFT JOIN'lu turetilmis bir gorunumdur — ayri bir kuyruk
/// tablosu YOK, kayit "henuz gonderilmedi" oldugu icin kendiliginden orada belirir).
///
/// Varsayilan [Web]: mevcut cagiranlarin davranisi degismez.
/// </summary>
public enum IntegrationSaveOrigin
{
    Web = 0,
    Mobile = 1,
}
