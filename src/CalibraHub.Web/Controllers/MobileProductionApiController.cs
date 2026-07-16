using System.Security.Claims;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Security;
using CalibraHub.Application.Services;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Mobil Üretim (Android) modülü — iş emri listesi/detayı + operasyon başlat/tamamla.
///
/// Web ShopFloor (kiosk) akışının mobil karşılığı. Header deseni MobileWarehouseApiController
/// ile aynı (cookie auth, CSRF muaf, ayrı CORS policy). Yeni iş mantığı YAZILMADI — is emri
/// okuma IWorkOrderService/IWorkOrderOperationService (ProductionController.WorkOrders/ShopFloor'un
/// kullandığı aynı servisler), operatör auth IPersonnelService.GetByPinOrCardAsync +
/// ShopFloorLockoutTracker (ProductionController.AuthOperator ile AYNEN), start/complete
/// WorkOrderOperationService.StartAsync/PartialCompleteAsync/CompleteAsync (ProductionController.
/// ShopFloorStart/PartialComplete/Complete'in çağırdığı aynı metodlar).
///
/// Endpoint'ler:
///   GET  /api/mobile/production/work-orders          — iş emri arama/listesi
///   GET  /api/mobile/production/work-orders/{id}     — iş emri detayı + operasyon listesi
///   POST /api/mobile/production/auth-operator         — PIN ile operatör doğrulama (ShopFloorLockoutTracker dahil)
///   POST /api/mobile/production/operations/start       — operasyon başlat
///   POST /api/mobile/production/operations/complete    — operasyon tamamla (iyi + fire miktarı)
///
/// Yetki — iki katman:
///   1) Cookie [Authorize] + merkezi IPermissionService.CheckAnyAsync (RequirePermissionAsync).
///      İş emri listesi/detayı → FormCodes.WorkOrders (web menüsündeki "İş Emirleri" sayfasıyla
///      aynı kapı). Operatör auth + operasyon start/complete → FormCodes.ShopFloor (web menüsündeki
///      "Üretim Terminali" sayfasıyla aynı kapı — o sayfaya erişebilen kullanıcı mobilden de
///      terminali kullanabilir). NOT: web'deki ShopFloor POST endpoint'lerinin bugün hiçbiri
///      [PermissionScope] taşımıyor (yalnızca controller-level [Authorize]) — bu, ayrı bir
///      bilinen boşluk (bkz. memory/project_permission_filter_gaps.md), bu dosyanın kapsamı
///      dışında bırakıldı. Mobil taraf burada web'den DAHA SIKI: her endpoint SHOP_FLOOR
///      formuna VIEW|VIEW_OWN ister.
///   2) Operatör PIN — auth-operator başarılı dönüşünde alınan operatorId istemcide saklanır,
///      ama start/complete çağrısında asla güvenilmez: her seferinde Personnel tablosuna karşı
///      yeniden doğrulanır (IsActive + IsProductionOperator, bkz. ValidateOperatorAsync).
///
/// AUDIT: WorkOrderOperationService.StartAsync/PartialCompleteAsync/CompleteAsync servis
/// katmanında zaten IAuditTrailService ile entity="WorkOrder" LogChanges çağrısı yapıyor
/// (bu dosyanın yazıldığı tarihte repoda mevcut — git log ile doğrulandı). Bu controller o
/// metodları AYNEN çağırdığı için mobilden tetiklenen start/partial/complete otomatik olarak
/// aynı işlem loguna düşer; ayrıca kod eklemeye gerek yok.
/// </summary>
[ApiController]
[Route("api/mobile/production")]
[IgnoreAntiforgeryToken]
[EnableCors("MobileApi")]
[Authorize]
public sealed class MobileProductionApiController : ControllerBase
{
    private readonly IWorkOrderService _workOrderService;
    private readonly IWorkOrderOperationService _workOrderOperations;
    private readonly IWorkOrderOperationActivityService _activities;
    private readonly IPersonnelService _personnel;
    private readonly IPersonnelRepository _personnelRepo;
    private readonly ICompanyParameterService _companyParameters;
    private readonly ShopFloorLockoutTracker _shopFloorLockout;
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly IPermissionService _permService;

