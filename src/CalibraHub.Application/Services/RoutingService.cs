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

    public async Task<int> SaveAsync(SaveRoutingRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) throw new ArgumentException("Rota adı zorunlu.");

        var name = req.Name.Trim();

        // K6 (CLAUDE.md): "Kullanıcı tarafından girilen kod alanı yok" — UI'dan Kod
        // alınmaz, isim üzerinden uniqueness + code auto-derive.
        var all = await _repo.ListAsync(null, ct);
        if (all.Any(r => r.Id != req.Id &&
                         string.Equals(r.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"Aynı isimde başka bir rota zaten tanımlı: '{name}'");
        }

        // Code DB'de var ama UI gostermez — auto-turetilir (mevcut record'sa onun kodunu koru)
        string code;
        if (req.Id > 0)
        {
            var existing = all.FirstOrDefault(r => r.Id == req.Id);
            code = !string.IsNullOrWhiteSpace(existing?.Code) ? existing!.Code : DeriveCode(name);
        }
        else
        {
            code = DeriveCode(name);
        }

        var header = new Routing
        {
            Id = req.Id,
            Code = code,
            Name = name,
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

        return await _repo.SaveAsync(header, ops, ct);
    }

    // Backward-compat: Code DB'de var ama UI'dan kaldirildi (K6).
    // Yeni kayit icin name'den turet (50 char ile sinirla — NVARCHAR(50)).
    private static string DeriveCode(string name)
    {
        var t = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(t)) t = "AUTO_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        return t.Length > 50 ? t[..50] : t;
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
