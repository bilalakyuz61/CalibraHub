namespace CalibraHub.Application.Contracts;

/// <summary>
/// Proje görevi satırı (ProjectTask ⨝ Users). IsBlocked TÜRETİLMİŞTİR — proje sıralı
/// ilerleme modundayken önünde açık görev bulunan Open görev için true döner
/// ("Sırada Bekliyor" chip'i); DB'de saklanmaz.
/// </summary>
public sealed record ProjectTaskDto(
    int Id,
    int ProjectId,
    string Title,
    string? Description,
    int OrderNo,
    byte Status,
    bool IsBlocked,
    int? AssignedUserId,
    string? AssignedUserName,
    DateTime? TargetDate,
    DateTime? CompletedAt,
    int? TemplateLineId,
    DateTime Created);

/// <summary>Görevler sekmesi yükü: proje sıra modu + hesaplanan ilerleme + görev listesi.</summary>
public sealed record ProjectTaskListResult(
    bool SequentialTasks,
    decimal? ComputedProgress,
    IReadOnlyCollection<ProjectTaskDto> Tasks);

/// <summary>Görev kaydetme isteği. Id=0 yeni, Id&gt;0 mevcut. ProjectId = AR-GE Document.id.</summary>
public sealed record SaveProjectTaskRequest(
    int Id,
    int ProjectId,
    string Title,
    string? Description,
    int OrderNo,
    int? AssignedUserId,
    DateTime? TargetDate);

/// <summary>Görev durum geçişi isteği. NewStatus: 0 Açık / 1 Tamamlandı / 2 İptal.</summary>
public sealed record ChangeProjectTaskStatusRequest(int TaskId, byte NewStatus);

/// <summary>Projenin sıralı ilerleme anahtarını değiştirme isteği.</summary>
public sealed record SetSequentialTasksRequest(int ProjectId, bool Enabled);

/// <summary>Şablondan görev seti uygulama isteği.</summary>
public sealed record ApplyTaskTemplateRequest(int ProjectId, int TemplateId);

/// <summary>"Görevlerim" satırı (kişiye atanmış görevler; proje bağlam bilgisiyle).</summary>
public sealed record MyProjectTaskItem(
    int TaskId,
    int ProjectId,
    string ProjectName,
    string DocumentNumber,
    string Title,
    string? Description,
    byte Status,
    bool IsBlocked,
    DateTime? TargetDate,
    int OrderNo);

/// <summary>Atanabilir kullanıcı dropdown seçeneği.</summary>
public sealed record ProjectTaskUserOption(int Id, string Name);

/// <summary>
/// Görev şablonu satırı. DefaultAssignedUserId: şablondan uygulanınca görev bu kullanıcıya
/// atanmış gelir (opsiyonel); DefaultAssignedUserName yalnız okuma/gösterim içindir.
/// </summary>
public sealed record ProjectTaskTemplateLineDto(
    int Id,
    string Title,
    string? Description,
    int OrderNo,
    int? DefaultAssignedUserId = null,
    string? DefaultAssignedUserName = null);

/// <summary>Görev şablonu (satırlarıyla).</summary>
public sealed record ProjectTaskTemplateDto(
    int Id,
    string Name,
    string? Description,
    bool IsSequentialDefault,
    IReadOnlyCollection<ProjectTaskTemplateLineDto> Lines);

/// <summary>Şablon kaydetme isteği. Id=0 yeni. Satırlar topluca yazılır (DELETE+INSERT).</summary>
public sealed record SaveProjectTaskTemplateRequest(
    int Id,
    string Name,
    string? Description,
    bool IsSequentialDefault,
    IReadOnlyList<ProjectTaskTemplateLineDto> Lines);
