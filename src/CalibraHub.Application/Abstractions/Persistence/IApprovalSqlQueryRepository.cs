using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// ApprovalSqlQuery tablosu için CRUD soyutlaması.
/// Admin SQL kütüphanesi — onay akışı Karar (Decision) node'larından çağrılan
/// named query'leri tutar.
/// </summary>
public interface IApprovalSqlQueryRepository
{
    Task<IReadOnlyList<ApprovalSqlQueryEntity>> GetAllAsync(CancellationToken ct);
    Task<ApprovalSqlQueryEntity?> GetByIdAsync(int id, CancellationToken ct);
    Task<int> AddAsync(ApprovalSqlQueryEntity entity, CancellationToken ct);
    Task UpdateAsync(ApprovalSqlQueryEntity entity, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}
