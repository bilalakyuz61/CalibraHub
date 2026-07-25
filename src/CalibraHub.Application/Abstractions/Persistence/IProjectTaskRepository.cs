using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Proje görev katmanı veri erişimi (ProjectTask + şablonlar). ArgeProject üzerindeki
/// görev-sahası kolonları (SequentialTasks, ProgressPercent) da BU repo'dan yazılır —
/// SqlArgeProjectRepository'ye dokunmamak bilinçli karar (zarar-vermeme).
/// </summary>
public interface IProjectTaskRepository
{
    // ── Görevler ────────────────────────────────────────────────────────────

    /// <summary>Projenin aktif görevleri (Users join + türetilmiş IsBlocked), (OrderNo, Id) sıralı.</summary>
    Task<IReadOnlyCollection<ProjectTaskDto>> ListByProjectAsync(int projectId, bool sequential, CancellationToken ct);

    /// <summary>Tek görev (aktif olmayan dahil — servis kararı). Yoksa null.</summary>
    Task<ProjectTask?> GetByIdAsync(int taskId, CancellationToken ct);

    /// <summary>Yeni görev ekler, üretilen Id'yi döner.</summary>
    Task<int> InsertAsync(ProjectTask task, CancellationToken ct);

    /// <summary>Görev alanlarını günceller (Title/Description/OrderNo/AssignedUserId/TargetDate).</summary>
    Task UpdateAsync(ProjectTask task, CancellationToken ct);

    /// <summary>Görev soft-delete (IsActive=0).</summary>
    Task SoftDeleteAsync(int taskId, int? updatedById, CancellationToken ct);

    /// <summary>
    /// Atomik guard'lı tamamlama: görev hâlâ Açık + (sıralı mod kapalı VEYA önünde açık görev yok)
    /// ise Completed yapar. Yarış-güvenli; etkilenen satır yoksa false (servis nedeni yeniden okur).
    /// </summary>
    Task<bool> TryCompleteGuardedAsync(int taskId, int? userId, bool sequential, CancellationToken ct);

    /// <summary>Durumu doğrudan yazar (İptal / Yeniden Aç). Reopen'da CompletedAt/By temizlenir.</summary>
    Task SetStatusAsync(int taskId, byte status, int? updatedById, CancellationToken ct);

    /// <summary>Sıra anahtarına göre bu görevden SONRA tamamlanmış aktif görev var mı (reopen guard'ı).</summary>
    Task<bool> HasCompletedAfterAsync(int projectId, int orderNo, int taskId, CancellationToken ct);

    /// <summary>Sıra anahtarına göre bu görevden sonraki ilk Açık görev (sıradaki-bildirim için). Yoksa null.</summary>
    Task<ProjectTask?> GetNextOpenTaskAsync(int projectId, int orderNo, int taskId, CancellationToken ct);

    /// <summary>Projedeki aktif görevlerin en büyük OrderNo'su (yoksa 0).</summary>
    Task<int> GetMaxOrderNoAsync(int projectId, CancellationToken ct);

    /// <summary>Görev setini tek transaction'da ekler (şablondan uygula). Eklenen sayıyı döner.</summary>
    Task<int> InsertBatchAsync(IReadOnlyList<ProjectTask> tasks, CancellationToken ct);

    /// <summary>Kullanıcıya atanmış aktif görevler (proje adı/numarası + IsBlocked ile).</summary>
    Task<IReadOnlyCollection<MyProjectTaskItem>> ListMineAsync(int userId, CancellationToken ct);

    /// <summary>Aktif kullanıcılar — atanan dropdown'u.</summary>
    Task<IReadOnlyCollection<ProjectTaskUserOption>> GetAssignableUsersAsync(CancellationToken ct);

    // ── ArgeProject görev-sahası (SequentialTasks / ProgressPercent) ────────

    /// <summary>Projenin sıralı ilerleme bayrağı (companion yoksa false).</summary>
    Task<bool> GetSequentialFlagAsync(int projectId, CancellationToken ct);

    /// <summary>Sıralı ilerleme bayrağını yazar.</summary>
    Task SetSequentialFlagAsync(int projectId, bool enabled, int? updatedById, CancellationToken ct);

    /// <summary>
    /// Görevlerden ilerleme yüzdesini hesaplayıp ArgeProject.ProgressPercent'e yazar.
    /// Payda = Açık+Tamamlandı (İptal düşer). Aktif görev yoksa YAZMAZ ve null döner
    /// (manuel ilerleme rejimi bozulmaz).
    /// </summary>
    Task<decimal?> RecalcProjectProgressAsync(int projectId, int? updatedById, CancellationToken ct);

    // ── Şablonlar ───────────────────────────────────────────────────────────

    /// <summary>Aktif şablonlar, satırlarıyla (OrderNo sıralı).</summary>
    Task<IReadOnlyCollection<ProjectTaskTemplateDto>> ListTemplatesAsync(CancellationToken ct);

    /// <summary>Tek şablon (satırlarıyla). Yoksa null.</summary>
    Task<ProjectTaskTemplateDto?> GetTemplateAsync(int templateId, CancellationToken ct);

    /// <summary>Şablon başlık + satırları tek transaction'da yazar (satırlar DELETE+INSERT). Id döner.</summary>
    Task<int> UpsertTemplateAsync(ProjectTaskTemplate template, IReadOnlyList<ProjectTaskTemplateLine> lines, CancellationToken ct);

    /// <summary>Şablon soft-delete (IsActive=0; satırlar da pasiflenir).</summary>
    Task SoftDeleteTemplateAsync(int templateId, int? updatedById, CancellationToken ct);
}
