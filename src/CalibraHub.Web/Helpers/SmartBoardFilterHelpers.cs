using CalibraHub.Application.Contracts;

namespace CalibraHub.Web.Helpers;

/// <summary>
/// 2026-05-24: CalibraSmartBoard (C-Grid) tum liste ekranlarinda filtre panelinin
/// tutarli calismasi icin ortak yardimcilar.
///
/// CalismaPattern:
///   1) Admin form widget'lari (dbo.WidgetMas) — BuildAdminFormWidgets() ile
///      Dictionary olarak serialize edilir. dropdown / multi-select tipleri icin
///      Options array'i {value,label} formuna donusturulur (filter combobox icin).
///      Her widget source="widget" etiketiyle filter panelin "Widget Alanlar (form)"
///      grubuna duser.
///
///   2) Sistem alanlari (controller'in kendi yazdigi w_xxx widget'lari) — MakeStdWidget()
///      ile group="standardalanlar", groupLabel="Standart Alanlar" alti collapsible.
///
///   3) Multi-select / options widget'lari — MakeOptionsWidget() ile {value,label}[]
///      seceneklerle birlikte.
///
/// Dictionary kullanma sebebi: System.Text.Json'un List&lt;object&gt; icindeki heterojen
/// anonymous type'larda bazi properties'i drop etme sorununa karsi. Dictionary her
/// zaman tum keys'i serialize eder.
/// </summary>
public static class SmartBoardFilterHelpers
{
    public const string STANDARD_GROUP_KEY   = "standardalanlar";
    public const string STANDARD_GROUP_LABEL = "Standart Alanlar";

    /// <summary>
    /// Admin Form Tasarimi'ndan gelen widget'lari filter panel + display icin Dictionary
    /// listesine cevirir. group/grid haric tum aktif widget'lari dahil eder.
    /// dropdown / multi-select tipleri icin Options → {value,label}[] donusumu.
    /// </summary>
    public static List<object> BuildAdminFormWidgets(WidgetFormSchemaDto? schema)
    {
        var result = new List<object>();
        if (schema == null) return result;
        foreach (var w in schema.Widgets.Where(w => w.IsActive
            && !string.Equals(w.DataType, "group", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(w.DataType, "grid",  StringComparison.OrdinalIgnoreCase)))
        {
            var dt = w.DataType.ToLowerInvariant();
            object? optionsArray = null;
            if ((dt == "dropdown" || dt == "multi-select" || dt == "multi_select" || dt == "multiselect")
                && w.Options != null && w.Options.Count > 0)
            {
                optionsArray = w.Options.Select(s => (object)new Dictionary<string, object?> {
                    ["value"] = s, ["label"] = s,
                }).ToList();
            }
            var wd = new Dictionary<string, object?>
            {
                ["id"]           = w.WidgetCode,
                ["dbId"]         = w.Id,
                ["isPlainField"] = w.IsPlainField,
                ["type"]         = "data",
                ["dataType"]     = dt,
                ["label"]        = w.Label,
                ["source"]       = "widget",
            };
            if (optionsArray != null) wd["options"] = optionsArray;
            result.Add(wd);
        }
        return result;
    }

    /// <summary>
    /// Sistem widget'i (controller'in kendi yazdigi alan) — "Standart Alanlar" collapsible
    /// altinda gosterilir. Default group ataniyor.
    /// </summary>
    public static Dictionary<string, object?> MakeStdWidget(
        string id, string label, string dataType,
        string group = STANDARD_GROUP_KEY, string groupLabel = STANDARD_GROUP_LABEL,
        IReadOnlyList<object>? options = null)
    {
        var d = new Dictionary<string, object?>
        {
            ["id"]           = id,
            ["dbId"]         = (int?)null,
            ["isPlainField"] = false,
            ["type"]         = "data",
            ["dataType"]     = dataType,
            ["label"]        = label,
            ["source"]       = "standard",
            ["group"]        = group,
            ["groupLabel"]   = groupLabel,
        };
        if (options != null) d["options"] = options;
        return d;
    }

    /// <summary>Multi-select widget — options dataType, custom group destegi.</summary>
    public static Dictionary<string, object?> MakeOptionsWidget(
        string id, string label, IReadOnlyList<object> options,
        string group = STANDARD_GROUP_KEY, string groupLabel = STANDARD_GROUP_LABEL)
        => MakeStdWidget(id, label, "options", group, groupLabel, options);

    /// <summary>
    /// String listeyi {value,label} formuna cevir — multi-select / dropdown widget'larin
    /// options array'i icin (value=label).
    /// </summary>
    public static List<object> ToOptionsList(IEnumerable<string> values)
        => values.Select(s => (object)new Dictionary<string, object?> {
            ["value"] = s, ["label"] = s,
        }).ToList();

    /// <summary>{value, label} farkli olan listeler icin.</summary>
    public static List<object> ToOptionsList(IEnumerable<(string Value, string Label)> pairs)
        => pairs.Select(p => (object)new Dictionary<string, object?> {
            ["value"] = p.Value, ["label"] = p.Label,
        }).ToList();
}
