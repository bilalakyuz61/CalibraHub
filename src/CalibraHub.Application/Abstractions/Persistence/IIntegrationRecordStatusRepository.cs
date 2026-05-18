using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// "Aktarim Kuyrugu" sayfasinin durum tablosu erisim katmani.
///
/// Iki gorev:
///   1. IntegrationRunner her run sonunda Upsert ile durumu (Sent/Failed) yazar
///   2. Kuyruk sayfasi Pending/Failed kayitlari listeler ve Skipped'a tasir
/// </summary>
public interface IIntegrationRecordStatusRepository
{
    /// <summary>
    /// (IntegrationId, RecordId) icin mevcut durum varsa onu doner, yoksa null.
    /// </summary>
    Task<IntegrationRecordStatus?> GetAsync(int integrationId, string recordId, CancellationToken ct);

    /// <summary>
    /// Run sonucunu kaydet — yoksa olustur, varsa guncelle. IntegrationRunner
    /// her basarili/basarisiz run sonunda cagirir. Status: Sent veya Failed.
    /// Skipped uzerine yazmaz (kullanici manuel haric tutmussa run sonucu
    /// status'u Pending'e dusurmemeli — bu yontem Skipped'i korur).
    /// </summary>
    Task UpsertRunResultAsync(int integrationId, string recordId,
        IntegrationRecordStatusType status, long? runId, string? error,
        string? actor, CancellationToken ct);

    /// <summary>
    /// Bir veya birden cok kaydi "Haric Tut" (Skipped) durumuna gec.
    /// Mevcut satir yoksa once Pending olarak yaratir, sonra Skipped'a alir.
    /// </summary>
    Task SkipManyAsync(int integrationId, IEnumerable<string> recordIds,
        string? reason, string actor, CancellationToken ct);

    /// <summary>
    /// Skipped durumdaki kayitlari geri al — Status = Pending. Tekrar kuyrukta
    /// gorunur.
    /// </summary>
    Task RestoreManyAsync(int integrationId, IEnumerable<string> recordIds,
        string actor, CancellationToken ct);

    /// <summary>
    /// Bir entegrasyonun belirli durumdaki kayitlarini listele. Cagiran
    /// taraf (Controller) bunu kaynak tablo (Items / Document / vb.) ile
    /// outer-join edip kuyruk row'larini olusturur. status null = hepsi.
    /// </summary>
    Task<IReadOnlyList<IntegrationRecordStatus>> ListByStatusAsync(
        int integrationId, IntegrationRecordStatusType? status, CancellationToken ct);

    /// <summary>
    /// Bir entegrasyon icin durum sayilari ozeti — kuyruk sayfasi basliginda
    /// "Bekleyen: 47, Hatali: 3, Gonderilen: 200, Haric: 5" gibi rozetler icin.
    /// </summary>
    Task<IntegrationRecordStatusSummary> GetSummaryAsync(
        int integrationId, CancellationToken ct);
}

/// <summary>
/// Kuyruk sayfasi basligindaki sayilar.
/// </summary>
public sealed record IntegrationRecordStatusSummary(
    int Pending,
    int Failed,
    int Sent,
    int Skipped);
