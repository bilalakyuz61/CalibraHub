using System.Text.Json;

namespace CalibraHub.Web.Services.FunctionalTests.Scenarios;

/// <summary>
/// Üretim grubu hazırlığı — mamul + iki hammadde kartı açar ve hammaddeleri Depo 1'e sokar.
///
/// Neden AYRI malzeme: ticari zincirin item1/item2 bakiyesi satış teslimatlarıyla tüketiliyor.
/// Üretim sarfı aynı bakiyeye girseydi iki grup birbirinin eksi-bakiye korumasına takılır,
/// çalıştırma sırasına göre rastgele kırılırdı. Birim / depo / personel / cari SEED_MASTER_DATA'dan
/// AYNEN devralınır (bkz. FunctionalTestContext.Keys sözleşmesi) — yeniden kurulmaz.
/// </summary>
public sealed class ProductionSeedScenario : FunctionalTestScenarioBase
{
    /// <summary>Hammadde başına açılış stoğu — sarf (20 + 10) ve olası tekrar çalıştırmalar için geniş.</summary>
    public const decimal ComponentStock = 400m;

    public override string Key => "PROD_SEED";
    public override string Group => "uretim";
    public override string Label => "Üretim Test Verisi Kurulumu";
    public override IReadOnlyList<string> DependsOn => new[] { "SEED_MASTER_DATA" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var unitId = ctx.GetInt(FunctionalTestContext.Keys.UnitId);
        var loc1 = ctx.GetInt(FunctionalTestContext.Keys.Location1Id);
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        var (mamulOk, mamulId) = await SaveCardAsync(ctx, steps, "Mamul kartı oluşturma",
            $"FNM-{suffix}", $"Fonksiyon Test Mamul ({suffix})", typeId: 1, unitId, ct);
        if (!mamulOk) return;
        ctx.Set(FunctionalTestContext.Keys.ProdItemId, mamulId);
        ctx.Set(FunctionalTestContext.Keys.ProdItemCode, $"FNM-{suffix}");

        var (c1Ok, comp1Id) = await SaveCardAsync(ctx, steps, "Hammadde 1 kartı oluşturma",
            $"FNH1-{suffix}", $"Fonksiyon Test Hammadde 1 ({suffix})", typeId: 3, unitId, ct);
        if (!c1Ok) return;
        ctx.Set(FunctionalTestContext.Keys.ProdComp1Id, comp1Id);
        ctx.Set(FunctionalTestContext.Keys.ProdComp1Code, $"FNH1-{suffix}");

        var (c2Ok, comp2Id) = await SaveCardAsync(ctx, steps, "Hammadde 2 kartı oluşturma",
            $"FNH2-{suffix}", $"Fonksiyon Test Hammadde 2 ({suffix})", typeId: 3, unitId, ct);
        if (!c2Ok) return;
        ctx.Set(FunctionalTestContext.Keys.ProdComp2Id, comp2Id);
        ctx.Set(FunctionalTestContext.Keys.ProdComp2Code, $"FNH2-{suffix}");

        var (stockOk, _) = await StepPostAsync(ctx, steps, "Hammadde stok girişi", "/Warehouse/SaveDocJson",
            new
            {
                id = (int?)null, docType = "STOCK_IN", docNo = (string?)null, docDate = DateTime.Today,
                fromLocationId = (int?)null, toLocationId = loc1, refNo = (string?)null,
                notes = "Fonksiyon testi — üretim hammadde girişi",
                lines = new object[]
                {
                    new { id = (int?)null, itemId = comp1Id, unitId, qty = ComponentStock, combinationId = (int?)null, notes = (string?)null, fromLocationId = (int?)null, toLocationId = loc1, unitCost = (decimal?)null },
                    new { id = (int?)null, itemId = comp2Id, unitId, qty = ComponentStock, combinationId = (int?)null, notes = (string?)null, fromLocationId = (int?)null, toLocationId = loc1, unitCost = (decimal?)null },
                },
                argeProjectId = (int?)null,
            }, ct);
        if (!stockOk) return;

        var balances = await FunctionalTestHelpers.GetStockBalancesAsync(ctx, new[] { comp1Id, comp2Id }, ct);
        var b1 = FunctionalTestHelpers.BalanceFor(balances, comp1Id, loc1);
        var b2 = FunctionalTestHelpers.BalanceFor(balances, comp2Id, loc1);
        if (b1 < ComponentStock || b2 < ComponentStock)
        {
            Fail(steps, "Hammadde bakiye doğrulama", $"Depo 1 bakiyesi düşük — hammadde1={b1}, hammadde2={b2}, beklenen>={ComponentStock}.");
            return;
        }
        Pass(steps, "Hammadde bakiye doğrulama", $"Depo 1: hammadde1={b1}, hammadde2={b2}.");
    }

