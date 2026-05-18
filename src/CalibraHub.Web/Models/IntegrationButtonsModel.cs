namespace CalibraHub.Web.Models;

/// <summary>
/// _IntegrationButtons.cshtml partial'ı için view model.
///
/// Form ekranlarında belirli bir kayıt için aktif Manual entegrasyonları
/// listeleyen butonları render eder. RecordId opsiyonel — bazı entegrasyonlar
/// recordId gerektirmeyebilir (formu butun olarak iste).
///
/// Kullanım örnekleri:
///   @await Html.PartialAsync("_IntegrationButtons",
///       new IntegrationButtonsModel { FormCode = "SALES_ORDER_NEW", RecordId = Model.Id.ToString() })
///
///   @await Html.PartialAsync("_IntegrationButtons",
///       new IntegrationButtonsModel { FormCode = "EINVOICE", RecordId = invoice.Number })
/// </summary>
public sealed class IntegrationButtonsModel
{
    /// <summary>Form kodu (örn. "SALES_ORDER_NEW", "EINVOICE", "ITEMS").</summary>
    public string FormCode { get; init; } = string.Empty;

    /// <summary>İşlenecek kayıt ID/Number. Boş olabilir (form-level çalıştırma).</summary>
    public string? RecordId { get; init; }
}
