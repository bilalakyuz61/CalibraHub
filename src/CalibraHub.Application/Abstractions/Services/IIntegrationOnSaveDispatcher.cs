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
    void FireOnSave(string formCode, string recordId, string? triggeredBy = null);

    /// <summary>
    /// Coklu form code (ornek: SALES_ORDER_NEW + SALES_ORDER_EDIT) — kayit hem yeni
    /// hem mevcut formdan tetiklenebileceginden ikisini de tarayip OnSave trigger'lari calistirir.
    /// </summary>
    void FireOnSave(IEnumerable<string> formCodes, string recordId, string? triggeredBy = null);
}
