using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Services;

public sealed class RoutingService : IRoutingService
{
    private readonly IRoutingRepository _repo;

    public RoutingService(IRoutingRepository repo) => _repo = repo;

    public Task<IReadOnlyCollection<RoutingDto>> ListAsync(int? itemId, CancellationToken ct)
        => _repo.ListAsync(itemId, ct);

    public Task<RoutingDto?> GetAsync(int id, CancellationToken ct) => _repo.GetAsync(id, ct);

    public Task<IReadOnlyCollection<RoutingOperationDto>> GetOperationsAsync(int routingId, CancellationToken ct)
        => _repo.GetOperationsAsync(routingId, ct);

    public Task<int> SaveAsync(SaveRoutingRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Code)) throw new ArgumentException("Rota kodu zorunlu.");
        if (string.IsNullOrWhiteSpace(req.Name)) throw new ArgumentException("Rota adı zorunlu.");

        var header = new Routing
        {
            Id = req.Id,
            Code = req.Code.Trim(),
            Name = req.Name.Trim(),
            ItemId = req.ItemId,
            ConfigId = req.ConfigId,
            Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
            IsActive = req.IsActive,
        };

        IReadOnlyList<RoutingOperation>? ops = req.Operations?.Select(o => new RoutingOperation
        {
            RoutingId = req.Id,  // Save sırasında repo override eder (yeni id ise)
            Sequence = o.Sequence,
            OperationId = o.OperationId,
            MachineId = o.MachineId,
            OverrideDuration = o.OverrideDuration,
            DurationUnit = o.DurationUnit,
            Notes = string.IsNullOrWhiteSpace(o.Notes) ? null : o.Notes.Trim(),
        }).ToArray();

        return _repo.SaveAsync(header, ops, ct);
    }

    public Task DeleteAsync(int id, CancellationToken ct) => _repo.DeleteAsync(id, ct);

    public Task<IReadOnlyCollection<RoutingDto>> ListByOperationAsync(int operationId, CancellationToken ct)
        => _repo.ListByOperationAsync(operationId, ct);

    public async Task<IReadOnlyList<RoutingWithOpsDto>> GetAllWithOperationsAsync(CancellationToken ct)
    {
        var routings = await _repo.ListAsync(null, ct);
        var allOps   = await _repo.GetAllOperationsAsync(ct);
        var byId     = allOps.GroupBy(o => o.RoutingId)
                             .ToDictionary(g => g.Key, g => (IReadOnlyList<RoutingOperationDto>)g.ToList());
        return routings.Select(r => new RoutingWithOpsDto(
            r, byId.TryGetValue(r.Id, out var ops) ? ops : [])).ToList();
    }

    public Task<IReadOnlyCollection<RoutingItemMapDto>> GetItemMapsAsync(int routingId, CancellationToken ct)
        => _repo.GetItemMapsAsync(routingId, ct);

    public Task<int> AddItemMapAsync(int routingId, int itemId, int? configId, CancellationToken ct)
        => _repo.AddItemMapAsync(routingId, itemId, configId, ct);

    public Task DeleteItemMapAsync(int id, CancellationToken ct)
        => _repo.DeleteItemMapAsync(id, ct);
}
