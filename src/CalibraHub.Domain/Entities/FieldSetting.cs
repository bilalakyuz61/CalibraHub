namespace CalibraHub.Domain.Entities;

/// <summary>
/// FldSet — Form sabit alanlarinin rehber eslestirmesi ve ayarlari.
///
/// Her satir bir formun (dbo.Forms) belirli bir HTML input alanini tanimlar.
/// GuideCode dolu ise o alana rehber lookup davranisi uygulanir (readonly + arama).
/// FilterJson, rehber arama sirasinda uygulanacak cascading constraint JSON'unu tutar.
///
/// Properties ADO.NET reader ile uyumlu olsun diye set'li (init degil).
/// </summary>
public sealed class FieldSetting
{
    public int Id { get; set; }
    public int FormId { get; set; }
    public string FieldKey { get; set; } = string.Empty;
    public string FieldLabel { get; set; } = string.Empty;
    public string? GuideCode { get; set; }
    public string? FilterJson { get; set; }
    public bool IsRequired { get; set; }
    public string? FormatJson { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
