namespace CalibraHub.Domain.Enums;

/// <summary>
/// Per-org Grafana yetki seviyesi. UserProfile.GrafanaRole NULL ise kullanici
/// Grafana'ya eklenmez; bir deger set edilirse Save sirasinda
/// IGrafanaProvisioningService.EnsureUserOrganizationMembershipAsync cagrilir.
/// </summary>
public enum GrafanaRole
{
    Viewer,
    Designer,
    Admin
}
