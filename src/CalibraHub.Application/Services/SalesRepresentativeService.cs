using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Services;

public sealed class SalesRepresentativeService : ISalesRepresentativeService
{
    private readonly ISalesRepresentativeRepository _repo;

    public SalesRepresentativeService(ISalesRepresentativeRepository repo) => _repo = repo;

    public async Task<IReadOnlyCollection<SalesRepresentativeDto>> GetAllAsync(CancellationToken ct)
    {
        var all = await _repo.GetAllAsync(ct);
        return all.Select(x => new SalesRepresentativeDto(x.Id, x.RepName, x.IsActive)).ToArray();
    }

    public async Task<SalesRepresentativeDto?> GetByIdAsync(int id, CancellationToken ct)
    {
        var e = await _repo.GetByIdAsync(id, ct);
        return e is null ? null : new SalesRepresentativeDto(e.Id, e.RepName, e.IsActive);
    }

    public async Task<(bool Success, string? Error, int? Id)> CreateAsync(CreateSalesRepresentativeRequest request, CancellationToken ct)
    {
        var name = request.RepName?.Trim() ?? "";
        if (string.IsNullOrEmpty(name)) return (false, "Temsilci adi bos olamaz.", null);

        var all = await _repo.GetAllAsync(ct);
        if (all.Any(x => x.RepName.Trim().Equals(name, StringComparison.OrdinalIgnoreCase)))
            return (false, $"Aynı isimde başka bir satış temsilcisi zaten tanımlı: '{name}'", null);

        var newId = await _repo.AddAsync(new SalesRepresentative { RepName = name, IsActive = request.IsActive }, ct);
        return (true, null, newId);
    }

    public async Task<(bool Success, string? Error)> UpdateAsync(UpdateSalesRepresentativeRequest request, CancellationToken ct)
    {
        var name = request.RepName?.Trim() ?? "";
        if (string.IsNullOrEmpty(name)) return (false, "Temsilci adi bos olamaz.");

        var existing = await _repo.GetByIdAsync(request.Id, ct);
        if (existing is null) return (false, "Temsilci bulunamadi.");

        var all = await _repo.GetAllAsync(ct);
        if (all.Any(x => x.Id != request.Id &&
                         x.RepName.Trim().Equals(name, StringComparison.OrdinalIgnoreCase)))
            return (false, $"Aynı isimde başka bir satış temsilcisi zaten tanımlı: '{name}'");

        existing.RepName = name;
        existing.IsActive = request.IsActive;
        await _repo.UpdateAsync(existing, ct);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> DeleteAsync(int id, CancellationToken ct)
    {
        var existing = await _repo.GetByIdAsync(id, ct);
        if (existing is null) return (false, "Temsilci bulunamadi.");
        await _repo.DeleteAsync(id, ct);
        return (true, null);
    }
}
