using System.Text.Json;

namespace CalibraHub.Web.Services.FunctionalTests.Scenarios;

/// <summary>
/// Veri Perdeleme (Satır Görünürlüğü) — bir kuralın satırı gerçekten GİZLEDİĞİNİ ve kural
/// kalkınca geri GELDİĞİNİ, üstelik SystemAdmin'in kuraldan muaf olduğunu doğrular.
///
/// Modelin sözleşmesi (SqlDataVisibilityFilter): "kısıtlanmayan izinlidir" — kurala TAKILAN
/// satır SystemAdmin dışında herkesten gizlenir. Sahiplik/grant kavramı yoktur.
///
/// Dört adım da ayrı ayrı gerekli, çünkü bu mekanizmanın kırılma biçimleri farklı:
///   1. Kural ÖNCESİ görünürlük  → testin kendisinin anlamlı olduğunun kanıtı. Bu adım
///      olmadan "kuraldan sonra görünmüyor" sonucu, satırın zaten hiç görünmediği
///      (yetki eksikliği, arama hatası) durumdan ayırt edilemezdi.
///   2. Kural SONRASI gizlenme   → asıl işlev.
///   3. SystemAdmin muafiyeti    → filtre yanlışlıkla admini de kısıtlarsa yönetici kendi
///      verisini göremez; sessiz ve can sıkıcı bir kırık.
///   4. Kural silinince geri gelme → kural iptali ANINDA etkili olmalı. Filtre 60 sn cache'li;
///      kaydetme/silme uçları cache'i temizlemezse kullanıcı bir dakika boyunca yanlış veri
///      görür. Bu adım tam olarak o temizliği sınar.
///
/// Sınama CONTACTS formu üzerinde: cari listesi bu filtreyi uygulayan repolardan biri ve
/// sınırlı yetkili kullanıcı için okunabilir bir liste ucu var.
/// </summary>
public sealed class DataVisibilityScenario : FunctionalTestScenarioBase
{
    private const string TargetForm = "CONTACTS";

