using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace CalibraHub.Designer;

/// <summary>CalibraHub Web API ile iletişim kurar.</summary>
public sealed class CalibraHubClient
{
    private static readonly HttpClient Http = new();

    public async Task<TemplateData?> GetTemplateAsync(string baseUrl, string templateId)
    {
        var response = await Http.GetAsync($"{baseUrl.TrimEnd('/')}/api/designer/template/{templateId}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TemplateData>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    public async Task SaveTemplateAsync(string baseUrl, string templateId, string frxContent)
    {
        var body = JsonSerializer.Serialize(new { frxContent });
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await Http.PostAsync(
            $"{baseUrl.TrimEnd('/')}/api/designer/template/{templateId}", content);
        response.EnsureSuccessStatusCode();
    }
}

public sealed record TemplateData(string Id, string Name, string? Type, string? FrxContent);
