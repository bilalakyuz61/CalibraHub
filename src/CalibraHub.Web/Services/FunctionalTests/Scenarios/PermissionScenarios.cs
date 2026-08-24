using System.Text.Json;

namespace CalibraHub.Web.Services.FunctionalTests.Scenarios;

/// <summary>
/// Yetki senaryolarının ortak hedef tablosu — her satır bir ekranı (sayfa) ve o ekrana ait
/// gerçek bir mutasyon ucunu temsil eder.
///
/// Neden hem sayfa hem mutasyon: yetki iki ayrı yerde zorlanır. Sayfa GET'i ekranı açmayı,
/// mutasyon POST'u işlemi yapmayı kapatır. Yalnız birini sınamak "menüde görünmüyor ama
/// uç açık" (ya da tersi) sınıfı açıkları kaçırır.
///
/// Kapsam notu: liste temsili seçilmiş 3 ekranı içerir; yeni satır eklemek tek bir kayıt
/// yazmak kadar basittir (Form kodu + sayfa yolu + uç + gövde üretici).
/// </summary>
/// <param name="PageFormCode">Sayfayı koruyan form kodu.</param>
/// <param name="MutationFormCode">
/// Mutasyonu koruyan form kodu. Genelde sayfayla AYNIdır ama olmak zorunda değil:
/// örneğin İş Emirleri listesi WORK_ORDERS ile, kayıt açma WORK_ORDER_EDIT ile korunur.
/// İkisini tek alan varsaymak, izin verme adımının yanlış izni vermesine yol açıyordu.
/// </param>
internal sealed record PermissionTarget(
    string Label,
    string PageFormCode,
    string MutationFormCode,
    string PagePath,
    string MutationPath,
    Func<FunctionalTestContext, object> BuildBody);

internal static class PermissionTargets
{
    public static IReadOnlyList<PermissionTarget> All => new[]
    {
        new PermissionTarget(
            "Malzeme Kartları", "MATERIAL_CARD_EDIT", "MATERIAL_CARD_EDIT",
            "/Logistics/MaterialCards", "/Logistics/SaveMaterialCardJson",
            ctx => new
            {
                itemId = (int?)null,
                code = "FNPERM-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(),
                name = "Yetki Testi Malzemesi",
                typeId = 8, unitId = ctx.GetInt(FunctionalTestContext.Keys.UnitId),
                combinations = false, taxRate = 20m, trackingType = "None",
                minStock = 0m, autoSerial = false,
            }),

        new PermissionTarget(
            // Sayfa WORK_ORDERS, kayıt açma WORK_ORDER_EDIT ile korunur — ayrı kodlar.
            "İş Emirleri", "WORK_ORDERS", "WORK_ORDER_EDIT",
            "/Production/WorkOrders", "/Production/Create",
            ctx => new
            {
                itemId = ctx.GetInt(FunctionalTestContext.Keys.Item1Id),
                configId = (int?)null, plannedQuantity = 1m,
                unitId = ctx.GetInt(FunctionalTestContext.Keys.UnitId),
                plannedStartDate = (DateTime?)null, plannedEndDate = (DateTime?)null,
                priority = "Medium", assignedUserId = (int?)null,
                warehouseLocationId = ctx.GetInt(FunctionalTestContext.Keys.Location1Id),
                routingId = (int?)null, defaultMachineId = (int?)null, assignedPersonnelId = (int?)null,
                notes = "Yetki testi", autoRelease = false,
                argeProjectId = (int?)null, orderDate = (DateTime?)null,
            }),

        new PermissionTarget(
            "Kullanıcı Tanımlamaları", "USER_MANAGEMENT", "USER_MANAGEMENT",
            "/CompanyUser/Index", "/CompanyUser/Save",
            ctx => new
            {
                id = (int?)null,
                fullName = "Yetki Testi Kullanıcı " + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(),
                email = "perm.probe." + Guid.NewGuid().ToString("N")[..8] + "@calibra.test",
                employeeCode = "PRB-" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(),
                departmentId = ctx.GetInt(PermissionSeedScenario.DepartmentIdKey),
                supervisorUserId = (int?)null, phoneNumber = (string?)null,
                role = 4, isActive = true, password = (string?)null,
            }),
    };
}

