using CalibraHub.Web.Services.FunctionalTests;

namespace CalibraHub.Web.Services.FunctionalTests.Scenarios;

/// <summary>
/// Faz 1 hazırlık senaryosu — test şirketi boş gelir, tüm sonraki ticari senaryoların
/// ihtiyaç duyduğu temel tanımları (ölçü birimi, 2 malzeme kartı, cari, 2 depo, personel)
/// GERÇEK HTTP uçlarından oluşturur. Bu senaryo başarısız olursa ona bağımlı HİÇBİR senaryo
/// çalışmaz (FunctionalTestRunner DependsOn zincirini "atlandı" olarak işaretler).
///
/// Ürettiği Id'ler <see cref="FunctionalTestContext.Keys"/> altında belgelenir — üretim/kalite
/// senaryolarını yazacak ikinci ajan bu anahtarları DependsOn=["SEED_MASTER_DATA"] ile okur,
/// kendi malzeme/cari/lokasyon setini yeniden kurmaz.
/// </summary>
public sealed class SeedMasterDataScenario : FunctionalTestScenarioBase
{
    public override string Key => "SEED_MASTER_DATA";
    public override string Group => "ticari";
    public override string Label => "Temel Test Verisi Kurulumu";

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];

        // ── 1) Ölçü birimi ──
        var unitCode = $"FNU{suffix}";
        var (unitSaveOk, _) = await StepPostAsync(ctx, steps, "Ölçü birimi oluşturma", "/Logistics/SaveMeasureUnitJson",
            new { unitCode, unitName = $"Fonksiyon Test Birimi {suffix}", intlCode = (string?)null, sortOrder = 0, isActive = true }, ct);
        if (!unitSaveOk) return;

        var (unitLookupOk, unitLookupJson) = await StepGetAsync(ctx, steps, "Ölçü birimi doğrulama",
            $"/Logistics/GetAllMeasureUnits?search={unitCode}", ct);
        if (!unitLookupOk) return;
        var unitRow = unitLookupJson.AsArray().FirstOrDefault(e => string.Equals(e.GetStringCI("code"), unitCode, StringComparison.OrdinalIgnoreCase));
        var unitId = unitRow.ValueKind == System.Text.Json.JsonValueKind.Object ? unitRow.GetIntCI("id") : 0;
        if (unitId <= 0) { Fail(steps, "Ölçü birimi doğrulama", $"Oluşturulan '{unitCode}' birimi listede bulunamadı."); return; }
        ctx.Set(FunctionalTestContext.Keys.UnitId, unitId);

        // ── 2) Malzeme kartı 1 ──
        var item1Code = $"FNI1-{suffix}";
        var (item1Ok, item1Json) = await StepPostAsync(ctx, steps, "Malzeme kartı 1 oluşturma (Hammadde)", "/Logistics/SaveMaterialCardJson",
            new
            {
                itemId = (int?)null, code = item1Code, name = $"Fonksiyon Test Malzeme 1 ({suffix})",
                typeId = 8, unitId, combinations = false, taxRate = 20m, trackingType = "None",
                minStock = 0m, autoSerial = false,
            }, ct);
        if (!item1Ok) return;
        var item1Id = item1Json.GetIntCI("id");
        if (item1Id <= 0) { Fail(steps, "Malzeme kartı 1 oluşturma (Hammadde)", "Sunucu Id döndürmedi."); return; }
        ctx.Set(FunctionalTestContext.Keys.Item1Id, item1Id);
        ctx.Set(FunctionalTestContext.Keys.Item1Code, item1Code);

        // ── 3) Malzeme kartı 2 ──
        var item2Code = $"FNI2-{suffix}";
        var (item2Ok, item2Json) = await StepPostAsync(ctx, steps, "Malzeme kartı 2 oluşturma (Ticari Mal)", "/Logistics/SaveMaterialCardJson",
            new
            {
                itemId = (int?)null, code = item2Code, name = $"Fonksiyon Test Malzeme 2 ({suffix})",
                typeId = 8, unitId, combinations = false, taxRate = 20m, trackingType = "None",
                minStock = 0m, autoSerial = false,
            }, ct);
        if (!item2Ok) return;
        var item2Id = item2Json.GetIntCI("id");
        if (item2Id <= 0) { Fail(steps, "Malzeme kartı 2 oluşturma (Ticari Mal)", "Sunucu Id döndürmedi."); return; }
        ctx.Set(FunctionalTestContext.Keys.Item2Id, item2Id);
        ctx.Set(FunctionalTestContext.Keys.Item2Code, item2Code);

        // Doğrulama: iki kart da /Sales/GetMaterials'ta (gerçek belge ekranının kullandığı
        // malzeme kaynağı) görünüyor mu — yalnız "HTTP 200" değil, veri gerçekten oluştu mu.
        var (matOk, matJson) = await StepGetAsync(ctx, steps, "Malzeme kartlarını belge ekranından doğrulama",
            "/Sales/GetMaterials?docType=satis_teklifi", ct);
        if (!matOk) return;
        var matRows = matJson.AsArray();
        var found1 = matRows.Any(r => r.GetIntCI("id") == item1Id);
        var found2 = matRows.Any(r => r.GetIntCI("id") == item2Id);
        if (!found1 || !found2)
        {
            Fail(steps, "Malzeme kartlarını belge ekranından doğrulama",
                $"GetMaterials listesinde bulunamadı — item1={found1}, item2={found2}.");
            return;
        }
        Pass(steps, "Malzeme kartlarını belge ekranından doğrulama");

        // ── 4) Cari (müşteri + tedarikçi ikisi birden — AccountType=3) ──
        var contactCode = $"FNC-{suffix}";
        var (contactOk, contactJson) = await StepPostAsync(ctx, steps, "Cari oluşturma", "/Finance/UpsertContact",
            new
            {
                id = (int?)null, accountType = (byte)3, accountCode = contactCode,
                accountTitle = $"Fonksiyon Test Cari {suffix}", taxNumber = (string?)null,
                identityNumber = (string?)null, taxOffice = (string?)null, phone = (string?)null,
                email = (string?)null, address = (string?)null, city = (string?)null, district = (string?)null,
                isActive = true, priceGroupId = (int?)null,
            }, ct);
        if (!contactOk) return;
        var contactId = contactJson.TryGetPropertyCI("account", out var accountEl) ? accountEl.GetIntCI("id") : 0;
        if (contactId <= 0) { Fail(steps, "Cari oluşturma", "Sunucu account.id döndürmedi."); return; }
        ctx.Set(FunctionalTestContext.Keys.ContactId, contactId);

        // ── 5+6) İki depo (kök seviye, çocuksuz → "yaprak" — belge ekranlarında seçilebilir) ──
        var loc1Code = $"FNL1-{suffix}";
        var (loc1Ok, _) = await StepPostAsync(ctx, steps, "Depo 1 oluşturma", "/Logistics/SaveLocationJson",
            new
            {
                id = (int?)null, parentId = (int?)null, locationTypeCode = "FACTORY", locationCode = loc1Code,
                locationName = $"Fonksiyon Test Depo 1 ({suffix})", sortOrder = 0,
                maxWeightCapacity = (decimal?)null, volumeCapacity = (decimal?)null, isActive = true,
                isMachinePark = false, isStorageArea = true, isCountReference = false, isSingleChildType = false,
                allowNegativeBalance = (bool?)null,
            }, ct);
        if (!loc1Ok) return;

        var loc2Code = $"FNL2-{suffix}";
        var (loc2Ok, _) = await StepPostAsync(ctx, steps, "Depo 2 oluşturma", "/Logistics/SaveLocationJson",
            new
            {
                id = (int?)null, parentId = (int?)null, locationTypeCode = "FACTORY", locationCode = loc2Code,
                locationName = $"Fonksiyon Test Depo 2 ({suffix})", sortOrder = 0,
                maxWeightCapacity = (decimal?)null, volumeCapacity = (decimal?)null, isActive = true,
                isMachinePark = false, isStorageArea = true, isCountReference = false, isSingleChildType = false,
                allowNegativeBalance = (bool?)null,
            }, ct);
        if (!loc2Ok) return;

        var (locLookupOk, locLookupJson) = await StepGetAsync(ctx, steps, "Depoları doğrulama", "/Logistics/GetAllLocations?search=FNL", ct);
        if (!locLookupOk) return;
        var locRows = locLookupJson.AsArray();
        var loc1Row = locRows.FirstOrDefault(r => string.Equals(r.GetStringCI("locationCode"), loc1Code, StringComparison.OrdinalIgnoreCase));
        var loc2Row = locRows.FirstOrDefault(r => string.Equals(r.GetStringCI("locationCode"), loc2Code, StringComparison.OrdinalIgnoreCase));
        var location1Id = loc1Row.ValueKind == System.Text.Json.JsonValueKind.Object ? loc1Row.GetIntCI("id") : 0;
        var location2Id = loc2Row.ValueKind == System.Text.Json.JsonValueKind.Object ? loc2Row.GetIntCI("id") : 0;
        if (location1Id <= 0 || location2Id <= 0)
        {
            Fail(steps, "Depoları doğrulama", $"Depo listede bulunamadı — depo1={location1Id}, depo2={location2Id}.");
            return;
        }
        ctx.Set(FunctionalTestContext.Keys.Location1Id, location1Id);
        ctx.Set(FunctionalTestContext.Keys.Location2Id, location2Id);

        // ── 7) Personel (İhtiyaç Kaydı "Talep Eden" alanı zorunlu — DocumentService.SaveQuoteAsync) ──
        var (personnelOk, personnelJson) = await StepPostAsync(ctx, steps, "Personel oluşturma", "/Production/SavePersonnel",
            new
            {
                id = 0, code = "", fullName = $"Fonksiyon Test Personel {suffix}", title = (string?)null,
                department = (string?)null, pinCode = (string?)null, cardNo = (string?)null,
                isProductionOperator = false, isActive = true, userId = (int?)null, phone = (string?)null,
                email = (string?)null, notes = (string?)null, birthDate = (DateTime?)null, locationId = (int?)null,
            }, ct);
        if (!personnelOk) return;
        var personnelId = personnelJson.GetIntCI("id");
        if (personnelId <= 0) { Fail(steps, "Personel oluşturma", "Sunucu Id döndürmedi."); return; }
        ctx.Set(FunctionalTestContext.Keys.PersonnelId, personnelId);

        // ── 8) Para birimi (TRY) — belge kaydı CurrencyId ister ──
        var (curOk, curJson) = await StepGetAsync(ctx, steps, "Para birimi (TRY) çözümleme", "/PriceList/GetCurrencies", ct);
        if (!curOk) return;
        var tryRow = curJson.AsArray().FirstOrDefault(r => string.Equals(r.GetStringCI("code"), "TRY", StringComparison.OrdinalIgnoreCase));
        var currencyId = tryRow.ValueKind == System.Text.Json.JsonValueKind.Object ? tryRow.GetIntCI("id") : 0;
        if (currencyId <= 0) { Fail(steps, "Para birimi (TRY) çözümleme", "TRY para birimi bulunamadı."); return; }
        ctx.Set(FunctionalTestContext.Keys.CurrencyId, currencyId);
    }
}
