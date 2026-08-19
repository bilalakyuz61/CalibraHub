using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Services.Import;

/// <summary>
/// İş emri içe-aktarım handler'ı (yalnız ÜST BİLGİ). Yazma
/// <see cref="IWorkOrderService"/> Create/UpdateAsync'e delege edilir; bileşenler
/// reçeteden patlatılır (ExplodeBom), içe aktarımla satır satır taşınmaz.
///
/// KISIT — emir numarası: <c>CreateWorkOrderRequest</c> emir numarası kabul etmez,
/// numarayı CalibraHub üretir. Bu nedenle eşleşme anahtarı olarak kullanılan
/// <c>OrderNumber</c> yalnız kaynak veri CalibraHub'ın KENDİ emir numarasını
/// taşıyorsa eşleşir. Dış sistemin kendi numarasıyla tekrar çalıştırılırsa her
/// turda yeni emir açılır. Dış numarayı kalıcı bağlamak için o alan makine/ürün
/// kartındaki gibi bir özel (widget) alana yazılmalıdır.
/// </summary>
public sealed class WorkOrderImportHandler : RowImportHandlerBase
{
    private readonly IWorkOrderService _workOrders;
    private readonly ILogisticsConfigurationRepository _logisticsRepo;
    private List<WorkOrderListItemDto>? _orders;
    private List<Item>? _items;

    public WorkOrderImportHandler(
        IWorkOrderService workOrders,
        ILogisticsConfigurationRepository logisticsRepo)
    {
        _workOrders = workOrders;
        _logisticsRepo = logisticsRepo;
    }

    public override string Entity => "WORK_ORDER";
    public override string Label => "İş Emri";

    public override IReadOnlyList<ImportTargetFieldDto> GetFields() => new List<ImportTargetFieldDto>
    {
        new("OrderNumber", "İş Emri No", "string", false, true,
            "Eşleştirme anahtarı — yalnız CalibraHub emir numarasıyla eşleşir", MaxLength: 50),
        new("ItemCode", "Ürün Kodu", "string", true, false, "Üretilecek stok kartının kodu (zorunlu)"),
        new("PlannedQuantity", "Planlanan Miktar", "decimal", true, false, "Zorunlu, sıfırdan büyük"),
        new("PlannedStartDate", "Planlanan Başlangıç", "date", false, false),
        new("PlannedEndDate", "Planlanan Bitiş", "date", false, false),
        new("Priority", "Öncelik", "string", false, false, "Düşük / Normal / Yüksek",
            new[] { "Düşük", "Normal", "Yüksek" }),
        new("Notes", "Notlar", "string", false, false),
    };

    protected override IReadOnlyList<string> ValidateRow(IReadOnlyDictionary<string, string?> d)
    {
        var errs = new List<string>();

        if (string.IsNullOrWhiteSpace(Get(d, "ItemCode"))) errs.Add("Ürün kodu boş.");

        var qtyRaw = Get(d, "PlannedQuantity");
        var qty = ParseDecimal(qtyRaw);
        if (string.IsNullOrWhiteSpace(qtyRaw)) errs.Add("Planlanan miktar boş.");
        else if (qty is null) errs.Add($"Planlanan miktar sayı değil: '{qtyRaw}'.");
        else if (qty <= 0) errs.Add("Planlanan miktar sıfırdan büyük olmalı.");

        foreach (var (key, label) in new[] { ("PlannedStartDate", "Planlanan başlangıç"), ("PlannedEndDate", "Planlanan bitiş") })
        {
            var raw = Get(d, key);
            if (!string.IsNullOrWhiteSpace(raw) && ImportParse.ParseDate(raw) is null)
                errs.Add($"{label} tarihi okunamadı: '{raw}'.");
        }

        var prio = Get(d, "Priority");
        if (!string.IsNullOrWhiteSpace(prio) && ResolvePriority(prio) is null)
            errs.Add($"Öncelik tanınmadı: '{prio}' (Düşük / Normal / Yüksek).");

        return errs;
    }

    protected override async Task<(string Action, int? ExistingId)> ResolveActionAsync(
        IReadOnlyDictionary<string, string?> d, IReadOnlyList<string> matchKeys, CancellationToken ct)
    {
        if (matchKeys.Count == 0) return ("insert", null);

        var orders = await EnsureOrdersAsync(ct);
        var hit = orders.FirstOrDefault(o => KeysMatch(d, matchKeys, k => WorkOrderValue(o, k)));
        return hit is not null ? ("update", hit.Id) : ("insert", null);
    }