    public MobileProductionApiController(
        IWorkOrderService workOrderService,
        IWorkOrderOperationService workOrderOperations,
        IWorkOrderOperationActivityService activities,
        IPersonnelService personnel,
        IPersonnelRepository personnelRepo,
        ICompanyParameterService companyParameters,
        ShopFloorLockoutTracker shopFloorLockout,
        SqlServerConnectionFactory connectionFactory,
        IPermissionService permService)
    {
        _workOrderService = workOrderService;
        _workOrderOperations = workOrderOperations;
        _activities = activities;
        _personnel = personnel;
        _personnelRepo = personnelRepo;
        _companyParameters = companyParameters;
        _shopFloorLockout = shopFloorLockout;
        _connectionFactory = connectionFactory;
        _permService = permService;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Yetki — MobileWarehouseApiController ile birebir aynı desen.
    // ──────────────────────────────────────────────────────────────────────

    private static readonly string[] WorkOrderFormCodes = { FormCodes.WorkOrders };
    private static readonly string[] ShopFloorFormCodes = { FormCodes.ShopFloor };
    private static readonly string[] ViewActions = { "VIEW", "VIEW_OWN" };

    private (int UserId, UserRole Role, int? DepartmentId) GetCurrentUser()
    {
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId);

        var roleStr = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        if (!UserAuthorizationCatalog.TryParseRole(roleStr, out var role))
            role = UserRole.Operator;

        int? departmentId = int.TryParse(User.FindFirstValue("department_id"), out var d) && d > 0 ? d : null;
        return (userId, role, departmentId);
    }

    private async Task<IActionResult?> RequirePermissionAsync(
        IReadOnlyList<string> formCodes, IReadOnlyList<string> actionCodes, CancellationToken ct)
    {
        var (userId, role, departmentId) = GetCurrentUser();
        foreach (var formCode in formCodes)
        {
            if (await _permService.CheckAnyAsync(userId, role, departmentId, formCode, actionCodes, ct))
                return null;
        }

        return new JsonResult(new
        {
            ok      = false,
            message = "Bu işlemi yapmak için yetkiniz yok.",
            error   = $"Yetki yok: {string.Join('|', formCodes)}:{string.Join('|', actionCodes)}",
        }) { StatusCode = StatusCodes.Status403Forbidden };
    }

    private int ResolveCurrentCompanyIdSafe()
    {
        try { return _connectionFactory.ResolveCurrentCompanyId(); }
        catch { return 0; }
    }

