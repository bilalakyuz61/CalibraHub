namespace CalibraHub.Application.Auditing;

/// <summary>
/// Şirket bazlı log saklama süresini (gün) çözümler. Persistence katmanında
/// CompanyConnectionRegistry üzerinden ilgili şirket DB'sindeki
/// CompanyParameter (SECURITY / AUDIT_RETENTION_DAYS) kaydı okunarak implement edilir.
/// Parametre tanımsız veya DB erişilemezse varsayılan döner.
/// </summary>
public interface IAuditRetentionResolver
{
    Task<int> GetRetentionDaysAsync(int companyId, CancellationToken ct);
}