    private static async Task<(bool Ok, int Id)> SaveCardAsync(
        FunctionalTestContext ctx, List<FunctionalTestStep> steps, string label,
        string code, string name, int typeId, int unitId, CancellationToken ct)
    {
        var (ok, json) = await StepPostAsync(ctx, steps, label, "/Logistics/SaveMaterialCardJson",
            new
            {
                itemId = (int?)null, code, name, typeId, unitId, combinations = false,
                taxRate = 20m, trackingType = "None", minStock = 0m, autoSerial = false,
            }, ct);
        if (!ok) return (false, 0);
        var id = json.GetIntCI("id");
        if (id <= 0) { Fail(steps, label, "Sunucu Id döndürmedi."); return (false, 0); }
        return (true, id);
    }
}

/// <summary>
/// Üretim Reçetesi (BOM) Tanımlama — mamul için 2 satırlı reçete kaydeder ve okuma
/// tarafını iki ayrı uçtan doğrular: kart okuması (GetBOM) + çok seviyeli patlatma
/// (ExplodeBOM, 10 adet için 20 + 10 bileşen ihtiyacı).
/// </summary>
public sealed class ProductionBomDefineScenario : FunctionalTestScenarioBase
{
    public const decimal Comp1PerUnit = 2m;
    public const decimal Comp2PerUnit = 1m;

    public override string Key => "PROD_BOM_DEFINE";
    public override string Group => "uretim";
    public override string Label => "Üretim Reçetesi Tanımlama";
    public override IReadOnlyList<string> DependsOn => new[] { "PROD_SEED" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var itemId = ctx.GetInt(FunctionalTestContext.Keys.ProdItemId);
        var itemCode = ctx.GetString(FunctionalTestContext.Keys.ProdItemCode);
        var comp1Id = ctx.GetInt(FunctionalTestContext.Keys.ProdComp1Id);
        var comp2Id = ctx.GetInt(FunctionalTestContext.Keys.ProdComp2Id);

        var (saveOk, saveJson) = await StepPostAsync(ctx, steps, "Reçete kaydetme", "/Logistics/SaveBOM",
            new
            {
                id = (int?)null, itemId, configId = (int?)null,
                parentMaterialCode = (string?)null, configurationCode = (string?)null,
                description = "Fonksiyon testi — üretim reçetesi",
                imageBase64 = (string?)null, imageMimeType = (string?)null, imageFitMode = (string?)null,
                imageRotation = 0,
                lines = new object[]
                {
                    new { itemId = comp1Id, configId = (int?)null, componentMaterialCode = (string?)null, componentConfigCode = (string?)null, quantity = Comp1PerUnit, scrapRatio = 0m, note = (string?)null, componentBomId = (int?)null },
                    new { itemId = comp2Id, configId = (int?)null, componentMaterialCode = (string?)null, componentConfigCode = (string?)null, quantity = Comp2PerUnit, scrapRatio = 0m, note = (string?)null, componentBomId = (int?)null },
                },
                routingId = (int?)null, routingCode = (string?)null, versionCode = (string?)null,
            }, ct);
        if (!saveOk) return;
        var bomId = saveJson.GetIntCI("id");
        if (bomId > 0) ctx.Set(FunctionalTestContext.Keys.ProdBomId, bomId);

        // Okuma doğrulaması — reçete gerçekten 2 satırla kaydedildi mi.
        var (readOk, readJson) = await StepGetAsync(ctx, steps, "Reçeteyi geri okuma",
            $"/Logistics/GetBOM?materialCode={Uri.EscapeDataString(itemCode ?? "")}", ct);
        if (!readOk) return;
        if (readJson.GetBoolCI("found") == false)
        {
            Fail(steps, "Reçeteyi geri okuma", $"'{itemCode}' için reçete bulunamadı (found=false).");
            return;
        }
        var lineCount = readJson.GetArrayCI("lines").Count;
        if (lineCount != 2)
        {
            Fail(steps, "Reçeteyi geri okuma", $"Reçete satır sayısı {lineCount} (2 bekleniyordu).");
            return;
        }
        Pass(steps, "Satır sayısı doğrulama", "2 bileşen.");

        // Patlatma doğrulaması — 10 adet mamul için beklenen bileşen miktarları.
        const decimal explodeQty = 10m;
        var (expOk, expJson) = await StepGetAsync(ctx, steps, "Reçete patlatma (10 adet)",
            $"/Logistics/ExplodeBOM?itemId={itemId}&quantity={explodeQty}", ct);
        if (!expOk) return;
        var comps = expJson.GetArrayCI("lines");
        var need1 = QtyOf(comps, comp1Id);
        var need2 = QtyOf(comps, comp2Id);
        if (Math.Abs(need1 - explodeQty * Comp1PerUnit) > 0.0001m || Math.Abs(need2 - explodeQty * Comp2PerUnit) > 0.0001m)
        {
            Fail(steps, "Patlatma miktar doğrulama",
                $"Beklenen {explodeQty * Comp1PerUnit}/{explodeQty * Comp2PerUnit}, gerçek {need1}/{need2}.");
            return;
        }
        Pass(steps, "Patlatma miktar doğrulama", $"10 adet → hammadde1={need1}, hammadde2={need2}.");
    }

