using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Proje görev şablonu başlığı. Satırları (ProjectTaskTemplateLine) bir projeye
/// "şablondan uygula" ile ProjectTask kayıtlarına kopyalanır.
/// </summary>
[Description("Proje görev şablonu. Satırları projeye uygulanınca ProjectTask kayıtları üretilir.")]
public sealed class ProjectTaskTemplate
{
    public int Id { get; init; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    [Description("Şablon uygulanırken projede sıralı ilerleme (SequentialTasks) kapalıysa açılır.")]
    public bool IsSequentialDefault { get; set; }

    public bool IsActive { get; set; } = true;

    public int? CreatedById { get; init; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? UpdatedById { get; set; }
    public DateTime? Updated { get; set; }
}
