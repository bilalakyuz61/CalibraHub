using System.ComponentModel.DataAnnotations;
using CalibraHub.Application.Contracts;

namespace CalibraHub.Web.Models;

public sealed class SalesRepresentativeViewModel
{
    public IReadOnlyCollection<SalesRepresentativeDto> Items { get; init; } = [];
    public SalesRepresentativeInput Input { get; init; } = new();
    public string? Search { get; init; }
}

public sealed class SalesRepresentativeInput
{
    public int? Id { get; set; }
    [Required, MaxLength(200)] public string RepName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class SalesRepSmartBoardViewModel
{
    public object? BoardConfig { get; init; }
}

public sealed class SalesRepEditViewModel
{
    public int?   Id       { get; init; }
    public string RepName  { get; init; } = "";
    public bool   IsActive { get; init; } = true;
}