    private static decimal QtyOf(IReadOnlyList<JsonElement> comps, int itemId)
        => comps.Where(c => c.GetIntCI("itemId") == itemId)
                .Select(c => c.GetDecimalCI("totalQuantity"))
                .DefaultIfEmpty(0m)
                .Sum();
}

/// <summary>
/// Operasyon ve Rota Tanımlama — iki operasyon (Kesim / Montaj) açar, mamule bağlı bir rota
/// kurar. İki adımlı rota bilinçli: iş emri yayımlanınca operasyonlar sıraya patlar ve
/// "üretim akış kaydı" senaryosu upstream-cap (önceki operasyon üretmeden sonraki başlayamaz)
/// kuralını gerçekten sınayabilir.
/// </summary>
public sealed class ProductionRoutingDefineScenario : FunctionalTestScenarioBase
{
    public override string Key => "PROD_ROUTING_DEFINE";
    public override string Group => "uretim";
    public override string Label => "Operasyon ve Rota Tanımlama";
    public override IReadOnlyList<string> DependsOn => new[] { "PROD_SEED" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var itemId = ctx.GetInt(FunctionalTestContext.Keys.ProdItemId);
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        var (op1Ok, op1Id) = await SaveOperationAsync(ctx, steps, "Operasyon 1 (Kesim) oluşturma",
            $"Fonksiyon Test Kesim {suffix}", 15m, ct);
        if (!op1Ok) return;

        var (op2Ok, op2Id) = await SaveOperationAsync(ctx, steps, "Operasyon 2 (Montaj) oluşturma",
            $"Fonksiyon Test Montaj {suffix}", 25m, ct);
        if (!op2Ok) return;

        var (rtOk, rtJson) = await StepPostAsync(ctx, steps, "Rota kaydetme", "/Production/SaveRouting",
            new
            {
                id = 0, code = $"FNR-{suffix}", name = $"Fonksiyon Test Rota {suffix}",
                itemId, configId = (int?)null, description = "Fonksiyon testi — 2 adımlı rota", isActive = true,
                operations = new object[]
                {
                    new { sequence = 10, operationId = op1Id, machineId = (int?)null, overrideDuration = (decimal?)null, durationUnit = "Minute", notes = (string?)null },
                    new { sequence = 20, operationId = op2Id, machineId = (int?)null, overrideDuration = (decimal?)null, durationUnit = "Minute", notes = (string?)null },
                },
            }, ct);
        if (!rtOk) return;
        var routingId = rtJson.GetIntCI("id");
        if (routingId <= 0) { Fail(steps, "Rota kaydetme", "Sunucu rota Id'si döndürmedi."); return; }
        ctx.Set(FunctionalTestContext.Keys.ProdRoutingId, routingId);
        Pass(steps, "Rota doğrulama", $"Rota #{routingId}, 2 operasyon.");
    }

