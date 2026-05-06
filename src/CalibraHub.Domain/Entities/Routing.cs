using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

[Description("Üretim rotası — bir mamulün operasyon dizisini tanımlar. Her rota bir veya birden fazla RoutingOperation içerir. ItemId+ConfigId opsiyonel: dolu ise belirli ürüne özel rota; boş ise sözlük (genel rota şablonu).")]
public sealed class Routing
{
    public int Id { get; init; }
    public int CompanyId { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public int? ItemId { get; init; }
    public int? ConfigId { get; init; }
    public string? Description { get; init; }
    public bool IsActive { get; init; } = true;
    public DateTime Created { get; init; }
    public DateTime? Updated { get; init; }
}
