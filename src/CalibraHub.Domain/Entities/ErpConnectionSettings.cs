using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

public sealed class ErpConnectionSettings : Entity
{
    public int CompanyId { get; init; }
    public string Provider { get; init; } = "Netsis";
    public required string Company { get; init; }
    public required string Business { get; init; }
    public required string Branch { get; init; }
    public required string Username { get; init; }
    public required string Password { get; init; }
    public bool IsActive { get; private set; } = true;
    public DateTime Created { get; init; } = DateTime.Now;
    public DateTime Updated { get; private set; } = DateTime.Now;

    public void Deactivate()
    {
        IsActive = false;
        Updated = DateTime.Now;
    }
}
