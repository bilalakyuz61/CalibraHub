using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Services.Dashboard;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Services;

/// <summary>
/// 2026-06-14 — Ana Sayfa Panosu servisi implementasyonu.
///
/// Bağımlılıkların tamamı DI'da kayıtlı. Layout JSON'u IUserSettingRepository
/// üzerinden okunur/yazılır; izin filtresi IPermissionService.CheckAnyAsync ile
/// (MenuDefinition ile aynı OR-action seti) yapılır; widget verileri mevcut
/// servislerden (onay/döviz/iş emri/belge) toplanır.
///
/// Stok uyarıları için henüz canlı stok-seviyesi veri kaynağı yok — boş liste
/// döner (widget "yapılandırılmadı" boş durumu gösterir).
/// </summary>
public sealed class DashboardService : IDashboardService
{
    private const string LayoutSettingKey = "dashboard_layout";

    private readonly IUserSettingRepository _userSettings;
    private readonly IPermissionService _permissions;
    private readonly IPendingApprovalService _pendingApprovals;
    private readonly ICurrencyService _currencyService;
    private readonly IExchangeRateRepository _exchangeRates;
    private readonly IDocumentService _documents;
    private readonly IWorkOrderService _workOrders;

    public DashboardService(
        IUserSettingRepository userSettings,
        IPermissionService permissions,
        IPendingApprovalService pendingApprovals,
        ICurrencyService currencyService,
        IExchangeRateRepository exchangeRates,
        IDocumentService documents,
        IWorkOrderService workOrders)
    {
        _userSettings = userSettings;
        _permissions = permissions;
        _pendingApprovals = pendingApprovals;
        _currencyService = currencyService;
        _exchangeRates = exchangeRates;
        _documents = documents;
        _workOrders = workOrders;
    }

    // ════════════════════════════════════════════════════════════════
    // Config / layout
    // ════════════════════════════════════════════════════════════════

    public async Task<DashboardConfigDto> GetConfigAsync(int userId, UserRole role, int? departmentId, CancellationToken ct)
    {
        // 1) Saklı JSON (veya null) — v1 otomatik migrate edilir
        var json = await _userSettings.GetAsync(userId, LayoutSettingKey, ct);
        var savedPages = DashboardLayoutSerializer.TryParsePages(json);

        // 2) Temel sayfa seçimi
        var basePages = savedPages ?? DashboardWidgetCatalog.DefaultPages();

        // 3) İzin-filtresi: her sayfadaki bilinmeyen / izinsiz widget'ları at
        var allowedTypes = await ResolveAllowedTypesAsync(userId, role, departmentId, ct);
        var pages = basePages.Select(p => new DashboardPageDto(
            p.Id,
            p.Label,
            p.Widgets
                .Where(w => DashboardWidgetCatalog.Find(w.Type) is not null && allowedTypes.Contains(w.Type))
                .Select(NormalizeInstance)
                .ToList()
        )).ToList();

        // 4) Eklenebilir katalog: izinli, AllowMultiple=false ise hiçbir sayfada yerleştirilmemiş olanlar
        var allPlaced = pages
            .SelectMany(p => p.Widgets.Select(w => w.Type))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var catalog = DashboardWidgetCatalog.All
            .Where(e => allowedTypes.Contains(e.Type))
            .Where(e => e.AllowMultiple || !allPlaced.Contains(e.Type))
            .Select(e => new DashboardWidgetCatalogItemDto(
                e.Type, e.Title, e.Description, e.Icon, e.IconColor, e.DefaultSize, e.AllowMultiple))
            .ToList();

        // 5) Quick-link seçenekleri: Web katmanı (MenuDefinition) doldurur — burada boş.
        return new DashboardConfigDto(pages, catalog, Array.Empty<QuickLinkOptionDto>());
    }

    public async Task SavePagesAsync(int userId, SaveDashboardPagesRequest request, CancellationToken ct)
    {
        // Yalnız bilinen tipleri ve geçerli boyutları sakla — çöp JSON birikmesin.
        var pages = (request?.Pages ?? Array.Empty<DashboardPageDto>())
            .Select(p => new DashboardPageDto(
                p.Id,
                p.Label,
                p.Widgets
                    .Where(w => DashboardWidgetCatalog.Find(w.Type) is not null)
                    .Select(NormalizeInstance)
                    .ToList()
            ))
            .ToList();

        var json = DashboardLayoutSerializer.SerializePages(pages);
        await _userSettings.SetAsync(userId, LayoutSettingKey, json, ct);
    }

