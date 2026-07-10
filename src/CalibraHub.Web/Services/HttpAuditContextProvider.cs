using System.Security.Claims;
using CalibraHub.Application.Auditing;

namespace CalibraHub.Web.Services;

/// <summary>
/// Aktif HTTP isteğinden audit context'i (şirket/kullanıcı/IP) çözümler.
/// Singleton kaydedilir — IHttpContextAccessor thread-safe'tir. HttpContext yoksa
/// (background job) boş context döner; arka plan akışları AuditActor ile kendini tanıtır.
/// </summary>
public sealed class HttpAuditContextProvider : IAuditContextProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpAuditContextProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public AuditContext Resolve()
    {
        var http = _httpContextAccessor.HttpContext;
        var user = http?.User;
        if (user?.Identity?.IsAuthenticated != true)
            return new AuditContext(0, null, null, http?.Connection.RemoteIpAddress?.ToString());

        var companyId = int.TryParse(user.FindFirstValue("company_id"), out var cid) ? cid : 0;
        int? userId = int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : null;

        var name = user.Identity!.Name;
        if (string.IsNullOrWhiteSpace(name))
            name = user.FindFirstValue(ClaimTypes.Email);
        if (name is { Length: > 120 })
            name = name[..120];

        return new AuditContext(companyId, userId,
            string.IsNullOrWhiteSpace(name) ? null : name,
            http!.Connection.RemoteIpAddress?.ToString());
    }
}
