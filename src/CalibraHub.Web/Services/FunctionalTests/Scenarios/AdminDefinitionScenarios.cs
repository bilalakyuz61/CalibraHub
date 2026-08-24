using System.Text.Json;

namespace CalibraHub.Web.Services.FunctionalTests.Scenarios;

/// <summary>
/// Departman Tanımlama — departman açar, listede göründüğünü ve adının kaydedildiğini
/// doğrular. Ürettiği Id onay akışı senaryosunun girdisidir (departman-onaycılı adım).
///
/// Kod alanı YOK: departmanda benzersizlik ad üzerinden sağlanır (CLAUDE.md "Kullanıcı
/// tarafından girilen kod alanı yok" kuralı) — senaryo bu yüzden yalnız Name gönderir.
/// </summary>
public sealed class DepartmentDefineScenario : FunctionalTestScenarioBase
{
    public const string DepartmentIdKey = "departmentId";
    public const string DepartmentNameKey = "departmentName";

    public override string Key => "DEPARTMENT_DEFINE";
    public override string Group => "ticari";
    public override string Label => "Departman Tanımlama";
    public override IReadOnlyList<string> DependsOn => new[] { "SEED_MASTER_DATA" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var name = $"Fonksiyon Test Departman {suffix}";

        var (saveOk, _) = await StepPostAsync(ctx, steps, "Departman oluşturma", "/Admin/SaveDepartmentJson",
            new { companyId = (int?)null, name }, ct);
        if (!saveOk) return;

        // Uç Id döndürmüyor → listeden ada göre bulunur (kaydın gerçekten oluştuğunun kanıtı).
        var (listOk, listJson) = await StepGetAsync(ctx, steps, "Departmanı listede doğrulama",
            $"/Admin/GetDepartmentsJson?search={Uri.EscapeDataString(suffix)}", ct);
        if (!listOk) return;
        var row = listJson.AsArray()
            .FirstOrDefault(d => string.Equals(d.GetStringCI("name"), name, StringComparison.OrdinalIgnoreCase));
        if (row.ValueKind != JsonValueKind.Object)
        {
            Fail(steps, "Departmanı listede doğrulama", $"'{name}' departmanı listede bulunamadı.");
            return;
        }
        var departmentId = row.GetIntCI("id");
        if (departmentId <= 0) { Fail(steps, "Departmanı listede doğrulama", "Departman Id'si okunamadı."); return; }
        if (row.GetBoolCI("isActive") != true)
        {
            Fail(steps, "Departman durum doğrulama", "Yeni departman pasif oluşturuldu (aktif bekleniyordu).");
            return;
        }
        ctx.Set(DepartmentIdKey, departmentId);
        ctx.Set(DepartmentNameKey, name);
        Pass(steps, "Departman durum doğrulama", $"Departman #{departmentId} aktif.");

        // Ad benzersizliği: aynı adla ikinci kayıt REDDEDİLMELİ. Bu kural sessizce kırılırsa
        // aynı isimli iki departman oluşur ve onay akışı hangisine gideceğini bilemez.
        var dupRes = await ctx.Http.PostAsync("/Admin/SaveDepartmentJson", new { companyId = (int?)null, name }, ct);
        if (dupRes.Ok)
        {
            Fail(steps, "Ad benzersizliği doğrulama", "Aynı isimli ikinci departman kabul edildi (reddedilmeliydi).");
            return;
        }
        Pass(steps, "Ad benzersizliği doğrulama", "Aynı isimli ikinci kayıt reddedildi.");
    }
}

/// <summary>
/// Onay Akışı Tanımlama — iki adımlı (departman onayı → yönetici onayı) bir onay akışı
/// kurar ve akışın gerçekten EŞLEŞTİĞİNİ doğrular.
///
/// Doğrulama neden Match üzerinden: akış kaydı "başarılı" dönse bile kural (RuleType) yanlış
/// yazılmışsa belge onaya hiç düşmez — bu sessiz kırık ancak eşleme sorgusuyla yakalanır.
/// Kural MinAmount=1000: 5.000 TL eşleşmeli, 100 TL eşleşmemeli (iki yönlü kontrol).
/// </summary>
public sealed class ApprovalFlowDefineScenario : FunctionalTestScenarioBase
{
    public const string FlowIdKey = "approvalFlowId";
    private const decimal MinAmount = 1000m;

