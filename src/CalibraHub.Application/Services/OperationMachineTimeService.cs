using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Services;

public sealed class OperationMachineTimeService : IOperationMachineTimeService
{
    private readonly IOperationMachineTimeRepository _repo;

    public OperationMachineTimeService(IOperationMachineTimeRepository repo) => _repo = repo;

    public Task<IReadOnlyCollection<OperationMachineTimeDto>> ListByOperationAsync(int operationId, int? routingId, CancellationToken ct)
        => _repo.ListByOperationAsync(operationId, routingId, ct);

    public async Task<int> SaveAsync(SaveOperationMachineTimeRequest req, CancellationToken ct)
    {
        if (req.OperationId <= 0) throw new ArgumentException("Operasyon zorunlu.");

        var hasMachine = req.MachineId is > 0;
        var hasMachineGroup = req.MachineGroupId is > 0;
        var hasItem = req.ItemId is > 0;
        var hasItemGroup = req.ItemGroupId is > 0;
        var hasUnit = req.UnitId is > 0;

        // XOR: makine ile makine grubundan yalnızca biri seçilmeli.
        if (hasMachine && hasMachineGroup)
            throw new ArgumentException("Makine ve makine grubu aynı anda seçilemez; yalnızca birini seçin.");
        if (!hasMachine && !hasMachineGroup)
            throw new ArgumentException("Makine veya makine grubundan biri zorunlu.");

        // Ürün ile ürün grubu karşılıklı dışlayıcı (ikisi de opsiyonel, ikisi birden olamaz).
        if (hasItem && hasItemGroup)
            throw new ArgumentException("Ürün ve ürün grubu aynı anda seçilemez; yalnızca birini seçin.");

        // Birim yalnızca somut bir ürün seçildiğinde anlamlı.
        if (hasUnit && !hasItem)
            throw new ArgumentException("Birim yalnızca bir ürün seçildiğinde belirtilebilir.");

        if (req.Quantity <= 0) throw new ArgumentException("Miktar 0'dan büyük olmalı.");
        if (req.DurationPerUnit < 0) throw new ArgumentException("Süre negatif olamaz.");
        if (req.SetupDuration is < 0) throw new ArgumentException("Hazırlık süresi negatif olamaz.");
        if (req.RequiredOperators < 1) throw new ArgumentException("Gerekli operatör sayısı en az 1 olmalı.");

        // CardGroup tip doğrulaması (ID-tabanlı): makine grubu CardType=3, ürün grubu CardType=1 olmalı.
        if (hasMachineGroup)
        {
            var cardType = await _repo.GetCardGroupCardTypeAsync(req.MachineGroupId!.Value, ct);
            if (cardType is null) throw new ArgumentException("Seçilen makine grubu bulunamadı.");
            if (cardType != 3) throw new ArgumentException("Seçilen grup bir makine grubu değil (kart tipi uyumsuz).");
        }
        if (hasItemGroup)
        {
            var cardType = await _repo.GetCardGroupCardTypeAsync(req.ItemGroupId!.Value, ct);
            if (cardType is null) throw new ArgumentException("Seçilen ürün grubu bulunamadı.");
            if (cardType != 1) throw new ArgumentException("Seçilen grup bir ürün grubu değil (kart tipi uyumsuz).");
        }

        // Birim, ürünün tanımlı birim setinde (baz birim veya alternatif birimler) olmalı.
        if (hasUnit && hasItem)
        {
            var inSet = await _repo.IsUnitInItemSetAsync(req.ItemId!.Value, req.UnitId!.Value, ct);
            if (!inSet) throw new ArgumentException("Seçilen birim, ürünün tanımlı birim setinde değil.");
        }

        var entity = new OperationMachineTime
        {
            Id = req.Id,
            OperationId = req.OperationId,
            RoutingId = req.RoutingId,
            MachineId = req.MachineId,
            MachineGroupId = req.MachineGroupId,
            ItemId = req.ItemId,
            ItemGroupId = req.ItemGroupId,
            UnitId = req.UnitId,
            Quantity = req.Quantity,
            DurationPerUnit = req.DurationPerUnit,
            DurationUnit = req.DurationUnit,
            SetupDuration = req.SetupDuration,
            RequiredOperators = req.RequiredOperators,
            IsActive = req.IsActive,
        };
        return await _repo.SaveAsync(entity, ct);
    }

    public Task DeleteAsync(int id, CancellationToken ct) => _repo.DeleteAsync(id, ct);

    public async Task<decimal?> ResolveDurationMinutesAsync(
        int operationId, int? machineId, int? itemId, int? routingId, decimal quantity, CancellationToken ct)
    {
        if (operationId <= 0) return null;

        var match = await _repo.FindBestMachineTimeMatchAsync(operationId, machineId, itemId, routingId, ct);
        if (match is not null)
        {
            var perUnit = match.Quantity != 0m ? match.DurationPerUnit / match.Quantity : match.DurationPerUnit;
            var totalRaw = perUnit * (quantity > 0m ? quantity : 1m);
            return match.DurationUnit == DurationUnit.Hour ? totalRaw * 60m : totalRaw;
        }

        if (routingId is > 0)
        {
            var routingMinutes = await _repo.GetRoutingOperationOverrideMinutesAsync(routingId.Value, operationId, ct);
            if (routingMinutes is not null) return routingMinutes;
        }

        return await _repo.GetOperationStandardMinutesAsync(operationId, ct);
    }

    public async Task<decimal?> ResolveSetupMinutesAsync(
        int operationId, int? machineId, int? itemId, int? routingId, CancellationToken ct)
    {
        if (operationId <= 0) return null;

        var match = await _repo.FindBestMachineTimeMatchAsync(operationId, machineId, itemId, routingId, ct);
        if (match?.SetupDuration is not > 0m) return null;

        return match.DurationUnit == DurationUnit.Hour ? match.SetupDuration.Value * 60m : match.SetupDuration.Value;
    }

    public async Task<byte> ResolveRequiredOperatorsAsync(
        int operationId, int? machineId, int? itemId, int? routingId, CancellationToken ct)
    {
        if (operationId <= 0) return 1;

        var match = await _repo.FindBestMachineTimeMatchAsync(operationId, machineId, itemId, routingId, ct);
        return match is not null && match.RequiredOperators > 0 ? match.RequiredOperators : (byte)1;
    }
}
