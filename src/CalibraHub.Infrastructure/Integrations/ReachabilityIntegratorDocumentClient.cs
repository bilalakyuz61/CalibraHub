using CalibraHub.Application.Abstractions.Integrations;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace CalibraHub.Infrastructure.Integrations;

public sealed class ReachabilityIntegratorDocumentClient : IIntegratorDocumentClient
{
    private const int MaxDocumentListWindowDays = 30;
    private const int EArchiveDocumentListWindowDays = 1;
    private const string DocumentDataFormat = "UBL";

    /// <summary>
    /// Named HttpClient kullanir (rapor §2.10 cozumu). Static field anti-pattern'i kaldirildi —
    /// IHttpClientFactory pool yonetir, DNS cache + socket exhaustion korunur.
    /// Program.cs: services.AddHttpClient("reachability-soap", c => c.Timeout = TimeSpan.FromSeconds(300))
    /// </summary>
    public const string HttpClientName = "reachability-soap";

    private readonly IHttpClientFactory _httpClientFactory;

    public ReachabilityIntegratorDocumentClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    private HttpClient CreateClient() => _httpClientFactory.CreateClient(HttpClientName);

    private static readonly DocumentKind[] SupportedKinds =
    [
        DocumentKind.EInvoice,
        DocumentKind.EArchive,
        DocumentKind.EDispatch
    ];

    public async Task<IReadOnlyCollection<IncomingDocumentPayload>> PullDocumentsAsync(
        IntegratorSettings settings,
        int maxRecordsPerPull,
        IntegratorDocumentPullOptions pullOptions,
        CancellationToken cancellationToken)
    {
        _ = pullOptions.IncludeReceivedDocumentsInPull;
        var serviceUri = ResolveServiceUri(settings.BaseUrl);
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        cancellationToken = linkedCts.Token;
        var sessionId = await LoginAsync(serviceUri, settings, cancellationToken);

        try
        {
            var pullLimit = Math.Clamp(maxRecordsPerPull, 1, 5000);
            var endDate = pullOptions.EndDate ?? DateOnly.FromDateTime(DateTime.Now.Date);
            var beginDate = pullOptions.StartDate ?? endDate.AddDays(-(settings.LookbackDays - 1));
            var payloads = new List<IncomingDocumentPayload>(pullLimit);
            var seenDocumentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pullScopes = BuildPullScopes(pullOptions);

            foreach (var pullScope in pullScopes)
            {
                if (payloads.Count >= pullLimit)
                {
                    break;
                }

                var documentType = ToDocumentType(pullScope.Kind);
                var operationType = ToOperationType(pullScope.Direction);
                var maxWindowDays = GetDocumentListWindowDays(pullScope.Kind);

                foreach (var (windowBeginDate, windowEndDate) in EnumerateDateWindows(beginDate, endDate, maxWindowDays))
                {
                    if (payloads.Count >= pullLimit)
                    {
                        break;
                    }

                    var documentList = await GetDocumentListAsync(
                        serviceUri,
                        sessionId,
                        documentType,
                        operationType,
                        windowBeginDate,
                        windowEndDate,
                        cancellationToken);

                    foreach (var item in documentList)
                    {
                        if (payloads.Count >= pullLimit)
                        {
                            break;
                        }

                        if (string.IsNullOrWhiteSpace(item.DocumentUuid))
                        {
                            continue;
                        }

                        var documentKey = $"{pullScope.Direction}:{pullScope.Kind}:{item.DocumentUuid}";
                        if (!seenDocumentKeys.Add(documentKey))
                        {
                            continue;
                        }

                        var documentData = await GetDocumentDataAsync(
                            serviceUri,
                            sessionId,
                            item.DocumentUuid,
                            documentType,
                            cancellationToken);

                        if (documentData is null)
                        {
                            continue;
                        }

                        var parsed = ParseIncomingBusinessData(documentData.BinaryBase64);
                        var resolvedUuid = FirstNotEmpty(
                            parsed.DocumentUuid,
                            item.DocumentUuid,
                            item.EnvelopeId,
                            documentData.EnvelopeId,
                            Guid.NewGuid().ToString("N"));
                        var resolvedDocumentNumber = FirstNotEmpty(
                            parsed.DocumentNumber,
                            item.Info.TryGetValue("ID", out var idValue) ? idValue : null,
                            item.Info.TryGetValue("DOCUMENTID", out var documentId) ? documentId : null,
                            documentData.FileName,
                            resolvedUuid);
                        var resolvedIssueDate = parsed.IssueDate ?? DateOnly.FromDateTime(DateTime.Now.Date);
                        var resolvedSenderTaxNumber = FirstNotEmpty(
                            parsed.SenderTaxNumber,
                            item.Info.TryGetValue("SENDERVKN", out var senderVkn) ? senderVkn : null,
                            item.Info.TryGetValue("SENDERVNO", out var senderVno) ? senderVno : null,
                            settings.CompanyTaxNumber);
                        var resolvedRecipientTaxNumber = FirstNotEmpty(
                            parsed.RecipientTaxNumber,
                            item.Info.TryGetValue("RECEIVERVKN", out var receiverVkn) ? receiverVkn : null,
                            item.Info.TryGetValue("RECEIVERVNO", out var receiverVno) ? receiverVno : null,
                            settings.CompanyTaxNumber);
                        var payloadRaw = !string.IsNullOrWhiteSpace(parsed.RawXml)
                            ? parsed.RawXml!
                            : BuildFallbackPayloadRaw(documentData, item, pullScope.Kind);

                        payloads.Add(new IncomingDocumentPayload(
                            resolvedUuid,
                            resolvedDocumentNumber,
                            pullScope.Kind,
                            pullScope.Direction,
                            resolvedIssueDate,
                            resolvedSenderTaxNumber,
                            parsed.SenderName,
                            resolvedRecipientTaxNumber,
                            payloadRaw));
                    }
                }
            }

            return payloads;
        }
        finally
        {
            await TryLogoutAsync(serviceUri, sessionId, cancellationToken);
        }
    }

