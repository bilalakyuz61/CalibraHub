using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

/// <summary>Belge turu (QUOTE, ORDER, INVOICE, vb.) — Document.DocumentTypeId bu tabloya baglidir.</summary>
public sealed class DocumentType
{
    [Description("Birincil anahtar. IDENTITY.")]
    public int Id { get; init; }

    [Description("Benzersiz kod (QUOTE, ORDER, INVOICE).")]
    public required string Code { get; init; }

    [Description("Kullaniciya gosterilen isim.")]
    public required string Name { get; init; }

    public string? SqlViewName { get; init; }
    public string? Description { get; init; }

    [Description("Soft delete — listede gosterilir mi?")]
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
