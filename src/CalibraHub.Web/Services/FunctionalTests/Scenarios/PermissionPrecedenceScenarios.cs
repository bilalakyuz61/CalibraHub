namespace CalibraHub.Web.Services.FunctionalTests.Scenarios;

/// <summary>
/// Yetki Öncelik Kuralları — bir iznin NEREDEN geldiğine göre doğru çözümlenip
/// çözümlenmediğini sınar. Dört durum, dört AYRI form üzerinde (birbirini etkilemesin diye):
///
///   1. Kendinde izin var, departmanda yok            → işlem YAPILABİLMELİ
///   2. Kendinde satır yok, departmanda izin var      → devralma çalışmalı, YAPILABİLMELİ
///   3. Departmanda izin var, kendinde açık RET       → override çalışmalı, YAPILAMAMALI
///   4. Departmanda açık RET, kendinde izin var       → kullanıcı kazanmalı, YAPILABİLMELİ
///
/// Çözümleyicinin sözleşmesi: KULLANICI satırı &gt; DEPARTMAN satırı &gt; (hiçbiri) reddet.
/// 3 ve 4 birbirinin aynadaki hâli — ikisi birden geçmezse öncelik yönü ters kurulmuş demektir.
///
/// ÖNEMLİ AYRIM (3. durum): "kendinde yetki yok" iki farklı şey olabilir — hiç satır olmaması
/// (o zaman departmandan devralınır ve işlem YAPILIR) ya da açık RET satırı (o zaman engellenir).
/// Bu senaryo açık RET satırı yazar; ikisini karıştırmak yetki modelinin en sık yanlış
/// kurulan yeridir.
/// </summary>
public sealed class PermissionPrecedenceScenario : FunctionalTestScenarioBase
{
    private sealed record Case(
        string Label, string FormCode, string MutationPath,
        bool? UserGrant, bool? DeptGrant, bool ExpectAllowed,
        Func<FunctionalTestContext, object> BuildBody);

    public override string Key => "PERM_PRECEDENCE";
    public override string Group => "yetki";
    public override string Label => "Yetki Öncelik Kuralları (Kendi / Departman)";
    public override IReadOnlyList<string> DependsOn => new[] { "PERM_REVOKE_DENY" };

    private static IReadOnlyList<Case> Cases => new[]
    {
        new Case("Kendinde izin var, departmanda yok", "MATERIAL_CARD_EDIT", "/Logistics/SaveMaterialCardJson",
            UserGrant: true, DeptGrant: null, ExpectAllowed: true,
            ctx => new
            {
                itemId = (int?)null, code = "FNPRC-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(),
                name = "Öncelik Testi Malzemesi", typeId = 8,
                unitId = ctx.GetInt(FunctionalTestContext.Keys.UnitId),
                combinations = false, taxRate = 20m, trackingType = "None", minStock = 0m, autoSerial = false,
            }),

        new Case("Departmandan devralma (kendinde satır yok)", "WORK_ORDER_EDIT", "/Production/Create",
            UserGrant: null, DeptGrant: true, ExpectAllowed: true,
            ctx => new
            {
                itemId = ctx.GetInt(FunctionalTestContext.Keys.Item1Id), configId = (int?)null,
                plannedQuantity = 1m, unitId = ctx.GetInt(FunctionalTestContext.Keys.UnitId),
                plannedStartDate = (DateTime?)null, plannedEndDate = (DateTime?)null,
                priority = "Medium", assignedUserId = (int?)null,
                warehouseLocationId = ctx.GetInt(FunctionalTestContext.Keys.Location1Id),
                routingId = (int?)null, defaultMachineId = (int?)null, assignedPersonnelId = (int?)null,
                notes = "Öncelik testi", autoRelease = false,
                argeProjectId = (int?)null, orderDate = (DateTime?)null,
            }),

        new Case("Departmanda izin var, kendinde açık RET", "USER_MANAGEMENT", "/CompanyUser/Save",
            UserGrant: false, DeptGrant: true, ExpectAllowed: false,
            ctx => new
            {
                id = (int?)null,
                fullName = "Öncelik Testi Kullanıcı " + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(),
                email = "prec.probe." + Guid.NewGuid().ToString("N")[..8] + "@calibra.test",
                employeeCode = "PRC-" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(),
                departmentId = ctx.GetInt(PermissionSeedScenario.DepartmentIdKey),
                supervisorUserId = (int?)null, phoneNumber = (string?)null,
                role = 4, isActive = true, password = (string?)null,
            }),

        new Case("Departmanda açık RET, kendinde izin var", "CONTACTS", "/Finance/UpsertContact",
            UserGrant: true, DeptGrant: false, ExpectAllowed: true,
            ctx => new
            {
                id = (int?)null, accountType = (byte)3,
                accountCode = "FNPRC-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(),
                accountTitle = "Öncelik Testi Cari", taxNumber = (string?)null,
                identityNumber = (string?)null, taxOffice = (string?)null, phone = (string?)null,
                email = (string?)null, address = (string?)null, city = (string?)null, district = (string?)null,
                isActive = true, priceGroupId = (int?)null,
            }),
    };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var client = ctx.Get<FunctionalTestHttpClient>(PermissionSeedScenario.UserClientKey);
        var userId = ctx.GetInt(PermissionSeedScenario.UserIdKey);
        var deptId = ctx.GetInt(PermissionSeedScenario.DepartmentIdKey);
        if (client is null || userId <= 0 || deptId <= 0)
        {
            Fail(steps, "Kullanıcı oturumu", "Yetki testi kullanıcısı/departmanı bağlamda yok.");
            return;
        }

