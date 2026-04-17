namespace CalibraHub.Domain.Entities;

/// <summary>Cari hesap — müşteri, satıcı veya her ikisi.</summary>
public sealed class Contact
{
    public int Id { get; init; }

    /// <summary>1 = Müşteri, 2 = Satıcı, 3 = Her İkisi</summary>
    public byte AccountType { get; init; }

    public required string AccountCode { get; init; }
    public required string AccountTitle { get; init; }
    public string? TaxNumber { get; init; }      // Vergi Numarası
    public string? IdentityNumber { get; init; } // TC Kimlik No
    public string? TaxOffice { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? Address { get; init; }
    public string? City { get; init; }
    public string? District { get; init; }
    public bool IsActive { get; init; } = true;
    public int? PriceGroupId { get; init; }
    public DateTime CreatedAt { get; init; }
}
