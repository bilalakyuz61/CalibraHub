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

    public AuditLogController(
        IAuditQueryService auditQuery, IWidgetService widgetService, IPermissionService permService)
    {
        _auditQuery = auditQuery;
        _widgetService = widgetService;
        _permService = permService;
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
        string? entity, string? user, string? text,
        int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        var (fromUtc, toUtc) = NormalizeRange(from, to);
        var result = await _auditQuery.SearchAsync(
            new AuditSearchRequest(fromUtc, toUtc, action, entity, user, text, page, pageSize), ct);

        return Json(new
        {
            ok = true,
            items = result.Items.Select(ToDto),
            total = result.Total,
            page,
            pageSize,
            facets = new
            {
                entities = result.Entities.Select(e => new { code = e, label = AuditFieldLabels.EntityLabel(e) }),
                users = result.Users,
                actions = AuditActions.All.Select(a => new
                {
                    code = a,
                    label = AuditFieldLabels.ActionLabels.GetValueOrDefault(a, a),
                }),
            },
        });
    }

    [HttpGet("/AuditLog/Stats")]
    [PermissionScope(FormCodes.AuditLog)]
    public async Task<IActionResult> Stats(DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var (fromUtc, toUtc) = NormalizeRange(from, to);
        var stats = await _auditQuery.GetStatsAsync(fromUtc, toUtc, ct);
        return Json(new { ok = true, stats });
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
        changes = e.Changes?.Select(c => new { field = c.Field, label = c.Label ?? c.Field, old = c.Old, @new = c.New }),
        detail = e.Detail,
        ip = e.Ip,
        src = e.Src,
    };
}
