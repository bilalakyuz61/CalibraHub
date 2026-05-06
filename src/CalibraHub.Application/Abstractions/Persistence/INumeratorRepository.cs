namespace CalibraHub.Application.Abstractions.Persistence;

public interface INumeratorRepository
{
    /// <summary>
    /// Atomik olarak entityType icin sayaci arttirip yeni degeri doner.
    /// resetPolicy = "Yearly" ise yil bazinda sifirlanir, "Never" ise sifirlanmaz.
    /// Mevcut request'in CompanyId'sine gore izole calisir.
    /// </summary>
    Task<int> GetNextValueAsync(string entityType, string resetPolicy, CancellationToken ct);
}
