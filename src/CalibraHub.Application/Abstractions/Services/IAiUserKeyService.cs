using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// 2026-05-23 — Kullanıcının kendi AI provider override key'leri (Profil → AI Anahtarlarım).
/// </summary>
public interface IAiUserKeyService
{
    /// <summary>
    /// Mevcut tüm aktif provider'lar + kullanıcının override durumu.
    /// Override yoksa HasOverride=false döner (UI bilgi verir).
    /// </summary>
    Task<IReadOnlyList<AiUserKeyDto>> ListForUserAsync(int userId, CancellationToken ct);

    /// <summary>Yeni override veya mevcut override güncelle.</summary>
    Task SaveAsync(int userId, SaveAiUserKeyRequest req, CancellationToken ct);

    /// <summary>Override sil — kullanıcı şirket default'una geri döner.</summary>
    Task DeleteAsync(int userId, int providerId, CancellationToken ct);
}
