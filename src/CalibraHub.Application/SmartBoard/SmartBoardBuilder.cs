// Rapor §2.5 cozumu — 63 ayri Build*BoardConfig metodunda tekrarlanan
// anonymous type boilerplate yerine type-safe fluent builder. Yeni board
// ~10 satirda yazilir; mevcut board'lar kademeli olarak migrate edilir.

namespace CalibraHub.Application.SmartBoard;

/// <summary>
/// SmartBoard (C-Grid) konfig'i icin static giris noktasi.
///
/// Kullanim:
/// <code>
/// var board = SmartBoard.For(machines)
///     .WithBoardKey("logistics-machines")
///     .WithTitle("Makine Tanımlamaları", subtitle: $"{machines.Count} makine")
///     .WithIcon("Cog", "indigo")
///     .WithRefreshUrl("/Logistics/MachinesBoardConfig")
///     .WithEmptyText("Henüz makine tanımlanmamış")
///     .AddHeaderAction("new", "Yeni Makine", "Plus", "/Logistics/MachineEdit")
///     .MapEntities(m =>
///         SmartBoardEntity.For(m.Id, m.MachineName ?? m.MachineCode)
///             .WithDescription(m.LocationCode ?? "")
///             .AddTextWidget("w_status", "Durum",
///                 m.IsActive ? "Aktif" : "Pasif",
///                 color: m.IsActive ? "emerald" : "slate")
///             .WithEditAndDelete(
///                 editUrl:        $"/Logistics/MachineEdit?id={m.Id}",
///                 deleteApiUrl:   $"/Logistics/DeleteMachineJson?id={m.Id}",
///                 deleteConfirm:  $"Silinsin mi? ({m.MachineName})"))
///     .Build();
/// </code>
/// </summary>
public static class SmartBoard
{
    /// <summary>Yeni board builder olustur. Items koleksiyon — entity'ler bu siralamada cizilir.</summary>
    public static SmartBoardBuilder<T> For<T>(IEnumerable<T> items) => new(items);
}

/// <summary>
/// SmartBoard header konfig + entity collection builder (fluent API).
/// Anonim type ile uretilen SmartBoard JSON sozlesmesi: SmartCard / SmartBoard.jsx okuyor.
/// </summary>
public sealed class SmartBoardBuilder<T>
{
    private readonly IEnumerable<T> _items;
    private string _boardKey = "smartboard";
    private string _title = "";
    private string? _subtitle;
    private string? _icon;
    private string? _iconColor;
    private string? _refreshUrl;
    private string? _searchPlaceholder;
    private string? _emptyText;
    private readonly List<object> _headerActions = new();
    private List<object>? _masterWidgets;
    private Func<T, SmartBoardEntityBuilder>? _entityMapper;
    // Tree/master-detail icin opsiyonel ek bolumler
    private readonly Dictionary<string, object?> _extras = new();

    internal SmartBoardBuilder(IEnumerable<T> items) => _items = items;

    public SmartBoardBuilder<T> WithBoardKey(string key) { _boardKey = key; return this; }

    public SmartBoardBuilder<T> WithTitle(string title, string? subtitle = null)
    {
        _title = title;
        _subtitle = subtitle;
        return this;
    }

    public SmartBoardBuilder<T> WithIcon(string icon, string color = "indigo")
    {
        _icon = icon;
        _iconColor = color;
        return this;
    }

    /// <summary>In-place refresh URL — kart toggle/sil sonrasi SmartBoard kendisini bu URL'den yeniler.</summary>
    public SmartBoardBuilder<T> WithRefreshUrl(string url) { _refreshUrl = url; return this; }
    public SmartBoardBuilder<T> WithSearchPlaceholder(string text) { _searchPlaceholder = text; return this; }
    public SmartBoardBuilder<T> WithEmptyText(string text) { _emptyText = text; return this; }

    /// <summary>Header sag bolumdeki action butonu (ornek: "Yeni Makine [+]").</summary>
    public SmartBoardBuilder<T> AddHeaderAction(string id, string label, string icon, string url, string variant = "primary")
    {
        _headerActions.Add(new { id, label, icon, variant, url });
        return this;
    }