    public override string Key => "APPROVAL_FLOW_DEFINE";
    public override string Group => "ticari";
    public override string Label => "Onay Akışı Tanımlama";
    public override IReadOnlyList<string> DependsOn => new[] { "DEPARTMENT_DEFINE" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var departmentId = ctx.GetInt(DepartmentDefineScenario.DepartmentIdKey);
        var departmentName = ctx.GetString(DepartmentDefineScenario.DepartmentNameKey);
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var flowName = $"Fonksiyon Test Onay Akışı {suffix}";

        // DocumentKind = belge tipi kodu; satış teklifi üzerinden sınanır.
        const string documentKind = "satis_teklifi";

        var (saveOk, saveJson) = await StepPostAsync(ctx, steps, "Onay akışı kaydetme", "/ApprovalFlow/Save",
            new
            {
                id = 0, name = flowName, description = "Fonksiyon testi — 2 adımlı onay akışı",
                documentKind, isActive = true,
                rules = new object[]
                {
                    new { id = 0, ruleType = "MinAmount", ruleValue = MinAmount.ToString(System.Globalization.CultureInfo.InvariantCulture), isActive = true },
                },
                steps = new object[]
                {
                    new { id = 0, stepOrder = 1, stepName = "Departman Onayı", approverType = "Department", approverId = departmentId.ToString(), approverLabel = departmentName, isActive = true, nodeType = "step", posX = 0, posY = 0, nodeData = (string?)null },
                    new { id = 0, stepOrder = 2, stepName = "Yönetici Onayı", approverType = "AnyUser", approverId = (string?)null, approverLabel = "Herhangi bir kullanıcı", isActive = true, nodeType = "step", posX = 0, posY = 160, nodeData = (string?)null },
                },
                edges = Array.Empty<object>(),
                variables = Array.Empty<object>(),
            }, ct);
        if (!saveOk) return;
        var flowId = saveJson.GetIntCI("id");
        if (flowId <= 0) { Fail(steps, "Onay akışı kaydetme", "Sunucu akış Id'si döndürmedi."); return; }
        ctx.Set(FlowIdKey, flowId);

        // Eşleşme (pozitif): tutar eşiğin üstünde → bu akış bulunmalı, 2 aktif adımı olmalı.
        var (matchOk, matchJson) = await StepGetAsync(ctx, steps, "Akış eşleşmesi (5.000 TL)",
            $"/ApprovalFlow/Match?kind={documentKind}&amount=5000&departmentId={departmentId}", ct);
        if (!matchOk) return;
        if (matchJson.GetBoolCI("matched") != true)
        {
            Fail(steps, "Akış eşleşmesi (5.000 TL)", "Eşik üstü tutarda akış eşleşmedi — belge onaya hiç düşmez.");
            return;
        }
        var stepCount = matchJson.GetIntCI("stepCount");
        if (stepCount != 2)
        {
            Fail(steps, "Adım sayısı doğrulama", $"Eşleşen akışta {stepCount} aktif adım var (2 bekleniyordu).");
            return;
        }
        Pass(steps, "Adım sayısı doğrulama", $"Akış #{matchJson.GetIntCI("flowId")}, 2 adım.");

        // Eşleşme (negatif): eşik altı tutarda BU akış dönmemeli. Ortamda başka bir akış
        // eşleşebileceği için "matched=false" değil, "dönen akış bu değil" aranır.
        var (lowOk, lowJson) = await StepGetAsync(ctx, steps, "Eşik altı tutarda eşleşmeme (100 TL)",
            $"/ApprovalFlow/Match?kind={documentKind}&amount=100&departmentId={departmentId}", ct);
        if (!lowOk) return;
        if (lowJson.GetBoolCI("matched") == true && lowJson.GetIntCI("flowId") == flowId)
        {
            Fail(steps, "Eşik altı tutarda eşleşmeme (100 TL)",
                $"MinAmount={MinAmount} kuralına rağmen 100 TL'de de bu akış eşleşti — kural uygulanmıyor.");
            return;
        }
        Pass(steps, "Eşik altı tutarda eşleşmeme (100 TL)", "Kural doğru uygulanıyor.");
    }
}

