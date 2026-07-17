using CalibraHub.Application.Constants;
using CalibraHub.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// SQL View Yönetimi — Faz 2 React ekranının host controller'ı.
///
/// API zaten <see cref="ViewBuilderController"/>'da (api/view-builder/*, Faz 1); bu controller
/// yalnızca React mount noktasını barındıran sayfayı döndürür. Aynı sınıfta iki controller
/// olamayacağı için (ViewBuilderController zaten [ApiController] + api/view-builder route'unu
/// kullanıyor) ayrı bir dosya/sınıf olarak eklendi — CLAUDE.md "SQL View Yönetimi — Kontrollü
/// İstisna" (2026-07-17): SystemAdmin-only, DatabaseMetadataController/LegacyMigrationController
/// ile BİREBİR aynı erişim deseni ([Authorize] + [PermissionScope(FormCodes.SetupDefinitions)]).
/// </summary>
[Authorize]
[PermissionScope(FormCodes.SetupDefinitions)]
public sealed class ViewBuilderPageController : Controller
{
    // GET /ViewBuilder
    [HttpGet("/ViewBuilder")]
    public IActionResult Index() => View("~/Views/ViewBuilder/Index.cshtml");
}
