using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CalibraHub.Web.Infrastructure.Security;

/// <summary>
/// Protect ettigi action/controller'a sadece TOTP gate'inden gecmis kullanicinin erisebilecegini garantiler.
/// Gate'den gecmis kullanicinin Session["GateUnlockedAt"] degeri set olmustur; TTL dolduysa tekrar kod ister.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class GateProtectedAttribute : Attribute, IAsyncAuthorizationFilter
{
    public Task OnAuthorizationAsync(AuthorizationFilterContext ctx)
    {
        var session = ctx.HttpContext.Session;
        var rawTs = session.GetString("GateUnlockedAt");
        if (!string.IsNullOrEmpty(rawTs)
            && long.TryParse(rawTs, out var unix)
            && DateTimeOffset.FromUnixTimeSeconds(unix) > DateTimeOffset.UtcNow.AddMinutes(-30))
        {
            return Task.CompletedTask;
        }

        var returnUrl = ctx.HttpContext.Request.Path + ctx.HttpContext.Request.QueryString;
        ctx.Result = new RedirectToActionResult("Index", "Gate",
            new { returnUrl = returnUrl.ToString() });
        return Task.CompletedTask;
    }
}
