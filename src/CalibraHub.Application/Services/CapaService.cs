using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Auditing;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Services;

/// <summary>
/// DÖF (Düzeltici/Önleyici Faaliyet — CAPA) servisi. QualityService'in Document shell deseni
/// (companion + Document 'dof' 1-1) birebir izlenir. Kapanış (Status=Kapali) yalnızca tüm
/// CapaAction satırları Tamamlandı/İptal VE EffectivenessVerified=1 ise izinlidir — server-side
/// guard burada uygulanır, bypass edilemez (frontend kontrolü bu guard'ın yerini tutmaz).
/// </summary>
public sealed class CapaService : ICapaService
{
    private const string TypeCode = "dof";
    private const string AuditEntity = "Capa";

    private readonly ICapaRepository _repo;
    private readonly IDocumentRepository _documents;
    private readonly IDocumentTypeRepository _documentTypes;
    private readonly IDocumentNumberService _numberService;
    private readonly ILogger<CapaService> _logger;
    private readonly IAuditTrailService? _audit;

    public CapaService(
        ICapaRepository repo,
        IDocumentRepository documents,
        IDocumentTypeRepository documentTypes,
        IDocumentNumberService numberService,
        ILogger<CapaService> logger,
        IAuditTrailService? audit = null)
    {
        _repo = repo;
        _documents = documents;
        _documentTypes = documentTypes;
        _numberService = numberService;
        _logger = logger;
        _audit = audit;
    }

    public Task<IReadOnlyCollection<CapaListItemDto>> ListAsync(string? search, byte? status, CancellationToken ct)
        => _repo.ListAsync(search, status, ct);

    public Task<CapaDetailDto?> GetAsync(int documentId, CancellationToken ct) => _repo.GetDetailAsync(documentId, ct);

    public Task<IReadOnlyCollection<CapaPersonnelOption>> GetPersonnelOptionsAsync(CancellationToken ct)
        => _repo.GetPersonnelOptionsAsync(ct);

