using System.Security.Claims;
using CalibraHub.Application.Auditing;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Security;
using CalibraHub.Domain.Enums;
using CalibraHub.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// İşlem Logları (audit trail) ekranı + JSON endpoint'leri.
///
///   GET /AuditLog                → izleme/raporlama ekranı (React AuditMonitor)
///   GET /AuditLog/Search         → filtreli log arama (sayfalı)
///   GET /AuditLog/Stats          → üst kartlar + gün bazlı dağılım
///   GET /AuditLog/Record         → tek kaydın zaman çizelgesi (belge ekranı sekmesi);
///                                  WidgetTraLog (Ek Alanlar) geçmişi de merge edilir.
///
/// Search/Stats ekran yetkisine (AUDIT_LOG) [PermissionScope] ile bağlıdır (admin-only veri
/// endpoint'leri — değişmedi). Index ise 2026-07-16 itibarıyla KOŞULLU: ?entity=&recordId=
/// ikisi de doluysa (kayda-kilitli mod — belge ekranındaki "Log Kayıtları" butonu) yalnızca
/// [Authorize] yeterli; belge ekranını zaten açabilen kullanıcı kendi kaydının geçmişini
/// görebilir. Parametreler eksikse (tam izleme/raporlama modu, ör. menüden doğrudan açılış)
/// AUDIT_LOG (VIEW/VIEW_OWN) yetkisi elle kontrol edilir — PermissionEnforcementFilter'ın GET
/// action'lar için uyguladığı aynı kontrol (bkz. GetCurrentUser/MakeForbidResult altta).
/// Record yalnızca [Authorize] ister (değişmedi).
/// </summary>
[Authorize]
public sealed class AuditLogController : Controller
{
    private readonly IAuditQueryService _auditQuery;
    private readonly IWidgetService _widgetService;
    private readonly IPermissionService _permService;
    private readonly CalibraHub.Application.Diagnostics.ISystemErrorLogQueryService _errorLogQuery;

    public AuditLogController(
        IAuditQueryService auditQuery, IWidgetService widgetService, IPermissionService permService,
        CalibraHub.Application.Diagnostics.ISystemErrorLogQueryService errorLogQuery)
    {
        _auditQuery = auditQuery;
        _widgetService = widgetService;
        _permService = permService;
        _errorLogQuery = errorLogQuery;
    }

    /// <summary>
    /// entity+recordId ikisi de doluysa kayda-kilitli mod → [Authorize] yeterli.
    /// Aksi halde (tam izleme modu) AUDIT_LOG:VIEW|VIEW_OWN yetkisi zorunlu.
    /// </summary>
    [HttpGet("/AuditLog")]
    public async Task<IActionResult> Index(string? entity, string? recordId, CancellationToken ct)
    {
        var recordScoped = !string.IsNullOrWhiteSpace(entity) && !string.IsNullOrWhiteSpace(recordId);
        if (!recordScoped)
        {
            var (userId, role, departmentId) = GetCurrentUser();
            var allowed = await _permService.CheckAnyAsync(
                userId, role, departmentId, FormCodes.AuditLog, new[] { "VIEW", "VIEW_OWN" }, ct);
            if (!allowed)
                return MakeForbidResult();

            // Hata Logları (2026-08-24): ayrı menü/ekran olmaktan çıkıp bu sayfanın ikinci
            // sekmesi oldu. Yetki kaynağı DEĞİŞMEDİ — eski /Admin/ErrorLog ekranıyla aynı
            // kapı (SetupDefinitions = SystemAdmin-only dev/sistem bucket'ı, bkz. CLAUDE.md
            // "DepartmentManager Rolü"). Yetkisi olmayan kullanıcı sekmeyi hiç görmez.
            ViewData["CanViewErrorLog"] = await _permService.CheckAnyAsync(
                userId, role, departmentId, FormCodes.SetupDefinitions, new[] { "VIEW", "VIEW_OWN" }, ct);
        }

        return View();
    }

