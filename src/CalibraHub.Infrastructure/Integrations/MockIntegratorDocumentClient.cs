using CalibraHub.Application.Abstractions.Integrations;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using System.Net.Http;

namespace CalibraHub.Infrastructure.Integrations;

public sealed class MockIntegratorDocumentClient : IIntegratorDocumentClient
{
    private static readonly HttpClient ReachabilityClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private readonly int _simulatedDocumentsPerPull;

    public MockIntegratorDocumentClient(int simulatedDocumentsPerPull)
    {
        _simulatedDocumentsPerPull = Math.Max(1, simulatedDocumentsPerPull);
    }

    public Task<IReadOnlyCollection<IncomingDocumentPayload>> PullDocumentsAsync(
        IntegratorSettings settings,
        int maxRecordsPerPull,
        IntegratorDocumentPullOptions pullOptions,
        CancellationToken cancellationToken)
    {
        return PullDocumentsInternalAsync(settings, maxRecordsPerPull, pullOptions, cancellationToken);
    }

    private async Task<IReadOnlyCollection<IncomingDocumentPayload>> PullDocumentsInternalAsync(
        IntegratorSettings settings,
        int maxRecordsPerPull,
        IntegratorDocumentPullOptions pullOptions,
        CancellationToken cancellationToken)
    {
        await EnsureBaseUrlReachableAsync(settings.BaseUrl, cancellationToken);

        var now = DateTime.Now;
        var effectivePullLimit = Math.Clamp(maxRecordsPerPull, 1, 5000);
        var requestedScopes = BuildPullScopes(pullOptions);
        var payloadTarget = Math.Min(_simulatedDocumentsPerPull, effectivePullLimit);
        var payloads = new List<IncomingDocumentPayload>(payloadTarget);

        for (var index = 0; index < payloadTarget; index += 1)
        {
            var scope = requestedScopes[index % requestedScopes.Count];
            var sequence = index + 1;
            var senderTaxNumber = scope.Direction == DocumentDirection.Incoming
                ? "1111111111"
                : settings.CompanyTaxNumber;
            var recipientTaxNumber = scope.Direction == DocumentDirection.Incoming
                ? settings.CompanyTaxNumber
                : $"222222222{sequence % 10}";

            payloads.Add(new IncomingDocumentPayload(
                EnvelopeId: $"ENV-{settings.CompanyTaxNumber}-{scope.Direction}-{scope.Kind}-{now:yyyyMMddHHmmss}-{sequence}",
                DocumentNumber: $"DOC-{scope.Kind}-{scope.Direction}-{now:yyyyMMdd}-{sequence:D4}",
                Kind: scope.Kind,
                Direction: scope.Direction,
                IssueDate: DateOnly.FromDateTime(now),
                SenderTaxNumber: senderTaxNumber,
                SenderName: scope.Direction == DocumentDirection.Incoming ? $"Mock Tedarikci {sequence}" : null,
                RecipientTaxNumber: recipientTaxNumber,
                PayloadRaw:
                $"{{\"provider\":\"{settings.Provider}\",\"kind\":\"{scope.Kind}\",\"direction\":\"{scope.Direction}\",\"baseUrl\":\"{settings.BaseUrl}\",\"includeReceivedDocumentsInPull\":{(pullOptions.IncludeReceivedDocumentsInPull ? "true" : "false")}}}"));
        }

        return payloads;
    }

    public async Task MarkDocumentsAsReceivedAsync(
        IntegratorSettings settings,
        IReadOnlyCollection<IncomingDocumentPayload> documents,
        CancellationToken cancellationToken)
    {
        await EnsureBaseUrlReachableAsync(settings.BaseUrl, cancellationToken);
    }

    private static IReadOnlyList<DocumentPullScope> BuildPullScopes(IntegratorDocumentPullOptions pullOptions)
    {
        var scopes = new List<DocumentPullScope>
        {
            new(DocumentKind.EInvoice, DocumentDirection.Incoming),
            new(DocumentKind.EArchive, DocumentDirection.Incoming),
            new(DocumentKind.EDispatch, DocumentDirection.Incoming)
        };

        if (pullOptions.IncludeIssuedEInvoicesInPull)
        {
            scopes.Add(new DocumentPullScope(DocumentKind.EInvoice, DocumentDirection.Outgoing));
        }

        if (pullOptions.IncludeIssuedEArchivesInPull)
        {
            scopes.Add(new DocumentPullScope(DocumentKind.EArchive, DocumentDirection.Outgoing));
        }

        if (pullOptions.IncludeIssuedEDispatchesInPull)
        {
            scopes.Add(new DocumentPullScope(DocumentKind.EDispatch, DocumentDirection.Outgoing));
        }

        return scopes;
    }

    private static async Task EnsureBaseUrlReachableAsync(string baseUrl, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException($"Base URL gecersiz: {baseUrl}");
        }

        try
        {
            using var headRequest = new HttpRequestMessage(HttpMethod.Head, baseUri);
            using var headResponse = await ReachabilityClient.SendAsync(
                headRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (headResponse.StatusCode != System.Net.HttpStatusCode.MethodNotAllowed)
            {
                return;
            }

            using var getRequest = new HttpRequestMessage(HttpMethod.Get, baseUri);
            using var getResponse = await ReachabilityClient.SendAsync(
                getRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException(
                $"Base URL erisimi saglanamadi: {baseUri}",
                ex);
        }
    }

    private sealed record DocumentPullScope(DocumentKind Kind, DocumentDirection Direction);
}
