namespace CalibraHub.Application.Contracts;

public sealed record ImportResultDto(
    int ImportedCount,
    int SkippedCount,
    IReadOnlyCollection<string> Notes);