    public async Task MarkDocumentsAsReceivedAsync(
        IntegratorSettings settings,
        IReadOnlyCollection<IncomingDocumentPayload> documents,
        CancellationToken cancellationToken)
    {
        if (documents.Count == 0)
        {
            return;
        }

        var serviceUri = ResolveServiceUri(settings.BaseUrl);
        var sessionId = await LoginAsync(serviceUri, settings, cancellationToken);

        try
        {
            var uniqueDocuments = documents
                .Where(x => !string.IsNullOrWhiteSpace(x.EnvelopeId))
                .GroupBy(x => $"{x.Kind}:{x.EnvelopeId}", StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .ToArray();

            foreach (var document in uniqueDocuments)
            {
                await GetDocumentDoneAsync(
                    serviceUri,
                    sessionId,
                    document.EnvelopeId,
                    ToDocumentType(document.Kind),
                    cancellationToken);
            }
        }
        finally
        {
            await TryLogoutAsync(serviceUri, sessionId, cancellationToken);
        }
    }

    private static IEnumerable<(DateOnly BeginDate, DateOnly EndDate)> EnumerateDateWindows(
        DateOnly beginDate,
        DateOnly endDate,
        int windowSizeDays)
    {
        if (beginDate > endDate)
        {
            yield break;
        }

        var effectiveWindowSize = Math.Max(1, windowSizeDays);
        var cursor = endDate;
        while (cursor >= beginDate)
        {
            var windowBeginDate = cursor.AddDays(-(effectiveWindowSize - 1));
            if (windowBeginDate < beginDate)
            {
                windowBeginDate = beginDate;
            }

            yield return (windowBeginDate, cursor);

            if (windowBeginDate == beginDate)
            {
                yield break;
            }

            cursor = windowBeginDate.AddDays(-1);
        }
    }

    private static Uri ResolveServiceUri(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"Base URL gecersiz: {baseUrl}");
        }

        if (uri.AbsolutePath.EndsWith(".svc", StringComparison.OrdinalIgnoreCase))
        {
            return uri;
        }

