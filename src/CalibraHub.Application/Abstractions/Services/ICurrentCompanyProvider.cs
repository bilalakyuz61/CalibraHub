namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Mevcut request'in şirket ID'sini ve dış erişim base URL'ini çözer.
/// Web katmanında IHttpContextAccessor ile implemente edilir.
/// QuickApproval gibi kimlik doğrulama gerektirmeyen endpoint'lerde
/// null / 0 dönebilir.
/// </summary>
public interface ICurrentCompanyProvider
{
    int GetCurrentCompanyId();
    string? GetBaseUrl();
}
