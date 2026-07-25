using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Görev şablonu satırı. Parent save'de satırlar topluca DELETE+INSERT yaşar;
/// bu yüzden ProjectTask.TemplateLineId bilinçli olarak FK'sız izlenebilirlik alanıdır.
/// </summary>
[Description("Görev şablonu satırı (TemplateId -> ProjectTaskTemplate). Uygulanınca ProjectTask'e kopyalanır.")]
public sealed class ProjectTaskTemplateLine
{
    public int Id { get; init; }

    [Description("Bağlı şablon. FK -> ProjectTaskTemplate.Id.")]
    public int TemplateId { get; init; }

    public required string Title { get; set; }

    public string? Description { get; set; }

    [Description("Uygulama sırası — projeye kopyalanırken görev OrderNo'su bu sıradan türetilir.")]
    public int OrderNo { get; set; }

    [Description("Varsayılan atanan kullanıcı — şablondan uygulanınca görev bu kullanıcıya atanmış gelir (opsiyonel). FK -> Users.Id.")]
    public int? DefaultAssignedUserId { get; set; }

    public bool IsActive { get; set; } = true;

    public int? CreatedById { get; init; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? UpdatedById { get; set; }
    public DateTime? Updated { get; set; }
}