    private static async Task<(bool Ok, int Id)> SaveOperationAsync(
        FunctionalTestContext ctx, List<FunctionalTestStep> steps, string label,
        string name, decimal standardDuration, CancellationToken ct)
    {
        // Kod alanı bilinçli olarak boş: "Kullanıcı kod girmez" kuralı — servis Code'u
        // isimden türetir (bkz. CLAUDE.md "Kullanıcı tarafından girilen kod alanı yok").
        var (ok, json) = await StepPostAsync(ctx, steps, label, "/Production/SaveOperation",
            new
            {
                id = 0, code = "", name, description = (string?)null,
                standardDuration, durationUnit = "Minute", hourlyRate = (decimal?)null,
                sortOrder = 0, isActive = true,
            }, ct);
        if (!ok) return (false, 0);
        var id = json.GetIntCI("id");
        if (id <= 0) { Fail(steps, label, "Sunucu operasyon Id'si döndürmedi."); return (false, 0); }
        return (true, id);
    }
}

/// <summary>
/// İş Emri Oluşturma — 10 adetlik iş emri açar, reçeteyi bileşenlere patlatır ve
/// planlanan bileşen ihtiyaçlarının reçete oranıyla uyuştuğunu doğrular.
/// </summary>
public sealed class ProductionWorkOrderCreateScenario : FunctionalTestScenarioBase
{
    public const decimal PlannedQty = 10m;

    public override string Key => "PROD_WORK_ORDER_CREATE";
    public override string Group => "uretim";
    public override string Label => "İş Emri Oluşturma";
    public override IReadOnlyList<string> DependsOn => new[] { "PROD_BOM_DEFINE", "PROD_ROUTING_DEFINE" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var itemId = ctx.GetInt(FunctionalTestContext.Keys.ProdItemId);
        var unitId = ctx.GetInt(FunctionalTestContext.Keys.UnitId);
        var loc1 = ctx.GetInt(FunctionalTestContext.Keys.Location1Id);
        var routingId = ctx.GetInt(FunctionalTestContext.Keys.ProdRoutingId);
        var comp1Id = ctx.GetInt(FunctionalTestContext.Keys.ProdComp1Id);
        var comp2Id = ctx.GetInt(FunctionalTestContext.Keys.ProdComp2Id);

        // autoRelease=false: yayımlama ayrı senaryoda (PROD_FLOW_RECORD) sınanır — şirket
        // parametresi ne olursa olsun bu senaryo Planned durumda bitmeli.
        var (createOk, createJson) = await StepPostAsync(ctx, steps, "İş emri oluşturma", "/Production/Create",
            new
            {
                itemId, configId = (int?)null, plannedQuantity = PlannedQty, unitId,
                plannedStartDate = DateTime.Today, plannedEndDate = DateTime.Today.AddDays(3),
                priority = "Medium", assignedUserId = (int?)null, warehouseLocationId = loc1,
                routingId, defaultMachineId = (int?)null, assignedPersonnelId = (int?)null,
                notes = "Fonksiyon testi — iş emri", autoRelease = false,
                argeProjectId = (int?)null, orderDate = DateTime.Today,
            }, ct);
        if (!createOk) return;
        var workOrderId = createJson.GetIntCI("id");
        if (workOrderId <= 0) { Fail(steps, "İş emri oluşturma", "Sunucu iş emri Id'si döndürmedi."); return; }
        ctx.Set(FunctionalTestContext.Keys.ProdWorkOrderId, workOrderId);

        // ExplodeBom query-string parametresi alır (gövde değil) — controller imzası: ExplodeBom(int workOrderId).
        var (expOk, _) = await StepPostAsync(ctx, steps, "Reçeteyi bileşenlere patlatma",
            $"/Production/ExplodeBom?workOrderId={workOrderId}", null, ct);
        if (!expOk) return;

        var (compOk, compJson) = await StepGetAsync(ctx, steps, "Bileşen listesini okuma",
            $"/Production/WorkOrderComponents?workOrderId={workOrderId}", ct);
        if (!compOk) return;
        var rows = compJson.AsArray();
        var req1 = rows.Where(r => r.GetIntCI("itemId") == comp1Id).Select(r => r.GetDecimalCI("requiredQuantity")).DefaultIfEmpty(0m).Sum();
        var req2 = rows.Where(r => r.GetIntCI("itemId") == comp2Id).Select(r => r.GetDecimalCI("requiredQuantity")).DefaultIfEmpty(0m).Sum();
        var expected1 = PlannedQty * ProductionBomDefineScenario.Comp1PerUnit;
        var expected2 = PlannedQty * ProductionBomDefineScenario.Comp2PerUnit;
        if (Math.Abs(req1 - expected1) > 0.0001m || Math.Abs(req2 - expected2) > 0.0001m)
        {
            Fail(steps, "Bileşen ihtiyaç doğrulama",
                $"Beklenen {expected1}/{expected2}, gerçek {req1}/{req2}.");
            return;
        }
        Pass(steps, "Bileşen ihtiyaç doğrulama", $"İş emri #{workOrderId}: hammadde1={req1}, hammadde2={req2}.");
    }
}

