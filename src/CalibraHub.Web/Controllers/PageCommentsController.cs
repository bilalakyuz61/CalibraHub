using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Auditing;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Security;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Sayfa-içi Yorum sistemi — iki erişim yüzeyi tek controller'da:
///
/// 1) KULLANICI ENDPOINT'LERİ (cookie + rol): yalnızca DepartmentManager veya SystemAdmin.
///    Sıradan kullanıcı 403 alır. Global antiforgery filtresi normal şekilde geçerlidir —
///    frontend JSON POST'larda RequestVerificationToken header'ı göndermelidir (bkz.
///    AiFloatingButton.jsx getCsrf() deseni — aynı "her sayfada mount edilen admin widget'ı"
///    kategorisi; bu yüzden CSRF muafiyeti UYGULANMADI).
///
/// 2) KATMAN-4 AJAN ENDPOINT'LERİ (agent-queue, agent-apply): cookie/rol pipeline'ına HİÇ
///    girmez — [AllowAnonymous] + elle üçlü gate (env + loopback + X-Agent-Key). Üçünden biri
///    bile sağlanmazsa 404 (403 değil — üretimde varlığını gizler). Bu iki endpoint global
///    antiforgery filtresinden [IgnoreAntiforgeryToken] ile muaftır (anonim istekte CSRF
///    cookie/token zaten yoktur).
/// </summary>
[Authorize]
[ApiController]
[Route("api/page-comments")]
public sealed class PageCommentsController : ControllerBase
{
    private readonly IPageCommentService _service;
    private readonly IWebHostEnvironment _env;
    private readonly CalibraHub.Application.Configuration.PageCommentsOptions _options;
    private readonly CompanyConnectionRegistry _companyRegistry;

    public PageCommentsController(
        IPageCommentService service,
        IWebHostEnvironment env,
        CalibraHub.Application.Configuration.PageCommentsOptions options,
        CompanyConnectionRegistry companyRegistry)
    {
        _service = service;
        _env = env;
        _options = options;
        _companyRegistry = companyRegistry;
    }