    /// <summary>ProductionController.GetShopFloorMaxPinAttemptsAsync ile birebir aynı (kopya — Mobile*ApiController'lar ortak base sınıf kullanmıyor, bkz. MobileWarehouseApiController).</summary>
    private async Task<int> GetShopFloorMaxPinAttemptsAsync(CancellationToken ct)
    {
        try
        {
            var p = await _companyParameters.ListAsync("PRODUCTION", ct);
            var raw = p.FirstOrDefault(x => x.ParamKey == "SHOPFLOOR_MAX_PIN_ATTEMPTS")?.ParamValue;
            if (int.TryParse(raw, System.Globalization.NumberStyles.Integer,
                              System.Globalization.CultureInfo.InvariantCulture, out var v)
                && v >= 0 && v <= 50)
                return v;
        }
        catch { /* parametre yoksa default */ }
        return 5;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Durum kod/etiket sözlükleri — API'den ham enum SERIALIZE edilmez (proje kuralı,
    // bkz. CLAUDE.md "React / Frontend — API'den Enum Yükleme Kuralı"). Mobil taraf için de
    // aynı gerekçe geçerli: stabil küçük-harf token + ayrı Türkçe etiket elle kurulur.
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>Tam token seti: planned/released/in_progress/completed/closed/cancelled.</summary>
    private static (string Code, string Label) MapWorkOrderStatus(WorkOrderStatus s) => s switch
    {
        WorkOrderStatus.Planned    => ("planned",     "Taslak"),
        WorkOrderStatus.Released   => ("released",    "Yayımlandı"),
        WorkOrderStatus.InProgress => ("in_progress", "Devam ediyor"),
        WorkOrderStatus.Completed  => ("completed",   "Tamamlandı"),
        WorkOrderStatus.Closed     => ("closed",      "Kapatıldı"),
        WorkOrderStatus.Cancelled  => ("cancelled",   "İptal"),
        _                          => ("unknown",     s.ToString()),
    };

    /// <summary>Tam token seti: pending/in_progress/completed/skipped.</summary>
    private static (string Code, string Label) MapOperationStatus(WorkOrderOperationStatus s) => s switch
    {
        WorkOrderOperationStatus.Pending    => ("pending",     "Bekliyor"),
        WorkOrderOperationStatus.InProgress => ("in_progress", "Devam ediyor"),
        WorkOrderOperationStatus.Completed  => ("completed",   "Tamamlandı"),
        WorkOrderOperationStatus.Skipped    => ("skipped",     "Atlandı"),
        _                                   => ("unknown",     s.ToString()),
    };

    // ──────────────────────────────────────────────────────────────────────
    // GET work-orders?q=&take=
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// İş emri arama/listesi. q boşsa tüm emirler (take ile sınırlı) döner; doluysa emir
    /// numarası/malzeme kodu/malzeme adında LIKE benzeri (Contains, case-insensitive) arama
    /// yapılır — sunucu tarafında IWorkOrderService.ListAsync (ProductionController.WorkOrders
    /// ile aynı kaynak) sonucu üzerinde in-memory filtre (web SmartBoard board'unun kendisi de
    /// aynı yaklaşımı kullanıyor). Durum bazlı gizli filtre YOK (Cancelled/Closed dahil tüm
    /// emirler döner) — bu endpoint web board'un aksine bilinçli olarak "iptal/kapalı da
    /// görünsün" tercih edildi, çünkü mobil tarafta ayrı bir durum filtresi parametresi
    /// tanımlanmadı; gerekirse lider kararıyla eklenir.
    /// </summary>
    [HttpGet("work-orders")]
    public async Task<IActionResult> WorkOrders([FromQuery] string? q, [FromQuery] int? take, CancellationToken ct)
    {
        if (await RequirePermissionAsync(WorkOrderFormCodes, ViewActions, ct) is { } denied)
            return denied;

        var pageSize = take.GetValueOrDefault(50);
        if (pageSize <= 0) pageSize = 50;
        if (pageSize > 100) pageSize = 100;

        var all = await _workOrderService.ListAsync(status: null, ct);

        var query = (q ?? string.Empty).Trim();
        IEnumerable<WorkOrderListItemDto> filtered = all;
        if (query.Length > 0)
        {
            filtered = all.Where(o =>
                (o.OrderNumber?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (o.ItemCode?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (o.ItemName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var result = filtered
            .OrderByDescending(o => o.OrderDate)
            .Take(pageSize)
            .Select(o =>
            {
                var (code, label) = MapWorkOrderStatus(o.Status);
                return new
                {
                    id          = o.Id,
                    number      = o.OrderNumber,
                    itemCode    = o.ItemCode ?? "",
                    itemName    = o.ItemName ?? "",
                    quantity    = o.PlannedQuantity,
                    unit        = o.UnitCode ?? "",
                    statusCode  = code,
                    statusLabel = label,
                    plannedDate = o.PlannedStartDate,
                };
            });

        return Ok(result);
    }

    // ──────────────────────────────────────────────────────────────────────
    // GET work-orders/{id}
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// İş emri detayı + operasyon listesi (Sequence sırasıyla — repo zaten ORDER BY Sequence
    /// döner). canStart/canComplete ShopFloor.cshtml'in queue satırı render kuralıyla birebir
    /// aynı türetilir: canStart = Pending &amp; UpstreamCap&gt;0 (web'in "canStart" JS değişkeni),
    /// canComplete = InProgress (web'in Bitir butonunu yalnızca InProgress satırlarda göstermesiyle
    /// aynı). Durum makinesi burada; mobil istemci yalnızca bu bayraklara göre buton disable eder.
    /// </summary>
    [HttpGet("work-orders/{id:int}")]
    public async Task<IActionResult> WorkOrderDetail(int id, CancellationToken ct)
    {
        if (await RequirePermissionAsync(WorkOrderFormCodes, ViewActions, ct) is { } denied)
            return denied;

        var wo = await _workOrderService.GetAsync(id, ct);
        if (wo is null)
            return NotFound(new { error = $"İş emri bulunamadı (Id: {id})." });

        var ops = await _workOrderOperations.GetByWorkOrderAsync(id, ct);
        var (woCode, woLabel) = MapWorkOrderStatus(wo.Status);

        var operations = ops.Select(op =>
        {
            var (opCode, opLabel) = MapOperationStatus(op.Status);
            return new
            {
                id            = op.Id,
                seq           = op.Sequence,
                name          = op.OperationName ?? op.OperationCode ?? $"Operasyon #{op.Id}",
                // WorkOrderOperationDto.Name/Code sütunları MAKİNE kod/adı taşır (bkz.
                // SqlWorkOrderOperationRepository.ReadListAsync — wo.MachineId, m.Code, m.Name
                // sırasıyla MachineId/Code/Name'e map edilir); OperationCode/OperationName ayrı
                // alanlardır (operasyon tanımı). Makine atanmamışsa "" döner (sözleşmede
                // machineName non-null string).
                machineName   = op.Name ?? op.Code ?? "",
                statusCode    = opCode,
                statusLabel   = opLabel,
                goodQuantity  = op.ProducedQuantity,
                scrapQuantity = op.ScrapQuantity,
                canStart      = op.Status == WorkOrderOperationStatus.Pending && op.UpstreamCap > 0,
                canComplete   = op.Status == WorkOrderOperationStatus.InProgress,
            };
        });

        return Ok(new
        {
            id          = wo.Id,
            number      = wo.OrderNumber,
            itemCode    = wo.ItemCode ?? "",
            itemName    = wo.ItemName ?? "",
            quantity    = wo.PlannedQuantity,
            unit        = wo.UnitCode ?? "",
            statusCode  = woCode,
            statusLabel = woLabel,
            operations,
        });
    }

    // ──────────────────────────────────────────────────────────────────────
    // POST auth-operator
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sözleşmenin zorunlu alanı `pin`dir; buna ek olarak `personnelCode` (Sicil No) da
    /// REQUIRED olarak eklendi (alan EKLEME serbest, çıkarma yasak — görev talimatı). Gerekçe:
    /// ProductionController.AuthOperator'da (web) 2026-05-22'den beri PIN-tek-başına yolu
    /// kilitli — Code+PIN ikilisi zorunlu, ShopFloorLockoutTracker de yalnızca bu yolda
    /// çalışıyor. personnelCode'suz mobil çağrı, lockout korumasız "legacy PIN-only" yoluna
    /// düşerdi (AYNEN reuse talimatına aykırı) — o yüzden burada da zorunlu kılındı. NFC/kart
    /// yolu mobile taşınmadı (Android tarafında henüz kamera/NFC yok, bkz.
    /// memory/project_mobile_modules.md) — yalnızca Code+PIN.
    /// </summary>
    public sealed record MobileAuthOperatorRequest(string? Pin, string? PersonnelCode);

    [HttpPost("auth-operator")]
    public async Task<IActionResult> AuthOperator([FromBody] MobileAuthOperatorRequest? req, CancellationToken ct)
    {
        if (await RequirePermissionAsync(ShopFloorFormCodes, ViewActions, ct) is { } denied)
            return denied;

        if (req is null || string.IsNullOrWhiteSpace(req.Pin))
            return BadRequest(new { error = "PIN girilmedi." });
        if (string.IsNullOrWhiteSpace(req.PersonnelCode))
            return BadRequest(new { error = "Sicil numarası girilmedi. Giriş için Sicil + PIN ikisi de gerekli." });

        // ── ShopFloor PIN lockout — ProductionController.AuthOperator ile AYNEN aynı akış ──
        var existing = await _personnelRepo.GetIdAndActiveByCodeAsync(req.PersonnelCode!, ct);
        if (existing is not null && !existing.Value.IsActive)
            return BadRequest(new { error = "Bu sicil bloklu. Yöneticinizle iletişime geçin." });

        var op = await _personnel.GetByPinOrCardAsync(req.PersonnelCode, req.Pin, cardNo: null, ct);
        if (op is null)
        {
            var companyId = ResolveCurrentCompanyIdSafe();
            var limit = await GetShopFloorMaxPinAttemptsAsync(ct);
            var shouldLock = _shopFloorLockout.RegisterFailure(companyId, req.PersonnelCode!, limit);
            if (shouldLock)
            {
                var existing2 = await _personnelRepo.GetIdAndActiveByCodeAsync(req.PersonnelCode!, ct);
                if (existing2 is not null && existing2.Value.IsActive)
                    await _personnelRepo.DeactivateAsync(existing2.Value.Id, ct);
                return BadRequest(new { error = "Hatalı PIN limiti aşıldı. Sicil bloklandı, yöneticinizle iletişime geçin." });
            }
            return BadRequest(new { error = "Operatör bulunamadı, sicil veya PIN hatalı (ya da operatör pasif)." });
        }

        if (!string.IsNullOrWhiteSpace(op.Code))
            _shopFloorLockout.Reset(ResolveCurrentCompanyIdSafe(), op.Code);

        return Ok(new { operatorId = op.Id, name = op.FullName });
    }

    // ──────────────────────────────────────────────────────────────────────
    // Operatör doğrulama — start/complete çağrılarında operatorId istemciden gelir ama
    // ASLA güvenilmez: her seferinde Personnel tablosuna karşı yeniden kontrol edilir.
    // ──────────────────────────────────────────────────────────────────────

    private async Task<IActionResult?> ValidateOperatorAsync(int operatorId, CancellationToken ct)
    {
        if (operatorId <= 0)
            return BadRequest(new { error = "Operatör belirtilmedi." });
        var op = await _personnel.GetAsync(operatorId, ct);
        if (op is null || !op.IsActive || !op.IsProductionOperator)
            return BadRequest(new { error = "Operatör bulunamadı, pasif veya üretim operatörü değil." });
        return null;
    }

    // ──────────────────────────────────────────────────────────────────────
    // POST operations/start
    // ──────────────────────────────────────────────────────────────────────

    public sealed record MobileOperationStartRequest(int OperationId, int OperatorId);

    /// <summary>ProductionController.ShopFloorStart'ın çağırdığı aynı servis metodu (WorkOrderOperationService.StartAsync) — upstream cap kuralı orada (ArgumentException/InvalidOperationException burada 400'e çevrilir).</summary>
    [HttpPost("operations/start")]
    public async Task<IActionResult> StartOperation([FromBody] MobileOperationStartRequest? req, CancellationToken ct)
    {
        if (await RequirePermissionAsync(ShopFloorFormCodes, ViewActions, ct) is { } denied)
            return denied;

        if (req is null || req.OperationId <= 0)
            return BadRequest(new { error = "Operasyon belirtilmedi." });

        var op = await _workOrderOperations.GetAsync(req.OperationId, ct);
        if (op is null)
            return NotFound(new { error = "Operasyon bulunamadı." });

        if (await ValidateOperatorAsync(req.OperatorId, ct) is { } invalidOperator)
            return invalidOperator;

        try
        {
            await _workOrderOperations.StartAsync(new StartOperationRequest(req.OperationId, req.OperatorId), ct);
            return Ok(new { ok = true });
        }
        catch (ArgumentException aex) { return BadRequest(new { error = aex.Message }); }
        catch (InvalidOperationException ioex) { return BadRequest(new { error = ioex.Message }); }
        catch (Exception) { return BadRequest(new { error = "İşlem sırasında bir hata oluştu." }); }
    }

    // ──────────────────────────────────────────────────────────────────────
    // POST operations/complete
    // ──────────────────────────────────────────────────────────────────────

    public sealed record MobileOperationCompleteRequest(
        int OperationId, int OperatorId, decimal GoodQuantity, decimal ScrapQuantity, string? Note);

    /// <summary>
    /// Web ShopFloor'da iki ayrı buton olan "+ Kısmi Bitir" (PartialCompleteAsync) ve "✓ Bitir"
    /// (CompleteAsync) akışlarını TEK mobil çağrıda birleştirir — mobil UX'te tek "Tamamla"
    /// ekranı var. Sırayla: (1) goodQuantity/scrapQuantity'den en az biri &gt;0 ise
    /// PartialCompleteAsync ile bu oturumun miktarı mevcut toplama eklenir (servisin kendi
    /// upstream-cap ve pozitif-miktar kuralı geçerli — miktar validasyonu servisin kuralıyla,
    /// burada ayrıca tekrarlanmadı), (2) CompleteAsync(FinalQuantity: null) durumu Tamamlandı'ya
    /// çevirir (PartialComplete'in az önce yazdığı toplamı korur), (3) EndCurrentAsync ile aktif
    /// aktivite kapatılır — ProductionController.ShopFloorComplete'in yaptığı üçüncü adımla aynı,
    /// tek fark: web bu adımda Notes:null gönderiyor, burada mobil kullanıcının girdiği `note`
    /// aktivite kapanış notuna yazılır (CompleteOperationRequest'te ayrı bir not alanı yok).
    /// </summary>
    [HttpPost("operations/complete")]
    public async Task<IActionResult> CompleteOperation([FromBody] MobileOperationCompleteRequest? req, CancellationToken ct)
    {
        if (await RequirePermissionAsync(ShopFloorFormCodes, ViewActions, ct) is { } denied)
            return denied;

        if (req is null || req.OperationId <= 0)
            return BadRequest(new { error = "Operasyon belirtilmedi." });
        if (req.GoodQuantity < 0 || req.ScrapQuantity < 0)
            return BadRequest(new { error = "Miktar negatif olamaz." });

        var op = await _workOrderOperations.GetAsync(req.OperationId, ct);
        if (op is null)
            return NotFound(new { error = "Operasyon bulunamadı." });

        if (await ValidateOperatorAsync(req.OperatorId, ct) is { } invalidOperator)
            return invalidOperator;

        try
        {
            if (req.GoodQuantity > 0 || req.ScrapQuantity > 0)
            {
                await _workOrderOperations.PartialCompleteAsync(
                    new PartialCompleteOperationRequest(req.OperationId, req.OperatorId, req.GoodQuantity, req.ScrapQuantity), ct);
            }
            await _workOrderOperations.CompleteAsync(
                new CompleteOperationRequest(req.OperationId, req.OperatorId, FinalQuantity: null), ct);

            // Operasyon tamamlandı — aktif aktivite varsa otomatik kapat (web ShopFloorComplete
            // ile aynı üçüncü adım). Tek try/catch içinde tutulur — web'de de EndCurrentAsync
            // hatası Complete'in başarılı sonucunu maskeleyebilir (mevcut web davranışı AYNEN
            // korunur, bu görevin kapsamında düzeltilmedi).
            await _activities.EndCurrentAsync(
                new EndActivityRequest(req.OperationId, req.OperatorId, req.Note), ct);

            return Ok(new { ok = true });
        }
        catch (ArgumentException aex) { return BadRequest(new { error = aex.Message }); }
        catch (InvalidOperationException ioex) { return BadRequest(new { error = ioex.Message }); }
        catch (Exception) { return BadRequest(new { error = "İşlem sırasında bir hata oluştu." }); }
    }
}
