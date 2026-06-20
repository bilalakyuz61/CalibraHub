using System.Text.Json;

namespace CalibraHub.Application.Contracts;

/// <summary>
/// 2026-06-14 — Özelleştirilebilir Ana Sayfa Panosu (Home Dashboard) DTO'ları.
///
/// Pano, React Shell'in "tab açık değil" (EmptyState) slotuna gömülür. Layout
/// kullanıcı bazlı JSON olarak <c>user_settings</c> tablosunda <c>"dashboard_layout"</c>
/// anahtarı altında saklanır. Widget kataloğu kod-tanımlı statik registry'dir
/// (<see cref="CalibraHub.Application.Services.Dashboard.DashboardWidgetCatalog"/>).
///
/// Görünürlük her yüklemede canlı izinlere göre yeniden hesaplanır — saklı JSON
/// yalnızca sıra + boyut + widget bazlı ayar için güven kaynağıdır (authz değil).
/// </summary>

// ── Katalog: hangi widget'lar EKLENEBİLİR (izin filtresinden sonra) ──
public sealed record DashboardWidgetCatalogItemDto(
    string Type,                 // "pending-approvals"
    string Title,                // "Onayda Bekleyenler"
    string Description,          // katalog modalı için kısa yardım metni
    string Icon,                 // Lucide adı, örn. "Inbox"
    string IconColor,            // palet token: indigo/emerald/amber/rose/blue/violet/slate
    string DefaultSize,          // "sm" | "md" | "lg"
    bool AllowMultiple);         // kullanıcı >1 ekleyebilir mi?

// ── Kullanıcının layout'undaki yerleştirilmiş bir widget (katalog + kullanıcı tercihleri) ──
public sealed record DashboardWidgetInstanceDto(
    string Type,
    string Size,
    JsonElement? Settings,       // opak widget-bazlı ayar bloğu
    int? Height = null,          // opsiyonel yükseklik çarpanı: 1-3 (null = varsayılan)
    JsonElement? Layout = null); // react-grid-layout pozisyonu: {x,y,w,h}

// ── Çok sayfalı pano içinde tek sayfa ──
public sealed record DashboardPageDto(
    string Id,
    string Label,
    IReadOnlyList<DashboardWidgetInstanceDto> Widgets);

// ── Mount anında React panosuna dönen tam payload ──
public sealed record DashboardConfigDto(
    IReadOnlyList<DashboardPageDto> Pages,                 // çok sayfalı layout (sıralı, izin-filtreli)
    IReadOnlyList<DashboardWidgetCatalogItemDto> Catalog,  // eklenebilir widget'lar (izin-filtreli)
    IReadOnlyList<QuickLinkOptionDto> QuickLinkOptions);   // yetkili menü yaprakları (düzleştirilmiş)

// ── Kısayol seçici seçenekleri = yetkili menü yaprakları (url != null) ──
public sealed record QuickLinkOptionDto(
    string Key, string Label, string Url, string? Icon, string GroupLabel);

// ── Client'tan gelen çok sayfalı kayıt isteği ──
public sealed record SaveDashboardPagesRequest(
    IReadOnlyList<DashboardPageDto> Pages);

// ── Widget bazlı veri DTO'ları (veri endpoint'lerinin döndürdüğü) ──
public sealed record PendingApprovalsWidgetDto(int TotalCount, string Url);

public sealed record ExchangeRateWidgetItemDto(
    string Code, string Name, string? Symbol,
    decimal? Buying, decimal? Selling, DateTime? RateDate,
    string? TrendVsPrev);        // "up" | "down" | "flat" | null

public sealed record RecentDocumentWidgetItemDto(
    string DocumentNumber, string DocumentTypeName, string? ContactName,
    decimal? GrandTotal, string? Currency, DateTime DocDate, string Url);

public sealed record WorkOrderSummaryWidgetDto(
    int Planned, int Released, int InProgress, int Completed, int TotalActive, string Url);

public sealed record SalesQuoteSummaryWidgetDto(
    int Draft, int Pending, int Approved, int Total, decimal? OpenTotal, string? Currency, string Url);

public sealed record StockAlertWidgetItemDto(
    string ItemCode, string ItemName, decimal OnHand, decimal MinLevel, string? UnitCode, string Url);
