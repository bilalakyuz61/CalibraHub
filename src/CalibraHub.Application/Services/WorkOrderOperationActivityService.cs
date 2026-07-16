using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Auditing;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Common;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Services;

/// <summary>
/// Üretim sahası aktivite log servisi — Faz 1 MVP (2026-05-20).
/// Transition mantığı: yeni aktivite başlarken eski otomatik kapanır.
/// </summary>
public sealed class WorkOrderOperationActivityService : IWorkOrderOperationActivityService
{
    private readonly IWorkOrderOperationActivityRepository _repo;
    private readonly IWorkOrderOperationRepository? _operations;
    private readonly IAuditTrailService? _audit;

    public WorkOrderOperationActivityService(
        IWorkOrderOperationActivityRepository repo,
        IWorkOrderOperationRepository? operations = null,
        IAuditTrailService? audit = null)
    {
        _repo = repo;
        _operations = operations;
        _audit = audit;
    }

    /// <summary>İşlem logu satırlarında operasyonu tanımlayan kısa etiket (ör. "OP10 Kesim (Sıra 1)").</summary>
    private static string OpLabel(WorkOrderOperationDto op) =>
        (op.OperationCode ?? op.OperationName ?? ("Operasyon #" + op.Id)) + " (Sıra " + op.Sequence + ")";

    public Task<WorkOrderOperationActivityDto?> GetActiveAsync(int workOrderOperationId, CancellationToken ct)
        => _repo.GetActiveAsync(workOrderOperationId, ct);

    public Task<IReadOnlyList<WorkOrderOperationActivityDto>> GetHistoryAsync(int workOrderOperationId, CancellationToken ct)
        => _repo.GetHistoryAsync(workOrderOperationId, ct);

    public async Task<int> StartAsync(StartActivityRequest request, CancellationToken ct)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (request.WorkOrderOperationId <= 0)
            throw new ArgumentException("Aktivite bir operasyona bağlanmalıdır.");
        if (request.PersonnelId <= 0)
            throw new ArgumentException("Aktiviteyi yapan personel seçilmelidir.");

        // 1) Mevcut aktif aktiviteyi (varsa) önce kapat — filtered unique index
        //    çakışmasın diye atomik bir adım gerekli. Notes = "Yeni aktiviteye geçildi"
        //    eklemiyoruz (saha ekranı zaten geçişi log'lar).
        await _repo.EndActiveAsync(request.WorkOrderOperationId, request.PersonnelId, notes: null, ct);

        // 2) Yeni aktivite oluştur (Domain invariant kontrolü)
        var entity = new WorkOrderOperationActivity
        {
            WorkOrderOperationId = request.WorkOrderOperationId,
            PersonnelId          = request.PersonnelId,
            ActivityType         = request.ActivityType,
            ActivityReasonId     = request.ActivityReasonId,
            StartedAt            = DateTime.UtcNow,
            EndedAt              = null,
            Quantity             = null,            // Production miktarı ayrı PartialComplete akışından kayda geçer
            ScrapQuantity        = null,
            Notes                = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            // PersonnelId is the production operator (not the web session user); CreatedById remains null.
        };

        try { entity.EnsureValid(); }
        catch (DomainException dex) { throw new ArgumentException(dex.Message, dex); }

        var id = await _repo.StartAsync(entity, ct);

        // İşlem logu — yeni aktivite başlangıcı; ilgili iş emrinin ("WorkOrder") Değişiklik
        // Geçmişi zaman çizelgesine yazılır (WorkOrderEdit ekranı ile aynı entity+id kovası).
        if (_audit is not null)
        {
            try
            {
                var op = _operations is not null ? await _operations.GetAsync(request.WorkOrderOperationId, ct) : null;
                if (op is not null)
                {
                    var typeLabel = request.ActivityType.ToString();
                    _audit.LogChanges("WorkOrder", op.WorkOrderId, op.WorkOrderNumber,
                        [new AuditFieldChange($"Activity[{id}].ActivityType",
                            $"{OpLabel(op)} — Aktivite Başlangıcı", null, typeLabel)],
                        detail: $"Aktivite başlatıldı — {typeLabel}" +
                                (request.ActivityReasonId is > 0 ? $" · Sebep #{request.ActivityReasonId}" : "") +
                                $" · {OpLabel(op)} · Operatör Personel #{request.PersonnelId}");
                }
            }
            catch { /* audit yazımı aktivite başlatmayı asla bozmaz */ }
        }

        return id;
    }

    public async Task<bool> EndCurrentAsync(EndActivityRequest request, CancellationToken ct)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (request.WorkOrderOperationId <= 0)
            throw new ArgumentException("Operasyon ID zorunlu.");
        if (request.PersonnelId <= 0)
            throw new ArgumentException("Kapatan personel ID zorunlu.");

        // İşlem logu için kapatılacak aktif aktiviteyi mutasyondan ÖNCE oku (tip/etiket bilgisi
        // için) — yalnızca audit amaçlı, okunamazsa kapatma işlemi yine de devam eder.
        WorkOrderOperationActivityDto? activeForAudit = null;
        if (_audit is not null)
        {
            try { activeForAudit = await _repo.GetActiveAsync(request.WorkOrderOperationId, ct); }
            catch { /* audit için aktif aktivite okunamadı */ }
        }

        var ended = await _repo.EndActiveAsync(
            request.WorkOrderOperationId,
            request.PersonnelId,
            notes: string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            ct);

        if (_audit is not null && ended && activeForAudit is not null)
        {
            try
            {
                var op = _operations is not null ? await _operations.GetAsync(request.WorkOrderOperationId, ct) : null;
                if (op is not null)
                {
                    _audit.LogChanges("WorkOrder", op.WorkOrderId, op.WorkOrderNumber,
                        [new AuditFieldChange($"Activity[{activeForAudit.Id}].EndedAt",
                            $"{OpLabel(op)} — Aktivite Bitişi ({activeForAudit.ActivityTypeLabel})", null,
                            AuditDiff.Normalize(DateTime.UtcNow))],
                        detail: $"Aktivite kapatıldı — {activeForAudit.ActivityTypeLabel} · {OpLabel(op)} · " +
                                $"Operatör Personel #{request.PersonnelId}");
                }
            }
            catch { /* audit yazımı aktivite kapatmayı asla bozmaz */ }
        }

        return ended;
    }
}
