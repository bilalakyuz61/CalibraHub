using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Services.Calendar;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Services.Mrp;

/// <summary>Bir malzemenin üretim süresi hesabının sonucu.</summary>
/// <param name="Minutes">Toplam üretim süresi (dakika). Çözülemezse null.</param>
/// <param name="Reason">Çözülememe gerekçesi — sessiz atlama yasak (CLAUDE.md #3).</param>
public sealed record MrpLeadTimeResult(decimal? Minutes, string? Reason);

/// <summary>
/// MRP tarihleme — üretim süresini ROTA OPERASYONLARINDAN hesaplar (2026-08-29).
///
/// <para><b>Neden lead-time kolonu yok:</b> kullanıcı kararı — süre, malzeme kartına elle
/// girilen bir sabit değil, rotanın kendisinden türetilir. Böylece operasyon süresi
/// değiştiğinde planlama kendiliğinden güncellenir.</para>
///
/// <para><b>Süre zinciri</b> <see cref="IOperationMachineTimeService"/> ile AYNIdır (kopyalanmadı):
/// OperationMachineTime (op × makine × ürün × rota; miktarla ölçeklenir) → RoutingOperation
/// .OverrideDuration (flat) → Operation.StandardDuration (flat). Setup süresi ölçeklenmez,
/// operasyon başına bir kez eklenir.</para>
///
/// <para><b>Operasyonlar SERİ kabul edilir</b> (süreler toplanır). Örtüşme/paralellik MRP
/// seviyesinde modellenmez — ince yerleştirmeyi Makine Planlama Released aşamasında yapar.</para>
/// </summary>
public sealed class MrpLeadTimeCalculator
{
    private readonly IWorkOrderRepository _workOrders;
    private readonly IRoutingService _routings;
    private readonly IOperationMachineTimeService _machineTimes;
    private readonly IMachineCalendarRepository _calendar;
    private readonly ILogger? _logger;

    // Aynı koşuda aynı malzeme birden çok kez sorulur (farklı gruplar) — tek hesap yeter.
    private readonly Dictionary<(int ItemId, int? ConfigId), MrpLeadTimeResult> _cache = [];
    private WorkCalendarWalker? _walker;

    public MrpLeadTimeCalculator(
        IWorkOrderRepository workOrders,
        IRoutingService routings,
        IOperationMachineTimeService machineTimes,
        IMachineCalendarRepository calendar,
        ILogger? logger = null)
    {
        _workOrders = workOrders;
        _routings = routings;
        _machineTimes = machineTimes;
        _calendar = calendar;
        _logger = logger;
    }

    /// <summary>Takvimi bir kez yükler (koşu başına). Pencere yoksa yürüyüş yapılmaz.</summary>
    public async Task<WorkCalendarWalker> GetWalkerAsync(CancellationToken ct)
    {
        if (_walker is not null) return _walker;
        try
        {
            var windows = await _calendar.ListWorkWindowsAsync(ct);
            var holidays = await _calendar.ListHolidaysAsync(ct);
            _walker = new WorkCalendarWalker(windows, holidays);
        }
        catch (Exception ex)
        {
            // Takvim okunamazsa tarihleme yapılamaz ama koşu ÇÖKMEZ: boş takvimli walker
            // (HasCalendar=false) döner, çağıran "kaydırma yapılamadı" mesajını yazar.
            _logger?.LogError(ex, "[MRP] Çalışma takvimi okunamadı — geriye tarihleme atlanacak.");
            _walker = new WorkCalendarWalker([], []);
        }
        return _walker;
    }

    /// <summary>
    /// <paramref name="quantity"/> adet için toplam üretim süresi (dakika).
    /// Rota yoksa / operasyon yoksa / hiçbir süre tanımlı değilse gerekçeyle null döner.
    /// </summary>
    public async Task<MrpLeadTimeResult> ResolveMinutesAsync(
        int itemId, int? configId, decimal quantity, CancellationToken ct)
    {
        if (quantity <= 0m) return new MrpLeadTimeResult(null, "Miktar sıfır — süre hesaplanmadı.");

        var key = (itemId, configId);
        // Süre miktarla ölçeklendiği için cache birim başına tutulur ve çarpılır.
        if (_cache.TryGetValue(key, out var cached))
            return cached.Minutes is null
                ? cached
                : new MrpLeadTimeResult(cached.Minutes.Value * quantity, null);

        MrpLeadTimeResult result;
        try
        {
            var routingId = await _workOrders.FindRoutingForItemAsync(itemId, configId, ct);
            if (routingId is not > 0)
            {
                result = new MrpLeadTimeResult(null, "Rota tanımlı değil — başlangıç tarihi bitişe eşitlendi.");
            }
            else
            {
                var ops = await _routings.GetOperationsAsync(routingId.Value, ct);
                if (ops.Count == 0)
                {
                    result = new MrpLeadTimeResult(null, "Rotada operasyon yok — başlangıç tarihi bitişe eşitlendi.");
                }
                else
                {
                    // BİRİM başına süre: her operasyon 1 adet için çözülür, setup bir kez eklenir.
                    // (Setup miktarla ölçeklenmez; birim maliyetine bölünmesi de yanıltıcı olurdu —
                    //  bu yüzden setup ayrı toplanıp cache'e birim-başına yazılmaz, aşağıda görülüyor.)
                    decimal perUnit = 0m, setupTotal = 0m;
                    var anyResolved = false;
                    foreach (var op in ops)
                    {
                        var d = await _machineTimes.ResolveDurationMinutesAsync(
                            op.OperationId, op.MachineId, itemId, routingId, 1m, ct);
                        if (d is > 0m) { perUnit += d.Value; anyResolved = true; }

                        var s = await _machineTimes.ResolveSetupMinutesAsync(
                            op.OperationId, op.MachineId, itemId, routingId, ct);
                        if (s is > 0m) { setupTotal += s.Value; anyResolved = true; }
                    }

                    result = anyResolved
                        ? new MrpLeadTimeResult(perUnit, null)
                        : new MrpLeadTimeResult(null, "Operasyon süresi tanımlı değil — başlangıç tarihi bitişe eşitlendi.");

                    // Setup, miktardan bağımsız sabit — cache'te birim süreyle karışmasın diye
                    // ayrı saklanır ve toplam hesabında bir kez eklenir.
                    if (anyResolved) _setupCache[key] = setupTotal;
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[MRP] Üretim süresi hesaplanamadı. ItemId={ItemId}", itemId);
            result = new MrpLeadTimeResult(null, "Üretim süresi hesaplanamadı — başlangıç tarihi bitişe eşitlendi.");
        }

        _cache[key] = result;
        if (result.Minutes is null) return result;

        var setup = _setupCache.TryGetValue(key, out var st) ? st : 0m;
        return new MrpLeadTimeResult(result.Minutes.Value * quantity + setup, null);
    }

    private readonly Dictionary<(int ItemId, int? ConfigId), decimal> _setupCache = [];
}
