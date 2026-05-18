using System.Diagnostics;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
// IPostProcedureExecutor + PostProcedureRunMeta IPostProcedureExecutor.cs icinde
// — Application.Abstractions.Services namespace'i ile zaten geliyor.

namespace CalibraHub.Application.Services.Integration;

/// <summary>
/// IIntegrationRunner implementasyonu — Entegrasyon Wizard'in runtime orchestrator'i.
///
/// Akis:
///   1. Integration aggregate (mappings + triggers + endpoint) yukle
///      - Validation: integration var mi? aktif mi? endpoint var mi?
///      - Validation hatasi → result Failed ile geri don (RUN log YAZILMAZ — sadece
///        gercek calistirma denemeleri audit'e gider; FK violation onlenir).
///   2. ApiProfile cek
///   3. Form sample record cek (recordId verilmemisse en son kayit)
///   4. MappingEngine.BuildAsync → JSON body
///   5. HttpExecutor.SendAsync → result
///   6. IntegrationRun INSERT — tek INSERT, sonuc bilgisiyle birlikte (post-run)
///
/// Hata yakalama: try/catch dis bloku — runner asla exception throw etmez. Run log
/// her durumda yazilir (validation'i gecmis olanlar icin); caller'a IntegrationRunnerResult
/// ile durum bilgisi doner.
/// </summary>
public sealed class IntegrationRunner : IIntegrationRunner
{
    private readonly IIntegrationRepository _integrationRepo;
    private readonly IIntegrationApiProfileRepository _apiProfileRepo;
    private readonly IFormMetadataService _formMeta;
    private readonly IFormLinesRepository _linesRepo;
    private readonly IItemCombinationResolver _comboResolver;
    private readonly IMappingEngine _mappingEngine;
    private readonly IHttpExecutor _httpExecutor;
    private readonly IPostProcedureExecutor? _postProcExecutor;
    private readonly IIntegrationStatusTracker? _statusTracker;
    private readonly IIntegrationRecordStatusRepository? _recordStatusRepo;

    public IntegrationRunner(
        IIntegrationRepository integrationRepo,
        IIntegrationApiProfileRepository apiProfileRepo,
        IFormMetadataService formMeta,
        IFormLinesRepository linesRepo,
        IItemCombinationResolver comboResolver,
        IMappingEngine mappingEngine,
        IHttpExecutor httpExecutor,
        IPostProcedureExecutor? postProcExecutor = null,
        IIntegrationStatusTracker? statusTracker = null,
        IIntegrationRecordStatusRepository? recordStatusRepo = null)
    {
        _integrationRepo = integrationRepo;
        _apiProfileRepo = apiProfileRepo;
        _formMeta = formMeta;
        _linesRepo = linesRepo;
        _comboResolver = comboResolver;
        _mappingEngine = mappingEngine;
        _httpExecutor = httpExecutor;
        _postProcExecutor = postProcExecutor;
        _statusTracker = statusTracker;
        _recordStatusRepo = recordStatusRepo;
    }

