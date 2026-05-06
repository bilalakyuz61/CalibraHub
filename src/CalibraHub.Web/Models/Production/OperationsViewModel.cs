namespace CalibraHub.Web.Models.Production;

public sealed class OperationsViewModel
{
    /// <summary>Server-side hazırlanan SmartBoard config'i (CalibraSmartBoard React component'ı için).</summary>
    public object? BoardConfig { get; init; }
}

public sealed class WorkOrdersViewModel
{
    public object? BoardConfig { get; init; }
}

public sealed class PersonnelViewModel
{
    public object? BoardConfig { get; init; }
}

public sealed class RoutingsViewModel
{
    public object? BoardConfig { get; init; }
}