    /// <summary>Mevcut iş emrinin hedef alan karşılığı (bileşik anahtar karşılaştırması için).</summary>
    private static string? WorkOrderValue(WorkOrderListItemDto o, string key) => key.ToLowerInvariant() switch
    {
        "ordernumber" => o.OrderNumber,
        "itemcode"    => o.ItemCode,
        _             => null,
    };

    protected override async Task<(bool Ok, string? Error, int? RecordId)> CommitRowAsync(
        IReadOnlyDictionary<string, string?> d, string action, int? existingId,
        int? userId, HashSet<string> usedCodes, CancellationToken ct)
    {
        var itemCode = Get(d, "ItemCode");
        var item = await ResolveItemAsync(itemCode, ct);
        if (item is null) return (false, $"Ürün bulunamadı: '{itemCode}'.", null);

        // Eşlenmemiş = null (0 DEĞİL): güncellemede mevcut miktar korunur, eklemede 0'a düşer.
        var qty = ParseDecimal(Get(d, "PlannedQuantity"));
        var start = ImportParse.ParseDate(Get(d, "PlannedStartDate"));
        var end = ImportParse.ParseDate(Get(d, "PlannedEndDate"));
        var notes = Get(d, "Notes")?.Trim();

        if (action == "update" && existingId is > 0)
        {
            // DETAY DTO'su okunur (liste DTO'su DEĞİL): WorkOrderListItemDto lokasyon/rota/
            // makine/not/AR-GE proje alanlarını taşımaz. Bu alanlar burada sabit null
            // geçildiği ve repo UPDATE'i tüm kolonları koşulsuz yazdığı için HER aktarımda
            // siliniyordu — eşleştirilebilir alan olmadıkları hâlde. (2026-08-18)
            var ex = await _workOrders.GetAsync(existingId.Value, ct);

            var req = new UpdateWorkOrderRequest(
                PlannedQuantity: qty ?? ex?.PlannedQuantity ?? 0m,
                UnitId: ex?.UnitId,
                PlannedStartDate: start ?? ex?.PlannedStartDate,
                PlannedEndDate: end ?? ex?.PlannedEndDate,
                Priority: ResolvePriority(Get(d, "Priority")) ?? ex?.Priority ?? WorkOrderPriority.Medium,
                AssignedUserId: ex?.AssignedUserId,
                WarehouseLocationId: ex?.WarehouseLocationId,
                RoutingId: ex?.RoutingId,
                DefaultMachineId: ex?.DefaultMachineId,
                AssignedPersonnelId: ex?.AssignedPersonnelId,
                Notes: notes ?? ex?.Notes,
                ArgeProjectId: ex?.ArgeProjectId);

            await _workOrders.UpdateAsync(existingId.Value, req, ct);
            _orders = null;
            return (true, null, existingId);
        }

        var createReq = new CreateWorkOrderRequest(
            ItemId: item.Id,
            ConfigId: null,
            PlannedQuantity: qty ?? 0m,
            UnitId: null,
            PlannedStartDate: start,
            PlannedEndDate: end,
            Priority: ResolvePriority(Get(d, "Priority")) ?? WorkOrderPriority.Medium,
            AssignedUserId: null,
            WarehouseLocationId: null,
            RoutingId: null,
            DefaultMachineId: null,
            AssignedPersonnelId: null,
            Notes: notes);

        var id = await _workOrders.CreateAsync(createReq, ct);
        _orders = null;
        return (true, null, id);
    }

    private async Task<List<WorkOrderListItemDto>> EnsureOrdersAsync(CancellationToken ct)
        => _orders ??= (await _workOrders.ListAsync(null, ct)).ToList();

    private async Task<Item?> ResolveItemAsync(string? code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        _items ??= (await _logisticsRepo.GetItemsAsync(ct)).ToList();
        return _items.FirstOrDefault(i => Same(i.Code, code));
    }

    private static WorkOrderPriority? ResolvePriority(string? raw)
    {
        var v = (raw ?? "").Trim().ToLowerInvariant();
        if (v.Length == 0) return null;
        if (v is "düşük" or "dusuk" or "low" or "0") return WorkOrderPriority.Low;
        if (v is "normal" or "orta" or "medium" or "1") return WorkOrderPriority.Medium;
        if (v is "yüksek" or "yuksek" or "high" or "2") return WorkOrderPriority.High;
        return null;
    }

    private static bool Same(string? a, string? b)
        => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
}
