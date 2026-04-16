using CalibraHub.Application.Abstractions.Integrations;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Services;

public sealed class DocumentImportService : IDocumentImportService
{
    private readonly IIntegratorSettingsRepository _integratorSettingsRepository;
    private readonly IIncomingDocumentRepository _incomingDocumentRepository;
    private readonly IIntegratorDocumentClient _integratorDocumentClient;
    private readonly IIntegratorImportLogRepository _integratorImportLogRepository;

    public DocumentImportService(
        IIntegratorSettingsRepository integratorSettingsRepository,
        IIncomingDocumentRepository incomingDocumentRepository,
        IIntegratorDocumentClient integratorDocumentClient,
        IIntegratorImportLogRepository integratorImportLogRepository)
    {
        _integratorSettingsRepository = integratorSettingsRepository;
        _incomingDocumentRepository = incomingDocumentRepository;
        _integratorDocumentClient = integratorDocumentClient;
        _integratorImportLogRepository = integratorImportLogRepository;
    }

    public Task<ImportResultDto> ImportFromActiveIntegratorsAsync(CancellationToken cancellationToken) =>
        ImportFromActiveIntegratorsAsync(null, null, cancellationToken);

    public async Task<ImportResultDto> ImportFromActiveIntegratorsAsync(
        DateOnly? startDate,
        DateOnly? endDate,
        CancellationToken cancellationToken)
    {
        var activeIntegrators = await _integratorSettingsRepository.GetActiveAsync(cancellationToken);
        var importedCount = 0;
        var skippedCount = 0;
        var notes = new List<string>();

        foreach (var integrator in activeIntegrators)
        {
            var maxRecordsPerPull = Math.Clamp(integrator.MaxRecordsPerPull, 1, 5000);
            var logRetentionDays = Math.Clamp(integrator.LogRetentionDays, 1, 3650);
            var integratorImportedCount = 0;
            var integratorSkippedCount = 0;

            try
            {
                var pullOptions = new IntegratorDocumentPullOptions(
                    integrator.IncludeReceivedDocumentsInPull,
                    integrator.IncludeIssuedEInvoicesInPull,
                    integrator.IncludeIssuedEArchivesInPull,
                    integrator.IncludeIssuedEDispatchesInPull,
                    startDate,
                    endDate);
                var pulledPayloads = await _integratorDocumentClient.PullDocumentsAsync(
                    integrator,
                    maxRecordsPerPull,
                    pullOptions,
                    cancellationToken);
                var payloads = pulledPayloads
                    .Take(maxRecordsPerPull)
                    .ToArray();

                foreach (var payload in payloads)
                {
                    var exists = await _incomingDocumentRepository.ExistsByEnvelopeIdAsync(payload.EnvelopeId, cancellationToken);
                    if (exists)
                    {
                        integratorSkippedCount++;
                        continue;
                    }

                    var duplicateByDocumentAndRecipient = await _incomingDocumentRepository.ExistsByDocumentNumberAndRecipientAsync(
                        payload.DocumentNumber,
                        payload.RecipientTaxNumber,
                        payload.Kind,
                        cancellationToken);
                    if (duplicateByDocumentAndRecipient)
                    {
                        integratorSkippedCount++;
                        continue;
                    }

                    var document = new IncomingDocument
                    {
                        IntegratorSettingsId = integrator.Id,
                        EnvelopeId = payload.EnvelopeId,
                        DocumentNumber = payload.DocumentNumber,
                        Kind = payload.Kind,
                        IssueDate = payload.IssueDate,
                        SenderTaxNumber = payload.SenderTaxNumber,
                        SenderName = payload.SenderName,
                        RecipientTaxNumber = payload.RecipientTaxNumber,
                        PayloadRaw = payload.PayloadRaw
                    };

                    await _incomingDocumentRepository.AddAsync(document, cancellationToken);
                    integratorImportedCount++;
                }

                if (integrator.MarkDownloadedDocumentsAsReceived)
                {
                    var receivableDocuments = payloads
                        .Where(x => x.Direction == DocumentDirection.Incoming)
                        .Where(x => x.Kind is DocumentKind.EInvoice or DocumentKind.EDispatch)
                        .ToArray();

                    if (receivableDocuments.Length > 0)
                    {
                        await _integratorDocumentClient.MarkDocumentsAsReceivedAsync(
                            integrator,
                            receivableDocuments,
                            cancellationToken);
                    }
                }

                notes.Add(
                    $"{integrator.Name} kaynagindan {payloads.Length} kayit alindi (base url: {integrator.BaseUrl}, max pull: {maxRecordsPerPull}, okunmus dahil: {(integrator.IncludeReceivedDocumentsInPull ? "evet" : "hayir")}).");

                await TryWriteLogAsync(
                    new IntegratorImportLogWriteRequest(
                        integrator.Id,
                        integrator.Name,
                        "Success",
                        $"{payloads.Length} kayit alindi (base url: {integrator.BaseUrl}). {integratorImportedCount} yeni kayit eklendi, {integratorSkippedCount} tekrar kayit atlandi.",
                        integratorImportedCount,
                        integratorSkippedCount,
                        integrator.CompanyId),
                    notes,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                var errorMessage = ex is OperationCanceledException
                    ? $"{integrator.Name} kaynaginda zaman asimi: servis yanit vermedi."
                    : $"{integrator.Name} kaynaginda hata: {ex.Message}";
                notes.Add(errorMessage);

                await TryWriteLogAsync(
                    new IntegratorImportLogWriteRequest(
                        integrator.Id,
                        integrator.Name,
                        "Error",
                        errorMessage,
                        integratorImportedCount,
                        integratorSkippedCount,
                        integrator.CompanyId),
                    notes,
                    cancellationToken);
            }
            finally
            {
                importedCount += integratorImportedCount;
                skippedCount += integratorSkippedCount;

                await TryCleanupLogsAsync(
                    integrator.Id,
                    logRetentionDays,
                    integrator.Name,
                    notes,
                    cancellationToken);
            }
        }

        return new ImportResultDto(importedCount, skippedCount, notes);
    }

    private async Task TryWriteLogAsync(
        IntegratorImportLogWriteRequest request,
        List<string> notes,
        CancellationToken cancellationToken)
    {
        try
        {
            await _integratorImportLogRepository.WriteAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            notes.Add($"{request.IntegratorName} log kaydi yazilamadi: {ex.Message}");
        }
    }

    private async Task TryCleanupLogsAsync(
        int integratorSettingsId,
        int retentionDays,
        string integratorName,
        List<string> notes,
        CancellationToken cancellationToken)
    {
        try
        {
            await _integratorImportLogRepository.CleanupExpiredAsync(
                integratorSettingsId,
                retentionDays,
                cancellationToken);
        }
        catch (Exception ex)
        {
            notes.Add($"{integratorName} log temizlik islemi basarisiz: {ex.Message}");
        }
    }
}
