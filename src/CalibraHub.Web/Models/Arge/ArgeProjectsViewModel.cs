using CalibraHub.Application.Contracts;

namespace CalibraHub.Web.Models.Arge;

/// <summary>
/// AR-GE / ÜR-GE "Komuta Güvertesi" board view model'i.
/// Bespoke ekran typed listeyi doğrudan render eder (SmartBoard mount kaldırıldı).
/// </summary>
public sealed class ArgeProjectsViewModel
{
    public IReadOnlyCollection<ArgeProjectListItem> Projects { get; init; } = System.Array.Empty<ArgeProjectListItem>();
}
