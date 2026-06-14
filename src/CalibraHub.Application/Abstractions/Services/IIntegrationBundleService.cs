using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Entegrasyon dışa/içe aktarma servisi (2026-05-21 — Faz 1).
/// Tek entegrasyonu JSON bundle olarak çıkarır; başka ortamda aynı bundle ile içeri alır.
/// Çakışma stratejisi: Overwrite (mevcut güncelle) / NewCopy (isme " (Kopya)" ekle) / Skip.
/// </summary>
public interface IIntegrationBundleService
{
    /// <summary>Verilen integration'ı bundle olarak hazırlar (DB'den dolu çek).</summary>
    Task<IntegrationBundleDto> ExportAsync(int integrationId, string? exportedBy, CancellationToken ct);

    /// <summary>Bundle'ı içeri alır — yeni Integration yaratır veya mevcudu üzerine yazar.</summary>
    Task<ImportIntegrationResultDto> ImportAsync(ImportIntegrationRequest request, string? actor, CancellationToken ct);
}
