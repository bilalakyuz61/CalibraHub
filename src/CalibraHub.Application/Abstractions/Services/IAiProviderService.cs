using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// 2026-05-23 — Şirket Ayarları "Yapay Zeka" sekmesinin CRUD service'i.
/// Admin yetki kontrolü Controller'da yapılır; bu servis sadece domain işlemini bilir.
/// </summary>
public interface IAiProviderService
{
    Task<IReadOnlyList<AiProviderDto>> ListAsync(bool includeInactive, CancellationToken ct);
    Task<AiProviderDto?> GetAsync(int id, CancellationToken ct);

    /// <summary>Id=0 yeni, >0 update. ApiKey null/boş ise mevcut korunur.</summary>
    Task<int> SaveAsync(SaveAiProviderRequest req, int? currentUserId, CancellationToken ct);

    Task DeleteAsync(int id, CancellationToken ct);

    /// <summary>
    /// Provider'a küçük bir ping mesajı gönderir, başarı/hata döner. Şirket Ayarları
    /// "Bağlantı Test" butonu için. Token kullanımı minimal (1-5 token).
    /// </summary>
    Task<(bool Ok, string? Error, string? Sample)> TestConnectionAsync(int providerId, CancellationToken ct);
}