    /// <summary>Admin tarafindan tanimlanmis widget seti (form widget'lari) — masterWidgets prop'una geçer.</summary>
    public SmartBoardBuilder<T> WithMasterWidgets(List<object> masterWidgets)
    {
        _masterWidgets = masterWidgets;
        return this;
    }

    /// <summary>Tree/master-detail board'larda ekstra alan ekle (orn. routingMasterWidgets, opFormCode).</summary>
    public SmartBoardBuilder<T> WithExtra(string propertyName, object? value)
    {
        _extras[propertyName] = value;
        return this;
    }

    /// <summary>Her entity icin SmartBoardEntityBuilder dondurun — items siralamasi korunur.</summary>
    public SmartBoardBuilder<T> MapEntities(Func<T, SmartBoardEntityBuilder> mapper)
    {
        _entityMapper = mapper;
        return this;
    }

    /// <summary>Final anonim object — SmartBoard.jsx'in beklediği shape.</summary>
    public object Build()
    {
        var entities = _entityMapper is null
            ? Array.Empty<object>()
            : _items.Select(i => _entityMapper(i).Build()).ToArray();

        // Tutarli shape — bos alanlar atlanmaz; SmartBoard.jsx null'lari handle eder.
        var basePayload = new Dictionary<string, object?>
        {
            ["boardKey"]          = _boardKey,
            ["title"]             = _title,
            ["subtitle"]          = _subtitle,
            ["icon"]              = _icon,
            ["iconColor"]         = _iconColor,
            ["refreshUrl"]        = _refreshUrl,
            ["searchPlaceholder"] = _searchPlaceholder,
            ["emptyText"]         = _emptyText,
            ["actions"]           = _headerActions.Count > 0 ? _headerActions.ToArray() : Array.Empty<object>(),
            ["masterWidgets"]     = _masterWidgets ?? new List<object>(),
            ["entities"]          = entities,
        };
        foreach (var (k, v) in _extras) basePayload[k] = v;
        return basePayload;
    }
}

// ═══════════════════════════════════════════════════════════════════════
// Entity builder
// ═══════════════════════════════════════════════════════════════════════

/// <summary>
/// Tek bir SmartBoard kart entity'sinin fluent builder'i. Widget eklemek + action
/// shortcut'lari (WithEditAndDelete, WithReadOnlyView, WithNavigation) bunda.
/// </summary>
public static class SmartBoardEntity
{
    public static SmartBoardEntityBuilder For(int id, string title, string? subtitle = null)
        => new SmartBoardEntityBuilder().WithId(id).WithTitle(title).WithSubtitle(subtitle);

    public static SmartBoardEntityBuilder For(string id, string title, string? subtitle = null)
        => new SmartBoardEntityBuilder().WithId(id).WithTitle(title).WithSubtitle(subtitle);
}

public sealed class SmartBoardEntityBuilder
{
    private object? _id;
    private string _title = "";
    private string? _subtitle;
    private string? _description;
    private string? _imageUrl;
    private object? _statusBadge;
    private readonly List<object> _widgets = new();
    private object? _primaryAction;
    private object? _secondaryAction;
    private readonly List<object> _extraActions = new();

    internal SmartBoardEntityBuilder() { }

    public SmartBoardEntityBuilder WithId(object id) { _id = id; return this; }
    public SmartBoardEntityBuilder WithTitle(string title) { _title = title; return this; }
    public SmartBoardEntityBuilder WithSubtitle(string? subtitle) { _subtitle = subtitle; return this; }
    public SmartBoardEntityBuilder WithDescription(string? description) { _description = description; return this; }
    public SmartBoardEntityBuilder WithImageUrl(string? url) { _imageUrl = url; return this; }

    /// <summary>Kart ust bolumunde renkli rozet (Aktif/Pasif gibi).</summary>
    public SmartBoardEntityBuilder WithStatusBadge(string label, string color)
    {
        _statusBadge = new { label, color };
        return this;
    }

