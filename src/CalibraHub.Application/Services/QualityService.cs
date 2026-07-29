using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Auditing;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Services;

/// <summary>
/// Kalite muayene servisi. Plan CRUD + muayene kaydı (Document numara + companion + native
/// satırlar). Ölçüm satırlarında tolerans karşı otomatik uygun/uygunsuz; genel Verdict türetilir.
/// Disposition YALNIZ karar kaydıdır — hiçbir stok hareketi/blok tetiklemez (blokaj stok işi).
/// ArgeProjectService'in Document shell desenini izler; DocumentService.SaveQuoteAsync KULLANILMAZ.
/// </summary>
public sealed class QualityService : IQualityService
{
    private const string TypeCode = "muayene";
    private const string AuditEntity = "QualityInspection";
    private const string AuditPlanEntity = "QualityInspectionPlan";

    private readonly IQualityRepository _repo;
    private readonly IDocumentRepository _documents;
    private readonly IDocumentTypeRepository _documentTypes;
    private readonly IDocumentNumberService _numberService;
    private readonly ILogger<QualityService> _logger;
    private readonly IAuditTrailService? _audit;

    public QualityService(
        IQualityRepository repo,
        IDocumentRepository documents,
        IDocumentTypeRepository documentTypes,
        IDocumentNumberService numberService,
        ILogger<QualityService> logger,
        IAuditTrailService? audit = null)
    {
        _repo = repo;
        _documents = documents;
        _documentTypes = documentTypes;
        _numberService = numberService;
        _logger = logger;
        _audit = audit;
    }

    // ── Plan ──────────────────────────────────────────────────────

    public Task<IReadOnlyCollection<QualityInspectionPlanListItem>> ListPlansAsync(string? search, byte? inspectionType, CancellationToken ct)
        => _repo.ListPlansAsync(search, inspectionType, ct);

    public Task<QualityInspectionPlanDetail?> GetPlanAsync(int planId, CancellationToken ct) => _repo.GetPlanAsync(planId, ct);

    public Task<QualityPlanLookup?> FindPlanForItemAsync(int itemId, byte inspectionType, CancellationToken ct)
        => _repo.FindPlanForItemAsync(itemId, inspectionType, ct);

    public Task<IReadOnlyCollection<MaterialGroupLookupItem>> ListMaterialGroupsAsync(CancellationToken ct)
        => _repo.ListMaterialGroupsAsync(ct);

    public async Task<(bool Ok, string? Error, int Id)> SavePlanAsync(SaveQualityInspectionPlanRequest request, int? userId, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return (false, "Plan adı zorunludur.", 0);
        if (!Enum.IsDefined(typeof(InspectionType), request.InspectionType)) return (false, "Geçersiz muayene tipi.", 0);
        // ID tabanlı eşleştirme: plan ya belirli bir malzemeye ya da malzeme grubuna bağlanır, ikisi birden olamaz.
        if (request.ItemId is > 0 && request.MaterialGroupId is > 0)
            throw new ArgumentException("Bir plan ya malzeme ya da malzeme grubu için tanımlanabilir, ikisi birden olamaz.");
        var lines = (request.Lines ?? Array.Empty<QualityInspectionPlanLineDto>())
            .Where(l => !string.IsNullOrWhiteSpace(l.CharacteristicName)).ToList();
        if (lines.Count == 0) return (false, "Planda en az bir karakteristik satırı olmalıdır.", 0);

        var plan = new QualityInspectionPlan
        {
            Id = request.Id,
            ItemId = request.ItemId,
            MaterialGroupId = request.MaterialGroupId,
            InspectionType = (InspectionType)request.InspectionType,
            Name = name,
            IsActive = request.IsActive,
            CreatedById = userId,
            UpdatedById = userId,
        };
        var lineEntities = lines.OrderBy(l => l.OrderNo).Select((l, i) => new QualityInspectionPlanLine
        {
            PlanId = request.Id,
            CharacteristicName = l.CharacteristicName.Trim(),
            Nominal = l.Nominal,
            LowerTol = l.LowerTol,
            UpperTol = l.UpperTol,
            UnitId = l.UnitId,
            Method = string.IsNullOrWhiteSpace(l.Method) ? null : l.Method.Trim(),
            GaugeName = string.IsNullOrWhiteSpace(l.GaugeName) ? null : l.GaugeName.Trim(),
            IsNumeric = l.IsNumeric,
            OrderNo = i + 1,
            CreatedById = userId,
        }).ToList();

        var id = await _repo.UpsertPlanAsync(plan, lineEntities, ct);
        if (request.Id > 0) _audit?.LogChanges(AuditPlanEntity, id, name,
            new[] { new AuditFieldChange("Lines", "Karakteristikler", null, $"{lineEntities.Count} satır") }, detail: "Plan güncellendi");
        else _audit?.LogInsert(AuditPlanEntity, id, name, detail: $"{lineEntities.Count} karakteristik");
        return (true, null, id);
    }