/// <summary>
/// Üretim Akış Kaydı — iş emrini yayımlar (rota → operasyon patlatma), ilk operasyonu
/// başlatır, kısmi üretim girer ve tamamlar. Doğrulanan: operasyonlar sıraya patladı mı,
/// üretilen miktar işlendi mi, ilk operasyon Tamamlandı'ya geçti mi.
/// </summary>
public sealed class ProductionFlowRecordScenario : FunctionalTestScenarioBase
{
    public override string Key => "PROD_FLOW_RECORD";
    public override string Group => "uretim";
    public override string Label => "Üretim Akış Kaydı (Operasyon Başlat/Tamamla)";
    public override IReadOnlyList<string> DependsOn => new[] { "PROD_WORK_ORDER_CREATE" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var workOrderId = ctx.GetInt(FunctionalTestContext.Keys.ProdWorkOrderId);
        var personnelId = ctx.GetInt(FunctionalTestContext.Keys.PersonnelId);
        var qty = ProductionWorkOrderCreateScenario.PlannedQty;

        var (relOk, _) = await StepPostAsync(ctx, steps, "İş emrini yayımlama (Released)",
            $"/Production/ChangeStatus?id={workOrderId}",
            new { workOrderId, newStatus = "Released" }, ct);
        if (!relOk) return;

        var (opsOk, opsJson) = await StepGetAsync(ctx, steps, "Operasyonları okuma",
            $"/Production/WorkOrderOperations?workOrderId={workOrderId}", ct);
        if (!opsOk) return;
        var ops = opsJson.AsArray().OrderBy(o => o.GetIntCI("sequence")).ToList();
        if (ops.Count != 2)
        {
            Fail(steps, "Operasyonları okuma", $"Yayımlama sonrası {ops.Count} operasyon patladı (2 bekleniyordu).");
            return;
        }
        var op1Id = ops[0].GetIntCI("id");
        var op2Id = ops[1].GetIntCI("id");
        if (op1Id <= 0 || op2Id <= 0) { Fail(steps, "Operasyonları okuma", "Operasyon Id'leri okunamadı."); return; }
        ctx.Set(FunctionalTestContext.Keys.ProdOp1Id, op1Id);
        ctx.Set(FunctionalTestContext.Keys.ProdOp2Id, op2Id);
        Pass(steps, "Operasyon patlatma doğrulama", $"2 operasyon (#{op1Id}, #{op2Id}).");

        var (startOk, _) = await StepPostAsync(ctx, steps, "1. operasyonu başlatma", "/Production/ShopFloor/Start",
            new { workOrderOperationId = op1Id, operatorPersonnelId = personnelId }, ct);
        if (!startOk) return;

        var (partOk, _) = await StepPostAsync(ctx, steps, "1. operasyonda kısmi üretim girişi", "/Production/ShopFloor/PartialComplete",
            new { workOrderOperationId = op1Id, operatorPersonnelId = personnelId, quantity = qty, scrapQuantity = (decimal?)null }, ct);
        if (!partOk) return;

        var (compOk, _) = await StepPostAsync(ctx, steps, "1. operasyonu tamamlama", "/Production/ShopFloor/Complete",
            new { workOrderOperationId = op1Id, operatorPersonnelId = personnelId, finalQuantity = (decimal?)null }, ct);
        if (!compOk) return;

        var (verifyOk, verifyJson) = await StepGetAsync(ctx, steps, "1. operasyon durum doğrulama",
            $"/Production/WorkOrderOperation?id={op1Id}", ct);
        if (!verifyOk) return;
        var produced = verifyJson.GetDecimalCI("producedQuantity");
        var status = verifyJson.GetStringCI("status");
        if (Math.Abs(produced - qty) > 0.0001m || !string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase))
        {
            Fail(steps, "1. operasyon durum doğrulama",
                $"Üretilen={produced} (beklenen {qty}), durum='{status}' (beklenen 'Completed').");
            return;
        }
        Pass(steps, "1. operasyon durum doğrulama", $"Üretilen={produced}, durum={status}.");
    }
}

