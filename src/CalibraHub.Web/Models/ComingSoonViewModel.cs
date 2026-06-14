namespace CalibraHub.Web.Models;

/// <summary>
/// Henuz tamamlanmamis sayfalar icin paylasilan placeholder VM.
/// Views/Shared/_ComingSoon.cshtml tarafindan render edilir.
/// </summary>
public sealed class ComingSoonViewModel
{
    public string Title { get; init; } = "Yakında";
    public string Description { get; init; } = "Bu ekran yapım aşamasındadır.";
    public string? Icon { get; init; }
}
