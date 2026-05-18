using System.Text.Json.Nodes;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Entegrasyon Wizard mapping kurallarini bir form kaydina uygulayarak hedef JSON
/// body'sini ureten motor. Strategy pattern:
///   FormField → kaynak alandan oku
///   Constant  → literal deger
///   Formula   → NCalc expression (form alanlari parametre)
///   Lookup    → standart rehber (cbv_Guide_*) cozumlemesi
///
/// Sonuc: JsonObject (System.Text.Json.Nodes) — HTTP body olarak serialize edilir.
/// </summary>
public interface IMappingEngine
{
    /// <summary>
    /// Tek-seviyeli (sadece header) mapping — mevcut kullanim, geriye uyum.
    /// Mapping rule'larin tamami SourceSection="Header" gibi davranir.
    /// </summary>
    Task<JsonObject> BuildAsync(
        Integration integration,
        IntegrationSampleRecordDto source,
        CancellationToken ct);

    /// <summary>
    /// Master-Detail mapping — 3 katmanli "veri seti":
    ///   • Header mapping'leri (SourceSection="Header") → headerData kullanilir
    ///   • Lines mapping'leri  (SourceSection="Lines")  → her line icin iterate, array hedef olusur
    ///   • Combination mapping'leri (SourceSection="Combination") → her line.CombinationId
    ///     resolve edilir, line context'inde combinationCodes[lineCombinationId] cozulur
    ///
    /// Hedef path array prefix iceriyorsa (orn. "Kalemler[].StokKodu"), her line icin
    /// "Kalemler[index].StokKodu" yazilir. Header mapping'leri her zaman tek satir uretir.
    ///
    /// linesData null veya bos ise sadece header islenir (geriye uyum).
    /// </summary>
    Task<JsonObject> BuildAsync(
        Integration integration,
        IReadOnlyDictionary<string, object?> headerData,
        IReadOnlyList<IReadOnlyDictionary<string, object?>>? linesData,
        IReadOnlyDictionary<int, string>? combinationCodes,
        CancellationToken ct);
}