/// <summary>
/// Belge Dizayn Tanımlama — Belge Tasarımcısı (DocDesigner) şablonu kaydeder ve geri okur.
/// Kaydetme PUT ile yapılır (uç: PUT /api/doc-designer/layouts); şablonun listede görünmesi
/// + detayında sayfa ölçüleri ve düzen JSON'unun korunması doğrulanır.
/// </summary>
public sealed class DocLayoutDefineScenario : FunctionalTestScenarioBase
{
    public const string LayoutIdKey = "docLayoutId";

    public override string Key => "DOC_LAYOUT_DEFINE";
    public override string Group => "ticari";
    public override string Label => "Belge Dizayn (Şablon) Tanımlama";
    public override IReadOnlyList<string> DependsOn => new[] { "SEED_MASTER_DATA" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var code = $"FNDL-{suffix}";
        var name = $"Fonksiyon Test Belge Dizaynı {suffix}";

        // Minimal ama gerçek bir düzen: A4 dikey, tek başlık metni. Tasarımcının kendi
        // şemasıyla birebir aynı olması gerekmiyor — kaydetme/okuma yuvarlağı sınanıyor.
        var layoutJson = JsonSerializer.Serialize(new
        {
            version = 1,
            elements = new object[]
            {
                new { id = "t1", type = "text", x = 20, y = 20, w = 200, h = 24, text = "Fonksiyon Testi", fontSize = 14 },
            },
        });

        var (saveOk, _) = await StepPutAsync(ctx, steps, "Belge dizaynı kaydetme", "/api/doc-designer/layouts",
            new
            {
                id = 0, code, name, docType = (string?)null,
                description = "Fonksiyon testi — belge şablonu",
                layoutJson,
                pageW = 210m, pageH = 297m,
                marginTop = 10m, marginBot = 10m, marginLeft = 10m, marginRight = 10m,
                isDefault = false,
                dataSources = Array.Empty<object>(),
                documentTypeId = (int?)null, outputFormat = "pdf",
            }, ct);
        if (!saveOk) return;

        var (listOk, listJson) = await StepGetAsync(ctx, steps, "Şablonu listede doğrulama", "/api/doc-designer/layouts", ct);
        if (!listOk) return;
        var row = listJson.AsArray()
            .FirstOrDefault(l => string.Equals(l.GetStringCI("code"), code, StringComparison.OrdinalIgnoreCase));
        if (row.ValueKind != JsonValueKind.Object)
        {
            Fail(steps, "Şablonu listede doğrulama", $"'{code}' kodlu şablon listede bulunamadı.");
            return;
        }
        var layoutId = row.GetIntCI("id");
        if (layoutId <= 0) { Fail(steps, "Şablonu listede doğrulama", "Şablon Id'si okunamadı."); return; }
        ctx.Set(LayoutIdKey, layoutId);

        var (detailOk, detailJson) = await StepGetAsync(ctx, steps, "Şablon detayını geri okuma",
            $"/api/doc-designer/layouts/{layoutId}", ct);
        if (!detailOk) return;
        var pageW = detailJson.GetDecimalCI("pageW");
        var pageH = detailJson.GetDecimalCI("pageH");
        var savedLayout = detailJson.GetStringCI("layoutJson") ?? "";
        if (Math.Abs(pageW - 210m) > 0.0001m || Math.Abs(pageH - 297m) > 0.0001m)
        {
            Fail(steps, "Sayfa ölçüsü doğrulama", $"Sayfa {pageW}×{pageH} mm (210×297 bekleniyordu).");
            return;
        }
        if (!savedLayout.Contains("Fonksiyon Testi", StringComparison.Ordinal))
        {
            Fail(steps, "Düzen içeriği doğrulama", "Kaydedilen düzen JSON'u geri okumada içeriğini kaybetmiş.");
            return;
        }
        Pass(steps, "Şablon doğrulama", $"Şablon #{layoutId} ({pageW}×{pageH} mm), düzen korundu.");
    }
}