/// <summary>
/// Yetki Testi Kullanıcısı Oluşturma — sınırlı yetkili (Operatör) bir kullanıcı açar ve
/// ONUN kimliğiyle ikinci bir HTTP oturumu kurar. Sonraki yetki senaryoları bu oturumu
/// kullanır: kontroller gerçek kullanıcı gibi, gerçek uçlara, kendi çerezleriyle yapılır.
///
/// Rol seçimi: sistemde ayrı bir "User" rolü yok; yetki kapılarının uygulandığı normal
/// (admin olmayan) rol <c>Operator</c>'dür — SystemAdmin ve DepartmentManager kısayolla
/// TÜM kontrolleri geçer, dolayısıyla onlarla yetki sınaması anlamsız olurdu.
/// </summary>
public sealed class PermissionSeedScenario : FunctionalTestScenarioBase
{
    public const string DepartmentIdKey = "permDepartmentId";
    public const string UserIdKey = "permUserId";
    public const string UserEmailKey = "permUserEmail";
    public const string UserClientKey = "permUserClient";

    /// <summary>Kullanıcı kaydında açıkça belirlenen şifre — varsayılan şifreye bağımlı kalmamak için.</summary>
    private const string Password = "Perm!Test1234";

    public override string Key => "PERM_SEED";
    public override string Group => "yetki";
    public override string Label => "Yetki Testi Kullanıcısı Oluşturma";
    public override IReadOnlyList<string> DependsOn => new[] { "SEED_MASTER_DATA" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ctx.BaseUrl) || ctx.CompanyId <= 0)
        {
            Fail(steps, "Ortam kontrolü", "Uygulama adresi/şirket bilgisi bağlamda yok — ikinci kullanıcı girişi yapılamaz.");
            return;
        }

        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var deptName = $"Yetki Testi Departmanı {suffix}";
        var email = $"yetki.test.{suffix.ToLowerInvariant()}@calibra.test";

        var (deptOk, _) = await StepPostAsync(ctx, steps, "Departman oluşturma", "/Admin/SaveDepartmentJson",
            new { companyId = (int?)null, name = deptName }, ct);
        if (!deptOk) return;

        var (dListOk, dListJson) = await StepGetAsync(ctx, steps, "Departmanı çözümleme",
            $"/Admin/GetDepartmentsJson?search={Uri.EscapeDataString(suffix)}", ct);
        if (!dListOk) return;
        var dRow = dListJson.AsArray().FirstOrDefault(d => string.Equals(d.GetStringCI("name"), deptName, StringComparison.OrdinalIgnoreCase));
        var departmentId = dRow.ValueKind == JsonValueKind.Object ? dRow.GetIntCI("id") : 0;
        if (departmentId <= 0) { Fail(steps, "Departmanı çözümleme", "Departman Id'si okunamadı."); return; }
        ctx.Set(DepartmentIdKey, departmentId);

        // role = 4 (Operator) — yetki kapılarının gerçekten uygulandığı normal rol.
        var (userOk, _) = await StepPostAsync(ctx, steps, "Sınırlı yetkili kullanıcı oluşturma", "/CompanyUser/Save",
            new
            {
                id = (int?)null, fullName = $"Yetki Testi Kullanıcısı {suffix}", email,
                employeeCode = $"YT-{suffix}", departmentId, supervisorUserId = (int?)null,
                phoneNumber = (string?)null, role = 4, isActive = true, password = Password,
            }, ct);
        if (!userOk) return;

        var (uListOk, uListJson) = await StepGetAsync(ctx, steps, "Kullanıcıyı çözümleme", "/CompanyUser/UsersLookup", ct);
        if (!uListOk) return;
        var uRow = uListJson.GetArrayCI("users")
            .FirstOrDefault(u => string.Equals(u.GetStringCI("email"), email, StringComparison.OrdinalIgnoreCase));
        var userId = uRow.ValueKind == JsonValueKind.Object ? uRow.GetIntCI("id") : 0;
        if (userId <= 0) { Fail(steps, "Kullanıcıyı çözümleme", $"'{email}' kullanıcısı listede bulunamadı."); return; }
        ctx.Set(UserIdKey, userId);
        ctx.Set(UserEmailKey, email);

        var (client, loginError) = await FunctionalTestHttpClient.LoginAsync(ctx.BaseUrl, email, Password, ctx.CompanyId, ct);
        if (client is null)
        {
            Fail(steps, "Kullanıcı oturumu açma", $"Giriş yapılamadı: {loginError}");
            return;
        }
        ctx.Set(UserClientKey, client);
        Pass(steps, "Kullanıcı oturumu açma", $"#{userId} · {email} oturumu açıldı.");
    }
}

