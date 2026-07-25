using System.ComponentModel;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// AR-GE proje görevi (WBS satırı). ProjectId -> Document.Id ('arge_proje' belgesi) —
/// ArgePrototype ile aynı child konumu. Atama login kullanıcısına yapılır
/// (AssignedUserId -> Users.Id); "kendi görevim" denetimi oturum NameIdentifier ile birebir.
/// Sıra kilidi saklanmaz: proje SequentialTasks modundaysa önünde açık görev olan
/// görev runtime'da "Sırada Bekliyor" türetilir.
/// </summary>
[Description("AR-GE proje görevi. ProjectId -> Document.Id (arge_proje). Durum: 0 Açık / 1 Tamamlandı / 2 İptal; 'Sırada Bekliyor' türetilir, saklanmaz.")]
public sealed class ProjectTask
{
    public int Id { get; init; }

    [Description("Bağlı proje belgesi. FK -> Document.Id.")]
    public int ProjectId { get; init; }

    public required string Title { get; set; }

    public string? Description { get; set; }

    [Description("Görev sırası (sıralı ilerleme modunda kilit anahtarı; eşitlikte Id kırar).")]
    public int OrderNo { get; set; }

    public ProjectTaskStatus Status { get; set; } = ProjectTaskStatus.Open;

    [Description("Atanan kullanıcı. FK -> Users.Id (login kullanıcısı).")]
    public int? AssignedUserId { get; set; }

    [Description("Hedef bitiş tarihi.")]
    public DateTime? TargetDate { get; set; }

    public DateTime? CompletedAt { get; set; }

    [Description("Görevi tamamlayan kullanıcı. FK -> Users.Id.")]
    public int? CompletedByUserId { get; set; }

    [Description("Faz 2 bağlı-kayıt otomasyonu: hedef kayıt türü (örn. 'ArgePrototype', 'WorkOrder'). Faz 1'de API'ye açık değil.")]
    public string? LinkedEntityKind { get; set; }

    [Description("Faz 2 bağlı-kayıt otomasyonu: hedef kayıt Id'si.")]
    public int? LinkedEntityId { get; set; }

    [Description("Görev şablon satırından üretildiyse kaynak satır (izlenebilirlik; bilinçli FK'sız — şablon satırları delete-reinsert yaşar).")]
    public int? TemplateLineId { get; set; }

    public bool IsActive { get; set; } = true;

    public int? CreatedById { get; init; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? UpdatedById { get; set; }
    public DateTime? Updated { get; set; }
}