    // ════════════════════════════════════════════════════════════════════
    // KULLANICI ENDPOINT'LERİ (admin-only)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>GET /api/page-comments — ScreenKey'e göre gruplanmış tüm yorumlar (Seq ASC).</summary>
    [HttpGet]
    public async Task<IActionResult> GetGrouped(CancellationToken ct)
    {
        if (!IsAdminOrSystemAdmin()) return Forbid();

        var comments = await _service.GetAllAsync(ct);
        var grouped = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var group in comments.GroupBy(c => c.ScreenKey))
        {
            grouped[group.Key] = new { comments = group.Select(ToCommentJson).ToList() };
        }
        return Ok(grouped);
    }

    /// <summary>POST /api/page-comments/comment — yeni yorum ekler.</summary>
    [HttpPost("comment")]
    [CalibraHub.Web.Authorization.PermissionGateReviewed("IsAdminOrSystemAdmin() -> Forbid()")]
    public async Task<IActionResult> CreateComment([FromBody] CreatePageCommentInput input, CancellationToken ct)
    {
        if (!IsAdminOrSystemAdmin()) return Forbid();
        if (input is null) return BadRequest(new { ok = false, error = "İstek gövdesi boş." });

        var dto = await _service.CreateAsync(input, ResolveCurrentUserName(), ct);
        if (dto is null) return BadRequest(new { ok = false, error = "Geçersiz istek (key/id/text zorunlu)." });

        return Ok(new { ok = true, comment = ToCommentJson(dto) });
    }

    /// <summary>POST /api/page-comments/comment/status — durum değişikliği + revizyon kaydı.</summary>
    [HttpPost("comment/status")]
    [CalibraHub.Web.Authorization.PermissionGateReviewed("IsAdminOrSystemAdmin() -> Forbid()")]
    public async Task<IActionResult> ChangeStatus([FromBody] ChangePageCommentStatusInput input, CancellationToken ct)
    {
        if (!IsAdminOrSystemAdmin()) return Forbid();
        if (input is null) return BadRequest(new { ok = false, error = "İstek gövdesi boş." });

        var dto = await _service.ChangeStatusAsync(input, ResolveCurrentUserName(), "comment.status", null, ct);
        if (dto is null) return NotFound(new { ok = false, error = "Yorum bulunamadı." });

        return Ok(new { ok = true, comment = ToCommentJson(dto) });
    }

    /// <summary>POST /api/page-comments/comment/edit — yorum metnini günceller.</summary>
    [HttpPost("comment/edit")]
    [CalibraHub.Web.Authorization.PermissionGateReviewed("IsAdminOrSystemAdmin() -> Forbid()")]
    public async Task<IActionResult> EditText([FromBody] EditPageCommentTextInput input, CancellationToken ct)
    {
        if (!IsAdminOrSystemAdmin()) return Forbid();
        if (input is null) return BadRequest(new { ok = false, error = "İstek gövdesi boş." });

        var dto = await _service.EditTextAsync(input, ResolveCurrentUserName(), ct);
        if (dto is null) return NotFound(new { ok = false, error = "Yorum bulunamadı." });

        return Ok(new { ok = true, comment = ToCommentJson(dto) });
    }

    /// <summary>POST /api/page-comments/comment/delete — yorumu siler (CASCADE: revizyon + görsel).</summary>
    [HttpPost("comment/delete")]
    [CalibraHub.Web.Authorization.PermissionGateReviewed("IsAdminOrSystemAdmin() -> Forbid()")]
    public async Task<IActionResult> DeleteComment([FromBody] DeletePageCommentInput input, CancellationToken ct)
    {
        if (!IsAdminOrSystemAdmin()) return Forbid();
        if (input is null || string.IsNullOrWhiteSpace(input.Id))
            return BadRequest(new { ok = false, error = "İstek gövdesi boş." });

        var deleted = await _service.DeleteAsync(input.Id, ct);
        return Ok(new { ok = deleted });
    }

    /// <summary>POST /api/page-comments/comment/image — base64 görsel ekler (ör. ekran görüntüsü).</summary>
    [HttpPost("comment/image")]
    [RequestSizeLimit(20L * 1024 * 1024)]
    [CalibraHub.Web.Authorization.PermissionGateReviewed("IsAdminOrSystemAdmin() -> Forbid()")]
    public async Task<IActionResult> AddImage([FromBody] AddPageCommentImageInput input, CancellationToken ct)
    {
        if (!IsAdminOrSystemAdmin()) return Forbid();
        if (input is null) return BadRequest(new { ok = false, error = "İstek gövdesi boş." });

        var image = await _service.AddImageAsync(input, ct);
        if (image is null)
            return BadRequest(new { ok = false, error = "Görsel eklenemedi (yorum bulunamadı veya veri geçersiz)." });

        return Ok(new { ok = true, image = new { id = image.Id, mime = image.Mime, name = image.Name } });
    }

    /// <summary>GET /api/page-comments/comment/image/{id} — ikili görsel indirme.</summary>
    [HttpGet("comment/image/{id}")]
    public async Task<IActionResult> GetImage(string id, CancellationToken ct)
    {
        if (!IsAdminOrSystemAdmin()) return Forbid();

        var result = await _service.GetImageBinaryAsync(id, ct);
        if (result is null) return NotFound();
        return File(result.Value.Bytes, result.Value.Mime);
    }

    /// <summary>
    /// GET /api/page-comments/agent-image/{id} — Katman-4 otonom döngünün ekli görsel okuma yüzeyi.
    /// GetImage ile aynı, ama cookie/rol yerine agent gate (env + loopback + X-Agent-Key) + companyId
    /// per-company override (SystemAdmin görselleri companyId=0 sistem DB'sinde). Döngü, resim ekli
    /// istekleri (ör. "resimdeki gibi yap") işleyebilmek için ekli ekran görüntülerini buradan çeker.
    /// </summary>
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [HttpGet("agent-image/{id}")]
    public async Task<IActionResult> AgentImage(string id, [FromQuery] int? companyId, CancellationToken ct)
    {
        if (!TryGateAgentAccess(out var deny)) return deny!;
        if (!TryResolveAgentCompany(companyId, out var resolvedCompanyId, out var companyError)) return companyError!;
        if (resolvedCompanyId != 0) HttpContext.Items["__override_company_id"] = resolvedCompanyId;

        var result = await _service.GetImageBinaryAsync(id, ct);
        if (result is null) return NotFound();
        return File(result.Value.Bytes, result.Value.Mime);
    }

    /// <summary>POST /api/page-comments/comment/image/delete — görseli siler.</summary>
    [HttpPost("comment/image/delete")]
    [CalibraHub.Web.Authorization.PermissionGateReviewed("IsAdminOrSystemAdmin() -> Forbid()")]
    public async Task<IActionResult> DeleteImage([FromBody] DeletePageCommentImageInput input, CancellationToken ct)
    {
        if (!IsAdminOrSystemAdmin()) return Forbid();
        if (input is null || string.IsNullOrWhiteSpace(input.Id))
            return BadRequest(new { ok = false, error = "İstek gövdesi boş." });

        var deleted = await _service.DeleteImageAsync(input.Id, ct);
        return Ok(new { ok = deleted });
    }

    /// <summary>GET /api/page-comments/activity — son N olay (Created DESC, varsayılan 300).</summary>
    [HttpGet("activity")]
    public async Task<IActionResult> GetActivity([FromQuery] int take = 300, CancellationToken ct = default)
    {
        if (!IsAdminOrSystemAdmin()) return Forbid();

        var items = await _service.GetActivityAsync(take, ct);
        return Ok(items.Select(a => new
        {
            id = a.Id,
            action = a.Action,
            screenKey = a.ScreenKey,
            detail = a.Detail,
            ts = a.Ts,
            created = a.Created,
        }));
    }

    // ════════════════════════════════════════════════════════════════════
    // KATMAN-4 AJAN ENDPOINT'LERİ — cookie/rol DEĞİL, loopback+dev-key gated
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// GET /api/page-comments/agent-queue — otonom döngünün okuma yüzeyi: Status='approved'
    /// yorumlar, Seq ASC, tam bağlam (id/seq/screenKey/pageTitle/formCode/text/ts/author/images).
    /// Tek şirket kayıtlıysa companyId opsiyoneldir (otomatik çözülür); birden fazla şirket varsa zorunludur.
    /// </summary>
    [HttpGet("agent-queue")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> AgentQueue([FromQuery] int? companyId, CancellationToken ct)
    {
        if (!TryGateAgentAccess(out var deny)) return deny!;
        if (!TryResolveAgentCompany(companyId, out var resolvedCompanyId, out var companyError)) return companyError!;

        if (resolvedCompanyId != 0) HttpContext.Items["__override_company_id"] = resolvedCompanyId;

        var items = await _service.GetApprovedQueueAsync(ct);
        return Ok(items.Select(c => new
        {
            id = c.Id,
            seq = c.Seq,
            screenKey = c.ScreenKey,
            pageTitle = c.PageTitle,
            formCode = c.FormCode,
            text = c.Text,
            ts = c.Ts,
            author = c.Author,
            images = c.Images.Select(i => new { id = i.Id, mime = i.Mime, name = i.Name }).ToList(),
        }));
    }

    /// <summary>
    /// POST /api/page-comments/agent-apply — comment/status ile AYNI mantık (revizyon + activity),
    /// ajan için ayrı giriş noktası. status genelde 'applied', ama 'abandoned'/'revised' de olabilir.
    /// </summary>
    [HttpPost("agent-apply")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> AgentApply(
        [FromBody] ChangePageCommentStatusInput input, [FromQuery] int? companyId, CancellationToken ct)
    {
        if (!TryGateAgentAccess(out var deny)) return deny!;
        if (input is null) return BadRequest(new { ok = false, error = "İstek gövdesi boş." });
        if (!TryResolveAgentCompany(companyId, out var resolvedCompanyId, out var companyError)) return companyError!;

        if (resolvedCompanyId != 0) HttpContext.Items["__override_company_id"] = resolvedCompanyId;

        var actingUser = string.IsNullOrWhiteSpace(input.User) ? "agent" : input.User.Trim();
        // HttpAuditContextProvider anonim istekte CompanyId=0 döner (yalnızca authenticated cookie
        // context'ini çözer) — merkezi audit trail bu satır olmadan SESSİZCE düşerdi (companyId<=0 guard'ı).
        var actor = new AuditActor(
            CompanyId: resolvedCompanyId,
            UserId: null,
            UserName: actingUser,
            Ip: HttpContext.Connection.RemoteIpAddress?.ToString(),
            Source: "Agent");

        var dto = await _service.ChangeStatusAsync(input, actingUser, "comment.agent_apply", actor, ct);
        if (dto is null) return NotFound(new { ok = false, error = "Yorum bulunamadı." });

        return Ok(new { ok = true, comment = ToCommentJson(dto) });
    }

    // ── Yardımcılar ──────────────────────────────────────────────────────

    /// <summary>DepartmentManager veya SystemAdmin — mevcut rol kontrol deseni (bkz. CollaborationController.IsSystemAdmin).</summary>
    private bool IsAdminOrSystemAdmin()
    {
        var roleString = User.FindFirstValue(ClaimTypes.Role);
        return UserAuthorizationCatalog.TryParseRole(roleString, out var role) &&
               (role == UserRole.SystemAdmin || role == UserRole.DepartmentManager);
    }

    private string? ResolveCurrentUserName()
    {
        var name = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name)) name = User.FindFirstValue(ClaimTypes.Email);
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <summary>
    /// Üçlü gate — hepsi sağlanmalı: (1) Development VEYA config AgentAccessEnabled,
    /// (2) istemci loopback, (3) X-Agent-Key header'ı sabit-zamanlı eşleşiyor. Herhangi biri
    /// eksikse 404 (403 DEĞİL — üretimde endpoint'in varlığını gizler).
    /// </summary>
    private bool TryGateAgentAccess(out IActionResult? deny)
    {
        deny = null;

        var envOk = _env.IsDevelopment() || _options.AgentAccessEnabled;
        if (!envOk) { deny = NotFound(); return false; }

        var remoteIp = HttpContext.Connection.RemoteIpAddress;
        if (remoteIp is null || !IPAddress.IsLoopback(remoteIp)) { deny = NotFound(); return false; }

        var providedKey = Request.Headers["X-Agent-Key"].ToString();
        if (string.IsNullOrEmpty(_options.AgentKey) ||
            string.IsNullOrEmpty(providedKey) ||
            !SecureEquals(providedKey, _options.AgentKey))
        {
            deny = NotFound();
            return false;
        }

        return true;
    }

    private static bool SecureEquals(string a, string b)
    {
        var ab = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        return ab.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ab, bb);
    }

    /// <summary>
    /// Anonim ajan isteğinde per-company DB seçimi — QuickApprovalController/WhatsAppBridgeEventsController
    /// ile aynı desen (HttpContext.Items["__override_company_id"]). companyId verilmezse ve tek
    /// şirket kayıtlıysa otomatik seçilir (tipik tek-şirket dev/test kurulumu); birden fazla
    /// şirket varsa açıkça istenir (yanlış şirkete sessizce yazmayı önler).
    /// </summary>
    private bool TryResolveAgentCompany(int? companyId, out int resolvedCompanyId, out IActionResult? error)
    {
        error = null;

        if (companyId.HasValue)
        {
            // 0 = sistem/ana DB (SystemAdmin yorumları buraya yazılır — company_id claim
            // per-company'ye çözülmediğinde _systemConnectionString kullanılır). Override konmaz.
            if (companyId.Value == 0)
            {
                resolvedCompanyId = 0;
                return true;
            }
            if (!_companyRegistry.TryGet(companyId.Value, out _))
            {
                resolvedCompanyId = 0;
                error = NotFound(new { ok = false, error = $"Şirket bulunamadı: {companyId.Value}" });
                return false;
            }
            resolvedCompanyId = companyId.Value;
            return true;
        }

        var ids = _companyRegistry.GetCompanyIds();
        if (ids.Count == 1)
        {
            resolvedCompanyId = ids.First();
            return true;
        }

        resolvedCompanyId = 0;
        error = BadRequest(new
        {
            ok = false,
            error = "Birden fazla şirket kayıtlı — companyId query parametresi zorunlu (0 = sistem/ana DB, SystemAdmin yorumları oraya düşer).",
            companies = ids,
        });
        return false;
    }

    private static object ToCommentJson(PageCommentDto c) => new
    {
        id = c.Id,
        seq = c.Seq,
        screenKey = c.ScreenKey,
        pageTitle = c.PageTitle,
        formCode = c.FormCode,
        text = c.Text,
        ts = c.Ts,
        author = c.Author,
        status = c.Status,
        appliedVersion = c.AppliedVersion,
        resolvedBy = c.ResolvedBy,
        resolvedAt = c.ResolvedAt,
        devNote = c.DevNote,
        revisions = c.Revisions.Select(r => new
        {
            status = r.Status,
            version = r.Version,
            note = r.Note,
            userName = r.UserName,
            createdAt = r.CreatedAt,
        }).ToList(),
        images = c.Images.Select(i => new { id = i.Id, mime = i.Mime, name = i.Name }).ToList(),
    };
}
