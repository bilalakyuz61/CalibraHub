using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Razor sayfalari. API endpoint'leri ReportingApiController icinde.
/// </summary>
[Authorize]
public sealed class ReportingController : Controller
{
    [HttpGet]
    public IActionResult Designer() => View();
}
