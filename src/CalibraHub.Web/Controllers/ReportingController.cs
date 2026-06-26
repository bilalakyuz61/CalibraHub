using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Razor sayfalari. API endpoint'leri ReportingApiController icinde.
/// </summary>
[Authorize]
[CalibraHub.Web.Authorization.PermissionScope(CalibraHub.Application.Constants.FormCodes.ReportDesigner)]
public sealed class ReportingController : Controller
{
    [HttpGet]
    public IActionResult Designer() => View();
}
