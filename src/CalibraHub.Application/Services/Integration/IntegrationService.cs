using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Services.Integration;

public sealed class IntegrationService : IIntegrationService
{
    private readonly IIntegrationRepository _repo;
    private readonly IIntegrationApiProfileRepository _apiProfileRepo;
    private readonly IFormMetadataService _formMeta;
    private readonly IMappingEngine _mappingEngine;
    private readonly IHttpExecutor _httpExecutor;
    private readonly IFormLinesRepository? _linesRepo;
    private readonly IItemCombinationResolver? _comboResolver;

    public IntegrationService(
        IIntegrationRepository repo,
        IIntegrationApiProfileRepository apiProfileRepo,
        IFormMetadataService formMeta,
        IMappingEngine mappingEngine,
        IHttpExecutor httpExecutor,
        IFormLinesRepository? linesRepo = null,
        IItemCombinationResolver? comboResolver = null)
    {
        _repo = repo;
        _apiProfileRepo = apiProfileRepo;
        _formMeta = formMeta;
        _mappingEngine = mappingEngine;
        _httpExecutor = httpExecutor;
        _linesRepo = linesRepo;
        _comboResolver = comboResolver;
    }

    public async Task<IReadOnlyList<IntegrationListItemDto>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var integrations = await _repo.ListAsync(includeInactive, ct);
        var result = new List<IntegrationListItemDto>(integrations.Count);

        // Endpoint + form labels icin yardimci cache
        var forms = await _formMeta.ListFormsAsync(ct);
        var formLabelById = forms.ToDictionary(f => f.FormCode, f => f.FormName, StringComparer.OrdinalIgnoreCase);

        foreach (var integ in integrations)
        {
            // Endpoint + profile detayi (display amacli). Faz O — endpoint NULL olabilir (sadece prosedur modu).
            var endpoint = integ.TargetEndpointId.HasValue
                ? await _repo.GetEndpointByIdAsync(integ.TargetEndpointId.Value, ct)
                : null;
            var profile = endpoint is not null
                ? await _apiProfileRepo.GetByIdAsync(endpoint.ApiProfileId, ct)
                : null;

            // Trigger sayisi (aktif)
            var triggers = await _repo.GetTriggersAsync(integ.Id, ct);
            var activeTriggerCount = triggers.Count(t => t.IsActive);

            // Run audit ozeti — son 1 run yeterli (top 1)
            var lastRuns = await _repo.GetRunsAsync(integ.Id, 1, ct);
            var lastRun = lastRuns.FirstOrDefault();
            var totalRunCount = await CountRunsAsync(integ.Id, ct);

            formLabelById.TryGetValue(integ.SourceFormCode, out var formLabel);

            result.Add(new IntegrationListItemDto(
                Id: integ.Id,
                Name: integ.Name,
                Description: integ.Description,
                SourceFormCode: integ.SourceFormCode,
                SourceFormLabel: formLabel,
                TargetEndpointId: integ.TargetEndpointId,
                EndpointName: endpoint?.Name ?? "(silinmis endpoint)",
                ApiProfileName: profile?.Name ?? "(silinmis profile)",
                ErrorBehavior: integ.ErrorBehavior.ToString(),
                IsActive: integ.IsActive,
                VersionNo: integ.VersionNo,
                TriggerCount: activeTriggerCount,
                Created: integ.Created,
                Updated: integ.Updated,
                RunCount: totalRunCount,
                LastRunAt: lastRun?.StartedAt,
                LastRunStatus: lastRun?.Status.ToString()));
        }

