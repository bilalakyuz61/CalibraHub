using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// DÖF (Düzeltici/Önleyici Faaliyet — CAPA) iş servisi: Document numara + companion + aksiyon
/// satırları CRUD, durum makinesi (Açık→Kök Neden→Aksiyonda→Doğrulama Bekliyor→Kapalı/İptal),
/// kapanış guard'ı (tüm aksiyonlar bitmiş + etkinlik doğrulanmış olmalı).
/// </summary>
public interface ICapaService
{
    Task<IReadOnlyCollection<CapaListItemDto>> ListAsync(string? search, byte? status, CancellationToken ct);
    Task<CapaDetailDto?> GetAsync(int documentId, CancellationToken ct);
    /// <summary>Kaydeder; yeni kayıtta Document 'dof' shell + numara oluşturulur. DocumentId döner.</summary>
    Task<(bool Ok, string? Error, int DocumentId)> SaveAsync(SaveCapaRequest request, int? userId, CancellationToken ct);

    /// <summary>
    /// Durum değiştirir. Kapanışta (NewStatus=Kapali) ApprovalFlow devredeyse (aktif bir "Capa"
    /// akışı eşleşirse) durum doğrudan Kapali'ya geçmez — DogrulamaBekliyor'da kalır ve onay
    /// süreci başlatılır; <c>Message</c> kullanıcıya bunu açıklar, <c>ActualStatus</c> gerçekte
    /// uygulanan durumu (istenenden farklıysa) taşır. Akış yoksa/kapalıysa eski davranış aynen
    /// sürer: (Ok=true, Error=null, Message=null, ActualStatus=null) — NewStatus uygulanmıştır.
    /// </summary>
    Task<(bool Ok, string? Error, string? Message, byte? ActualStatus)> ChangeStatusAsync(ChangeCapaStatusRequest request, int? userId, CancellationToken ct);
    Task<(bool Ok, string? Error)> DeleteAsync(int documentId, int? userId, CancellationToken ct);

    /// <summary>
    /// ApprovalController tarafından, EntityKind="Capa" onay örneği tamamlandığında (Approved/Rejected)
    /// çağrılır — bkz. <see cref="ChangeStatusAsync"/> XML doc'u (kapanış onayı akışı). Idempotent:
    /// kayıt yoksa veya Status artık DogrulamaBekliyor değilse no-op (loglanır, sessiz değil).
    /// </summary>
    Task OnClosureApprovalCompletedAsync(int documentId, bool approved, int? actorUserId, CancellationToken ct);

    Task<IReadOnlyCollection<CapaPersonnelOption>> GetPersonnelOptionsAsync(CancellationToken ct);

    /// <summary>KPI panosu agregasyonu — salt-okunur, audit YOK.</summary>
    Task<CapaKpiDto> GetKpiAsync(CancellationToken ct);
}
