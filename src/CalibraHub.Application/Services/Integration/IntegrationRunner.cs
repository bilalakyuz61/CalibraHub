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
    private readonly IntegrationFilterEngine? _filterEngine;

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
        IIntegrationRecordStatusRepository? recordStatusRepo = null,
        IntegrationFilterEngine? filterEngine = null)
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
        _filterEngine = filterEngine;
    }

    public Task<IntegrationRunnerResult> RunAsync(
        int integrationId,
        string? sourceRecordId,
        IntegrationTriggerType triggerType,
        string? triggeredBy,
        CancellationToken ct)
        => RunInternalAsync(integrationId, sourceRecordId, triggerType, triggeredBy,
                            parentRunId: null, ancestorIntegrationIds: null, depth: 0, ct);

    // 2026-05-22 Cascade: Recursion-aware private runner.
    // ParentRunId: Bu run cascade ile tetiklendiyse parent'in RunId'si (audit tree icin).
    // ancestorIntegrationIds: Cycle detection — zincirde gorulen integration ID'ler.
    // depth: Recursion guard — max 5 derinlik (DefaultMaxCascadeDepth).
    private const int DefaultMaxCascadeDepth = 5;

    private async Task<IntegrationRunnerResult> RunInternalAsync(
        int integrationId,
        string? sourceRecordId,
        IntegrationTriggerType triggerType,
        string? triggeredBy,
        long? parentRunId,
        HashSet<int>? ancestorIntegrationIds,
        int depth,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        // Recursion guard — derinligi as
        if (depth > DefaultMaxCascadeDepth)
        {
            return new IntegrationRunnerResult
            {
                Success = false,
                RunId = 0,
                HttpStatusCode = null,
                ErrorMessage = $"Cascade derinligi asildi (max {DefaultMaxCascadeDepth}). Cycle veya zincir cok uzun.",
            };
        }
        // Cycle guard — bu integration zincirde zaten var mi?
        if (ancestorIntegrationIds is not null && ancestorIntegrationIds.Contains(integrationId))
        {
            return new IntegrationRunnerResult
            {
                Success = false,
                RunId = 0,
                HttpStatusCode = null,
                ErrorMessage = $"Cascade cycle algilandi: Integration #{integrationId} zincirde zaten var.",
            };
        }

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

            // 3a) 2026-05-22 Pre-flight Filter guard — Integration.SourceFilterJson kuralları
            // sample record'a karşı değerlendirilir. Fail ise Skipped log + RecordStatus.Skipped.
            // Queue zaten filtre uyguluyor ama defense in depth — manuel button veya OnSave
            // dispatcher direkt çağırdıysa runner yine doğru tepki versin.
            if (_filterEngine is not null && !string.IsNullOrWhiteSpace(integration.SourceFilterJson))
            {
                var passes = _filterEngine.EvaluateRecord(integration.SourceFilterJson, sample.FieldValues);
                if (!passes)
                {
                    sw.Stop();
                    var skipReason = "Pre-flight filter başarısız — kayıt entegrasyon koşullarını sağlamıyor.";
                    var skipRun = new IntegrationRun
                    {
                        IntegrationId  = integrationId,
                        TriggerType    = triggerType,
                        SourceRecordId = sample.RecordId.Length > 0 ? sample.RecordId : sourceRecordId,
                        StartedAt      = DateTime.UtcNow.AddMilliseconds(-sw.ElapsedMilliseconds),
                        FinishedAt     = DateTime.UtcNow,
                        DurationMs     = (int)sw.ElapsedMilliseconds,
                        Status         = IntegrationRunStatus.Skipped,
                        HttpStatusCode = null,
                        RequestBody    = null,
                        ResponseBody   = null,
                        ErrorMessage   = skipReason,
                        TriggeredBy    = triggeredBy,
                    };
                    skipRun.Id = await _integrationRepo.AddRunAsync(skipRun, ct);
                    return new IntegrationRunnerResult
                    {
                        Success        = false,
                        RunId          = skipRun.Id,
                        HttpStatusCode = null,
                        ErrorMessage   = skipReason,
                    };
                }
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

            // 3b2) 2026-05-22 CASCADE — Mapping satirlarinda CascadeToIntegrationId set olanlari
            // tara, FK degerini sample/lines'tan oku, IntegrationRecordStatus'a bak:
            //   • Status=Sent  → atla (zaten gonderilmis)
            //   • diger durum  → cascade child run tetikle (TriggerType=Cascade, ParentRunId=this run)
            // Herhangi bir cascade fail olursa parent integration FAIL — HTTP cagrilmaz, ana run
            // Status=Failed. Fail-fast davranisi.
            //
            // ParentRunId icin parent run'i pre-insert ediyoruz (status=Pending) — boylece child'lar
            // bu Id'yi ParentRunId olarak alir. Cascade yoksa pre-insert yapilmaz, klasik tek-INSERT
            // pattern korunur (perf + audit minimallik).
            var cascadeMappings = integration.Mappings
                .Where(m => m.CascadeToIntegrationId.HasValue && m.CascadeToIntegrationId.Value > 0)
                .ToList();

            long? thisRunId = null;
            string? cascadeError = null;
            if (cascadeMappings.Count > 0 && _recordStatusRepo is not null)
            {
                // Pre-insert parent run (status=Pending) — child runs icin ParentRunId
                var parentRunStub = new IntegrationRun
                {
                    IntegrationId  = integrationId,
                    TriggerType    = triggerType,
                    SourceRecordId = sample.RecordId.Length > 0 ? sample.RecordId : sourceRecordId,
                    StartedAt      = DateTime.UtcNow.AddMilliseconds(-sw.ElapsedMilliseconds),
                    Status         = IntegrationRunStatus.Pending,
                    TriggeredBy    = triggeredBy,
                    ParentRunId    = parentRunId,
                };
                parentRunStub.Id = await _integrationRepo.AddRunAsync(parentRunStub, ct);
                thisRunId = parentRunStub.Id;

                // Cycle-set'i bu integration'la genislet
                var nextAncestors = ancestorIntegrationIds is null
                    ? new HashSet<int> { integrationId }
                    : new HashSet<int>(ancestorIntegrationIds) { integrationId };

                foreach (var m in cascadeMappings)
                {
                    var targetIntegrationId = m.CascadeToIntegrationId!.Value;

                    // Stale-ID guard: cascade hedef integration silinmis veya pasif olabilir.
                    // (FK ON DELETE NO ACTION oldugu icin silme RED edilir ama AllowAsCascadeTarget
                    // false'a cekilmis veya integration deactivate edilmis olabilir.)
                    var targetIntegration = await _integrationRepo.GetByIdAsync(targetIntegrationId, ct);
                    if (targetIntegration is null)
                    {
                        cascadeError = $"Cascade [{m.TargetPath}]: hedef integration #{targetIntegrationId} bulunamadi (silinmis olabilir).";
                        break;
                    }
                    if (!targetIntegration.IsActive)
                    {
                        cascadeError = $"Cascade [{m.TargetPath}]: hedef integration '{targetIntegration.Name}' pasif.";
                        break;
                    }

                    // FK degerlerini topla — Header tek deger, Lines her satir icin ayri deger
                    var fkValues = ResolveCascadeFkValues(m, sample.FieldValues, linesData);
                    if (fkValues.Count == 0) continue;   // FK bos veya bulunamadi — cascade etmiyoruz

                    foreach (var fkValue in fkValues.Distinct())
                    {
                        // CascadeByValue: fkValue bir KOD string'i (orn. "120-01-001"), entity ID degil.
                        // Hedef integration'in SourceCodeColumn'u ile kod → entity ID cevirimi yapilir.
                        string cascadeRecordId = fkValue;
                        if (m.CascadeByValue && !string.IsNullOrWhiteSpace(targetIntegration.SourceCodeColumn))
                        {
                            var resolvedId = await _formMeta.FindRecordIdByFieldValueAsync(
                                targetIntegration.SourceFormCode,
                                targetIntegration.SourceCodeColumn,
                                fkValue, ct);
                            if (string.IsNullOrWhiteSpace(resolvedId))
                                continue;   // Kod CalibraHub'da bulunamadı — bu satırı atla
                            cascadeRecordId = resolvedId;
                        }

                        // Daha once Sent edilmis mi, ya da kullanici Aktarim Kuyrugu'nda
                        // manuel "haric tut" (Skipped) demis mi?
                        var existingStatus = await _recordStatusRepo.GetAsync(targetIntegrationId, cascadeRecordId, ct);
                        if (existingStatus is not null &&
                            (existingStatus.Status == CalibraHub.Domain.Enums.IntegrationRecordStatusType.Sent ||
                             existingStatus.Status == CalibraHub.Domain.Enums.IntegrationRecordStatusType.Skipped))
                        {
                            continue;   // atla — ERP'de zaten var veya kullanici haric tuttu
                        }

                        // Recursive cascade — child run
                        var childResult = await RunInternalAsync(
                            targetIntegrationId,
                            cascadeRecordId,
                            IntegrationTriggerType.Cascade,
                            triggeredBy: $"cascade:#{integrationId}",
                            parentRunId: thisRunId,
                            ancestorIntegrationIds: nextAncestors,
                            depth: depth + 1,
                            ct);

                        if (!childResult.Success)
                        {
                            cascadeError = $"Cascade [{m.TargetPath} → Integration#{targetIntegrationId}/{cascadeRecordId}]: " +
                                           (childResult.ErrorMessage ?? "bilinmeyen hata");
                            break;   // fail-fast — bu mapping'i terk et
                        }
                    }
                    if (cascadeError is not null) break;   // fail-fast — diger mapping'leri de atla
                }
            }

            // Cascade fail oldu mu? Evet ise parent run'i Failed olarak finalize et ve HTTP'yi atla.
            if (cascadeError is not null)
            {
                sw.Stop();
                var failedRun = new IntegrationRun
                {
                    Id             = thisRunId ?? 0,
                    IntegrationId  = integrationId,
                    TriggerType    = triggerType,
                    SourceRecordId = sample.RecordId.Length > 0 ? sample.RecordId : sourceRecordId,
                    StartedAt      = DateTime.UtcNow.AddMilliseconds(-sw.ElapsedMilliseconds),
                    FinishedAt     = DateTime.UtcNow,
                    DurationMs     = (int)sw.ElapsedMilliseconds,
                    Status         = IntegrationRunStatus.Failed,
                    HttpStatusCode = null,
                    RequestBody    = null,
                    ResponseBody   = null,
                    ErrorMessage   = cascadeError,
                    TriggeredBy    = triggeredBy,
                    ParentRunId    = parentRunId,
                };
                if (thisRunId.HasValue)
                    await _integrationRepo.UpdateRunAsync(failedRun, ct);   // pre-insert edilmisti, finalize
                else
                    failedRun.Id = await _integrationRepo.AddRunAsync(failedRun, ct);

                return new IntegrationRunnerResult
                {
                    Success = false,
                    RunId = failedRun.Id,
                    HttpStatusCode = null,
                    ErrorMessage = cascadeError,
                };
            }

            // Buraya kadar validation + cascade gecti — artik IntegrationRun yazilabilir.

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

            // 5b) Yanit semantigi — HTTP 200 olsa bile hedef sistem (Netsis vb.) body
            // icinde "IsSuccessful: false" + "ErrorCode/ErrorDesc" donmus olabilir.
            // Bu durumda run'i basarili saymak yanlis — Failed olarak isaretle ve
            // ErrorMessage'a temizlenmis, okunabilir bir ozet koy. UI tarafi (toast +
            // run log listesi) bu temiz mesaji gosterir; raw body Response tab'inde kalir.
            if (http.Success)
            {
                var (semOk, parsedErr) = ParseResponseSemantics(http.ResponseBody);
                if (!semOk)
                {
                    http = new HttpInvocationResult
                    {
                        Success      = false,
                        StatusCode   = http.StatusCode,
                        RequestBody  = http.RequestBody,
                        ResponseBody = http.ResponseBody,
                        ErrorMessage = parsedErr ?? "Hedef sistem semantik hata dondu (IsSuccessful=false).",
                        DurationMs   = http.DurationMs,
                    };
                }
            }

            // 6) Run log INSERT veya UPDATE.
            // Cascade pre-insert yapildiysa (thisRunId dolu) UPDATE; aksi halde tek INSERT.
            var status = http.Success ? IntegrationRunStatus.Success : IntegrationRunStatus.Failed;
            var run = new IntegrationRun
            {
                Id = thisRunId ?? 0,
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
                ParentRunId = parentRunId,
            };
            if (thisRunId.HasValue)
                await _integrationRepo.UpdateRunAsync(run, ct);   // cascade pre-insert finalize
            else
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
                        int.TryParse(triggeredBy, out var _tbid) ? _tbid : (int?)null,
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

    /// <summary>
    /// 2026-05-22 Cascade: Bir mapping satirinin FK source field degerini oku.
    /// SourceSection'a gore Header sample.FieldValues'tan veya Lines linesData'dan ceker.
    /// Combination section icin cascade desteklenmiyor (kombinasyon kodu zaten resolver tarafindan
    /// dolduruluyor — ayri integration tetiklemek anlamsiz).
    ///
    /// Donus: cascade hedef RunAsync'e gecirilecek recordId string listesi (Header icin 1 eleman,
    /// Lines icin her satirda bir eleman). Bos/null/0 olanlar elenir.
    /// </summary>
    private static List<string> ResolveCascadeFkValues(
        CalibraHub.Domain.Entities.IntegrationMapping mapping,
        IReadOnlyDictionary<string, object?> headerValues,
        IReadOnlyList<IReadOnlyDictionary<string, object?>>? linesData)
    {
        var result = new List<string>();
        // FK alan adi sourceType'a bagli:
        //   FormField (0)            → SourceValue dogrudan FK (orn. "ContactId")
        //   Lookup (3) / Function(4) → LookupSourceField FK (SourceValue rehber kodu / function id)
        //   Diger (Constant/Formula) → cascade anlamsiz
        string? sourceField = mapping.SourceType switch
        {
            CalibraHub.Domain.Enums.IntegrationSourceType.FormField => mapping.SourceValue,
            CalibraHub.Domain.Enums.IntegrationSourceType.Lookup    => mapping.LookupSourceField,
            CalibraHub.Domain.Enums.IntegrationSourceType.Function  => mapping.LookupSourceField,
            _ => null,
        };
        if (string.IsNullOrWhiteSpace(sourceField)) return result;

        var section = mapping.SourceSection;
        if (string.Equals(section, "Lines", StringComparison.OrdinalIgnoreCase))
        {
            if (linesData is null) return result;
            foreach (var line in linesData)
            {
                if (line.TryGetValue(sourceField, out var v) && IsMeaningful(v))
                    result.Add(v!.ToString()!);
            }
        }
        else if (string.Equals(section, "Combination", StringComparison.OrdinalIgnoreCase))
        {
            // Combination cascade desteklenmiyor — runtime resolver zaten kodu cozer
            return result;
        }
        else
        {
            // Header (default)
            if (headerValues.TryGetValue(sourceField, out var v) && IsMeaningful(v))
                result.Add(v!.ToString()!);
        }
        return result;

        static bool IsMeaningful(object? v)
        {
            if (v is null or DBNull) return false;
            var s = v.ToString();
            if (string.IsNullOrWhiteSpace(s)) return false;
            // FK genelde int — 0 anlamsiz
            if (int.TryParse(s, out var i) && i == 0) return false;
            return true;
        }
    }

    /// <summary>
    /// Hedef sistemin JSON yanitini analiz eder. HTTP 200 olsa bile body icinde
    /// "IsSuccessful: false" / "success: false" gibi bir flag varsa bu semantik
    /// hata olarak yorumlanir. Donus: (basariliMi, temizErrorMessage). Mesaj
    /// "Hata {Code}: {Desc}" formatinda; XML tag'leri ve \r\n escape'leri temizlenir.
    /// </summary>
    private static (bool SemanticSuccess, string? ParsedError) ParseResponseSemantics(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody)) return (true, null);
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(responseBody);
            var root = doc.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object) return (true, null);

            // IsSuccessful / IsSuccess / Success / success — case-insensitive lookup
            bool? semOk = TryGetBool(root, "IsSuccessful")
                       ?? TryGetBool(root, "IsSuccess")
                       ?? TryGetBool(root, "Success")
                       ?? TryGetBool(root, "success")
                       ?? TryGetBool(root, "ok");

            // Eger flag yoksa veya true ise — yanit basarili kabul (klasik HTTP semantigi)
            if (semOk != false) return (true, null);

            // false ise — ErrorCode + ErrorDesc/message/error/detail topla
            var code = TryGetString(root, "ErrorCode")
                    ?? TryGetString(root, "errorCode")
                    ?? TryGetString(root, "code");
            var desc = TryGetString(root, "ErrorDesc")
                    ?? TryGetString(root, "errorDesc")
                    ?? TryGetString(root, "error")
                    ?? TryGetString(root, "message")
                    ?? TryGetString(root, "detail");

            var cleaned = CleanErrorMessage(desc);
            string msg = !string.IsNullOrWhiteSpace(code)
                ? (cleaned is null ? $"Hata {code}" : $"Hata {code}: {cleaned}")
                : (cleaned ?? "Hedef sistem hata dondu (IsSuccessful=false).");

            return (false, msg);
        }
        catch
        {
            // Body JSON degilse veya parse hatasi varsa — HTTP semantiginie guven
            return (true, null);
        }
    }

    private static bool? TryGetBool(System.Text.Json.JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el)) return null;
        return el.ValueKind switch
        {
            System.Text.Json.JsonValueKind.True  => true,
            System.Text.Json.JsonValueKind.False => false,
            _ => null,
        };
    }

    private static string? TryGetString(System.Text.Json.JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el)) return null;
        return el.ValueKind == System.Text.Json.JsonValueKind.String ? el.GetString() : null;
    }

    /// <summary>
    /// \r\n escape'leri, XML tag'leri ve fazla bosluklari temizler. Cok uzunsa keser.
    /// Toast mesaji icin tek satir, okunabilir cikti.
    /// </summary>
    private static string? CleanErrorMessage(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ");
        // XML tag'lerini cikar (orn. <ErrorHeader>...</ErrorHeader>)
        t = System.Text.RegularExpressions.Regex.Replace(t, @"<[^>]+>", " ");
        // Cift bosluklari teke indir
        t = System.Text.RegularExpressions.Regex.Replace(t, @"\s+", " ").Trim();
        if (t.Length == 0) return null;
        if (t.Length > 400) t = t.Substring(0, 397) + "...";
        return t;
    }
}
