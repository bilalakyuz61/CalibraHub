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
    Task<(bool Ok, string? Error)> ChangeStatusAsync(ChangeCapaStatusRequest request, int? userId, CancellationToken ct);
    Task<(bool Ok, string? Error)> DeleteAsync(int documentId, int? userId, CancellationToken ct);

    Task<IReadOnlyCollection<CapaPersonnelOption>> GetPersonnelOptionsAsync(CancellationToken ct);

    /// <summary>KPI panosu agregasyonu — salt-okunur, audit YOK.</summary>
    Task<CapaKpiDto> GetKpiAsync(CancellationToken ct);
}
