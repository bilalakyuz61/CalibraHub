using System.ComponentModel.DataAnnotations;
using CalibraHub.Application.Contracts;

namespace CalibraHub.Web.Models.Admin;

public sealed class IntegrationEventsViewModel
{
    public IReadOnlyCollection<IntegrationEventDefinitionDto> Definitions { get; init; } = [];
    public IReadOnlyCollection<IntegrationEventLogDto> Logs { get; init; } = [];
    public IntegrationEventInput Input { get; init; } = new();
}

public sealed class IntegrationEventInput
{
    public Guid? Id { get; set; }

    [Required(ErrorMessage = "Tanim adi zorunludur.")]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Event kaynagi zorunludur.")]
    public string EventSource { get; set; } = string.Empty;

    [Required(ErrorMessage = "Event tipi zorunludur.")]
    public string EventType { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? EventDetail { get; set; }

    public string? SqlCommand { get; set; }

    public bool StopOnError { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public int ExecutionOrder { get; set; }

    public string ActionType { get; set; } = "SqlCommand";
    public string? ProcedureName { get; set; }
    public string? ParametersJson { get; set; }
    public string? ApiConfigJson { get; set; }
}
