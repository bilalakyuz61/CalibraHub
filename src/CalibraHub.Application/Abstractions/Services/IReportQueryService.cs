using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface IReportQueryService
{
    /// <summary>Kayıtlı kaynak ID'siyle sorgu çalıştırır (SQL backend'de tutulur).</summary>
    Task<ReportQueryResult> QuerySourceAsync(int sourceId, CancellationToken ct);

    /// <summary>Panel'den gelen inline SQL'i çalıştırır ve cache'ler.</summary>
    Task<ReportQueryResult> QueryInlineAsync(string sql, int cacheTtlMinutes, CancellationToken ct);

    /// <summary>Kaynağın snapshot tablosunu (dbo.ReportSnapshot_{id}) yeniden oluşturur. Satır sayısı döner. (Web/HttpContext bağlamı.)</summary>
    Task<int> MaterializeSourceAsync(int sourceId, CancellationToken ct);

    /// <summary>Snapshot'ı belirli bir şirket DB'sinde yeniden oluşturur (zamanlanmış görev/worker — HttpContext yok, companyId açık verilir).</summary>
    Task<int> MaterializeSourceForCompanyAsync(int companyId, int sourceId, CancellationToken ct);

    /// <summary>Belirtilen kaynağın canlı-SQL cache'ini temizler (SQL/parametre değişince — örn. kaynak kaydedilince çağrılır).</summary>
    Task InvalidateSourceAsync(int sourceId, CancellationToken ct);
}
