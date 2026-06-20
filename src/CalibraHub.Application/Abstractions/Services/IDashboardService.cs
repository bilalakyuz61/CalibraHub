using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// 2026-06-14 — Ana Sayfa Panosu (Home Dashboard) servisi.
///
/// Layout (sıra + boyut + widget ayarları) kullanıcı bazlı JSON olarak
/// <c>user_settings."dashboard_layout"</c> içinde saklanır. Görünürlük her
/// yüklemede canlı izinlere göre yeniden hesaplanır — saklı JSON authz için
/// güven kaynağı değildir.
///
/// Quick-link seçenekleri (menü URL'leri Web katmanındaki MenuDefinition'da
/// olduğundan) bu servis tarafından üretilmez; controller üretip
/// <see cref="GetConfigAsync"/> sonucuna eklenir.
/// </summary>
public interface IDashboardService
{
    // ── Config / layout ──
    /// <summary>
    /// Kullanıcının izin-filtreli layout'u + eklenebilir katalog döner.
    /// QuickLinkOptions burada boş gelir — controller doldurur.
    /// </summary>
    Task<DashboardConfigDto> GetConfigAsync(int userId, UserRole role, int? departmentId, CancellationToken ct);

    /// <summary>Kullanıcı sayfa düzenini JSON olarak kalıcılaştırır.</summary>
    Task SavePagesAsync(int userId, SaveDashboardPagesRequest request, CancellationToken ct);

    /// <summary>Saklı layout'u siler ve varsayılanı (izin-filtreli) döner.</summary>
    Task<DashboardConfigDto> ResetLayoutAsync(int userId, UserRole role, int? departmentId, CancellationToken ct);

    /// <summary>Tek widget izin probu — veri endpoint'leri forged istekleri reddetmek için kullanır.</summary>
    Task<bool> CanSeeWidgetAsync(int userId, UserRole role, int? departmentId, string widgetType, CancellationToken ct);

    // ── Widget bazlı veri toplama ──
    Task<PendingApprovalsWidgetDto> GetPendingApprovalsAsync(CancellationToken ct);
    Task<IReadOnlyList<ExchangeRateWidgetItemDto>> GetExchangeRatesAsync(IReadOnlyList<string> codes, CancellationToken ct);
    Task<IReadOnlyList<RecentDocumentWidgetItemDto>> GetRecentDocumentsAsync(int take, CancellationToken ct);
    Task<WorkOrderSummaryWidgetDto> GetWorkOrderSummaryAsync(CancellationToken ct);
    Task<SalesQuoteSummaryWidgetDto> GetSalesQuoteSummaryAsync(CancellationToken ct);
    Task<IReadOnlyList<StockAlertWidgetItemDto>> GetStockAlertsAsync(int take, CancellationToken ct);
}