    public async Task<(bool Ok, string? Error)> DeletePlanAsync(int planId, int? userId, CancellationToken ct)
    {
        var plan = await _repo.GetPlanAsync(planId, ct);
        if (plan is null) return (false, "Plan bulunamadı.");
        await _repo.SoftDeletePlanAsync(planId, userId, ct);
        _audit?.LogDelete(AuditPlanEntity, planId, plan.Name);
        return (true, null);
    }

    // ── Muayene ───────────────────────────────────────────────────

    public Task<IReadOnlyCollection<QualityInspectionListItem>> ListInspectionsAsync(string? search, byte? status, byte? verdict, CancellationToken ct)
        => _repo.ListInspectionsAsync(search, status, verdict, ct);

    public Task<QualityInspectionDetail?> GetInspectionAsync(int documentId, CancellationToken ct) => _repo.GetInspectionDetailAsync(documentId, ct);

    public Task<IReadOnlyCollection<QualityDefectCodeOption>> GetDefectCodeOptionsAsync(CancellationToken ct) => _repo.GetDefectCodeOptionsAsync(ct);

    public async Task<(bool Ok, string? Error, int DocumentId)> SaveInspectionAsync(SaveQualityInspectionRequest request, int? userId, CancellationToken ct)
    {
        if (!Enum.IsDefined(typeof(InspectionType), request.InspectionType)) return (false, "Geçersiz muayene tipi.", 0);
        var reqLines = (request.Lines ?? Array.Empty<SaveQualityInspectionLine>())
            .Where(l => !string.IsNullOrWhiteSpace(l.CharacteristicName)).ToList();
        if (reqLines.Count == 0) return (false, "Muayenede en az bir ölçüm satırı olmalıdır.", 0);

        // Satır sonuçlarını değerlendir (tolerans karşı otomatik) + uygunsuzda hata kodu zorunlu
        var lineEntities = new List<QualityInspectionLine>(reqLines.Count);
        var order = 0;
        foreach (var l in reqLines.OrderBy(l => l.OrderNo))
        {
            order++;
            var result = EvaluateLine(l);
            if (result == InspectionLineResult.NonConforming && (l.DefectCodeId is null || l.DefectCodeId <= 0))
                return (false, $"'{l.CharacteristicName.Trim()}' uygunsuz — hata kodu seçilmeli.", 0);
            lineEntities.Add(new QualityInspectionLine
            {
                InspectionId = 0, // upsert sonrası set edilir (repo InspectionId'yi kendi yazar)
                PlanLineId = l.PlanLineId,
                CharacteristicName = l.CharacteristicName.Trim(),
                Nominal = l.Nominal,
                LowerTol = l.LowerTol,
                UpperTol = l.UpperTol,
                Measured = l.Measured,
                IsNumeric = l.IsNumeric,
                Result = result,
                DefectCodeId = result == InspectionLineResult.NonConforming ? l.DefectCodeId : null,
                OrderNo = order,
                Notes = string.IsNullOrWhiteSpace(l.Notes) ? null : l.Notes.Trim(),
                CreatedById = userId,
            });
        }

        // Genel Verdict: herhangi uygunsuz → Uygunsuz; en az bir uygun ve uygunsuz yok → Uygun; hepsi değerlendirilmedi → null
        InspectionVerdict? verdict =
            lineEntities.Any(x => x.Result == InspectionLineResult.NonConforming) ? InspectionVerdict.NonConforming
            : lineEntities.Any(x => x.Result == InspectionLineResult.Conforming) ? InspectionVerdict.Conforming
            : null;

        // ── Mevcut kayıt: companion güncelle ──
        if (request.Id > 0)
        {
            var existing = await _repo.GetInspectionByDocumentIdAsync(request.Id, ct);
            if (existing is null) return (false, "Muayene kaydı bulunamadı.", 0);
            var old = Clone(existing);

            existing.PlanId = request.PlanId;
            existing.ItemId = request.ItemId;
            existing.InspectionType = (InspectionType)request.InspectionType;
            existing.Verdict = verdict;
            // Disposition SetDisposition ile yönetilir; uygunsuz değilse temizle
            if (verdict != InspectionVerdict.NonConforming) existing.Disposition = null;
            existing.SourceKind = request.SourceKind;
            existing.SourceId = request.SourceId;
            existing.Quantity = request.Quantity;
            existing.InspectedByPersonnelId = request.InspectedByPersonnelId;
            existing.InspectedAt = request.InspectedAt;
            existing.Notes = request.Notes;
            existing.UpdatedById = userId;

            await _repo.UpsertInspectionAsync(existing, lineEntities, ct);
            _audit?.LogUpdate(AuditEntity, request.Id, existing.DocumentNumberOrId(), old, existing);
            return (true, null, request.Id);
        }

        // ── Yeni kayıt: Document shell + companion ──
        var type = await _documentTypes.GetByCodeAsync(TypeCode, ct);
        if (type is null) return (false, "'muayene' belge tipi tanımlı değil (DB init çalışmadı mı?).", 0);
        var number = await _numberService.GenerateNextAsync(
                         new DocumentNumberContext(type.Id, null, null, userId, null, DateTime.Now), ct)
                     ?? await _documents.GetNextDocumentNumberAsync(ct);
        var doc = new Document
        {
            DocumentNumber = number,
            DocumentTypeId = type.Id,
            DocumentDate = DateTime.Now,
            Status = DocumentStatus.Draft, // otorite companion.Status'te
            CreatedById = userId,
        };
        var documentId = await _documents.UpsertAsync(doc, ct);

        var inspection = new QualityInspection
        {
            DocumentId = documentId,
            PlanId = request.PlanId,
            ItemId = request.ItemId,
            InspectionType = (InspectionType)request.InspectionType,
            Status = InspectionStatus.Draft,
            Verdict = verdict,
            SourceKind = request.SourceKind,
            SourceId = request.SourceId,
            Quantity = request.Quantity,
            InspectedByPersonnelId = request.InspectedByPersonnelId,
            InspectedAt = request.InspectedAt,
            Notes = request.Notes,
            CreatedById = userId,
        };
        await _repo.UpsertInspectionAsync(inspection, lineEntities, ct);
        _audit?.LogInsert(AuditEntity, documentId, number, snapshot: inspection);
        return (true, null, documentId);
    }

