using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Approval;
using CalibraHub.Application.Approval.EntityTypes;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Enums;
using CalibraHub.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;

namespace CalibraHub.Web.Controllers;

[Authorize]
public sealed class ApprovalFlowController : Controller
{
    private readonly IApprovalFlowService _service;
    private readonly IUserProfileRepository _userRepo;
    private readonly IDepartmentRepository _deptRepo;
    private readonly ICariGroupService _cariGroupService;
    private readonly ILogisticsConfigurationService _logisticsService;
    private readonly ICardGroupRepository _cardGroupRepo;
    private readonly IApprovalSqlQueryService _sqlQueryService;
    private readonly IApprovalEntityTypeRegistry _entityRegistry;
    private readonly IIntegrationService _integrationService;
    private readonly IDocumentRepository _documentRepo;
    private readonly IDocumentTypeRepository _documentTypeRepo;

    public ApprovalFlowController(
        IApprovalFlowService service,
        IUserProfileRepository userRepo,
        IDepartmentRepository deptRepo,
        ICariGroupService cariGroupService,
        ILogisticsConfigurationService logisticsService,
        ICardGroupRepository cardGroupRepo,
        IApprovalSqlQueryService sqlQueryService,
        IApprovalEntityTypeRegistry entityRegistry,
        IIntegrationService integrationService,
        IDocumentRepository documentRepo,
        IDocumentTypeRepository documentTypeRepo)
    {
        _service = service;
        _userRepo = userRepo;
        _deptRepo = deptRepo;
        _cariGroupService = cariGroupService;
        _logisticsService = logisticsService;
        _cardGroupRepo = cardGroupRepo;
        _sqlQueryService = sqlQueryService;
        _entityRegistry = entityRegistry;
        _integrationService = integrationService;
        _documentRepo = documentRepo;
        _documentTypeRepo = documentTypeRepo;
    }

    // ── Document.Id (int) → deterministic Guid ────────────────────────────────
    // DocumentApprovalInstance.DocumentId UNIQUEIDENTIFIER kolonu kullaniyor (eski
    // IncomingDocument tabanli tasarim). Outbound Document INT PK; deterministic bir
    // packing ile ayni int her zaman ayni Guid'e map'lenir (CRUD'lar arasinda tutarli).
    private static Guid DocumentIntToGuid(int documentId)
    {
        // İlk 4 byte = int (little-endian), kalan 12 byte sabit imza ("CalibraDoc!!" benzeri).
        var bytes = new byte[16];
        BitConverter.GetBytes(documentId).CopyTo(bytes, 0);
        // Sabit "DOCx" magic suffix — collision'i imkansiz hale getirir (yalnizca DocumentIntToGuid
        // ile uretilen Guid'ler bu pattern'a sahip; herhangi bir random Guid ile cakismaz).
        var magic = new byte[] { 0x44, 0x4F, 0x43, 0x49, 0x4E, 0x54, 0x32, 0x47, 0x55, 0x49, 0x44, 0x21 };
        magic.CopyTo(bytes, 4);
        return new Guid(bytes);
    }

    // ── Liste ─────────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var flows = await _service.GetAllAsync(ct);
        var boardConfig = BuildBoardConfig(flows);
        ViewData["Title"] = "Onay Akış Tanımları";
        ViewData["BoardConfigJson"] = JsonSerializer.Serialize(boardConfig,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return View();
    }