    public override string Key => "DATA_VISIBILITY";
    public override string Group => "yetki";
    public override string Label => "Veri Perdeleme (Satır Görünürlüğü)";
    public override IReadOnlyList<string> DependsOn => new[] { "PERM_SEED" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var client = ctx.Get<FunctionalTestHttpClient>(PermissionSeedScenario.UserClientKey);
        var userId = ctx.GetInt(PermissionSeedScenario.UserIdKey);
        if (client is null || userId <= 0)
        {
            Fail(steps, "Kullanıcı oturumu", "Yetki testi kullanıcısının oturumu bağlamda yok.");
            return;
        }

        // ── Sınırlı kullanıcıya cari GÖRÜNTÜLEME izni ver ──
        // Perdeleme, yetkiden BAĞIMSIZ bir katmandır: önce listeyi görebilmesi gerekir ki
        // "gizlendi mi" sorusu anlamlı olsun.
        var (mapOk, defMap) = await PermissionHelpers.ResolveDefMapAsync(
            ctx, steps, userId, new[] { TargetForm }, "VIEW", ct);
        if (!mapOk || !defMap.TryGetValue(TargetForm, out var viewDefId))
        {
            Fail(steps, "İzin tanımını çözümleme", $"'{TargetForm}' için VIEW izin tanımı bulunamadı.");
            return;
        }
        var (grantOk, _) = await StepPostAsync(ctx, steps, "Kullanıcıya cari görüntüleme izni", $"/Permission/User/{userId}/Save",
            new { userId, departmentId = (int?)null, items = new[] { new { permissionDefId = viewDefId, isGranted = true } } }, ct);
        if (!grantOk) return;

        // ── Perdelenecek cariyi oluştur ──
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var hiddenCode = $"DVGIZLI-{suffix}";
        var (cOk, _) = await StepPostAsync(ctx, steps, "Perdelenecek cariyi oluşturma", "/Finance/UpsertContact",
            new
            {
                id = (int?)null, accountType = (byte)3, accountCode = hiddenCode,
                accountTitle = $"Perdeleme Testi Cari {suffix}", taxNumber = (string?)null,
                identityNumber = (string?)null, taxOffice = (string?)null, phone = (string?)null,
                email = (string?)null, address = (string?)null, city = (string?)null, district = (string?)null,
                isActive = true, priceGroupId = (int?)null,
            }, ct);
        if (!cOk) return;

        // ── 1) Kural ÖNCESİ: sınırlı kullanıcı cariyi GÖREBİLMELİ ──
        var beforeVisible = await UserSeesContactAsync(client, hiddenCode, ct);
        if (!beforeVisible)
        {
            Fail(steps, "Kural öncesi görünürlük",
                "Sınırlı kullanıcı cariyi kural KONULMADAN da göremedi — sonraki adımlar anlamsız olurdu " +
                "(yetki ya da liste sorgusu kaynaklı olabilir).");
            return;
        }
        Pass(steps, "Kural öncesi görünürlük", "Cari, kural yokken görünüyor.");

        // ── Perdeleme kuralı: AccountCode = <kod> olan satırları gizle ──
        var (ruleOk, ruleJson) = await StepPostAsync(ctx, steps, "Perdeleme kuralı oluşturma", "/Admin/SaveDataVisibilityRule",
            new
            {
                id = 0,
                name = $"Fonksiyon Testi Perdeleme {suffix}",
                formCode = TargetForm,
                fieldKind = 0,              // 0 = Column (entity tablosunun gerçek kolonu)
                fieldKey = "AccountCode",
                @operator = "eq",
                widgetId = (int?)null,
                isActive = true,
                values = new[] { hiddenCode },
                grantUserIds = Array.Empty<int>(),
                grantDeptIds = Array.Empty<int>(),
            }, ct);
        if (!ruleOk) return;
        var ruleId = ruleJson.GetIntCI("id");

        // ── 2) Kural SONRASI: sınırlı kullanıcı GÖRMEMELİ ──
        var afterHidden = await UserSeesContactAsync(client, hiddenCode, ct);
        if (afterHidden)
        {
            Fail(steps, "Kural sonrası gizlenme",
                "Perdeleme kuralı tanımlandığı hâlde satır sınırlı kullanıcıya hâlâ görünüyor — " +
                "kural uygulanmıyor ya da filtre bu sorguya bağlanmamış.");
            await CleanupAsync(ctx, ruleId, ct);
            return;
        }
        Pass(steps, "Kural sonrası gizlenme", "Satır sınırlı kullanıcıdan gizlendi.");

        // ── 3) SystemAdmin muafiyeti ──
        var adminSees = await AdminSeesContactAsync(ctx, hiddenCode, ct);
        if (!adminSees)
        {
            Fail(steps, "SystemAdmin muafiyeti",
                "Perdeleme SystemAdmin'i de kısıtladı — yönetici kendi verisini göremez hâle gelir.");
            await CleanupAsync(ctx, ruleId, ct);
            return;
        }
        Pass(steps, "SystemAdmin muafiyeti", "Yönetici satırı görmeye devam ediyor.");

        // ── 4) Kural kalkınca geri gelme (cache temizliği) ──
        if (ruleId <= 0)
        {
            Fail(steps, "Kuralı kaldırma", "Sunucu kural Id'si döndürmedi — kural silinemez, temizlik yapılamıyor.");
            return;
        }
        var (delOk, _) = await StepPostAsync(ctx, steps, "Kuralı kaldırma",
            $"/Admin/DeleteDataVisibilityRule?id={ruleId}", null, ct);
        if (!delOk) return;

        var restored = await UserSeesContactAsync(client, hiddenCode, ct);
        if (!restored)
        {
            Fail(steps, "Kural kalkınca geri gelme",
                "Kural silindiği hâlde satır hâlâ gizli — görünürlük önbelleği temizlenmiyor olabilir " +
                "(filtre 60 sn cache'li; iptal ANINDA etkili olmalı).");
            return;
        }
        Pass(steps, "Kural kalkınca geri gelme", "Satır yeniden görünür oldu.");
    }

    /// <summary>Sınırlı kullanıcı, verilen koda sahip cariyi listede görüyor mu.</summary>
    private static async Task<bool> UserSeesContactAsync(
        FunctionalTestHttpClient client, string code, CancellationToken ct)
    {
        var res = await client.GetAsync($"/Finance/GetContactsPage?page=1&pageSize=50&search={Uri.EscapeDataString(code)}", ct);
        return res.Ok && ContainsCode(res.Json, code);
    }

    /// <summary>Yönetici (testi çalıştıran oturum) aynı cariyi görüyor mu.</summary>
    private static async Task<bool> AdminSeesContactAsync(
        FunctionalTestContext ctx, string code, CancellationToken ct)
    {
        var res = await ctx.Http.GetAsync($"/Finance/GetContactsPage?page=1&pageSize=50&search={Uri.EscapeDataString(code)}", ct);
        return res.Ok && ContainsCode(res.Json, code);
    }

    /// <summary>
    /// Yanıt biçimine bağımlı kalmamak için kodu HAM JSON içinde arar — liste ucu
    /// entities/rows/accounts gibi farklı sarmalayıcılar kullanabilir ve alan adı
    /// (accountCode / subtitle / code) sürüme göre değişebilir.
    /// </summary>
    private static bool ContainsCode(JsonElement json, string code)
    {
        try
        {
            return json.GetRawText().Contains(code, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static async Task CleanupAsync(FunctionalTestContext ctx, int ruleId, CancellationToken ct)
    {
        // Test yarıda kalsa bile kural ORTAMDA KALMAMALI — kalırsa sonraki senaryolar ve
        // ekranda çalışan kullanıcı için cari sessizce görünmez olur.
        if (ruleId > 0)
            await ctx.Http.PostAsync($"/Admin/DeleteDataVisibilityRule?id={ruleId}", null, ct);
    }
}