/// <summary>
/// Yetkisiz Kullanıcı Engelleniyor mu — hiçbir izin verilmemiş kullanıcı için HER hedefte
/// hem sayfanın açılmaması hem de mutasyonun reddedilmesi beklenir.
///
/// Bu senaryonun başarısız olması gerçek bir güvenlik açığıdır: yetkisi olmayan kullanıcı
/// kayıt yazabiliyor demektir. Bu yüzden "engellendi" ölçütü gevşek DEĞİL: sayfa için
/// 2xx dönmemesi, mutasyon için isteğin (HTTP ya da iş kuralı seviyesinde) başarısız olması.
/// </summary>
public sealed class PermissionDenyScenario : FunctionalTestScenarioBase
{
    public override string Key => "PERM_DENY_WITHOUT_GRANT";
    public override string Group => "yetki";
    public override string Label => "Yetkisiz Kullanıcı Engelleniyor mu";
    public override IReadOnlyList<string> DependsOn => new[] { "PERM_SEED" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var client = ctx.Get<FunctionalTestHttpClient>(PermissionSeedScenario.UserClientKey);
        if (client is null) { Fail(steps, "Kullanıcı oturumu", "Yetki testi kullanıcısının oturumu bağlamda yok."); return; }

        foreach (var t in PermissionTargets.All)
        {
            var status = await client.GetStatusCodeAsync(t.PagePath, ct);
            if (status >= 200 && status < 300)
            {
                Fail(steps, $"{t.Label} — sayfa engeli", $"Yetkisiz kullanıcı sayfayı açabildi (HTTP {status}).");
                return;
            }
            Pass(steps, $"{t.Label} — sayfa engeli", $"HTTP {status} ile engellendi.");

            var res = await client.PostAsync(t.MutationPath, t.BuildBody(ctx), ct);
            if (res.Ok)
            {
                Fail(steps, $"{t.Label} — işlem engeli",
                    "Yetkisiz kullanıcı kayıt yazabildi — bu bir güvenlik açığıdır.");
                return;
            }
            Pass(steps, $"{t.Label} — işlem engeli", "İşlem reddedildi.");
        }
    }
}

/// <summary>
/// Yetki Verilince İşlem Yapılabiliyor mu — aynı kullanıcıya her hedef için GÖRÜNTÜLEME ve
/// EKLEME izinleri verilir; ardından hem sayfanın açıldığı hem de mutasyonun geçtiği
/// doğrulanır. Bu senaryo yetki katmanının "fazla kapalı" olmadığını sınar: sadece
/// engellemek yetmez, izin verilince çalışması da gerekir.
/// </summary>
public sealed class PermissionGrantScenario : FunctionalTestScenarioBase
{
    /// <summary>Verilen izin kodları — ekranı açmak (VIEW) + kayıt eklemek (CREATE).</summary>
    private static readonly string[] Actions = { "VIEW", "CREATE" };

    public override string Key => "PERM_GRANT_ALLOW";
    public override string Group => "yetki";
    public override string Label => "Yetki Verilince İşlem Yapılabiliyor mu";
    public override IReadOnlyList<string> DependsOn => new[] { "PERM_DENY_WITHOUT_GRANT" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var client = ctx.Get<FunctionalTestHttpClient>(PermissionSeedScenario.UserClientKey);
        var userId = ctx.GetInt(PermissionSeedScenario.UserIdKey);
        if (client is null || userId <= 0) { Fail(steps, "Kullanıcı oturumu", "Yetki testi kullanıcısı bağlamda yok."); return; }

        var formCodes = PermissionTargets.All
            .SelectMany(t => new[] { t.PageFormCode, t.MutationFormCode })
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var (defsOk, defIds) = await PermissionHelpers.ResolveDefIdsAsync(
            ctx, steps, userId, formCodes, Actions, ct);
        if (!defsOk) return;

        var (saveOk, _) = await StepPostAsync(ctx, steps, "İzinleri verme", $"/Permission/User/{userId}/Save",
            new { userId, departmentId = (int?)null, items = defIds.Select(id => new { permissionDefId = id, isGranted = true }).ToArray() }, ct);
        if (!saveOk) return;

        foreach (var t in PermissionTargets.All)
        {
            var status = await client.GetStatusCodeAsync(t.PagePath, ct);
            if (status is < 200 or >= 300)
            {
                Fail(steps, $"{t.Label} — sayfa erişimi", $"Görüntüleme izni verildiği hâlde sayfa açılmadı (HTTP {status}).");
                return;
            }
            Pass(steps, $"{t.Label} — sayfa erişimi", $"HTTP {status}.");

            var res = await client.PostAsync(t.MutationPath, t.BuildBody(ctx), ct);
            if (!res.Ok)
            {
                Fail(steps, $"{t.Label} — işlem izni",
                    $"Ekleme izni verildiği hâlde işlem reddedildi: {res.Error}");
                return;
            }
            Pass(steps, $"{t.Label} — işlem izni", "İşlem yapılabildi.");
        }
    }
}

