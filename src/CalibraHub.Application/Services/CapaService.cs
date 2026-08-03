using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Approval.EntityTypes;
using CalibraHub.Application.Auditing;
using CalibraHub.Application.Constants;
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
///
/// Kapanış onayı (2026-08-04): ApprovalFlow "Capa" entity kind'ı (<see cref="CapaApprovalEntityType"/>)
/// üzerinden mevcut onay akışı altyapısına ADDITIVE olarak bağlanır. Şirket parametresi
/// (ApprovalParameters, kind="Capa") ile açık/kapalı; eşleşen aktif akış yoksa eski davranış
/// (doğrudan kapanış) aynen sürer — belge (Document/SalesQuote vb.) onay yolları bu değişiklikten
/// ETKİLENMEZ (ayrı EntityKind="Capa", ayrı kod dalı).
/// </summary>
public sealed class CapaService : ICapaService
{
    private const string TypeCode = "dof";
    private const string AuditEntity = "Capa";

    /// <summary>
    /// ApprovalFlow entity kind'ı — <see cref="CapaApprovalEntityType.Code"/> ile TUTARLI olmalı
    /// (kayıt/DI ve ApprovalController/SqlApprovalInstanceRepository'deki "Capa" literalleriyle de).
    /// </summary>
    private const string ApprovalEntityKind = "Capa";

    private readonly ICapaRepository _repo;
    private readonly IDocumentRepository _documents;
    private readonly IDocumentTypeRepository _documentTypes;
    private readonly IDocumentNumberService _numberService;
    private readonly ILogger<CapaService> _logger;
    private readonly IAuditTrailService? _audit;
    private readonly IApprovalFlowService? _approvalFlowService;
    private readonly ICompanyParameterService? _companyParameters;
    private readonly IUserProfileRepository? _userProfiles;

