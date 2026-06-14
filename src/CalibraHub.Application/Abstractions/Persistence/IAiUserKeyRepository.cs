using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// 2026-05-23 — AiUserKey tablosu CRUD. Impl: SqlAiUserKeyRepository.
///
/// Kullanıcının bir provider için kendi override API key'i. Profil → AI Anahtarlarım'dan
/// yönetilir. AiClientFactory önce buraya bakar; yoksa şirket default'una düşer.
/// </summary>
public interface IAiUserKeyRepository
{
    Task<IReadOnlyList<AiUserKey>> ListByUserAsync(int userId, CancellationToken ct);
    Task<AiUserKey?> GetAsync(int userId, int providerId, CancellationToken ct);

    /// <summary>
    /// Save (UPSERT): aynı (UserId, ProviderId) varsa update, yoksa insert.
    /// plainApiKey daima encrypt edilir.
    /// </summary>
    Task<int> SaveAsync(int userId, int providerId, string plainApiKey, CancellationToken ct);

    Task DeleteAsync(int userId, int providerId, CancellationToken ct);

    /// <summary>
    /// Kullanıcının bu provider için override key'inin decrypted hâli.
    /// NULL = override yok (caller şirket default'una düşer).
    /// </summary>
    Task<string?> GetDecryptedApiKeyAsync(int userId, int providerId, CancellationToken ct);
}