    public async Task<(bool Ok, string? Error)> ChangeInspectionStatusAsync(ChangeInspectionStatusRequest request, int? userId, CancellationToken ct)
    {
        if (!Enum.IsDefined(typeof(InspectionStatus), request.NewStatus)) return (false, "Geçersiz durum.");
        var q = await _repo.GetInspectionByDocumentIdAsync(request.DocumentId, ct);
        if (q is null) return (false, "Muayene kaydı bulunamadı.");
        var next = (InspectionStatus)request.NewStatus;
        if (q.Status == next) return (true, null);
        if (!ValidateTransition(q.Status, next))
            return (false, $"Geçersiz durum geçişi: {SqlDescribe(q.Status)} → {SqlDescribe(next)}.");

        // Tamamlandı: genel karar gerekli; uygunsuzsa karar (Disposition) da girilmiş olmalı
        if (next == InspectionStatus.Completed)
        {
            if (q.Verdict is null) return (false, "Muayene tamamlanmadan önce ölçüm sonucu (karar) oluşmalı.");
            if (q.Verdict == InspectionVerdict.NonConforming && q.Disposition is null)
                return (false, "Uygunsuz muayene tamamlanmadan önce uygunsuzluk kararı (Kabul/Ret/Yeniden İşlem) verilmeli.");
        }

        await _repo.UpdateInspectionStateAsync(request.DocumentId, (byte)next,
            q.Verdict.HasValue ? (byte)q.Verdict.Value : null,
            q.Disposition.HasValue ? (byte)q.Disposition.Value : null, userId, ct);
        _audit?.LogChanges(AuditEntity, request.DocumentId, null,
            new[] { new AuditFieldChange("Status", "Durum", SqlDescribe(q.Status), SqlDescribe(next)) });
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> SetInspectionDispositionAsync(SetInspectionDispositionRequest request, int? userId, CancellationToken ct)
    {
        if (!Enum.IsDefined(typeof(InspectionDisposition), request.Disposition)) return (false, "Geçersiz karar.");
        var q = await _repo.GetInspectionByDocumentIdAsync(request.DocumentId, ct);
        if (q is null) return (false, "Muayene kaydı bulunamadı.");
        if (q.Verdict != InspectionVerdict.NonConforming)
            return (false, "Uygunsuzluk kararı yalnızca uygunsuz muayenelerde verilir.");

        var disp = (InspectionDisposition)request.Disposition;
        await _repo.UpdateInspectionStateAsync(request.DocumentId, (byte)q.Status,
            (byte)q.Verdict.Value, (byte)disp, userId, ct);
        _audit?.LogChanges(AuditEntity, request.DocumentId, null,
            new[] { new AuditFieldChange("Disposition", "Uygunsuzluk Kararı",
                q.Disposition.HasValue ? SqlDescribe(q.Disposition.Value) : null, SqlDescribe(disp)) });
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> DeleteInspectionAsync(int documentId, int? userId, CancellationToken ct)
    {
        var q = await _repo.GetInspectionByDocumentIdAsync(documentId, ct);
        if (q is null) return (false, "Muayene kaydı bulunamadı.");
        // Muayene belgesi = Document; silme Document üzerinden (companion JOIN ile filtrelenir)
        await _documents.DeleteAsync(documentId, ct);
        _audit?.LogDelete(AuditEntity, documentId, null);
        return (true, null);
    }

    // ── Yardımcılar ───────────────────────────────────────────────

    private static InspectionLineResult EvaluateLine(SaveQualityInspectionLine l)
    {
        if (l.IsNumeric)
        {
            if (l.Measured is null) return InspectionLineResult.NotEvaluated;
            var okLow = l.LowerTol is null || l.Measured >= l.LowerTol;
            var okUp = l.UpperTol is null || l.Measured <= l.UpperTol;
            return okLow && okUp ? InspectionLineResult.Conforming : InspectionLineResult.NonConforming;
        }
        // Görsel satır: sonuç operatörden gelir (0/1/2)
        return Enum.IsDefined(typeof(InspectionLineResult), l.Result) ? (InspectionLineResult)l.Result : InspectionLineResult.NotEvaluated;
    }

    private static bool ValidateTransition(InspectionStatus current, InspectionStatus next) => (current, next) switch
    {
        (InspectionStatus.Draft, InspectionStatus.InProgress) => true,
        (InspectionStatus.Draft, InspectionStatus.Completed) => true,
        (InspectionStatus.InProgress, InspectionStatus.Completed) => true,
        (InspectionStatus.InProgress, InspectionStatus.Draft) => true,
        (InspectionStatus.Completed, InspectionStatus.InProgress) => true, // yeniden aç
        _ => false,
    };

    private static string SqlDescribe(Enum v) => Persistence_Describe(v);
    // Domain enum Description'ı (repo'daki helper Persistence'ta; burada küçük yansıma kopyası)
    private static string Persistence_Describe(Enum v)
    {
        var m = v.GetType().GetMember(v.ToString()).FirstOrDefault();
        var a = m?.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false).FirstOrDefault()
            as System.ComponentModel.DescriptionAttribute;
        return a?.Description ?? v.ToString();
    }

    private static QualityInspection Clone(QualityInspection q) => new()
    {
        Id = q.Id, DocumentId = q.DocumentId, PlanId = q.PlanId, ItemId = q.ItemId,
        InspectionType = q.InspectionType, Status = q.Status, Verdict = q.Verdict, Disposition = q.Disposition,
        SourceKind = q.SourceKind, SourceId = q.SourceId, Quantity = q.Quantity,
        InspectedByPersonnelId = q.InspectedByPersonnelId, InspectedAt = q.InspectedAt, Notes = q.Notes,
    };
}

internal static class QualityInspectionExtensions
{
    public static string DocumentNumberOrId(this QualityInspection q) => q.DocumentId.ToString();
}
