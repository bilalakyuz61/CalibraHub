using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

[Description("Bir cariye ait birden fazla teslim/fatura adresi. Her adresin kullanici tarafindan verilmis bir ismi olur (ornegin 'Merkez', 'Depo', 'Sube-Ankara').")]
public sealed class ContactAddress
{
    public int Id { get; init; }

    /// <summary>Bagli oldugu cari. FK -> Contact.Id</summary>
    public int ContactId { get; init; }

    /// <summary>Kullanici tarafindan verilen adres ismi (ornegin: Merkez, Depo, Sube-Ankara).</summary>
    public required string Name { get; init; }

    public string? CountryCode { get; init; } = "TR";
    public string? CityName { get; init; }
    public string? DistrictName { get; init; }
    public string? NeighborhoodName { get; init; }
    public string? PostalCode { get; init; }

    /// <summary>Sokak + bina no + daire vb. serbest metin.</summary>
    public string? AddressLine { get; init; }

    /// <summary>Cari icin varsayilan teslim adresi mi?</summary>
    public bool IsDefault { get; init; }

    public DateTime CreatedAt { get; init; }
}
