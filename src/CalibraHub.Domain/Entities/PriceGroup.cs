using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

[Description("Fiyat gruplari (bayilik/perakende/VIP/kampanya vb.). Contact.PriceGroupId ile cariler bu gruba baglanir, PriceList satirlari da bu grupla eslesir.")]
public sealed class PriceGroup
{
    public int Id { get; set; }
    public required string GroupCode { get; set; }
    public required string GroupName { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
