namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Belge tablolarinda (document, ileride digerleri) entegrasyon "gonderildi mi"
/// durumunu tutmaya yarayan helper. IntegrationRunner basarili HTTP sonrasi
/// MarkSentAsync cagirir; OnSave ve UI duplicate-prevention bu kolonlari okur.
///
/// Tracking kolonlari (Document tablosunda):
///   IntegrationSentAt    DATETIME2  — son basarili gonderim zamani
///   IntegrationStatus    NVARCHAR   — 'Sent' | 'Failed' | NULL
///   LastIntegrationRunId BIGINT     — son IntegrationRun.Id (audit linki)
///   LastIntegrationId    INT        — hangi entegrasyon konfigi calisti
///
/// Form'un BaseTable'i 'document' degilse silently no-op (sadece document tablo
/// destegi var; ileride Item/Contact icin de eklenebilir).
/// </summary>
public interface IIntegrationStatusTracker
{
    /// <summary>
    /// Bir kaydi 'Sent' olarak isaretle (basarili HTTP sonrasi).
    /// </summary>
    Task MarkSentAsync(string baseTable, string baseRecordKey, string recordId,
        int integrationId, long runId, CancellationToken ct);

    /// <summary>
    /// Bir kaydi 'Failed' olarak isaretle (hata olduysa). LastIntegrationRunId
    /// guncellenir ama IntegrationSentAt dokunulmaz (eski basarili gonderim varsa kalsin).
    /// </summary>
    Task MarkFailedAsync(string baseTable, string baseRecordKey, string recordId,
        int integrationId, long runId, CancellationToken ct);

    /// <summary>
    /// Bir kaydin onceden 'Sent' edilip edilmedigini sorgula. OnSave duplicate-guard
    /// ve UI'da "Yeniden Gonder" gostermek icin.
    /// </summary>
    Task<DateTime?> GetSentAtAsync(string baseTable, string baseRecordKey, string recordId,
        CancellationToken ct);
}
