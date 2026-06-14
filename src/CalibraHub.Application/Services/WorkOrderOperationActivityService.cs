using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
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

    public WorkOrderOperationActivityService(IWorkOrderOperationActivityRepository repo)
    {
        _repo = repo;
    }

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

        return await _repo.StartAsync(entity, ct);
    }

    public async Task<bool> EndCurrentAsync(EndActivityRequest request, CancellationToken ct)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (request.WorkOrderOperationId <= 0)
            throw new ArgumentException("Operasyon ID zorunlu.");
        if (request.PersonnelId <= 0)
            throw new ArgumentException("Kapatan personel ID zorunlu.");

        return await _repo.EndActiveAsync(
            request.WorkOrderOperationId,
            request.PersonnelId,
            notes: string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            ct);
    }
}
