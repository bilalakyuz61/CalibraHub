using CalibraHub.Application.Abstractions.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CalibraHub.Web.Infrastructure.Collaboration;

/// <summary>
/// Server yeniden başladığında DB'deki aktif kilitleri in-memory store'a yükler.
/// IHostedService olarak çalışır, uygulama başlamadan önce tamamlanır.
/// </summary>
public sealed class CollaborationStartupLoadService : IHostedService
{
    private readonly CollaborationRuntimeStore _runtimeStore;
    private readonly IServiceScopeFactory _scopeFactory;

    public CollaborationStartupLoadService(
        CollaborationRuntimeStore runtimeStore,
        IServiceScopeFactory scopeFactory)
    {
        _runtimeStore = runtimeStore;
        _scopeFactory = scopeFactory;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<ICollaborationLockRepository>();
            var activeLocks = await repo.GetActiveAsync(cancellationToken);

            foreach (var lk in activeLocks)
            {
                if (!Guid.TryParse(lk.UserId, out var userId))
                {
                    continue;
                }

                _runtimeStore.RestoreLock(new CollaborationLockSnapshot(
                    lk.RecordType,
                    lk.RecordId,
                    userId,
                    lk.UserName,
                    lk.RecordTitle,
                    lk.PageUrl,
                    lk.AcquiredAt,
                    lk.LastHeartbeatAt));
            }
        }
        catch
        {
            // DB yoksa veya tablo henüz oluşturulmamışsa sessizce devam et.
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
