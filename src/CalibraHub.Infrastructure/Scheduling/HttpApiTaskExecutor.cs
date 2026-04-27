using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Infrastructure.Scheduling;

/// <summary>
/// HTTP API cagrisi yapan executor.
/// ParametersJson format:
///   {
///     "url": "https://api.example.com/endpoint",
///     "method": "POST",
///     "headers": {"Authorization":"Bearer ...","X-Custom":"..."},
///     "body": "{...}",
///     "contentType": "application/json",
///     "timeoutSeconds": 60,
///     "expectedStatusCodes": [200, 201, 204]
///   }
/// </summary>
public sealed class HttpApiTaskExecutor : IScheduledTaskExecutor
{
    private readonly IHttpClientFactory _httpClientFactory;

    public HttpApiTaskExecutor(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public ScheduledTaskType SupportedType => ScheduledTaskType.HttpApi;

    public async Task<TaskExecutionResult> ExecuteAsync(ScheduledTask task, CancellationToken cancellationToken)
    {
        try
        {
            var config = ParseConfig(task.ParametersJson);
            if (string.IsNullOrWhiteSpace(config.Url))
                return TaskExecutionResult.Error("Gecersiz ParametersJson: 'url' gerekli.");

            if (!Uri.TryCreate(config.Url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return TaskExecutionResult.Error($"Gecersiz URL: '{config.Url}' (http veya https olmali).");
            }

            var method = new HttpMethod(string.IsNullOrWhiteSpace(config.Method) ? "GET" : config.Method.ToUpperInvariant());

            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds > 0 ? config.TimeoutSeconds : 60);

            using var request = new HttpRequestMessage(method, uri);

            // Headers
            if (config.Headers is not null)
            {
                foreach (var kv in config.Headers)
                {
                    // Content-Type / Authorization / diger — default header'lara eklemeye calis,
                    // olmuyorsa Content.Headers'a ekleyecegiz.
                    if (!request.Headers.TryAddWithoutValidation(kv.Key, kv.Value))
                    {
                        // Content-Type degilse ignore; content headers'a body set edildiginde donecegiz
                    }
                }
            }

            // Body
            if (!string.IsNullOrEmpty(config.Body) && method != HttpMethod.Get && method != HttpMethod.Delete)
            {
                request.Content = new StringContent(config.Body, Encoding.UTF8);
                var ct = string.IsNullOrWhiteSpace(config.ContentType) ? "application/json" : config.ContentType;
                try { request.Content.Headers.ContentType = new MediaTypeHeaderValue(ct); } catch { /* swallow */ }
            }

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
            var code = (int)response.StatusCode;
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var bodyShort = body.Length > 500 ? body[..500] + "..." : body;

            var expected = config.ExpectedStatusCodes ?? new List<int> { 200, 201, 202, 204 };
            if (!expected.Contains(code))
            {
                return TaskExecutionResult.Error($"HTTP {code} ({response.ReasonPhrase}) — beklenen: {string.Join(",", expected)}. Body: {bodyShort}");
            }

            return TaskExecutionResult.Success($"HTTP {code} ({response.ReasonPhrase}). Body-len: {body.Length}");
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            return TaskExecutionResult.Error("HTTP timeout.");
        }
        catch (OperationCanceledException) { return TaskExecutionResult.Error("Iptal edildi."); }
        catch (Exception ex) { return TaskExecutionResult.Error(ex.Message); }
    }

    private static HttpApiConfig ParseConfig(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return JsonSerializer.Deserialize<HttpApiConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            }) ?? new();
        }
        catch { return new(); }
    }

    private sealed class HttpApiConfig
    {
        public string Url { get; set; } = string.Empty;
        public string Method { get; set; } = "GET";
        public Dictionary<string, string>? Headers { get; set; }
        public string? Body { get; set; }
        public string? ContentType { get; set; }
        public int TimeoutSeconds { get; set; } = 60;
        public List<int>? ExpectedStatusCodes { get; set; }
    }
}
