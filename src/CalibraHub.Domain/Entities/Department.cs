using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

public sealed class Department : Entity
{
    public int CompanyId { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public Guid? ParentDepartmentId { get; init; }
    public bool IsActive { get; private set; } = true;

    public void Deactivate() => IsActive = false;
}
