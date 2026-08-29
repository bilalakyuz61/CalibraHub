using System.Diagnostics;
using System.Net;
using System.Text.Json;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Security;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using CalibraHub.Web.Models.Diagnostics;
using CalibraHub.Web.Models.Navigation;
using CalibraHub.Web.Services;
using CalibraHub.Web.Services.FunctionalTests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// 2026-05-26 — Sistem Saglik Kontrolu sayfasi.
/// Tum menu URL'lerini server-side HttpClient ile tek tek dener,
/// HTTP status + sure + hata mesajini doner. Frontend tablo halinde gosterir.
/// </summary>
[Authorize]
[CalibraHub.Web.Authorization.PermissionScope(CalibraHub.Application.Constants.FormCodes.SetupDefinitions)]
public sealed class HealthCheckController : Controller
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<HealthCheckController> _logger;
    private readonly SchemaProbeService _schemaProbe;
    private readonly IAdminManagementService _adminManagement;
    private readonly ICompanyRepository _companyRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly CalibraDatabaseInitializer _dbInitializer;
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly CalibraHub.Application.Abstractions.Persistence.IDocumentTypeRepository _documentTypeRepo;
    private readonly string _schema;

    private readonly Microsoft.AspNetCore.Mvc.Infrastructure.IActionDescriptorCollectionProvider _actionProvider;
    // Şifreleme anahtarı kontrolü eski (ContentRoot altındaki) konumu da raporlar.
    private readonly IWebHostEnvironment _hostEnvironment;
    private readonly CalibraHub.Persistence.Database.SchemaInitStatusStore _schemaStatus;

    public HealthCheckController(
        IHttpClientFactory httpFactory,
        IHttpContextAccessor httpContextAccessor,
        ILogger<HealthCheckController> logger,
        SchemaProbeService schemaProbe,
        IAdminManagementService adminManagement,
        ICompanyRepository companyRepository,
        IDepartmentRepository departmentRepository,
        CalibraDatabaseInitializer dbInitializer,
        SqlServerConnectionFactory connectionFactory,
        CalibraHub.Application.Abstractions.Persistence.IDocumentTypeRepository documentTypeRepo,
        Microsoft.AspNetCore.Mvc.Infrastructure.IActionDescriptorCollectionProvider actionProvider,
        IWebHostEnvironment hostEnvironment,
        CalibraDatabaseOptions dbOptions,
        CalibraHub.Persistence.Database.SchemaInitStatusStore schemaStatus)
    {
        _schemaStatus = schemaStatus;
        _actionProvider = actionProvider;
        _hostEnvironment = hostEnvironment;
        _httpFactory = httpFactory;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        _schemaProbe = schemaProbe;
        _adminManagement = adminManagement;
        _companyRepository = companyRepository;
        _departmentRepository = departmentRepository;
        _dbInitializer = dbInitializer;
        _connectionFactory = connectionFactory;
        _documentTypeRepo = documentTypeRepo;
        _schema = string.IsNullOrWhiteSpace(dbOptions.Schema) ? "dbo" : dbOptions.Schema.Trim();
    }

    [HttpGet("/Admin/HealthCheck")]
    public IActionResult Index() => View();

    /// <summary>
    /// Tum menu URL'lerini iterate eder, her birine HTTP GET atar.
    /// Auth cookie'yi forward eder ki authenticated endpoint'ler 200 donsun.
    /// </summary>
    [HttpPost("/Admin/HealthCheck/Run")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Run(CancellationToken ct)
    {
        var (checks, client, baseUrl, cookieHeader) = PrepareRun();

        var results = new List<CheckResult>();
        foreach (var target in checks)
            results.Add(await RunSingleAsync(client, baseUrl, cookieHeader, target, ct));

        // Altyapı / Şema derinlik kontrolleri (menü URL smoke'unun kapsamadığı)
        await using (var infraConn = await TryOpenAsync(ct))
            foreach (var spec in BuildInfraSpecs())
                results.Add(await RunInfraAsync(spec, infraConn, ct));

        var summary = new
        {
            total      = results.Count,
            ok         = results.Count(r => r.Status == "ok"),
            redirect   = results.Count(r => r.Status == "redirect"),
            warn       = results.Count(r => r.Status == "warn"),
            error      = results.Count(r => r.Status == "error"),
            exception  = results.Count(r => r.Status == "exception"),
            durationMs = results.Sum(r => r.DurationMs),
        };

        return Json(new { ok = true, summary, results });
    }

    /// <summary>
    /// NDJSON streaming: her satir bir frame.
    /// Frame tipleri:
    ///   - { type:"start", total }
    ///   - { type:"checking", index, total, label, parentLabel, path }
    ///   - { type:"result",   index, total, result }
    ///   - { type:"done",     summary }
    /// Frontend her satiri parse edip "su an X kontrol ediliyor" gosterimini canli gunceller.
    /// </summary>
    [HttpPost("/Admin/HealthCheck/Stream")]
    [ValidateAntiForgeryToken]
    public async Task Stream(CancellationToken ct)
    {
        Response.ContentType = "application/x-ndjson; charset=utf-8";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no"; // nginx vb. proxy buffer'i devre disi
        Response.StatusCode = 200;

        var (checks, client, baseUrl, cookieHeader) = PrepareRun();
        var infraSpecs = BuildInfraSpecs();
        var total = checks.Count + infraSpecs.Count;
        var results = new List<CheckResult>(total);

        // Hazırlık konsolu (2026-08-27 kullanıcı isteği): form kontrolleri MEVCUT şirkette
        // koşar — test şirketi açılmaz. Buradaki adımlar bir şey KURMAZ, koşulacak ortamı
        // tespit edip gösterir; kullanıcı "hangi veritabanına bakıyorum" sorusunu ekranı
        // terk etmeden yanıtlayabilsin diye açık açık yazılır.
        await StreamCurrentEnvironmentAsync(checks.Count, infraSpecs.Count, ct);

        // items: arayüz kontrol edilecek TÜM satırları baştan "bekliyor" olarak çizsin diye
        // gönderilir; sonuçlar geldikçe satırlar yerinde renk değiştirir (2026-08-24 kullanıcı isteği).
        await WriteFrameAsync(new
        {
            type = "start",
            total,
            items = checks.Select((c, i) => new { index = i + 1, label = c.Label, parentLabel = c.ParentLabel, path = c.Path })
                .Concat(infraSpecs.Select((sp, i) => new { index = checks.Count + i + 1, label = sp.Label, parentLabel = (string?)sp.Group, path = (string?)null }))
                .ToArray(),
        }, ct);

        for (var i = 0; i < checks.Count; i++)
        {
            var target = checks[i];
            await WriteFrameAsync(new
            {
                type        = "checking",
                index       = i + 1,
                total,
                label       = target.Label,
                parentLabel = target.ParentLabel,
                path        = target.Path,
            }, ct);

            var result = await RunSingleAsync(client, baseUrl, cookieHeader, target, ct);

            // Schema probe: registry'de tanım varsa INSERT...ROLLBACK testi yap
            SchemaProbeResult? schemaProbe = null;
            var probeDef = SchemaProbeRegistry.Resolve(target.Path);
            if (probeDef != null)
            {
                schemaProbe = await _schemaProbe.ProbeAsync(probeDef, ct);
                result.SchemaProbe = schemaProbe;
            }

            results.Add(result);

            await WriteFrameAsync(new
            {
                type   = "result",
                index  = i + 1,
                total,
                result,
            }, ct);
        }

        // Altyapı / Şema derinlik kontrolleri — aynı canlı akışa devam
        await using (var infraConn = await TryOpenAsync(ct))
        {
            for (var j = 0; j < infraSpecs.Count; j++)
            {
                var spec = infraSpecs[j];
                await WriteFrameAsync(new
                {
                    type        = "checking",
                    index       = checks.Count + j + 1,
                    total,
                    label       = spec.Label,
                    parentLabel = spec.Group,
                    path        = "",
                }, ct);
                var result = await RunInfraAsync(spec, infraConn, ct);
                results.Add(result);
                await WriteFrameAsync(new
                {
                    type   = "result",
                    index  = checks.Count + j + 1,
                    total,
                    result,
                }, ct);
            }
        }

        await WriteFrameAsync(new
        {
            type    = "done",
            summary = new
            {
                total,
                ok         = results.Count(r => r.Status == "ok"),
                redirect   = results.Count(r => r.Status == "redirect"),
                warn       = results.Count(r => r.Status == "warn"),
                error      = results.Count(r => r.Status == "error"),
                exception  = results.Count(r => r.Status == "exception"),
                durationMs = results.Sum(r => r.DurationMs),
            },
        }, ct);
    }

    private async Task WriteFrameAsync(object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        await Response.WriteAsync(json + "\n", ct);
        await Response.Body.FlushAsync(ct);
    }

    // ── Altyapı / Şema derinlik kontrolleri ──────────────────────────
    // Menü-URL smoke'unun kapsamadığı: bağlantı, yazma yeteneği, çekirdek tablo/kolon
    // bütünlüğü ve seed sayımları. CheckResult olarak üretilir (ParentLabel = grup),
    // aynı JSON/NDJSON akışına eklenir; frontend değişikliği gerekmez.

    /// <summary>
    /// Varlığı denetlenen çekirdek tablolar. Yeni bir modül canlıya alındığında ANA tablosu
    /// buraya eklenir: tablo self-healing ensure adımında oluşturulamazsa ekran çalışmaz ve
    /// bu tarama olmadan sebep ancak kullanıcı hata aldığında anlaşılır.
    /// (2026-08-29: makine çizelgesi, rezervasyon, lot/seri, MRP, AR-GE görev, kalite,
    /// karşılama defteri, form davranışı ve view yönetimi modülleri eklendi.)
    /// </summary>
    private static readonly string[] CoreTables =
    {
        "Users", "Forms", "Location", "Items", "ItemLocation", "ItemDocumentLock",
        "Document", "DocumentLine", "Contact", "DecimalSetting", "PermissionDef", "ApprovalFlow",
        // Üretim planlama
        "MachineScheduleBlock", "MrpRun", "WorkOrderPeg",
        // Stok izlenebilirlik ve rezervasyon
        "Lot", "ItemSerial", "DocumentLineSerial", "StockReservation",
        // İhtiyaç karşılama · kit
        "DocumentLineFulfillment", "ItemKit",
        // Kalite · AR-GE
        "QualityInspection", "Capa", "ProjectTask",
        // Uyarlama katmanı
        "FormFieldBehavior", "ViewDefinition",
    };

    /// <summary>
    /// Sonradan ALTER ile eklenen, eksikliği ekranı sessizce bozan kolonlar. Bir kolonu
    /// buraya eklemek, "self-healing ensure çalıştı mı" sorusunu tek bakışta yanıtlar.
    /// </summary>
    private static readonly (string Table, string Column)[] CoreColumns =
    {
        ("Items", "MinStock"), ("ItemLocation", "MinStock"), ("DocumentLine", "BaseQuantity"),
        ("Document", "Status"), ("ItemDocumentLock", "DocType"),
        // 2026-08-29 eklenenler
        ("Users", "MustChangePassword"),        // zorunlu parola değişimi (giriş akışı buna bakar)
        ("Personnel", "IsMobilePinRequired"),   // mobilde PIN sorma tercihi
        ("Items", "WorkOrderSplitPolicy"),      // MRP iş emri bölme kuralı
    };

    private sealed record InfraSpec(
        string Key, string Label, string Group,
        Func<SqlConnection?, CancellationToken, Task<(string Status, string Detail)>> Run);

    /// <summary>
    /// Yetki kapısı OLMAYAN controller'lar — bilinçli istisnalar. Her biri ya kimlik
    /// doğrulamadan ÖNCE çalışır (giriş, ilk kurulum), ya kendi ayrı kapısını taşır
    /// (Sistem Yönetimi şifresi, operatör PIN'i), ya da yetki kavramı dışındadır.
    /// Buraya isim eklemek denetimi zayıflatır — yeni ekran eklerken listeye değil,
    /// action'a [PermissionScope] ekleyin.
    /// </summary>
    private static readonly HashSet<string> PermissionGateExempt = new(StringComparer.OrdinalIgnoreCase)
    {
        "Account",      // giriş/çıkış/token — oturum açılmadan çalışır
        "Setup",        // ilk kurulum sihirbazı — henüz kullanıcı yok
        "Gate",         // Sistem Yönetimi — kendi şifre katmanı
        "Home",         // karşılama/hata sayfaları
        "HealthCheck",  // bu ekranın kendisi (SystemAdmin menüsünden açılır)
        "MobileWarehouseApi", "MobileProductionApi", "MobileWidgetApi", // operatör PIN + kendi form kodu kontrolü
    };

    /// <summary>
    /// Yetki kapısı taraması — çalışma zamanında TÜM action'ları gezip
    /// <c>[PermissionScope]</c> taşımayanları raporlar.
    ///
    /// Neden gerekli: yetki filtresi OPT-IN çalışır (kapsam yoksa filtre geçer). Yani bir
    /// ekran/uç yanlışlıkla kapısız bırakıldığında hiçbir hata vermez, sessizce herkese
    /// açık kalır. Fonksiyon testleri yalnız sınadıkları ekranlarda bunu yakalayabilir;
    /// bu tarama ise istisnasız hepsini kapsar.
    ///
    /// Kaynak olarak kaynak kodu değil ÇALIŞMA ZAMANI action listesi kullanılır — dağıtılan
    /// derlemede gerçekte ne varsa o denetlenir.
    /// </summary>
    private (string Status, string Detail) ScanPermissionGates()
    {
        var actions = _actionProvider.ActionDescriptors.Items
            .OfType<Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor>()
            .ToList();

        var uncovered = new List<(string Controller, string Action, string Method)>();
        foreach (var a in actions)
        {
            if (PermissionGateExempt.Contains(a.ControllerName)) continue;

            var meta = a.EndpointMetadata;
            if (meta.OfType<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>().Any()) continue;
            if (meta.OfType<CalibraHub.Web.Authorization.PermissionScopeAttribute>().Any()) continue;
            if (meta.OfType<CalibraHub.Web.Authorization.PermissionScopeAnyAttribute>().Any()) continue;
            // Kapısı gövdede olan (ya da izne ihtiyaç duymayan) uçlar. Bu eleme OLMADAN
            // rapor 28 mutasyon diyordu ve 22'si aslında korumalıydı — gerçek bulgu
            // gürültüde kayboluyordu. Gerekçe özniteliğin içinde yazılıdır.
            if (meta.OfType<CalibraHub.Web.Authorization.PermissionGateReviewedAttribute>().Any()) continue;

            var method = meta.OfType<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()
                .SelectMany(m => (IEnumerable<string>)m.HttpMethods).FirstOrDefault() ?? "?";
            uncovered.Add((a.ControllerName, a.ActionName, method));
        }

        var total = actions.Count;
        if (uncovered.Count == 0)
            return ("ok", $"{total} action denetlendi — kapısız uç yok.");

        var mutations = uncovered.Count(u => !string.Equals(u.Method, "GET", StringComparison.OrdinalIgnoreCase));
        // Mutasyon yoksa okuma uçları tek başına "uyarı" sayılmaz: GET'ler ekran açar,
        // veri değiştirmez ve çoğu zaten sayfa seviyesinde korunur. Uyarıyı asıl riske
        // sakla — her denetimde kalıcı sarı bir satır görmek uyarıyı anlamsızlaştırır.
        if (mutations == 0)
            return ("ok",
                $"{total} action denetlendi — kapısız mutasyon yok " +
                $"({uncovered.Count} okuma ucu kapısız; bunlar veri değiştirmez).");

        var mutationList = uncovered
            .Where(u => !string.Equals(u.Method, "GET", StringComparison.OrdinalIgnoreCase))
            .Select(u => $"{u.Controller}.{u.Action}")
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Mutasyonların ADLARI verilir, yalnız sayısı değil: sayı "gidip bul" demektir,
        // ad ise doğrudan gidilecek yeri gösterir. Gövdesinde kapısı olanlar zaten
        // [PermissionGateReviewed] ile elendi — burada kalan gerçekten kapısızdır.
        return ("warn",
            $"{mutations} mutasyon ucu yetki kapısı taşımıyor: {string.Join(" · ", mutationList)}. " +
            $"(Ayrıca {uncovered.Count - mutations} okuma ucu kapısız; {total} action denetlendi.)");
    }

    private List<InfraSpec> BuildInfraSpecs()
    {
        const string gdb = "Altyapı / Veritabanı";
        const string gseed = "Altyapı / Seed";
        const string gsec = "Altyapı / Güvenlik";
        // Veri bütünlüğü: YALNIZCA SELECT COUNT — canlı sistemde de güvenle çalışır,
        // hiçbir şey yazmaz/düzeltmez. Amaç "bozulmuş veri VAR MI" sorusunu yanıtlamak;
        // düzeltme kararı insana aittir (otomatik onarım, sebebi bilinmeden veri kaybettirir).
        const string gint = "Veri Bütünlüğü";
        return new List<InfraSpec>
        {
            new("infra.permgate", "Yetki kapısı taraması", gsec, (conn, ct) => Task.FromResult(ScanPermissionGates())),

            // Şifreleme anahtarı — kaybı GERİ DÖNÜŞSÜZ ve SESSİZ (şifreli notlar açılamaz,
            // hata da düşmez; ekranda anlamsız karakter görünür). Bu yüzden varlığı her
            // sağlık kontrolünde teyit edilir, "yok" durumu HATA sayılır.
            new("infra.dpkeys", "Şifreleme anahtarı (Data Protection)", gsec, (conn, ct) =>
            {
                var stable = CalibraHub.Infrastructure.Security.DataProtectionKeyStore.GetStablePath();
                var legacy = CalibraHub.Infrastructure.Security.DataProtectionKeyStore
                    .GetLegacyPath(_hostEnvironment.ContentRootPath);
                var stableCount = CalibraHub.Infrastructure.Security.DataProtectionKeyStore.CountKeys(stable);
                var legacyCount = CalibraHub.Infrastructure.Security.DataProtectionKeyStore.CountKeys(legacy);

                if (stableCount > 0)
                    return Task.FromResult(("ok", $"{stableCount} anahtar · kalıcı konum: {stable}"));

                if (legacyCount > 0)
                    return Task.FromResult(("warn",
                        $"{legacyCount} anahtar YALNIZCA eski konumda: {legacy} — bu klasör güncellemede silinebilir. " +
                        "Uygulamayı yeniden başlatınca kalıcı konuma taşınır."));

                return Task.FromResult(("error",
                    "Hiç Data Protection anahtarı bulunamadı. Şifreli not içerikleri açılamaz — " +
                    "yedekten anahtar klasörünü geri yükleyin."));
            }),
            // Yarım göçmüş şema — açılışta bir şirketin migration'ı patlarsa startup DEVAM
            // eder (tek bozuk şirket uygulamayı engellemesin diye). O şirket normal
            // görünmeye devam ettiği için ariza ancak eksik kolon ilk kullanıldığında,
            // "Invalid column name" 500'ü olarak ortaya çıkar. Burada açıkça raporlanır.
            new("infra.schema", "Şirket şema göçü (açılış)", gsec, (conn, ct) =>
            {
                var broken = _schemaStatus.Snapshot();
                if (broken.Count == 0)
                    return Task.FromResult(("ok", "Tüm şirketlerin şema göçü başarılı."));

                var detail = string.Join(" · ", broken.Take(5).Select(kv => $"Şirket #{kv.Key}: {kv.Value}"));
                return Task.FromResult(("error",
                    $"{broken.Count} şirketin şeması yarım kaldı ve girişi kapatıldı. {detail}" +
                    (broken.Count > 5 ? " …" : "") +
                    " Sorun giderilip uygulama yeniden başlatılmalı."));
            }),
            // ── Veri bütünlüğü ────────────────────────────────────────────────
            // Kolon/tablo adları şemadan DOĞRULANDI (2026-08-25): Document PK'si küçük harf
            // "id", ItemFeatureMappings ÇOĞUL, FeatureValue kolonları küçük harf. Bu isimler
            // tahminle yazılsaydı sorgular "Invalid column name" verip sessiz catch'te kaybolurdu.
            new("data.orphan_docline", "Sahipsiz belge kalemi", gint, async (conn, ct) =>
            {
                if (conn is null) return ("error", "Bağlantı yok");
                if (!await ObjExistsAsync(conn, $"[{_schema}].[DocumentLine]", ct)) return ("skip", "DocumentLine yok");
                var n = await ScalarCountAsync(conn, $@"
                    SELECT COUNT(*) FROM [{_schema}].[DocumentLine] dl
                     WHERE NOT EXISTS (SELECT 1 FROM [{_schema}].[Document] d WHERE d.[id] = dl.[DocumentId])", ct);
                return n == 0
                    ? ("ok", "Her kalem bir belgeye bağlı.")
                    : ("error", $"{n} kalem, var olmayan bir belgeye işaret ediyor. Bu satırlar hiçbir ekranda görünmez ama stok/tutar toplamlarına karışabilir.");
            }),
            new("data.orphan_lineitem", "Kalemde kayıp malzeme", gint, async (conn, ct) =>
            {
                if (conn is null) return ("error", "Bağlantı yok");
                if (!await ObjExistsAsync(conn, $"[{_schema}].[DocumentLine]", ct)) return ("skip", "DocumentLine yok");
                var n = await ScalarCountAsync(conn, $@"
                    SELECT COUNT(*) FROM [{_schema}].[DocumentLine] dl
                     WHERE dl.[ItemId] IS NOT NULL
                       AND NOT EXISTS (SELECT 1 FROM [{_schema}].[Items] i WHERE i.[Id] = dl.[ItemId])", ct);
                return n == 0
                    ? ("ok", "Tüm kalemlerin malzemesi mevcut.")
                    : ("error", $"{n} kalemin malzeme kaydı yok — belge açılışında ad/birim çözülemez.");
            }),
            new("data.missing_combination", "Kombinasyonsuz varyant kalemi", gint, async (conn, ct) =>
            {
                if (conn is null) return ("error", "Bağlantı yok");
                if (!await ObjExistsAsync(conn, $"[{_schema}].[DocumentLine]", ct)) return ("skip", "DocumentLine yok");
                var n = await ScalarCountAsync(conn, $@"
                    SELECT COUNT(*) FROM [{_schema}].[DocumentLine] dl
                     INNER JOIN [{_schema}].[Items] i ON i.[Id] = dl.[ItemId]
                     WHERE i.[Combinations] = 1 AND dl.[CombinationId] IS NULL", ct);
                return n == 0
                    ? ("ok", "Varyantlı malzemelerin kalemlerinde kombinasyon dolu.")
                    : ("warn", $"{n} kalem kombinasyon takipli bir malzemeye ait ama kombinasyonu boş. " +
                               "Eski kayıtlar meşru olabilir; yeni kayıtlarda hangi varyantın satıldığı belirsizdir.");
            }),
            new("data.orphan_bomline", "Sahipsiz reçete kalemi", gint, async (conn, ct) =>
            {
                if (conn is null) return ("error", "Bağlantı yok");
                if (!await ObjExistsAsync(conn, $"[{_schema}].[BOMLine]", ct)) return ("skip", "BOMLine yok");
                var n = await ScalarCountAsync(conn, $@"
                    SELECT COUNT(*) FROM [{_schema}].[BOMLine] bl
                     WHERE NOT EXISTS (SELECT 1 FROM [{_schema}].[BOM] b WHERE b.[Id] = bl.[BOMId])", ct);
                return n == 0
                    ? ("ok", "Her reçete kalemi bir reçeteye bağlı.")
                    : ("error", $"{n} reçete kalemi, var olmayan bir reçeteye işaret ediyor.");
            }),
            new("data.bom_selfref", "Kendini içeren reçete", gint, async (conn, ct) =>
            {
                if (conn is null) return ("error", "Bağlantı yok");
                if (!await ObjExistsAsync(conn, $"[{_schema}].[BOMLine]", ct)) return ("skip", "BOMLine yok");
                var n = await ScalarCountAsync(conn, $@"
                    SELECT COUNT(*) FROM [{_schema}].[BOMLine] bl
                     INNER JOIN [{_schema}].[BOM] b ON b.[Id] = bl.[BOMId]
                     WHERE bl.[ItemId] = b.[ItemId]", ct);
                return n == 0
                    ? ("ok", "Hiçbir reçete kendini bileşen olarak içermiyor.")
                    : ("error", $"{n} reçete kaleminde bileşen, mamulün KENDİSİ. " +
                                "İhtiyaç patlatma ve maliyet hesabı sonsuz döngüye girer.");
            }),
            new("data.dup_base_bom", "Çift baz reçete", gint, async (conn, ct) =>
            {
                if (conn is null) return ("error", "Bağlantı yok");
                if (!await ObjExistsAsync(conn, $"[{_schema}].[BOM]", ct)) return ("skip", "BOM yok");
                // UX_BOM_Base bunu bugün ENGELLER; ama indeks eklenmeden önce oluşmuş veri
                // varsa indeks hiç kurulamamış olabilir — o durumda kopyalar hâlâ durur.
                var n = await ScalarCountAsync(conn, $@"
                    SELECT COUNT(*) FROM (
                        SELECT [ItemId], [ConfigId]
                          FROM [{_schema}].[BOM]
                         WHERE [IsActive] = 1 AND [VersionCode] IS NULL
                         GROUP BY [ItemId], [ConfigId]
                        HAVING COUNT(*) > 1) x", ct);
                return n == 0
                    ? ("ok", "Her mamulün tek aktif baz reçetesi var.")
                    : ("error", $"{n} mamulde birden fazla aktif baz reçete var — hangisinin kullanılacağı belirsiz.");
            }),
            new("data.orphan_featuremap", "Kayıp özellik/değer bağı", gint, async (conn, ct) =>
            {
                if (conn is null) return ("error", "Bağlantı yok");
                if (!await ObjExistsAsync(conn, $"[{_schema}].[ItemFeatureMappings]", ct)) return ("skip", "ItemFeatureMappings yok");
                var n = await ScalarCountAsync(conn, $@"
                    SELECT COUNT(*) FROM [{_schema}].[ItemFeatureMappings] m
                     WHERE m.[IsActive] = 1
                       AND (NOT EXISTS (SELECT 1 FROM [{_schema}].[ItemFeature] f WHERE f.[Id] = m.[FeatureId])
                            OR (m.[FeatureValueId] IS NOT NULL
                                AND NOT EXISTS (SELECT 1 FROM [{_schema}].[FeatureValue] v WHERE v.[id] = m.[FeatureValueId])))", ct);
                return n == 0
                    ? ("ok", "Stok-özellik bağlarının hepsi geçerli.")
                    : ("error", $"{n} stok-özellik bağı silinmiş bir özelliğe/değere işaret ediyor.");
            }),
            new("infra.conn", "Veritabanı bağlantısı", gdb, async (conn, ct) =>
            {
                if (conn is null) return ("error", "Bağlantı açılamadı");
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT DB_NAME(), CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(64));";
                await using var r = await cmd.ExecuteReaderAsync(ct);
                if (await r.ReadAsync(ct))
                    return ("ok", $"DB: {(r.IsDBNull(0) ? "?" : r.GetString(0))} · SQL {(r.IsDBNull(1) ? "?" : r.GetString(1))}");
                return ("ok", "bağlı");
            }),
            new("infra.write", "Yazma yeteneği (geçici tablo)", gdb, async (conn, ct) =>
            {
                if (conn is null) return ("error", "Bağlantı yok");
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "CREATE TABLE #hc_probe(x INT); INSERT INTO #hc_probe VALUES(1); SELECT COUNT(*) FROM #hc_probe; DROP TABLE #hc_probe;";
                var n = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct) ?? 0);
                return n == 1 ? ("ok", "yaz/oku başarılı — gerçek veri etkilenmez") : ("error", $"beklenen 1, gelen {n}");
            }),
            new("infra.tables", "Çekirdek tablolar", gdb, async (conn, ct) =>
            {
                if (conn is null) return ("error", "Bağlantı yok");
                var missing = new List<string>();
                foreach (var t in CoreTables)
                    if (!await ObjExistsAsync(conn, $"[{_schema}].[{t}]", ct)) missing.Add(t);
                return missing.Count == 0
                    ? ("ok", $"{CoreTables.Length}/{CoreTables.Length} mevcut")
                    : ("error", $"EKSİK ({missing.Count}): {string.Join(", ", missing)}");
            }),
            new("infra.columns", "Kritik kolonlar", gdb, async (conn, ct) =>
            {
                if (conn is null) return ("error", "Bağlantı yok");
                var missing = new List<string>();
                foreach (var (t, c) in CoreColumns)
                    if (!await ColExistsAsync(conn, $"[{_schema}].[{t}]", c, ct)) missing.Add($"{t}.{c}");
                return missing.Count == 0
                    ? ("ok", $"{CoreColumns.Length}/{CoreColumns.Length} mevcut")
                    : ("error", $"EKSİK: {string.Join(", ", missing)} — self-healing ensure devreye girmeli");
            }),
            new("seed.users", "Kullanıcı kaydı", gseed, async (conn, ct) =>
            {
                var n = await CountAsync(conn, "Users", ct);
                return n < 0 ? ("error", "okunamadı") : n > 0 ? ("ok", $"{n} kayıt") : ("error", "en az bir admin bekleniyor");
            }),
            new("seed.forms", "Form tanımı (seed)", gseed, async (conn, ct) =>
            {
                var n = await CountAsync(conn, "Forms", ct);
                return n < 0 ? ("error", "okunamadı") : n > 0 ? ("ok", $"{n} kayıt") : ("warn", "form seed eksik");
            }),
            new("seed.perms", "İzin tanımı (seed)", gseed, async (_, ct) =>
            {
                // PermissionDef SİSTEM (master) DB'de yaşar — SqlPermissionDefRepository tüm
                // sorgularını OpenSystemConnectionAsync ile açar, PermissionDefDiscoveryService
                // de oraya upsert eder. Şirket DB'sinde tablo yalnızca şema-ensure nedeniyle
                // VAR ama HER ZAMAN BOŞTUR; burada company connection'ını saymak kalıcı ve
                // yanıltıcı bir "izin seed eksik" uyarısı üretiyordu (2026-08-23 düzeltme).
                try
                {
                    await using var sysConn = await _connectionFactory.OpenSystemConnectionAsync(ct);
                    var n = await CountAsync(sysConn, "PermissionDef", ct);
                    return n < 0 ? ("error", "okunamadı")
                         : n > 0 ? ("ok", $"{n} kayıt (sistem DB)")
                         : ("warn", "izin seed eksik — PermissionDefDiscoveryService çalışmamış olabilir");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[HealthCheck] PermissionDef sayımı (sistem DB) başarısız.");
                    return ("error", "sistem DB okunamadı");
                }
            }),
            new("seed.decimal", "Ondalık ayarı", gseed, async (conn, ct) =>
            {
                var n = await CountAsync(conn, "DecimalSetting", ct);
                return n < 0 ? ("error", "okunamadı") : ("ok", n > 0 ? $"{n} kayıt" : "kayıt yok (varsayılanlar devrede)");
            }),
        };
    }

    private async Task<CheckResult> RunInfraAsync(InfraSpec spec, SqlConnection? conn, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        string status = "ok", detail = "";
        try { (status, detail) = await spec.Run(conn, ct); }
        catch (Exception ex) { status = "exception"; detail = ex.Message.Length > 200 ? ex.Message[..200] : ex.Message; }
        sw.Stop();
        // View, errorSnippet'i kırmızı stille gösterir → sadece gerçek hata detayı oraya düşsün.
        // ok/uyarı detayı etiket satırına eklenir (yeşil/amber satır temiz kalır).
        var problem = status is "error" or "exception";
        return new CheckResult
        {
            Key = spec.Key,
            Label = (!problem && !string.IsNullOrWhiteSpace(detail)) ? $"{spec.Label} — {detail}" : spec.Label,
            Path = "",
            ParentLabel = spec.Group,
            Status = status,
            DurationMs = (int)sw.ElapsedMilliseconds,
            ErrorSnippet = problem && !string.IsNullOrWhiteSpace(detail) ? detail : null,
        };
    }

    /// <summary>
    /// Form kontrollerinden ÖNCE çalışan hazırlık konsolu: koşulacak ortamı tespit edip
    /// adım adım yayınlar. Hiçbir şey KURMAZ — mevcut şirkette çalışıldığı için kurulacak
    /// bir şey yoktur; amaç "hangi şirket, hangi veritabanı, kaç ekran" sorularını
    /// kullanıcının ekranı terk etmeden yanıtlayabilmesi.
    ///
    /// Her adım kendi bulgusunu (<c>detail</c>) taşır; boş bir ilerleme çubuğu yerine
    /// gerçekten keşfedilen bilgi gösterilir.
    /// </summary>
    private async Task StreamCurrentEnvironmentAsync(int screenCount, int infraCount, CancellationToken ct)
    {
        var labels = new[]
        {
            "Oturum doğrulanıyor",
            "Şirket çözümleniyor",
            "Veritabanı bağlantısı açılıyor",
            "Denetlenecek ekranlar toplanıyor",
        };
        await WriteFrameAsync(new { type = "setup_start", total = labels.Length, labels, mode = "current" }, ct);

        // 1) Oturum
        var userEmail = User.Identity?.Name ?? "(bilinmiyor)";
        var roleName  = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "(rol yok)";
        await WriteFrameAsync(new
        {
            type = "setup_step", step = 1, total = labels.Length, message = labels[0],
            detail = $"{userEmail} · rol: {roleName}",
        }, ct);

        // 2) Şirket
        int.TryParse(User.FindFirst("company_id")?.Value, out var companyId);
        var company = companyId > 0 ? await _companyRepository.GetByIdAsync(companyId, ct) : null;
        var companyName = company?.Name ?? (companyId > 0 ? $"#{companyId}" : "(şirket claim'i yok)");
        await WriteFrameAsync(new
        {
            type = "setup_step", step = 2, total = labels.Length, message = labels[1],
            detail = companyId > 0 ? $"{companyName} (#{companyId})" : companyName,
        }, ct);

        // 3) Veritabanı — ad + sunucu + gerçekten açılabildi mi (süresiyle)
        var sw = Stopwatch.StartNew();
        string dbDetail;
        var dbOk = false;
        await using (var probe = await TryOpenAsync(ct))
        {
            sw.Stop();
            if (probe is null)
            {
                dbDetail = "Bağlantı AÇILAMADI — altyapı kontrolleri boş dönebilir";
            }
            else
            {
                dbOk = true;
                dbDetail = $"{probe.Database} @ {probe.DataSource} · şema {_schema} · {sw.ElapsedMilliseconds} ms";
            }
        }
        await WriteFrameAsync(new
        {
            type = "setup_step", step = 3, total = labels.Length, message = labels[2],
            detail = dbDetail, warn = !dbOk,
        }, ct);

        // 4) Kapsam
        await WriteFrameAsync(new
        {
            type = "setup_step", step = 4, total = labels.Length, message = labels[3],
            detail = $"{screenCount} menü ekranı + {infraCount} altyapı kontrolü = {screenCount + infraCount} kontrol",
        }, ct);

        await WriteFrameAsync(new
        {
            type = "setup_done", mode = "current",
            companyName, userEmail,
            databaseName = dbOk ? dbDetail : null,
            testCompanyId = (int?)null,
        }, ct);
    }

    private async Task<SqlConnection?> TryOpenAsync(CancellationToken ct)
    {
        try { return await _connectionFactory.OpenConnectionAsync(ct); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[HealthCheck] Altyapı bağlantısı açılamadı.");
            return null;
        }
    }

    // StreamTestCompany için: test şirketinin connection string'i ile aç (mevcut şirket değil)
    private static async Task<SqlConnection?> TryOpenConnStrAsync(string? connStr, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(connStr)) return null;
        try { var c = new SqlConnection(connStr); await c.OpenAsync(ct); return c; }
        catch { return null; }
    }

    /// <summary>
    /// Salt-okunur sayım. Bütünlük kontrollerinin TEK yazma-dışı aracı — buradan geçmeyen
    /// bir SQL bu gruba eklenmemeli (canlıda çalışacağı garantisi buna dayanıyor).
    /// </summary>
    private static async Task<int> ScalarCountAsync(SqlConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var raw = await cmd.ExecuteScalarAsync(ct);
        return raw is null || raw is DBNull ? 0 : Convert.ToInt32(raw);
    }

    private static async Task<bool> ObjExistsAsync(SqlConnection conn, string objName, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT CASE WHEN OBJECT_ID(@n, N'U') IS NOT NULL THEN 1 ELSE 0 END;";
        cmd.Parameters.Add(new SqlParameter("@n", objName));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct) ?? 0) == 1;
    }

    private static async Task<bool> ColExistsAsync(SqlConnection conn, string objName, string col, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT CASE WHEN COL_LENGTH(@n, @c) IS NOT NULL THEN 1 ELSE 0 END;";
        cmd.Parameters.Add(new SqlParameter("@n", objName));
        cmd.Parameters.Add(new SqlParameter("@c", col));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct) ?? 0) == 1;
    }

    private async Task<int> CountAsync(SqlConnection? conn, string table, CancellationToken ct)
    {
        if (conn is null) return -1;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(1) FROM [{_schema}].[{table}];";
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct) ?? 0);
        }
        catch { return -1; }
    }

    private (List<CheckTarget> Checks, HttpClient Client, string BaseUrl, string CookieHeader) PrepareRun()
    {
        var checks = BuildCheckList();
        var req = _httpContextAccessor.HttpContext!.Request;
        var baseUrl = $"{req.Scheme}://{req.Host}";
        var cookieHeader = string.Join("; ", req.Cookies.Select(c => $"{c.Key}={c.Value}"));
        var client = _httpFactory.CreateClient("health-check");
        client.Timeout = TimeSpan.FromSeconds(15);
        return (checks, client, baseUrl, cookieHeader);
    }

    private List<CheckTarget> BuildCheckList()
    {
        var isAdmin = string.Equals(User.Identity?.Name, "admin@calibra.local", StringComparison.OrdinalIgnoreCase);
        var menu = MenuDefinition.GetMainMenu(isAdmin);
        var checks = new List<CheckTarget>();
        FlattenMenu(menu, null, checks);
        return checks;
    }

    private async Task<CheckResult> RunSingleAsync(
        HttpClient client, string baseUrl, string cookieHeader, CheckTarget target, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var result = new CheckResult { Key = target.Key, Label = target.Label, Path = target.Path, ParentLabel = target.ParentLabel };
        try
        {
            using var msg = new HttpRequestMessage(HttpMethod.Get, baseUrl + target.Path);
            if (!string.IsNullOrEmpty(cookieHeader))
                msg.Headers.Add("Cookie", cookieHeader);
            msg.Headers.Add("Sec-Fetch-Dest", "iframe");

            using var resp = await client.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ct);
            sw.Stop();
            result.StatusCode = (int)resp.StatusCode;
            result.DurationMs = (int)sw.ElapsedMilliseconds;

            if ((int)resp.StatusCode == 200)
                result.Status = "ok";
            else if ((int)resp.StatusCode is 301 or 302 or 303 or 307 or 308)
                result.Status = "redirect";
            else if ((int)resp.StatusCode >= 500)
            {
                result.Status = "error";
                try
                {
                    var body = await resp.Content.ReadAsStringAsync(ct);
                    result.ErrorSnippet = ExtractErrorSnippet(body);
                }
                catch { /* ignore */ }
            }
            else
            {
                result.Status = "warn";
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.DurationMs = (int)sw.ElapsedMilliseconds;
            result.Status = "exception";
            result.ErrorSnippet = "İşlem sırasında bir hata oluştu.";
            _logger.LogError(ex, "[HealthCheck] {Path} kontrolü sırasında hata.", target.Path);
        }
        return result;
    }

    /// <summary>
    /// Test şirketi oluştur → test kullanıcısı oluştur → login → tüm formları test et.
    /// Her adım NDJSON stream olarak frontend'e iletilir.
    /// Frame tipleri: setup_start | setup_step | setup_done | setup_error | start | checking | result | done
    /// </summary>
    [HttpPost("/Admin/HealthCheck/StreamTestCompany")]
    [ValidateAntiForgeryToken]
    public async Task StreamTestCompany([FromQuery] bool createNewDb = false, CancellationToken ct = default)
    {
        // Varsayilan KAPALI (2026-08-27 kullanici karari): test sirketi mevcut veritabaninda
        // acilir, ayri DB kurulmaz. Acik birakilirsa her kosuda bir DB birikiyordu.
        //
        // DIKKAT — kapaliyken Fonksiyon Testleri CALISMAZ: StreamFunctionalTests'teki guvenlik
        // kilidi, test sirketi canli bir sirketle ayni veritabanini paylasiyorsa reddeder
        // (testler gercek belge/stok hareketi yazar). Kilit dogru; kalkmasi icin once
        // sorgulara CompanyId suzgeci girmeli. O tamamlanana kadar fonksiyon testi
        // calistiracaksaniz anahtari ACIN.
        Response.ContentType = "application/x-ndjson; charset=utf-8";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
        Response.StatusCode = 200;

        // Test sirketi adi: TEST_ddMMyyHHmm — YEREL saat.
        // Projede saklanan tarihler UTC'dir, ama bu bir AD; kullanicinin saatine uymali.
        // UtcNow ile uretilince Turkiye'de (UTC+3) adlar 3 saat geride cikiyordu ve
        // "az once olusturdugum sirket hangisi" sorusu cevapsiz kaliyordu.
        var now = DateTime.Now;
        var testCompanyName = $"TEST_{now:ddMMyy}{now:HHmm}";
        var testEmail       = $"test.hc.{now:ddMMyyHHmm}@calibra.test";
        var testPassword    = $"Hc!{Guid.NewGuid().ToString("N")[..8]}";

        // Adım listesi: createNewDb true ise DB oluşturma 1. adım olarak eklenir
        var stepTotal = createNewDb ? 4 : 3;
        var stepLabels = createNewDb
            ? new[] { "Veritabanı oluşturuluyor", "Test şirketi oluşturuluyor", "Test kullanıcısı oluşturuluyor", "Test oturumu başlatılıyor" }
            : new[] { "Test şirketi oluşturuluyor", "Test kullanıcısı oluşturuluyor", "Test oturumu başlatılıyor" };

        await WriteFrameAsync(new { type = "setup_start", total = stepTotal, labels = stepLabels, mode = "test" }, ct);

        // Mevcut şirketin DB bağlantısını al (template olarak kullanılır)
        int.TryParse(User.FindFirst("company_id")?.Value, out var currentCompanyId);
        var currentCompany  = currentCompanyId > 0
            ? await _companyRepository.GetByIdAsync(currentCompanyId, ct)
            : null;
        var connectionString = currentCompany?.DatabaseConnectionString;

        // Admin kullanıcısının company_id claim'i olmayabilir; createNewDb modunda
        // SQL Server adresi gerekiyor → şifre çözülmüş sistem DB connection string'ini kullan
        if (createNewDb && string.IsNullOrWhiteSpace(connectionString))
            connectionString = _connectionFactory.ResolveConnectionStringForCompany(0);

        // Adım offset: createNewDb'de ilk adım DB oluşturma
        var stepOffset = createNewDb ? 1 : 0;

        int testCompanyId;
        try
        {
            // [Opsiyonel] Adım 1: Yeni test veritabanı oluştur ve şemayı init et
            if (createNewDb)
            {
                await WriteFrameAsync(new { type = "setup_step", step = 1, total = stepTotal, message = stepLabels[0] }, ct);
                var newDbName = $"CalibraTest_{now:ddMMyyHHmm}";
                var (newConnStr, dbError) = await CreateTestDatabaseAsync(connectionString, newDbName, _logger, ct);
                if (dbError != null)
                {
                    await WriteFrameAsync(new { type = "setup_error", message = $"Veritabanı oluşturulamadı: {dbError}" }, ct);
                    return;
                }
                // Tam şema init (tüm Ensure* + Seed* metodları)
                await _dbInitializer.InitializeForConnectionAsync(newConnStr, ct);
                connectionString = newConnStr;
            }

            // Adım stepOffset+1: Test şirketi oluştur
            await WriteFrameAsync(new { type = "setup_step", step = 1 + stepOffset, total = stepTotal, message = stepLabels[stepOffset] }, ct);
            var taxNumber = $"TST-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
            testCompanyId = await _adminManagement.SaveCompanyAsync(
                new SaveCompanyRequest(null, testCompanyName, testCompanyName, "-", null, null, null, "-", taxNumber, false, true, connectionString),
                ct);

            // Adım stepOffset+2: Test departmanı + kullanıcısı oluştur
            await WriteFrameAsync(new { type = "setup_step", step = 2 + stepOffset, total = stepTotal, message = stepLabels[1 + stepOffset] }, ct);
            await _adminManagement.CreateDepartmentAsync(new CreateDepartmentRequest(testCompanyId, "Yönetim"), ct);
            var allDepts = await _departmentRepository.GetAllAsync(ct);
            var dept = allDepts.First(x => x.CompanyId == testCompanyId);
            await _adminManagement.CreateUserAsync(
                new CreateUserRequest(
                    testCompanyId, "Test Admin", testEmail, "TST-001", dept.Id, null,
                    UserRole.SystemAdmin, UserAuthorizationCatalog.GetAllowedPermissions(UserRole.SystemAdmin),
                    testPassword),
                ct);

            // Test sirketine, isteyen kullanicinin KENDI e-postasiyla ikinci bir yonetici acilir.
            // Sebep: yukaridaki test kullanicisinin sifresi yalniz bu istegin icinde uretiliyor ve
            // hicbir yerde saklanmiyor; onunla giris yapmak mumkun degil. /Account/SwitchCompany
            // ise "hedef sirkette AYNI e-postaya ait aktif kullanici var mi" diye bakiyor —
            // bu kayit sayesinde kullanici test sirketine normal "Sirket Degistir" akisiyla,
            // hicbir sifre gosterilmeden gecebilir. Bu olmadan Fonksiyon Testleri ekranina
            // hic ulasilamaz (testler yalniz test sirketi oturumunda calisir).
            var callerEmail = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
            if (!string.IsNullOrWhiteSpace(callerEmail)
                && !string.Equals(callerEmail, testEmail, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var callerName = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "Yönetici";
                    await _adminManagement.CreateUserAsync(
                        new CreateUserRequest(
                            testCompanyId, callerName, callerEmail, "TST-002", dept.Id, null,
                            UserRole.SystemAdmin, UserAuthorizationCatalog.GetAllowedPermissions(UserRole.SystemAdmin),
                            $"Hc!{Guid.NewGuid().ToString("N")[..8]}"),
                        ct);
                }
                catch (Exception ex)
                {
                    // Ortami cokertme: test sirketi kuruldu, yalniz "gecis kullanicisi" acilamadi.
                    // Sessizce yutma — logla ve kullaniciya bildir.
                    _logger.LogError(ex, "[StreamTestCompany] Gecis kullanicisi olusturulamadi. Email={Email}", callerEmail);
                    await WriteFrameAsync(new
                    {
                        type = "setup_warning",
                        message = "Test şirketi kuruldu ancak kendi e-postanızla geçiş kullanıcısı açılamadı; " +
                                  "Şirket Değiştir listesinde görünmeyebilir.",
                    }, ct);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[StreamTestCompany] Test ortamı oluşturulamadı.");
            await WriteFrameAsync(new { type = "setup_error", message = $"Ortam oluşturulamadı: {ex.Message}" }, ct);
            return;
        }

        // Adım son: Test oturumu başlat (programmatic login)
        try
        {
            await WriteFrameAsync(new { type = "setup_step", step = stepTotal, total = stepTotal, message = stepLabels[^1] }, ct);
            var req     = _httpContextAccessor.HttpContext!.Request;
            var baseUrl = $"{req.Scheme}://{req.Host}";

            var (testCookieHeader, loginError) = await LoginAsTestUserAsync(baseUrl, testEmail, testPassword, testCompanyId, ct);
            if (testCookieHeader == null)
            {
                await WriteFrameAsync(new { type = "setup_error", message = $"Test oturumu başlatılamadı: {loginError}" }, ct);
                return;
            }

            await WriteFrameAsync(new { type = "setup_done", mode = "test", companyName = testCompanyName, userEmail = testEmail, testCompanyId }, ct);

            // Hazirlik BURADA biter (2026-08-27). Form kontrolleri artik MEVCUT sirkette
            // kosuyor (/Admin/HealthCheck/Stream) — bu uc yalniz Fonksiyon Testleri icin
            // izole sirket+veritabani kurar. Eskiden form kontrollerini de burada kosturmak,
            // her calistirmada gereksiz bir test sirketi acilmasina yol aciyordu.
            await WriteFrameAsync(new { type = "setup_ready", companyName = testCompanyName, testCompanyId }, ct);
        }
        catch (Exception ex)
        {
            await WriteFrameAsync(new { type = "setup_error", message = $"Test sırasında hata: {ex.Message}" }, ct);
        }
    }

    /// <summary>
    /// Fonksiyon testleri — ticari (Faz 1) / üretim / kalite senaryolarını gerçek HTTP uçlarına
    /// istek atarak çalıştırır (bkz. Services/FunctionalTests/). NDJSON frame tipleri:
    ///   fn_start(total, groups) | fn_step(index,total,key,group,label) |
    ///   fn_result(index,total,key,group,label,ok,skipped,durationMs,message,steps[]) |
    ///   fn_done(passed,failed,skipped) | fn_error(message)
    ///
    /// GÜVENLİK KİLİDİ (zorunlu, sunucu tarafı): yalnız oturumdaki şirket TEST_ önekli bir test
    /// şirketiyse çalışır. Testler gerçek belge/stok hareketi yazar — canlı şirkette ASLA
    /// çalıştırılamaz. Bu kontrol tek koruma katmanıdır; arayüz ayrıca gizleyebilir ama asıl
    /// zorlama burasıdır (aynı desen: SalesController.ChangeQuoteStatus governance kontrolü).
    /// </summary>
    /// <summary>
    /// Iki sirket ayni veritabanina mi bakiyor? Sirket kaydindaki <c>DatabaseName</c> tek
    /// basina YETMEZ: varsayilan (ilk) sirkette bu alan bos olur ve baglanti appsettings'teki
    /// varsayilan dizeden cozulur — bos degeri "bilinmiyor" sayip fail-closed davranmak her
    /// test sirketini de bloklardi. Bu yuzden karsilastirma, factory'nin sirket icin GERCEKTEN
    /// cozdugu baglanti dizesi (sunucu + veritabani) uzerinden yapilir. Cozulemezse TRUE doner
    /// — emin olamadigimiz durumda testleri calistirmayiz.
    /// </summary>
    private bool SameDatabase(int companyIdA, int companyIdB)
    {
        var a = TryResolveTarget(companyIdA);
        var b = TryResolveTarget(companyIdB);
        if (a is null || b is null) return true;
        return string.Equals(a.Value.Server, b.Value.Server, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Value.Database, b.Value.Database, StringComparison.OrdinalIgnoreCase);
    }

    private (string Server, string Database)? TryResolveTarget(int companyId)
    {
        try
        {
            var cs = _connectionFactory.ResolveConnectionStringForCompany(companyId);
            if (string.IsNullOrWhiteSpace(cs)) return null;
            var b = new SqlConnectionStringBuilder(cs);
            var db = b.InitialCatalog?.Trim();
            var srv = b.DataSource?.Trim();
            if (string.IsNullOrWhiteSpace(db) || string.IsNullOrWhiteSpace(srv)) return null;
            return (srv, db);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[StreamFunctionalTests] Sirket {CompanyId} baglantisi cozulemedi.", companyId);
            return null;
        }
    }

    [HttpPost("/Admin/HealthCheck/StreamFunctionalTests")]
    [ValidateAntiForgeryToken]
    public async Task StreamFunctionalTests([FromQuery] string? groups, CancellationToken ct)
    {
        Response.ContentType = "application/x-ndjson; charset=utf-8";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
        Response.StatusCode = 200;

        string? companyName;
        string? sharedWithLiveCompany = null;
        // Senaryo baglamina gecirilir (yetki senaryolari ikinci kullanici girisinde kullanir).
        int.TryParse(User.FindFirst("company_id")?.Value, out var currentCompanyId);
        try
        {
            var company = currentCompanyId > 0 ? await _companyRepository.GetByIdAsync(currentCompanyId, ct) : null;
            companyName = company?.Name;

            // "TEST_" adi TEK BASINA yeterli DEGIL: "Test Sirketi Olustur" akisi yeni DB
            // olusturulmadan calistirildiginda test sirketi MEVCUT (canli) sirketin baglanti
            // dizesini devralir — ayni veritabanina bakar. Bu durumda testlerin yazdigi belge
            // ve stok hareketleri CANLI veriye duser. Bu yuzden test sirketinin veritabanini
            // TEST olmayan bir sirket de kullaniyorsa calistirma reddedilir (fail-closed).
            if (company is not null)
            {
                var all = await _companyRepository.GetAllAsync(ct);
                sharedWithLiveCompany = all
                    .Where(c => c.Id != company.Id
                                && !(c.Name ?? string.Empty).StartsWith("TEST_", StringComparison.Ordinal)
                                && SameDatabase(c.Id, company.Id))
                    .Select(c => c.Name)
                    .FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[StreamFunctionalTests] Şirket bilgisi okunamadı — güvenlik kilidi fail-closed.");
            await WriteFrameAsync(new { type = "fn_error", message = "Şirket bilgisi doğrulanamadı — işlem güvenlik nedeniyle durduruldu." }, ct);
            return;
        }

        if (string.IsNullOrWhiteSpace(companyName) || !companyName.StartsWith("TEST_", StringComparison.Ordinal))
        {
            await WriteFrameAsync(new
            {
                type = "fn_error",
                message = "Fonksiyon testleri yalnız 'TEST_' önekli test şirketlerinde çalıştırılabilir " +
                           $"(mevcut şirket: {(string.IsNullOrWhiteSpace(companyName) ? "bilinmiyor" : companyName)}). " +
                           "Önce 'Test Şirketi Oluştur' akışıyla izole bir test şirketine geçin.",
            }, ct);
            return;
        }

        if (sharedWithLiveCompany is not null)
        {
            _logger.LogWarning("[StreamFunctionalTests] Test sirketi '{Test}' canli sirket '{Live}' ile ayni veritabanini paylasiyor — reddedildi.",
                companyName, sharedWithLiveCompany);
            await WriteFrameAsync(new
            {
                type = "fn_error",
                message = $"Bu test şirketi '{sharedWithLiveCompany}' şirketiyle AYNI veritabanını kullanıyor; testler gerçek belge ve " +
                          "stok hareketi yazdığı için çalıştırma durduruldu. Test şirketini 'Yeni Veritabanı Oluştur' seçeneğiyle kurun.",
            }, ct);
            return;
        }

        var req = _httpContextAccessor.HttpContext!.Request;
        var baseUrl = $"{req.Scheme}://{req.Host}";
        var cookieHeader = string.Join("; ", req.Cookies.Select(c => $"{c.Key}={c.Value}"));

        var (httpClient, clientError) = await FunctionalTestHttpClient.CreateAsync(baseUrl, cookieHeader, ct);
        if (httpClient == null)
        {
            await WriteFrameAsync(new { type = "fn_error", message = "Test HTTP istemcisi kurulamadı: " + clientError }, ct);
            return;
        }

        IReadOnlyCollection<string>? groupList = string.IsNullOrWhiteSpace(groups)
            ? null
            : groups.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        try
        {
            var ctx = new FunctionalTestContext(httpClient, _documentTypeRepo, baseUrl, currentCompanyId);
            var runner = new FunctionalTestRunner(FunctionalTestScenarioRegistry.BuildAll());
            await runner.RunAsync(groupList, ctx, (frame, frameCt) => WriteFrameAsync(frame, frameCt), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[StreamFunctionalTests] Test çalıştırma sırasında beklenmeyen hata.");
            await WriteFrameAsync(new { type = "fn_error", message = "Test çalıştırma sırasında beklenmeyen bir hata oluştu." }, ct);
        }
        finally
        {
            httpClient.Dispose();
        }
    }

    /// <summary>
    /// Programmatic login: GET login sayfasından CSRF token al, POST ile giriş yap, auth cookie'yi döndür.
    /// </summary>
    private async Task<(string? CookieHeader, string? Error)> LoginAsTestUserAsync(
        string baseUrl, string email, string password, int companyId, CancellationToken ct)
    {
        var cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies        = true,
            CookieContainer   = cookieContainer,
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        var loginUri = new Uri($"{baseUrl}/Account/Login");

        try
        {
            // 1. GET login sayfası → antiforgery cookie + form token
            using var getResp = await client.GetAsync(loginUri, ct);
            var html = await getResp.Content.ReadAsStringAsync(ct);
            var csrfToken = ExtractCsrfToken(html);
            if (string.IsNullOrWhiteSpace(csrfToken))
                return (null, "CSRF token bulunamadı");

            // 2. POST login
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["CompanyId"]                    = companyId.ToString(),
                ["Email"]                        = email,
                ["Password"]                     = password,
                ["RememberMe"]                   = "false",
                ["__RequestVerificationToken"]   = csrfToken,
            });
            using var postResp = await client.PostAsync(loginUri, form, ct);

            // Başarı = 302 redirect to home
            if (postResp.StatusCode is not (HttpStatusCode.Redirect or HttpStatusCode.OK or HttpStatusCode.Found))
                return (null, $"Giriş başarısız (HTTP {(int)postResp.StatusCode})");

            var cookies = cookieContainer.GetCookies(loginUri);
            if (cookies.Count == 0)
                return (null, "Oturum cookie'si alınamadı");

            var cookieHeader = string.Join("; ", cookies.Cast<Cookie>().Select(c => $"{c.Name}={c.Value}"));
            return (cookieHeader, null);
        }
        catch (Exception ex)
        {
            return (null, $"Login hatası: {ex.Message}");
        }
    }

    /// <summary>
    /// Mevcut connection string'in gösterdiği sunucuda yeni bir test DB'si oluşturur.
    /// DB adı alphanumerik+alt_çizgi olduğundan doğrudan identifier olarak kullanılabilir.
    /// </summary>
    private static async Task<(string NewConnectionString, string? Error)> CreateTestDatabaseAsync(
        string? templateConnectionString, string dbName, ILogger logger, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(templateConnectionString))
            return (string.Empty, "Kaynak şirketin bağlantı bilgisi bulunamadı");
        try
        {
            var builder = new SqlConnectionStringBuilder(templateConnectionString);
            builder.InitialCatalog = "master";

            await using var masterConn = new SqlConnection(builder.ConnectionString);
            await masterConn.OpenAsync(ct);
            await using var cmd = masterConn.CreateCommand();
            // dbName: CalibraTest_DDMMYYHHII — yalnızca alfanümerik ve alt çizgi, injection riski yok
            cmd.CommandText = $"""
                IF NOT EXISTS (SELECT name FROM master.sys.databases WHERE name = N'{dbName}')
                    CREATE DATABASE [{dbName}];
                """;
            await cmd.ExecuteNonQueryAsync(ct);

            var newBuilder = new SqlConnectionStringBuilder(templateConnectionString)
            {
                InitialCatalog = dbName
            };
            return (newBuilder.ConnectionString, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[HealthCheck] Test veritabanı oluşturulamadı: {DbName}", dbName);
            return (string.Empty, ex.Message);
        }
    }

    private static string? ExtractCsrfToken(string html)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            html,
            @"<input[^>]+name=""__RequestVerificationToken""[^>]+value=""([^""]+)""",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static void FlattenMenu(IReadOnlyList<MenuDefinition.MenuNode> menu, string? parentLabel, List<CheckTarget> output)
    {
        foreach (var node in menu)
        {
            // Sadece URL'i olan node'lar test edilir (grup baslari atlanir)
            if (!string.IsNullOrEmpty(node.Url))
            {
                output.Add(new CheckTarget
                {
                    Key = node.Key,
                    Label = node.Label,
                    Path = node.Url,
                    ParentLabel = parentLabel,
                });
            }
            if (node.Children != null && node.Children.Count > 0)
            {
                var childParent = string.IsNullOrEmpty(parentLabel) ? node.Label : $"{parentLabel} › {node.Label}";
                FlattenMenu(node.Children, childParent, output);
            }
        }
    }

    /// <summary>HTML hata sayfasindan anlamli kismi cek (exception message'i icerir).</summary>
    private static string ExtractErrorSnippet(string body)
    {
        if (string.IsNullOrEmpty(body)) return string.Empty;

        // ASP.NET dev exception page'inin baslik kismindan exception type ve mesajini al
        var titleIdx = body.IndexOf("<title>", StringComparison.OrdinalIgnoreCase);
        if (titleIdx >= 0)
        {
            var endIdx = body.IndexOf("</title>", titleIdx, StringComparison.OrdinalIgnoreCase);
            if (endIdx > titleIdx)
            {
                var title = body.Substring(titleIdx + 7, endIdx - titleIdx - 7);
                if (!string.IsNullOrWhiteSpace(title) && title.Length < 200)
                    return title.Trim();
            }
        }

        // SqlException tipini ara
        var sqlIdx = body.IndexOf("SqlException", StringComparison.OrdinalIgnoreCase);
        if (sqlIdx >= 0)
        {
            var end = Math.Min(sqlIdx + 300, body.Length);
            return body.Substring(sqlIdx, end - sqlIdx).Replace("\n", " ").Replace("  ", " ");
        }

        // Genel: ilk 300 character (HTML tag'lerini at)
        var stripped = System.Text.RegularExpressions.Regex.Replace(body, "<[^>]+>", " ");
        stripped = System.Text.RegularExpressions.Regex.Replace(stripped, @"\s+", " ").Trim();
        return stripped.Length > 300 ? stripped.Substring(0, 300) + "..." : stripped;
    }

    private sealed class CheckTarget
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public string Path { get; set; } = "";
        public string? ParentLabel { get; set; }
    }

    private sealed class CheckResult
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public string Path { get; set; } = "";
        public string? ParentLabel { get; set; }
        public int StatusCode { get; set; }
        public int DurationMs { get; set; }
        public string Status { get; set; } = "pending";   // ok/redirect/warn/error/exception
        public string? ErrorSnippet { get; set; }
        /// <summary>Schema probe sonucu (registry'de tanımlıysa); yoksa null = "—" gösterilir.</summary>
        public SchemaProbeResult? SchemaProbe { get; set; }
    }
}
