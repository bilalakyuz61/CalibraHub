namespace CalibraHub.Domain.Entities;

/// <summary>
/// Bir provider+resource+field icin dokumantasyon kaydi. Wizard Step 2'de
/// hedef path'in basina 'i' badge olarak gosterilir; admin UI'dan editlenir.
/// </summary>
public sealed class IntegrationFieldDoc
{
    public int Id { get; set; }
    public int ProviderId { get; set; }
    public required string Resource { get; set; }       // "ItemSlips"
    public required string FieldPath { get; set; }      // "FatUst.Tip" or "Kalems[].StokKodu"
    public string? Label { get; set; }                  // "Belge Tipi"
    public string? Description { get; set; }
    public string? Example { get; set; }
    public string? Notes { get; set; }
    /// <summary>Opsiyonel — alan bir enum referansi tasiyorsa FK.</summary>
    public int? EnumDefinitionId { get; set; }
    public bool IsRequired { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public string? CreatedBy { get; set; }
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public string? UpdatedBy { get; set; }
    public DateTime? Updated { get; set; }
}
