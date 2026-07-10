namespace CalibraHub.Application.Auditing;

/// <summary>Log satırına damgalanacak ortam bilgisi.</summary>
/// <param name="CompanyId">Aktif şirket (claim "company_id"); çözümlenemezse 0.</param>
/// <param name="UserId">Aktif kullanıcı Id (NameIdentifier claim).</param>
/// <param name="UserName">Kullanıcı adı/e-posta; anonim ise null.</param>
/// <param name="Ip">İstemci IP adresi; background akışta null.</param>
public sealed record AuditContext(int CompanyId, int? UserId, string? UserName, string? Ip);

/// <summary>
/// Aktif isteğin kullanıcı/şirket/IP bilgisini çözümler. Web katmanında
/// IHttpContextAccessor tabanlı implement edilir (HttpAuditContextProvider);
/// HttpContext yoksa (background) boş context döner.
/// </summary>
public interface IAuditContextProvider
{
    AuditContext Resolve();
}