/// <summary>
/// Yetki Geri Alınınca Engelleniyor mu — verilen izinler kaldırılır ve aynı kullanıcının
/// yeniden engellendiği doğrulanır. İzin iptalinin ANINDA etkili olması gerekir; yetki
/// önbelleği temizlenmezse kullanıcı iptalden sonra da işlem yapmaya devam eder ki bu
/// sessiz bir açıktır — senaryo tam olarak bunu yakalar.
/// </summary>
public sealed class PermissionRevokeScenario : FunctionalTestScenarioBase
{
    public override string Key => "PERM_REVOKE_DENY";
    public override string Group => "yetki";
    public override string Label => "Yetki Geri Alınınca Engelleniyor mu";
    public override IReadOnlyList<string> DependsOn => new[] { "PERM_GRANT_ALLOW" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var client = ctx.Get<FunctionalTestHttpClient>(PermissionSeedScenario.UserClientKey);
        var userId = ctx.GetInt(PermissionSeedScenario.UserIdKey);
        if (client is null || userId <= 0) { Fail(steps, "Kullanıcı oturumu", "Yetki testi kullanıcısı bağlamda yok."); return; }

        // Boş liste = kullanıcıya ait TÜM override'lar silinir (BulkReplaceForOwnerAsync).
        var (revokeOk, _) = await StepPostAsync(ctx, steps, "İzinleri geri alma", $"/Permission/User/{userId}/Save",
            new { userId, departmentId = (int?)null, items = Array.Empty<object>() }, ct);
        if (!revokeOk) return;

        foreach (var t in PermissionTargets.All)
        {
            var status = await client.GetStatusCodeAsync(t.PagePath, ct);
            if (status >= 200 && status < 300)
            {
                Fail(steps, $"{t.Label} — sayfa engeli", $"İzin geri alındığı hâlde sayfa hâlâ açılıyor (HTTP {status}).");
                return;
            }
            Pass(steps, $"{t.Label} — sayfa engeli", $"HTTP {status} ile yeniden engellendi.");

            var res = await client.PostAsync(t.MutationPath, t.BuildBody(ctx), ct);
            if (res.Ok)
            {
                Fail(steps, $"{t.Label} — işlem engeli",
                    "İzin geri alındığı hâlde kullanıcı kayıt yazabildi — yetki önbelleği temizlenmiyor olabilir.");
                return;
            }
            Pass(steps, $"{t.Label} — işlem engeli", "İşlem yeniden reddedildi.");
        }
    }
}

internal static class PermissionHelpers
{
    /// <summary>
    /// (Form kodu, aksiyon kodu) çiftlerini PermissionDef Id'lerine çevirir. Kaynak, yetki
    /// yönetimi ekranının kullandığı uçtur (<c>/Permission/User/{id}</c>) — böylece test,
    /// izin kataloğunu yeniden tanımlamak yerine ürünün kendi tanımını kullanır.
    /// </summary>
    public static async Task<(bool Ok, IReadOnlyList<int> DefIds)> ResolveDefIdsAsync(
        FunctionalTestContext ctx, List<FunctionalTestStep> steps, int userId,
        IEnumerable<string> formCodes, IReadOnlyList<string> actions, CancellationToken ct)
    {
        var res = await ctx.Http.GetAsync($"/Permission/User/{userId}", ct);
        if (!res.Ok)
        {
            steps.Add(new FunctionalTestStep("İzin tanımlarını okuma", false, res.Error));
            return (false, Array.Empty<int>());
        }

        var forms = new HashSet<string>(formCodes, StringComparer.OrdinalIgnoreCase);
        var wanted = new HashSet<string>(actions, StringComparer.OrdinalIgnoreCase);
        var ids = res.Json.GetArrayCI("permissions")
            .Where(p => forms.Contains(p.GetStringCI("formCode") ?? "")
                     && wanted.Contains(p.GetStringCI("actionCode") ?? ""))
            .Select(p => p.GetIntCI("permissionDefId"))
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
        {
            steps.Add(new FunctionalTestStep("İzin tanımlarını okuma", false,
                "Hedef formlar için VIEW/CREATE izin tanımı bulunamadı — PermissionDef kataloğu eksik olabilir."));
            return (false, Array.Empty<int>());
        }
        steps.Add(new FunctionalTestStep("İzin tanımlarını okuma", true, $"{ids.Count} izin tanımı çözüldü."));
        return (true, ids);
    }
}