    public async Task<IntegrationRunnerResult> RunAsync(
        int integrationId,
        string? sourceRecordId,
        IntegrationTriggerType triggerType,
        string? triggeredBy,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            // 1) Integration aggregate yukle — validation
            var integration = await _integrationRepo.GetByIdAsync(integrationId, ct);
            if (integration is null)
            {
                sw.Stop();
                return ValidationError(integrationId, "Integration bulunamadi.", sw);
            }
            if (!integration.IsActive)
            {
                sw.Stop();
                return ValidationError(integrationId, "Integration pasif.", sw);
            }
            // Faz O — Sadece-Prosedur modu: Endpoint NULL olabilir.
            // Ya endpoint dolu olmali ya da en az bir prosedur (Pre/Post) tanimli olmali.
            var isProcedureOnly = integration.Endpoint is null;
            if (isProcedureOnly
                && string.IsNullOrWhiteSpace(integration.PreProcedureName)
                && string.IsNullOrWhiteSpace(integration.PostProcedureName))
            {
                sw.Stop();
                return ValidationError(integrationId,
                    "Integration endpoint'i ve prosedurleri tanimsiz — calistirilacak hicbir is yok.", sw);
            }

            // 2) ApiProfile cek — sadece HTTP modunda gerekli
            CalibraHub.Domain.Entities.IntegrationApiProfile? profile = null;
            if (!isProcedureOnly)
            {
                profile = await _apiProfileRepo.GetByIdAsync(integration.Endpoint!.ApiProfileId, ct);
                if (profile is null)
                {
                    sw.Stop();
                    return ValidationError(integrationId, "Endpoint icin ApiProfile bulunamadi.", sw);
                }
                if (!profile.IsActive)
                {
                    sw.Stop();
                    return ValidationError(integrationId, "ApiProfile pasif.", sw);
                }
            }

            // 3) Sample record cek — validation
            var sample = await _formMeta.GetSampleRecordAsync(integration.SourceFormCode, sourceRecordId, ct);
            if (sample is null)
            {
                sw.Stop();
                return ValidationError(integrationId,
                    $"Form kaydi bulunamadi: {integration.SourceFormCode}/{sourceRecordId ?? "<son>"}", sw);
            }

            // 3b) Master-Detail — Form'un kalem form'u varsa kalem satirlarini cek
            //     ve kombinasyon kodlarini toplu resolve et.
            //     Form'un LinesFormCode'u Forms tablosundan okunur (FormMetadataService).
            IReadOnlyList<IReadOnlyDictionary<string, object?>>? linesData = null;
            IReadOnlyDictionary<int, string>? combinationCodes = null;

            var sourceForm = await _formMeta.GetFormAsync(integration.SourceFormCode, ct);
            if (sourceForm is not null
                && !string.IsNullOrWhiteSpace(sourceForm.LinesFormCode)
                && !string.IsNullOrWhiteSpace(sourceForm.LinesParentColumn)
                && !string.IsNullOrWhiteSpace(sample.RecordId))
            {
                // Lines parent FK genelde int (DocumentLine.DocumentId → Document.Id),
                // ama header sample.RecordId DocumentNumber string olabilir (Forms.BaseRecordKey).
                // Çözüm: sample.FieldValues içinde "Id" kolonu varsa onu öncelikli kullan,
                // yoksa RecordId fallback (string-PK formları için).
                var linesParentValue = sample.FieldValues.TryGetValue("Id", out var idVal) && idVal is not null and not DBNull
                    ? idVal.ToString()
                    : sample.RecordId;

                linesData = await _linesRepo.GetLinesAsync(
                    sourceForm.LinesFormCode,
                    sourceForm.LinesParentColumn,
                    linesParentValue!,
                    ct);

                // Kombinasyon kodlari — line.CombinationId'leri toplu resolve (N+1 onler)
                if (linesData.Count > 0)
                {
                    var combinationIds = linesData
                        .Select(l => l.TryGetValue("CombinationId", out var v) ? v : null)
                        .Where(v => v is not null and not DBNull)
                        .Select(v =>
                        {
                            if (v is int i) return i;
                            if (v is long l && l <= int.MaxValue) return (int)l;
                            if (v is decimal d) return (int)d;
                            return int.TryParse(v!.ToString(), out var p) ? p : 0;
                        })
                        .Where(i => i > 0)
                        .Distinct()
                        .ToList();

                    if (combinationIds.Count > 0)
                        combinationCodes = await _comboResolver.GetCombinationCodesAsync(combinationIds, ct);
                }
            }

            // Buraya kadar validation gecti — artik IntegrationRun yazilabilir.

            // 3c) PRE-PROCEDURE (opsiyonel) — HTTP'den ONCE calistir.
            //     Kullanim: lock atma, pre-validation, staging snapshot. Hata = entegrasyon iptal.
            //     run.Id henuz yok (0) — RunMeta'da geciyoruz; PostProc'ta gercek RunId olacak.
            string? preNote = null;
            bool preFailed = false;
            if (!string.IsNullOrWhiteSpace(integration.PreProcedureName)
                && _postProcExecutor is not null)
            {
                try
                {
                    var preMeta = new PostProcedureRunMeta(
                        RunId:          0,                    // henuz run ID yok
                        IntegrationId:  integrationId,
                        StartedAt:      DateTime.UtcNow,
                        SourceRecordId: sample.RecordId,
                        TriggeredBy:    triggeredBy);
                    var preRes = await _postProcExecutor.ExecuteAsync(
                        integration.PreProcedureName!,
                        integration.PreProcedureParamsJson,
                        sample.FieldValues,
                        linesData,
                        preMeta,
                        httpResponseBody: null,         // henuz HTTP yok
                        httpStatusCode:   null,
                        ct);
                    if (!preRes.Success)
                    {
                        preFailed = true;
                        preNote = $"[PreProc HATA: {preRes.ErrorMessage}]";
                    }
                }
                catch (Exception pex)
                {
                    preFailed = true;
                    preNote = $"[PreProc EXCEPTION: {pex.Message}]";
                }
            }

            // 4) MappingEngine — sadece HTTP modu (endpoint dolu) icin JSON body uret.
            //    Sadece-Prosedur modunda MappingEngine atlanir, body NULL.
            System.Text.Json.Nodes.JsonObject? body = null;
            if (!isProcedureOnly)
            {
                body = await _mappingEngine.BuildAsync(
                    integration, sample.FieldValues, linesData, combinationCodes, ct);
            }

            // 5) HttpExecutor — sadece HTTP modunda calisir. Sadece-Prosedur modunda
            //    "synthetic success" sonucu uretilir (PostProc tetiklenir, run loglanir).
            HttpInvocationResult http;
            if (preFailed)
            {
                http = new HttpInvocationResult
                {
                    Success      = false,
                    StatusCode   = null,
                    ResponseBody = null,
                    RequestBody  = body?.ToJsonString(),
                    ErrorMessage = "Pre-procedure başarısız — entegrasyon iptal edildi. " + (preNote ?? string.Empty),
                };
            }
            else if (isProcedureOnly)
            {
                http = new HttpInvocationResult
                {
                    Success      = true,
                    StatusCode   = null,
                    ResponseBody = "(sadece prosedür modu — HTTP isteği yok)",
                    RequestBody  = null,
                    ErrorMessage = null,
                };
            }
            else
            {
                http = await _httpExecutor.SendAsync(integration.Endpoint!, profile!, body!, ct);
            }

            sw.Stop();

            // 6) Run log INSERT (tek INSERT — pre-run yok)
            var status = http.Success ? IntegrationRunStatus.Success : IntegrationRunStatus.Failed;
            var run = new IntegrationRun
            {
                IntegrationId = integrationId,
                TriggerType = triggerType,
                SourceRecordId = sample.RecordId.Length > 0 ? sample.RecordId : sourceRecordId,
                StartedAt = DateTime.UtcNow.AddMilliseconds(-sw.ElapsedMilliseconds),
                FinishedAt = DateTime.UtcNow,
                DurationMs = (int)sw.ElapsedMilliseconds,
                Status = status,
                HttpStatusCode = http.StatusCode,
                RequestBody = http.RequestBody,
                ResponseBody = http.ResponseBody,
                ErrorMessage = http.ErrorMessage,
                TriggeredBy = triggeredBy,
            };
            run.Id = await _integrationRepo.AddRunAsync(run, ct);

            // 6b) Belge tablo tracking — sourceForm.BaseTable doluysa kayda 'Sent'/'Failed' isaretle.
            //     Bu sayede liste/edit ekraninda "Aktarildi" rozeti gosterilebilir + duplicate guard.
            if (_statusTracker is not null
                && sourceForm is not null
                && !string.IsNullOrWhiteSpace(sourceForm.BaseTable)
                && !string.IsNullOrWhiteSpace(sourceForm.BaseRecordKey)
                && !string.IsNullOrWhiteSpace(run.SourceRecordId))
            {
                try
                {
                    if (http.Success)
                        await _statusTracker.MarkSentAsync(
                            sourceForm.BaseTable, sourceForm.BaseRecordKey, run.SourceRecordId,
                            integrationId, run.Id, ct);
                    else
                        await _statusTracker.MarkFailedAsync(
                            sourceForm.BaseTable, sourceForm.BaseRecordKey, run.SourceRecordId,
                            integrationId, run.Id, ct);
                }
                catch { /* tracker hatasi run sonucunu bozmasin */ }
            }

            // 6c) Aktarim Kuyrugu (IntegrationRecordStatus) — kuyruk sayfasinin
            //     veri kaynagi. Form-agnostik (BaseTable'a bagimli degil),
            //     Skipped olan kayitlar uzerine yazmaz (kullanici "haric tut"
            //     dediyse run sonucu o durumu degistirmesin).
            if (_recordStatusRepo is not null && !string.IsNullOrWhiteSpace(run.SourceRecordId))
            {
                try
                {
                    var qStatus = http.Success
                        ? CalibraHub.Domain.Enums.IntegrationRecordStatusType.Sent
                        : CalibraHub.Domain.Enums.IntegrationRecordStatusType.Failed;
                    await _recordStatusRepo.UpsertRunResultAsync(
                        integrationId,
                        run.SourceRecordId!,
                        qStatus,
                        run.Id,
                        http.Success ? null : (http.ErrorMessage ?? $"HTTP {http.StatusCode}"),
                        triggeredBy,
                        ct);
                }
                catch { /* kuyruk yazimi run sonucunu bozmasin */ }
            }

            // 7) Post-procedure (opsiyonel) — sadece HTTP basarili ise calistir.
            //    Hata olursa run log'a appendle (orijinal sonucu degistirme).
            string? postNote = null;
            if (http.Success
                && !string.IsNullOrWhiteSpace(integration.PostProcedureName)
                && _postProcExecutor is not null)
            {
                try
                {
                    var meta = new PostProcedureRunMeta(
                        RunId:          run.Id,
                        IntegrationId:  integrationId,
                        StartedAt:      run.StartedAt,
                        SourceRecordId: run.SourceRecordId,
                        TriggeredBy:    triggeredBy);
                    var postRes = await _postProcExecutor.ExecuteAsync(
                        integration.PostProcedureName!,
                        integration.PostProcedureParamsJson,
                        sample.FieldValues,
                        linesData,
                        meta,
                        http.ResponseBody,
                        http.StatusCode,
                        ct);
                    if (!postRes.Success)
                        postNote = $"[PostProc HATA: {postRes.ErrorMessage}]";
                }
                catch (Exception pex)
                {
                    postNote = $"[PostProc EXCEPTION: {pex.Message}]";
                }
            }

            // Pre + Post note'lari final ErrorMessage'a birlestir
            var notes = new List<string>();
            if (!string.IsNullOrWhiteSpace(http.ErrorMessage)) notes.Add(http.ErrorMessage);
            if (!string.IsNullOrWhiteSpace(preNote))           notes.Add(preNote);
            if (!string.IsNullOrWhiteSpace(postNote))          notes.Add(postNote);

            return new IntegrationRunnerResult
            {
                Success = http.Success,
                RunId = run.Id,
                HttpStatusCode = http.StatusCode,
                ErrorMessage = notes.Count > 0 ? string.Join(' ', notes) : null,
                RequestBody = http.RequestBody,
                ResponseBody = http.ResponseBody,
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            // Beklenmedik hata — yine de bir RUN log INSERT etmeye calismayalim (FK riski).
            return new IntegrationRunnerResult
            {
                Success = false,
                RunId = 0,
                HttpStatusCode = null,
                ErrorMessage = $"Beklenmedik hata: {ex.Message}",
            };
        }
    }

    /// <summary>
    /// Validation hatalari icin (integration yok / pasif / endpoint yok). RUN log YAZILMAZ
    /// cunku FK gerektiriyor; bunlar zaten hatali state, audit'e geregi yok. Caller mesaji gorur.
    /// </summary>
    private static IntegrationRunnerResult ValidationError(int integrationId, string message, Stopwatch sw)
        => new()
        {
            Success = false,
            RunId = 0,
            HttpStatusCode = null,
            ErrorMessage = message,
        };
}
