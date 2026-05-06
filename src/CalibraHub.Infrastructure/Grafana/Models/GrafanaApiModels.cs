using System.Text.Json.Serialization;

namespace CalibraHub.Infrastructure.Grafana.Models;

internal sealed record GrafanaOrgResponse(int Id, string Name);

internal sealed record GrafanaOrgCreatedResponse([property: JsonPropertyName("orgId")] int OrgId, string Message);

internal sealed record GrafanaUserLookupResponse(int Id, string Email, string Login, string Name);

internal sealed record GrafanaUserCreatedResponse(int Id, string Message);

internal sealed record GrafanaOrgUserResponse(int OrgId, int UserId, string Email, string Login, string Role);

internal sealed record GrafanaDataSourceResponse(int Id, string Name, string Uid);

internal sealed record GrafanaDashboardImportResponse(string Status, string Slug, int Version, int Id);

// Grafana /api/search?type=dash-db yanit ogesi. Url alani relative path:
// "/d/{uid}/{slug}". Tags bos array olabilir (Grafana null donmuyor).
internal sealed record GrafanaSearchItem(
    [property: JsonPropertyName("uid")] string Uid,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("folderTitle")] string? FolderTitle,
    [property: JsonPropertyName("tags")] string[]? Tags);
