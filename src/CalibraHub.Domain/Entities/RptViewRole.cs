using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

public sealed class RptViewRole
{
    public int Id { get; set; }
    public int ViewId { get; set; }
    public UserRole Role { get; set; }
    public bool CanQuery { get; set; } = true;
    public bool CanDesign { get; set; }
}
