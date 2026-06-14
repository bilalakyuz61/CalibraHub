namespace CalibraHub.Web.Authorization;

/// <summary>
/// 2026-06-07 — Controller veya action'a izin kontrolü kapsamı tanımlar. PermissionEnforcementFilter
/// bu attribute'u bulunca otomatik HTTP method → action code map yapar:
///
///   GET                 → VIEW
///   POST + Save*/Update*/Create* → CREATE | EDIT_OWN | EDIT_ALL (herhangi biri yeterli)
///   POST + Delete*/Remove*       → DELETE_OWN | DELETE_ALL
///   POST (diğer)        → kontrol yapılmaz (özel iş aksiyonu, BUTTON:* ile ayrıca tanımla)
///
/// **Kullanım:**
/// <code>
/// [PermissionScope("PERSONNEL")]               // ← Controller seviyesi: tüm action'lara
/// public class PersonnelController : Controller
/// {
///     [HttpGet] public IActionResult Index() { ... }        // VIEW
///     [HttpPost] public IActionResult Save(...) { ... }     // CREATE/EDIT
///     [HttpPost] public IActionResult Delete(...) { ... }   // DELETE
///
///     [PermissionScope("PERSONNEL_EDIT")]      // ← Action seviyesi: override
///     [HttpGet] public IActionResult Edit(int id) { ... }
/// }
/// </code>
///
/// **SystemAdmin shortcut:** rolu SystemAdmin olan kullanıcılar bu filtreden her zaman geçer.
/// **Anonymous endpoint:** [AllowAnonymous] varsa filter atlar (cookie auth zaten önce çalışır).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class PermissionScopeAttribute : Attribute
{
    public string FormCode { get; }

    public PermissionScopeAttribute(string formCode)
    {
        if (string.IsNullOrWhiteSpace(formCode))
            throw new ArgumentException("FormCode zorunlu.", nameof(formCode));
        FormCode = formCode;
    }
}
