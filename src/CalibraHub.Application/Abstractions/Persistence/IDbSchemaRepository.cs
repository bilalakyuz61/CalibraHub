using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Aktif sirket DB'sinden fiziksel sema metadata'si okur (sys.* views).
/// Sadece metadata; ornek veri / PII okunmaz.
/// </summary>
public interface IDbSchemaRepository
{
    Task<IReadOnlyList<DbTableSummaryDto>> GetTablesAsync(CancellationToken cancellationToken);

    Task<DbTableDetailDto?> GetTableDetailAsync(string schema, string name, CancellationToken cancellationToken);

    /// <summary>Tum FK'leri tek sorguda doner (Mermaid ER export icin).</summary>
    Task<IReadOnlyList<DbForeignKeyDto>> GetAllForeignKeysAsync(CancellationToken cancellationToken);
}
