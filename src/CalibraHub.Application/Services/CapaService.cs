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

    public Task<CapaKpiDto> GetKpiAsync(CancellationToken ct) => _repo.GetKpiAsync(ct);

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
                // Id: request'ten gelen aksiyon Id'si yalnız audit diff eşlemesi İÇİN taşınır
                // (var olan satır mı, yeni satır mı ayrımı — BuildActionChanges). Repo'nun INSERT
                // ifadesi [Id] kolonunu YAZMAZ (IDENTITY), bu yüzden burada set etmek DB'ye gitmez.
                Id = a.Id,
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
            // Aksiyon satırlarının eski hali — repo UpsertAsync bunları DELETE+INSERT ile yeniden
            // yazacağı için diff'i BUNDAN ÖNCE (silinmeden önceki hal) almalıyız.
            var oldDetail = await _repo.GetDetailAsync(request.Id, ct);

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

            // KAPALI KAYIT DOĞRULAMA GUARD'I: "Kapalı ⇒ etkinlik doğrulanmış" invaryantı Save
            // ile bozulamaz — kapalı bir DÖF'te doğrulama alanları salt-okunurdur, request'ten
            // gelen değerler YOK SAYILIR (mevcut değerler korunur). Yeniden açma yalnızca
            // ChangeStatusAsync üzerinden yapılır.
            if (existing.Status != CapaStatus.Kapali)
            {
                existing.EffectivenessVerified = request.EffectivenessVerified;
                existing.VerifiedByPersonnelId = request.VerifiedByPersonnelId;
                existing.VerifiedAt = request.VerifiedAt;
            }
            existing.EffectivenessNote = string.IsNullOrWhiteSpace(request.EffectivenessNote) ? null : request.EffectivenessNote.Trim();
            existing.UpdatedById = userId;

            await _repo.UpsertAsync(existing, actionEntities, ct);
            _audit?.LogUpdate(AuditEntity, request.Id, title, old, existing);
            var actionChanges = BuildActionChanges(oldDetail?.Actions ?? Array.Empty<CapaActionDto>(), actionEntities);
            if (actionChanges.Count > 0)
                _audit?.LogChanges(AuditEntity, request.Id, title, actionChanges, detail: "Aksiyon satırları güncellendi");
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
        // Yeni kayıtta aksiyon satırları da "boş → değer" dökümü olarak eklenir (audit #5 —
        // insert'te ilk değer dökümü kuralı, satırlar için LogChanges/elle diff ile genişletilir).
        var insertActionChanges = BuildActionChanges(Array.Empty<CapaActionDto>(), actionEntities);
        _audit?.LogInsert(AuditEntity, documentId, number, snapshot: capa,
            extraChanges: insertActionChanges.Count > 0 ? insertActionChanges : null);
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
        // Silinen içeriğin dökümü — header alanları + aksiyon satırları — SİLİNMEDEN ÖNCE
        // çekilir ("ne kayboldu" izlenebilsin, CLAUDE.md audit #5/Delete kuralı).
        var detail = await _repo.GetDetailAsync(documentId, ct);
        var snapshot = new List<AuditFieldChange>
        {
            new("Title", "Konu", capa.Title, null),
            new("CapaType", "DÖF Türü", Describe(capa.CapaType), null),
            new("Severity", "Önem Derecesi", Describe(capa.Severity), null),
            new("Status", "Durum", Describe(capa.Status), null),
        };
        if (!string.IsNullOrWhiteSpace(capa.ProblemDescription))
            snapshot.Add(new AuditFieldChange("ProblemDescription", "Problem Tanımı", capa.ProblemDescription, null));
        if (detail is not null)
            snapshot.AddRange(BuildActionChanges(detail.Actions, Array.Empty<CapaAction>()));

        // DÖF belgesi = Document; silme Document üzerinden (companion JOIN ile filtrelenir).
        await _documents.DeleteAsync(documentId, ct);
        _audit?.LogDelete(AuditEntity, documentId, capa.Title, snapshot: snapshot);
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

    /// <summary>
    /// Aksiyon satırı diff'i — DocumentService.BuildLineChanges deseninin sadeleştirilmiş hali
    /// (kalem satırlarındaki içerik-anahtarı fallback'i burada gerekmiyor; aksiyon satırları
    /// serbest metin olduğu için eşleme yalnız Id üzerinden yapılır — client mevcut satırın
    /// Id'sini korur, yeni eklenen satırlarda Id &lt;= 0 gelir).
    /// Silinen/eklenen satırlar için Old/New'den biri null (rapor: "Aksiyon Silindi/Eklendi");
    /// eşleşen satırlarda alan bazlı (Description/ActionType/Status/Sorumlu/Termin/Tamamlanma) diff.
    /// </summary>
    private static List<AuditFieldChange> BuildActionChanges(
        IReadOnlyCollection<CapaActionDto> oldActions, IReadOnlyList<CapaAction> newActions)
    {
        var changes = new List<AuditFieldChange>();
        var oldById = oldActions.Where(a => a.Id > 0).ToDictionary(a => a.Id);
        var matchedOldIds = new HashSet<int>();
        var pairs = new List<(CapaActionDto Old, CapaAction New)>();
        var added = new List<CapaAction>();

        foreach (var n in newActions)
        {
            if (n.Id > 0 && oldById.TryGetValue(n.Id, out var o))
            {
                pairs.Add((o, n));
                matchedOldIds.Add(n.Id);
            }
            else
            {
                added.Add(n);
            }
        }
        var removed = oldActions.Where(o => o.Id > 0 && !matchedOldIds.Contains(o.Id)).ToList();

        foreach (var o in removed)
            changes.Add(new AuditFieldChange($"Action[{o.Id}]", $"Aksiyon Silindi — {o.Description}", o.Description, null));

        foreach (var n in added)
            changes.Add(new AuditFieldChange($"Action[new{n.OrderNo}]", $"Aksiyon Eklendi — {n.Description}", null, n.Description));

        foreach (var (o, n) in pairs)
        {
            var name = n.Description;
            void AddIfChanged(string field, string label, object? oldVal, object? newVal)
            {
                var os = AuditDiff.Normalize(oldVal);
                var ns = AuditDiff.Normalize(newVal);
                if (!string.Equals(os ?? "", ns ?? "", StringComparison.Ordinal))
                    changes.Add(new AuditFieldChange($"Action[{o.Id}].{field}", $"{name} · {label}", os, ns));
            }
            AddIfChanged("Description", "Açıklama", o.Description, n.Description);
            AddIfChanged("ActionType", "Aksiyon Tipi", Describe((CapaActionType)o.ActionType), Describe(n.ActionType));
            AddIfChanged("Status", "Durum", Describe((CapaActionStatus)o.Status), Describe(n.Status));
            AddIfChanged("ResponsiblePersonnelId", "Sorumlu", o.ResponsiblePersonnelId, n.ResponsiblePersonnelId);
            AddIfChanged("DueDate", "Termin", o.DueDate, n.DueDate);
            AddIfChanged("CompletedAt", "Tamamlanma Zamanı", o.CompletedAt, n.CompletedAt);
        }
        return changes;
    }
}