        var normalizedPath = uri.AbsolutePath.TrimEnd('/');
        var servicePath = string.IsNullOrEmpty(normalizedPath)
            ? "/postboxservice.svc"
            : $"{normalizedPath}/postboxservice.svc";
        var builder = new UriBuilder(uri)
        {
            Path = servicePath,
            Query = string.Empty
        };
        return builder.Uri;
    }

    private async Task<string> LoginAsync(
        Uri serviceUri,
        IntegratorSettings settings,
        CancellationToken cancellationToken)
    {
        var username = EscapeXml(settings.Username);
        var password = EscapeXml(settings.Secret);

        var body = $"""
            <tem:Login xmlns:tem="http://tempuri.org/" xmlns:efat="http://schemas.datacontract.org/2004/07/eFaturaWebService">
                <tem:login>
                    <efat:appStr>CalibraHub</efat:appStr>
                    <efat:passWord>{password}</efat:passWord>
                    <efat:source>1</efat:source>
                    <efat:userName>{username}</efat:userName>
                    <efat:version>1.0</efat:version>
                </tem:login>
            </tem:Login>
            """;

        var document = await SendSoapRequestAsync(serviceUri, "Login", body, cancellationToken);
        var loginResult = FindDescendantValue(document, "LoginResult");
        var sessionId = FindDescendantValue(document, "sessionID");

        if (!string.Equals(loginResult, "true", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(sessionId))
        {
            var message = FirstNotEmpty(
                FindDescendantValue(document, "resultMsg"),
                FindDescendantValue(document, "faultstring"),
                "Login basarisiz.");
            throw new InvalidOperationException(message);
        }

        return sessionId;
    }

    private async Task TryLogoutAsync(
        Uri serviceUri,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        var body = $"""
            <tem:Logout xmlns:tem="http://tempuri.org/">
                <tem:sessionID>{EscapeXml(sessionId)}</tem:sessionID>
            </tem:Logout>
            """;

        try
        {
            await SendSoapRequestAsync(serviceUri, "Logout", body, cancellationToken);
        }
        catch
        {
            // Logout hatasi import akisinin sonucunu etkilememeli.
        }
    }

    private async Task<IReadOnlyCollection<DocumentListItem>> GetDocumentListAsync(
        Uri serviceUri,
        string sessionId,
        string documentType,
        string operationType,
        DateOnly beginDate,
        DateOnly endDate,
        CancellationToken cancellationToken)
    {
        var beginDateValue = beginDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var endDateValue = endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var body = $"""
            <tem:GetDocumentList xmlns:tem="http://tempuri.org/" xmlns:arr="http://schemas.microsoft.com/2003/10/Serialization/Arrays">
                <tem:sessionID>{EscapeXml(sessionId)}</tem:sessionID>
                <tem:paramList>
                    <arr:string>DOCUMENTTYPE={EscapeXml(documentType)}</arr:string>
                    <arr:string>OPTYPE={EscapeXml(operationType)}</arr:string>
                    <arr:string>DATEBY=0</arr:string>
                    <arr:string>BEGINDATE={beginDateValue}</arr:string>
                    <arr:string>ENDDATE={endDateValue}</arr:string>
                </tem:paramList>
            </tem:GetDocumentList>
            """;

        var response = await SendSoapRequestAsync(serviceUri, "GetDocumentList", body, cancellationToken);
        var resultCode = FindDescendantValue(response, "resultCode");
        if (!string.Equals(resultCode, "1", StringComparison.OrdinalIgnoreCase))
        {
            var resultMessage = FirstNotEmpty(
                FindDescendantValue(response, "resultMsg"),
                "GetDocumentList cagrisinda sonuc donmedi.");
            throw new InvalidOperationException(resultMessage);
        }

        var documents = new List<DocumentListItem>();
        foreach (var documentElement in FindDescendants(response, "Document"))
        {
            var uuid = FindDescendantValue(documentElement, "documentUuid");
            if (string.IsNullOrWhiteSpace(uuid))
            {
                continue;
            }

            var info = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var infoValue in FindDescendants(documentElement, "string")
                         .Select(x => x.Value?.Trim())
                         .Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                var separatorIndex = infoValue!.IndexOf('=');
                if (separatorIndex <= 0 || separatorIndex >= infoValue.Length - 1)
                {
                    continue;
                }

                var key = infoValue[..separatorIndex].Trim();
                var value = infoValue[(separatorIndex + 1)..].Trim();
                if (key.Length == 0)
                {
                    continue;
                }

                info[key] = value;
            }

            info.TryGetValue("ENVELOPEID", out var envelopeId);
            documents.Add(new DocumentListItem(uuid, envelopeId, info));
        }

        return documents;
    }

    private async Task<DocumentDataResult?> GetDocumentDataAsync(
        Uri serviceUri,
        string sessionId,
        string uuid,
        string documentType,
        CancellationToken cancellationToken)
    {
        var body = $"""
            <tem:GetDocumentData xmlns:tem="http://tempuri.org/" xmlns:arr="http://schemas.microsoft.com/2003/10/Serialization/Arrays">
                <tem:sessionID>{EscapeXml(sessionId)}</tem:sessionID>
                <tem:uuid>{EscapeXml(uuid)}</tem:uuid>
                <tem:paramList>
                    <arr:string>DOCUMENTTYPE={EscapeXml(documentType)}</arr:string>
                    <arr:string>DATAFORMAT={DocumentDataFormat}</arr:string>
                </tem:paramList>
            </tem:GetDocumentData>
            """;

        var response = await SendSoapRequestAsync(serviceUri, "GetDocumentData", body, cancellationToken);
        var resultCode = FindDescendantValue(response, "resultCode");
        if (!string.Equals(resultCode, "1", StringComparison.OrdinalIgnoreCase))
        {
            var resultMessage = FirstNotEmpty(FindDescendantValue(response, "resultMsg"));
            if (string.IsNullOrWhiteSpace(resultMessage))
            {
                return null;
            }

            throw new InvalidOperationException(resultMessage);
        }

        var documentElement = FindDescendants(response, "document").FirstOrDefault();
        if (documentElement is null)
        {
            return null;
        }

        var binaryBase64 = FirstNotEmpty(
            FindDescendantValue(documentElement, "Value"),
            FindDescendantValue(documentElement, "value"),
            FindDescendantValue(documentElement, "binaryData"));
        if (string.IsNullOrWhiteSpace(binaryBase64))
        {
            return null;
        }

        var envelopeId = FindDescendantValue(documentElement, "envelopeId");
        var fileName = FindDescendantValue(documentElement, "fileName");
        var currentDateValue = FindDescendantValue(documentElement, "currentDate");
        DateTimeOffset? currentDate = null;
        if (DateTimeOffset.TryParse(currentDateValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedDate))
        {
            currentDate = parsedDate;
        }

        return new DocumentDataResult(binaryBase64!, envelopeId, fileName, currentDate);
    }

    private async Task GetDocumentDoneAsync(
        Uri serviceUri,
        string sessionId,
        string uuid,
        string documentType,
        CancellationToken cancellationToken)
    {
        var body = $"""
            <tem:GetDocumentDone xmlns:tem="http://tempuri.org/" xmlns:arr="http://schemas.microsoft.com/2003/10/Serialization/Arrays">
                <tem:sessionID>{EscapeXml(sessionId)}</tem:sessionID>
                <tem:uuid>{EscapeXml(uuid)}</tem:uuid>
                <tem:paramList>
                    <arr:string>DOCUMENTTYPE={EscapeXml(documentType)}</arr:string>
                </tem:paramList>
            </tem:GetDocumentDone>
            """;

        var response = await SendSoapRequestAsync(serviceUri, "GetDocumentDone", body, cancellationToken);
        var resultCode = FindDescendantValue(response, "resultCode");
        if (!string.IsNullOrWhiteSpace(resultCode) &&
            !string.Equals(resultCode, "1", StringComparison.OrdinalIgnoreCase))
        {
            var message = FirstNotEmpty(
                FindDescendantValue(response, "resultMsg"),
                $"Belge alindi isaretleme basarisiz. UUID: {uuid}");
            throw new InvalidOperationException(message);
        }
    }

    private async Task<XDocument> SendSoapRequestAsync(
        Uri serviceUri,
        string operation,
        string bodyXml,
        CancellationToken cancellationToken)
    {
        var actionCandidates = new[]
        {
            $"http://tempuri.org/IPostBoxService/{operation}",
            $"http://tempuri.org/{operation}"
        };

        Exception? lastException = null;
        for (var index = 0; index < actionCandidates.Length; index++)
        {
            var soapAction = actionCandidates[index];
            try
            {
                return await SendSoapRequestInternalAsync(
                    serviceUri,
                    soapAction,
                    operation,
                    bodyXml,
                    cancellationToken);
            }
            catch (InvalidOperationException ex) when (
                index < actionCandidates.Length - 1 &&
                IsSoapActionMismatch(ex.Message))
            {
                lastException = ex;
            }
        }

        throw lastException ?? new InvalidOperationException($"{operation} istegi basarisiz oldu.");
    }

    private async Task<XDocument> SendSoapRequestInternalAsync(
        Uri serviceUri,
        string soapAction,
        string operation,
        string bodyXml,
        CancellationToken cancellationToken)
    {
        var requestXml = $"""
            <soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/">
                <soapenv:Header/>
                <soapenv:Body>
                    {bodyXml}
                </soapenv:Body>
            </soapenv:Envelope>
            """;

        using var request = new HttpRequestMessage(HttpMethod.Post, serviceUri);
        request.Headers.Add("SOAPAction", $"\"{soapAction}\"");
        request.Content = new StringContent(requestXml, Encoding.UTF8, "text/xml");

        // IHttpClientFactory pool yonetir — DNS cache + socket exhaustion korumasi (rapor §2.10).
        var serviceClient = CreateClient();
        using var response = await serviceClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(responseContent))
        {
            throw new InvalidOperationException($"{operation} yaniti bos dondu.");
        }

        XDocument responseXml;
        try
        {
            responseXml = XDocument.Parse(responseContent, LoadOptions.PreserveWhitespace);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"{operation} yaniti XML olarak ayrıştırılamadı.", ex);
        }

        var faultString = FindDescendantValue(responseXml, "faultstring");
        if (!string.IsNullOrWhiteSpace(faultString))
        {
            throw new InvalidOperationException(faultString);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"{operation} istegi basarisiz oldu. HTTP {(int)response.StatusCode} - {response.ReasonPhrase}");
        }

        return responseXml;
    }

    private static bool IsSoapActionMismatch(string message) =>
        message.Contains("ActionNotSupported", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("ContractFilter", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("SOAPAction", StringComparison.OrdinalIgnoreCase);

    private static ParsedIncomingBusinessData ParseIncomingBusinessData(string binaryBase64)
    {
        if (string.IsNullOrWhiteSpace(binaryBase64))
        {
            return ParsedIncomingBusinessData.Empty;
        }

        var rawXml = TryExtractXml(binaryBase64);
        if (string.IsNullOrWhiteSpace(rawXml))
        {
            return ParsedIncomingBusinessData.Empty;
        }

        try
        {
            var xml = XDocument.Parse(rawXml, LoadOptions.PreserveWhitespace);
            var root = xml.Root;
            if (root is null)
            {
                return ParsedIncomingBusinessData.Empty with { RawXml = rawXml };
            }

            var documentNumber = GetDirectChildValue(root, "ID");
            var documentUuid = GetDirectChildValue(root, "UUID");
            var issueDateValue = GetDirectChildValue(root, "IssueDate");

            DateOnly? issueDate = null;
            if (!string.IsNullOrWhiteSpace(issueDateValue) &&
                DateOnly.TryParse(issueDateValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedIssueDate))
            {
                issueDate = parsedIssueDate;
            }

            var senderTax = FirstNotEmpty(
                GetPathValue(root, "AccountingSupplierParty", "Party", "PartyTaxScheme", "CompanyID"),
                GetPathValue(root, "AccountingSupplierParty", "Party", "PartyIdentification", "ID"),
                GetPathValue(root, "DespatchSupplierParty", "Party", "PartyTaxScheme", "CompanyID"),
                GetPathValue(root, "DespatchSupplierParty", "Party", "PartyIdentification", "ID"));
            var senderName = ResolvePartyName(root, "AccountingSupplierParty")
                          ?? ResolvePartyName(root, "DespatchSupplierParty");
            var recipientTax = FirstNotEmpty(
                GetPathValue(root, "AccountingCustomerParty", "Party", "PartyTaxScheme", "CompanyID"),
                GetPathValue(root, "AccountingCustomerParty", "Party", "PartyIdentification", "ID"),
                GetPathValue(root, "DeliveryCustomerParty", "Party", "PartyTaxScheme", "CompanyID"),
                GetPathValue(root, "DeliveryCustomerParty", "Party", "PartyIdentification", "ID"));

            return new ParsedIncomingBusinessData(
                DocumentUuid: NormalizeTaxOrCode(documentUuid),
                DocumentNumber: NormalizeTaxOrCode(documentNumber),
                SenderTaxNumber: NormalizeTaxOrCode(senderTax),
                SenderName: string.IsNullOrWhiteSpace(senderName) ? null : senderName.Trim(),
                RecipientTaxNumber: NormalizeTaxOrCode(recipientTax),
                IssueDate: issueDate,
                RawXml: rawXml);
        }
        catch
        {
            return ParsedIncomingBusinessData.Empty with { RawXml = rawXml };
        }
    }

    private static string? TryExtractXml(string binaryBase64)
    {
        byte[] binary;
        try
        {
            binary = Convert.FromBase64String(binaryBase64);
        }
        catch
        {
            return null;
        }

        if (binary.Length >= 2 && binary[0] == 0x50 && binary[1] == 0x4B)
        {
            try
            {
                using var zipStream = new MemoryStream(binary);
                using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);
                var entry = archive.Entries.FirstOrDefault(x =>
                        x.Length > 0 &&
                        (x.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                         x.Name.EndsWith(".ubl", StringComparison.OrdinalIgnoreCase))) ??
                    archive.Entries.FirstOrDefault(x => x.Length > 0);
                if (entry is null)
                {
                    return null;
                }

                using var entryStream = entry.Open();
                using var reader = new StreamReader(entryStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                return reader.ReadToEnd();
            }
            catch
            {
                return null;
            }
        }

        var text = Encoding.UTF8.GetString(binary);
        return text.TrimStart().StartsWith('<') ? text : null;
    }

    private static string BuildFallbackPayloadRaw(DocumentDataResult data, DocumentListItem item, DocumentKind kind)
    {
        var payload = new
        {
            kind = kind.ToString(),
            documentUuid = item.DocumentUuid,
            envelopeId = FirstNotEmpty(item.EnvelopeId, data.EnvelopeId),
            fileName = data.FileName,
            info = item.Info
        };
        return JsonSerializer.Serialize(payload);
    }

    private static IEnumerable<XElement> FindDescendants(XContainer container, string localName) =>
        container
            .Descendants()
            .Where(x => string.Equals(x.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase));

    private static string? FindDescendantValue(XContainer container, string localName) =>
        FindDescendants(container, localName)
            .Select(x => x.Value?.Trim())
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

    private static string? GetDirectChildValue(XElement parent, string localName) =>
        parent
            .Elements()
            .FirstOrDefault(x => string.Equals(x.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase))
            ?.Value
            ?.Trim();

    private static string? GetPathValue(XElement root, params string[] path)
    {
        XElement? current = root;
        foreach (var segment in path)
        {
            current = current?
                .Elements()
                .FirstOrDefault(x => string.Equals(x.Name.LocalName, segment, StringComparison.OrdinalIgnoreCase));
            if (current is null)
            {
                return null;
            }
        }

        return current.Value?.Trim();
    }

    private static string? ResolvePartyName(XElement root, string partyElementName)
    {
        var partyElement = FindDescendants(root, partyElementName).FirstOrDefault();
        if (partyElement is null)
        {
            return null;
        }

        return FindDescendants(partyElement, "PartyName")
                   .Select(x => FindDescendants(x, "Name")
                       .Select(n => n.Value?.Trim())
                       .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)))
                   .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
               ?? FindDescendants(partyElement, "RegistrationName")
                   .Select(x => x.Value?.Trim())
                   .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
    }

    private static string EscapeXml(string? value) =>
        SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;

    private static string FirstNotEmpty(params string?[] values) =>
        values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;

    private static string? NormalizeTaxOrCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static string ToDocumentType(DocumentKind kind) =>
        kind switch
        {
            DocumentKind.EInvoice => "EINVOICE",
            DocumentKind.EArchive => "EARCHIVE",
            DocumentKind.EDispatch => "DESPATCHADVICE",
            _ => "EINVOICE"
        };

    private static string ToOperationType(DocumentDirection direction) =>
        direction == DocumentDirection.Outgoing ? "1" : "2";

    private static int GetDocumentListWindowDays(DocumentKind kind) =>
        kind == DocumentKind.EArchive ? EArchiveDocumentListWindowDays : MaxDocumentListWindowDays;

    private static IReadOnlyList<DocumentPullScope> BuildPullScopes(IntegratorDocumentPullOptions pullOptions)
    {
        var scopes = SupportedKinds
            .Select(kind => new DocumentPullScope(kind, DocumentDirection.Incoming))
            .ToList();

        foreach (var kind in SupportedKinds.Where(pullOptions.IncludesIssuedKind))
        {
            scopes.Add(new DocumentPullScope(kind, DocumentDirection.Outgoing));
        }

        return scopes;
    }

    private sealed record DocumentPullScope(DocumentKind Kind, DocumentDirection Direction);

    private sealed record DocumentListItem(
        string DocumentUuid,
        string? EnvelopeId,
        IReadOnlyDictionary<string, string> Info);

    private sealed record DocumentDataResult(
        string BinaryBase64,
        string? EnvelopeId,
        string? FileName,
        DateTimeOffset? CurrentDate);

    private sealed record ParsedIncomingBusinessData(
        string? DocumentUuid,
        string? DocumentNumber,
        string? SenderTaxNumber,
        string? SenderName,
        string? RecipientTaxNumber,
        DateOnly? IssueDate,
        string? RawXml)
    {
        public static ParsedIncomingBusinessData Empty { get; } = new(
            DocumentUuid: null,
            DocumentNumber: null,
            SenderTaxNumber: null,
            SenderName: null,
            RecipientTaxNumber: null,
            IssueDate: null,
            RawXml: null);
    }
}
