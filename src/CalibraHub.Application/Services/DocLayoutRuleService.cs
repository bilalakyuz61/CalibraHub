using CalibraHub.Application.Abstractions.DesignProvider;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Services;

public sealed class DocLayoutRuleService : IDocLayoutRuleService
{
    private readonly IDocLayoutRuleRepository _repo;
    private readonly IDocLayoutRepository _layoutRepo;
    private readonly IMemoryCache _cache;
    private readonly ILogger<DocLayoutRuleService> _logger;

    public DocLayoutRuleService(
        IDocLayoutRuleRepository repo,
        IDocLayoutRepository layoutRepo,
        IMemoryCache cache,
        ILogger<DocLayoutRuleService> logger)
    {
        _repo = repo;
        _layoutRepo = layoutRepo;
        _cache = cache;
        _logger = logger;
    }

    public Task<IReadOnlyCollection<DocLayoutRuleDto>> ListAllAsync(CancellationToken ct)
        => _repo.ListAllAsync(ct);

    public Task<DocLayoutRuleDto?> GetAsync(int id, CancellationToken ct)
        => _repo.GetByIdAsync(id, ct);

    public async Task<int> SaveAsync(SaveDocLayoutRuleRequest req, CancellationToken ct)
    {
        if (req is null) throw new ArgumentNullException(nameof(req));
        if (string.IsNullOrWhiteSpace(req.DocType))
            throw new InvalidOperationException("Belge tipi seçilmelidir.");
        if (req.LayoutId <= 0)
            throw new InvalidOperationException("Bir tasarım seçilmelidir.");

        // DocType ↔ Layout tutarlılığı
        var layout = await _layoutRepo.GetByIdAsync(req.LayoutId, ct);
        if (layout is null)
            throw new InvalidOperationException("Seçilen tasarım bulunamadı veya pasif.");
        if (!string.Equals(layout.DocType, req.DocType, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Seçtiğiniz tasarım, kuralın belge türüyle uyumlu değil " +
                $"(tasarım: {layout.DocType}, kural: {req.DocType}). " +
                "Aynı belge türü için tasarlanmış bir şablon seçin.");

        // Update senaryosunda DocType değişmiş olabilir — eski DocType cache'ini de invalidate et
        string? oldDocType = null;
        if (req.Id > 0)
        {
            var existing = await _repo.GetByIdAsync(req.Id, ct);
            if (existing != null && !string.Equals(existing.DocType, req.DocType, StringComparison.OrdinalIgnoreCase))
                oldDocType = existing.DocType;
        }

        var id = await _repo.UpsertAsync(req, ct);

        InvalidateRulesCache(req.DocType);
        if (oldDocType != null) InvalidateRulesCache(oldDocType);

        return id;
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        // Soft-delete öncesi kuralın DocType'ını al ki cache'i temizleyebilelim
        var existing = await _repo.GetByIdAsync(id, ct);
        await _repo.SoftDeleteAsync(id, ct);

        if (existing != null)
            InvalidateRulesCache(existing.DocType);
    }

    private void InvalidateRulesCache(string docType)
    {
        var key = DesignProviderCacheKeys.Rules(docType);
        _cache.Remove(key);
        _logger.LogDebug("[DocLayoutRuleService] Cache invalidated: {Key}", key);
    }
}
