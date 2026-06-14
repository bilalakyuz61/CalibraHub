using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// ApprovalSqlQuery iş katmanı — CRUD pass-through + güvenli SQL
/// validation/execution. ScriptDom ile parse + tablo whitelist + 5sn timeout.
/// </summary>
public interface IApprovalSqlQueryService
{
    Task<IReadOnlyList<ApprovalSqlQueryDto>> GetAllAsync(CancellationToken ct);
    Task<ApprovalSqlQueryDto?> GetByIdAsync(int id, CancellationToken ct);
    Task<int> SaveAsync(SaveApprovalSqlQueryRequest request, int? userId, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);

    /// <summary>
    /// SQL'in çalıştırılabilir olduğunu doğrula. Tek SELECT statement,
    /// DML/DDL yok, sadece whitelist tablo/view. Hata varsa (false, error mesajı).
    /// </summary>
    Task<(bool Ok, string? Error)> ValidateSqlAsync(string sql);

    /// <summary>
    /// SQL'i validate edip parametrize çalıştırır (ExecuteScalarAsync).
    /// Hata durumunda Ok=false + Error doldurulur.
    /// </summary>
    Task<ExecuteApprovalSqlResult> ExecuteAsync(
        string? sqlText,
        IReadOnlyDictionary<string, object?>? parameters,
        CancellationToken ct);
}
