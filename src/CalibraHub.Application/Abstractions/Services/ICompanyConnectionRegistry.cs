namespace CalibraHub.Application.Abstractions.Services;

public interface ICompanyConnectionRegistry
{
    void Set(int companyId, string? connectionString);
    void Remove(int companyId);
}
