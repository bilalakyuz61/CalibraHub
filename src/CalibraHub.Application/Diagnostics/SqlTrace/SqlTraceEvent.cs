namespace CalibraHub.Application.Diagnostics.SqlTrace;

/// <summary>Bir izleme kaydının türü. AuditMonitor bu üç değeri ayrı ayrı render eder.</summary>
public static class SqlTraceEventKind
{
    /// <summary>Başarıyla tamamlanan SQL komutu (WriteCommandAfter).</summary>
    public const string Sql = "sql";
    /// <summary>HTTP isteği özeti (yol+metot+durum kodu+süre).</summary>
    public const string Request = "request";
    /// <summary>Hata ile sonuçlanan SQL komutu (WriteCommandError).</summary>
    public const string Error = "error";
}

/// <summary>Yakalanan tek bir SQL parametresi — ad her zaman görünür, değer gerekirse maskelenir.</summary>
public sealed record SqlTraceParam(string Name, string? Value);

/// <summary>
/// Halka tampondaki (ring buffer) tek bir izleme kaydı. Yalnızca BELLEKTE tutulur, diske
/// YAZILMAZ (kullanıcı kararı: parametre değerleri gerçek müşteri verisi taşıyabilir; canlı
/// izleme isteniyor ama dosyalarda kalıcı birikmesi istenmiyor — bkz. SqlTraceBuffer).
/// </summary>
public sealed record SqlTraceEvent(
    long Seq,
    DateTime TsUtc,
    string Kind,
    string? RequestId,
    double? DurationMs,
    string? Text,
    IReadOnlyList<SqlTraceParam>? Parameters,
    string? Path,
    string? Method,
    int? StatusCode,
    string? Error,
    string? Database,
    bool Truncated);

/// <summary>GET /AuditLog/Trace/Events yanıtının servis-katmanı karşılığı.</summary>
public sealed record SqlTraceEventsResult(
    bool Running,
    DateTime? ExpiresAtUtc,
    IReadOnlyList<SqlTraceEvent> Events,
    long DroppedCount);
