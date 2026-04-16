using System.ComponentModel.DataAnnotations;

namespace CalibraHub.Web.Models.Admin;

public sealed class ProjectInstructionRequestItemInput
{
    [Display(Name = "Istek ID")]
    public string RequestId { get; set; } = string.Empty;

    [Display(Name = "Kategori")]
    public string Category { get; set; } = string.Empty;

    [Display(Name = "Talep")]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "Detay")]
    public string Description { get; set; } = string.Empty;

    [Display(Name = "Tamamlandi")]
    public bool IsCompleted { get; set; }

    [Display(Name = "Kullanici Yorumu")]
    public string UserComment { get; set; } = string.Empty;
}
