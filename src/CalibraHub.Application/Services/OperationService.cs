using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Services;

public sealed class OperationService : IOperationService
{
    private readonly IOperationRepository _repo;

    public OperationService(IOperationRepository repo) => _repo = repo;

    public async Task<IReadOnlyCollection<OperationDto>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var list = await _repo.ListAsync(includeInactive, ct);
        return list.Select(Map).ToArray();
    }

    public async Task<OperationDto?> GetAsync(int id, CancellationToken ct)
    {
        var e = await _repo.GetAsync(id, ct);
        return e is null ? null : Map(e);
    }

    public Task<int> SaveAsync(SaveOperationRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
            throw new ArgumentException("Operasyon kodu zorunlu.", nameof(request.Code));
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Operasyon adı zorunlu.", nameof(request.Name));
        if (request.StandardDuration.HasValue && request.StandardDuration < 0)
            throw new ArgumentException("Standart süre negatif olamaz.", nameof(request.StandardDuration));
        if (request.HourlyRate.HasValue && request.HourlyRate < 0)
            throw new ArgumentException("Saatlik ücret negatif olamaz.", nameof(request.HourlyRate));

        var entity = new Operation
        {
            Id = request.Id,
            Code = request.Code.Trim(),
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            StandardDuration = request.StandardDuration,
            DurationUnit = request.DurationUnit,
            HourlyRate = request.HourlyRate,
            SortOrder = request.SortOrder,
            IsActive = request.IsActive,
        };
        return _repo.UpsertAsync(entity, ct);
    }

    public Task DeleteAsync(int id, CancellationToken ct) => _repo.DeleteAsync(id, ct);

    private static OperationDto Map(Operation e) =>
        new(e.Id, e.Code, e.Name, e.Description, e.StandardDuration, e.DurationUnit,
            e.HourlyRate, e.SortOrder, e.IsActive, e.Created, e.Updated);
}