/// <summary>
/// Üretim Sarfı — iş emrinin bileşenlerini depodan düşer ve hem stok bakiyesinin hem de
/// bileşenin "sarf edilen" (IssuedQuantity) alanının doğru arttığını denetler.
/// </summary>
public sealed class ProductionConsumptionScenario : FunctionalTestScenarioBase
{
    public override string Key => "PROD_CONSUMPTION";
    public override string Group => "uretim";
    public override string Label => "Üretim Sarfı (Bileşen Tüketimi)";
    public override IReadOnlyList<string> DependsOn => new[] { "PROD_FLOW_RECORD" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var workOrderId = ctx.GetInt(FunctionalTestContext.Keys.ProdWorkOrderId);
        var unitId = ctx.GetInt(FunctionalTestContext.Keys.UnitId);
        var loc1 = ctx.GetInt(FunctionalTestContext.Keys.Location1Id);
        var comp1Id = ctx.GetInt(FunctionalTestContext.Keys.ProdComp1Id);
        var comp2Id = ctx.GetInt(FunctionalTestContext.Keys.ProdComp2Id);
        var qty = ProductionWorkOrderCreateScenario.PlannedQty;
        var need1 = qty * ProductionBomDefineScenario.Comp1PerUnit;
        var need2 = qty * ProductionBomDefineScenario.Comp2PerUnit;

        var (compOk, compJson) = await StepGetAsync(ctx, steps, "Bileşen kayıtlarını okuma",
            $"/Production/WorkOrderComponents?workOrderId={workOrderId}", ct);
        if (!compOk) return;
        var rows = compJson.AsArray();
        var c1 = rows.FirstOrDefault(r => r.GetIntCI("itemId") == comp1Id);
        var c2 = rows.FirstOrDefault(r => r.GetIntCI("itemId") == comp2Id);
        var c1Id = c1.ValueKind == JsonValueKind.Object ? c1.GetIntCI("id") : 0;
        var c2Id = c2.ValueKind == JsonValueKind.Object ? c2.GetIntCI("id") : 0;
        if (c1Id <= 0 || c2Id <= 0)
        {
            Fail(steps, "Bileşen kayıtlarını okuma", $"Bileşen kayıtları bulunamadı (id1={c1Id}, id2={c2Id}).");
            return;
        }

        var before = await FunctionalTestHelpers.GetStockBalancesAsync(ctx, new[] { comp1Id, comp2Id }, ct);
        var before1 = FunctionalTestHelpers.BalanceFor(before, comp1Id, loc1);
        var before2 = FunctionalTestHelpers.BalanceFor(before, comp2Id, loc1);

        var (issueOk, _) = await StepPostAsync(ctx, steps, "Üretim sarfı kaydetme", "/Production/WorkOrder/IssueConsumptionJson",
            new
            {
                workOrderId, producedQuantity = qty,
                lines = new object[]
                {
                    new { workOrderComponentId = c1Id, itemId = comp1Id, materialCode = (string?)null, unitId, qty = need1, combinationId = (int?)null, fromLocationId = loc1, lotNo = (string?)null, serials = (string[]?)null, notes = (string?)null },
                    new { workOrderComponentId = c2Id, itemId = comp2Id, materialCode = (string?)null, unitId, qty = need2, combinationId = (int?)null, fromLocationId = loc1, lotNo = (string?)null, serials = (string[]?)null, notes = (string?)null },
                },
            }, ct);
        if (!issueOk) return;

        var after = await FunctionalTestHelpers.GetStockBalancesAsync(ctx, new[] { comp1Id, comp2Id }, ct);
        var after1 = FunctionalTestHelpers.BalanceFor(after, comp1Id, loc1);
        var after2 = FunctionalTestHelpers.BalanceFor(after, comp2Id, loc1);
        if (Math.Abs((before1 - need1) - after1) > 0.0001m || Math.Abs((before2 - need2) - after2) > 0.0001m)
        {
            Fail(steps, "Sarf bakiye doğrulama",
                $"Beklenen {before1 - need1}/{before2 - need2}, gerçek {after1}/{after2} (önce {before1}/{before2}).");
            return;
        }
        Pass(steps, "Sarf bakiye doğrulama", $"hammadde1 {before1}→{after1}, hammadde2 {before2}→{after2}.");

        var (recheckOk, recheckJson) = await StepGetAsync(ctx, steps, "Sarf edilen miktar doğrulama",
            $"/Production/WorkOrderComponents?workOrderId={workOrderId}", ct);
        if (!recheckOk) return;
        var issued1 = recheckJson.AsArray().Where(r => r.GetIntCI("itemId") == comp1Id)
            .Select(r => r.GetDecimalCI("issuedQuantity")).DefaultIfEmpty(0m).Sum();
        if (Math.Abs(issued1 - need1) > 0.0001m)
        {
            Fail(steps, "Sarf edilen miktar doğrulama", $"Bileşen 'sarf edilen' {issued1}, beklenen {need1}.");
            return;
        }
        Pass(steps, "Sarf edilen miktar doğrulama", $"hammadde1 sarf edilen={issued1}.");
    }
}