        var (mapOk, defMap) = await PermissionHelpers.ResolveDefMapAsync(
            ctx, steps, userId, Cases.Select(c => c.FormCode), "CREATE", ct);
        if (!mapOk) return;

        var missing = Cases.Where(c => !defMap.ContainsKey(c.FormCode)).Select(c => c.FormCode).ToList();
        if (missing.Count > 0)
        {
            Fail(steps, "İzin tanımlarını çözümleme", $"Şu formlar için CREATE izin tanımı yok: {string.Join(", ", missing)}.");
            return;
        }

        // Dört durumun tamamı TEK kaydetmeyle kurulur — kullanıcı/departman kaydetme uçları
        // sahibin TÜM satırlarını değiştirdiği için (BulkReplace) durum durum kaydetmek
        // önceki durumları silerdi.
        var userItems = Cases.Where(c => c.UserGrant.HasValue)
            .Select(c => new { permissionDefId = defMap[c.FormCode], isGranted = c.UserGrant!.Value }).ToArray();
        var deptItems = Cases.Where(c => c.DeptGrant.HasValue)
            .Select(c => new { permissionDefId = defMap[c.FormCode], isGranted = c.DeptGrant!.Value }).ToArray();

        var (uOk, _) = await StepPostAsync(ctx, steps, "Kullanıcı izin satırlarını kurma", $"/Permission/User/{userId}/Save",
            new { userId, departmentId = (int?)null, items = userItems }, ct);
        if (!uOk) return;

        var (dOk, _) = await StepPostAsync(ctx, steps, "Departman izin satırlarını kurma", $"/Permission/Department/{deptId}/Save",
            new { userId = (int?)null, departmentId = deptId, items = deptItems }, ct);
        if (!dOk) return;

        foreach (var c in Cases)
        {
            var res = await client.PostAsync(c.MutationPath, c.BuildBody(ctx), ct);
            if (res.Ok == c.ExpectAllowed)
            {
                Pass(steps, c.Label, c.ExpectAllowed ? "İşlem yapılabildi (beklenen)." : "İşlem reddedildi (beklenen).");
                continue;
            }

            Fail(steps, c.Label, c.ExpectAllowed
                ? $"İzin bu yoldan gelmeliydi ama işlem reddedildi: {res.Error}"
                : "İzin geçersiz olmalıydı ama işlem yapılabildi — kullanıcı RET satırı departman iznini ezmiyor.");
            return;
        }
    }
}

/// <summary>
/// Grup Yetkisi Sızdırmıyor mu — grup üyeliğinin yetki VERMEDİĞİNİ doğrular.
///
/// Bu ters yönlü bir testtir ve bilinçli bir ürün kararını sabitler: grup yetki mantığı
/// 2026-08-05'te enforcement'tan çıkarıldı (PermissionService, "Seq 1090"). Grup tabloları
/// ve uçları geri alınabilir olsun diye duruyor ama karar VERMİYOR. Eğer bir gün grup
/// satırları yeniden dikkate alınmaya başlarsa, arayüzden kaldırılmış eski grup üyelikleri
/// sessizce yetki dağıtır — kimsenin fark etmeyeceği bir açık. Bu senaryo o günü yakalar.
///
/// Grup mekanizması yeniden AÇILMAK istenirse bu senaryonun beklentisi de bilinçli olarak
/// tersine çevrilmelidir; testin kırılması "karar değişti" sinyalidir, gürültü değil.
/// </summary>
public sealed class PermissionGroupDormantScenario : FunctionalTestScenarioBase
{
    private const string TargetFormCode = "MATERIAL_CARD_EDIT";

