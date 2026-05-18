using CalibraHub.Application.Abstractions.DesignProvider;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Services.DesignProvider;

/// <summary>
/// IDesignProvider implementasyonu — MemoryCache destekli.
///
/// Akış:
///   1) DocLayoutRule listesi (per DocType) IMemoryCache'te 5 dk TTL'li tutulur.
///      Cache miss → SQL → cache'e yaz (standart pattern).
///   2) Eşleşme/ağırlık hesabı RAM'de yapılır (kriter listesi DI'dan alınır).
///   3) Eşleşme yoksa DocLayout.IsDefault fallback'i — bu da cache'lenir
///      (key: layout_default_{docType}).
///   4) Invalidation: DocLayoutRuleService ve DocDesignerService kural/layout
///      kaydedip silerken ilgili cache key'ini Remove eder (bkz. DesignProviderCacheKeys).
/// </summary>
public sealed class DesignProvider : IDesignProvider
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly IDocLayoutRuleRepository _rules;
    private readonly IReadOnlyList<IDesignCriterion> _criteria;
    private readonly IMemoryCache _cache;
    private readonly ILogger<DesignProvider> _logger;

    public DesignProvider(
        IDocLayoutRuleRepository rules,
        IEnumerable<IDesignCriterion> criteria,
        IMemoryCache cache,
        ILogger<DesignProvider> logger)
    {
        _rules    = rules;
        _criteria = criteria.OrderByDescending(c => c.Weight).ToList();
        _cache    = cache;
        _logger   = logger;
    }

    public async Task<int> GetEffectiveLayoutIdAsync(DesignSelectionContext ctx, CancellationToken ct = default)
    {
        var id = await TryGetEffectiveLayoutIdAsync(ctx, ct);
        if (id.HasValue) return id.Value;
        throw new InvalidOperationException(
            $"'{ctx.DocType}' belge tipi için hiç tasarım tanımlı değil (kural yok, varsayılan yok).");
    }

    public async Task<int?> TryGetEffectiveLayoutIdAsync(DesignSelectionContext ctx, CancellationToken ct = default)
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));
        if (string.IsNullOrWhiteSpace(ctx.DocType))
            throw new ArgumentException("DocType zorunludur.", nameof(ctx));

        // ── 1) Cache'den (veya SQL'den) kural listesini al ──────────────────────
        var rules = await GetCachedRulesAsync(ctx.DocType, ct);

        // ── 2) In-memory eşleşme + 2^n ağırlık ─────────────────────────────────
        DocLayoutRuleMatchRow? best = null;
        int bestWeight = -1;
        foreach (var r in rules)
        {
            // Her kriter: rule'da NULL ise wildcard (kabul), aksi halde eşit olmalı
            if (r.CustomerId.HasValue     && r.CustomerId     != ctx.CustomerId)     continue;
            if (r.ContactGroupId.HasValue && r.ContactGroupId != ctx.ContactGroupId) continue;
            if (r.UserId.HasValue         && r.UserId         != ctx.UserId)         continue;
            if (r.BranchId.HasValue       && r.BranchId       != ctx.BranchId)       continue;
            if (r.WarehouseId.HasValue    && r.WarehouseId    != ctx.WarehouseId)    continue;

            int weight = ComputeWeight(r);
            // Daha yüksek ağırlık kazanır; eşitlikte daha yeni UpdatedAt önce gelir
            if (weight > bestWeight ||
                (weight == bestWeight && best != null && r.UpdatedAt > best.UpdatedAt))
            {
                best       = r;
                bestWeight = weight;
            }
        }

        if (best != null)
        {
            _logger.LogDebug(
                "[DesignProvider] {DocType} (cust={Cust}, user={User}, branch={Branch}, wh={Wh}) " +
                "→ cached rule LayoutId={Id} (weight={Weight}, ruleId={RuleId})",
                ctx.DocType, ctx.CustomerId, ctx.UserId, ctx.BranchId, ctx.WarehouseId,
                best.LayoutId, bestWeight, best.Id);
            return best.LayoutId;
        }

        // ── 3) Fallback: DocLayout.IsDefault (de cache'li) ─────────────────────
        var fallback = await GetCachedDefaultAsync(ctx.DocType, ct);
        if (fallback.HasValue)
        {
            _logger.LogDebug("[DesignProvider] {DocType} → kural yok, varsayılan LayoutId={Id}",
                ctx.DocType, fallback.Value);
            return fallback.Value;
        }

        _logger.LogDebug("[DesignProvider] {DocType} için yeni motor tarafında hiç tasarım yok.", ctx.DocType);
        return null;
    }

    // ── Cache helpers ────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<DocLayoutRuleMatchRow>> GetCachedRulesAsync(string docType, CancellationToken ct)
    {
        var key = DesignProviderCacheKeys.Rules(docType);
        if (_cache.TryGetValue<IReadOnlyList<DocLayoutRuleMatchRow>>(key, out var cached) && cached != null)
            return cached;

        var rules = await _rules.ListActiveByDocTypeAsync(docType, ct);
        _cache.Set(key, rules, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheTtl,
            Priority = CacheItemPriority.Normal,
        });
        _logger.LogDebug("[DesignProvider] Cache miss → {Key} yüklendi ({Count} kural).", key, rules.Count);
        return rules;
    }

    private async Task<int?> GetCachedDefaultAsync(string docType, CancellationToken ct)
    {
        var key = DesignProviderCacheKeys.Default(docType);
        if (_cache.TryGetValue<int?>(key, out var cached))
            return cached;

        var id = await _rules.FindDefaultAsync(docType, ct);
        _cache.Set(key, id, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheTtl,
            Priority = CacheItemPriority.Low,
        });
        return id;
    }

    // ── Weight ───────────────────────────────────────────────────────────────

    private int ComputeWeight(DocLayoutRuleMatchRow r)
    {
        int w = 0;
        foreach (var c in _criteria)
        {
            object? val = c.ColumnName switch
            {
                "CustomerId"     => r.CustomerId,
                "ContactGroupId" => r.ContactGroupId,
                "UserId"         => r.UserId,
                "BranchId"       => r.BranchId,
                "WarehouseId"    => r.WarehouseId,
                _                => null,
            };
            if (val != null) w += c.Weight;
        }
        return w;
    }
}
