namespace CalibraHub.Web.Services.FunctionalTests;

/// <summary>Bir senaryo içindeki tek adımın sonucu — "hangi adımda kırıldığı" burada görünür.</summary>
public sealed record FunctionalTestStep(string Label, bool Ok, string? Message);

/// <summary>Bir senaryonun toplam sonucu — Ok, ilk başarısız adımın mesajıdır (varsa).</summary>
public sealed record FunctionalTestOutcome(bool Ok, string? Message, IReadOnlyList<FunctionalTestStep> Steps);

/// <summary>
/// Senaryo sözleşmesi. Yeni senaryo (üretim/kalite dahil) bu arayüzü uygular:
///   - <see cref="Key"/>: benzersiz, ekran-anlamlı kod (örn. "SALES_QUOTE_CREATE").
///   - <see cref="Group"/>: "ticari" | "uretim" | "kalite" — NDJSON groups filtresi bunu kullanır.
///   - <see cref="DependsOn"/>: önkoşul senaryo Key listesi. Önkoşul başarısız/atlanmışsa bu
///     senaryo da ÇALIŞTIRILMADAN "atlandı" raporlanır (bkz. FunctionalTestRunner).
/// </summary>
public interface IFunctionalTestScenario
{
    string Key { get; }
    string Group { get; }
    string Label { get; }
    IReadOnlyList<string> DependsOn { get; }

    Task<FunctionalTestOutcome> RunAsync(FunctionalTestContext ctx, CancellationToken ct);
}

/// <summary>
/// Adım-adım ilerleyen senaryolar için taban sınıf. <see cref="ExecuteAsync"/> içinde
/// <see cref="Pass"/>/<see cref="Fail"/> ile adım kaydedilir; bir adım <see cref="Fail"/>
/// olunca metottan erken dönülmesi ÖNERİLİR (sonraki adımlar genelde önceki adımın ürettiği
/// veriye bağlıdır) — ama zorunlu değildir, taban sınıf listedeki İLK başarısız adımı
/// senaryonun genel mesajı olarak kullanır. Senaryo içinde beklenmeyen bir exception
/// fırlarsa (örn. HTTP client çöktü) taban sınıf bunu yakalayıp "Beklenmeyen hata" adımına
/// çevirir — runner asla senaryo kodundaki bir exception yüzünden tüm akışı düşürmez.
/// </summary>
public abstract class FunctionalTestScenarioBase : IFunctionalTestScenario
{
    public abstract string Key { get; }
    public abstract string Group { get; }
    public abstract string Label { get; }
    public virtual IReadOnlyList<string> DependsOn => Array.Empty<string>();

    protected abstract Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct);

    public async Task<FunctionalTestOutcome> RunAsync(FunctionalTestContext ctx, CancellationToken ct)
    {
        var steps = new List<FunctionalTestStep>();
        try
        {
            await ExecuteAsync(ctx, steps, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            steps.Add(new FunctionalTestStep("Beklenmeyen hata", false, ex.Message));
        }
        var firstFailed = steps.FirstOrDefault(s => !s.Ok);
        return new FunctionalTestOutcome(firstFailed is null, firstFailed?.Message, steps);
    }

    protected static void Pass(List<FunctionalTestStep> steps, string label, string? message = null)
        => steps.Add(new FunctionalTestStep(label, true, message));

    protected static void Fail(List<FunctionalTestStep> steps, string label, string? message)
        => steps.Add(new FunctionalTestStep(label, false, message ?? "Bilinmeyen hata."));

    /// <summary>Adımı çalıştırır (POST); başarısızsa Fail kaydeder ve false döner (çağıran genelde bunun üstüne "return" yapar).</summary>
    protected static async Task<(bool Ok, System.Text.Json.JsonElement Json)> StepPostAsync(
        FunctionalTestContext ctx, List<FunctionalTestStep> steps, string label, string path, object? body, CancellationToken ct)
    {
        var res = await ctx.Http.PostAsync(path, body, ct);
        if (!res.Ok) { Fail(steps, label, res.Error); return (false, res.Json); }
        Pass(steps, label);
        return (true, res.Json);
    }

    /// <summary>PUT ile kaydeden REST uçları için (bkz. FunctionalTestHttpClient.PutAsync).</summary>
    protected static async Task<(bool Ok, System.Text.Json.JsonElement Json)> StepPutAsync(
        FunctionalTestContext ctx, List<FunctionalTestStep> steps, string label, string path, object? body, CancellationToken ct)
    {
        var res = await ctx.Http.PutAsync(path, body, ct);
        if (!res.Ok) { Fail(steps, label, res.Error); return (false, res.Json); }
        Pass(steps, label);
        return (true, res.Json);
    }

    protected static async Task<(bool Ok, System.Text.Json.JsonElement Json)> StepGetAsync(
        FunctionalTestContext ctx, List<FunctionalTestStep> steps, string label, string path, CancellationToken ct)
    {
        var res = await ctx.Http.GetAsync(path, ct);
        if (!res.Ok) { Fail(steps, label, res.Error); return (false, res.Json); }
        Pass(steps, label);
        return (true, res.Json);
    }
}
