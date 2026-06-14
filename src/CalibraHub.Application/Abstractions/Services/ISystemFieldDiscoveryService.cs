namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Sprint 1 — Universal Form Engine. Domain entity'lerinin public property'lerini
/// hedef formun WidgetMas tablosuna IsSystemField=true ile seed eder.
///
/// Idempotent: ayni EntityColumn'a sahip bir kayit varsa atlanir; yalnizca yeni
/// property'ler eklenir. Mevcut kayitlarin label/IsRequired/visibility gibi
/// admin override'lari korunur — discovery sadece "schema-readonly" semasal
/// alanlari (IsSystemField, EntityColumn, DataType base'i) ekleyen idempotent
/// bir seed'dir.
///
/// Hangi entity hangi form'a baglanir? Implementasyon icindeki sabit registry:
///   - Item       → ITEMS
/// (Sprint 2'de Contact, Document, Currency vb. de buraya eklenecek)
/// </summary>
public interface ISystemFieldDiscoveryService
{
    /// <summary>
    /// Kayitli tum entity-form ciftleri icin discovery calistirir.
    /// Donus: toplam yeni eklenen widget sayisi.
    /// Try/catch yok — hata firlatirsa Program.cs caller sessizce loglayip devam eder.
    /// </summary>
    Task<int> DiscoverAndSeedAsync(CancellationToken ct);
}
