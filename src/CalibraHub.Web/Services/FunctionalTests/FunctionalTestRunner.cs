using System.Diagnostics;

namespace CalibraHub.Web.Services.FunctionalTests;

/// <summary>
/// Kayıtlı senaryoları bağımlılık sırasına göre çalıştırır ve her adımı NDJSON frame'i
/// olarak <paramref name="emit"/> delegate'ine yazdırır (bkz. HealthCheckController.
/// StreamFunctionalTests — frame şekilleri orada sabitlenmiştir, burada DEĞİŞTİRİLMEZ).
///
/// Bağımlılık çözümü: <paramref name="groups"/> filtresi yalnız HANGİ senaryoların "istendiğini"
/// belirler; bir istenen senaryonun DependsOn zinciri (grubu ne olursa olsun — örn. üretim
/// senaryosu SEED_MASTER_DATA'ya (grup=ticari) bağımlı olabilir) OTOMATİK olarak kapsama dahil
/// edilir (transitive closure). Aksi halde "yalnız üretim testlerini çalıştır" isteği seed
/// adımını atlar ve her şey "önkoşul bulunamadı" ile boşa düşerdi.
/// </summary>
public sealed class FunctionalTestRunner
{
    private readonly IReadOnlyList<IFunctionalTestScenario> _all;

    public FunctionalTestRunner(IEnumerable<IFunctionalTestScenario> scenarios)
    {
        _all = scenarios.ToList();
    }

    public async Task RunAsync(
        IReadOnlyCollection<string>? groups,
        FunctionalTestContext ctx,
        Func<object, CancellationToken, Task> emit,
        CancellationToken ct)
    {
        var byKey = new Dictionary<string, IFunctionalTestScenario>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in _all)
        {
            if (!byKey.TryAdd(s.Key, s))
                throw new InvalidOperationException($"Senaryo anahtarı tekrarlı: '{s.Key}'.");
        }

        var requestedGroups = (groups is { Count: > 0 })
            ? new HashSet<string>(groups, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(_all.Select(s => s.Group), StringComparer.OrdinalIgnoreCase);

        var selected = _all.Where(s => requestedGroups.Contains(s.Group)).ToList();

        // Bağımlılık kapanışı — topological sıra (DFS post-order).
        var ordered = new List<IFunctionalTestScenario>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Visit(IFunctionalTestScenario s)
        {
            if (visited.Contains(s.Key)) return;
            if (!visiting.Add(s.Key))
                throw new InvalidOperationException($"Döngüsel bağımlılık tespit edildi: '{s.Key}'.");
            foreach (var depKey in s.DependsOn)
                if (byKey.TryGetValue(depKey, out var dep))
                    Visit(dep);
            visiting.Remove(s.Key);
            visited.Add(s.Key);
            ordered.Add(s);
        }

        foreach (var s in selected)
            Visit(s);

        var groupSummary = ordered
            .GroupBy(s => s.Group, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { key = g.Key, label = GroupLabel(g.Key), count = g.Count() })
            .ToArray();

        await emit(new { type = "fn_start", total = ordered.Count, groups = groupSummary }, ct);

        var notRunnable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int passed = 0, failed = 0, skipped = 0;

        for (var i = 0; i < ordered.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var s = ordered[i];

            await emit(new
            {
                type = "fn_step",
                index = i + 1,
                total = ordered.Count,
                key = s.Key,
                group = s.Group,
                label = s.Label,
            }, ct);

            var missingDep = s.DependsOn.FirstOrDefault(d => notRunnable.Contains(d));
            var sw = Stopwatch.StartNew();
            bool isSkipped;
            FunctionalTestOutcome outcome;

            if (missingDep != null)
            {
                isSkipped = true;
                outcome = new FunctionalTestOutcome(false,
                    $"Önkoşul '{missingDep}' başarısız veya atlandığı için bu senaryo çalıştırılmadı.",
                    Array.Empty<FunctionalTestStep>());
            }
            else
            {
                isSkipped = false;
                try
                {
                    outcome = await s.RunAsync(ctx, ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    outcome = new FunctionalTestOutcome(false, "Senaryo çalıştırılırken beklenmeyen hata: " + ex.Message,
                        Array.Empty<FunctionalTestStep>());
                }
            }
            sw.Stop();

            if (isSkipped) { skipped++; notRunnable.Add(s.Key); }
            else if (!outcome.Ok) { failed++; notRunnable.Add(s.Key); }
            else passed++;

            await emit(new
            {
                type = "fn_result",
                index = i + 1,
                total = ordered.Count,
                key = s.Key,
                group = s.Group,
                label = s.Label,
                ok = outcome.Ok,
                skipped = isSkipped,
                durationMs = (int)sw.ElapsedMilliseconds,
                message = outcome.Message,
                steps = outcome.Steps.Select(st => new { label = st.Label, ok = st.Ok, message = st.Message }).ToArray(),
            }, ct);
        }

        await emit(new { type = "fn_done", passed, failed, skipped }, ct);
    }

    private static string GroupLabel(string key) => key.ToLowerInvariant() switch
    {
        "ticari" => "Ticari",
        "uretim" => "Üretim",
        "kalite" => "Kalite",
        _ => key,
    };
}
