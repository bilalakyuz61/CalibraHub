using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Services.Integration;

/// <summary>
/// IIntegrationOnSaveDispatcher implementasyonu — fire-and-forget Task.Run ile
/// arka planda calisir. Per-company DB context'ini korumak icin IServiceScopeFactory
/// uzerinden YENI scope acar (HTTP context bittiginde scope'taki kayit kaybolmasin).
///
/// Hata yutma stratejisi: bir entegrasyon patlasa diger entegrasyonlari etkilemez.
/// Tum exception'lar loglanir; kullaniciya feedback gitmez (zaten Save success donmus).
/// </summary>
public sealed class IntegrationOnSaveDispatcher : IIntegrationOnSaveDispatcher
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<IntegrationOnSaveDispatcher> _log;

    public IntegrationOnSaveDispatcher(
        IServiceScopeFactory scopes,
        ILogger<IntegrationOnSaveDispatcher> log)
    {
        _scopes = scopes;
        _log = log;
    }

    public void FireOnSave(string formCode, string recordId, string? triggeredBy = null)
        => FireOnSave(new[] { formCode }, recordId, triggeredBy);

    public void FireOnSave(IEnumerable<string> formCodes, string recordId, string? triggeredBy = null)
    {
        if (string.IsNullOrWhiteSpace(recordId)) return;
        var codes = (formCodes ?? Array.Empty<string>())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (codes.Count == 0) return;

        // Fire-and-forget — caller'i bekletme. Background task icinde scope ac.
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var repo   = scope.ServiceProvider.GetRequiredService<IIntegrationRepository>();
                var runner = scope.ServiceProvider.GetRequiredService<IIntegrationRunner>();

                foreach (var formCode in codes)
                {
                    IReadOnlyCollection<Domain.Entities.Integration> integrations;
                    try
                    {
                        integrations = await repo.ListByFormCodeAsync(
                            formCode, IntegrationTriggerType.OnSave, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex, "[OnSaveDispatcher] ListByFormCodeAsync hatasi (formCode={FormCode})", formCode);
                        continue;
                    }

                    foreach (var integ in integrations.Where(i => i.IsActive))
                    {
                        try
                        {
                            var result = await runner.RunAsync(
                                integ.Id, recordId, IntegrationTriggerType.OnSave,
                                triggeredBy ?? "system-onsave", CancellationToken.None);
                            if (result.Success)
                                _log.LogInformation("[OnSaveDispatcher] {IntegrationName} (#{Id}) basarili (HTTP {Status}) — recordId={RecordId}",
                                    integ.Name, integ.Id, result.HttpStatusCode, recordId);
                            else
                                _log.LogWarning("[OnSaveDispatcher] {IntegrationName} (#{Id}) basarisiz: {Err} — recordId={RecordId}",
                                    integ.Name, integ.Id, result.ErrorMessage, recordId);
                        }
                        catch (Exception ex)
                        {
                            _log.LogError(ex, "[OnSaveDispatcher] {IntegrationName} (#{Id}) exception — recordId={RecordId}",
                                integ.Name, integ.Id, recordId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "[OnSaveDispatcher] Beklenmedik hata (formCodes={Codes}, recordId={RecordId})",
                    string.Join(",", codes), recordId);
            }
        });
    }
}
