using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IUserNotificationRepository
{
    Task AddAsync(UserNotification notification, CancellationToken cancellationToken);

    /// <summary>Kullanicinin son N bildirimini (once okunmamis, sonra yenilik sirasinda) doner.</summary>
    Task<IReadOnlyCollection<UserNotification>> GetRecentAsync(Guid userId, int take, CancellationToken cancellationToken);

    Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken);

    Task MarkReadAsync(Guid notificationId, Guid userId, DateTime readAt, CancellationToken cancellationToken);

    Task MarkAllReadAsync(Guid userId, DateTime readAt, CancellationToken cancellationToken);
}
