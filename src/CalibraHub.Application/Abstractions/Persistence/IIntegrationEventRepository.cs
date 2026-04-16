using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public record ProcedureParameter(
    string Name,
    System.Data.ParameterDirection Direction,
    System.Data.SqlDbType DbType,
    object? Value);

public interface IIntegrationEventRepository
{
    Task<IReadOnlyCollection<IntegrationEventDefinition>> GetByCompanyAsync(int companyId, CancellationToken ct);
    Task<IReadOnlyCollection<IntegrationEventDefinition>> GetActiveAsync(int companyId, string eventSource, string eventType, CancellationToken ct);
    Task<IntegrationEventDefinition?> GetByIdAsync(Guid id, CancellationToken ct);
    Task UpsertDefinitionAsync(IntegrationEventDefinition def, CancellationToken ct);
    Task DeleteDefinitionAsync(Guid id, CancellationToken ct);
    Task AddLogAsync(IntegrationEventLog log, CancellationToken ct);
    Task<IReadOnlyCollection<IntegrationEventLog>> GetRecentLogsAsync(int companyId, int take, CancellationToken ct);

    /// <summary>
    /// Sirketin kendi DB'sinde (veya sistem DB'sinde) verilen SQL'i calistirir.
    /// </summary>
    Task ExecuteSqlOnCompanyDbAsync(int companyId, string sql, int timeoutSeconds, CancellationToken ct);

    /// <summary>
    /// Sirketin kendi DB'sinde saklı yordam çalıştırır. Output parametreleri (returnCode, returnMessage) geri döner.
    /// </summary>
    Task<(int returnCode, string? returnMessage)> ExecuteProcedureOnCompanyDbAsync(
        int companyId,
        string procedureName,
        IReadOnlyList<ProcedureParameter> parameters,
        int timeoutSeconds,
        CancellationToken ct);
}