    [HttpGet("/ApprovalFlow/BoardConfig")]
    public async Task<IActionResult> BoardConfig(CancellationToken ct)
    {
        var flows = await _service.GetAllAsync(ct);
        return Json(BuildBoardConfig(flows),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    // ── Edit (GET: formu doldur) ──────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        // 2026-05-25: Iframe cache busting — designer guncellemeleri her seferinde fresh gelmeli.
        Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        Response.Headers["Pragma"] = "no-cache";
        ApprovalFlowDto? flow = null;
        if (id != 0)
        {
            flow = await _service.GetByIdAsync(id, ct);
            if (flow is null) return NotFound();
        }
        ViewData["Title"] = id == 0 ? "Yeni Onay Akışı" : $"Akış Düzenle — {flow!.Name}";

        // "Belirli Kullanıcı" tipi onaylayıcı icin dropdown listesi —
        // 2026-05-25: Yalniz icinde bulunulan SIRKETE ait aktif kullanicilar.
        var (companyId, _) = GetCurrentUser();
        var users = (await _userRepo.GetAllAsync(ct))
            .Where(u => u.IsActive && (companyId == 0 || u.CompanyId == companyId))
            .OrderBy(u => u.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(u => new { id = u.Id.ToString(), name = u.FullName, email = u.Email })
            .ToArray();
        ViewData["Users"] = users;

        // "Departman" tipi kosul icin multi-select listesi —
        // 2026-05-25: Yalniz icinde bulunulan SIRKETE ait aktif departmanlar.
        var departments = (await _deptRepo.GetAllAsync(ct))
            .Where(d => d.IsActive && (companyId == 0 || d.CompanyId == companyId))
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Select(d => new { id = d.Id, name = d.Name })
            .ToArray();
        ViewData["Departments"] = departments;

        // 2026-05-25: Karar koşulları için Cari + Stok grupları (designer dropdownları)
        // CARI: Mevcut card_groups (cardType=2) sistemini kullaniyoruz — Cari Kartı "Gruplar"
        // tab'ında zaten bu yapıyla seviye 1-5 grup atanıyor. Yeni eklenen CariGroup/ContactGroupMapping
        // tablolari SU AN KULLANILMIYOR (gelecek refactor ya da silinebilir).
        var allCariCardGroups = new List<CalibraHub.Domain.Entities.CardGroup>();
        for (int lv = 1; lv <= 5; lv++)
        {
            var groups = await _cardGroupRepo.GetByLevelAsync(2, lv, null, ct);
            allCariCardGroups.AddRange(groups);
        }
        var cariGroupsByCat = new Dictionary<string, object>();
        for (int lv = 1; lv <= 5; lv++)
        {
            cariGroupsByCat[lv.ToString()] = allCariCardGroups
                .Where(g => g.Level == lv)
                .OrderBy(g => g.Code, StringComparer.OrdinalIgnoreCase)
                .Select(g => new {
                    id = g.Id,    // CardGroupMapping.cardGroupId ile match — entity_type=2, entity_id=contactId üzerinde
                    name = string.IsNullOrWhiteSpace(g.Description) ? g.Code : $"{g.Code} — {g.Description}",
                })
                .ToArray();
        }
        ViewData["CariGroups"] = cariGroupsByCat;

        var allMatGroups = await _logisticsService.GetMaterialGroupsAsync(null, ct);
        // 5 kategorili dictionary: { "1": [{code, name}], "2": [...], ... "5": [...] }
        var matGroupsByCat = new Dictionary<string, object>();
        for (int cat = 1; cat <= 5; cat++)
        {
            matGroupsByCat[cat.ToString()] = allMatGroups
                .Where(g => g.GroupCategory == cat)
                .OrderBy(g => g.GroupCode, StringComparer.OrdinalIgnoreCase)
                .Select(g => new {
                    id = g.GroupCode,   // FK kullanılan değer (her item için MaterialGroupMapping.GroupCode)
                    name = string.IsNullOrWhiteSpace(g.GroupDescription) ? g.GroupCode : $"{g.GroupCode} — {g.GroupDescription}",
                })
                .ToArray();
        }
        ViewData["MaterialGroups"] = matGroupsByCat;

        // 2026-05-25: Entegrasyon node listesi — designer'da "Entegrasyon" düğümü
        // için dropdown beslemesi. Sadece aktif integration'lar listelenir.
        // Tetiklendiğinde IIntegrationRunner.RunAsync(integrationId, entityId, Cascade, ...).
        var integrations = await _integrationService.ListAsync(includeInactive: false, ct);
        ViewData["Integrations"] = integrations.Select(i => new
        {
            id              = i.Id,
            name            = i.Name,
            sourceFormCode  = i.SourceFormCode,
            sourceFormLabel = i.SourceFormLabel,
            endpointName    = i.EndpointName,
            hasEndpoint     = i.TargetEndpointId.HasValue,
            isActive        = i.IsActive,
        }).OrderBy(x => x.name, StringComparer.OrdinalIgnoreCase).ToArray();

        // 2026-05-25: Karar (Decision) node SQL koşulları için kütüphane —
        // designer dropdown'ı `SqlQueries` prop'undan beslenir.
        var sqlQueries = await _sqlQueryService.GetAllAsync(ct);
        ViewData["SqlQueries"] = sqlQueries.Where(q => q.IsActive).Select(q => new
        {
            id          = q.Id,
            name        = q.Name,
            description = q.Description,
            sqlText     = q.SqlText,
            parameters  = q.ParametersJson,
            resultType  = q.ResultType,
        }).ToArray();

        // 2026-05-25 (entity-agnostic): Plugin registry'den entity tipleri katalogu.
        // Designer'da fKind dropdown'ı + decision field listesi bu listeden beslenir.
        // 2026-05-25 (UX): "Document" wildcard'ı dropdown'dan gizli — kullanıcı belge bazlı
        // spesifik akış tasarlar, "tüm belgeler" gerekirse listeden kopyalayarak çoğaltır.
        // Registry'de kalmaya devam eder (eski flow'lar için match wildcard'ı backward-compat).
        ViewData["EntityTypes"] = _entityRegistry.All
            .Where(et => !string.Equals(et.Code, "Document", StringComparison.OrdinalIgnoreCase))
            .Select(et => new
        {
            code          = et.Code,
            label         = et.Label,
            icon          = et.Icon,
            groupCategory = et.GroupCategory,
            fields = et.GetFields().Select(f => new
            {
                code         = f.Code,
                label        = f.Label,
                type         = f.Type,
                scope        = f.Scope,
                groupLabel   = f.GroupLabel,
                lookupSource = f.LookupSource,
            }).ToArray(),
            parameters = et.GetSqlParameters().Select(p => new
            {
                name        = p.Name,
                type        = p.Type,
                description = p.Description,
            }).ToArray(),
        }).ToArray();

        // React Flow designer icin baslangic nodes/edges. Flow yoksa bos donulur,
        // designer kendi default Start+End'ini atar.
        ViewData["InitialNodesEdges"] = BuildDesignerInitialPayload(flow);

        // Surec-scoped degisken tanimlari (flow.Variables) — designer Variables paneli icin
        ViewData["InitialVariables"] = (flow?.Variables ?? new List<ApprovalFlowVariableDto>())
            .OrderBy(v => v.SortOrder)
            .Select(v => new
            {
                id           = v.Id,
                name         = v.Name,
                typeCode     = v.TypeCode,
                defaultValue = v.DefaultValue,
                description  = v.Description,
                sortOrder    = v.SortOrder,
            }).ToArray();

        return View(flow);
    }

    // Flow.Steps + Flow.Edges → designer'in beklediği nodes/edges JSON şekli
    // (React Flow native format: { id, type, position:{x,y}, data:{...} } + edges)
    private static object BuildDesignerInitialPayload(ApprovalFlowDto? flow)
    {
        if (flow is null)
            return new { nodes = Array.Empty<object>(), edges = Array.Empty<object>() };

        var nodes = flow.Steps
            .Where(s => s.IsActive)
            .Select(s =>
            {
                // NodeData JSON parse — varsa ek alanlari data'ya merge et.
                Dictionary<string, object?> data = new()
                {
                    ["stepName"]      = s.StepName,
                    ["approverType"]  = s.ApproverType.ToString(),
                    ["approverId"]    = s.ApproverId,
                    ["approverLabel"] = s.ApproverLabel,
                };
                if (!string.IsNullOrWhiteSpace(s.NodeData))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(s.NodeData);
                        if (doc.RootElement.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var prop in doc.RootElement.EnumerateObject())
                            {
                                // 2026-05-25: Array + Object da destekliyoruz (örn. conditionRules array).
                                // Eski versiyon sadece scalar'a izin veriyordu, karar koşulları reload'da kayboluyordu.
                                data[prop.Name] = ConvertJsonElement(prop.Value);
                            }
                        }
                    }
                    catch { /* bozuk JSON'da ek alan eklenmez */ }
                }

                // dbId designer payload'inda kullanilmiyor (clientId 'step_{Id}' format), ancak
                // edge clientId'leri ile eslesme icin id format'i 'step_{Id}' olmali.
                return (object)new
                {
                    id       = $"step_{s.Id}",
                    type     = string.IsNullOrWhiteSpace(s.NodeType) ? "step" : s.NodeType,
                    position = new { x = s.PosX, y = s.PosY },
                    data     = data,
                };
            })
            .ToArray();

