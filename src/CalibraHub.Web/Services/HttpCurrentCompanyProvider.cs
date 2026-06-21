using CalibraHub.Application.Abstractions.Services;
using System.Security.Claims;

namespace CalibraHub.Web.Services;

public sealed class HttpCurrentCompanyProvider : ICurrentCompanyProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpCurrentCompanyProvider(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor;

    public int GetCurrentCompanyId()
    {
        var ctx = _httpContextAccessor.HttpContext;
        if (ctx?.User.Identity?.IsAuthenticated != true) return 0;
        var raw = ctx.User.FindFirst("company_id")?.Value;
        return int.TryParse(raw, out var id) ? id : 0;
    }

    public string? GetBaseUrl()
    {
        var ctx = _httpContextAccessor.HttpContext;
        if (ctx is null) return null;
        return $"{ctx.Request.Scheme}://{ctx.Request.Host}";
    }
}
