using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface ICompanyRepository
{
    Task<IReadOnlyCollection<Company>> GetAllAsync(CancellationToken cancellationToken);
    Task<Company?> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<int> AddAsync(Company company, CancellationToken cancellationToken);
    Task UpdateAsync(Company company, CancellationToken cancellationToken);
    /// <summary>
    /// FAZ 1 backfill/derivation icin dar kapsamli update — yalnizca [DatabaseName] kolonunu
    /// yazar, digger alanlara (Updated dahil) dokunmaz. Tam UpdateAsync'in aksine yan etkisizdir.
    /// </summary>
    Task UpdateDatabaseNameAsync(int id, string databaseName, CancellationToken cancellationToken);
    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
