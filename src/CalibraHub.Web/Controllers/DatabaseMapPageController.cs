using CalibraHub.Application.Constants;
using CalibraHub.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Veritabanı Haritası — React ekranının host controller'ı.
///
/// Veri ucu <see cref="DatabaseMetadataController"/> içinde (GET /api/database/map); bu sınıf
/// yalnız mount noktasını barındıran sayfayı döndürür. Ayrı dosya olmasının sebebi
/// <see cref="ViewBuilderPageController"/> ile aynı: DatabaseMetadataController zaten
/// [ApiController] + api/database route'unu kullanıyor, sayfa action'ı oraya sığmaz.
///
/// Erişim SystemAdmin-only ([PermissionScope(SetupDefinitions)]) — ekran şemanın tamamını
/// (tablo/kolon adları, satır sayıları) gösterir; DatabaseMetadataController ve /ViewBuilder
/// ile birebir aynı kapı.
/// </summary>
[Authorize]
[PermissionScope(FormCodes.SetupDefinitions)]
public sealed class DatabaseMapPageController : Controller
{
    // GET /DatabaseMap
    [HttpGet("/DatabaseMap")]
    public IActionResult Index() => View("~/Views/DatabaseMap/Index.cshtml");
}
