using System.Text.Json;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Custom Event tetikleyicisi dispatch endpoint'i. Herhangi bir koddan/raporu basildiktan
/// sonra/job tamamlandiktan sonra bu endpoint cagrilir; eslesen Event trigger'lari
/// olan tum aktif entegrasyonlar fire-and-forget calistirilir.
///
/// Trigger config formati: { "eventName": "DocumentApproved" } (case-insensitive eslesir).
///
/// Kullanim ornekleri:
///   • Belge onaylandiginda  → POST { eventName: "DocumentApproved",  recordId: "1234" }
///   • Stok hareketi         → POST { eventName: "StockMovement",     recordId: "..." }
///   • Manuel tetikleme      → curl/Postman ile
/// </summary>
[ApiController]
[Route("api/integration-events")]
public sealed class IntegrationEventsController : ControllerBase
{
    private readonly IServiceScopeFactory _scopeFactory;

    public IntegrationEventsController(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public sealed record FireEventRequest(
        string EventName,
        string? RecordId,
        bool Wait = false);

    /// <summary>
    /// Bir custom event'i fire eder. Default fire-and-forget (200 OK hizli doner).
    /// wait=true ise tum tetiklenen entegrasyonlarin RunAsync'i tamamlanmasini bekler.
    /// </summary>
    [HttpPost("fire")]
    public async Task<IActionResult> Fire([FromBody] FireEventRequest? body, CancellationToken ct)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.EventName))
            return BadRequest(new { success = false, message = "EventName zorunlu." });

        var triggeredBy = User?.Identity?.Name ?? "Event";

        if (body.Wait)
        {
            var stats = await DispatchAsync(body.EventName, body.RecordId, triggeredBy, ct);
            return Ok(new { success = true, fired = stats.Count, succeeded = stats.Successes, failed = stats.Failures });
        }

        // Fire-and-forget — yanit hizli, hatalar IntegrationRun audit'e duser
        _ = Task.Run(async () =>
        {
            try { await DispatchAsync(body.EventName, body.RecordId, triggeredBy, default); }
            catch { /* sessizce yut */ }
        });

        return Accepted(new { success = true, queued = true, eventName = body.EventName });
    }

    private async Task<(int Count, int Successes, int Failures)> DispatchAsync(
        string eventName, string? recordId, string triggeredBy, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo   = scope.ServiceProvider.GetRequiredService<IIntegrationRepository>();
        var runner = scope.ServiceProvider.GetRequiredService<IIntegrationRunner>();

        var candidates = await repo.ListByTriggerTypeAsync(IntegrationTriggerType.Event, ct);
        int count = 0, ok = 0, fail = 0;

        foreach (var integ in candidates)
        {
            // Aggregate children DOLDURULMAZ — tetikleyici config'i icin tek tek cek
            var triggers = await repo.GetTriggersAsync(integ.Id, ct);
            var match = triggers.FirstOrDefault(t =>
                t.IsActive
                && t.TriggerType == IntegrationTriggerType.Event
                && EventNameMatches(t.Config, eventName));

            if (match is null) continue;

            count++;
            try
            {
                var res = await runner.RunAsync(
                    integrationId: integ.Id,
                    sourceRecordId: recordId,
                    triggerType: IntegrationTriggerType.Event,
                    triggeredBy: triggeredBy,
                    ct: ct);
                if (res.Success) ok++; else fail++;
            }
            catch { fail++; }
        }

        return (count, ok, fail);
    }

    private static bool EventNameMatches(string? configJson, string eventName)
    {
        if (string.IsNullOrWhiteSpace(configJson)) return false;
        try
        {
            using var doc = JsonDocument.Parse(configJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!doc.RootElement.TryGetProperty("eventName", out var en)) return false;
            var name = en.ValueKind == JsonValueKind.String ? en.GetString() : null;
            return string.Equals(name, eventName, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
