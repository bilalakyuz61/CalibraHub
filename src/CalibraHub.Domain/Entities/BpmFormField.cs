using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

public sealed class BpmFormField
{
    public int          Id               { get; set; }
    public int          FormDefinitionId { get; init; }
    public string       Key              { get; set; } = "";   // camelCase → NCalc değişken adı
    public string       Label            { get; set; } = "";
    public BpmFieldType FieldType        { get; set; }
    public bool         IsRequired       { get; set; }
    public int          SortOrder        { get; set; }
    public string?      OptionsJson      { get; set; }         // Dropdown → ["A","B","C"]
    public string?      Placeholder      { get; set; }
    public string?      DefaultValue     { get; set; }
    public int          LayoutRow        { get; set; }         // Grid satırı (0-tabanlı)
    public int          LayoutCol        { get; set; }         // Grid sütunu (0-tabanlı)
    public int          LayoutColSpan    { get; set; } = 12;  // Genişlik: 12=tam, 6=yarım, 4=üçte bir
}