/// <summary>
/// Üretim Sonu Kaydı — son operasyon tamamlanır. Bu adımda sunucu mamul girişini (stok
/// hareketi) AYNI işlemde yazar (bkz. WorkOrderOperationService.CompleteAsync → stockLine),
/// dolayısıyla senaryo mamulün depo bakiyesinin üretilen kadar arttığını doğrular —
/// "HTTP 200" değil, stoğa gerçekten mamul girmiş olması aranır.
/// </summary>
public sealed class ProductionCompletionScenario : FunctionalTestScenarioBase
{
    public override string Key => "PROD_COMPLETION";
    public override string Group => "uretim";
    public override string Label => "Üretim Sonu Kaydı (Mamul Girişi)";
    public override IReadOnlyList<string> DependsOn => new[] { "PROD_CONSUMPTION" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var workOrderId = ctx.GetInt(FunctionalTestContext.Keys.ProdWorkOrderId);
        var op2Id = ctx.GetInt(FunctionalTestContext.Keys.ProdOp2Id);
        var personnelId = ctx.GetInt(FunctionalTestContext.Keys.PersonnelId);
        var itemId = ctx.GetInt(FunctionalTestContext.Keys.ProdItemId);
        var loc1 = ctx.GetInt(FunctionalTestContext.Keys.Location1Id);
        var qty = ProductionWorkOrderCreateScenario.PlannedQty;

        var before = FunctionalTestHelpers.BalanceFor(
            await FunctionalTestHelpers.GetStockBalancesAsync(ctx, new[] { itemId }, ct), itemId, loc1);

        var (startOk, _) = await StepPostAsync(ctx, steps, "Son operasyonu başlatma", "/Production/ShopFloor/Start",
            new { workOrderOperationId = op2Id, operatorPersonnelId = personnelId }, ct);
        if (!startOk) return;

        var (partOk, _) = await StepPostAsync(ctx, steps, "Son operasyonda üretim girişi", "/Production/ShopFloor/PartialComplete",
            new { workOrderOperationId = op2Id, operatorPersonnelId = personnelId, quantity = qty, scrapQuantity = (decimal?)null }, ct);
        if (!partOk) return;

        var (compOk, _) = await StepPostAsync(ctx, steps, "Son operasyonu tamamlama (mamul girişi)", "/Production/ShopFloor/Complete",
            new { workOrderOperationId = op2Id, operatorPersonnelId = personnelId, finalQuantity = qty }, ct);
        if (!compOk) return;

        var after = FunctionalTestHelpers.BalanceFor(
            await FunctionalTestHelpers.GetStockBalancesAsync(ctx, new[] { itemId }, ct), itemId, loc1);
        if (Math.Abs((before + qty) - after) > 0.0001m)
        {
            Fail(steps, "Mamul stok girişi doğrulama",
                $"Mamul bakiyesi {before}→{after}, beklenen {before + qty}. Son operasyon tamamlandı ama stoğa mamul girmedi.");
            return;
        }
        Pass(steps, "Mamul stok girişi doğrulama", $"Depo 1 mamul: {before} → {after}.");

        // İş emri başlığını kapat — durum makinesi İKİ ADIMLI geçmeyi zorunlu kılar.
        //
        // Önemli davranış: operasyonlar üretim işlese bile iş emri BAŞLIĞI kendiliğinden
        // "Devam Ediyor"a geçmiyor, "Yayımlandı"da kalıyor (WorkOrderService içinde başlığı
        // otomatik ilerleten bir yol yok). ValidateTransition ise Yayımlandı → Tamamlandı
        // sıçramasını reddediyor; geçerli yol Yayımlandı → Devam Ediyor → Tamamlandı.
        // Ekranda kullanıcının izlediği yol da budur — senaryo onu birebir taklit eder.
        var (progressOk, _) = await StepPostAsync(ctx, steps, "İş emrini Devam Ediyor'a alma",
            $"/Production/ChangeStatus?id={workOrderId}",
            new { workOrderId, newStatus = "InProgress" }, ct);
        if (!progressOk) return;

        var (statusOk, _) = await StepPostAsync(ctx, steps, "İş emrini Tamamlandı'ya alma",
            $"/Production/ChangeStatus?id={workOrderId}",
            new { workOrderId, newStatus = "Completed" }, ct);
        if (!statusOk) return;

        // Geri okuma: iş emri gerçekten "Tamamlandı" filtresinde görünüyor mu — POST'un
        // ok dönmesi durumun yazıldığını kanıtlamaz.
        var (boardOk, boardJson) = await StepGetAsync(ctx, steps, "İş emri durum doğrulama",
            "/Production/WorkOrdersBoardConfig?status=Completed", ct);
        if (!boardOk) return;
        var found = boardJson.GetArrayCI("entities").Any(e => e.GetIntCI("id") == workOrderId);
        if (!found)
        {
            Fail(steps, "İş emri durum doğrulama",
                $"İş emri #{workOrderId} 'Tamamlandı' listesinde bulunamadı — durum yazılmamış olabilir.");
            return;
        }
        Pass(steps, "İş emri durum doğrulama", "Tamamlandı listesinde görünüyor.");
    }
}