    [HttpGet("/AuditLog/Search")]
    [PermissionScope(FormCodes.AuditLog)]
    public async Task<IActionResult> Search(
        DateTime? from, DateTime? to,
        // [FromQuery] ZORUNLU: "action" MVC route değeriyle (action="Search") çakışır —
        // route value provider query'den önce geldiği için parametre her istekte "Search"
        // değerini alır ve arama hep 0 döner. FromQuery bağlamayı query string'e kilitler.
        [FromQuery(Name = "action")] string? action,
        string? entity, string? user, string? text, string? source,
        int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        var (fromUtc, toUtc) = NormalizeRange(from, to);
        var (userId, role, departmentId) = GetCurrentUser();

        // Hata logu satirlari yalniz SystemAdmin'e (SetupDefinitions) gorunur. Bu TEK kontrol
        // hem SATIRLARI hem FACET'leri kapsar - ikisi ayri yollardan gelseydi satirlar gizliyken
        // acilir listede "SqlException" gorunup varligi sizardi.
        var canViewErrors = await _permService.CheckAnyAsync(
            userId, role, departmentId, FormCodes.SetupDefinitions, new[] { "VIEW", "VIEW_OWN" }, ct);

        if (!canViewErrors)
        {
            // Yetkisiz kullanici icin davranis birebir eski hali - sayfalama servis icinde.
            var plain = await _auditQuery.SearchAsync(
                new AuditSearchRequest(fromUtc, toUtc, action, entity, user, text, page, pageSize), ct);

            return Json(new
            {
                ok = true,
                canViewErrors = false,
                items = plain.Items.Select(ToDto),
                total = plain.Total,
                page,
                pageSize,
                facets = new
                {
                    entities = plain.Entities.Select(e => new { code = e, label = AuditFieldLabels.EntityLabel(e) }),
                    users = plain.Users,
                    actions = AuditActions.All.Select(a => new
                    {
                        code = a,
                        label = AuditFieldLabels.ActionLabels.GetValueOrDefault(a, a),
                    }),
                    sources = Array.Empty<object>(),
                },
            });
        }

        // -- Birlesik yol -----------------------------------------------------
        // Iki kaynak AYRI AYRI sayfalanamaz: her biri kendi ilk 50'sini dondurse birlesik liste
        // zaman sirasina gore eksik olur (2. sayfada kayitlar atlanir). Bu yuzden ikisi de
        // sayfalanmadan cekilir, zaman sirasina gore birlestirilir, sayfalama EN SON yapilir.
        var auditResult = await _auditQuery.SearchAsync(
            new AuditSearchRequest(fromUtc, toUtc, action, entity, user, text, Unpaged: true), ct);

        var errorResult = await _errorLogQuery.SearchAsync(
            new CalibraHub.Application.Diagnostics.SystemErrorSearchRequest(
                fromUtc, toUtc,
                Level: MapActionToLevel(action),
                Text: string.IsNullOrWhiteSpace(text) ? null : text.Trim(),
                UserName: string.IsNullOrWhiteSpace(user) ? null : user.Trim(),
                Category: string.IsNullOrWhiteSpace(source) ? null : source.Trim(),
                Unpaged: true,
                CompanyScope: CurrentCompanyId()),
            ct);

        // "Islem" filtresi audit islemlerinden birine ayarliysa hata satiri gosterilmez;
        // "Kayit turu" filtresi verilmisse hata satirinin exception turuyle eslesmeli.
        var errorRowsVisible = string.IsNullOrWhiteSpace(action) || IsErrorAction(action);
        var errorEntries = !errorRowsVisible
            ? new List<CalibraHub.Application.Diagnostics.SystemErrorEntry>()
            : errorResult.Items
                .Where(e => string.IsNullOrWhiteSpace(entity)
                         || string.Equals(e.ExceptionType, entity, StringComparison.OrdinalIgnoreCase))
                .ToList();

        // Audit satiri "Kaynak" secimiyle daraltilamaz - o alan hata logunun kategorisidir;
        // secim yapildiginda audit satirlari listeden cikar.
        var auditEntries = string.IsNullOrWhiteSpace(source)
            ? auditResult.Items
            : (IReadOnlyList<AuditEntry>)Array.Empty<AuditEntry>();

        var merged = auditEntries.Select(e => (Ts: e.Ts, Dto: ToDto(e)))
            .Concat(errorEntries.Select(e => (Ts: e.TimestampUtc, Dto: ErrorToDto(e))))
            .OrderByDescending(x => x.Ts)
            .ToList();

        var pageNo = Math.Max(1, page);
        var size = Math.Clamp(pageSize, 1, 500);
        var pageItems = merged.Skip((pageNo - 1) * size).Take(size).Select(x => x.Dto).ToList();

        var entityFacets = auditResult.Entities
            .Select(e => new { code = e, label = AuditFieldLabels.EntityLabel(e) })
            .Concat(errorResult.ExceptionTypes.Select(t => new { code = t, label = ShortTypeName(t) }))
            .ToList();

        return Json(new
        {
            ok = true,
            canViewErrors = true,
            items = pageItems,
            total = merged.Count,
            page = pageNo,
            pageSize = size,
            facets = new
            {
                entities = entityFacets,
                users = auditResult.Users.Concat(errorResult.Users)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(u => u, StringComparer.OrdinalIgnoreCase),
                actions = AuditActions.All.Concat(AuditActions.ErrorOnly).Select(a => new
                {
                    code = a,
                    label = AuditFieldLabels.ActionLabels.GetValueOrDefault(a, a),
                }),
                sources = errorResult.Categories.Select(c => new { code = c, label = ShortTypeName(c) }),
            },
        });
    }

