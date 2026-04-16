using System.ComponentModel.DataAnnotations;

namespace CalibraHub.Web.Models.Admin;

public sealed class ProjectInstructionsJsonInput
{
    public string? Content { get; set; }
}

public sealed class ProjectInstructionsViewModel
{
    [Display(Name = "Talimat Metni")]
    public string Content { get; set; } = string.Empty;

    public string RelativePath { get; init; } = "PROJECT_INSTRUCTIONS.txt";

    public int LineCount { get; init; }

    public int CharacterCount { get; init; }

    public DateTime? LastModified { get; init; }

    public string RequestsRelativePath { get; init; } = "PROJECT_REQUESTS.json";

    public int RequestCount { get; init; }

    public int CompletedRequestCount { get; init; }

    public DateTime? RequestsLastModified { get; init; }

    public List<ProjectInstructionRequestItemInput> RequestItems { get; init; } = [];
}
