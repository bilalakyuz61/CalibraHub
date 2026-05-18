namespace CalibraHub.Domain.Enums;

/// <summary>
/// Zamanlanmis gorevin turunu belirler — executor dispatch icin kullanilir.
/// Scheduler'in string degeri case-insensitive kontrol ettigi icin yeni tip
/// eklendiginde executor registry'ye de kayit eklemek gerekir.
/// </summary>
public enum ScheduledTaskType
{
    /// <summary>
    /// .NET BackgroundService ile hardcoded implementation (DocumentImport, Reminder, ExchangeRate).
    /// Worker'daki service kendi scheduling'ini yonetir; scheduler sadece durum okur/yazar.
    /// </summary>
    Builtin = 0,

    /// <summary>
    /// SQL stored procedure calistirir. Parameters: {"procedureName":"sp_X", "parameters":{...}}.
    /// </summary>
    SqlProcedure = 1,

    /// <summary>
    /// HTTP API cagrisi yapar (GET/POST). Parameters: {"url":"...", "method":"GET",
    /// "headers":{...}, "body":"..."}.
    /// </summary>
    HttpApi = 2,

    /// <summary>
    /// Dosya transferi/isleme (FTP/SFTP/local). Parameters: {"host":"...", "path":"...", "operation":"download"}.
    /// </summary>
    FileTransfer = 3,

    /// <summary>
    /// TCMB'den doviz kurlarini ceker ve Exchange tablosuna yazar.
    /// Parametresiz — CurrencyService.UpdateRatesFromTcmbAsync icindeki hafta sonu/tatil
    /// fallback mantigi ile en yakin is gununun kurlarini bugune stamp eder.
    /// </summary>
    CurrencyRefresh = 4,

    /// <summary>
    /// DB view'ini sorgulayip sonucu rapor dosyasi (CSV/XLSX/PDF) olarak belirli mail
    /// adreslerine gonderir.
    /// Parameters: {"viewName":"vw_X","recipients":["a@b","c@d"],"format":"csv|xlsx|pdf",
    /// "subject":"...","bodyText":"...","maxRows":10000}.
    /// </summary>
    ViewReport = 5,

    /// <summary>
    /// Bir entegrasyonu cron/interval ile zamanli calistirir (IntegrationRunner).
    /// Parameters: {"integrationId": 12, "recordId": "optional source record id"}.
    /// recordId verilmezse Integration'in tum kayitlari icin (V2 batch) calismaz —
    /// V1'de tek bir "manuel calistir" semantigi (recordId=null ise wizard test akisi).
    /// </summary>
    Integration = 6,
}
