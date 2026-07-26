using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Proje görev katmanı iş servisi: görev CRUD + sıra kilidi + otomatik ilerleme % +
/// atama bildirimi + şablondan uygula + "Görevlerim".
/// </summary>
public interface IProjectTaskService
{
    /// <summary>Projenin görevleri (sıra modu + hesaplanan ilerleme ile).</summary>
    Task<ProjectTaskListResult> ListAsync(int projectId, CancellationToken ct);

    /// <summary>
    /// Görev ekle/güncelle. Atama yeni/değiştiyse atanana bildirim düşer.
    /// companyId: oturum şirketi — atanan kullanıcı bu şirkete ait olmalı (çapraz-şirket yasak).
    /// </summary>
    Task<(bool Ok, string? Error, int Id)> SaveAsync(SaveProjectTaskRequest request, int? userId, int companyId, CancellationToken ct);

    /// <summary>
    /// Durum geçişi. restrictToAssignee=true iken yalnız görevin atananı işlem yapabilir
    /// (EDIT scope'u "All" olmayan kullanıcılar için controller geçirir).
    /// </summary>
    Task<(bool Ok, string? Error)> ChangeStatusAsync(ChangeProjectTaskStatusRequest request, int? userId, bool restrictToAssignee, CancellationToken ct);

    /// <summary>Görev soft-delete + ilerleme yeniden hesap.</summary>
    Task<(bool Ok, string? Error)> DeleteAsync(int taskId, int? userId, CancellationToken ct);

    /// <summary>Projenin sıralı ilerleme anahtarını değiştirir.</summary>
    Task<(bool Ok, string? Error)> SetSequentialAsync(SetSequentialTasksRequest request, int? userId, CancellationToken ct);

    /// <summary>
    /// Görevli projede ilerlemeyi görev-hesabına geri çeker (ArgeController.SaveProject
    /// post-step'i — manuel % görev-hesabını ezemesin). Görevsiz projede no-op.
    /// </summary>
    Task ReassertComputedProgressAsync(int projectId, int? userId, CancellationToken ct);

    /// <summary>Kullanıcıya atanmış aktif görevler ("Görevlerim").</summary>
    Task<IReadOnlyCollection<MyProjectTaskItem>> ListMineAsync(int userId, CancellationToken ct);

    /// <summary>Atanabilir kullanıcılar (dropdown) — yalnız oturum şirketinin aktif kullanıcıları.</summary>
    Task<IReadOnlyCollection<ProjectTaskUserOption>> GetAssignableUsersAsync(int companyId, CancellationToken ct);

    // ── Şablonlar ───────────────────────────────────────────────────────────

    Task<IReadOnlyCollection<ProjectTaskTemplateDto>> ListTemplatesAsync(CancellationToken ct);

    Task<(bool Ok, string? Error, int Id)> SaveTemplateAsync(SaveProjectTaskTemplateRequest request, int? userId, CancellationToken ct);

    Task<(bool Ok, string? Error)> DeleteTemplateAsync(int templateId, int? userId, CancellationToken ct);

    /// <summary>
    /// Şablon satırlarını projeye ATANMAMIŞ görev olarak uygular (mevcut görevlerin arkasına).
    /// Kişi ataması projede görev bazında yapılır. Şablon IsSequentialDefault ise ve projede
    /// kapalıysa sıralı mod açılır (SequentialEnabled=true döner — UI kullanıcıya söyler).
    /// </summary>
    Task<(bool Ok, string? Error, int Created, bool SequentialEnabled)> ApplyTemplateAsync(ApplyTaskTemplateRequest request, int? userId, CancellationToken ct);
}
