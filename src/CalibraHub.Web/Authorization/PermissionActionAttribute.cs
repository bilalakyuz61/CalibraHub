namespace CalibraHub.Web.Authorization;

/// <summary>
/// 2026-07-18 — Action seviyesinde **AÇIK** izin kodu tanımı. Verildiğinde
/// <see cref="PermissionEnforcementFilter"/> isim-kalıbı tahminini (ResolveActionCodes)
/// tamamen devre dışı bırakır ve buradaki kodları kullanır.
///
/// **Neden var:** İzin kodunu action ADINDAN tahmin etmek kırılgandır — ada uymayan iş
/// aksiyonları (ChangeStatus, Convert*, Approve*, Revise, Toggle*, StockIn/Out ...) eskiden
/// sessizce kontrolsüz geçiyordu (fail-open). Artık varsayılan fail-closed; ama bir action'ın
/// doğru kodu varsayılandan farklıysa tahmine güvenmek yerine burada AÇIKÇA belirt.
///
/// **Kullanım:**
/// <code>
/// [PermissionAction("VIEW", "VIEW_OWN")]            // salt-okunur POST (önizleme/export/hesap)
/// [PermissionAction("DELETE_OWN", "DELETE_ALL")]    // ada uymayan silme (EmptyTrash gibi)
/// [PermissionAction]                                // bilinçli olarak izin kontrolü YOK
/// </code>
///
/// Kod listesi boş bırakılırsa (parametresiz kullanım) o action için izin kontrolü yapılmaz —
/// bu bilinçli bir muafiyettir, gerekçesini yorumda belirt.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class PermissionActionAttribute : Attribute
{
    /// <summary>Bu action için yeterli sayılan izin kodları (herhangi biri yeterli).</summary>
    public string[] ActionCodes { get; }

    public PermissionActionAttribute(params string[] actionCodes)
    {
        ActionCodes = actionCodes is null
            ? Array.Empty<string>()
            : actionCodes.Where(c => !string.IsNullOrWhiteSpace(c)).ToArray();
    }
}
