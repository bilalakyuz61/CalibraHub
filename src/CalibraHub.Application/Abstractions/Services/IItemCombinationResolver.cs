namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Master-Detail entegrasyonun "kombinasyon kodu" katmaninin runtime resolver'i.
///
/// Her DocumentLine.CombinationId → ItemConfiguration.RecordCode ile cozulur.
/// Wizard Step 3'te kullanici Combination.Code field'ini source olarak secebilir;
/// IntegrationRunner runtime'da bu servis ile her kalem icin kombinasyon kodunu
/// cozer ve mapping engine'a verir.
///
/// Performans: Toplu fetch (GetCombinationCodesAsync) — N+1 onler. Bos veya null
/// id'ler skip edilir. Donus dictionary'sinde combinationId → code mapping.
/// </summary>
public interface IItemCombinationResolver
{
    /// <summary>Tek bir kombinasyon kodu cozumlemesi (test/preview icin).</summary>
    Task<string?> GetCombinationCodeAsync(int? combinationId, CancellationToken ct);

    /// <summary>
    /// Toplu — N kalem icin tek SQL sorgu. Empty/duplicate set frontend'den gelse
    /// bile repository tarafinda dedup yapilir.
    /// Donus: combinationId → RecordCode dictionary.
    /// </summary>
    Task<IReadOnlyDictionary<int, string>> GetCombinationCodesAsync(
        IEnumerable<int> combinationIds, CancellationToken ct);
}