    /// <summary>Islem filtresi hata satirlarina mi bakiyor.</summary>
    private static bool IsErrorAction(string? action)
        => AuditActions.ErrorOnly.Any(a => string.Equals(a, action, StringComparison.OrdinalIgnoreCase));

    /// <summary>Islem secimini hata logu seviyesine cevirir; digeri/bos ise tumu.</summary>
    private static string? MapActionToLevel(string? action)
        => IsErrorAction(action) ? action : null;

    /// <summary>"CalibraHub.Web.Controllers.AdminController" -> "AdminController".</summary>
    private static string ShortTypeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var i = value.LastIndexOf('.');
        return i >= 0 && i < value.Length - 1 ? value[(i + 1)..] : value;
    }

    private int? CurrentCompanyId()
        => int.TryParse(User.FindFirstValue("company_id"), out var cid) && cid > 0 ? cid : null;

    /// <summary>
    /// Hata logu satirini Islem Loglari satir sozlesmesine cevirir. Alan eslemesi:
    ///   islem      -> seviye (Hata / Kritik Hata)
    ///   kayit turu -> exception turu (kullanici karari 2026-08-25)
    ///   kaynak     -> logger kategorisi (mevcut "Kaynak" kolonu)
    ///   degisiklik -> exception mesaji (hata satirinda alan degisimi yoktur)
    /// stack / requestPath yalniz hata satirlarinda dolu; genisletilen satirda gosterilir.
    /// </summary>
    private static object ErrorToDto(CalibraHub.Application.Diagnostics.SystemErrorEntry e)
    {
        var action = string.Equals(e.Level, "Critical", StringComparison.OrdinalIgnoreCase)
            ? AuditActions.Critical
            : AuditActions.Error;
        return new
        {
            ts = e.TimestampUtc,
            user = string.IsNullOrWhiteSpace(e.UserName) ? "SISTEM" : e.UserName,
            userId = (int?)null,
            action,
            actionLabel = AuditFieldLabels.ActionLabels.GetValueOrDefault(action, action),
            entity = e.ExceptionType ?? string.Empty,
            entityLabel = string.IsNullOrWhiteSpace(e.ExceptionType) ? "Hata" : ShortTypeName(e.ExceptionType),
            entityId = (string?)null,
            title = e.Message,
            changes = (object?)null,
            detail = string.IsNullOrWhiteSpace(e.ExceptionMessage) ? e.Message : e.ExceptionMessage,
            ip = (string?)null,
            src = ShortTypeName(e.Category),
            // Hata satirina ozgu alanlar - audit satirlarinda hic bulunmaz.
            stack = e.StackTrace,
            requestPath = e.RequestPath,
            sourceFull = e.Category,
        };
    }

    [HttpGet("/AuditLog/Stats")]
    [PermissionScope(FormCodes.AuditLog)]
    public async Task<IActionResult> Stats(DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var (fromUtc, toUtc) = NormalizeRange(from, to);
        var stats = await _auditQuery.GetStatsAsync(fromUtc, toUtc, ct);

        var (userId, role, departmentId) = GetCurrentUser();
        var canViewErrors = await _permService.CheckAnyAsync(
            userId, role, departmentId, FormCodes.SetupDefinitions, new[] { "VIEW", "VIEW_OWN" }, ct);

        if (!canViewErrors)
            return Json(new { ok = true, stats });

        // Hata satirlari listeye karisiyorsa SAYAÇLARA da karismali. Aksi halde ust kart
        // "Toplam 120" derken liste 145 kayit gosterir; kullanici hangisinin dogru oldugunu
        // bilemez. Sayaclar da satirlarla AYNI yetki kontrolunden gecer.
        var errors = await _errorLogQuery.SearchAsync(
            new CalibraHub.Application.Diagnostics.SystemErrorSearchRequest(
                fromUtc, toUtc, Unpaged: true, CompanyScope: CurrentCompanyId()),
            ct);

        var byDay = stats.ByDay.ToDictionary(d => d.Day, d => d.Count, StringComparer.Ordinal);
        foreach (var e in errors.Items)
        {
            var day = e.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd");
            byDay[day] = byDay.GetValueOrDefault(day) + 1;
        }

        return Json(new
        {
            ok = true,
            stats = new
            {
                total = stats.Total + errors.Total,
                inserts = stats.Inserts,
                updates = stats.Updates,
                deletes = stats.Deletes,
                securityEvents = stats.SecurityEvents,
                errors = errors.Total,
                distinctUsers = stats.DistinctUsers,
                byDay = byDay.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                             .Select(kv => new { day = kv.Key, count = kv.Value }),
                topEntities = stats.TopEntities,
                topUsers = stats.TopUsers,
            },
        });
    }

    /// <summary>
    /// Tek kaydın değişiklik geçmişi. <paramref name="widgetFormCode"/> verilirse
    /// o formun Ek Alanlar (WidgetTraLog) geçmişi de aynı çizelgeye eklenir.
    /// </summary>
    [HttpGet("/AuditLog/Record")]
    public async Task<IActionResult> Record(string entity, string id, string? widgetFormCode,
        int max = 300, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(entity) || string.IsNullOrWhiteSpace(id))
            return Json(new { ok = false, error = "entity ve id zorunludur." });

        var trail = (await _auditQuery.GetRecordTrailAsync(entity, id, max, ct)).ToList();

        // Ek Alanlar (EAV widget) geçmişini merge et — aynı kaydın custom alan değişimleri
        if (!string.IsNullOrWhiteSpace(widgetFormCode))
        {
            var widgetHistory = await _widgetService.GetValueHistoryAsync(widgetFormCode, id, ct);
            if (widgetHistory is { Count: > 0 })
            {
                // Aynı kayıt anındaki (kullanıcı + saniye) alan değişimleri tek girdide toplanır
                var grouped = widgetHistory
                    .GroupBy(h => (h.ChangedBy, Second: new DateTime(
                        h.ChangedAt.Year, h.ChangedAt.Month, h.ChangedAt.Day,
                        h.ChangedAt.Hour, h.ChangedAt.Minute, h.ChangedAt.Second, DateTimeKind.Utc)))
                    .Select(g => new AuditEntry
                    {
                        Ts = g.Key.Second,
                        User = g.Key.ChangedBy ?? "SYSTEM",
                        Action = AuditActions.Update,
                        Entity = entity,
                        EntityId = id,
                        Detail = "Ek Alanlar",
                        Src = "Widget",
                        Changes = g.Select(h => new AuditFieldChange(
                            h.WidgetCode,
                            string.IsNullOrWhiteSpace(h.Label) ? h.WidgetCode : h.Label,
                            h.OldValue, h.NewValue)).ToList(),
                    });
                trail.AddRange(grouped);
            }
        }

        var ordered = trail.OrderByDescending(t => t.Ts).Take(Math.Clamp(max, 1, 1000));
        return Json(new { ok = true, items = ordered.Select(ToDto) });
    }

    // ── Yardımcılar ─────────────────────────────────────────────────────────

    /// <summary>
    /// PermissionEnforcementFilter.OnAuthorizationAsync ile birebir aynı claim çözümü
    /// (userId/role/departmentId) — Index'teki manuel yetki kontrolü filter'ın GET path'iyle
    /// aynı sonucu üretir. Rol claim'i hem enum adı hem Türkçe label olabilir; TryParseRole
    /// ikisini de çözer, bilinmeyense Operator'a düşer (filter ile aynı fallback).
    /// </summary>
    private (int UserId, UserRole Role, int? DepartmentId) GetCurrentUser()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        int.TryParse(userIdStr, out var userId);

        var roleStr = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        if (!UserAuthorizationCatalog.TryParseRole(roleStr, out var role))
            role = UserRole.Operator;

        var deptStr = User.FindFirstValue("department_id");
        int? departmentId = int.TryParse(deptStr, out var d) && d > 0 ? d : null;

        return (userId, role, departmentId);
    }

    /// <summary>
    /// PermissionEnforcementFilter.MakeForbidResult ile birebir aynı 403 deseni — bu, Index'in
    /// [PermissionScope(FormCodes.AuditLog)] taşıdığı zaman ürettiği sonuçla aynıdır (kaldırılan
    /// attribute'un davranışını tam izleme modu için burada elle taklit eder). AJAX/JSON istekte
    /// JSON 403 gövdesi, normal tarayıcı navigasyonunda ForbidResult (cookie challenge/AccessDenied).
    /// </summary>
    private IActionResult MakeForbidResult()
    {
        var req = Request;
        var isApi = req.Headers.Accept.ToString().Contains("application/json")
                 || req.Headers["X-Requested-With"].ToString()
                       .Equals("XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
                 || req.Path.StartsWithSegments("/api")
                 || (req.ContentType?.Contains("application/json") == true)
                 || req.Method.Equals("POST", StringComparison.OrdinalIgnoreCase);
        if (isApi)
        {
            return new JsonResult(new
            {
                ok      = false,
                message = "Bu işlemi yapmak için yetkiniz yok.",
                error   = $"Yetki yok: {FormCodes.AuditLog}:VIEW|VIEW_OWN",
            }) { StatusCode = StatusCodes.Status403Forbidden };
        }
        return Forbid();
    }

    /// <summary>
    /// Yerel gün girdilerini (yyyy-MM-dd) UTC aralığına çevirir; boşsa son 7 gün.
    /// Dönen aralık: FromUtc dahil, ToUtc HARİÇ (yerel günün sonu → ertesi yerel gün 00:00 UTC karşılığı).
    /// </summary>
    private static (DateTime FromUtc, DateTime ToUtc) NormalizeRange(DateTime? from, DateTime? to)
    {
        var localTo = (to ?? DateTime.Now.Date).Date;
        var localFrom = (from ?? localTo.AddDays(-6)).Date;
        if (localFrom > localTo) (localFrom, localTo) = (localTo, localFrom);
        // Tarama maliyeti sınırı: tek istekte en fazla 400 günlük pencere
        if ((localTo - localFrom).TotalDays > 400) localFrom = localTo.AddDays(-400);

        var fromUtc = DateTime.SpecifyKind(localFrom, DateTimeKind.Local).ToUniversalTime();
        var toUtc = DateTime.SpecifyKind(localTo.AddDays(1), DateTimeKind.Local).ToUniversalTime();
        return (fromUtc, toUtc);
    }

    private static object ToDto(AuditEntry e) => new
    {
        ts = e.Ts,
        user = e.User,
        userId = e.UserId,
        action = e.Action,
        actionLabel = AuditFieldLabels.ActionLabels.GetValueOrDefault(e.Action, e.Action),
        entity = e.Entity,
        entityLabel = AuditFieldLabels.EntityLabel(e.Entity),
        entityId = e.EntityId,
        title = e.Title,
        // 2026-07-21 (PageComment Seq 20 geri bildirimi): etiket+değer çevirisi TEK noktadan
        // (AuditFieldLabels.ResolveChange) — hem eksik Türkçe etiketleri hem ham enum/bool
        // değerlerini görüntüleme anında çözer, eski log kayıtları da otomatik düzelir.
        changes = e.Changes?.Select(c =>
        {
            var (label, oldVal, newVal) = AuditFieldLabels.ResolveChange(e.Entity, e.Src, c);
            return new { field = c.Field, label, old = oldVal, @new = newVal };
        }),
        detail = e.Detail,
        ip = e.Ip,
        src = e.Src,
    };
}