    public CapaService(
        ICapaRepository repo,
        IDocumentRepository documents,
        IDocumentTypeRepository documentTypes,
        IDocumentNumberService numberService,
        ILogger<CapaService> logger,
        IAuditTrailService? audit = null,
        IApprovalFlowService? approvalFlowService = null,
        ICompanyParameterService? companyParameters = null,
        IUserProfileRepository? userProfiles = null)
    {
        _repo = repo;
        _documents = documents;
        _documentTypes = documentTypes;
        _numberService = numberService;
        _logger = logger;
        _audit = audit;
        _approvalFlowService = approvalFlowService;
        _companyParameters = companyParameters;
        _userProfiles = userProfiles;
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

        // Yapısal kök neden satırları (5 Neden / Ishikawa / 8D) — aksiyon satırlarıyla AYNI
        // muamele: boş Text elenir (anlamsız satır sessizce yutulmaz, filtrelenir), Sequence
        // request'ten geldiği gibi korunur (5 Neden'de seviye, Ishikawa'da kategori-içi sıra —
        // aksiyonun global OrderNo'sundan farklı olarak burada global yeniden numaralama YOK).
        var rootCauseRequests = (request.RootCauseItems ?? Array.Empty<SaveCapaRootCauseItemRequest>())
            .Where(x => !string.IsNullOrWhiteSpace(x.Text)).ToList();
        var rootCauseEntities = new List<CapaRootCauseItem>(rootCauseRequests.Count);
        foreach (var x in rootCauseRequests)
        {
            if (!Enum.IsDefined(typeof(RootCauseMethod), x.Method)) return (false, "Geçersiz kök neden yöntemi (yapısal analiz).", 0);
            if (x.Category.HasValue && !Enum.IsDefined(typeof(IshikawaCategory), x.Category.Value))
                return (false, "Geçersiz Ishikawa (6M) kategorisi.", 0);
            // method↔category invaryantı — yapısal satır TEKNİĞE aittir (5 Neden veya Ishikawa; 8D bir teknik değil,
            // kapsayıcı yöntemdir). Aksi kombinasyonlar load'da hiçbir editöre render edilmeyip sonraki save'de
            // sessizce silinirdi → server-side reddet (code review 2026-08-03 fix).
            if (x.Method == (byte)RootCauseMethod.SekizD)
                return (false, "Yapısal kök neden satırı 5 Neden veya Ishikawa tekniğine ait olmalı.", 0);
            if (x.Method == (byte)RootCauseMethod.Ishikawa && !x.Category.HasValue)
                return (false, "Ishikawa (6M) satırında kategori zorunludur.", 0);
            if (x.Method == (byte)RootCauseMethod.FiveWhy && x.Category.HasValue)
                return (false, "5 Neden satırında 6M kategorisi olmamalıdır.", 0);
            rootCauseEntities.Add(new CapaRootCauseItem
            {
                Id = x.Id,
                CapaId = 0, // upsert sonrası repo kendi CapaId'yi yazar
                Method = (RootCauseMethod)x.Method,
                Category = x.Category.HasValue ? (IshikawaCategory)x.Category.Value : null,
                Sequence = x.Sequence,
                Text = x.Text.Trim(),
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

            await _repo.UpsertAsync(existing, actionEntities, rootCauseEntities, ct);
            _audit?.LogUpdate(AuditEntity, request.Id, title, old, existing);
            var actionChanges = BuildActionChanges(oldDetail?.Actions ?? Array.Empty<CapaActionDto>(), actionEntities);
            if (actionChanges.Count > 0)
                _audit?.LogChanges(AuditEntity, request.Id, title, actionChanges, detail: "Aksiyon satırları güncellendi");
            var rootCauseChanges = BuildRootCauseChanges(oldDetail?.RootCauseItems ?? Array.Empty<CapaRootCauseItemDto>(), rootCauseEntities);
            if (rootCauseChanges.Count > 0)
                _audit?.LogChanges(AuditEntity, request.Id, title, rootCauseChanges, detail: "Yapısal kök neden analizi güncellendi");
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
        await _repo.UpsertAsync(capa, actionEntities, rootCauseEntities, ct);
        // Yeni kayıtta aksiyon + kök neden satırları da "boş → değer" dökümü olarak eklenir
        // (audit #5 — insert'te ilk değer dökümü kuralı, satırlar için LogChanges/elle diff ile genişletilir).
        var insertActionChanges = BuildActionChanges(Array.Empty<CapaActionDto>(), actionEntities);
        var insertRootCauseChanges = BuildRootCauseChanges(Array.Empty<CapaRootCauseItemDto>(), rootCauseEntities);
        var insertExtraChanges = insertActionChanges.Concat(insertRootCauseChanges).ToList();
        _audit?.LogInsert(AuditEntity, documentId, number, snapshot: capa,
            extraChanges: insertExtraChanges.Count > 0 ? insertExtraChanges : null);
        return (true, null, documentId);
    }

    public async Task<(bool Ok, string? Error, string? Message, byte? ActualStatus)> ChangeStatusAsync(ChangeCapaStatusRequest request, int? userId, CancellationToken ct)
    {
        if (!Enum.IsDefined(typeof(CapaStatus), request.NewStatus)) return (false, "Geçersiz durum.", null, null);
        var capa = await _repo.GetByDocumentIdAsync(request.Id, ct);
        if (capa is null) return (false, "DÖF kaydı bulunamadı.", null, null);
        var next = (CapaStatus)request.NewStatus;
        if (capa.Status == next) return (true, null, null, null);
        if (!ValidateTransition(capa.Status, next))
            return (false, $"Geçersiz durum geçişi: {Describe(capa.Status)} → {Describe(next)}.", null, null);

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
                return (false, "Kapatmak için tüm aksiyonlar tamamlanmalı ve etkinlik doğrulanmalı.", null, null);
            if (!capa.EffectivenessVerified)
                return (false, "Kapatmak için tüm aksiyonlar tamamlanmalı ve etkinlik doğrulanmalı.", null, null);

            // Kapanış onayı (ADDITIVE): aktif bir "Capa" ApprovalFlow eşleşirse durum burada
            // Kapali'ya GEÇMEZ — DogrulamaBekliyor'da kalır, onay süreci başlatılır. Gerçek
            // kapanış OnClosureApprovalCompletedAsync (ApprovalController → onay tamamlandığında)
            // içinde yapılır. Servis inject edilmemişse (test/DI eksik) veya eşleşen akış yoksa
            // ya da şirket parametresi kapalıysa eski davranış (doğrudan kapanış) aynen sürer.
            if (_approvalFlowService is not null)
            {
                var approvalEnabled = true;
                if (_companyParameters is not null)
                {
                    approvalEnabled = await _companyParameters.GetBoolAsync(
                        ApprovalParameters.FormCode, ApprovalParameters.EnabledKey(ApprovalEntityKind), ct) ?? true;
                }

                if (approvalEnabled)
                {
                    ApprovalFlowDto? flow = null;
                    try
                    {
                        flow = await _approvalFlowService.MatchFlowAsync(ApprovalEntityKind, 0m, null, null, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "DÖF kapanış onayı akış eşleme hatası (documentId={Id})", request.Id);
                        // Fail-closed: kapanış onayı bir governance kapısıdır — eşleme geçici bir hatayla
                        // belirlenemezse DÖF onaysız kapanmamalı. Kapatmayı blokla, kullanıcı tekrar denesin
                        // (code review 2026-08-04). Eşleşen akış GERÇEKTEN yoksa MatchFlowAsync null döner
                        // (exception değil) → aşağıdaki flow==null yolu doğrudan kapatır, eski davranış korunur.
                        return (false, "Kapanış onayı durumu belirlenemedi. Lütfen tekrar deneyin.", null, null);
                    }

                    if (flow is not null)
                    {
                        var existingInstance = await _approvalFlowService.GetInstanceByDocumentIdAsync(request.Id, ct);
                        if (existingInstance is not null
                            && string.Equals(existingInstance.Status, "Pending", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(existingInstance.EntityKind, ApprovalEntityKind, StringComparison.OrdinalIgnoreCase))
                        {
                            return (false, "Bu DÖF zaten kapanış onayında; onay bekleniyor.", null, null);
                        }

                        var startedBy = "system";
                        if (userId.HasValue && _userProfiles is not null)
                        {
                            var profile = await _userProfiles.GetByIdAsync(userId.Value, ct);
                            if (profile is not null) startedBy = profile.FullName;
                        }

                        try
                        {
                            await _approvalFlowService.StartAsync(
                                new StartApprovalRequest(
                                    DocumentId:      request.Id,
                                    FlowId:          flow.Id,
                                    StartedBy:       startedBy,
                                    StartedByUserId: userId,
                                    EntityKind:      ApprovalEntityKind),
                                ct);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "DÖF kapanış onayı başlatılamadı (documentId={Id})", request.Id);
                            return (false, "Kapanış onaya gönderilemedi.", null, null);
                        }

                        // Durum burada değiştirilmez — mevcut durumda (DogrulamaBekliyor) kalır.
                        _audit?.LogChanges(AuditEntity, request.Id, capa.Title,
                            new[] { new AuditFieldChange("ApprovalStatus", "Kapanış Onayı", "—", "Onaya Gönderildi") });
                        return (true, null, "Kapanış onaya gönderildi. Onaylandığında DÖF kapanacak.", (byte)capa.Status);
                    }
                }
            }
        }

        var closedAt = next == CapaStatus.Kapali ? DateTime.UtcNow : (DateTime?)null;
        await _repo.UpdateStatusAsync(request.Id, (byte)next, closedAt, userId, ct);
        _audit?.LogChanges(AuditEntity, request.Id, capa.Title,
            new[] { new AuditFieldChange("Status", "Durum", Describe(capa.Status), Describe(next)) });
        return (true, null, null, null);
    }

    /// <summary>
    /// ApprovalController tarafından EntityKind="Capa" onay örneği tamamlandığında (Approved/Rejected)
    /// çağrılır. Idempotent: kayıt bulunamazsa veya Status artık DogrulamaBekliyor değilse
    /// (zaten kapanmış/iptal/elle değiştirilmiş olabilir) no-op — ama SESSİZ değil, logla.
    /// approved=true → kapanış guard'ı TEKRAR doğrulanır (onay sürecinde aksiyon/etkinlik durumu
    /// değişmiş olabilir); guard geçmezse kapatma yapılmaz. approved=false → Aksiyonda'ya döner.
    /// </summary>
    public async Task OnClosureApprovalCompletedAsync(int documentId, bool approved, int? actorUserId, CancellationToken ct)
    {
        var capa = await _repo.GetByDocumentIdAsync(documentId, ct);
        if (capa is null)
        {
            _logger.LogWarning("DÖF kapanış onayı tamamlandı ama kayıt bulunamadı (documentId={Id}).", documentId);
            return;
        }
        if (capa.Status != CapaStatus.DogrulamaBekliyor)
        {
            _logger.LogInformation(
                "DÖF kapanış onayı tamamlandı ama durum DogrulamaBekliyor değil (documentId={Id}, status={Status}) — no-op.",
                documentId, capa.Status);
            return;
        }

        if (approved)
        {
            var detail = await _repo.GetDetailAsync(documentId, ct);
            var actionsNotDone = detail?.Actions.Any(a =>
                (CapaActionStatus)a.Status != CapaActionStatus.Tamamlandi &&
                (CapaActionStatus)a.Status != CapaActionStatus.Iptal) ?? false;
            if (actionsNotDone || !capa.EffectivenessVerified)
            {
                _logger.LogWarning(
                    "DÖF kapanış onayı ONAYLANDI ama kapanış guard'ı artık geçmiyor (documentId={Id}) — kapatılmadı.",
                    documentId);
                return;
            }

            await _repo.UpdateStatusAsync(documentId, (byte)CapaStatus.Kapali, DateTime.UtcNow, actorUserId, ct);
            _audit?.LogChanges(AuditEntity, documentId, capa.Title,
                new[]
                {
                    new AuditFieldChange("Status", "Durum", Describe(CapaStatus.DogrulamaBekliyor), Describe(CapaStatus.Kapali)),
                    new AuditFieldChange("ApprovalStatus", "Kapanış Onayı", "Onaya Gönderildi", "Onaylandı"),
                },
                detail: "Kapanış onayı ile kapatıldı");
        }
        else
        {
            await _repo.UpdateStatusAsync(documentId, (byte)CapaStatus.Aksiyonda, null, actorUserId, ct);
            _audit?.LogChanges(AuditEntity, documentId, capa.Title,
                new[]
                {
                    new AuditFieldChange("Status", "Durum", Describe(CapaStatus.DogrulamaBekliyor), Describe(CapaStatus.Aksiyonda)),
                    new AuditFieldChange("ApprovalStatus", "Kapanış Onayı", "Onaya Gönderildi", "Reddedildi"),
                },
                detail: "Kapanış onayı reddedildi — yeniden çalışma");
        }
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
        {
            snapshot.AddRange(BuildActionChanges(detail.Actions, Array.Empty<CapaAction>()));
            snapshot.AddRange(BuildRootCauseChanges(detail.RootCauseItems, Array.Empty<CapaRootCauseItem>()));
        }

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

    /// <summary>
    /// Yapısal kök neden satırı diff'i — BuildActionChanges'in analoğu. Eşleme yalnız Id
    /// üzerinden yapılır (client mevcut satırın Id'sini korur, yeni eklenen satırlarda Id &lt;= 0
    /// gelir). Silinen/eklenen satırlar için Old/New'den biri null; eşleşen satırlarda
    /// Text/Method/Category/Sequence alan bazlı diff.
    /// </summary>
    private static List<AuditFieldChange> BuildRootCauseChanges(
        IReadOnlyCollection<CapaRootCauseItemDto> oldItems, IReadOnlyList<CapaRootCauseItem> newItems)
    {
        var changes = new List<AuditFieldChange>();
        var oldById = oldItems.Where(x => x.Id > 0).ToDictionary(x => x.Id);
        var matchedOldIds = new HashSet<int>();
        var pairs = new List<(CapaRootCauseItemDto Old, CapaRootCauseItem New)>();
        var added = new List<CapaRootCauseItem>();

        foreach (var n in newItems)
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
        var removed = oldItems.Where(o => o.Id > 0 && !matchedOldIds.Contains(o.Id)).ToList();

        foreach (var o in removed)
            changes.Add(new AuditFieldChange($"RootCause[{o.Id}]", $"Kök Neden Satırı Silindi — {o.Text}", o.Text, null));

        foreach (var n in added)
            changes.Add(new AuditFieldChange($"RootCause[new{n.Sequence}]", $"Kök Neden Satırı Eklendi — {n.Text}", null, n.Text));

        foreach (var (o, n) in pairs)
        {
            var name = n.Text;
            void AddIfChanged(string field, string label, object? oldVal, object? newVal)
            {
                var os = AuditDiff.Normalize(oldVal);
                var ns = AuditDiff.Normalize(newVal);
                if (!string.Equals(os ?? "", ns ?? "", StringComparison.Ordinal))
                    changes.Add(new AuditFieldChange($"RootCause[{o.Id}].{field}", $"{name} · {label}", os, ns));
            }
            AddIfChanged("Text", "Metin", o.Text, n.Text);
            AddIfChanged("Method", "Yöntem", Describe((RootCauseMethod)o.Method), Describe(n.Method));
            AddIfChanged("Category", "6M Kategorisi", o.CategoryLabel, n.Category.HasValue ? Describe(n.Category.Value) : null);
            AddIfChanged("Sequence", "Sıra", o.Sequence, n.Sequence);
        }
        return changes;
    }
}
