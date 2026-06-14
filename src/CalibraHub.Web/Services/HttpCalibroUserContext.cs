using System.Security.Claims;
using CalibraHub.Application.Services.Ai.Tools;

namespace CalibraHub.Web.Services;

/// <summary>
/// 2026-05-24 — HttpContext'ten Calibo kullanici bilgisini cikarir (scoped).
/// </summary>
public sealed class HttpCalibroUserContext : ICalibroUserContext
{
    private readonly IHttpContextAccessor _accessor;

    public HttpCalibroUserContext(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public int UserId
    {
        get
        {
            var raw = _accessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(raw, out var id) ? id : 0;
        }
    }

    public string? UserName => _accessor.HttpContext?.User?.Identity?.Name;
}
