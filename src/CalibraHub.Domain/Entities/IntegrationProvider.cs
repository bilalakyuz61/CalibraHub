namespace CalibraHub.Domain.Entities;

/// <summary>
/// Entegrasyon provider'i (Netsis, Logo, SAP, CustomREST...). Tum enum tanimlari
/// ve alan aciklamalari bir provider'a baglanir — namespace gibi davranir.
/// </summary>
public sealed class IntegrationProvider
{
    public int Id { get; set; }
    public required string Code { get; set; }       // "Netsis", "Logo"
    public required string Label { get; set; }      // "Netsis NetOpenX"
    public string? Description { get; set; }
    public string? SourceInfo { get; set; }         // "Interop.NetOpenX50.dll v8.0.6"
    public string? IconColor { get; set; } = "indigo";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public string? CreatedBy { get; set; }
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public string? UpdatedBy { get; set; }
    public DateTime? Updated { get; set; }
}
