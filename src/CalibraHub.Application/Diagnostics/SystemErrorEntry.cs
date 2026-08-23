namespace CalibraHub.Application.Diagnostics;

/// <summary>
/// Tek bir yazılım/DB hata kaydı — günlük JSONL dosyasına yazılan satır.
/// <see cref="Application.Auditing.AuditEntry"/> ile PARALEL ama BAĞIMSIZ bir yapı
/// (audit trail'e dokunulmaz — bkz. CLAUDE.md "İşlem Log Modülü").
/// Alan adları camelCase serialize edilir (id, timestampUtc, level, category, message,
/// exceptionType, exceptionMessage, stackTrace, source, requestPath, userName, companyId).
/// </summary>
public sealed class SystemErrorEntry
{
    /// <summary>
    /// Kayıt için stabil kimlik ({yyyyMMddHHmmssfff}-{seq}) — dosyada satır sırasından
    /// bağımsız, frontend liste key'i ve detay paneli için kullanılır.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    public DateTime TimestampUtc { get; set; }

    /// <summary>"Error" | "Critical".</summary>
    public string Level { get; set; } = "Error";

    /// <summary>ILogger kategori adı (genelde tam tip adı — ör. "CalibraHub.Web.Controllers.SalesController").</summary>
    public string Category { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string? ExceptionType { get; set; }

    public string? ExceptionMessage { get; set; }

    public string? StackTrace { get; set; }

    /// <summary>Exception.Source — genelde derleme/assembly adı.</summary>
    public string? Source { get; set; }

    /// <summary>İstek varsa HTTP path'i (ör. "/Sales/SaveDocument").</summary>
    public string? RequestPath { get; set; }

    /// <summary>Aktif kullanıcı adı/e-posta; kimliksiz veya background akışta null.</summary>
    public string? UserName { get; set; }

    public int? CompanyId { get; set; }
}
