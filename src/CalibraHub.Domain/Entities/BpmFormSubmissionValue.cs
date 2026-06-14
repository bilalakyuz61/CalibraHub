namespace CalibraHub.Domain.Entities;

public sealed class BpmFormSubmissionValue
{
    public int     Id           { get; set; }
    public int     SubmissionId { get; init; }
    public string  FieldKey     { get; init; } = "";
    public string? Value        { get; set; }
}