        // 2026-05-25: Edge'in sourceHandle bilgisini source node tipi + EdgeKind'tan compute et.
        // step + true → 'approve' ; step + false → 'reject'
        // decision + true → 'true' ; decision + false → 'false'
        // diğer durumlarda null (tek source handle olan node'lar için React Flow toleranslı).
        var stepNodeTypeById = flow.Steps
            .Where(s => s.IsActive)
            .ToDictionary(s => s.Id, s => string.IsNullOrWhiteSpace(s.NodeType) ? "step" : s.NodeType);

        var edges = flow.Edges
            .OrderBy(e => e.SortOrder)
            .Select(e =>
            {
                var kind = string.IsNullOrWhiteSpace(e.EdgeKind) ? "default" : e.EdgeKind;
                stepNodeTypeById.TryGetValue(e.SourceStepId, out var srcType);
                string? sourceHandle = null;
                if (srcType == "step")
                {
                    sourceHandle = kind == "true"  ? "approve"
                                 : kind == "false" ? "reject"
                                 : null;
                }
                else if (srcType == "decision")
                {
                    sourceHandle = kind == "true" ? "true"
                                 : kind == "false" ? "false"
                                 : null;
                }
                return (object)new
                {
                    id           = $"e_{e.Id}",
                    source       = $"step_{e.SourceStepId}",
                    sourceHandle = sourceHandle,
                    target       = $"step_{e.TargetStepId}",
                    label        = e.Label,
                    data         = new
                    {
                        edgeKind  = kind,
                        condition = e.Condition,
                    },
                };
            })
            .ToArray();

