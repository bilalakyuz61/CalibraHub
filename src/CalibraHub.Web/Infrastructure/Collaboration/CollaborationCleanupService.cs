using CalibraHub.Application.Abstractions.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace CalibraHub.Web.Infrastructure.Collaboration;

public sealed class CollaborationCleanupService : BackgroundService
{
    private static readonly TimeSpan DbSyncInterval = TimeSpan.FromSeconds(30);

    private readonly CollaborationRuntimeStore _runtimeStore;
    private readonly IHubContext<CollaborationHub> _hubContext;
    private readonly IServiceScopeFactory _scopeFactory;
    private DateTime _lastDbSync = DateTime.MinValue;

    public CollaborationCleanupService(
        CollaborationRuntimeStore runtimeStore,
        IHubContext<CollaborationHub> hubContext,
        IServiceScopeFactory scopeFactory)
    {
        _runtimeStore = runtimeStore;
        _hubContext = hubContext;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CollaborationRuntimeDefaults.HeartbeatInterval);

        while (!stoppingToken.IsCancellationRequested &&
               await timer.WaitForNextTickAsync(stoppingToken))
        {
            var now = DateTime.Now;

            var expirationResult = _runtimeStore.ExpireStaleEntries(now);

            if (expirationResult.PresenceChanged)
            {
                await _hubContext.Clients.All.SendAsync(
                    "presenceUpdated",
                    _runtimeStore.GetPresenceSnapshot(),
                    stoppingToken);
            }

            foreach (var releasedEvent in expirationResult.ReleasedLocks)
            {
                await _hubContext.Clients.Group(
                        CollaborationGroupNames.ForRecord(releasedEvent.RecordType, releasedEvent.RecordId))
                    .SendAsync("recordLockChanged", releasedEvent, stoppingToken);
            }

            if (now - _lastDbSync >= DbSyncInterval)
            {
                _lastDbSync = now;
                await SyncToDbAsync(now, stoppingToken);
            }
        }
    }

    private async Task SyncToDbAsync(DateTime now, CancellationToken ct)
    {
        try
        {
            var activeLocks = _runtimeStore.GetAllActiveLocks(now);
            var records = activeLocks.Select(lk => new Application.Abstractions.Persistence.GlobalLockRecord(
                RecordType: lk.RecordType,
                RecordId: lk.RecordId,
                UserId: lk.OwnerUserId.ToString("D"),
                UserName: lk.OwnerDisplayName,
                SessionId: string.Empty,
                RecordTitle: lk.RecordTitle,
                PageUrl: lk.PageUrl,
                AcquiredAt: lk.AcquiredAt,
                LastHeartbeatAt: lk.LastHeartbeatAt)).ToArray();

            await using var scope = _scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<ICollaborationLockRepository>();
            await repo.FullSyncAsync(records, ct);
            await repo.CleanExpiredAsync(CollaborationRuntimeDefaults.LockTimeout, ct);
        }
        catch
        {
            // DB sync hatası memory store'u etkilemez — bir sonraki tickte tekrar denenir.
        }
    }
}