    public async Task<DashboardConfigDto> ResetLayoutAsync(int userId, UserRole role, int? departmentId, CancellationToken ct)
    {
        // null → MERGE setting_value = NULL; GetAsync sonra null döner (≡ saklı düzen yok).
        await _userSettings.SetAsync(userId, LayoutSettingKey, null, ct);
        return await GetConfigAsync(userId, role, departmentId, ct);
    }

    public async Task<bool> CanSeeWidgetAsync(int userId, UserRole role, int? departmentId, string widgetType, CancellationToken ct)
    {
        var entry = DashboardWidgetCatalog.Find(widgetType);
        if (entry is null) return false;
        if (string.IsNullOrEmpty(entry.PermissionFormCode)) return true;     // kapısız widget
        if (role == UserRole.SystemAdmin) return true;                       // admin kısayolu
        return await _permissions.CheckAnyAsync(
            userId, role, departmentId, entry.PermissionFormCode, entry.PermissionActions, ct);
    }

    /// <summary>Katalogdaki her girdi için tek tek izin probu — sonucu set olarak döner.</summary>
    private async Task<HashSet<string>> ResolveAllowedTypesAsync(int userId, UserRole role, int? departmentId, CancellationToken ct)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in DashboardWidgetCatalog.All)
        {
            if (await CanSeeWidgetAsync(userId, role, departmentId, entry.Type, ct))
                allowed.Add(entry.Type);
        }
        return allowed;
    }

    private static DashboardWidgetInstanceDto NormalizeInstance(DashboardWidgetInstanceDto w)
    {
        var size = w.Size?.Trim().ToLowerInvariant() switch
        {
            "sm" => "sm",
            "lg" => "lg",
            _ => "md",
        };
        return w with { Size = size };
    }

    // ════════════════════════════════════════════════════════════════
    // Widget verileri
    // ════════════════════════════════════════════════════════════════

    public async Task<PendingApprovalsWidgetDto> GetPendingApprovalsAsync(CancellationToken ct)
    {
        // Index.cshtml ile aynı: "mine" scope, grupların Count toplamı.
        var groups = await _pendingApprovals.GetGroupsAsync(PendingApprovalScope.Mine, ct);
        var total = groups.Sum(g => g.Count);
        return new PendingApprovalsWidgetDto(total, "/PendingApproval");
    }

    public async Task<IReadOnlyList<ExchangeRateWidgetItemDto>> GetExchangeRatesAsync(IReadOnlyList<string> codes, CancellationToken ct)
    {
        var wanted = (codes is { Count: > 0 } ? codes : new[] { "USD", "EUR", "GBP" })
            .Select(c => c.Trim().ToUpperInvariant())
            .Where(c => c.Length > 0)
            .Distinct()
            .ToList();

        // Tanımlı para birimleri (display: ad + sembol)
        var currencies = await _currencyService.GetAllAsync(ct);
        var currencyByCode = currencies.ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);

        // En güncel kurlar + trend için bir önceki günün kurları
        var latest = await _exchangeRates.GetLatestRatesAsync(ct);
        var latestByCode = latest.ToDictionary(r => r.CurrencyCode, StringComparer.OrdinalIgnoreCase);

        // Trend: son kur tarihinden bir gün önceki kayıt (yoksa flat/null)
        var anchorDate = latest.Count > 0 ? latest.Max(r => r.Date.Date) : DateTime.Today;
        var prev = await _exchangeRates.GetRatesForDateAsync(anchorDate.AddDays(-1), ct);
        var prevByCode = prev.ToDictionary(r => r.CurrencyCode, StringComparer.OrdinalIgnoreCase);

        var items = new List<ExchangeRateWidgetItemDto>();
        foreach (var code in wanted)
        {
            // 2026-06-19: Currencies tablosunda tanımlı olmasa bile widget'a ekle —
            // ad/sembol bilinmiyorsa code'un kendisi gösterilir, kur "—". Aksi
            // takdirde kullanıcı widget ayarlarından seçtiği CHF/JPY/RUB vb. silindi
            // sanıyordu (sessizce atlanıyordu).
            currencyByCode.TryGetValue(code, out var cur);
            latestByCode.TryGetValue(code, out var rate);
            prevByCode.TryGetValue(code, out var prevRate);

            string? trend = null;
            if (rate is not null && prevRate is not null)
            {
                trend = rate.SellingRate > prevRate.SellingRate ? "up"
                      : rate.SellingRate < prevRate.SellingRate ? "down"
                      : "flat";
            }

            items.Add(new ExchangeRateWidgetItemDto(
                Code: cur?.Code ?? code,
                Name: cur?.Name ?? code,
                Symbol: cur?.Symbol ?? string.Empty,
                Buying: rate?.BuyingRate,
                Selling: rate?.SellingRate,
                RateDate: rate?.Date,
                TrendVsPrev: trend));
        }

        return items;
    }

    public async Task<IReadOnlyList<RecentDocumentWidgetItemDto>> GetRecentDocumentsAsync(int take, CancellationToken ct)
    {
        if (take <= 0) take = 8;

        // GetQuotesAsync zaten Created DESC sıralı döner (SqlDocumentRepository).
        var quotes = await _documents.GetQuotesAsync(null, null, ct);

        return quotes
            .Take(take)
            .Select(q => new RecentDocumentWidgetItemDto(
                DocumentNumber: q.DocumentNumber,
                DocumentTypeName: "Satış Teklifi",
                ContactName: q.ContactName,
                GrandTotal: q.GrandTotal,
                Currency: q.CurrencyCode,
                DocDate: q.DocumentDate,
                Url: $"/Sales/Quotes?focus={q.Id}"))
            .ToList();
    }

    public async Task<WorkOrderSummaryWidgetDto> GetWorkOrderSummaryAsync(CancellationToken ct)
    {
        // ListAsync(null) — repository Cancelled/Closed haricini döndürür (aktif emirler).
        var orders = await _workOrders.ListAsync(null, ct);

        int planned = orders.Count(o => o.Status == WorkOrderStatus.Planned);
        int released = orders.Count(o => o.Status == WorkOrderStatus.Released);
        int inProgress = orders.Count(o => o.Status == WorkOrderStatus.InProgress);
        int completed = orders.Count(o => o.Status == WorkOrderStatus.Completed);

        return new WorkOrderSummaryWidgetDto(
            Planned: planned,
            Released: released,
            InProgress: inProgress,
            Completed: completed,
            TotalActive: orders.Count,
            Url: "/Production/WorkOrders");
    }

    public async Task<SalesQuoteSummaryWidgetDto> GetSalesQuoteSummaryAsync(CancellationToken ct)
    {
        var quotes = await _documents.GetQuotesAsync(null, null, ct);

        // Document.status string'i (Draft/Sent/Approved/...) — DocumentStatus enum'una karşılık gelir.
        int draft = quotes.Count(q => IsStatus(q.Status, "Draft"));
        int pending = quotes.Count(q => IsStatus(q.Status, "Sent"));
        int approved = quotes.Count(q => IsStatus(q.Status, "Approved"));

        // Açık teklif tutarı: henüz dönüştürülmemiş/iptal edilmemiş/reddedilmemiş olanlar.
        var openQuotes = quotes
            .Where(q => IsStatus(q.Status, "Draft") || IsStatus(q.Status, "Sent") || IsStatus(q.Status, "Approved"))
            .ToList();
        decimal? openTotal = openQuotes.Count > 0 ? openQuotes.Sum(q => q.GrandTotal) : null;

        // Birim para — açık tekliflerde tek para birimi varsa onu göster, karışıksa null.
        var currencies = openQuotes
            .Select(q => q.CurrencyCode)
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct()
            .ToList();
        string? currency = currencies.Count == 1 ? currencies[0] : null;

        return new SalesQuoteSummaryWidgetDto(
            Draft: draft,
            Pending: pending,
            Approved: approved,
            Total: quotes.Count,
            OpenTotal: openTotal,
            Currency: currency,
            Url: "/Sales/Quotes");
    }

    public Task<IReadOnlyList<StockAlertWidgetItemDto>> GetStockAlertsAsync(int take, CancellationToken ct)
    {
        // Henüz canlı stok-seviyesi + minimum-seviye veri kaynağı yok.
        // Boş liste → widget "uyarı yok / yapılandırılmadı" boş durumu gösterir.
        IReadOnlyList<StockAlertWidgetItemDto> empty = Array.Empty<StockAlertWidgetItemDto>();
        return Task.FromResult(empty);
    }

    private static bool IsStatus(string? value, string expected) =>
        string.Equals(value?.Trim(), expected, StringComparison.OrdinalIgnoreCase);
}
