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

    /// <summary>
    /// Rehber bazında varsayılan SQL WHERE fragment'ı. Bu rehberin kullanıldığı
    /// her formda, FldSet'in field-level filtresinden bağımsız olarak otomatik
    /// uygulanır (AND ile birleşir). Örn: cbv_Guide_Items için "TYPID IN (2,3)"
    /// — yarı mamul/mamul filtrelemesi her yerde geçerli.
    /// Token desteği yok — bu seviye global, tablodan tabloya değişmez.
    /// </summary>
    public string? DefaultFilterJson { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
