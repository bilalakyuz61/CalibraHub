using System.Text.Json.Nodes;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// MappingEngine'in urettigi JSON body'sini hedef REST endpoint'e gonderen executor.
/// integration_api_profiles tablosundan auth config'i (AuthType + AuthConfigJson) cozer.
///
/// Desteklenen auth tipleri (V1):
///   - "None"   : auth header eklenmez
///   - "ApiKey" : config = { "headerName": "X-API-Key", "key": "..." }
///   - "Basic"  : config = { "username": "...", "password": "..." }
///   - "Bearer" : config = { "token": "..." }
///
/// V2: OAuth2 (token refresh), AWS Signature, mTLS.
/// </summary>
public interface IHttpExecutor
{
    Task<HttpInvocationResult> SendAsync(
        IntegrationEndpoint endpoint,
        IntegrationApiProfile profile,
        JsonObject body,
        CancellationToken ct);
}

/// <summary>HttpExecutor sonucu — runtime tarafindan IntegrationRun log'a yazilir.</summary>
public sealed class HttpInvocationResult
{
    public bool Success { get; init; }
    public int? StatusCode { get; init; }
    public string? RequestBody { get; init; }
    public string? ResponseBody { get; init; }
    public string? ErrorMessage { get; init; }
    public int DurationMs { get; init; }
}
