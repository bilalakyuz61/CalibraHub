using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

public sealed class RptDefinitionRole
{
    public int Id { get; set; }
    public int DefId { get; set; }
    public UserRole Role { get; set; }
    public bool CanView { get; set; } = true;
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
}
