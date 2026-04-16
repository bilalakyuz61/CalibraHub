namespace CalibraHub.Domain.Entities;

/// <summary>
/// SQL View tabanli jenerik rehber (Lookup / List-Of-Values) tanimi.
///
/// Her satir bir ERP rehberini tanimlar: hangi SQL View'a bakilacagi,
/// kullaniciya hangi kolonun gosterilecegi (DisplayColumn), WidgetTra'ya
/// hangi degerin yazilacagi (ValueColumn), ve arama/grid icin hangi
/// kolonlarin kullanilacagi (GridColumnsJson).
///
/// Properties Dapper okumasi ile uyumlu olsun diye `set`'li (init degil).
/// </summary>
public sealed class GuideDefinition
{
    public int Id { get; set; }
    public string GuideCode { get; set; } = string.Empty;
    public string GuideLabel { get; set; } = string.Empty;
    public string ViewName { get; set; } = string.Empty;
    public string ValueColumn { get; set; } = string.Empty;
    public string DisplayColumn { get; set; } = string.Empty;
    public string GridColumnsJson { get; set; } = "[]";
    public string? DefaultSortColumn { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
