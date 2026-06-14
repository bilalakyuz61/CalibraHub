using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IStockDocRepository
{
    Task<IReadOnlyList<StockDocDto>> GetByTypeAsync(string docType, CancellationToken ct);
    Task<IReadOnlyList<StockDocDto>> GetByTypesAsync(IEnumerable<string> docTypes, CancellationToken ct);
    Task<StockDocDto?> GetByIdAsync(int id, CancellationToken ct);
    Task<IReadOnlyList<StockDocLineDto>> GetLinesAsync(int docId, CancellationToken ct);
    Task<(int Id, string DocNo)> SaveAsync(SaveStockDocRequest request, int? createdById, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}