        return result;
    }

    private async Task<int> CountRunsAsync(int integrationId, CancellationToken ct)
    {
        // GetRunsAsync limit ile kullaniyoruz; toplam sayim icin repository'de COUNT(*) endpoint'i yok
        // — V1 minimum: limit=1000 cek ve sayisini al (audit sayisi onlerde olur). V2'de COUNT(*) sorgu eklenir.
        var runs = await _repo.GetRunsAsync(integrationId, 1000, ct);
        return runs.Count;
    }

    public async Task<IntegrationDetailDto?> GetDetailAsync(int id, CancellationToken ct)
    {
        var integ = await _repo.GetByIdAsync(id, ct);
        if (integ is null) return null;

        // ApiProfile adi icin
        var profile = integ.Endpoint is not null
            ? await _apiProfileRepo.GetByIdAsync(integ.Endpoint.ApiProfileId, ct)
            : null;

        return new IntegrationDetailDto(
            Id: integ.Id,
            Name: integ.Name,
            Description: integ.Description,
            SourceFormCode: integ.SourceFormCode,
            TargetEndpointId: integ.TargetEndpointId,
            ErrorBehavior: integ.ErrorBehavior,
            RetryCount: integ.RetryCount,
            IsActive: integ.IsActive,
            VersionNo: integ.VersionNo,
            Mappings: integ.Mappings.Select(m => new IntegrationMappingDto(
                Id: m.Id,
                TargetPath: m.TargetPath,
                TargetDataType: m.TargetDataType,
                SourceType: m.SourceType,
                SourceValue: m.SourceValue,
                LookupSourceField: m.LookupSourceField,
                DefaultValue: m.DefaultValue,
                FormatPattern: m.FormatPattern,
                IsRequired: m.IsRequired,
                SortOrder: m.SortOrder,
                GroupKey: m.GroupKey,
                SourceSection: string.IsNullOrWhiteSpace(m.SourceSection) ? "Header" : m.SourceSection,
                LookupFiltersJson: m.LookupFiltersJson,
                LookupReturnColumn: m.LookupReturnColumn,
                LookupParam:        m.LookupParam,
                CascadeToIntegrationId: m.CascadeToIntegrationId)).ToList(),
            Triggers: integ.Triggers.Select(t => new IntegrationTriggerDto(
                Id: t.Id,
                TriggerType: t.TriggerType,
                Config: t.Config,
                IsActive: t.IsActive)).ToList(),
            Endpoint: integ.Endpoint is null ? null : new IntegrationEndpointDto(
                Id: integ.Endpoint.Id,
                ApiProfileId: integ.Endpoint.ApiProfileId,
                ApiProfileName: profile?.Name ?? "(silinmis profile)",
                Name: integ.Endpoint.Name,
                HttpMethod: integ.Endpoint.HttpMethod,
                UrlTemplate: integ.Endpoint.UrlTemplate,
                BodySchema: integ.Endpoint.BodySchema,
                Description: integ.Endpoint.Description,
                IsActive: integ.Endpoint.IsActive),
            PreProcedureName:        integ.PreProcedureName,
            PreProcedureParamsJson:  integ.PreProcedureParamsJson,
            PostProcedureName:       integ.PostProcedureName,
            PostProcedureParamsJson: integ.PostProcedureParamsJson,
            SourceFilterJson:        integ.SourceFilterJson,
            AllowAsCascadeTarget:    integ.AllowAsCascadeTarget);
    }

    public async Task<int> SaveAsync(SaveIntegrationRequest request, int? currentUserId, CancellationToken ct)
    {
        ValidateRequest(request);

        int integrationId;
        if (request.Id <= 0)
        {
            // Create
            var entity = new global::CalibraHub.Domain.Entities.Integration
            {
                Name = request.Name.Trim(),
                Description = request.Description,
                SourceFormCode = request.SourceFormCode.Trim(),
                TargetEndpointId = request.TargetEndpointId,
                ErrorBehavior = request.ErrorBehavior,
                RetryCount = request.RetryCount,
                IsActive = request.IsActive,
                VersionNo = 1,
                CreatedById = currentUserId,
                PreProcedureName        = string.IsNullOrWhiteSpace(request.PreProcedureName) ? null : request.PreProcedureName.Trim(),
                PreProcedureParamsJson  = string.IsNullOrWhiteSpace(request.PreProcedureParamsJson) ? null : request.PreProcedureParamsJson,
                PostProcedureName       = string.IsNullOrWhiteSpace(request.PostProcedureName) ? null : request.PostProcedureName.Trim(),
                PostProcedureParamsJson = string.IsNullOrWhiteSpace(request.PostProcedureParamsJson) ? null : request.PostProcedureParamsJson,
                SourceFilterJson        = string.IsNullOrWhiteSpace(request.SourceFilterJson) ? null : request.SourceFilterJson,
                AllowAsCascadeTarget    = request.AllowAsCascadeTarget,
            };
            integrationId = await _repo.AddAsync(entity, ct);
        }
        else
        {
            // Update — mevcut VersionNo'yu artir
            var existing = await _repo.GetByIdAsync(request.Id, ct)
                ?? throw new ArgumentException($"Integration bulunamadi: {request.Id}");

            existing.Name = request.Name.Trim();
            existing.Description = request.Description;
            existing.SourceFormCode = request.SourceFormCode.Trim();
            existing.TargetEndpointId = request.TargetEndpointId;
            existing.ErrorBehavior = request.ErrorBehavior;
            existing.RetryCount = request.RetryCount;
            existing.IsActive = request.IsActive;
            existing.VersionNo = existing.VersionNo + 1;
            existing.UpdatedById = currentUserId;
            existing.Updated = DateTime.UtcNow;
            existing.PreProcedureName        = string.IsNullOrWhiteSpace(request.PreProcedureName) ? null : request.PreProcedureName.Trim();
            existing.PreProcedureParamsJson  = string.IsNullOrWhiteSpace(request.PreProcedureParamsJson) ? null : request.PreProcedureParamsJson;
            existing.PostProcedureName       = string.IsNullOrWhiteSpace(request.PostProcedureName) ? null : request.PostProcedureName.Trim();
            existing.PostProcedureParamsJson = string.IsNullOrWhiteSpace(request.PostProcedureParamsJson) ? null : request.PostProcedureParamsJson;
            existing.SourceFilterJson        = string.IsNullOrWhiteSpace(request.SourceFilterJson) ? null : request.SourceFilterJson;
            existing.AllowAsCascadeTarget    = request.AllowAsCascadeTarget;
            await _repo.UpdateAsync(existing, ct);
            integrationId = existing.Id;
        }

        // Mappings — replace (tum eskileri sil, yenileri ekle).
        // ÖNEMLİ: Lookup zenginleştirme alanları (LookupFiltersJson + LookupReturnColumn)
        // burada eksikti — DTO'da vardı ama entity'ye taşınmıyordu, kaydedince kayboluyordu.
        var mappings = request.Mappings.Select(m => new IntegrationMapping
        {
            IntegrationId = integrationId,
            TargetPath = m.TargetPath,
            TargetDataType = m.TargetDataType,
            SourceType = m.SourceType,
            SourceValue = m.SourceValue,
            LookupSourceField = m.LookupSourceField,
            DefaultValue = m.DefaultValue,
            FormatPattern = m.FormatPattern,
            IsRequired = m.IsRequired,
            SortOrder = m.SortOrder,
            GroupKey = m.GroupKey,
            SourceSection      = string.IsNullOrWhiteSpace(m.SourceSection) ? "Header" : m.SourceSection,
            LookupFiltersJson  = m.LookupFiltersJson,
            LookupReturnColumn = m.LookupReturnColumn,
            LookupParam        = m.LookupParam,
            CascadeToIntegrationId = m.CascadeToIntegrationId,
        }).ToList();
        await _repo.ReplaceMappingsAsync(integrationId, mappings, ct);

        // Triggers — replace
        var triggers = request.Triggers.Select(t => new IntegrationTrigger
        {
            IntegrationId = integrationId,
            TriggerType = t.TriggerType,
            Config = t.Config,
            IsActive = t.IsActive,
        }).ToList();
        await _repo.ReplaceTriggersAsync(integrationId, triggers, ct);

        return integrationId;
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        // Hard delete akisi:
        //   1. IntegrationRun kayitlari (FK_IntegrationRun_Integration CASCADE degil — manuel sil)
        //   2. Integration row (CASCADE ile IntegrationMapping + IntegrationTrigger otomatik silinir)
        //
        // Audit log korumak isteyen kullanici "Sil" yerine Toggle (Pasif) kullanir.
        await _repo.DeleteRunsForIntegrationAsync(id, ct);
        await _repo.DeleteAsync(id, ct);
    }

    public async Task<bool> ToggleActiveAsync(int id, CancellationToken ct)
    {
        var integ = await _repo.GetByIdAsync(id, ct)
            ?? throw new ArgumentException($"Integration bulunamadi: {id}");
        integ.IsActive = !integ.IsActive;
        integ.Updated = DateTime.UtcNow;
        await _repo.UpdateAsync(integ, ct);
        return integ.IsActive;
    }

    public async Task<int> DuplicateAsync(int id, int? currentUserId, CancellationToken ct)
    {
        var src = await _repo.GetByIdAsync(id, ct)
            ?? throw new ArgumentException($"Integration bulunamadi: {id}");

        var copy = new global::CalibraHub.Domain.Entities.Integration
        {
            Name = $"{src.Name} (Kopya)",
            Description = src.Description,
            SourceFormCode = src.SourceFormCode,
            TargetEndpointId = src.TargetEndpointId,
            ErrorBehavior = src.ErrorBehavior,
            RetryCount = src.RetryCount,
            IsActive = false,                   // pasif kopya — kullanici aktif et dedikten sonra calissin
            VersionNo = 1,
            CreatedById = currentUserId,
        };
        var newId = await _repo.AddAsync(copy, ct);

        // Mappings'i de kopyala (id'leri sifirla) — Lookup zenginleştirme alanları da dahil
        var copiedMappings = src.Mappings.Select(m => new IntegrationMapping
        {
            IntegrationId = newId,
            TargetPath = m.TargetPath,
            TargetDataType = m.TargetDataType,
            SourceType = m.SourceType,
            SourceValue = m.SourceValue,
            LookupSourceField = m.LookupSourceField,
            DefaultValue = m.DefaultValue,
            FormatPattern = m.FormatPattern,
            IsRequired = m.IsRequired,
            SortOrder = m.SortOrder,
            GroupKey = m.GroupKey,
            SourceSection      = string.IsNullOrWhiteSpace(m.SourceSection) ? "Header" : m.SourceSection,
            LookupFiltersJson  = m.LookupFiltersJson,
            LookupReturnColumn = m.LookupReturnColumn,
            LookupParam        = m.LookupParam,
            CascadeToIntegrationId = m.CascadeToIntegrationId,
        }).ToList();
        await _repo.ReplaceMappingsAsync(newId, copiedMappings, ct);

        // Triggers — kopyala (ama hepsini pasif baslat ki ikiz aktif olmasin)
        var copiedTriggers = src.Triggers.Select(t => new IntegrationTrigger
        {
            IntegrationId = newId,
            TriggerType = t.TriggerType,
            Config = t.Config,
            IsActive = false,
        }).ToList();
        await _repo.ReplaceTriggersAsync(newId, copiedTriggers, ct);

        return newId;
    }

    public async Task<TestIntegrationResponse> TestAsync(TestIntegrationRequest request, string? currentUserName, CancellationToken ct)
    {
        var warnings = new List<string>();
        ValidateRequest(request.Integration, warnings);

        // Faz O — Sadece-Prosedur modu: endpoint NULL ise test "preview" Pre/Post prosedurleri ozetler.
        // HTTP cagrisi yok; mapping engine atlanir. UI calistirma sirasinda runner zaten ayni mantik kullanir.
        var isProcedureOnly = !request.Integration.TargetEndpointId.HasValue;
        IntegrationEndpoint? endpoint = null;
        if (!isProcedureOnly)
        {
            endpoint = await _repo.GetEndpointByIdAsync(request.Integration.TargetEndpointId!.Value, ct);
            if (endpoint is null)
                return new TestIntegrationResponse(false, null, null, null, "Endpoint bulunamadi.", warnings);
        }

        // Sample record cek
        var sample = await _formMeta.GetSampleRecordAsync(
            request.Integration.SourceFormCode, request.SampleRecordId, ct);
        if (sample is null)
        {
            return new TestIntegrationResponse(false, null, null, null,
                $"Form kaydi bulunamadi: {request.Integration.SourceFormCode}", warnings);
        }

        // Geçici Integration aggregate'i bellekte olustur (DB'ye yazmadan mapping engine'i besle)
        // ÖNEMLİ: Lookup zenginleştirme alanlarini (LookupFiltersJson, LookupReturnColumn) da kopyala —
        // aksi halde test'te Rehber mapping'leri eski single-key yola düşer ve yanlış sonuç verir.
        var transient = new global::CalibraHub.Domain.Entities.Integration
        {
            Id = 0,
            Name = request.Integration.Name,
            SourceFormCode = request.Integration.SourceFormCode,
            TargetEndpointId = request.Integration.TargetEndpointId,
            ErrorBehavior = request.Integration.ErrorBehavior,
            IsActive = true,
            Mappings = request.Integration.Mappings.Select((m, i) => new IntegrationMapping
            {
                Id = i + 1,
                IntegrationId = 0,
                TargetPath = m.TargetPath,
                TargetDataType = m.TargetDataType,
                SourceType = m.SourceType,
                SourceValue = m.SourceValue,
                LookupSourceField = m.LookupSourceField,
                DefaultValue = m.DefaultValue,
                FormatPattern = m.FormatPattern,
                IsRequired = m.IsRequired,
                SortOrder = m.SortOrder,
                GroupKey = m.GroupKey,
                SourceSection      = string.IsNullOrWhiteSpace(m.SourceSection) ? "Header" : m.SourceSection,
                LookupFiltersJson  = m.LookupFiltersJson,
                LookupReturnColumn = m.LookupReturnColumn,
                LookupParam        = m.LookupParam,
                CascadeToIntegrationId = m.CascadeToIntegrationId,
            }).ToList(),
            Endpoint = endpoint,
        };

        // Master-Detail: form'un LinesFormCode'u varsa kalemleri ve kombinasyon kodlarını çek
        // (Runner ile birebir aynı akış — test ve gerçek run sonucu aynı çıksın).
        IReadOnlyList<IReadOnlyDictionary<string, object?>>? linesData = null;
        IReadOnlyDictionary<int, string>? combinationCodes = null;

        var sourceForm = await _formMeta.GetFormAsync(request.Integration.SourceFormCode, ct);

        // Tani amacli net uyarilar: kalem mapping'leri var ama lines konfigurasyonu yetersiz
        // ise kullanici neyin yanlis oldugunu hemen gorsin.
        var hasLineMappings = request.Integration.Mappings.Any(m =>
            string.Equals(m.SourceSection, "Lines", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(m.SourceSection, "Combination", StringComparison.OrdinalIgnoreCase));

        if (sourceForm is null)
        {
            if (hasLineMappings) warnings.Add($"Form metadata bulunamadi: {request.Integration.SourceFormCode}. Kalem mapping'leri uygulanamaz.");
        }
        else if (string.IsNullOrWhiteSpace(sourceForm.LinesFormCode))
        {
            if (hasLineMappings) warnings.Add($"Bu form ({sourceForm.FormCode}) icin LinesFormCode tanimli degil. Kalem mapping'leri uygulanamaz — Admin/Form Metadata'dan baglayin.");
        }
        else if (string.IsNullOrWhiteSpace(sourceForm.LinesParentColumn))
        {
            if (hasLineMappings) warnings.Add($"LinesParentColumn ({sourceForm.LinesFormCode}) tanimli degil. Kalem mapping'leri uygulanamaz.");
        }
        else if (string.IsNullOrWhiteSpace(sample.RecordId))
        {
            if (hasLineMappings) warnings.Add($"Test kaydinin RecordId'si bos. Kalem mapping'leri uygulanamaz.");
        }
        else if (_linesRepo is null)
        {
            if (hasLineMappings) warnings.Add("IFormLinesRepository servisi DI'a kayitli degil. Kalem mapping'leri uygulanamaz.");
        }
        else
        {
            // Lines parent FK genelde int (DocumentLine.DocumentId → Document.Id),
            // ama header sample.RecordId DocumentNumber string olabilir. Sample.FieldValues'da
            // "Id" varsa onu öncelikli kullan, yoksa RecordId fallback (string-PK formlarına).
            var linesParentValue = sample.FieldValues.TryGetValue("Id", out var idVal) && idVal is not null and not DBNull
                ? idVal.ToString()
                : sample.RecordId;

            linesData = await _linesRepo.GetLinesAsync(
                sourceForm.LinesFormCode!,
                sourceForm.LinesParentColumn!,
                linesParentValue!,
                ct);

            if (linesData is { Count: > 0 } && _comboResolver is not null)
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
                    .Where(i => i > 0).Distinct().ToList();
                if (combinationIds.Count > 0)
                    combinationCodes = await _comboResolver.GetCombinationCodesAsync(combinationIds, ct);
            }

            if (linesData is null || linesData.Count == 0)
            {
                warnings.Add($"Test kaydinda ({sourceForm.FormCode} RecordId={sample.RecordId}) hic kalem yok — '{sourceForm.LinesFormCode}' view'inda '{sourceForm.LinesParentColumn}' = {linesParentValue} ile eslesen satir yok. Kalem mapping'leri ciktida gorunmez.");
            }
            else
            {
                // Diagnostik: Lookup mapping'lerinin sourceField'i gercekten lines data'sinda var mi?
                // Yoksa kullanici field adini yanlis sectiyse (case farki, yanlis kolon) null doner.
                var firstLineKeys = new HashSet<string>(linesData[0].Keys, StringComparer.Ordinal);
                var lineLookupRules = request.Integration.Mappings
                    .Where(m => m.SourceType == IntegrationSourceType.Lookup
                             && string.Equals(m.SourceSection, "Lines", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var missingSourceFields = new HashSet<string>(StringComparer.Ordinal);
                foreach (var r in lineLookupRules)
                {
                    if (!string.IsNullOrWhiteSpace(r.LookupSourceField)
                        && !firstLineKeys.Contains(r.LookupSourceField))
                        missingSourceFields.Add(r.LookupSourceField);
                    if (!string.IsNullOrWhiteSpace(r.LookupFiltersJson))
                    {
                        try
                        {
                            using var doc = System.Text.Json.JsonDocument.Parse(r.LookupFiltersJson);
                            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                            {
                                foreach (var el in doc.RootElement.EnumerateArray())
                                {
                                    if (el.TryGetProperty("sourceField", out var sf)
                                        && sf.ValueKind == System.Text.Json.JsonValueKind.String)
                                    {
                                        var sfn = sf.GetString();
                                        if (!string.IsNullOrWhiteSpace(sfn) && !firstLineKeys.Contains(sfn))
                                            missingSourceFields.Add(sfn);
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }
                if (missingSourceFields.Count > 0)
                {
                    var allKeys = linesData[0].Keys.ToList();
                    var availableSample = string.Join(", ", allKeys.Take(15))
                                          + (allKeys.Count > 15 ? ", …" : "");
                    warnings.Add($"Kalem Lookup mapping'inde kullanılan alan(lar) lines data'sinda yok: " +
                                 $"[{string.Join(", ", missingSourceFields)}]. Mevcut kalem alanlari (ilk 15): {availableSample}");
                }
            }
        }

        // Sadece-Prosedur modu: MappingEngine + HTTP atlanir, preview ozeti dondurulur.
        if (isProcedureOnly)
        {
            var preview = "Sadece Prosedür modu — HTTP isteği yok.\n";
            if (!string.IsNullOrWhiteSpace(transient.PreProcedureName))
                preview += $"\nÖncesi  : EXEC {transient.PreProcedureName}";
            if (!string.IsNullOrWhiteSpace(transient.PostProcedureName))
                preview += $"\nSonrası : EXEC {transient.PostProcedureName}";
            if (string.IsNullOrWhiteSpace(transient.PreProcedureName)
                && string.IsNullOrWhiteSpace(transient.PostProcedureName))
                preview += "\n⚠ Hiçbir prosedür tanımlı değil — kaydetmek mantıksız.";
            return new TestIntegrationResponse(true, preview, null, null, null, warnings);
        }

        // MappingEngine — Master-Detail overload (header + lines + combination)
        var body = await _mappingEngine.BuildAsync(
            transient, sample.FieldValues, linesData, combinationCodes, ct);
        var bodyJson = body.ToJsonString();

        // sendForReal=false ise sadece preview
        if (!request.SendForReal)
        {
            return new TestIntegrationResponse(true, bodyJson, null, null, null, warnings);
        }

        // ApiProfile cek
        var profile = await _apiProfileRepo.GetByIdAsync(endpoint!.ApiProfileId, ct);
        if (profile is null)
        {
            return new TestIntegrationResponse(false, bodyJson, null, null,
                "ApiProfile bulunamadi (test sadece preview yapildi).", warnings);
        }

        // 2026-05-25: Kullanici Step 4'te body'yi duzenlediyse onu kullan; aksi halde
        // mapping ciktisini gonder.
        var bodyToSend = body;
        var bodyJsonForResponse = bodyJson;
        if (!string.IsNullOrWhiteSpace(request.OverrideRequestBody))
        {
            try
            {
                var parsed = System.Text.Json.Nodes.JsonNode.Parse(request.OverrideRequestBody);
                if (parsed is System.Text.Json.Nodes.JsonObject obj)
                {
                    bodyToSend = obj;
                    bodyJsonForResponse = obj.ToJsonString();
                }
                else
                {
                    return new TestIntegrationResponse(false, bodyJson, null, null,
                        "Düzenlenmiş body geçerli bir JSON object değil (root '{ }' olmalı).", warnings);
                }
            }
            catch (Exception ex)
            {
                return new TestIntegrationResponse(false, bodyJson, null, null,
                    "Düzenlenmiş body parse edilemedi: " + ex.Message, warnings);
            }
        }

        // Gercek HTTP cagrisi
        var http = await _httpExecutor.SendAsync(endpoint, profile, bodyToSend, ct);
        return new TestIntegrationResponse(
            Success: http.Success,
            RequestBody: bodyJsonForResponse,
            HttpStatusCode: http.StatusCode,
            ResponseBody: http.ResponseBody,
            ErrorMessage: http.ErrorMessage,
            ValidationWarnings: warnings);
    }

    // ── Validation ──────────────────────────────────────────────────────

    private static void ValidateRequest(SaveIntegrationRequest req, List<string>? warnings = null)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            throw new ArgumentException("Entegrasyon adi zorunlu.");
        if (string.IsNullOrWhiteSpace(req.SourceFormCode))
            throw new ArgumentException("Kaynak form secimi zorunlu.");

        // Faz O — Endpoint VEYA en az bir prosedur olmali (ikisinden biri).
        var hasEndpoint  = req.TargetEndpointId.HasValue && req.TargetEndpointId.Value > 0;
        var hasPreProc   = !string.IsNullOrWhiteSpace(req.PreProcedureName);
        var hasPostProc  = !string.IsNullOrWhiteSpace(req.PostProcedureName);
        if (!hasEndpoint && !hasPreProc && !hasPostProc)
            throw new ArgumentException("Hedef endpoint VEYA en az bir prosedür (Öncesi/Sonrası) seçimi zorunlu.");

        var isProcedureOnly = !hasEndpoint;
        if (isProcedureOnly)
        {
            // Sadece-Prosedur modunda mapping kurali aranmaz — HTTP body uretilmiyor.
            return;
        }

        if (req.Mappings is null || req.Mappings.Count == 0)
        {
            if (warnings is not null) warnings.Add("En az 1 mapping kurali olmali (preview bos olabilir).");
            else throw new ArgumentException("En az 1 mapping kurali zorunlu.");
        }
        if (req.Mappings is not null)
        {
            foreach (var m in req.Mappings)
            {
                if (string.IsNullOrWhiteSpace(m.TargetPath))
                    throw new ArgumentException("Mapping satirinda hedef path bos.");
                if (m.SourceType == IntegrationSourceType.FormField && string.IsNullOrWhiteSpace(m.SourceValue))
                    throw new ArgumentException($"Mapping '{m.TargetPath}': FormField icin kaynak alan adi bos.");
                if (m.SourceType == IntegrationSourceType.Lookup && string.IsNullOrWhiteSpace(m.LookupSourceField))
                    throw new ArgumentException($"Mapping '{m.TargetPath}': Lookup icin kaynak alan adi bos.");
            }
        }
    }
}
