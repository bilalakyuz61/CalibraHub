namespace CalibraHub.Application.Diagnostics.SqlTrace;

/// <summary>
/// Canlı SQL+istek izleme (SQL Profiler benzeri) oturumu. Kapalıyken (varsayılan durum,
/// uygulama açılışında hep kapalı) hiçbir yakalama/ayrıştırma çalışmaz — bkz. SqlTraceService
/// XML doc'u. Yalnızca /AuditLog ekranındaki "İzlemeyi Başlat" ile açılır, kendiliğinden
/// (süre dolunca) veya elle kapanır.
/// </summary>
public interface ISqlTraceService
{
    bool IsRunning { get; }
    DateTime? ExpiresAtUtc { get; }

    /// <summary>Oturumu başlatır (veya zaten açıksa süreyi/tamponu yeniler). Süre 1-60 dk arasına kırpılır.</summary>
    DateTime Start(int durationMinutes);

    /// <summary>Oturumu hemen kapatır — DiagnosticListener aboneliği GERÇEKTEN sökülür (bayrak değil).</summary>
    void Stop();

    /// <summary>seq &gt; afterSeq olan kayıtları döner (istemci long-poll için).</summary>
    SqlTraceEventsResult GetEvents(long afterSeq);

    /// <summary>
    /// HTTP isteği tamamlandığında Web katmanı middleware'i tarafından çağrılır. Oturum kapalıyken
    /// no-op'tur (çağıran taraf zaten IsRunning kontrolüyle bu maliyeti de atlayabilir).
    /// </summary>
    void RecordRequest(string requestId, string path, string method, int statusCode, double durationMs);
}
