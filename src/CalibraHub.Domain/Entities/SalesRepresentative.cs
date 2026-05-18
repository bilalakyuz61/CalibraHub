namespace CalibraHub.Domain.Entities;

public sealed class SalesRepresentative
{
    public int Id { get; init; }
    public string RepName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
