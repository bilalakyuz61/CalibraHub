using CalibraHub.Application.Abstractions.Integrations;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
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
    private readonly ICompanyParameterService _companyParameters;
    private readonly IExternalDbConnectionRepository _externalDbConnections;
    private readonly IOfflineEDocumentSource _offlineSource;

    public DocumentImportService(
        IIntegratorSettingsRepository integratorSettingsRepository,
        IIncomingDocumentRepository incomingDocumentRepository,
        IIntegratorDocumentClient integratorDocumentClient,
        IIntegratorImportLogRepository integratorImportLogRepository,
        ICompanyParameterService companyParameters,
        IExternalDbConnectionRepository externalDbConnections,
        IOfflineEDocumentSource offlineSource)
    {
        _integratorSettingsRepository = integratorSettingsRepository;
        _incomingDocumentRepository = incomingDocumentRepository;
        _integratorDocumentClient = integratorDocumentClient;
        _integratorImportLogRepository = integratorImportLogRepository;
        _companyParameters = companyParameters;
        _externalDbConnections = externalDbConnections;
        _offlineSource = offlineSource;
    }

    /// <summary>
    /// CEVRIMDISI (ERP) ice aktarim — sirket parametresiyle secilen kaynak profilinden okur
    /// ve YERLI e-belge tablolarina yazar.
    ///
    /// <para><b>Her turda guvenle cagrilabilir:</b> yontem Offline degilse, saglayici tanimsizsa
    /// ya da ERP baglantisi secilmemisse hicbir sey yapmaz, sebebi nota yazip BOS sonuc doner.
    /// Boylece worker tek bir dongude hem online hem offline yolu isletebilir.</para>
    ///
    /// <para><b>Dedup:</b> EnvelopeId uzerinden. Kaynak okuyucu bu anahtari ERP birincil
    /// anahtarina baglar (ERP'nin kendi UUID'si benzersiz DEGIL), dolayisiyla ayni belge
    /// tekrar okundugunda ATLANIR — tarama araligi kisa tutulsa bile kopya olusmaz.</para>
    ///
    /// <para>Tek belgede olusan hata TUM taramayi durdurmaz: loglanir, sayilir ve digerlerine
    /// devam edilir — bozuk bir ERP satiri yuzunden saglam belgeler kaybedilmez.</para>
    /// </summary>
    public async Task<ImportResultDto> ImportFromOfflineSourceAsync(CancellationToken cancellationToken)
    {
        var notes = new List<string>();

        var methodRaw = await _companyParameters.GetStringAsync(
            EDocumentParameters.FormCode, EDocumentParameters.IngestMethodKey, cancellationToken);

        if (!Enum.TryParse<EDocumentIngestSource>(methodRaw, true, out var method)
            || method != EDocumentIngestSource.Offline)
        {
            // Online yapilandirmada bu yol KAPALIDIR — sessiz cikis, hata degil.
            return new ImportResultDto(0, 0, Array.Empty<string>());
        }

        var providerRaw = await _companyParameters.GetStringAsync(
            EDocumentParameters.FormCode, EDocumentParameters.IngestProviderKey, cancellationToken);

        if (!Enum.TryParse<EDocumentSourceProvider>(providerRaw, true, out var provider)
            || !EDocumentSourceCatalog.IsValid(method, provider))
        {
            notes.Add($"E-Belge kaynak sağlayıcı geçersiz veya seçilmemiş ('{providerRaw}').");
            return new ImportResultDto(0, 0, notes);
        }

        var connectionId = await _companyParameters.GetIntAsync(
            EDocumentParameters.FormCode, EDocumentParameters.ErpConnectionIdKey, cancellationToken);

        if (connectionId is null or <= 0)
        {
            notes.Add("E-Belge çevrimdışı aktarımı için ERP veritabanı bağlantısı seçilmemiş.");
            return new ImportResultDto(0, 0, notes);
        }

        var connection = await _externalDbConnections.GetByIdAsync(connectionId.Value, cancellationToken);
        if (connection is null || !connection.IsActive)
        {
            notes.Add($"Seçili ERP bağlantısı bulunamadı veya pasif (Id={connectionId}).");
            return new ImportResultDto(0, 0, notes);
        }

        // Pencere: son basarili okumadan degil, geriye-bakis suresinden baslar. Dedup
        // EnvelopeId ile yapildigi icin ortusen pencere kopya URETMEZ; buna karsilik "son
        // calisma zamani" tutmak, bir tur atlandiginda belge KACIRMA riski dogururdu.
        //
        // Sure PARAMETRIKTIR: sabit 30 gun, arsivi eskiye dayanan bir ERP'de hicbir belge
        // getirmez (yasandi) ve ilk yuklemede gecmisi disarida birakir.
        var lookbackDays = await _companyParameters.GetIntAsync(
            EDocumentParameters.FormCode, EDocumentParameters.LookbackDaysKey, cancellationToken)
            ?? EDocumentParameters.DefaultLookbackDays;
        var since = DateTime.Today.AddDays(-Math.Clamp(lookbackDays, 1, 3650));

        IReadOnlyList<OfflineEDocument> documents;
        try
        {
            documents = await _offlineSource.ReadAsync(
                connection, since, EDocumentParameters.MaxDocumentsPerPull, cancellationToken);
        }
        catch (Exception ex)
        {
            // Kaynak okunamadi (baglanti/izin/sema). Sessiz yutma YOK: sebep nota yazilir.
            notes.Add($"ERP kaynağı okunamadı: {ex.Message}");
            return new ImportResultDto(0, 0, notes);
        }

        var imported = 0;
        var skipped = 0;

        foreach (var doc in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await _incomingDocumentRepository.ExistsByEnvelopeIdAsync(
                        doc.Header.EnvelopeId, cancellationToken))
                {
                    skipped++;
                    continue;
                }

                await _incomingDocumentRepository.AddAsync(doc.Header, cancellationToken, doc.Details);
                imported++;
            }
            catch (Exception ex)
            {
                // Tek belge patlarsa taramanin tamami durmaz.
                skipped++;
                notes.Add($"{doc.Header.DocumentNumber}: {ex.Message}");
            }
        }

        return new ImportResultDto(imported, skipped, notes);
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
