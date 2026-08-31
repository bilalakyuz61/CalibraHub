namespace CalibraHub.Application.Diagnostics.SqlTrace;

/// <summary>
/// İstek↔SQL eşleştirmesi için AsyncLocal tabanlı bağlam. Web katmanındaki inline middleware
/// (Program.cs) her istek başında <see cref="Current"/>'ı `HttpContext.TraceIdentifier` ile
/// set eder; ExecutionContext akışı sayesinde aynı async zincirde çalışan SQL komutları bu
/// değeri okuyup kendi kaydına ekler (SqlTraceService.OnSqlEvent). Arka plan işleri (Worker,
/// hosted service) içinde çalışan SQL'lerde bu değer null kalır — kayıt yine tutulur, sadece
/// requestId boş görünür.
/// </summary>
public static class SqlTraceContext
{
    /// <summary>
    /// Kendini-izleme döngüsünü kesmek için özel değer: /AuditLog/Trace/* uçlarını servis eden
    /// istek boyunca bu değer set edilir. SqlTraceService bu değeri gördüğünde SQL kaydı ASLA
    /// tampona eklenmez — aksi halde "izlemeyi sorgula → kaydı tampona yaz → bir sonraki
    /// sorguda o kaydı da göster" döngüsü tamponu sürekli büyütür.
    /// </summary>
    public const string Excluded = "__sqltrace_excluded__";

    private static readonly AsyncLocal<string?> _current = new();

    public static string? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}