    public async Task<(bool Ok, string? Error, int DocumentId)> SaveAsync(SaveCapaRequest request, int? userId, CancellationToken ct)
    {
        var title = request.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title)) return (false, "DÖF konusu zorunludur.", 0);
        if (!Enum.IsDefined(typeof(CapaType), request.CapaType)) return (false, "Geçersiz DÖF türü.", 0);
        if (!Enum.IsDefined(typeof(CapaSeverity), request.Severity)) return (false, "Geçersiz önem derecesi.", 0);
        if (request.RootCauseMethod.HasValue && !Enum.IsDefined(typeof(RootCauseMethod), request.RootCauseMethod.Value))
            return (false, "Geçersiz kök neden yöntemi.", 0);

        var actions = (request.Actions ?? Array.Empty<SaveCapaActionRequest>())
            .Where(a => !string.IsNullOrWhiteSpace(a.Description)).ToList();
        var order = 0;
        var actionEntities = new List<CapaAction>(actions.Count);
        foreach (var a in actions.OrderBy(a => a.OrderNo))
        {
            if (!Enum.IsDefined(typeof(CapaActionType), a.ActionType)) return (false, "Geçersiz aksiyon tipi.", 0);
            if (!Enum.IsDefined(typeof(CapaActionStatus), a.Status)) return (false, "Geçersiz aksiyon durumu.", 0);
            order++;
            actionEntities.Add(new CapaAction
            {
                CapaId = 0, // upsert sonrası repo kendi CapaId'yi yazar
                ActionType = (CapaActionType)a.ActionType,
                Description = a.Description.Trim(),
                ResponsiblePersonnelId = a.ResponsiblePersonnelId,
                DueDate = a.DueDate,
                Status = (CapaActionStatus)a.Status,
                CompletedAt = a.CompletedAt,
                OrderNo = order,
                CreatedById = userId,
            });
        }

        // ── Mevcut kayıt: companion güncelle ──
        if (request.Id > 0)
        {
            var existing = await _repo.GetByDocumentIdAsync(request.Id, ct);
            if (existing is null) return (false, "DÖF kaydı bulunamadı.", 0);
            var old = Clone(existing);

            existing.CapaType = (CapaType)request.CapaType;
            existing.SourceKind = request.SourceKind;
            existing.SourceId = request.SourceId;
            existing.Title = title;
            existing.ProblemDescription = string.IsNullOrWhiteSpace(request.ProblemDescription) ? null : request.ProblemDescription.Trim();
            existing.DefectCodeId = request.DefectCodeId;
            existing.Severity = (CapaSeverity)request.Severity;
            existing.RootCauseMethod = request.RootCauseMethod.HasValue ? (RootCauseMethod)request.RootCauseMethod.Value : null;
            existing.RootCause = string.IsNullOrWhiteSpace(request.RootCause) ? null : request.RootCause.Trim();
            existing.ResponsiblePersonnelId = request.ResponsiblePersonnelId;
            existing.DueDate = request.DueDate;
            existing.EffectivenessVerified = request.EffectivenessVerified;
            existing.VerifiedByPersonnelId = request.VerifiedByPersonnelId;
            existing.VerifiedAt = request.VerifiedAt;
            existing.EffectivenessNote = string.IsNullOrWhiteSpace(request.EffectivenessNote) ? null : request.EffectivenessNote.Trim();
            existing.UpdatedById = userId;

            await _repo.UpsertAsync(existing, actionEntities, ct);
            _audit?.LogUpdate(AuditEntity, request.Id, title, old, existing);
            return (true, null, request.Id);
        }

        // ── Yeni kayıt: Document shell + companion ──
        var type = await _documentTypes.GetByCodeAsync(TypeCode, ct);
        if (type is null) return (false, "'dof' belge tipi tanımlı değil (DB init çalışmadı mı?).", 0);
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

        var capa = new Capa
        {
            DocumentId = documentId,
            CapaType = (CapaType)request.CapaType,
            SourceKind = request.SourceKind,
            SourceId = request.SourceId,
            Title = title,
            ProblemDescription = string.IsNullOrWhiteSpace(request.ProblemDescription) ? null : request.ProblemDescription.Trim(),
            DefectCodeId = request.DefectCodeId,
            Severity = (CapaSeverity)request.Severity,
            RootCauseMethod = request.RootCauseMethod.HasValue ? (RootCauseMethod)request.RootCauseMethod.Value : null,
            RootCause = string.IsNullOrWhiteSpace(request.RootCause) ? null : request.RootCause.Trim(),
            ResponsiblePersonnelId = request.ResponsiblePersonnelId,
            DueDate = request.DueDate,
            Status = CapaStatus.Acik,
            EffectivenessVerified = request.EffectivenessVerified,
            VerifiedByPersonnelId = request.VerifiedByPersonnelId,
            VerifiedAt = request.VerifiedAt,
            EffectivenessNote = string.IsNullOrWhiteSpace(request.EffectivenessNote) ? null : request.EffectivenessNote.Trim(),
            CreatedById = userId,
        };
        await _repo.UpsertAsync(capa, actionEntities, ct);
        _audit?.LogInsert(AuditEntity, documentId, number, snapshot: capa);
        return (true, null, documentId);
    }

    public async Task<(bool Ok, string? Error)> ChangeStatusAsync(ChangeCapaStatusRequest request, int? userId, CancellationToken ct)
    {
        if (!Enum.IsDefined(typeof(CapaStatus), request.NewStatus)) return (false, "Geçersiz durum.");
        var capa = await _repo.GetByDocumentIdAsync(request.Id, ct);
        if (capa is null) return (false, "DÖF kaydı bulunamadı.");
        var next = (CapaStatus)request.NewStatus;
        if (capa.Status == next) return (true, null);
        if (!ValidateTransition(capa.Status, next))
            return (false, $"Geçersiz durum geçişi: {Describe(capa.Status)} → {Describe(next)}.");

        // KAPANIŞ GUARD (server-side, kritik): tüm aksiyonlar Tamamlandı/İptal olmalı VE
        // etkinlik doğrulanmış olmalı. Bu mesaj kullanıcı-dostu bir iş kuralı açıklamasıdır,
        // iç detay sızdırmaz — doğrudan istemciye döndürülebilir.
        if (next == CapaStatus.Kapali)
        {
            var detail = await _repo.GetDetailAsync(request.Id, ct);
            var actionsNotDone = detail?.Actions.Any(a =>
                (CapaActionStatus)a.Status != CapaActionStatus.Tamamlandi &&
                (CapaActionStatus)a.Status != CapaActionStatus.Iptal) ?? false;
            if (actionsNotDone)
                return (false, "Kapatmak için tüm aksiyonlar tamamlanmalı ve etkinlik doğrulanmalı.");
            if (!capa.EffectivenessVerified)
                return (false, "Kapatmak için tüm aksiyonlar tamamlanmalı ve etkinlik doğrulanmalı.");
        }

        var closedAt = next == CapaStatus.Kapali ? DateTime.UtcNow : (DateTime?)null;
        await _repo.UpdateStatusAsync(request.Id, (byte)next, closedAt, userId, ct);
        _audit?.LogChanges(AuditEntity, request.Id, capa.Title,
            new[] { new AuditFieldChange("Status", "Durum", Describe(capa.Status), Describe(next)) });
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> DeleteAsync(int documentId, int? userId, CancellationToken ct)
    {
        var capa = await _repo.GetByDocumentIdAsync(documentId, ct);
        if (capa is null) return (false, "DÖF kaydı bulunamadı.");
        // DÖF belgesi = Document; silme Document üzerinden (companion JOIN ile filtrelenir).
        await _documents.DeleteAsync(documentId, ct);
        _audit?.LogDelete(AuditEntity, documentId, capa.Title);
        return (true, null);
    }

    private static bool ValidateTransition(CapaStatus current, CapaStatus next) => (current, next) switch
    {
        (CapaStatus.Acik, CapaStatus.KokNedenAnaliz) => true,
        (CapaStatus.Acik, CapaStatus.Iptal) => true,
        (CapaStatus.KokNedenAnaliz, CapaStatus.Aksiyonda) => true,
        (CapaStatus.KokNedenAnaliz, CapaStatus.Acik) => true,
        (CapaStatus.KokNedenAnaliz, CapaStatus.Iptal) => true,
        (CapaStatus.Aksiyonda, CapaStatus.DogrulamaBekliyor) => true,
        (CapaStatus.Aksiyonda, CapaStatus.KokNedenAnaliz) => true,
        (CapaStatus.Aksiyonda, CapaStatus.Iptal) => true,
        (CapaStatus.DogrulamaBekliyor, CapaStatus.Kapali) => true,
        (CapaStatus.DogrulamaBekliyor, CapaStatus.Aksiyonda) => true, // yetersiz doğrulama, geri aç
        (CapaStatus.DogrulamaBekliyor, CapaStatus.Iptal) => true,
        (CapaStatus.Kapali, CapaStatus.DogrulamaBekliyor) => true, // yeniden aç
        _ => false,
    };

    private static string Describe(Enum v)
    {
        var m = v.GetType().GetMember(v.ToString()).FirstOrDefault();
        var a = m?.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false).FirstOrDefault()
            as System.ComponentModel.DescriptionAttribute;
        return a?.Description ?? v.ToString();
    }

    private static Capa Clone(Capa c) => new()
    {
        Id = c.Id, DocumentId = c.DocumentId, CapaType = c.CapaType,
        SourceKind = c.SourceKind, SourceId = c.SourceId, Title = c.Title,
        ProblemDescription = c.ProblemDescription, DefectCodeId = c.DefectCodeId, Severity = c.Severity,
        RootCauseMethod = c.RootCauseMethod, RootCause = c.RootCause,
        ResponsiblePersonnelId = c.ResponsiblePersonnelId, DueDate = c.DueDate, Status = c.Status,
        EffectivenessVerified = c.EffectivenessVerified, VerifiedByPersonnelId = c.VerifiedByPersonnelId,
        VerifiedAt = c.VerifiedAt, EffectivenessNote = c.EffectivenessNote, ClosedAt = c.ClosedAt,
    };
}
