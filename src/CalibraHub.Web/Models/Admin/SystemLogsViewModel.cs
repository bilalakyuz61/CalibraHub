using CalibraHub.Application.Contracts;
using CalibraHub.Web.Models.Shared;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CalibraHub.Web.Models.Admin;

public sealed class SystemLogsViewModel
{
    public required IReadOnlyCollection<IntegratorImportLogEntryDto> Logs { get; init; }
    public required IReadOnlyCollection<SelectListItem> CompanyOptions { get; init; }
    public required GridListStateViewModel ListState { get; init; }
    public string SearchTerm { get; init; } = string.Empty;
    public string Level { get; init; } = string.Empty;
    public int? CompanyId { get; init; }
}
