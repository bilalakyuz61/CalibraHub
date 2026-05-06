namespace CalibraHub.Application.Configuration;

public sealed class GrafanaOptions
{
    public const string SectionName = "Grafana";

    public bool Enabled { get; set; }

    public string Url { get; set; } = "http://127.0.0.1:61005";

    public string PublicPath { get; set; } = "/grafana";

    public string AdminUser { get; set; } = "admin";

    public string AdminPassword { get; set; } = string.Empty;

    public string OrgNamePrefix { get; set; } = "Calibra_";

    public string DefaultDashboardFolder { get; set; } = "CalibraHub Default";
}