    public override string Key => "PERM_GROUP_DORMANT";
    public override string Group => "yetki";
    public override string Label => "Grup Yetkisi Sızdırmıyor mu";
    public override IReadOnlyList<string> DependsOn => new[] { "PERM_PRECEDENCE" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var client = ctx.Get<FunctionalTestHttpClient>(PermissionSeedScenario.UserClientKey);
        var userId = ctx.GetInt(PermissionSeedScenario.UserIdKey);
        var deptId = ctx.GetInt(PermissionSeedScenario.DepartmentIdKey);
        if (client is null || userId <= 0 || deptId <= 0)
        {
            Fail(steps, "Kullanıcı oturumu", "Yetki testi kullanıcısı/departmanı bağlamda yok.");
            return;
        }

        var (mapOk, defMap) = await PermissionHelpers.ResolveDefMapAsync(
            ctx, steps, userId, new[] { TargetFormCode }, "CREATE", ct);
        if (!mapOk || !defMap.TryGetValue(TargetFormCode, out var defId))
        {
            Fail(steps, "İzin tanımını çözümleme", $"'{TargetFormCode}' için CREATE izin tanımı bulunamadı.");
            return;
        }

        // Kullanıcı ve departman satırlarını TEMİZLE — kalan izin YALNIZCA gruptan gelebilsin.
        var (clrUserOk, _) = await StepPostAsync(ctx, steps, "Kullanıcı izinlerini temizleme", $"/Permission/User/{userId}/Save",
            new { userId, departmentId = (int?)null, items = Array.Empty<object>() }, ct);
        if (!clrUserOk) return;
        var (clrDeptOk, _) = await StepPostAsync(ctx, steps, "Departman izinlerini temizleme", $"/Permission/Department/{deptId}/Save",
            new { userId = (int?)null, departmentId = deptId, items = Array.Empty<object>() }, ct);
        if (!clrDeptOk) return;

        var (grpOk, grpJson) = await StepPostAsync(ctx, steps, "Yetki grubu oluşturma", "/Permission/Groups/Save",
            new
            {
                id = (int?)null,
                name = "Yetki Testi Grubu " + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(),
                description = "Fonksiyon testi", isActive = true,
            }, ct);
        if (!grpOk) return;
        var groupId = grpJson.GetIntCI("id");
        if (groupId <= 0)
        {
            // Grup uçları dormant; kaldırılmışsa senaryo anlamını yitirir — sessizce geçmek yerine söyle.
            Fail(steps, "Yetki grubu oluşturma", "Sunucu grup Id'si döndürmedi (grup uçları kaldırılmış olabilir).");
            return;
        }

        var (matOk, _) = await StepPostAsync(ctx, steps, "Gruba izin verme", $"/Permission/Group/{groupId}/Save",
            new
            {
                userId = (int?)null, departmentId = (int?)null,
                items = new[] { new { permissionDefId = defId, isGranted = true } },
            }, ct);
        if (!matOk) return;

        var (memOk, _) = await StepPostAsync(ctx, steps, "Kullanıcıyı gruba ekleme", $"/Permission/Group/{groupId}/Members/Save",
            new { userIds = new[] { userId } }, ct);
        if (!memOk) return;

        var res = await client.PostAsync("/Logistics/SaveMaterialCardJson",
            new
            {
                itemId = (int?)null, code = "FNGRP-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(),
                name = "Grup Testi Malzemesi", typeId = 8,
                unitId = ctx.GetInt(FunctionalTestContext.Keys.UnitId),
                combinations = false, taxRate = 20m, trackingType = "None", minStock = 0m, autoSerial = false,
            }, ct);

        if (res.Ok)
        {
            Fail(steps, "Grup izni sızma kontrolü",
                "Kullanıcının tek izin kaynağı grup üyeliğiydi ve işlem YAPILABİLDİ — grup yetkileri " +
                "enforcement'a geri sızmış. Ya karar değişti (senaryo güncellenmeli) ya da sessiz bir açık var.");
            return;
        }
        Pass(steps, "Grup izni sızma kontrolü", "Grup üyeliği tek başına yetki vermedi (beklenen).");
    }
}
