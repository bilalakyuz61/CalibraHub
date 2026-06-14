using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// 2026-05-23 — AiProvider tablosu CRUD. Impl: SqlAiProviderRepository.
///
/// **Şifreleme:** ApiKey alanı IntegratorSecretProtector ile encrypt-on-write,
/// decrypt-on-read uygulanır. Domain entity'ye geri verilirken ApiKeyEncrypted alanı
/// HÂLÂ ŞİFRELİ — caller (service layer) gerçek key'i almak için
/// <see cref="GetDecryptedApiKeyAsync"/> kullanır.
/// </summary>
public interface IAiProviderRepository
{
    Task<IReadOnlyList<AiProvider>> ListAsync(bool includeInactive, CancellationToken ct);
    Task<AiProvider?> GetByIdAsync(int id, CancellationToken ct);
    Task<AiProvider?> GetByCodeAsync(string code, CancellationToken ct);
    Task<AiProvider?> GetDefaultAsync(CancellationToken ct);

    /// <summary>
    /// Save (create veya update). plainApiKey verilirse encrypt edilip persist edilir.
    /// NULL ise mevcut ApiKeyEncrypted korunur (admin sadece label/endpoint güncelliyorsa).
    /// IsDefault=true ise diğer tüm provider'larda IsDefault=false yapılır (single-default invariant).
    /// </summary>
    Task<int> SaveAsync(AiProvider entity, string? plainApiKey, CancellationToken ct);

    Task DeleteAsync(int id, CancellationToken ct);

    /// <summary>
    /// Provider'ın decrypted ApiKey'ini döner (kullanıcı override yoksa kullanılır).
    /// NULL = provider yok veya key girilmemiş.
    /// </summary>
    Task<string?> GetDecryptedApiKeyAsync(int providerId, CancellationToken ct);
}
