using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Services.EDocument;

/// <inheritdoc />
public sealed class EDocumentEnvelopeService : IEDocumentEnvelopeService
{
    private readonly IIncomingDocumentRepository _documents;
    private readonly ICompanyParameterService _companyParameters;
    private readonly IExternalDbConnectionRepository _externalDbConnections;
    private readonly IOfflineEDocumentSource _offlineSource;
    private readonly ILogger<EDocumentEnvelopeService> _logger;

    public EDocumentEnvelopeService(
        IIncomingDocumentRepository documents,
        ICompanyParameterService companyParameters,
        IExternalDbConnectionRepository externalDbConnections,
        IOfflineEDocumentSource offlineSource,
        ILogger<EDocumentEnvelopeService> logger)
    {
        _documents = documents;
        _companyParameters = companyParameters;
        _externalDbConnections = externalDbConnections;
        _offlineSource = offlineSource;
        _logger = logger;
    }

    public async Task<string?> TryFetchAndPersistXmlAsync(IncomingDocument document, CancellationToken ct)
    {
        if (document.IngestSource != EDocumentIngestSource.Offline) return null;

        // ERP anahtari EnvelopeId'de durur ('NETSIS-EFAT-14175'); PayloadRaw'a bagli DEGIL,
        // cunku bu metot tam da PayloadRaw'i degistirmek icin calisir.
        var sourceKey = ParseSourceKey(document.EnvelopeId);
        if (sourceKey <= 0) return null;

        try
        {
            var connectionId = await _companyParameters.GetIntAsync(
                EDocumentParameters.FormCode, EDocumentParameters.ErpConnectionIdKey, ct);
            if (connectionId is null or <= 0) return null;

            var connection = await _externalDbConnections.GetByIdAsync(connectionId.Value, ct);
            if (connection is null || !connection.IsActive) return null;

            var xml = await _offlineSource.TryReadEnvelopeXmlAsync(
                connection, document.Kind, sourceKey, ct);
            if (string.IsNullOrWhiteSpace(xml)) return null;

            await _documents.UpdatePayloadRawAsync(document.Id, xml, ct);
            return xml;
        }
        catch (Exception ex)
        {
            // Sessiz yutma YOK: sebep sunucuya loglanir, kullanici akisi bozulmaz.
            _logger.LogWarning(ex,
                "[EBelge] Zarf XML'i okunamadi (Id={Id}, Envelope={Envelope}).",
                document.Id, document.EnvelopeId);
            return null;
        }
    }

    /// <summary>'NETSIS-EFAT-14175' → 14175. Bicim taninmazsa 0.</summary>
    private static int ParseSourceKey(string? envelopeId)
    {
        if (string.IsNullOrWhiteSpace(envelopeId)) return 0;
        var idx = envelopeId.LastIndexOf('-');
        return idx >= 0 && int.TryParse(envelopeId[(idx + 1)..], out var key) ? key : 0;
    }
}
