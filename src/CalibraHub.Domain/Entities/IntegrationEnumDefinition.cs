namespace CalibraHub.Domain.Entities;

/// <summary>
/// Bir provider'a ait enum tanimi (orn. Netsis::TFaturaTip). Belirli sayida
/// IntegrationEnumValue alir.
/// </summary>
public sealed class IntegrationEnumDefinition
{
    public int Id { get; set; }
    public int ProviderId { get; set; }
    public required string Code { get; set; }       // "TFaturaTip"
    public required string Label { get; set; }      // "Belge Tipi"
    public string? Description { get; set; }
    public string? SourceInfo { get; set; }

    /// <summary>
    /// Bu enum'un kullanildigi hedef-JSON path listesi (JSON array string).
    /// Ornek: ["FatUst.Tip", "FatUst.FaturaTipi", "Kalemler[].Tip"]
    /// Wizard runtime: bu listedeki her path icin tooltip'inde allowedValues = enum.Values gosterilir.
    /// Bos/null → enum sadece referans olarak durur (tooltip eslesmesi yok).
    /// Yeni model (tek-ekran enum yonetimi): IntegrationFieldDoc.EnumDefinitionId FK'sinin
    /// yerine gecer — her enum, hangi alanlarda kullanildigini KENDI uzerinde tutar.
    /// </summary>
    public string? UsedInFieldPaths { get; set; }

    public bool IsActive { get; set; } = true;

    public string? CreatedBy { get; set; }
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public string? UpdatedBy { get; set; }
    public DateTime? Updated { get; set; }

    public List<IntegrationEnumValue> Values { get; set; } = new();
}

/// <summary>
/// Bir enum tanimi icin tek bir izin verilen deger.
/// </summary>
public sealed class IntegrationEnumValue
{
    public int Id { get; set; }
    public int EnumDefinitionId { get; set; }
    public required string Value { get; set; }      // "7"
    public required string Label { get; set; }      // "Müşteri Siparişi (ftSSip)"
    public string? TechnicalCode { get; set; }      // "ftSSip"
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