        return new { nodes, edges };
    }

    // 2026-05-25: JsonElement → CLR object (array / object / scalar recursive).
    // System.Text.Json serializer object/dictionary'i camelCase JSON'a roundtrip eder.
    private static object? ConvertJsonElement(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.String: return el.GetString();
            case JsonValueKind.Number: return el.TryGetInt64(out var l) ? (object)l : el.GetDouble();
            case JsonValueKind.True:   return true;
            case JsonValueKind.False:  return false;
            case JsonValueKind.Null:   return null;
            case JsonValueKind.Array:
                var list = new List<object?>();
                foreach (var item in el.EnumerateArray()) list.Add(ConvertJsonElement(item));
                return list;
            case JsonValueKind.Object:
                var dict = new Dictionary<string, object?>();
                foreach (var p in el.EnumerateObject()) dict[p.Name] = ConvertJsonElement(p.Value);
                return dict;
            default: return null;
        }
    }

    // ── Save (POST) ───────────────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromBody] SaveApprovalFlowRequest request, CancellationToken ct)
    {
        try
        {
            int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId);
            var id = await _service.SaveAsync(request, userId == 0 ? (int?)null : userId, ct);
            return Json(new { ok = true, id });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }

    // ── Duplicate (POST) ──────────────────────────────────────────────────────
    // Mevcut akışı kopyalar — name'e " (Kopya)" eklenir, IsActive=false.
    // Yeni id JSON cevabında döner; SmartCard refresh tetiklenince liste güncellenir.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Duplicate(int id, CancellationToken ct)
    {
        try
        {
            int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId);
            var newId = await _service.DuplicateAsync(id, userId == 0 ? (int?)null : userId, ct);
            return Json(new { success = true, ok = true, id = newId, message = "Akış kopyalandı." });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, ok = false, message = ex.Message });
        }
    }

    // ── Delete ────────────────────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        try
        {
            await _service.DeleteAsync(id, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }

    // ── Belge için uygun akışı döner (AJAX) ──────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> Match(string kind, decimal? amount, string? taxNo, int? departmentId, CancellationToken ct)
    {
        var flow = await _service.MatchFlowAsync(kind, amount, taxNo, departmentId, ct);
        if (flow is null) return Json(new { matched = false });
        return Json(new
        {
            matched = true,
            flowId = flow.Id,
            flowName = flow.Name,
            stepCount = flow.Steps.Count(s => s.IsActive),
        });
    }

    // ── Belge ID üzerinden tek-tıkla başlat (frontend yardımcı) ───────────────
    // Frontend'in kind / amount / department vs. resolve etmesine gerek kalmasın diye
    // belge ID alir, kendisi document tipinden kind cikartir, eslestiren ilk akisi baslatir.
    // - Belge yoksa veya akis eslemiyorsa { ok=false, error=... } doner.
    // - Birden fazla akis eslesirse: MatchFlowAsync zaten priority'ye gore en uygunu doner
    //   (MVP — flow.Priority + flow.Rules degerlendirilir).
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartByDocument(int documentId, CancellationToken ct)
    {
        try
        {
            var document = await _documentRepo.GetByIdAsync(documentId, ct);
            if (document is null)
                return Json(new { ok = false, error = "Belge bulunamadı." });

            // Document.DocumentTypeId → DocumentType.Code → DocumentEntityTypes.Definitions.Filter
            // ile kind string'i bulunur. Esleme yoksa "Document" wildcard'a dusulur.
            var kind = "Document";
            if (document.DocumentTypeId.HasValue)
            {
                var docType = await _documentTypeRepo.GetByIdAsync(document.DocumentTypeId.Value, ct);
                if (docType is not null && !string.IsNullOrWhiteSpace(docType.Code))
                {
                    var match = DocumentEntityTypes.Definitions
                        .FirstOrDefault(d => string.Equals(d.Filter, docType.Code, StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrEmpty(match.Code))
                        kind = match.Code;
                }
            }

            decimal? totalAmount = document.GrandTotal > 0 ? document.GrandTotal : (decimal?)null;
            // Document'ta departman/VKN alanı yok — null gecilir; MVP icin esleme amount + kind uzerinden.
            string? senderTaxNo = null;
            int? departmentId = null;

            var flow = await _service.MatchFlowAsync(kind, totalAmount, senderTaxNo, departmentId, ct);
            if (flow is null)
                return Json(new { ok = false, error = "Bu belge için tanımlı aktif onay akışı yok." });

            var userName = User.FindFirstValue(ClaimTypes.Name) ?? "system";
            var docGuid = DocumentIntToGuid(documentId);
            var startReq = new StartApprovalRequest(docGuid, flow.Id, userName);
            var instance = await _service.StartAsync(startReq, ct);

            return Json(new
            {
                ok = true,
                instanceId = instance.Id,
                status = instance.Status,
                flowName = flow.Name,
            });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }

    // ── Belge GUID üzerinden instance al (frontend yardımcı) ──────────────────
    // _WorkflowPanel.cshtml documentId'yi int olarak gonderir; bu wrapper int → Guid
    // conversion yapar, sonra mevcut GetInstance mantigini calistirir.
    [HttpGet]
    public async Task<IActionResult> GetInstanceByDocument(int documentId, CancellationToken ct)
    {
        var docGuid = DocumentIntToGuid(documentId);
        var instance = await _service.GetInstanceByDocumentIdAsync(docGuid, ct);
        if (instance is null) return Json(new { found = false });
        return Json(new
        {
            found = true,
            instanceId = instance.Id,
            status = instance.Status,
            flowName = instance.FlowName,
            currentStep = instance.CurrentStep,
            totalSteps = instance.TotalSteps,
            startedBy = instance.StartedBy,
            startedAt = instance.StartedAt.ToString("dd.MM.yyyy HH:mm"),
            rejectNote = instance.RejectNote,
        });
    }

    // ── Onay başlat ───────────────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartApproval([FromBody] StartApprovalRequest request, CancellationToken ct)
    {
        try
        {
            var userName = User.FindFirstValue(ClaimTypes.Name) ?? "system";
            var reqWithUser = request with { StartedBy = userName };
            var instance = await _service.StartAsync(reqWithUser, ct);
            return Json(new { ok = true, instanceId = instance.Id, status = instance.Status });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }

    // ── Adımı onayla ──────────────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveStep([FromBody] ApproveStepRequest request, CancellationToken ct)
    {
        try
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            var userName = User.FindFirstValue(ClaimTypes.Name) ?? "system";
            var reqWithUser = request with { ApproverId = userId, ApproverName = userName };
            var instance = await _service.ApproveStepAsync(reqWithUser, ct);
            return Json(new
            {
                ok = true,
                instanceId = instance.Id,
                status = instance.Status,
                currentStep = instance.CurrentStep,
                totalSteps = instance.TotalSteps,
            });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }

    // ── Reddet ────────────────────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectStep([FromBody] RejectStepRequest request, CancellationToken ct)
    {
        try
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            var userName = User.FindFirstValue(ClaimTypes.Name) ?? "system";
            var reqWithUser = request with { ApproverId = userId, ApproverName = userName };
            var instance = await _service.RejectAsync(reqWithUser, ct);
            return Json(new { ok = true, instanceId = instance.Id, status = instance.Status });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }

    // ── İptal et ──────────────────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelApproval(int instanceId, CancellationToken ct)
    {
        try
        {
            var userName = User.FindFirstValue(ClaimTypes.Name) ?? "system";
            await _service.CancelAsync(instanceId, userName, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }

    // ── Belgenin aktif onay örneğini getir ────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetInstance(Guid documentId, CancellationToken ct)
    {
        var instance = await _service.GetInstanceByDocumentIdAsync(documentId, ct);
        if (instance is null) return Json(new { found = false });
        return Json(new
        {
            found = true,
            instanceId = instance.Id,
            status = instance.Status,
            flowName = instance.FlowName,
            currentStep = instance.CurrentStep,
            totalSteps = instance.TotalSteps,
            startedBy = instance.StartedBy,
            startedAt = instance.StartedAt.ToString("dd.MM.yyyy HH:mm"),
            rejectNote = instance.RejectNote,
            steps = instance.StepRecords.Select(s => new
            {
                stepOrder = s.StepOrder,
                stepName = s.StepName,
                status = s.Status,
                approverName = s.ApproverName,
                note = s.Note,
                actionDate = s.ActionDate?.ToString("dd.MM.yyyy HH:mm"),
            }),
        });
    }

    // ── Board config ─────────────────────────────────────────────────────────
    private static object BuildBoardConfig(IReadOnlyList<ApprovalFlowSummaryDto> flows)
    {
        var entities = flows.Select(f => (object)new
        {
            id = f.Id,
            title = f.Name,
            subtitle = KindLabel(f.DocumentKind),
            description = f.Description,
            imageUrl = (string?)null,
            statusBadge = f.IsActive
                ? new { label = "Aktif", color = "emerald" }
                : (object?)new { label = "Pasif", color = "slate" },
            widgets = new object[]
            {
                new { id="w_steps",    type="data", dataType="numeric", label="Adım Sayısı",   value=f.StepCount.ToString(), color="indigo" },
                new { id="w_rules",    type="data", dataType="numeric", label="Koşul Sayısı",  value=f.RuleCount.ToString(), color="blue"   },
                new { id="w_priority", type="data", dataType="numeric", label="Öncelik",       value=f.Priority.ToString(),  color="amber"  },
                new { id="w_kind",     type="data", dataType="options", label="Belge Türü",    value=KindLabel(f.DocumentKind), color="slate" },
            },
            primaryAction = new
            {
                label = "Düzenle",
                icon = "Edit",
                color = "amber",
                url = $"/ApprovalFlow/Edit?id={f.Id}",
                hideButton = true,
            },
            secondaryAction = new
            {
                label = "Sil",
                icon = "Trash2",
                apiUrl = $"/ApprovalFlow/Delete?id={f.Id}",
                apiMethod = "POST",
                confirm = $"'{f.Name}' akışını silmek istediğinize emin misiniz?",
            },
            extraActions = new object[]
            {
                // Kopyala — benzer akış tasarımı için kullanışlı bir başlangıç.
                // type=api-post → POST + onRefresh; kopya pasif (IsActive=false) oluşur.
                new
                {
                    label   = "Kopyala",
                    icon    = "Copy",
                    color   = "indigo",
                    type    = "api-post",
                    url     = $"/ApprovalFlow/Duplicate?id={f.Id}",
                    confirm = $"'{f.Name}' akışını kopyalamak istiyor musunuz? Kopya pasif olarak oluşturulur.",
                },
            },
        }).ToArray();

        return new
        {
            boardKey = "approval-flows",
            title = "Onay Akış Tanımları",
            subtitle = $"{flows.Count} akış",
            icon = "GitBranch",
            iconColor = "indigo",
            refreshUrl = "/ApprovalFlow/BoardConfig",
            searchPlaceholder = "Akış adı, belge türü...",
            emptyText = "Henüz onay akışı tanımlanmamış",
            actions = new object[]
            {
                new { id = "new", label = "Yeni Akış", icon = "Plus", variant = "primary", url = "/ApprovalFlow/Edit?id=0" },
            },
            masterWidgets = new List<object>
            {
                SmartBoardFilterHelpers.MakeStdWidget   ("w_steps",    "Adım Sayısı",  "numeric"),
                SmartBoardFilterHelpers.MakeStdWidget   ("w_rules",    "Koşul Sayısı", "numeric"),
                SmartBoardFilterHelpers.MakeStdWidget   ("w_priority", "Öncelik",      "numeric"),
                SmartBoardFilterHelpers.MakeOptionsWidget("w_kind",     "Belge Türü",
                    SmartBoardFilterHelpers.ToOptionsList(new[] {
                        "e-Fatura", "e-Arşiv", "e-İrsaliye",
                        "İhtiyaç Kaydı", "Satın Alma Teklifi", "Satın Alma Siparişi",
                        "Satış Teklifi", "Satış Siparişi",
                        "Tümü",
                    })),
            },
            entities,
        };
    }

    private static string KindLabel(string kind) => kind switch
    {
        // Entity-agnostic plugin entity tipleri (yeni standart)
        "Document"         => "Tüm Belgeler",
        "WorkOrder"        => "İş Emri",
        "Item"             => "Stok Kartı",
        "Contact"          => "Cari Hesap",
        "ProductionRecord" => "Üretim Sonu Kaydı",
        // Spesifik belge türleri (DocumentEntityTypes.Definitions ile senkron)
        "EInvoice"        => "e-Fatura",
        "EArchive"        => "e-Arşiv",
        "EDispatch"       => "e-İrsaliye",
        "PurchaseRequest" => "İhtiyaç Kaydı",
        "PurchaseQuote"   => "Satın Alma Teklifi",
        "PurchaseOrder"   => "Satın Alma Siparişi",
        "SalesQuote"      => "Satış Teklifi",
        "SalesOrder"      => "Satış Siparişi",
        "All"             => "Tümü",
        _                 => kind,
    };

    // 2026-05-25: Aktif kullanicinin sirket+id'sini claims'ten alir
    // (OrgChartController patterniyle ozdes). Multi-tenant filtreleme icin kullanilir.
    private (int CompanyId, int UserId) GetCurrentUser()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var companyIdStr = User.FindFirstValue("company_id") ?? string.Empty;
        int.TryParse(userIdStr, out var userId);
        int.TryParse(companyIdStr, out var companyId);
        return (companyId, userId);
    }
}