    /// <summary>Text widget — en yaygin tip. color: emerald/slate/indigo/amber/rose/blue/violet.</summary>
    public SmartBoardEntityBuilder AddTextWidget(string id, string label, string? value,
        string? detail = null, string color = "indigo")
    {
        _widgets.Add(new { id, type = "data", dataType = "text", label, value, detail, color });
        return this;
    }

    /// <summary>Numeric widget — value string olarak formatlanmis sayi.</summary>
    public SmartBoardEntityBuilder AddNumericWidget(string id, string label, string value,
        string? detail = null, string color = "indigo")
    {
        _widgets.Add(new { id, type = "data", dataType = "numeric", label, value, detail, color });
        return this;
    }

    /// <summary>Aktif/Pasif gibi bool widget — boolean'i otomatik renklendirir.</summary>
    public SmartBoardEntityBuilder AddStatusWidget(string id, string label, bool isActive,
        string activeText = "Aktif", string inactiveText = "Pasif")
    {
        _widgets.Add(new
        {
            id,
            type = "data",
            dataType = "text",
            label,
            value = isActive ? activeText : inactiveText,
            detail = (string?)null,
            color = isActive ? "emerald" : "slate",
        });
        return this;
    }

    /// <summary>Hazir widget objelerini direkt ekle (eski helper'lardan migrate ederken).</summary>
    public SmartBoardEntityBuilder AppendWidgets(IEnumerable<object> widgets)
    {
        _widgets.AddRange(widgets);
        return this;
    }

    /// <summary>Yapilandirilmis primaryAction — Duzenle / Goruntule gibi.</summary>
    public SmartBoardEntityBuilder WithPrimaryAction(string label, string icon, string url,
        string color = "amber", bool hideButton = true)
    {
        _primaryAction = new { label, icon, color, url, hideButton };
        return this;
    }

    /// <summary>Kart tiklaminda navigate eden read-only goruntuleme (hideButton=false).</summary>
    public SmartBoardEntityBuilder WithNavigateAction(string label, string icon, string url, string color = "indigo")
    {
        _primaryAction = new { label, icon, color, url, hideButton = false };
        return this;
    }

    /// <summary>Secondary action — Sil / Pasife Al gibi danger islemler.</summary>
    public SmartBoardEntityBuilder WithSecondaryAction(string label, string icon,
        string apiUrl, string apiMethod = "POST", string? confirm = null)
    {
        _secondaryAction = new { label, icon, apiUrl, apiMethod, confirm };
        return this;
    }

    /// <summary>
    /// EN YAYGIN PATTERN — kart "Duzenle" tiklaminda navigate + "Sil" ile API call.
    /// Tek satirla iki action birden tanimlar.
    /// </summary>
    public SmartBoardEntityBuilder WithEditAndDelete(
        string editUrl, string deleteApiUrl, string deleteConfirm,
        string editLabel = "Düzenle", string deleteLabel = "Sil")
    {
        WithPrimaryAction(editLabel, "Edit", editUrl, color: "amber", hideButton: true);
        WithSecondaryAction(deleteLabel, "Trash2", deleteApiUrl, "POST", deleteConfirm);
        return this;
    }

    /// <summary>Kart sag ust kosesinde ek action ikonu (in-line toggle gibi).</summary>
    public SmartBoardEntityBuilder AddExtraAction(string icon, string color, string tooltip,
        string type, string? url = null, string? apiUrl = null, string? confirm = null)
    {
        _extraActions.Add(new { icon, color, tooltip, type, url, apiUrl, confirm });
        return this;
    }

    internal object Build()
    {
        var entity = new Dictionary<string, object?>
        {
            ["id"]              = _id,
            ["title"]           = _title,
            ["subtitle"]        = _subtitle,
            ["description"]     = _description ?? string.Empty,
            ["imageUrl"]        = _imageUrl,
            ["statusBadge"]     = _statusBadge,
            ["widgets"]         = _widgets.ToArray(),
            ["primaryAction"]   = _primaryAction,
            ["secondaryAction"] = _secondaryAction,
        };
        if (_extraActions.Count > 0)
            entity["extraActions"] = _extraActions.ToArray();
        return entity;
    }
}
