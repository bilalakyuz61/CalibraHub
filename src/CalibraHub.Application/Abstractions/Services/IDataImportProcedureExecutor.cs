using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// İçe aktarım öncesi/sonrası stored procedure çalıştırıcısı.
///
/// Hedef iki olabilir (bkz. <see cref="DataImportProcedureTarget"/>):
///   • <c>Calibra</c> — CalibraHub'ın kendi şirket DB'si (varsayılan).
///   • <c>Source</c>  — harici kaynak DB. Bu, kaynağa YAZMANIN tek yoludur
///     (örn. okunan satırları "aktarıldı" işaretlemek) ve yalnız iş tanımında
///     açıkça seçilmişse çalışır. Okuma yolu her koşulda salt-okunur kalır.
///
/// Prosedür adı identifier doğrulamasından geçer; parametreler <c>SqlParameter</c>
/// olarak bağlanır (SQL injection güvenli). Metod exception fırlatmaz — hata sonuca taşınır.
/// </summary>
public interface IDataImportProcedureExecutor
{
    /// <param name="sourceConnection">
    /// <paramref name="target"/> = <c>Source</c> ise zorunlu; <c>Calibra</c> ise yok sayılır.
    /// </param>
    Task<DataImportProcedureResult> ExecuteAsync(
        string procedureName,
        DataImportProcedureTarget target,
        ExternalDbConnection? sourceConnection,
        string? paramsJson,
        DataImportProcedureContext context,
        CancellationToken ct);
}
