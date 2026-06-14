using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Services.Ai;

/// <summary>
/// 2026-05-23 — Kullanıcının override AI key'leri (Profil → AI Anahtarlarım).
/// ListForUser tüm aktif provider'ları + user'ın override durumunu birleştirir.
/// </summary>
public sealed class AiUserKeyService : IAiUserKeyService
{
    private readonly IAiProviderRepository _providerRepo;
    private readonly IAiUserKeyRepository _userKeyRepo;

    public AiUserKeyService(IAiProviderRepository providerRepo, IAiUserKeyRepository userKeyRepo)
    {
        _providerRepo = providerRepo;
        _userKeyRepo = userKeyRepo;
    }

    public async Task<IReadOnlyList<AiUserKeyDto>> ListForUserAsync(int userId, CancellationToken ct)
    {
        var providers = await _providerRepo.ListAsync(includeInactive: false, ct).ConfigureAwait(false);
        if (providers.Count == 0) return Array.Empty<AiUserKeyDto>();

        var overrides = await _userKeyRepo.ListByUserAsync(userId, ct).ConfigureAwait(false);
        var ovrMap = overrides.ToDictionary(o => o.AiProviderId);

        var result = new List<AiUserKeyDto>(providers.Count);
        foreach (var p in providers)
        {
            var ovr = ovrMap.GetValueOrDefault(p.Id);
            result.Add(new AiUserKeyDto(
                ProviderId:       p.Id,
                ProviderCode:     p.Code,
                ProviderLabel:    p.Label,
                HasOverride:      ovr is not null,
                OverrideCreated:  ovr?.Created));
        }
        return result;
    }

    public async Task SaveAsync(int userId, SaveAiUserKeyRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ApiKey))
            throw new ArgumentException("ApiKey boş olamaz.");
        // Provider gerçekten var mı kontrol et
        var provider = await _providerRepo.GetByIdAsync(req.ProviderId, ct).ConfigureAwait(false);
        if (provider is null || !provider.IsActive)
            throw new InvalidOperationException("Provider bulunamadı veya pasif.");
        await _userKeyRepo.SaveAsync(userId, req.ProviderId, req.ApiKey, ct).ConfigureAwait(false);
    }

    public Task DeleteAsync(int userId, int providerId, CancellationToken ct)
        => _userKeyRepo.DeleteAsync(userId, providerId, ct);
}
