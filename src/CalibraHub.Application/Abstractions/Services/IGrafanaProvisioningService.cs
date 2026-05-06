using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Abstractions.Services;

public sealed record GrafanaDashboardSummary(
    string Uid,
    string Title,
    string? FolderTitle,
    IReadOnlyList<string> Tags,
    string Url);

// Per-company Grafana org/datasource/dashboard provisioning. Tum metodlar
// idempotent: ayni companyId/userId ile tekrar cagrilirsa duplicate yaratmaz.
// Implementation HTTP basarisizliginda exception firlatmamali — caller log
// gorur, CalibraHub flow'u devam eder.
public interface IGrafanaProvisioningService
{
    bool IsEnabled { get; }

    // Org create-or-find. Donus: Grafana org id (orgId), Enabled=false ise 0.
    Task<int> EnsureOrganizationAsync(int companyId, string companyName, CancellationToken ct);

    // Per-org MSSQL datasource. Connection string Grafana'nin kendi secure
    // storage'ina yazilir (dpapi degil).
    Task EnsureDataSourceAsync(int orgId, string companyName, string connectionString, CancellationToken ct);

    // Default dashboard'lari (sales-overview, customer-aging, ...) hedef org'a
    // import eder. Mevcutsa version bump ile overwrite.
    Task ProvisionDefaultDashboardsAsync(int orgId, CancellationToken ct);

    // Kullaniciyi org'a istenen rolde ekler (idempotent: rol farkli ise update).
    Task EnsureUserOrganizationMembershipAsync(
        int orgId,
        string username,
        string email,
        string fullName,
        GrafanaRole role,
        CancellationToken ct);

    // Kullaniciyi org'tan cikarir. Idempotent: kullanici zaten yoksa veya org
    // yoksa sessiz no-op. UserProfile.GrafanaRole NULL'a cevrildiginde cagirilir.
    Task RemoveUserFromOrganizationAsync(int orgId, string username, CancellationToken ct);

    // Hedef org icindeki dashboard listesi. HTTP fail durumunda bos liste doner
    // (warning log uretilir). Frontend kart listesi icin kullanilir.
    Task<IReadOnlyList<GrafanaDashboardSummary>> ListDashboardsAsync(int orgId, CancellationToken ct);
}
