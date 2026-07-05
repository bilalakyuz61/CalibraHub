using CalibraHub.Domain.Common;
using CalibraHub.Domain.Entities;
using Xunit;

namespace CalibraHub.Tests.Domain;

/// <summary>
/// BOM.EnsureNoCycle / EnsureNoCycleAsync — döngüsel reçete koruması.
/// Ağaç, getChildren delegate'ine verilen sözlükle simüle edilir (itemId → alt bileşen id'leri).
/// </summary>
public sealed class BomCycleTests
{
    private static Func<int, Task<IEnumerable<int>>> Lookup(Dictionary<int, int[]> tree)
        => childId => Task.FromResult<IEnumerable<int>>(
            tree.TryGetValue(childId, out var kids) ? kids : []);

    [Fact]
    public async Task DirektSelfReference_DomainException()
    {
        // Mamul kendi bileşen listesinde
        await Assert.ThrowsAsync<DomainException>(() =>
            BOM.EnsureNoCycleAsync(parentItemId: 1, proposedChildItemIds: [2, 1], Lookup([])));
    }

    [Fact]
    public async Task DolayliDongu_DomainException()
    {
        // 1 → 2 → 3 → 1 (üç seviyeli dolaylı döngü)
        var tree = new Dictionary<int, int[]> { [2] = [3], [3] = [1] };
        await Assert.ThrowsAsync<DomainException>(() =>
            BOM.EnsureNoCycleAsync(1, [2], Lookup(tree)));
    }

    [Fact]
    public async Task DonguYok_Gecer()
    {
        // 1 → 2 → 3, 1 → 4; hiçbir yol 1'e geri dönmüyor
        var tree = new Dictionary<int, int[]> { [2] = [3], [3] = [], [4] = [3] };
        await BOM.EnsureNoCycleAsync(1, [2, 4], Lookup(tree)); // exception fırlatmamalı
    }

    [Fact]
    public async Task PaylasilanAltBilesen_DonguDegil()
    {
        // Elmas şekli: 1 → 2 → 5 ve 1 → 3 → 5 — 5 iki yoldan geliyor ama döngü yok
        var tree = new Dictionary<int, int[]> { [2] = [5], [3] = [5], [5] = [] };
        await BOM.EnsureNoCycleAsync(1, [2, 3], Lookup(tree));
    }

    [Fact]
    public void SyncWrapper_AyniDavranis()
    {
        // Sync köprü (Func<int, IEnumerable<int>>) — dolaylı döngüyü aynı şekilde yakalar
        var tree = new Dictionary<int, int[]> { [2] = [1] };
        IEnumerable<int> GetChildren(int id) => tree.TryGetValue(id, out var kids) ? kids : [];

        Assert.Throws<DomainException>(() => BOM.EnsureNoCycle(1, [2], GetChildren));
        BOM.EnsureNoCycle(1, [3], GetChildren); // döngüsüz — geçer
    }
}
