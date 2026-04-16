using Microsoft.AspNetCore.SignalR;

namespace CalibraHub.Web.Infrastructure.Collaboration;

public sealed class CollaborationCleanupService : BackgroundService
{
    private readonly CollaborationRuntimeStore _runtimeStore;
    private readonly IHubContext<CollaborationHub> _hubContext;

    public CollaborationCleanupService(
        CollaborationRuntimeStore runtimeStore,
        IHubContext<CollaborationHub> hubContext)
    {
        _runtimeStore = runtimeStore;
        _hubContext = hubContext;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CollaborationRuntimeDefaults.HeartbeatInterval);

        while (!stoppingToken.IsCancellationRequested &&
               await timer.WaitForNextTickAsync(stoppingToken))
        {
            var expirationResult = _runtimeStore.ExpireStaleEntries(DateTime.Now);
            if (!expirationResult.PresenceChanged && expirationResult.ReleasedLocks.Count == 0)
            {
                continue;
            }

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
        }
    }
}
